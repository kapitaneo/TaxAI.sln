using DocumentExtraction.Models;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using ExtractionWorkflow.Tests.Ingest;

namespace ExtractionWorkflow.Tests.Review;

public sealed class ClassificationOverrideTests
{
    [Theory]
    [InlineData(DocumentClassification.Unsupported)]
    [InlineData((DocumentClassification)999)]
    public async Task OnlyTheTwoSupportedPathsCanBeOverrideTargets(
        DocumentClassification target)
    {
        var harness = await ReviewHarness.CreateT4Async();
        var writesBefore = harness.Store.WriteCount;

        var result = await harness.Workflow.OverrideClassificationAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new OverrideExtractionClassificationCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
                TargetClassification = target,
            },
            IngestHarness.Instant.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(writesBefore, harness.Store.WriteCount);
    }

    [Fact]
    public async Task OverrideRetainsAutomatedClassificationBesideHumanDecision()
    {
        var harness = await ReviewHarness.CreateT4Async();

        var result = await harness.Workflow.OverrideClassificationAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new OverrideExtractionClassificationCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
                TargetClassification = DocumentClassification.MedicalExpenseEvidence,
            },
            IngestHarness.Instant.AddMinutes(1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentClassification.T4Slip, result.Value!.AutomatedClassification);
        Assert.Equal(
            DocumentClassification.MedicalExpenseEvidence,
            result.Value.EffectiveClassification);
        Assert.True(result.Value.IsClassificationOverridden);
    }

    [Fact]
    public async Task OverrideAloneCannotAcceptAnExtractionFromTheOldPath()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.Workflow.OverrideClassificationAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new OverrideExtractionClassificationCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
                TargetClassification = DocumentClassification.MedicalExpenseEvidence,
            },
            IngestHarness.Instant.AddMinutes(1),
            CancellationToken.None);

        var accepted = await harness.AcceptDocumentAsync();

        Assert.Equal(ExtractionResultCode.Invalid, accepted.Code);
        Assert.Empty((await harness.DocumentAsync()).Acceptances);
    }

    [Fact]
    public async Task UnsupportedDocumentCanBeOverriddenButOnlyReentryCreatesTaxInput()
    {
        var harness = await ReviewHarness.CreateUnsupportedAsync();

        var overridden = await harness.Workflow.OverrideClassificationAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new OverrideExtractionClassificationCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
                TargetClassification = DocumentClassification.T4Slip,
            },
            IngestHarness.Instant.AddMinutes(1),
            CancellationToken.None);
        var acceptanceBeforeExtraction = await harness.AcceptDocumentAsync();

        Assert.True(overridden.IsSuccess);
        Assert.Equal(
            DocumentClassification.Unsupported,
            overridden.Value!.AutomatedClassification);
        Assert.Equal(0, overridden.Value.ExtractionVersionCount);
        Assert.Equal(ExtractionResultCode.Invalid, acceptanceBeforeExtraction.Code);

        harness.Provider.T4Result = IngestHarness.ValidT4Result();
        var extracted = await harness.Ingest.ExtractAsync(
            nowUtc: IngestHarness.Instant.AddMinutes(2));
        var accepted = await harness.Workflow.AcceptDocumentAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new AcceptExtractionDocumentCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
            },
            IngestHarness.Instant.AddMinutes(3),
            CancellationToken.None);

        Assert.True(extracted.IsSuccess);
        Assert.True(extracted.Value!.IsEngineAccepted);
        Assert.True(accepted.IsSuccess);
    }

    [Fact]
    public void PolicyRetainsUnsupportedAutomatedOutcomeWithoutTurningItIntoAcceptance()
    {
        var created = ClassificationOverridePolicy.TryCreate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DocumentClassification.Unsupported,
            DocumentClassification.T4Slip,
            Guid.NewGuid(),
            IngestHarness.Instant,
            out var record);

        Assert.True(created);
        Assert.Equal(DocumentClassification.Unsupported, record!.AutomatedClassification);
        Assert.Equal(DocumentClassification.T4Slip, record.OverriddenClassification);
    }

    [Fact]
    public async Task OverrideAppendsOneStructuralAuditAndNoExtractionVersion()
    {
        var harness = await ReviewHarness.CreateT4Async();
        var versionCount = (await harness.DocumentAsync()).Versions.Count;
        var auditCount = harness.Store.AuditEvents.Count;

        await harness.Workflow.OverrideClassificationAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new OverrideExtractionClassificationCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
                TargetClassification = DocumentClassification.MedicalExpenseEvidence,
            },
            IngestHarness.Instant.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(versionCount, (await harness.DocumentAsync()).Versions.Count);
        Assert.Equal(auditCount + 1, harness.Store.AuditEvents.Count);
        Assert.Equal(
            ExtractionAuditKind.ClassificationOverridden,
            harness.Store.AuditEvents[^1].Kind);
    }
}
