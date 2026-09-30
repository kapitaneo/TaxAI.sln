using System.Reflection;
using ClientIntake.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Services;
using Persistence.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Tests.Lifecycle;

/// <summary>
/// Spec 016 T06 — the recovered database's side of recovery, on a real
/// relational database with every real migration applied (AC-102, AC-110,
/// AC-113, AC-114; DEP-17, DEP-19, DEP-20).
/// <para>
/// A restore is simulated for real: the migrated database is copied with the
/// provider's online backup at the restore point, the live database then moves
/// on (a purge, a sign-out, a revoked membership), and the copy — which knows
/// nothing of what happened after it — is what the reconciler must repair. All
/// data is synthetic.
/// </para>
/// </summary>
public sealed class RecoveryQuarantineTests
{
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);
    private static readonly TrustedFirmId OtherFirm = TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId);
    private static readonly Guid Case = Guid.Parse("12121212-1212-4212-8212-121212121212");
    private static readonly DateTimeOffset Start = SyntheticFixtures.CreatedAtUtc.AddHours(1);

    private static DatabaseIncarnation Incarnation(char fill) =>
        DatabaseIncarnation.TryCreate(new string(fill, 64), out var incarnation) ? incarnation! : throw new InvalidOperationException();

    private static SqlRecoveryReconciler Reconciler(TaxAiDbContext context, char incarnation = 'b') =>
        new(context, (_, _) => Task.FromResult<DatabaseIncarnation?>(Incarnation(incarnation)));

    /// <summary>A byte-for-byte copy of the database as it is now: the backup.</summary>
    private static async Task<RelationalTestContext> BackupAsync(RelationalTestContext live)
    {
        var copy = await RelationalTestContext.CreateAsync(applyMigrations: false);
        await using var source = live.CreateContext();
        await using var target = copy.CreateContext();
        ((SqliteConnection)source.Database.GetDbConnection())
            .BackupDatabase((SqliteConnection)target.Database.GetDbConnection());
        return copy;
    }

    private static async Task<RelationalTestContext> SeedAsync(bool withIntake = true)
    {
        var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        context.Add(SyntheticFixtures.Firm(SyntheticFixtures.OtherFirmId));
        context.Add(SyntheticFixtures.User());
        await context.SaveChangesAsync();

        if (withIntake)
        {
            var store = new SqlIntakeStore(context);
            Assert.Equal(IntakeStoreWriteCode.Success, await store.CreateAsync(SyntheticFixtures.IntakeCase(), CancellationToken.None));
            Assert.Equal(IntakeStoreWriteCode.Success, await store.CreateAsync(
                SyntheticFixtures.IntakeCase(SyntheticFixtures.OtherFirmId, digestByte: 0x31),
                CancellationToken.None));
        }

        return database;
    }

    // ------------------------------------------------ AC-102: terminal replay

    [Fact] // AC-102, DEP-19: a review record purged after the backup is re-deleted.
    public async Task ARestoreFromBeforeAStructuredPurgeIsRepairedByReplay()
    {
        await using var live = await SeedAsync(withIntake: false);
        await using (var context = live.CreateContext())
        {
            context.Add(SyntheticFixtures.ReviewRecord());
            context.Add(SyntheticFixtures.ReviewRecord(SyntheticFixtures.OtherFirmId, packageVersion: 2) is var foreign ? foreign : null!);
            await context.SaveChangesAsync();
        }

        await using var backup = await BackupAsync(live);
        var recordId = SyntheticFixtures.ReviewRecord().RecordId;

        // After the backup, the live database purges the record.
        await using (var context = live.CreateContext())
        {
            await context.Set<PersistedReviewRecord>()
                .Where(record => record.FirmId == Firm.Value && record.RecordId == recordId)
                .ExecuteDeleteAsync();
        }

        // The restore brings it back...
        await using var restored = backup.CreateContext();
        Assert.True(await restored.Set<PersistedReviewRecord>().AnyAsync(record => record.FirmId == Firm.Value));

        // ...a foreign-firm replay touches nothing...
        Assert.Equal(RecoveryReplayOutcome.Absent,
            await Reconciler(restored).ReplayTerminalStructuredAsync(TrustedFirmId.FromGuid(Guid.NewGuid()), recordId, CancellationToken.None));

        // ...and the ledger's replay removes exactly it, idempotently.
        Assert.Equal(RecoveryReplayOutcome.Applied,
            await Reconciler(restored).ReplayTerminalStructuredAsync(Firm, recordId, CancellationToken.None));
        Assert.Equal(RecoveryReplayOutcome.Absent,
            await Reconciler(restored).ReplayTerminalStructuredAsync(Firm, recordId, CancellationToken.None));
        Assert.False(await restored.Set<PersistedReviewRecord>().AnyAsync(record => record.FirmId == Firm.Value));
        Assert.True(await restored.Set<PersistedReviewRecord>().AnyAsync(record => record.FirmId == OtherFirm.Value));
    }

    [Fact] // Replay never cascades into retained history.
    public async Task AReviewRecordStillReferencedByIntakeHistoryIsBlockedNotCascaded()
    {
        await using var database = await SeedAsync();
        var record = SyntheticFixtures.ReviewRecord();
        await using (var context = database.CreateContext())
        {
            context.Add(record);
            context.Add(new IntakeReviewAssociationEntity
            {
                FirmId = Firm.Value,
                CaseId = Case,
                RecordId = record.RecordId,
                AssociatedAtUtc = SyntheticFixtures.CreatedAtUtc,
            });
            await context.SaveChangesAsync();
        }

        await using var restored = database.CreateContext();
        Assert.Equal(RecoveryReplayOutcome.Blocked,
            await Reconciler(restored).ReplayTerminalStructuredAsync(Firm, record.RecordId, CancellationToken.None));
        Assert.True(await restored.Set<PersistedReviewRecord>().AnyAsync(candidate => candidate.RecordId == record.RecordId));
        Assert.True(await restored.Set<IntakeReviewAssociationEntity>().AnyAsync());
    }

    [Fact] // AC-102, AC-110: a resurrected source registration is fenced from every reader.
    public async Task ARestoreFromBeforeASourcePurgeFencesTheResurrectedRegistration()
    {
        await using var live = await SeedAsync();
        var resource = Guid.NewGuid();
        await using (var context = live.CreateContext())
        {
            Assert.Equal(SourceRegistryWriteCode.Success, await new SqlSourceRegistryStore(context)
                .RegisterStagedAsync(Firm, Case, resource, new string('k', 43), SyntheticFixtures.CreatedAtUtc, CancellationToken.None));
        }

        await using var backup = await BackupAsync(live);
        await using (var context = live.CreateContext())
        {
            Assert.Equal(SourceRegistryWriteCode.Success, await new SqlSourceRegistryStore(context)
                .RecordPurgeAsync(Firm, Case, resource, SourceObjectPurgeOutcome.Pending, Start, CancellationToken.None));
        }

        await using var restored = backup.CreateContext();
        var registry = new SqlSourceRegistryStore(restored);
        Assert.Equal(SourceRegistryState.Staged, (await registry.FindAsync(Firm, Case, new string('k', 43), CancellationToken.None))!.State);

        Assert.Equal(RecoveryReplayOutcome.Absent,
            await Reconciler(restored).ReplayTerminalSourceAsync(OtherFirm, Case, resource, Start, CancellationToken.None));
        Assert.Equal(RecoveryReplayOutcome.Applied,
            await Reconciler(restored).ReplayTerminalSourceAsync(Firm, Case, resource, Start, CancellationToken.None));
        Assert.Equal(RecoveryReplayOutcome.AlreadyTerminal,
            await Reconciler(restored).ReplayTerminalSourceAsync(Firm, Case, resource, Start, CancellationToken.None));

        var fenced = await registry.FindAsync(Firm, Case, new string('k', 43), CancellationToken.None);
        Assert.Equal(SourceRegistryState.PurgePending, fenced!.State);
        Assert.Equal(Start, fenced.PurgeRecordedAtUtc);
    }

    [Fact]
    public async Task AnInvalidReplayRequestIsIncompleteNotApplied()
    {
        await using var database = await SeedAsync();
        await using var context = database.CreateContext();

        Assert.Equal(RecoveryReplayOutcome.Incomplete,
            await Reconciler(context).ReplayTerminalSourceAsync(Firm, Guid.Empty, Guid.NewGuid(), Start, CancellationToken.None));
        Assert.Equal(RecoveryReplayOutcome.Incomplete,
            await Reconciler(context).ReplayTerminalSourceAsync(Firm, Case, Guid.NewGuid(), Start.ToOffset(TimeSpan.FromHours(-5)), CancellationToken.None));
        Assert.Equal(RecoveryReplayOutcome.Incomplete,
            await Reconciler(context).ReplayTerminalStructuredAsync(Firm, Guid.Empty, CancellationToken.None));
    }

    // ---------------------------------------- AC-113, AC-114: no resurrection

    [Fact] // AC-113, DEP-20: a session signed out after the backup stays denied.
    public async Task ASessionRevokedAfterTheBackupIsDeniedAfterRecovery()
    {
        await using var live = await SeedAsync(withIntake: false);
        var session = Session(Guid.NewGuid(), SyntheticFixtures.CreatedAtUtc.AddMinutes(50));
        await using (var context = live.CreateContext())
        {
            context.Add(session);
            await context.SaveChangesAsync();
        }

        await using var backup = await BackupAsync(live);
        await using (var context = live.CreateContext())
        {
            var store = new SqlAuthenticationSessionStore(context);
            await store.RevokeAsync((await store.FindAsync(session.SessionId, CancellationToken.None))!, SyntheticFixtures.CreatedAtUtc.AddMinutes(55), CancellationToken.None);
        }

        await using var restored = backup.CreateContext();
        // The restore has undone the sign-out: the old cookie's session is live again...
        Assert.NotNull(await new SqlAuthenticationSessionStore(restored).FindAsync(session.SessionId, CancellationToken.None));

        var result = await Reconciler(restored).InvalidatePreRecoveryAccessAsync(Start, CancellationToken.None);

        // ...until recovery revokes every pre-recovery session.
        Assert.Equal(1, result.AuthenticationSessions);
        await using var after = backup.CreateContext();
        Assert.Null(await new SqlAuthenticationSessionStore(after).FindAsync(session.SessionId, CancellationToken.None));
    }

    [Fact] // AC-114: expired credentials are preserved exactly as expired.
    public async Task ExpiredCredentialsStayExpiredAndLiveOnesAreRevoked()
    {
        await using var database = await SeedAsync();
        var live = Session(Guid.NewGuid(), SyntheticFixtures.CreatedAtUtc.AddMinutes(40));
        var expired = Session(Guid.NewGuid(), SyntheticFixtures.CreatedAtUtc.AddMinutes(-60));
        var later = Session(Guid.NewGuid(), Start.AddMinutes(1));
        await using (var context = database.CreateContext())
        {
            context.AddRange(live, expired, later);

            // The other firm's link expired before recovery; this firm's is live.
            await context.Set<IntakeLinkEntity>()
                .Where(link => link.FirmId == OtherFirm.Value)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    link => link.ExpiresAtUtc,
                    SyntheticFixtures.CreatedAtUtc.AddMinutes(30)));
            var liveLink = await context.Set<IntakeLinkEntity>().SingleAsync(link => link.FirmId == Firm.Value);
            context.Add(ClientSession(liveLink, 0x51, SyntheticFixtures.CreatedAtUtc.AddMinutes(50), Start.AddMinutes(10)));
            context.Add(ClientSession(liveLink, 0x52, SyntheticFixtures.CreatedAtUtc.AddMinutes(5), SyntheticFixtures.CreatedAtUtc.AddMinutes(20)));
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var result = await Reconciler(context).InvalidatePreRecoveryAccessAsync(Start, CancellationToken.None);

            Assert.Equal(1, result.AuthenticationSessions);
            Assert.Equal(1, result.ClientSessions);
            Assert.Equal(1, result.IntakeLinks);
        }

        await using var check = database.CreateContext();
        var sessions = await check.Set<AuthenticationSession>().AsNoTracking().ToDictionaryAsync(session => session.SessionId);
        Assert.Equal(Start, sessions[live.SessionId].RevokedAtUtc);
        Assert.Null(sessions[expired.SessionId].RevokedAtUtc);
        Assert.Equal(expired.IdleExpiresAtUtc, sessions[expired.SessionId].IdleExpiresAtUtc);
        Assert.Null(sessions[later.SessionId].RevokedAtUtc);

        var links = await check.Set<IntakeLinkEntity>().AsNoTracking().ToListAsync();
        Assert.Equal(Start, links.Single(link => link.FirmId == Firm.Value).RevokedAtUtc);
        var stillExpired = links.Single(link => link.FirmId == OtherFirm.Value);
        Assert.Null(stillExpired.RevokedAtUtc);
        Assert.Equal(SyntheticFixtures.CreatedAtUtc.AddMinutes(30), stillExpired.ExpiresAtUtc);

        var clientSessions = await check.Set<IntakeClientSessionEntity>().AsNoTracking().ToListAsync();
        Assert.Single(clientSessions, session => session.RevokedAtUtc == Start);
        Assert.Single(clientSessions, session => session.RevokedAtUtc is null && session.IdleExpiresAtUtc < Start);

        // Idempotent: a second, concurrent or repeated reconciliation changes nothing.
        await using var again = database.CreateContext();
        var repeat = await Reconciler(again).InvalidatePreRecoveryAccessAsync(Start, CancellationToken.None);
        Assert.Equal((0, 0, 0), (repeat.AuthenticationSessions, repeat.ClientSessions, repeat.IntakeLinks));
    }

    // -------------------------------------------- current authorization

    [Fact] // A membership revoked after the backup does not come back.
    public async Task MembershipsTheAttestationNoLongerAuthorizesAreDeactivated()
    {
        await using var live = await SeedAsync(withIntake: false);
        var secondUser = Guid.NewGuid();
        await using (var context = live.CreateContext())
        {
            context.Add(SyntheticFixtures.Membership());
            context.Add(Construct<PortalUser>(
                secondUser, "https://identity.example.test", "synthetic-subject-2", true,
                SyntheticFixtures.CreatedAtUtc, SyntheticFixtures.CreatedAtUtc));
            context.Add(Membership(Guid.NewGuid(), SyntheticFixtures.OtherFirmId, secondUser));
            await context.SaveChangesAsync();
        }

        var lostUser = Guid.NewGuid();
        var attestation = new RecoveryAuthorizationAttestation(Start, [
            new RecoveryAuthorizedMembership(Firm, SyntheticFixtures.UserId),
            new RecoveryAuthorizedMembership(Firm, lostUser),
        ]);

        await using (var context = live.CreateContext())
        {
            var result = await Reconciler(context).ApplyAuthorizationAttestationAsync(attestation, Start, CancellationToken.None);
            Assert.Equal(1, result.Deactivated);
            Assert.Equal(1, result.Unrestored);
        }

        await using var check = live.CreateContext();
        var memberships = await check.Set<FirmMembership>().AsNoTracking().ToListAsync();
        Assert.True(memberships.Single(membership => membership.UserId == SyntheticFixtures.UserId).IsActive);
        var revoked = memberships.Single(membership => membership.UserId == secondUser);
        Assert.False(revoked.IsActive);
        Assert.Equal(Start, revoked.UpdatedAtUtc);
        Assert.DoesNotContain(memberships, membership => membership.UserId == lostUser);

        var repeat = await Reconciler(check).ApplyAuthorizationAttestationAsync(attestation, Start, CancellationToken.None);
        Assert.Equal(0, repeat.Deactivated);
    }

    // ------------------------------------------------- extent and identity

    [Fact] // The bootstrap case: a brand-new database holds nothing.
    public async Task APristineDatabaseHoldsNoDataAndNoActivity()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();

        var extent = await Reconciler(context).ReadDataExtentAsync(CancellationToken.None);

        Assert.True(extent.IsReadable);
        Assert.False(extent.HoldsTaxpayerData);
        Assert.Null(extent.LatestActivityUtc);
    }

    [Fact] // The newest activity is the lower bound of the restore point.
    public async Task TheLatestActivityIsTheNewestRecordedInstant()
    {
        await using var database = await SeedAsync();
        await using (var context = database.CreateContext())
        {
            context.Add(Session(Guid.NewGuid(), SyntheticFixtures.CreatedAtUtc.AddMinutes(45)));
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var extent = await Reconciler(check).ReadDataExtentAsync(CancellationToken.None);

        Assert.True(extent.HoldsTaxpayerData);
        Assert.Equal(SyntheticFixtures.CreatedAtUtc.AddMinutes(45), extent.LatestActivityUtc);
        Assert.Equal(TimeSpan.Zero, extent.LatestActivityUtc!.Value.Offset);
    }

    [Fact]
    public async Task AnUnreadableDatabaseIsConservativelyAnUnknownExtentAndIncarnation()
    {
        await using var database = await RelationalTestContext.CreateAsync(applyMigrations: false);
        await using var context = database.CreateContext();
        var failing = new SqlRecoveryReconciler(context, (_, _) => throw new InvalidOperationException("synthetic"));

        var extent = await failing.ReadDataExtentAsync(CancellationToken.None);

        Assert.False(extent.IsReadable);
        Assert.True(extent.HoldsTaxpayerData);
        Assert.Null(await failing.ReadIncarnationAsync(CancellationToken.None));
    }

    [Fact] // The production reader names SQL Server's catalog; other providers yield nothing.
    public async Task TheProductionIncarnationReaderReadsOnlyTheSqlServerCatalog()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Null(await new SqlRecoveryReconciler(context).ReadIncarnationAsync(CancellationToken.None));
    }

    [Fact] // A restored database is a new incarnation; the fingerprint names nothing.
    public void TheIncarnationIsAnOpaqueFingerprintOfServerDatabaseAndCreation()
    {
        var original = SqlRecoveryReconciler.FromIdentity("sql-cc\ntaxai\n2026-01-01T00:00:00");
        var restoredCopy = SqlRecoveryReconciler.FromIdentity("sql-cc\ntaxai\n2026-09-29T12:00:00");
        var geoSecondary = SqlRecoveryReconciler.FromIdentity("sql-ce\ntaxai\n2026-01-02T00:00:00");

        Assert.NotNull(original);
        Assert.False(original!.SameAs(restoredCopy!));
        Assert.False(original.SameAs(geoSecondary!));
        Assert.True(original.SameAs(SqlRecoveryReconciler.FromIdentity("SQL-CC\nTAXAI\n2026-01-01T00:00:00")!));
        Assert.DoesNotContain("sql", original.Fingerprint, StringComparison.Ordinal);
        Assert.Null(SqlRecoveryReconciler.FromIdentity(" "));
        Assert.Equal(nameof(SqlRecoveryReconciler), new SqlRecoveryReconciler(RelationalTestContext.CreateSqlServerContext()).ToString());
    }

    [Fact] // One registration, resolved only through Tenancy's port.
    public void TheReconcilerIsRegisteredAsTheScopedTenancyPort()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTaxAiPersistence(
            "Server=tcp:sql.example.test,1433;Database=taxai;Encrypt=Strict;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;User Id=33333333-3333-4333-8333-333333333333");

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(IRecoveryReconciler));
        Assert.Equal(typeof(SqlRecoveryReconciler), descriptor.ImplementationType);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.False(typeof(SqlRecoveryReconciler).IsPublic);
    }

    // -------------------------------------------------------------- helpers

    private static AuthenticationSession Session(Guid sessionId, DateTimeOffset createdAtUtc) =>
        Construct<AuthenticationSession>(
            sessionId,
            SyntheticFixtures.UserId,
            null,
            createdAtUtc,
            createdAtUtc,
            createdAtUtc.AddMinutes(30),
            createdAtUtc.AddHours(8),
            null);

    private static FirmMembership Membership(Guid membershipId, Guid firmId, Guid userId) =>
        Construct<FirmMembership>(membershipId, firmId, userId, true, SyntheticFixtures.CreatedAtUtc, SyntheticFixtures.CreatedAtUtc);

    private static IntakeClientSessionEntity ClientSession(
        IntakeLinkEntity link,
        byte digest,
        DateTimeOffset createdAtUtc,
        DateTimeOffset idleExpiresAtUtc) =>
        new()
        {
            FirmId = link.FirmId,
            CaseId = link.CaseId,
            SessionId = Guid.NewGuid(),
            LinkId = link.LinkId,
            SessionDigest = Enumerable.Repeat(digest, 32).ToArray(),
            CreatedAtUtc = createdAtUtc,
            AbsoluteExpiresAtUtc = createdAtUtc.AddHours(8),
            IdleExpiresAtUtc = idleExpiresAtUtc,
        };

    private static T Construct<T>(params object?[] arguments) =>
        (T)typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length)
            .Invoke(arguments);
}
