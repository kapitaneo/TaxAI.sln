using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Tests.Support;
using TaxCases.Models;
using Tenancy.Models;

namespace Persistence.Tests.Lifecycle;

/// <summary>
/// Spec 016 T04 — the relational source registry, on a real relational
/// database with every real migration applied.
/// <para>
/// A staged object is tracked before it is reachable; the real intake upload
/// commit binds it in the same transaction; compensating cleanup and a metadata
/// commit racing for the same staged object cannot both win; a bound or purged
/// object is never cleaned up or resurrected; and every read and write is
/// scoped to the trusted firm and case, with a foreign lookup indistinguishable
/// from an absent one. All data is synthetic.
/// </para>
/// </summary>
public sealed class SourceRegistryTests
{
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);
    private static readonly TrustedFirmId OtherFirm = TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId);
    private static readonly Guid Case = Guid.Parse("12121212-1212-4212-8212-121212121212");
    private static readonly Guid ExpectedItem = Guid.Parse("13131313-1313-4313-8313-131313131313");
    private static readonly DateTimeOffset Staged = SyntheticFixtures.CreatedAtUtc.AddMinutes(5);

    private static string Key(char fill) => new(fill, 43);

    /// <summary>A real case, a redeemed link and a live client session.</summary>
    private static async Task<(RelationalTestContext Database, IntakeClientScope Scope)> ArrangeAsync()
    {
        var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        context.Add(SyntheticFixtures.Firm(SyntheticFixtures.OtherFirmId));
        await context.SaveChangesAsync();
        var store = new SqlIntakeStore(context);
        var intake = SyntheticFixtures.IntakeCase();
        Assert.Equal(IntakeStoreWriteCode.Success, await store.CreateAsync(intake, CancellationToken.None));
        Assert.Equal(IntakeStoreWriteCode.Success, await store.CreateAsync(
            SyntheticFixtures.IntakeCase(SyntheticFixtures.OtherFirmId, digestByte: 0x31),
            CancellationToken.None));

        var link = await store.FindLinkScopeAsync(intake.Case.CurrentLink.CredentialDigest, SyntheticFixtures.CreatedAtUtc.AddMinutes(1), CancellationToken.None);
        var session = new NewIntakeSession(
            Guid.NewGuid(),
            SyntheticFixtures.Digest(0x22),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(1),
            SyntheticFixtures.CreatedAtUtc.AddHours(8),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(31));
        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await store.RedeemLinkAsync(link!, session, SyntheticFixtures.Audit(IntakeAuditKind.LinkRedeemed, session.SessionId), CancellationToken.None));
        var scope = await store.FindClientScopeAsync(session.SessionDigest, session.CreatedAtUtc.AddMinutes(1), CancellationToken.None);
        return (database, scope!);
    }

    private static IntakeDocumentRecord Document(string contentKey, Guid? documentId = null, int version = 1) =>
        new(
            documentId ?? Guid.NewGuid(),
            ExpectedItem,
            DocumentCategory.T4Slip,
            version,
            contentKey,
            IntakeMediaKind.Pdf,
            10,
            Enumerable.Repeat((byte)7, 32).ToArray(),
            Staged.AddMinutes(1));

    private static async Task<IntakeStoreWriteCode> CommitAsync(
        RelationalTestContext database,
        IntakeClientScope scope,
        IntakeDocumentRecord document,
        long concurrencyVersion = 1)
    {
        await using var context = database.CreateContext();
        return await new SqlIntakeStore(context).AppendUploadAttemptAsync(
            scope with { EvaluationAtUtc = Staged },
            concurrencyVersion,
            Staged.AddMinutes(20),
            new IntakeUploadAttemptRecord(Guid.NewGuid(), ExpectedItem, IntakeUploadOutcome.Accepted, Staged),
            document,
            SyntheticFixtures.Audit(IntakeAuditKind.UploadAccepted, scope.SessionId, ExpectedItem),
            CancellationToken.None);
    }

    private static SqlSourceRegistryStore Registry(RelationalTestContext database) =>
        new(database.CreateContext());

    [Fact]
    public async Task AStagedObjectIsRegisteredAndReadBackOnlyInItsTrustedScope()
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;
        var resource = Guid.NewGuid();

        Assert.Equal(SourceRegistryWriteCode.Success, await Registry(database).RegisterStagedAsync(Firm, Case, resource, Key('A'), Staged, CancellationToken.None));

        var found = await Registry(database).FindAsync(Firm, Case, Key('A'), CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(SourceRegistryState.Staged, found!.State);
        Assert.Equal(resource, found.ResourceId);
        Assert.Equal(Staged, found.StagedAtUtc);
        Assert.Null(await Registry(database).FindAsync(OtherFirm, Case, Key('A'), CancellationToken.None));
        Assert.Null(await Registry(database).FindAsync(Firm, Guid.NewGuid(), Key('A'), CancellationToken.None));
    }

    [Fact]
    public async Task RegistrationForAnUnknownOrForeignCaseIsNotFound()
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;

        Assert.Equal(SourceRegistryWriteCode.NotFound, await Registry(database).RegisterStagedAsync(Firm, Guid.NewGuid(), Guid.NewGuid(), Key('A'), Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryWriteCode.NotFound, await Registry(database).RegisterStagedAsync(TrustedFirmId.FromGuid(Guid.NewGuid()), Case, Guid.NewGuid(), Key('A'), Staged, CancellationToken.None));
        await using var context = database.CreateContext();
        Assert.Equal(0, await context.Set<SourceRegistrationEntity>().CountAsync());
    }

    [Fact] // An identifier is never reused, so a purged object is never resurrected.
    public async Task AReusedResourceOrContentKeyIsAConflict()
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;
        var resource = Guid.NewGuid();
        await Registry(database).RegisterStagedAsync(Firm, Case, resource, Key('A'), Staged, CancellationToken.None);

        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RegisterStagedAsync(Firm, Case, resource, Key('B'), Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RegisterStagedAsync(Firm, Case, Guid.NewGuid(), Key('A'), Staged, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    public async Task AMalformedRegistrationIsRefusedWithoutAWrite(string contentKey)
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;

        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RegisterStagedAsync(Firm, Case, Guid.NewGuid(), contentKey, Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RegisterStagedAsync(Firm, Case, Guid.Empty, Key('A'), Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RegisterStagedAsync(Firm, Case, Guid.NewGuid(), Key('A'), Staged.ToOffset(TimeSpan.FromHours(2)), CancellationToken.None));
    }

    [Fact] // Atomic registry binding with the real intake metadata commit.
    public async Task TheIntakeUploadCommitBindsTheStagedObjectToItsDocument()
    {
        var (database, scope) = await ArrangeAsync();
        await using var _d = database;
        await Registry(database).RegisterStagedAsync(Firm, Case, Guid.NewGuid(), Key('A'), Staged, CancellationToken.None);
        var document = Document(Key('A'));

        Assert.Equal(IntakeStoreWriteCode.Success, await CommitAsync(database, scope, document));

        var bound = await Registry(database).FindBoundAsync(Firm, Case, document.DocumentId, Key('A'), CancellationToken.None);
        Assert.NotNull(bound);
        Assert.Equal(SourceRegistryState.Bound, bound!.State);
        Assert.Equal(document.DocumentId, bound.DocumentId);
        Assert.Equal(document.UploadedAtUtc, bound.BoundAtUtc);
    }

    public static TheoryData<string> ForeignBoundLookups() => new()
    {
        "foreign firm",
        "wrong case",
        "wrong document",
        "wrong key",
    };

    [Theory] // AC-030 – AC-033 at the registry.
    [MemberData(nameof(ForeignBoundLookups))]
    public async Task ABoundLookupWithAnyMismatchedScopeValueFindsNothing(string scenario)
    {
        var (database, scope) = await ArrangeAsync();
        await using var _d = database;
        await Registry(database).RegisterStagedAsync(Firm, Case, Guid.NewGuid(), Key('A'), Staged, CancellationToken.None);
        var document = Document(Key('A'));
        await CommitAsync(database, scope, document);

        Assert.Null(await Registry(database).FindBoundAsync(
            scenario == "foreign firm" ? OtherFirm : Firm,
            scenario == "wrong case" ? Guid.NewGuid() : Case,
            scenario == "wrong document" ? Guid.NewGuid() : document.DocumentId,
            scenario == "wrong key" ? Key('B') : Key('A'),
            CancellationToken.None));
    }

    [Fact]
    public async Task AStagedButUncommittedObjectIsNotBound()
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;
        await Registry(database).RegisterStagedAsync(Firm, Case, Guid.NewGuid(), Key('A'), Staged, CancellationToken.None);

        Assert.Null(await Registry(database).FindBoundAsync(Firm, Case, Guid.NewGuid(), Key('A'), CancellationToken.None));
    }

    [Fact] // Cleanup that took the object first makes the racing commit lose, atomically.
    public async Task ACommitForAnObjectCleanupAlreadyTookIsRefusedAndLeavesNoMetadata()
    {
        var (database, scope) = await ArrangeAsync();
        await using var _d = database;
        var resource = Guid.NewGuid();
        await Registry(database).RegisterStagedAsync(Firm, Case, resource, Key('A'), Staged, CancellationToken.None);
        Assert.Equal(SourceRegistryWriteCode.Success, await Registry(database).RecordPurgeAsync(Firm, Case, resource, SourceObjectPurgeOutcome.Unavailable, Staged, CancellationToken.None));

        var result = await CommitAsync(database, scope, Document(Key('A')));

        Assert.Equal(IntakeStoreWriteCode.Conflict, result);
        await using var context = database.CreateContext();
        Assert.Equal(0, await context.Set<IntakeDocumentEntity>().CountAsync());
        Assert.Equal(0, await context.Set<IntakeUploadAttemptEntity>().CountAsync());
        Assert.Equal(1, (await context.Set<IntakeCaseEntity>().SingleAsync(entity => entity.FirmId == Firm.Value && entity.CaseId == Case)).ConcurrencyVersion);
        Assert.Equal(SourceRegistryState.PurgePending, (await Registry(database).FindAsync(Firm, Case, Key('A'), CancellationToken.None))!.State);
    }

    [Fact] // A commit that won first makes cleanup lose: a bound object is never deleted.
    public async Task CleanupOfABoundObjectIsAConflictAndChangesNothing()
    {
        var (database, scope) = await ArrangeAsync();
        await using var _d = database;
        var resource = Guid.NewGuid();
        await Registry(database).RegisterStagedAsync(Firm, Case, resource, Key('A'), Staged, CancellationToken.None);
        await CommitAsync(database, scope, Document(Key('A')));

        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RecordPurgeAsync(Firm, Case, resource, SourceObjectPurgeOutcome.Complete, Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryState.Bound, (await Registry(database).FindAsync(Firm, Case, Key('A'), CancellationToken.None))!.State);
    }

    [Fact] // The Development filesystem store keeps no registry; its commits are unchanged.
    public async Task ACommitWithNoRegistrationAtAllIsUnchanged()
    {
        var (database, scope) = await ArrangeAsync();
        await using var _d = database;

        Assert.Equal(IntakeStoreWriteCode.Success, await CommitAsync(database, scope, Document(Key('Z'))));
        await using var context = database.CreateContext();
        Assert.Equal(0, await context.Set<SourceRegistrationEntity>().CountAsync());
        Assert.Equal(1, await context.Set<IntakeDocumentEntity>().CountAsync());
    }

    [Fact]
    public async Task APendingCleanupBecomesPurgedOnlyOnACompleteOutcomeAndNeverMovesBack()
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;
        var resource = Guid.NewGuid();
        await Registry(database).RegisterStagedAsync(Firm, Case, resource, Key('A'), Staged, CancellationToken.None);

        Assert.Equal(SourceRegistryWriteCode.Success, await Registry(database).RecordPurgeAsync(Firm, Case, resource, SourceObjectPurgeOutcome.Pending, Staged.AddMinutes(1), CancellationToken.None));
        Assert.Equal(SourceRegistryState.PurgePending, (await Registry(database).FindAsync(Firm, Case, Key('A'), CancellationToken.None))!.State);
        Assert.Equal(SourceRegistryWriteCode.Success, await Registry(database).RecordPurgeAsync(Firm, Case, resource, SourceObjectPurgeOutcome.Complete, Staged.AddMinutes(2), CancellationToken.None));

        var purged = await Registry(database).FindAsync(Firm, Case, Key('A'), CancellationToken.None);
        Assert.Equal(SourceRegistryState.Purged, purged!.State);
        Assert.Equal(Staged.AddMinutes(2), purged.PurgeRecordedAtUtc);
        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RecordPurgeAsync(Firm, Case, resource, SourceObjectPurgeOutcome.Pending, Staged.AddMinutes(3), CancellationToken.None));
        Assert.Equal(SourceRegistryState.Purged, (await Registry(database).FindAsync(Firm, Case, Key('A'), CancellationToken.None))!.State);
    }

    [Fact]
    public async Task APurgeRecordForAForeignOrUnknownResourceChangesNothing()
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;
        var resource = Guid.NewGuid();
        await Registry(database).RegisterStagedAsync(Firm, Case, resource, Key('A'), Staged, CancellationToken.None);

        Assert.Equal(SourceRegistryWriteCode.NotFound, await Registry(database).RecordPurgeAsync(OtherFirm, Case, resource, SourceObjectPurgeOutcome.Complete, Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryWriteCode.NotFound, await Registry(database).RecordPurgeAsync(Firm, Case, Guid.NewGuid(), SourceObjectPurgeOutcome.Complete, Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryWriteCode.Conflict, await Registry(database).RecordPurgeAsync(Firm, Case, resource, (SourceObjectPurgeOutcome)9, Staged, CancellationToken.None));
        Assert.Equal(SourceRegistryState.Staged, (await Registry(database).FindAsync(Firm, Case, Key('A'), CancellationToken.None))!.State);
    }

    [Fact] // Orphan tracking and recovery replay, firm-scoped and bounded.
    public async Task TheUncommittedWorkListIsFirmScopedBoundedAndNeverIncludesBoundOrPurgedObjects()
    {
        var (database, scope) = await ArrangeAsync();
        await using var _d = database;
        var registry = Registry(database);
        var old = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var purged = Guid.NewGuid();
        await registry.RegisterStagedAsync(Firm, Case, old, Key('A'), Staged, CancellationToken.None);
        await registry.RegisterStagedAsync(Firm, Case, pending, Key('B'), Staged.AddHours(30), CancellationToken.None);
        await registry.RecordPurgeAsync(Firm, Case, pending, SourceObjectPurgeOutcome.Pending, Staged.AddHours(30), CancellationToken.None);
        await registry.RegisterStagedAsync(Firm, Case, purged, Key('C'), Staged, CancellationToken.None);
        await registry.RecordPurgeAsync(Firm, Case, purged, SourceObjectPurgeOutcome.Complete, Staged, CancellationToken.None);
        await registry.RegisterStagedAsync(Firm, Case, Guid.NewGuid(), Key('D'), Staged, CancellationToken.None);
        await CommitAsync(database, scope, Document(Key('D')));
        await registry.RegisterStagedAsync(Firm, Case, Guid.NewGuid(), Key('E'), Staged.AddHours(29), CancellationToken.None);
        await registry.RegisterStagedAsync(OtherFirm, Guid.Parse("12121212-1212-4212-8212-121212121212"), Guid.NewGuid(), Key('F'), Staged, CancellationToken.None);

        var work = await Registry(database).ListUncommittedAsync(Firm, Staged.AddHours(24), 50, CancellationToken.None);

        Assert.Equal(new[] { old, pending }, work.Select(entry => entry.ResourceId));
        Assert.All(work, entry => Assert.Equal(Firm, entry.Firm));
        Assert.Single(await Registry(database).ListUncommittedAsync(Firm, Staged.AddHours(24), 1, CancellationToken.None));
        Assert.Empty(await Registry(database).ListUncommittedAsync(Firm, Staged.AddHours(24), 0, CancellationToken.None));
        Assert.Empty(await Registry(database).ListUncommittedAsync(Firm, Staged.AddHours(24), 501, CancellationToken.None));
    }

    [Fact]
    public async Task TheSchemaRefusesAnInconsistentRowEvenWithoutTheStore()
    {
        var (database, _) = await ArrangeAsync();
        await using var _d = database;
        await using var context = database.CreateContext();
        context.Add(new SourceRegistrationEntity
        {
            FirmId = Firm.Value,
            CaseId = Case,
            ResourceId = Guid.NewGuid(),
            ContentKey = Key('A'),
            State = SourceRegistryState.Bound,
            StagedAtUtc = Staged,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void TheStoreIsStructurallyRendered()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.Equal(nameof(SqlSourceRegistryStore), new SqlSourceRegistryStore(context).ToString());
    }
}
