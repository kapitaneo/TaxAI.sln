using System.Reflection;
using System.Text.RegularExpressions;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Tax-year isolation and data-driven-mapping acceptance suite for spec 003 (T04).
/// Covers PRD acceptance criteria 6 (a non-2025 slip fails closed and is never
/// coerced), 24 (no 2025-specific box rule is applied to another year), and 25
/// (no CRA line number appears in the compiled T4 types — box codes are the only
/// identifiers).
/// </summary>
public class T4SlipParserYearIsolationTests
{
    private readonly T4SlipParser _parser = new();

    // A slip that is fully valid apart from whatever the test overrides.
    private static T4RawInput InputForYear(int? year, params T4RawBox[] extra)
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
            Year = year,
            EmployerName = "ACME CORP",
            EmployerAddress = "1 WAY ST, OTTAWA ON",
            EmployeeName = "DOE, JANE",
            EmployeeAddress = "2 HOME AVE, OTTAWA ON",
            Boxes = boxes,
        };
    }

    private static bool HasYearError(ParseResult<T4Slip> result) =>
        result.Errors.Any(e => e.Field == nameof(T4RawInput.Year));

    // ---- AC6: wrong tax year fails closed, both directions ---------------

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    public void Parse_NonMatchingYear_FailsClosedWithYearError(int year)
    {
        var result = _parser.Parse(InputForYear(year));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.True(HasYearError(result));
    }

    [Fact]
    public void Parse_WrongYear_IsNotCoercedTo2025()
    {
        // An otherwise-valid slip for 2024 must not slip through as a 2025 slip:
        // had it been coerced, every other field is valid and it would have
        // succeeded. Instead it fails closed on the year.
        var result = _parser.Parse(InputForYear(2024));

        Assert.True(result.IsFailure);
        Assert.True(HasYearError(result));
    }

    // ---- AC24: 2025-only box rules not applied to another year -----------

    [Fact]
    public void Parse_NonMatchingYear_WithYearSensitiveBoxes_StillFailsClosed()
    {
        // 16A/17A (second CPP/QPP, 2024+) and Other-information codes 90/91/92
        // (valid 2024–2025) are present. For a 2024 slip the parser must not apply
        // any 2025 interpretation that would let it succeed — it fails closed on the
        // year instead.
        var result = _parser.Parse(InputForYear(
            2024,
            new T4RawBox(T4BoxCodes.SecondCppContributions, "100.00"),
            new T4RawBox(T4BoxCodes.SecondQppContributions, "0.00"),
            new T4RawBox("90", "50.00"),
            new T4RawBox("91", "60.00"),
            new T4RawBox("92", "70.00")));

        Assert.True(result.IsFailure);
        Assert.True(HasYearError(result));
    }

    [Fact]
    public void Parse_MatchingYear_WithSameBoxes_Succeeds()
    {
        // Control: the identical box set for 2025 is accepted, proving the failure
        // above is the year rule and not the boxes themselves.
        var result = _parser.Parse(InputForYear(
            2025,
            new T4RawBox(T4BoxCodes.SecondCppContributions, "100.00"),
            new T4RawBox(T4BoxCodes.SecondQppContributions, "0.00"),
            new T4RawBox("90", "50.00"),
            new T4RawBox("91", "60.00"),
            new T4RawBox("92", "70.00")));

        Assert.True(result.IsSuccess);
    }

    // ---- AC25: no CRA line number in the compiled T4 types ---------------

    [Fact]
    public void T4BoxCodes_AreBoxCodes_NotCraLineNumbers()
    {
        // Box codes are 1–2 digits with an optional trailing letter (e.g. "16A").
        // CRA line numbers are longer numeric strings (e.g. "10100", "43700"); none
        // may appear as a box-code constant.
        var boxCodeShape = new Regex(@"^\d{1,2}[A-Z]?$");
        var lineNumberShape = new Regex(@"^\d{3,}$");

        var codes = typeof(T4BoxCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(codes);
        Assert.All(codes, code =>
        {
            Assert.Matches(boxCodeShape, code);
            Assert.DoesNotMatch(lineNumberShape, code);
        });
    }

    [Fact]
    public void CompiledT4Types_ExposeNoCraLineNumberConstant()
    {
        // No public string constant anywhere on the T4* production types looks like
        // a CRA line number — mapping (box → line) is spec 004 and lives in JSON.
        var lineNumberShape = new Regex(@"^\d{3,}$");

        var t4Types = typeof(T4Slip).Assembly
            .GetTypes()
            .Where(t => t.Name.StartsWith("T4", StringComparison.Ordinal));

        foreach (var type in t4Types)
        {
            var constStrings = type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
                .Select(f => (string)f.GetValue(null)!);

            Assert.DoesNotContain(constStrings, s => lineNumberShape.IsMatch(s));
        }
    }
}
