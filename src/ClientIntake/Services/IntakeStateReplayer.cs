using ClientIntake.Models;
using TaxCases.Models;
using Tenancy.Models;

namespace ClientIntake.Services;

internal static class IntakeStateReplayer
{
    internal static TaxCase? ReplayLatestSubmission(StoredIntakeCase storedCase)
    {
        ArgumentNullException.ThrowIfNull(storedCase);

        if (!ValidStoredShape(storedCase))
        {
            return null;
        }

        var created = TaxCase.Create(
            new CreateTaxCaseRequest(
                storedCase.CaseId.ToString("D"),
                storedCase.Firm.ToCanonicalString(),
                storedCase.TaxYear,
                new ParticipantDescriptor(
                    storedCase.PrimaryParticipantId.ToString("D"),
                    RelationshipKind.PrimaryTaxpayer,
                    storedCase.PrimaryTaxpayerDisplayName,
                    null,
                    storedCase.Firm.ToCanonicalString()),
                CorrelationReference: null));
        if (!created.IsSuccess)
        {
            return null;
        }

        var requirements = storedCase.ExpectedItems
            .GroupBy(item => item.Category)
            .OrderBy(group => (int)group.Key)
            .Select(group => new EvidenceRequirementDescriptor(
                $"requirement-{IntakeQuestionnairePolicy.QuestionId(group.Key)}",
                storedCase.PrimaryParticipantId.ToString("D"),
                group.Key.ToString(),
                group.Count()))
            .ToArray();
        var checklist = created.Value!.Case.ConfirmChecklist(
            new ChecklistDescriptor(
                requirements,
                IntakeQuestionnairePolicy.DomainQuestions(storedCase.ExpectedItems)),
            correlationReference: null);
        if (!checklist.IsSuccess)
        {
            return null;
        }

        var transitioned = checklist.Value!.Case.TransitionTo(
            storedCase.CaseStatus,
            correlationReference: null);
        if (!transitioned.IsSuccess)
        {
            return null;
        }

        var current = transitioned.Value!.Case;
        var submission = LatestSubmission(storedCase);
        if (submission is null)
        {
            return current;
        }

        if (submission.Answers
                .Select(reference => reference.Category)
                .Distinct()
                .Count() != submission.Answers.Count ||
            submission.Documents
                .Select(reference => reference.DocumentId)
                .Distinct()
                .Count() != submission.Documents.Count)
        {
            return null;
        }

        foreach (var answerReference in submission.Answers)
        {
            var answers = storedCase.AnswerVersions
                .Where(answer =>
                    answer.Category == answerReference.Category &&
                    answer.Version == answerReference.AnswerVersion)
                .ToArray();
            if (answers.Length != 1)
            {
                return null;
            }

            var answered = current.AnswerQuestion(
                new IntakeAnswerDescriptor(
                    IntakeQuestionnairePolicy.QuestionId(answerReference.Category),
                    ParticipantId: null,
                    IntakeQuestionnairePolicy.AnswerValue(answers[0].Answer)),
                correlationReference: null);
            if (!answered.IsSuccess)
            {
                return null;
            }

            current = answered.Value!.Case;
        }

        foreach (var documentReference in submission.Documents)
        {
            var documents = storedCase.Documents
                .Where(document => document.DocumentId == documentReference.DocumentId)
                .ToArray();
            if (documents.Length != 1)
            {
                return null;
            }

            var document = documents[0];
            var expected = storedCase.ExpectedItems
                .SingleOrDefault(item => item.ExpectedItemId == document.ExpectedItemId);
            if (expected is null || expected.Category != document.Category)
            {
                return null;
            }

            var attached = current.AttachEvidence(
                new EvidenceDescriptor(
                    document.DocumentId.ToString("D"),
                    document.Category.ToString(),
                    storedCase.PrimaryParticipantId.ToString("D"),
                    storedCase.TaxYear,
                    SourceReference: null,
                    storedCase.Firm.ToCanonicalString()),
                correlationReference: null);
            if (!attached.IsSuccess)
            {
                return null;
            }

            current = attached.Value!.Case;
        }

        return current;
    }

    internal static IntakeSubmissionRecord? LatestSubmission(StoredIntakeCase storedCase) =>
        storedCase.Submissions
            .OrderByDescending(submission => submission.Version)
            .FirstOrDefault();

    private static bool ValidStoredShape(StoredIntakeCase storedCase) =>
        storedCase.Firm is not null &&
        storedCase.CaseId != Guid.Empty &&
        storedCase.PrimaryParticipantId != Guid.Empty &&
        IntakeDisplayNameGuard.IsValid(storedCase.PrimaryTaxpayerDisplayName) &&
        storedCase.CaseStatus == CaseStatus.InformationGathering &&
        storedCase.ExpectedItems.Count > 0 &&
        storedCase.ExpectedItems.All(item =>
            item.ExpectedItemId != Guid.Empty &&
            item.Ordinal > 0 &&
            IntakeQuestionnairePolicy.IsSupportedCategory(item.Category)) &&
        storedCase.ExpectedItems
            .GroupBy(item => new { item.Category, item.Ordinal })
            .All(group => group.Count() == 1);
}
