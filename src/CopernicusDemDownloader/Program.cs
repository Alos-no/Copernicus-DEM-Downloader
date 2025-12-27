using System.CommandLine;
using CopernicusDemDownloader.Interactive;
using CopernicusDemDownloader.Models;
using CopernicusDemDownloader.Services;

const string DefaultEndpoint = "https://eodata.dataspace.copernicus.eu";
const string DefaultBucket = "eodata";
const string BasePrefix = "auxdata/CopDEM/";

// CLI Options
var interactiveOption = new Option<bool>("--interactive", () => false, "Force interactive mode");
var batchOption = new Option<bool>("--batch", () => false, "Non-interactive batch mode with defaults");
var accessKeyOption = new Option<string?>("--access-key", "S3 access key (or set CDSE_ACCESS_KEY env var)");
var secretKeyOption = new Option<string?>("--secret-key", "S3 secret key (or set CDSE_SECRET_KEY env var)");
var outputOption = new Option<DirectoryInfo?>("--output", "Output directory");
var endpointOption = new Option<string>("--endpoint", () => DefaultEndpoint, "S3 endpoint URL");
var bucketOption = new Option<string>("--bucket", () => DefaultBucket, "S3 bucket name");
var datasetOption = new Option<string?>("--dataset", "Dataset: EEA-10, GLO-30[-DGED|-DTED], GLO-90[-DGED|-DTED] (add _PUBLIC for public variants)");
var versionOption = new Option<string?>("--version-year", "Dataset version year (e.g., 2024_1)");
var prefixOption = new Option<string?>("--prefix", "Custom S3 prefix path (overrides --dataset)");
var parallelOption = new Option<int?>("--parallel", "Number of parallel downloads");
var retryOption = new Option<int>("--retries", () => 3, "Max retry attempts per file");
var bboxOption = new Option<string?>("--bbox", "Bounding box: minLon,minLat,maxLon,maxLat");
var dryRunOption = new Option<bool>("--dry-run", () => false, "List files without downloading");
var masksOption = new Option<string?>("--masks", "Mask types to include: DEM,EDM,FLM,HEM,WBM (comma-separated)");
var stateFileOption = new Option<string>("--state-file", () => "download_state.json", "State file for resume");
var forceOption = new Option<bool>("--force", () => false, "Re-download existing files");

var rootCommand = new RootCommand("Copernicus DEM Downloader - Downloads elevation data from CDSE")
{
    interactiveOption, batchOption, accessKeyOption, secretKeyOption, outputOption,
    endpointOption, bucketOption, datasetOption, versionOption, prefixOption, parallelOption,
    retryOption, bboxOption, dryRunOption, masksOption, stateFileOption, forceOption
};

rootCommand.SetHandler(async (context) =>
{
    var ct = context.GetCancellationToken();
    var parseResult = context.ParseResult;

    var batch = parseResult.GetValueForOption(batchOption);
    var interactive = parseResult.GetValueForOption(interactiveOption);

    // Determine if we should run interactively
    bool isInteractive = interactive || (!batch && !HasCredentials(parseResult, accessKeyOption, secretKeyOption));

    try
    {
        if (isInteractive)
        {
            await RunInteractiveAsync(parseResult, ct);
        }
        else
        {
            await RunBatchAsync(parseResult, ct);
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\nOperation cancelled.");
        context.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"\nError: {ex.Message}");
        context.ExitCode = 1;
    }
});

return await rootCommand.InvokeAsync(args);

// ============================================================================
// Helper Methods
// ============================================================================

bool HasCredentials(System.CommandLine.Parsing.ParseResult parseResult, Option<string?> accessKeyOpt, Option<string?> secretKeyOpt)
{
    var accessKey = parseResult.GetValueForOption(accessKeyOpt)
                    ?? Environment.GetEnvironmentVariable("CDSE_ACCESS_KEY");
    var secretKey = parseResult.GetValueForOption(secretKeyOpt)
                    ?? Environment.GetEnvironmentVariable("CDSE_SECRET_KEY");

    return !string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey);
}

