using ClientIntake.Models;
using Tenancy.Models;

namespace ClientIntake.Contracts;

/// <summary>
/// The source registry (spec 016 T04): the durable record of every stored source
/// object's ownership, staging, binding and terminal state.
/// <para>
/// It exists so that no stored object is ever untracked. A production content
/// store registers the object <b>before</b> its storage allocation becomes
/// reachable; the intake metadata commit binds it to the committed document in
/// the same transaction; compensating cleanup of an object that never became
/// reachable metadata records its intent and verified outcome here, so an
/// interrupted cleanup is replayed rather than forgotten.
/// </para>
/// <para>
/// Every member requires a <see cref="TrustedFirmId"/> and an explicit case;
/// there is no firm-omitting overload, and a foreign-firm lookup is
/// indistinguishable from an absent one. The registry holds no source byte, key
/// material, file name or taxpayer value. It is not a lifecycle-deadline or due
/// purge authority: a <see cref="SourceRegistryState.Bound"/> object is never
/// cleaned up through this contract.
/// </para>
/// </summary>
public interface ISourceRegistryStore
{
    /// <summary>
    /// Records a newly allocated, not-yet-reachable object. A resource identifier
    /// or content key already known for the case is a conflict: an identifier is
    /// never reused, so a purged object is never resurrected under it.
    /// </summary>
    Task<SourceRegistryWriteCode> RegisterStagedAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        string contentKey,
        DateTimeOffset stagedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>Reads the registration for one content key in the trusted case.</summary>
    Task<SourceRegistration?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the registration only when it is <see cref="SourceRegistryState.Bound"/>
    /// to exactly this committed document with exactly this content key. Every
    /// mismatch, an unbound object and a purged one all return null.
    /// </summary>
    Task<SourceRegistration?> FindBoundAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a compensating-cleanup deletion intent or outcome for an object
    /// that was never bound. <see cref="SourceObjectPurgeOutcome.Complete"/> moves
    /// it to <see cref="SourceRegistryState.Purged"/>; any other outcome leaves it
    /// <see cref="SourceRegistryState.PurgePending"/> for replay. A bound object is
    /// a conflict, and a purged one never moves back.
    /// </summary>
    Task<SourceRegistryWriteCode> RecordPurgeAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        SourceObjectPurgeOutcome outcome,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists, for one trusted firm, a bounded page of objects that never became
    /// reachable: pending cleanups, and staged objects older than the supplied
    /// instant. This is the orphan and recovery-replay work list; it never returns
    /// a bound or purged object.
    /// </summary>
    Task<IReadOnlyList<SourceRegistration>> ListUncommittedAsync(
        TrustedFirmId firm,
        DateTimeOffset stagedBeforeUtc,
        int maximumCount,
        CancellationToken cancellationToken);
}

/// <summary>
/// Physically deletes one registered source object and reports only what it
/// could observe (spec 016 T04).
/// <para>
/// The caller has already established the object's trusted scope through the
/// registry; the purger additionally refuses an object whose stored envelope
/// names another scope. It never reports success it did not observe: an object
/// whose replicas cannot be observed absent is
/// <see cref="SourceObjectPurgeOutcome.Pending"/>, not complete.
/// </para>
/// </summary>
public interface ISourceObjectPurger
{
    Task<SourceObjectPurgeOutcome> PurgeAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        CancellationToken cancellationToken);
}
