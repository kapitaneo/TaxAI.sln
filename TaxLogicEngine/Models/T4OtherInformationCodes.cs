namespace TaxLogicEngine.Models;

/// <summary>
/// The set of valid T4 "Other information" box codes for a tax year — <b>box codes,
/// not CRA line numbers</b>, so these are legitimately C# constants (the
/// no-hardcoding rule governs line numbers). Used only to validate mapping-config
/// completeness: a config entry whose code is neither a standard <see cref="T4BoxCodes"/>
/// box nor one of these fails closed (FR8), so a config typo cannot silently
/// misroute real dollars.
/// <para>
/// Wave 0 contract stage: <see cref="All"/> is an empty placeholder; the
/// CRA-verified 2025 code set is filled in the implementation task.
/// </para>
/// </summary>
public static class T4OtherInformationCodes
{
    /// <summary>The recognized "Other information" codes (2025). Empty until implemented.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal);
}
