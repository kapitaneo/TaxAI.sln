using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Control;

/// <summary>
/// Reads the approved release evidence for one exact release from the Canadian
/// evidence store (spec 016 T06, FR-14).
/// <para>
/// The release process publishes the secret-free evidence manifest to the
/// immutable <see cref="EvidenceContainerName"/> container in both control
/// accounts, named by a SHA-256 of the exact artifact version, configuration
/// digest and subscription — so evidence for one binding can never be read for
/// another. The container is immutable, so evidence is withdrawn by publishing
/// a create-only withdrawal marker for the same binding; a withdrawal found in
/// <b>either</b> reachable copy wins over a document in the other, so later
/// invalidation withdraws authorization without redeploying the host.
/// </para>
/// <para>
/// The application identity holds read access only. Nothing is logged; the
/// result carries a closed state and, when present, the manifest text.
/// </para>
/// </summary>
public sealed class AzureProductionEvidenceReader : IProductionEvidenceReader
{
    public const string EvidenceContainerName = "taxai-release-evidence";
    public const int MaximumManifestBytes = 262_144;

    public const string ReleasePrefix = "release/";
    public const string WithdrawnPrefix = "withdrawn/";

    private readonly BlobContainerClient[] _copies;

    /// <param name="canadaCentral">The Canada Central control account.</param>
    /// <param name="canadaEast">The Canada East control account.</param>
    public AzureProductionEvidenceReader(BlobServiceClient canadaCentral, BlobServiceClient canadaEast)
    {
        ArgumentNullException.ThrowIfNull(canadaCentral);
        ArgumentNullException.ThrowIfNull(canadaEast);

        _copies =
        [
            canadaCentral.GetBlobContainerClient(EvidenceContainerName),
            canadaEast.GetBlobContainerClient(EvidenceContainerName),
        ];
    }

    public async Task<ProductionEvidenceDocument> ReadAsync(
        ProductionReleaseBinding release,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        var key = BindingKey(release);
        string? manifest = null;
        var reachable = false;
        var invalid = false;

        foreach (var copy in _copies)
        {
            var withdrawn = await ReadAsync(copy, WithdrawnPrefix + key + ".json", cancellationToken).ConfigureAwait(false);
            if (withdrawn.State == State.Found || withdrawn.State == State.Invalid)
            {
                // Any trace of a withdrawal, even a malformed one, withdraws.
                return ProductionEvidenceDocument.Refused(ProductionEvidenceDocumentStatus.Withdrawn);
            }

            var document = await ReadAsync(copy, ReleasePrefix + key + ".json", cancellationToken).ConfigureAwait(false);
            if (withdrawn.State == State.Unavailable || document.State == State.Unavailable)
            {
                continue;
            }

            reachable = true;
            if (document.State == State.Invalid)
            {
                invalid = true;
            }
            else if (document.State == State.Found)
            {
                manifest ??= document.Text;
                if (!string.Equals(manifest, document.Text, StringComparison.Ordinal))
                {
                    // Two copies disagreeing about one binding is not evidence.
                    invalid = true;
                }
            }
        }

        if (invalid)
        {
            return ProductionEvidenceDocument.Refused(ProductionEvidenceDocumentStatus.Invalid);
        }

        if (manifest is not null)
        {
            return ProductionEvidenceDocument.Present(manifest);
        }

        return ProductionEvidenceDocument.Refused(reachable
            ? ProductionEvidenceDocumentStatus.Absent
            : ProductionEvidenceDocumentStatus.Unavailable);
    }

    /// <summary>
    /// The opaque name segment for one exact binding. The subscription is part
    /// of the hash, never of the name.
    /// </summary>
    public static string BindingKey(ProductionReleaseBinding release)
    {
        ArgumentNullException.ThrowIfNull(release);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
            '\n',
            release.ArtifactVersion,
            release.ConfigurationDigest,
            release.SubscriptionId.ToString("D")))));
    }

    public override string ToString() => nameof(AzureProductionEvidenceReader);

    private static async Task<(State State, string? Text)> ReadAsync(
        BlobContainerClient container,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var download = await container.GetBlobClient(name)
                .DownloadContentAsync(cancellationToken)
                .ConfigureAwait(false);
            var content = download.Value.Content.ToArray();
            if (content.Length is 0 or > MaximumManifestBytes)
            {
                return (State.Invalid, null);
            }

            return (State.Found, new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content));
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            return (State.Absent, null);
        }
        catch (DecoderFallbackException)
        {
            return (State.Invalid, null);
        }
        catch (Exception failure) when (AzureRecoveryControlStore.IsUnavailable(failure, cancellationToken))
        {
            return (State.Unavailable, null);
        }
    }

    private enum State
    {
        Unavailable = 0,
        Found = 1,
        Absent = 2,
        Invalid = 3,
    }
}
