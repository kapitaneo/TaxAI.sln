using System.Net;
using TaxAI.Portal.Tests.Production;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 016 T06 — the recovery journey across the real portal, the real
/// recovery orchestrator and the real Azure control ledger (over controlled
/// in-memory Canadian accounts) (AC-102, AC-113, AC-115; DEP-19, DEP-20).
/// <para>
/// The HTTP host is the real composition with its real authentication,
/// antiforgery and firm-scoped authorization; only its SQL-backed collaborators
/// are the established in-memory doubles. The relational revocation, replay and
/// fence semantics the reconciler applies are proved separately on a real
/// relational database (<c>Persistence.Tests/Lifecycle/RecoveryQuarantineTests</c>);
/// here the same revocation is applied to the host's session store, so the
/// proof is that the portal itself refuses a pre-recovery cookie.
/// </para>
/// </summary>
[Collection("Extraction network observation")]
public sealed class ProductionRecoveryAcceptanceTests
{
    [Fact] // AC-113, DEP-20: a copied pre-recovery cookie is refused; a fresh sign-in works.
    public async Task APreRecoveryCookieIsRefusedAfterReconciliationAndAFreshSignInWorks()
    {
        await using var world = IntakeWorld.Create();
        using var preRecovery = world.FirmOne.CreateAnonymousClient();
        await world.FirmOne.SignInAsync(preRecovery);
        Assert.Equal(HttpStatusCode.OK, (await preRecovery.GetAsync("/intake-cases/new")).StatusCode);

        var harness = RecoveryOrchestratorTests.Harness.Restored();
        var sessions = world.FirmOne.Sessions;
        harness.Reconciler.RevokeSessions = async startedAt =>
        {
            var live = sessions.All.Where(session => session.RevokedAtUtc is null && session.CreatedAtUtc < startedAt).ToArray();
            foreach (var session in live)
            {
                await sessions.RevokeAsync(session, startedAt, CancellationToken.None);
            }

            return live.Length;
        };
        harness.Clock = new SettableClock(DateTimeOffset.UtcNow.AddMinutes(1));
        harness.Accounts.ClearAttestations();
        harness.Accounts.PutAttestation(harness.Clock.GetUtcNow().AddMinutes(-5), (RecoveryOrchestratorTests.Firm.Value, RecoveryOrchestratorTests.User));
        harness.Accounts.Put(
            ControlledControlAccounts.EastHost,
            TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore.LedgerContainerName,
            TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore.GenesisName,
            TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore.Serialize(
                RecoveryControlRecord.Genesis(RecoveryOrchestratorTests.Incarnation('e'), harness.Clock.GetUtcNow().AddDays(-60))));
        harness.Reconciler.Extent = RecoveredDataExtent.Observed(true, harness.Clock.GetUtcNow().AddHours(-1));

        var result = await harness.RunAsync();

        Assert.True(result.IsReleased);
        Assert.True(result.WasReconciled);
        Assert.NotEqual(HttpStatusCode.OK, (await preRecovery.GetAsync("/intake-cases/new")).StatusCode);

        using var fresh = world.FirmOne.CreateAnonymousClient();
        await world.FirmOne.SignInAsync(fresh);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/intake-cases/new")).StatusCode);
    }

    [Fact] // A released incarnation never logs everyone out again on scale-out or restart.
    public async Task ALaterInstanceOfTheReleasedIncarnationStartsWithoutRevokingAnything()
    {
        var first = RecoveryOrchestratorTests.Harness.Restored();
        Assert.True((await first.RunAsync()).IsReleased);

        var later = new RecoveryOrchestratorTests.Harness(first.Accounts);
        var result = await later.RunAsync();

        Assert.True(result.IsReleased);
        Assert.False(result.WasReconciled);
        Assert.DoesNotContain("invalidate", later.Reconciler.Calls);
    }

    [Fact] // AC-102: a release never carries over to a later restore of the same database.
    public async Task AFreshRestoreOfAReleasedDatabaseIsQuarantinedUntilReconciledAgain()
    {
        var released = RecoveryOrchestratorTests.Harness.Restored();
        Assert.True((await released.RunAsync()).IsReleased);

        var restoredAgain = new RecoveryOrchestratorTests.Harness(released.Accounts);
        restoredAgain.Reconciler.IncarnationValue = RecoveryOrchestratorTests.Incarnation('b');
        restoredAgain.Accounts.ClearAttestations();

        var quarantined = await restoredAgain.RunAsync();
        Assert.False(quarantined.IsReleased);
        Assert.Contains(RecoveryQuarantineReason.AuthorizationAttestationMissing, quarantined.Reasons);

        restoredAgain.Accounts.PutAttestation(RecoveryOrchestratorTests.Now.AddMinutes(-10), (RecoveryOrchestratorTests.Firm.Value, RecoveryOrchestratorTests.User));
        var reconciled = await restoredAgain.RunAsync();
        Assert.True(reconciled.IsReleased);
        Assert.True(reconciled.WasReconciled);
    }

    [Fact] // AC-102, DEP-19: every acknowledged terminal fact is replayed, in its own firm.
    public async Task EveryAcknowledgedTerminalFactIsReplayedBeforeRelease()
    {
        var harness = RecoveryOrchestratorTests.Harness.Restored();
        var ledger = harness.Ledger();
        var sources = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var resource in sources)
        {
            Assert.Equal(
                RecoveryControlAppendOutcome.Acknowledged,
                await ledger.AppendAsync(
                    RecoveryControlRecord.TerminalSource(RecoveryOrchestratorTests.Firm, Guid.NewGuid(), resource, RecoveryOrchestratorTests.Now.AddDays(-5)),
                    CancellationToken.None));
        }

        var result = await harness.RunAsync();

        Assert.True(result.IsReleased);
        Assert.Equal(sources.OrderBy(id => id), harness.Purger.Purged.Intersect(sources).OrderBy(id => id));
        var invalidation = harness.Reconciler.Calls.IndexOf("invalidate");
        Assert.All(sources, resource => Assert.True(harness.Reconciler.Calls.IndexOf($"source:{resource}") < invalidation));
    }
}
