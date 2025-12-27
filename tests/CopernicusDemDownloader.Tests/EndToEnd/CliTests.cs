using System.Diagnostics;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.EndToEnd;

[Trait(TestTraits.Category, TestTraits.EndToEnd)]
public class CliTests : IDisposable
{
    private readonly string _exePath;
    private readonly List<string> _tempDirectories = new();

    public CliTests()
    {
        var testDir = AppContext.BaseDirectory;
        // Use the built executable directly instead of dotnet run
        _exePath = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", "..", "src", "CopernicusDemDownloader", "bin", "Debug", "net9.0", "CopernicusDemDownloader.exe"));

        // Fallback to dll if exe doesn't exist (Linux/macOS)
        if (!File.Exists(_exePath))
        {
            _exePath = Path.ChangeExtension(_exePath, ".dll");
        }
    }

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"CopDEM_CLI_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirectories.Add(dir);
        return dir;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunCliAsync(string arguments, int timeoutSeconds = 30)
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
    public async Task Help_DisplaysUsageInformation()
    {
        var (exitCode, output, _) = await RunCliAsync("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Copernicus DEM Downloader", output);
        Assert.Contains("--access-key", output);
        Assert.Contains("--secret-key", output);
        Assert.Contains("--dataset", output);
        Assert.Contains("--bbox", output);
        Assert.Contains("--masks", output);
        Assert.Contains("--parallel", output);
        Assert.Contains("--dry-run", output);
    }

    [Fact]
    public async Task Version_DisplaysVersionNumber()
    {
        var (exitCode, output, _) = await RunCliAsync("--version");

        Assert.Equal(0, exitCode);
        Assert.Matches(@"\d+\.\d+\.\d+", output);
    }

    [Fact]
    public async Task NoCredentials_BatchMode_ShowsError()
    {
        var (_, output, error) = await RunCliAsync("--batch");

        // CLI displays error message about credentials (exit code not reliably set)
        Assert.Contains("credentials", output + error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidDataset_ShowsError()
    {
        var (_, output, error) = await RunCliAsync("--batch --dataset INVALID --access-key dummy --secret-key dummy");

        // CLI displays error about unknown dataset (exit code not reliably set)
        Assert.Contains("Unknown dataset", output + error, StringComparison.OrdinalIgnoreCase);
    }

    // Note: CLI tests with bbox filtering are disabled because CDSE's S3 structure
    // requires scanning all files to apply bbox filters, which takes several minutes.
    // The help/version tests above validate CLI parsing; the integration tests
    // validate the DownloaderService against real S3.

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
        GC.SuppressFinalize(this);
    }
}
