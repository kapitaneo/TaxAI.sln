using CaseProcessing.Services;
using ClientIntake.Models;
using DocumentExtraction.Ports;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using ExtractionWorkflow.Tests.Ingest;
using TaxCases.Models;
using TaxLogicEngine.Services;
using Tenancy.Models;
using Tenancy.Services;

namespace ExtractionWorkflow.Tests.Review;

public sealed class ReviewRecordCreationTests
{
    [Fact]
    public async Task ExplicitFinalizationSavesExactlyOneVersionedRecordThenAssociatesIt()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();

        var result = await harness.FinalizeAsync();

        Assert.True(result.IsSuccess);
        var record = Assert.Single(harness.ReviewRecords.Saved);
        Assert.Equal(1, record.PackageVersion);
        Assert.Single(record.Slips);
        Assert.Equal(
            new[] { record.RecordId },
            harness.Accountant.AssociatedRecordIds);
        Assert.Equal(record.RecordId, result.Value!.ReviewRecordId);
    }

    [Fact]
    public async Task FinalizationCoversEveryAcceptedT4DocumentExactlyOnce()
    {
        var harness = await MultiReviewHarness.CreateTwoT4Async();
        Assert.True((await harness.AcceptAsync(MultiReviewHarness.FirstDocumentId)).IsSuccess);
        Assert.True((await harness.AcceptAsync(MultiReviewHarness.SecondDocumentId)).IsSuccess);

        var result = await harness.FinalizeAsync();

        Assert.True(result.IsSuccess);
        var record = Assert.Single(harness.ReviewRecords.Saved);
        Assert.Equal(2, record.Slips.Count);
        Assert.Equal(2, result.Value!.AcceptedT4DocumentCount);
        Assert.Single(harness.Accountant.AssociatedRecordIds);
    }

    [Fact]
    public async Task AcceptedMedicalDocumentNeverEntersAT4ReviewRecord()
    {
        var harness = await MultiReviewHarness.CreateT4AndMedicalAsync();
        Assert.True((await harness.AcceptAsync(MultiReviewHarness.FirstDocumentId)).IsSuccess);
        Assert.True((await harness.AcceptAsync(MultiReviewHarness.SecondDocumentId)).IsSuccess);

        var result = await harness.FinalizeAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(Assert.Single(harness.ReviewRecords.Saved).Slips);
        Assert.Equal(1, result.Value!.AcceptedT4DocumentCount);
    }

    [Fact]
    public async Task FinalizationAppendsSeparateCaseAcceptanceAndReviewCreationAudits()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();

        await harness.FinalizeAsync();

        var stored = await harness.CaseAsync();
        Assert.Single(stored.CaseAcceptances);
        Assert.Contains(
            stored.AuditEvents,
            audit => audit.Kind == ExtractionAuditKind.CaseAccepted);
        Assert.Contains(
            stored.AuditEvents,
            audit => audit.Kind == ExtractionAuditKind.ReviewRecordCreated);
    }

    [Fact]
    public async Task SecondFinalizationFailsClosedWithoutAnotherSaveOrAssociation()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();
        Assert.True((await harness.FinalizeAsync()).IsSuccess);

        var second = await harness.FinalizeAsync();

        Assert.Equal(ExtractionResultCode.Conflict, second.Code);
        Assert.Single(harness.ReviewRecords.Saved);
        Assert.Single(harness.Accountant.AssociatedRecordIds);
        Assert.Single((await harness.CaseAsync()).CaseAcceptances);
    }

    [Fact]
    public async Task FinalRecordUsesLatestAcceptedCorrectionNotSupersededVersion()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.CorrectAsync(CorrectionField.T4BoxValue, "51000.00", "14");
        await harness.AcceptDocumentAsync();

        await harness.FinalizeAsync();

        var slip = Assert.Single(Assert.Single(harness.ReviewRecords.Saved).Slips);
        var income = Assert.Single(slip.BoxAmounts, box => box.BoxCode == "14");
        Assert.Equal(51000.00m, income.Amount);
    }

    [Fact]
    public async Task MedicalEvidenceAloneCannotCreateAT4ReviewRecord()
    {
        var harness = await ReviewHarness.CreateMedicalAsync();
        Assert.True((await harness.AcceptDocumentAsync()).IsSuccess);

        var result = await harness.FinalizeAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Empty(harness.ReviewRecords.Saved);
        Assert.Empty(harness.Accountant.AssociatedRecordIds);
        Assert.Empty((await harness.CaseAsync()).CaseAcceptances);
    }

    [Fact]
    public async Task FinalizationFailsClosedWhenReplayNoLongerMatchesAcceptedVersion()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(
            employmentIncome: "99999.99");

        var result = await harness.FinalizeAsync();

        Assert.Equal(ExtractionResultCode.Invalid, result.Code);
        Assert.Empty(harness.ReviewRecords.Saved);
        Assert.Empty(harness.Accountant.AssociatedRecordIds);
        Assert.Empty((await harness.CaseAsync()).CaseAcceptances);
    }

    [Fact]
    public async Task PostFinalizationCorrectionOverrideAndAcceptanceAllRefuse()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();
        await harness.FinalizeAsync();
        var writes = harness.Store.WriteCount;

        var correction = await harness.CorrectAsync(
            CorrectionField.T4BoxValue, "52000.00", "14");
        var acceptance = await harness.AcceptDocumentAsync();
        var classification = await harness.Workflow.OverrideClassificationAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new OverrideExtractionClassificationCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = IngestHarness.DocumentId,
                TargetClassification = DocumentClassification.MedicalExpenseEvidence,
            },
            IngestHarness.Instant.AddMinutes(4),
            CancellationToken.None);

        Assert.Equal(ExtractionResultCode.Conflict, correction.Code);
        Assert.Equal(ExtractionResultCode.Conflict, acceptance.Code);
        Assert.Equal(ExtractionResultCode.Conflict, classification.Code);
        Assert.Equal(writes, harness.Store.WriteCount);
    }
}

