using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class T5MappingResultTests
{
    [Fact]
    public void HoldsLinesAndUnmappedBoxCodes()
    {
        var lines = new[] { new TaxLineEntry("12100", 100.00m) };
        var unmapped = new[] { "10", "24" };

        var result = new T5MappingResult(lines, unmapped);

        Assert.Equal(lines, result.Lines);
        Assert.Equal(unmapped, result.UnmappedBoxCodes);
    }

    [Fact]
    public void Empty_HasNoLinesAndNothingUnmapped()
    {
        Assert.Empty(T5MappingResult.Empty.Lines);
        Assert.Empty(T5MappingResult.Empty.UnmappedBoxCodes);
    }
}
