using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Rounding, boundary, passthrough, and determinism acceptance suite for spec 003
/// (T05). Covers PRD acceptance criteria 16 (positive half-cent rounds up on a
/// real box), 17 (both signs asserted at the reused MoneyRounding helper), 18
/// (zero companion accepted and stored as 0m), 26/27 (known and unknown/future
/// Other-information codes pass through verbatim), and 28 (determinism).
/// </summary>
public class T4SlipParserBoundaryTests
{
    private readonly T4SlipParser _parser = new();

    private static T4RawInput ValidInput(params T4RawBox[] extra)
    {
        var boxes = new List<T4RawBox>
        {
            new(T4BoxCodes.SocialInsuranceNumber, "123456789"),
            new(T4BoxCodes.EmployerAccountNumber, "998877665RP0001"),
            new(T4BoxCodes.DentalBenefits, "1"),
        };
        boxes.AddRange(extra);

        return new T4RawInput
        {
            Year = 2025,
            EmployerName = "ACME CORP",
            EmployerAddress = "1 WAY ST, OTTAWA ON",
            EmployeeName = "DOE, JANE",
            EmployeeAddress = "2 HOME AVE, OTTAWA ON",
            Boxes = boxes,
        };
    }

    // ---- AC16: positive half-cent rounds up on a real box ----------------

    [Fact]
    public void Parse_PositiveHalfCent_RoundsUpAwayFromZero()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.EmploymentIncome, "100.005")));

        Assert.True(result.IsSuccess);
        Assert.Equal(100.01m, result.Value!.EmploymentIncome);
    }

    // ---- AC17: both signs asserted at the reused helper ------------------

    [Theory]
    [InlineData("100.005", "100.01")]
    [InlineData("-100.005", "-100.01")]
    public void MoneyRounding_ToCents_IsHalfUpAwayFromZero_BothSigns(string input, string expected)
    {
        var value = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        var want = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        // T4 rejects negative box inputs (A2), so the negative-midpoint behaviour is
        // asserted directly on the shared helper (per spec 001), not via a box.
        Assert.Equal(want, MoneyRounding.ToCents(value));
    }

    // ---- AC18: zero companion accepted and stored as 0m ------------------

    [Fact]
    public void Parse_ZeroCompanionAmount_StoredAsZero_NoError()
    {
        // Box 18 present requires Box 24; Box 24 = "0" is valid.
        var result = _parser.Parse(ValidInput(
            new T4RawBox(T4BoxCodes.EiPremiums, "900.00"),
            new T4RawBox(T4BoxCodes.EiInsurableEarnings, "0")));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal(0m, result.Value!.EiInsurableEarnings);
    }

    // ---- AC26/27: Other-information passthrough --------------------------

    [Fact]
    public void Parse_KnownOtherInformationCode_PassesThroughVerbatim()
    {
        // Code 40 = "Other taxable allowances and benefits".
        var result = _parser.Parse(ValidInput(new T4RawBox("40", "250.00")));

        Assert.True(result.IsSuccess);
        var pair = Assert.Single(result.Value!.OtherInformation);
        Assert.Equal("40", pair.Code);
        Assert.Equal(250.00m, pair.Amount);
    }

    [Fact]
    public void Parse_UnknownFutureCode_IsPreserved_NotRejected()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox("99", "12.34")));

        Assert.True(result.IsSuccess);
        var pair = Assert.Single(result.Value!.OtherInformation);
        Assert.Equal("99", pair.Code);
        Assert.Equal(12.34m, pair.Amount);
    }

    [Fact]
    public void Parse_OtherInformationAmount_IsCentsRounded()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox("40", "10.005")));

        Assert.True(result.IsSuccess);
        Assert.Equal(10.01m, Assert.Single(result.Value!.OtherInformation).Amount);
    }

    // ---- AC28: determinism -----------------------------------------------

    [Fact]
    public void Parse_IdenticalInput_ProducesEqualSuccessResults()
    {
        T4RawInput Build() => ValidInput(
            new T4RawBox(T4BoxCodes.EmploymentIncome, "50000.00"),
            new T4RawBox(T4BoxCodes.ProvinceOfEmployment, "ON"),
            new T4RawBox("40", "250.00"));

        var a = _parser.Parse(Build());
        var b = _parser.Parse(Build());

        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        Assert.Equal(a.Value!.ToString(), b.Value!.ToString());
        Assert.Equal(a.Value!.EmploymentIncome, b.Value!.EmploymentIncome);
        Assert.Equal(a.Value!.ProvinceOfEmployment, b.Value!.ProvinceOfEmployment);
        Assert.Equal(a.Value!.OtherInformation, b.Value!.OtherInformation);
    }

    [Fact]
    public void Parse_IdenticalInvalidInput_ProducesSameErrors()
    {
        T4RawInput Build() => ValidInput(new T4RawBox(T4BoxCodes.ProvinceOfEmployment, "XX"));

        var a = _parser.Parse(Build());
        var b = _parser.Parse(Build());

        Assert.True(a.IsFailure);
        Assert.True(b.IsFailure);
        Assert.Equal(
            a.Errors.Select(e => (e.Field, e.Message)),
            b.Errors.Select(e => (e.Field, e.Message)));
    }
}
