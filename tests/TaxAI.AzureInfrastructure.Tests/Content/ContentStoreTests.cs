using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using ClientIntake.Contracts;
using ClientIntake.Models;
using TaxAI.AzureInfrastructure.Content;
using TaxAI.AzureInfrastructure.Keys;
using TaxAI.AzureInfrastructure.Tests.Keys;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Tests.Content;

/// <summary>
/// Spec 016 T04 — the production write-only source store (FR-03, FR-04;
/// AC-026, AC-027, AC-028, AC-038, AC-039).
/// <para>
/// Every case drives the real <see cref="AzureIntakeContentStore"/>, the real
/// Blob SDK over a controlled in-memory source account, the real Key Vault
/// wrapper over the controlled vault with real RSA, and an in-memory registry
/// with the contract's semantics. All content is synthetic; no network, cloud
/// resource or taxpayer value is involved.
/// </para>
/// </summary>
public sealed class ContentStoreTests
{
    internal static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"));
    internal static readonly TrustedFirmId OtherFirm = TrustedFirmId.FromGuid(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"));
    internal static readonly Guid Case = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    internal static readonly Guid OtherCase = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    internal static readonly byte[] FingerprintKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    /// <summary>A recognisable synthetic document. Never a real taxpayer value.</summary>
    internal static byte[] SyntheticDocument(int length = 150_000, byte seed = 7)
    {
        var marker = Encoding.ASCII.GetBytes("SYNTHETIC-SOURCE-DOCUMENT-PLANTED-MARKER-");
        var content = new byte[length];
        for (var index = 0; index < length; index++)
        {
            content[index] = index % 97 < marker.Length ? marker[index % 97] : (byte)((index * 31) + seed);
        }

        return content;
    }

    [Fact] // AC-026: the stored object is application ciphertext, never plaintext.
    public async Task AnUploadIsStoredOnlyAsAuthenticatedCiphertext()
    {
        var harness = new SourceHarness();
        var document = SyntheticDocument();

        var staged = await harness.Store().StageAsync(Firm, Case, new MemoryStream(document), 1_048_576, CancellationToken.None);

        Assert.Equal(document.Length, staged.ByteCount);
        var registration = Assert.Single(harness.Registry.All);
        Assert.Equal(SourceRegistryState.Staged, registration.State);
        var stored = harness.Storage.Objects;
        Assert.Equal(2, stored.Count);
        var ciphertext = stored[SourceEnvelopeCodec.ContentObjectName(registration.ResourceId)];
        Assert.StartsWith("TAISRC01", Encoding.ASCII.GetString(ciphertext, 0, 8), StringComparison.Ordinal);
        Assert.All(stored.Values, value => Assert.False(Contains(value, document.AsSpan(0, 41))));
        Assert.All(stored.Values, value => Assert.False(Contains(value, "SYNTHETIC-SOURCE"u8)));
        Assert.Equal(SourceEnvelopeCodec.MaximumCiphertextLength(document.Length), ciphertext.Length);
    }

    [Fact] // AC-038: every upload has its own fresh data key.
    public async Task TwoUploadsOfIdenticalContentUseDistinctDataKeysAndCiphertext()
    {
        var harness = new SourceHarness();
        var document = SyntheticDocument(70_000);

        await harness.Store().StageAsync(Firm, Case, new MemoryStream(document), 1_048_576, CancellationToken.None);
        await harness.Store().StageAsync(Firm, Case, new MemoryStream(document), 1_048_576, CancellationToken.None);

        var registrations = harness.Registry.All;
        Assert.Equal(2, registrations.Count);
        var keys = new List<byte[]>();
        foreach (var registration in registrations)
        {
            var scope = new SourceEnvelopeScope(Firm, Case, registration.ResourceId);
            var wrapped = SourceEnvelopeCodec.DecodeKeyEnvelope(
                harness.Storage.Objects[SourceEnvelopeCodec.KeyObjectName(registration.ResourceId)],
                scope);
            keys.Add(await harness.Wrapper.UnwrapAsync(wrapped, CancellationToken.None));
        }

        Assert.Equal(32, keys[0].Length);
        Assert.False(keys[0].AsSpan().SequenceEqual(keys[1]));
        Assert.False(harness.Storage.Objects[SourceEnvelopeCodec.ContentObjectName(registrations[0].ResourceId)].AsSpan(20)
            .SequenceEqual(harness.Storage.Objects[SourceEnvelopeCodec.ContentObjectName(registrations[1].ResourceId)].AsSpan(20)));
        Assert.NotEqual(registrations[0].ContentKey, registrations[1].ContentKey);
    }

    [Fact] // AC-039: no plaintext data key is persisted anywhere the store writes.
    public async Task NoPlaintextDataKeyIsPersisted()
    {
        var harness = new SourceHarness();
        await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        var registration = Assert.Single(harness.Registry.All);
        var dataKey = await harness.Wrapper.UnwrapAsync(
            SourceEnvelopeCodec.DecodeKeyEnvelope(
                harness.Storage.Objects[SourceEnvelopeCodec.KeyObjectName(registration.ResourceId)],
                new SourceEnvelopeScope(Firm, Case, registration.ResourceId)),
            CancellationToken.None);

        Assert.All(harness.Storage.Objects.Values, value => Assert.False(Contains(value, dataKey)));
        Assert.All(harness.Storage.Objects.Values, value => Assert.False(Contains(value, Encoding.ASCII.GetBytes(Convert.ToBase64String(dataKey)))));
        Assert.DoesNotContain(Convert.ToBase64String(dataKey), registration.ToString(), StringComparison.Ordinal);
    }

    [Fact] // AC-027: both objects are written upload-once.
    public async Task EveryWriteIsConditionedOnTheObjectNotExisting()
    {
        var harness = new SourceHarness();
        await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);

        var commits = harness.Storage.Log.Where(entry => entry.Method == "PUT" && entry.Operation is "blob" or "blocklist").ToArray();
        Assert.Equal(2, commits.Length);
        Assert.All(commits, commit => Assert.Equal("*", commit.IfNoneMatch));
        Assert.All(harness.Storage.Log, entry => Assert.Equal("Bearer", entry.AuthorizationScheme));
        Assert.All(harness.Storage.Log, entry => Assert.DoesNotContain("sig=", entry.Query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // AC-027: an existing source object can never be overwritten.
    public async Task AnAttemptToOverwriteAnExistingSourceObjectFails()
    {
        var harness = new SourceHarness();
        await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        var registration = Assert.Single(harness.Registry.All);
        var before = harness.Storage.Objects[SourceEnvelopeCodec.ContentObjectName(registration.ResourceId)];

        var content = harness.Container.GetBlockBlobClient(SourceEnvelopeCodec.ContentObjectName(registration.ResourceId));
        var envelope = harness.Container.GetBlobClient(SourceEnvelopeCodec.KeyObjectName(registration.ResourceId));
        var once = new BlobRequestConditions { IfNoneMatch = ETag.All };

        await content.StageBlockAsync(Convert.ToBase64String("00000000"u8.ToArray()), new MemoryStream(new byte[] { 1, 2, 3 }));
        var overwrite = await Assert.ThrowsAsync<RequestFailedException>(() => content.CommitBlockListAsync(
            new[] { Convert.ToBase64String("00000000"u8.ToArray()) },
            new CommitBlockListOptions { Conditions = once }));
        var rewrite = await Assert.ThrowsAsync<RequestFailedException>(() => envelope.UploadAsync(
            BinaryData.FromBytes(new byte[] { 9 }),
            new BlobUploadOptions { Conditions = once }));

        Assert.Equal(409, overwrite.Status);
        Assert.Equal(409, rewrite.Status);
        Assert.Equal(before, harness.Storage.Objects[SourceEnvelopeCodec.ContentObjectName(registration.ResourceId)]);
    }

    [Fact] // Tracked before allocation: the registry row precedes every storage request.
    public async Task TheRegistryEntryIsRecordedBeforeAnyStorageAllocation()
    {
        var journal = new List<string>();
        var harness = new SourceHarness(journal);

        await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);

        Assert.Equal("registry:staged", journal[0]);
        Assert.Contains(journal, entry => entry.StartsWith("storage:", StringComparison.Ordinal));
        Assert.True(journal.IndexOf("registry:staged") < journal.FindIndex(entry => entry.StartsWith("storage:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARegistryRefusalAllocatesNothing()
    {
        var harness = new SourceHarness();
        harness.Registry.RefuseRegistration = true;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None));

        Assert.Equal("The content could not be staged.", failure.Message);
        Assert.Empty(harness.Storage.Log);
        Assert.Empty(harness.Vault.Requests);
    }

    [Fact] // Size limits are enforced by the adapter itself, with compensating cleanup.
    public async Task AnOversizedUploadIsRefusedAndItsAllocationIsCleanedUp()
    {
        var harness = new SourceHarness();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument(200_000)), 100_000, CancellationToken.None));

        Assert.Equal("The content could not be staged.", failure.Message);
        Assert.Null(failure.InnerException);
        Assert.Empty(harness.Storage.Objects);
        Assert.Equal(SourceRegistryState.PurgePending, Assert.Single(harness.Registry.All).State);
        Assert.DoesNotContain(harness.Storage.Log, entry => entry.Operation == "blocklist");
    }

    [Fact]
    public async Task AnEmptyUploadIsRefused()
    {
        var harness = new SourceHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Store().StageAsync(Firm, Case, new MemoryStream(), 1_048_576, CancellationToken.None));

        Assert.Empty(harness.Storage.Objects);
    }

