namespace TaxLogicEngine.Models;

/// <summary>
/// The successful outcome of mapping T4 slips to CRA lines: the aggregated
/// <see cref="TaxLineEntry"/> items, plus the codes of any populated boxes the
/// config did not map (so nothing a slip reported is silently dropped — FR14).
/// Mirrors <c>T5MappingResult</c>.
/// </summary>
public sealed record T4MappingResult(
    IReadOnlyList<TaxLineEntry> Lines,
    IReadOnlyList<string> UnmappedBoxCodes)
{
    /// <summary>An empty result — no mapped lines and nothing unmapped.</summary>
    public static T4MappingResult Empty { get; } =
        new(Array.Empty<TaxLineEntry>(), Array.Empty<string>());
}
