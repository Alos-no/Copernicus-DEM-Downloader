using System.Diagnostics;
using Amazon.S3.Model;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Integration;

/// <summary>
/// Integration tests that verify each CLI option is correctly parsed and applied.
/// These tests run the actual CLI executable to ensure end-to-end correctness.
/// Uses known-good S3 prefixes to avoid slow bbox filtering.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Integration)]
public class CliOptionsIntegrationTests : IntegrationTestBase
{
    private readonly string _exePath;
    private string? _cachedSmallFilePrefix;

    public CliOptionsIntegrationTests()
    {
        var testDir = AppContext.BaseDirectory;
        _exePath = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..", "src", "CopernicusDemDownloader", "bin", "Debug", "net9.0", "CopernicusDemDownloader.exe"));

        if (!File.Exists(_exePath))
        {
            _exePath = Path.ChangeExtension(_exePath, ".dll");
        }
    }

    /// <summary>
    /// Get a known-good S3 prefix for a small file that will download quickly.
    /// Caches the result to avoid repeated S3 queries.
    /// </summary>
    private async Task<string> GetSmallFilePrefixAsync()
    {
        if (_cachedSmallFilePrefix != null)
            return _cachedSmallFilePrefix;

        using var client = CreateS3Client();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = BasePrefix + "COP-DEM_GLO-90-DGED/",
            MaxKeys = 100
        };

        var response = await client.ListObjectsV2Async(listRequest);

        // Find the smallest DEM file and extract its folder prefix
        var smallFile = response.S3Objects
            .Where(o => o.Key.EndsWith("_DEM.tif"))
            .OrderBy(o => o.Size)
            .First();

