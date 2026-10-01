using System.Net;
using System.Security;
using System.Text;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.DataProtection;
using TaxAI.AzureInfrastructure;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Control;
using TaxAI.AzureInfrastructure.Tests.Configuration;
using TaxAI.AzureInfrastructure.Tests.Content;
using TaxAI.AzureInfrastructure.Tests.Keys;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Tests.Control;

/// <summary>
/// Spec 016 T06 — the independent recovery control ledger over two controlled
/// in-memory Canadian Blob accounts (FR-10, FR-11; AC-102, AC-103, AC-115).
/// <para>
/// The real <see cref="AzureRecoveryControlStore"/> runs over a controlled
/// transport that accepts only a bearer token, enforces create-only writes and
/// can make either account unreachable or tamper with a stored object. Every
/// acknowledgement asserted here was observed by reading the copy back.
/// </para>
/// </summary>
public sealed class RecoveryControlStoreTests
{
    internal static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000061"));
    internal static readonly Guid Case = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000061");
    internal static readonly Guid Resource = Guid.Parse("cccccccc-0000-4000-8000-000000000061");
    internal static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    internal static DatabaseIncarnation Incarnation(char fill = 'a') =>
        DatabaseIncarnation.TryCreate(new string(fill, 64), out var incarnation) ? incarnation! : throw new InvalidOperationException();

    // ------------------------------------------------------------ appends

    [Fact] // AC-102: a terminal fact is acknowledged only when both copies hold it.
    public async Task ATerminalFactIsAcknowledgedOnlyAfterBothCopiesAreReadBack()
    {
        var accounts = new ControlledControlAccounts();
        var record = RecoveryControlRecord.TerminalSource(Firm, Case, Resource, Now);

        var outcome = await accounts.Store().AppendAsync(record, CancellationToken.None);

        Assert.Equal(RecoveryControlAppendOutcome.Acknowledged, outcome);
        var name = AzureRecoveryControlStore.NameOf(record);
        Assert.True(accounts.Contains(ControlledControlAccounts.EastHost, AzureRecoveryControlStore.LedgerContainerName, name));
        Assert.True(accounts.Contains(ControlledControlAccounts.CentralHost, AzureRecoveryControlStore.LedgerContainerName, name));

        // Written create-only, then read back from each copy; east first.
        var writes = accounts.Log.Where(entry => entry.Method == "PUT").ToArray();
        Assert.Equal(new[] { ControlledControlAccounts.EastHost, ControlledControlAccounts.CentralHost }, writes.Select(entry => entry.Host));
        Assert.All(writes, entry => Assert.Equal("*", entry.IfNoneMatch));
        Assert.Equal(2, accounts.Log.Count(entry => entry.Method == "GET" && entry.Name == name));
    }

    [Theory]
    [InlineData(ControlledControlAccounts.CentralHost)]
    [InlineData(ControlledControlAccounts.EastHost)]
    public async Task ATerminalFactWithEitherCopyUnreachableIsNotAcknowledged(string unreachable)
    {
        var accounts = new ControlledControlAccounts();
        accounts.Unreachable.Add(unreachable);

        var outcome = await accounts.Store().AppendAsync(
            RecoveryControlRecord.TerminalSource(Firm, Case, Resource, Now),
            CancellationToken.None);

        Assert.Equal(RecoveryControlAppendOutcome.Unacknowledged, outcome);
        if (unreachable == ControlledControlAccounts.EastHost)
        {
            // Nothing is written anywhere before the authoritative copy holds it.
            Assert.DoesNotContain(accounts.Log, entry => entry.Method == "PUT" && entry.Host == ControlledControlAccounts.CentralHost);
        }
    }

    [Fact] // A Canada Central outage cannot block an authorized Canadian recovery.
    public async Task AReleaseNeedsOnlyTheAuthoritativeCanadaEastCopy()
    {
        var accounts = new ControlledControlAccounts();
        accounts.Unreachable.Add(ControlledControlAccounts.CentralHost);

        var outcome = await accounts.Store().AppendAsync(
            RecoveryControlRecord.Release(Incarnation(), Now),
            CancellationToken.None);

        Assert.Equal(RecoveryControlAppendOutcome.Acknowledged, outcome);
    }

