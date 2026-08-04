using System.Globalization;
using System.Text.Json.Serialization;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Models;

/// <summary>
/// What one ingest run produced, as a closed vocabulary.
/// <para>
/// <see cref="NothingRecorded"/> is the zero value and is what an unrecognized
/// document yields: no payload, no envelope, no processed extraction, no
/// evidence version — an audit event and nothing else. A forgotten assignment
/// therefore reports that nothing was recorded rather than claiming a slip.
/// </para>
/// </summary>
public enum ExtractionRecordedKind
{
    NothingRecorded = 0,
    T4ExtractionRecorded = 1,
    MedicalEvidenceRecorded = 2
}

/// <summary>
/// One document's current state, projected from its append-only history.
/// <para>
/// Everything here is <b>derived</b>. There is no writable status, no mutable
/// acceptance flag, and no stored "latest" marker: a later version supersedes an
/// earlier one by existing, and this type computes that rather than reading a
/// column something could overwrite.
/// </para>
/// <para>
/// It carries counts, closed enum values, and structural outcomes. It carries no
/// box code, no box value, no amount, no date, no display name, no content key,
/// and no SIN in any form — not because a filter removes them, but because there
/// is no member able to hold one.
/// </para>
/// </summary>
public sealed class DocumentExtractionView
{
    private readonly FieldValidationOutcome[] _latestFieldOutcomes;

    /// <exception cref="ArgumentNullException">A required collection is absent.</exception>
    /// <exception cref="ArgumentException">The document identifier is absent or an entry is absent.</exception>
    public DocumentExtractionView(
        Guid documentId,
        DocumentClassification automatedClassification,
        DocumentClassification effectiveClassification,
        bool isClassificationOverridden,
        int extractionVersionCount,
        int medicalEvidenceVersionCount,
        int? latestVersion,
        IReadOnlyList<FieldValidationOutcome> latestFieldOutcomes,
        ExtractionAcceptanceState acceptanceState,
        int correctionCount,
        bool hasUnresolvedDuplicateCandidate)
    {
        ArgumentNullException.ThrowIfNull(latestFieldOutcomes);

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("The identifier is absent.", nameof(documentId));
        }

        var outcomes = latestFieldOutcomes.ToArray();

        if (Array.Exists(outcomes, outcome => outcome is null))
        {
            throw new ArgumentException(
                "A collection entry is absent.", nameof(latestFieldOutcomes));
        }

        _latestFieldOutcomes = outcomes;
        DocumentId = documentId;
        AutomatedClassification = automatedClassification;
        EffectiveClassification = effectiveClassification;
        IsClassificationOverridden = isClassificationOverridden;
        ExtractionVersionCount = extractionVersionCount;
        MedicalEvidenceVersionCount = medicalEvidenceVersionCount;
        LatestVersion = latestVersion;
        AcceptanceState = acceptanceState;
        CorrectionCount = correctionCount;
        HasUnresolvedDuplicateCandidate = hasUnresolvedDuplicateCandidate;
    }

    [JsonIgnore]
    public Guid DocumentId { get; }

    /// <summary>What the provider said, retained whatever a human said later.</summary>
    public DocumentClassification AutomatedClassification { get; }

    /// <summary>
    /// The value that selected the extraction path. Absent an override the
    /// automated classification controls, so the two are shown together and
    /// neither silently wins.
    /// </summary>
    public DocumentClassification EffectiveClassification { get; }

    public bool IsClassificationOverridden { get; }

    public int ExtractionVersionCount { get; }

    public int MedicalEvidenceVersionCount { get; }

    /// <summary>The highest version number recorded, or null when none is.</summary>
    public int? LatestVersion { get; }

    /// <summary>
    /// The structural outcomes of the latest version. Each names a field
    /// position and a kind and carries no message text: the explanation for a
    /// refused value is the engine's own error, surfaced verbatim.
    /// </summary>
    public IReadOnlyList<FieldValidationOutcome> LatestFieldOutcomes => _latestFieldOutcomes;

    /// <summary>
    /// Whether a human accepted this document. Projected from the append-only
    /// acceptance facts; no machine output arrives accepted.
    /// </summary>
    public ExtractionAcceptanceState AcceptanceState { get; }

    public int CorrectionCount { get; }

    /// <summary>
    /// Whether a probable duplicate is flagged and still awaiting an accountant
    /// decision. A flag is never a rejection: both documents remain present.
    /// </summary>
    public bool HasUnresolvedDuplicateCandidate { get; }

    /// <summary>Fixed structural facts only — counts, closed enum values, and flags.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "DocumentExtractionView(Automated: {0}, Effective: {1}, Overridden: {2}, Versions: {3}, MedicalVersions: {4}, Outcomes: {5}, Acceptance: {6}, Corrections: {7}, DuplicatePending: {8})",
            AutomatedClassification,
            EffectiveClassification,
            IsClassificationOverridden,
            ExtractionVersionCount,
            MedicalEvidenceVersionCount,
            _latestFieldOutcomes.Length,
            AcceptanceState,
            CorrectionCount,
            HasUnresolvedDuplicateCandidate);
}

