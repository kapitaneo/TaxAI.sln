using System.Globalization;
using TaxCases.Services;
using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public static ParseResult<CaseOperationOutcome> Create(CreateTaxCaseRequest request)
    {
        if (request is null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError("Request", "A create request is required."));
        }

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(request.CaseId))
        {
            errors.Add(new ValidationError(
                nameof(CreateTaxCaseRequest.CaseId),
                "A non-empty case identifier is required."));
        }

        if (string.IsNullOrWhiteSpace(request.FirmId))
        {
            errors.Add(new ValidationError(
                nameof(CreateTaxCaseRequest.FirmId),
                "A non-empty firm identifier is required."));
        }

        if (request.TaxYear is null)
        {
            errors.Add(new ValidationError(
                nameof(CreateTaxCaseRequest.TaxYear),
                "An explicit tax year is required; a case is never assigned a default year."));
        }
        else if (!SupportedTaxYears.IsSupported(request.TaxYear.Value))
        {
            errors.Add(new ValidationError(
                nameof(CreateTaxCaseRequest.TaxYear),
                $"Tax year {SafeDiagnosticText.RedactSinLikeSequences(request.TaxYear.Value.ToString(CultureInfo.InvariantCulture))} " +
                $"is not supported (supported: {string.Join(", ", SupportedTaxYears.All)}); no adjacent-year fallback is applied."));
        }

        var primary = ValidatePrimaryTaxpayer(request, errors);

        if (errors.Count > 0)
        {
            return ParseResult<CaseOperationOutcome>.Failure(errors);
        }

        var taxCase = new TaxCase(
            request.CaseId!,
            request.FirmId!,
            request.TaxYear!.Value,
            CaseStatus.Intake,
            [primary!],
            [],
            checklist: null,
            answers: []);

        var change = taxCase.BuildChange(
            CaseChangeKind.CaseCreated,
            participantId: primary!.ParticipantId,
            resultingStatus: CaseStatus.Intake,
            correlationReference: request.CorrelationReference);

        return ParseResult<CaseOperationOutcome>.Success(new CaseOperationOutcome(taxCase, change));
    }

    private static CaseParticipant? ValidatePrimaryTaxpayer(
        CreateTaxCaseRequest request,
        List<ValidationError> errors)
    {
        const string field = nameof(CreateTaxCaseRequest.PrimaryTaxpayer);
        var descriptor = request.PrimaryTaxpayer;

        if (descriptor is null)
        {
            errors.Add(new ValidationError(field, "A primary taxpayer participant is required."));
            return null;
        }

        var valid = true;

        if (!Enum.IsDefined(descriptor.Relationship))
        {
            errors.Add(new ValidationError(
                field,
                "The primary taxpayer's relationship kind is not a defined value."));
            valid = false;
        }
        else if (descriptor.Relationship != RelationshipKind.PrimaryTaxpayer)
        {
            errors.Add(new ValidationError(
                field,
                $"The initial participant must have relationship {RelationshipKind.PrimaryTaxpayer}, not {descriptor.Relationship}."));
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(descriptor.ParticipantId))
        {
            errors.Add(new ValidationError(
                field,
                "The primary taxpayer requires a non-empty participant identifier."));
            valid = false;
        }

        // Cross-firm consistency is only decidable against a usable case firm id;
        // a blank case firm id has already produced its own error.
        if (descriptor.FirmId is not null &&
            !string.IsNullOrWhiteSpace(request.FirmId) &&
            !string.Equals(descriptor.FirmId, request.FirmId, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                field,
                "The primary taxpayer references a different firm than the case; cross-firm references are rejected."));
            valid = false;
        }

        RecipientIdentifier? sin = null;

        if (descriptor.RawSin is not null)
        {
            try
            {
                sin = new RecipientIdentifier(descriptor.RawSin);
            }
            catch (ArgumentException)
            {
                errors.Add(new ValidationError(
                    field,
                    "A provided SIN must contain at least one alphanumeric character; omit it entirely when unknown."));
                valid = false;
            }
        }

        return valid
            ? new CaseParticipant(
                descriptor.ParticipantId!,
                RelationshipKind.PrimaryTaxpayer,
                descriptor.DisplayName,
                sin)
            : null;
    }
}