    [Fact]
    public async Task AReleaseIsNeverAcknowledgedWithoutCanadaEast()
    {
        var accounts = new ControlledControlAccounts();
        accounts.Unreachable.Add(ControlledControlAccounts.EastHost);

        Assert.Equal(
            RecoveryControlAppendOutcome.Unacknowledged,
            await accounts.Store().AppendAsync(RecoveryControlRecord.Release(Incarnation(), Now), CancellationToken.None));
        Assert.DoesNotContain(accounts.Log, entry => entry.Method == "PUT" && entry.Host == ControlledControlAccounts.CentralHost);
    }

    [Fact] // A retried append of the same fact is idempotent.
    public async Task RetryingTheIdenticalFactIsAcknowledgedWithoutASecondObject()
    {
        var accounts = new ControlledControlAccounts();
        var record = RecoveryControlRecord.TerminalStructured(Firm, Resource, Now);
        var store = accounts.Store();

        Assert.Equal(RecoveryControlAppendOutcome.Acknowledged, await store.AppendAsync(record, CancellationToken.None));
        Assert.Equal(RecoveryControlAppendOutcome.Acknowledged, await store.AppendAsync(record, CancellationToken.None));
        Assert.Equal(1, accounts.Count(ControlledControlAccounts.EastHost, AzureRecoveryControlStore.LedgerContainerName));
    }

    [Fact] // Append-only: a different fact never replaces a stored one.
    public async Task ADifferentFactUnderAnOccupiedIdentityIsAConflictNotAnOverwrite()
    {
        var accounts = new ControlledControlAccounts();
        var store = accounts.Store();
        Assert.Equal(
            RecoveryControlAppendOutcome.Acknowledged,
            await store.AppendAsync(RecoveryControlRecord.Genesis(Incarnation('a'), Now), CancellationToken.None));

        var second = await store.AppendAsync(RecoveryControlRecord.Genesis(Incarnation('b'), Now), CancellationToken.None);

        Assert.Equal(RecoveryControlAppendOutcome.Conflict, second);
        var snapshot = await store.ReadAsync(CancellationToken.None);
        Assert.True(Assert.Single(snapshot.Records).Incarnation!.SameAs(Incarnation('a')));
    }

    [Fact]
    public async Task AStoredObjectThatDiffersOnReadBackIsAConflict()
    {
        var accounts = new ControlledControlAccounts { TamperOnRead = true };

        var outcome = await accounts.Store().AppendAsync(
            RecoveryControlRecord.TerminalSource(Firm, Case, Resource, Now),
            CancellationToken.None);

        Assert.Equal(RecoveryControlAppendOutcome.Conflict, outcome);
    }

