using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// End-to-end acceptance suite for T5 box → CRA line mapping: one test per PRD
/// acceptance criterion (AC1–AC8), driven through <see cref="T5LineMapper"/> backed
/// by the real embedded 2025 config, plus provenance and SIN-safety sweeps.
/// </summary>
public class T5LineMappingAcceptanceTests
{
    private const string Sin = "123456789";
    private static readonly T5LineMapper Mapper = new(new EmbeddedT5LineMapProvider());

    private static T5Slip Slip(
        Action<Boxes> configure, int year = 2025, string currency = "CAD")
    {
        var b = new Boxes();
        configure(b);
        return new T5Slip
        {
            TaxYear = year,
            PayerName = "Big Bank Inc.",
            RecipientName = "Doe, Jane",
            RecipientType = 1,
            RecipientId = new RecipientIdentifier(Sin),
            CurrencyCode = currency,
            InterestFromCanadianSources = b.Interest,
            ForeignIncome = b.ForeignIncome,
            OtherThanEligibleDividendsTaxable = b.OtherThanEligibleTaxable,
            OtherThanEligibleDividendsTaxCredit = b.OtherThanEligibleCredit,
            EligibleDividendsTaxCredit = b.EligibleCredit,
            OtherThanEligibleDividendsActual = b.OtherThanEligibleActual,
        };
    }

    private static TaxLineEntry Line(T5MappingResult r, string line) =>
        r.Lines.Single(e => e.LineNumber == line);

    [Fact]
    public void AC1_SingleBoxToLine()
    {
        var r = Mapper.Map(new[] { Slip(b => b.Interest = 100.00m) });

        Assert.True(r.IsSuccess);
        Assert.Equal(100.00m, Line(r.Value!, "12100").Amount);
    }

    [Fact]
    public void AC2_AggregationAcrossBoxesOntoOneLine()
    {
        var r = Mapper.Map(new[] { Slip(b => { b.Interest = 100.00m; b.ForeignIncome = 50.00m; }) });

        Assert.Equal(150.00m, Line(r.Value!, "12100").Amount);
    }

    [Fact]
    public void AC3_AggregationAcrossMultipleSlips()
    {
        var r = Mapper.Map(new[]
        {
            Slip(b => b.Interest = 100.00m),
            Slip(b => b.Interest = 100.00m),
        });

        Assert.Equal(200.00m, Line(r.Value!, "12100").Amount);
    }

    [Fact]
    public void AC4_NonIncomeDestination()
    {
        var r = Mapper.Map(new[] { Slip(b => { b.OtherThanEligibleCredit = 5.00m; b.EligibleCredit = 10.00m; }) });

        Assert.Equal(15.00m, Line(r.Value!, "40425").Amount);
    }

    [Fact]
    public void AC5_UnmappedBoxPreservedNotDropped()
    {
        var r = Mapper.Map(new[] { Slip(b => b.OtherThanEligibleActual = 55.00m) });

        Assert.Contains("10", r.Value!.UnmappedBoxCodes);
    }

    [Fact]
    public void AC6_ForeignCurrencyRejected()
    {
        var r = Mapper.Map(new[] { Slip(b => b.Interest = 100.00m, currency: "USD") });

        Assert.False(r.IsSuccess);
        Assert.Null(r.Value);
        Assert.Contains(r.Errors, e => e.Field == "CurrencyCode");
    }

    [Fact]
    public void AC7_PerTaxYearConfig()
    {
        var r = Mapper.Map(new[] { Slip(b => b.Interest = 100.00m, year: 2025) });

        Assert.True(r.IsSuccess);
        Assert.Equal(100.00m, Line(r.Value!, "12100").Amount);
    }

    [Fact]
    public void AC8_Rounding()
    {
        var r = Mapper.Map(new[] { Slip(b => { b.Interest = 100.00m; b.ForeignIncome = 0.005m; }) });

        Assert.Equal(100.01m, Line(r.Value!, "12100").Amount);
    }

    // ----- Cross-cutting -----

    [Fact]
    public void Provenance_EntryRecordsContributingBoxes()
    {
        var r = Mapper.Map(new[] { Slip(b => { b.Interest = 100.00m; b.ForeignIncome = 50.00m; }) });

        var entry = Line(r.Value!, "12100");
        Assert.Contains("13", entry.SourceBoxCodes);
        Assert.Contains("15", entry.SourceBoxCodes);
    }

    [Fact]
    public void SinSweep_ResultCarriesNoPii()
    {
        var r = Mapper.Map(new[] { Slip(b => { b.Interest = 100.00m; b.OtherThanEligibleActual = 5.00m; }) });

        Assert.All(r.Value!.Lines, e =>
        {
            Assert.DoesNotContain(Sin, e.LineNumber);
            Assert.All(e.SourceBoxCodes, c => Assert.DoesNotContain(Sin, c));
        });
        Assert.All(r.Value!.UnmappedBoxCodes, c => Assert.DoesNotContain(Sin, c));
    }

    private sealed class Boxes
    {
        public decimal Interest { get; set; }
        public decimal ForeignIncome { get; set; }
        public decimal OtherThanEligibleTaxable { get; set; }
        public decimal OtherThanEligibleCredit { get; set; }
        public decimal EligibleCredit { get; set; }
        public decimal OtherThanEligibleActual { get; set; }
    }
}
