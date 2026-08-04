using ExtractionWorkflow.Models;
using ExtractionWorkflow.Tests.Ingest;

namespace ExtractionWorkflow.Tests.Review;

public sealed class AcceptanceAndFinalizationTests
{
    [Fact]
    public async Task DocumentAcceptanceNamesExactLatestVersionAndCreatesNoReviewRecord()
    {
        var harness = await ReviewHarness.CreateT4Async();

        var result = await harness.AcceptDocumentAsync();

        Assert.True(result.IsSuccess);
        var stored = await harness.DocumentAsync();
        var acceptance = Assert.Single(stored.Acceptances);
        Assert.Equal(stored.Versions[^1].ExtractionVersionId, acceptance.AcceptedVersionId);
        Assert.Empty(harness.ReviewRecords.Saved);
        Assert.Empty(harness.Accountant.AssociatedRecordIds);
    }

    [Fact]
    public async Task CorrectionSupersedesAnEarlierAcceptance()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();

        var correction = await harness.CorrectAsync(
            CorrectionField.T4BoxValue, "51000.00", "14");
        var finalization = await harness.FinalizeAsync();

        Assert.True(correction.IsSuccess);
        Assert.Equal(
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            correction.Value!.Document.AcceptanceState);
        Assert.Equal(ExtractionResultCode.Invalid, finalization.Code);
        Assert.Empty(harness.ReviewRecords.Saved);
    }

    [Fact]
    public async Task CorrectedLatestVersionMustBeSeparatelyAccepted()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();
        await harness.CorrectAsync(CorrectionField.T4BoxValue, "51000.00", "14");

        var secondAcceptance = await harness.AcceptDocumentAsync();

        Assert.True(secondAcceptance.IsSuccess);
        var stored = await harness.DocumentAsync();
        Assert.Equal(2, stored.Acceptances.Count);
        Assert.Equal(stored.Versions[^1].ExtractionVersionId, stored.Acceptances[^1].AcceptedVersionId);
    }

    [Fact]
    public async Task UnresolvedDuplicateBlocksAcceptanceWithoutDiscardingEitherIdentifier()
    {
        var harness = await ReviewHarness.CreateT4Async();
        var candidateId = await harness.AddDuplicateCandidateAsync();

        var result = await harness.AcceptDocumentAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        var stored = await harness.DocumentAsync();
        Assert.Contains(stored.DuplicateCandidates, item => item.CandidateId == candidateId);
        Assert.Empty(stored.Acceptances);
    }

    [Fact]
    public async Task MedicalRequiredFieldsBlockAcceptanceButOptionalFieldsDoNot()
    {
        var blocked = await ReviewHarness.CreateMedicalAsync();
        blocked.Provider.MedicalResult = IngestHarness.ValidMedicalResult(serviceDate: null);
        await blocked.CorrectAsync(
            CorrectionField.MedicalProviderDisplayName, "Updated provider");

        var blockedResult = await blocked.AcceptDocumentAsync();

        Assert.Equal(ExtractionResultCode.Invalid, blockedResult.Code);

        var allowed = await ReviewHarness.CreateMedicalAsync();
        allowed.Provider.MedicalResult = IngestHarness.ValidMedicalResult(patientName: null);
        await allowed.CorrectAsync(
            CorrectionField.MedicalProviderDisplayName, "Updated provider");

        Assert.True((await allowed.AcceptDocumentAsync()).IsSuccess);
    }

    [Fact]
    public async Task DuplicateResolutionIsExplicitAppendOnlyAndCannotBeRepeated()
    {
        var harness = await ReviewHarness.CreateT4Async();
        var candidateId = await harness.AddDuplicateCandidateAsync();

        var first = await harness.Workflow.ResolveDuplicateAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new ResolveExtractionDuplicateCommand
            {
                CaseId = IngestHarness.CaseId,
                CandidateId = candidateId,
                Resolution = DuplicateResolutionKind.DistinctDocuments,
            },
            IngestHarness.Instant.AddMinutes(2),
            CancellationToken.None);
        var second = await harness.Workflow.ResolveDuplicateAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new ResolveExtractionDuplicateCommand
            {
                CaseId = IngestHarness.CaseId,
                CandidateId = candidateId,
                Resolution = DuplicateResolutionKind.ConfirmedDuplicate,
            },
            IngestHarness.Instant.AddMinutes(3),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(ExtractionResultCode.Conflict, second.Code);
        var stored = await harness.DocumentAsync();
        Assert.Single(stored.DuplicateCandidates);
        Assert.Single(stored.DuplicateResolutions);
        Assert.Equal(IngestHarness.DocumentId, stored.DocumentId);
    }
}
