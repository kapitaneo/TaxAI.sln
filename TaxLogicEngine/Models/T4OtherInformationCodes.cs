namespace TaxLogicEngine.Models;

/// <summary>
/// The set of valid T4 "Other information" box codes for a tax year — <b>box codes,
/// not CRA line numbers</b>, so these are legitimately C# constants (the
/// no-hardcoding rule governs line numbers). Used only to validate mapping-config
/// completeness: a config entry whose code is neither a standard <see cref="T4BoxCodes"/>
/// box nor one of these fails closed (FR8), so a config typo cannot silently
/// misroute real dollars.
/// <para>
/// The set is the CRA-verified <b>2025</b> "Other information" code set (see the
/// CRA-sources table in <c>specs/003-t4-slip-parsing/prd.md</c>). Codes 90/91/92
/// are valid for 2024 and after; the whole set is year-scoped, consistent with the
/// per-tax-year rule.
/// </para>
/// </summary>
public static class T4OtherInformationCodes
{
    /// <summary>The recognized "Other information" codes for the 2025 tax year.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "30", "31", "32", "33", "34", "36", "38", "39", "40", "41", "42", "43",
        "66", "67", "69", "71", "74", "75", "77", "78", "79", "80", "81", "82",
        "83", "85", "86", "87", "88", "90", "91", "92", "94", "95",
    };
}