    [Fact] // Opaque identifiers only; names hash them.
    public void RecordsCarryOpaqueIdentifiersOnlyAndTerminalNamesHashThem()
    {
        var record = RecoveryControlRecord.TerminalSource(Firm, Case, Resource, Now);
        var name = AzureRecoveryControlStore.NameOf(record);
        var body = Encoding.UTF8.GetString(AzureRecoveryControlStore.Serialize(record));

        Assert.StartsWith("terminal/2026-09-29/", name, StringComparison.Ordinal);
        Assert.DoesNotContain(Firm.Value.ToString("D"), name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Resource.ToString("D"), name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            new[] { "schemaVersion", "kind", "recordedAtUtc", "incarnation", "firmId", "caseId", "resourceId" },
            System.Text.Json.JsonDocument.Parse(body).RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("RecoveryControlRecord(TerminalSource)", record.ToString());
        Assert.Equal(nameof(AzureRecoveryControlStore), new AzureRecoveryControlStore(
            new ControlledControlAccounts().Service(ControlledControlAccounts.CentralHost),
            new ControlledControlAccounts().Service(ControlledControlAccounts.EastHost),
            TimeProvider.System).ToString());
    }

    [Fact]
    public void EveryKindRoundTripsAndAMisplacedRecordIsRejected()
    {
        foreach (var record in new[]
                 {
                     RecoveryControlRecord.Genesis(Incarnation(), Now),
                     RecoveryControlRecord.Release(Incarnation('c'), Now),
                     RecoveryControlRecord.TerminalSource(Firm, Case, Resource, Now),
                     RecoveryControlRecord.TerminalStructured(Firm, Resource, Now),
                 })
        {
            var body = AzureRecoveryControlStore.Serialize(record);
            var parsed = AzureRecoveryControlStore.Parse(body, AzureRecoveryControlStore.NameOf(record));
            Assert.NotNull(parsed);
            Assert.True(parsed!.SameAs(record));
            Assert.Null(AzureRecoveryControlStore.Parse(body, "terminal/2026-09-29/misplaced.json"));
        }
    }

    public static TheoryData<string> MalformedRecords() => new()
    {
        "{}",
        "not json",
        """{"schemaVersion":2,"kind":"Genesis","recordedAtUtc":"2026-09-29T12:00:00.0000000+00:00","incarnation":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","firmId":null,"caseId":null,"resourceId":null}""",
        """{"schemaVersion":1,"kind":"Genesis","recordedAtUtc":"2026-09-29T12:00:00.0000000-05:00","incarnation":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","firmId":null,"caseId":null,"resourceId":null}""",
        """{"schemaVersion":1,"kind":"Genesis","recordedAtUtc":"2026-09-29T12:00:00.0000000+00:00","incarnation":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","firmId":null,"caseId":null,"resourceId":null}""",
        """{"schemaVersion":1,"kind":"Genesis","recordedAtUtc":"2026-09-29T12:00:00.0000000+00:00","incarnation":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","firmId":"aaaaaaaa-0000-4000-8000-000000000061","caseId":null,"resourceId":null}""",
        """{"schemaVersion":1,"kind":"Waived","recordedAtUtc":"2026-09-29T12:00:00.0000000+00:00","incarnation":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","firmId":null,"caseId":null,"resourceId":null}""",
        """{"schemaVersion":1,"kind":"Genesis","recordedAtUtc":"2026-09-29T12:00:00.0000000+00:00","incarnation":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","firmId":null,"caseId":null,"resourceId":null,"taxpayer":"x"}""",
    };

    [Theory]
    [MemberData(nameof(MalformedRecords))]
    public void AMalformedRecordIsRejected(string body) =>
        Assert.Null(AzureRecoveryControlStore.Parse(Encoding.UTF8.GetBytes(body), AzureRecoveryControlStore.GenesisName));

    // --------------------------------------------------------------- reads

    [Fact]
    public async Task AReadReturnsEveryRetainedFactAndTheNewestAttestation()
    {
        var accounts = new ControlledControlAccounts();
        var store = accounts.Store(Now);
        await store.AppendAsync(RecoveryControlRecord.Genesis(Incarnation(), Now.AddDays(-60)), CancellationToken.None);
        await store.AppendAsync(RecoveryControlRecord.TerminalSource(Firm, Case, Resource, Now.AddDays(-2)), CancellationToken.None);
        accounts.PutAttestation(Now.AddDays(-1), (Firm.Value, Guid.Parse("dddddddd-0000-4000-8000-000000000001")));
        accounts.PutAttestation(Now.AddHours(-1), (Firm.Value, Guid.Parse("dddddddd-0000-4000-8000-000000000002")));

        var snapshot = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(RecoveryControlReadStatus.Complete, snapshot.Status);
        Assert.Equal(Now, snapshot.ReadAtUtc);
        Assert.Equal(
            new[] { RecoveryControlRecordKind.Genesis, RecoveryControlRecordKind.TerminalSource },
            snapshot.Records.Select(record => record.Kind).OrderBy(kind => kind));
        Assert.Equal(Now.AddHours(-1), snapshot.Attestation!.RecordedAtUtc);
        Assert.Equal(Guid.Parse("dddddddd-0000-4000-8000-000000000002"), Assert.Single(snapshot.Attestation.Memberships).UserId);
    }

    [Fact] // The authoritative copy must be readable; Canada Central is never a substitute read.
    public async Task AnUnreachableCanadaEastCopyIsAnUnavailableLedger()
    {
        var accounts = new ControlledControlAccounts();
        await accounts.Store().AppendAsync(RecoveryControlRecord.Genesis(Incarnation(), Now), CancellationToken.None);
        accounts.Unreachable.Add(ControlledControlAccounts.EastHost);

        var snapshot = await accounts.Store().ReadAsync(CancellationToken.None);

        Assert.Equal(RecoveryControlReadStatus.Unavailable, snapshot.Status);
        Assert.Empty(snapshot.Records);
        Assert.Null(snapshot.Attestation);
    }

    public static TheoryData<string> CorruptLedgers() => new()
    {
        "malformed record",
        "misplaced record",
        "oversized record",
        "malformed attestation",
        "attestation instant mismatch",
    };

    [Theory]
    [MemberData(nameof(CorruptLedgers))]
    public async Task ACorruptLedgerIsInvalidNeverPartiallyTrusted(string corruption)
    {
        var accounts = new ControlledControlAccounts();
        await accounts.Store().AppendAsync(RecoveryControlRecord.Genesis(Incarnation(), Now), CancellationToken.None);
        var ledger = AzureRecoveryControlStore.LedgerContainerName;
        var attestations = AzureRecoveryControlStore.AttestationContainerName;
        switch (corruption)
        {
            case "malformed record":
                accounts.Put(ControlledControlAccounts.EastHost, ledger, "terminal/2026-09-29/x.json", "{\"kind\":1}"u8.ToArray());
                break;
            case "misplaced record":
                accounts.Put(ControlledControlAccounts.EastHost, ledger, "release/elsewhere.json",
                    AzureRecoveryControlStore.Serialize(RecoveryControlRecord.Release(Incarnation('b'), Now)));
                break;
            case "oversized record":
                accounts.Put(ControlledControlAccounts.EastHost, ledger, "terminal/2026-09-29/big.json", new byte[AzureRecoveryControlStore.MaximumRecordBytes + 1]);
                break;
            case "malformed attestation":
                accounts.Put(ControlledControlAccounts.EastHost, attestations, AzureRecoveryControlStore.AttestationNameOf(Now), "[]"u8.ToArray());
                break;
            default:
                accounts.PutAttestation(Now, (Firm.Value, Guid.NewGuid()));
                accounts.Put(ControlledControlAccounts.EastHost, attestations, AzureRecoveryControlStore.AttestationNameOf(Now.AddHours(1)),
                    accounts.Get(ControlledControlAccounts.EastHost, attestations, AzureRecoveryControlStore.AttestationNameOf(Now))!);
                break;
        }

        var snapshot = await accounts.Store().ReadAsync(CancellationToken.None);

        Assert.Equal(RecoveryControlReadStatus.Invalid, snapshot.Status);
        Assert.Empty(snapshot.Records);
    }

    // ------------------------------------------------------ release lookup

    [Fact]
    public async Task AReleaseOrGenesisOfExactlyThisIncarnationIsObserved()
    {
        var accounts = new ControlledControlAccounts();
        var store = accounts.Store();
        await store.AppendAsync(RecoveryControlRecord.Genesis(Incarnation('a'), Now), CancellationToken.None);
        await store.AppendAsync(RecoveryControlRecord.Release(Incarnation('b'), Now), CancellationToken.None);

        Assert.True(await store.HasReleaseAsync(Incarnation('a'), CancellationToken.None));
        Assert.True(await store.HasReleaseAsync(Incarnation('b'), CancellationToken.None));
        Assert.False(await store.HasReleaseAsync(Incarnation('c'), CancellationToken.None));
    }

    [Fact] // An authoritative "no" is never overruled by the other copy.
    public async Task CanadaEastSayingNoIsNotOverruledByCanadaCentral()
    {
        var accounts = new ControlledControlAccounts();
        accounts.Put(ControlledControlAccounts.CentralHost, AzureRecoveryControlStore.LedgerContainerName,
            AzureRecoveryControlStore.NameOf(RecoveryControlRecord.Release(Incarnation('d'), Now)),
            AzureRecoveryControlStore.Serialize(RecoveryControlRecord.Release(Incarnation('d'), Now)));

        Assert.False(await accounts.Store().HasReleaseAsync(Incarnation('d'), CancellationToken.None));
    }

    [Fact] // Canada Central holds a release only after Canada East acknowledged it.
    public async Task WithCanadaEastUnreachableAReleaseInCanadaCentralStillCounts()
    {
        var accounts = new ControlledControlAccounts();
        await accounts.Store().AppendAsync(RecoveryControlRecord.Release(Incarnation('e'), Now), CancellationToken.None);
        accounts.Unreachable.Add(ControlledControlAccounts.EastHost);

        Assert.True(await accounts.Store().HasReleaseAsync(Incarnation('e'), CancellationToken.None));
        Assert.False(await accounts.Store().HasReleaseAsync(Incarnation('f'), CancellationToken.None));

        accounts.Unreachable.Add(ControlledControlAccounts.CentralHost);
        Assert.False(await accounts.Store().HasReleaseAsync(Incarnation('e'), CancellationToken.None));
    }

    [Fact] // No shared key, SAS or anonymous access ever reaches a control account.
    public async Task EveryRequestCarriesTheManagedIdentityBearerTokenOnly()
    {
        var accounts = new ControlledControlAccounts();
        var store = accounts.Store();
        await store.AppendAsync(RecoveryControlRecord.TerminalSource(Firm, Case, Resource, Now), CancellationToken.None);
        await store.ReadAsync(CancellationToken.None);

        Assert.NotEmpty(accounts.Log);
        Assert.All(accounts.Log, entry => Assert.Equal("Bearer", entry.AuthorizationScheme));
        Assert.DoesNotContain(accounts.Log, entry => entry.Query.Contains("sig=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RecoveryControlAppendOutcome.Unacknowledged,
            await new AzureRecoveryControlStore(
                    accounts.Service(ControlledControlAccounts.CentralHost, "foreign-token"),
                    accounts.Service(ControlledControlAccounts.EastHost, "foreign-token"),
                    TimeProvider.System)
                .AppendAsync(RecoveryControlRecord.TerminalSource(Firm, Case, Guid.NewGuid(), Now), CancellationToken.None));
    }

    // --------------------------------------------- registration and probe

    [Fact]
    public void RegistrationAddsTheLedgerAndEvidenceReaderOnlyForTwoUsableDistinctAccounts()
    {
        var content = new SourceContentSettings(new byte[32], 0);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddTaxAiAzureInfrastructure(ProductionConfigurationTests.Options(ProductionConfigurationTests.ApprovedSettings()), content);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRecoveryControlStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IProductionEvidenceReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRecoveryInventoryProbe));

        foreach (var (key, value) in new (string, string?)[]
                 {
                     ("Recovery:ControlLedger:Endpoint", null),
                     ("Recovery:ControlLedgerReplica:Endpoint", "https://sttaxailedce.file.core.windows.net/"),
                     ("Recovery:ControlLedgerReplica:Endpoint", "https://sttaxailedcc.blob.core.windows.net/"),
                     ("ManagedIdentity:ClientId", null),
                 })
        {
            var settings = ProductionConfigurationTests.ApprovedSettings();
            settings[key] = value;
            var refused = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            refused.AddTaxAiAzureInfrastructure(ProductionConfigurationTests.Options(settings), content);
            Assert.DoesNotContain(refused, descriptor => descriptor.ServiceType == typeof(IRecoveryControlStore));
            Assert.DoesNotContain(refused, descriptor => descriptor.ServiceType == typeof(IProductionEvidenceReader));
        }
    }

