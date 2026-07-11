using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Loads per-tax-year box→line config from JSON embedded in this assembly
/// (<c>Mappings/T4LineMap.{year}.json</c>). A missing year, malformed JSON, or a
/// config whose declared year does not match, all fail closed. Mirrors
/// <c>EmbeddedT5LineMapProvider</c>.
/// <para>
/// Wave 0 contract stage: <see cref="TryGetForYear"/> is not yet implemented.
/// </para>
/// </summary>
public sealed class EmbeddedT4LineMapProvider : IT4LineMapProvider
{
    public ParseResult<T4LineMap> TryGetForYear(int taxYear) => throw new NotImplementedException();
}
