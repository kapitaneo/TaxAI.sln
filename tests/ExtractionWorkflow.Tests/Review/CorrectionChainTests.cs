using CaseProcessing.Services;
using ClientIntake.Contracts;
using ClientIntake.Models;
using DocumentExtraction.Ports;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using ExtractionWorkflow.Tests.Ingest;
using TaxCases.Models;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;
using Tenancy.Contracts;
using Tenancy.Models;

namespace ExtractionWorkflow.Tests.Review;

public sealed class CorrectionChainTests
{
    [Fact]
    public async Task CorrectionRereadsSourceAndRerunsTheExistingGateAndEngine()
    {
        var harness = await ReviewHarness.CreateT4Async();
        var opensBefore = harness.Content.OpenCallCount;
        var submissionsBefore = harness.Ingress.SubmitCount;

        var result = await harness.CorrectAsync(
            CorrectionField.T4BoxValue, "51000.00", boxCode: "14");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsEngineAccepted);
        Assert.Equal(opensBefore + 1, harness.Content.OpenCallCount);
        Assert.Equal(submissionsBefore + 1, harness.Ingress.SubmitCount);

        var stored = await harness.DocumentAsync();
        Assert.Equal(2, stored.Versions.Count);
        Assert.Equal(stored.Versions[0].ExtractionVersionId, stored.Versions[1].PrecedingVersionId);
        Assert.Equal(
            "50000.00",
            stored.Versions[0].BoxValues.Single(box => box.Code == "14").Value);
        Assert.Equal("51000.00", stored.Versions[1].BoxValues.Single(box => box.Code == "14").Value);
    }

    [Fact]
    public async Task CorrectionAppendsDerivedPriorValueAndNeverAcceptsItself()
    {
        var harness = await ReviewHarness.CreateT4Async();

        await harness.CorrectAsync(
            CorrectionField.T4BoxValue, "51000.00", boxCode: "14");

        var stored = await harness.DocumentAsync();
        var correction = Assert.Single(stored.Corrections);
        Assert.Equal("50000.00", correction.PriorValue);
        Assert.Equal("51000.00", correction.CorrectedValue);
        Assert.Equal(stored.Versions[0].ExtractionVersionId, correction.PrecedingVersionId);
        Assert.Empty(stored.Acceptances);
    }

    [Theory]
    [InlineData("12", "999999999", CorrectionField.T4BoxValue)]
    [InlineData("14", "12", CorrectionField.T4BoxCode)]
    public async Task EveryBoxTwelveCorrectionChannelRefusesWithoutMutation(
        string boxCode,
        string correctedValue,
        CorrectionField field)
    {
        var harness = await ReviewHarness.CreateT4Async();
        var writesBefore = harness.Store.WriteCount;
        var opensBefore = harness.Content.OpenCallCount;

        var result = await harness.CorrectAsync(field, correctedValue, boxCode);

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(writesBefore, harness.Store.WriteCount);
        Assert.Equal(opensBefore, harness.Content.OpenCallCount);
        var stored = await harness.DocumentAsync();
        Assert.Single(stored.Versions);
        Assert.Empty(stored.Corrections);
    }

    [Fact]
    public async Task UndefinedCorrectionFieldRefusesWithoutMutationOrVersion()
    {
        var harness = await ReviewHarness.CreateT4Async();
        var writesBefore = harness.Store.WriteCount;

        var result = await harness.CorrectAsync(
            (CorrectionField)999, "value", boxCode: null);

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Equal(writesBefore, harness.Store.WriteCount);
        Assert.Single((await harness.DocumentAsync()).Versions);
    }

    [Fact]
    public async Task EngineInvalidCorrectionCreatesAwaitingVersionButCannotBeAccepted()
    {
        var harness = await ReviewHarness.CreateT4Async();

        var correction = await harness.CorrectAsync(
            CorrectionField.EmployeeDisplayName, correctedValue: null);
        var acceptance = await harness.AcceptDocumentAsync();

        Assert.True(correction.IsSuccess);
        Assert.False(correction.Value!.IsEngineAccepted);
        Assert.NotEmpty(correction.Value.EngineErrors);
        Assert.Equal(ExtractionResultCode.Invalid, acceptance.Code);
        var stored = await harness.DocumentAsync();
        Assert.Equal(2, stored.Versions.Count);
        Assert.Empty(stored.Acceptances);
    }

    [Fact]
    public async Task MedicalCorrectionRereadsAndAppendsOnlyMedicalEvidence()
    {
        var harness = await ReviewHarness.CreateMedicalAsync();
        var opensBefore = harness.Content.OpenCallCount;

        var result = await harness.CorrectAsync(
            CorrectionField.MedicalGrossAmount, "$1,220.50");

        Assert.True(result.IsSuccess);
        Assert.Equal(opensBefore + 1, harness.Content.OpenCallCount);
        var stored = await harness.DocumentAsync();
        Assert.Empty(stored.Versions);
        Assert.Equal(2, stored.MedicalEvidenceVersions.Count);
        Assert.Equal("$1,220.50", stored.MedicalEvidenceVersions[1].GrossAmountAsPrinted);
    }
}

