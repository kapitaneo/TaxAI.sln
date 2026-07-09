using System.Text.Json;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Loads per-tax-year box→line config from JSON embedded in this assembly
/// (<c>Mappings/T5LineMap.{year}.json</c>). A missing year, malformed JSON, or a
/// config whose declared year does not match, all fail closed.
/// </summary>
public sealed class EmbeddedT5LineMapProvider : IT5LineMapProvider
{
    private const string ResourcePrefix = "TaxLogicEngine.Mappings.T5LineMap.";
    private const string ResourceSuffix = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ParseResult<T5LineMap> TryGetForYear(int taxYear)
    {
        var resourceName = $"{ResourcePrefix}{taxYear}{ResourceSuffix}";
        var assembly = typeof(EmbeddedT5LineMapProvider).Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return ParseResult<T5LineMap>.Failure(
                new ValidationError("TaxYear", $"No T5 line-mapping config for tax year {taxYear}."));
        }

        T5LineMappingConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<T5LineMappingConfig>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ParseResult<T5LineMap>.Failure(
                new ValidationError("Config", $"T5 line-mapping config for {taxYear} is malformed: {ex.Message}"));
        }

        if (config is null)
        {
            return ParseResult<T5LineMap>.Failure(
                new ValidationError("Config", $"T5 line-mapping config for {taxYear} is empty."));
        }

        if (config.TaxYear != taxYear)
        {
            return ParseResult<T5LineMap>.Failure(
                new ValidationError("TaxYear", $"Config for tax year {taxYear} declares {config.TaxYear}."));
        }

        return T5LineMap.Create(config);
    }
}
