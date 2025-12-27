using Amazon.S3;
using Amazon.S3.Model;
using CopernicusDemDownloader.Models;

namespace CopernicusDemDownloader.Services;

/// <summary>
/// Service for discovering available Copernicus DEM datasets and versions.
/// </summary>
public class DatasetDiscoveryService
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly string _basePrefix;

    /// <summary>
    /// Known dataset configurations. EEA-10 requires CCM (Copernicus Contributing Missions) access.
    /// DGED = 32-bit float with quality layers, DTED = 16-bit integer (smaller).
    /// PUBLIC = pre-filtered for redistribution (missing tiles for Armenia/Azerbaijan).
    /// </summary>
    public static readonly Dictionary<string, DatasetInfo> KnownDatasets = new()
    {
        // EEA-10 (European, 10m) - requires CCM access
        ["EEA-10"] = new("EEA-10", "COP-DEM_EEA-10-INSP", "10m European coverage (requires CCM access)", 10, DatasetCoverage.European),

        // GLO-30 variants (Global, 30m)
        ["GLO-30-DGED"] = new("GLO-30", "COP-DEM_GLO-30-DGED", "30m global, 32-bit float, full coverage", 30, DatasetCoverage.Global, DatasetFormat.DGED),
        ["GLO-30-DGED-PUBLIC"] = new("GLO-30 PUBLIC", "COP-DEM_GLO-30-DGED_PUBLIC", "30m global, 32-bit float, missing AM/AZ tiles", 30, DatasetCoverage.Global, DatasetFormat.DGED, true),
        ["GLO-30-DTED"] = new("GLO-30 DTED", "COP-DEM_GLO-30-DTED", "30m global, 16-bit integer, full coverage", 30, DatasetCoverage.Global, DatasetFormat.DTED),
        ["GLO-30-DTED-PUBLIC"] = new("GLO-30 DTED PUBLIC", "COP-DEM_GLO-30-DTED_PUBLIC", "30m global, 16-bit integer, missing AM/AZ tiles", 30, DatasetCoverage.Global, DatasetFormat.DTED, true),

        // GLO-90 variants (Global, 90m)
        ["GLO-90-DGED"] = new("GLO-90", "COP-DEM_GLO-90-DGED", "90m global, 32-bit float", 90, DatasetCoverage.Global, DatasetFormat.DGED),
        ["GLO-90-DTED"] = new("GLO-90 DTED", "COP-DEM_GLO-90-DTED", "90m global, 16-bit integer", 90, DatasetCoverage.Global, DatasetFormat.DTED),
    };

    /// <summary>
    /// Base prefix for CCM datasets (EEA-10). Requires special access permissions.
    /// </summary>
    public const string CcmPrefix = "CCM/";

    public DatasetDiscoveryService(AmazonS3Client client, string bucket, string basePrefix = "auxdata/CopDEM/")
    {
        _client = client;
        _bucket = bucket;
        _basePrefix = basePrefix.TrimEnd('/') + '/';
    }

    /// <summary>
    /// Discover all available dataset types (EEA-10, GLO-30, GLO-90).
    /// EEA-10 is under CCM/ prefix and requires special access permissions.
    /// </summary>
    public async Task<List<DiscoveredDataset>> DiscoverDatasetsAsync(CancellationToken ct = default)
    {
        var datasets = new List<DiscoveredDataset>();

        try
        {
            // Search in main auxdata/CopDEM/ prefix (GLO-30, GLO-90)
            var request = new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = _basePrefix,
                Delimiter = "/"
            };

            var response = await _client.ListObjectsV2Async(request, ct);

            foreach (var prefix in response.CommonPrefixes ?? [])
            {
                var name = prefix.TrimEnd('/').Split('/').Last();
                if (name.StartsWith("COP-DEM"))
                {
                    var info = KnownDatasets.Values.FirstOrDefault(d => name.Contains(d.Prefix));
                    datasets.Add(new DiscoveredDataset(
                        Name: name,
                        FullPrefix: _basePrefix + name + "/",
                        Info: info
                    ));
                }
            }

            // Also search CCM/ prefix for EEA-10 (requires special access)
            var ccmRequest = new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = CcmPrefix,
                Delimiter = "/"
            };

            var ccmResponse = await _client.ListObjectsV2Async(ccmRequest, ct);

            foreach (var prefix in ccmResponse.CommonPrefixes ?? [])
            {
                var name = prefix.TrimEnd('/').Split('/').Last();
                if (name.StartsWith("COP-DEM") && name.Contains("EEA"))
                {
                    var info = KnownDatasets.Values.FirstOrDefault(d => name.Contains(d.Prefix));
                    datasets.Add(new DiscoveredDataset(
                        Name: name,
                        FullPrefix: CcmPrefix + name + "/",
                        Info: info
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to discover datasets: {ex.Message}", ex);
        }

        return datasets.OrderBy(d => d.Info?.Resolution ?? 999).ToList();
    }

    /// <summary>
    /// Discover available versions/years for a specific dataset.
    /// </summary>
    public async Task<List<DatasetVersion>> DiscoverVersionsAsync(string datasetPrefix, CancellationToken ct = default)
    {
        var versions = new List<DatasetVersion>();

        try
        {
            // List subdirectories under the dataset
            var request = new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = datasetPrefix.TrimEnd('/') + "/",
                Delimiter = "/"
            };

            var response = await _client.ListObjectsV2Async(request, ct);

            // Check for versioned subdirectories (e.g., 2024_1/)
            foreach (var prefix in response.CommonPrefixes ?? [])
            {
                var name = prefix.TrimEnd('/').Split('/').Last();

                // Try to parse as year_release format
                var parts = name.Split('_');
                if (parts.Length >= 1 && int.TryParse(parts[0], out var year) && year >= 2019 && year <= 2100)
                {
                    var release = parts.Length > 1 ? parts[1] : "1";
                    versions.Add(new DatasetVersion(name, prefix, parts[0], release));
                }
            }

            // If no versioned subdirectories, the files are directly in the dataset folder
            if (versions.Count == 0)
            {
                // Check if there are actual files in this prefix
                var filesRequest = new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = datasetPrefix,
                    MaxKeys = 10
                };
                var filesResponse = await _client.ListObjectsV2Async(filesRequest, ct);

                if (filesResponse.S3Objects.Any(o => o.Key.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)))
                {
                    versions.Add(new DatasetVersion("Current", datasetPrefix, "Latest", ""));
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to discover versions: {ex.Message}", ex);
        }

        return versions.OrderByDescending(v => v.Year).ThenByDescending(v => v.Release).ToList();
    }
}

/// <summary>
/// A discovered dataset with optional metadata.
/// </summary>
public record DiscoveredDataset(
    string Name,
    string FullPrefix,
    DatasetInfo? Info
);
