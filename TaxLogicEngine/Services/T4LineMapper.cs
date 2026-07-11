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
/// <para>
/// Wave 0 contract stage: the signature is fixed; <see cref="Map"/> is not yet
/// implemented.
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
    public ParseResult<T4MappingResult> Map(IReadOnlyCollection<T4Slip> slips) =>
        throw new NotImplementedException();
}
