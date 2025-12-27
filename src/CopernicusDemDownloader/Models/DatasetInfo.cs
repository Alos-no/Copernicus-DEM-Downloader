namespace CopernicusDemDownloader.Models;

/// <summary>
/// Information about a Copernicus DEM dataset.
/// </summary>
public record DatasetInfo(
    string Name,
    string Prefix,
    string Description,
    int Resolution,
    DatasetCoverage Coverage,
    DatasetFormat Format = DatasetFormat.DGED,
    bool IsPublic = false
)
{
    public string FormatDescription => Format switch
    {
        DatasetFormat.DGED => "32-bit float, includes quality layers",
        DatasetFormat.DTED => "16-bit integer, smaller files",
        _ => ""
    };
}

/// <summary>
/// Data format type for Copernicus DEM.
/// </summary>
public enum DatasetFormat
{
    /// <summary>DEM Geocoding Editing - 32-bit floating point with quality layers.</summary>
    DGED,
    /// <summary>Digital Terrain Elevation Data - 16-bit integer, NATO standard.</summary>
    DTED
}

/// <summary>
/// Geographic coverage type.
/// </summary>
public enum DatasetCoverage
{
    Global,
    European
}

/// <summary>
/// Information about a specific dataset version/release.
/// </summary>
public record DatasetVersion(
    string Name,
    string FullPrefix,
    string Year,
    string Release
)
{
    /// <summary>
    /// Parse version info from a prefix like "COP-DEM_GLO-30-DGED__2024_1"
    /// </summary>
    public static DatasetVersion? TryParse(string prefix)
    {
        // Format: COP-DEM_GLO-30-DGED__2024_1 or COP-DEM_GLO-30-DGED (no version)
        var parts = prefix.Split("__");
        if (parts.Length == 2)
        {
            var versionParts = parts[1].Split('_');
            if (versionParts.Length >= 1)
            {
                var year = versionParts[0];
                var release = versionParts.Length > 1 ? versionParts[1] : "1";
                return new DatasetVersion(prefix, prefix, year, release);
            }
        }

        // No version suffix - might be the base dataset
        return new DatasetVersion(prefix, prefix, "Latest", "");
    }

    public override string ToString() => string.IsNullOrEmpty(Release)
        ? $"{Year}"
        : $"{Year}_{Release}";
}

/// <summary>
/// Mask file types available in Copernicus DEM.
/// </summary>
[Flags]
public enum MaskType
{
    None = 0,
    DEM = 1,      // Elevation data (always included)
    EDM = 2,      // Editing Mask - areas that were edited/corrected
    FLM = 4,      // Filling Mask - areas filled from other sources
    HEM = 8,      // Height Error Mask - estimated vertical accuracy
    WBM = 16,     // Water Body Mask - ocean/lake areas
    All = DEM | EDM | FLM | HEM | WBM
}

public static class MaskTypeExtensions
{
    public static string GetDescription(this MaskType mask) => mask switch
    {
        MaskType.DEM => "Digital Elevation Model (elevation data)",
        MaskType.EDM => "Editing Mask (areas that were edited/corrected)",
        MaskType.FLM => "Filling Mask (areas filled from other sources)",
        MaskType.HEM => "Height Error Mask (estimated vertical accuracy)",
        MaskType.WBM => "Water Body Mask (ocean/lake areas)",
        _ => mask.ToString()
    };

    public static string GetFileSuffix(this MaskType mask) => mask switch
    {
        MaskType.DEM => "_DEM.tif",
        MaskType.EDM => "_EDM.tif",
        MaskType.FLM => "_FLM.tif",
        MaskType.HEM => "_HEM.tif",
        MaskType.WBM => "_WBM.tif",
        _ => ".tif"
    };

    public static IEnumerable<MaskType> GetIndividualMasks(this MaskType masks)
    {
        if (masks.HasFlag(MaskType.DEM)) yield return MaskType.DEM;
        if (masks.HasFlag(MaskType.EDM)) yield return MaskType.EDM;
        if (masks.HasFlag(MaskType.FLM)) yield return MaskType.FLM;
        if (masks.HasFlag(MaskType.HEM)) yield return MaskType.HEM;
        if (masks.HasFlag(MaskType.WBM)) yield return MaskType.WBM;
    }
}
