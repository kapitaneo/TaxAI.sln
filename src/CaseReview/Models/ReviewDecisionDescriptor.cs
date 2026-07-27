using System.Globalization;
using System.Text.Json.Serialization;

namespace CaseReview.Models;

/// <summary>
/// Untrusted decision input. Validation belongs to
/// <see cref="AccountantReviewPackage.RecordDecision"/>, so this carrier retains
/// null, blank, undefined-enum, and nullable-acknowledgement values exactly.
/// </summary>
public sealed class ReviewDecisionDescriptor
{
    public ReviewDecisionDescriptor(
        SignOffDecision decision,
        string? reviewerReference,
        string? decidedAtReference,
        string? note,
        bool? unmappedBoxesAcknowledged)
    {
        Decision = decision;
        ReviewerReference = reviewerReference;
        DecidedAtReference = decidedAtReference;
        Note = note;
        UnmappedBoxesAcknowledged = unmappedBoxesAcknowledged;
    }

    public SignOffDecision Decision { get; }

    [JsonIgnore]
    public string? ReviewerReference { get; }

    [JsonIgnore]
    public string? DecidedAtReference { get; }

    [JsonIgnore]
    public string? Note { get; }

    public bool? UnmappedBoxesAcknowledged { get; }

    /// <summary>Structural presence facts only; no caller string is rendered.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ReviewDecisionDescriptor(HasReviewer: {0}, HasDecidedAt: {1}, HasNote: {2}, HasAcknowledgement: {3})",
            ReviewerReference is not null,
            DecidedAtReference is not null,
            Note is not null,
            UnmappedBoxesAcknowledged.HasValue);
}
