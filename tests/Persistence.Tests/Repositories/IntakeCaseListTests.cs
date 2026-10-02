using ClientIntake.Models;
using Persistence.Repositories;
using Persistence.Tests.Support;
using TaxCases.Models;
using Tenancy.Models;

namespace Persistence.Tests.Repositories;

/// <summary>
/// Spec 017 T03 — the firm-scoped case-list read against the real
/// <see cref="SqlIntakeStore"/> and a real relational database with the
/// repository's migrations applied: firm isolation, newest-first order by the
/// stored case-created audit instant, the stable tie-break, and the bound.
/// </summary>
public sealed class IntakeCaseListTests
{
    private static readonly TrustedFirmId Firm =
        TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);

    private static readonly TrustedFirmId OtherFirm =
        TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId);

    [Fact] // AC-60, FR-34
    public async Task AFirmListsOnlyItsOwnCases()
    {
        await using var database = await TwoFirmsAsync();
        var mine = await CreateAsync(database, Firm, "Test Taxpayer Own", minute: 1, digest: 0x31);
        var theirs = await CreateAsync(database, OtherFirm, "Test Taxpayer Foreign", minute: 2, digest: 0x32);

        var own = await ListAsync(database, Firm, 100);
        var foreign = await ListAsync(database, OtherFirm, 100);

        var ownEntry = Assert.Single(own.Cases);
        Assert.Equal(mine, ownEntry.CaseId);
        Assert.Equal("Test Taxpayer Own", ownEntry.PrimaryTaxpayerDisplayName);
        Assert.Equal(2025, ownEntry.TaxYear);
        Assert.False(own.HasMore);

        Assert.Equal(theirs, Assert.Single(foreign.Cases).CaseId);
    }

    [Fact] // AC-61, FR-34
    public async Task AnotherFirmsCasesNeverChangeTheListOrTheBoundSignal()
    {
        await using var database = await TwoFirmsAsync();
        var first = await CreateAsync(database, Firm, "Test Taxpayer One", minute: 1, digest: 0x31);
        var second = await CreateAsync(database, Firm, "Test Taxpayer Two", minute: 2, digest: 0x32);
        var before = await ListAsync(database, Firm, 2);

        // The other firm gains newer cases than anything this firm holds.
        await CreateAsync(database, OtherFirm, "Test Taxpayer Foreign A", minute: 8, digest: 0x41);
        await CreateAsync(database, OtherFirm, "Test Taxpayer Foreign B", minute: 9, digest: 0x42);
        var after = await ListAsync(database, Firm, 2);

        Assert.Equal(new[] { second, first }, before.Cases.Select(entry => entry.CaseId));
        Assert.Equal(
            before.Cases.Select(entry => entry.CaseId),
            after.Cases.Select(entry => entry.CaseId));
        Assert.False(before.HasMore);
        Assert.False(after.HasMore);
    }

    [Fact] // FR-35
    public async Task CasesAreNewestFirstByTheStoredCaseCreatedInstant()
    {
        await using var database = await TwoFirmsAsync();

        // Inserted out of creation order, so insertion order cannot be what
        // the list returns.
        var middle = await CreateAsync(database, Firm, "Test Taxpayer Middle", minute: 5, digest: 0x31);
        var newest = await CreateAsync(database, Firm, "Test Taxpayer Newest", minute: 9, digest: 0x32);
        var oldest = await CreateAsync(database, Firm, "Test Taxpayer Oldest", minute: 1, digest: 0x33);

        var listed = await ListAsync(database, Firm, 100);

        Assert.Equal(
            new[] { newest, middle, oldest },
            listed.Cases.Select(entry => entry.CaseId));
        Assert.Equal(
            new[] { "Test Taxpayer Newest", "Test Taxpayer Middle", "Test Taxpayer Oldest" },
            listed.Cases.Select(entry => entry.PrimaryTaxpayerDisplayName));
    }

    [Fact] // FR-35
    public async Task LaterActivityOnACaseDoesNotReorderTheList()
    {
        await using var database = await TwoFirmsAsync();
        var older = await CreateAsync(database, Firm, "Test Taxpayer Older", minute: 1, digest: 0x31);
        var newer = await CreateAsync(database, Firm, "Test Taxpayer Newer", minute: 2, digest: 0x32);

        // A much later audit event on the older case: the order is by the
        // immutable case-created instant, not by latest activity.
        await using (var context = database.CreateContext())
        {
            Assert.Equal(
                IntakeStoreWriteCode.Success,
                await new SqlIntakeStore(context).RevokeLinkAsync(
                    Firm,
                    older,
                    0,
                    SyntheticFixtures.CreatedAtUtc.AddDays(30),
                    Audit(IntakeAuditKind.LinkRevoked, SyntheticFixtures.CreatedAtUtc.AddDays(30)),
                    CancellationToken.None));
        }

        var listed = await ListAsync(database, Firm, 100);

        Assert.Equal(new[] { newer, older }, listed.Cases.Select(entry => entry.CaseId));
    }

    [Fact] // FR-35
    public async Task CasesCreatedAtTheSameInstantFallBackToTheStableCaseIdOrder()
    {
        await using var database = await TwoFirmsAsync();
        var created = new List<Guid>();
        byte digest = 0x31;
        foreach (var caseId in new[]
                 {
                     Guid.Parse("c3c3c3c3-0000-4000-8000-000000000003"),
                     Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001"),
                     Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002"),
                 })
        {
            created.Add(await CreateAsync(
                database, Firm, "Test Taxpayer Tie", minute: 4, digest: digest++, caseId: caseId));
        }

        var first = await ListAsync(database, Firm, 100);
        var second = await ListAsync(database, Firm, 100);

        Assert.Equal(
            created.OrderBy(caseId => caseId).ToArray(),
            first.Cases.Select(entry => entry.CaseId).ToArray());
        Assert.Equal(
            first.Cases.Select(entry => entry.CaseId),
            second.Cases.Select(entry => entry.CaseId));
    }

    [Fact] // AC-69, FR-35
    public async Task TheBoundKeepsTheNewestCasesAndReportsOnlyThatMoreExist()
    {
        await using var database = await TwoFirmsAsync();
        var created = new List<Guid>();
        for (var minute = 1; minute <= 4; minute++)
        {
            created.Add(await CreateAsync(
                database, Firm, "Test Taxpayer Bound", minute, digest: (byte)(0x30 + minute)));
        }

        var bounded = await ListAsync(database, Firm, 3);
        var exact = await ListAsync(database, Firm, 4);
        var roomy = await ListAsync(database, Firm, 5);

        Assert.Equal(
            new[] { created[3], created[2], created[1] },
            bounded.Cases.Select(entry => entry.CaseId));
        Assert.True(bounded.HasMore);

        Assert.Equal(4, exact.Cases.Count);
        Assert.False(exact.HasMore);
        Assert.Equal(4, roomy.Cases.Count);
        Assert.False(roomy.HasMore);
    }

    [Fact] // AC-70
    public async Task AFirmWithNoCaseListsNothing()
    {
        await using var database = await TwoFirmsAsync();
        await CreateAsync(database, OtherFirm, "Test Taxpayer Foreign", minute: 1, digest: 0x31);

        var listed = await ListAsync(database, Firm, 100);

        Assert.Empty(listed.Cases);
        Assert.False(listed.HasMore);
    }

    [Fact] // AC-68, FR-30
    public async Task AFirmAndAPositiveBoundAreRequired()
    {
        await using var database = await TwoFirmsAsync();
        await using var context = database.CreateContext();
        var store = new SqlIntakeStore(context);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.ListCasesAsync(null!, 100, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.ListCasesAsync(Firm, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.ListCasesAsync(Firm, -1, CancellationToken.None));
    }

    [Fact] // FR-30
    public void BothQueriesLeadWithTheFirmAndTheReadWritesNothing()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "src", "Persistence", "Repositories", "SqlIntakeStore.cs"));
        var start = source.IndexOf("public async Task<IntakeCaseList> ListCasesAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<StoredIntakeCase?> FindForClientAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("audit.FirmId == firmId &&", method, StringComparison.Ordinal);
        Assert.Contains("intakeCase.FirmId == firmId &&", method, StringComparison.Ordinal);
        Assert.Contains("audit.Kind == IntakeAuditKind.CaseCreated", method, StringComparison.Ordinal);

        // A read: it opens no transaction and writes nothing.
        foreach (var forbidden in new[]
                 {
                     "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction",
                     "_context.Add", "FromSql",
                 })
        {
            Assert.DoesNotContain(forbidden, method, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<RelationalTestContext> TwoFirmsAsync()
    {
        var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        context.Add(SyntheticFixtures.Firm(
            SyntheticFixtures.OtherFirmId,
            "Example Firm B"));
        await context.SaveChangesAsync();
        return database;
    }

    private static async Task<IntakeCaseList> ListAsync(
        RelationalTestContext database,
        TrustedFirmId firm,
        int maximumCases)
    {
        // A fresh context per read, as the portal's scoped registration gives.
        await using var context = database.CreateContext();
        return await new SqlIntakeStore(context)
            .ListCasesAsync(firm, maximumCases, CancellationToken.None);
    }

    private static async Task<Guid> CreateAsync(
        RelationalTestContext database,
        TrustedFirmId firm,
        string displayName,
        int minute,
        byte digest,
        Guid? caseId = null)
    {
        var selectedCaseId = caseId ?? Guid.NewGuid();
        var createdAtUtc = SyntheticFixtures.CreatedAtUtc.AddMinutes(minute);
        var stored = new StoredIntakeCase(
            firm,
            selectedCaseId,
            Guid.NewGuid(),
            displayName,
            2025,
            CaseStatus.InformationGathering,
            0,
            [new IntakeExpectedItemRecord(Guid.NewGuid(), DocumentCategory.T4Slip, 1)],
            new IntakeLinkRecord(
                Guid.NewGuid(),
                SyntheticFixtures.Digest(digest),
                createdAtUtc,
                createdAtUtc.AddDays(2),
                null,
                null),
            [],
            [],
            [],
            [],
            null);

        await using var context = database.CreateContext();
        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await new SqlIntakeStore(context).CreateAsync(
                new NewIntakeCase(
                    stored,
                    [
                        Audit(IntakeAuditKind.CaseCreated, createdAtUtc),
                        Audit(IntakeAuditKind.ChecklistConfirmed, createdAtUtc),
                        Audit(IntakeAuditKind.LinkIssued, createdAtUtc),
                    ]),
                CancellationToken.None));
        return selectedCaseId;
    }

    private static IntakeAuditEventRecord Audit(
        IntakeAuditKind kind,
        DateTimeOffset occurredAtUtc) =>
        new(
            Guid.NewGuid(),
            0,
            kind,
            IntakeActorKind.Accountant,
            Guid.NewGuid(),
            null,
            occurredAtUtc);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