    public static TheoryData<string, bool> ControlContainerPostures() => new()
    {
        { "approved", true },
        { "no immutability policy", false },
        { "legal hold", false },
        { "public container", false },
    };

    [Theory] // AC-103: the containers are immutable, private and unheld.
    [MemberData(nameof(ControlContainerPostures))]
    public void TheControlContainerPostureRulesApplyIndependently(string posture, bool approved)
    {
        var properties = BlobsModelFactory.BlobContainerProperties(
            lastModified: Now,
            eTag: new Azure.ETag("0x1"),
            publicAccess: posture == "public container" ? PublicAccessType.Blob : PublicAccessType.None,
            hasImmutabilityPolicy: posture != "no immutability policy",
            hasLegalHold: posture == "legal hold");

        Assert.Equal(approved, ApprovedResourceProbe.IsApprovedControlContainer(properties));
    }

    [Fact]
    public async Task TheRecoveryInventoryOfTwoApprovedAccountsHasNoFinding()
    {
        var accounts = new ControlledControlAccounts();

        var result = await accounts.Probe(ProductionConfigurationTests.ApprovedSettings())
            .ProbeRecoveryInventoryAsync(CancellationToken.None);

        Assert.True(result.IsApproved);
        Assert.Equal(6, accounts.Log.Count(entry => entry.Operation == "container"));
    }

