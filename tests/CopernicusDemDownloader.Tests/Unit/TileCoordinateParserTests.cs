using CopernicusDemDownloader.Models;
using CopernicusDemDownloader.Services;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

public class TileCoordinateParserTests
{
    [Theory]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_DEM.tif", 45.0, 6.0)]
    [InlineData("Copernicus_DSM_COG_10_S45_00_W006_00_DEM.tif", -45.0, -6.0)]
    [InlineData("Copernicus_DSM_COG_10_N00_00_E000_00_DEM.tif", 0.0, 0.0)]
    [InlineData("Copernicus_DSM_COG_10_S89_00_W179_00_DEM.tif", -89.0, -179.0)]
    public void TryParseCoordinates_ValidTileNames_ReturnsCorrectCoordinates(string key, double expectedLat, double expectedLon)
    {
        var result = TileCoordinateParser.TryParseCoordinates(key);

        Assert.NotNull(result);
        Assert.Equal(expectedLat, result.Value.Lat, precision: 5);
        Assert.Equal(expectedLon, result.Value.Lon, precision: 5);
    }

    [Theory]
    [InlineData("Copernicus_DSM_COG_10_N45_30_E006_30_DEM.tif", 45.5, 6.5)]
    [InlineData("Copernicus_DSM_COG_10_S45_30_W006_30_DEM.tif", -45.5, -6.5)]
    public void TryParseCoordinates_WithMinutes_ReturnsCorrectCoordinates(string key, double expectedLat, double expectedLon)
    {
        var result = TileCoordinateParser.TryParseCoordinates(key);

        Assert.NotNull(result);
        Assert.Equal(expectedLat, result.Value.Lat, precision: 5);
        Assert.Equal(expectedLon, result.Value.Lon, precision: 5);
    }

    [Theory]
    [InlineData("some_random_file.tif")]
    [InlineData("data.csv")]
    [InlineData("")]
    public void TryParseCoordinates_InvalidFormat_ReturnsNull(string key)
    {
        var result = TileCoordinateParser.TryParseCoordinates(key);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("auxdata/CopDEM/COP-DEM_GLO-30-DGED/2024_1/Copernicus_DSM_COG_10_N45_00_E006_00_DEM.tif")]
    [InlineData("some/path/Copernicus_DSM_COG_10_S12_00_W045_00_DEM.tif")]
    public void TryParseCoordinates_WithFullPath_ExtractsCoordinates(string key)
    {
        var result = TileCoordinateParser.TryParseCoordinates(key);
        Assert.NotNull(result);
    }

    [Fact]
    public void IsInBoundingBox_TileInsideBbox_ReturnsTrue()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);
        var key = "Copernicus_DSM_COG_10_N45_00_E010_00_DEM.tif"; // Tile at (10, 45)

        Assert.True(TileCoordinateParser.IsInBoundingBox(key, bbox));
    }

    [Fact]
    public void IsInBoundingBox_TileOutsideBbox_ReturnsFalse()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);
        var key = "Copernicus_DSM_COG_10_N70_00_E050_00_DEM.tif"; // Tile at (50, 70) - outside

        Assert.False(TileCoordinateParser.IsInBoundingBox(key, bbox));
    }

    [Fact]
    public void IsInBoundingBox_UnparsableKey_ReturnsTrue()
    {
        var bbox = new BoundingBox(-10, 35, 30, 60);
        var key = "metadata.json"; // Not a tile file

        // Should return true to include non-standard files
        Assert.True(TileCoordinateParser.IsInBoundingBox(key, bbox));
    }

    [Fact]
    public void IsInBoundingBox_TileAtBboxEdge_ReturnsTrue()
    {
        var bbox = new BoundingBox(10, 45, 20, 55);
        var key = "Copernicus_DSM_COG_10_N45_00_E010_00_DEM.tif"; // Tile at exactly (10, 45)

        Assert.True(TileCoordinateParser.IsInBoundingBox(key, bbox));
    }

    [Theory]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_DEM.tif", MaskType.DEM, true)]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_EDM.tif", MaskType.DEM, false)]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_EDM.tif", MaskType.EDM, true)]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_DEM.tif", MaskType.DEM | MaskType.EDM, true)]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_EDM.tif", MaskType.DEM | MaskType.EDM, true)]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_FLM.tif", MaskType.DEM | MaskType.EDM, false)]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_HEM.tif", MaskType.All, true)]
    [InlineData("Copernicus_DSM_COG_10_N45_00_E006_00_WBM.tif", MaskType.All, true)]
    public void MatchesMaskFilter_VariousCombinations_ReturnsCorrectResult(string key, MaskType masks, bool expected)
    {
        var result = TileCoordinateParser.MatchesMaskFilter(key, masks);
        Assert.Equal(expected, result);
    }
}
