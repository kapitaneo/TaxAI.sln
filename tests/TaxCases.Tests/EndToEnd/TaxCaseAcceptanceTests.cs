using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.EndToEnd;

/// <summary>
/// Exercises the finished module only through its public API: the full family
/// case (AC-2), the readiness-to-review-to-completion path, accepted record
/// shapes (AC-32), rejected-operation atomicity (AC-33/42), participant
/// separation, and firm/year isolation (AC-39).
/// </summary>
public sealed class TaxCaseAcceptanceTests
{
    private const string CaseId = "case-1";
    private const string FirmA = "firm-a";
    private const string PrimaryId = "p-primary";
    private const string SpouseId = "p-spouse";
    private const string DependantId = "p-dep-1";
    private const string SecondDependantId = "p-dep-2";

    private static TaxCase Ok(ParseResult<CaseOperationOutcome> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        return result.Value!.Case;
    }

    /// <summary>Builds the AC-2 full family case through public operations only.</summary>
    private static TaxCase FullFamilyCase()
    {
        var taxCase = Ok(TaxCase.Create(new CreateTaxCaseRequest(
            CaseId,
            FirmA,
            2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, "Alex Client", "123456789", FirmA),
            "corr-create")));

        taxCase = Ok(taxCase.AddParticipant(
            new ParticipantDescriptor(SpouseId, RelationshipKind.SpouseOrCommonLaw, "Sam Client", null, null), null));
        taxCase = Ok(taxCase.AddParticipant(
            new ParticipantDescriptor(DependantId, RelationshipKind.Dependant, "Dep One", null, null), null));
        taxCase = Ok(taxCase.AddParticipant(
            new ParticipantDescriptor(SecondDependantId, RelationshipKind.Dependant, "Dep Two", null, null), null));

        taxCase = Ok(taxCase.ConfirmChecklist(
            new ChecklistDescriptor(
                [
                    new EvidenceRequirementDescriptor("req-primary-t4", PrimaryId, "T4Slip", 1),
                    new EvidenceRequirementDescriptor("req-spouse-med", SpouseId, "MedicalExpenseEvidence", 1)
                ],
                [
                    new IntakeQuestion("q-case", "Any address change?", IntakeQuestionScope.Case, null),
                    new IntakeQuestion("q-spouse", "Spouse medical total?", IntakeQuestionScope.Participant, SpouseId)
                ]),
            null));

        taxCase = Ok(taxCase.AttachEvidence(
            new EvidenceDescriptor("e-t4", "T4Slip", PrimaryId, 2025, "upload-1", null), null));
        taxCase = Ok(taxCase.AttachEvidence(
            new EvidenceDescriptor("e-med", "MedicalExpenseEvidence", SpouseId, 2025, "upload-2", null), null));

        taxCase = Ok(taxCase.AnswerQuestion(new IntakeAnswerDescriptor("q-case", null, "no change"), null));
        taxCase = Ok(taxCase.AnswerQuestion(new IntakeAnswerDescriptor("q-spouse", SpouseId, "$450"), null));

