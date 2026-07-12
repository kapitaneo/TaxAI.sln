namespace TaxLogicEngine.Models;

/// <summary>
/// One extracted T4 box as received from upstream — a box code and its raw string
/// value (which may be missing/blank). Values are left as strings here; the parser
/// is responsible for typing and validating them.
/// </summary>
/// <param name="Code">The box code, e.g. <c>"14"</c> or <c>"16A"</c>.</param>
/// <param name="Value">The raw value, or null when the box was present but empty.</param>
public sealed record T4RawBox(string Code, string? Value);

/// <summary>
/// The already-extracted, structured input for a single T4 slip. Everything is
/// nullable/loose because it is unvalidated upstream data; the parser turns this
/// into a validated <see cref="T4Slip"/>.
/// <para>
/// <see cref="Boxes"/> is a <b>list, not a dictionary</b>, on purpose: it can carry
/// the same box code twice, which the parser must detect and reject. A dictionary
/// would silently collapse duplicates.
/// </para>
/// <para>
/// Box 28 (Exempt — CPP/QPP, EI, PPIP) is a checkbox trio, not a code/amount pair,
/// so it is modelled as the three nullable booleans below rather than as entries in
/// <see cref="Boxes"/>. Null (or false) means "not marked".
/// </para>
/// </summary>
public sealed class T4RawInput
{
    /// <summary>The four-digit calendar year of the slip. This parser accepts 2025 only.</summary>
    public int? Year { get; init; }

    // Identification
    public string? EmployerName { get; init; }
    public string? EmployerAddress { get; init; }
    public string? EmployeeName { get; init; }
    public string? EmployeeAddress { get; init; }

    // Box 28 – Exempt: three independent checkbox indicators.
    public bool? ExemptCppQpp { get; init; }
    public bool? ExemptEi { get; init; }
    public bool? ExemptPpip { get; init; }

    /// <summary>The extracted boxes, in the order received; may contain duplicate codes.</summary>
    public IReadOnlyList<T4RawBox> Boxes { get; init; } = Array.Empty<T4RawBox>();
}
