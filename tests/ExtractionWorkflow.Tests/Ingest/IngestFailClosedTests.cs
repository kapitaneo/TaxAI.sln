using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;

namespace ExtractionWorkflow.Tests.Ingest;

/// <summary>
/// Spec 015 T04 — every way untrusted input can go wrong, and the one thing that
/// happens each time: nothing partial is stored, and nothing is thrown to the
/// caller.
/// </summary>
public sealed class IngestFailClosedTests
{
    [Fact]
    public async Task AProviderThatThrowsDuringClassificationStoresNothing()
    {
        var harness = IngestHarness.Create();
        harness.Provider.ThrowOnClassify = true;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Null(result.Value);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task AProviderThatThrowsDuringExtractionStoresNothing()
    {
        var harness = IngestHarness.Create();
        harness.Provider.ThrowOnExtract = true;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task AProviderThatCancelsStoresNothingAndThrowsNothingToTheCaller()
    {
        var harness = IngestHarness.Create();
        harness.Provider.CancelOnExtract = true;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task AProviderThatNeverReturnsIsBoundedByTheConfiguredCallLimit()
    {
        var harness = IngestHarness.CreateWithCallLimit(TimeSpan.FromMilliseconds(50));
        harness.Provider.HangOnExtract = true;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task AnEmptyClassificationResultFailsClosed()
    {
        var harness = IngestHarness.Create();
        harness.Provider.ReturnNoClassification = true;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task AnAbsentOrUndefinedClassificationIsRefusedRatherThanMapped(int? claimed)
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = (DocumentClassification?)claimed;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task AnEmptyT4ResultFailsClosed()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = null;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task ANullBoxCollectionFailsClosedBeforeTheEngineIsReached()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = new ProviderT4Result
        {
            Year = IngestHarness.TaxYear,
            EmployerName = "Acme Manufacturing",
            EmployeeName = "Casey Taxpayer",
            Boxes = null,
        };

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
        Assert.Equal(0, harness.Ingress.SubmitCount);
    }

    [Fact]
    public async Task AnAbsentBoxEntryFailsClosedBeforeTheEngineIsReached()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = new ProviderT4Result
        {
            Year = IngestHarness.TaxYear,
            EmployerName = "Acme Manufacturing",
            EmployeeName = "Casey Taxpayer",
            Boxes = new ProviderBoxValue?[] { null },
        };

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
        Assert.Equal(0, harness.Ingress.SubmitCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABoxWithNoCodeFailsClosedBeforeTheEngineIsReached(string? code)
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = new ProviderT4Result
        {
            Year = IngestHarness.TaxYear,
            EmployerName = "Acme Manufacturing",
            EmployeeName = "Casey Taxpayer",
            Boxes = new ProviderBoxValue?[]
            {
                new() { Code = code, Value = "50000.00" },
            },
        };

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
        Assert.Equal(0, harness.Ingress.SubmitCount);
    }

    [Fact]
    public async Task AnEmptyMedicalResultFailsClosed()
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.MedicalExpenseEvidence;
        harness.Provider.MedicalResult = null;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Store.WriteCount);
    }

    [Fact]
    public async Task ARefusedSecondContentReadLeavesNoPartialPersistence()
    {
        var harness = IngestHarness.Create();
        harness.Content.RefuseOpenCall = 2;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.NotFound, result.Code);
        Assert.True(harness.Provider.ClassificationRequested);
        Assert.False(harness.Provider.T4Requested);
        Assert.Equal(2, harness.Content.OpenCallCount);
        Assert.Equal(0, harness.Store.WriteCount);
        Assert.Empty(harness.Store.Versions);
        Assert.Empty(harness.Store.MedicalVersions);
        Assert.Empty(harness.Store.AuditEvents);
    }

    [Fact]
    public async Task ContentThatDiffersBetweenTheTwoReadsFailsClosed()
    {
        var harness = IngestHarness.Create();
        harness.Content.SecondOpenContent =
            System.Text.Encoding.UTF8.GetBytes("DIFFERENT-SOURCE-CONTENT");

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.True(harness.Provider.ClassificationRequested);
        Assert.False(harness.Provider.T4Requested);
        Assert.Equal(2, harness.Content.OpenCallCount);
        Assert.Equal(0, harness.Store.WriteCount);
        Assert.Empty(harness.Store.Versions);
        Assert.Empty(harness.Store.MedicalVersions);
        Assert.Empty(harness.Store.AuditEvents);
    }

    [Fact]
    public async Task AStoreThatRefusesLeavesNothingClaimedAsRecorded()
    {
        var harness = IngestHarness.Create();
        harness.Store.NextWriteCode = ExtractionStoreWriteCode.Conflict;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Conflict, result.Code);
        Assert.Null(result.Value);
        Assert.Empty(harness.Store.Versions);
    }

    [Fact]
    public async Task AMissingCaseExtractionHistoryIsIndistinguishableFromAMissingCase()
    {
        var harness = IngestHarness.CreateWithForeignExtractionStore();

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.NotFound, result.Code);
        Assert.Equal(0, harness.Content.OpenCallCount);
        Assert.False(harness.Provider.ClassificationRequested);
    }

    // ---------------------------------------------------- argument refusals

    [Fact]
    public async Task AnAbsentCommandIsRefusedBeforeAnythingIsRead()
    {
        var harness = IngestHarness.Create();

        var result = await harness.Workflow.ExtractAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            null!,
            IngestHarness.Instant,
            default);

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Intake.FindCallCount);
    }

    [Fact]
    public async Task AnAbsentActorReferenceIsRefusedBeforeAnythingIsRead()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync(actorReference: Guid.Empty);

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Intake.FindCallCount);
    }

    [Fact]
    public async Task ANonUtcInstantIsRefusedBeforeAnythingIsRead()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync(
            nowUtc: new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.FromHours(-4)));

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Intake.FindCallCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AnAbsentSelectorIsRefusedBeforeAnythingIsRead(
        bool blankCase,
        bool blankDocument)
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync(
            caseId: blankCase ? Guid.Empty : IngestHarness.CaseId,
            documentId: blankDocument ? Guid.Empty : IngestHarness.DocumentId);

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(0, harness.Intake.FindCallCount);
    }

    // ------------------------------------------------------ the options guard

    [Fact]
    public void TheWorkflowRefusesToExistWithoutItsConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => IngestHarness.CreateWithOptions(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TheWorkflowRefusesANonPositiveCallLimit(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IngestHarness.CreateWithOptions(
            new ExtractionWorkflowOptions { ProviderCallLimit = TimeSpan.FromSeconds(seconds) }));
    }

    [Fact]
    public void TheWorkflowRefusesEveryAbsentCollaborator()
    {
        Assert.Equal(8, typeof(ExtractionIngestWorkflow)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Length);

        Assert.All(
            Enumerable.Range(0, 8),
            absent => Assert.ThrowsAny<ArgumentException>(
                () => IngestHarness.CreateWithAbsentCollaborator(absent)));
    }
}