        return taxCase;
    }

    // --- AC-2: full valid case ---

    [Fact]
    public void FullFamilyCase_AllStatePresentAndCorrectlyAttributed()
    {
        var taxCase = FullFamilyCase();

        Assert.Equal(FirmA, taxCase.FirmId);
        Assert.Equal(2025, taxCase.TaxYear);
        Assert.Equal(4, taxCase.Participants.Count);
        Assert.Equal(RelationshipKind.PrimaryTaxpayer,
            Assert.Single(taxCase.Participants, p => p.ParticipantId == PrimaryId).Relationship);
        Assert.Equal(RelationshipKind.SpouseOrCommonLaw,
            Assert.Single(taxCase.Participants, p => p.ParticipantId == SpouseId).Relationship);
        Assert.Equal(2, taxCase.Participants.Count(p => p.Relationship == RelationshipKind.Dependant));

        var t4 = Assert.Single(taxCase.Evidence, e => e.EvidenceId == "e-t4");
        Assert.Equal(PrimaryId, t4.ParticipantId);
        Assert.Equal(DocumentCategory.T4Slip, t4.Category);
        Assert.Equal(2025, t4.TaxYear);
        Assert.Equal("upload-1", t4.SourceReference);

        var medical = Assert.Single(taxCase.Evidence, e => e.EvidenceId == "e-med");
        Assert.Equal(SpouseId, medical.ParticipantId);
        Assert.Equal(DocumentCategory.MedicalExpenseEvidence, medical.Category);

        Assert.Equal(2, taxCase.Answers.Count);
        Assert.Null(Assert.Single(taxCase.Answers, a => a.QuestionId == "q-case").ParticipantId);
        Assert.Equal(SpouseId, Assert.Single(taxCase.Answers, a => a.QuestionId == "q-spouse").ParticipantId);
    }

    [Fact]
    public void FullFamilyCase_TravelsReadinessToReviewToCompletionAndReopen()
    {
        var taxCase = FullFamilyCase();
        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);

        taxCase = Ok(taxCase.TransitionTo(CaseStatus.InformationGathering, null));
        taxCase = Ok(taxCase.TransitionTo(CaseStatus.InReview, null));
        taxCase = Ok(taxCase.TransitionTo(CaseStatus.Completed, null));
        Assert.Equal(CaseStatus.Completed, taxCase.Status);

        taxCase = Ok(taxCase.Reopen("Late slip arrived.", null));
        Assert.Equal(CaseStatus.InformationGathering, taxCase.Status);
        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
    }

    // --- AC-32: every accepted change kind identifies its case/kind/subject ---

    [Fact]
    public void AcceptedOperations_ProduceEveryChangeKindWithIdentifyingFields()
    {
        var create = TaxCase.Create(new CreateTaxCaseRequest(
            CaseId, FirmA, 2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            "corr-1"));
        Assert.True(create.IsSuccess);
        Assert.Equal(CaseChangeKind.CaseCreated, create.Value!.Change.Kind);
        Assert.Equal(CaseId, create.Value.Change.CaseId);

        var added = create.Value.Case.AddParticipant(
            new ParticipantDescriptor(SpouseId, RelationshipKind.SpouseOrCommonLaw, null, null, null), "corr-2");
        Assert.True(added.IsSuccess);
        Assert.Equal(CaseChangeKind.ParticipantAdded, added.Value!.Change.Kind);
        Assert.Equal(SpouseId, added.Value.Change.ParticipantId);

        var confirmed = added.Value.Case.ConfirmChecklist(
            new ChecklistDescriptor([], [new IntakeQuestion("q-1", "Anything else?", IntakeQuestionScope.Case, null)]),
            "corr-3");
        Assert.True(confirmed.IsSuccess);
        Assert.Equal(CaseChangeKind.ChecklistConfirmed, confirmed.Value!.Change.Kind);

        var attached = confirmed.Value.Case.AttachEvidence(
            new EvidenceDescriptor("e-1", "T4Slip", PrimaryId, 2025, null, null), "corr-4");
        Assert.True(attached.IsSuccess);
        Assert.Equal(CaseChangeKind.EvidenceAttached, attached.Value!.Change.Kind);
        Assert.Equal("e-1", attached.Value.Change.EvidenceId);
        Assert.Equal(PrimaryId, attached.Value.Change.ParticipantId);

        var answered = attached.Value.Case.AnswerQuestion(
            new IntakeAnswerDescriptor("q-1", null, "no"), "corr-5");
        Assert.True(answered.IsSuccess);
        Assert.Equal(CaseChangeKind.QuestionAnswered, answered.Value!.Change.Kind);
        Assert.Equal("q-1", answered.Value.Change.QuestionId);

        var moved = answered.Value.Case.TransitionTo(CaseStatus.InformationGathering, "corr-6");
        Assert.True(moved.IsSuccess);
        Assert.Equal(CaseChangeKind.StatusChanged, moved.Value!.Change.Kind);
        Assert.Equal(CaseStatus.Intake, moved.Value.Change.PreviousStatus);
        Assert.Equal(CaseStatus.InformationGathering, moved.Value.Change.ResultingStatus);

        var inReview = moved.Value.Case.TransitionTo(CaseStatus.InReview, null);
        Assert.True(inReview.IsSuccess);
        var completed = inReview.Value!.Case.TransitionTo(CaseStatus.Completed, null);
        Assert.True(completed.IsSuccess);

        var reopened = completed.Value!.Case.Reopen("New document expected.", "corr-7");
        Assert.True(reopened.IsSuccess);
        Assert.Equal(CaseChangeKind.CaseReopened, reopened.Value!.Change.Kind);
        Assert.Equal("New document expected.", reopened.Value.Change.Detail);
        Assert.Equal("corr-7", reopened.Value.Change.CorrelationReference);
        Assert.Equal(CaseStatus.Completed, reopened.Value.Change.PreviousStatus);
        Assert.Equal(CaseStatus.InformationGathering, reopened.Value.Change.ResultingStatus);
    }

    // --- AC-33/42: one rejected input per public operation, full atomicity ---

    [Fact]
    public void Create_Rejected_ProducesNoCaseAndNoRecord()
    {
        var result = TaxCase.Create(new CreateTaxCaseRequest(
            CaseId, "  ", 2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            null));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void AddParticipant_Rejected_LeavesCaseAtomicallyUnchanged()
    {
        var original = FullFamilyCase();

        var result = original.AddParticipant(
            new ParticipantDescriptor(PrimaryId, RelationshipKind.Dependant, null, null, null), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(4, original.Participants.Count);
    }

    [Fact]
    public void AttachEvidence_Rejected_LeavesCaseAtomicallyUnchanged()
    {
        var original = FullFamilyCase();

        var result = original.AttachEvidence(
            new EvidenceDescriptor("e-2024", "T4Slip", PrimaryId, 2024, null, null), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(2, original.Evidence.Count);
        Assert.Equal(2025, original.TaxYear);
    }

    [Fact]
    public void ConfirmChecklist_Rejected_LeavesCaseAtomicallyUnchanged()
    {
        var original = FullFamilyCase();

        var result = original.ConfirmChecklist(
            new ChecklistDescriptor(
                [new EvidenceRequirementDescriptor("req-x", PrimaryId, "T4Slip", 0)],
                []),
            null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(2, original.Checklist!.EvidenceRequirements.Count);
        Assert.Equal(2, original.Answers.Count);
    }

    [Fact]
    public void AnswerQuestion_Rejected_LeavesCaseAtomicallyUnchanged()
    {
        var original = FullFamilyCase();

        var result = original.AnswerQuestion(new IntakeAnswerDescriptor("q-unknown", null, "yes"), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(2, original.Answers.Count);
    }

    [Fact]
    public void TransitionTo_Rejected_LeavesStatusUnchanged()
    {
        var original = FullFamilyCase();

        var result = original.TransitionTo(CaseStatus.Completed, null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(CaseStatus.Intake, original.Status);
    }

    [Fact]
    public void Reopen_Rejected_LeavesStatusUnchanged()
    {
        var original = FullFamilyCase();

        var result = original.Reopen("Reason.", null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(CaseStatus.Intake, original.Status);
    }

    // --- Participant separation ---

    [Fact]
    public void ParticipantAData_IsNeverReturnedAsParticipantBs()
    {
        var taxCase = FullFamilyCase();

        Assert.DoesNotContain(taxCase.Evidence, e =>
            e.ParticipantId == SpouseId && e.Category == DocumentCategory.T4Slip);
        Assert.DoesNotContain(taxCase.Evidence, e =>
            e.ParticipantId == PrimaryId && e.Category == DocumentCategory.MedicalExpenseEvidence);
        Assert.DoesNotContain(taxCase.Answers, a => a.ParticipantId == PrimaryId);

        var spouseEvidence = taxCase.Evidence.Where(e => e.ParticipantId == SpouseId).ToArray();
        Assert.Equal("e-med", Assert.Single(spouseEvidence).EvidenceId);
    }

    [Fact]
    public void SpouseRequirement_IsNotSatisfiedByPrimaryEvidence()
    {
        var taxCase = Ok(TaxCase.Create(new CreateTaxCaseRequest(
            CaseId, FirmA, 2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            null)));
        taxCase = Ok(taxCase.AddParticipant(
            new ParticipantDescriptor(SpouseId, RelationshipKind.SpouseOrCommonLaw, null, null, null), null));
        taxCase = Ok(taxCase.ConfirmChecklist(
            new ChecklistDescriptor(
                [new EvidenceRequirementDescriptor("req-spouse-t4", SpouseId, "T4Slip", 1)],
                []),
            null));

        taxCase = Ok(taxCase.AttachEvidence(
            new EvidenceDescriptor("e-primary-t4", "T4Slip", PrimaryId, 2025, null, null), null));

        Assert.Equal(ReadinessState.NotReady, taxCase.Readiness.State);
    }

    // --- AC-39: firm and year isolation ---

    [Fact]
    public void CrossFirmInputs_AreRejectedEverywhereAndNeverAppearInState()
    {
        var original = FullFamilyCase();

        var participant = original.AddParticipant(
            new ParticipantDescriptor("p-b", RelationshipKind.Dependant, null, null, "firm-b"), null);
        var evidence = original.AttachEvidence(
            new EvidenceDescriptor("e-b", "T4Slip", PrimaryId, 2025, null, "firm-b"), null);
        var create = TaxCase.Create(new CreateTaxCaseRequest(
            "case-b", FirmA, 2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, "firm-b"),
            null));

        Assert.True(participant.IsFailure);
        Assert.True(evidence.IsFailure);
        Assert.True(create.IsFailure);

        Assert.Equal(FirmA, original.FirmId);
        Assert.DoesNotContain(original.Participants, p => p.ParticipantId == "p-b");
        Assert.DoesNotContain(original.Evidence, e => e.EvidenceId == "e-b");
    }

    [Fact]
    public void A2025Case_NeverAdoptsAnotherYearsEvidenceOrRules()
    {
        var original = FullFamilyCase();

        var evidence2024 = original.AttachEvidence(
            new EvidenceDescriptor("e-2024", "T4Slip", PrimaryId, 2024, null, null), null);
        var create2024 = TaxCase.Create(new CreateTaxCaseRequest(
            "case-2024", FirmA, 2024,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            null));

        Assert.True(evidence2024.IsFailure);
        Assert.True(create2024.IsFailure);
        Assert.Null(create2024.Value);

        Assert.Equal(2025, original.TaxYear);
        Assert.All(original.Evidence, e => Assert.Equal(2025, e.TaxYear));
    }

    [Fact]
    public void FirmOwnership_IsInheritedAndHasNoTransferSurface()
    {
        var taxCase = FullFamilyCase();

        Assert.Equal(FirmA, taxCase.FirmId);

        // No public member can change the owning firm (AC-13 arm at the API level).
        Assert.Null(typeof(TaxCase).GetProperty(nameof(TaxCase.FirmId))!.SetMethod);
        Assert.DoesNotContain(
            typeof(TaxCase).GetMethods(),
            m => m.Name.Contains("Firm", StringComparison.Ordinal) && m.Name != "get_FirmId");
    }
}
