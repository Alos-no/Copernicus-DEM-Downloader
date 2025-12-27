using Amazon.S3.Model;
using CopernicusDemDownloader.Services;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Integration;

/// <summary>
/// Integration tests for DatasetDiscoveryService.
/// These tests verify that version discovery returns valid prefixes
/// that actually contain downloadable files.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Integration)]
public class DatasetDiscoveryIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task DiscoverDatasetsAsync_ReturnsMultipleDatasets()
    {
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();

        Assert.NotEmpty(datasets);
        Assert.Contains(datasets, d => d.Name.Contains("GLO-30"));
        Assert.Contains(datasets, d => d.Name.Contains("GLO-90"));
    }

    [Fact]
    public async Task DiscoverVersionsAsync_GLO90_ReturnsVersions()
    {
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo90 = datasets.First(d => d.Name.Contains("GLO-90-DGED") && !d.Name.Contains("PUBLIC"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo90.FullPrefix);

        Assert.NotEmpty(versions);
        // Versions should have valid prefixes, not just "Latest"
        Assert.All(versions, v =>
        {
            Assert.False(string.IsNullOrEmpty(v.FullPrefix), "Version FullPrefix should not be empty");
            Assert.Contains("/", v.FullPrefix);
        });
    }

    [Fact]
    public async Task DiscoverVersionsAsync_GLO30_ReturnsVersions()
    {
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo30 = datasets.First(d => d.Name.Contains("GLO-30-DGED") && !d.Name.Contains("PUBLIC"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo30.FullPrefix);

        Assert.NotEmpty(versions);
    }

    [Fact]
    public async Task VersionPrefix_ActuallyContainsFiles()
    {
        // Critical test: verify that discovered version prefixes actually contain files
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo90 = datasets.First(d => d.Name.Contains("GLO-90-DGED") && !d.Name.Contains("PUBLIC"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo90.FullPrefix);
        Assert.NotEmpty(versions);

        var latestVersion = versions.First();

        // List files under the version prefix
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = latestVersion.FullPrefix,
            MaxKeys = 10
        };

        var response = await client.ListObjectsV2Async(listRequest);

        Assert.NotEmpty(response.S3Objects);
        Assert.Contains(response.S3Objects, o => o.Key.EndsWith("_DEM.tif"));
    }

    [Fact]
    public async Task VersionPrefix_EndsWithSlash()
    {
        // Verify version prefixes are properly formatted for S3 listing
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo90 = datasets.First(d => d.Name.Contains("GLO-90-DGED"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo90.FullPrefix);

        Assert.All(versions, v =>
        {
            // If it's a versioned folder, it should end with slash
            if (v.Year != "Latest")
            {
                Assert.True(v.FullPrefix.EndsWith("/"),
                    $"Version prefix should end with /: {v.FullPrefix}");
            }
        });
    }

    // =========================================================================
    // Batch Mode Discovery Flow Tests
    // These tests simulate exactly what Program.cs RunBatchAsync does
    // =========================================================================

    [Theory]
    [InlineData("GLO-90", "COP-DEM_GLO-90-DGED")]
    [InlineData("GLO-30", "COP-DEM_GLO-30-DGED")]
    [InlineData("GLO-90-DGED", "COP-DEM_GLO-90-DGED")]
    // Note: GLO-30-DTED does not exist in CDSE S3 (only DGED variants are available)
    public async Task BatchModeFlow_DatasetOption_ResolvesToCorrectPrefix(string datasetName, string expectedPrefix)
    {
        // This test simulates the exact flow in Program.cs RunBatchAsync
        // to verify the --dataset option resolves to a working S3 prefix

        // Step 1: Normalize dataset name (same as NormalizeDatasetKey in Program.cs)
        var upper = datasetName.ToUpperInvariant().Replace("_", "-");
        var key = upper switch
        {
            "GLO-30" or "GLO30" => "GLO-30-DGED",
            "GLO-90" or "GLO90" => "GLO-90-DGED",
            _ => upper
        };

        // Step 2: Look up in KnownDatasets
        Assert.True(DatasetDiscoveryService.KnownDatasets.TryGetValue(key, out var info),
            $"Dataset key '{key}' should exist in KnownDatasets");

        // Step 3: Create discovery service and find matching dataset
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var matchingDataset = datasets.FirstOrDefault(d => d.Name.Contains(info.Prefix));

        Assert.NotNull(matchingDataset);
        Assert.Contains(expectedPrefix, matchingDataset.Name);

        // Step 4: Discover versions
        var versions = await discoveryService.DiscoverVersionsAsync(matchingDataset.FullPrefix);
        Assert.NotEmpty(versions);

        // Step 5: Get the final prefix (either versioned or base)
        string finalPrefix;
        if (versions.Count > 0 && versions[0].Year != "Latest")
        {
            finalPrefix = versions[0].FullPrefix;
        }
        else
        {
            // CDSE doesn't have versioned subdirectories, so we use the base prefix
            finalPrefix = matchingDataset.FullPrefix;
        }

        // Step 6: Verify the prefix actually contains DEM files
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = finalPrefix,
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);

        // Must find files - this is the critical assertion that would have caught the original bug
        Assert.True(response.S3Objects.Count > 0 || response.CommonPrefixes.Count > 0,
            $"Prefix '{finalPrefix}' must contain files or subdirectories");
    }

    [Fact]
    public async Task BatchModeFlow_DiscoveredPrefix_ContainsDEMFiles()
    {
        // End-to-end test: verify we can find actual DEM files using the batch mode flow
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        // Simulate batch mode discovery
        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo90 = datasets.First(d => d.Name.Contains("GLO-90-DGED") && !d.Name.Contains("PUBLIC"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo90.FullPrefix);
        var prefix = versions.Count > 0 && versions[0].Year != "Latest"
            ? versions[0].FullPrefix
            : glo90.FullPrefix;

        // List files in the prefix - go deeper into tile folders
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = prefix,
            MaxKeys = 1000  // Need enough to find some .tif files
        };

        var response = await client.ListObjectsV2Async(listRequest);

        // CDSE structure: prefix/TileFolder/file.tif
        // We should find either direct .tif files or CommonPrefixes (tile folders)
        var hasTifFiles = response.S3Objects.Any(o => o.Key.EndsWith(".tif"));
        var hasTileFolders = response.CommonPrefixes?.Count > 0;

        Assert.True(hasTifFiles || hasTileFolders,
            $"Prefix '{prefix}' should contain .tif files or tile folders. " +
            $"Found {response.S3Objects.Count} objects, {response.CommonPrefixes?.Count ?? 0} prefixes");

        // If we have tile folders, verify at least one contains DEM files
        if (hasTileFolders && !hasTifFiles)
        {
            var firstTileFolder = response.CommonPrefixes.First();
            var tileRequest = new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = firstTileFolder,
                MaxKeys = 10
            };

            var tileResponse = await client.ListObjectsV2Async(tileRequest);
            Assert.Contains(tileResponse.S3Objects, o => o.Key.EndsWith("_DEM.tif"));
        }
    }

    [Fact]
    public async Task KnownDatasets_AllExistInS3()
    {
        // Verify all known datasets can be found in S3
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var discoveredDatasets = await discoveryService.DiscoverDatasetsAsync();

        // Check GLO datasets (EEA-10 requires special CCM access)
        var gloDatasets = DatasetDiscoveryService.KnownDatasets
            .Where(kv => !kv.Key.StartsWith("EEA"))
            .ToList();

        foreach (var (key, info) in gloDatasets)
        {
            var match = discoveredDatasets.FirstOrDefault(d => d.Name.Contains(info.Prefix));
            Assert.True(match != null,
                $"Known dataset '{key}' with prefix '{info.Prefix}' should exist in S3. " +
                $"Available: {string.Join(", ", discoveredDatasets.Select(d => d.Name))}");
        }
    }
}
