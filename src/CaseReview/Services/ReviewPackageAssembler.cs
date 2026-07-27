using CaseProcessing.Models;
using CaseReview.Models;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseReview.Services;

/// <summary>
/// The deterministic package-assembly boundary. Wave 0 declares the signature;
/// T04 implements the fail-closed pipeline.
/// </summary>
public static class ReviewPackageAssembler
{
    private const string ExtractionsRequired =
        "A processed extraction collection is required.";
    private const string ExtractionsEmpty =
        "At least one processed extraction is required.";
    private const string ExtractionRequired =
        "Every processed extraction is required.";
    private const string FirmMismatch =
        "All processed extractions must share one firm identifier.";
    private const string CaseMismatch =
        "All processed extractions must share one case identifier.";
    private const string ParticipantMismatch =
        "All processed extractions must share one participant identifier.";
    private const string TaxYearMismatch =
        "All processed extractions must share one tax year.";
    private const string TaxYearUnsupported =
        "Every tax year must be supported for review.";
    private const string DuplicateDocument =
        "Document identifiers must be unique within a review package.";

    public static ParseResult<AccountantReviewPackage> Assemble(
        IReadOnlyList<ProcessedT4Extraction?>? extractions)
    {
        if (extractions is null)
        {
            return Failure(ReviewFields.Extractions, ExtractionsRequired);
        }

        var materialized = extractions.ToArray();

        if (materialized.Length == 0)
        {
            return Failure(ReviewFields.Extractions, ExtractionsEmpty);
        }

        if (materialized.Any(extraction => extraction is null))
        {
            return Failure(ReviewFields.Extraction, ExtractionRequired);
        }

        var first = materialized[0]!;
        var firmMismatch = false;
        var caseMismatch = false;
        var participantMismatch = false;
        var taxYearMismatch = false;
        var unsupportedTaxYear = false;
        var duplicateDocument = false;
        var documentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var extraction in materialized)
        {
            firmMismatch |= !string.Equals(
                extraction!.FirmId,
                first.FirmId,
                StringComparison.Ordinal);
            caseMismatch |= !string.Equals(
                extraction.CaseId,
                first.CaseId,
                StringComparison.Ordinal);
            participantMismatch |= !string.Equals(
                extraction.ParticipantId,
                first.ParticipantId,
                StringComparison.Ordinal);
            taxYearMismatch |= extraction.TaxYear != first.TaxYear;
            unsupportedTaxYear |= !SupportedReviewTaxYears.IsSupported(extraction.TaxYear);
            duplicateDocument |= !documentIds.Add(extraction.DocumentId);
        }

        var errors = new List<ValidationError>();

        if (firmMismatch)
        {
            errors.Add(new ValidationError(ReviewFields.FirmId, FirmMismatch));
        }

        if (caseMismatch)
        {
            errors.Add(new ValidationError(ReviewFields.CaseId, CaseMismatch));
        }

        if (participantMismatch)
        {
            errors.Add(new ValidationError(ReviewFields.ParticipantId, ParticipantMismatch));
        }

        if (taxYearMismatch)
        {
            errors.Add(new ValidationError(ReviewFields.TaxYear, TaxYearMismatch));
        }

        if (unsupportedTaxYear)
        {
            errors.Add(new ValidationError(ReviewFields.TaxYear, TaxYearUnsupported));
        }

        if (duplicateDocument)
        {
            errors.Add(new ValidationError(ReviewFields.DocumentId, DuplicateDocument));
        }

        if (errors.Count > 0)
        {
            return ParseResult<AccountantReviewPackage>.Failure(errors);
        }

        var reviewedSlips = materialized
            .Select(BuildReviewedSlip)
            .ToArray();

        return ParseResult<AccountantReviewPackage>.Success(
            new AccountantReviewPackage(
                first.FirmId,
                first.CaseId,
                first.ParticipantId,
                first.TaxYear,
                reviewedSlips,
                signOff: null));
    }

    private static ReviewedSlip BuildReviewedSlip(ProcessedT4Extraction? extraction)
    {
        var processed = extraction!;
        var boxAmounts = T4SlipBoxAmounts
            .Enumerate(processed.Slip)
            .Select(amount => new ReviewedBoxAmount(amount.BoxCode, amount.Amount))
            .ToArray();

        return new ReviewedSlip(
            processed.DocumentId,
            processed.TaxYear,
            processed.SourceReference,
            processed.Slip,
            processed.Mapping.Lines,
            boxAmounts,
            processed.Mapping.UnmappedBoxCodes);
    }

    private static ParseResult<AccountantReviewPackage> Failure(
        string field,
        string message) =>
        ParseResult<AccountantReviewPackage>.Failure(
            new ValidationError(field, message));
}
