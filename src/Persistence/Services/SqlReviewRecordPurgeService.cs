using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Services;

/// <summary>
/// The explicit, privileged retention purge for persisted review records.
/// <para>
/// Candidate discovery is always scoped by the independently trusted firm and
/// materializes only opaque record identifiers, retention instants, and
/// structural slip counts. No taxpayer content enters the purge or its
/// diagnostics.
/// </para>
/// </summary>
internal sealed class SqlReviewRecordPurgeService : IReviewRecordPurgeService
{
    private static readonly Action<ILogger, int, int, Guid, Exception?>
        PurgeCompleted = LoggerMessage.Define<int, int, Guid>(
            LogLevel.Information,
            new EventId(15, nameof(PurgeAsync)),
            "Purged {RecordsDeleted} review records and {SlipsDeleted} slips for firm {FirmId}.");

    private readonly TaxAiDbContext _context;
    private readonly ILogger<SqlReviewRecordPurgeService> _logger;

    public SqlReviewRecordPurgeService(
        TaxAiDbContext context,
        ILogger<SqlReviewRecordPurgeService> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _logger = logger;
    }

    public async Task<PurgeOutcome> PurgeAsync(
        TrustedFirmId firm,
        DateTimeOffset evaluationInstantUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);

        if (UtcInstantGuard.Validate(
                evaluationInstantUtc,
                TenancyFields.EvaluationInstant) is not null)
        {
            throw new ArgumentException(
                "The purge evaluation instant must have a zero UTC offset.",
                nameof(evaluationInstantUtc));
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var candidates = await _context.Set<PersistedReviewRecord>()
            .AsNoTracking()
            .Where(record => record.FirmId == firm.Value)
            .Select(record => new
            {
                record.RecordId,
                record.RetentionUntilUtc,
                SlipsDeleted = record.Slips.Count,
            })
            .ToArrayAsync(cancellationToken);

        var eligible = candidates
            .Where(candidate => RetentionPolicy.IsPurgeEligible(
                candidate.RetentionUntilUtc,
                evaluationInstantUtc))
            .ToArray();

        if (eligible.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            PurgeCompleted(_logger, 0, 0, firm.Value, null);
            return PurgeOutcome.NoEligibleRecords();
        }

        var eligibleIds = eligible
            .Select(candidate => candidate.RecordId)
            .ToArray();
        var slipsDeleted = checked(eligible.Sum(candidate => candidate.SlipsDeleted));

        var recordsDeleted = await _context.Set<PersistedReviewRecord>()
            .Where(record =>
                record.FirmId == firm.Value &&
                eligibleIds.Contains(record.RecordId))
            .ExecuteDeleteAsync(cancellationToken);

        if (recordsDeleted != eligible.Length)
        {
            throw new DbUpdateConcurrencyException(
                "The eligible review-record set changed during the purge.");
        }

        await transaction.CommitAsync(cancellationToken);

        PurgeCompleted(
            _logger,
            recordsDeleted,
            slipsDeleted,
            firm.Value,
            null);

        return PurgeOutcome.RecordsPurged(recordsDeleted, slipsDeleted);
    }

    /// <summary>Fixed type identity only; never context or tenant state.</summary>
    public override string ToString() => nameof(SqlReviewRecordPurgeService);
}
