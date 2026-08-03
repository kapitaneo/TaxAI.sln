using System.Text.RegularExpressions;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Spec 015 T01 — the employee-copy T4 parser semantics.
/// <para>
/// Every document a client uploads is the copy the employer gave the employee, not
/// the employer/filing-side copy the spec 003 <c>[CONFIRM]</c> decision presumed.
/// Two CRA facts, both read from "T4 slip – Information for employers" on
/// canada.ca on 2026-08-03, decide what this parser may require of it:
/// </para>
/// <list type="bullet">
/// <item>the payroll account number goes in Box 54 on the employer's own copy and
/// the copy sent to the CRA only, and is <b>not</b> entered on the copies given to
/// employees — so Box 54 cannot be required;</item>
/// <item>indicating employer-offered dental benefits is mandatory for calendar year
/// 2023 and after, and the administrative policy permitting its omission when code
/// 1 applies covers calendar years 2023 and 2024 <b>only</b> — so Box 45 remains
/// required for 2025.</item>
/// </list>
/// <para>
/// These tests fix both directions of that change: what the parser must now accept,
/// and — just as important — what it must still refuse. Relaxing a required-field
/// rule is only safe if the rules either side of it demonstrably did not move, so
/// the "unchanged" cases below are asserted as explicitly as the new ones.
/// </para>
/// </summary>
public class T4SlipParserEmployeeCopyTests
{
    private readonly T4SlipParser _parser = new();

    private const string Sin = "123456789";

    /// <summary>
    /// A realistic employee copy: no Box 54, no addresses, and the tax content a
    /// real slip carries. This is the shape spec 015 exists to accept.
    /// </summary>
    private static T4RawInput EmployeeCopy(
        int? year = 2025,
        string? employerName = "ACME CORP",
        string? employeeName = "DOE, JANE",
        string? employerAddress = null,
        string? employeeAddress = null,
        IEnumerable<T4RawBox>? boxes = null) =>
        new()
        {
            Year = year,
            EmployerName = employerName,
            EmployeeName = employeeName,
            EmployerAddress = employerAddress,
            EmployeeAddress = employeeAddress,
            Boxes = (boxes ?? DefaultBoxes()).ToList(),
        };

    private static List<T4RawBox> DefaultBoxes() => new()
    {
        new(T4BoxCodes.SocialInsuranceNumber, Sin),
        new(T4BoxCodes.DentalBenefits, "1"),
        new(T4BoxCodes.EmploymentIncome, "52000.00"),
        new(T4BoxCodes.IncomeTaxDeducted, "8000.00"),
    };

    private static bool HasError(ParseResult<T4Slip> result, string field) =>
        result.Errors.Any(e => e.Field == field);

    // ---- Box 54 is no longer required --------------------------------------

    [Fact]
    public void Parse_ValidEmployeeCopyWithoutBox54_Succeeds()
    {
        var result = _parser.Parse(EmployeeCopy());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);

