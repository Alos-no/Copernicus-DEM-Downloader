using System.Text.Json;
using CopernicusDemDownloader.Models;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

/// <summary>
/// Tests for download state persistence and resume functionality.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Unit)]
public class DownloadStateTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    [Fact]
    public void DownloadState_Serializes_Correctly()
    {
        var state = new DownloadState(new Dictionary<string, FileState>
        {
            ["file1.tif"] = new FileState(1024, "etag1", new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc)),
            ["file2.tif"] = new FileState(2048, "etag2", new DateTime(2024, 1, 16, 13, 30, 0, DateTimeKind.Utc))
        });

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<DownloadState>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Files.Count);
        Assert.Equal(1024, deserialized.Files["file1.tif"].Size);
        Assert.Equal("etag1", deserialized.Files["file1.tif"].ETag);
        Assert.Equal(2048, deserialized.Files["file2.tif"].Size);
    }

    [Fact]
    public void DownloadState_EmptyFiles_SerializesCorrectly()
    {
        var state = new DownloadState(new Dictionary<string, FileState>());

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<DownloadState>(json);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Files);
    }

    [Fact]
    public void FileState_RecordEquality_Works()
    {
        var dt = DateTime.UtcNow;
        var state1 = new FileState(1024, "etag1", dt);
        var state2 = new FileState(1024, "etag1", dt);
        var state3 = new FileState(2048, "etag1", dt);

        Assert.Equal(state1, state2);
        Assert.NotEqual(state1, state3);
    }

    [Fact]
    public void DownloadState_CanBeWrittenToFile()
    {
        var tempFile = CreateTempFile();
        var state = new DownloadState(new Dictionary<string, FileState>
        {
            ["test.tif"] = new FileState(512, "etag", DateTime.UtcNow)
        });

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tempFile, json);

        Assert.True(File.Exists(tempFile));
        var content = File.ReadAllText(tempFile);
        Assert.Contains("test.tif", content);
        Assert.Contains("512", content);
    }

    [Fact]
    public void DownloadState_CanBeLoadedFromFile()
    {
        var tempFile = CreateTempFile();
        var json = """
        {
            "Files": {
                "path/to/file.tif": {
                    "Size": 4096,
                    "ETag": "abc123",
                    "Downloaded": "2024-06-15T10:30:00Z"
                }
            }
        }
        """;
        File.WriteAllText(tempFile, json);

        var content = File.ReadAllText(tempFile);
        var state = JsonSerializer.Deserialize<DownloadState>(content);

        Assert.NotNull(state);
        Assert.Single(state.Files);
        Assert.Equal(4096, state.Files["path/to/file.tif"].Size);
        Assert.Equal("abc123", state.Files["path/to/file.tif"].ETag);
    }

    [Fact]
    public void DownloadState_CorruptedJson_ThrowsException()
    {
        var tempFile = CreateTempFile();
        File.WriteAllText(tempFile, "{ this is not valid json }");

        Assert.Throws<JsonException>(() =>
        {
            var content = File.ReadAllText(tempFile);
            JsonSerializer.Deserialize<DownloadState>(content);
        });
    }

    [Fact]
    public void DownloadState_MissingFile_FileNotFound()
    {
        var nonExistentFile = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json");

        Assert.False(File.Exists(nonExistentFile));
    }

    [Fact]
    public void DownloadState_LargeNumberOfFiles_HandlesCorrectly()
    {
        var files = new Dictionary<string, FileState>();
        for (int i = 0; i < 10000; i++)
        {
            files[$"file_{i:D5}.tif"] = new FileState(
                1024 * i,
                $"etag_{i}",
                DateTime.UtcNow.AddMinutes(-i));
        }
        var state = new DownloadState(files);

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<DownloadState>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(10000, deserialized.Files.Count);
        Assert.Equal(1024 * 5000, deserialized.Files["file_05000.tif"].Size);
    }

    [Fact]
    public void FileState_SpecialCharactersInPath_HandledCorrectly()
    {
        var state = new DownloadState(new Dictionary<string, FileState>
        {
            ["path/with spaces/file (1).tif"] = new FileState(100, "etag", DateTime.UtcNow),
            ["path/with/unicode/файл.tif"] = new FileState(200, "etag2", DateTime.UtcNow)
        });

        var json = JsonSerializer.Serialize(state);
        var deserialized = JsonSerializer.Deserialize<DownloadState>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Files.Count);
        Assert.True(deserialized.Files.ContainsKey("path/with spaces/file (1).tif"));
    }

    private string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_state_{Guid.NewGuid()}.json");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
        GC.SuppressFinalize(this);
    }
}
