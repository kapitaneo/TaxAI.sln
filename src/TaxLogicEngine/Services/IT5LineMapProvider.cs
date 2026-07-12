using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Resolves the box→CRA line map for a given tax year. Abstracted so the mapper
/// stays free of file/resource I/O and tests can supply an in-memory map.
/// </summary>
public interface IT5LineMapProvider
{
    /// <summary>
    /// Returns the validated <see cref="T5LineMap"/> for <paramref name="taxYear"/>,
    /// or a fail-closed error when no config exists for that year or it is malformed.
    /// </summary>
    ParseResult<T5LineMap> TryGetForYear(int taxYear);
}