    public static TheoryData<string, string?, ApprovedResourceFinding?> InventoryScenarios() => new()
    {
        { "primary", null, null },
        { "primary", "east mutable", ApprovedResourceFinding.ControlLedgerNotApproved },
        { "primary", "central mutable", ApprovedResourceFinding.ControlLedgerNotApproved },
        { "primary", "east container missing", ApprovedResourceFinding.ControlLedgerNotApproved },
        { "primary", "east unreachable", ApprovedResourceFinding.ControlLedgerUnavailable },
        { "primary", "central unreachable", ApprovedResourceFinding.ControlLedgerUnavailable },
        // Recovering from Canada Central: its copy may be unreachable, never unapproved.
        { "recovery", "central unreachable", null },
        { "recovery", "central mutable", ApprovedResourceFinding.ControlLedgerNotApproved },
        { "recovery", "east unreachable", ApprovedResourceFinding.ControlLedgerUnavailable },
    };

    [Theory]
    [MemberData(nameof(InventoryScenarios))]
    public async Task EachRecoveryInventoryFindingIsIndependent(string profile, string? change, ApprovedResourceFinding? expected)
    {
        var accounts = new ControlledControlAccounts();
        switch (change)
        {
            case "east mutable": accounts.Mutable.Add(ControlledControlAccounts.EastHost); break;
            case "central mutable": accounts.Mutable.Add(ControlledControlAccounts.CentralHost); break;
            case "east container missing": accounts.MissingContainers.Add((ControlledControlAccounts.EastHost, AzureProductionEvidenceReader.EvidenceContainerName)); break;
            case "east unreachable": accounts.Unreachable.Add(ControlledControlAccounts.EastHost); break;
            case "central unreachable": accounts.Unreachable.Add(ControlledControlAccounts.CentralHost); break;
        }

        var settings = profile == "recovery"
            ? ProductionConfigurationTests.RecoverySettings()
            : ProductionConfigurationTests.ApprovedSettings();
        var result = await accounts.Probe(settings).ProbeRecoveryInventoryAsync(CancellationToken.None);

        if (expected is null)
        {
            Assert.True(result.IsApproved);
        }
        else
        {
            Assert.Equal(new[] { expected.Value }, result.Findings);
            Assert.Equal(new[] { ProductionAdmissionRefusal.StorageBindingInvalid }, result.Refusals);
        }
    }

