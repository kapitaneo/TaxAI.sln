using System.Globalization;
using System.Text.Json.Serialization;

namespace CaseReview.Models;

/// <summary>An accepted immutable human decision bound to one review package.</summary>
public sealed class ReviewSignOff : IEquatable<ReviewSignOff>
{
    internal ReviewSignOff(
        SignOffDecision decision,
        string reviewerReference,
        string decidedAtReference,
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
    public string ReviewerReference { get; }

    [JsonIgnore]
    public string DecidedAtReference { get; }

    [JsonIgnore]
    public string? Note { get; }

    public bool? UnmappedBoxesAcknowledged { get; }

    public bool Equals(ReviewSignOff? other) => throw new NotImplementedException();

    public override bool Equals(object? obj) => throw new NotImplementedException();

    public override int GetHashCode() => throw new NotImplementedException();

    /// <summary>Structural presence facts only; no caller string is rendered.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ReviewSignOff(Decision: {0}, HasNote: {1}, HasAcknowledgement: {2})",
            Decision,
            Note is not null,
            UnmappedBoxesAcknowledged.HasValue);
}
