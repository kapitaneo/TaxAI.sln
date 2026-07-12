using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Unit tests for <see cref="T4LineMapper"/> (spec 004, T05), using a fake
/// <see cref="IT4LineMapProvider"/> so the mapper is exercised independently of the
/// embedded config: happy path, aggregation, unmapped preservation, deferred-box
/// surfacing, rounding, empty/mixed-year fail-closed, and determinism — PRD ACs 1,
/// 3, 4, 10, 11, 12, 13, 14, 15, 16, 19, 23.
/// </summary>
public class T4LineMapperTests
{
    // ---- test doubles ----------------------------------------------------

    private sealed class FakeProvider : IT4LineMapProvider
    {
        private readonly ParseResult<T4LineMap> _result;
        private FakeProvider(ParseResult<T4LineMap> result) => _result = result;

        public ParseResult<T4LineMap> TryGetForYear(int taxYear) => _result;

        public static FakeProvider WithMap(params (string Box, string[] Lines)[] mappings)
        {
            var config = new T4LineMappingConfig(2025,
                mappings.Select(m => new T4BoxLineMapping(m.Box, m.Lines)).ToList());
            var map = T4LineMap.Create(config);
            Assert.True(map.IsSuccess);
            return new FakeProvider(map);
        }

        public static FakeProvider Failing() =>
            new(ParseResult<T4LineMap>.Failure(new ValidationError("TaxYear", "No config for that year.")));
    }

    private static T4Slip Slip(int year = 2025, Action<T4SlipBuilder>? configure = null)
    {
        var b = new T4SlipBuilder();
        configure?.Invoke(b);
        return b.Build(year);
    }

    private sealed class T4SlipBuilder
    {
        public decimal EmploymentIncome, CppContributions, PpipPremiums;
        public IReadOnlyList<T4OtherInformation> Other = Array.Empty<T4OtherInformation>();

        public T4Slip Build(int year) => new()
        {
            Year = year,
            EmployerName = "ACME CORP",
            EmployeeName = "DOE, JANE",
            DentalBenefitCode = "1",
            Sin = new RecipientIdentifier("123456789"),
            EmploymentIncome = EmploymentIncome,
            CppContributions = CppContributions,
            PpipPremiums = PpipPremiums,
            OtherInformation = Other,
        };
    }

    private static TaxLineEntry Line(T4MappingResult r, string line) =>
        r.Lines.Single(e => e.LineNumber == line);

    // ---- AC1: single box to a line ---------------------------------------

