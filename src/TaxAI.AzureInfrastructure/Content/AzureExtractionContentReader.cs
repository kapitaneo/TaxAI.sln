using System.Security.Cryptography;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using ClientIntake.Contracts;
using ExtractionWorkflow.Contracts;
using TaxAI.AzureInfrastructure.Keys;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Content;

/// <summary>
/// The production extraction-only source reader (spec 016 FR-03; AC-030 to
/// AC-037).
/// <para>
/// All four scope values are required, and none authorizes a read on its own:
/// the registry must hold a <b>bound</b> registration for exactly this trusted
/// firm, case, committed document and content key, whose committed intake
/// document still names the same key. Only then is anything fetched — the key
/// envelope must name the same scope, the wrapping key must unwrap it by its
/// recorded version, and the complete bounded ciphertext must authenticate under
/// that scope before a single plaintext byte is released.
/// </para>
/// <para>
/// Every mismatch, absence, purged object, tampered or oversized ciphertext,
/// unusable wrapping key and storage failure returns <see langword="null"/>, so
/// the caller's shared empty response is not an oracle. The plaintext lives only
/// in one in-memory buffer inside the returned lease and is cleared when the
/// lease is disposed; nothing is written to a file, cache or log.
/// </para>
/// </summary>
public sealed class AzureExtractionContentReader : IExtractionContentReader
{
    private readonly BlobContainerClient _container;
    private readonly KeyVaultContentKeyWrapper _wrapper;
    private readonly ISourceRegistryStore _registry;
    private readonly long _maximumSourceBytes;

    public AzureExtractionContentReader(
        BlobContainerClient container,
        KeyVaultContentKeyWrapper wrapper,
        ISourceRegistryStore registry,
        long maximumSourceBytes)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(wrapper);
        ArgumentNullException.ThrowIfNull(registry);
        if (maximumSourceBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSourceBytes));
        }

        _container = container;
        _wrapper = wrapper;
        _registry = registry;
        _maximumSourceBytes = maximumSourceBytes;
    }

    public async Task<ExtractionSourceLease?> OpenAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty ||
            documentId == Guid.Empty ||
            !AzureIntakeContentStore.IsCanonicalKey(contentKey))
        {
            return null;
        }

        byte[]? dataKey = null;
        byte[]? plaintext = null;
        try
        {
            var registration = await _registry
                .FindBoundAsync(firm, caseId, documentId, contentKey, cancellationToken)
                .ConfigureAwait(false);
            if (registration is null ||
                !registration.Firm.Equals(firm) ||
                registration.CaseId != caseId ||
                registration.DocumentId != documentId ||
                !string.Equals(registration.ContentKey, contentKey, StringComparison.Ordinal))
            {
                return null;
            }

            var scope = new SourceEnvelopeScope(firm, caseId, registration.ResourceId);
            var envelope = await DownloadBoundedAsync(
                    SourceEnvelopeCodec.KeyObjectName(registration.ResourceId),
                    SourceEnvelopeCodec.MaximumKeyEnvelopeBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var ciphertext = await DownloadBoundedAsync(
                    SourceEnvelopeCodec.ContentObjectName(registration.ResourceId),
                    SourceEnvelopeCodec.MaximumCiphertextLength(_maximumSourceBytes),
                    cancellationToken)
                .ConfigureAwait(false);
            if (envelope is null || ciphertext is null)
            {
                return null;
            }

            var wrapped = SourceEnvelopeCodec.DecodeKeyEnvelope(envelope, scope);
            dataKey = await _wrapper.UnwrapAsync(wrapped, cancellationToken).ConfigureAwait(false);
            plaintext = SourceEnvelopeCodec.Decrypt(dataKey, scope, ciphertext, _maximumSourceBytes);

            var lease = ExtractionSourceLease.Create(new ClearingReadStream(plaintext));
            plaintext = null;
            return lease;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (IsRefusal(failure))
        {
            return null;
        }
        finally
        {
            if (dataKey is not null)
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public override string ToString() => nameof(AzureExtractionContentReader);

    /// <summary>
    /// Downloads one object into memory only when its reported length is within
    /// the bound, and refuses a body longer than it reported.
    /// </summary>
    private async Task<byte[]?> DownloadBoundedAsync(
        string name,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(name);
        var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var length = properties.Value.ContentLength;
        if (length <= 0 || length > maximumBytes || length > int.MaxValue)
        {
            return null;
        }

        var result = await blob
            .DownloadStreamingAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await using var body = result.Value.Content;
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await body
                .ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        var trailing = new byte[1];
        return await body.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) == 0
            ? buffer
            : null;
    }

    private static bool IsRefusal(Exception failure) =>
        failure is RequestFailedException or
            CryptographicException or
            AuthenticationFailedException or
            CredentialUnavailableException or
            HttpRequestException or
            IOException or
            InvalidOperationException or
            ArgumentException or
            OperationCanceledException;

    /// <summary>
    /// A read-only in-memory stream that clears its buffer on disposal.
    /// </summary>
    private sealed class ClearingReadStream : MemoryStream
    {
        private readonly byte[] _content;
        private bool _cleared;

        internal ClearingReadStream(byte[] content)
            : base(content, writable: false) =>
            _content = content;

        protected override void Dispose(bool disposing)
        {
            if (!_cleared)
            {
                CryptographicOperations.ZeroMemory(_content);
                _cleared = true;
            }

            base.Dispose(disposing);
        }

        public override string ToString() => nameof(ClearingReadStream);
    }
}
