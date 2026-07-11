using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Flattens a <see cref="T4Slip"/> into the (box code, amount) pairs the mapper
/// works with — the single place that knows box code → typed property. Yields the
/// amount boxes whose value is non-zero (absent boxes default to 0 and are skipped,
/// so they are not reported as unmapped), plus every "Other information" entry
/// verbatim. Non-amount boxes (Box 12 SIN, 54, 50, 10, 29, 45, the 28 exempt
/// indicators) are never enumerated, so the SIN never enters the pipeline. Mirrors
/// <c>T5SlipBoxAmounts</c>.
/// <para>
/// Wave 0 contract stage: <see cref="Enumerate"/> is not yet implemented.
/// </para>
/// </summary>
public static class T4SlipBoxAmounts
{
    /// <summary>Yields the slip's populated box amounts as (code, amount) pairs.</summary>
    public static IEnumerable<(string BoxCode, decimal Amount)> Enumerate(T4Slip slip) =>
        throw new NotImplementedException();
}
