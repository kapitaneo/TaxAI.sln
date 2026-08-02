using ClientIntake.Models;
using TaxCases.Models;

namespace ClientIntake.Services;

internal static class IntakeProjection
{
    internal static ClientIntakeView? Client(StoredIntakeCase storedCase)
    {
        var replayed = IntakeStateReplayer.ReplayLatestSubmission(storedCase);
        if (replayed is null)
        {
            return null;
        }

        var latest = IntakeStateReplayer.LatestSubmission(storedCase);
        return new ClientIntakeView(
            storedCase.TaxYear,
            IntakeQuestionnairePolicy.Views(storedCase),
            ExpectedDocuments(storedCase),
            latest?.Version,
            HasUnsubmittedChanges(storedCase));
    }

    internal static AccountantIntakeView? Accountant(
        StoredIntakeCase storedCase,
        DateTimeOffset nowUtc,
        ReviewHandoffState handoffState,
        Guid? reviewRecordId)
    {
        var replayed = IntakeStateReplayer.ReplayLatestSubmission(storedCase);
        if (replayed is null)
        {
            return null;
        }

        var categories = storedCase.ExpectedItems
            .GroupBy(item => item.Category)
            .ToDictionary(group => group.Key, group => group.Count());
        var currentAnswers = IntakeQuestionnairePolicy.CurrentAnswers(storedCase);
        var latest = IntakeStateReplayer.LatestSubmission(storedCase);

        return new AccountantIntakeView(
            storedCase.PrimaryTaxpayerDisplayName,
            storedCase.TaxYear,
            storedCase.CaseStatus,
            replayed.Readiness.State,
            categories.GetValueOrDefault(DocumentCategory.T4Slip),
            categories.GetValueOrDefault(DocumentCategory.MedicalExpenseEvidence),
            ExpectedDocuments(storedCase),
            currentAnswers.Count,
            categories.Count,
            latest?.Version,
            HasUnsubmittedChanges(storedCase),
            LinkState(storedCase.CurrentLink, nowUtc),
            storedCase.CurrentLink.IssuedAtUtc,
            storedCase.CurrentLink.ExpiresAtUtc,
            storedCase.CurrentLink.RevokedAtUtc,
            storedCase.CurrentLink.SpentAtUtc,
            handoffState,
            reviewRecordId);
    }

    internal static IReadOnlyList<ExpectedDocumentView> ExpectedDocuments(
        StoredIntakeCase storedCase)
    {
        var submission = IntakeStateReplayer.LatestSubmission(storedCase);
        var submittedDocumentIds = submission?.Documents
            .Select(reference => reference.DocumentId)
            .ToHashSet() ?? [];
        var submittedAnswers = submission?.Answers
            .Select(reference => storedCase.AnswerVersions.SingleOrDefault(answer =>
                answer.Category == reference.Category &&
                answer.Version == reference.AnswerVersion))
            .Where(answer => answer is not null)
            .ToDictionary(answer => answer!.Category, answer => answer!)
            ?? new Dictionary<DocumentCategory, IntakeAnswerVersionRecord>();

        return Array.AsReadOnly(
            storedCase.ExpectedItems
                .OrderBy(item => (int)item.Category)
                .ThenBy(item => item.Ordinal)
                .Select(item => new ExpectedDocumentView(
                    item.Category,
                    item.Ordinal,
                    Status(item, storedCase, submittedDocumentIds, submittedAnswers)))
                .ToArray());
    }

    internal static bool HasUnsubmittedChanges(StoredIntakeCase storedCase)
    {
        var currentAnswers = IntakeQuestionnairePolicy.CurrentAnswers(storedCase);
        var currentDocuments = storedCase.Documents
            .GroupBy(document => document.ExpectedItemId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(document => document.Version).First());
        var submission = IntakeStateReplayer.LatestSubmission(storedCase);

        if (submission is null)
        {
            return currentAnswers.Count > 0 || currentDocuments.Count > 0;
        }

        var submittedAnswers = submission.Answers.ToDictionary(
            reference => reference.Category,
            reference => reference.AnswerVersion);
        if (currentAnswers.Any(current =>
                !submittedAnswers.TryGetValue(current.Key, out var version) ||
                version != current.Value.Version) ||
            submittedAnswers.Count != currentAnswers.Count)
        {
            return true;
        }

        var submittedDocuments = submission.Documents
            .Select(reference => reference.DocumentId)
            .ToHashSet();
        return currentDocuments.Values.Any(document =>
            !submittedDocuments.Contains(document.DocumentId));
    }

    internal static IntakeLinkState LinkState(
        IntakeLinkRecord link,
        DateTimeOffset nowUtc)
    {
        if (link.RevokedAtUtc is not null)
        {
            return IntakeLinkState.Revoked;
        }

        if (link.SpentAtUtc is not null)
        {
            return IntakeLinkState.Spent;
        }

        return nowUtc >= link.ExpiresAtUtc
            ? IntakeLinkState.Expired
            : IntakeLinkState.Active;
    }

    private static ExpectedDocumentStatus Status(
        IntakeExpectedItemRecord item,
        StoredIntakeCase storedCase,
        HashSet<Guid> submittedDocumentIds,
        IReadOnlyDictionary<DocumentCategory, IntakeAnswerVersionRecord> submittedAnswers)
    {
        var itemDocuments = storedCase.Documents
            .Where(document => document.ExpectedItemId == item.ExpectedItemId)
            .ToArray();
        if (itemDocuments.Any(document => submittedDocumentIds.Contains(document.DocumentId)))
        {
            return ExpectedDocumentStatus.Ready;
        }

        if (itemDocuments.Length > 0)
        {
            return ExpectedDocumentStatus.Uploaded;
        }

        var latestAttempt = storedCase.UploadAttempts
            .Where(attempt => attempt.ExpectedItemId == item.ExpectedItemId)
            .OrderByDescending(attempt => attempt.AttemptedAtUtc)
            .FirstOrDefault();
        if (latestAttempt is not null &&
            latestAttempt.Outcome is IntakeUploadOutcome.Rejected or
                IntakeUploadOutcome.Duplicate)
        {
            return ExpectedDocumentStatus.Rejected;
        }

        if (submittedAnswers.TryGetValue(item.Category, out var submittedAnswer) &&
            submittedAnswer.Answer == AvailabilityAnswerCode.Unavailable)
        {
            return ExpectedDocumentStatus.Missing;
        }

        return ExpectedDocumentStatus.Requested;
    }
}
