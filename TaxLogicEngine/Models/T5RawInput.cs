namespace TaxLogicEngine.Models;

/// <summary>
/// One extracted T5 box as received from upstream — a box code and its raw string
/// value (which may be missing/blank). Values are left as strings here; the parser
/// is responsible for typing and validating them.
/// </summary>
/// <param name="Code">The box code, e.g. <c>"13"</c>.</param>
/// <param name="Value">The raw value, or null when the box was present but empty.</param>
public sealed record T5RawBox(string Code, string? Value);

/// <summary>
/// The already-extracted, structured input for a single T5 slip. Everything is
/// nullable/loose because it is unvalidated upstream data; the parser turns this
/// into a validated <see cref="T5Slip"/>.
/// <para>
/// <see cref="Boxes"/> is a <b>list, not a dictionary</b>, on purpose: it can carry
/// the same box code twice, which the parser must detect and reject. A dictionary
/// would silently collapse duplicates.
/// </para>
/// </summary>
public sealed class T5RawInput
{
    public int? TaxYear { get; init; }
    public string? PayerName { get; init; }
    public string? PayerAddress { get; init; }
    public string? RecipientName { get; init; }
    public string? RecipientAddress { get; init; }

    /// <summary>The extracted boxes, in the order received; may contain duplicate codes.</summary>
    public IReadOnlyList<T5RawBox> Boxes { get; init; } = Array.Empty<T5RawBox>();
}
