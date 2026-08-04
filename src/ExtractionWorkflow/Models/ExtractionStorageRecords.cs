using System.Globalization;
using System.Text.Json.Serialization;
using DocumentExtraction.Models;

namespace ExtractionWorkflow.Models;

/// <summary>
/// Rebuilds the provenance a stored version can honestly report.
/// <para>
/// It exists so the storage adapter never has to name a
/// <c>DocumentExtraction</c> type. That module is reachable from here and
/// deliberately not from infrastructure: the adapter maps this feature's own
/// records and nothing from the tax modules, and a boundary that depends on
/// nobody typing a namespace is not a boundary.
/// </para>
/// <para>
/// The opaque source reference is absent from the schema by design — the key
/// that reaches the encrypted source object stays in the intake metadata it
/// already belongs to — so it is absent here too, rather than reconstructed from
/// something that merely resembles it.
/// </para>
/// </summary>
public static class StoredExtractionProvenance
{
    /// <summary>
    /// Returns the provenance for a stored label and confidence, or null when a
    /// version recorded neither.
    /// </summary>
    public static ExtractionProvenance? FromStorage(
        string? providerLabel,
        decimal? confidence) =>
        providerLabel is null && confidence is null
            ? null
            : new ExtractionProvenance
            {
                ProviderLabel = providerLabel,
                Confidence = confidence,
            };
}

/// <summary>
/// The append-only audit vocabulary for extraction.
/// <para>
/// This is a <b>parallel</b> enum, deliberately not new members on
/// <c>ClientIntake.Models.IntakeAuditKind</c>. That enum is a merged public
/// contract of another module, and one shared audit vocabulary would be simpler
/// to query at the cost of changing a contract this feature is not authorized to
/// touch.
/// </para>
/// <para>
/// An audit event records that something structural happened. It never carries a
/// SIN, an amount, a date from a document, a box code, a CRA line, a provider or
/// patient name, a file name, a content key, a fingerprint, a prompt, a provider
/// payload, or free text.
/// </para>
/// </summary>
public enum ExtractionAuditKind
{
    ExtractionAttempted = 0,
    ExtractionRecorded = 1,
    ExtractionRefused = 2,
    ClassificationOverridden = 3,
    CorrectionRecorded = 4,
    DuplicateFlagged = 5,
    DuplicateResolved = 6,
    DocumentAccepted = 7,
    CaseAccepted = 8,
    MedicalEvidenceRecorded = 9,
    ReviewRecordCreated = 10
}

/// <summary>
/// Who acted. Extraction is an accountant-initiated operation in a single
/// authenticated firm-scoped request: no background worker, queue, timer, or
/// anonymous client path can reach it, so there is exactly one actor kind.
/// </summary>
public enum ExtractionActorKind
{
    Accountant = 0
}

/// <summary>
/// What an accountant decided about a flagged probable duplicate.
/// <para>
/// Both outcomes keep both documents. Nothing here rejects, discards, merges,
/// accepts, or excludes a document — a machine dropping a genuine second slip
/// loses income, and a machine merging two scans doubles it.
/// </para>
/// </summary>
public enum DuplicateResolutionKind
{
    DistinctDocuments = 0,
    ConfirmedDuplicate = 1
}

/// <summary>
/// Whether an acceptance covers one document or the whole case. They are
/// separate explicit actions: only the case-level one may create a review
/// record.
/// </summary>
public enum ExtractionAcceptanceScope
{
    Document = 0,
    Case = 1
}

/// <summary>
/// The store's closed outcome vocabulary. It carries no message, no identifier,
/// and no field name — a failure code a caller can read is a code that cannot
/// become an oracle.
/// </summary>
public enum ExtractionStoreWriteCode
{
    Success = 0,
    NotFound = 1,
    Conflict = 2
}

