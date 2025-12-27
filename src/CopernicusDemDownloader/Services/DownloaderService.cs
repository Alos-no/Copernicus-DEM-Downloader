using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using CopernicusDemDownloader.Models;

namespace CopernicusDemDownloader.Services;

/// <summary>
/// Core download service for Copernicus DEM tiles.
/// </summary>
public class DownloaderService
{
    private readonly AmazonS3Client _client;
    private readonly DownloadOptions _options;
    private readonly ConcurrentDictionary<string, FileState> _downloadedFiles = new();
    private readonly string _stateFilePath;

    private long _totalBytes;
    private long _downloadedBytes;
    private int _totalFiles;
    private int _completedFiles;
    private int _skippedFiles;
    private int _failedFiles;

    public DownloaderService(DownloadOptions options)
    {
        // Validate and clamp options
        _options = options with
        {
            Parallelism = Math.Clamp(options.Parallelism, 1, 32),
            MaxRetries = Math.Clamp(options.MaxRetries, 0, 10)
        };

        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true,
            UseHttp = !_options.Endpoint.StartsWith("https"),
        };

        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        _client = new AmazonS3Client(credentials, config);
        _stateFilePath = Path.Combine(_options.OutputDirectory, _options.StateFile);
    }

    public static AmazonS3Client CreateClient(string accessKey, string secretKey, string endpoint)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            UseHttp = !endpoint.StartsWith("https"),
        };
        var credentials = new BasicAWSCredentials(accessKey, secretKey);
        return new AmazonS3Client(credentials, config);
    }

    public async Task<DownloadResult> RunAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_options.OutputDirectory);
        LoadState();

        var prefix = _options.Prefix.TrimEnd('/') + '/';

        progress?.Report(new DownloadProgress { Status = "Listing objects..." });
        var objects = await ListObjectsAsync(prefix, ct);

        _totalFiles = objects.Count;
        _totalBytes = objects.Sum(o => o.Size ?? 0);

        progress?.Report(new DownloadProgress
        {
            Status = $"Found {_totalFiles} files ({FormatBytes(_totalBytes)})",
            TotalFiles = _totalFiles,
            TotalBytes = _totalBytes
        });

        if (_options.DryRun)
        {
            return new DownloadResult
            {
                TotalFiles = _totalFiles,
                TotalBytes = _totalBytes,
                CompletedFiles = 0,
                SkippedFiles = 0,
                FailedFiles = 0,
                DownloadedBytes = 0,
                DryRun = true,
                Files = objects.Select(o => o.Key).ToList()
            };
        }

        if (_totalFiles == 0)
        {
            Console.WriteLine($"\nNo files found matching criteria.");
            Console.WriteLine($"  Prefix: {prefix}");
            Console.WriteLine($"  Masks: {string.Join(", ", _options.Masks.GetIndividualMasks())}");
            Console.WriteLine($"  Bbox: {_options.BoundingBox?.ToString() ?? "None"}");
            Console.WriteLine("\nTip: Try running with --dry-run to see what files would be downloaded.");

            return new DownloadResult
            {
                TotalFiles = 0,
                TotalBytes = 0,
                CompletedFiles = 0,
                SkippedFiles = 0,
                FailedFiles = 0,
                DownloadedBytes = 0
            };
        }

        var startTime = DateTime.UtcNow;
        var progressTimer = new Timer(_ =>
        {
            progress?.Report(new DownloadProgress
            {
                Status = "Downloading...",
                TotalFiles = _totalFiles,
                CompletedFiles = _completedFiles,
                SkippedFiles = _skippedFiles,
                FailedFiles = _failedFiles,
                TotalBytes = _totalBytes,
                DownloadedBytes = _downloadedBytes,
                Elapsed = DateTime.UtcNow - startTime
            });
        }, null, 0, 1000);

        try
        {
            await Parallel.ForEachAsync(
                objects,
                new ParallelOptions { MaxDegreeOfParallelism = _options.Parallelism, CancellationToken = ct },
                async (obj, token) => await DownloadFileAsync(obj, prefix, token));
        }
        finally
        {
            await progressTimer.DisposeAsync();
            SaveState();
        }

        var elapsed = DateTime.UtcNow - startTime;

        return new DownloadResult
        {
            TotalFiles = _totalFiles,
            TotalBytes = _totalBytes,
            CompletedFiles = _completedFiles,
            SkippedFiles = _skippedFiles,
            FailedFiles = _failedFiles,
            DownloadedBytes = _downloadedBytes,
            Elapsed = elapsed
        };
    }

    private async Task<List<S3Object>> ListObjectsAsync(string prefix, CancellationToken ct)
    {
        // Use optimized tile-folder listing when bbox is specified
        // This is MUCH faster than scanning all objects
        if (_options.BoundingBox != null)
        {
            return await ListObjectsWithBboxOptimizationAsync(prefix, ct);
        }

        // Full listing (slow for large datasets)
        return await ListAllObjectsAsync(prefix, ct);
    }

    private async Task<List<S3Object>> ListObjectsWithBboxOptimizationAsync(string prefix, CancellationToken ct)
    {
        var objects = new List<S3Object>();
        var bbox = _options.BoundingBox!;

        Console.WriteLine($"\nSearching for tiles in bounding box {bbox}...");

        // List ALL objects and filter by both mask AND bbox at the object level.
        // The bbox filter uses coordinates parsed from the object key (file path).
        // This is necessary because the S3 folder structure has product folders (no coordinates)
        // at level 1 and tile folders (with coordinates) at level 2.
        string? continuationToken = null;
        int scanned = 0;
        int matchedMask = 0;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };

            var response = await _client.ListObjectsV2Async(request, ct);

            if (response.S3Objects != null)
            {
                foreach (var obj in response.S3Objects)
                {
                    scanned++;

                    // Filter by mask first (fast string check)
                    if (!TileCoordinateParser.MatchesMaskFilter(obj.Key, _options.Masks))
                        continue;

                    matchedMask++;

                    // Then filter by bbox (parse coordinates from key)
                    if (!TileCoordinateParser.IsInBoundingBox(obj.Key, bbox))
                        continue;

                    objects.Add(obj);
                }
            }

            // Show progress every 5000 objects
            if (scanned % 5000 == 0)
            {
                Console.Write($"\r  Scanned {scanned:N0} objects, found {objects.Count:N0} tiles in bbox...        ");
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        } while (continuationToken != null);

        Console.WriteLine($"\r  Scanned {scanned:N0} objects, found {objects.Count:N0} tiles in bbox (out of {matchedMask:N0} total DEM files).        ");

        if (objects.Count == 0 && matchedMask > 0)
        {
            Console.WriteLine($"\n⚠ No tiles found in bounding box {bbox}");
            Console.WriteLine($"  The dataset has {matchedMask:N0} DEM files, but none are within your specified area.");
        }

        return objects;
    }

    private async Task<List<S3Object>> ListAllObjectsAsync(string prefix, CancellationToken ct)
    {
        var objects = new List<S3Object>();
        string? continuationToken = null;
        int totalScanned = 0;
        int pageCount = 0;

        Console.WriteLine("\n⚠ No bounding box specified. Full dataset listing may take 10+ minutes.");
        Console.WriteLine("  Consider using --bbox to limit the geographic area.");
        Console.WriteLine("  Example: --bbox -10,35,30,70 (Europe)");

        Console.Write("\nScanning S3 for DEM files");

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };

            pageCount++;
            var response = await _client.ListObjectsV2Async(request, ct);

            // Show progress every 10 pages (approximately every 10,000 objects)
            if (pageCount % 10 == 0)
            {
                Console.Write($"\r  Scanned {totalScanned:N0} objects, found {objects.Count:N0} DEM files...        ");
            }

            if (response.S3Objects == null)
                continue;

            foreach (var obj in response.S3Objects)
            {
                totalScanned++;

                // Filter by mask type
                if (TileCoordinateParser.MatchesMaskFilter(obj.Key, _options.Masks))
                {
                    objects.Add(obj);
                }
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;

        } while (continuationToken != null);

        // Clear the progress line and show summary
        Console.WriteLine($"\r  Scanned {totalScanned:N0} objects, found {objects.Count:N0} matching files.        ");

        if (objects.Count == 0)
        {
            if (totalScanned == 0)
            {
                Console.WriteLine($"\n⚠ No objects found in S3 for prefix '{prefix}'");
                Console.WriteLine("  Check that the dataset exists and try running with --dataset option.");
            }
            else
            {
                Console.WriteLine($"\n⚠ No files matched the filter criteria.");
                Console.WriteLine($"  Scanned {totalScanned:N0} objects but none matched masks: {string.Join(", ", _options.Masks.GetIndividualMasks())}");
                if (_options.BoundingBox != null)
                {
                    Console.WriteLine($"  Bounding box: {_options.BoundingBox}");
                }
                Console.WriteLine("  Try using a different bounding box or check the mask selection.");
            }
        }

        return objects;
    }

    private async Task DownloadFileAsync(S3Object obj, string prefix, CancellationToken ct)
    {
        var relativePath = obj.Key[prefix.Length..];
        var localPath = Path.Combine(_options.OutputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var objSize = obj.Size ?? 0;

        // Check if already downloaded (resume support)
        if (!_options.Force && File.Exists(localPath))
        {
            var fileInfo = new FileInfo(localPath);
            if (fileInfo.Length == objSize)
            {
                if (_downloadedFiles.TryGetValue(obj.Key, out var state) && state.Size == objSize)
                {
                    Interlocked.Increment(ref _skippedFiles);
                    return;
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        for (int attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                await DownloadWithRetryAsync(obj, localPath, ct);

                _downloadedFiles[obj.Key] = new FileState(objSize, obj.ETag, DateTime.UtcNow);
                Interlocked.Add(ref _downloadedBytes, objSize);
                Interlocked.Increment(ref _completedFiles);
                return;
            }
            catch (Exception) when (attempt < _options.MaxRetries && !ct.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _failedFiles);
            }
        }
    }

    private async Task DownloadWithRetryAsync(S3Object obj, string localPath, CancellationToken ct)
    {
        var tempPath = localPath + ".tmp";

        try
        {
            using var response = await _client.GetObjectAsync(_options.Bucket, obj.Key, ct);
            await using var responseStream = response.ResponseStream;
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous);

            await responseStream.CopyToAsync(fileStream, ct);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }

        File.Move(tempPath, localPath, overwrite: true);
    }

    private void LoadState()
    {
        if (!File.Exists(_stateFilePath)) return;

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<DownloadState>(json);
            if (state?.Files != null)
            {
                foreach (var kvp in state.Files)
                {
                    _downloadedFiles[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
            // Ignore corrupted state file
        }
    }

    private void SaveState()
    {
        try
        {
            var state = new DownloadState(_downloadedFiles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
            // Ignore state save errors
        }
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1) { size /= 1024; i++; }
        return $"{size:F1} {suffixes[i]}";
    }
}

public class DownloadProgress
{
    public string Status { get; init; } = "";
    public int TotalFiles { get; init; }
    public int CompletedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int FailedFiles { get; init; }
    public long TotalBytes { get; init; }
    public long DownloadedBytes { get; init; }
    public TimeSpan Elapsed { get; init; }

    public double PercentComplete => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
    public long BytesPerSecond => Elapsed.TotalSeconds > 0 ? (long)(DownloadedBytes / Elapsed.TotalSeconds) : 0;
    public TimeSpan EstimatedRemaining => BytesPerSecond > 0
        ? TimeSpan.FromSeconds((TotalBytes - DownloadedBytes) / (double)BytesPerSecond)
        : TimeSpan.Zero;
}

public class DownloadResult
{
    public int TotalFiles { get; init; }
    public long TotalBytes { get; init; }
    public int CompletedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int FailedFiles { get; init; }
    public long DownloadedBytes { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool DryRun { get; init; }
    public List<string>? Files { get; init; }
}