internal sealed class MultiReviewHarness
{
    internal static readonly Guid FirstDocumentId =
        new("77777777-7777-7777-7777-777777777777");

    internal static readonly Guid SecondDocumentId =
        new("88888888-8888-8888-8888-888888888888");

    private MultiReviewHarness(
        ExtractionReviewWorkflow workflow,
        RecordingReviewRepository reviewRecords,
        RecordingAccountantIntakeWorkflow accountant)
    {
        Workflow = workflow;
        ReviewRecords = reviewRecords;
        Accountant = accountant;
    }

    private ExtractionReviewWorkflow Workflow { get; }

    internal RecordingReviewRepository ReviewRecords { get; }

    internal RecordingAccountantIntakeWorkflow Accountant { get; }

    internal static Task<MultiReviewHarness> CreateTwoT4Async() =>
        CreateAsync(secondIsMedical: false);

    internal static Task<MultiReviewHarness> CreateT4AndMedicalAsync() =>
        CreateAsync(secondIsMedical: true);

    internal Task<ExtractionResult<DocumentExtractionView>> AcceptAsync(Guid documentId) =>
        Workflow.AcceptDocumentAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new AcceptExtractionDocumentCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = documentId,
            },
            IngestHarness.Instant.AddMinutes(3),
            CancellationToken.None);

    internal Task<ExtractionResult<FinalizedExtractionReview>> FinalizeAsync() =>
        Workflow.FinalizeCaseAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new FinalizeExtractionCaseCommand { CaseId = IngestHarness.CaseId },
            IngestHarness.Instant.AddMinutes(4),
            CancellationToken.None);

    private static async Task<MultiReviewHarness> CreateAsync(bool secondIsMedical)
    {
        var intake = new StubIntakeStore();
        intake.Add(BuildCase());
        var store = new RecordingExtractionStore(IngestHarness.Firm, IngestHarness.CaseId);
        var reader = new MultiContentReader();
        var provider = new MultiDocumentProvider(secondIsMedical);
        var ingress = new RecordingIngress();
        var identifiers = new SequentialIdentifierSource();
        var options = new ExtractionWorkflowOptions
        {
            ProviderCallLimit = TimeSpan.FromSeconds(30),
        };
        var processor = new T4CaseProcessor(new EmbeddedT4LineMapProvider());
        var ingest = new ExtractionIngestWorkflow(
            intake,
            store,
            reader,
            provider,
            ingress,
            processor,
            identifiers,
            options);

        var first = await ingest.ExtractAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new ExtractDocumentCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = FirstDocumentId,
            },
            IngestHarness.Instant,
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await ingest.ExtractAsync(
            IngestHarness.Firm,
            IngestHarness.ActorReference,
            new ExtractDocumentCommand
            {
                CaseId = IngestHarness.CaseId,
                DocumentId = SecondDocumentId,
            },
            IngestHarness.Instant.AddMinutes(1),
            CancellationToken.None);
        Assert.True(second.IsSuccess);

        var records = new RecordingReviewRepository();
        var accountant = new RecordingAccountantIntakeWorkflow();
        var review = new ExtractionReviewWorkflow(
            intake,
            accountant,
            store,
            reader,
            provider,
            ingress,
            processor,
            records,
            identifiers,
            options);

        return new MultiReviewHarness(review, records, accountant);
    }

    private static StoredIntakeCase BuildCase() =>
        new(
            IngestHarness.Firm,
            IngestHarness.CaseId,
            IngestHarness.ParticipantId,
            "Casey Taxpayer",
            IngestHarness.TaxYear,
            CaseStatus.InformationGathering,
            ConcurrencyVersion: 1,
            new[]
            {
                new IntakeExpectedItemRecord(Guid.NewGuid(), DocumentCategory.T4Slip, 0),
                new IntakeExpectedItemRecord(
                    Guid.NewGuid(), DocumentCategory.MedicalExpenseEvidence, 1),
            },
            new IntakeLinkRecord(
                Guid.NewGuid(),
                new IntakeTokenDigest(new byte[32]),
                IngestHarness.Instant,
                IngestHarness.Instant.AddDays(1),
                SpentAtUtc: null,
                RevokedAtUtc: null),
            Array.Empty<IntakeAnswerVersionRecord>(),
            new[]
            {
                Document(FirstDocumentId, "multi-content-1", DocumentCategory.T4Slip),
                Document(
                    SecondDocumentId,
                    "multi-content-2",
                    DocumentCategory.MedicalExpenseEvidence),
            },
            Array.Empty<IntakeUploadAttemptRecord>(),
            Array.Empty<IntakeSubmissionRecord>(),
            ReviewAssociation: null);

    private static IntakeDocumentRecord Document(
        Guid documentId,
        string contentKey,
        DocumentCategory category) =>
        new(
            documentId,
            Guid.NewGuid(),
            category,
            Version: 1,
            contentKey,
            IntakeMediaKind.Pdf,
            ByteCount: 1024,
            new byte[32],
            IngestHarness.Instant);
}