/// <summary>
/// One immutable structural audit event.
/// <para>
/// Everything on it is either a closed enum value, an opaque identifier, a
/// sequence, or an instant. There is no member able to carry taxpayer content,
/// so an audit row cannot leak one however it is written.
/// </para>
/// </summary>
public sealed class ExtractionAuditEventRecord
{
    /// <exception cref="ArgumentException">An identifier, sequence, or enum value is invalid.</exception>
    public ExtractionAuditEventRecord(
        Guid eventId,
        long sequence,
        ExtractionAuditKind kind,
        ExtractionActorKind actorKind,
        Guid actorReference,
        DateTimeOffset occurredAtUtc)
    {
        ExtractionRecordGuard.RequireIdentifier(eventId, nameof(eventId));
        ExtractionRecordGuard.RequireIdentifier(actorReference, nameof(actorReference));
        ExtractionRecordGuard.RequireDefined(kind, nameof(kind));
        ExtractionRecordGuard.RequireDefined(actorKind, nameof(actorKind));
        ExtractionRecordGuard.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);

        EventId = eventId;
        Sequence = sequence;
        Kind = kind;
        ActorKind = actorKind;
        ActorReference = actorReference;
        OccurredAtUtc = occurredAtUtc;
    }

    [JsonIgnore]
    public Guid EventId { get; }

    public long Sequence { get; }

    public ExtractionAuditKind Kind { get; }

    public ExtractionActorKind ActorKind { get; }

    /// <summary>An opaque reference to the actor. Never a name.</summary>
    [JsonIgnore]
    public Guid ActorReference { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionAuditEventRecord(Sequence: {0}, Kind: {1}, ActorKind: {2})",
            Sequence,
            Kind,
            ActorKind);
}

/// <summary>
/// One immutable accountant correction.
/// <para>
/// The field is drawn from the closed <see cref="CorrectionField"/> vocabulary,
/// which has no Box 12 member, so a SIN correction is unrepresentable rather
/// than refused. The prior and corrected values are the only place an address
/// may be persisted at all — as an opaque value identified by the field
/// vocabulary, never as a dedicated address column and never in an audit row.
/// </para>
/// </summary>
public sealed class ExtractionCorrectionRecord
{
    /// <exception cref="ArgumentException">An identifier, enum value, instant, or box code is invalid.</exception>
    public ExtractionCorrectionRecord(
        Guid correctionId,
        Guid documentId,
        CorrectionField field,
        string? boxCode,
        string? priorValue,
        string? correctedValue,
        ExtractionActorKind actorKind,
        Guid actorReference,
        DateTimeOffset suppliedAtUtc,
        Guid precedingVersionId)
    {
        ExtractionRecordGuard.RequireIdentifier(correctionId, nameof(correctionId));
        ExtractionRecordGuard.RequireIdentifier(documentId, nameof(documentId));
        ExtractionRecordGuard.RequireIdentifier(precedingVersionId, nameof(precedingVersionId));
        ExtractionRecordGuard.RequireIdentifier(actorReference, nameof(actorReference));
        ExtractionRecordGuard.RequireDefined(field, nameof(field));
        ExtractionRecordGuard.RequireDefined(actorKind, nameof(actorKind));
        ExtractionRecordGuard.RequireUtc(suppliedAtUtc, nameof(suppliedAtUtc));

        // The one box code a correction may never name. The check lives here as
        // well as in the vocabulary because a box-code correction carries the
        // code as data, and data is where a filter gets forgotten.
        if (ExtractionBoxValue.IsSocialInsuranceNumberBox(boxCode))
        {
            throw new ArgumentException(
                "The correction names the excluded box.", nameof(boxCode));
        }

        CorrectionId = correctionId;
        DocumentId = documentId;
        Field = field;
        BoxCode = boxCode;
        PriorValue = priorValue;
        CorrectedValue = correctedValue;
        ActorKind = actorKind;
        ActorReference = actorReference;
        SuppliedAtUtc = suppliedAtUtc;
        PrecedingVersionId = precedingVersionId;
    }

    [JsonIgnore]
    public Guid CorrectionId { get; }

    [JsonIgnore]
    public Guid DocumentId { get; }