string NormalizeDatasetKey(string name)
{
    var upper = name.ToUpperInvariant().Replace("_", "-");

    // Handle shorthand names (backwards compatibility)
    return upper switch
    {
        "GLO-30" or "GLO30" => "GLO-30-DGED",
        "GLO-90" or "GLO90" => "GLO-90-DGED",
        "GLO-30-PUBLIC" => "GLO-30-DGED-PUBLIC",
        "GLO-90-PUBLIC" => "GLO-90-DGED-PUBLIC",
        "EEA-10" or "EEA10" => "EEA-10",
        _ => upper
    };
}

MaskType ParseMasks(string? masksStr)
{
    if (string.IsNullOrEmpty(masksStr))
        return MaskType.DEM;

    MaskType result = MaskType.None;
    foreach (var part in masksStr.Split(',', ' ', ';'))
    {
        if (Enum.TryParse<MaskType>(part.Trim(), ignoreCase: true, out var mask))
        {
            result |= mask;
        }
    }

    // Always include DEM
    return result == MaskType.None ? MaskType.DEM : result | MaskType.DEM;
}

async Task RunInteractiveAsync(System.CommandLine.Parsing.ParseResult parseResult, CancellationToken ct)
{
    ConsoleHelper.WriteHeader();

    // Get credentials
    var credentials = ConsoleHelper.PromptCredentials(
        parseResult.GetValueForOption(accessKeyOption),
        parseResult.GetValueForOption(secretKeyOption));

    if (credentials == null)
        return;

    var (accessKey, secretKey) = credentials.Value;
    var endpoint = parseResult.GetValueForOption(endpointOption)!;
    var bucket = parseResult.GetValueForOption(bucketOption)!;

    // Discover datasets
    Console.WriteLine("\nConnecting to CDSE and discovering available datasets...");
    var client = DownloaderService.CreateClient(accessKey, secretKey, endpoint);
    var discoveryService = new DatasetDiscoveryService(client, bucket, BasePrefix);

    var datasets = await discoveryService.DiscoverDatasetsAsync(ct);
    if (datasets.Count == 0)
    {
        Console.Error.WriteLine("Error: No datasets found. Check your credentials and network connection.");
        return;
    }

    // Select dataset
    var customPrefix = parseResult.GetValueForOption(prefixOption);
    string prefix;

    if (!string.IsNullOrEmpty(customPrefix))
    {
        prefix = customPrefix;
        Console.WriteLine($"\nUsing custom prefix: {prefix}");
    }
    else
    {
        var selectedDataset = ConsoleHelper.PromptDatasetSelection(datasets, parseResult.GetValueForOption(datasetOption));
        if (selectedDataset == null)
            return;

        // Discover and select version
        Console.WriteLine("\nDiscovering available versions...");
        var versions = await discoveryService.DiscoverVersionsAsync(selectedDataset.FullPrefix, ct);

        var selectedVersion = ConsoleHelper.PromptVersionSelection(versions);
        prefix = selectedVersion?.FullPrefix ?? selectedDataset.FullPrefix;
    }

    // Select masks
    var masks = ConsoleHelper.PromptMaskSelection(
        parseResult.GetValueForOption(masksOption) is string m ? ParseMasks(m) : null);

    // Select output directory
    var datasetName = prefix.Split('/').FirstOrDefault(s => s.StartsWith("COP-DEM")) ?? "CopDEM";
    var outputDir = ConsoleHelper.PromptOutputDirectory(
        parseResult.GetValueForOption(outputOption)?.FullName,
        datasetName);

    // Bounding box
    var bbox = ConsoleHelper.PromptBoundingBox(parseResult.GetValueForOption(bboxOption));

    // Parallelism
    var parallelism = ConsoleHelper.PromptParallelism(parseResult.GetValueForOption(parallelOption));

    // Confirm
    var dryRun = parseResult.GetValueForOption(dryRunOption);
    if (!dryRun && !ConsoleHelper.PromptConfirmation(prefix, outputDir, masks, parallelism, bbox))
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    // Run download
    var options = new DownloadOptions
    {
        AccessKey = accessKey,
        SecretKey = secretKey,
        Endpoint = endpoint,
        Bucket = bucket,
        Prefix = prefix,
        OutputDirectory = outputDir,
        Parallelism = parallelism,
        MaxRetries = parseResult.GetValueForOption(retryOption),
        StateFile = parseResult.GetValueForOption(stateFileOption)!,
        Force = parseResult.GetValueForOption(forceOption),
        DryRun = dryRun,
        Masks = masks,
        BoundingBox = bbox
    };

    var downloader = new DownloaderService(options);
    var progress = new Progress<DownloadProgress>(ConsoleHelper.ShowProgress);

    var result = await downloader.RunAsync(progress, ct);

    if (result.DryRun)
    {
        Console.WriteLine($"\n[DRY RUN] Would download {result.TotalFiles} files ({DownloaderService.FormatBytes(result.TotalBytes)})");
        if (result.Files != null)
        {
            foreach (var file in result.Files.Take(20))
            {
                Console.WriteLine($"  {file}");
            }
            if (result.Files.Count > 20)
            {
                Console.WriteLine($"  ... and {result.Files.Count - 20} more");
            }
        }
    }
    else
    {
        ConsoleHelper.ShowResult(result);
    }
}

