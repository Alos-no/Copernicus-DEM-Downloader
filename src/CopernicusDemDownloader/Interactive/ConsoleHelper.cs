using System.Text;
using CopernicusDemDownloader.Models;
using CopernicusDemDownloader.Services;

namespace CopernicusDemDownloader.Interactive;

/// <summary>
/// Helper class for interactive console prompts.
/// </summary>
public static class ConsoleHelper
{
    public static void WriteHeader()
    {
        Console.WriteLine();
        Console.WriteLine("==================================================================");
        Console.WriteLine("           Copernicus DEM Downloader v1.2.0                      ");
        Console.WriteLine("==================================================================");
        Console.WriteLine();
    }

    public static (string AccessKey, string SecretKey)? PromptCredentials(string? existingAccessKey, string? existingSecretKey)
    {
        var accessKey = existingAccessKey ?? Environment.GetEnvironmentVariable("CDSE_ACCESS_KEY");
        var secretKey = existingSecretKey ?? Environment.GetEnvironmentVariable("CDSE_SECRET_KEY");

        if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
        {
            Console.WriteLine("Using credentials from environment/arguments.");
            return (accessKey, secretKey);
        }

        Console.WriteLine("CDSE S3 credentials required.");
        Console.WriteLine("Get yours at: https://eodata-s3keysmanager.dataspace.copernicus.eu/");
        Console.WriteLine();

        if (string.IsNullOrEmpty(accessKey))
        {
            Console.Write("Access Key: ");
            accessKey = Console.ReadLine()?.Trim() ?? "";
        }

        if (string.IsNullOrEmpty(secretKey))
        {
            Console.Write("Secret Key: ");
            secretKey = ReadPassword();
            Console.WriteLine();
        }

        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
        {
            Console.Error.WriteLine("Error: Both access key and secret key are required.");
            return null;
        }

        Console.WriteLine("\nTip: Set CDSE_ACCESS_KEY and CDSE_SECRET_KEY environment variables to skip this prompt.");
        return (accessKey, secretKey);
    }

