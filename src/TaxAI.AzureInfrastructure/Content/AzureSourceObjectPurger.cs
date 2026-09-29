using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Content;

/// <summary>
/// The scoped physical deletion adapter for source objects (spec 016 T04).
/// <para>
/// It deletes one resource's ciphertext and key envelope — including any
/// snapshot, although the approved account configuration enables none — and
/// then observes both objects absent from the primary. If the stored key
/// envelope names another firm, case or resource, nothing is deleted.
/// </para>
/// <para>
/// <b>It never reports a completion it did not observe.</b> The approved
/// geo-zone-redundant account replicates asynchronously to Canada East and
/// offers the application no read access to the secondary, so replica removal
/// cannot be observed from here. A verified primary deletion is therefore
/// <see cref="SourceObjectPurgeOutcome.Pending"/>, and anything unverifiable is
/// <see cref="SourceObjectPurgeOutcome.Unavailable"/>; both stay in the registry
/// for replay. Replica and every-retained-form verification is a later slice's
/// completion evidence, not a success fabricated here.
/// </para>
/// </summary>
public sealed class AzureSourceObjectPurger : ISourceObjectPurger
{
    private readonly BlobContainerClient _container;

    public AzureSourceObjectPurger(BlobContainerClient container)
    {
        ArgumentNullException.ThrowIfNull(container);
        _container = container;
    }

    public async Task<SourceObjectPurgeOutcome> PurgeAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || resourceId == Guid.Empty)
        {
            return SourceObjectPurgeOutcome.Unavailable;
        }

        var expected = new SourceEnvelopeScope(firm, caseId, resourceId);
        var envelope = _container.GetBlobClient(SourceEnvelopeCodec.KeyObjectName(resourceId));
        var content = _container.GetBlobClient(SourceEnvelopeCodec.ContentObjectName(resourceId));

        try
        {
            if (!await EnvelopeAllowsDeletionAsync(envelope, expected, cancellationToken).ConfigureAwait(false))
            {
                return SourceObjectPurgeOutcome.Unavailable;
            }

            await content
                .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await envelope
                .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var contentAbsent = !(await content.ExistsAsync(cancellationToken).ConfigureAwait(false)).Value;
            var envelopeAbsent = !(await envelope.ExistsAsync(cancellationToken).ConfigureAwait(false)).Value;

            return contentAbsent && envelopeAbsent
                ? SourceObjectPurgeOutcome.Pending
                : SourceObjectPurgeOutcome.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (
            failure is RequestFailedException or
                AuthenticationFailedException or
                CredentialUnavailableException or
                HttpRequestException or
                IOException or
                InvalidOperationException or
                OperationCanceledException)
        {
            return SourceObjectPurgeOutcome.Unavailable;
        }
    }

    public override string ToString() => nameof(AzureSourceObjectPurger);

    /// <summary>
    /// An absent envelope (the upload failed before it was written) permits the
    /// deletion the registry already scoped; a present one must name exactly the
    /// expected scope.
    /// </summary>
    private static async Task<bool> EnvelopeAllowsDeletionAsync(
        BlobClient envelope,
        SourceEnvelopeScope expected,
        CancellationToken cancellationToken)
    {
        try
        {
            var properties = await envelope.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (properties.Value.ContentLength is <= 0 or > SourceEnvelopeCodec.MaximumKeyEnvelopeBytes)
            {
                return false;
            }

            var body = await envelope.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            return SourceEnvelopeCodec.TryReadKeyEnvelopeScope(body.Value.Content.ToMemory().Span, out var stored) &&
                   stored!.SameAs(expected);
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            return true;
        }
    }
}
