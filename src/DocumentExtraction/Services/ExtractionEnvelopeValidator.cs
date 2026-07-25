using DocumentExtraction.Models;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Services;

/// <summary>
/// The single deterministic, pure, fail-closed gate every
/// <see cref="ExtractionEnvelope"/> passes through. Responsibility: envelope
/// well-formedness and attribution <b>presence</b> — nothing else. It never
/// authenticates a caller, never queries Tax Case Core, and proves no
/// membership. It never parses, types, normalizes, redacts, converts, maps,
/// rounds, or calculates; on success the payload passes through
/// reference-identical and value-unchanged.
/// <para>
/// Error fields use <see cref="ExtractionFields"/> constants only; error
/// messages are fixed templates carrying no caller or payload value (PII by
/// omission).
/// </para>
/// </summary>
public static class ExtractionEnvelopeValidator
{
    // Fixed message templates. Deliberately constants with no interpolation:
    // a template that cannot take an argument cannot leak a caller or payload
    // value. The supported tax year is named only by SupportedExtractionTaxYears,
    // never spelled out here, so the year policy stays in exactly one place.
    private const string EnvelopeRequired = "An extraction envelope is required.";
    private const string DocumentIdRequired = "DocumentId is required and must be non-blank.";
    private const string FirmIdRequired = "FirmId is required and must be non-blank.";
    private const string CaseIdRequired = "CaseId is required and must be non-blank.";
    private const string ParticipantIdRequired = "ParticipantId is required and must be non-blank.";
    private const string SourceReferenceRequired = "SourceReference is required and must be non-blank.";
    private const string KindUnsupported = "Kind is required and must be a supported extraction document kind.";
    private const string TaxYearRequired = "TaxYear is required.";
    private const string TaxYearUnsupported = "TaxYear is not a supported extraction tax year.";
    private const string PayloadRequiredForT4 = "A T4Slip envelope requires a T4 payload.";
    private const string PayloadForbiddenForGenericEvidence = "A GenericEvidence envelope must not carry a T4 payload.";
    private const string PayloadYearRequired = "The T4 payload year is required.";
    private const string PayloadYearMismatch = "The T4 payload year must equal the envelope tax year.";
    private const string PayloadEmpty = "The T4 payload carries no boxes and no identity text.";
    private const string ConfidenceOutOfRange = "Confidence must be between 0 and 1 inclusive.";