    [Fact]
    public async Task AnUnusableControlEndpointIsAConfigurationFinding()
    {
        var settings = ProductionConfigurationTests.ApprovedSettings();
        settings["Recovery:ControlLedgerReplica:Endpoint"] = "https://attacker.example/";

        var result = await new ControlledControlAccounts().Probe(settings).ProbeRecoveryInventoryAsync(CancellationToken.None);

        Assert.Contains(ApprovedResourceFinding.ConfigurationUnusable, result.Findings);
    }

    [Theory] // The recovery profile accepts the failed-over source redundancy; the primary never does.
    [InlineData("Standard_LRS", false, false)]
    [InlineData("Standard_LRS", true, true)]
    [InlineData("Standard_ZRS", true, true)]
    [InlineData("Standard_RAGZRS", true, false)]
    [InlineData("Standard_GZRS", false, true)]
    public void TheFailedOverSourceRedundancyIsAcceptedOnlyWhileRecovering(string sku, bool recovering, bool approved)
    {
        var container = BlobsModelFactory.BlobContainerProperties(lastModified: Now, eTag: new Azure.ETag("0x1"));

        Assert.Equal(approved, ApprovedResourceProbe.IsApprovedSourceStorage(sku, "StorageV2", "false", container, recovering));
    }
}

