namespace TaxLogicEngine.Models;

/// <summary>
/// One box→line rule as authored in the JSON mapping config: a T4 box code and the
/// CRA line(s) its amount is reported on. A box may map to <b>several</b> lines, so
/// <see cref="LineNumbers"/> is a list. Mirrors <c>BoxLineMapping</c>.
/// </summary>
public sealed record T4BoxLineMapping(string BoxCode, IReadOnlyList<string> LineNumbers);

/// <summary>
/// The deserialized per-tax-year T4 mapping config (one JSON file per year). The
/// resolved, validated lookup form is <see cref="T4LineMap"/>. Mirrors
/// <c>T5LineMappingConfig</c>.
/// </summary>
public sealed record T4LineMappingConfig(int TaxYear, IReadOnlyList<T4BoxLineMapping> Mappings);
