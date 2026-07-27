namespace CaseReview.Models;

/// <summary>
/// The closed set of fields this module may name in its own fixed validation
/// errors. No caller or taxpayer value is ever used as a field name.
/// </summary>
public static class ReviewFields
{
    public const string Extractions = "Extractions";
    public const string Extraction = "Extraction";
    public const string FirmId = "FirmId";
    public const string CaseId = "CaseId";
    public const string ParticipantId = "ParticipantId";
    public const string TaxYear = "TaxYear";
    public const string DocumentId = "DocumentId";
    public const string Decision = "Decision";
    public const string ReviewerReference = "ReviewerReference";
    public const string DecidedAtReference = "DecidedAtReference";
    public const string Note = "Note";
    public const string SignOff = "SignOff";
    public const string UnmappedBoxAcknowledgement = "UnmappedBoxAcknowledgement";
}
