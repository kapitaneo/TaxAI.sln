using TaxLogicEngine.Models;

namespace CaseReview.Models;

public sealed partial class AccountantReviewPackage
{
    private const string DecisionRequired =
        "A review decision is required.";
    private const string SignOffAlreadyRecorded =
        "A review sign-off has already been recorded.";
    private const string DecisionUndefined =
        "Decision must be a defined sign-off decision.";
    private const string ReviewerReferenceRequired =
        "A reviewer reference is required.";
    private const string DecidedAtReferenceRequired =
        "A decided-at reference is required.";
    private const string NoteRequired =
        "A note is required for this decision.";
    private const string UnmappedAcknowledgementRequired =
        "Approval requires an unmapped-box acknowledgement.";
    private const string UnmappedAcknowledgementNegative =
        "Approval requires an affirmative unmapped-box acknowledgement.";

    /// <summary>
    /// Records exactly one validated human decision on a replacement package.
    /// Validation is fail-closed and deterministic; no caller value is
    /// normalized, interpreted, or included in an error.
    /// </summary>
    public ParseResult<AccountantReviewPackage> RecordDecision(
        ReviewDecisionDescriptor? decision)
    {
        if (decision is null)
        {
            return Failure(ReviewFields.Decision, DecisionRequired);
        }

        if (SignOff is not null)
        {
            return Failure(ReviewFields.SignOff, SignOffAlreadyRecorded);
        }

        var errors = new List<ValidationError>();
        var decisionIsDefined = decision.Decision is
            SignOffDecision.Approved or
            SignOffDecision.ChangesRequested or
            SignOffDecision.Rejected;

        if (!decisionIsDefined)
        {
            errors.Add(
                new ValidationError(ReviewFields.Decision, DecisionUndefined));
        }

        if (string.IsNullOrWhiteSpace(decision.ReviewerReference))
        {
            errors.Add(
                new ValidationError(
                    ReviewFields.ReviewerReference,
                    ReviewerReferenceRequired));
        }

        if (string.IsNullOrWhiteSpace(decision.DecidedAtReference))
        {
            errors.Add(
                new ValidationError(
                    ReviewFields.DecidedAtReference,
                    DecidedAtReferenceRequired));
        }

        var noteIsRequired = decision.Decision is
            SignOffDecision.ChangesRequested or
            SignOffDecision.Rejected;

        if (noteIsRequired && string.IsNullOrWhiteSpace(decision.Note))
        {
            errors.Add(new ValidationError(ReviewFields.Note, NoteRequired));
        }

        var hasUnmappedBoxes = false;
        foreach (var reviewedSlip in ReviewedSlips)
        {
            hasUnmappedBoxes |= reviewedSlip.UnmappedBoxCodes.Count > 0;
        }

        if (decision.Decision == SignOffDecision.Approved && hasUnmappedBoxes)
        {
            if (decision.UnmappedBoxesAcknowledged is null)
            {
                errors.Add(
                    new ValidationError(
                        ReviewFields.UnmappedBoxAcknowledgement,
                        UnmappedAcknowledgementRequired));
            }
            else if (!decision.UnmappedBoxesAcknowledged.Value)
            {
                errors.Add(
                    new ValidationError(
                        ReviewFields.UnmappedBoxAcknowledgement,
                        UnmappedAcknowledgementNegative));
            }
        }

        if (errors.Count > 0)
        {
            return ParseResult<AccountantReviewPackage>.Failure(errors);
        }

        var signOff = new ReviewSignOff(
            decision.Decision,
            decision.ReviewerReference!,
            decision.DecidedAtReference!,
            decision.Note,
            decision.UnmappedBoxesAcknowledged);

        return ParseResult<AccountantReviewPackage>.Success(
            new AccountantReviewPackage(
                FirmId,
                CaseId,
                ParticipantId,
                TaxYear,
                ReviewedSlips,
                signOff));
    }

    private static ParseResult<AccountantReviewPackage> Failure(
        string field,
        string message) =>
        ParseResult<AccountantReviewPackage>.Failure(
            new ValidationError(field, message));
}