internal sealed class MultiContentReader : IExtractionContentReader
{
    public Task<ExtractionSourceLease?> OpenAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        var expectedKey = documentId == MultiReviewHarness.FirstDocumentId
            ? "multi-content-1"
            : documentId == MultiReviewHarness.SecondDocumentId
                ? "multi-content-2"
                : null;

        if (!firm.Equals(IngestHarness.Firm) ||
            caseId != IngestHarness.CaseId ||
            !string.Equals(expectedKey, contentKey, StringComparison.Ordinal))
        {
            return Task.FromResult<ExtractionSourceLease?>(null);
        }

        var content = System.Text.Encoding.UTF8.GetBytes(
            string.Concat("source-", documentId.ToString("D")));
        return Task.FromResult<ExtractionSourceLease?>(
            ExtractionSourceLease.Create(new MemoryStream(content, writable: false)));
    }
}

internal sealed class MultiDocumentProvider : IDocumentIntelligenceProvider
{
    private readonly bool _secondIsMedical;

    internal MultiDocumentProvider(bool secondIsMedical) =>
        _secondIsMedical = secondIsMedical;

    public async Task<ProviderClassificationResult?> ClassifyAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var documentId = await ReadDocumentIdAsync(source, cancellationToken);
        return new ProviderClassificationResult
        {
            Classification =
                _secondIsMedical && documentId == MultiReviewHarness.SecondDocumentId
                    ? DocumentClassification.MedicalExpenseEvidence
                    : DocumentClassification.T4Slip,
            ProviderLabel = "multi-document-test-provider",
        };
    }

    public async Task<ProviderT4Result?> ExtractT4Async(
        Stream source,
        CancellationToken cancellationToken)
    {
        var documentId = await ReadDocumentIdAsync(source, cancellationToken);
        return documentId == MultiReviewHarness.SecondDocumentId
            ? IngestHarness.ValidT4Result(
                employerName: "Different Employer",
                employmentIncome: "42000.00")
            : IngestHarness.ValidT4Result();
    }

    public async Task<ProviderMedicalResult?> ExtractMedicalAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        await ReadDocumentIdAsync(source, cancellationToken);
        return IngestHarness.ValidMedicalResult();
    }

    private static async Task<Guid> ReadDocumentIdAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            source,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return Guid.Parse(text["source-".Length..]);
    }
}
