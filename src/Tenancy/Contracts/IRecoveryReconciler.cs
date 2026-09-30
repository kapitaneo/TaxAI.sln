using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// The recovered application database's side of recovery reconciliation
/// (spec 016 T06, FR-10, FR-11). It is implemented by the module that owns the
/// schema and is invoked only by the host's recovery orchestrator while the
/// environment is quarantined — never by an HTTP request.
/// <para>
/// Every replay is scoped by the trusted firm named in an acknowledged ledger
/// fact and re-applies only a destruction that was already decided before the
/// restore; nothing here authorizes a new or early purge. The two
/// environment-wide members only <i>remove</i> access in the identity carve-out
/// and intake credential tables: they read no taxpayer data, return counts
/// only, and are not lookups, so they add no firm-scope exemption.
/// </para>
/// </summary>
public interface IRecoveryReconciler
{
    /// <summary>
    /// The physical incarnation of the database this host is connected to, or
    /// <see langword="null"/> when it cannot be established.
    /// </summary>
    Task<DatabaseIncarnation?> ReadIncarnationAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether the database holds any taxpayer data or session, and the newest
    /// recorded activity instant — the lower bound of its restore point.
    /// </summary>
    Task<RecoveredDataExtent> ReadDataExtentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Re-fences one source object whose terminal destruction the ledger
    /// records: a resurrected registration can no longer be read.
    /// </summary>
    Task<RecoveryReplayOutcome> ReplayTerminalSourceAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        DateTimeOffset replayedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-deletes one review record whose terminal destruction the ledger
    /// records. A record other retained history still references is
    /// <see cref="RecoveryReplayOutcome.Blocked"/>, never cascaded.
    /// </summary>
    Task<RecoveryReplayOutcome> ReplayTerminalStructuredAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every authentication session, client intake session and unspent
    /// intake link issued before <paramref name="recoveryStartedAtUtc"/>.
    /// Expired credentials stay expired: no expiry is changed and nothing is
    /// re-issued, so no revocation lost by the restore can be undone.
    /// </summary>
    Task<RecoveryAccessInvalidation> InvalidatePreRecoveryAccessAsync(
        DateTimeOffset recoveryStartedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deactivates every active membership the current attestation does not
    /// list. It never creates or reactivates a membership; attested memberships
    /// the restore lost are counted and must be provisioned again.
    /// </summary>
    Task<RecoveryMembershipReconciliation> ApplyAuthorizationAttestationAsync(
        RecoveryAuthorizationAttestation attestation,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken);
}
