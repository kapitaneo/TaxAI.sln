using System.Text.Json.Serialization;
using TaxCases.Models;
using Tenancy.Models;

namespace ClientIntake.Models;

/// <summary>
/// The closed persistence vocabulary the intake store speaks.
/// <para>
/// These are the module's own immutable records, not EF entities: the domain
/// declares the shape it needs and the infrastructure adapter maps to it. That
/// direction is what keeps EF Core out of this project and keeps
/// <c>Persistence</c> the only place a table exists.
/// </para>
/// <para>
/// The history records are append-only by design. There is no mutable answer, no
/// mutable document version, no writable readiness flag, and no update or delete
/// member anywhere in the store contract — current state is projected from the
/// facts rather than overwritten onto them.
/// </para>
/// <para>
/// No record here carries a SIN in any form, a file name, a raw credential, a
/// cookie, document content, an amount, a prompt, or free text. The only secret
/// that appears is a keyed digest.
/// </para>
/// </summary>
public sealed record IntakeExpectedItemRecord(
    [property: JsonIgnore] Guid ExpectedItemId,
    DocumentCategory Category,
    int Ordinal)
{
    public override string ToString() =>
        $"{nameof(IntakeExpectedItemRecord)}({Category}, {Ordinal})";
}

/// <summary>
/// One issued bootstrap link. Only the digest is stored; the raw credential
/// exists in the process for exactly as long as it takes to render the URL once.
/// </summary>
public sealed record IntakeLinkRecord(
    [property: JsonIgnore] Guid LinkId,
    [property: JsonIgnore] IntakeTokenDigest CredentialDigest,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? SpentAtUtc,
    DateTimeOffset? RevokedAtUtc)
{
    public override string ToString() => nameof(IntakeLinkRecord);
}

public sealed record IntakeAnswerVersionRecord(
    DocumentCategory Category,
    int Version,
    AvailabilityAnswerCode Answer,
    IntakeActorKind ActorKind,
    [property: JsonIgnore] Guid ActorReference,
    DateTimeOffset RecordedAtUtc)
{
    public override string ToString() =>
        $"{nameof(IntakeAnswerVersionRecord)}({Category}, {Version}, {Answer}, {ActorKind})";
}

/// <summary>
/// Accepted upload metadata. The content itself lives behind
/// <c>IIntakeContentStore</c> under an opaque key; this record holds only the
/// key, the byte count, and the scoped keyed fingerprint.
/// </summary>
public sealed record IntakeDocumentRecord(
    [property: JsonIgnore] Guid DocumentId,
    [property: JsonIgnore] Guid ExpectedItemId,
    DocumentCategory Category,
    int Version,
    [property: JsonIgnore] string ContentKey,
    IntakeMediaKind MediaKind,
    long ByteCount,
    IReadOnlyList<byte> Fingerprint,
    DateTimeOffset UploadedAtUtc)
{
    // The attribute belongs on the explicitly declared property, not on the
    // positional parameter: a `[property:]` target on a parameter whose property
    // is declared by hand is discarded (CS0657), which would have left the
    // fingerprint serializable while looking exactly like it was omitted.
    [JsonIgnore]
    public IReadOnlyList<byte> Fingerprint { get; } =
        Array.AsReadOnly(
            (Fingerprint ?? throw new ArgumentNullException(nameof(Fingerprint))).ToArray());

    public override string ToString() =>
        $"{nameof(IntakeDocumentRecord)}({Category}, {Version}, {MediaKind})";
}

/// <summary>
/// The structural fact that an upload was attempted and how it ended. It records
/// no file name and no content — a rejected upload leaves evidence that it
/// happened, not what it was.
/// </summary>
public sealed record IntakeUploadAttemptRecord(
    [property: JsonIgnore] Guid AttemptId,
    [property: JsonIgnore] Guid ExpectedItemId,
    IntakeUploadOutcome Outcome,
    DateTimeOffset AttemptedAtUtc)
{
    public override string ToString() =>
        $"{nameof(IntakeUploadAttemptRecord)}({Outcome})";
}

