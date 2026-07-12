using System.Runtime.CompilerServices;
using System.Text.Json;
using TaxLogicEngine.Models;

// The malformed-JSON and declared-year-mismatch fail-closed branches are only
// reachable with a hand-built stream, not the single well-formed embedded 2025
// config; expose the parse seam to the test assembly so those paths are covered.
[assembly: InternalsVisibleTo("TaxLogicEngine.Tests")]

namespace TaxLogicEngine.Services;

/// <summary>
/// Loads per-tax-year box→line config from JSON embedded in this assembly
/// (<c>Mappings/T4LineMap.{year}.json</c>). A missing year, malformed JSON, or a
/// config whose declared year does not match, all fail closed. Mirrors
/// <c>EmbeddedT5LineMapProvider</c>.
/// </summary>
public sealed class EmbeddedT4LineMapProvider : IT4LineMapProvider
{
    private const string ResourcePrefix = "TaxLogicEngine.Mappings.T4LineMap.";
    private const string ResourceSuffix = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ParseResult<T4LineMap> TryGetForYear(int taxYear)
    {
        var resourceName = $"{ResourcePrefix}{taxYear}{ResourceSuffix}";
        var assembly = typeof(EmbeddedT4LineMapProvider).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return ParseResult<T4LineMap>.Failure(
                new ValidationError("TaxYear", $"No T4 line-mapping config for tax year {taxYear}."));
        }

        return ParseConfig(stream, taxYear);
    }

    /// <summary>
    /// Deserializes and validates a config stream for <paramref name="taxYear"/>,
    /// failing closed on malformed JSON, an empty payload, or a declared-year
    /// mismatch before delegating to <see cref="T4LineMap.Create"/>. Internal so the
    /// tests can drive the malformed/mismatch paths that no shipped resource hits.
    /// </summary>
    internal static ParseResult<T4LineMap> ParseConfig(Stream stream, int taxYear)
    {
        T4LineMappingConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<T4LineMappingConfig>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ParseResult<T4LineMap>.Failure(
                new ValidationError("Config", $"T4 line-mapping config for {taxYear} is malformed: {ex.Message}"));
        }

        if (config is null)
        {
            return ParseResult<T4LineMap>.Failure(
                new ValidationError("Config", $"T4 line-mapping config for {taxYear} is empty."));
        }

        if (config.TaxYear != taxYear)
        {
            return ParseResult<T4LineMap>.Failure(
                new ValidationError("TaxYear", $"Config for tax year {taxYear} declares {config.TaxYear}."));
        }

        return T4LineMap.Create(config);
    }
}
