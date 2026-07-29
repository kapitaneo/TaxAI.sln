using CaseReview.Models;
using Microsoft.EntityFrameworkCore;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Repositories;

/// <summary>
/// The relational implementation of the immutable review-record port.
/// <para>
/// Every query is scoped by the trusted firm. Saving projects through the
/// closed allowlist before opening a transaction, then assigns the next version
/// and inserts the complete graph atomically. There is no update, upsert, or
/// delete path.
/// </para>
/// </summary>
internal sealed class SqlReviewRecordRepository : IReviewRecordRepository
{
    private const string SaveRefusedMessage =
        "The review record could not be saved.";

    private readonly TaxAiDbContext _context;
    private readonly IOpaqueIdentifierSource _identifiers;

    public SqlReviewRecordRepository(
        TaxAiDbContext context,
        IOpaqueIdentifierSource identifiers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(identifiers);

        _context = context;
        _identifiers = identifiers;
    }

    public async Task<ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm,
        AccountantReviewPackage package,
        DateTimeOffset evaluationInstantUtc,
        CancellationToken cancellationToken)
    {
        var projection = ReviewPackageProjection.Project(
            firm,
            package,
            evaluationInstantUtc,
            _identifiers);

        if (projection.IsFailure)
        {
            return projection;
        }

        var projected = projection.Value!;

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingMaximum = await _context
                .Set<PersistedReviewRecord>()
                .Where(record =>
                    record.FirmId == firm.Value &&
                    record.CaseId == projected.CaseId &&
                    record.ParticipantId == projected.ParticipantId &&
                    record.TaxYear == projected.TaxYear)
                .Select(record => (int?)record.PackageVersion)
                .MaxAsync(cancellationToken)
                ?? 0;

            var nextVersion = checked(existingMaximum + 1);
            _context.Entry(projected)
                .Property(record => record.PackageVersion)
                .CurrentValue = nextVersion;

            _context.Add(projected);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ParseResult<PersistedReviewRecord>.Success(projected);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();

            return ParseResult<PersistedReviewRecord>.Failure(
                new ValidationError(
                    TenancyFields.Package,
                    SaveRefusedMessage));
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();

            return ParseResult<PersistedReviewRecord>.Failure(
                new ValidationError(
                    TenancyFields.Package,
                    SaveRefusedMessage));
        }
    }

    public Task<PersistedReviewRecord?> FindAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken) =>
        _context
            .Set<PersistedReviewRecord>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(record => record.Slips.OrderBy(slip => slip.Ordinal))
                .ThenInclude(slip => slip.BoxAmounts.OrderBy(amount => amount.Ordinal))
            .Include(record => record.Slips.OrderBy(slip => slip.Ordinal))
                .ThenInclude(slip => slip.LineEntries.OrderBy(entry => entry.Ordinal))
                    .ThenInclude(entry =>
                        entry.SourceBoxCodes.OrderBy(code => code.Ordinal))
            .Include(record => record.Slips.OrderBy(slip => slip.Ordinal))
                .ThenInclude(slip =>
                    slip.UnmappedBoxCodes.OrderBy(code => code.Ordinal))
            .SingleOrDefaultAsync(
                record =>
                    record.FirmId == firm.Value &&
                    record.RecordId == recordId,
                cancellationToken);

    /// <summary>Fixed type identity only; never database state or record data.</summary>
    public override string ToString() => nameof(SqlReviewRecordRepository);
}
