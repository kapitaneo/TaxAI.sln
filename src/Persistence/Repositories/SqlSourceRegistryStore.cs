using ClientIntake.Contracts;
using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Tenancy.Models;

namespace Persistence.Repositories;

/// <summary>
/// The relational source registry (spec 016 T04). Every operation is scoped by
/// the trusted firm and the explicit case; there is no firm-omitting query.
/// State changes are single conditional updates, so a compensating cleanup and a
/// metadata commit racing for the same staged object cannot both win: whichever
/// moves it out of <see cref="SourceRegistryState.Staged"/> first owns it.
/// </summary>
internal sealed class SqlSourceRegistryStore : ISourceRegistryStore
{
    private const int MaximumPageSize = 500;

    private readonly TaxAiDbContext _context;

    public SqlSourceRegistryStore(TaxAiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<SourceRegistryWriteCode> RegisterStagedAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        string contentKey,
        DateTimeOffset stagedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty ||
            resourceId == Guid.Empty ||
            !IsContentKey(contentKey) ||
            stagedAtUtc.Offset != TimeSpan.Zero)
        {
            return SourceRegistryWriteCode.Conflict;
        }

        var caseExists = await _context.Set<IntakeCaseEntity>()
            .AsNoTracking()
            .AnyAsync(
                intakeCase => intakeCase.FirmId == firm.Value && intakeCase.CaseId == caseId,
                cancellationToken);
        if (!caseExists)
        {
            return SourceRegistryWriteCode.NotFound;
        }

        _context.Add(new SourceRegistrationEntity
        {
            FirmId = firm.Value,
            CaseId = caseId,
            ResourceId = resourceId,
            ContentKey = contentKey,
            State = SourceRegistryState.Staged,
            StagedAtUtc = stagedAtUtc,
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return SourceRegistryWriteCode.Success;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            return SourceRegistryWriteCode.Conflict;
        }
    }

