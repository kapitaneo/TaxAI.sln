using System.Text.Json.Serialization;
using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// One fact in the independent recovery control ledger (spec 016 T06, FR-10).
/// <para>
/// It carries opaque identifiers and one UTC instant only: the database
/// incarnation for <see cref="RecoveryControlRecordKind.Genesis"/> and
/// <see cref="RecoveryControlRecordKind.Release"/>, and the trusted firm plus
/// the case and resource (or the review record) for a terminal destruction.
/// No taxpayer value, name, amount, file name, key or provider identifier can
/// be expressed. Every identifier is serialization-omitted and
/// <see cref="ToString"/> names the kind only.
/// </para>
/// </summary>
public sealed class RecoveryControlRecord
{
    private RecoveryControlRecord(
        RecoveryControlRecordKind kind,
        DateTimeOffset recordedAtUtc,
        DatabaseIncarnation? incarnation,
        TrustedFirmId? firm,
        Guid caseId,
        Guid resourceId)
    {
        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A control record instant must be UTC.", nameof(recordedAtUtc));
        }

        Kind = kind;
        RecordedAtUtc = recordedAtUtc;
        Incarnation = incarnation;
        Firm = firm;
        CaseId = caseId;
        ResourceId = resourceId;
    }

    public RecoveryControlRecordKind Kind { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    /// <summary>Set for genesis and release records only.</summary>
    [JsonIgnore]
    public DatabaseIncarnation? Incarnation { get; }

    /// <summary>Set for terminal records only.</summary>
    [JsonIgnore]
    public TrustedFirmId? Firm { get; }

    /// <summary>The case of a terminal source record; empty otherwise.</summary>
    [JsonIgnore]
    public Guid CaseId { get; }

    /// <summary>
    /// The source resource of a terminal source record, or the review record of
    /// a terminal structured record.
    /// </summary>
    [JsonIgnore]
    public Guid ResourceId { get; }

    public static RecoveryControlRecord Genesis(DatabaseIncarnation incarnation, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(incarnation);
        return new RecoveryControlRecord(
            RecoveryControlRecordKind.Genesis, recordedAtUtc, incarnation, null, Guid.Empty, Guid.Empty);
    }

    public static RecoveryControlRecord Release(DatabaseIncarnation incarnation, DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(incarnation);
        return new RecoveryControlRecord(
            RecoveryControlRecordKind.Release, recordedAtUtc, incarnation, null, Guid.Empty, Guid.Empty);
    }

    public static RecoveryControlRecord TerminalSource(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || resourceId == Guid.Empty)
        {
            throw new ArgumentException("A terminal source record needs its case and resource.");
        }

        return new RecoveryControlRecord(
            RecoveryControlRecordKind.TerminalSource, recordedAtUtc, null, firm, caseId, resourceId);
    }

    public static RecoveryControlRecord TerminalStructured(
        TrustedFirmId firm,
        Guid recordId,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("A terminal structured record needs its record.", nameof(recordId));
        }

        return new RecoveryControlRecord(
            RecoveryControlRecordKind.TerminalStructured, recordedAtUtc, null, firm, Guid.Empty, recordId);
    }

    /// <summary>
    /// True when both records state the same fact. The recorded instant is part
    /// of the fact, so a retried append must resend the identical record.
    /// </summary>
    public bool SameAs(RecoveryControlRecord other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Kind == other.Kind &&
               RecordedAtUtc == other.RecordedAtUtc &&
               RecordedAtUtc.Offset == other.RecordedAtUtc.Offset &&
               (Incarnation is null
                   ? other.Incarnation is null
                   : other.Incarnation is not null && Incarnation.SameAs(other.Incarnation)) &&
               (Firm is null ? other.Firm is null : other.Firm is not null && Firm.Value == other.Firm.Value) &&
               CaseId == other.CaseId &&
               ResourceId == other.ResourceId;
    }

    public override string ToString() => $"{nameof(RecoveryControlRecord)}({Kind})";
}

/// <summary>One currently authorized active membership: opaque identifiers only.</summary>
public sealed class RecoveryAuthorizedMembership
{
    public RecoveryAuthorizedMembership(TrustedFirmId firm, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("An authorized membership needs its user.", nameof(userId));
        }

        Firm = firm;
        UserId = userId;
    }

    [JsonIgnore]
    public TrustedFirmId Firm { get; }

    [JsonIgnore]
    public Guid UserId { get; }

    public override string ToString() => nameof(RecoveryAuthorizedMembership);
}

/// <summary>
/// The trusted provisioning process's statement of every currently authorized
/// active membership, published to the control ledger's separately permissioned
/// attestation store (FR-11: "missing current authorization evidence blocks
/// release"). The application can read it but never write it.
/// </summary>
public sealed class RecoveryAuthorizationAttestation
{
    private readonly RecoveryAuthorizedMembership[] _memberships;

    public RecoveryAuthorizationAttestation(
        DateTimeOffset recordedAtUtc,
        IEnumerable<RecoveryAuthorizedMembership> memberships)
    {
        ArgumentNullException.ThrowIfNull(memberships);
        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("An attestation instant must be UTC.", nameof(recordedAtUtc));
        }

