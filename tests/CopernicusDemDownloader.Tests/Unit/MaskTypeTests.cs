using CopernicusDemDownloader.Models;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

public class MaskTypeTests
{
    [Theory]
    [InlineData(MaskType.DEM, "_DEM.tif")]
    [InlineData(MaskType.EDM, "_EDM.tif")]
    [InlineData(MaskType.FLM, "_FLM.tif")]
    [InlineData(MaskType.HEM, "_HEM.tif")]
    [InlineData(MaskType.WBM, "_WBM.tif")]
    public void GetFileSuffix_ReturnsCorrectSuffix(MaskType mask, string expectedSuffix)
    {
        Assert.Equal(expectedSuffix, mask.GetFileSuffix());
    }

    [Fact]
    public void GetIndividualMasks_SingleMask_ReturnsSingleItem()
    {
        var masks = MaskType.DEM.GetIndividualMasks().ToList();

        Assert.Single(masks);
        Assert.Equal(MaskType.DEM, masks[0]);
    }

    [Fact]
    public void GetIndividualMasks_MultipleMasks_ReturnsAllItems()
    {
        var combined = MaskType.DEM | MaskType.EDM | MaskType.HEM;
        var masks = combined.GetIndividualMasks().ToList();

        Assert.Equal(3, masks.Count);
        Assert.Contains(MaskType.DEM, masks);
        Assert.Contains(MaskType.EDM, masks);
        Assert.Contains(MaskType.HEM, masks);
    }

    [Fact]
    public void GetIndividualMasks_AllMasks_ReturnsFiveItems()
    {
        var masks = MaskType.All.GetIndividualMasks().ToList();

        Assert.Equal(5, masks.Count);
        Assert.Contains(MaskType.DEM, masks);
        Assert.Contains(MaskType.EDM, masks);
        Assert.Contains(MaskType.FLM, masks);
        Assert.Contains(MaskType.HEM, masks);
        Assert.Contains(MaskType.WBM, masks);
    }

    [Fact]
    public void GetIndividualMasks_None_ReturnsEmpty()
    {
        var masks = MaskType.None.GetIndividualMasks().ToList();
        Assert.Empty(masks);
    }

    [Theory]
    [InlineData(MaskType.DEM)]
    [InlineData(MaskType.EDM)]
    [InlineData(MaskType.FLM)]
    [InlineData(MaskType.HEM)]
    [InlineData(MaskType.WBM)]
    public void GetDescription_AllMasks_ReturnsNonEmptyDescription(MaskType mask)
    {
        var description = mask.GetDescription();

        Assert.NotNull(description);
        Assert.NotEmpty(description);
        Assert.NotEqual(mask.ToString(), description); // Should be descriptive, not just the enum name
    }

    [Fact]
    public void MaskType_FlagsWork_CorrectCombination()
    {
        var combined = MaskType.DEM | MaskType.EDM;

        Assert.True(combined.HasFlag(MaskType.DEM));
        Assert.True(combined.HasFlag(MaskType.EDM));
        Assert.False(combined.HasFlag(MaskType.FLM));
    }

    [Fact]
    public void MaskType_All_IncludesAllMasks()
    {
        Assert.True(MaskType.All.HasFlag(MaskType.DEM));
        Assert.True(MaskType.All.HasFlag(MaskType.EDM));
        Assert.True(MaskType.All.HasFlag(MaskType.FLM));
        Assert.True(MaskType.All.HasFlag(MaskType.HEM));
        Assert.True(MaskType.All.HasFlag(MaskType.WBM));
    }
}
