using System.Globalization;
using System.Text.Json.Serialization;
using DocumentExtraction.Models;
using Tenancy.Services;

namespace ExtractionWorkflow.Models;

/// <summary>
/// Whether the receipt stated a currency, and which one if it did.
/// <para>
/// "Not stated" is carried explicitly rather than as a null that a later reader
/// could quietly treat as the local currency. Nothing here defaults, infers, or
/// converts: an amount whose currency the document never printed is an amount
/// whose currency is unknown, and it stays that way.
/// </para>
/// </summary>
public sealed class CurrencyDeclaration
{
    private CurrencyDeclaration(bool isKnown, string? codeAsPrinted)
    {
        IsKnown = isKnown;
        CodeAsPrinted = codeAsPrinted;
    }

    /// <summary>The declaration for a receipt that printed no currency.</summary>
    public static CurrencyDeclaration Unknown { get; } = new(false, null);

    public bool IsKnown { get; }

    /// <summary>The currency exactly as printed, or null when unknown.</summary>
    [JsonIgnore]
    public string? CodeAsPrinted { get; }

    /// <summary>
    /// Takes the currency the document printed. An absent or blank value yields
    /// <see cref="Unknown"/> — never a substituted code.
    /// </summary>
    public static CurrencyDeclaration FromPrintedCode(string? codeAsPrinted) =>
        string.IsNullOrWhiteSpace(codeAsPrinted)
            ? Unknown
            : new CurrencyDeclaration(true, codeAsPrinted);

    /// <summary>Fixed structural facts only — never the printed code.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "CurrencyDeclaration(IsKnown: {0})",
            IsKnown);
}

/// <summary>
/// One immutable extraction of one medical expense receipt.
/// <para>
/// This is <b>neutral evidence for a future medical-expense specification</b>,
/// not a tax result. It states what a receipt showed and nothing more. It makes
/// no claim — in any member, name, comment, or rendered string — that an
/// expense is eligible, deductible, claimable, reimbursed, unreimbursed,
/// assigned to anyone, or associated with a CRA line, and it performs no
/// aggregation, period selection, dependant determination, threshold, or
/// calculation. Those judgements depend on whom the expense concerns and on
/// claim logic spanning several receipts and the taxpayer's income, so a single
/// receipt cannot settle them and this feature does not try.
/// </para>
/// <para>
/// The content members are exactly the approved closed field set: the date and
/// the gross amount as printed, the currency or an explicit unknown, the
/// provider and patient display names, the receipt reference, the extraction
/// provenance, and the structural validation outcomes. There is no member able
/// to carry a diagnosis, a treatment narrative, a medication name, a detailed
/// service description, payment-card information, an insurance identifier, or a
/// health-card number. That is a closed list, not a minimum.
/// </para>
/// <para>
/// Medical evidence never enters the T4 review record. The T4-only review
/// contracts are unchanged and this model is deliberately separate from them.
/// </para>
/// </summary>
public sealed class MedicalEvidenceVersion
{
    private readonly FieldValidationOutcome[] _fieldOutcomes;