    public async Task<SourceRegistration?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || !IsContentKey(contentKey))
        {
            return null;
        }

        var entity = await _context.Set<SourceRegistrationEntity>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.FirmId == firm.Value &&
                    candidate.CaseId == caseId &&
                    candidate.ContentKey == contentKey,
                cancellationToken);

        return entity is null ? null : ToRegistration(entity);
    }

    public async Task<SourceRegistration?> FindBoundAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || documentId == Guid.Empty || !IsContentKey(contentKey))
        {
            return null;
        }

        // The committed intake document must still name the same key: the
        // registry binding and the document row are checked together, so neither
        // alone authorizes a read.
        var entity = await _context.Set<SourceRegistrationEntity>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.FirmId == firm.Value &&
                candidate.CaseId == caseId &&
                candidate.DocumentId == documentId &&
                candidate.ContentKey == contentKey &&
                candidate.State == SourceRegistryState.Bound &&
                _context.Set<IntakeDocumentEntity>().Any(document =>
                    document.FirmId == candidate.FirmId &&
                    document.CaseId == candidate.CaseId &&
                    document.DocumentId == candidate.DocumentId &&
                    document.ContentKey == candidate.ContentKey))
            .ToListAsync(cancellationToken);

        return entity.Count == 1 ? ToRegistration(entity[0]) : null;
    }

    public async Task<SourceRegistryWriteCode> RecordPurgeAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        SourceObjectPurgeOutcome outcome,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty ||
            resourceId == Guid.Empty ||
            !Enum.IsDefined(outcome) ||
            recordedAtUtc.Offset != TimeSpan.Zero)
        {
            return SourceRegistryWriteCode.Conflict;
        }

        var next = outcome == SourceObjectPurgeOutcome.Complete
            ? SourceRegistryState.Purged
            : SourceRegistryState.PurgePending;

        // Only an object that was never bound, and is not already purged, moves.
        var updated = await _context.Set<SourceRegistrationEntity>()
            .Where(candidate =>
                candidate.FirmId == firm.Value &&
                candidate.CaseId == caseId &&
                candidate.ResourceId == resourceId &&
                candidate.DocumentId == null &&
                (candidate.State == SourceRegistryState.Staged ||
                 candidate.State == SourceRegistryState.PurgePending))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.State, next)
                    .SetProperty(candidate => candidate.PurgeRecordedAtUtc, (DateTimeOffset?)recordedAtUtc),
                cancellationToken);
        if (updated == 1)
        {
            return SourceRegistryWriteCode.Success;
        }

        var exists = await _context.Set<SourceRegistrationEntity>()
            .AsNoTracking()
            .AnyAsync(
                candidate =>
                    candidate.FirmId == firm.Value &&
                    candidate.CaseId == caseId &&
                    candidate.ResourceId == resourceId,
                cancellationToken);

        return exists ? SourceRegistryWriteCode.Conflict : SourceRegistryWriteCode.NotFound;
    }

    public async Task<IReadOnlyList<SourceRegistration>> ListUncommittedAsync(
        TrustedFirmId firm,
        DateTimeOffset stagedBeforeUtc,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (maximumCount is <= 0 or > MaximumPageSize ||
            stagedBeforeUtc.Offset != TimeSpan.Zero)
        {
            return Array.Empty<SourceRegistration>();
        }

        var candidates = await _context.Set<SourceRegistrationEntity>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.FirmId == firm.Value &&
                candidate.DocumentId == null &&
                (candidate.State == SourceRegistryState.Staged ||
                 candidate.State == SourceRegistryState.PurgePending))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(candidate =>
                candidate.State == SourceRegistryState.PurgePending ||
                candidate.StagedAtUtc < stagedBeforeUtc)
            .OrderBy(candidate => candidate.StagedAtUtc)
            .ThenBy(candidate => candidate.ResourceId)
            .Take(maximumCount)
            .Select(ToRegistration)
            .ToArray();
    }

    /// <summary>
    /// Binds a staged object to the intake document committed in the caller's
    /// open transaction. Called only by <see cref="SqlIntakeStore"/> after the
    /// document row is saved and before the transaction commits.
    /// <para>
    /// Returns <see langword="true"/> when the object was bound, or when the
    /// content key has no registration at all (a content store that keeps no
    /// registry, such as the Development filesystem store). Returns
    /// <see langword="false"/> — and the caller rolls back — when a registration
    /// exists but is no longer staged: cleanup already took it, so committing
    /// metadata for it would make a deleted object reachable.
    /// </para>
    /// </summary>
    internal static async Task<bool> BindAsync(
        TaxAiDbContext context,
        Guid firmId,
        Guid caseId,
        Guid documentId,
        string contentKey,
        DateTimeOffset boundAtUtc,
        CancellationToken cancellationToken)
    {
        var updated = await context.Set<SourceRegistrationEntity>()
            .Where(candidate =>
                candidate.FirmId == firmId &&
                candidate.CaseId == caseId &&
                candidate.ContentKey == contentKey &&
                candidate.DocumentId == null &&
                candidate.State == SourceRegistryState.Staged)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.State, SourceRegistryState.Bound)
                    .SetProperty(candidate => candidate.DocumentId, (Guid?)documentId)
                    .SetProperty(candidate => candidate.BoundAtUtc, (DateTimeOffset?)boundAtUtc),
                cancellationToken);
        if (updated == 1)
        {
            return true;
        }

        return !await context.Set<SourceRegistrationEntity>()
            .AsNoTracking()
            .AnyAsync(
                candidate =>
                    candidate.FirmId == firmId &&
                    candidate.CaseId == caseId &&
                    candidate.ContentKey == contentKey,
                cancellationToken);
    }

    /// <summary>Fixed type identity only.</summary>
    public override string ToString() => nameof(SqlSourceRegistryStore);

    private static SourceRegistration ToRegistration(SourceRegistrationEntity entity) =>
        new(
            TrustedFirmId.FromGuid(entity.FirmId),
            entity.CaseId,
            entity.ResourceId,
            entity.ContentKey,
            entity.State,
            entity.StagedAtUtc,
            entity.DocumentId,
            entity.BoundAtUtc,
            entity.PurgeRecordedAtUtc);

    private static bool IsContentKey(string? contentKey) =>
        contentKey is { Length: > 0 and <= 128 } &&
        contentKey.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
