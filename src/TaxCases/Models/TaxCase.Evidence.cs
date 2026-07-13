using TaxCases.Services;
using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> AttachEvidence(
        EvidenceDescriptor evidence,
        string? correlationReference)
    {
        var statusError = GuardMutable();

        if (statusError is not null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(statusError);
        }

        if (evidence is null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError("Evidence", "An evidence descriptor is required."));
        }

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(evidence.EvidenceId))
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.EvidenceId),
                "A non-empty evidence identifier is required."));
        }
        else if (Evidence.Any(existing =>
            string.Equals(existing.EvidenceId, evidence.EvidenceId, StringComparison.Ordinal)))
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.EvidenceId),
                "Evidence with this identifier already exists on the case."));
        }

        DocumentCategory category = default;

        if (string.IsNullOrWhiteSpace(evidence.Category))
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.Category),
                "A document category is required."));
        }
        else if (!TryParseCategory(evidence.Category, out category))
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.Category),
                $"The document category '{SafeDiagnosticText.RedactSinLikeSequences(evidence.Category)}' is not supported."));
        }

        if (string.IsNullOrWhiteSpace(evidence.ParticipantId))
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.ParticipantId),
                "A non-empty participant identifier is required."));
        }
        else if (!Participants.Any(existing =>
            string.Equals(existing.ParticipantId, evidence.ParticipantId, StringComparison.Ordinal)))
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.ParticipantId),
                "Evidence must be attributed to a participant who belongs to this case."));
        }

        if (evidence.TaxYear is null)
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.TaxYear),
                "An evidence tax year is required."));
        }
        else if (evidence.TaxYear.Value != TaxYear)
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.TaxYear),
                $"The evidence tax year {evidence.TaxYear.Value} does not equal the case tax year {TaxYear}; evidence is never adopted across years."));
        }

        if (evidence.FirmId is not null &&
            !string.Equals(evidence.FirmId, FirmId, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                nameof(EvidenceDescriptor.FirmId),
                "The evidence references a different firm than the case; cross-firm references are rejected."));
        }

        if (errors.Count > 0)
        {
            return ParseResult<CaseOperationOutcome>.Failure(errors);
        }

        var attached = new ReceivedEvidence(
            evidence.EvidenceId!,
            category,
            evidence.ParticipantId!,
            evidence.TaxYear!.Value,
            evidence.SourceReference);

        var replacement = With(evidence: [.. Evidence, attached]);

        var change = BuildChange(
            CaseChangeKind.EvidenceAttached,
            participantId: attached.ParticipantId,
            evidenceId: attached.EvidenceId,
            correlationReference: correlationReference);

        return ParseResult<CaseOperationOutcome>.Success(new CaseOperationOutcome(replacement, change));
    }

    private static bool TryParseCategory(string value, out DocumentCategory category)
    {
        foreach (var candidate in Enum.GetValues<DocumentCategory>())
        {
            if (string.Equals(value, candidate.ToString(), StringComparison.Ordinal))
            {
                category = candidate;
                return true;
            }
        }

        category = default;
        return false;
    }
}
