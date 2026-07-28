namespace Tenancy.Models;

/// <summary>
/// The structural slip-type discriminator owned by this module.
/// <para>
/// It asserts <strong>no</strong> CRA semantics: no box meaning, no line
/// meaning, no rate, no threshold, no filing rule. It is a storage
/// discriminator and nothing more — the tax meaning of a T4 lives in
/// TaxLogicEngine's mapping config, and duplicating any part of it here would
/// create a second authority.
/// </para>
/// </summary>
public static class SlipTypeCodes
{
    /// <summary>The only slip type the merged upstream contract produces.</summary>
    public const string T4 = "T4";

    /// <summary>Every recognized structural code.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { T4 });

    /// <summary>An unrecognized code is refused; it is never stored "as given".</summary>
    public static bool IsRecognized(string? code) =>
        string.Equals(code, T4, StringComparison.Ordinal);
}
