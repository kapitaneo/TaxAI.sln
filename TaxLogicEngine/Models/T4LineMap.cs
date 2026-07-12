using System.Reflection;

namespace TaxLogicEngine.Models;

/// <summary>
/// The resolved, validated box→line lookup for a single tax year, built from a
/// <see cref="T4LineMappingConfig"/>. Construction is fail-closed: a config with a
/// duplicate box code, an empty line list, a blank code/line, or an <b>unknown box
/// code</b> (not a standard <see cref="T4BoxCodes"/> box nor a
/// <see cref="T4OtherInformationCodes"/> code) yields
/// <see cref="ParseResult{T}.Failure"/> rather than a half-built map (FR6–FR8).
/// Mirrors <c>T5LineMap</c>, plus the unknown-code check that keeps a config typo
/// from silently misrouting real dollars.
/// </summary>
public sealed class T4LineMap
{
    // Every valid T4 box code: the standard boxes (read once from T4BoxCodes so the
    // list is never duplicated as literals) plus the 2025 "Other information" codes.
    private static readonly IReadOnlySet<string> RecognizedBoxCodes = BuildRecognizedBoxCodes();

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _linesByBox;

    private T4LineMap(int taxYear, IReadOnlyDictionary<string, IReadOnlyList<string>> linesByBox)
    {
        TaxYear = taxYear;
        _linesByBox = linesByBox;
    }

    /// <summary>The tax year this map applies to.</summary>
    public int TaxYear { get; }

    /// <summary>The box codes this map knows about.</summary>
    public IReadOnlyCollection<string> BoxCodes => (IReadOnlyCollection<string>)_linesByBox.Keys;

    /// <summary>Builds a validated map from config, or returns the config errors.</summary>
    public static ParseResult<T4LineMap> Create(T4LineMappingConfig config)
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

            if (!RecognizedBoxCodes.Contains(mapping.BoxCode))
            {
                errors.Add(new ValidationError($"Box {mapping.BoxCode}",
                    "is not a recognized T4 box or 2025 \"Other information\" code."));
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
            ? ParseResult<T4LineMap>.Failure(errors)
            : ParseResult<T4LineMap>.Success(new T4LineMap(config.TaxYear, linesByBox));
    }

    /// <summary>The CRA lines a box maps to, or null when the box has no mapping.</summary>
    public IReadOnlyList<string>? LinesFor(string boxCode) =>
        _linesByBox.TryGetValue(boxCode, out var lines) ? lines : null;

    private static IReadOnlySet<string> BuildRecognizedBoxCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in typeof(T4BoxCodes)
                     .GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string)))
        {
            codes.Add((string)field.GetValue(null)!);
        }

        codes.UnionWith(T4OtherInformationCodes.All);
        return codes;
    }
}