/// <summary>
/// One case's extraction state, projected from its append-only history.
/// </summary>
public sealed class CaseExtractionView
{
    private readonly DocumentExtractionView[] _documents;

    /// <exception cref="ArgumentNullException"><paramref name="documents"/> is absent.</exception>
    /// <exception cref="ArgumentException">The case identifier is absent or an entry is absent.</exception>
    public CaseExtractionView(
        Guid caseId,
        IReadOnlyList<DocumentExtractionView> documents,
        bool isCaseAccepted)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (caseId == Guid.Empty)
        {
            throw new ArgumentException("The identifier is absent.", nameof(caseId));
        }

        var copy = documents.ToArray();

        if (Array.Exists(copy, document => document is null))
        {
            throw new ArgumentException("A collection entry is absent.", nameof(documents));
        }

        _documents = copy;
        CaseId = caseId;
        IsCaseAccepted = isCaseAccepted;
    }

    [JsonIgnore]
    public Guid CaseId { get; }

    public IReadOnlyList<DocumentExtractionView> Documents => _documents;

    /// <summary>
    /// Whether the one explicit case-level acceptance has happened. It is a
    /// separate act from accepting a document, and only it may lead to a review
    /// record.
    /// </summary>
    public bool IsCaseAccepted { get; }

    /// <summary>Fixed structural facts only.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "CaseExtractionView(Documents: {0}, CaseAccepted: {1})",
            _documents.Length,
            IsCaseAccepted);
}

/// <summary>
/// What one ingest run did, and what the engine said about it.
/// <para>
/// The engine's <see cref="ValidationError"/>s are carried <b>verbatim and in
/// order</b>. Nothing here translates, summarizes, re-words, filters, or
/// interleaves them with a message of this module's own: two descriptions of the
/// same tax rule are how the two start to disagree.
/// </para>
/// </summary>
public sealed class DocumentExtractionOutcome
{
    private readonly ValidationError[] _engineErrors;

    /// <exception cref="ArgumentNullException">A required reference is absent.</exception>
    /// <exception cref="ArgumentException">A collection entry is absent.</exception>
    public DocumentExtractionOutcome(
        ExtractionRecordedKind recorded,
        DocumentExtractionView document,
        IReadOnlyList<ValidationError> engineErrors)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(engineErrors);

        var errors = engineErrors.ToArray();

        if (Array.Exists(errors, error => error is null))
        {
            throw new ArgumentException("A collection entry is absent.", nameof(engineErrors));
        }

        _engineErrors = errors;
        Recorded = recorded;
        Document = document;
    }

    public ExtractionRecordedKind Recorded { get; }

    /// <summary>The document's current state after the run.</summary>
    public DocumentExtractionView Document { get; }

    /// <summary>
    /// The existing engine's errors, exactly as it produced them. Empty when the
    /// engine produced none.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ValidationError> EngineErrors => _engineErrors;

    public bool IsEngineAccepted => _engineErrors.Length == 0;

    /// <summary>Fixed structural facts only — never an engine message.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "DocumentExtractionOutcome(Recorded: {0}, EngineErrors: {1})",
            Recorded,
            _engineErrors.Length);
}
