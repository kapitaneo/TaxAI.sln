using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

public class T5SlipBoxAmountsTests
{
    private static T5Slip Slip(Action<SlipBuilder>? configure = null)
    {
        var b = new SlipBuilder();
        configure?.Invoke(b);
        return b.Build();
    }

    [Fact]
    public void Enumerate_YieldsOnlyPopulatedTypedBoxes()
    {
        var slip = Slip(b =>
        {
            b.Interest = 100.00m;
            b.EligibleActual = 200.00m;
        });

        var pairs = T5SlipBoxAmounts.Enumerate(slip).ToList();

        Assert.Contains(("13", 100.00m), pairs);
        Assert.Contains(("24", 200.00m), pairs);
        Assert.Equal(2, pairs.Count); // the other 11 typed boxes are zero -> skipped
    }

    [Fact]
    public void Enumerate_SkipsZeroBoxes_EmptySlipYieldsNothing()
    {
        Assert.Empty(T5SlipBoxAmounts.Enumerate(Slip()));
    }

    [Fact]
    public void Enumerate_IncludesOtherInformation_IncludingBox30()
    {
        var slip = Slip(b => b.Other = new[]
        {
            new T5OtherInformation(T5BoxCodes.EquityLinkedNotesInterest, 42.50m),
            new T5OtherInformation("99", 7.00m),
        });

        var pairs = T5SlipBoxAmounts.Enumerate(slip).ToList();

        Assert.Contains(("30", 42.50m), pairs);
        Assert.Contains(("99", 7.00m), pairs);
    }

    [Fact]
    public void Enumerate_IncludesActualDividendBoxes_WhenNonZero()
    {
        // Actual boxes 10/24 are enumerated (they land in unmapped later, not dropped).
        var slip = Slip(b => b.OtherThanEligibleActual = 55.00m);

        Assert.Contains(("10", 55.00m), T5SlipBoxAmounts.Enumerate(slip));
    }

    [Fact]
    public void Enumerate_NullSlip_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => T5SlipBoxAmounts.Enumerate(null!).ToList());
    }

    private sealed class SlipBuilder
    {
        public decimal Interest { get; set; }
        public decimal EligibleActual { get; set; }
        public decimal OtherThanEligibleActual { get; set; }
        public IReadOnlyList<T5OtherInformation> Other { get; set; } = Array.Empty<T5OtherInformation>();

        public T5Slip Build() => new()
        {
            TaxYear = 2025,
            PayerName = "Payer",
            RecipientName = "Doe, Jane",
            RecipientType = 1,
            InterestFromCanadianSources = Interest,
            EligibleDividendsActual = EligibleActual,
            OtherThanEligibleDividendsActual = OtherThanEligibleActual,
            OtherInformation = Other,
        };
    }
}
