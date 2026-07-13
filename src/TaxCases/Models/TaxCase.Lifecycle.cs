using TaxCases.Services;
using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> TransitionTo(
        CaseStatus target,
        string? correlationReference)
    {
        if (!CaseStatusTransitions.IsAllowed(Status, target))
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError(
                    nameof(Status),
                    $"A transition from {Status} to {target} is not allowed."));
        }

        // Entering review additionally requires the derived readiness gate.
        if (target == CaseStatus.InReview && Readiness.State != ReadinessState.Ready)
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError(
                    nameof(Readiness),
                    "A case can enter review only when its derived readiness is Ready."));
        }

        var replacement = With(status: target);

        var change = BuildChange(
            CaseChangeKind.StatusChanged,
            previousStatus: Status,
            resultingStatus: target,
            correlationReference: correlationReference);

        return ParseResult<CaseOperationOutcome>.Success(new CaseOperationOutcome(replacement, change));
    }

    public ParseResult<CaseOperationOutcome> Reopen(
        string? reason,
        string? correlationReference)
    {
        var errors = new List<ValidationError>();

        if (Status != CaseStatus.Completed)
        {
            errors.Add(new ValidationError(
                nameof(Status),
                $"Only a {CaseStatus.Completed} case can be reopened; this case is {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            errors.Add(new ValidationError(
                "Reason",
                "A non-blank reopen reason is required."));
        }

        if (errors.Count > 0)
        {
            return ParseResult<CaseOperationOutcome>.Failure(errors);
        }

        var replacement = With(status: CaseStatus.InformationGathering);

        var change = BuildChange(
            CaseChangeKind.CaseReopened,
            previousStatus: Status,
            resultingStatus: CaseStatus.InformationGathering,
            correlationReference: correlationReference,
            detail: reason);

        return ParseResult<CaseOperationOutcome>.Success(new CaseOperationOutcome(replacement, change));
    }
}
