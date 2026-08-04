using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Services;

/// <summary>
/// The fail-closed checks the ingest workflow runs before it will exist, and the
/// argument checks it runs before it will do anything.
/// <para>
/// Configuration is validated at construction rather than at first use, so a
/// host wired with a missing or nonsensical value refuses at composition time
/// instead of on some later accountant's request. Nothing here defaults a
/// missing value: a bound nobody chose is a bound nobody reviewed.
/// </para>
/// <para>
/// Internal on purpose. These are this module's own guards, not a vocabulary
/// anyone outside it should be coding against; the behaviour they protect is
/// observable through the workflow's public contract.
/// </para>
/// </summary>
internal static class ExtractionWorkflowOptionsGuard
{
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A configured value is missing or invalid.</exception>
    internal static ExtractionWorkflowOptions RequireValid(ExtractionWorkflowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ProviderCallLimit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        return options;
    }

    /// <summary>
    /// The one UTC rule this module applies to a supplied instant. A caller that
    /// hands over a local time is handing over an instant whose meaning depends
    /// on where the host happens to run.
    /// </summary>
    internal static bool IsUtc(DateTimeOffset instant) => instant.Offset == TimeSpan.Zero;

    /// <summary>
    /// Whether the scope arguments every firm-scoped read shares are present.
    /// The firm is a <c>TrustedFirmId</c> and therefore cannot have come from a
    /// route, query, form, header, or cookie without a visible conversion; the
    /// selectors are checked for presence only, and are verified against
    /// server-stored state by the caller before anything is read.
    /// </summary>
    internal static bool ValidScope(Tenancy.Models.TrustedFirmId? firm, Guid caseId) =>
        firm is not null && caseId != Guid.Empty;
}
