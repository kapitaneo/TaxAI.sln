using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class T5SlipTests
{
    private static T5Slip MinimalSlip() => new()
    {
        TaxYear = 2024,
        PayerName = "Big Bank Inc.",
        RecipientName = "Doe, Jane",
        RecipientType = 1,
    };

    [Fact]
    public void RequiredAndDefaultFields_ArePopulated()
    {
        var slip = MinimalSlip();

        Assert.Equal(2024, slip.TaxYear);
        Assert.Equal("Big Bank Inc.", slip.PayerName);
        Assert.Equal("Doe, Jane", slip.RecipientName);
        Assert.Equal(1, slip.RecipientType);
        Assert.Equal("CAD", slip.CurrencyCode);            // default
        Assert.Empty(slip.OtherInformation);               // default
        Assert.Null(slip.RecipientId);                     // optional
        Assert.Equal(0m, slip.InterestFromCanadianSources); // absent amount box
    }

    [Fact]
    public void AllBoxesCanBeSet()
    {
        var slip = new T5Slip
        {
            TaxYear = 2024,
            PayerName = "Payer",
            PayerAddress = "1 Bay St",
            RecipientName = "Doe, Jane",
            RecipientAddress = "2 King St",
            RecipientType = 2,
            RecipientId = new RecipientIdentifier("123456789"),
            ReportCode = "O",
            TransitNumber = "12345",
            AccountNumber = "0001234",
            CurrencyCode = "USD",
            OtherThanEligibleDividendsActual = 10.10m,
            OtherThanEligibleDividendsTaxable = 11.11m,
            OtherThanEligibleDividendsTaxCredit = 12.12m,
            InterestFromCanadianSources = 13.13m,
            OtherIncomeFromCanadianSources = 14.14m,
            ForeignIncome = 15.15m,
            ForeignTaxPaid = 16.16m,
            RoyaltiesFromCanadianSources = 17.17m,
            CapitalGainsDividends = 18.18m,
            AccruedIncomeAnnuities = 19.19m,
            EligibleDividendsActual = 24.24m,
            EligibleDividendsTaxable = 25.25m,
            EligibleDividendsTaxCredit = 26.26m,
            OtherInformation = new[] { new T5OtherInformation(T5BoxCodes.EquityLinkedNotesInterest, 30.30m) },
        };

        Assert.Equal("USD", slip.CurrencyCode);
        Assert.Equal(13.13m, slip.InterestFromCanadianSources);
        Assert.Equal(26.26m, slip.EligibleDividendsTaxCredit);
        Assert.Equal("30", Assert.Single(slip.OtherInformation).Code);
    }

    [Fact]
    public void ToString_MasksRecipientId_AndNeverLeaksFullSin()
    {
        var slip = new T5Slip
        {
            TaxYear = 2024,
            PayerName = "Payer",
            RecipientName = "Doe, Jane",
            RecipientType = 1,
            RecipientId = new RecipientIdentifier("123456789"),
        };

        var text = slip.ToString();

        Assert.Contains("***-**-789", text);
        Assert.DoesNotContain("123456789", text);
        Assert.DoesNotContain("123456", text);
    }

    [Fact]
    public void ToString_WithoutRecipientId_ShowsNotAvailable()
    {
        Assert.Contains("recipient=n/a", MinimalSlip().ToString());
    }
}