        var slip = result.Value!;
        Assert.Equal(2025, slip.Year);
        Assert.Equal("ACME CORP", slip.EmployerName);
        Assert.Equal("DOE, JANE", slip.EmployeeName);
        Assert.Equal(52000.00m, slip.EmploymentIncome);
        Assert.Equal(8000.00m, slip.IncomeTaxDeducted);
        Assert.Equal("1", slip.DentalBenefitCode);
    }

    [Fact]
    public void Parse_MissingBox54_ProducesNoErrorAtAll()
    {
        // Not merely "still succeeds": the retired rule must leave no trace. A
        // Box 54 error surfacing on any other code path would fail here.
        var result = _parser.Parse(EmployeeCopy());

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Errors, e => e.Field == "Box 54");
        Assert.DoesNotContain(
            result.Errors,
            e => e.Message.Contains("account number", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_MissingBox54_InventsNoAccountNumber()
    {
        // The rule was removed, not replaced by a default. A placeholder here would
        // put a fabricated employer identifier into a filing input.
        var result = _parser.Parse(EmployeeCopy());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.EmployerAccountNumber);
    }

    [Fact]
    public void Parse_BlankBox54_IsAcceptedAndNotSubstituted()
    {
        // A scan that produced an empty Box 54 is the same case as an absent one.
        var boxes = DefaultBoxes();
        boxes.Add(new T4RawBox(T4BoxCodes.EmployerAccountNumber, "   "));

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsSuccess);
        Assert.False(HasError(result, "Box 54"));
        Assert.Equal("   ", result.Value!.EmployerAccountNumber);
    }

    [Fact]
    public void Parse_Box54WhenPresent_StillFlowsThroughUnchanged()
    {
        // Optional does not mean discarded: an employer-copy slip must still surface
        // its account number exactly as extracted.
        var boxes = DefaultBoxes();
        boxes.Add(new T4RawBox(T4BoxCodes.EmployerAccountNumber, "123456789RP0001"));

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsSuccess);
        Assert.Equal("123456789RP0001", result.Value!.EmployerAccountNumber);
    }

    // ---- Box 45 is still required for 2025 ---------------------------------

    [Fact]
    public void Parse_2025SlipWithoutBox45_FailsClosedThroughTheEngine()
    {
        var boxes = DefaultBoxes().Where(b => b.Code != T4BoxCodes.DentalBenefits);

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.True(HasError(result, "Box 45"));
    }

    [Fact]
    public void Parse_2025SlipWithBlankBox45_FailsClosed()
    {
        var boxes = DefaultBoxes().Select(b =>
            b.Code == T4BoxCodes.DentalBenefits ? new T4RawBox(b.Code, "  ") : b);

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 45"));
    }

    [Fact]
    public void Parse_InvalidBox45Code_StillFailsClosed()
    {
        // The constrained-value rule is independent of the required-presence rule
        // and neither moved.
        var boxes = DefaultBoxes().Select(b =>
            b.Code == T4BoxCodes.DentalBenefits ? new T4RawBox(b.Code, "6") : b);

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 45"));
    }

    // ---- Addresses are optional metadata -----------------------------------

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData(null, "2 HOME AVE, OTTAWA ON")]
    [InlineData("1 WAY ST, OTTAWA ON", null)]
    public void Parse_MissingOrBlankAddresses_DoNotBlockReview(
        string? employerAddress,
        string? employeeAddress)
    {
        var result = _parser.Parse(EmployeeCopy(
            employerAddress: employerAddress,
            employeeAddress: employeeAddress));

        Assert.True(result.IsSuccess);
        Assert.False(HasError(result, nameof(T4RawInput.EmployerAddress)));
        Assert.False(HasError(result, nameof(T4RawInput.EmployeeAddress)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_AbsentAddress_IsCarriedThroughUnchangedNotSubstituted(string? address)
    {
        // Whatever the extraction produced — null, empty, or whitespace — must reach
        // the slip byte-identically. A substituted "N/A" or copied-from-the-other
        // address would be an invented taxpayer fact.
        var result = _parser.Parse(EmployeeCopy(
            employerAddress: address,
            employeeAddress: address));

        Assert.True(result.IsSuccess);
        Assert.Equal(address, result.Value!.EmployerAddress);
        Assert.Equal(address, result.Value!.EmployeeAddress);
    }

    [Fact]
    public void Parse_PresentAddresses_StillFlowThroughUnchanged()
    {
        var result = _parser.Parse(EmployeeCopy(
            employerAddress: "1 WAY ST, OTTAWA ON",
            employeeAddress: "2 HOME AVE, OTTAWA ON"));

        Assert.True(result.IsSuccess);
        Assert.Equal("1 WAY ST, OTTAWA ON", result.Value!.EmployerAddress);
        Assert.Equal("2 HOME AVE, OTTAWA ON", result.Value!.EmployeeAddress);
    }

    // ---- Display names are still required ----------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_MissingEmployerName_StillFailsClosed(string? employerName)
    {
        var result = _parser.Parse(EmployeeCopy(employerName: employerName));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, nameof(T4RawInput.EmployerName)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_MissingEmployeeName_StillFailsClosed(string? employeeName)
    {
        var result = _parser.Parse(EmployeeCopy(employeeName: employeeName));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, nameof(T4RawInput.EmployeeName)));
    }

    // ---- Box 12 and the tax-year rule are unchanged -------------------------

    [Fact]
    public void Parse_MissingBox12_StillFailsClosed()
    {
        var boxes = DefaultBoxes().Where(b => b.Code != T4BoxCodes.SocialInsuranceNumber);

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.True(HasError(result, "Box 12"));
    }

    [Fact]
    public void Parse_MissingBox12_ErrorStillNeverEchoesASin()
    {
        var boxes = DefaultBoxes().Select(b =>
            b.Code == T4BoxCodes.SocialInsuranceNumber ? new T4RawBox(b.Code, "   ") : b);

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 12"));
        Assert.DoesNotContain(result.Errors, e => Regex.IsMatch(e.Message, @"\d{9}"));
    }

    [Fact]
    public void Parse_ValidEmployeeCopy_MasksTheSin()
    {
        // The one permitted SIN representation is unchanged by this task.
        var result = _parser.Parse(EmployeeCopy());

        Assert.True(result.IsSuccess);
        Assert.Equal("***-**-789", result.Value!.Sin!.Masked);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(null)]
    public void Parse_UnsupportedYear_StillFailsClosedWithNoAdjacentFallback(int? year)
    {
        var result = _parser.Parse(EmployeeCopy(year: year));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.True(HasError(result, nameof(T4RawInput.Year)));
    }

    // ---- The relaxation is scoped: nothing else moved -----------------------

    [Fact]
    public void Parse_EmployeeCopyWithOtherDefects_StillReportsThemAll()
    {
        // An employee copy is not a waiver. Amount, constrained-value, companion,
        // and duplicate rules all still fire on one.
        var boxes = new List<T4RawBox>
        {
            new(T4BoxCodes.SocialInsuranceNumber, Sin),
            new(T4BoxCodes.DentalBenefits, "1"),
            new(T4BoxCodes.EmploymentIncome, "-1.00"),
            new(T4BoxCodes.ProvinceOfEmployment, "XX"),
            new(T4BoxCodes.CppContributions, "3000.00"),
        };

        var result = _parser.Parse(EmployeeCopy(boxes: boxes));

        Assert.True(result.IsFailure);
        Assert.True(HasError(result, "Box 14"));  // negative amount
        Assert.True(HasError(result, "Box 10"));  // invalid province
        Assert.True(HasError(result, "Box 26"));  // missing CPP companion
    }

    [Fact]
    public void Parse_EmployeeCopy_IsDeterministic()
    {
        var first = _parser.Parse(EmployeeCopy());
        var second = _parser.Parse(EmployeeCopy());

        Assert.Equal(first.IsSuccess, second.IsSuccess);
        Assert.Equal(
            first.Errors.Select(e => (e.Field, e.Message)),
            second.Errors.Select(e => (e.Field, e.Message)));
        Assert.Equal(first.Value!.EmployerAccountNumber, second.Value!.EmployerAccountNumber);
        Assert.Equal(first.Value!.EmploymentIncome, second.Value!.EmploymentIncome);
    }
}
