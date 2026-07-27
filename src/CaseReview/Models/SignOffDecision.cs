namespace CaseReview.Models;

/// <summary>The closed set of decisions a reviewer may record.</summary>
public enum SignOffDecision
{
    Approved = 1,
    ChangesRequested = 2,
    Rejected = 3,
}
