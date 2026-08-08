using ExtractionWorkflow.Models;
using System.Text.Json.Serialization;
using TaxCases.Models;

namespace TaxAI.Portal.Components.Pages;

public sealed record ExtractionListPageModel(
    [property: JsonIgnore] Guid CaseId,
    int TaxYear,
    IReadOnlyList<ExtractionListItemModel> Documents,
    bool IsFinalized);

public sealed record ExtractionListItemModel(
    [property: JsonIgnore] Guid DocumentId,
    DocumentCategory DeclaredCategory,
    int DeclaredOrdinal,
    DocumentClassification AutomatedClassification,
    DocumentClassification EffectiveClassification,
    ExtractionAcceptanceState AcceptanceState,
    int ExtractionVersionCount,
    int MedicalEvidenceVersionCount,
    bool HasUnresolvedDuplicateCandidate);

public sealed record ExtractionReviewPageModel(
    DocumentCategory DeclaredCategory,
    int DeclaredOrdinal,
    DocumentClassification AutomatedClassification,
    DocumentClassification EffectiveClassification,
    bool IsClassificationOverridden,
    ExtractionAcceptanceState AcceptanceState,
    int? LatestVersion,
    int CorrectionCount,
    string? ProviderLabel,
    string? Confidence,
    IReadOnlyList<ExtractionFieldRowModel> FieldOutcomes,
    T4ExtractionPanelModel? T4,
    MedicalEvidencePanelModel? Medical,
    IReadOnlyList<DuplicateCandidateModel> DuplicateCandidates);

public sealed record ExtractionFieldRowModel(
    string Field,
    FieldOutcomeKind Outcome);

public sealed record T4ExtractionPanelModel(
    string? EmployerDisplayName,
    string? EmployeeDisplayName,
    string? EmployerAddress,
    string? EmployeeAddress,
    IReadOnlyList<T4BoxDisplayModel> Boxes);

public sealed record T4BoxDisplayModel(
    string Code,
    string? Value);

public sealed record MedicalEvidencePanelModel(
    string? ServiceDateAsPrinted,
    string? GrossAmountAsPrinted,
    string? CurrencyAsPrinted,
    string? ProviderDisplayName,
    string? PatientDisplayName,
    string? ReceiptReference);

public sealed record DuplicateCandidateModel(
    [property: JsonIgnore] Guid CandidateId);
