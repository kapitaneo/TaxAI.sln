using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using ClientIntake.Contracts;
using ClientIntake.Models;
using TaxAI.AzureInfrastructure.Keys;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Content;

/// <summary>
/// The production write-only source store (spec 016 FR-03/FR-04; AC-026,
/// AC-027, AC-038, AC-039).
/// <para>
/// One upload becomes exactly two upload-once objects in the private Canadian
/// source container: the authenticated ciphertext and, separately, its key
/// envelope holding the Key-Vault-wrapped per-content data key. Both names are
/// the opaque resource identifier — never the content key, a file name or a
/// taxpayer value — and both are written with <c>If-None-Match: *</c>, so an
/// existing object can never be overwritten.
/// </para>
/// <para>
/// The registry entry is committed <b>before</b> any storage allocation, so no
/// object can exist untracked. Plaintext is streamed one 64 KiB chunk at a time
/// into bounded in-memory staged blocks; it never touches a file. Any failure
/// or cancellation triggers compensating cleanup of whatever was allocated, and
/// the caller sees one fixed refusal that names nothing.
/// </para>
/// <para>
/// There is deliberately no read member (the intake port is write-only), no SAS,
/// no URL and no public access. The fingerprint is the same scoped keyed HMAC
/// the intake workflow compares for duplicate detection; its key is the
/// production fingerprint material loaded from Key Vault.
/// </para>
/// </summary>
public sealed class AzureIntakeContentStore : IIntakeContentStore
{
    /// <summary>The single private source container.</summary>
    public const string SourceContainerName = "taxai-source";

    /// <summary>The bounded size of one staged block of ciphertext.</summary>
    public const int BlockSize = 4 * 1024 * 1024;

    /// <summary>[ASSUMED] The finite budget for compensating cleanup of one upload.</summary>
    public static readonly TimeSpan CompensationBudget = TimeSpan.FromSeconds(30);

    internal const string RefusalMessage = "The content could not be staged.";

    private readonly BlobContainerClient _container;
    private readonly KeyVaultContentKeyWrapper _wrapper;
    private readonly ISourceRegistryStore _registry;
    private readonly ISourceObjectPurger _purger;
    private readonly byte[] _fingerprintKey;
    private readonly TimeProvider _time;

    public AzureIntakeContentStore(
        BlobContainerClient container,
        KeyVaultContentKeyWrapper wrapper,
        ISourceRegistryStore registry,
        ISourceObjectPurger purger,
        ReadOnlyMemory<byte> fingerprintKey,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(wrapper);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(purger);
        ArgumentNullException.ThrowIfNull(time);
        if (fingerprintKey.Length != 32)
        {
            throw new ArgumentException("The fingerprint key is invalid.", nameof(fingerprintKey));
        }

        _container = container;
        _wrapper = wrapper;
        _registry = registry;
        _purger = purger;
        _fingerprintKey = fingerprintKey.ToArray();
        _time = time;
    }

