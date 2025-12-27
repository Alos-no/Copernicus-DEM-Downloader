using CopernicusDemDownloader.Services;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

/// <summary>
/// Tests for DownloadProgress and DownloadResult calculations.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Unit)]
public class DownloaderProgressTests
{
    [Theory]
    [InlineData(0, "0.0 B")]
    [InlineData(512, "512.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormatBytes_ReturnsCorrectFormat(long bytes, string expected)
    {
        var result = DownloaderService.FormatBytes(bytes);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatBytes_LargeValues_FormatsCorrectly()
    {
        var result = DownloaderService.FormatBytes(5_000_000_000_000); // 5 TB
        Assert.Contains("TB", result);
        Assert.StartsWith("4", result); // 5TB / 1024 = ~4.55 TB
    }

    [Fact]
    public void DownloadProgress_PercentComplete_CalculatesCorrectly()
    {
        var progress = new DownloadProgress
        {
            TotalBytes = 1000,
            DownloadedBytes = 250
        };

        Assert.Equal(25.0, progress.PercentComplete);
    }

    [Fact]
    public void DownloadProgress_PercentComplete_ZeroTotalBytes_ReturnsZero()
    {
        var progress = new DownloadProgress
        {
            TotalBytes = 0,
            DownloadedBytes = 0
        };

        Assert.Equal(0, progress.PercentComplete);
    }

    [Fact]
    public void DownloadProgress_BytesPerSecond_CalculatesCorrectly()
    {
        var progress = new DownloadProgress
        {
            DownloadedBytes = 10000,
            Elapsed = TimeSpan.FromSeconds(10)
        };

        Assert.Equal(1000, progress.BytesPerSecond);
    }

    [Fact]
    public void DownloadProgress_BytesPerSecond_ZeroElapsed_ReturnsZero()
    {
        var progress = new DownloadProgress
        {
            DownloadedBytes = 10000,
            Elapsed = TimeSpan.Zero
        };

        Assert.Equal(0, progress.BytesPerSecond);
    }

    [Fact]
    public void DownloadProgress_EstimatedRemaining_CalculatesCorrectly()
    {
        var progress = new DownloadProgress
        {
            TotalBytes = 10000,
            DownloadedBytes = 5000,
            Elapsed = TimeSpan.FromSeconds(10)
        };

        // 5000 bytes remaining, 500 bytes/second = 10 seconds
        Assert.Equal(TimeSpan.FromSeconds(10), progress.EstimatedRemaining);
    }

    [Fact]
    public void DownloadProgress_EstimatedRemaining_ZeroSpeed_ReturnsZero()
    {
        var progress = new DownloadProgress
        {
            TotalBytes = 10000,
            DownloadedBytes = 5000,
            Elapsed = TimeSpan.Zero
        };

        Assert.Equal(TimeSpan.Zero, progress.EstimatedRemaining);
    }

    [Fact]
    public void DownloadProgress_AllProperties_InitializeCorrectly()
    {
        var progress = new DownloadProgress
        {
            Status = "Downloading...",
            TotalFiles = 100,
            CompletedFiles = 50,
            SkippedFiles = 10,
            FailedFiles = 2,
            TotalBytes = 1000000,
            DownloadedBytes = 500000,
            Elapsed = TimeSpan.FromMinutes(5)
        };

        Assert.Equal("Downloading...", progress.Status);
        Assert.Equal(100, progress.TotalFiles);
        Assert.Equal(50, progress.CompletedFiles);
        Assert.Equal(10, progress.SkippedFiles);
        Assert.Equal(2, progress.FailedFiles);
        Assert.Equal(1000000, progress.TotalBytes);
        Assert.Equal(500000, progress.DownloadedBytes);
        Assert.Equal(TimeSpan.FromMinutes(5), progress.Elapsed);
    }

    [Fact]
    public void DownloadResult_AllProperties_InitializeCorrectly()
    {
        var files = new List<string> { "file1.tif", "file2.tif" };
        var result = new DownloadResult
        {
            TotalFiles = 100,
            TotalBytes = 1000000,
            CompletedFiles = 95,
            SkippedFiles = 3,
            FailedFiles = 2,
            DownloadedBytes = 950000,
            Elapsed = TimeSpan.FromMinutes(10),
            DryRun = false,
            Files = files
        };

        Assert.Equal(100, result.TotalFiles);
        Assert.Equal(1000000, result.TotalBytes);
        Assert.Equal(95, result.CompletedFiles);
        Assert.Equal(3, result.SkippedFiles);
        Assert.Equal(2, result.FailedFiles);
        Assert.Equal(950000, result.DownloadedBytes);
        Assert.Equal(TimeSpan.FromMinutes(10), result.Elapsed);
        Assert.False(result.DryRun);
        Assert.NotNull(result.Files);
        Assert.Equal(2, result.Files.Count);
    }

    [Fact]
    public void DownloadResult_DryRun_HasFilesProperty()
    {
        var result = new DownloadResult
        {
            TotalFiles = 10,
            TotalBytes = 10000,
            DryRun = true,
            Files = new List<string> { "file1.tif", "file2.tif", "file3.tif" }
        };

        Assert.True(result.DryRun);
        Assert.NotNull(result.Files);
        Assert.Equal(3, result.Files.Count);
    }

    [Fact]
    public void DownloadResult_NonDryRun_FilesCanBeNull()
    {
        var result = new DownloadResult
        {
            TotalFiles = 10,
            DryRun = false,
            Files = null
        };

        Assert.Null(result.Files);
    }
}
