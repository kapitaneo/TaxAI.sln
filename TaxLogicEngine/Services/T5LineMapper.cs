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
/// Fail-closed: the slips are validated first (non-empty, all one tax year, every
/// slip in CAD, and a config exists for the year); on any problem the mapper returns
/// errors and no result. Error messages reference slips/year/currency only — never
/// the recipient identification number.
/// </para>
/// </summary>
public sealed class T5LineMapper
{
    private const string CanadianCurrency = "CAD";

    private readonly IT5LineMapProvider _provider;

    public T5LineMapper(IT5LineMapProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>Maps the slips to aggregated CRA line entries, or returns errors.</summary>
    public ParseResult<T5MappingResult> Map(IReadOnlyCollection<T5Slip> slips)
    {
        ArgumentNullException.ThrowIfNull(slips);

        var errors = Validate(slips);
        if (errors.Count > 0)
        {
            return ParseResult<T5MappingResult>.Failure(errors);
        }

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

    private static List<ValidationError> Validate(IReadOnlyCollection<T5Slip> slips)
    {
        var errors = new List<ValidationError>();

        if (slips.Count == 0)
        {
            errors.Add(new ValidationError("Slips", "At least one T5 slip is required."));
            return errors; // nothing else to check
        }

        var years = slips.Select(s => s.TaxYear).Distinct().OrderBy(y => y).ToList();
        if (years.Count > 1)
        {
            errors.Add(new ValidationError("TaxYear",
                $"All slips must be for one tax year; got {string.Join(", ", years)}."));
        }

        foreach (var currency in slips
                     .Select(s => s.CurrencyCode)
                     .Where(c => !string.Equals(c, CanadianCurrency, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError("CurrencyCode",
                $"Slip amounts must be in {CanadianCurrency}; got {currency}. Foreign currency conversion is out of scope."));
        }

        return errors;
    }

    private sealed class LineAccumulator
    {
        public decimal Amount;
        public List<string> SourceBoxes { get; } = new();
    }
}
