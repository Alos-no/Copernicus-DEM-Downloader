using Amazon.S3;
using CopernicusDemDownloader.Services;

namespace CopernicusDemDownloader.Tests.Infrastructure;

/// <summary>
/// Base class for integration tests that require S3 access.
/// Tests will be skipped if credentials are not configured.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly string? AccessKey;
    protected readonly string? SecretKey;
    protected readonly string Endpoint;
    protected readonly string Bucket;
    protected readonly string BasePrefix;
    protected readonly bool HasCredentials;
    protected readonly string TestOutputDirectory;

    private readonly List<string> _tempDirectories = new();

    protected IntegrationTestBase()
    {
        AccessKey = TestConfiguration.AccessKey;
        SecretKey = TestConfiguration.SecretKey;
        Endpoint = TestConfiguration.Endpoint;
        Bucket = TestConfiguration.Bucket;
        BasePrefix = TestConfiguration.BasePrefix;
        HasCredentials = TestConfiguration.HasCredentials;

        // Create a unique temp directory for this test
        TestOutputDirectory = Path.Combine(Path.GetTempPath(), $"CopDEM_Test_{Guid.NewGuid():N}");
        _tempDirectories.Add(TestOutputDirectory);
    }

    protected AmazonS3Client CreateS3Client()
    {
        return DownloaderService.CreateClient(AccessKey!, SecretKey!, Endpoint);
    }

    protected DatasetDiscoveryService CreateDiscoveryService()
    {
        return new DatasetDiscoveryService(CreateS3Client(), Bucket, BasePrefix);
    }

    protected string CreateTempDirectory(string? suffix = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"CopDEM_Test_{Guid.NewGuid():N}{suffix ?? ""}");
        Directory.CreateDirectory(dir);
        _tempDirectories.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        // Clean up temp directories
        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        GC.SuppressFinalize(this);
    }
}
