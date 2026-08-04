using ExtractionWorkflow.Models;

namespace Persistence.Entities;

/// <summary>
/// One immutable extraction version at rest.
/// <para>
/// There is no SIN column, no partial-SIN value, no SIN-presence flag, and no
/// address column: <c>ExtractionBoxValue</c> cannot represent the SIN box, so no
/// box row can carry one, and an address is only ever persisted as an opaque
/// correction value identified by the closed correction vocabulary.
/// </para>
/// <para>
/// There is also no source reference and no content key. The provenance a
/// version keeps is the server-owned provider label and the transported
/// confidence; the opaque key that reaches the encrypted source object stays in
/// the intake metadata it already belongs to, so no query, index, or diagnostic
/// here can reach content.
/// </para>
/// </summary>
internal sealed class ExtractionVersionEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public Guid ExtractionVersionId { get; set; }
    public Guid ParticipantId { get; set; }
    public DocumentClassification AutomatedClassification { get; set; }
    public DocumentClassification? OverriddenClassification { get; set; }
    public Guid? OverrideActorReference { get; set; }
    public DateTimeOffset? OverriddenAtUtc { get; set; }
    public string? EmployerDisplayName { get; set; }
    public string? EmployeeDisplayName { get; set; }
    public string? ProviderLabel { get; set; }
    public decimal? Confidence { get; set; }
    public ExtractionAcceptanceState AcceptanceState { get; set; }
    public Guid? PrecedingVersionId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// One non-SIN box code and its printed value, belonging to one version.
/// Ordinal preserves the order the document showed, duplicates included.
/// </summary>
internal sealed class ExtractionBoxValueEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public int Ordinal { get; set; }
    public string BoxCode { get; set; } = string.Empty;
    public string? PrintedValue { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// One per-field outcome. It carries a field key and a closed outcome kind and
/// deliberately no message text: the explanation for a refused value is the
/// engine's own error, and a second wording here would be a second rule.
/// </summary>
internal sealed class ExtractionFieldOutcomeEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public int Ordinal { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public FieldOutcomeKind OutcomeKind { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// One immutable medical evidence version: the approved closed field set and
/// nothing else. No column can carry a diagnosis, a treatment narrative, a
/// medication name, a detailed service description, payment-card information,
/// an insurance identifier, or a health-card number, and none asserts
/// eligibility, deductibility, or a CRA line.
/// </summary>
internal sealed class MedicalEvidenceVersionEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public Guid MedicalEvidenceVersionId { get; set; }
    public Guid ParticipantId { get; set; }
    public string? ServiceDateAsPrinted { get; set; }
    public string? GrossAmountAsPrinted { get; set; }
    public bool CurrencyIsKnown { get; set; }
    public string? CurrencyCodeAsPrinted { get; set; }
    public string? ProviderDisplayName { get; set; }
    public string? PatientDisplayName { get; set; }
    public string? ReceiptReference { get; set; }
    public string? ProviderLabel { get; set; }
    public decimal? Confidence { get; set; }
    public ExtractionAcceptanceState AcceptanceState { get; set; }
    public Guid? PrecedingVersionId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// One immutable accountant correction. The prior and corrected values are the
/// only place an address may be stored, as opaque text identified by the closed
/// field vocabulary. The vocabulary has no Box 12 member, so a SIN correction is
/// unrepresentable rather than filtered.
/// </summary>
internal sealed class ExtractionCorrectionEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid CorrectionId { get; set; }
    public Guid DocumentId { get; set; }
    public CorrectionField Field { get; set; }
    public string? BoxCode { get; set; }
    public string? PriorValue { get; set; }
    public string? CorrectedValue { get; set; }
    public ExtractionActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public DateTimeOffset SuppliedAtUtc { get; set; }
    public Guid PrecedingVersionId { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>One immutable classification override, retaining the original.</summary>
internal sealed class ExtractionClassificationOverrideEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid OverrideId { get; set; }
    public Guid DocumentId { get; set; }
    public DocumentClassification AutomatedClassification { get; set; }
    public DocumentClassification OverriddenClassification { get; set; }
    public ExtractionActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public DateTimeOffset OverriddenAtUtc { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// One probable-duplicate determination, as a relationship between two opaque
/// document identifiers. No taxpayer-data-derived fingerprint is stored, so
/// nothing here can be used to test whether two documents match.
/// </summary>
internal sealed class ExtractionDuplicateCandidateEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ComparedDocumentId { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// The accountant's decision about one candidate, stored beside the
/// determination rather than over it.
/// </summary>
internal sealed class ExtractionDuplicateResolutionEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid ResolutionId { get; set; }
    public Guid CandidateId { get; set; }
    public DuplicateResolutionKind Resolution { get; set; }
    public ExtractionActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public DateTimeOffset ResolvedAtUtc { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// One explicit human acceptance. A document acceptance names the exact version
/// accepted; a case acceptance names none.
/// </summary>
internal sealed class ExtractionAcceptanceEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid AcceptanceId { get; set; }
    public ExtractionAcceptanceScope Scope { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? AcceptedVersionId { get; set; }
    public ExtractionActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public DateTimeOffset AcceptedAtUtc { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}

/// <summary>
/// One structural audit event. Every column is a closed enum value, an opaque
/// identifier, a sequence, or an instant — there is no column able to carry an
/// amount, a date from a document, a box code, a CRA line, a provider or patient
/// name, a file name, a content key, a fingerprint, a prompt, or free text.
/// </summary>
internal sealed class ExtractionAuditEventEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid EventId { get; set; }
    public long Sequence { get; set; }
    public ExtractionAuditKind Kind { get; set; }
    public ExtractionActorKind ActorKind { get; set; }
    public Guid ActorReference { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset RetentionUntilUtc { get; set; }
}
