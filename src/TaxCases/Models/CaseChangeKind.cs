namespace TaxCases.Models;

public enum CaseChangeKind
{
    CaseCreated = 0,
    ParticipantAdded = 1,
    EvidenceAttached = 2,
    QuestionAnswered = 3,
    ChecklistConfirmed = 4,
    StatusChanged = 5,
    CaseReopened = 6
}
