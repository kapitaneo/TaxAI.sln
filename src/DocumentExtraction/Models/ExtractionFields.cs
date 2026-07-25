namespace DocumentExtraction.Models;

/// <summary>
/// The fixed field names every <c>ValidationError.Field</c> produced by this
/// module must use. Declared once as constants so tests can assert the closed
/// set mechanically. Error messages are fixed templates that name these fields
/// and never echo a caller or payload value (PII by omission).
/// </summary>
public static class ExtractionFields
{
    public const string Envelope = "Envelope";
    public const string DocumentId = "DocumentId";
    public const string Kind = "Kind";
    public const string FirmId = "FirmId";
    public const string CaseId = "CaseId";
    public const string ParticipantId = "ParticipantId";
    public const string TaxYear = "TaxYear";
    public const string SourceReference = "SourceReference";
    public const string Confidence = "Confidence";
    public const string Payload = "Payload";
    public const string PayloadYear = "PayloadYear";
    public const string PayloadContent = "PayloadContent";
}
