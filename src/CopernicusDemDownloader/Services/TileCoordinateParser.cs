using System.Text.RegularExpressions;
using CopernicusDemDownloader.Models;

namespace CopernicusDemDownloader.Services;

/// <summary>
/// Parses tile coordinates from Copernicus DEM file names.
/// </summary>
public static partial class TileCoordinateParser
{
    // Matches coordinate patterns like _N45_00_E006_00_ or _S13_00_E021_00_
    // Example: Copernicus_DSM_30_S13_00_E021_00_DEM.tif
    [GeneratedRegex(@"_([NS])(\d+)_(\d+)_([EW])(\d+)_(\d+)_", RegexOptions.Compiled)]
    private static partial Regex TileCoordRegex();


    /// <summary>
    /// Try to extract tile coordinates from a file path/key.
    /// Returns null if the format doesn't match.
    /// </summary>
    public static (double Lat, double Lon)? TryParseCoordinates(string key)
    {
        var match = TileCoordRegex().Match(key);
        if (!match.Success)
            return null;

        var ns = match.Groups[1].Value;
        var latDeg = int.Parse(match.Groups[2].Value);
        var latMin = int.Parse(match.Groups[3].Value);
        var ew = match.Groups[4].Value;
        var lonDeg = int.Parse(match.Groups[5].Value);
        var lonMin = int.Parse(match.Groups[6].Value);

        // Convert to decimal degrees (tile corner is the SW corner)
        double lat = latDeg + latMin / 60.0;
        if (ns == "S") lat = -lat;

        double lon = lonDeg + lonMin / 60.0;
        if (ew == "W") lon = -lon;

        return (lat, lon);
    }

    /// <summary>
    /// Check if a file key falls within a bounding box.
    /// Returns true if the key cannot be parsed (to include non-standard files).
    /// </summary>
    public static bool IsInBoundingBox(string key, BoundingBox bbox)
    {
        var coords = TryParseCoordinates(key);
        if (coords == null)
            return true; // Include if we can't parse

        return bbox.IntersectsTile(coords.Value.Lon, coords.Value.Lat);
    }

    /// <summary>
    /// Check if a file matches the requested mask types.
    /// </summary>
    public static bool MatchesMaskFilter(string key, MaskType masks)
    {
        foreach (var mask in masks.GetIndividualMasks())
        {
            if (key.EndsWith(mask.GetFileSuffix(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

}
