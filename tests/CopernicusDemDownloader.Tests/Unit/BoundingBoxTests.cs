using CopernicusDemDownloader.Models;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

public class BoundingBoxTests
{
    [Fact]
    public void Parse_ValidInput_ReturnsCorrectBoundingBox()
    {
        var bbox = BoundingBox.Parse("-10,35,30,60");

        Assert.Equal(-10, bbox.MinLon);
        Assert.Equal(35, bbox.MinLat);
        Assert.Equal(30, bbox.MaxLon);
        Assert.Equal(60, bbox.MaxLat);
        Assert.False(bbox.WasNormalized);
    }

    [Fact]
    public void Parse_InvertedLongitude_SwapsValues()
    {
        var bbox = BoundingBox.Parse("30,35,-10,60");

        Assert.Equal(-10, bbox.MinLon);
        Assert.Equal(30, bbox.MaxLon);
        Assert.True(bbox.WasNormalized);
        Assert.Contains("longitude", bbox.NormalizationWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_InvertedLatitude_SwapsValues()
    {
        var bbox = BoundingBox.Parse("-10,60,30,35");

        Assert.Equal(35, bbox.MinLat);
        Assert.Equal(60, bbox.MaxLat);
        Assert.True(bbox.WasNormalized);
        Assert.Contains("latitude", bbox.NormalizationWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_BothInverted_SwapsBoth()
    {
        var bbox = BoundingBox.Parse("30,60,-10,35");

        Assert.Equal(-10, bbox.MinLon);
        Assert.Equal(35, bbox.MinLat);
        Assert.Equal(30, bbox.MaxLon);
        Assert.Equal(60, bbox.MaxLat);
        Assert.True(bbox.WasNormalized);
    }

    [Fact]
    public void Parse_OutOfRangeValues_ClampsToValidRange()
    {
        var bbox = BoundingBox.Parse("-200,35,200,60");

        Assert.Equal(-180, bbox.MinLon);
        Assert.Equal(180, bbox.MaxLon);
        Assert.True(bbox.WasNormalized);
        Assert.Contains("Clamped", bbox.NormalizationWarning);
    }

    [Fact]
    public void Parse_LatitudeOutOfRange_ClampsToValidRange()
    {
        var bbox = BoundingBox.Parse("-10,-100,30,100");

        Assert.Equal(-90, bbox.MinLat);
        Assert.Equal(90, bbox.MaxLat);
        Assert.True(bbox.WasNormalized);
    }

    [Theory]
    [InlineData("-10,35,30,60")]
    [InlineData("-10 35 30 60")]
    [InlineData("-10;35;30;60")]
    [InlineData("-10, 35, 30, 60")]
    public void Parse_DifferentSeparators_AllWork(string input)
    {
        var bbox = BoundingBox.Parse(input);

        Assert.Equal(-10, bbox.MinLon);
        Assert.Equal(35, bbox.MinLat);
        Assert.Equal(30, bbox.MaxLon);
        Assert.Equal(60, bbox.MaxLat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_EmptyOrNull_ThrowsArgumentException(string? input)
    {
        Assert.Throws<ArgumentException>(() => BoundingBox.Parse(input!));
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,4,5")]
    [InlineData("1,2")]
    public void Parse_WrongNumberOfValues_ThrowsArgumentException(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => BoundingBox.Parse(input));
        Assert.Contains("4 values", ex.Message);
    }

    [Theory]
    [InlineData("a,b,c,d")]
    [InlineData("1,2,three,4")]
    public void Parse_NonNumericValues_ThrowsArgumentException(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => BoundingBox.Parse(input));
        Assert.Contains("valid numbers", ex.Message);
    }

    [Fact]
    public void TryParse_ValidInput_ReturnsBoundingBox()
    {
        var bbox = BoundingBox.TryParse("-10,35,30,60");

        Assert.NotNull(bbox);
        Assert.Equal(-10, bbox.MinLon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("1,2,3")]
    public void TryParse_InvalidInput_ReturnsNull(string? input)
    {
        var bbox = BoundingBox.TryParse(input);
        Assert.Null(bbox);
    }

    [Fact]
    public void Contains_PointInside_ReturnsTrue()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);

        Assert.True(bbox.Contains(10, 45));
        Assert.True(bbox.Contains(-10, 35)); // Edge
        Assert.True(bbox.Contains(30, 60));  // Edge
    }

    [Fact]
    public void Contains_PointOutside_ReturnsFalse()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);

        Assert.False(bbox.Contains(-20, 45));
        Assert.False(bbox.Contains(10, 25));
        Assert.False(bbox.Contains(40, 45));
        Assert.False(bbox.Contains(10, 70));
    }

    [Fact]
    public void IntersectsTile_TileFullyInside_ReturnsTrue()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);

        Assert.True(bbox.IntersectsTile(5, 45)); // Tile at (5,45) to (6,46)
    }

    [Fact]
    public void IntersectsTile_TilePartiallyOverlapping_ReturnsTrue()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);

        // Tile at (-11, 34) to (-10, 35) - touches corner
        Assert.True(bbox.IntersectsTile(-11, 34));

        // Tile at (29, 59) to (30, 60) - touches corner
        Assert.True(bbox.IntersectsTile(29, 59));
    }

    [Fact]
    public void IntersectsTile_TileFullyOutside_ReturnsFalse()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);

        // Tile completely to the left
        Assert.False(bbox.IntersectsTile(-20, 45));

        // Tile completely below
        Assert.False(bbox.IntersectsTile(10, 25));
    }

    [Fact]
    public void IntersectsTile_BboxLargerThanDataset_WorksCorrectly()
    {
        // Bbox larger than entire world
        var bbox = new BoundingBox(-180, -90, 180, 90);

        // Any tile should intersect
        Assert.True(bbox.IntersectsTile(0, 0));
        Assert.True(bbox.IntersectsTile(-179, -89));
        Assert.True(bbox.IntersectsTile(179, 89));
    }

    [Fact]
    public void AreaDegrees_CalculatesCorrectly()
    {
        var bbox = new BoundingBox(0, 0, 10, 10);
        Assert.Equal(100, bbox.AreaDegrees);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var bbox = new BoundingBox(-10.5, 35.25, 30.75, 60.0);
        var str = bbox.ToString();

        Assert.Contains("-10.50", str);
        Assert.Contains("35.25", str);
        Assert.Contains("30.75", str);
        Assert.Contains("60.00", str);
    }
}
