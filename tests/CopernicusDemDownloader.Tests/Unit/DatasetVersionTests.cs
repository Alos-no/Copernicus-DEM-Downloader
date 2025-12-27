using CopernicusDemDownloader.Models;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

public class DatasetVersionTests
{
    [Theory]
    [InlineData("COP-DEM_GLO-30-DGED__2024_1", "2024", "1")]
    [InlineData("COP-DEM_GLO-30-DGED__2023_1", "2023", "1")]
    [InlineData("COP-DEM_EEA-10-DGED__2022_2", "2022", "2")]
    public void TryParse_ValidVersionedPrefix_ReturnsCorrectVersion(string prefix, string expectedYear, string expectedRelease)
    {
        var version = DatasetVersion.TryParse(prefix);

        Assert.NotNull(version);
        Assert.Equal(expectedYear, version.Year);
        Assert.Equal(expectedRelease, version.Release);
    }

    [Fact]
    public void TryParse_NoVersionSuffix_ReturnsLatest()
    {
        var version = DatasetVersion.TryParse("COP-DEM_GLO-30-DGED");

        Assert.NotNull(version);
        Assert.Equal("Latest", version.Year);
        Assert.Equal("", version.Release);
    }

    [Fact]
    public void ToString_WithRelease_IncludesRelease()
    {
        var version = new DatasetVersion("test", "test", "2024", "1");
        Assert.Equal("2024_1", version.ToString());
    }

    [Fact]
    public void ToString_WithoutRelease_OmitsUnderscore()
    {
        var version = new DatasetVersion("test", "test", "2024", "");
        Assert.Equal("2024", version.ToString());
    }

    [Fact]
    public void ToString_Latest_ShowsLatest()
    {
        var version = new DatasetVersion("test", "test", "Latest", "");
        Assert.Equal("Latest", version.ToString());
    }
}