    public CorrectionField Field { get; }

    /// <summary>The box the correction names, when the field is a box field.</summary>
    [JsonIgnore]
    public string? BoxCode { get; }

    [JsonIgnore]
    public string? PriorValue { get; }

    [JsonIgnore]
    public string? CorrectedValue { get; }

    public ExtractionActorKind ActorKind { get; }

    [JsonIgnore]
    public Guid ActorReference { get; }

    /// <summary>The instant the caller supplied. Nothing here reads a clock.</summary>
    public DateTimeOffset SuppliedAtUtc { get; }

    [JsonIgnore]
    public Guid PrecedingVersionId { get; }

    /// <summary>Fixed structural facts only — never a value, never a box code.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionCorrectionRecord(Field: {0}, ActorKind: {1}, HasPriorValue: {2}, HasCorrectedValue: {3})",
            Field,
            ActorKind,
            PriorValue is not null,
            CorrectedValue is not null);
}

/// <summary>
/// One immutable classification override. The automated value is retained
/// beside it, so the two can be shown together and neither silently wins.
/// </summary>
public sealed class ClassificationOverrideRecord
{
    /// <exception cref="ArgumentException">An identifier, enum value, or instant is invalid.</exception>
    public ClassificationOverrideRecord(
        Guid overrideId,
        Guid documentId,
        DocumentClassification automatedClassification,
        DocumentClassification overriddenClassification,
        ExtractionActorKind actorKind,
        Guid actorReference,
        DateTimeOffset overriddenAtUtc)
    {
        ExtractionRecordGuard.RequireIdentifier(overrideId, nameof(overrideId));
        ExtractionRecordGuard.RequireIdentifier(documentId, nameof(documentId));
        ExtractionRecordGuard.RequireIdentifier(actorReference, nameof(actorReference));
        ExtractionRecordGuard.RequireDefined(automatedClassification, nameof(automatedClassification));
        ExtractionRecordGuard.RequireDefined(overriddenClassification, nameof(overriddenClassification));
        ExtractionRecordGuard.RequireDefined(actorKind, nameof(actorKind));
        ExtractionRecordGuard.RequireUtc(overriddenAtUtc, nameof(overriddenAtUtc));

        // Only the two supported paths. An override can never make an
        // unrecognized document into unrecognized tax input.
        if (overriddenClassification == DocumentClassification.Unsupported)
        {
            throw new ArgumentException(
                "An override names one of the two supported paths.",
                nameof(overriddenClassification));
        }

        OverrideId = overrideId;
        DocumentId = documentId;
        AutomatedClassification = automatedClassification;
        OverriddenClassification = overriddenClassification;
        ActorKind = actorKind;
        ActorReference = actorReference;
        OverriddenAtUtc = overriddenAtUtc;
    }

    [JsonIgnore]
    public Guid OverrideId { get; }

    [JsonIgnore]
    public Guid DocumentId { get; }

    public DocumentClassification AutomatedClassification { get; }

    public DocumentClassification OverriddenClassification { get; }

    public ExtractionActorKind ActorKind { get; }

    [JsonIgnore]
    public Guid ActorReference { get; }

    public DateTimeOffset OverriddenAtUtc { get; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ClassificationOverrideRecord(Automated: {0}, Overridden: {1}, ActorKind: {2})",
            AutomatedClassification,
            OverriddenClassification,
            ActorKind);
}

