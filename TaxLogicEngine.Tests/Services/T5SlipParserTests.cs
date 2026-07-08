using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

public class T5SlipParserTests
{
    private static readonly T5SlipParser Parser = new();

    private static T5RawInput Input(int? year, params (string Code, string? Value)[] boxes) => new()
    {
        TaxYear = year,
        PayerName = "Big Bank Inc.",
        RecipientName = "Doe, Jane",
        Boxes = boxes.Select(b => new T5RawBox(b.Code, b.Value)).ToArray(),
    };

    [Fact]
    public void Parse_FullValidSlip_ReturnsSuccessWithTypedProperties() // AC1
    {
        var input = Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.RecipientIdentificationNumber, "123456789"),
            (T5BoxCodes.ReportCode, "O"),
            (T5BoxCodes.Transit, "12345"),
            (T5BoxCodes.RecipientAccount, "0001234"),
            (T5BoxCodes.InterestFromCanadianSources, "100.00"),
            (T5BoxCodes.EligibleDividendsActual, "200.00"),
            (T5BoxCodes.EligibleDividendsTaxable, "276.00"),
            (T5BoxCodes.EligibleDividendsTaxCredit, "41.45"));

        var result = Parser.Parse(input);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var slip = result.Value!;
        Assert.Equal(2024, slip.TaxYear);
        Assert.Equal("Big Bank Inc.", slip.PayerName);
        Assert.Equal(1, slip.RecipientType);
        Assert.Equal("***-**-789", slip.RecipientId!.Masked);
        Assert.Equal("O", slip.ReportCode);
        Assert.Equal("12345", slip.TransitNumber);
        Assert.Equal("0001234", slip.AccountNumber);
        Assert.Equal(100.00m, slip.InterestFromCanadianSources);
        Assert.Equal(200.00m, slip.EligibleDividendsActual);
        Assert.Equal(276.00m, slip.EligibleDividendsTaxable);
        Assert.Equal(41.45m, slip.EligibleDividendsTaxCredit);
        Assert.Equal("CAD", slip.CurrencyCode);
    }

    [Fact]
    public void Parse_RoundsAmountsHalfUpToCents() // AC2
    {
        var result = Parser.Parse(Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.InterestFromCanadianSources, "100.005")));

        Assert.Equal(100.01m, result.Value!.InterestFromCanadianSources);
    }

    [Fact]
    public void Parse_UnknownAndNamedOtherInfoBoxes_GoToOtherInformation() // AC4
    {
        var result = Parser.Parse(Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.EquityLinkedNotesInterest, "42.50"), // box 30, named other-info
            ("99", "7.00")));                                // arbitrary non-standard box

        var other = result.Value!.OtherInformation;
        Assert.Equal(2, other.Count);
        Assert.Contains(other, o => o.Code == "30" && o.Amount == 42.50m);
        Assert.Contains(other, o => o.Code == "99" && o.Amount == 7.00m);
        // Box 30 is NOT promoted to a typed property.
    }

    [Fact]
    public void Parse_ForeignCurrency_IsPreservedNotConverted() // AC7
    {
        var result = Parser.Parse(Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.ForeignCurrency, "USD"),
            (T5BoxCodes.ForeignIncome, "500.00"),
            (T5BoxCodes.ForeignTaxPaid, "75.00")));

        var slip = result.Value!;
        Assert.Equal("USD", slip.CurrencyCode);
        Assert.Equal(500.00m, slip.ForeignIncome);
        Assert.Equal(75.00m, slip.ForeignTaxPaid);
    }

    [Fact]
    public void Parse_AbsentAmountBoxes_DefaultToZero()
    {
        var result = Parser.Parse(Input(2024, (T5BoxCodes.RecipientType, "1")));

        Assert.Equal(0m, result.Value!.InterestFromCanadianSources);
        Assert.Empty(result.Value!.OtherInformation);
    }

    [Fact]
    public void Parse_IsDeterministic()
    {
        var input = Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.InterestFromCanadianSources, "10.00"));

        Assert.Equal(
            Parser.Parse(input).Value!.InterestFromCanadianSources,
            Parser.Parse(input).Value!.InterestFromCanadianSources);
    }

    [Fact]
    public void Parse_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Parser.Parse(null!));
    }

    // ----- T08: validation (fail-closed) -----

    [Fact]
    public void Parse_MissingTaxYear_FailsWithNoModel() // AC5
    {
        var result = Parser.Parse(Input(null, (T5BoxCodes.RecipientType, "1")));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, e => e.Field == "TaxYear");
    }

    [Fact]
    public void Parse_MissingPayerOrRecipientName_Fails()
    {
        var input = new T5RawInput
        {
            TaxYear = 2024,
            // PayerName and RecipientName intentionally absent
            Boxes = new[] { new T5RawBox(T5BoxCodes.RecipientType, "1") },
        };

        var result = Parser.Parse(input);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "PayerName");
        Assert.Contains(result.Errors, e => e.Field == "RecipientName");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("5")]
    public void Parse_RecipientTypeInRange_Succeeds(string type)
    {
        var result = Parser.Parse(Input(2024, (T5BoxCodes.RecipientType, type)));

        Assert.True(result.IsSuccess);
        Assert.Equal(int.Parse(type), result.Value!.RecipientType);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("9")] // AC6
    public void Parse_RecipientTypeOutOfRange_Fails(string type)
    {
        var result = Parser.Parse(Input(2024, (T5BoxCodes.RecipientType, type)));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 23");
    }

    [Fact]
    public void Parse_RecipientTypeMissing_Fails()
    {
        var result = Parser.Parse(Input(2024)); // no box 23

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 23");
    }

    [Fact]
    public void Parse_RecipientTypeNotNumeric_Fails()
    {
        var result = Parser.Parse(Input(2024, (T5BoxCodes.RecipientType, "individual")));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 23");
    }

    [Fact]
    public void Parse_NegativeAmount_Fails()
    {
        var result = Parser.Parse(Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.InterestFromCanadianSources, "-5.00")));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 13");
    }

    [Fact]
    public void Parse_NonNumericAmount_Fails()
    {
        var result = Parser.Parse(Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.InterestFromCanadianSources, "lots")));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 13");
    }

    [Fact]
    public void Parse_DuplicateBoxCode_Fails()
    {
        var result = Parser.Parse(Input(2024,
            (T5BoxCodes.RecipientType, "1"),
            (T5BoxCodes.InterestFromCanadianSources, "100.00"),
            (T5BoxCodes.InterestFromCanadianSources, "200.00")));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 13");
    }

    [Fact]
    public void Parse_CollectsMultipleErrorsAtOnce()
    {
        var input = new T5RawInput
        {
            // missing tax year + payer name; bad recipient type; negative amount
            RecipientName = "Doe, Jane",
            Boxes = new[]
            {
                new T5RawBox(T5BoxCodes.RecipientType, "9"),
                new T5RawBox(T5BoxCodes.InterestFromCanadianSources, "-1.00"),
            },
        };

        var result = Parser.Parse(input);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void Parse_ValidationErrorMessages_NeverContainFullSin()
    {
        var input = new T5RawInput
        {
            // invalid (missing tax year) but includes a SIN in box 22
            RecipientName = "Doe, Jane",
            PayerName = "Payer",
            Boxes = new[]
            {
                new T5RawBox(T5BoxCodes.RecipientIdentificationNumber, "123456789"),
                new T5RawBox(T5BoxCodes.RecipientType, "9"),
            },
        };

        var result = Parser.Parse(input);

        Assert.False(result.IsSuccess);
        Assert.All(result.Errors, e => Assert.DoesNotContain("123456789", e.Field + " " + e.Message));
    }
}
