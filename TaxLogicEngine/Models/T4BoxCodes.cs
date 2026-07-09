namespace TaxLogicEngine.Models;

/// <summary>
/// Canonical T4 box-code identifiers, as printed on the CRA T4 slip. These are
/// <b>box codes, not CRA line numbers</b> — retaining box codes as the canonical
/// identifiers is what CLAUDE.md requires; the box → CRA line mapping is a
/// separate, JSON-driven feature (spec 004) and no line number is hardcoded here.
/// <para>
/// Box 28 (Exempt) is deliberately absent: it is a checkbox trio carried on
/// <see cref="T4RawInput"/> as three booleans, not a code/value box. Codes not
/// listed here are not errors — the parser routes them to
/// <see cref="T4OtherInformation"/>.
/// </para>
/// <para>All values are CRA-verified for the 2025 tax year — see the CRA-sources
/// table in <c>specs/003-t4-slip-parsing/prd.md</c>.</para>
/// </summary>
public static class T4BoxCodes
{
    public const string ProvinceOfEmployment = "10";
    public const string SocialInsuranceNumber = "12";
    public const string EmploymentIncome = "14";

    public const string CppContributions = "16";
    public const string SecondCppContributions = "16A";
    public const string QppContributions = "17";
    public const string SecondQppContributions = "17A";

    public const string EiPremiums = "18";
    public const string RppContributions = "20";
    public const string IncomeTaxDeducted = "22";
    public const string EiInsurableEarnings = "24";
    public const string CppQppPensionableEarnings = "26";

    public const string EmploymentCode = "29";
    public const string UnionDues = "44";
    public const string DentalBenefits = "45";
    public const string CharitableDonations = "46";

    public const string RppDpspRegistrationNumber = "50";
    public const string PensionAdjustment = "52";
    public const string EmployerAccountNumber = "54";

    public const string PpipPremiums = "55";
    public const string PpipInsurableEarnings = "56";
}