/// <summary>
/// One probable-duplicate determination, as a relationship between two opaque
/// document identifiers.
/// <para>
/// It stores <b>no taxpayer-data-derived fingerprint</b>. The comparison inputs
/// that produced it are not persisted, so nothing here can be used to test
/// whether two taxpayers' documents match.
/// </para>
/// </summary>
public sealed class DuplicateCandidateRecord
{
    /// <exception cref="ArgumentException">An identifier or instant is invalid, or the pair is not distinct.</exception>
    public DuplicateCandidateRecord(
        Guid candidateId,
        Guid documentId,
        Guid comparedDocumentId,
        DateTimeOffset detectedAtUtc)
    {
        ExtractionRecordGuard.RequireIdentifier(candidateId, nameof(candidateId));
        ExtractionRecordGuard.RequireIdentifier(documentId, nameof(documentId));
        ExtractionRecordGuard.RequireIdentifier(comparedDocumentId, nameof(comparedDocumentId));
        ExtractionRecordGuard.RequireUtc(detectedAtUtc, nameof(detectedAtUtc));

        if (documentId == comparedDocumentId)
        {
            throw new ArgumentException(
                "A document is not a duplicate candidate of itself.",
                nameof(comparedDocumentId));
        }

        CandidateId = candidateId;
        DocumentId = documentId;
        ComparedDocumentId = comparedDocumentId;
        DetectedAtUtc = detectedAtUtc;
    }

    [JsonIgnore]
    public Guid CandidateId { get; }

    [JsonIgnore]
    public Guid DocumentId { get; }

    [JsonIgnore]
    public Guid ComparedDocumentId { get; }

    public DateTimeOffset DetectedAtUtc { get; }

    public override string ToString() => nameof(DuplicateCandidateRecord);
}

/// <summary>
/// The accountant's immutable decision about a flagged candidate. It leaves the
/// determination row untouched: the flag and the decision are separate facts.
/// </summary>
public sealed class DuplicateResolutionRecord
{
    /// <exception cref="ArgumentException">An identifier, enum value, or instant is invalid.</exception>
    public DuplicateResolutionRecord(
        Guid resolutionId,
        Guid candidateId,
        DuplicateResolutionKind resolution,
        ExtractionActorKind actorKind,
        Guid actorReference,
        DateTimeOffset resolvedAtUtc)
    {
        ExtractionRecordGuard.RequireIdentifier(resolutionId, nameof(resolutionId));
        ExtractionRecordGuard.RequireIdentifier(candidateId, nameof(candidateId));
        ExtractionRecordGuard.RequireIdentifier(actorReference, nameof(actorReference));
        ExtractionRecordGuard.RequireDefined(resolution, nameof(resolution));
        ExtractionRecordGuard.RequireDefined(actorKind, nameof(actorKind));
        ExtractionRecordGuard.RequireUtc(resolvedAtUtc, nameof(resolvedAtUtc));

        ResolutionId = resolutionId;
        CandidateId = candidateId;
        Resolution = resolution;
        ActorKind = actorKind;
        ActorReference = actorReference;
        ResolvedAtUtc = resolvedAtUtc;
    }

    [JsonIgnore]
    public Guid ResolutionId { get; }

    [JsonIgnore]
    public Guid CandidateId { get; }

    public DuplicateResolutionKind Resolution { get; }

    public ExtractionActorKind ActorKind { get; }

    [JsonIgnore]
    public Guid ActorReference { get; }

    public DateTimeOffset ResolvedAtUtc { get; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "DuplicateResolutionRecord(Resolution: {0}, ActorKind: {1})",
            Resolution,
            ActorKind);
}