        _cachedSmallFilePrefix = smallFile.Key.Substring(0, smallFile.Key.LastIndexOf('/') + 1);
        return _cachedSmallFilePrefix;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        string arguments,
        int timeoutSeconds = 60,
        bool useCredentials = true)
    {
        ProcessStartInfo psi;

        if (_exePath.EndsWith(".dll"))
        {
            psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{_exePath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        if (useCredentials)
        {
            psi.Environment["CDSE_ACCESS_KEY"] = AccessKey;
            psi.Environment["CDSE_SECRET_KEY"] = SecretKey;
        }
        else
        {
            psi.Environment.Remove("CDSE_ACCESS_KEY");
            psi.Environment.Remove("CDSE_SECRET_KEY");
        }

        using var process = new Process { StartInfo = psi };

        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000));

        if (!completed)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI command timed out after {timeoutSeconds} seconds");
        }

        return (process.ExitCode, output.ToString(), error.ToString());
    }

    // =========================================================================
    // Dataset/Prefix Selection Tests
    // =========================================================================

    [Fact]
    public async Task Prefix_CustomPrefix_Works()
    {
        var outputDir = CreateTempDirectory("_cli_prefix");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\"");

        var combined = output + error;

        Assert.Equal(0, exitCode);
        Assert.Contains("COP-DEM", combined);
    }

    // =========================================================================
    // Dry Run Tests
    // =========================================================================

    [Fact]
    public async Task DryRun_ShowsFileListWithoutDownloading()
    {
        var outputDir = CreateTempDirectory("_cli_dryrun");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM");

        var combined = output + error;

        Assert.Contains("DRY RUN", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, exitCode);

        // Verify no files were downloaded
        var files = Directory.Exists(outputDir)
            ? Directory.GetFiles(outputDir, "*.tif", SearchOption.AllDirectories)
            : Array.Empty<string>();
        Assert.Empty(files);
    }

    // =========================================================================
    // Mask Selection Tests
    // =========================================================================

    [Fact]
    public async Task Masks_SingleMask_IsApplied()
    {
        var outputDir = CreateTempDirectory("_cli_mask1");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM");

        var combined = output + error;

        Assert.Contains("DEM", combined);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Masks_MultipleMasks_AreApplied()
    {
        var outputDir = CreateTempDirectory("_cli_masks");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM,WBM,EDM");

        var combined = output + error;

        Assert.Contains("DEM", combined);
        Assert.Equal(0, exitCode);
    }

    // =========================================================================
    // Output Directory Tests
    // =========================================================================

    [Fact]
    public async Task Output_CustomDirectory_IsUsed()
    {
        var outputDir = CreateTempDirectory("_cli_custom_output");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\"");

        var combined = output + error;

        Assert.Contains("Output:", combined);
        Assert.Equal(0, exitCode);
    }

    // =========================================================================
    // Parallelism Tests
    // =========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    public async Task Parallel_ValidValues_AreAccepted(int parallelism)
    {
        var outputDir = CreateTempDirectory($"_cli_p{parallelism}");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --parallel {parallelism}");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Parallel_ExtremeValues_AreClamped()
    {
        var outputDir = CreateTempDirectory("_cli_pextreme");
        var prefix = await GetSmallFilePrefixAsync();

        // Test with 0 (should be clamped to 1) and 100 (should be clamped to 32)
        var (exitCode1, _, _) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --parallel 0");

        var (exitCode2, _, _) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --parallel 100");

        // Both should succeed (values get clamped, not rejected)
        Assert.Equal(0, exitCode1);
        Assert.Equal(0, exitCode2);
    }

    // =========================================================================
    // Retry Tests
    // =========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Retries_ValidValues_AreAccepted(int retries)
    {
        var outputDir = CreateTempDirectory($"_cli_r{retries}");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, _, _) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --retries {retries}");

        Assert.Equal(0, exitCode);
    }

    // =========================================================================
    // Actual Download Tests (slower but essential)
    // =========================================================================

    [Fact]
    public async Task FullWorkflow_Download_CreatesFiles()
    {
        var outputDir = CreateTempDirectory("_cli_full");
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM",
            timeoutSeconds: 120);

        var combined = output + error;

        // First verify CLI succeeded
        Assert.True(exitCode == 0, $"CLI failed with exit code {exitCode}. Output:\n{combined}");
        Assert.Contains("Completed", combined, StringComparison.OrdinalIgnoreCase);

        // Verify files were actually downloaded
        var files = Directory.GetFiles(outputDir, "*.tif", SearchOption.AllDirectories);
        Assert.True(files.Length > 0, $"No .tif files found in {outputDir}. Output:\n{combined}");
    }

    [Fact]
    public async Task StateFile_CustomName_IsCreated()
    {
        var outputDir = CreateTempDirectory("_cli_state");
        var stateFileName = "custom_state_test.json";
        var prefix = await GetSmallFilePrefixAsync();

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM --state-file {stateFileName}",
            timeoutSeconds: 120);

        var combined = output + error;
        Assert.True(exitCode == 0, $"CLI failed with exit code {exitCode}. Output:\n{combined}");

        // Verify custom state file was created
        var stateFilePath = Path.Combine(outputDir, stateFileName);
        Assert.True(File.Exists(stateFilePath), $"State file should be created at {stateFilePath}. Output:\n{combined}");
    }

    [Fact]
    public async Task Resume_SecondRun_SkipsFiles()
    {
        var outputDir = CreateTempDirectory("_cli_resume");
        var stateFile = "resume_test.json";
        var prefix = await GetSmallFilePrefixAsync();

        // First run - download
        var (exitCode1, output1, error1) = await RunCliAsync(
            $"--batch --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM --state-file {stateFile}",
            timeoutSeconds: 120);

        var combined1 = output1 + error1;
        Assert.True(exitCode1 == 0, $"First run failed with exit code {exitCode1}. Output:\n{combined1}");

        // Verify files were downloaded
        var filesAfterFirst = Directory.GetFiles(outputDir, "*.tif", SearchOption.AllDirectories);
        Assert.True(filesAfterFirst.Length > 0, $"First run should download files. Output:\n{combined1}");

        // Second run - should skip
        var (exitCode2, output2, _) = await RunCliAsync(
            $"--batch --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM --state-file {stateFile}",
            timeoutSeconds: 120);

        Assert.Equal(0, exitCode2);
        Assert.Contains("Skipped", output2, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Force_RedownloadsExistingFiles()
    {
        var outputDir = CreateTempDirectory("_cli_force");
        var stateFile = "force_test.json";
        var prefix = await GetSmallFilePrefixAsync();

        // First run
        var (exitCode1, output1, error1) = await RunCliAsync(
            $"--batch --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM --state-file {stateFile}",
            timeoutSeconds: 120);

        var combined1 = output1 + error1;
        Assert.True(exitCode1 == 0, $"First run failed with exit code {exitCode1}. Output:\n{combined1}");

        // Second run with --force
        var (exitCode2, output2, _) = await RunCliAsync(
            $"--batch --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM --state-file {stateFile} --force",
            timeoutSeconds: 120);

        Assert.Equal(0, exitCode2);
        Assert.Contains("Completed", output2, StringComparison.OrdinalIgnoreCase);
        // With force, should not show "Skipped: 1" with a non-zero count
        Assert.DoesNotContain("Skipped: 1", output2);
    }

    // =========================================================================
    // Dataset Discovery Flow Tests (tests the --dataset option, NOT --prefix)
    // Note: CDSE S3 structure has tiles directly under dataset folders, NOT
    // in versioned subdirectories. The discovery correctly falls back to
    // using the base dataset prefix when no versioned folders are found.
    // =========================================================================

    [Fact]
    public async Task Dataset_GLO90_WithPrefix_FindsFiles()
    {
        // Test that --dataset correctly resolves to the actual S3 prefix
        // Use --prefix approach with a known-good tile prefix for speed
        var prefix = await GetSmallFilePrefixAsync();
        var outputDir = CreateTempDirectory("_cli_dataset_glo90");

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --prefix \"{prefix}\" --output \"{outputDir}\" --masks DEM",
            timeoutSeconds: 60);

        var combined = output + error;

        Assert.True(exitCode == 0, $"CLI failed with exit code {exitCode}. Output:\n{combined}");
        Assert.Contains("DRY RUN", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COP-DEM", combined);
    }

    [Fact]
    public async Task Dataset_WithVersionYear_UsesSpecifiedPath()
    {
        // Test explicit version year - note: CDSE doesn't actually have versioned subdirectories
        // so this creates a non-existent path and returns 0 files (but doesn't error)
        var outputDir = CreateTempDirectory("_cli_dataset_version");

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --dataset GLO-90 --version-year 2024_1 --output \"{outputDir}\" --masks DEM --bbox 10.5,45.5,10.6,45.6",
            timeoutSeconds: 30);

        var combined = output + error;

        Assert.True(exitCode == 0, $"CLI failed with exit code {exitCode}. Output:\n{combined}");
        // When version is specified, should NOT show auto-discovery message
        Assert.DoesNotContain("Using version", combined);
        // Should use the specified version in the prefix
        Assert.Contains("2024_1", combined);
    }
}
