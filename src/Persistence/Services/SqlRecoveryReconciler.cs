using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Services;

/// <summary>
/// The recovered database's side of recovery reconciliation (spec 016 T06,
/// FR-10, FR-11).
/// <para>
/// It is resolved only by the host's recovery orchestrator while the
/// environment is quarantined. Each replay re-applies a destruction an
/// acknowledged ledger fact already records, inside that fact's trusted firm;
/// nothing here decides that anything is due. The two environment-wide members
/// only revoke or deactivate access and return counts.
/// </para>
/// <para>
/// Instants are compared in memory rather than in SQL so that the same code
/// runs against every relational provider the module is verified on; each
/// update then targets exact keys in bounded batches and repeats its guard
/// predicate, so a concurrent or repeated reconciliation changes nothing twice.
/// </para>
/// </summary>
internal sealed class SqlRecoveryReconciler : IRecoveryReconciler
{
    private const int BatchSize = 500;

    private readonly TaxAiDbContext _context;
    private readonly Func<TaxAiDbContext, CancellationToken, Task<DatabaseIncarnation?>> _incarnationReader;

    public SqlRecoveryReconciler(TaxAiDbContext context)
        : this(context, ReadSqlServerIncarnationAsync)
    {
    }

    /// <summary>
    /// The seam for a relational provider whose catalog differs from SQL
    /// Server's. Only the test project uses it; production reads the SQL Server
    /// catalog.
    /// </summary>
    internal SqlRecoveryReconciler(
        TaxAiDbContext context,
        Func<TaxAiDbContext, CancellationToken, Task<DatabaseIncarnation?>> incarnationReader)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(incarnationReader);

