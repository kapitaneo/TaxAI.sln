using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Unit tests for <see cref="T4SlipBoxAmounts"/> (spec 004, T02): the flattener
/// yields non-zero amount boxes + "Other information" verbatim, skips zero/absent
/// boxes, includes the deferred amount boxes when populated (so the mapper can
/// surface them unmapped), and never emits a non-amount box (the SIN never enters
/// the pipeline).
/// </summary>
public class T4SlipBoxAmountsTests
{
    private static T4Slip Slip(Action<T4SlipBuilder>? configure = null)
    {
        var b = new T4SlipBuilder();
        configure?.Invoke(b);
        return b.Build();
    }

    private sealed class T4SlipBuilder
    {
        public decimal EmploymentIncome, CppContributions, SecondCpp, EiPremiums, PpipPremiums, UnionDues;
        public IReadOnlyList<T4OtherInformation> Other = Array.Empty<T4OtherInformation>();

        public T4Slip Build() => new()
        {
            Year = 2025,
            EmployerName = "ACME CORP",
            EmployeeName = "DOE, JANE",
            DentalBenefitCode = "1",
            EmployerAccountNumber = "998877665RP0001",
            Sin = new RecipientIdentifier("123456789"),
            ProvinceOfEmployment = "ON",
            EmploymentIncome = EmploymentIncome,
            CppContributions = CppContributions,
            SecondCppContributions = SecondCpp,
            EiPremiums = EiPremiums,
            PpipPremiums = PpipPremiums,
            UnionDues = UnionDues,
            OtherInformation = Other,
        };
    }

    private static Dictionary<string, decimal> Pairs(T4Slip slip) =>
        T4SlipBoxAmounts.Enumerate(slip).ToDictionary(p => p.BoxCode, p => p.Amount, StringComparer.Ordinal);

    [Fact]
    public void Enumerate_YieldsNonZeroAmountBoxes_WithCorrectCodes()
    {
        var slip = Slip(b =>
        {
            b.EmploymentIncome = 50000.00m;
            b.UnionDues = 250.00m;
        });

        var pairs = Pairs(slip);

        Assert.Equal(50000.00m, pairs["14"]);
        Assert.Equal(250.00m, pairs["44"]);
    }

    [Fact]
    public void Enumerate_SkipsZeroAndAbsentAmountBoxes()
    {
        var slip = Slip(b => b.EmploymentIncome = 100.00m); // everything else defaults to 0m

        var pairs = Pairs(slip);

        Assert.True(pairs.ContainsKey("14"));
        Assert.False(pairs.ContainsKey("18")); // EI premiums = 0m → skipped
        Assert.False(pairs.ContainsKey("44")); // union dues = 0m → skipped
        Assert.Single(pairs);
    }

    [Fact]
    public void Enumerate_IncludesDeferredAmountBoxes_WhenPopulated()
    {
        // Boxes 16 (CPP) and 55 (PPIP) are not mapped by the config, but must still
        // be enumerated so the mapper can surface them as unmapped, not drop them.
        var slip = Slip(b =>
        {
            b.CppContributions = 3500.00m;
            b.PpipPremiums = 400.00m;
        });

        var pairs = Pairs(slip);

        Assert.Equal(3500.00m, pairs["16"]);
        Assert.Equal(400.00m, pairs["55"]);
    }

    [Fact]
    public void Enumerate_PassesThroughOtherInformationVerbatim()
    {
        var slip = Slip(b => b.Other = new[]
        {
            new T4OtherInformation("40", 250.00m),
            new T4OtherInformation("99", 12.34m),
        });

        var pairs = Pairs(slip);

        Assert.Equal(250.00m, pairs["40"]);
        Assert.Equal(12.34m, pairs["99"]);
    }

    [Fact]
    public void Enumerate_NeverYieldsNonAmountBoxes_IncludingTheSin()
    {
        var slip = Slip(b => b.EmploymentIncome = 50000.00m);

        var codes = T4SlipBoxAmounts.Enumerate(slip).Select(p => p.BoxCode).ToList();

        // The SIN (12), account number (54), province (10), dental (45) etc. are not amounts.
        Assert.DoesNotContain("12", codes);
        Assert.DoesNotContain("54", codes);
        Assert.DoesNotContain("10", codes);
        Assert.DoesNotContain("45", codes);
    }

    [Fact]
    public void Enumerate_EmptyOfAmounts_YieldsNothing()
    {
        var slip = Slip(); // all amount boxes 0m, no Other information

        Assert.Empty(T4SlipBoxAmounts.Enumerate(slip));
    }

    [Fact]
    public void Enumerate_NullSlip_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => T4SlipBoxAmounts.Enumerate(null!).ToList());
    }
}
