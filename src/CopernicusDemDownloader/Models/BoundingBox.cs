namespace CopernicusDemDownloader.Models;

/// <summary>
/// Represents a geographic bounding box with fault-tolerant parsing and normalization.
/// </summary>
public record BoundingBox
{
    public double MinLon { get; init; }
    public double MinLat { get; init; }
    public double MaxLon { get; init; }
    public double MaxLat { get; init; }

    /// <summary>
    /// Indicates if the original coordinates were swapped during normalization.
    /// </summary>
    public bool WasNormalized { get; init; }

    /// <summary>
    /// Warning message if the bounding box was modified during parsing.
    /// </summary>
    public string? NormalizationWarning { get; init; }

    public BoundingBox(double minLon, double minLat, double maxLon, double maxLat)
    {
        var warnings = new List<string>();
        bool normalized = false;

        // Swap if inverted longitude
        if (minLon > maxLon)
        {
            (minLon, maxLon) = (maxLon, minLon);
            warnings.Add($"Swapped inverted longitude values");
            normalized = true;
        }

        // Swap if inverted latitude
        if (minLat > maxLat)
        {
            (minLat, maxLat) = (maxLat, minLat);
            warnings.Add($"Swapped inverted latitude values");
            normalized = true;
        }

        // Clamp to valid ranges
        if (minLon < -180 || maxLon > 180 || minLat < -90 || maxLat > 90)
        {
            var origMinLon = minLon;
            var origMaxLon = maxLon;
            var origMinLat = minLat;
            var origMaxLat = maxLat;

            minLon = Math.Clamp(minLon, -180, 180);
            maxLon = Math.Clamp(maxLon, -180, 180);
            minLat = Math.Clamp(minLat, -90, 90);
            maxLat = Math.Clamp(maxLat, -90, 90);

            if (origMinLon != minLon || origMaxLon != maxLon || origMinLat != minLat || origMaxLat != maxLat)
            {
                warnings.Add($"Clamped coordinates to valid range [-180,180] x [-90,90]");
                normalized = true;
            }
        }

        MinLon = minLon;
        MinLat = minLat;
        MaxLon = maxLon;
        MaxLat = maxLat;
        WasNormalized = normalized;
        NormalizationWarning = warnings.Count > 0 ? string.Join("; ", warnings) : null;
    }

    /// <summary>
    /// Parse a bounding box from a comma-separated string.
    /// Handles various formats and normalizes the result.
    /// </summary>
    public static BoundingBox Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Bounding box string cannot be empty", nameof(input));

        // Support both comma and space separators
        var separators = new[] { ',', ' ', ';' };
        var parts = input.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 4)
            throw new ArgumentException(
                $"Bounding box must have 4 values (minLon,minLat,maxLon,maxLat), got {parts.Length}",
                nameof(input));

        if (!double.TryParse(parts[0], out var v1) ||
            !double.TryParse(parts[1], out var v2) ||
            !double.TryParse(parts[2], out var v3) ||
            !double.TryParse(parts[3], out var v4))
        {
            throw new ArgumentException("All bounding box values must be valid numbers", nameof(input));
        }

        return new BoundingBox(v1, v2, v3, v4);
    }

    /// <summary>
    /// Try to parse a bounding box, returning null on failure.
    /// </summary>
    public static BoundingBox? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        try
        {
            return Parse(input);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if a coordinate falls within (or overlaps with) this bounding box.
    /// For 1x1 degree tiles, checks if any part of the tile intersects.
    /// </summary>
    public bool IntersectsTile(double tileLon, double tileLat, double tileWidth = 1.0, double tileHeight = 1.0)
    {
        return MinLon <= tileLon + tileWidth && MaxLon >= tileLon &&
               MinLat <= tileLat + tileHeight && MaxLat >= tileLat;
    }

    /// <summary>
    /// Check if a point is within this bounding box.
    /// </summary>
    public bool Contains(double lon, double lat)
    {
        return lon >= MinLon && lon <= MaxLon && lat >= MinLat && lat <= MaxLat;
    }

    /// <summary>
    /// Get the area in square degrees.
    /// </summary>
    public double AreaDegrees => (MaxLon - MinLon) * (MaxLat - MinLat);

    /// <summary>
    /// Get approximate area in square kilometers (rough estimate at equator).
    /// </summary>
    public double ApproxAreaKm2
    {
        get
        {
            const double kmPerDegree = 111.32; // at equator
            var avgLat = (MinLat + MaxLat) / 2;
            var lonScale = Math.Cos(avgLat * Math.PI / 180);
            return (MaxLon - MinLon) * kmPerDegree * lonScale * (MaxLat - MinLat) * kmPerDegree;
        }
    }

    public override string ToString() => $"[{MinLon:F2},{MinLat:F2}] to [{MaxLon:F2},{MaxLat:F2}]";
}