async Task RunBatchAsync(System.CommandLine.Parsing.ParseResult parseResult, CancellationToken ct)
{
    var accessKey = parseResult.GetValueForOption(accessKeyOption)
                    ?? Environment.GetEnvironmentVariable("CDSE_ACCESS_KEY");
    var secretKey = parseResult.GetValueForOption(secretKeyOption)
                    ?? Environment.GetEnvironmentVariable("CDSE_SECRET_KEY");

    if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
    {
        Console.Error.WriteLine("Error: S3 credentials required.");
        Console.Error.WriteLine("  Set CDSE_ACCESS_KEY and CDSE_SECRET_KEY environment variables,");
        Console.Error.WriteLine("  or use --access-key and --secret-key options,");
        Console.Error.WriteLine("  or run without --batch for interactive mode.");
        return;
    }

    var endpoint = parseResult.GetValueForOption(endpointOption)!;
    var bucket = parseResult.GetValueForOption(bucketOption)!;

    // Determine prefix
    string prefix;
    var customPrefix = parseResult.GetValueForOption(prefixOption);
    var datasetName = parseResult.GetValueForOption(datasetOption) ?? "GLO-30";
    var versionYear = parseResult.GetValueForOption(versionOption);

    if (!string.IsNullOrEmpty(customPrefix))
    {
        prefix = customPrefix;
    }
    else
    {
        // Normalize dataset name for backwards compatibility
        var key = NormalizeDatasetKey(datasetName);
        if (!DatasetDiscoveryService.KnownDatasets.TryGetValue(key, out var info))
        {
            Console.Error.WriteLine($"Error: Unknown dataset '{datasetName}'.");
            Console.Error.WriteLine("  Available: EEA-10, GLO-30, GLO-30-DTED, GLO-90, GLO-90-DTED");
            Console.Error.WriteLine("  Add -PUBLIC suffix for public variants (e.g., GLO-30-PUBLIC)");
            Console.Error.WriteLine("  Note: EEA-10 requires CCM (Copernicus Contributing Missions) access.");
            return;
        }

        // EEA-10 uses CCM prefix, others use auxdata/CopDEM prefix
        var datasetBasePrefix = info.Name == "EEA-10" ? DatasetDiscoveryService.CcmPrefix : BasePrefix;
        var datasetPrefix = datasetBasePrefix + info.Prefix + "/";

        // If version specified, append it; otherwise auto-discover the latest version
        if (!string.IsNullOrEmpty(versionYear))
        {
            prefix = datasetPrefix + versionYear.TrimEnd('/') + "/";
        }
        else
        {
            // Auto-discover the actual dataset from S3 and find latest version
            var client = DownloaderService.CreateClient(accessKey, secretKey, endpoint);
            var discoveryService = new DatasetDiscoveryService(client, bucket, BasePrefix);

            // First, discover actual datasets from S3 to get the correct prefix
            var datasets = await discoveryService.DiscoverDatasetsAsync(ct);
            var matchingDataset = datasets.FirstOrDefault(d => d.Name.Contains(info.Prefix));

            if (matchingDataset == null)
            {
                Console.Error.WriteLine($"Error: Dataset '{info.Prefix}' not found in S3.");
                Console.Error.WriteLine($"  Available datasets: {string.Join(", ", datasets.Select(d => d.Name))}");
                return;
            }

            // Now discover versions for the matched dataset
            var versions = await discoveryService.DiscoverVersionsAsync(matchingDataset.FullPrefix, ct);

            if (versions.Count == 0)
            {
                // Dataset folder exists but has no content (common for DTED variants on CDSE)
                Console.Error.WriteLine($"Error: Dataset '{matchingDataset.Name}' exists but contains no DEM files.");
                Console.Error.WriteLine($"  CDSE may not have this format available. Try a DGED variant instead:");
                Console.Error.WriteLine($"  --dataset GLO-30 (defaults to GLO-30-DGED)");
                Console.Error.WriteLine($"  --dataset GLO-90 (defaults to GLO-90-DGED)");
                return;
            }

            if (versions[0].Year != "Latest")
            {
                prefix = versions[0].FullPrefix;
                Console.WriteLine($"Using version: {versions[0].Name}");
            }
            else
            {
                // No versioned subdirectories found (typical for CDSE), use the dataset prefix directly
                prefix = matchingDataset.FullPrefix;
            }
        }
    }

    var outputOpt = parseResult.GetValueForOption(outputOption);
    var outputDir = outputOpt?.FullName ?? $"./CopDEM_{datasetName.Replace("-", "")}";
    var masks = ParseMasks(parseResult.GetValueForOption(masksOption));
    var parallelism = parseResult.GetValueForOption(parallelOption) ?? ConsoleHelper.GetDefaultParallelism();
    var bbox = BoundingBox.TryParse(parseResult.GetValueForOption(bboxOption));

    if (bbox?.WasNormalized == true)
    {
        Console.WriteLine($"Note: Bounding box was normalized. {bbox.NormalizationWarning}");
    }

    var options = new DownloadOptions
    {
        AccessKey = accessKey,
        SecretKey = secretKey,
        Endpoint = endpoint,
        Bucket = bucket,
        Prefix = prefix,
        OutputDirectory = outputDir,
        Parallelism = parallelism,
        MaxRetries = parseResult.GetValueForOption(retryOption),
        StateFile = parseResult.GetValueForOption(stateFileOption)!,
        Force = parseResult.GetValueForOption(forceOption),
        DryRun = parseResult.GetValueForOption(dryRunOption),
        Masks = masks,
        BoundingBox = bbox
    };

    Console.WriteLine($"Downloading from: {prefix}");
    Console.WriteLine($"Output: {outputDir}");
    Console.WriteLine($"Masks: {string.Join(", ", masks.GetIndividualMasks())}");

    var downloader = new DownloaderService(options);
    var progress = new Progress<DownloadProgress>(ConsoleHelper.ShowProgress);

    var result = await downloader.RunAsync(progress, ct);

    if (result.DryRun)
    {
        Console.WriteLine($"\n[DRY RUN] Would download {result.TotalFiles} files ({DownloaderService.FormatBytes(result.TotalBytes)})");
    }
    else
    {
        ConsoleHelper.ShowResult(result);
    }
}
