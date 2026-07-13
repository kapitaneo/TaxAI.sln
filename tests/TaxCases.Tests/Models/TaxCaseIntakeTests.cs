using System.Text.Json;
using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.Models;

public sealed class TaxCaseIntakeTests
{
    private const string CaseId = "case-1";
    private const string FirmA = "firm-a";
    private const string PrimaryId = "p-1";
    private const string SpouseId = "p-2";

    private static TaxCase NewCase()
    {
        var created = TaxCase.Create(new CreateTaxCaseRequest(
            CaseId,
            FirmA,
            2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            CorrelationReference: null));

        Assert.True(created.IsSuccess);

        var withSpouse = created.Value!.Case.AddParticipant(
            new ParticipantDescriptor(SpouseId, RelationshipKind.SpouseOrCommonLaw, null, null, null),
            correlationReference: null);

        Assert.True(withSpouse.IsSuccess);
        return withSpouse.Value!.Case;
    }

    private static EvidenceRequirementDescriptor Requirement(
        string? requirementId = "req-1",
        string? participantId = PrimaryId,
        string? category = "T4Slip",
        int? minimumCount = 1) =>
        new(requirementId, participantId, category, minimumCount);

    private static IntakeQuestion CaseQuestion(
        string questionId = "q-case",
        string prompt = "Did your marital status change?") =>
        new(questionId, prompt, IntakeQuestionScope.Case, null);

    private static IntakeQuestion ParticipantQuestion(
        string questionId = "q-part",
        string participantId = SpouseId,
        string prompt = "Any medical expenses?") =>
        new(questionId, prompt, IntakeQuestionScope.Participant, participantId);

    private static ChecklistDescriptor Checklist(
        IReadOnlyList<EvidenceRequirementDescriptor>? requirements = null,
        IReadOnlyList<IntakeQuestion>? questions = null) =>
        new(requirements ?? [], questions ?? []);

