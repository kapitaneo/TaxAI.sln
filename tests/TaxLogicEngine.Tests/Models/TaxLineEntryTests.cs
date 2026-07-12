using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class TaxLineEntryTests
{
    [Fact]
    public void HoldsLineNumberAmountAndSourceBoxes()
    {
        var entry = new TaxLineEntry("12100", 150.00m, new[] { "13", "15" });

        Assert.Equal("12100", entry.LineNumber);
        Assert.Equal(150.00m, entry.Amount);
        Assert.Equal(new[] { "13", "15" }, entry.SourceBoxCodes);
    }

    [Fact]
    public void ConvenienceCtor_DefaultsToNoProvenance()
    {
        var entry = new TaxLineEntry("12100", 100.00m);

        Assert.Empty(entry.SourceBoxCodes);
    }

    [Fact]
    public void HasValueEqualityOnLineAndAmount()
    {
        var a = new TaxLineEntry("12100", 100.00m);
        var b = new TaxLineEntry("12100", 100.00m);

        Assert.Equal(a, b);
    }

    [Fact]
    public void DiffersOnLineOrAmount()
    {
        var baseline = new TaxLineEntry("12100", 100.00m);

        Assert.NotEqual(baseline, new TaxLineEntry("12000", 100.00m));
        Assert.NotEqual(baseline, new TaxLineEntry("12100", 200.00m));
    }
}
