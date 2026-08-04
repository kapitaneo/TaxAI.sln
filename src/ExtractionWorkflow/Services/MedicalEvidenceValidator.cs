using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Services;

/// <summary>
/// Structural validation of one medical expense receipt extraction.
/// <para>
/// <b>Structural, never tax-semantic.</b> It answers one question per approved
/// field — did the document show it? — and nothing else. It asserts nothing
/// about whether an expense is eligible, deductible, claimable, reimbursed,
/// unreimbursed, assigned to anyone, or associated with a CRA line; it performs
/// no aggregation, period selection, dependant determination, or calculation of
/// any kind. Those judgements depend on whom the expense concerns and on claim
/// logic spanning several receipts and the taxpayer's income, so a single
/// receipt cannot settle them and this feature does not try.
/// </para>
/// <para>
/// Nothing here invents, defaults, normalizes, converts, or infers a value. A
/// date the scan did not show is a missing date, and it stays missing.
/// </para>
/// <para>
/// The date and the amount are the two fields whose absence blocks acceptance of
/// that version: an evidence record with neither of them states nothing a later
/// medical-expense specification could use. Every other approved field is
/// surfaced as missing and is <b>explicitly not</b> a decision about the
/// expense — a receipt that omits the patient name is a receipt that omits the
/// patient name.
/// </para>
/// </summary>
public static class MedicalEvidenceValidator
{
    /// <summary>
    /// Produces one structural outcome per approved field, in a fixed order, so
    /// the same provider result always yields the same sequence.
    /// <para>
    /// An absent provider result yields an outcome set in which every field is
    /// missing rather than an exception: untrusted provider output never reaches
    /// a caller as a throw.
    /// </para>
    /// </summary>
    public static IReadOnlyList<FieldValidationOutcome> Validate(
        ProviderMedicalResult? providerResult)
    {
        var outcomes = new List<FieldValidationOutcome>(6);

        Add(outcomes, ExtractionWorkflowFields.MedicalServiceDate, providerResult?.ServiceDateAsPrinted);
        Add(outcomes, ExtractionWorkflowFields.MedicalGrossAmount, providerResult?.GrossAmountAsPrinted);
        Add(outcomes, ExtractionWorkflowFields.MedicalCurrency, providerResult?.CurrencyCodeAsPrinted);
        Add(outcomes, ExtractionWorkflowFields.MedicalProviderDisplayName, providerResult?.ProviderDisplayName);
        Add(outcomes, ExtractionWorkflowFields.MedicalPatientDisplayName, providerResult?.PatientDisplayName);
        Add(outcomes, ExtractionWorkflowFields.MedicalReceiptReference, providerResult?.ReceiptReference);

        return outcomes;
    }

    /// <summary>
    /// Whether these outcomes leave the version un-acceptable.
    /// <para>
    /// True when the service date or the gross amount is missing. The optional
    /// fields never appear here: their absence is surfaced, not judged.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="outcomes"/> is null.</exception>
    public static bool BlocksAcceptance(IReadOnlyList<FieldValidationOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        return IsMissing(outcomes, ExtractionWorkflowFields.MedicalServiceDate) ||
               IsMissing(outcomes, ExtractionWorkflowFields.MedicalGrossAmount);
    }

    private static void Add(
        List<FieldValidationOutcome> outcomes,
        string field,
        string? printedValue)
    {
        // Blank is absent. A receipt that printed only whitespace where a date
        // belongs showed no date, and recording it as extracted would be this
        // module deciding the scan was good enough.
        var kind = string.IsNullOrWhiteSpace(printedValue)
            ? FieldOutcomeKind.Missing
            : FieldOutcomeKind.Extracted;

        if (FieldValidationOutcome.TryCreate(field, kind, out var outcome))
        {
            outcomes.Add(outcome!);
        }
    }

    private static bool IsMissing(
        IReadOnlyList<FieldValidationOutcome> outcomes,
        string field)
    {
        foreach (var outcome in outcomes)
        {
            if (outcome is not null &&
                string.Equals(outcome.Field, field, StringComparison.Ordinal))
            {
                return outcome.Kind != FieldOutcomeKind.Extracted;
            }
        }

        // An outcome the caller never produced is not an outcome this method may
        // treat as satisfied.
        return true;
    }
}