/// <summary>
/// One explicit human acceptance, of one document or of the whole case.
/// <para>
/// A document acceptance names the exact version accepted, so "the latest
/// accepted version" is a fact rather than an inference. A case acceptance names
/// no document: it is the separate, explicit act that a review record depends
/// on.
/// </para>
/// </summary>
public sealed class ExtractionAcceptanceRecord
{
    /// <exception cref="ArgumentException">An identifier, enum value, instant, or scope combination is invalid.</exception>
    public ExtractionAcceptanceRecord(
        Guid acceptanceId,
        ExtractionAcceptanceScope scope,
        Guid? documentId,
        Guid? acceptedVersionId,
        ExtractionActorKind actorKind,
        Guid actorReference,
        DateTimeOffset acceptedAtUtc)
    {
        ExtractionRecordGuard.RequireIdentifier(acceptanceId, nameof(acceptanceId));
        ExtractionRecordGuard.RequireIdentifier(actorReference, nameof(actorReference));
        ExtractionRecordGuard.RequireDefined(scope, nameof(scope));
        ExtractionRecordGuard.RequireDefined(actorKind, nameof(actorKind));
        ExtractionRecordGuard.RequireUtc(acceptedAtUtc, nameof(acceptedAtUtc));

        if (scope == ExtractionAcceptanceScope.Document)
        {
            ExtractionRecordGuard.RequireIdentifier(
                documentId ?? Guid.Empty, nameof(documentId));
            ExtractionRecordGuard.RequireIdentifier(
                acceptedVersionId ?? Guid.Empty, nameof(acceptedVersionId));
        }
        else if (documentId is not null || acceptedVersionId is not null)
        {
            throw new ArgumentException(
                "A case acceptance names no document or version.", nameof(scope));
        }

        AcceptanceId = acceptanceId;
        Scope = scope;
        DocumentId = documentId;
        AcceptedVersionId = acceptedVersionId;
        ActorKind = actorKind;
        ActorReference = actorReference;
        AcceptedAtUtc = acceptedAtUtc;
    }

    [JsonIgnore]
    public Guid AcceptanceId { get; }

    public ExtractionAcceptanceScope Scope { get; }

    [JsonIgnore]
    public Guid? DocumentId { get; }

    [JsonIgnore]
    public Guid? AcceptedVersionId { get; }

    public ExtractionActorKind ActorKind { get; }

    [JsonIgnore]
    public Guid ActorReference { get; }

    public DateTimeOffset AcceptedAtUtc { get; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionAcceptanceRecord(Scope: {0}, ActorKind: {1})",
            Scope,
            ActorKind);
}

/// <summary>
/// One document's complete immutable extraction history, as stored.
/// <para>
/// Every collection is the append-only record, in order. Current state — the
/// effective version, whether it is accepted, whether a duplicate is
/// outstanding — is <b>projected</b> from these by the workflow, never stored as
/// a mutable status anyone could write to.
/// </para>
/// </summary>
public sealed class StoredDocumentExtraction
{
    /// <exception cref="ArgumentNullException">A collection is absent.</exception>
    /// <exception cref="ArgumentException">The document identifier is absent.</exception>
    public StoredDocumentExtraction(
        Guid documentId,
        Guid participantId,
        IReadOnlyList<ExtractionVersion> versions,
        IReadOnlyList<MedicalEvidenceVersion> medicalEvidenceVersions,
        IReadOnlyList<ExtractionCorrectionRecord> corrections,
        IReadOnlyList<ClassificationOverrideRecord> classificationOverrides,
        IReadOnlyList<DuplicateCandidateRecord> duplicateCandidates,
        IReadOnlyList<DuplicateResolutionRecord> duplicateResolutions,
        IReadOnlyList<ExtractionAcceptanceRecord> acceptances)
    {
        ExtractionRecordGuard.RequireIdentifier(documentId, nameof(documentId));

        DocumentId = documentId;
        ParticipantId = participantId;
        Versions = ExtractionRecordGuard.Snapshot(versions, nameof(versions));
        MedicalEvidenceVersions = ExtractionRecordGuard.Snapshot(
            medicalEvidenceVersions, nameof(medicalEvidenceVersions));
        Corrections = ExtractionRecordGuard.Snapshot(corrections, nameof(corrections));
        ClassificationOverrides = ExtractionRecordGuard.Snapshot(
            classificationOverrides, nameof(classificationOverrides));
        DuplicateCandidates = ExtractionRecordGuard.Snapshot(
            duplicateCandidates, nameof(duplicateCandidates));
        DuplicateResolutions = ExtractionRecordGuard.Snapshot(
            duplicateResolutions, nameof(duplicateResolutions));
        Acceptances = ExtractionRecordGuard.Snapshot(acceptances, nameof(acceptances));
    }

    [JsonIgnore]
    public Guid DocumentId { get; }

    [JsonIgnore]
    public Guid ParticipantId { get; }

    public IReadOnlyList<ExtractionVersion> Versions { get; }

