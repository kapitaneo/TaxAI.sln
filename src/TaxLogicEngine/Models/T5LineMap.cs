namespace TaxLogicEngine.Models;

/// <summary>
/// The resolved, validated box→line lookup for a single tax year, built from a
/// <see cref="T5LineMappingConfig"/>. Construction is fail-closed: a config with a
/// duplicate box code, an empty line list, or a blank code/line yields
/// <see cref="ParseResult{T}.Failure"/> rather than a half-built map.
/// </summary>
public sealed class T5LineMap
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _linesByBox;

    private T5LineMap(int taxYear, IReadOnlyDictionary<string, IReadOnlyList<string>> linesByBox)
    {
        TaxYear = taxYear;
        _linesByBox = linesByBox;
    }

    /// <summary>The tax year this map applies to.</summary>
    public int TaxYear { get; }

    /// <summary>The box codes this map knows about.</summary>
    public IReadOnlyCollection<string> BoxCodes => (IReadOnlyCollection<string>)_linesByBox.Keys;

    /// <summary>Builds a validated map from config, or returns the config errors.</summary>
    public static ParseResult<T5LineMap> Create(T5LineMappingConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();
        var linesByBox = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var mapping in config.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.BoxCode))
            {
                errors.Add(new ValidationError("BoxCode", "A mapping box code must not be empty."));
                continue;
            }

            if (mapping.LineNumbers is null || mapping.LineNumbers.Count == 0)
            {
                errors.Add(new ValidationError($"Box {mapping.BoxCode}", "must map to at least one CRA line."));
                continue;
            }

            if (mapping.LineNumbers.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add(new ValidationError($"Box {mapping.BoxCode}", "has a blank CRA line number."));
                continue;
            }

            if (!linesByBox.TryAdd(mapping.BoxCode, mapping.LineNumbers.ToArray()))
            {
                errors.Add(new ValidationError($"Box {mapping.BoxCode}", "is mapped more than once."));
            }
        }

        return errors.Count > 0
            ? ParseResult<T5LineMap>.Failure(errors)
            : ParseResult<T5LineMap>.Success(new T5LineMap(config.TaxYear, linesByBox));
    }

    /// <summary>The CRA lines a box maps to, or null when the box has no mapping.</summary>
    public IReadOnlyList<string>? LinesFor(string boxCode) =>
        _linesByBox.TryGetValue(boxCode, out var lines) ? lines : null;
}
