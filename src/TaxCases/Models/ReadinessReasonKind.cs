namespace TaxCases.Models;

public enum ReadinessReasonKind
{
    ChecklistNotConfigured = 0,
    UnsatisfiedEvidenceRequirement = 1,
    UnansweredIntakeQuestion = 2,
    NoPrimaryTaxpayer = 3
}
