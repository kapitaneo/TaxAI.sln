using System.Text.Json;
using System.Text.RegularExpressions;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// PII acceptance suite for spec 003 (T03) — the CLAUDE.md SIN rule for Box 12.
/// Covers PRD acceptance criteria 21 (masking on every output surface), 22 (no
/// full SIN in any built-in string/serialization form), and 23 (the CRA
/// <c>000000000</c> no-SIN sentinel is accepted). A "full SIN" is any run of nine
/// consecutive digits; these tests assert it never appears.
/// </summary>
public class T4SlipParserPiiTests
{
    private const string RealSin = "123456789";
    private const string NoSinSentinel = "000000000";

    private static readonly Regex NineDigitRun = new(@"\d{9}", RegexOptions.Compiled);

    private readonly T4SlipParser _parser = new();

    private static T4RawInput InputWithSin(string sin, params T4RawBox[] extra)
    {
        // Employer account number (Box 54) is a business number, not the SIN. Its
        // own 9-digit BN prefix is deliberately chosen to differ from RealSin so a
        // SIN-specific check is not confused by the (legitimate) BN digits.
        var boxes = new List<T4RawBox>
        {
            new(T4BoxCodes.SocialInsuranceNumber, sin),
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

    // ---- AC21: masking on output surfaces --------------------------------

    [Fact]
    public void ToString_MasksSin_AndNeverShowsFullValue()
    {
        var result = _parser.Parse(InputWithSin(RealSin));

        Assert.True(result.IsSuccess);
        var text = result.Value!.ToString();

        Assert.Contains("***-**-789", text);
        Assert.DoesNotContain(RealSin, text);
    }

    [Fact]
    public void ValidationErrors_NeverContainTheFullSin()
    {
        // Box 12 carries a real SIN, but another rule fails (bad province). No error
        // message may echo the SIN.
        var result = _parser.Parse(InputWithSin(RealSin, new T4RawBox(T4BoxCodes.ProvinceOfEmployment, "XX")));

        Assert.True(result.IsFailure);
        Assert.NotEmpty(result.Errors);
        Assert.DoesNotContain(result.Errors, e => e.Message.Contains(RealSin));
        Assert.DoesNotContain(result.Errors, e => NineDigitRun.IsMatch(e.Message));
    }

    [Fact]
    public void RecipientIdentifier_Masked_ExposesOnlyLastThree()
    {
        var slip = _parser.Parse(InputWithSin(RealSin)).Value!;

        Assert.Equal("***-**-789", slip.Sin!.Masked);
        Assert.DoesNotContain(RealSin, slip.Sin!.Masked);
    }

    // ---- AC22: no full SIN in any built-in string/serialization form -----

    [Fact]
    public void ToString_ContainsNoNineDigitRun()
    {
        var slip = _parser.Parse(InputWithSin(RealSin)).Value!;

        Assert.False(NineDigitRun.IsMatch(slip.ToString()));
    }

    [Fact]
    public void JsonSerialization_ContainsNoFullSin()
    {
        var slip = _parser.Parse(InputWithSin(RealSin)).Value!;

        var json = JsonSerializer.Serialize(slip);

        // The SIN itself must never appear; the Sin field serializes only its
        // masked form (RecipientIdentifier exposes no raw value).
        Assert.DoesNotContain(RealSin, json);
        Assert.Contains("***-**-789", json);
    }

    // ---- AC23: no-SIN sentinel accepted ----------------------------------

    [Fact]
    public void NoSinSentinel_IsAccepted_WithNoError()
    {
        var result = _parser.Parse(InputWithSin(NoSinSentinel));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value!.Sin);
    }

    [Fact]
    public void NoSinSentinel_MaskedFormRevealsNoRealSin()
    {
        var slip = _parser.Parse(InputWithSin(NoSinSentinel)).Value!;

        // The sentinel masks like any nine-digit value: only the last three show.
        Assert.Equal("***-**-000", slip.Sin!.Masked);
        Assert.DoesNotContain(NoSinSentinel, slip.ToString());
        Assert.False(NineDigitRun.IsMatch(slip.ToString()));
    }
}