    [Fact] // AC-035 at write time: an unusable wrapping key stores nothing readable.
    public async Task AnUnusableWrappingKeyRefusesTheUploadWithoutStoringContent()
    {
        var harness = new SourceHarness(token: "synthetic-foreign-identity-token");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None));

        Assert.Equal("The content could not be staged.", failure.Message);
        Assert.Empty(harness.Storage.Objects);
        Assert.Equal(SourceRegistryState.PurgePending, Assert.Single(harness.Registry.All).State);
    }

    [Fact]
    public async Task AStorageFailureMidUploadIsRefusedAndCompensated()
    {
        var harness = new SourceHarness();
        harness.Storage.FailOperation = "blocklist";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None));

        Assert.Empty(harness.Storage.Objects);
        Assert.Contains(harness.Storage.Log, entry => entry.Method == "DELETE");
    }

    [Fact]
    public async Task CancellationPropagatesAfterCompensatingCleanup()
    {
        var harness = new SourceHarness();
        using var cancellation = new CancellationTokenSource();
        var content = new CancellingStream(SyntheticDocument(), cancellation, cancelAfterBytes: 70_000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Store().StageAsync(Firm, Case, content, 1_048_576, cancellation.Token));

        Assert.Empty(harness.Storage.Objects);
        Assert.Equal(SourceRegistryState.PurgePending, Assert.Single(harness.Registry.All).State);
    }

    [Fact] // The scoped keyed fingerprint duplicate detection compares.
    public async Task TheFingerprintIsTheScopedKeyedDigestOfThePlaintext()
    {
        var harness = new SourceHarness();
        var document = SyntheticDocument(90_000);

        var staged = await harness.Store().StageAsync(Firm, Case, new MemoryStream(document), 1_048_576, CancellationToken.None);
        var sameScope = await harness.Store().StageAsync(Firm, Case, new MemoryStream(document), 1_048_576, CancellationToken.None);
        var otherCase = await harness.Store().StageAsync(Firm, OtherCase, new MemoryStream(document), 1_048_576, CancellationToken.None);

        var scopedKey = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            FingerprintKey,
            32,
            Array.Empty<byte>(),
            Encoding.UTF8.GetBytes($"TaxAI.Intake.Fingerprint.v1|{Firm.ToCanonicalString()}|{Case:D}"));
        Assert.Equal(HMACSHA256.HashData(scopedKey, document), staged.Fingerprint.ToArray());
        Assert.Equal(staged.Fingerprint, sameScope.Fingerprint);
        Assert.NotEqual(staged.Fingerprint, otherCase.Fingerprint);
    }

    [Fact] // AC-028: nothing returned to a caller can address the object.
    public async Task TheStagedResultCarriesNoLocationSasOrObjectName()
    {
        var harness = new SourceHarness();

        var staged = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        var registration = Assert.Single(harness.Registry.All);

        Assert.Equal(43, staged.ContentKey.Length);
        Assert.DoesNotContain(registration.ResourceId.ToString("N"), staged.ContentKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blob.core.windows.net", staged.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sig=", staged.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(AzureIntakeContentStore), harness.Store().ToString());
    }

    // ------------------------------------------------ compensating cleanup

    [Fact]
    public async Task DeletingAStagedObjectRemovesBothObjectsAndRecordsAPendingPurge()
    {
        var harness = new SourceHarness();
        var staged = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);

        await harness.Store().DeleteIfExistsAsync(Firm, Case, staged.ContentKey, CancellationToken.None);

        Assert.Empty(harness.Storage.Objects);
        // The primary is observed absent; the replica cannot be, so it stays
        // pending for replay rather than being reported complete.
        Assert.Equal(SourceRegistryState.PurgePending, Assert.Single(harness.Registry.All).State);
    }

    [Fact] // An uncertain commit that actually bound the object never loses its source.
    public async Task ABoundObjectIsNeverDeletedByCompensatingCleanup()
    {
        var harness = new SourceHarness();
        var staged = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        harness.Registry.Bind(Firm, Case, staged.ContentKey, Guid.NewGuid());

        await harness.Store().DeleteIfExistsAsync(Firm, Case, staged.ContentKey, CancellationToken.None);

        Assert.Equal(2, harness.Storage.Objects.Count);
        Assert.Equal(SourceRegistryState.Bound, Assert.Single(harness.Registry.All).State);
        Assert.DoesNotContain(harness.Storage.Log, entry => entry.Method == "DELETE");
    }

    [Fact]
    public async Task AForeignScopeOrUnknownKeyDeletesNothing()
    {
        var harness = new SourceHarness();
        var staged = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);

        await harness.Store().DeleteIfExistsAsync(OtherFirm, Case, staged.ContentKey, CancellationToken.None);
        await harness.Store().DeleteIfExistsAsync(Firm, OtherCase, staged.ContentKey, CancellationToken.None);
        await harness.Store().DeleteIfExistsAsync(Firm, Case, new string('A', 43), CancellationToken.None);

        Assert.Equal(2, harness.Storage.Objects.Count);
        Assert.Equal(SourceRegistryState.Staged, Assert.Single(harness.Registry.All).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("contains/a/slash-and-is-exactly-forty-three")]
    public async Task ANonCanonicalContentKeyIsRejected(string key)
    {
        var harness = new SourceHarness();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Store().DeleteIfExistsAsync(Firm, Case, key, CancellationToken.None));
    }

    [Fact] // Orphan tracking and recovery replay.
    public async Task CleanupReplayRemovesOnlyStaleUncommittedObjectsOfTheTrustedFirm()
    {
        var harness = new SourceHarness();
        var stale = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        var bound = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        harness.Registry.Bind(Firm, Case, bound.ContentKey, Guid.NewGuid());
        var foreign = await harness.Store().StageAsync(OtherFirm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        harness.Clock.Advance(TimeSpan.FromHours(25));
        var fresh = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);

        var outcomes = await harness.Store().CleanUpUncommittedAsync(
            Firm,
            harness.Clock.GetUtcNow().AddHours(-24),
            100,
            CancellationToken.None);

        Assert.Equal(new[] { SourceObjectPurgeOutcome.Pending }, outcomes);
        Assert.Equal(SourceRegistryState.PurgePending, harness.Registry.Find(Firm, Case, stale.ContentKey)!.State);
        Assert.Equal(SourceRegistryState.Bound, harness.Registry.Find(Firm, Case, bound.ContentKey)!.State);
        Assert.Equal(SourceRegistryState.Staged, harness.Registry.Find(OtherFirm, Case, foreign.ContentKey)!.State);
        Assert.Equal(SourceRegistryState.Staged, harness.Registry.Find(Firm, Case, fresh.ContentKey)!.State);
        Assert.Equal(6, harness.Storage.Objects.Count);
    }

    [Fact]
    public async Task APendingCleanupIsReplayedAgain()
    {
        var harness = new SourceHarness();
        var staged = await harness.Store().StageAsync(Firm, Case, new MemoryStream(SyntheticDocument()), 1_048_576, CancellationToken.None);
        harness.Storage.FailOperation = "delete";
        await harness.Store().DeleteIfExistsAsync(Firm, Case, staged.ContentKey, CancellationToken.None);
        Assert.Equal(2, harness.Storage.Objects.Count);
        harness.Storage.FailOperation = null;

        var outcomes = await harness.Store().CleanUpUncommittedAsync(Firm, harness.Clock.GetUtcNow(), 10, CancellationToken.None);

        Assert.Equal(new[] { SourceObjectPurgeOutcome.Pending }, outcomes);
        Assert.Empty(harness.Storage.Objects);
    }

    [Fact]
    public void ConstructionRefusesAnUnusableFingerprintKey()
    {
        var harness = new SourceHarness();

        Assert.Throws<ArgumentException>(() => new AzureIntakeContentStore(
            harness.Container,
            harness.Wrapper,
            harness.Registry,
            new AzureSourceObjectPurger(harness.Container),
            new byte[16],
            TimeProvider.System));
    }

    internal static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) =>
        haystack.IndexOf(needle) >= 0;

    private sealed class CancellingStream(byte[] content, CancellationTokenSource cancellation, int cancelAfterBytes) : MemoryStream(content)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= cancelAfterBytes)
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}

