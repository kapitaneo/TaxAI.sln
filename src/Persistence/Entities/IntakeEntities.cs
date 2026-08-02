using ClientIntake.Models;
using TaxCases.Models;

namespace Persistence.Entities;

internal sealed class IntakeCaseEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid PrimaryParticipantId { get; set; }
    public string PrimaryTaxpayerDisplayName { get; set; } = string.Empty;
    public int TaxYear { get; set; }
    public CaseStatus CaseStatus { get; set; }
    public int QuestionnaireVersion { get; set; }
    public long ConcurrencyVersion { get; set; }
}

internal sealed class IntakeExpectedItemEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid ExpectedItemId { get; set; }
    public DocumentCategory Category { get; set; }
    public int Ordinal { get; set; }
}

internal sealed class IntakeLinkEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid LinkId { get; set; }
    public byte[] CredentialDigest { get; set; } = [];
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? SpentAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

internal sealed class IntakeClientSessionEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid SessionId { get; set; }
    public Guid LinkId { get; set; }
    public byte[] SessionDigest { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }
    public DateTimeOffset IdleExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

internal sealed class IntakeAnswerVersionEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public DocumentCategory Category { get; set; }
    public int Version { get; set; }
    public AvailabilityAnswerCode Answer { get; set; }
    public IntakeActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class IntakeDocumentEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ExpectedItemId { get; set; }
    public DocumentCategory Category { get; set; }
    public int Version { get; set; }
    public string ContentKey { get; set; } = string.Empty;
    public IntakeMediaKind MediaKind { get; set; }
    public long ByteCount { get; set; }
    public byte[] Fingerprint { get; set; } = [];
    public DateTimeOffset UploadedAtUtc { get; set; }
}

internal sealed class IntakeUploadAttemptEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid ExpectedItemId { get; set; }
    public IntakeUploadOutcome Outcome { get; set; }
    public DateTimeOffset AttemptedAtUtc { get; set; }
}

internal sealed class IntakeSubmissionEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public int Version { get; set; }
    public IntakeActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
}

internal sealed class IntakeSubmissionAnswerEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public int SubmissionVersion { get; set; }
    public DocumentCategory Category { get; set; }
    public int AnswerVersion { get; set; }
}

internal sealed class IntakeSubmissionDocumentEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public int SubmissionVersion { get; set; }
    public Guid DocumentId { get; set; }
}

internal sealed class IntakeReviewAssociationEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid RecordId { get; set; }
    public DateTimeOffset AssociatedAtUtc { get; set; }
}

internal sealed class IntakeAuditEventEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid EventId { get; set; }
    public long Sequence { get; set; }
    public IntakeAuditKind Kind { get; set; }
    public IntakeActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public Guid? ExpectedItemId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
