using TaxCases.Models;

namespace TaxCases.Services;

public static class CaseStatusTransitions
{
    public static bool IsAllowed(CaseStatus from, CaseStatus to) =>
        (from, to) switch
        {
            (CaseStatus.Intake, CaseStatus.InformationGathering) => true,
            (CaseStatus.Intake, CaseStatus.Cancelled) => true,
            (CaseStatus.InformationGathering, CaseStatus.InReview) => true,
            (CaseStatus.InformationGathering, CaseStatus.Cancelled) => true,
            (CaseStatus.InReview, CaseStatus.InformationGathering) => true,
            (CaseStatus.InReview, CaseStatus.Completed) => true,
            (CaseStatus.InReview, CaseStatus.Cancelled) => true,
            _ => false
        };
}
