using TaxLogicEngine.Models;
using Tenancy.Models;

namespace Tenancy.Services;

/// <summary>
/// Refuses any instant that is not an unambiguous UTC instant.
/// <para>
/// It refuses rather than converts, deliberately. Silently converting a local
/// instant to UTC turns a caller's mistake into a stored fact that looks
/// correct, and a retention boundary computed from a misread instant deletes a
/// taxpayer's record on the wrong day.
/// </para>
/// <para>
/// <see cref="DateTimeOffset"/> does not retain the originating
/// <see cref="DateTime.Kind"/>. At this boundary, an offset of zero is therefore
/// the enforceable UTC representation.
/// </para>
/// </summary>
public static class UtcInstantGuard
{
    /// <summary>
    /// Returns null when the instant carries a zero offset, and a fixed
    /// validation error otherwise. Never coerces, never converts, never assumes.
    /// </summary>
    public static ValidationError? Validate(DateTimeOffset instant, string field) =>
        instant.Offset == TimeSpan.Zero
            ? null
            : new ValidationError(field, TenancyErrors.InstantNotUtc);
}
