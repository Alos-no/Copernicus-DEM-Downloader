namespace CopernicusDemDownloader.Models;

/// <summary>
/// Persisted state for resume support.
/// </summary>
public record DownloadState(Dictionary<string, FileState> Files);

/// <summary>
/// State of a single downloaded file.
/// </summary>
public record FileState(long Size, string ETag, DateTime Downloaded);