    private static TaxCase Confirmed(TaxCase taxCase, ChecklistDescriptor descriptor)
    {
        var result = taxCase.ConfirmChecklist(descriptor, correlationReference: null);
        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static TaxCase Answered(TaxCase taxCase, IntakeAnswerDescriptor descriptor)
    {
        var result = taxCase.AnswerQuestion(descriptor, correlationReference: null);
        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static void AssertFailsClosed(
        ParseResult<CaseOperationOutcome> result,
        string expectedField)
    {
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, error => error.Field == expectedField);
    }

    private static void AssertConfirmFailsClosedAndUnchanged(
        TaxCase original,
        ParseResult<CaseOperationOutcome> result,
        string expectedField)
    {
        AssertFailsClosed(result, expectedField);

        // The original case is untouched: still unconfigured, no answers (AC-33/42).
        Assert.Null(original.Checklist);
        Assert.Empty(original.Answers);
        Assert.Equal(CaseStatus.Intake, original.Status);
    }

    // --- Unconfigured vs confirmed-empty (AC-16, requirement 19) ---

    [Fact]
    public void NewCase_ChecklistIsNull_MeaningNeverConfigured()
    {
        Assert.Null(NewCase().Checklist);
    }

    [Fact]
    public void ConfirmChecklist_ExplicitlyEmpty_IsConfiguredAndDistinctFromNull()
    {
        var taxCase = Confirmed(NewCase(), Checklist());

        Assert.NotNull(taxCase.Checklist);
        Assert.Empty(taxCase.Checklist!.EvidenceRequirements);
        Assert.Empty(taxCase.Checklist.Questions);
    }

    // --- Happy-path configuration (AC-16, requirements 18-21) ---

    [Fact]
    public void ConfirmChecklist_SeparatePrimaryAndSpouseT4Requirements_StoredExactlyInOrder()
    {
        var taxCase = Confirmed(NewCase(), Checklist(requirements:
        [
            Requirement("req-primary-t4", PrimaryId),
            Requirement("req-spouse-t4", SpouseId)
        ]));

        var requirements = taxCase.Checklist!.EvidenceRequirements;
        Assert.Equal(2, requirements.Count);
        Assert.Equal(new EvidenceRequirement("req-primary-t4", PrimaryId, DocumentCategory.T4Slip, 1), requirements[0]);
        Assert.Equal(new EvidenceRequirement("req-spouse-t4", SpouseId, DocumentCategory.T4Slip, 1), requirements[1]);
    }

    [Fact]
    public void ConfirmChecklist_TwoT4Minimum_StoresPositiveMinimumCount()
    {
        var taxCase = Confirmed(NewCase(), Checklist(requirements:
        [
            Requirement(minimumCount: 2)
        ]));

        Assert.Equal(2, Assert.Single(taxCase.Checklist!.EvidenceRequirements).MinimumCount);
    }

    [Fact]
    public void ConfirmChecklist_CaseAndParticipantQuestions_StoredExactlyInOrder()
    {
        var caseQuestion = CaseQuestion();
        var participantQuestion = ParticipantQuestion();

        var taxCase = Confirmed(NewCase(), Checklist(questions: [caseQuestion, participantQuestion]));

        var questions = taxCase.Checklist!.Questions;
        Assert.Equal(2, questions.Count);
        Assert.Equal(caseQuestion, questions[0]);
        Assert.Equal(participantQuestion, questions[1]);
    }

    [Fact]
    public void ConfirmChecklist_RepeatedParticipantCategoryPairs_AllowedWithDistinctIds()
    {
        var taxCase = Confirmed(NewCase(), Checklist(requirements:
        [
            Requirement("req-1", PrimaryId),
            Requirement("req-2", PrimaryId)
        ]));

        Assert.Equal(2, taxCase.Checklist!.EvidenceRequirements.Count);
    }

    [Fact]
    public void ConfirmChecklist_DefensiveSnapshot_LaterInputMutationDoesNotAffectCase()
    {
        var requirements = new List<EvidenceRequirementDescriptor> { Requirement() };
        var questions = new List<IntakeQuestion> { CaseQuestion() };
        var descriptor = new ChecklistDescriptor(requirements, questions);

        var taxCase = Confirmed(NewCase(), descriptor);

        requirements.Add(Requirement("req-late", SpouseId));
        questions.Clear();

        Assert.Single(taxCase.Checklist!.EvidenceRequirements);
        Assert.Single(taxCase.Checklist.Questions);
    }

    // --- Reconfirmation (snapshot replacement + answer clearing) ---

    [Fact]
    public void Reconfirm_ReplacesPriorChecklistEntirely()
    {
        var taxCase = Confirmed(NewCase(), Checklist(requirements: [Requirement("req-old")]));

        taxCase = Confirmed(taxCase, Checklist(requirements: [Requirement("req-new", SpouseId, "Other")]));

        var requirement = Assert.Single(taxCase.Checklist!.EvidenceRequirements);
        Assert.Equal("req-new", requirement.RequirementId);
    }

    [Fact]
    public void Reconfirm_ClearsAnswersForRemovedQuestions_KeepsSurvivors()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions:
        [
            CaseQuestion("q-kept"),
            CaseQuestion("q-removed", "Second prompt?")
        ]));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-kept", null, "yes"));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-removed", null, "no"));

        taxCase = Confirmed(taxCase, Checklist(questions: [CaseQuestion("q-kept")]));

        var answer = Assert.Single(taxCase.Answers);
        Assert.Equal("q-kept", answer.QuestionId);
    }

    [Fact]
    public void Reconfirm_ClearsAnswerForRetargetedQuestion()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions: [ParticipantQuestion("q-1", SpouseId)]));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", SpouseId, "some detail"));

        // Same id, now targeting the primary: the spouse's answer must not survive.
        taxCase = Confirmed(taxCase, Checklist(questions: [ParticipantQuestion("q-1", PrimaryId)]));

        Assert.Empty(taxCase.Answers);
    }

    [Fact]
    public void Reconfirm_ClearsAnswerWhenScopeChangesFromCaseToParticipant()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", null, "yes"));

        taxCase = Confirmed(taxCase, Checklist(questions: [ParticipantQuestion("q-1", SpouseId, "Now targeted?")]));

        Assert.Empty(taxCase.Answers);
    }

    // --- Answering (exact attribution) ---

    [Fact]
    public void AnswerQuestion_CaseLevel_NullAttribution_Succeeds()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", null, "no change"));

        Assert.Equal(new IntakeAnswer("q-1", null, "no change"), Assert.Single(taxCase.Answers));
    }

    [Fact]
    public void AnswerQuestion_ParticipantTargeted_ExactTarget_Succeeds()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions: [ParticipantQuestion("q-1", SpouseId)]));

        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", SpouseId, "about $200"));

        Assert.Equal(new IntakeAnswer("q-1", SpouseId, "about $200"), Assert.Single(taxCase.Answers));
    }

    [Fact]
    public void AnswerQuestion_ReAnswering_ReplacesPriorAnswer()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", null, "first"));

        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", null, "second"));

        var answer = Assert.Single(taxCase.Answers);
        Assert.Equal("second", answer.Answer);
    }

    [Fact]
    public void AnswerQuestion_ReplacementPreservesAnswerOrder()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions:
        [
            CaseQuestion("q-1"),
            CaseQuestion("q-2", "Second prompt?")
        ]));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", null, "first"));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-2", null, "second"));

        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", null, "revised"));

        Assert.Equal(
            new[] { "q-1", "q-2" },
            taxCase.Answers.Select(a => a.QuestionId).ToArray());
        Assert.Equal("revised", taxCase.Answers[0].Answer);
    }

    // --- Change records, immutability, determinism (AC-32/33/42) ---

    [Fact]
    public void ConfirmChecklist_EmitsExactlyOneDeterministicChecklistConfirmedRecord()
    {
        var result = NewCase().ConfirmChecklist(Checklist(), "corr-5");

        Assert.True(result.IsSuccess);
        var expected = new CaseChangeRecord(
            CaseId,
            CaseChangeKind.ChecklistConfirmed,
            ParticipantId: null,
            EvidenceId: null,
            QuestionId: null,
            PreviousStatus: null,
            ResultingStatus: null,
            CorrelationReference: "corr-5",
            Detail: null);

        Assert.Equal(expected, result.Value!.Change);
    }

    [Fact]
    public void AnswerQuestion_EmitsExactlyOneDeterministicQuestionAnsweredRecord()
    {
        var taxCase = Confirmed(NewCase(), Checklist(questions: [ParticipantQuestion("q-1", SpouseId)]));

        var result = taxCase.AnswerQuestion(new IntakeAnswerDescriptor("q-1", SpouseId, "yes"), "corr-6");

        Assert.True(result.IsSuccess);
        var expected = new CaseChangeRecord(
            CaseId,
            CaseChangeKind.QuestionAnswered,
            ParticipantId: SpouseId,
            EvidenceId: null,
            QuestionId: "q-1",
            PreviousStatus: null,
            ResultingStatus: null,
            CorrelationReference: "corr-6",
            Detail: null);

        Assert.Equal(expected, result.Value!.Change);
    }

    [Fact]
    public void ConfirmChecklist_Success_LeavesOriginalCaseUnchanged()
    {
        var original = NewCase();

        var result = original.ConfirmChecklist(Checklist(requirements: [Requirement()]), null);

        Assert.True(result.IsSuccess);
        Assert.NotSame(original, result.Value!.Case);
        Assert.Null(original.Checklist);
    }

    [Fact]
    public void AnswerQuestion_Success_LeavesOriginalCaseUnchanged()
    {
        var original = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-1", null, "yes"), null);

        Assert.True(result.IsSuccess);
        Assert.NotSame(original, result.Value!.Case);
        Assert.Empty(original.Answers);
    }

    [Fact]
    public void ConfirmAndAnswer_SameInputsTwice_YieldEqualStateAndRecords()
    {
        static (TaxCase Case, CaseChangeRecord Confirm, CaseChangeRecord Answer) Run()
        {
            var confirmed = NewCase().ConfirmChecklist(
                Checklist(
                    requirements: [Requirement("req-1", PrimaryId, "T4Slip", 2)],
                    questions: [CaseQuestion("q-1")]),
                "corr-7");
            Assert.True(confirmed.IsSuccess);

            var answered = confirmed.Value!.Case.AnswerQuestion(
                new IntakeAnswerDescriptor("q-1", null, "yes"), "corr-8");
            Assert.True(answered.IsSuccess);

            return (answered.Value!.Case, confirmed.Value.Change, answered.Value.Change);
        }

        var first = Run();
        var second = Run();

        Assert.Equal(first.Confirm, second.Confirm);
        Assert.Equal(first.Answer, second.Answer);
        Assert.Equal(
            first.Case.Checklist!.EvidenceRequirements,
            second.Case.Checklist!.EvidenceRequirements);
        Assert.Equal(first.Case.Checklist.Questions, second.Case.Checklist.Questions);
        Assert.Equal(first.Case.Answers, second.Case.Answers);
    }

    // --- Fail-closed: ConfirmChecklist ---

    [Fact]
    public void ConfirmChecklist_NullDescriptor_FailsClosedWithoutThrowing()
    {
        var original = NewCase();

        var exception = Record.Exception(() =>
            AssertConfirmFailsClosedAndUnchanged(
                original,
                original.ConfirmChecklist(null!, correlationReference: null),
                "Checklist"));

        Assert.Null(exception);
    }

    [Fact]
    public void ConfirmChecklist_NullRequirementList_FailsClosed()
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(new ChecklistDescriptor(null, []), null),
            "EvidenceRequirements");
    }

    [Fact]
    public void ConfirmChecklist_NullQuestionList_FailsClosed()
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(new ChecklistDescriptor([], null), null),
            "Questions");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfirmChecklist_BlankRequirementId_FailsClosed(string? requirementId)
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(Checklist(requirements: [Requirement(requirementId)]), null),
            "RequirementId");
    }

    [Fact]
    public void ConfirmChecklist_DuplicateRequirementId_FailsClosed()
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(
                Checklist(requirements: [Requirement("req-1"), Requirement("req-1", SpouseId, "Other")]),
                null),
            "RequirementId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("p-99")]
    public void ConfirmChecklist_MissingOrNonMemberRequirementParticipant_FailsClosed(string? participantId)
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(Checklist(requirements: [Requirement(participantId: participantId)]), null),
            "ParticipantId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("t4slip")]
    [InlineData("NotACategory")]
    public void ConfirmChecklist_MissingOrUnknownCategory_FailsClosed(string? category)
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(Checklist(requirements: [Requirement(category: category)]), null),
            "Category");
    }

    [Fact]
    public void ConfirmChecklist_SinLikeUnknownCategory_ErrorNamesOnlyRedactedForm()
    {
        var original = NewCase();
        var result = original.ConfirmChecklist(
            Checklist(requirements: [Requirement(category: "123456789")]), null);

        AssertConfirmFailsClosedAndUnchanged(original, result, "Category");
        var error = Assert.Single(result.Errors, e => e.Field == "Category");
        Assert.DoesNotContain("123456789", error.Message);
        Assert.Contains("***-**-789", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ConfirmChecklist_AbsentZeroOrNegativeMinimum_FailsClosed(int? minimumCount)
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(Checklist(requirements: [Requirement(minimumCount: minimumCount)]), null),
            "MinimumCount");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ConfirmChecklist_BlankQuestionId_FailsClosed(string? questionId)
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(
                Checklist(questions: [new IntakeQuestion(questionId!, "Prompt?", IntakeQuestionScope.Case, null)]),
                null),
            "QuestionId");
    }

    [Fact]
    public void ConfirmChecklist_DuplicateQuestionId_FailsClosed()
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(
                Checklist(questions: [CaseQuestion("q-1"), CaseQuestion("q-1", "Different prompt?")]),
                null),
            "QuestionId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void ConfirmChecklist_BlankPrompt_FailsClosed(string? prompt)
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(
                Checklist(questions: [new IntakeQuestion("q-1", prompt!, IntakeQuestionScope.Case, null)]),
                null),
            "Prompt");
    }

    [Fact]
    public void ConfirmChecklist_UndefinedScope_FailsClosed()
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(
                Checklist(questions: [new IntakeQuestion("q-1", "Prompt?", (IntakeQuestionScope)99, null)]),
                null),
            "Scope");
    }

    [Fact]
    public void ConfirmChecklist_CaseScopeWithParticipant_FailsClosed()
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(
                Checklist(questions: [new IntakeQuestion("q-1", "Prompt?", IntakeQuestionScope.Case, PrimaryId)]),
                null),
            "ParticipantId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("p-99")]
    public void ConfirmChecklist_ParticipantScopeWithNullOrNonMemberTarget_FailsClosed(string? participantId)
    {
        var original = NewCase();

        AssertConfirmFailsClosedAndUnchanged(
            original,
            original.ConfirmChecklist(
                Checklist(questions: [new IntakeQuestion("q-1", "Prompt?", IntakeQuestionScope.Participant, participantId)]),
                null),
            "ParticipantId");
    }

    // --- Fail-closed: AnswerQuestion ---

    [Fact]
    public void AnswerQuestion_WhileUnconfigured_FailsClosed()
    {
        var original = NewCase();
        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-1", null, "yes"), null);

        AssertFailsClosed(result, "Checklist");
        Assert.Empty(original.Answers);
        Assert.Null(original.Checklist);
    }

    [Fact]
    public void AnswerQuestion_NullDescriptor_FailsClosedWithoutThrowing()
    {
        var original = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        var exception = Record.Exception(() =>
        {
            var result = original.AnswerQuestion(null!, null);
            AssertFailsClosed(result, "Answer");
            Assert.Empty(original.Answers);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AnswerQuestion_UnknownQuestion_FailsClosed()
    {
        var original = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-99", null, "yes"), null);

        AssertFailsClosed(result, "QuestionId");
        Assert.Empty(original.Answers);
    }

    [Fact]
    public void AnswerQuestion_ParticipantAAnswerToParticipantBTarget_FailsClosed()
    {
        var original = Confirmed(NewCase(), Checklist(questions: [ParticipantQuestion("q-1", SpouseId)]));

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-1", PrimaryId, "yes"), null);

        AssertFailsClosed(result, "ParticipantId");
        Assert.Empty(original.Answers);
    }

    [Fact]
    public void AnswerQuestion_NullAttributionOnParticipantQuestion_FailsClosed()
    {
        var original = Confirmed(NewCase(), Checklist(questions: [ParticipantQuestion("q-1", SpouseId)]));

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-1", null, "yes"), null);

        AssertFailsClosed(result, "ParticipantId");
        Assert.Empty(original.Answers);
    }

    [Fact]
    public void AnswerQuestion_NonNullAttributionOnCaseLevelQuestion_FailsClosed()
    {
        var original = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-1", PrimaryId, "yes"), null);

        AssertFailsClosed(result, "ParticipantId");
        Assert.Empty(original.Answers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnswerQuestion_BlankAnswer_FailsClosed(string? answer)
    {
        var original = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-1", null, answer), null);

        AssertFailsClosed(result, "Answer");
        Assert.Empty(original.Answers);
    }

    [Fact]
    public void AnswerQuestion_Failure_ProducesNoCaseAndNoChangeRecord()
    {
        var original = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-1", null, "  "), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }

    // --- PII (AC-37 intake arm) ---

    [Fact]
    public void PromptAndAnswerWithSin_RemainDomainData_ButAbsentFromJsonAndDiagnostics()
    {
        var prompt = "Is 123456789 your SIN?";
        var answerText = "My SIN is 123-456-789";

        var taxCase = Confirmed(NewCase(), Checklist(questions:
        [
            new IntakeQuestion("q-1", prompt, IntakeQuestionScope.Case, null)
        ]));
        taxCase = Answered(taxCase, new IntakeAnswerDescriptor("q-1", null, answerText));

        // Raw values remain available where the domain contract requires them.
        Assert.Equal(prompt, taxCase.Checklist!.Questions[0].Prompt);
        Assert.Equal(answerText, taxCase.Answers[0].Answer);

        // Defensive JSON contains neither raw pattern.
        var checklistJson = JsonSerializer.Serialize(taxCase.Checklist);
        var answersJson = JsonSerializer.Serialize(taxCase.Answers);
        Assert.DoesNotContain("123456789", checklistJson);
        Assert.DoesNotContain("123-456-789", checklistJson);
        Assert.DoesNotContain("123456789", answersJson);
        Assert.DoesNotContain("123-456-789", answersJson);

        // Public diagnostics contain neither raw pattern.
        foreach (var text in new[]
        {
            taxCase.ToString(),
            taxCase.Checklist.ToString(),
            taxCase.Checklist.Questions[0].ToString(),
            taxCase.Answers[0].ToString()
        })
        {
            Assert.DoesNotContain("123456789", text);
            Assert.DoesNotContain("123-456-789", text);
        }
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    public void ConfirmChecklist_RejectedInputCarryingSinLikeText_NeverEchoesRawSinInErrors(string sinLike)
    {
        // Non-member participant forces failure while SIN-like text rides on every string field.
        var result = NewCase().ConfirmChecklist(
            Checklist(
                requirements: [new EvidenceRequirementDescriptor(sinLike, "p-99", sinLike, null)],
                questions: [new IntakeQuestion(sinLike, sinLike, IntakeQuestionScope.Participant, "p-99")]),
            null);

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("123456789", error.Message);
            Assert.DoesNotContain("123-456-789", error.Message);
        });
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    public void AnswerQuestion_RejectedAnswerCarryingSinLikeText_NeverEchoesRawSinInErrors(string sinLike)
    {
        var confirmed = Confirmed(NewCase(), Checklist(questions: [CaseQuestion("q-1")]));

        // Unknown question id forces failure while the SIN-like text rides on the answer.
        var result = confirmed.AnswerQuestion(new IntakeAnswerDescriptor(sinLike, sinLike, sinLike), null);

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("123456789", error.Message);
            Assert.DoesNotContain("123-456-789", error.Message);
        });
    }
}