/// <summary>A source store, reader and purger over the controlled account, vault and registry.</summary>
internal sealed class SourceHarness
{
    internal const string SourceKey = "source-envelope";

    public SourceHarness(List<string>? journal = null, string token = ControlledKeyVault.ApprovedToken)
    {
        Journal = journal;
        Storage = new ControlledSourceStorage(journal);
        Registry = new InMemorySourceRegistry(journal);
        SourceKeyVersion = Vault.AddKey(SourceKey);
        Wrapper = Vault.Wrapper(SourceKey, token);
        Container = Storage.Container();
    }

    public List<string>? Journal { get; }

    public ControlledKeyVault Vault { get; } = new();

    public string SourceKeyVersion { get; }

    public ControlledSourceStorage Storage { get; }

    public InMemorySourceRegistry Registry { get; }

    public KeyVaultContentKeyWrapper Wrapper { get; }

    public BlobContainerClient Container { get; }

    public SettableClock Clock { get; } = new(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));

    public AzureIntakeContentStore Store() =>
        new(Container, Wrapper, Registry, new AzureSourceObjectPurger(Container), ContentStoreTests.FingerprintKey, Clock);

    public AzureExtractionContentReader Reader(long maximumSourceBytes = 1_048_576, string token = ControlledKeyVault.ApprovedToken) =>
        new(Container, Vault.Wrapper(SourceKey, token), Registry, maximumSourceBytes);

    /// <summary>Stages a document and binds it to a new committed document identifier.</summary>
    public async Task<(StagedIntakeContent Staged, Guid DocumentId, SourceRegistration Registration)> CommitAsync(
        byte[] document,
        TrustedFirmId? firm = null,
        Guid? caseId = null)
    {
        var owner = firm ?? ContentStoreTests.Firm;
        var scope = caseId ?? ContentStoreTests.Case;
        var staged = await Store().StageAsync(owner, scope, new MemoryStream(document), 1_048_576, CancellationToken.None);
        var documentId = Guid.NewGuid();
        Registry.Bind(owner, scope, staged.ContentKey, documentId);
        return (staged, documentId, Registry.Find(owner, scope, staged.ContentKey)!);
    }
}

internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// An in-memory source registry with exactly the contract's scoping and
/// state-transition semantics. It is test-only; the relational implementation
/// is proved separately against the real migration.
/// </summary>
internal sealed class InMemorySourceRegistry(List<string>? journal = null) : ISourceRegistryStore
{
    private readonly object _gate = new();
    private readonly List<SourceRegistration> _rows = new();

    public bool RefuseRegistration { get; set; }

    public int FindBoundCalls { get; private set; }

    public IReadOnlyList<SourceRegistration> All
    {
        get
        {
            lock (_gate)
            {
                return _rows.ToArray();
            }
        }
    }

    public SourceRegistration? Find(TrustedFirmId firm, Guid caseId, string contentKey)
    {
        lock (_gate)
        {
            return _rows.SingleOrDefault(row => row.Firm.Equals(firm) && row.CaseId == caseId && row.ContentKey == contentKey);
        }
    }

    public void Bind(TrustedFirmId firm, Guid caseId, string contentKey, Guid documentId)
    {
        lock (_gate)
        {
            var row = _rows.Single(candidate => candidate.Firm.Equals(firm) && candidate.CaseId == caseId && candidate.ContentKey == contentKey);
            Assert.Equal(SourceRegistryState.Staged, row.State);
            Replace(row, new SourceRegistration(firm, caseId, row.ResourceId, contentKey, SourceRegistryState.Bound, row.StagedAtUtc, documentId, row.StagedAtUtc, null));
        }
    }

