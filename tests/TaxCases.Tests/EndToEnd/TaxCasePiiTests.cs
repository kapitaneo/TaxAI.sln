using System.Text.Json;
using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.EndToEnd;

/// <summary>
/// Repeats the mandatory PII matrix through the finished public operations and
/// sweeps accepted state, rejected errors, readiness reasons, change records,
/// defensive JSON, and every public diagnostic touched by the scenario.
/// </summary>
public sealed class TaxCasePiiTests
{
    private static readonly string[] RawSinForms =
    [
        "123456789",
        "123-456-789",
        "123 456 789"
    ];

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    public void MandatoryPiiMatrix_ActualOperationsRetainDomainValuesButProtectEveryOutput(string raw)
    {
        var scenario = RunPiiScenario(raw);

        AssertRetainedDomainValues(scenario, raw);
        AssertScenarioOutputsAreSafe(scenario);
        Assert.Contains("***-**-789", scenario.Primary.ToString(), StringComparison.Ordinal);
        Assert.Contains("***-**-789", scenario.ReopenChange.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SpaceSeparatedSin_IsSafeInAcceptedValueRejectedErrorReadinessReasonAndChangeDiagnostic()
    {
        const string raw = "123 456 789";
        var scenario = RunPiiScenario(raw);

        Assert.Equal(raw, scenario.CreatedCase.CaseId);
        Assert.Equal(raw, scenario.UnreadyCase.Checklist!.EvidenceRequirements[0].RequirementId);
        Assert.Equal(raw, scenario.InvalidCategory.Category);
        Assert.Equal(raw, scenario.ReopenChange.Detail);

        Assert.NotEmpty(scenario.UnreadyCase.Readiness.Reasons);
        Assert.NotEmpty(scenario.InvalidCategoryResult.Errors);
        AssertScenarioOutputsAreSafe(scenario);
        Assert.Contains("***-**-789", scenario.UnreadyCase.Readiness.Reasons[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("***-**-789", scenario.ReopenChange.ToString(), StringComparison.Ordinal);
    }

    private static PiiScenario RunPiiScenario(string raw)
    {
        var primaryDescriptor = new ParticipantDescriptor(
            raw,
            RelationshipKind.PrimaryTaxpayer,
            raw,
            raw,
            raw);
        var createRequest = new CreateTaxCaseRequest(raw, raw, 2025, primaryDescriptor, raw);
        var create = TaxCase.Create(createRequest);
        var createdCase = Accepted(create);
        var primary = Assert.Single(createdCase.Participants);

        var dependantDescriptor = new ParticipantDescriptor(
            $"dependant-{raw}",
            RelationshipKind.Dependant,
            $"dependant {raw}",
            raw,
            raw);
        var added = createdCase.AddParticipant(dependantDescriptor, raw);
        var familyCase = Accepted(added);

        var requirementDescriptor = new EvidenceRequirementDescriptor(raw, raw, "T4Slip", 1);
        var question = new IntakeQuestion(raw, raw, IntakeQuestionScope.Participant, raw);
        var checklistDescriptor = new ChecklistDescriptor([requirementDescriptor], [question]);
        var confirmed = familyCase.ConfirmChecklist(checklistDescriptor, raw);
        var unreadyCase = Accepted(confirmed);

        Assert.Equal(ReadinessState.NotReady, unreadyCase.Readiness.State);
        Assert.Contains(
            unreadyCase.Readiness.Reasons,
            reason => reason.Kind == ReadinessReasonKind.UnsatisfiedEvidenceRequirement);
        Assert.Contains(
            unreadyCase.Readiness.Reasons,
            reason => reason.Kind == ReadinessReasonKind.UnansweredIntakeQuestion);

        var evidenceDescriptor = new EvidenceDescriptor(raw, "T4Slip", raw, 2025, raw, raw);
        var attached = unreadyCase.AttachEvidence(evidenceDescriptor, raw);
        var evidencedCase = Accepted(attached);

        var answerDescriptor = new IntakeAnswerDescriptor(raw, raw, raw);
        var answered = evidencedCase.AnswerQuestion(answerDescriptor, raw);
        var readyCase = Accepted(answered);
        Assert.Equal(ReadinessState.Ready, readyCase.Readiness.State);

        var gathering = readyCase.TransitionTo(CaseStatus.InformationGathering, raw);
        var inReview = Accepted(gathering).TransitionTo(CaseStatus.InReview, raw);
        var completed = Accepted(inReview).TransitionTo(CaseStatus.Completed, raw);
        var completedCase = Accepted(completed);

        var reopened = completedCase.Reopen(raw, raw);
        var reopenedCase = Accepted(reopened);

        var invalidCategory = new EvidenceDescriptor($"invalid-{raw}", raw, raw, 2025, raw, raw);
        var invalidCategoryResult = reopenedCase.AttachEvidence(invalidCategory, raw);
        Assert.True(invalidCategoryResult.IsFailure);
        Assert.Null(invalidCategoryResult.Value);
        Assert.NotEmpty(invalidCategoryResult.Errors);

        return new PiiScenario(
            primaryDescriptor,
            createRequest,
            create,
            createdCase,
            primary,
            dependantDescriptor,
            added,
            familyCase,
            requirementDescriptor,
            question,
            checklistDescriptor,
            confirmed,
            unreadyCase,
            evidenceDescriptor,
            attached,
            evidencedCase,
            answerDescriptor,
            answered,
            readyCase,
            gathering,
            inReview,
            completed,
            completedCase,
            reopened,
            reopenedCase,
            reopened.Value!.Change,
            invalidCategory,
            invalidCategoryResult);
    }

    private static TaxCase Accepted(ParseResult<CaseOperationOutcome> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
        return result.Value.Case;
    }

    private static void AssertRetainedDomainValues(PiiScenario scenario, string raw)
    {
        Assert.Equal(raw, scenario.CreateRequest.CaseId);
        Assert.Equal(raw, scenario.CreateRequest.FirmId);
        Assert.Equal(raw, scenario.PrimaryDescriptor.ParticipantId);
        Assert.Equal(raw, scenario.PrimaryDescriptor.DisplayName);
        Assert.Equal(raw, scenario.Primary.ParticipantId);
        Assert.Equal(raw, scenario.Primary.DisplayName);
        Assert.Contains(raw, scenario.DependantDescriptor.ParticipantId!, StringComparison.Ordinal);
        Assert.Contains(raw, scenario.FamilyCase.Participants[1].DisplayName!, StringComparison.Ordinal);
        Assert.Equal(raw, scenario.EvidenceDescriptor.EvidenceId);
        Assert.Equal(raw, scenario.EvidencedCase.Evidence[0].EvidenceId);
        Assert.Equal(raw, scenario.EvidencedCase.Evidence[0].SourceReference);
        Assert.Equal(raw, scenario.RequirementDescriptor.RequirementId);
        Assert.Equal(raw, scenario.UnreadyCase.Checklist!.EvidenceRequirements[0].RequirementId);
        Assert.Equal(raw, scenario.Question.QuestionId);
        Assert.Equal(raw, scenario.Question.Prompt);
        Assert.Equal(raw, scenario.ReadyCase.Answers[0].Answer);
        Assert.Equal(raw, scenario.InvalidCategory.Category);
        Assert.Equal(raw, scenario.CreateRequest.CorrelationReference);
        Assert.Equal(raw, scenario.ReopenChange.CorrelationReference);
        Assert.Equal(raw, scenario.ReopenChange.Detail);
    }

    private static void AssertScenarioOutputsAreSafe(PiiScenario scenario)
    {
        object[] publicValues =
        [
            scenario.PrimaryDescriptor,
            scenario.CreateRequest,
            scenario.CreateResult.Value!,
            scenario.CreateResult.Value!.Change,
            scenario.CreatedCase,
            scenario.Primary,
            scenario.DependantDescriptor,
            scenario.AddResult.Value!,
            scenario.AddResult.Value!.Change,
            scenario.FamilyCase.Participants[1],
            scenario.RequirementDescriptor,
            scenario.Question,
            scenario.ChecklistDescriptor,
            scenario.ConfirmResult.Value!,
            scenario.ConfirmResult.Value!.Change,
            scenario.UnreadyCase,
            scenario.UnreadyCase.Checklist!,
            .. scenario.UnreadyCase.Readiness.Reasons,
            scenario.EvidenceDescriptor,
            scenario.AttachResult.Value!,
            scenario.AttachResult.Value!.Change,
            scenario.EvidencedCase.Evidence[0],
            scenario.AnswerDescriptor,
            scenario.AnswerResult.Value!,
            scenario.AnswerResult.Value!.Change,
            scenario.ReadyCase.Answers[0],
            scenario.GatheringResult.Value!,
            scenario.GatheringResult.Value!.Change,
            scenario.InReviewResult.Value!,
            scenario.InReviewResult.Value!.Change,
            scenario.CompletedResult.Value!,
            scenario.CompletedResult.Value!.Change,
            scenario.CompletedCase,
            scenario.ReopenResult.Value!,
            scenario.ReopenResult.Value!.Change,
            scenario.ReopenedCase,
            scenario.InvalidCategory,
            .. scenario.InvalidCategoryResult.Errors
        ];

        foreach (var value in publicValues)
        {
            AssertSafe(JsonSerializer.Serialize(value, value.GetType()));
            AssertSafe(value.ToString()!);
        }

        foreach (var error in scenario.InvalidCategoryResult.Errors)
        {
            AssertSafe(error.Field);
            AssertSafe(error.Message);
        }

        foreach (var reason in scenario.UnreadyCase.Readiness.Reasons)
        {
            AssertSafe(reason.Detail);
        }
    }

    private static void AssertSafe(string output)
    {
        foreach (var raw in RawSinForms)
        {
            Assert.DoesNotContain(raw, output, StringComparison.Ordinal);
        }
    }

    private sealed record PiiScenario(
        ParticipantDescriptor PrimaryDescriptor,
        CreateTaxCaseRequest CreateRequest,
        ParseResult<CaseOperationOutcome> CreateResult,
        TaxCase CreatedCase,
        CaseParticipant Primary,
        ParticipantDescriptor DependantDescriptor,
        ParseResult<CaseOperationOutcome> AddResult,
        TaxCase FamilyCase,
        EvidenceRequirementDescriptor RequirementDescriptor,
        IntakeQuestion Question,
        ChecklistDescriptor ChecklistDescriptor,
        ParseResult<CaseOperationOutcome> ConfirmResult,
        TaxCase UnreadyCase,
        EvidenceDescriptor EvidenceDescriptor,
        ParseResult<CaseOperationOutcome> AttachResult,
        TaxCase EvidencedCase,
        IntakeAnswerDescriptor AnswerDescriptor,
        ParseResult<CaseOperationOutcome> AnswerResult,
        TaxCase ReadyCase,
        ParseResult<CaseOperationOutcome> GatheringResult,
        ParseResult<CaseOperationOutcome> InReviewResult,
        ParseResult<CaseOperationOutcome> CompletedResult,
        TaxCase CompletedCase,
        ParseResult<CaseOperationOutcome> ReopenResult,
        TaxCase ReopenedCase,
        CaseChangeRecord ReopenChange,
        EvidenceDescriptor InvalidCategory,
        ParseResult<CaseOperationOutcome> InvalidCategoryResult);
}