    [Fact]
    public void Map_SingleBoxToLine_ProducesEntryWithProvenance()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[] { Slip(configure: b => b.EmploymentIncome = 50000.00m) });

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(result.Value!.Lines);
        Assert.Equal("10100", entry.LineNumber);
        Assert.Equal(50000.00m, entry.Amount);
        Assert.Contains("14", entry.SourceBoxCodes);
    }

    // ---- AC3: empty / null fail closed -----------------------------------

    [Fact]
    public void Map_EmptySlipSet_FailsClosed()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(Array.Empty<T4Slip>());

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Slips");
    }

    [Fact]
    public void Map_NullSlipSet_FailsClosed()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(null!);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Slips");
    }

    // ---- AC4: mixed tax years --------------------------------------------

    [Fact]
    public void Map_MixedTaxYears_FailsClosed()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[]
        {
            Slip(2025, b => b.EmploymentIncome = 100m),
            Slip(2024, b => b.EmploymentIncome = 100m),
        });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Year");
    }

    [Fact]
    public void Map_FailingProvider_PropagatesError()
    {
        var mapper = new T4LineMapper(FakeProvider.Failing());

        var result = mapper.Map(new[] { Slip(configure: b => b.EmploymentIncome = 100m) });

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "TaxYear");
    }

    // ---- AC10 / AC11: aggregation ----------------------------------------

    [Fact]
    public void Map_SameLineAcrossSlips_SumsToOneEntry()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[]
        {
            Slip(configure: b => b.EmploymentIncome = 30000.00m),
            Slip(configure: b => b.EmploymentIncome = 30000.00m),
        });

        Assert.Equal(60000.00m, Line(result.Value!, "10100").Amount);
    }

    [Fact]
    public void Map_TwoBoxesOntoOneLine_SumAndRecordBothSources()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(
            ("39", new[] { "24900" }),
            ("41", new[] { "24900" })));

        var result = mapper.Map(new[]
        {
            Slip(configure: b => b.Other = new[]
            {
                new T4OtherInformation("39", 100.00m),
                new T4OtherInformation("41", 50.00m),
            }),
        });

        var entry = Line(result.Value!, "24900");
        Assert.Equal(150.00m, entry.Amount);
        Assert.Contains("39", entry.SourceBoxCodes);
        Assert.Contains("41", entry.SourceBoxCodes);
    }

    // ---- AC12 / AC13 / AC14: unmapped preservation -----------------------

    [Fact]
    public void Map_UnmappedOtherInformationCode_SurfacesInUnmapped()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[]
        {
            Slip(configure: b => b.Other = new[] { new T4OtherInformation("40", 250.00m) }),
        });

        Assert.Contains("40", result.Value!.UnmappedBoxCodes);
    }

    [Fact]
    public void Map_DeferredCalculationBox_SurfacesUnmapped_NotSummed()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[]
        {
            Slip(configure: b =>
            {
                b.EmploymentIncome = 50000.00m;
                b.CppContributions = 3500.00m; // Box 16, not in config
            }),
        });

        Assert.Contains("16", result.Value!.UnmappedBoxCodes);
        Assert.DoesNotContain(result.Value!.Lines, e => e.SourceBoxCodes.Contains("16"));
    }

    [Fact]
    public void Map_ResidenceDependentBox55_SurfacesUnmapped()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[] { Slip(configure: b => b.PpipPremiums = 400.00m) });

        Assert.Contains("55", result.Value!.UnmappedBoxCodes);
        Assert.DoesNotContain(result.Value!.Lines, e => e.LineNumber is "31205" or "31200");
    }

    // ---- AC15 / AC16: rounding -------------------------------------------

    [Fact]
    public void Map_LineTotalHalfCent_RoundsUpAwayFromZero()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[] { Slip(configure: b => b.EmploymentIncome = 100.005m) });

        Assert.Equal(100.01m, Line(result.Value!, "10100").Amount);
    }

    [Theory]
    [InlineData("100.005", "100.01")]
    [InlineData("-100.005", "-100.01")]
    public void MoneyRounding_ToCents_BothSigns_AtTheAggregationChokepoint(string input, string expected)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var want = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(want, MoneyRounding.ToCents(value));
    }

    // ---- AC19: empty-of-amounts slip -------------------------------------

    [Fact]
    public void Map_SlipWithNoAmounts_SucceedsWithNothing()
    {
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var result = mapper.Map(new[] { Slip() });

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Lines);
        Assert.Empty(result.Value!.UnmappedBoxCodes);
    }

    // ---- AC23: determinism -----------------------------------------------

    [Fact]
    public void Map_IdenticalInput_IsDeterministic()
    {
        T4Slip[] Build() => new[]
        {
            Slip(configure: b =>
            {
                b.EmploymentIncome = 50000.00m;
                b.Other = new[] { new T4OtherInformation("40", 10.00m) };
            }),
        };
        var mapper = new T4LineMapper(FakeProvider.WithMap(("14", new[] { "10100" })));

        var a = mapper.Map(Build()).Value!;
        var b = mapper.Map(Build()).Value!;

        Assert.Equal(
            a.Lines.Select(e => (e.LineNumber, e.Amount)),
            b.Lines.Select(e => (e.LineNumber, e.Amount)));
        Assert.Equal(a.UnmappedBoxCodes, b.UnmappedBoxCodes);
    }
}
