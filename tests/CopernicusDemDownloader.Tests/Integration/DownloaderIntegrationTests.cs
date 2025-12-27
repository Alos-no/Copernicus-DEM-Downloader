using System.Text.Json;
using Amazon.S3.Model;
using CopernicusDemDownloader.Models;
using CopernicusDemDownloader.Services;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Integration;

/// <summary>
/// Integration tests for DownloaderService against real CDSE S3.
/// These tests REQUIRE valid S3 credentials configured in user secrets or environment variables.
/// If credentials are missing or invalid, tests will FAIL - as they should.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Integration)]
public class DownloaderIntegrationTests : IntegrationTestBase
{
    [Fact]
    public async Task Downloader_CanConnectToS3()
    {
        // This test verifies S3 connectivity works
        // If credentials are wrong or missing, this test FAILS
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();

        Assert.NotEmpty(datasets);
        Assert.Contains(datasets, d => d.Name.Contains("GLO-30"));
        Assert.Contains(datasets, d => d.Name.Contains("GLO-90"));
    }

    [Fact]
    public async Task Downloader_CanListFilesFromS3()
    {
        // Verify we can list files from a known prefix
        using var client = CreateS3Client();

        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 10
        };

        var response = await client.ListObjectsV2Async(listRequest);

        Assert.NotNull(response.S3Objects);
        Assert.NotEmpty(response.S3Objects);
    }

    [Fact]
    public async Task Download_SingleSmallFile_Succeeds()
    {
        var outputDir = CreateTempDirectory("_download");

        // First, find a small file to download
        using var client = CreateS3Client();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);

        // Find the smallest DEM file
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First(); // Will throw if no files found - that's a test failure

        // Extract just this file's folder prefix
        var filePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);

        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = filePrefix,
            OutputDirectory = outputDir,
            Parallelism = 1,
            DryRun = false,
            Masks = MaskType.DEM
        };

        var downloader = new DownloaderService(options);

        // 5 minute timeout for downloading a single file
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await downloader.RunAsync(ct: cts.Token);

        // Verify the download succeeded
        Assert.False(result.DryRun);
        Assert.True(result.TotalFiles > 0, "Should find at least one file to download");
        Assert.True(result.CompletedFiles > 0, "Should complete at least one download");
        Assert.Equal(0, result.FailedFiles);

        // Verify file actually exists on disk
        var downloadedFiles = Directory.GetFiles(outputDir, "*.tif", SearchOption.AllDirectories);
        Assert.NotEmpty(downloadedFiles);
    }

    [Fact]
    public async Task Download_CreatesStateFile()
    {
        var outputDir = CreateTempDirectory("_statefile");
        var stateFileName = "test_state.json";
        var stateFilePath = Path.Combine(outputDir, stateFileName);

        // Find a small file
        using var client = CreateS3Client();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First();

        var filePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);

        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = filePrefix,
            OutputDirectory = outputDir,
            Parallelism = 1,
            DryRun = false,
            Masks = MaskType.DEM,
            StateFile = stateFileName
        };

        var downloader = new DownloaderService(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await downloader.RunAsync(ct: cts.Token);

        // Verify download succeeded
        Assert.True(result.CompletedFiles > 0, "Should complete at least one download");

        // Verify state file was created
        Assert.True(File.Exists(stateFilePath), "State file should be created after download");

        // Verify state file is valid JSON with correct structure
        var stateJson = File.ReadAllText(stateFilePath);
        var state = JsonSerializer.Deserialize<DownloadState>(stateJson);
        Assert.NotNull(state);
        Assert.NotEmpty(state.Files);
    }

    [Fact]
    public async Task Resume_SkipsAlreadyDownloadedFiles()
    {
        var outputDir = CreateTempDirectory("_resume");
        var stateFileName = "resume_state.json";

        // Find a small file
        using var client = CreateS3Client();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First();

        var filePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);

        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = filePrefix,
            OutputDirectory = outputDir,
            Parallelism = 1,
            DryRun = false,
            Masks = MaskType.DEM,
            StateFile = stateFileName
        };

        // First download
        var downloader1 = new DownloaderService(options);
        using var cts1 = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result1 = await downloader1.RunAsync(ct: cts1.Token);

        Assert.True(result1.CompletedFiles > 0, "First download should complete files");
        var completedFirstRun = result1.CompletedFiles;

        // Second download with same options - should skip everything
        var downloader2 = new DownloaderService(options);
        using var cts2 = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result2 = await downloader2.RunAsync(ct: cts2.Token);

        Assert.Equal(completedFirstRun, result2.SkippedFiles);
        Assert.Equal(0, result2.CompletedFiles);
    }

    [Fact]
    public async Task Force_RedownloadsExistingFiles()
    {
        var outputDir = CreateTempDirectory("_force");
        var stateFileName = "force_state.json";

        // Find a small file
        using var client = CreateS3Client();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First();

        var filePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);

        // First download
        var options1 = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = filePrefix,
            OutputDirectory = outputDir,
            Parallelism = 1,
            DryRun = false,
            Masks = MaskType.DEM,
            StateFile = stateFileName,
            Force = false
        };

        var downloader1 = new DownloaderService(options1);
        using var cts1 = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result1 = await downloader1.RunAsync(ct: cts1.Token);

        Assert.True(result1.CompletedFiles > 0, "First download should complete");

        // Second download with Force = true
        var options2 = options1 with { Force = true };
        var downloader2 = new DownloaderService(options2);
        using var cts2 = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result2 = await downloader2.RunAsync(ct: cts2.Token);

        // With Force, files should be re-downloaded, not skipped
        Assert.True(result2.CompletedFiles > 0, "Force should re-download files");
        Assert.Equal(0, result2.SkippedFiles);
    }

    [Fact]
    public async Task DryRun_DoesNotDownloadFiles()
    {
        var outputDir = CreateTempDirectory("_dryrun");

        // Find a small file's prefix
        using var client = CreateS3Client();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First();

        var filePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);

        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = filePrefix,
            OutputDirectory = outputDir,
            Parallelism = 1,
            DryRun = true, // DRY RUN
            Masks = MaskType.DEM
        };

        var downloader = new DownloaderService(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await downloader.RunAsync(ct: cts.Token);

        Assert.True(result.DryRun);
        Assert.True(result.TotalFiles > 0, "Should list files");
        Assert.NotNull(result.Files);
        Assert.NotEmpty(result.Files);

        // Verify NO files were actually downloaded
        var downloadedFiles = Directory.Exists(outputDir)
            ? Directory.GetFiles(outputDir, "*.tif", SearchOption.AllDirectories)
            : Array.Empty<string>();
        Assert.Empty(downloadedFiles);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceledException()
    {
        var outputDir = CreateTempDirectory("_cancel");

        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-30-DGED/", // Large dataset
            OutputDirectory = outputDir,
            Parallelism = 4,
            DryRun = false,
            Masks = MaskType.DEM
        };

        var downloader = new DownloaderService(options);

        // Cancel after 3 seconds
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Should throw OperationCanceledException (or TaskCanceledException which inherits from it)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await downloader.RunAsync(ct: cts.Token);
        });
    }

    [Fact]
    public async Task Progress_ReportsUpdates()
    {
        var outputDir = CreateTempDirectory("_progress");
        var progressReports = new List<DownloadProgress>();

        // Find a small file
        using var client = CreateS3Client();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First();

        var filePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);

        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = filePrefix,
            OutputDirectory = outputDir,
            Parallelism = 1,
            DryRun = false,
            Masks = MaskType.DEM
        };

        var downloader = new DownloaderService(options);
        var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        await downloader.RunAsync(progress, cts.Token);

        // Should have received progress updates
        Assert.NotEmpty(progressReports);
        Assert.Contains(progressReports, p => !string.IsNullOrEmpty(p.Status));
    }

    [Fact]
    public void TileCoordinateParser_ParsesRealFileNames()
    {
        var testKey = "auxdata/CopDEM/COP-DEM_GLO-30-DGED/test/Copernicus_DSM_10_S18_00_E020_00/DEM/Copernicus_DSM_10_S18_00_E020_00_DEM.tif";

        var coords = TileCoordinateParser.TryParseCoordinates(testKey);

        Assert.NotNull(coords);
        Assert.Equal(-18, coords.Value.Lat);
        Assert.Equal(20, coords.Value.Lon);
    }

    [Fact]
    public void BoundingBox_IntersectsTile_WorksCorrectly()
    {
        var bbox = new BoundingBox(19, -19, 21, -17);

        Assert.True(bbox.IntersectsTile(20, -18));
        Assert.False(bbox.IntersectsTile(10, 45));
    }

    [Fact]
    public void MaskTypeFilter_MatchesCorrectFiles()
    {
        var masks = MaskType.DEM | MaskType.WBM;

        Assert.True(TileCoordinateParser.MatchesMaskFilter("test_DEM.tif", masks));
        Assert.True(TileCoordinateParser.MatchesMaskFilter("test_WBM.tif", masks));
        Assert.False(TileCoordinateParser.MatchesMaskFilter("test_EDM.tif", masks));
        Assert.False(TileCoordinateParser.MatchesMaskFilter("test_FLM.tif", masks));
    }

    // =========================================================================
    // End-to-End Batch Mode Flow Tests
    // These tests simulate the complete --dataset flow using DownloaderService
    // =========================================================================

    [Fact]
    public async Task BatchModeFlow_DownloaderService_FindsFilesWithDiscoveredPrefix()
    {
        // This test validates the GLO-90 discovery flow works correctly.
        // Uses a known tile prefix for speed (same approach as CliOptionsIntegrationTests).

        var outputDir = CreateTempDirectory("_batch_flow");

        // Step 1: Simulate dataset discovery (same as Program.cs RunBatchAsync)
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo90 = datasets.First(d => d.Name.Contains("GLO-90-DGED") && !d.Name.Contains("PUBLIC"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo90.FullPrefix);
        var prefix = versions.Count > 0 && versions[0].Year != "Latest"
            ? versions[0].FullPrefix
            : glo90.FullPrefix;

        // Step 2: Get a known small tile prefix for fast testing
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = prefix,
            MaxKeys = 100
        };
        var response = await client.ListObjectsV2Async(listRequest);
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First();
        var tilePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);

        // Step 3: Create DownloaderService with the tile prefix (no bbox needed)
        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = tilePrefix,
            OutputDirectory = outputDir,
            Parallelism = 4,
            DryRun = true,  // Dry run to avoid actual download
            Masks = MaskType.DEM
        };

        var downloader = new DownloaderService(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        // Step 4: Run and verify files are found
        var result = await downloader.RunAsync(ct: cts.Token);

        // CRITICAL ASSERTION: Must find files, not 0
        Assert.True(result.TotalFiles > 0,
            $"GLO-90 batch mode with discovered prefix must find files. " +
            $"Prefix used: '{tilePrefix}'. Got {result.TotalFiles} files.");

        Assert.NotNull(result.Files);
        Assert.NotEmpty(result.Files);
        Assert.Contains(result.Files, f => f.EndsWith("_DEM.tif"));
    }

    [Fact]
    public async Task BatchModeFlow_DownloaderService_FindsFilesForGLO30()
    {
        // Same test but for GLO-30 dataset
        var outputDir = CreateTempDirectory("_batch_flow_glo30");

        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo30 = datasets.First(d => d.Name.Contains("GLO-30-DGED") && !d.Name.Contains("PUBLIC"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo30.FullPrefix);
        var prefix = versions.Count > 0 && versions[0].Year != "Latest"
            ? versions[0].FullPrefix
            : glo30.FullPrefix;

        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = prefix,
            OutputDirectory = outputDir,
            Parallelism = 4,
            DryRun = true,
            Masks = MaskType.DEM,
            BoundingBox = new BoundingBox(10, 45, 11, 46)
        };

        var downloader = new DownloaderService(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));  // GLO-30 has more tiles

        var result = await downloader.RunAsync(ct: cts.Token);

        Assert.True(result.TotalFiles > 0,
            $"GLO-30 with bbox 10,45,11,46 must find files. Got {result.TotalFiles}.");
    }

    [Fact]
    public async Task InteractiveModeFlow_NoBbox_ListsFilesFromDatasetPrefix()
    {
        // This test mimics EXACTLY what happens in interactive mode:
        // 1. User selects dataset (e.g., COP-DEM_GLO-90-DGED)
        // 2. No bounding box specified
        // 3. Listing should find files
        //
        // This test catches the "0 files found" bug reported by user

        // Step 1: Discover datasets (same as interactive mode)
        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo90 = datasets.First(d => d.Name.Contains("GLO-90-DGED") && !d.Name.Contains("PUBLIC"));

        // Step 2: Discover versions (same as interactive mode)
        var versions = await discoveryService.DiscoverVersionsAsync(glo90.FullPrefix);
        Assert.NotEmpty(versions);

        var selectedVersion = versions[0];
        var prefix = selectedVersion.FullPrefix;

        // Step 3: List objects directly using S3 client with the dataset prefix
        // This is what DownloaderService.ListObjectsAsync does
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = prefix
            // NO delimiter - should return files recursively
        };

        var response = await client.ListObjectsV2Async(listRequest);

        // CRITICAL: S3 should return files when listing with the dataset prefix
        Assert.True(response.S3Objects?.Count > 0,
            $"S3 should return files for prefix '{prefix}'. " +
            $"Got {response.S3Objects?.Count ?? 0} objects, IsTruncated={response.IsTruncated}");

        // Verify we got DEM files
        var demFiles = response.S3Objects!.Where(o => o.Key.EndsWith("_DEM.tif")).ToList();
        Assert.True(demFiles.Count > 0,
            $"Should find _DEM.tif files. First 5 keys: {string.Join(", ", response.S3Objects.Take(5).Select(o => o.Key))}");
    }

    [Fact]
    public async Task InteractiveModeFlow_DownloaderService_WithBbox_FindsFiles()
    {
        // This test verifies the DownloaderService flow with discovery + bbox.
        //
        // IMPORTANT: Full dataset listing WITHOUT a bbox takes 10+ minutes because
        // S3 must scan millions of auxiliary files (.kml, .xml in AUXFILES folders)
        // before reaching the actual DEM .tif files (which come later alphabetically).
        // A bounding box is REQUIRED for reasonable performance.

        var outputDir = CreateTempDirectory("_interactive_downloader");

        using var client = CreateS3Client();
        var discoveryService = new DatasetDiscoveryService(client, Bucket, BasePrefix);

        var datasets = await discoveryService.DiscoverDatasetsAsync();
        var glo90 = datasets.First(d => d.Name.Contains("GLO-90-DGED") && !d.Name.Contains("PUBLIC"));

        var versions = await discoveryService.DiscoverVersionsAsync(glo90.FullPrefix);
        Assert.NotEmpty(versions);

        var prefix = versions[0].FullPrefix;

        // Use a small bbox - required for reasonable listing performance
        var options = new DownloadOptions
        {
            AccessKey = AccessKey!,
            SecretKey = SecretKey!,
            Endpoint = Endpoint,
            Bucket = Bucket,
            Prefix = prefix,
            OutputDirectory = outputDir,
            Parallelism = 8,
            DryRun = true,
            Masks = MaskType.DEM | MaskType.WBM,
            BoundingBox = new BoundingBox(10, 45, 11, 46)  // Small bbox in Alps
        };

        var downloader = new DownloaderService(options);
        // GLO-90 listing can take 5-7 minutes even with bbox due to S3 scanning auxiliary files
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));

        var result = await downloader.RunAsync(ct: cts.Token);

        Assert.True(result.TotalFiles > 0,
            $"DownloaderService with bbox should find files. Got {result.TotalFiles}.");
    }
}
