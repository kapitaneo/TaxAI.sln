using System.Text.Json.Serialization;
using TaxCases.Services;
using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    private TaxCase(
        string caseId,
        string firmId,
        int taxYear,
        CaseStatus status,
        IReadOnlyList<CaseParticipant> participants,
        IReadOnlyList<ReceivedEvidence> evidence,
        RequirementChecklist? checklist,
        IReadOnlyList<IntakeAnswer> answers)
    {
        CaseId = caseId;
        FirmId = firmId;
        TaxYear = taxYear;
        Status = status;
        Participants = Array.AsReadOnly(participants.ToArray());
        Evidence = Array.AsReadOnly(evidence.ToArray());
        Checklist = checklist;
        Answers = Array.AsReadOnly(answers.ToArray());
    }

    [JsonIgnore]
    public string CaseId { get; }

    [JsonIgnore]
    public string FirmId { get; }

    public int TaxYear { get; }

    public CaseStatus Status { get; }

    public IReadOnlyList<CaseParticipant> Participants { get; }

    public IReadOnlyList<ReceivedEvidence> Evidence { get; }

    public RequirementChecklist? Checklist { get; }

    public IReadOnlyList<IntakeAnswer> Answers { get; }

    public ReviewReadiness Readiness => ReadinessCalculator.Calculate(this);

    internal TaxCase With(
        CaseStatus? status = null,
        IReadOnlyList<CaseParticipant>? participants = null,
        IReadOnlyList<ReceivedEvidence>? evidence = null,
        RequirementChecklist? checklist = null,
        bool replaceChecklist = false,
        IReadOnlyList<IntakeAnswer>? answers = null) =>
        new(
            CaseId,
            FirmId,
            TaxYear,
            status ?? Status,
            participants ?? Participants,
            evidence ?? Evidence,
            replaceChecklist ? checklist : Checklist,
            answers ?? Answers);

    internal ValidationError? GuardMutable() =>
        Status is CaseStatus.Completed or CaseStatus.Cancelled
            ? new ValidationError(nameof(Status), $"A case in {Status} status cannot be changed.")
            : null;

    internal CaseChangeRecord BuildChange(
        CaseChangeKind kind,
        string? participantId = null,
        string? evidenceId = null,
        string? questionId = null,
        CaseStatus? previousStatus = null,
        CaseStatus? resultingStatus = null,
        string? correlationReference = null,
        string? detail = null) =>
        new(
            CaseId,
            kind,
            participantId,
            evidenceId,
            questionId,
            previousStatus,
            resultingStatus,
            correlationReference,
            detail);

    public override string ToString() =>
        $"TaxCase {{ CaseId = {SafeDiagnosticText.RedactSinLikeSequences(CaseId)}, " +
        $"FirmId = {SafeDiagnosticText.RedactSinLikeSequences(FirmId)}, TaxYear = {TaxYear}, " +
        $"Status = {Status}, ParticipantCount = {Participants.Count}, EvidenceCount = {Evidence.Count} }}";
}