    public void MarkPurged(SourceRegistration row)
    {
        lock (_gate)
        {
            Replace(row, new SourceRegistration(row.Firm, row.CaseId, row.ResourceId, row.ContentKey, SourceRegistryState.Purged, row.StagedAtUtc, row.DocumentId, row.BoundAtUtc, row.StagedAtUtc));
        }
    }

    public Task<SourceRegistryWriteCode> RegisterStagedAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, string contentKey, DateTimeOffset stagedAtUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (RefuseRegistration)
            {
                return Task.FromResult(SourceRegistryWriteCode.Conflict);
            }

            if (_rows.Any(row => row.ResourceId == resourceId || (row.Firm.Equals(firm) && row.CaseId == caseId && row.ContentKey == contentKey)))
            {
                return Task.FromResult(SourceRegistryWriteCode.Conflict);
            }

            _rows.Add(new SourceRegistration(firm, caseId, resourceId, contentKey, SourceRegistryState.Staged, stagedAtUtc, null, null, null));
            journal?.Add("registry:staged");
            return Task.FromResult(SourceRegistryWriteCode.Success);
        }
    }

    public Task<SourceRegistration?> FindAsync(TrustedFirmId firm, Guid caseId, string contentKey, CancellationToken cancellationToken) =>
        Task.FromResult(Find(firm, caseId, contentKey));

    public Task<SourceRegistration?> FindBoundAsync(TrustedFirmId firm, Guid caseId, Guid documentId, string contentKey, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            FindBoundCalls++;
            return Task.FromResult(_rows.SingleOrDefault(row =>
                row.Firm.Equals(firm) &&
                row.CaseId == caseId &&
                row.DocumentId == documentId &&
                row.ContentKey == contentKey &&
                row.State == SourceRegistryState.Bound));
        }
    }

    public Task<SourceRegistryWriteCode> RecordPurgeAsync(TrustedFirmId firm, Guid caseId, Guid resourceId, SourceObjectPurgeOutcome outcome, DateTimeOffset recordedAtUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var row = _rows.SingleOrDefault(candidate => candidate.Firm.Equals(firm) && candidate.CaseId == caseId && candidate.ResourceId == resourceId);
            if (row is null)
            {
                return Task.FromResult(SourceRegistryWriteCode.NotFound);
            }

            if (row.DocumentId is not null || row.State is not (SourceRegistryState.Staged or SourceRegistryState.PurgePending))
            {
                return Task.FromResult(SourceRegistryWriteCode.Conflict);
            }

            var next = outcome == SourceObjectPurgeOutcome.Complete ? SourceRegistryState.Purged : SourceRegistryState.PurgePending;
            Replace(row, new SourceRegistration(firm, caseId, resourceId, row.ContentKey, next, row.StagedAtUtc, null, null, recordedAtUtc));
            journal?.Add($"registry:{next}");
            return Task.FromResult(SourceRegistryWriteCode.Success);
        }
    }

    public Task<IReadOnlyList<SourceRegistration>> ListUncommittedAsync(TrustedFirmId firm, DateTimeOffset stagedBeforeUtc, int maximumCount, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<SourceRegistration> work = _rows
                .Where(row => row.Firm.Equals(firm) && row.DocumentId is null)
                .Where(row => row.State == SourceRegistryState.PurgePending || (row.State == SourceRegistryState.Staged && row.StagedAtUtc < stagedBeforeUtc))
                .OrderBy(row => row.StagedAtUtc)
                .Take(maximumCount)
                .ToArray();
            return Task.FromResult(work);
        }
    }

    private void Replace(SourceRegistration existing, SourceRegistration replacement) =>
        _rows[_rows.IndexOf(existing)] = replacement;
}

