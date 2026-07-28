using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// The privileged retention purge.
/// <para>
/// Deleting a taxpayer's record early destroys a record they may need; keeping
/// it past its policy is an over-retention. Both are domain errors, so the
/// operation deletes exactly the records whose exclusive retention boundary has
/// been reached or passed, and nothing else.
/// </para>
/// <para>
/// It requires an explicit trusted firm scope with no firm-omitting overload,
/// and it is invoked explicitly — never by a timer, hosted service, or
/// scheduler. Scheduling it belongs to a later deployment specification; the
/// behaviour belongs here.
/// </para>
/// <para>
/// No normal portal user can reach this. In this feature there is no endpoint,
/// no reachable repository member, and no cascade that deletes a persisted
/// review record.
/// </para>
/// </summary>
public interface IReviewRecordPurgeService
{
    Task<PurgeOutcome> PurgeAsync(
        TrustedFirmId firm,
        DateTimeOffset evaluationInstantUtc,
        CancellationToken cancellationToken);
}
