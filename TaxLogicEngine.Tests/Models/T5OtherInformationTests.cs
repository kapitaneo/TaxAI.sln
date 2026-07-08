using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class T5OtherInformationTests
{
    [Fact]
    public void HoldsCodeAndAmount()
    {
        var info = new T5OtherInformation(T5BoxCodes.EquityLinkedNotesInterest, 42.50m);

        Assert.Equal("30", info.Code);
        Assert.Equal(42.50m, info.Amount);
    }

    [Fact]
    public void HasValueEquality()
    {
        var a = new T5OtherInformation("30", 42.50m);
        var b = new T5OtherInformation("30", 42.50m);

        Assert.Equal(a, b);
    }

    [Fact]
    public void DiffersOnCodeOrAmount()
    {
        var baseline = new T5OtherInformation("30", 42.50m);

        Assert.NotEqual(baseline, new T5OtherInformation("31", 42.50m));
        Assert.NotEqual(baseline, new T5OtherInformation("30", 10.00m));
    }
}
