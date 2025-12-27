using CopernicusDemDownloader.Interactive;
using CopernicusDemDownloader.Tests.Infrastructure;
using Xunit;

namespace CopernicusDemDownloader.Tests.Unit;

/// <summary>
/// Tests for ConsoleHelper static methods that can be tested without console interaction.
/// </summary>
[Trait(TestTraits.Category, TestTraits.Unit)]
public class ConsoleHelperTests
{
    [Fact]
    public void GetDefaultParallelism_ReturnsValueBetween4And16()
    {
        var result = ConsoleHelper.GetDefaultParallelism();

        Assert.InRange(result, 4, 16);
    }

    [Fact]
    public void GetDefaultParallelism_IsBasedOnProcessorCount()
    {
        var cores = Environment.ProcessorCount;
        var expected = Math.Clamp(cores * 2, 4, 16);
        var result = ConsoleHelper.GetDefaultParallelism();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDefaultParallelism_Consistent_AcrossMultipleCalls()
    {
        var result1 = ConsoleHelper.GetDefaultParallelism();
        var result2 = ConsoleHelper.GetDefaultParallelism();
        var result3 = ConsoleHelper.GetDefaultParallelism();

        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }
}