    public async Task<StagedIntakeContent> StageAsync(
        TrustedFirmId firm,
        Guid caseId,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(content);
        if (caseId == Guid.Empty || maximumBytes <= 0 || !content.CanRead)
        {
            throw new ArgumentException(RefusalMessage);
        }

        var resourceId = Guid.NewGuid();
        var contentKey = Encode(RandomNumberGenerator.GetBytes(32));
        var scope = new SourceEnvelopeScope(firm, caseId, resourceId);

        // Tracked before anything is allocated: a crash after this point leaves a
        // staged registry row for compensating cleanup, never an untracked object.
        var registered = await _registry
            .RegisterStagedAsync(firm, caseId, resourceId, contentKey, UtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (registered != SourceRegistryWriteCode.Success)
        {
            throw new InvalidOperationException(RefusalMessage);
        }

        var dataKey = RandomNumberGenerator.GetBytes(SourceEnvelopeCodec.DataKeySize);
        try
        {
            var wrapped = await _wrapper.WrapAsync(dataKey, cancellationToken).ConfigureAwait(false);
            await _container
                .GetBlobClient(SourceEnvelopeCodec.KeyObjectName(resourceId))
                .UploadAsync(
                    BinaryData.FromBytes(SourceEnvelopeCodec.EncodeKeyEnvelope(scope, wrapped)),
                    UploadOnce(),
                    cancellationToken)
                .ConfigureAwait(false);

            using var fingerprint = IncrementalHash.CreateHMAC(
                HashAlgorithmName.SHA256,
                ScopedFingerprintKey(firm, caseId));
            var sink = new BlockStagingStream(
                _container.GetBlockBlobClient(SourceEnvelopeCodec.ContentObjectName(resourceId)));
            var byteCount = await SourceEnvelopeCodec
                .EncryptAsync(dataKey, scope, content, sink, maximumBytes, fingerprint, cancellationToken)
                .ConfigureAwait(false);
            await sink.CommitOnceAsync(cancellationToken).ConfigureAwait(false);

            return new StagedIntakeContent(contentKey, byteCount, fingerprint.GetHashAndReset());
        }
        catch (Exception failure)
        {
            await CompensateAsync(firm, caseId, resourceId).ConfigureAwait(false);
            if (failure is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new InvalidOperationException(RefusalMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>
    /// Compensating cleanup for an object that never became reachable metadata.
    /// A committed (bound) object is never deleted here — an uncertain metadata
    /// commit that actually succeeded must not lose its source — and neither is an
    /// unknown key or a key of another scope.
    /// </summary>
    public async Task DeleteIfExistsAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || !IsCanonicalKey(contentKey))
        {
            throw new ArgumentException("The content key is invalid.");
        }

        var registration = await _registry
            .FindAsync(firm, caseId, contentKey, cancellationToken)
            .ConfigureAwait(false);
        if (registration is not { State: SourceRegistryState.Staged or SourceRegistryState.PurgePending })
        {
            return;
        }

        await PurgeUncommittedAsync(firm, caseId, registration.ResourceId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Replays cleanup for one trusted firm: every pending cleanup and every
    /// staged object older than <paramref name="stagedBeforeUtc"/> that never
    /// became reachable. Returns the verified outcome of each attempt, in order.
    /// This is the orphan and recovery-replay path; it never touches a bound object.
    /// </summary>
    public async Task<IReadOnlyList<SourceObjectPurgeOutcome>> CleanUpUncommittedAsync(
        TrustedFirmId firm,
        DateTimeOffset stagedBeforeUtc,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);

        var work = await _registry
            .ListUncommittedAsync(firm, stagedBeforeUtc, maximumCount, cancellationToken)
            .ConfigureAwait(false);
        var outcomes = new List<SourceObjectPurgeOutcome>(work.Count);
        foreach (var registration in work)
        {
            if (!registration.Firm.Equals(firm))
            {
                continue;
            }

            outcomes.Add(await PurgeUncommittedAsync(
                    firm,
                    registration.CaseId,
                    registration.ResourceId,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return outcomes;
    }

    public override string ToString() => nameof(AzureIntakeContentStore);

    private async Task<SourceObjectPurgeOutcome> PurgeUncommittedAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        // Intent first: this conditional move out of Staged is what makes a racing
        // metadata commit lose, so the object is never deleted after it was bound.
        var intent = await _registry
            .RecordPurgeAsync(
                firm,
                caseId,
                resourceId,
                SourceObjectPurgeOutcome.Unavailable,
                UtcNow(),
                cancellationToken)
            .ConfigureAwait(false);
        if (intent != SourceRegistryWriteCode.Success)
        {
            return SourceObjectPurgeOutcome.Unavailable;
        }

        var outcome = await _purger
            .PurgeAsync(firm, caseId, resourceId, cancellationToken)
            .ConfigureAwait(false);
        await _registry
            .RecordPurgeAsync(firm, caseId, resourceId, outcome, UtcNow(), cancellationToken)
            .ConfigureAwait(false);
        return outcome;
    }

    private async Task CompensateAsync(TrustedFirmId firm, Guid caseId, Guid resourceId)
    {
        // Bounded: cleanup is attempted even after the caller cancelled, but never
        // becomes an unlimited detached task.
        using var budget = new CancellationTokenSource(CompensationBudget);
        try
        {
            await PurgeUncommittedAsync(firm, caseId, resourceId, budget.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The staged registry row remains; the cleanup replay retries it.
        }
    }

    private DateTimeOffset UtcNow() => _time.GetUtcNow().ToUniversalTime();

    private byte[] ScopedFingerprintKey(TrustedFirmId firm, Guid caseId) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _fingerprintKey,
            outputLength: 32,
            salt: Array.Empty<byte>(),
            info: Encoding.UTF8.GetBytes(
                $"TaxAI.Intake.Fingerprint.v1|{firm.ToCanonicalString()}|{caseId:D}"));

    private static BlobUploadOptions UploadOnce() => new()
    {
        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
    };

    internal static bool IsCanonicalKey(string? contentKey) =>
        contentKey is { Length: 43 } &&
        contentKey.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>
    /// A write-only stream that stages ciphertext as bounded blocks of the
    /// content object and commits the block list exactly once, conditioned on
    /// the object not existing yet.
    /// </summary>
    private sealed class BlockStagingStream : Stream
    {
        private readonly BlockBlobClient _blob;
        private readonly List<string> _blockIds = new();
        private readonly byte[] _buffer = new byte[BlockSize];
        private int _count;

        internal BlockStagingStream(BlockBlobClient blob) => _blob = blob;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            while (buffer.Length > 0)
            {
                var take = Math.Min(buffer.Length, _buffer.Length - _count);
                buffer[..take].CopyTo(_buffer.AsMemory(_count));
                _count += take;
                buffer = buffer[take..];
                if (_count == _buffer.Length)
                {
                    await StageAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        internal async Task CommitOnceAsync(CancellationToken cancellationToken)
        {
            if (_count > 0)
            {
                await StageAsync(cancellationToken).ConfigureAwait(false);
            }

            await _blob
                .CommitBlockListAsync(
                    _blockIds,
                    new CommitBlockListOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public override void Flush()
        {
        }

        public override string ToString() => nameof(BlockStagingStream);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private async Task StageAsync(CancellationToken cancellationToken)
        {
            var blockId = Convert.ToBase64String(
                Encoding.ASCII.GetBytes(_blockIds.Count.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)));
            using var block = new MemoryStream(_buffer, 0, _count, writable: false);
            await _blob.StageBlockAsync(blockId, block, cancellationToken: cancellationToken).ConfigureAwait(false);
            _blockIds.Add(blockId);
            _count = 0;
        }
    }
}
