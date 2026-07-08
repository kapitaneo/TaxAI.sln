namespace TaxLogicEngine.Models;

/// <summary>
/// One aggregated destination on the tax return: a CRA line number and the total
/// <see cref="decimal"/> amount that lands on it, plus the box codes that fed it.
/// <para>
/// <see cref="LineNumber"/> is a CRA line string (e.g. <c>"12100"</c>) sourced from
/// the JSON mapping config — never a hardcoded C# constant. <see cref="SourceBoxCodes"/>
/// is SIN-free provenance (box codes only) for auditing and later explanations.
/// The same shape is used for every destination — income lines and credit lines alike.
/// </para>
/// </summary>
public sealed record TaxLineEntry(string LineNumber, decimal Amount, IReadOnlyList<string> SourceBoxCodes)
{
    /// <summary>Convenience overload for an entry with no recorded provenance.</summary>
    public TaxLineEntry(string lineNumber, decimal amount)
        : this(lineNumber, amount, Array.Empty<string>())
    {
    }
}
