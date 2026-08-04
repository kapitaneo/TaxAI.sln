using System.Globalization;
using System.Text.Json.Serialization;

namespace ExtractionWorkflow.Models;

/// <summary>
/// Supplies one correction from the closed <see cref="CorrectionField"/>
/// vocabulary. Scope that belongs to the server — firm, participant, tax year,
/// content key, CRA line, and validation result — has no member here.
/// </summary>
public sealed class ApplyExtractionCorrectionCommand
{
    [JsonIgnore]
    public Guid CaseId { get; init; }

    [JsonIgnore]
    public Guid DocumentId { get; init; }

    public CorrectionField Field { get; init; }

    /// <summary>
    /// The existing box code for a value/code/remove correction, or the new box
    /// code for an add. It is ignored for non-box fields.
    /// </summary>
    [JsonIgnore]
    public string? BoxCode { get; init; }

    /// <summary>
    /// The replacement value, or the new code for <see cref="CorrectionField.T4BoxCode"/>.
    /// A remove correction requires this to be absent.
    /// </summary>
    [JsonIgnore]
    public string? CorrectedValue { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ApplyExtractionCorrectionCommand(Field: {0}, HasBoxCode: {1}, HasCorrectedValue: {2})",
            Field,
            BoxCode is not null,
            CorrectedValue is not null);
}

/// <summary>
/// One explicit accountant classification decision. The automated
/// classification is read from immutable history, never accepted from here.
/// </summary>
public sealed class OverrideExtractionClassificationCommand
{
    [JsonIgnore]
    public Guid CaseId { get; init; }

    [JsonIgnore]
    public Guid DocumentId { get; init; }

    public DocumentClassification TargetClassification { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "OverrideExtractionClassificationCommand(Target: {0})",
            TargetClassification);
}

/// <summary>One explicit decision about one already-flagged duplicate candidate.</summary>
public sealed class ResolveExtractionDuplicateCommand
{
    [JsonIgnore]
    public Guid CaseId { get; init; }

    [JsonIgnore]
    public Guid CandidateId { get; init; }

    public DuplicateResolutionKind Resolution { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ResolveExtractionDuplicateCommand(Resolution: {0})",
            Resolution);
}

/// <summary>Accepts the exact latest version of one stored document.</summary>
public sealed class AcceptExtractionDocumentCommand
{
    [JsonIgnore]
    public Guid CaseId { get; init; }

    [JsonIgnore]
    public Guid DocumentId { get; init; }

    public override string ToString() => nameof(AcceptExtractionDocumentCommand);
}

/// <summary>
/// The only command that may create and associate a review record.
/// </summary>
public sealed class FinalizeExtractionCaseCommand
{
    [JsonIgnore]
    public Guid CaseId { get; init; }

    public override string ToString() => nameof(FinalizeExtractionCaseCommand);
}

/// <summary>The structural result of one successful finalization.</summary>
public sealed class FinalizedExtractionReview
{
    public FinalizedExtractionReview(
        Guid reviewRecordId,
        int reviewRecordVersion,
        int acceptedT4DocumentCount)
    {
        if (reviewRecordId == Guid.Empty)
        {
            throw new ArgumentException("The identifier is absent.", nameof(reviewRecordId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(reviewRecordVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(acceptedT4DocumentCount, 1);

        ReviewRecordId = reviewRecordId;
        ReviewRecordVersion = reviewRecordVersion;
        AcceptedT4DocumentCount = acceptedT4DocumentCount;
    }

    [JsonIgnore]
    public Guid ReviewRecordId { get; }

    public int ReviewRecordVersion { get; }

    public int AcceptedT4DocumentCount { get; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "FinalizedExtractionReview(Version: {0}, AcceptedT4Documents: {1})",
            ReviewRecordVersion,
            AcceptedT4DocumentCount);
}