    public static string ReadPassword()
    {
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Length--;
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write("*");
            }
        }
        return password.ToString();
    }

    public static DiscoveredDataset? PromptDatasetSelection(List<DiscoveredDataset> datasets, string? preselected)
    {
        Console.WriteLine("\nAvailable datasets:");

        // Default to EEA-10 (10m) if available, otherwise GLO-30
        var defaultDataset = datasets.FirstOrDefault(d => d.Name.Contains("EEA-10"))
                          ?? datasets.FirstOrDefault(d => d.Name.Contains("GLO-30"))
                          ?? datasets.FirstOrDefault();

        for (int i = 0; i < datasets.Count; i++)
        {
            var ds = datasets[i];
            var isDefault = ds == defaultDataset;
            var marker = isDefault ? " [default]" : "";

            Console.WriteLine($"  [{i + 1}] {ds.Name}{marker}");
            if (ds.Info != null)
            {
                var formatInfo = !string.IsNullOrEmpty(ds.Info.FormatDescription) ? $", {ds.Info.FormatDescription}" : "";
                var publicInfo = ds.Info.IsPublic ? " (missing AM/AZ tiles)" : "";
                Console.WriteLine($"      {ds.Info.Resolution}m resolution, {ds.Info.Coverage} coverage{formatInfo}{publicInfo}");
            }
        }

        // Handle preselection
        if (!string.IsNullOrEmpty(preselected))
        {
            var match = datasets.FirstOrDefault(d => d.Name.Contains(preselected, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                Console.WriteLine($"\nUsing preselected: {match.Name}");
                return match;
            }
        }

        var defaultIdx = datasets.IndexOf(defaultDataset!) + 1;
        Console.Write($"\nSelect dataset [1-{datasets.Count}] (default: {defaultIdx}): ");
        var input = Console.ReadLine()?.Trim();

        int selection;
        if (string.IsNullOrEmpty(input))
        {
            selection = defaultIdx;
        }
        else if (!int.TryParse(input, out selection) || selection < 1 || selection > datasets.Count)
        {
            Console.WriteLine($"Invalid selection, using default.");
            selection = defaultIdx;
        }

        return datasets[selection - 1];
    }

    public static DatasetVersion? PromptVersionSelection(List<DatasetVersion> versions)
    {
        if (versions.Count == 0)
        {
            Console.WriteLine("\nNo versions found for this dataset.");
            return null;
        }

        if (versions.Count == 1)
        {
            Console.WriteLine($"\nUsing version: {versions[0]}");
            return versions[0];
        }

        Console.WriteLine("\nAvailable versions:");

        // Default to latest (first after sorting)
        for (int i = 0; i < versions.Count; i++)
        {
            var v = versions[i];
            var marker = i == 0 ? " [default - latest]" : "";
            Console.WriteLine($"  [{i + 1}] {v.Year}{(string.IsNullOrEmpty(v.Release) ? "" : $"_{v.Release}")}{marker}");
        }

        Console.Write($"\nSelect version [1-{versions.Count}] (default: 1): ");
        var input = Console.ReadLine()?.Trim();

        int selection;
        if (string.IsNullOrEmpty(input))
        {
            selection = 1;
        }
        else if (!int.TryParse(input, out selection) || selection < 1 || selection > versions.Count)
        {
            Console.WriteLine($"Invalid selection, using latest.");
            selection = 1;
        }

        return versions[selection - 1];
    }

    public static MaskType PromptMaskSelection(MaskType? preselected)
    {
        if (preselected.HasValue)
        {
            return preselected.Value;
        }

        Console.WriteLine("\nAvailable data layers:");

        var masks = new[] { MaskType.DEM, MaskType.EDM, MaskType.FLM, MaskType.HEM, MaskType.WBM };
        var selected = new HashSet<MaskType> { MaskType.DEM }; // DEM always selected by default

        for (int i = 0; i < masks.Length; i++)
        {
            var mask = masks[i];
            var isSelected = selected.Contains(mask);
            var isDefault = mask == MaskType.DEM;
            var marker = isDefault ? " (always included)" : "";

            Console.WriteLine($"  [{i + 1}] {mask,-4} - {mask.GetDescription()}{marker}");
        }

        Console.WriteLine();
        Console.WriteLine("Enter numbers to toggle selection (comma-separated), or press Enter for DEM only.");
        Console.WriteLine("Example: 2,3,4 to add EDM, FLM, HEM");
        Console.Write("\nSelect masks [default: 1 (DEM only)]: ");

        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
        {
            return MaskType.DEM;
        }

        // Parse selections
        MaskType result = MaskType.DEM; // Always include DEM
        var parts = input.Split(',', ' ', ';');

        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out var idx) && idx >= 1 && idx <= masks.Length)
            {
                result |= masks[idx - 1];
            }
        }

        // Show what was selected
        var selectedMasks = result.GetIndividualMasks().ToList();
        Console.WriteLine($"Selected: {string.Join(", ", selectedMasks)}");

        return result;
    }

    public static string PromptOutputDirectory(string? preselected, string datasetName)
    {
        if (!string.IsNullOrEmpty(preselected))
        {
            return preselected;
        }

        var defaultDir = $"./{datasetName}";
        Console.Write($"\nOutput directory [{defaultDir}]: ");
        var input = Console.ReadLine()?.Trim();

        return string.IsNullOrEmpty(input) ? defaultDir : input;
    }

    public static BoundingBox? PromptBoundingBox(string? preselected)
    {
        if (!string.IsNullOrEmpty(preselected))
        {
            var parsed = BoundingBox.TryParse(preselected);
            if (parsed != null && parsed.WasNormalized)
            {
                Console.WriteLine($"Note: Bounding box was normalized. {parsed.NormalizationWarning}");
            }
            return parsed;
        }

        Console.WriteLine("\nGeographic filter (optional):");
        Console.WriteLine("  Format: minLon,minLat,maxLon,maxLat");
        Console.WriteLine("  Example for Europe: -25,34,45,72");
        Console.WriteLine("  Leave empty for full dataset download");

        Console.Write("\nBounding box [none]: ");
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
            return null;

        var bbox = BoundingBox.TryParse(input);
        if (bbox == null)
        {
            Console.WriteLine("Invalid format, proceeding without geographic filter.");
            return null;
        }

        if (bbox.WasNormalized)
        {
            Console.WriteLine($"Note: {bbox.NormalizationWarning}");
            Console.WriteLine($"Using: {bbox}");
        }

        // Show approximate area
        Console.WriteLine($"Approximate area: {bbox.ApproxAreaKm2:N0} km²");

        return bbox;
    }

    public static int PromptParallelism(int? preselected)
    {
        var defaultValue = GetDefaultParallelism();

        if (preselected.HasValue)
        {
            return Math.Clamp(preselected.Value, 1, 32);
        }

        Console.WriteLine($"\nParallel download connections (more = faster, but may hit rate limits)");
        Console.Write($"Parallelism [1-32] (default: {defaultValue}): ");
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
            return defaultValue;

        if (int.TryParse(input, out var value) && value >= 1 && value <= 32)
        {
            return value;
        }

        Console.WriteLine($"Invalid value, using default: {defaultValue}");
        return defaultValue;
    }

    public static int GetDefaultParallelism()
    {
        var cores = Environment.ProcessorCount;
        return Math.Clamp(cores * 2, 4, 16);
    }

    public static bool PromptConfirmation(string prefix, string outputDir, MaskType masks, int parallelism, BoundingBox? bbox)
    {
        Console.WriteLine("\n------------------------------------------------------------------");
        Console.WriteLine("Configuration Summary:");
        Console.WriteLine($"  Dataset:     {prefix}");
        Console.WriteLine($"  Output:      {outputDir}");
        Console.WriteLine($"  Layers:      {string.Join(", ", masks.GetIndividualMasks())}");
        Console.WriteLine($"  Parallelism: {parallelism} connections");
        Console.WriteLine($"  Bbox:        {bbox?.ToString() ?? "None (full dataset)"}");
        Console.WriteLine("------------------------------------------------------------------");

        Console.Write("\nProceed with download? [Y/n]: ");
        var confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
        return confirm != "n" && confirm != "no";
    }

    public static void ShowProgress(DownloadProgress progress)
    {
        var speed = DownloaderService.FormatBytes(progress.BytesPerSecond);
        var downloaded = DownloaderService.FormatBytes(progress.DownloadedBytes);
        var total = DownloaderService.FormatBytes(progress.TotalBytes);

        Console.Write($"\r[{progress.PercentComplete,5:F1}%] {progress.CompletedFiles}/{progress.TotalFiles} files | " +
                      $"{downloaded}/{total} | {speed}/s | ETA: {FormatDuration(progress.EstimatedRemaining)}   ");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }
        return $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    public static void ShowResult(DownloadResult result)
    {
        Console.WriteLine("\n");
        Console.WriteLine("Download complete!");
        Console.WriteLine($"  Completed: {result.CompletedFiles} files ({DownloaderService.FormatBytes(result.DownloadedBytes)})");
        Console.WriteLine($"  Skipped:   {result.SkippedFiles} files (already downloaded)");
        Console.WriteLine($"  Failed:    {result.FailedFiles} files");
        Console.WriteLine($"  Time:      {FormatDuration(result.Elapsed)}");

        if (result.Elapsed.TotalSeconds > 0 && result.DownloadedBytes > 0)
        {
            Console.WriteLine($"  Speed:     {DownloaderService.FormatBytes((long)(result.DownloadedBytes / result.Elapsed.TotalSeconds))}/s");
        }
    }
}