    /// <exception cref="ArgumentNullException">A required reference is absent.</exception>
    /// <exception cref="ArgumentException">An identifier, instant, collection entry, or enum value is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is below one.</exception>
    public MedicalEvidenceVersion(
        Guid medicalEvidenceVersionId,
        Guid documentId,
        int version,
        string? serviceDateAsPrinted,
        string? grossAmountAsPrinted,
        CurrencyDeclaration currency,
        string? providerDisplayName,
        string? patientDisplayName,
        string? receiptReference,
        ExtractionProvenance? provenance,
        IReadOnlyList<FieldValidationOutcome> fieldOutcomes,
        ExtractionAcceptanceState acceptanceState,
        Guid? precedingVersionId,
        DateTimeOffset recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(fieldOutcomes);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        if (medicalEvidenceVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The identifier is absent.", nameof(medicalEvidenceVersionId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("The identifier is absent.", nameof(documentId));
        }

        if (!Enum.IsDefined(acceptanceState))
        {
            throw new ArgumentException(
                "The acceptance state is not a defined value.", nameof(acceptanceState));
        }

        if (precedingVersionId is { } preceding &&
            (preceding == Guid.Empty || preceding == medicalEvidenceVersionId))
        {
            throw new ArgumentException(
                "The preceding version reference is not a distinct identifier.",
                nameof(precedingVersionId));
        }

        if (UtcInstantGuard.Validate(recordedAtUtc, nameof(recordedAtUtc)) is { } instantError)
        {
            throw new ArgumentException(instantError.Message, nameof(recordedAtUtc));
        }

        var outcomes = fieldOutcomes.ToArray();

        if (Array.Exists(outcomes, outcome => outcome is null))
        {
            throw new ArgumentException("A collection entry is absent.", nameof(fieldOutcomes));
        }

        _fieldOutcomes = outcomes;
        MedicalEvidenceVersionId = medicalEvidenceVersionId;
        DocumentId = documentId;
        Version = version;
        ServiceDateAsPrinted = serviceDateAsPrinted;
        GrossAmountAsPrinted = grossAmountAsPrinted;
        Currency = currency;
        ProviderDisplayName = providerDisplayName;
        PatientDisplayName = patientDisplayName;
        ReceiptReference = receiptReference;
        Provenance = provenance;
        AcceptanceState = acceptanceState;
        PrecedingVersionId = precedingVersionId;
        RecordedAtUtc = recordedAtUtc;
    }

    [JsonIgnore]
    public Guid MedicalEvidenceVersionId { get; }

    [JsonIgnore]
    public Guid DocumentId { get; }

    public int Version { get; }

    /// <summary>The service or payment date exactly as printed. Never normalized.</summary>
    [JsonIgnore]
    public string? ServiceDateAsPrinted { get; }

    /// <summary>
    /// The gross amount exactly as printed, including whatever symbol or
    /// separator the receipt used. It is a string because it is not a number
    /// this feature is entitled to interpret: no arithmetic, no rounding, and no
    /// currency conversion happens anywhere in this module.
    /// </summary>
    [JsonIgnore]
    public string? GrossAmountAsPrinted { get; }

    public CurrencyDeclaration Currency { get; }

    /// <summary>Sensitive display text. Accountant-facing surfaces only.</summary>
    [JsonIgnore]
    public string? ProviderDisplayName { get; }

    [JsonIgnore]
    public string? PatientDisplayName { get; }

    [JsonIgnore]
    public string? ReceiptReference { get; }

    /// <summary>The existing provenance contract, reused; no second one is declared.</summary>
    public ExtractionProvenance? Provenance { get; }

    /// <summary>
    /// Structural outcomes only. A missing date or amount is surfaced as
    /// missing; a missing optional field is surfaced as missing and is
    /// explicitly not an eligibility decision.
    /// </summary>
    public IReadOnlyList<FieldValidationOutcome> FieldOutcomes => _fieldOutcomes;

    public ExtractionAcceptanceState AcceptanceState { get; }

    [JsonIgnore]
    public Guid? PrecedingVersionId { get; }

    /// <summary>The instant supplied by the caller. Nothing here reads a clock.</summary>
    public DateTimeOffset RecordedAtUtc { get; }

    /// <summary>
    /// Fixed structural facts only — presence flags, counts, and closed enum
    /// values. Never a date, an amount, a provider name, or a patient name.
    /// </summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "MedicalEvidenceVersion(Version: {0}, HasServiceDate: {1}, HasGrossAmount: {2}, CurrencyKnown: {3}, HasProviderName: {4}, HasPatientName: {5}, HasReceiptReference: {6}, OutcomeCount: {7}, Acceptance: {8}, HasPreceding: {9})",
            Version,
            ServiceDateAsPrinted is not null,
            GrossAmountAsPrinted is not null,
            Currency.IsKnown,
            ProviderDisplayName is not null,
            PatientDisplayName is not null,
            ReceiptReference is not null,
            _fieldOutcomes.Length,
            AcceptanceState,
            PrecedingVersionId is not null);
}
