using TaxCases.Services;
using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> ConfirmChecklist(
        ChecklistDescriptor checklist,
        string? correlationReference)
    {
        var statusError = GuardMutable();

        if (statusError is not null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(statusError);
        }

        if (checklist is null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError("Checklist", "A checklist descriptor is required."));
        }

        var errors = new List<ValidationError>();

        if (checklist.EvidenceRequirements is null)
        {
            errors.Add(new ValidationError(
                nameof(ChecklistDescriptor.EvidenceRequirements),
                "An evidence requirement list is required; confirm an empty list explicitly."));
        }

        if (checklist.Questions is null)
        {
            errors.Add(new ValidationError(
                nameof(ChecklistDescriptor.Questions),
                "A question list is required; confirm an empty list explicitly."));
        }

        List<EvidenceRequirement> requirements = checklist.EvidenceRequirements is null
            ? []
            : ValidateRequirements(checklist.EvidenceRequirements, errors);

        List<IntakeQuestion> questions = checklist.Questions is null
            ? []
            : ValidateQuestions(checklist.Questions, errors);

        if (errors.Count > 0)
        {
            return ParseResult<CaseOperationOutcome>.Failure(errors);
        }

        var confirmed = new RequirementChecklist(requirements, questions);

        // An answer survives reconfirmation only when its question still exists
        // with the same scope and target; removed or retargeted questions lose
        // their answers.
        var retainedAnswers = Answers
            .Where(answer => questions.Any(question =>
                string.Equals(question.QuestionId, answer.QuestionId, StringComparison.Ordinal) &&
                AttributionMatches(question, answer.ParticipantId)))
            .ToArray();

        var replacement = With(
            checklist: confirmed,
            replaceChecklist: true,
            answers: retainedAnswers);

        var change = BuildChange(
            CaseChangeKind.ChecklistConfirmed,
            correlationReference: correlationReference);

        return ParseResult<CaseOperationOutcome>.Success(new CaseOperationOutcome(replacement, change));
    }

    public ParseResult<CaseOperationOutcome> AnswerQuestion(
        IntakeAnswerDescriptor answer,
        string? correlationReference)
    {
        var statusError = GuardMutable();

        if (statusError is not null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(statusError);
        }

        if (answer is null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError("Answer", "An intake answer descriptor is required."));
        }

        if (Checklist is null)
        {
            return ParseResult<CaseOperationOutcome>.Failure(
                new ValidationError(
                    "Checklist",
                    "Answers cannot be recorded before a checklist has been confirmed."));
        }

        var errors = new List<ValidationError>();
        IntakeQuestion? question = null;

        if (string.IsNullOrWhiteSpace(answer.QuestionId))
        {
            errors.Add(new ValidationError(
                nameof(IntakeAnswerDescriptor.QuestionId),
                "A non-empty question identifier is required."));
        }
        else
        {
            question = Checklist.Questions.FirstOrDefault(existing =>
                string.Equals(existing.QuestionId, answer.QuestionId, StringComparison.Ordinal));

            if (question is null)
            {
                errors.Add(new ValidationError(
                    nameof(IntakeAnswerDescriptor.QuestionId),
                    "No confirmed intake question has this identifier."));
            }
        }

        if (string.IsNullOrWhiteSpace(answer.Answer))
        {
            errors.Add(new ValidationError(
                nameof(IntakeAnswerDescriptor.Answer),
                "A non-blank answer is required; a missing answer is not an answer."));
        }

        if (question is not null && !AttributionMatches(question, answer.ParticipantId))
        {
            errors.Add(question.Scope == IntakeQuestionScope.Case
                ? new ValidationError(
                    nameof(IntakeAnswerDescriptor.ParticipantId),
                    "A case-level question accepts only an unattributed answer.")
                : new ValidationError(
                    nameof(IntakeAnswerDescriptor.ParticipantId),
                    "A participant-targeted question must be answered for its exact target participant."));
        }

        if (errors.Count > 0)
        {
            return ParseResult<CaseOperationOutcome>.Failure(errors);
        }

        var recorded = new IntakeAnswer(answer.QuestionId!, answer.ParticipantId, answer.Answer!);

        // Re-answering replaces the prior answer in place, preserving order.
        var answers = Answers.ToList();
        var existingIndex = answers.FindIndex(existing =>
            string.Equals(existing.QuestionId, recorded.QuestionId, StringComparison.Ordinal));

        if (existingIndex >= 0)
        {
            answers[existingIndex] = recorded;
        }
        else
        {
            answers.Add(recorded);
        }

        var replacement = With(answers: answers);

        var change = BuildChange(
            CaseChangeKind.QuestionAnswered,
            participantId: recorded.ParticipantId,
            questionId: recorded.QuestionId,
            correlationReference: correlationReference);

        return ParseResult<CaseOperationOutcome>.Success(new CaseOperationOutcome(replacement, change));
    }

    private List<EvidenceRequirement> ValidateRequirements(
        IReadOnlyList<EvidenceRequirementDescriptor> descriptors,
        List<ValidationError> errors)
    {
        var requirements = new List<EvidenceRequirement>(descriptors.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            if (descriptor is null)
            {
                errors.Add(new ValidationError(
                    nameof(ChecklistDescriptor.EvidenceRequirements),
                    "An evidence requirement descriptor must not be null."));
                continue;
            }

            var errorCountBefore = errors.Count;

            if (string.IsNullOrWhiteSpace(descriptor.RequirementId))
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.RequirementId),
                    "A non-empty requirement identifier is required."));
            }
            else if (!seenIds.Add(descriptor.RequirementId))
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.RequirementId),
                    "A requirement with this identifier appears more than once in the checklist."));
            }

            if (string.IsNullOrWhiteSpace(descriptor.ParticipantId))
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.ParticipantId),
                    "Every evidence requirement must target a participant; a case-wide category alone is never a requirement."));
            }
            else if (!IsCaseParticipant(descriptor.ParticipantId))
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.ParticipantId),
                    "An evidence requirement must target a participant who belongs to this case."));
            }

            DocumentCategory category = default;

            if (string.IsNullOrWhiteSpace(descriptor.Category))
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.Category),
                    "A document category is required."));
            }
            else if (!TryParseCategory(descriptor.Category, out category))
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.Category),
                    $"The document category '{SafeDiagnosticText.RedactSinLikeSequences(descriptor.Category)}' is not supported."));
            }

            if (descriptor.MinimumCount is null)
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.MinimumCount),
                    "A minimum evidence count is required."));
            }
            else if (descriptor.MinimumCount.Value <= 0)
            {
                errors.Add(new ValidationError(
                    nameof(EvidenceRequirementDescriptor.MinimumCount),
                    "A minimum evidence count must be strictly positive."));
            }

            if (errors.Count == errorCountBefore)
            {
                requirements.Add(new EvidenceRequirement(
                    descriptor.RequirementId!,
                    descriptor.ParticipantId!,
                    category,
                    descriptor.MinimumCount!.Value));
            }
        }

        return requirements;
    }

    private List<IntakeQuestion> ValidateQuestions(
        IReadOnlyList<IntakeQuestion> candidates,
        List<ValidationError> errors)
    {
        var questions = new List<IntakeQuestion>(candidates.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                errors.Add(new ValidationError(
                    nameof(ChecklistDescriptor.Questions),
                    "An intake question must not be null."));
                continue;
            }

            var errorCountBefore = errors.Count;

            if (string.IsNullOrWhiteSpace(candidate.QuestionId))
            {
                errors.Add(new ValidationError(
                    nameof(IntakeQuestion.QuestionId),
                    "A non-empty question identifier is required."));
            }
            else if (!seenIds.Add(candidate.QuestionId))
            {
                errors.Add(new ValidationError(
                    nameof(IntakeQuestion.QuestionId),
                    "A question with this identifier appears more than once in the checklist."));
            }

            if (string.IsNullOrWhiteSpace(candidate.Prompt))
            {
                errors.Add(new ValidationError(
                    nameof(IntakeQuestion.Prompt),
                    "A non-empty question prompt is required."));
            }

            if (!Enum.IsDefined(candidate.Scope))
            {
                errors.Add(new ValidationError(
                    nameof(IntakeQuestion.Scope),
                    "The question scope is not a defined value."));
            }
            else if (candidate.Scope == IntakeQuestionScope.Case)
            {
                if (candidate.ParticipantId is not null)
                {
                    errors.Add(new ValidationError(
                        nameof(IntakeQuestion.ParticipantId),
                        "A case-level question must not name a participant."));
                }
            }
            else if (string.IsNullOrWhiteSpace(candidate.ParticipantId))
            {
                errors.Add(new ValidationError(
                    nameof(IntakeQuestion.ParticipantId),
                    "A participant-scoped question must target exactly one participant."));
            }
            else if (!IsCaseParticipant(candidate.ParticipantId))
            {
                errors.Add(new ValidationError(
                    nameof(IntakeQuestion.ParticipantId),
                    "A participant-scoped question must target a participant who belongs to this case."));
            }

            if (errors.Count == errorCountBefore)
            {
                questions.Add(candidate);
            }
        }

        return questions;
    }

    private bool IsCaseParticipant(string participantId) =>
        Participants.Any(existing =>
            string.Equals(existing.ParticipantId, participantId, StringComparison.Ordinal));

    private static bool AttributionMatches(IntakeQuestion question, string? participantId) =>
        question.Scope == IntakeQuestionScope.Case
            ? participantId is null
            : string.Equals(question.ParticipantId, participantId, StringComparison.Ordinal);
}
