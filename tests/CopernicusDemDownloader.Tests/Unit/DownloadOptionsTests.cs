using CopernicusDemDownloader.Models;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

/// <summary>
/// Tests for DownloadOptions record and its default values.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Unit)]
public class DownloadOptionsTests
{
    [Fact]
    public void DownloadOptions_RequiredProperties_MustBeSet()
    {
        var options = new DownloadOptions
        {
            AccessKey = "access",
            SecretKey = "secret",
            Prefix = "prefix/",
            OutputDirectory = "/output"
        };

        Assert.Equal("access", options.AccessKey);
        Assert.Equal("secret", options.SecretKey);
        Assert.Equal("prefix/", options.Prefix);
        Assert.Equal("/output", options.OutputDirectory);
    }

    [Fact]
    public void DownloadOptions_DefaultEndpoint_IsCorrect()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Equal("https://eodata.dataspace.copernicus.eu", options.Endpoint);
    }

    [Fact]
    public void DownloadOptions_DefaultBucket_IsCorrect()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Equal("eodata", options.Bucket);
    }

    [Fact]
    public void DownloadOptions_DefaultParallelism_Is8()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Equal(8, options.Parallelism);
    }

    [Fact]
    public void DownloadOptions_DefaultMaxRetries_Is3()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Equal(3, options.MaxRetries);
    }

    [Fact]
    public void DownloadOptions_DefaultStateFile_IsDownloadStateJson()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Equal("download_state.json", options.StateFile);
    }

    [Fact]
    public void DownloadOptions_DefaultForce_IsFalse()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.False(options.Force);
    }

    [Fact]
    public void DownloadOptions_DefaultDryRun_IsFalse()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.False(options.DryRun);
    }

    [Fact]
    public void DownloadOptions_DefaultMasks_IsDemOnly()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Equal(MaskType.DEM, options.Masks);
    }

    [Fact]
    public void DownloadOptions_DefaultBoundingBox_IsNull()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Null(options.BoundingBox);
    }

    [Fact]
    public void DownloadOptions_CustomValues_Override()
    {
        var bbox = new BoundingBox(10, 40, 15, 45);
        var options = new DownloadOptions
        {
            AccessKey = "custom_access",
            SecretKey = "custom_secret",
            Endpoint = "https://custom.endpoint.com",
            Bucket = "custombucket",
            Prefix = "custom/prefix/",
            OutputDirectory = "/custom/output",
            Parallelism = 16,
            MaxRetries = 5,
            StateFile = "custom_state.json",
            Force = true,
            DryRun = true,
            Masks = MaskType.DEM | MaskType.WBM,
            BoundingBox = bbox
        };

        Assert.Equal("custom_access", options.AccessKey);
        Assert.Equal("custom_secret", options.SecretKey);
        Assert.Equal("https://custom.endpoint.com", options.Endpoint);
        Assert.Equal("custombucket", options.Bucket);
        Assert.Equal("custom/prefix/", options.Prefix);
        Assert.Equal("/custom/output", options.OutputDirectory);
        Assert.Equal(16, options.Parallelism);
        Assert.Equal(5, options.MaxRetries);
        Assert.Equal("custom_state.json", options.StateFile);
        Assert.True(options.Force);
        Assert.True(options.DryRun);
        Assert.Equal(MaskType.DEM | MaskType.WBM, options.Masks);
        Assert.NotNull(options.BoundingBox);
        Assert.Equal(10, options.BoundingBox.MinLon);
    }

    [Fact]
    public void DownloadOptions_RecordEquality_Works()
    {
        var options1 = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        var options2 = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        var options3 = new DownloadOptions
        {
            AccessKey = "different",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o"
        };

        Assert.Equal(options1, options2);
        Assert.NotEqual(options1, options3);
    }

    [Fact]
    public void DownloadOptions_Parallelism_AcceptsRange1To32()
    {
        var options1 = new DownloadOptions
        {
            AccessKey = "a", SecretKey = "s", Prefix = "p/", OutputDirectory = "/o",
            Parallelism = 1
        };

        var options32 = new DownloadOptions
        {
            AccessKey = "a", SecretKey = "s", Prefix = "p/", OutputDirectory = "/o",
            Parallelism = 32
        };

        Assert.Equal(1, options1.Parallelism);
        Assert.Equal(32, options32.Parallelism);
    }

    [Fact]
    public void DownloadOptions_MasksCanBeCombined()
    {
        var options = new DownloadOptions
        {
            AccessKey = "a",
            SecretKey = "s",
            Prefix = "p/",
            OutputDirectory = "/o",
            Masks = MaskType.DEM | MaskType.EDM | MaskType.FLM | MaskType.HEM | MaskType.WBM
        };

        Assert.Equal(MaskType.All, options.Masks);
        Assert.True(options.Masks.HasFlag(MaskType.DEM));
        Assert.True(options.Masks.HasFlag(MaskType.WBM));
    }
}
