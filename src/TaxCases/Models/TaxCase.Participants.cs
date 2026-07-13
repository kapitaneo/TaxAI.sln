using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> AddParticipant(
        ParticipantDescriptor participant,
        string? correlationReference)
    {
        var statusError = GuardMutable();

        if (statusError is not null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(statusError);
        }

        if (participant is null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError("Participant", "A participant descriptor is required."));
        }

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(participant.ParticipantId))
        {
            errors.Add(new ValidationError(
                nameof(ParticipantDescriptor.ParticipantId),
                "A non-empty participant identifier is required."));
        }
        else if (Participants.Any(existing =>
            string.Equals(existing.ParticipantId, participant.ParticipantId, StringComparison.Ordinal)))
        {
            errors.Add(new ValidationError(
                nameof(ParticipantDescriptor.ParticipantId),
                "A participant with this identifier already exists on the case."));
        }

        if (!Enum.IsDefined(participant.Relationship))
        {
            errors.Add(new ValidationError(
                nameof(ParticipantDescriptor.Relationship),
                "The participant's relationship kind is not a defined value."));
        }
        else if (participant.Relationship == RelationshipKind.PrimaryTaxpayer &&
            Participants.Any(existing => existing.Relationship == RelationshipKind.PrimaryTaxpayer))
        {
            errors.Add(new ValidationError(
                nameof(ParticipantDescriptor.Relationship),
                $"The case already has its {RelationshipKind.PrimaryTaxpayer}; exactly one primary taxpayer is allowed."));
        }
        else if (participant.Relationship == RelationshipKind.SpouseOrCommonLaw &&
            Participants.Any(existing => existing.Relationship == RelationshipKind.SpouseOrCommonLaw))
        {
            errors.Add(new ValidationError(
                nameof(ParticipantDescriptor.Relationship),
                $"The case already has a {RelationshipKind.SpouseOrCommonLaw} participant; at most one is allowed."));
        }

        if (participant.FirmId is not null &&
            !string.Equals(participant.FirmId, FirmId, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                nameof(ParticipantDescriptor.FirmId),
                "The participant references a different firm than the case; cross-firm references are rejected."));
        }

        RecipientIdentifier? sin = null;

        if (participant.RawSin is not null)
        {
            try
            {
                sin = new RecipientIdentifier(participant.RawSin);
            }
            catch (ArgumentException)
            {
                errors.Add(new ValidationError(
                    "Sin",
                    "A provided SIN must contain at least one alphanumeric character; omit it entirely when unknown."));
            }
        }

        if (errors.Count > 0)
        {
            return ParseResult<CaseOperationOutcome>.Failure(errors);
        }

        var added = new CaseParticipant(
            participant.ParticipantId!,
            participant.Relationship,
            participant.DisplayName,
            sin);

        var replacement = With(participants: [.. Participants, added]);

        var change = BuildChange(
            CaseChangeKind.ParticipantAdded,
            participantId: added.ParticipantId,
            correlationReference: correlationReference);

        return ParseResult<CaseOperationOutcome>.Success(new CaseOperationOutcome(replacement, change));
    }
}
