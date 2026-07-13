using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.Models;

public sealed class TaxCaseLifecycleTests
{
    private const string CaseId = "case-1";
    private const string FirmA = "firm-a";
    private const string PrimaryId = "p-1";

    private static readonly (CaseStatus From, CaseStatus To)[] AllowedEdges =
    [
        (CaseStatus.Intake, CaseStatus.InformationGathering),
        (CaseStatus.Intake, CaseStatus.Cancelled),
        (CaseStatus.InformationGathering, CaseStatus.InReview),
        (CaseStatus.InformationGathering, CaseStatus.Cancelled),
        (CaseStatus.InReview, CaseStatus.InformationGathering),
        (CaseStatus.InReview, CaseStatus.Completed),
        (CaseStatus.InReview, CaseStatus.Cancelled)
    ];

    private static TaxCase NewCase()
    {
        var created = TaxCase.Create(new CreateTaxCaseRequest(
            CaseId,
            FirmA,
            2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            CorrelationReference: null));

        Assert.True(created.IsSuccess);
        return created.Value!.Case;
    }

    private static TaxCase ConfirmedEmpty(TaxCase taxCase)
    {
        var result = taxCase.ConfirmChecklist(new ChecklistDescriptor([], []), null);
        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static TaxCase Transitioned(TaxCase taxCase, CaseStatus target)
    {
        var result = taxCase.TransitionTo(target, correlationReference: null);
        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    /// <summary>Builds a Ready (confirmed-empty checklist) case in the given status.</summary>
    private static TaxCase CaseIn(CaseStatus status)
    {
        var taxCase = ConfirmedEmpty(NewCase());

        if (status == CaseStatus.Intake)
        {
            return taxCase;
        }

        if (status == CaseStatus.Cancelled)
        {
            return Transitioned(taxCase, CaseStatus.Cancelled);
        }

        taxCase = Transitioned(taxCase, CaseStatus.InformationGathering);

        if (status == CaseStatus.InformationGathering)
        {
            return taxCase;
        }

        taxCase = Transitioned(taxCase, CaseStatus.InReview);

        return status == CaseStatus.InReview
            ? taxCase
            : Transitioned(taxCase, CaseStatus.Completed);
    }

    public static TheoryData<CaseStatus, CaseStatus> ForbiddenPairs()
    {
        var data = new TheoryData<CaseStatus, CaseStatus>();

        foreach (var from in Enum.GetValues<CaseStatus>())
        {
            foreach (var to in Enum.GetValues<CaseStatus>())
            {
                if (!AllowedEdges.Contains((from, to)))
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    // --- Allowed edges (AC-14, AC-27, AC-28) ---

    [Theory]
    [InlineData(CaseStatus.Intake, CaseStatus.InformationGathering)]
    [InlineData(CaseStatus.Intake, CaseStatus.Cancelled)]
    [InlineData(CaseStatus.InformationGathering, CaseStatus.InReview)]
    [InlineData(CaseStatus.InformationGathering, CaseStatus.Cancelled)]
    [InlineData(CaseStatus.InReview, CaseStatus.InformationGathering)]
    [InlineData(CaseStatus.InReview, CaseStatus.Completed)]
    [InlineData(CaseStatus.InReview, CaseStatus.Cancelled)]
    public void TransitionTo_EveryAllowedEdge_SucceedsWithExactStatuses(CaseStatus from, CaseStatus to)
    {
        var original = CaseIn(from);

        var result = original.TransitionTo(to, correlationReference: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(to, result.Value!.Case.Status);
        Assert.Equal(from, original.Status);
        Assert.Equal(CaseChangeKind.StatusChanged, result.Value.Change.Kind);
        Assert.Equal(from, result.Value.Change.PreviousStatus);
        Assert.Equal(to, result.Value.Change.ResultingStatus);
    }

    [Fact]
    public void TransitionTo_ReadyCase_EntersReview()
    {
        var taxCase = CaseIn(CaseStatus.InformationGathering);
        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);

        var result = taxCase.TransitionTo(CaseStatus.InReview, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(CaseStatus.InReview, result.Value!.Case.Status);
    }

    [Fact]
    public void TransitionTo_EmitsExactlyOneDeterministicStatusChangedRecord()
    {
        var result = CaseIn(CaseStatus.Intake).TransitionTo(CaseStatus.InformationGathering, "corr-9");

        Assert.True(result.IsSuccess);
        var expected = new CaseChangeRecord(
            CaseId,
            CaseChangeKind.StatusChanged,
            ParticipantId: null,
            EvidenceId: null,
            QuestionId: null,
            PreviousStatus: CaseStatus.Intake,
            ResultingStatus: CaseStatus.InformationGathering,
            CorrelationReference: "corr-9",
            Detail: null);

        Assert.Equal(expected, result.Value!.Change);
    }

    // --- Reopen (AC-29, AC-30) ---

    private static TaxCase CompletedWithSatisfiedT4Requirement()
    {
        var created = NewCase();

        var confirmed = created.ConfirmChecklist(
            new ChecklistDescriptor(
                [new EvidenceRequirementDescriptor("req-1", PrimaryId, "T4Slip", 1)],
                []),
            null);
        Assert.True(confirmed.IsSuccess);

        var withEvidence = confirmed.Value!.Case.AttachEvidence(
            new EvidenceDescriptor("t4-1", "T4Slip", PrimaryId, 2025, null, null), null);
        Assert.True(withEvidence.IsSuccess);

        var taxCase = Transitioned(withEvidence.Value!.Case, CaseStatus.InformationGathering);
        taxCase = Transitioned(taxCase, CaseStatus.InReview);
        return Transitioned(taxCase, CaseStatus.Completed);
    }

    [Fact]
    public void Reopen_CompletedCase_ReturnsToInformationGatheringWithRecomputedReadiness()
    {
        var completed = CompletedWithSatisfiedT4Requirement();

        var result = completed.Reopen("Client found another slip.", "corr-10");

        Assert.True(result.IsSuccess);
        var reopened = result.Value!.Case;
        Assert.Equal(CaseStatus.InformationGathering, reopened.Status);
        Assert.Equal(CaseStatus.Completed, completed.Status);

        // Readiness is derived fresh from state, never restored from a snapshot.
        Assert.Equal(ReadinessState.Ready, reopened.Readiness.State);

        var expected = new CaseChangeRecord(
            CaseId,
            CaseChangeKind.CaseReopened,
            ParticipantId: null,
            EvidenceId: null,
            QuestionId: null,
            PreviousStatus: CaseStatus.Completed,
            ResultingStatus: CaseStatus.InformationGathering,
            CorrelationReference: "corr-10",
            Detail: "Client found another slip.");

        Assert.Equal(expected, result.Value.Change);
    }

    [Fact]
    public void Reopen_ReopenedCase_AcceptsMutationsAgain()
    {
        var reopened = CompletedWithSatisfiedT4Requirement()
            .Reopen("More evidence expected.", null);
        Assert.True(reopened.IsSuccess);

        var attached = reopened.Value!.Case.AttachEvidence(
            new EvidenceDescriptor("t4-2", "T4Slip", PrimaryId, 2025, null, null), null);

        Assert.True(attached.IsSuccess);
        Assert.Equal(2, attached.Value!.Case.Evidence.Count);
    }

    [Fact]
    public void Reopen_ReasonWithSinLikeText_RetainedAsDomainDataButRedactedInDiagnostics()
    {
        var result = CompletedWithSatisfiedT4Requirement()
            .Reopen("Slip for SIN 123456789 arrived late.", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Slip for SIN 123456789 arrived late.", result.Value!.Change.Detail);

        var diagnostic = result.Value.Change.ToString();
        Assert.DoesNotContain("123456789", diagnostic);
        Assert.Contains("***-**-789", diagnostic);
    }

    // --- Forbidden transitions (AC-15, AC-27, AC-31) ---

    [Theory]
    [MemberData(nameof(ForbiddenPairs))]
    public void TransitionTo_EveryForbiddenPair_FailsClosedAndChangesNothing(CaseStatus from, CaseStatus to)
    {
        var original = CaseIn(from);

        var result = original.TransitionTo(to, correlationReference: null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Status");
        Assert.Equal(from, original.Status);
    }

    [Fact]
    public void TransitionTo_UndefinedTargetStatus_FailsClosed()
    {
        var original = CaseIn(CaseStatus.Intake);

        var result = original.TransitionTo((CaseStatus)99, null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(CaseStatus.Intake, original.Status);
    }

    [Fact]
    public void TransitionTo_NotReadyCase_CannotEnterReview()
    {
        // Unconfigured checklist keeps readiness NotReady while the table edge
        // InformationGathering -> InReview would otherwise allow the move.
        var original = Transitioned(NewCase(), CaseStatus.InformationGathering);
        Assert.Equal(ReadinessState.NotReady, original.Readiness.State);

        var result = original.TransitionTo(CaseStatus.InReview, null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Readiness");
        Assert.Equal(CaseStatus.InformationGathering, original.Status);
    }

    [Fact]
    public void TransitionTo_PlainTransitionOutOfCompleted_FailsClosed()
    {
        var completed = CaseIn(CaseStatus.Completed);

        var result = completed.TransitionTo(CaseStatus.InformationGathering, null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(CaseStatus.Completed, completed.Status);
    }

    // --- Reopen rejections (AC-29, AC-31) ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reopen_NullOrBlankReason_FailsClosed(string? reason)
    {
        var completed = CaseIn(CaseStatus.Completed);

        var result = completed.Reopen(reason, null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Reason");
        Assert.Equal(CaseStatus.Completed, completed.Status);
    }

    [Theory]
    [InlineData(CaseStatus.Intake)]
    [InlineData(CaseStatus.InformationGathering)]
    [InlineData(CaseStatus.InReview)]
    [InlineData(CaseStatus.Cancelled)]
    public void Reopen_OutsideCompleted_FailsClosed(CaseStatus status)
    {
        var original = CaseIn(status);

        var result = original.Reopen("A valid reason.", null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Status");
        Assert.Equal(status, original.Status);
    }

    [Fact]
    public void Reopen_RejectedReasonCarryingSinLikeText_NeverEchoesRawSinInErrors()
    {
        // Reopen against Cancelled fails while a SIN-like reason rides along.
        var result = CaseIn(CaseStatus.Cancelled).Reopen("SIN 123-456-789 update", null);

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("123456789", error.Message);
            Assert.DoesNotContain("123-456-789", error.Message);
        });
    }

    // --- Terminal-state mutation guard (AC-15, AC-31, lifecycle arms of AC-33/42) ---

    public static TheoryData<CaseStatus> TerminalStatuses() =>
        new() { CaseStatus.Completed, CaseStatus.Cancelled };

    [Theory]
    [MemberData(nameof(TerminalStatuses))]
    public void AddParticipant_AgainstTerminalCase_FailsClosedAndChangesNothing(CaseStatus status)
    {
        var original = CaseIn(status);

        var result = original.AddParticipant(
            new ParticipantDescriptor("d-1", RelationshipKind.Dependant, null, null, null), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Status");
        Assert.Single(original.Participants);
    }

    [Theory]
    [MemberData(nameof(TerminalStatuses))]
    public void AttachEvidence_AgainstTerminalCase_FailsClosedAndChangesNothing(CaseStatus status)
    {
        var original = CaseIn(status);

        var result = original.AttachEvidence(
            new EvidenceDescriptor("e-1", "T4Slip", PrimaryId, 2025, null, null), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Status");
        Assert.Empty(original.Evidence);
    }

    [Theory]
    [MemberData(nameof(TerminalStatuses))]
    public void ConfirmChecklist_AgainstTerminalCase_FailsClosedAndChangesNothing(CaseStatus status)
    {
        var original = CaseIn(status);

        var result = original.ConfirmChecklist(
            new ChecklistDescriptor(
                [new EvidenceRequirementDescriptor("req-9", PrimaryId, "T4Slip", 1)],
                []),
            null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Status");
        Assert.Empty(original.Checklist!.EvidenceRequirements);
    }

    [Theory]
    [MemberData(nameof(TerminalStatuses))]
    public void AnswerQuestion_AgainstTerminalCase_FailsClosedAndChangesNothing(CaseStatus status)
    {
        var original = CaseIn(status);

        var result = original.AnswerQuestion(
            new IntakeAnswerDescriptor("q-1", null, "yes"), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Status");
        Assert.Empty(original.Answers);
    }

    // --- Determinism ---

    [Fact]
    public void TransitionAndReopen_SameInputsTwice_YieldEqualRecords()
    {
        var firstTransition = CaseIn(CaseStatus.Intake).TransitionTo(CaseStatus.InformationGathering, "corr-9");
        var secondTransition = CaseIn(CaseStatus.Intake).TransitionTo(CaseStatus.InformationGathering, "corr-9");

        Assert.True(firstTransition.IsSuccess);
        Assert.True(secondTransition.IsSuccess);
        Assert.Equal(firstTransition.Value!.Change, secondTransition.Value!.Change);

        var firstReopen = CompletedWithSatisfiedT4Requirement().Reopen("Same reason.", "corr-10");
        var secondReopen = CompletedWithSatisfiedT4Requirement().Reopen("Same reason.", "corr-10");

        Assert.True(firstReopen.IsSuccess);
        Assert.True(secondReopen.IsSuccess);
        Assert.Equal(firstReopen.Value!.Change, secondReopen.Value!.Change);
    }
}