internal sealed class ReviewHarness
{
    private ReviewHarness(
        IngestHarness ingest,
        RecordingIngress ingress,
        RecordingReviewRepository reviewRecords,
        RecordingAccountantIntakeWorkflow accountant,
        ExtractionReviewWorkflow workflow)
    {
        Ingest = ingest;
        Ingress = ingress;
        ReviewRecords = reviewRecords;
        Accountant = accountant;
        Workflow = workflow;
    }

    internal IngestHarness Ingest { get; }
    internal StubIntakeStore Intake => Ingest.Intake;
    internal RecordingExtractionStore Store => Ingest.Store;
    internal StubContentReader Content => Ingest.Content;
    internal StubDocumentIntelligenceProvider Provider => Ingest.Provider;
    internal RecordingIngress Ingress { get; }
    internal RecordingReviewRepository ReviewRecords { get; }
    internal RecordingAccountantIntakeWorkflow Accountant { get; }
    internal ExtractionReviewWorkflow Workflow { get; }

    internal static async Task<ReviewHarness> CreateT4Async()
    {
        var ingest = IngestHarness.Create();
        Assert.True((await ingest.ExtractAsync()).IsSuccess);
        return Create(ingest);
    }

    internal static async Task<ReviewHarness> CreateMedicalAsync()
    {
        var ingest = IngestHarness.Create();
        ingest.Provider.Classification = DocumentClassification.MedicalExpenseEvidence;
        Assert.True((await ingest.ExtractAsync()).IsSuccess);
        return Create(ingest);
    }

    internal static async Task<ReviewHarness> CreateUnsupportedAsync()
    {
        var ingest = IngestHarness.Create();
        ingest.Provider.Classification = DocumentClassification.Unsupported;
        var outcome = await ingest.ExtractAsync();
        Assert.True(outcome.IsSuccess);
        Assert.Equal(ExtractionRecordedKind.NothingRecorded, outcome.Value!.Recorded);
        return Create(ingest);
    }

    private static ReviewHarness Create(IngestHarness ingest)
    {
        var ingress = new RecordingIngress();
        var records = new RecordingReviewRepository();
        var accountant = new RecordingAccountantIntakeWorkflow();
        var workflow = new ExtractionReviewWorkflow(
            ingest.Intake,
            accountant,
            ingest.Store,
            ingest.Content,
            ingest.Provider,
            ingress,
            new T4CaseProcessor(new EmbeddedT4LineMapProvider()),
            records,
            new SequentialIdentifierSource(),
            new ExtractionWorkflowOptions { ProviderCallLimit = TimeSpan.FromSeconds(30) });
        return new ReviewHarness(ingest, ingress, records, accountant, workflow);
    }

