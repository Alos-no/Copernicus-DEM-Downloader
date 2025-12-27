namespace CopernicusDemDownloader.Models;

/// <summary>
/// Configuration options for a download session.
/// </summary>
public record DownloadOptions
{
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public string Endpoint { get; init; } = "https://eodata.dataspace.copernicus.eu";
    public string Bucket { get; init; } = "eodata";
    public required string Prefix { get; init; }
    public required string OutputDirectory { get; init; }
    public int Parallelism { get; init; } = 8;
    public int MaxRetries { get; init; } = 3;
    public string StateFile { get; init; } = "download_state.json";
    public bool Force { get; init; } = false;
    public bool DryRun { get; init; } = false;
    public MaskType Masks { get; init; } = MaskType.DEM;
    public BoundingBox? BoundingBox { get; init; }
}
