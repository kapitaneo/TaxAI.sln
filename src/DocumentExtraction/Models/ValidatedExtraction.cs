using System.Globalization;
using System.Text.Json.Serialization;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Models;

/// <summary>
/// The success value of envelope validation. The constructor is <b>internal</b>
/// so only the validator can create one — a <see cref="ValidatedExtraction"/>
/// existing <i>is</i> the proof the envelope passed the fail-closed gate.
/// <para>
/// It proves envelope well-formedness and attribution <b>presence only</b> —
/// never that the case belongs to the firm, the participant to the case, or
/// that the caller is authorized; those are the future application workflow's
/// job. The payload stays untrusted: it is the same <see cref="T4RawInput"/>
/// instance the envelope carried, reference-identical and value-unchanged,
/// never a <c>T4Slip</c> or <c>TaxLineEntry</c>.
/// </para>
/// <para>
/// PII is handled by <b>omission</b>: the payload and every caller-controlled
/// string are exposed programmatically only — readable in memory,
/// <see cref="JsonIgnoreAttribute"/> for supported direct JSON serialization,
/// and absent from <see cref="ToString"/>. A sealed class, not a record, so no
/// compiler-generated <c>ToString()</c> can print property values.
/// </para>
/// </summary>
public sealed class ValidatedExtraction
{
    internal ValidatedExtraction(
        string documentId,
        ExtractedDocumentKind kind,
        string firmId,
        string caseId,
        string participantId,
        int taxYear,
        string sourceReference,
        string? providerLabel,
        decimal? confidence,
        T4RawInput? t4Payload)
    {
        DocumentId = documentId;
        Kind = kind;
        FirmId = firmId;
        CaseId = caseId;
        ParticipantId = participantId;
        TaxYear = taxYear;
        SourceReference = sourceReference;
        ProviderLabel = providerLabel;
        Confidence = confidence;
        T4Payload = t4Payload;
    }

    /// <summary>The caller-supplied document identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string DocumentId { get; }

    /// <summary>The validated document kind.</summary>
    public ExtractedDocumentKind Kind { get; }

    /// <summary>The caller-supplied firm identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string FirmId { get; }

    /// <summary>The caller-supplied case identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string CaseId { get; }

    /// <summary>The caller-supplied case-local participant identifier, exactly as supplied.</summary>
    [JsonIgnore]
    public string ParticipantId { get; }

    /// <summary>The validated tax year (exactly 2025 under spec 007's policy).</summary>
    public int TaxYear { get; }

    /// <summary>The caller-supplied source reference, exactly as supplied.</summary>
    [JsonIgnore]
    public string SourceReference { get; }

    /// <summary>The caller-supplied provider label, exactly as supplied, if any.</summary>
    [JsonIgnore]
    public string? ProviderLabel { get; }

    /// <summary>The provider confidence, if any; already range-checked by the validator.</summary>
    public decimal? Confidence { get; }

    /// <summary>
    /// The raw payload, non-null iff <see cref="Kind"/> is
    /// <see cref="ExtractedDocumentKind.T4Slip"/>. Reference-identical to the
    /// envelope's payload — never cloned, collapsed, or reordered — and still
    /// untrusted input for the downstream T4 Case Processor.
    /// </summary>
    [JsonIgnore]
    public T4RawInput? T4Payload { get; }

    /// <summary>Fixed structural facts only — never a caller or payload value.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ValidatedExtraction(Kind: {0}, TaxYear: {1}, HasPayload: {2}, BoxCount: {3}, Confidence: {4})",
            Kind,
            TaxYear,
            T4Payload is not null,
            T4Payload?.Boxes?.Count ?? 0,
            Confidence?.ToString(CultureInfo.InvariantCulture) ?? "absent");
}
