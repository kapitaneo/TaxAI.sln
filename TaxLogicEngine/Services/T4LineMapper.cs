using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Maps a set of validated <see cref="T4Slip"/>s to aggregated
/// <see cref="TaxLineEntry"/> items using the per-tax-year JSON config. Pure and
/// deterministic: amounts on the same CRA line are summed (half-up to cents), each
/// entry records the box codes that fed it, and any populated box the config does
/// not map is surfaced in <see cref="T4MappingResult.UnmappedBoxCodes"/> rather
/// than dropped. Mirrors <c>T5LineMapper</c> (without the currency check — T4 is
/// CAD by definition).
/// <para>
/// Fail-closed: the slips are validated first (non-empty, all one tax year, and a
/// config exists for the year); on any problem the mapper returns errors and no
/// result. Error messages never reference the Box 12 SIN.
/// </para>
/// </summary>
public sealed class T4LineMapper
{
    private readonly IT4LineMapProvider _provider;

    public T4LineMapper(IT4LineMapProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>Maps the slips to aggregated CRA line entries, or returns errors.</summary>
    public ParseResult<T4MappingResult> Map(IReadOnlyCollection<T4Slip> slips)
    {
        var errors = Validate(slips);
        if (errors.Count > 0)
        {
            return ParseResult<T4MappingResult>.Failure(errors);
        }

        var taxYear = slips!.First().Year;

        var mapResult = _provider.TryGetForYear(taxYear);
        if (mapResult.IsFailure)
        {
            return ParseResult<T4MappingResult>.Failure(mapResult.Errors);
        }

        var map = mapResult.Value!;
        var byLine = new Dictionary<string, LineAccumulator>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        var seenUnmapped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slip in slips)
        {
            foreach (var (boxCode, amount) in T4SlipBoxAmounts.Enumerate(slip))
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

        return ParseResult<T4MappingResult>.Success(new T4MappingResult(entries, unmapped));
    }

    // Fail-closed on a null or empty slip set, and on slips spanning more than one
    // tax year. No currency check: T4 amounts are CAD by definition.
    private static List<ValidationError> Validate(IReadOnlyCollection<T4Slip>? slips)
    {
        var errors = new List<ValidationError>();

        if (slips is null || slips.Count == 0)
        {
            errors.Add(new ValidationError("Slips", "At least one T4 slip is required."));
            return errors; // nothing else to check
        }

        var years = slips.Select(s => s.Year).Distinct().OrderBy(y => y).ToList();
        if (years.Count > 1)
        {
            errors.Add(new ValidationError("Year",
                $"All slips must be for one tax year; got {string.Join(", ", years)}."));
        }

        return errors;
    }

    private sealed class LineAccumulator
    {
        public decimal Amount;
        public List<string> SourceBoxes { get; } = new();
    }
}
