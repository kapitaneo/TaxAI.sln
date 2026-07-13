using System.Reflection;
using TaxCases.Models;
using TaxCases.Services;

namespace TaxCases.Tests.Services;

public sealed class ReadinessCalculatorTests
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

    private static TaxCase Confirmed(
        TaxCase taxCase,
        IReadOnlyList<EvidenceRequirementDescriptor>? requirements = null,
        IReadOnlyList<IntakeQuestion>? questions = null)
    {
        var result = taxCase.ConfirmChecklist(
            new ChecklistDescriptor(requirements ?? [], questions ?? []),
            correlationReference: null);

        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static TaxCase WithEvidence(
        TaxCase taxCase,
        string evidenceId,
        string category = "T4Slip",
        string participantId = PrimaryId)
    {
        var result = taxCase.AttachEvidence(
            new EvidenceDescriptor(evidenceId, category, participantId, 2025, null, null),
            correlationReference: null);

        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static TaxCase Answered(TaxCase taxCase, string questionId, string? participantId, string answer)
    {
        var result = taxCase.AnswerQuestion(
            new IntakeAnswerDescriptor(questionId, participantId, answer),
            correlationReference: null);

        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static EvidenceRequirementDescriptor Requirement(
        string requirementId = "req-1",
        string participantId = PrimaryId,
        string category = "T4Slip",
        int minimumCount = 1) =>
        new(requirementId, participantId, category, minimumCount);

    /// <summary>
    /// Rebuilds a case through the private constructor so defensive derivation
    /// can be exercised against states the public operations make unreachable.
    /// </summary>
    private static TaxCase Rebuild(
        TaxCase source,
        IReadOnlyList<CaseParticipant>? participants = null,
        IReadOnlyList<IntakeAnswer>? answers = null)
    {
        var constructor = typeof(TaxCase)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();

        return (TaxCase)constructor.Invoke(
        [
            source.CaseId,
            source.FirmId,
            source.TaxYear,
            source.Status,
            participants ?? source.Participants,
            source.Evidence,
            source.Checklist,
            answers ?? source.Answers
        ]);
    }

    private static void AssertNotReadyWithSingleReason(TaxCase taxCase, ReadinessReasonKind expectedKind)
    {
        var readiness = taxCase.Readiness;

        Assert.Equal(ReadinessState.NotReady, readiness.State);
        var reason = Assert.Single(readiness.Reasons);
        Assert.Equal(expectedKind, reason.Kind);
    }

    // --- Ready derivations (AC-17, AC-22, AC-25) ---

    [Fact]
    public void ConfirmedEmptyChecklist_IsReadyWithNoReasons()
    {
        var readiness = Confirmed(NewCase()).Readiness;

        Assert.Equal(ReadinessState.Ready, readiness.State);
        Assert.Empty(readiness.Reasons);
    }

    [Fact]
    public void LastRequiredEvidence_FlipsDerivedStateToReady()
    {
        var taxCase = Confirmed(NewCase(), requirements: [Requirement()]);
        Assert.Equal(ReadinessState.NotReady, taxCase.Readiness.State);

        taxCase = WithEvidence(taxCase, "t4-1");

        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
        Assert.Empty(taxCase.Readiness.Reasons);
    }

    [Fact]
    public void LastRequiredAnswer_FlipsDerivedStateToReady()
    {
        var taxCase = Confirmed(NewCase(), questions:
        [
            new IntakeQuestion("q-1", "Marital change?", IntakeQuestionScope.Case, null)
        ]);
        Assert.Equal(ReadinessState.NotReady, taxCase.Readiness.State);

        taxCase = Answered(taxCase, "q-1", null, "no");

        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
    }

    [Fact]
    public void SeparatePrimaryAndSpouseT4Requirements_EachSatisfiedOnlyByItsOwnEvidence()
    {
        var taxCase = Confirmed(NewCase(), requirements:
        [
            Requirement("req-primary", PrimaryId),
            Requirement("req-spouse", SpouseId)
        ]);

        taxCase = WithEvidence(taxCase, "t4-primary", participantId: PrimaryId);
        Assert.Equal(ReadinessState.NotReady, taxCase.Readiness.State);

        taxCase = WithEvidence(taxCase, "t4-spouse", participantId: SpouseId);
        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
    }

    [Fact]
    public void TwoT4Minimum_SatisfiedByTwoDistinctT4Identities()
    {
        var taxCase = Confirmed(NewCase(), requirements: [Requirement(minimumCount: 2)]);

        taxCase = WithEvidence(taxCase, "t4-1");
        taxCase = WithEvidence(taxCase, "t4-2");

        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
    }

    [Fact]
    public void RequiredOther_IsSatisfiedByExactlyOtherEvidence()
    {
        var taxCase = Confirmed(NewCase(), requirements: [Requirement(category: "Other")]);

        taxCase = WithEvidence(taxCase, "other-1", "Other");

        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
    }

    [Fact]
    public void Readiness_IsRecomputedOnEachImmutableReplacement()
    {
        var before = Confirmed(NewCase(), requirements: [Requirement()]);

        var after = WithEvidence(before, "t4-1");

        // The original derivation is unchanged; the replacement derives fresh.
        Assert.Equal(ReadinessState.NotReady, before.Readiness.State);
        Assert.Equal(ReadinessState.Ready, after.Readiness.State);
    }

    [Fact]
    public void Calculate_SameCase_YieldsEqualDeterministicResults()
    {
        var taxCase = Confirmed(NewCase(), requirements:
        [
            Requirement("req-1", PrimaryId, "T4Slip", 2),
            Requirement("req-2", SpouseId, "MedicalExpenseEvidence")
        ]);

        var first = ReadinessCalculator.Calculate(taxCase);
        var second = ReadinessCalculator.Calculate(taxCase);

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Reasons, second.Reasons);
    }

    // --- Fail-closed derivations (AC-23, AC-24, AC-26) ---

    [Fact]
    public void UnconfiguredChecklist_IsAlwaysNotReady()
    {
        AssertNotReadyWithSingleReason(NewCase(), ReadinessReasonKind.ChecklistNotConfigured);
    }

    [Fact]
    public void OneParticipantsT4_NeverSatisfiesAnotherParticipantsRequirement()
    {
        var taxCase = Confirmed(NewCase(), requirements: [Requirement("req-spouse", SpouseId)]);

        taxCase = WithEvidence(taxCase, "t4-primary", participantId: PrimaryId);

        AssertNotReadyWithSingleReason(taxCase, ReadinessReasonKind.UnsatisfiedEvidenceRequirement);
    }

    [Fact]
    public void OneParticipantsMedical_NeverSatisfiesAnotherParticipantsMedicalRequirement()
    {
        var taxCase = Confirmed(NewCase(), requirements:
        [
            Requirement("req-spouse-med", SpouseId, "MedicalExpenseEvidence")
        ]);

        taxCase = WithEvidence(taxCase, "med-primary", "MedicalExpenseEvidence", PrimaryId);

        AssertNotReadyWithSingleReason(taxCase, ReadinessReasonKind.UnsatisfiedEvidenceRequirement);
    }

    [Fact]
    public void OneT4_FailsATwoT4Minimum_ReasonNamesRemainingCount()
    {
        var taxCase = Confirmed(NewCase(), requirements: [Requirement(minimumCount: 2)]);

        taxCase = WithEvidence(taxCase, "t4-1");

        var readiness = taxCase.Readiness;
        Assert.Equal(ReadinessState.NotReady, readiness.State);
        var reason = Assert.Single(readiness.Reasons);
        Assert.Equal(ReadinessReasonKind.UnsatisfiedEvidenceRequirement, reason.Kind);
        Assert.Contains("1 more", reason.Detail);
    }

    [Fact]
    public void OneEvidenceRecord_CannotSatisfyTwoSameKeyRequirements()
    {
        var taxCase = Confirmed(NewCase(), requirements:
        [
            Requirement("req-first"),
            Requirement("req-second")
        ]);

        taxCase = WithEvidence(taxCase, "t4-only");

        var readiness = taxCase.Readiness;
        Assert.Equal(ReadinessState.NotReady, readiness.State);

        // Deterministic allocation: the earlier requirement consumed the record.
        var reason = Assert.Single(readiness.Reasons);
        Assert.Equal(ReadinessReasonKind.UnsatisfiedEvidenceRequirement, reason.Kind);
        Assert.Contains("req-second", reason.Detail);
        Assert.DoesNotContain("req-first", reason.Detail);
    }

    [Theory]
    [InlineData("T4Slip")]
    [InlineData("MedicalExpenseEvidence")]
    public void OtherEvidence_NeverSatisfiesT4OrMedicalRequirements(string requiredCategory)
    {
        var taxCase = Confirmed(NewCase(), requirements:
        [
            Requirement(category: requiredCategory)
        ]);

        taxCase = WithEvidence(taxCase, "other-1", "Other");

        AssertNotReadyWithSingleReason(taxCase, ReadinessReasonKind.UnsatisfiedEvidenceRequirement);
    }

    [Fact]
    public void T4Evidence_NeverSatisfiesARequiredOther()
    {
        var taxCase = Confirmed(NewCase(), requirements: [Requirement(category: "Other")]);

        taxCase = WithEvidence(taxCase, "t4-1", "T4Slip");

        AssertNotReadyWithSingleReason(taxCase, ReadinessReasonKind.UnsatisfiedEvidenceRequirement);
    }

    [Fact]
    public void UnansweredQuestion_ForcesNotReady()
    {
        var taxCase = Confirmed(NewCase(), questions:
        [
            new IntakeQuestion("q-1", "Any dependants?", IntakeQuestionScope.Case, null)
        ]);

        AssertNotReadyWithSingleReason(taxCase, ReadinessReasonKind.UnansweredIntakeQuestion);
    }

    [Fact]
    public void MisattributedAnswer_InjectedByReflection_DoesNotSatisfyItsQuestion()
    {
        var taxCase = Confirmed(NewCase(), questions:
        [
            new IntakeQuestion("q-1", "Any medical expenses?", IntakeQuestionScope.Participant, SpouseId)
        ]);

        // The public operations forbid this attribution, so inject it defensively.
        var tampered = Rebuild(taxCase, answers: [new IntakeAnswer("q-1", PrimaryId, "yes")]);

        AssertNotReadyWithSingleReason(tampered, ReadinessReasonKind.UnansweredIntakeQuestion);
    }

    [Fact]
    public void CaseLevelQuestion_WithAttributedAnswerInjectedByReflection_RemainsUnanswered()
    {
        var taxCase = Confirmed(NewCase(), questions:
        [
            new IntakeQuestion("q-1", "Marital change?", IntakeQuestionScope.Case, null)
        ]);

        var tampered = Rebuild(taxCase, answers: [new IntakeAnswer("q-1", PrimaryId, "yes")]);

        AssertNotReadyWithSingleReason(tampered, ReadinessReasonKind.UnansweredIntakeQuestion);
    }

    [Fact]
    public void NoPrimaryTaxpayer_CreatedByReflection_ForcesNotReady()
    {
        var taxCase = Confirmed(NewCase());
        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);

        var spouseOnly = taxCase.Participants
            .Where(participant => participant.Relationship != RelationshipKind.PrimaryTaxpayer)
            .ToArray();
        var tampered = Rebuild(taxCase, participants: spouseOnly);

        AssertNotReadyWithSingleReason(tampered, ReadinessReasonKind.NoPrimaryTaxpayer);
    }

    // --- Reason ordering and content (AC-24) ---

    [Fact]
    public void Reasons_FollowChecklistInsertionOrder_RequirementsThenQuestions()
    {
        var taxCase = Confirmed(
            NewCase(),
            requirements:
            [
                Requirement("req-a", PrimaryId),
                Requirement("req-b", SpouseId, "MedicalExpenseEvidence")
            ],
            questions:
            [
                new IntakeQuestion("q-a", "First?", IntakeQuestionScope.Case, null),
                new IntakeQuestion("q-b", "Second?", IntakeQuestionScope.Participant, SpouseId)
            ]);

        var readiness = taxCase.Readiness;

        Assert.Equal(ReadinessState.NotReady, readiness.State);
        Assert.Equal(4, readiness.Reasons.Count);
        Assert.Contains("req-a", readiness.Reasons[0].Detail);
        Assert.Contains("req-b", readiness.Reasons[1].Detail);
        Assert.Contains("q-a", readiness.Reasons[2].Detail);
        Assert.Contains("q-b", readiness.Reasons[3].Detail);
    }

    [Fact]
    public void MissingEvidenceReason_NamesRequirementParticipantCategoryAndRemainingCount()
    {
        var taxCase = Confirmed(NewCase(), requirements:
        [
            Requirement("req-spouse-t4", SpouseId, "T4Slip", 2)
        ]);

        var reason = Assert.Single(taxCase.Readiness.Reasons);

        Assert.Equal(ReadinessReasonKind.UnsatisfiedEvidenceRequirement, reason.Kind);
        Assert.Contains("req-spouse-t4", reason.Detail);
        Assert.Contains(SpouseId, reason.Detail);
        Assert.Contains("T4Slip", reason.Detail);
        Assert.Contains("2 more", reason.Detail);
    }

    [Fact]
    public void QuestionReason_NamesOnlyTheQuestionId_NeverPromptOrAnswer()
    {
        var taxCase = Confirmed(NewCase(), questions:
        [
            new IntakeQuestion("q-1", "Is your SIN 123456789 and income private?", IntakeQuestionScope.Case, null)
        ]);

        var reason = Assert.Single(taxCase.Readiness.Reasons);

        Assert.Equal(ReadinessReasonKind.UnansweredIntakeQuestion, reason.Kind);
        Assert.Contains("q-1", reason.Detail);
        Assert.DoesNotContain("income", reason.Detail);
        Assert.DoesNotContain("123456789", reason.Detail);
    }

    // --- SIN-safe reason text (AC-24, AC-37 readiness arm) ---

    [Fact]
    public void SinLikeIdentifiers_AppearOnlyRedactedInReasonDetails()
    {
        // A participant whose caller-supplied id is SIN-like, referenced by a
        // requirement and a question whose ids are also SIN-like.
        var created = TaxCase.Create(new CreateTaxCaseRequest(
            CaseId,
            FirmA,
            2025,
            new ParticipantDescriptor("123-456-789", RelationshipKind.PrimaryTaxpayer, null, null, null),
            CorrelationReference: null));
        Assert.True(created.IsSuccess);

        var confirmed = created.Value!.Case.ConfirmChecklist(
            new ChecklistDescriptor(
                [new EvidenceRequirementDescriptor("123456789", "123-456-789", "T4Slip", 1)],
                [new IntakeQuestion("123456789", "Prompt?", IntakeQuestionScope.Participant, "123-456-789")]),
            correlationReference: null);
        Assert.True(confirmed.IsSuccess);

        var readiness = confirmed.Value!.Case.Readiness;

        Assert.Equal(ReadinessState.NotReady, readiness.State);
        Assert.Equal(2, readiness.Reasons.Count);
        Assert.All(readiness.Reasons, reason =>
        {
            Assert.DoesNotContain("123456789", reason.Detail);
            Assert.DoesNotContain("123-456-789", reason.Detail);
            Assert.Contains("***-**-789", reason.Detail);
            Assert.DoesNotContain("123456789", reason.ToString());
            Assert.DoesNotContain("123-456-789", reason.ToString());
        });
    }
}
