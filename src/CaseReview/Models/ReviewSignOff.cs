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

    public bool Equals(ReviewSignOff? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
               Decision == other.Decision &&
               string.Equals(
                   ReviewerReference,
                   other.ReviewerReference,
                   StringComparison.Ordinal) &&
               string.Equals(
                   DecidedAtReference,
                   other.DecidedAtReference,
                   StringComparison.Ordinal) &&
               string.Equals(Note, other.Note, StringComparison.Ordinal) &&
               UnmappedBoxesAcknowledged == other.UnmappedBoxesAcknowledged;
    }

    public override bool Equals(object? obj) => Equals(obj as ReviewSignOff);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Decision);
        hash.Add(ReviewerReference, StringComparer.Ordinal);
        hash.Add(DecidedAtReference, StringComparer.Ordinal);
        hash.Add(Note, StringComparer.Ordinal);
        hash.Add(UnmappedBoxesAcknowledged);
        return hash.ToHashCode();
    }

    /// <summary>Structural presence facts only; no caller string is rendered.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ReviewSignOff(Decision: {0}, HasNote: {1}, HasAcknowledgement: {2})",
            Decision,
            Note is not null,
            UnmappedBoxesAcknowledged.HasValue);
}