/// <summary>
/// An in-memory pair of private control accounts (Canada Central and Canada
/// East): bearer token only, create-only writes, reads, listings and the
/// container properties the recovery inventory probe inspects. Either account
/// can be made unreachable, mutable or tampered.
/// </summary>
internal sealed class ControlledControlAccounts : HttpMessageHandler
{
    internal const string CentralHost = "sttaxailedcc.blob.core.windows.net";
    internal const string EastHost = "sttaxailedce.blob.core.windows.net";
    internal const string ApprovedToken = "synthetic-control-token";

    private readonly object _gate = new();
    private readonly Dictionary<(string Host, string Container, string Name), byte[]> _objects = new();
    private readonly List<LogEntry> _log = new();

    internal sealed record LogEntry(string Host, string Method, string Container, string Name, string Operation, string Query, string? IfNoneMatch, string? AuthorizationScheme);

    public HashSet<string> Unreachable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Mutable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<(string Host, string Container)> MissingContainers { get; } = new();

    public bool TamperOnRead { get; set; }

    public IReadOnlyList<LogEntry> Log
    {
        get
        {
            lock (_gate)
            {
                return _log.ToArray();
            }
        }
    }

    public BlobServiceClient Service(string host, string token = ApprovedToken) =>
        new(new Uri($"https://{host}/"), new StaticCredential(token), Options());