/// <summary>
/// An in-memory Blob endpoint for the private source account: bearer token only
/// (no shared key or SAS is accepted), upload-once conditions, staged blocks
/// and committed block lists, reads, deletes, and the account/container
/// properties the approved-resource probe inspects. Every request is logged.
/// </summary>
internal sealed class ControlledSourceStorage(List<string>? journal = null) : HttpMessageHandler
{
    internal const string Host = "sttaxaisrccc.blob.core.windows.net";
    internal const string ApprovedToken = "synthetic-storage-token";
    internal const string ContainerPath = "/" + AzureIntakeContentStore.SourceContainerName + "/";

    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, byte[]>> _blocks = new(StringComparer.Ordinal);
    private readonly List<LogEntry> _log = new();

    internal sealed record LogEntry(string Method, string Name, string Operation, string Query, string? IfNoneMatch, string? AuthorizationScheme);

    public string? FailOperation { get; set; }

    public string SkuName { get; set; } = "Standard_GZRS";

    public string AccountKind { get; set; } = "StorageV2";

    public string HierarchicalNamespace { get; set; } = "false";

    public string? PublicAccess { get; set; }

    public bool ImmutabilityPolicy { get; set; }

    public bool LegalHold { get; set; }

    public bool VersionLevelImmutability { get; set; }

