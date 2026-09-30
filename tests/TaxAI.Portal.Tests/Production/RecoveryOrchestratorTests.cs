using System.Net;
using System.Reflection;
using System.Security;
using System.Text;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Control;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T06 — the recovery orchestrator (FR-10, FR-11; AC-102, AC-110,
/// AC-113 to AC-116, AC-165; DEP-19, DEP-20).
/// <para>
/// The real orchestrator runs over the real Azure control ledger on a
/// controlled in-memory pair of Canadian accounts, with a recording reconciler,
/// registry and purger standing in for the recovered database and source
/// storage. Every quarantine reason is produced by the real release policy and
/// the real ledger; nothing is skipped to meet an objective.
/// </para>
/// </summary>
public sealed class RecoveryOrchestratorTests
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    internal static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000081"));
    internal static readonly Guid User = Guid.Parse("dddddddd-0000-4000-8000-000000000081");

    internal static DatabaseIncarnation Incarnation(char fill) =>
        DatabaseIncarnation.TryCreate(new string(fill, 64), out var incarnation) ? incarnation! : throw new InvalidOperationException();

    // ------------------------------------------------------ fast paths

    [Fact] // A released incarnation starts without touching the database's data.
    public async Task AnAlreadyReleasedIncarnationStartsWithoutReconciliation()
    {
        var harness = new Harness();
        await harness.Ledger().AppendAsync(RecoveryControlRecord.Release(Incarnation('a'), Now), CancellationToken.None);

        var result = await harness.RunAsync();

        Assert.True(result.IsReleased);
        Assert.False(result.WasReconciled);
        Assert.Empty(harness.Reconciler.Calls.Where(call => call != "incarnation"));
    }

    [Fact] // First deployment: an empty ledger beside a pristine database.
    public async Task APristineEnvironmentWritesGenesisAndIsReleased()
    {
        var harness = new Harness();
        harness.Reconciler.Extent = RecoveredDataExtent.Observed(false, null);

        var result = await harness.RunAsync();

        Assert.True(result.IsReleased);
        Assert.True(await harness.Ledger().HasReleaseAsync(Incarnation('a'), CancellationToken.None));
        Assert.DoesNotContain("invalidate", harness.Reconciler.Calls);

        // A second instance of the same incarnation is simply released.
        Assert.True((await new Harness(harness.Accounts).RunAsync()).IsReleased);
    }

    [Fact] // AC-115: missing purge control history keeps the environment quarantined.
    public async Task AnEmptyLedgerBesideExistingDataIsQuarantined()
    {
        var harness = new Harness();

        var result = await harness.RunAsync();

        Assert.False(result.IsReleased);
        Assert.Contains(RecoveryQuarantineReason.ControlGenesisMissing, result.Reasons);
        Assert.DoesNotContain("invalidate", harness.Reconciler.Calls);
        Assert.Equal(0, harness.Accounts.Count(ControlledControlAccounts.EastHost, AzureRecoveryControlStore.LedgerContainerName));
    }

    // ------------------------------------------------ full reconciliation

    [Fact] // AC-102, AC-113, DEP-19, DEP-20: the complete ordered reconciliation.
    public async Task ARestoredIncarnationIsReplayedInvalidatedReattestedAndOnlyThenReleased()
    {
        var harness = Harness.Restored();
        var resource = Guid.NewGuid();
        var record = Guid.NewGuid();
        await harness.Ledger().AppendAsync(RecoveryControlRecord.TerminalSource(Firm, Guid.Parse("12121212-1212-4212-8212-121212121212"), resource, Now.AddDays(-3)), CancellationToken.None);
        await harness.Ledger().AppendAsync(RecoveryControlRecord.TerminalStructured(Firm, record, Now.AddDays(-2)), CancellationToken.None);

        var result = await harness.RunAsync();

        Assert.True(result.IsReleased);
        Assert.True(result.WasReconciled);
        Assert.Equal(
            new[] { "incarnation", "extent", $"source:{resource}", $"structured:{record}", "invalidate", "attest" },
            harness.Reconciler.Calls);
        Assert.Equal(new[] { resource }, harness.Purger.Purged);
        Assert.Equal(Now, harness.Reconciler.InvalidatedAt);
        Assert.True(await harness.Ledger().HasReleaseAsync(Incarnation('a'), CancellationToken.None));
    }

    public static TheoryData<string, RecoveryQuarantineReason> Quarantines() => new()
    {
        { "database incarnation unknown", RecoveryQuarantineReason.DatabaseIncarnationUnavailable },
        { "ledger unreachable", RecoveryQuarantineReason.ControlLedgerUnavailable },
        { "ledger corrupt", RecoveryQuarantineReason.ControlLedgerInvalid },
        { "ledger read went stale", RecoveryQuarantineReason.ControlEvidenceStale },
        { "restored data predates the ledger", RecoveryQuarantineReason.RecoveryIntervalUncovered },
        { "restored data older than any backup", RecoveryQuarantineReason.RecoveredDataOutsideBackupWindow },
        { "no current authorization attestation", RecoveryQuarantineReason.AuthorizationAttestationMissing },
        { "attestation predates the outage", RecoveryQuarantineReason.AuthorizationAttestationStale },
        { "source object deletion unverifiable", RecoveryQuarantineReason.TerminalReplayIncomplete },
        { "source registry unwritable", RecoveryQuarantineReason.TerminalReplayIncomplete },
        { "review record still referenced", RecoveryQuarantineReason.TerminalReplayIncomplete },
        { "registry tombstone deletion unverifiable", RecoveryQuarantineReason.RegistryReplayIncomplete },
        { "session revocation failed", RecoveryQuarantineReason.AccessInvalidationIncomplete },
        { "release not acknowledged", RecoveryQuarantineReason.ReleaseUnacknowledged },
        { "recovery windows invalid", RecoveryQuarantineReason.SecurityConfigurationInvalid },
    };

    [Theory] // The verification list: every failure quarantines, and nothing is released.
    [MemberData(nameof(Quarantines))]
    public async Task EachFailureQuarantinesAndReleasesNothing(string scenario, RecoveryQuarantineReason expected)
    {
        var harness = Harness.Restored();
        await harness.Ledger().AppendAsync(
            RecoveryControlRecord.TerminalSource(Firm, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1)), CancellationToken.None);
        await harness.Ledger().AppendAsync(RecoveryControlRecord.TerminalStructured(Firm, Guid.NewGuid(), Now.AddDays(-1)), CancellationToken.None);

        switch (scenario)
        {
            case "database incarnation unknown": harness.Reconciler.IncarnationValue = null; break;
            case "ledger unreachable": harness.Accounts.Unreachable.Add(ControlledControlAccounts.EastHost); break;
            case "ledger corrupt":
                harness.Accounts.Put(ControlledControlAccounts.EastHost, AzureRecoveryControlStore.LedgerContainerName, "terminal/2026-09-28/x.json", "{}"u8.ToArray());
                break;
            case "ledger read went stale": harness.LedgerClock = new SettableClock(Now.AddMinutes(-20)); break;
            case "restored data predates the ledger":
                harness.Reconciler.Extent = RecoveredDataExtent.Observed(true, Now.AddDays(-85));
                break;
            case "restored data older than any backup":
                harness.Reconciler.Extent = RecoveredDataExtent.Observed(true, Now.AddDays(-50));
                break;
            case "no current authorization attestation": harness.Accounts.ClearAttestations(); break;
            case "attestation predates the outage":
                harness.Accounts.ClearAttestations();
                harness.Accounts.PutAttestation(Now.AddHours(-3), (Firm.Value, User));
                break;
            case "source object deletion unverifiable": harness.Purger.Outcome = SourceObjectPurgeOutcome.Unavailable; break;
            case "source registry unwritable": harness.Reconciler.SourceOutcome = RecoveryReplayOutcome.Incomplete; break;
            case "review record still referenced": harness.Reconciler.StructuredOutcome = RecoveryReplayOutcome.Blocked; break;
            case "registry tombstone deletion unverifiable":
                harness.Registry.Pending.Add((Firm, Guid.NewGuid(), Guid.NewGuid()));
                harness.Purger.TombstoneOutcome = SourceObjectPurgeOutcome.Unavailable;
                break;
            case "session revocation failed": harness.Reconciler.FailInvalidation = true; break;
            case "release not acknowledged": harness.Accounts.RefuseWritesTo.Add(ControlledControlAccounts.EastHost); break;
            default: harness.Options = Harness.OptionsWith(ledgerDays: "42"); break;
        }

        var result = await harness.RunAsync();

        Assert.False(result.IsReleased);
        Assert.Contains(expected, result.Reasons);
        harness.Accounts.Unreachable.Clear();
        Assert.False(await harness.Ledger().HasReleaseAsync(Incarnation('a'), CancellationToken.None));
    }

    [Fact] // A precondition failure is decided before anything is changed.
    public async Task APreconditionFailureChangesNothingInTheRecoveredDatabase()
    {
        var harness = Harness.Restored();
        harness.Accounts.ClearAttestations();

        await harness.RunAsync();

        Assert.Equal(new[] { "incarnation", "extent" }, harness.Reconciler.Calls);
        Assert.Empty(harness.Purger.Purged);
    }

    [Fact] // AC-165: a failure still performs every other step, but releases nothing.
    public async Task AFailedStepIsNeverSkippedToMeetTheObjective()
    {
        var harness = Harness.Restored();
        await harness.Ledger().AppendAsync(RecoveryControlRecord.TerminalStructured(Firm, Guid.NewGuid(), Now.AddDays(-1)), CancellationToken.None);
        harness.Reconciler.StructuredOutcome = RecoveryReplayOutcome.Blocked;

        var result = await harness.RunAsync();

        Assert.False(result.IsReleased);
        Assert.Contains("invalidate", harness.Reconciler.Calls);
        Assert.Contains("attest", harness.Reconciler.Calls);
    }

    [Fact] // Registry tombstones of attested firms are cleaned again, scoped per firm.
    public async Task RegistryTombstonesOfAttestedFirmsAreReplayedThroughTheScopedPurger()
    {
        var harness = Harness.Restored();
        var tombstone = (Firm, Guid.NewGuid(), Guid.NewGuid());
        var foreign = (TrustedFirmId.FromGuid(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid());
        harness.Registry.Pending.Add(tombstone);
        harness.Registry.Pending.Add(foreign);

        var result = await harness.RunAsync();

        Assert.True(result.IsReleased);
        Assert.Contains(tombstone.Item3, harness.Purger.Purged);
        Assert.DoesNotContain(foreign.Item3, harness.Purger.Purged);
        Assert.Equal(new[] { tombstone.Item3 }, harness.Registry.Recorded);
    }

    [Fact] // AC-110: a source lost to asynchronous replication is not a quarantine.
    public async Task ALostSourceDuringReplayIsObservedAbsentNotRecreated()
    {
        var harness = Harness.Restored();
        await harness.Ledger().AppendAsync(
            RecoveryControlRecord.TerminalSource(Firm, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1)), CancellationToken.None);
        harness.Purger.Outcome = SourceObjectPurgeOutcome.Pending;

        Assert.True((await harness.RunAsync()).IsReleased);
    }

    // -------------------------------------------- startup refusal wrapper

    [Fact]
    public void RequireReleaseThrowsOneFixedMessageWhenQuarantined()
    {
        var harness = Harness.Restored();
        harness.Accounts.ClearAttestations();
        var provider = harness.HostProvider();

        var failure = Assert.Throws<TargetInvocationException>(() => Harness.RequireRelease(provider, harness.Options));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(Harness.QuarantineMessage, failure.InnerException!.Message);
        Assert.DoesNotContain(Firm.Value.ToString("D"), failure.InnerException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequireReleaseRefusesWithoutAnOrchestratorOrPastItsDeadline()
    {
        var empty = new ServiceCollection().BuildServiceProvider();
        Assert.Equal(Harness.QuarantineMessage,
            Assert.Throws<TargetInvocationException>(() => Harness.RequireRelease(empty, Harness.OptionsWith())).InnerException!.Message);

        var harness = Harness.Restored();
        harness.Reconciler.Hang = true;
        harness.Options = Harness.OptionsWith(deadline: "00:00:01");
        Assert.Equal(Harness.QuarantineMessage,
            Assert.Throws<TargetInvocationException>(() => Harness.RequireRelease(harness.HostProvider(), harness.Options)).InnerException!.Message);
    }

    [Fact]
    public void RequireReleaseAdmitsAReleasedHost()
    {
        var harness = new Harness();
        harness.Reconciler.Extent = RecoveredDataExtent.Observed(false, null);

        Assert.Null(Record.Exception(() => Harness.RequireRelease(harness.HostProvider(), harness.Options)));
    }

    // ----------------------------------------------------------- harness

    /// <summary>Drives the internal orchestrator through reflection.</summary>
    internal sealed class Harness
    {
        private static readonly Type OrchestratorType = typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.RecoveryOrchestrator", throwOnError: true)!;

        internal static string QuarantineMessage =>
            (string)OrchestratorType.GetField("QuarantineMessage", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;

        public Harness(ControlledControlAccounts? accounts = null)
        {
            Accounts = accounts ?? new ControlledControlAccounts();
        }

        public ControlledControlAccounts Accounts { get; }

        public RecordingReconciler Reconciler { get; } = new();

        public RecordingRegistry Registry { get; } = new();

        public RecordingPurger Purger { get; } = new();

        public SettableClock Clock { get; set; } = new(Now);

        public SettableClock? LedgerClock { get; set; }

        public AzureInfrastructureOptions Options { get; set; } = OptionsWith();

        /// <summary>A restored database: data, a genesis long before it, a current attestation.</summary>
        public static Harness Restored()
        {
            var harness = new Harness();
            harness.Reconciler.Extent = RecoveredDataExtent.Observed(true, Now.AddHours(-2));
            harness.Accounts.Put(
                ControlledControlAccounts.EastHost,
                AzureRecoveryControlStore.LedgerContainerName,
                AzureRecoveryControlStore.GenesisName,
                AzureRecoveryControlStore.Serialize(RecoveryControlRecord.Genesis(Incarnation('e'), Now.AddDays(-80))));
            harness.Accounts.PutAttestation(Now.AddMinutes(-30), (Firm.Value, User));
            return harness;
        }

        public static AzureInfrastructureOptions OptionsWith(string ledgerDays = "90", string deadline = "00:02:00") => new()
        {
            Resilience = new ResilienceSettings { OperationDeadline = deadline },
            Recovery = new RecoverySettings
            {
                Profile = "Primary",
                SqlPointInTimeRestoreDays = "35",
                BackupChainDays = "7",
                ControlLedgerRetentionDays = ledgerDays,
            },
        };

        public AzureRecoveryControlStore Ledger() => Accounts.Store(LedgerClock ?? Clock);

        public ServiceProvider Scopes()
        {
            var services = new ServiceCollection();
            services.AddScoped<IRecoveryReconciler>(_ => Reconciler);
            services.AddScoped<ISourceRegistryStore>(_ => Registry);
            services.AddSingleton<ISourceObjectPurger>(Purger);
            return services.BuildServiceProvider();
        }

        public object Orchestrator() => Activator.CreateInstance(
            OrchestratorType,
            Scopes().GetRequiredService<IServiceScopeFactory>(),
            Ledger(),
            Clock,
            Options)!;

        /// <summary>A host container holding exactly the orchestrator.</summary>
        public ServiceProvider HostProvider()
        {
            var orchestrator = Orchestrator();
            var services = new ServiceCollection();
            services.AddSingleton(OrchestratorType, orchestrator);
            return services.BuildServiceProvider();
        }

        public async Task<Outcome> RunAsync()
        {
            var task = (Task)OrchestratorType
                .GetMethod("EnsureReleasedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Orchestrator(), new object[] { CancellationToken.None })!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            var type = result.GetType();
            T Read<T>(string name) => (T)type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(result)!;
            Assert.Equal("RecoveryOrchestration", result.ToString());
            return new Outcome(
                Read<bool>("IsReleased"),
                Read<bool>("WasReconciled"),
                Read<IReadOnlyList<RecoveryQuarantineReason>>("Reasons"));
        }

        public static void RequireRelease(IServiceProvider provider, AzureInfrastructureOptions options) =>
            OrchestratorType
                .GetMethod("RequireRelease", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { provider, options });
    }

    internal sealed record Outcome(bool IsReleased, bool WasReconciled, IReadOnlyList<RecoveryQuarantineReason> Reasons);
}

internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A reconciler that records every call in order and fails on request.</summary>
internal sealed class RecordingReconciler : IRecoveryReconciler
{
    public List<string> Calls { get; } = new();

    public DatabaseIncarnation? IncarnationValue { get; set; } = RecoveryOrchestratorTests.Incarnation('a');

    public RecoveredDataExtent Extent { get; set; } = RecoveredDataExtent.Observed(true, RecoveryOrchestratorTests.Now.AddHours(-2));

    public RecoveryReplayOutcome SourceOutcome { get; set; } = RecoveryReplayOutcome.Applied;

    public RecoveryReplayOutcome StructuredOutcome { get; set; } = RecoveryReplayOutcome.Applied;

    public bool FailInvalidation { get; set; }

    public bool Hang { get; set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    /// <summary>Applies the revocation to a real session store, when a test supplies one.</summary>
    public Func<DateTimeOffset, Task<int>>? RevokeSessions { get; set; }

    public async Task<DatabaseIncarnation?> ReadIncarnationAsync(CancellationToken cancellationToken)
    {
        Calls.Add("incarnation");
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return IncarnationValue;
    }

    public Task<RecoveredDataExtent> ReadDataExtentAsync(CancellationToken cancellationToken)
    {
        Calls.Add("extent");
        return Task.FromResult(Extent);
    }

    public Task<RecoveryReplayOutcome> ReplayTerminalSourceAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, DateTimeOffset replayedAtUtc, CancellationToken cancellationToken)
    {
        Calls.Add($"source:{resourceId}");
        return Task.FromResult(SourceOutcome);
    }

    public Task<RecoveryReplayOutcome> ReplayTerminalStructuredAsync(TrustedFirmId firm, Guid recordId, CancellationToken cancellationToken)
    {
        Calls.Add($"structured:{recordId}");
        return Task.FromResult(StructuredOutcome);
    }

    public Task<RecoveryAccessInvalidation> InvalidatePreRecoveryAccessAsync(DateTimeOffset recoveryStartedAtUtc, CancellationToken cancellationToken)
    {
        Calls.Add("invalidate");
        InvalidatedAt = recoveryStartedAtUtc;
        if (FailInvalidation)
        {
            throw new InvalidOperationException("synthetic storage failure");
        }

        return RevokeSessions is null
            ? Task.FromResult(new RecoveryAccessInvalidation(1, 1, 1))
            : RevokeAsync(recoveryStartedAtUtc);
    }

    private async Task<RecoveryAccessInvalidation> RevokeAsync(DateTimeOffset recoveryStartedAtUtc) =>
        new(await RevokeSessions!(recoveryStartedAtUtc), 0, 0);

    public Task<RecoveryMembershipReconciliation> ApplyAuthorizationAttestationAsync(RecoveryAuthorizationAttestation attestation, DateTimeOffset appliedAtUtc, CancellationToken cancellationToken)
    {
        Calls.Add("attest");
        return Task.FromResult(new RecoveryMembershipReconciliation(0, 0));
    }

    public override string ToString() => nameof(RecordingReconciler);
}

/// <summary>A registry holding pending tombstones; every read is firm-scoped.</summary>
internal sealed class RecordingRegistry : ISourceRegistryStore
{
    public List<(TrustedFirmId Firm, Guid Case, Guid Resource)> Pending { get; } = new();

    public List<Guid> Recorded { get; } = new();

    public Task<SourceRegistryWriteCode> RegisterStagedAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, string contentKey, DateTimeOffset stagedAtUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SourceRegistration?> FindAsync(TrustedFirmId firm, Guid caseId, string contentKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SourceRegistration?> FindBoundAsync(TrustedFirmId firm, Guid caseId, Guid documentId, string contentKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<SourceRegistryWriteCode> RecordPurgeAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, SourceObjectPurgeOutcome outcome, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        Recorded.Add(resourceId);
        return Task.FromResult(SourceRegistryWriteCode.Success);
    }

    public Task<IReadOnlyList<SourceRegistration>> ListUncommittedAsync(TrustedFirmId firm, DateTimeOffset stagedBeforeUtc, int maximumCount, CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceRegistration> work = Pending
            .Where(item => item.Firm.Equals(firm))
            .Select(item => new SourceRegistration(item.Firm, item.Case, item.Resource, "k" + item.Resource.ToString("N"),
                SourceRegistryState.PurgePending, stagedBeforeUtc.AddDays(-1), null, null, stagedBeforeUtc.AddDays(-1)))
            .ToArray();
        return Task.FromResult(work);
    }
}

/// <summary>A purger that reports a configured outcome and records what it was asked to delete.</summary>
internal sealed class RecordingPurger : ISourceObjectPurger
{
    public List<Guid> Purged { get; } = new();

    public SourceObjectPurgeOutcome Outcome { get; set; } = SourceObjectPurgeOutcome.Pending;

    public SourceObjectPurgeOutcome? TombstoneOutcome { get; set; }

    public Task<SourceObjectPurgeOutcome> PurgeAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, CancellationToken cancellationToken)
    {
        Purged.Add(resourceId);
        return Task.FromResult(TombstoneOutcome ?? Outcome);
    }
}

/// <summary>
/// An in-memory pair of private control accounts for the portal tests: bearer
/// token only, create-only writes, reads, listings and container properties.
/// </summary>
internal sealed class ControlledControlAccounts : HttpMessageHandler
{
    internal const string CentralHost = "sttaxailedcc.blob.core.windows.net";
    internal const string EastHost = "sttaxailedce.blob.core.windows.net";
    internal const string ApprovedToken = "synthetic-control-token";

    private readonly object _gate = new();
    private readonly Dictionary<(string Host, string Container, string Name), byte[]> _objects = new();

    public HashSet<string> Unreachable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> RefuseWritesTo { get; } = new(StringComparer.OrdinalIgnoreCase);

    public BlobServiceClient Service(string host)
    {
        var options = new BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)) };
        options.Retry.MaxRetries = 0;
        return new BlobServiceClient(new Uri($"https://{host}/"), new StaticToken(), options);
    }

    public AzureRecoveryControlStore Store(TimeProvider clock) => new(Service(CentralHost), Service(EastHost), clock);

    public AzureProductionEvidenceReader Reader() => new(Service(CentralHost), Service(EastHost));

    public int Count(string host, string container)
    {
        lock (_gate)
        {
            return _objects.Keys.Count(key => key.Host == host && key.Container == container);
        }
    }

    public void Put(string host, string container, string name, byte[] content)
    {
        lock (_gate)
        {
            _objects[(host, container, name)] = content.ToArray();
        }
    }

    public void ClearAttestations()
    {
        lock (_gate)
        {
            foreach (var key in _objects.Keys.Where(key => key.Container == AzureRecoveryControlStore.AttestationContainerName).ToArray())
            {
                _objects.Remove(key);
            }
        }
    }

    public void PutAttestation(DateTimeOffset recordedAtUtc, params (Guid Firm, Guid User)[] memberships)
    {
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            recordedAtUtc = recordedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            memberships = memberships.Select(pair => new { firmId = pair.Firm.ToString("D"), userId = pair.User.ToString("D") }).ToArray(),
        });
        foreach (var host in new[] { CentralHost, EastHost })
        {
            Put(host, AzureRecoveryControlStore.AttestationContainerName, AzureRecoveryControlStore.AttestationNameOf(recordedAtUtc), body);
        }
    }

    /// <summary>Publishes an evidence document for a binding to both accounts.</summary>
    public void PublishEvidence(ProductionReleaseBinding release, string manifest, string prefix = "release/")
    {
        foreach (var host in new[] { CentralHost, EastHost })
        {
            Put(host, AzureProductionEvidenceReader.EvidenceContainerName,
                prefix + AzureProductionEvidenceReader.BindingKey(release) + ".json", Encoding.UTF8.GetBytes(manifest));
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri!.Host;
        if (host != CentralHost && host != EastHost)
        {
            throw new HttpRequestException("Unexpected destination.");
        }

        if (Unreachable.Contains(host))
        {
            throw new HttpRequestException("Synthetic regional outage.");
        }

        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: ApprovedToken })
        {
            return Task.FromResult(Error(HttpStatusCode.Forbidden, "AuthorizationFailure"));
        }

        var path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath).TrimStart('/');
        var slash = path.IndexOf('/');
        var container = slash < 0 ? path : path[..slash];
        var name = slash < 0 ? string.Empty : path[(slash + 1)..];
        var query = request.RequestUri.Query;

        lock (_gate)
        {
            if (query.Contains("comp=list", StringComparison.Ordinal))
            {
                var prefix = QueryValue(query, "prefix") ?? string.Empty;
                var blobs = _objects
                    .Where(entry => entry.Key.Host == host && entry.Key.Container == container &&
                                    entry.Key.Name.StartsWith(prefix, StringComparison.Ordinal))
                    .OrderBy(entry => entry.Key.Name, StringComparer.Ordinal)
                    .Select(entry =>
                        $"<Blob><Name>{SecurityElement.Escape(entry.Key.Name)}</Name><Properties>" +
                        "<Last-Modified>Tue, 29 Sep 2026 12:00:00 GMT</Last-Modified><Etag>0x1</Etag>" +
                        $"<Content-Length>{entry.Value.Length}</Content-Length><BlobType>BlockBlob</BlobType></Properties></Blob>");
                var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                          $"<EnumerationResults ServiceEndpoint=\"https://{host}/\" ContainerName=\"{container}\">" +
                          $"<Prefix>{SecurityElement.Escape(prefix)}</Prefix><Blobs>{string.Concat(blobs)}</Blobs><NextMarker /></EnumerationResults>";
                var list = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };
                list.Headers.TryAddWithoutValidation("x-ms-version", "2025-01-05");
                return Task.FromResult(list);
            }

            if (request.Method == HttpMethod.Put)
            {
                if (RefuseWritesTo.Contains(host))
                {
                    return Task.FromResult(Error(HttpStatusCode.ServiceUnavailable, "ServerBusy"));
                }

                if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == "*") && _objects.ContainsKey((host, container, name)))
                {
                    return Task.FromResult(Error(HttpStatusCode.Conflict, "BlobAlreadyExists"));
                }

                _objects[(host, container, name)] = request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
                return Task.FromResult(WithBlobHeaders(new HttpResponseMessage(HttpStatusCode.Created) { Content = new ByteArrayContent([]) }));
            }

            if (!_objects.TryGetValue((host, container, name), out var content))
            {
                return Task.FromResult(Error(HttpStatusCode.NotFound, "BlobNotFound"));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content.ToArray()) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            response.Content.Headers.ContentLength = content.Length;
            return Task.FromResult(WithBlobHeaders(response));
        }
    }

    private static string? QueryValue(string query, string key) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => string.Equals(pair[0], key, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty)
            .FirstOrDefault();

    private static HttpResponseMessage WithBlobHeaders(HttpResponseMessage response)
    {
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"0x{Guid.NewGuid():N}\"");
        response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        response.Headers.TryAddWithoutValidation("x-ms-version", "2025-01-05");
        response.Headers.TryAddWithoutValidation("Last-Modified", "Tue, 29 Sep 2026 12:00:00 GMT");
        response.Content.Headers.TryAddWithoutValidation("Last-Modified", "Tue, 29 Sep 2026 12:00:00 GMT");
        return response;
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string code)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Error><Code>{code}</Code><Message>synthetic refusal</Message></Error>",
                Encoding.UTF8,
                "application/xml"),
        };
        response.Headers.TryAddWithoutValidation("x-ms-error-code", code);
        return response;
    }

    private sealed class StaticToken : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(ApprovedToken, DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