public sealed record IntakeSubmissionAnswerReference(
    DocumentCategory Category,
    int AnswerVersion)
{
    public override string ToString() =>
        $"{nameof(IntakeSubmissionAnswerReference)}({Category}, {AnswerVersion})";
}

public sealed record IntakeSubmissionDocumentReference(
    [property: JsonIgnore] Guid DocumentId)
{
    public override string ToString() => nameof(IntakeSubmissionDocumentReference);
}

/// <summary>
/// One immutable submission snapshot: the exact answer versions and document
/// versions readiness was derived from. A later saved answer or accepted upload
/// never rewrites one of these.
/// </summary>
public sealed record IntakeSubmissionRecord(
    int Version,
    IReadOnlyList<IntakeSubmissionAnswerReference> Answers,
    IReadOnlyList<IntakeSubmissionDocumentReference> Documents,
    IntakeActorKind ActorKind,
    [property: JsonIgnore] Guid ActorReference,
    DateTimeOffset SubmittedAtUtc)
{
    public IReadOnlyList<IntakeSubmissionAnswerReference> Answers { get; } =
        Array.AsReadOnly((Answers ?? throw new ArgumentNullException(nameof(Answers))).ToArray());

    public IReadOnlyList<IntakeSubmissionDocumentReference> Documents { get; } =
        Array.AsReadOnly((Documents ?? throw new ArgumentNullException(nameof(Documents))).ToArray());

    public override string ToString() =>
        $"{nameof(IntakeSubmissionRecord)}({Version}, answers: {Answers.Count}, "
        + $"documents: {Documents.Count}, {ActorKind})";
}

public sealed record IntakeReviewAssociationRecord(
    [property: JsonIgnore] Guid RecordId,
    DateTimeOffset AssociatedAtUtc)
{
    public override string ToString() => nameof(IntakeReviewAssociationRecord);
}

public sealed record IntakeAuditEventRecord(
    [property: JsonIgnore] Guid EventId,
    long Sequence,
    IntakeAuditKind Kind,
    IntakeActorKind ActorKind,
    [property: JsonIgnore] Guid ActorReference,
    [property: JsonIgnore] Guid? ExpectedItemId,
    DateTimeOffset OccurredAtUtc)
{
    public override string ToString() =>
        $"{nameof(IntakeAuditEventRecord)}({Sequence}, {Kind}, {ActorKind})";
}

/// <summary>
/// One intake case as stored, with its complete immutable history. The firm is a
/// <c>TrustedFirmId</c> rather than a bare GUID, so a caller-supplied value
/// cannot reach a store read without a visible conversion.
/// </summary>
public sealed record StoredIntakeCase(
    [property: JsonIgnore] TrustedFirmId Firm,
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] Guid PrimaryParticipantId,
    [property: JsonIgnore] string PrimaryTaxpayerDisplayName,
    int TaxYear,
    CaseStatus CaseStatus,
    long ConcurrencyVersion,
    IReadOnlyList<IntakeExpectedItemRecord> ExpectedItems,
    IntakeLinkRecord CurrentLink,
    IReadOnlyList<IntakeAnswerVersionRecord> AnswerVersions,
    IReadOnlyList<IntakeDocumentRecord> Documents,
    IReadOnlyList<IntakeUploadAttemptRecord> UploadAttempts,
    IReadOnlyList<IntakeSubmissionRecord> Submissions,
    IntakeReviewAssociationRecord? ReviewAssociation)
{
    public IReadOnlyList<IntakeExpectedItemRecord> ExpectedItems { get; } =
        Array.AsReadOnly((ExpectedItems ?? throw new ArgumentNullException(nameof(ExpectedItems))).ToArray());

    public IReadOnlyList<IntakeAnswerVersionRecord> AnswerVersions { get; } =
        Array.AsReadOnly((AnswerVersions ?? throw new ArgumentNullException(nameof(AnswerVersions))).ToArray());

    public IReadOnlyList<IntakeDocumentRecord> Documents { get; } =
        Array.AsReadOnly((Documents ?? throw new ArgumentNullException(nameof(Documents))).ToArray());

    public IReadOnlyList<IntakeUploadAttemptRecord> UploadAttempts { get; } =
        Array.AsReadOnly((UploadAttempts ?? throw new ArgumentNullException(nameof(UploadAttempts))).ToArray());

    public IReadOnlyList<IntakeSubmissionRecord> Submissions { get; } =
        Array.AsReadOnly((Submissions ?? throw new ArgumentNullException(nameof(Submissions))).ToArray());

    public override string ToString() =>
        $"{nameof(StoredIntakeCase)}({CaseStatus}, items: {ExpectedItems.Count}, "
        + $"answers: {AnswerVersions.Count}, documents: {Documents.Count}, "
        + $"submissions: {Submissions.Count})";
}

