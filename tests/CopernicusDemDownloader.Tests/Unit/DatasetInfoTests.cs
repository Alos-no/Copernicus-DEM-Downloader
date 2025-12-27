using CopernicusDemDownloader.Models;
using CopernicusDemDownloader.Services;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

/// <summary>
/// Tests for DatasetInfo, DiscoveredDataset, and DatasetDiscoveryService.KnownDatasets.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Unit)]
public class DatasetInfoTests
{
    [Fact]
    public void KnownDatasets_ContainsEEA10()
    {
        Assert.True(DatasetDiscoveryService.KnownDatasets.ContainsKey("EEA-10"));
    }

    [Fact]
    public void KnownDatasets_ContainsGLO30()
    {
        Assert.True(DatasetDiscoveryService.KnownDatasets.ContainsKey("GLO-30-DGED"));
    }

    [Fact]
    public void KnownDatasets_ContainsGLO90()
    {
        Assert.True(DatasetDiscoveryService.KnownDatasets.ContainsKey("GLO-90-DGED"));
    }

    [Fact]
    public void KnownDatasets_EEA10_Has10mResolution()
    {
        var dataset = DatasetDiscoveryService.KnownDatasets["EEA-10"];
        Assert.Equal(10, dataset.Resolution);
    }

    [Fact]
    public void KnownDatasets_GLO30_Has30mResolution()
    {
        var dataset = DatasetDiscoveryService.KnownDatasets["GLO-30-DGED"];
        Assert.Equal(30, dataset.Resolution);
    }

    [Fact]
    public void KnownDatasets_GLO90_Has90mResolution()
    {
        var dataset = DatasetDiscoveryService.KnownDatasets["GLO-90-DGED"];
        Assert.Equal(90, dataset.Resolution);
    }

    [Fact]
    public void KnownDatasets_EEA10_IsEuropean()
    {
        var dataset = DatasetDiscoveryService.KnownDatasets["EEA-10"];
        Assert.Equal(DatasetCoverage.European, dataset.Coverage);
    }

    [Fact]
    public void KnownDatasets_GLO30_IsGlobal()
    {
        var dataset = DatasetDiscoveryService.KnownDatasets["GLO-30-DGED"];
        Assert.Equal(DatasetCoverage.Global, dataset.Coverage);
    }

    [Fact]
    public void KnownDatasets_GLO90_IsGlobal()
    {
        var dataset = DatasetDiscoveryService.KnownDatasets["GLO-90-DGED"];
        Assert.Equal(DatasetCoverage.Global, dataset.Coverage);
    }

    [Fact]
    public void KnownDatasets_AllHaveDescriptions()
    {
        foreach (var kvp in DatasetDiscoveryService.KnownDatasets)
        {
            Assert.False(string.IsNullOrEmpty(kvp.Value.Description),
                $"Dataset {kvp.Key} should have a description");
        }
    }

    [Fact]
    public void KnownDatasets_AllHavePrefixes()
    {
        foreach (var kvp in DatasetDiscoveryService.KnownDatasets)
        {
            Assert.False(string.IsNullOrEmpty(kvp.Value.Prefix),
                $"Dataset {kvp.Key} should have a prefix");
        }
    }

    [Fact]
    public void KnownDatasets_PrefixesStartWithCOPDEM()
    {
        foreach (var kvp in DatasetDiscoveryService.KnownDatasets)
        {
            Assert.True(kvp.Value.Prefix.StartsWith("COP-DEM"),
                $"Dataset {kvp.Key} prefix should start with COP-DEM but was: {kvp.Value.Prefix}");
        }
    }

    [Fact]
    public void DatasetInfo_RecordEquality_Works()
    {
        var info1 = new DatasetInfo("Test", "prefix/", "Description", 30, DatasetCoverage.Global);
        var info2 = new DatasetInfo("Test", "prefix/", "Description", 30, DatasetCoverage.Global);
        var info3 = new DatasetInfo("Other", "prefix/", "Description", 30, DatasetCoverage.Global);

        Assert.Equal(info1, info2);
        Assert.NotEqual(info1, info3);
    }

    [Fact]
    public void DiscoveredDataset_Properties_Work()
    {
        var info = new DatasetInfo("Test", "prefix/", "Description", 30, DatasetCoverage.Global);
        var discovered = new DiscoveredDataset("COP-DEM_TEST", "auxdata/CopDEM/COP-DEM_TEST/", info);

        Assert.Equal("COP-DEM_TEST", discovered.Name);
        Assert.Equal("auxdata/CopDEM/COP-DEM_TEST/", discovered.FullPrefix);
        Assert.NotNull(discovered.Info);
        Assert.Equal(30, discovered.Info.Resolution);
    }

    [Fact]
    public void DiscoveredDataset_InfoCanBeNull()
    {
        var discovered = new DiscoveredDataset("Unknown", "auxdata/Unknown/", null);

        Assert.Equal("Unknown", discovered.Name);
        Assert.Null(discovered.Info);
    }

    [Fact]
    public void DatasetCoverage_HasExpectedValues()
    {
        Assert.Equal(0, (int)DatasetCoverage.Global);
        Assert.Equal(1, (int)DatasetCoverage.European);
    }
}