    public bool ContainerExists { get; set; } = true;

    public IReadOnlyDictionary<string, byte[]> Objects
    {
        get
        {
            lock (_gate)
            {
                return _objects.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.Ordinal);
            }
        }
    }

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

    public void Replace(string name, byte[] content)
    {
        lock (_gate)
        {
            _objects[name] = content.ToArray();
        }
    }

    public BlobClientOptions Options()
    {
        var options = new BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)) };
        options.Retry.MaxRetries = 0;
        return options;
    }

    public BlobContainerClient Container(string token = ApprovedToken) =>
        new BlobServiceClient(new Uri($"https://{Host}/"), new StaticCredential(token), Options())
            .GetBlobContainerClient(AzureIntakeContentStore.SourceContainerName);

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.RequestUri!.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Unexpected destination.");
        }

        var path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath);
        var query = request.RequestUri.Query;
        var comp = QueryValue(query, "comp");
        var restype = QueryValue(query, "restype");
        var name = path.StartsWith(ContainerPath, StringComparison.Ordinal) ? path[ContainerPath.Length..] : string.Empty;
        var operation =
            restype == "account" ? "account" :
            restype == "container" ? "container" :
            request.Method == HttpMethod.Delete ? "delete" :
            comp is "block" or "blocklist" ? comp :
            request.Method == HttpMethod.Put ? "blob" :
            "read";

        lock (_gate)
        {
            _log.Add(new LogEntry(
                request.Method.Method,
                name,
                operation,
                query,
                request.Headers.IfNoneMatch.FirstOrDefault()?.Tag,
                request.Headers.Authorization?.Scheme));
            journal?.Add($"storage:{operation}");
        }

        if (request.Headers.Authorization is not { Scheme: "Bearer" } authorization ||
            !string.Equals(authorization.Parameter, ApprovedToken, StringComparison.Ordinal) ||
            query.Contains("sig=", StringComparison.OrdinalIgnoreCase))
        {
            return Error(HttpStatusCode.Forbidden, "AuthorizationFailure");
        }

        if (FailOperation is not null && string.Equals(FailOperation, operation, StringComparison.Ordinal))
        {
            return Error(HttpStatusCode.ServiceUnavailable, "ServerBusy");
        }

        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        lock (_gate)
        {
            switch (operation)
            {
                case "account":
                {
                    var response = Empty(HttpStatusCode.OK);
                    response.Headers.TryAddWithoutValidation("x-ms-sku-name", SkuName);
                    response.Headers.TryAddWithoutValidation("x-ms-account-kind", AccountKind);
                    response.Headers.TryAddWithoutValidation("x-ms-is-hns-enabled", HierarchicalNamespace);
                    return response;
                }

                case "container":
                {
                    if (!ContainerExists)
                    {
                        return Error(HttpStatusCode.NotFound, "ContainerNotFound");
                    }

                    var response = Empty(HttpStatusCode.OK);
                    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x1\"");
                    response.Headers.TryAddWithoutValidation("Last-Modified", "Sun, 27 Sep 2026 12:00:00 GMT");
                    if (PublicAccess is not null)
                    {
                        response.Headers.TryAddWithoutValidation("x-ms-blob-public-access", PublicAccess);
                    }

                    response.Headers.TryAddWithoutValidation("x-ms-has-immutability-policy", ImmutabilityPolicy ? "true" : "false");
                    response.Headers.TryAddWithoutValidation("x-ms-has-legal-hold", LegalHold ? "true" : "false");
                    response.Headers.TryAddWithoutValidation("x-ms-immutable-storage-with-versioning-enabled", VersionLevelImmutability ? "true" : "false");
                    return response;
                }

                case "block":
                {
                    var blockId = QueryValue(query, "blockid")!;
                    if (!_blocks.TryGetValue(name, out var blocks))
                    {
                        _blocks[name] = blocks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                    }

                    blocks[blockId] = body ?? Array.Empty<byte>();
                    return Empty(HttpStatusCode.Created);
                }

                case "blocklist":
                {
                    if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == "*") && _objects.ContainsKey(name))
                    {
                        return Error(HttpStatusCode.Conflict, "BlobAlreadyExists");
                    }

                    var ids = XDocument.Load(new MemoryStream(body!)).Root!.Elements().Select(element => element.Value).ToArray();
                    _blocks.TryGetValue(name, out var staged);
                    if (staged is null || ids.Any(id => !staged.ContainsKey(id)))
                    {
                        return Error(HttpStatusCode.BadRequest, "InvalidBlockList");
                    }

                    _objects[name] = ids.SelectMany(id => staged[id]).ToArray();
                    _blocks.Remove(name);
                    return WithBlobHeaders(Empty(HttpStatusCode.Created));
                }

                case "blob":
                {
                    if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == "*") && _objects.ContainsKey(name))
                    {
                        return Error(HttpStatusCode.Conflict, "BlobAlreadyExists");
                    }

                    _objects[name] = body ?? Array.Empty<byte>();
                    return WithBlobHeaders(Empty(HttpStatusCode.Created));
                }

                case "delete":
                {
                    _blocks.Remove(name);
                    if (!_objects.Remove(name))
                    {
                        return Error(HttpStatusCode.NotFound, "BlobNotFound");
                    }

                    return Empty(HttpStatusCode.Accepted);
                }

                default:
                {
                    if (!_objects.TryGetValue(name, out var content))
                    {
                        return Error(HttpStatusCode.NotFound, "BlobNotFound");
                    }

                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(request.Method == HttpMethod.Head ? Array.Empty<byte>() : content.ToArray()),
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    response.Content.Headers.ContentLength = content.Length;
                    return WithBlobHeaders(response);
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
        response.Headers.TryAddWithoutValidation("x-ms-request-server-encrypted", "true");
        response.Headers.TryAddWithoutValidation("x-ms-server-encrypted", "true");
        response.Headers.TryAddWithoutValidation("Last-Modified", "Sun, 27 Sep 2026 12:00:00 GMT");
        response.Content ??= new ByteArrayContent(Array.Empty<byte>());
        response.Content.Headers.TryAddWithoutValidation("Last-Modified", "Sun, 27 Sep 2026 12:00:00 GMT");
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
