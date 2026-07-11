using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Unit tests for <see cref="T4SlipParser"/> (spec 003, T02): the happy path plus
/// every fail-closed validation branch — PRD acceptance criteria 1–5, 7–15, 19, 20.
/// PII masking (21–23), tax-year isolation/data-driven (6, 24, 25), and
/// rounding/boundary/passthrough/determinism (16–18, 26–28) live in the T03–T05
/// suites.
/// </summary>
public class T4SlipParserTests
{
    private readonly T4SlipParser _parser = new();

    // The three A1 required-present boxes, valid.
    private static List<T4RawBox> RequiredBoxes() => new()
    {
        new(T4BoxCodes.SocialInsuranceNumber, "123456789"),
        new(T4BoxCodes.EmployerAccountNumber, "123456789RP0001"),
        new(T4BoxCodes.DentalBenefits, "1"),
    };

    // Builds a slip with valid identification + required boxes; any argument can be
    // overridden. T4RawInput is a class (not a record), so there is no `with`.
    private static T4RawInput Input(
        int? year = 2025,
        string? employerName = "ACME CORP",
        string? employerAddress = "1 WAY ST, OTTAWA ON",
        string? employeeName = "DOE, JANE",
        string? employeeAddress = "2 HOME AVE, OTTAWA ON",
        IEnumerable<T4RawBox>? boxes = null,
        bool? exemptCppQpp = null,
        bool? exemptEi = null,
        bool? exemptPpip = null) =>
        new()
        {
            Year = year,
            EmployerName = employerName,
            EmployerAddress = employerAddress,
            EmployeeName = employeeName,
            EmployeeAddress = employeeAddress,
            Boxes = (boxes ?? RequiredBoxes()).ToList(),
            ExemptCppQpp = exemptCppQpp,
            ExemptEi = exemptEi,
            ExemptPpip = exemptPpip,
        };

    // Valid identification + required boxes, plus any extra boxes for the case.
    private static T4RawInput ValidInput(params T4RawBox[] extraBoxes)
    {
        var boxes = RequiredBoxes();
        boxes.AddRange(extraBoxes);
        return Input(boxes: boxes);
    }

    private static bool HasError(ParseResult<T4Slip> result, string field) =>
        result.Errors.Any(e => e.Field == field);

    // ---- AC1: happy path -------------------------------------------------

    [Fact]
    public void Parse_FullValidSlip_SucceedsWithTypedValues()
    {
        var input = ValidInput(
            new T4RawBox(T4BoxCodes.EmploymentIncome, "50000.00"),
            new T4RawBox(T4BoxCodes.CppContributions, "3000.00"),
            new T4RawBox(T4BoxCodes.CppQppPensionableEarnings, "50000.00"),
            new T4RawBox(T4BoxCodes.EiPremiums, "900.00"),
            new T4RawBox(T4BoxCodes.EiInsurableEarnings, "50000.00"),
            new T4RawBox(T4BoxCodes.ProvinceOfEmployment, "ON"),
            new T4RawBox(T4BoxCodes.EmploymentCode, "11"));

        var result = _parser.Parse(input);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var slip = result.Value!;
        Assert.Equal(2025, slip.Year);
        Assert.Equal("ACME CORP", slip.EmployerName);
        Assert.Equal(50000.00m, slip.EmploymentIncome);
        Assert.Equal(3000.00m, slip.CppContributions);
        Assert.Equal("ON", slip.ProvinceOfEmployment);
        Assert.Equal("11", slip.EmploymentCode);
        Assert.Equal("1", slip.DentalBenefitCode);
        Assert.Equal("123456789RP0001", slip.EmployerAccountNumber);
        Assert.Equal("***-**-789", slip.Sin!.Masked);
    }

    // ---- AC2: duplicate box code ----------------------------------------

    [Fact]
    public void Parse_DuplicateBoxCode_FailsClosed()
    {
        var input = ValidInput(
            new T4RawBox(T4BoxCodes.EmploymentIncome, "100.00"),
            new T4RawBox(T4BoxCodes.EmploymentIncome, "200.00"));

        var result = _parser.Parse(input);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.True(HasError(result, "Box 14"));
    }

    // ---- AC3: malformed amount ------------------------------------------