    internal Task<ExtractionResult<DocumentExtractionOutcome>> CorrectAsync(
        CorrectionField field,
        string? correctedValue,
        string? boxCode = null) =>
        Workflow.CorrectAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new ApplyExtractionCorrectionCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
                Field = field,
                BoxCode = boxCode,
                CorrectedValue = correctedValue,
            },
            IngestHarness.Instant.AddMinutes(1),
            CancellationToken.None);

    internal Task<ExtractionResult<DocumentExtractionView>> AcceptDocumentAsync() =>
        Workflow.AcceptDocumentAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new AcceptExtractionDocumentCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
            },
            IngestHarness.Instant.AddMinutes(2),
            CancellationToken.None);

    internal Task<ExtractionResult<FinalizedExtractionReview>> FinalizeAsync() =>
        Workflow.FinalizeCaseAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new FinalizeExtractionCaseCommand { CaseId = IngestHarness.CaseId },
            IngestHarness.Instant.AddMinutes(3),
            CancellationToken.None);

    internal async Task<StoredDocumentExtraction> DocumentAsync() =>
        (await Store.FindDocumentAsync(
            IngestHarness.Firm,
            IngestHarness.CaseId,
            IngestHarness.DocumentId,
            CancellationToken.None))!;

    internal async Task<StoredCaseExtractions> CaseAsync() =>
        (await Store.FindCaseAsync(
            IngestHarness.Firm,
            IngestHarness.CaseId,
            CancellationToken.None))!;

    internal async Task<Guid> AddDuplicateCandidateAsync()
    {
        var candidateId = Guid.NewGuid();
        var extractions = await CaseAsync();
        var audit = new ExtractionAuditEventRecord(
            Guid.NewGuid(),
            extractions.AuditEvents.Max(item => item.Sequence) + 1,
            ExtractionAuditKind.DuplicateFlagged,
            ExtractionActorKind.Accountant,
            IngestHarness.ActorReference,
            IngestHarness.Instant.AddMinutes(1));
        await Store.AppendDuplicateCandidateAsync(
            IngestHarness.Firm,
            IngestHarness.CaseId,
            new DuplicateCandidateRecord(
                candidateId,
                IngestHarness.DocumentId,
                Guid.NewGuid(),
                IngestHarness.Instant.AddMinutes(1)),
            Tenancy.Services.RetentionPolicy.ComputeRetentionUntilUtc(IngestHarness.TaxYear),
            audit,
            CancellationToken.None);
        return candidateId;
    }
}

internal sealed class RecordingReviewRepository : IReviewRecordRepository
{
    internal List<PersistedReviewRecord> Saved { get; } = new();

    public Task<ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm,
        PersistedReviewRecord record,
        CancellationToken cancellationToken)
    {
        Saved.Add(record);
        return Task.FromResult(ParseResult<PersistedReviewRecord>.Success(record));
    }

    public Task<PersistedReviewRecord?> FindAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken) =>
        Task.FromResult<PersistedReviewRecord?>(
            Saved.FirstOrDefault(record => record.RecordId == recordId));
}

internal sealed class RecordingAccountantIntakeWorkflow : IAccountantIntakeWorkflow
{
    internal List<Guid> AssociatedRecordIds { get; } = new();

    public Task<IntakeResult<AccountantIntakeView>> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid recordId,
        Guid accountantActorReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        AssociatedRecordIds.Add(recordId);
        return Task.FromResult(
            IntakeResult<AccountantIntakeView>.Success(
                new AccountantIntakeView(
                    "Casey Taxpayer",
                    IngestHarness.TaxYear,
                    CaseStatus.InformationGathering,
                    ReadinessState.Ready,
                    1,
                    0,
                    Array.Empty<ExpectedDocumentView>(),
                    0,
                    0,
                    null,
                    false,
                    IntakeLinkState.Active,
                    IngestHarness.Instant,
                    IngestHarness.Instant.AddDays(1),
                    null,
                    null,
                    ReviewHandoffState.Available,
                    recordId)));
    }

    public Task<IntakeResult<CreatedIntakeCase>> CreateAsync(
        TrustedFirmId f, Guid a, CreateIntakeCaseCommand c, DateTimeOffset n, CancellationToken t) =>
        throw new NotSupportedException();

    public Task<IntakeResult<AccountantIntakeView>> GetProgressAsync(
        TrustedFirmId f, Guid c, DateTimeOffset n, CancellationToken t) =>
        throw new NotSupportedException();

    public Task<IntakeResult<IssuedIntakeLink>> ReissueLinkAsync(
        TrustedFirmId f, Guid c, Guid a, DateTimeOffset n, CancellationToken t) =>
        throw new NotSupportedException();

    public Task<IntakeResult<AccountantIntakeView>> RevokeLinkAsync(
        TrustedFirmId f, Guid c, Guid a, DateTimeOffset n, CancellationToken t) =>
        throw new NotSupportedException();
}