        _context = context;
        _incarnationReader = incarnationReader;
    }

    public async Task<DatabaseIncarnation?> ReadIncarnationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _incarnationReader(_context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsStorageFailure(failure, cancellationToken))
        {
            return null;
        }
    }

    public async Task<RecoveredDataExtent> ReadDataExtentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var holdsData =
                await _context.Set<IntakeCaseEntity>().AnyAsync(cancellationToken).ConfigureAwait(false) ||
                await _context.Set<PersistedReviewRecord>().AnyAsync(cancellationToken).ConfigureAwait(false) ||
                await _context.Set<SourceRegistrationEntity>().AnyAsync(cancellationToken).ConfigureAwait(false) ||
                await _context.Set<AuthenticationSession>().AnyAsync(cancellationToken).ConfigureAwait(false);

            // The newest recorded activity is the lower bound of the restore
            // point: nothing in a restored database can be newer than it.
            var latest = await _context.Database
                .SqlQueryRaw<DateTimeOffset?>(LatestActivityQuery)
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false);

            return RecoveredDataExtent.Observed(holdsData, latest?.ToUniversalTime());
        }
        catch (Exception failure) when (IsStorageFailure(failure, cancellationToken))
        {
            return RecoveredDataExtent.Unreadable;
        }
    }

    public async Task<RecoveryReplayOutcome> ReplayTerminalSourceAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        DateTimeOffset replayedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || resourceId == Guid.Empty || replayedAtUtc.Offset != TimeSpan.Zero)
        {
            return RecoveryReplayOutcome.Incomplete;
        }

        try
        {
            var state = await _context.Set<SourceRegistrationEntity>()
                .AsNoTracking()
                .Where(candidate =>
                    candidate.FirmId == firm.Value &&
                    candidate.CaseId == caseId &&
                    candidate.ResourceId == resourceId)
                .Select(candidate => (SourceRegistryState?)candidate.State)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (state is null)
            {
                return RecoveryReplayOutcome.Absent;
            }

            if (state is SourceRegistryState.PurgePending or SourceRegistryState.Purged)
            {
                return RecoveryReplayOutcome.AlreadyTerminal;
            }

            // A resurrected registration is fenced: every reader requires a
            // bound row, so a pending purge can no longer be read. The committed
            // document keeps its binding for history; its bytes are gone.
            var updated = await _context.Set<SourceRegistrationEntity>()
                .Where(candidate =>
                    candidate.FirmId == firm.Value &&
                    candidate.CaseId == caseId &&
                    candidate.ResourceId == resourceId &&
                    (candidate.State == SourceRegistryState.Staged ||
                     candidate.State == SourceRegistryState.Bound))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.State, SourceRegistryState.PurgePending)
                        .SetProperty(candidate => candidate.PurgeRecordedAtUtc, (DateTimeOffset?)replayedAtUtc),
                    cancellationToken)
                .ConfigureAwait(false);

            return updated == 1 ? RecoveryReplayOutcome.Applied : RecoveryReplayOutcome.AlreadyTerminal;
        }
        catch (Exception failure) when (IsStorageFailure(failure, cancellationToken))
        {
            return RecoveryReplayOutcome.Incomplete;
        }
    }

    public async Task<RecoveryReplayOutcome> ReplayTerminalStructuredAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (recordId == Guid.Empty)
        {
            return RecoveryReplayOutcome.Incomplete;
        }

        try
        {
            var exists = await _context.Set<PersistedReviewRecord>()
                .AsNoTracking()
                .AnyAsync(
                    record => record.FirmId == firm.Value && record.RecordId == recordId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
            {
                return RecoveryReplayOutcome.Absent;
            }

            // Retained history that still references the record is never
            // cascaded away here; the environment stays quarantined instead.
            var referenced = await _context.Set<IntakeReviewAssociationEntity>()
                .AsNoTracking()
                .AnyAsync(
                    association => association.FirmId == firm.Value && association.RecordId == recordId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (referenced)
            {
                return RecoveryReplayOutcome.Blocked;
            }

            var deleted = await _context.Set<PersistedReviewRecord>()
                .Where(record => record.FirmId == firm.Value && record.RecordId == recordId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return deleted == 1 ? RecoveryReplayOutcome.Applied : RecoveryReplayOutcome.AlreadyTerminal;
        }
        catch (DbUpdateException)
        {
            return RecoveryReplayOutcome.Blocked;
        }
        catch (Exception failure) when (IsStorageFailure(failure, cancellationToken))
        {
            return RecoveryReplayOutcome.Incomplete;
        }
    }

    public async Task<RecoveryAccessInvalidation> InvalidatePreRecoveryAccessAsync(
        DateTimeOffset recoveryStartedAtUtc,
        CancellationToken cancellationToken)
    {
        if (recoveryStartedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The recovery instant must be UTC.", nameof(recoveryStartedAtUtc));
        }

        // Only credentials that are still live are revoked. An expired one is
        // left exactly as it was: its expiry is never moved, so it stays
        // unusable, and nothing is re-issued.
        var sessions = (await _context.Set<AuthenticationSession>()
                .AsNoTracking()
                .Where(session => session.RevokedAtUtc == null)
                .Select(session => new
                {
                    session.SessionId,
                    session.CreatedAtUtc,
                    session.IdleExpiresAtUtc,
                    session.AbsoluteExpiresAtUtc,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(session =>
                session.CreatedAtUtc < recoveryStartedAtUtc &&
                session.IdleExpiresAtUtc > recoveryStartedAtUtc &&
                session.AbsoluteExpiresAtUtc > recoveryStartedAtUtc)
            .Select(session => session.SessionId)
            .ToArray();

        var revokedSessions = 0;
        foreach (var batch in sessions.Chunk(BatchSize))
        {
            revokedSessions += await _context.Set<AuthenticationSession>()
                .Where(session => batch.Contains(session.SessionId) && session.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(session => session.RevokedAtUtc, (DateTimeOffset?)recoveryStartedAtUtc),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var clientSessions = (await _context.Set<IntakeClientSessionEntity>()
                .AsNoTracking()
                .Where(session => session.RevokedAtUtc == null)
                .Select(session => new
                {
                    session.FirmId,
                    session.CaseId,
                    session.SessionId,
                    session.CreatedAtUtc,
                    session.IdleExpiresAtUtc,
                    session.AbsoluteExpiresAtUtc,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(session =>
                session.CreatedAtUtc < recoveryStartedAtUtc &&
                session.IdleExpiresAtUtc > recoveryStartedAtUtc &&
                session.AbsoluteExpiresAtUtc > recoveryStartedAtUtc)
            .ToArray();

        // Intake identifiers are unique only within their firm and case, so
        // every update names the complete scoped key, never the bare identifier.
        var revokedClientSessions = 0;
        foreach (var scope in clientSessions.GroupBy(session => (session.FirmId, session.CaseId)))
        {
            foreach (var batch in scope.Select(session => session.SessionId).Chunk(BatchSize))
            {
                revokedClientSessions += await _context.Set<IntakeClientSessionEntity>()
                    .Where(session =>
                        session.FirmId == scope.Key.FirmId &&
                        session.CaseId == scope.Key.CaseId &&
                        batch.Contains(session.SessionId) &&
                        session.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(session => session.RevokedAtUtc, (DateTimeOffset?)recoveryStartedAtUtc),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var links = (await _context.Set<IntakeLinkEntity>()
                .AsNoTracking()
                .Where(link => link.RevokedAtUtc == null && link.SpentAtUtc == null)
                .Select(link => new { link.FirmId, link.CaseId, link.LinkId, link.IssuedAtUtc, link.ExpiresAtUtc })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(link =>
                link.IssuedAtUtc < recoveryStartedAtUtc &&
                link.ExpiresAtUtc > recoveryStartedAtUtc)
            .ToArray();

        var revokedLinks = 0;
        foreach (var scope in links.GroupBy(link => (link.FirmId, link.CaseId)))
        {
            foreach (var batch in scope.Select(link => link.LinkId).Chunk(BatchSize))
            {
                revokedLinks += await _context.Set<IntakeLinkEntity>()
                    .Where(link =>
                        link.FirmId == scope.Key.FirmId &&
                        link.CaseId == scope.Key.CaseId &&
                        batch.Contains(link.LinkId) &&
                        link.RevokedAtUtc == null &&
                        link.SpentAtUtc == null)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(link => link.RevokedAtUtc, (DateTimeOffset?)recoveryStartedAtUtc),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new RecoveryAccessInvalidation(revokedSessions, revokedClientSessions, revokedLinks);
    }

    public async Task<RecoveryMembershipReconciliation> ApplyAuthorizationAttestationAsync(
        RecoveryAuthorizationAttestation attestation,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        if (appliedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The application instant must be UTC.", nameof(appliedAtUtc));
        }

        var authorized = attestation.Memberships
            .Select(membership => (membership.Firm.Value, membership.UserId))
            .ToHashSet();

        var active = await _context.Set<FirmMembership>()
            .AsNoTracking()
            .Where(membership => membership.IsActive)
            .Select(membership => new { membership.MembershipId, membership.FirmId, membership.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var unauthorized = active
            .Where(membership => !authorized.Contains((membership.FirmId, membership.UserId)))
            .Select(membership => membership.MembershipId)
            .ToArray();

        var deactivated = 0;
        foreach (var batch in unauthorized.Chunk(BatchSize))
        {
            deactivated += await _context.Set<FirmMembership>()
                .Where(membership => batch.Contains(membership.MembershipId) && membership.IsActive)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(membership => membership.IsActive, false)
                        .SetProperty(membership => membership.UpdatedAtUtc, appliedAtUtc),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var held = active
            .Select(membership => (membership.FirmId, membership.UserId))
            .ToHashSet();
        var unrestored = authorized.Count(pair => !held.Contains(pair));

        return new RecoveryMembershipReconciliation(deactivated, unrestored);
    }

    /// <summary>Fixed type identity only.</summary>
    public override string ToString() => nameof(SqlRecoveryReconciler);

    /// <summary>
    /// The newest recorded activity across sessions, intake audit, source
    /// registrations and review records. Quoted with brackets, which every
    /// relational provider the module is verified on accepts.
    /// </summary>
    private const string LatestActivityQuery =
        "SELECT MAX([Value]) AS [Value] FROM (" +
        "SELECT MAX([LastSeenAtUtc]) AS [Value] FROM [AuthenticationSessions] " +
        "UNION ALL SELECT MAX([CreatedAtUtc]) FROM [AuthenticationSessions] " +
        "UNION ALL SELECT MAX([OccurredAtUtc]) FROM [IntakeAuditEvents] " +
        "UNION ALL SELECT MAX([OccurredAtUtc]) FROM [ExtractionAuditEvents] " +
        "UNION ALL SELECT MAX([StagedAtUtc]) FROM [SourceRegistrations] " +
        "UNION ALL SELECT MAX([CreatedAtUtc]) FROM [PersistedReviewRecords]" +
        ") AS [Activity]";

    /// <summary>
    /// The SQL Server catalog's identity of this physical database: the
    /// logical server, the database and its creation instant. A point-in-time
    /// restore, a copy and a geo-secondary are each a different database with a
    /// different creation instant. Only the SHA-256 fingerprint leaves here.
    /// </summary>
    private static async Task<DatabaseIncarnation?> ReadSqlServerIncarnationAsync(
        TaxAiDbContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer())
        {
            return null;
        }

        var identity = await context.Database
            .SqlQueryRaw<string>(
                "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) + NCHAR(10) + DB_NAME() + NCHAR(10) + " +
                "CONVERT(nvarchar(64), [create_date], 126) AS [Value] FROM [sys].[databases] WHERE [database_id] = DB_ID()")
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return FromIdentity(identity);
    }

    /// <summary>Hashes a catalog identity into an opaque incarnation.</summary>
    internal static DatabaseIncarnation? FromIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return null;
        }

        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToUpper(CultureInfo.InvariantCulture))));
        return DatabaseIncarnation.TryCreate(fingerprint, out var incarnation) ? incarnation : null;
    }

    private static bool IsStorageFailure(Exception failure, CancellationToken cancellationToken) =>
        failure is DbException or DbUpdateException or InvalidOperationException or TimeoutException or FormatException ||
        (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
