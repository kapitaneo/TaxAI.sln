using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

public class T5LineMapperTests
{
    private static readonly T5LineMapper Mapper = new(new EmbeddedT5LineMapProvider());

    private static T5Slip Slip(Action<Amounts> configure)
    {
        var a = new Amounts();
        configure(a);
        return new T5Slip
        {
            TaxYear = 2025,
            PayerName = "Payer",
            RecipientName = "Doe, Jane",
            RecipientType = 1,
            InterestFromCanadianSources = a.Interest,
            ForeignIncome = a.ForeignIncome,
            OtherThanEligibleDividendsTaxable = a.OtherThanEligibleTaxable,
            OtherThanEligibleDividendsTaxCredit = a.OtherThanEligibleCredit,
            EligibleDividendsTaxCredit = a.EligibleCredit,
            OtherThanEligibleDividendsActual = a.OtherThanEligibleActual,
        };
    }

    private static TaxLineEntry Line(T5MappingResult r, string line) =>
        r.Lines.Single(e => e.LineNumber == line);

    [Fact]
    public void Map_SingleBoxToLine() // AC1
    {
        var result = Mapper.Map(new[] { Slip(a => a.Interest = 100.00m) });

        Assert.True(result.IsSuccess);
        var entry = Line(result.Value!, "12100");
        Assert.Equal(100.00m, entry.Amount);
        Assert.Equal(new[] { "13" }, entry.SourceBoxCodes);
    }

    [Fact]
    public void Map_MultipleBoxesOntoOneLine() // AC2
    {
        var result = Mapper.Map(new[] { Slip(a => { a.Interest = 100.00m; a.ForeignIncome = 50.00m; }) });

        var entry = Line(result.Value!, "12100");
        Assert.Equal(150.00m, entry.Amount);
        Assert.Contains("13", entry.SourceBoxCodes);
        Assert.Contains("15", entry.SourceBoxCodes);
    }

    [Fact]
    public void Map_AggregatesAcrossSlips() // AC3
    {
        var result = Mapper.Map(new[]
        {
            Slip(a => a.Interest = 100.00m),
            Slip(a => a.Interest = 100.00m),
        });

        Assert.Equal(200.00m, Line(result.Value!, "12100").Amount);
    }

    [Fact]
    public void Map_CreditBoxesOntoCreditLine_NotIncomeLine() // AC4
    {
        var result = Mapper.Map(new[]
        {
            Slip(a => { a.OtherThanEligibleCredit = 5.00m; a.EligibleCredit = 10.00m; }),
        });

        var entry = Line(result.Value!, "40425");
        Assert.Equal(15.00m, entry.Amount);
        Assert.Contains("12", entry.SourceBoxCodes);
        Assert.Contains("26", entry.SourceBoxCodes);
        Assert.DoesNotContain(result.Value!.Lines, e => e.LineNumber == "12100");
    }

    [Fact]
    public void Map_OneBoxToManyLines() // box 11 -> 12000 and 12010
    {
        var result = Mapper.Map(new[] { Slip(a => a.OtherThanEligibleTaxable = 80.00m) });

        Assert.Equal(80.00m, Line(result.Value!, "12000").Amount);
        Assert.Equal(80.00m, Line(result.Value!, "12010").Amount);
    }

    [Fact]
    public void Map_UnmappedBoxPreserved_NotDropped() // AC5
    {
        var result = Mapper.Map(new[] { Slip(a => a.OtherThanEligibleActual = 55.00m) });

        Assert.Contains("10", result.Value!.UnmappedBoxCodes);
        Assert.Empty(result.Value!.Lines); // box 10 has no mapping -> no line
    }

    [Fact]
    public void Map_SumsRoundedHalfUpToCents() // AC8
    {
        var result = Mapper.Map(new[] { Slip(a => { a.Interest = 100.00m; a.ForeignIncome = 0.005m; }) });

        Assert.Equal(100.01m, Line(result.Value!, "12100").Amount);
    }

    [Fact]
    public void Map_NullSlips_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Mapper.Map(null!));
    }

    private sealed class Amounts
    {
        public decimal Interest { get; set; }
        public decimal ForeignIncome { get; set; }
        public decimal OtherThanEligibleTaxable { get; set; }
        public decimal OtherThanEligibleCredit { get; set; }
        public decimal EligibleCredit { get; set; }
        public decimal OtherThanEligibleActual { get; set; }
    }
}
