using TaxCases.Models;

namespace TaxCases.Services;

public static class ReadinessCalculator
{
    public static ReviewReadiness Calculate(TaxCase taxCase)
    {
        ArgumentNullException.ThrowIfNull(taxCase);

        // An unconfigured checklist alone forces NotReady; nothing else can be
        // assessed against a checklist that was never confirmed.
        if (taxCase.Checklist is null)
        {
            return new ReviewReadiness(
                ReadinessState.NotReady,
                [
                    new ReadinessReason(
                        ReadinessReasonKind.ChecklistNotConfigured,
                        "The requirement checklist has not been confirmed.")
                ]);
        }

        var reasons = new List<ReadinessReason>();

        if (taxCase.Participants.Count(participant =>
            participant.Relationship == RelationshipKind.PrimaryTaxpayer) != 1)
        {
            reasons.Add(new ReadinessReason(
                ReadinessReasonKind.NoPrimaryTaxpayer,
                "The case does not have exactly one primary taxpayer."));
        }

        AddUnsatisfiedRequirementReasons(taxCase, reasons);
        AddUnansweredQuestionReasons(taxCase, reasons);

        return reasons.Count == 0
            ? new ReviewReadiness(ReadinessState.Ready, [])
            : new ReviewReadiness(ReadinessState.NotReady, reasons);
    }

    private static void AddUnsatisfiedRequirementReasons(TaxCase taxCase, List<ReadinessReason> reasons)
    {
        // Requirements are evaluated in checklist insertion order and evidence
        // in evidence insertion order; an allocated record is never reused, so
        // one evidence identity satisfies at most one requirement.
        var allocated = new bool[taxCase.Evidence.Count];

        foreach (var requirement in taxCase.Checklist!.EvidenceRequirements)
        {
            var matched = 0;

            for (var index = 0; index < taxCase.Evidence.Count && matched < requirement.MinimumCount; index++)
            {
                if (allocated[index])
                {
                    continue;
                }

                var evidence = taxCase.Evidence[index];

                if (string.Equals(evidence.ParticipantId, requirement.ParticipantId, StringComparison.Ordinal) &&
                    evidence.Category == requirement.Category)
                {
                    allocated[index] = true;
                    matched++;
                }
            }

            if (matched < requirement.MinimumCount)
            {
                var remaining = requirement.MinimumCount - matched;

                reasons.Add(new ReadinessReason(
                    ReadinessReasonKind.UnsatisfiedEvidenceRequirement,
                    $"Evidence requirement '{SafeDiagnosticText.RedactSinLikeSequences(requirement.RequirementId)}' " +
                    $"for participant '{SafeDiagnosticText.RedactSinLikeSequences(requirement.ParticipantId)}' " +
                    $"needs {remaining} more {requirement.Category} evidence record(s)."));
            }
        }
    }

    private static void AddUnansweredQuestionReasons(TaxCase taxCase, List<ReadinessReason> reasons)
    {
        foreach (var question in taxCase.Checklist!.Questions)
        {
            var answered = taxCase.Answers.Any(answer =>
                string.Equals(answer.QuestionId, question.QuestionId, StringComparison.Ordinal) &&
                (question.Scope == IntakeQuestionScope.Case
                    ? answer.ParticipantId is null
                    : string.Equals(answer.ParticipantId, question.ParticipantId, StringComparison.Ordinal)));

            if (!answered)
            {
                reasons.Add(new ReadinessReason(
                    ReadinessReasonKind.UnansweredIntakeQuestion,
                    $"Intake question '{SafeDiagnosticText.RedactSinLikeSequences(question.QuestionId)}' " +
                    "has no answer with its required attribution."));
            }
        }
    }
}
