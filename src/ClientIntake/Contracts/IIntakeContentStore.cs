using ClientIntake.Models;
using Tenancy.Models;

namespace ClientIntake.Contracts;

/// <summary>
/// The one narrow boundary opaque source bytes cross.
/// <para>
/// This is the only place in the system where a byte stream may contain a SIN,
/// because a real T4 image does. It must never be described as SIN-free. That is
/// exactly why the contract exposes <b>no read member</b>: there is no way to get
/// the content back out, no URL that serves it, and no EF mapping that could
/// bring it into a query or a diagnostic.
/// </para>
/// <para>
/// The content never enters the relational model. The store returns only an
/// opaque key, the byte count it actually observed, and a scoped keyed
/// fingerprint — the three values the SIN-free metadata tables hold.
/// </para>
/// </summary>
public interface IIntakeContentStore
{
    /// <summary>
    /// Streams one upload to storage outside the web root, encrypting as it goes,
    /// and computes the whole-file fingerprint while streaming.
    /// <para>
    /// <paramref name="maximumBytes"/> is enforced by the adapter as well as by
    /// the endpoint: a size limit checked in one place is a size limit that stops
    /// being checked when a second caller appears. The firm and case are
    /// server-derived and participate in key derivation, so content staged for one
    /// scope cannot be read or fingerprint-matched under another.
    /// </para>
    /// </summary>
    Task<StagedIntakeContent> StageAsync(
        TrustedFirmId firm,
        Guid caseId,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a staged object that never became reachable metadata.
    /// <para>
    /// This exists for one purpose: compensating cleanup after a duplicate,
    /// conflict, or failure on the metadata commit. It is never exposed as a user
    /// route, and there is no user-reachable delete path for accepted content.
    /// </para>
    /// </summary>
    Task DeleteIfExistsAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        CancellationToken cancellationToken);
}
