using Microsoft.EntityFrameworkCore;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Repositories;

/// <summary>
/// The relational implementation of the immutable review-record port.
/// <para>
/// Every query is scoped by the trusted firm. Saving accepts only the completed
/// <see cref="PersistedReviewRecord"/> projection, then assigns the next version
/// and inserts the complete graph atomically. There is no update, upsert, or
/// delete path.
/// </para>
/// </summary>
internal sealed class SqlReviewRecordRepository : IReviewRecordRepository
{
    private const string SaveRefusedMessage =
        "The review record could not be saved.";
    private const string FirmMismatchMessage =
        "The review record does not belong to the trusted firm.";

    private readonly TaxAiDbContext _context;

    public SqlReviewRecordRepository(TaxAiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm,
        PersistedReviewRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(record);

        if (record.FirmId != firm.Value)
        {
            return ParseResult<PersistedReviewRecord>.Failure(
                new ValidationError(
                    TenancyFields.FirmId,
                    FirmMismatchMessage));
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingMaximum = await _context
                .Set<PersistedReviewRecord>()
                .Where(candidate =>
                    candidate.FirmId == firm.Value &&
                    candidate.CaseId == record.CaseId &&
                    candidate.ParticipantId == record.ParticipantId &&
                    candidate.TaxYear == record.TaxYear)
                .Select(candidate => (int?)candidate.PackageVersion)
                .MaxAsync(cancellationToken)
                ?? 0;

            var nextVersion = checked(existingMaximum + 1);
            _context.Entry(record)
                .Property(record => record.PackageVersion)
                .CurrentValue = nextVersion;

            _context.Add(record);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ParseResult<PersistedReviewRecord>.Success(record);
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
