using ClientIntake.Models;
using TaxCases.Models;

namespace ClientIntake.Services;

internal static class IntakeQuestionnairePolicy
{
    private static readonly IReadOnlyList<AvailabilityAnswerCode> AnswerOptions =
        Array.AsReadOnly(Enum.GetValues<AvailabilityAnswerCode>());

    internal static bool IsSupportedCategory(DocumentCategory category) =>
        category is DocumentCategory.T4Slip or
            DocumentCategory.MedicalExpenseEvidence;

    internal static string QuestionId(DocumentCategory category) =>
        category switch
        {
            DocumentCategory.T4Slip => "t4-availability",
            DocumentCategory.MedicalExpenseEvidence => "medical-availability",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    internal static string Prompt(DocumentCategory category) =>
        category switch
        {
            DocumentCategory.T4Slip => "Are the requested T4 documents available now?",
            DocumentCategory.MedicalExpenseEvidence =>
                "Are the requested medical-expense documents available now?",
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    internal static string AnswerValue(AvailabilityAnswerCode answer) =>
        answer switch
        {
            AvailabilityAnswerCode.AvailableNow => "available-now",
            AvailabilityAnswerCode.WillProvideLater => "will-provide-later",
            AvailabilityAnswerCode.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(answer)),
        };

    internal static IntakeQuestion DomainQuestion(DocumentCategory category) =>
        new(
            QuestionId(category),
            Prompt(category),
            IntakeQuestionScope.Case,
            ParticipantId: null);

    internal static IReadOnlyList<IntakeQuestion> DomainQuestions(
        IReadOnlyList<IntakeExpectedItemRecord> expectedItems) =>
        Array.AsReadOnly(
            expectedItems
                .Select(item => item.Category)
                .Distinct()
                .OrderBy(category => (int)category)
                .Select(DomainQuestion)
                .ToArray());

    internal static IReadOnlyList<IntakeQuestionView> Views(
        StoredIntakeCase storedCase)
    {
        var currentAnswers = CurrentAnswers(storedCase);

        return Array.AsReadOnly(
            storedCase.ExpectedItems
                .Select(item => item.Category)
                .Distinct()
                .OrderBy(category => (int)category)
                .Select(category => new IntakeQuestionView(
                    category,
                    Prompt(category),
                    AnswerOptions,
                    currentAnswers.TryGetValue(category, out var current)
                        ? current.Answer
                        : null))
                .ToArray());
    }

    internal static IReadOnlyDictionary<DocumentCategory, IntakeAnswerVersionRecord>
        CurrentAnswers(StoredIntakeCase storedCase) =>
        storedCase.AnswerVersions
            .GroupBy(answer => answer.Category)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(answer => answer.Version)
                    .First());
}