        _memberships = memberships.ToArray();
        if (_memberships.Any(membership => membership is null) ||
            _memberships
                .GroupBy(membership => (membership.Firm.Value, membership.UserId))
                .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("An attestation lists each membership exactly once.", nameof(memberships));
        }

        RecordedAtUtc = recordedAtUtc;
    }

    public DateTimeOffset RecordedAtUtc { get; }

    [JsonIgnore]
    public IReadOnlyList<RecoveryAuthorizedMembership> Memberships => _memberships.ToArray();

    public override string ToString() => nameof(RecoveryAuthorizationAttestation);
}

/// <summary>
/// The authoritative ledger as read: every retained fact in the
/// independently recoverable Canada East copy, the newest authorization
/// attestation, and when the read completed. Anything but
/// <see cref="RecoveryControlReadStatus.Complete"/> carries no facts.
/// </summary>
public sealed class RecoveryControlSnapshot
{
    private readonly RecoveryControlRecord[] _records;

    public RecoveryControlSnapshot(
        RecoveryControlReadStatus status,
        DateTimeOffset readAtUtc,
        IEnumerable<RecoveryControlRecord> records,
        RecoveryAuthorizationAttestation? attestation)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (readAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A read instant must be UTC.", nameof(readAtUtc));
        }

        _records = status == RecoveryControlReadStatus.Complete ? records.ToArray() : [];
        if (_records.Any(record => record is null))
        {
            throw new ArgumentException("A snapshot record cannot be null.", nameof(records));
        }

        Status = status;
        ReadAtUtc = readAtUtc;
        Attestation = status == RecoveryControlReadStatus.Complete ? attestation : null;
    }

    public static RecoveryControlSnapshot Unavailable(DateTimeOffset readAtUtc) =>
        new(RecoveryControlReadStatus.Unavailable, readAtUtc, [], null);

    public static RecoveryControlSnapshot Invalid(DateTimeOffset readAtUtc) =>
        new(RecoveryControlReadStatus.Invalid, readAtUtc, [], null);

    public RecoveryControlReadStatus Status { get; }

    public DateTimeOffset ReadAtUtc { get; }

    public IReadOnlyList<RecoveryControlRecord> Records => _records.ToArray();

    public RecoveryAuthorizationAttestation? Attestation { get; }

    public override string ToString() => $"{nameof(RecoveryControlSnapshot)}({Status})";
}

/// <summary>
/// The independent recovery control ledger (spec 016 T06, FR-10, FR-11).
/// <para>
/// It lives outside the restorable application database, in two separately
/// permissioned Canadian stores: a Canada Central copy and the authoritative,
/// independently recoverable Canada East copy. Records are create-only and
/// immutable for longer than the longest recoverable backup window.
/// </para>
/// <para>
/// A terminal fact is <see cref="RecoveryControlAppendOutcome.Acknowledged"/>
/// only when it is written, read back and identical in <b>both</b> copies; no
/// destructive progress may follow anything less. A genesis or release fact
/// needs the authoritative Canada East copy, so a Canada Central outage cannot
/// block an authorized Canadian recovery. A mere asynchronously replicated write
/// is never an acknowledgement.
/// </para>
/// </summary>
public interface IRecoveryControlStore
{
    Task<RecoveryControlAppendOutcome> AppendAsync(
        RecoveryControlRecord record,
        CancellationToken cancellationToken);

    /// <summary>Reads every retained fact and the newest attestation.</summary>
    Task<RecoveryControlSnapshot> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// True only when a release (or genesis) of exactly this incarnation is
    /// observed. The Canada Central copy is consulted only when Canada East is
    /// unreachable; it holds such a record only after Canada East acknowledged
    /// it. Any doubt is <see langword="false"/>.
    /// </summary>
    Task<bool> HasReleaseAsync(
        DatabaseIncarnation incarnation,
        CancellationToken cancellationToken);
}

/// <summary>
/// The stored, secret-free release evidence document for one exact release, as
/// read from the Canadian evidence store. The manifest text is omitted from
/// serialization and <see cref="ToString"/>.
/// </summary>
public sealed class ProductionEvidenceDocument
{
    private ProductionEvidenceDocument(ProductionEvidenceDocumentStatus status, string? manifestText)
    {
        Status = status;
        ManifestText = manifestText;
    }

    public static ProductionEvidenceDocument Present(string manifestText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestText);
        return new ProductionEvidenceDocument(ProductionEvidenceDocumentStatus.Present, manifestText);
    }

    public static ProductionEvidenceDocument Refused(ProductionEvidenceDocumentStatus status)
    {
        if (status == ProductionEvidenceDocumentStatus.Present || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new ProductionEvidenceDocument(status, null);
    }

    public ProductionEvidenceDocumentStatus Status { get; }

    [JsonIgnore]
    public string? ManifestText { get; }

    public override string ToString() => $"{nameof(ProductionEvidenceDocument)}({Status})";
}

/// <summary>
/// Reads the approved exact-configuration evidence for one release from the
/// Canadian evidence store (FR-14). A withdrawn document is never returned as
/// present: later invalidation withdraws authorization.
/// </summary>
public interface IProductionEvidenceReader
{
    Task<ProductionEvidenceDocument> ReadAsync(
        ProductionReleaseBinding release,
        CancellationToken cancellationToken);
}
