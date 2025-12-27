using System.Diagnostics;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Integration;

/// <summary>
/// Integration tests for CLI batch mode error handling and argument validation.
/// Actual download functionality is tested by DownloaderIntegrationTests which
/// call the DownloaderService directly - those tests are more reliable than
/// subprocess execution.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Integration)]
public class CliBatchIntegrationTests : IntegrationTestBase
{
    private readonly string _exePath;

    public CliBatchIntegrationTests()
    {
        var testDir = AppContext.BaseDirectory;
        _exePath = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..", "src", "CopernicusDemDownloader", "bin", "Debug", "net9.0", "CopernicusDemDownloader.exe"));

        if (!File.Exists(_exePath))
        {
            _exePath = Path.ChangeExtension(_exePath, ".dll");
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCliAsync(
        string arguments,
        int timeoutSeconds = 30,
        Dictionary<string, string?>? envOverrides = null)
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

        // Apply environment overrides
        if (envOverrides != null)
        {
            foreach (var kvp in envOverrides)
            {
                if (kvp.Value == null)
                    psi.Environment.Remove(kvp.Key);
                else
                    psi.Environment[kvp.Key] = kvp.Value;
            }
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

    [Fact]
    public async Task BatchMode_NoCredentials_ShowsError()
    {
        var outputDir = CreateTempDirectory("_cli_nocreds");

        var (_, output, error) = await RunCliAsync(
            $"--batch --dry-run --dataset GLO-90 --output \"{outputDir}\"",
            envOverrides: new Dictionary<string, string?>
            {
                ["CDSE_ACCESS_KEY"] = null,
                ["CDSE_SECRET_KEY"] = null
            });

        var combined = output + error;

        Assert.Contains("credentials", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchMode_OnlyAccessKey_ShowsCredentialsError()
    {
        var outputDir = CreateTempDirectory("_cli_partialcreds");

        var (_, output, error) = await RunCliAsync(
            $"--batch --dry-run --dataset GLO-90 --output \"{outputDir}\"",
            envOverrides: new Dictionary<string, string?>
            {
                ["CDSE_ACCESS_KEY"] = AccessKey,
                ["CDSE_SECRET_KEY"] = null
            });

        var combined = output + error;

        // Should require BOTH credentials (tests our HasCredentials fix)
        Assert.Contains("credentials", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchMode_OnlySecretKey_ShowsCredentialsError()
    {
        var outputDir = CreateTempDirectory("_cli_partialcreds2");

        var (_, output, error) = await RunCliAsync(
            $"--batch --dry-run --dataset GLO-90 --output \"{outputDir}\"",
            envOverrides: new Dictionary<string, string?>
            {
                ["CDSE_ACCESS_KEY"] = null,
                ["CDSE_SECRET_KEY"] = SecretKey
            });

        var combined = output + error;

        Assert.Contains("credentials", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchMode_InvalidDataset_ShowsError()
    {
        var outputDir = CreateTempDirectory("_cli_invalid");

        var (_, output, error) = await RunCliAsync(
            $"--batch --dry-run --dataset INVALID-DATASET --output \"{outputDir}\"",
            envOverrides: new Dictionary<string, string?>
            {
                ["CDSE_ACCESS_KEY"] = "dummy",
                ["CDSE_SECRET_KEY"] = "dummy"
            });

        var combined = output + error;

        Assert.Contains("Unknown dataset", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchMode_InvalidCredentials_ShowsS3Error()
    {
        var outputDir = CreateTempDirectory("_cli_badcreds");

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --dataset GLO-90 --output \"{outputDir}\" --bbox 20,-18,21,-17",
            timeoutSeconds: 60,
            envOverrides: new Dictionary<string, string?>
            {
                ["CDSE_ACCESS_KEY"] = "invalid_key",
                ["CDSE_SECRET_KEY"] = "invalid_secret"
            });

        // Should fail with S3 error (access denied or invalid credentials)
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task BatchMode_EmptyDataset_ShowsError()
    {
        // GLO-30-DTED exists in CDSE S3 but has no content (no DEM files)
        // The CLI should detect this and show a helpful error
        var outputDir = CreateTempDirectory("_cli_empty_dataset");

        var (exitCode, output, error) = await RunCliAsync(
            $"--batch --dry-run --dataset GLO-30-DTED --output \"{outputDir}\"",
            timeoutSeconds: 60,
            envOverrides: new Dictionary<string, string?>
            {
                ["CDSE_ACCESS_KEY"] = AccessKey,
                ["CDSE_SECRET_KEY"] = SecretKey
            });

        var combined = output + error;

        // Should show error about empty dataset and suggest DGED variant
        Assert.Contains("no DEM files", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DGED", combined);
    }

    // Note: Tests for actual download with valid credentials are in DownloaderIntegrationTests
    // which call the DownloaderService directly. CLI subprocess tests are unreliable for
    // long-running operations due to S3 listing timeouts.
}