    public BlobClientOptions Options()
    {
        var options = new BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)) };
        options.Retry.MaxRetries = 0;
        return options;
    }

    public AzureRecoveryControlStore Store(DateTimeOffset? now = null) =>
        new(Service(CentralHost), Service(EastHost), new SettableClock(now ?? RecoveryControlStoreTests.Now));

    public AzureProductionEvidenceReader Reader() => new(Service(CentralHost), Service(EastHost));

    public ApprovedResourceProbe Probe(IReadOnlyDictionary<string, string?> settings) =>
        new(
            ProductionConfigurationTests.Options(settings),
            new StaticCredential(ApprovedToken),
            new EphemeralDataProtectionProvider(),
            new Azure.Security.KeyVault.Keys.KeyClientOptions(),
            new Azure.Security.KeyVault.Keys.Cryptography.CryptographyClientOptions(),
            Options());

    public bool Contains(string host, string container, string name)
    {
        lock (_gate)
        {
            return _objects.ContainsKey((host, container, name));
        }
    }

    public int Count(string host, string container)
    {
        lock (_gate)
        {
            return _objects.Keys.Count(key => key.Host == host && key.Container == container);
        }
    }

    public byte[]? Get(string host, string container, string name)
    {
        lock (_gate)
        {
            return _objects.TryGetValue((host, container, name), out var value) ? value.ToArray() : null;
        }
    }

    public void Put(string host, string container, string name, byte[] content)
    {
        lock (_gate)
        {
            _objects[(host, container, name)] = content.ToArray();
        }
    }

    /// <summary>Publishes an attestation to both accounts, as the provisioning process does.</summary>
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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri!.Host;
        if (!string.Equals(host, CentralHost, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(host, EastHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Unexpected destination.");
        }

        var path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath).TrimStart('/');
        var slash = path.IndexOf('/');
        var container = slash < 0 ? path : path[..slash];
        var name = slash < 0 ? string.Empty : path[(slash + 1)..];
        var query = request.RequestUri.Query;
        var restype = QueryValue(query, "restype");
        var comp = QueryValue(query, "comp");
        var operation = restype == "container" && comp == "list" ? "list" :
            restype == "container" ? "container" :
            request.Method == HttpMethod.Put ? "blob" : "read";

        lock (_gate)
        {
            _log.Add(new LogEntry(host, request.Method.Method, container, name, operation, query,
                request.Headers.IfNoneMatch.FirstOrDefault()?.Tag, request.Headers.Authorization?.Scheme));
        }

        if (Unreachable.Contains(host))
        {
            throw new HttpRequestException("Synthetic regional outage.");
        }

        if (request.Headers.Authorization is not { Scheme: "Bearer" } authorization ||
            authorization.Parameter != ApprovedToken ||
            query.Contains("sig=", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Error(HttpStatusCode.Forbidden, "AuthorizationFailure"));
        }

        if (MissingContainers.Contains((host, container)))
        {
            return Task.FromResult(Error(HttpStatusCode.NotFound, "ContainerNotFound"));
        }

        lock (_gate)
        {
            switch (operation)
            {
                case "container":
                {
                    var response = Empty(HttpStatusCode.OK);
                    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x1\"");
                    response.Headers.TryAddWithoutValidation("Last-Modified", "Tue, 29 Sep 2026 12:00:00 GMT");
                    response.Headers.TryAddWithoutValidation("x-ms-has-immutability-policy", Mutable.Contains(host) ? "false" : "true");
                    response.Headers.TryAddWithoutValidation("x-ms-has-legal-hold", "false");
                    return Task.FromResult(response);
                }

                case "list":
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
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
                    };
                    response.Headers.TryAddWithoutValidation("x-ms-version", "2025-01-05");
                    return Task.FromResult(response);
                }

                case "blob":
                {
                    if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == "*") && _objects.ContainsKey((host, container, name)))
                    {
                        return Task.FromResult(Error(HttpStatusCode.Conflict, "BlobAlreadyExists"));
                    }

                    _objects[(host, container, name)] = request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
                    return Task.FromResult(WithBlobHeaders(Empty(HttpStatusCode.Created)));
                }

                default:
                {
                    if (!_objects.TryGetValue((host, container, name), out var content))
                    {
                        return Task.FromResult(Error(HttpStatusCode.NotFound, "BlobNotFound"));
                    }

                    var body = content.ToArray();
                    if (TamperOnRead && body.Length > 0)
                    {
                        body = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("2026-09-29", "2026-09-28", StringComparison.Ordinal));
                    }

                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    response.Content.Headers.ContentLength = body.Length;
                    return Task.FromResult(WithBlobHeaders(response));
                }
            }
        }
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (string.Equals(pair[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
        }

        return null;
    }

    private static HttpResponseMessage Empty(HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(Array.Empty<byte>()) };
        response.Headers.TryAddWithoutValidation("x-ms-version", "2025-01-05");
        return response;
    }

    private static HttpResponseMessage WithBlobHeaders(HttpResponseMessage response)
    {
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"0x{Guid.NewGuid():N}\"");
        response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        response.Headers.TryAddWithoutValidation("x-ms-version", "2025-01-05");
        response.Headers.TryAddWithoutValidation("Last-Modified", "Tue, 29 Sep 2026 12:00:00 GMT");
        response.Content ??= new ByteArrayContent(Array.Empty<byte>());
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
}
