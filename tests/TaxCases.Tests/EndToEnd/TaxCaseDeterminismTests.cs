using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.EndToEnd;

/// <summary>
/// Replays identical operation sequences and proves equal observable state,
/// equal readiness, and equal change-record sequences (AC-35, AC-43).
/// </summary>
public sealed class TaxCaseDeterminismTests
{
    private const string CaseId = "case-1";
    private const string FirmA = "firm-a";
    private const string PrimaryId = "p-primary";
    private const string SpouseId = "p-spouse";

    /// <summary>
    /// Runs the scripted sequence — including one rejected operation per
    /// family — collecting the change records of every accepted operation.
    /// </summary>
    private static (TaxCase Case, IReadOnlyList<CaseChangeRecord> Records) Replay()
    {
        var records = new List<CaseChangeRecord>();

        TaxCase Track(ParseResult<CaseOperationOutcome> result)
        {
            Assert.True(result.IsSuccess);
            records.Add(result.Value!.Change);
            return result.Value.Case;
        }

        static void Rejected(ParseResult<CaseOperationOutcome> result)
        {
            Assert.True(result.IsFailure);
            Assert.Null(result.Value);
        }

        var taxCase = Track(TaxCase.Create(new CreateTaxCaseRequest(
            CaseId, FirmA, 2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, "Alex Client", "123456789", null),
            "corr-create")));

        taxCase = Track(taxCase.AddParticipant(
            new ParticipantDescriptor(SpouseId, RelationshipKind.SpouseOrCommonLaw, "Sam Client", null, null),
            "corr-add"));

        // Rejected operations contribute no record and no state.
        Rejected(taxCase.AddParticipant(
            new ParticipantDescriptor(SpouseId, RelationshipKind.Dependant, null, null, null), "corr-dup"));
        Rejected(taxCase.AttachEvidence(
            new EvidenceDescriptor("e-bad", "T4Slip", PrimaryId, 2024, null, null), "corr-bad-year"));

        taxCase = Track(taxCase.ConfirmChecklist(
            new ChecklistDescriptor(
                [
                    new EvidenceRequirementDescriptor("req-t4", PrimaryId, "T4Slip", 2),
                    new EvidenceRequirementDescriptor("req-med", SpouseId, "MedicalExpenseEvidence", 1)
                ],
                [
                    new IntakeQuestion("q-case", "Address change?", IntakeQuestionScope.Case, null),
                    new IntakeQuestion("q-spouse", "Medical total?", IntakeQuestionScope.Participant, SpouseId)
                ]),
            "corr-confirm"));

        taxCase = Track(taxCase.AttachEvidence(
            new EvidenceDescriptor("e-t4-1", "T4Slip", PrimaryId, 2025, "src-1", null), "corr-e1"));
        taxCase = Track(taxCase.AttachEvidence(
            new EvidenceDescriptor("e-t4-2", "T4Slip", PrimaryId, 2025, "src-2", null), "corr-e2"));
        taxCase = Track(taxCase.AttachEvidence(
            new EvidenceDescriptor("e-med", "MedicalExpenseEvidence", SpouseId, 2025, null, null), "corr-e3"));

        taxCase = Track(taxCase.AnswerQuestion(
            new IntakeAnswerDescriptor("q-case", null, "no change"), "corr-a1"));
        taxCase = Track(taxCase.AnswerQuestion(
            new IntakeAnswerDescriptor("q-spouse", SpouseId, "$450"), "corr-a2"));
        taxCase = Track(taxCase.AnswerQuestion(
            new IntakeAnswerDescriptor("q-case", null, "one move in June"), "corr-a3"));

        taxCase = Track(taxCase.TransitionTo(CaseStatus.InformationGathering, "corr-t1"));
        taxCase = Track(taxCase.TransitionTo(CaseStatus.InReview, "corr-t2"));
        taxCase = Track(taxCase.TransitionTo(CaseStatus.Completed, "corr-t3"));

        Rejected(taxCase.TransitionTo(CaseStatus.InformationGathering, "corr-illegal"));

        taxCase = Track(taxCase.Reopen("Late slip arrived.", "corr-reopen"));

        return (taxCase, records);
    }

    [Fact]
    public void IdenticalReplays_ProduceEqualChangeRecordSequences()
    {
        var first = Replay();
        var second = Replay();

        Assert.Equal(first.Records.Count, second.Records.Count);
        Assert.Equal(first.Records, second.Records);
    }

    [Fact]
    public void IdenticalReplays_ProduceEqualObservableState()
    {
        var first = Replay().Case;
        var second = Replay().Case;

        Assert.Equal(first.CaseId, second.CaseId);
        Assert.Equal(first.FirmId, second.FirmId);
        Assert.Equal(first.TaxYear, second.TaxYear);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Participants, second.Participants);
        Assert.Equal(first.Evidence, second.Evidence);
        Assert.Equal(first.Checklist!.EvidenceRequirements, second.Checklist!.EvidenceRequirements);
        Assert.Equal(first.Checklist.Questions, second.Checklist.Questions);
        Assert.Equal(first.Answers, second.Answers);
    }

    [Fact]
    public void IdenticalReplays_ProduceEqualReadiness()
    {
        var first = Replay().Case.Readiness;
        var second = Replay().Case.Readiness;

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Reasons, second.Reasons);
    }

    [Fact]
    public void Replay_ProducesExactlyOneRecordPerAcceptedOperationInOrder()
    {
        var (_, records) = Replay();

        Assert.Equal(
            new[]
            {
                CaseChangeKind.CaseCreated,
                CaseChangeKind.ParticipantAdded,
                CaseChangeKind.ChecklistConfirmed,
                CaseChangeKind.EvidenceAttached,
                CaseChangeKind.EvidenceAttached,
                CaseChangeKind.EvidenceAttached,
                CaseChangeKind.QuestionAnswered,
                CaseChangeKind.QuestionAnswered,
                CaseChangeKind.QuestionAnswered,
                CaseChangeKind.StatusChanged,
                CaseChangeKind.StatusChanged,
                CaseChangeKind.StatusChanged,
                CaseChangeKind.CaseReopened
            },
            records.Select(record => record.Kind).ToArray());

        Assert.All(records, record => Assert.Equal(CaseId, record.CaseId));
    }

    [Fact]
    public void ReplayedCase_DerivesReadinessFromStateAfterReopen()
    {
        var (taxCase, _) = Replay();

        Assert.Equal(CaseStatus.InformationGathering, taxCase.Status);
        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
    }
}