    /// <summary>
    /// Validates an untrusted envelope. Fail-closed: failure carries at least
    /// one <see cref="ValidationError"/> and no value; success carries a
    /// <see cref="ValidatedExtraction"/> and no errors; nothing is mutated,
    /// coerced, or partially accepted. A null envelope fails with a single
    /// <see cref="ExtractionFields.Envelope"/> error — it never throws.
    /// </summary>
    public static ParseResult<ValidatedExtraction> Validate(ExtractionEnvelope? envelope)
    {
        // Rule 1 — null envelope: a single error, never an exception.
        if (envelope is null)
        {
            return ParseResult<ValidatedExtraction>.Failure(
                new ValidationError(ExtractionFields.Envelope, EnvelopeRequired));
        }

        // Errors accumulate in the architecture's fixed rule order, so the error
        // sequence for a given envelope is always identical (AC-28) and an empty
        // envelope reports one error per missing field rather than only the first
        // (AC-19).
        var errors = new List<ValidationError>();

        // Rule 2 — attribution presence. Null, empty, and whitespace-only are all
        // blank (AC-17); these checks are independent and all run in one pass.
        AddIfBlank(errors, envelope.DocumentId, ExtractionFields.DocumentId, DocumentIdRequired);
        AddIfBlank(errors, envelope.FirmId, ExtractionFields.FirmId, FirmIdRequired);
        AddIfBlank(errors, envelope.CaseId, ExtractionFields.CaseId, CaseIdRequired);
        AddIfBlank(errors, envelope.ParticipantId, ExtractionFields.ParticipantId, ParticipantIdRequired);

        // A null Provenance reports the SourceReference error: the reference is
        // required for every kind, so its container being absent is the same
        // failure as the reference being blank.
        AddIfBlank(
            errors,
            envelope.Provenance?.SourceReference,
            ExtractionFields.SourceReference,
            SourceReferenceRequired);

        // Rule 3 — kind. Absent or any undefined value (including a casted unknown
        // or a T5 stand-in) fails closed.
        var kind = envelope.Kind;
        var kindIsDefined = kind.HasValue && Enum.IsDefined(kind.Value);
        if (!kindIsDefined)
        {
            errors.Add(new ValidationError(ExtractionFields.Kind, KindUnsupported));
        }

        // Rule 4 — envelope tax year. Never coerced, no adjacent-year fallback.
        var taxYear = envelope.TaxYear;
        if (!taxYear.HasValue)
        {
            errors.Add(new ValidationError(ExtractionFields.TaxYear, TaxYearRequired));
        }
        else if (!SupportedExtractionTaxYears.IsSupported(taxYear.Value))
        {
            errors.Add(new ValidationError(ExtractionFields.TaxYear, TaxYearUnsupported));
        }

        var payload = envelope.T4Payload;

        // Rules 5 and 6 are only meaningful once the kind is known; an undefined
        // kind has already failed closed above, and guessing which coherence rule
        // it "meant" would be a coercion.
        if (kindIsDefined)
        {
            // Rule 5 — payload/kind coherence, both directions.
            if (kind!.Value == ExtractedDocumentKind.T4Slip && payload is null)
            {
                errors.Add(new ValidationError(ExtractionFields.Payload, PayloadRequiredForT4));
            }
            else if (kind.Value == ExtractedDocumentKind.GenericEvidence && payload is not null)
            {
                errors.Add(new ValidationError(ExtractionFields.Payload, PayloadForbiddenForGenericEvidence));
            }

            // Rule 6 — T4-only checks. Tax-year isolation and a mechanical
            // emptiness rule; no box code or box value is ever inspected.
            if (kind.Value == ExtractedDocumentKind.T4Slip && payload is not null)
            {
                if (!payload.Year.HasValue)
                {
                    errors.Add(new ValidationError(ExtractionFields.PayloadYear, PayloadYearRequired));
                }
                else if (taxYear.HasValue && payload.Year.Value != taxYear.Value)
                {
                    // Compared only when the envelope year is present: with no
                    // envelope year there is nothing to cohere with, and rule 4
                    // has already reported that.
                    errors.Add(new ValidationError(ExtractionFields.PayloadYear, PayloadYearMismatch));
                }

                if (IsEmptyExtraction(payload))
                {
                    errors.Add(new ValidationError(ExtractionFields.PayloadContent, PayloadEmpty));
                }
            }
        }

        // Rule 7 — confidence. 0m, 1m, and absent are all accepted; there is no
        // low-confidence threshold, so a low value never rejects.
        var confidence = envelope.Provenance?.Confidence;
        if (confidence.HasValue && (confidence.Value < 0m || confidence.Value > 1m))
        {
            errors.Add(new ValidationError(ExtractionFields.Confidence, ConfidenceOutOfRange));
        }

        if (errors.Count > 0)
        {
            return ParseResult<ValidatedExtraction>.Failure(errors);
        }

        // Rule 8 — success. References pass through exactly as supplied (opaque to
        // this contract, never normalized or trimmed) and the payload passes through
        // reference-identical, so duplicate box codes survive in order (AC-18).
        return ParseResult<ValidatedExtraction>.Success(
            new ValidatedExtraction(
                envelope.DocumentId!,
                kind!.Value,
                envelope.FirmId!,
                envelope.CaseId!,
                envelope.ParticipantId!,
                taxYear!.Value,
                envelope.Provenance!.SourceReference!,
                envelope.Provenance.ProviderLabel,
                confidence,
                payload));
    }

    private static void AddIfBlank(
        List<ValidationError> errors,
        string? value,
        string field,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError(field, message));
        }
    }

    /// <summary>
    /// The mechanical empty-extraction rule: no boxes at all <b>and</b> no identity
    /// text. Deliberately makes no judgement about box codes or box values — a
    /// payload carrying one box of garbage passes this gate and is the engine's
    /// problem downstream (AC-21).
    /// </summary>
    private static bool IsEmptyExtraction(T4RawInput payload) =>
        (payload.Boxes is null || payload.Boxes.Count == 0) &&
        string.IsNullOrWhiteSpace(payload.EmployerName) &&
        string.IsNullOrWhiteSpace(payload.EmployerAddress) &&
        string.IsNullOrWhiteSpace(payload.EmployeeName) &&
        string.IsNullOrWhiteSpace(payload.EmployeeAddress);
}
