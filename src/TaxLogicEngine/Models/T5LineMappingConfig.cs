namespace TaxLogicEngine.Models;

/// <summary>
/// One box→line rule as authored in the JSON mapping config: a T5 box code and the
/// CRA line(s) its amount is reported on. A box may map to <b>several</b> lines
/// (e.g. box 11 → 12000 and 12010), so <see cref="LineNumbers"/> is a list.
/// </summary>
public sealed record BoxLineMapping(string BoxCode, IReadOnlyList<string> LineNumbers);

/// <summary>
/// The deserialized per-tax-year mapping config (one JSON file per year). The
/// resolved, validated lookup form is <see cref="T5LineMap"/>.
/// </summary>
public sealed record T5LineMappingConfig(int TaxYear, IReadOnlyList<BoxLineMapping> Mappings);