    [Fact]
    public void Parse_NonNumericAmount_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.EmploymentIncome, "abc")));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 14"));
    }

    // ---- AC4: negative amount (A2) --------------------------------------

    [Fact]
    public void Parse_NegativeAmount_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.EmploymentIncome, "-1.00")));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Box 14" && e.Message.Contains("negative"));
    }

    // ---- AC5: missing year ----------------------------------------------

    [Fact]
    public void Parse_MissingYear_FailsClosed()
    {
        var result = _parser.Parse(Input(year: null));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, nameof(T4RawInput.Year)));
    }

    // ---- AC7/AC8: missing employer/employee name ------------------------

    [Fact]
    public void Parse_MissingEmployerName_FailsClosed()
    {
        var result = _parser.Parse(Input(employerName: "  "));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, nameof(T4RawInput.EmployerName)));
    }

    [Fact]
    public void Parse_MissingEmployeeName_FailsClosed()
    {
        var result = _parser.Parse(Input(employeeName: null));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, nameof(T4RawInput.EmployeeName)));
    }

    // ---- A1 required boxes: 12, 54, 45 ----------------------------------

    [Theory]
    [InlineData(T4BoxCodes.SocialInsuranceNumber, "Box 12")]
    [InlineData(T4BoxCodes.EmployerAccountNumber, "Box 54")]
    [InlineData(T4BoxCodes.DentalBenefits, "Box 45")]
    public void Parse_MissingRequiredBox_FailsClosed(string codeToOmit, string expectedField)
    {
        var kept = RequiredBoxes().Where(b => b.Code != codeToOmit);
        var result = _parser.Parse(Input(boxes: kept));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, expectedField));
    }

    [Fact]
    public void Parse_SinRequiredError_DoesNotEchoSin()
    {
        // Box 12 present but blank → required error; the message must never contain
        // a 9-digit SIN regardless.
        var boxes = new List<T4RawBox>
        {
            new(T4BoxCodes.SocialInsuranceNumber, "   "),
            new(T4BoxCodes.EmployerAccountNumber, "123456789RP0001"),
            new(T4BoxCodes.DentalBenefits, "1"),
        };

        var result = _parser.Parse(Input(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 12"));
        Assert.DoesNotContain(result.Errors, e => System.Text.RegularExpressions.Regex.IsMatch(e.Message, @"\d{9}"));
    }

    // ---- AC9/10/11: constrained-value boxes -----------------------------

    [Fact]
    public void Parse_InvalidProvinceCode_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.ProvinceOfEmployment, "XX")));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 10"));
    }

    [Fact]
    public void Parse_InvalidEmploymentCode_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.EmploymentCode, "18")));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 29"));
    }

    [Fact]
    public void Parse_InvalidDentalCode_FailsClosed()
    {
        var boxes = new List<T4RawBox>
        {
            new(T4BoxCodes.SocialInsuranceNumber, "123456789"),
            new(T4BoxCodes.EmployerAccountNumber, "123456789RP0001"),
            new(T4BoxCodes.DentalBenefits, "6"),
        };

        var result = _parser.Parse(Input(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 45"));
    }

    // ---- AC12–15: conditional companion boxes ---------------------------

    [Fact]
    public void Parse_Box16WithoutBox26_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.CppContributions, "3000.00")));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 26"));
    }

    [Fact]
    public void Parse_Box18WithoutBox24_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.EiPremiums, "900.00")));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 24"));
    }

    [Fact]
    public void Parse_Box55WithoutBox56_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.PpipPremiums, "300.00")));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 56"));
    }

    [Fact]
    public void Parse_Box52WithoutBox50_FailsClosed()
    {
        var result = _parser.Parse(ValidInput(new T4RawBox(T4BoxCodes.PensionAdjustment, "1200.00")));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 50"));
    }

    [Fact]
    public void Parse_ConditionalCompanionPresentWithZero_Succeeds()
    {
        // Companion value may be 0; here we confirm presence-with-zero satisfies
        // the conditional rule (the 0m storage boundary is asserted in T05).
        var input = ValidInput(
            new T4RawBox(T4BoxCodes.EiPremiums, "900.00"),
            new T4RawBox(T4BoxCodes.EiInsurableEarnings, "0"));

        var result = _parser.Parse(input);

        Assert.True(result.IsSuccess);
    }

    // ---- AC19: absent optional box defaults ------------------------------

    [Fact]
    public void Parse_AbsentOptionalBoxes_TakeDocumentedDefaults()
    {
        var result = _parser.Parse(ValidInput());

        Assert.True(result.IsSuccess);
        var slip = result.Value!;
        Assert.Equal(0m, slip.EmploymentIncome);
        Assert.Equal(0m, slip.UnionDues);
        Assert.Null(slip.ProvinceOfEmployment);
        Assert.Null(slip.EmploymentCode);
        Assert.False(slip.ExemptCppQpp);
        Assert.False(slip.ExemptEi);
        Assert.False(slip.ExemptPpip);
        Assert.Empty(slip.OtherInformation);
    }

    [Fact]
    public void Parse_ExemptIndicators_FlowThrough()
    {
        var input = Input(exemptCppQpp: true, exemptEi: true, exemptPpip: false);

        var result = _parser.Parse(input);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ExemptCppQpp);
        Assert.True(result.Value!.ExemptEi);
        Assert.False(result.Value!.ExemptPpip);
    }

    // ---- AC20: empty slip -> one error per missing required field --------

    [Fact]
    public void Parse_EmptySlip_FailsClosedWithoutThrowing()
    {
        var result = _parser.Parse(new T4RawInput());

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Errors);
        // Year + 4 identification fields + Box 12 + Box 54 + Box 45 = 8 required items.
        Assert.True(HasError(result, nameof(T4RawInput.Year)));
        Assert.True(HasError(result, nameof(T4RawInput.EmployerName)));
        Assert.True(HasError(result, nameof(T4RawInput.EmployerAddress)));
        Assert.True(HasError(result, nameof(T4RawInput.EmployeeName)));
        Assert.True(HasError(result, nameof(T4RawInput.EmployeeAddress)));
        Assert.True(HasError(result, "Box 12"));
        Assert.True(HasError(result, "Box 54"));
        Assert.True(HasError(result, "Box 45"));
    }

    [Fact]
    public void Parse_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _parser.Parse(null!));
    }
}
