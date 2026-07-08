using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// End-to-end acceptance suite for T5 slip parsing: one test per acceptance
/// criterion (AC1–AC7) driven through <see cref="T5SlipParser"/>, plus a
/// cross-cutting sweep asserting the recipient SIN (Box 22) never leaks.
/// </summary>
public class T5SlipAcceptanceTests
{
    private const string Sin = "123456789";
    private static readonly T5SlipParser Parser = new();

    private static T5RawInput ValidInput(params (string Code, string? Value)[] boxes) => new()
    {
        TaxYear = 2024,
        PayerName = "Big Bank Inc.",
        PayerAddress = "1 Bay St, Toronto ON",
        RecipientName = "Doe, Jane",
        RecipientAddress = "2 King St, Toronto ON",
        Boxes = boxes.Select(b => new T5RawBox(b.Code, b.Value)).ToArray(),
    };

    [Fact]
    public void AC1_FullValidSlip_ParsesToPopulatedModelWithNoErrors()
    {
        var result = Parser.Parse(ValidInput(
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.RecipientIdentificationNumber, Sin),
            (T5BoxCodes.ReportCode, "O"),
            (T5BoxCodes.Transit, "12345"),
            (T5BoxCodes.RecipientAccount, "0001234"),
            (T5BoxCodes.OtherThanEligibleDividendsActual, "10.00"),
            (T5BoxCodes.OtherThanEligibleDividendsTaxable, "11.50"),
            (T5BoxCodes.OtherThanEligibleDividendsTaxCredit, "1.27"),
            (T5BoxCodes.InterestFromCanadianSources, "13.00"),
            (T5BoxCodes.EligibleDividendsActual, "24.00"),
            (T5BoxCodes.EligibleDividendsTaxable, "33.12"),
            (T5BoxCodes.EligibleDividendsTaxCredit, "4.97")));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var slip = result.Value!;
        Assert.Equal(2024, slip.TaxYear);
        Assert.Equal(1, slip.RecipientType);
        Assert.Equal(13.00m, slip.InterestFromCanadianSources);
        Assert.Equal(24.00m, slip.EligibleDividendsActual);
        Assert.Equal("O", slip.ReportCode);
        Assert.Equal("CAD", slip.CurrencyCode);
    }

    [Fact]
    public void AC2_MonetaryValue_IsRoundedHalfUpToCents()
    {
        var result = Parser.Parse(ValidInput(
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.InterestFromCanadianSources, "100.005")));

        Assert.Equal(100.01m, result.Value!.InterestFromCanadianSources);
    }

    [Fact]
    public void AC3_Sin_IsMaskedOnOutput_AndNeverAppearsInFull()
    {
        var result = Parser.Parse(ValidInput(
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.RecipientIdentificationNumber, Sin)));

        var text = result.Value!.ToString();
        Assert.Contains("***-**-789", text);
        Assert.DoesNotContain(Sin, text);
    }

    [Fact]
    public void AC4_NonStandardBox_IsPreservedInOtherInformation()
    {
        var result = Parser.Parse(ValidInput(
            (T5BoxCodes.RecipientType, "1"),
            ("35", "12.34"))); // non-standard code

        Assert.Contains(result.Value!.OtherInformation, o => o.Code == "35" && o.Amount == 12.34m);
    }

    [Fact]
    public void AC5_MissingTaxYear_ProducesErrorAndNoValidSlip()
    {
        var input = new T5RawInput
        {
            PayerName = "Big Bank Inc.",
            RecipientName = "Doe, Jane",
            Boxes = new[] { new T5RawBox(T5BoxCodes.RecipientType, "1") },
        };

        var result = Parser.Parse(input);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, e => e.Field == "TaxYear");
    }

    [Fact]
    public void AC6_RecipientTypeOutOfRange_IsFlagged()
    {
        var result = Parser.Parse(ValidInput((T5BoxCodes.RecipientType, "9")));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 23");
    }

    [Fact]
    public void AC7_ForeignCurrency_IsPreservedAndNotConverted()
    {
        var result = Parser.Parse(ValidInput(
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.ForeignCurrency, "USD"),
            (T5BoxCodes.ForeignIncome, "500.00"),
            (T5BoxCodes.ForeignTaxPaid, "75.00")));

        var slip = result.Value!;
        Assert.Equal("USD", slip.CurrencyCode);
        Assert.Equal(500.00m, slip.ForeignIncome);
        Assert.Equal(75.00m, slip.ForeignTaxPaid);
    }

    // ----- Cross-cutting SIN-safety sweep -----

    [Fact]
    public void SinSweep_SuccessfulSlip_ToStringNeverLeaksSin()
    {
        var result = Parser.Parse(ValidInput(
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.RecipientIdentificationNumber, Sin)));

        var text = result.Value!.ToString();
        Assert.DoesNotContain(Sin, text);
        Assert.DoesNotContain("123456", text); // leading six digits must not appear
    }

    [Fact]
    public void SinSweep_InvalidSlipCarryingSin_NoErrorMessageLeaksSin()
    {
        // Invalid (bad recipient type) but a real SIN is present in Box 22.
        var input = new T5RawInput
        {
            TaxYear = 2024,
            PayerName = "Big Bank Inc.",
            RecipientName = "Doe, Jane",
            Boxes = new[]
            {
                new T5RawBox(T5BoxCodes.RecipientIdentificationNumber, Sin),
                new T5RawBox(T5BoxCodes.RecipientType, "9"),
                new T5RawBox(T5BoxCodes.InterestFromCanadianSources, "-1.00"),
            },
        };

        var result = Parser.Parse(input);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, e =>
        {
            Assert.DoesNotContain(Sin, e.Field);
            Assert.DoesNotContain(Sin, e.Message);
        });
    }
}
