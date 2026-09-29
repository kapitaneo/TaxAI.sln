using System.Reflection;
using ExtractionWorkflow.Contracts;
using TaxAI.AzureInfrastructure.Content;
using TaxAI.AzureInfrastructure.Tests.Keys;

namespace TaxAI.AzureInfrastructure.Tests.Content;

/// <summary>
/// Spec 016 T04 — the production extraction-only reader (FR-03; AC-030 to
/// AC-037).
/// <para>
/// The real <see cref="AzureExtractionContentReader"/> reads what the real
/// store wrote, over the controlled source account and vault. Every mismatch
/// of the four scope values returns no lease — and, for a scope the registry
/// does not bind, reads nothing from storage at all. Integrity, key and bound
/// failures return no lease either, and every lease clears its plaintext when
/// disposed.
/// </para>
/// </summary>
public sealed class ContentReaderTests
{
    private static readonly byte[] Document = ContentStoreTests.SyntheticDocument(200_000);

    [Fact] // The positive control: the bound document reads back exactly.
    public async Task TheBoundDocumentReadsBackExactly()
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);

        await using var lease = await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None);

        Assert.NotNull(lease);
        using var copy = new MemoryStream();
        await lease!.Content.CopyToAsync(copy);
        Assert.Equal(Document, copy.ToArray());
    }

    public static TheoryData<string> ForeignScopes() => new()
    {
        "foreign firm",   // AC-030
        "wrong case",     // AC-031
        "wrong document", // AC-032
        "unassociated key", // AC-033
    };

    [Theory]
    [MemberData(nameof(ForeignScopes))]
    public async Task AnyMismatchedScopeValueReturnsNoLeaseAndReadsNothing(string scenario)
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);
        var (other, _, _) = await harness.CommitAsync(Document);
        var reads = harness.Storage.Log.Count;

        var lease = await harness.Reader().OpenAsync(
            scenario == "foreign firm" ? ContentStoreTests.OtherFirm : ContentStoreTests.Firm,
            scenario == "wrong case" ? ContentStoreTests.OtherCase : ContentStoreTests.Case,
            scenario == "wrong document" ? Guid.NewGuid() : documentId,
            scenario == "unassociated key" ? other.ContentKey : staged.ContentKey,
            CancellationToken.None);

        Assert.Null(lease);
        Assert.Equal(reads, harness.Storage.Log.Count);
        Assert.Empty(harness.Vault.Requests.Where(request => request.Contains("unwrap", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AnUnboundStagedObjectReturnsNoLease()
    {
        var harness = new SourceHarness();
        var staged = await harness.Store().StageAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, new MemoryStream(Document), 1_048_576, CancellationToken.None);

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, Guid.NewGuid(), staged.ContentKey, CancellationToken.None));
    }

    [Fact] // A purged object is never read, even if bytes lingered.
    public async Task APurgedRegistrationReturnsNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, registration) = await harness.CommitAsync(Document);
        harness.Registry.MarkPurged(registration);

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Theory] // AC-034
    [InlineData("flipped ciphertext byte")]
    [InlineData("flipped tag byte")]
    [InlineData("truncated")]
    [InlineData("extended")]
    [InlineData("chunks reordered")]
    [InlineData("header altered")]
    public async Task TamperedCiphertextReturnsNoLease(string tamper)
    {
        var harness = new SourceHarness();
        var (staged, documentId, registration) = await harness.CommitAsync(Document);
        var name = SourceEnvelopeCodec.ContentObjectName(registration.ResourceId);
        var stored = harness.Storage.Objects[name];

        harness.Storage.Replace(name, EnvelopeCodecTests.Tamper(stored, tamper));

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Fact] // Substitution: another resource's objects under this resource's name.
    public async Task ObjectsTransplantedFromAnotherResourceReturnNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, registration) = await harness.CommitAsync(Document);
        var (_, _, donor) = await harness.CommitAsync(Document);
        var objects = harness.Storage.Objects;

        harness.Storage.Replace(
            SourceEnvelopeCodec.ContentObjectName(registration.ResourceId),
            objects[SourceEnvelopeCodec.ContentObjectName(donor.ResourceId)]);
        harness.Storage.Replace(
            SourceEnvelopeCodec.KeyObjectName(registration.ResourceId),
            objects[SourceEnvelopeCodec.KeyObjectName(donor.ResourceId)]);

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Fact]
    public async Task ATamperedKeyEnvelopeReturnsNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, registration) = await harness.CommitAsync(Document);
        var name = SourceEnvelopeCodec.KeyObjectName(registration.ResourceId);
        var envelope = harness.Storage.Objects[name];
        envelope[^1] ^= 0x01;
        harness.Storage.Replace(name, envelope);

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Fact] // AC-035
    public async Task AWrappingKeyVersionThatCannotBeUsedReturnsNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);
        harness.Vault.Disable(SourceHarness.SourceKey, harness.SourceKeyVersion);

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Fact] // AC-035
    public async Task AReaderWithoutTheApprovedIdentityReturnsNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);

        Assert.Null(await harness.Reader(token: "synthetic-foreign-identity-token").OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Fact] // AC-045 source integration: retained old ciphertext stays readable after rotation.
    public async Task SourceWrappedBeforeARotationIsReadableWithItsRetainedVersion()
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);
        var rotated = harness.Vault.Rotate(SourceHarness.SourceKey);
        Assert.NotEqual(harness.SourceKeyVersion, rotated);

        await using var lease = await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None);

        Assert.NotNull(lease);
        Assert.Contains(
            harness.Vault.Requests,
            request => request.Contains($"/keys/{SourceHarness.SourceKey}/{harness.SourceKeyVersion}/unwrapkey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContentBeyondTheReaderBoundIsRefusedBeforeDownload()
    {
        var harness = new SourceHarness();
        var (staged, documentId, registration) = await harness.CommitAsync(Document);

        Assert.Null(await harness.Reader(maximumSourceBytes: 100_000).OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
        Assert.DoesNotContain(
            harness.Storage.Log,
            entry => entry.Method == "GET" && entry.Name == SourceEnvelopeCodec.ContentObjectName(registration.ResourceId));
    }

    [Fact]
    public async Task MissingObjectsReturnNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, registration) = await harness.CommitAsync(Document);
        await new AzureSourceObjectPurger(harness.Container).PurgeAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, registration.ResourceId, CancellationToken.None);

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Fact]
    public async Task AStorageFailureReturnsNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);
        harness.Storage.FailOperation = "read";

        Assert.Null(await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-canonical")]
    public async Task ANonCanonicalKeyOrEmptyScopeReturnsNoLeaseWithoutAnyLookup(string key)
    {
        var harness = new SourceHarness();

        Assert.Null(await harness.Reader().OpenAsync(ContentStoreTests.Firm, ContentStoreTests.Case, Guid.NewGuid(), key, CancellationToken.None));
        Assert.Null(await harness.Reader().OpenAsync(ContentStoreTests.Firm, Guid.Empty, Guid.NewGuid(), new string('A', 43), CancellationToken.None));
        Assert.Null(await harness.Reader().OpenAsync(ContentStoreTests.Firm, ContentStoreTests.Case, Guid.Empty, new string('A', 43), CancellationToken.None));
        Assert.Equal(0, harness.Registry.FindBoundCalls);
    }

    [Fact] // AC-036: a finished read clears its in-memory plaintext.
    public async Task DisposingTheLeaseClearsThePlaintextBuffer()
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);

        var lease = await harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, CancellationToken.None);
        var buffer = PlaintextBuffer(lease!.Content);
        Assert.Equal(Document, buffer);

        await lease.DisposeAsync();

        Assert.True(lease.IsDisposed);
        Assert.All(buffer, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => lease.Content);
    }

    [Fact] // AC-036: cancellation propagates and releases nothing.
    public async Task CancellationPropagatesAndReturnsNoLease()
    {
        var harness = new SourceHarness();
        var (staged, documentId, _) = await harness.CommitAsync(Document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Reader().OpenAsync(
            ContentStoreTests.Firm, ContentStoreTests.Case, documentId, staged.ContentKey, cancelled.Token));
    }

    [Fact]
    public void TheReaderIsStructurallyRenderedAndRefusesAnUnboundedConfiguration()
    {
        var harness = new SourceHarness();

        Assert.Equal(nameof(AzureExtractionContentReader), harness.Reader().ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => harness.Reader(maximumSourceBytes: 0));
        Assert.IsAssignableFrom<IExtractionContentReader>(harness.Reader());
    }

    private static byte[] PlaintextBuffer(Stream content) =>
        (byte[])content.GetType()
            .GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(content)!;
}
