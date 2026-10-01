using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Control;

/// <summary>
/// The independent recovery control ledger over two private Canadian Blob
/// accounts (spec 016 T06, FR-10, FR-11; AC-102, AC-103, AC-115).
/// <para>
/// Each account holds the same two containers: the ledger, which this
/// application may append to, and the authorization attestations, which only
/// the trusted provisioning process writes. Both carry a locked time-based
/// immutability policy longer than the longest recoverable backup window
/// (<c>infra/azure/modules/control-storage.bicep</c>), so a record cannot be
/// changed or deleted while any backup that predates it could be restored.
/// </para>
/// <para>
/// <b>Acknowledgement is observed, never assumed.</b> A record is written
/// create-only (<c>If-None-Match: *</c>) to the authoritative Canada East copy
/// and then read back; a terminal destruction fact is then written and read
/// back in the Canada Central copy too, and is acknowledged only when both
/// copies hold an identical record. A genesis or release fact needs only the
/// Canada East copy (Canada Central is written best-effort afterwards), so an
/// authorized Canadian recovery is never blocked by a Canada Central outage.
/// Asynchronous storage replication is never counted.
/// </para>
/// <para>
/// Records carry opaque identifiers and one UTC instant; names are derived from
/// those identifiers by SHA-256 or from the incarnation fingerprint, so no blob
/// name or body holds a taxpayer value. Nothing here logs, and nothing returned
/// carries an exception, endpoint or blob name.
/// </para>
/// </summary>
public sealed class AzureRecoveryControlStore : IRecoveryControlStore
{
    public const string LedgerContainerName = "taxai-recovery-ledger";
    public const string AttestationContainerName = "taxai-recovery-attestation";
    public const int MaximumRecordBytes = 4_096;
    public const int MaximumAttestationBytes = 1_048_576;

    /// <summary>[ASSUMED] The bound on retained records one read will accept.</summary>
    public const int MaximumRetainedRecords = 200_000;

    public const string GenesisName = "genesis.json";
    public const string ReleasePrefix = "release/";
    public const string TerminalPrefix = "terminal/";
    public const string AttestationPrefix = "attestation/";
    public const string AttestationInstantFormat = "yyyyMMdd'T'HHmmssfffffff'Z'";

    private const int SchemaVersion = 1;

    private readonly BlobContainerClient _centralLedger;
    private readonly BlobContainerClient _eastLedger;
    private readonly BlobContainerClient _eastAttestations;
    private readonly TimeProvider _clock;

    /// <param name="canadaCentral">The Canada Central ledger account.</param>
    /// <param name="canadaEast">The authoritative Canada East ledger account.</param>
    public AzureRecoveryControlStore(
        BlobServiceClient canadaCentral,
        BlobServiceClient canadaEast,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(canadaCentral);
        ArgumentNullException.ThrowIfNull(canadaEast);
        ArgumentNullException.ThrowIfNull(clock);

        _centralLedger = canadaCentral.GetBlobContainerClient(LedgerContainerName);
        _eastLedger = canadaEast.GetBlobContainerClient(LedgerContainerName);
        _eastAttestations = canadaEast.GetBlobContainerClient(AttestationContainerName);
        _clock = clock;
    }

    public async Task<RecoveryControlAppendOutcome> AppendAsync(
        RecoveryControlRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var name = NameOf(record);
        var body = Serialize(record);

        var east = await WriteVerifiedAsync(_eastLedger, name, body, record, cancellationToken).ConfigureAwait(false);
        if (east != RecoveryControlAppendOutcome.Acknowledged)
        {
            return east;
        }

        var central = await WriteVerifiedAsync(_centralLedger, name, body, record, cancellationToken).ConfigureAwait(false);
        if (record.Kind is RecoveryControlRecordKind.Genesis or RecoveryControlRecordKind.Release)
        {
            // The authoritative copy acknowledged it; Canada Central is a
            // best-effort second copy. A conflicting one is still an integrity
            // failure worth refusing on.
            return central == RecoveryControlAppendOutcome.Conflict
                ? RecoveryControlAppendOutcome.Conflict
                : RecoveryControlAppendOutcome.Acknowledged;
        }

        return central;
    }

