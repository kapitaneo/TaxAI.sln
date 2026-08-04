using System.Globalization;
using System.Text.Json.Serialization;

namespace ExtractionWorkflow.Models;

/// <summary>
/// What a document intelligence provider claims one document is.
/// <para>
/// Untrusted upstream metadata, so everything is nullable and loose — exactly
/// like <c>ExtractionEnvelope</c>. A classification is a machine's opinion about
/// bytes; it becomes a tax input only by surviving the existing fail-closed
/// chain, and never by being believed here.
/// </para>
/// </summary>
public sealed class ProviderClassificationResult
{
    /// <summary>The claimed classification; absent or undefined fails closed upstream.</summary>
    public DocumentClassification? Classification { get; init; }

    /// <summary>
    /// Optional provider confidence, transported unchanged. It is attention
    /// metadata for a human reader and nothing else: it gates nothing, accepts
    /// nothing, and is never a statement about how right the value is. Range
    /// checking belongs to the existing envelope validator.
    /// </summary>
    public decimal? Confidence { get; init; }

    /// <summary>The configured, server-owned label naming the adapter that ran.</summary>
    [JsonIgnore]
    public string? ProviderLabel { get; init; }

    /// <summary>Fixed structural facts only.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ProviderClassificationResult(Classification: {0}, HasConfidence: {1}, HasProviderLabel: {2})",
            Classification?.ToString() ?? "absent",
            Confidence is not null,
            ProviderLabel is not null);
}

/// <summary>
/// One box a provider claims to have read off a T4.
/// <para>
/// The code is an opaque string copied from the document. Nothing in this
/// module decides what a box code means: there is no box→line table, no
/// box→meaning table, and no per-box required, valid, or plausible rule. The
/// engine owns every one of those judgements.
/// </para>
/// <para>
/// The value may be a social insurance number when the code is Box 12, so both
/// members are omitted from serialization and from
/// <see cref="ToString"/>.
/// </para>
/// </summary>
public sealed class ProviderBoxValue
{
    [JsonIgnore]
    public string? Code { get; init; }

    [JsonIgnore]
    public string? Value { get; init; }

    /// <summary>Optional per-field provider confidence, transported unchanged.</summary>
    public decimal? Confidence { get; init; }

    /// <summary>Fixed structural facts only — never the code and never the value.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ProviderBoxValue(HasCode: {0}, HasValue: {1}, HasConfidence: {2})",
            Code is not null,
            Value is not null,
            Confidence is not null);
}

/// <summary>
/// What a provider claims to have read off a T4 slip.
/// <para>
/// Untrusted, loose, and nullable throughout. It mirrors the shape of the
/// engine's own raw input because that is what it is assembled into — in
/// memory, for one call, by <c>ExtractionPayloadBuilder</c> — and nothing here
/// is normalized, typed, defaulted, or repaired on the way. In particular the
/// box collection is a list rather than a dictionary, so a document that shows
/// the same box twice still reaches the engine's own duplicate-box rejection.
/// </para>
/// <para>
/// <see cref="Year"/> is what the document showed. The year an extraction is
/// attributed to comes from the server-stored intake case instead, and the
/// existing envelope validator refuses the pair when they disagree. Neither
/// value is coerced here.
/// </para>
/// </summary>
public sealed class ProviderT4Result
{
    /// <summary>The calendar year printed on the document, exactly as read.</summary>
    public int? Year { get; init; }

    [JsonIgnore]
    public string? EmployerName { get; init; }

    [JsonIgnore]
    public string? EmployerAddress { get; init; }

    [JsonIgnore]
    public string? EmployeeName { get; init; }

    [JsonIgnore]
    public string? EmployeeAddress { get; init; }

    public bool? ExemptCppQpp { get; init; }

    public bool? ExemptEi { get; init; }

    public bool? ExemptPpip { get; init; }

    /// <summary>
    /// The claimed boxes, in the order read. Entries may be absent and codes may
    /// be absent; both fail closed when the payload is assembled.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ProviderBoxValue?>? Boxes { get; init; }

    /// <summary>Optional document-level provider confidence, transported unchanged.</summary>
    public decimal? Confidence { get; init; }

    /// <summary>Fixed structural facts only — never a name, an address, or a box.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ProviderT4Result(HasYear: {0}, HasEmployerName: {1}, HasEmployeeName: {2}, BoxCount: {3}, HasConfidence: {4})",
            Year is not null,
            EmployerName is not null,
            EmployeeName is not null,
            Boxes?.Count ?? 0,
            Confidence is not null);
}

/// <summary>
/// What a provider claims to have read off a medical expense receipt.
/// <para>
/// The closed approved field set and nothing else. There is no member able to
/// carry a diagnosis, a treatment narrative, a medication name, a detailed
/// service description, payment-card information, an insurance identifier, or a
/// health-card number — a provider that returns such content has nowhere to put
/// it.
/// </para>
/// <para>
/// The date and the amount are transported <b>as printed</b>: strings, never
/// normalized to a canonical representation, never converted between
/// currencies, never rounded, and never arithmetic. This module asserts nothing
/// about whether the expense is eligible, deductible, claimable, reimbursed, or
/// assigned to anyone.
/// </para>
/// </summary>
public sealed class ProviderMedicalResult
{
    [JsonIgnore]
    public string? ServiceDateAsPrinted { get; init; }

    [JsonIgnore]
    public string? GrossAmountAsPrinted { get; init; }

    [JsonIgnore]
    public string? CurrencyCodeAsPrinted { get; init; }

    [JsonIgnore]
    public string? ProviderDisplayName { get; init; }

    [JsonIgnore]
    public string? PatientDisplayName { get; init; }

    [JsonIgnore]
    public string? ReceiptReference { get; init; }

    /// <summary>Optional provider confidence, transported unchanged.</summary>
    public decimal? Confidence { get; init; }

    /// <summary>Fixed structural facts only — never a date, an amount, or a name.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ProviderMedicalResult(HasServiceDate: {0}, HasGrossAmount: {1}, HasCurrency: {2}, HasProviderName: {3}, HasPatientName: {4}, HasReceiptReference: {5}, HasConfidence: {6})",
            ServiceDateAsPrinted is not null,
            GrossAmountAsPrinted is not null,
            CurrencyCodeAsPrinted is not null,
            ProviderDisplayName is not null,
            PatientDisplayName is not null,
            ReceiptReference is not null,
            Confidence is not null);
}