/// <summary>
/// A case and its opening audit events, committed in one transaction. The events
/// travel with the case so a created case without its audit trail is not a
/// representable state.
/// </summary>
public sealed record NewIntakeCase(
    StoredIntakeCase Case,
    IReadOnlyList<IntakeAuditEventRecord> AuditEvents)
{
    public IReadOnlyList<IntakeAuditEventRecord> AuditEvents { get; } =
        Array.AsReadOnly((AuditEvents ?? throw new ArgumentNullException(nameof(AuditEvents))).ToArray());

    public override string ToString() =>
        $"{nameof(NewIntakeCase)}(events: {AuditEvents.Count})";
}

/// <summary>
/// The trusted scope a bootstrap-link digest lookup returns. It carries scope,
/// never content, and cannot be used to enumerate: it is produced by a digest
/// lookup, not accepted from a caller.
/// </summary>
public sealed record IntakeLinkScope(
    [property: JsonIgnore] TrustedFirmId Firm,
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] IntakeTokenDigest LinkDigest,
    DateTimeOffset ExpiresAtUtc)
{
    public override string ToString() => nameof(IntakeLinkScope);
}

/// <summary>
/// The trusted scope a client-session digest lookup returns.
/// <para>
/// It carries the session digest as well as the firm and case, because every
/// scoped read and write revalidates that the same session is still live in the
/// same database operation. A lookup followed by an unconditional read would
/// leave a time-of-check/time-of-use window in which a revoked session still
/// worked.
/// </para>
/// </summary>
public sealed record IntakeClientScope(
    [property: JsonIgnore] TrustedFirmId Firm,
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] Guid SessionId,
    [property: JsonIgnore] IntakeTokenDigest SessionDigest,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset IdleExpiresAtUtc,
    DateTimeOffset EvaluationAtUtc)
{
    public override string ToString() => nameof(IntakeClientScope);
}

public sealed record NewIntakeSession(
    [property: JsonIgnore] Guid SessionId,
    [property: JsonIgnore] IntakeTokenDigest SessionDigest,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    DateTimeOffset IdleExpiresAtUtc)
{
    public override string ToString() => nameof(NewIntakeSession);
}

/// <summary>
/// What the content adapter returns after streaming one upload: an opaque key,
/// the byte count it actually saw, and the scoped keyed fingerprint. Never a
/// path, a file name, or the content.
/// </summary>
public sealed record StagedIntakeContent(
    [property: JsonIgnore] string ContentKey,
    long ByteCount,
    IReadOnlyList<byte> Fingerprint)
{
    // See IntakeDocumentRecord.Fingerprint: the attribute must sit on the
    // hand-declared property or it is silently discarded.
    [JsonIgnore]
    public IReadOnlyList<byte> Fingerprint { get; } =
        Array.AsReadOnly((Fingerprint ?? throw new ArgumentNullException(nameof(Fingerprint))).ToArray());

    public override string ToString() =>
        $"{nameof(StagedIntakeContent)}({ByteCount})";
}
