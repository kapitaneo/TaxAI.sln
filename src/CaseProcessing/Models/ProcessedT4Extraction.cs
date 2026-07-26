using System.Globalization;
using System.Text.Json.Serialization;
using TaxLogicEngine.Models;

namespace CaseProcessing.Models;

/// <summary>
/// The success value of T4 case processing: the typed slip, its CRA line
/// mapping, and the attribution the caller supplied, carried forward unchanged.
/// The constructor is <b>internal</b> so only the processor can create one — a
/// <see cref="ProcessedT4Extraction"/> existing <i>is</i> the proof the whole
/// fail-closed pipeline succeeded, mirroring <c>ValidatedExtraction</c>.
/// <para>
/// Attribution is echoed, never verified. This type is <b>not</b> proof that the
/// case belongs to the firm, the participant to the case, or that the caller was
/// authorized; those remain the future application workflow's job.
/// </para>
/// <para>
/// The raw <see cref="T4RawInput"/> is deliberately absent. Once parsed its role
/// is over, and retaining it would create a second, <i>unmasked</i> carrier of
/// the Box 12 SIN beside the parser's masked <see cref="RecipientIdentifier"/>.
/// Callers that still need it hold the <c>ValidatedExtraction</c>.
/// </para>
/// <para>
/// PII plumbing: every caller-controlled string, plus <see cref="Slip"/> and
/// <see cref="Mapping"/> (the engine's own types, which carry identity text and
/// amounts), are exposed programmatically only — readable in memory,
/// <see cref="JsonIgnoreAttribute"/> for supported direct JSON serialization,
/// and absent from <see cref="ToString"/>. A sealed class, not a record, so no
/// compiler-generated <c>ToString()</c> can print property values.
/// </para>
/// </summary>
public sealed class ProcessedT4Extraction
{
    internal ProcessedT4Extraction(
        string documentId,
        string firmId,
        string caseId,
        string participantId,
        int taxYear,
        string sourceReference,
        T4Slip slip,
        T4MappingResult mapping)
    {
        DocumentId = documentId;
        FirmId = firmId;
        CaseId = caseId;
        ParticipantId = participantId;
        TaxYear = taxYear;
        SourceReference = sourceReference;
        Slip = slip;
        Mapping = mapping;
    }

    /// <summary>The caller-supplied document identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string DocumentId { get; }

    /// <summary>The caller-supplied firm identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string FirmId { get; }

    /// <summary>The caller-supplied case identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string CaseId { get; }

    /// <summary>The caller-supplied case-local participant identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string ParticipantId { get; }

    /// <summary>
    /// The processed tax year — the one supported year the extraction declared and
    /// the parsed slip agreed with. The single serializable property: a four-digit
    /// year is a structural fact, not caller-controlled data.
    /// </summary>
    public int TaxYear { get; }

    /// <summary>The caller-supplied source reference, exactly as supplied.</summary>
    [JsonIgnore]
    public string SourceReference { get; }

    /// <summary>
    /// The typed slip exactly as <c>T4SlipParser</c> produced it. Box 12 appears
    /// only through its masked <see cref="RecipientIdentifier"/>.
    /// </summary>
    [JsonIgnore]
    public T4Slip Slip { get; }

    /// <summary>
    /// The CRA line entries exactly as <c>T4LineMapper</c> produced them, including
    /// <c>UnmappedBoxCodes</c> unchanged and unfiltered.
    /// </summary>
    [JsonIgnore]
    public T4MappingResult Mapping { get; }

    /// <summary>
    /// Fixed structural facts only — never a caller value, an identity string, an
    /// amount, or a SIN in any form, masked included.
    /// </summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ProcessedT4Extraction(TaxYear: {0}, LineEntries: {1}, UnmappedBoxes: {2}, HasSin: {3})",
            TaxYear,
            Mapping.Lines.Count,
            Mapping.UnmappedBoxCodes.Count,
            Slip.Sin is not null);
}
