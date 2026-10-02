using ClientIntake.Models;
using Tenancy.Models;

namespace ClientIntake.Contracts;

/// <summary>
/// The only way to intake metadata.
/// <para>
/// Concrete and named for its aggregate. There is deliberately no generic
/// repository, no exposed queryable, no unscoped case lookup, no raw credential
/// store, and no raw-content read member: a <c>Save&lt;T&gt;</c> is a save nobody
/// can constrain to a tenant, and an exposed queryable hands the caller the
/// ability to write its own unscoped query.
/// </para>
/// <para>
/// There is no update or delete of immutable history. Answers, documents,
/// submissions, associations, and audit events are appended; current state is
/// projected from them.
/// </para>
/// <para>
/// <see cref="FindLinkScopeAsync"/> and <see cref="FindClientScopeAsync"/> are
/// the <b>only</b> firm-omitting members in this contract. Each takes a keyed
/// digest and <i>returns</i> a trusted scope rather than accepting one, so
/// neither can be used to enumerate. Every other member requires either a
/// <see cref="TrustedFirmId"/> or a scope one of those two produced.
/// </para>
/// </summary>
public interface IIntakeStore
{
    /// <summary>
    /// Commits the case, its expected items, its first link, and its opening
    /// audit events in one transaction.
    /// </summary>
    Task<IntakeStoreWriteCode> CreateAsync(
        NewIntakeCase intakeCase,
        CancellationToken cancellationToken);

    /// <summary>
    /// Looks a bootstrap link up by digest. Returns <c>null</c> for unknown,
    /// expired, spent, and revoked links alike.
    /// </summary>
    Task<IntakeLinkScope?> FindLinkScopeAsync(
        IntakeTokenDigest linkDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Conditionally spends one still-active link and creates the session derived
    /// from it, in a single transaction. A concurrent second redemption of the
    /// same link loses and gets <see cref="IntakeStoreWriteCode.NotFound"/>.
    /// </summary>
    Task<IntakeStoreWriteCode> RedeemLinkAsync(
        IntakeLinkScope linkScope,
        NewIntakeSession session,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Looks a client session up by digest, returning only trusted scope.
    /// </summary>
    Task<IntakeClientScope?> FindClientScopeAsync(
        IntakeTokenDigest sessionDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Reads one case scoped to a trusted firm.</summary>
    Task<StoredIntakeCase?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the cases of exactly one trusted firm, newest first by the stored
    /// case-created instant, bounded to <paramref name="maximumCases"/>. The
    /// firm is the only scope: there is no search, filter, or paging argument,
    /// and no overload that omits the firm.
    /// </summary>
    Task<IntakeCaseList> ListCasesAsync(
        TrustedFirmId firm,
        int maximumCases,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the case a client scope names, revalidating that the same session is
    /// still live in the same database operation.
    /// </summary>
    Task<StoredIntakeCase?> FindForClientAsync(
        IntakeClientScope scope,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically revokes every active link and derived session for the case and
    /// inserts the replacement, retaining every prior row.
    /// </summary>
    Task<IntakeStoreWriteCode> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeLinkRecord replacement,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken);

    /// <summary>The same invalidation, with no replacement.</summary>
    Task<IntakeStoreWriteCode> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        DateTimeOffset revokedAtUtc,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends answer versions under the live session and concurrency guard, and
    /// advances the supplied idle boundary.
    /// </summary>
    Task<IntakeStoreWriteCode> AppendAnswersAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IReadOnlyList<IntakeAnswerVersionRecord> answers,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends the attempt fact and, when the upload was accepted, its document
    /// metadata. The document is optional because a rejected or duplicate attempt
    /// is still recorded.
    /// </summary>
    Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeUploadAttemptRecord attempt,
        IntakeDocumentRecord? document,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends one immutable submission under a unique version constraint. A
    /// concurrent next-version winner leaves the loser with no partial snapshot.
    /// </summary>
    Task<IntakeStoreWriteCode> AppendSubmissionAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeSubmissionRecord submission,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends the one immutable review association for the case. Replacement is
    /// refused by the store, not merely by the caller.
    /// </summary>
    Task<IntakeStoreWriteCode> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeReviewAssociationRecord association,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken);
}