    public async Task<RecoveryControlSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var records = new List<RecoveryControlRecord>();
        try
        {
            await foreach (var item in _eastLedger
                               .GetBlobsAsync(cancellationToken: cancellationToken)
                               .ConfigureAwait(false))
            {
                if (records.Count >= MaximumRetainedRecords ||
                    item.Properties.ContentLength is not (> 0 and <= MaximumRecordBytes))
                {
                    return RecoveryControlSnapshot.Invalid(Now());
                }

                var read = await ReadRecordAsync(_eastLedger, item.Name, cancellationToken).ConfigureAwait(false);
                if (read.State != ObjectState.Found)
                {
                    // Listed but now missing is as untrustworthy as malformed.
                    return read.State == ObjectState.Unavailable
                        ? RecoveryControlSnapshot.Unavailable(Now())
                        : RecoveryControlSnapshot.Invalid(Now());
                }

                records.Add(read.Record!);
            }

            var attestation = await ReadNewestAttestationAsync(cancellationToken).ConfigureAwait(false);
            if (attestation.State is ObjectState.Unavailable)
            {
                return RecoveryControlSnapshot.Unavailable(Now());
            }

            if (attestation.State is ObjectState.Invalid)
            {
                return RecoveryControlSnapshot.Invalid(Now());
            }

            return new RecoveryControlSnapshot(
                RecoveryControlReadStatus.Complete,
                Now(),
                records,
                attestation.Attestation);
        }
        catch (Exception failure) when (IsUnavailable(failure, cancellationToken))
        {
            return RecoveryControlSnapshot.Unavailable(Now());
        }
    }

    public async Task<bool> HasReleaseAsync(
        DatabaseIncarnation incarnation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incarnation);

        var east = await FindReleaseAsync(_eastLedger, incarnation, cancellationToken).ConfigureAwait(false);
        if (east != ObjectState.Unavailable)
        {
            return east == ObjectState.Found;
        }

        // Canada East is unreachable. Canada Central holds a genesis or release
        // record only after Canada East acknowledged it, so its presence there
        // still proves the release; its absence proves nothing.
        return await FindReleaseAsync(_centralLedger, incarnation, cancellationToken).ConfigureAwait(false) ==
               ObjectState.Found;
    }

    public override string ToString() => nameof(AzureRecoveryControlStore);

    /// <summary>
    /// The one blob name a record may have. Terminal names are partitioned by
    /// the recorded UTC day and hash the record's identifiers, so a retried
    /// append of the same fact addresses the same object.
    /// </summary>
    public static string NameOf(RecoveryControlRecord record) => record.Kind switch
    {
        RecoveryControlRecordKind.Genesis => GenesisName,
        RecoveryControlRecordKind.Release => ReleasePrefix + record.Incarnation!.Fingerprint + ".json",
        _ => TerminalPrefix +
             record.RecordedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" +
             Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
                 '|',
                 (int)record.Kind,
                 record.Firm!.Value.ToString("D"),
                 record.CaseId.ToString("D"),
                 record.ResourceId.ToString("D"))))) + ".json",
    };

    /// <summary>The canonical UTF-8 JSON body of a record.</summary>
    public static byte[] Serialize(RecoveryControlRecord record)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("kind", record.Kind.ToString());
            writer.WriteString("recordedAtUtc", record.RecordedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            WriteNullable(writer, "incarnation", record.Incarnation?.Fingerprint);
            WriteNullable(writer, "firmId", record.Firm?.Value.ToString("D"));
            WriteNullable(writer, "caseId", record.CaseId == Guid.Empty ? null : record.CaseId.ToString("D"));
            WriteNullable(writer, "resourceId", record.ResourceId == Guid.Empty ? null : record.ResourceId.ToString("D"));
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Parses a stored record strictly: exactly the known properties, the
    /// known schema, a UTC instant, and the shape its kind requires. The record
    /// must also be stored under the one name it may have.
    /// </summary>
    public static RecoveryControlRecord? Parse(ReadOnlySpan<byte> body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            string[] allowed = ["schemaVersion", "kind", "recordedAtUtc", "incarnation", "firmId", "caseId", "resourceId"];
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal)) ||
                root.EnumerateObject().Count() != allowed.Length ||
                root.GetProperty("schemaVersion").GetInt32() != SchemaVersion ||
                !Enum.TryParse<RecoveryControlRecordKind>(root.GetProperty("kind").GetString(), ignoreCase: false, out var kind) ||
                !Enum.IsDefined(kind) ||
                !DateTimeOffset.TryParseExact(
                    root.GetProperty("recordedAtUtc").GetString(),
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var recordedAtUtc) ||
                recordedAtUtc.Offset != TimeSpan.Zero)
            {
                return null;
            }

            var incarnationText = NullableString(root, "incarnation");
            var firm = NullableGuid(root, "firmId");
            var caseId = NullableGuid(root, "caseId");
            var resourceId = NullableGuid(root, "resourceId");

            RecoveryControlRecord record;
            switch (kind)
            {
                case RecoveryControlRecordKind.Genesis or RecoveryControlRecordKind.Release:
                    if (!DatabaseIncarnation.TryCreate(incarnationText, out var incarnation) ||
                        firm is not null || caseId is not null || resourceId is not null)
                    {
                        return null;
                    }

                    record = kind == RecoveryControlRecordKind.Genesis
                        ? RecoveryControlRecord.Genesis(incarnation!, recordedAtUtc)
                        : RecoveryControlRecord.Release(incarnation!, recordedAtUtc);
                    break;
                case RecoveryControlRecordKind.TerminalSource:
                    if (incarnationText is not null || firm is not { } sourceFirm || caseId is not { } sourceCase ||
                        resourceId is not { } sourceResource)
                    {
                        return null;
                    }

                    record = RecoveryControlRecord.TerminalSource(
                        TrustedFirmId.FromGuid(sourceFirm), sourceCase, sourceResource, recordedAtUtc);
                    break;
                default:
                    if (incarnationText is not null || firm is not { } structuredFirm || caseId is not null ||
                        resourceId is not { } recordId)
                    {
                        return null;
                    }

                    record = RecoveryControlRecord.TerminalStructured(
                        TrustedFirmId.FromGuid(structuredFirm), recordId, recordedAtUtc);
                    break;
            }

            return string.Equals(NameOf(record), name, StringComparison.Ordinal) ? record : null;
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or
                                            FormatException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a stored attestation strictly. Its name must encode its own UTC
    /// instant, so listing order is attestation order.
    /// </summary>
    public static RecoveryAuthorizationAttestation? ParseAttestation(ReadOnlySpan<byte> body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 3 ||
                root.GetProperty("schemaVersion").GetInt32() != SchemaVersion ||
                !DateTimeOffset.TryParseExact(
                    root.GetProperty("recordedAtUtc").GetString(),
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var recordedAtUtc) ||
                recordedAtUtc.Offset != TimeSpan.Zero ||
                !string.Equals(AttestationNameOf(recordedAtUtc), name, StringComparison.Ordinal))
            {
                return null;
            }

            var memberships = root.GetProperty("memberships");
            if (memberships.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new List<RecoveryAuthorizedMembership>();
            foreach (var membership in memberships.EnumerateArray())
            {
                if (membership.ValueKind != JsonValueKind.Object ||
                    membership.EnumerateObject().Count() != 2 ||
                    NullableGuid(membership, "firmId") is not { } firm ||
                    NullableGuid(membership, "userId") is not { } user)
                {
                    return null;
                }

                parsed.Add(new RecoveryAuthorizedMembership(TrustedFirmId.FromGuid(firm), user));
            }

            return new RecoveryAuthorizationAttestation(recordedAtUtc, parsed);
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or
                                            FormatException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The one name an attestation recorded at an instant may have.</summary>
    public static string AttestationNameOf(DateTimeOffset recordedAtUtc) =>
        AttestationPrefix + recordedAtUtc.UtcDateTime.ToString(AttestationInstantFormat, CultureInfo.InvariantCulture) + ".json";

    private async Task<ObjectState> FindReleaseAsync(
        BlobContainerClient ledger,
        DatabaseIncarnation incarnation,
        CancellationToken cancellationToken)
    {
        var release = await ReadRecordAsync(ledger, ReleasePrefix + incarnation.Fingerprint + ".json", cancellationToken)
            .ConfigureAwait(false);
        if (release.State == ObjectState.Found)
        {
            return release.Record!.Incarnation!.SameAs(incarnation) ? ObjectState.Found : ObjectState.Invalid;
        }

        if (release.State != ObjectState.Absent)
        {
            return release.State;
        }

        var genesis = await ReadRecordAsync(ledger, GenesisName, cancellationToken).ConfigureAwait(false);
        return genesis.State switch
        {
            ObjectState.Found => genesis.Record!.Incarnation!.SameAs(incarnation) ? ObjectState.Found : ObjectState.Absent,
            _ => genesis.State,
        };
    }

    private async Task<RecoveryControlAppendOutcome> WriteVerifiedAsync(
        BlobContainerClient ledger,
        string name,
        byte[] body,
        RecoveryControlRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await ledger.GetBlobClient(name)
                    .UploadAsync(
                        new BinaryData(body),
                        new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException failure) when (failure.Status is 409 or 412)
            {
                // Already present: it counts only if it states the same fact.
            }

            // Read back from the copy itself: the write is proven, not assumed.
            var stored = await ReadRecordAsync(ledger, name, cancellationToken).ConfigureAwait(false);
            return stored.State switch
            {
                ObjectState.Found when stored.Record!.SameAs(record) => RecoveryControlAppendOutcome.Acknowledged,
                ObjectState.Found or ObjectState.Invalid => RecoveryControlAppendOutcome.Conflict,
                _ => RecoveryControlAppendOutcome.Unacknowledged,
            };
        }
        catch (Exception failure) when (IsUnavailable(failure, cancellationToken))
        {
            return RecoveryControlAppendOutcome.Unacknowledged;
        }
    }

    private static async Task<(ObjectState State, RecoveryControlRecord? Record)> ReadRecordAsync(
        BlobContainerClient ledger,
        string name,
        CancellationToken cancellationToken)
    {
        var body = await ReadBoundedAsync(ledger, name, MaximumRecordBytes, cancellationToken).ConfigureAwait(false);
        if (body.State != ObjectState.Found)
        {
            return (body.State, null);
        }

        var record = Parse(body.Content!, name);
        return record is null ? (ObjectState.Invalid, null) : (ObjectState.Found, record);
    }

    private async Task<(ObjectState State, RecoveryAuthorizationAttestation? Attestation)> ReadNewestAttestationAsync(
        CancellationToken cancellationToken)
    {
        string? newest = null;
        await foreach (var item in _eastAttestations
                           .GetBlobsAsync(prefix: AttestationPrefix, cancellationToken: cancellationToken)
                           .ConfigureAwait(false))
        {
            if (newest is null || string.CompareOrdinal(item.Name, newest) > 0)
            {
                newest = item.Name;
            }
        }

        if (newest is null)
        {
            return (ObjectState.Absent, null);
        }

        var body = await ReadBoundedAsync(_eastAttestations, newest, MaximumAttestationBytes, cancellationToken)
            .ConfigureAwait(false);
        if (body.State != ObjectState.Found)
        {
            return (body.State == ObjectState.Unavailable ? ObjectState.Unavailable : ObjectState.Invalid, null);
        }

        var attestation = ParseAttestation(body.Content!, newest);
        return attestation is null ? (ObjectState.Invalid, null) : (ObjectState.Found, attestation);
    }

    private static async Task<(ObjectState State, byte[]? Content)> ReadBoundedAsync(
        BlobContainerClient container,
        string name,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var download = await container.GetBlobClient(name)
                .DownloadContentAsync(cancellationToken)
                .ConfigureAwait(false);
            var content = download.Value.Content.ToArray();
            return content.Length is > 0 && content.Length <= maximumBytes
                ? (ObjectState.Found, content)
                : (ObjectState.Invalid, null);
        }
        catch (RequestFailedException failure) when (failure.Status == 404)
        {
            return (ObjectState.Absent, null);
        }
        catch (Exception failure) when (IsUnavailable(failure, cancellationToken))
        {
            return (ObjectState.Unavailable, null);
        }
    }

    private DateTimeOffset Now() => _clock.GetUtcNow().ToUniversalTime();

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static string? NullableString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new FormatException(),
        };
    }

    private static Guid? NullableGuid(JsonElement element, string name)
    {
        var text = NullableString(element, name);
        if (text is null)
        {
            return null;
        }

        return Guid.TryParseExact(text, "D", out var value) && value != Guid.Empty
            ? value
            : throw new FormatException();
    }

    internal static bool IsUnavailable(Exception failure, CancellationToken cancellationToken) =>
        failure is RequestFailedException or
            AuthenticationFailedException or
            CredentialUnavailableException or
            HttpRequestException or
            IOException or
            InvalidOperationException or
            AggregateException ||
        (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private enum ObjectState
    {
        Unavailable = 0,
        Found = 1,
        Absent = 2,
        Invalid = 3,
    }
}
