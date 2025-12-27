using Microsoft.Extensions.Configuration;

namespace CopernicusDemDownloader.Tests.Infrastructure;

/// <summary>
/// Provides access to test configuration including User Secrets.
/// </summary>
public static class TestConfiguration
{
    private static readonly Lazy<IConfiguration> _configuration = new(BuildConfiguration);

    public static IConfiguration Configuration => _configuration.Value;

    public static string? AccessKey => Configuration["CDSE:AccessKey"]
                                       ?? Configuration["CDSE_ACCESS_KEY"]
                                       ?? Environment.GetEnvironmentVariable("CDSE_ACCESS_KEY");

    public static string? SecretKey => Configuration["CDSE:SecretKey"]
                                       ?? Configuration["CDSE_SECRET_KEY"]
                                       ?? Environment.GetEnvironmentVariable("CDSE_SECRET_KEY");

    public static bool HasCredentials => !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey);

    public static string Endpoint => Configuration["CDSE:Endpoint"]
                                     ?? "https://eodata.dataspace.copernicus.eu";

    public static string Bucket => Configuration["CDSE:Bucket"] ?? "eodata";

    public static string BasePrefix => Configuration["CDSE:BasePrefix"] ?? "auxdata/CopDEM/";

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddUserSecrets(typeof(TestConfiguration).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}

/// <summary>
/// Trait to mark tests that require real S3 credentials.
/// </summary>
public static class TestTraits
{
    public const string Category = "Category";
    public const string Integration = "Integration";
    public const string Unit = "Unit";
    public const string EndToEnd = "EndToEnd";
}