    public IReadOnlyList<MedicalEvidenceVersion> MedicalEvidenceVersions { get; }

    public IReadOnlyList<ExtractionCorrectionRecord> Corrections { get; }

    public IReadOnlyList<ClassificationOverrideRecord> ClassificationOverrides { get; }

    public IReadOnlyList<DuplicateCandidateRecord> DuplicateCandidates { get; }

    public IReadOnlyList<DuplicateResolutionRecord> DuplicateResolutions { get; }

    public IReadOnlyList<ExtractionAcceptanceRecord> Acceptances { get; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "StoredDocumentExtraction(Versions: {0}, MedicalVersions: {1}, Corrections: {2}, Overrides: {3}, Candidates: {4}, Resolutions: {5}, Acceptances: {6})",
            Versions.Count,
            MedicalEvidenceVersions.Count,
            Corrections.Count,
            ClassificationOverrides.Count,
            DuplicateCandidates.Count,
            DuplicateResolutions.Count,
            Acceptances.Count);
}

/// <summary>
/// One case's extraction history: every document's, plus the case-level
/// acceptances and the structural audit trail.
/// </summary>
public sealed class StoredCaseExtractions
{
    /// <exception cref="ArgumentNullException">A collection is absent.</exception>
    /// <exception cref="ArgumentException">The case identifier is absent.</exception>
    public StoredCaseExtractions(
        Guid caseId,
        IReadOnlyList<StoredDocumentExtraction> documents,
        IReadOnlyList<ExtractionAcceptanceRecord> caseAcceptances,
        IReadOnlyList<ExtractionAuditEventRecord> auditEvents)
    {
        ExtractionRecordGuard.RequireIdentifier(caseId, nameof(caseId));

        CaseId = caseId;
        Documents = ExtractionRecordGuard.Snapshot(documents, nameof(documents));
        CaseAcceptances = ExtractionRecordGuard.Snapshot(
            caseAcceptances, nameof(caseAcceptances));
        AuditEvents = ExtractionRecordGuard.Snapshot(auditEvents, nameof(auditEvents));
    }

    [JsonIgnore]
    public Guid CaseId { get; }

    public IReadOnlyList<StoredDocumentExtraction> Documents { get; }

    public IReadOnlyList<ExtractionAcceptanceRecord> CaseAcceptances { get; }

    public IReadOnlyList<ExtractionAuditEventRecord> AuditEvents { get; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "StoredCaseExtractions(Documents: {0}, CaseAcceptances: {1}, AuditEvents: {2})",
            Documents.Count,
            CaseAcceptances.Count,
            AuditEvents.Count);
}

/// <summary>
/// The invariants every storage record shares, written once.
/// <para>
/// Repeating "the identifier must not be empty" at nine call sites is how the
/// tenth one ends up missing it.
/// </para>
/// </summary>
internal static class ExtractionRecordGuard
{
    internal static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier is absent.", parameterName);
        }
    }

    internal static void RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException(
                "The value is not a defined member of its vocabulary.", parameterName);
        }
    }

    /// <summary>
    /// The one UTC rule already in the repository, reused rather than restated.
    /// </summary>
    internal static void RequireUtc(DateTimeOffset instant, string parameterName)
    {
        if (Tenancy.Services.UtcInstantGuard.Validate(instant, parameterName) is { } error)
        {
            throw new ArgumentException(error.Message, parameterName);
        }
    }

    /// <summary>
    /// Copies into a read-only snapshot, so a caller holding the original list
    /// cannot change what was validated after validation.
    /// </summary>
    internal static IReadOnlyList<TEntry> Snapshot<TEntry>(
        IReadOnlyList<TEntry> entries,
        string parameterName)
        where TEntry : class
    {
        ArgumentNullException.ThrowIfNull(entries, parameterName);
        var copy = entries.ToArray();

        if (Array.Exists(copy, entry => entry is null))
        {
            throw new ArgumentException("A collection entry is absent.", parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}
