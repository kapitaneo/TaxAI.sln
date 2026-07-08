namespace TaxLogicEngine.Models;

/// <summary>
/// Canonical T5 box-code identifiers, as printed on the CRA T5 slip. These are
/// <b>box codes, not CRA line numbers</b> — retaining box codes as the canonical
/// identifiers is what CLAUDE.md requires; the box → CRA line mapping is a
/// separate, JSON-driven feature and no line number is hardcoded here.
/// <para>
/// Codes not listed here are not errors: the parser routes them to
/// <see cref="T5OtherInformation"/>. CRA does not define a fixed catalogue of
/// named boxes 31–39; that area is the generic "Other information" region.
/// </para>
/// </summary>
public static class T5BoxCodes
{
    // Dividends other than eligible dividends
    public const string OtherThanEligibleDividendsActual = "10";
    public const string OtherThanEligibleDividendsTaxable = "11";
    public const string OtherThanEligibleDividendsTaxCredit = "12";

    // Interest / other income
    public const string InterestFromCanadianSources = "13";
    public const string OtherIncomeFromCanadianSources = "14";

    // Foreign
    public const string ForeignIncome = "15";
    public const string ForeignTaxPaid = "16";

    public const string RoyaltiesFromCanadianSources = "17";
    public const string CapitalGainsDividends = "18";
    public const string AccruedIncomeAnnuities = "19";

    // Identification / control
    public const string ReportCode = "21";
    public const string RecipientIdentificationNumber = "22";
    public const string RecipientType = "23";

    // Eligible dividends
    public const string EligibleDividendsActual = "24";
    public const string EligibleDividendsTaxable = "25";
    public const string EligibleDividendsTaxCredit = "26";

    public const string ForeignCurrency = "27";
    public const string Transit = "28";
    public const string RecipientAccount = "29";

    // Named "Other information" box
    public const string EquityLinkedNotesInterest = "30";
}
