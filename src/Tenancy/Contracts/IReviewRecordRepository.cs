using CaseReview.Models;
using TaxLogicEngine.Models;
using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// The only way to a persisted review record.
/// <para>
/// Concrete and named for its aggregate. There is deliberately no generic
/// repository abstraction, no generic save, and no exposed queryable: a
/// <c>Save&lt;T&gt;</c> is a save nobody can constrain to a tenant, and an
/// exposed queryable hands the caller the ability to write its own unscoped
/// query.
/// </para>
/// <para>
/// Both members require a trusted firm. Neither has an overload, optional
/// argument, or default that omits it — there is no firm-omitting path to find.
/// </para>
/// <para>
/// There is no update, no upsert, and no delete member. A persisted record is
/// immutable, and saving again for the same subject key creates a new version
/// rather than rewriting one. A mutation an HTTP request could reach is a
/// mutation that will eventually be reached.
/// </para>
/// </summary>
public interface IReviewRecordRepository
{
    /// <summary>
    /// Projects the package through the fail-closed allowlist and writes it as
    /// the next version for its subject key. Nothing is written unless every
    /// guard passes, and the <strong>trusted</strong> firm is stored regardless
    /// of the firm the package carried.
    /// </summary>
    Task<ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm,
        AccountantReviewPackage package,
        DateTimeOffset evaluationInstantUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one record scoped to the trusted firm.
    /// <para>
    /// A record that does not exist and a record owned by another firm produce
    /// the <strong>same</strong> result, with no identifier echoed and nothing
    /// to distinguish them. A caller must not be able to learn that a record
    /// exists by being refused differently.
    /// </para>
    /// </summary>
    Task<PersistedReviewRecord?> FindAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken);
}
