using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Maps a set of validated <see cref="T5Slip"/>s to aggregated
/// <see cref="TaxLineEntry"/> items using the per-tax-year JSON config. Pure and
/// deterministic: amounts on the same CRA line are summed (half-up to cents), each
/// entry records the box codes that fed it, and any populated box the config does
/// not map is surfaced in <see cref="T5MappingResult.UnmappedBoxCodes"/> rather than
/// dropped.
/// <para>
/// This is the happy-path aggregation (T06): it resolves config and aggregates.
/// The fail-closed slip validation (non-empty, single tax year, CAD only) is added
/// in T07.
/// </para>
/// </summary>
public sealed class T5LineMapper
{
    private readonly IT5LineMapProvider _provider;

    public T5LineMapper(IT5LineMapProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>Maps the slips to aggregated CRA line entries, or returns errors.</summary>
    public ParseResult<T5MappingResult> Map(IReadOnlyCollection<T5Slip> slips)
    {
        ArgumentNullException.ThrowIfNull(slips);

        var taxYear = slips.First().TaxYear;

        var mapResult = _provider.TryGetForYear(taxYear);
        if (mapResult.IsFailure)
        {
            return ParseResult<T5MappingResult>.Failure(mapResult.Errors);
        }

        var map = mapResult.Value!;
        var byLine = new Dictionary<string, LineAccumulator>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        var seenUnmapped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slip in slips)
        {
            foreach (var (boxCode, amount) in T5SlipBoxAmounts.Enumerate(slip))
            {
                var lines = map.LinesFor(boxCode);
                if (lines is null)
                {
                    if (seenUnmapped.Add(boxCode))
                    {
                        unmapped.Add(boxCode);
                    }

                    continue;
                }

                foreach (var line in lines)
                {
                    if (!byLine.TryGetValue(line, out var acc))
                    {
                        acc = new LineAccumulator();
                        byLine[line] = acc;
                    }

                    acc.Amount += amount;
                    if (!acc.SourceBoxes.Contains(boxCode))
                    {
                        acc.SourceBoxes.Add(boxCode);
                    }
                }
            }
        }

        var entries = byLine
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new TaxLineEntry(kv.Key, MoneyRounding.ToCents(kv.Value.Amount), kv.Value.SourceBoxes))
            .ToList();

        return ParseResult<T5MappingResult>.Success(new T5MappingResult(entries, unmapped));
    }

    private sealed class LineAccumulator
    {
        public decimal Amount;
        public List<string> SourceBoxes { get; } = new();
    }
}
