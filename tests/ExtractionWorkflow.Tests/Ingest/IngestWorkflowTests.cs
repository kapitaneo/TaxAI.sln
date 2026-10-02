using CaseProcessing.Services;
using ClientIntake.Contracts;
using ClientIntake.Models;
using DocumentExtraction.Models;
using DocumentExtraction.Ports;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using TaxCases.Models;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace ExtractionWorkflow.Tests.Ingest;

/// <summary>
/// Spec 015 T04 — the journey one stored document takes, end to end, through the
/// existing chain.
/// <para>
/// The chain is real here: the engine's own parser, mapper, and 2025 embedded
/// box→line config all run, reached through the existing
/// <c>T4CaseProcessor</c>. Only the four boundaries this module declares ports
/// for — the intake store, the extraction store, the content reader, and the
/// document-intelligence provider — are doubles, and each is a faithful
/// in-memory implementation rather than a mock that asserts call shapes.
/// </para>
/// <para>
/// This file also owns the shared harness the sibling suites use.
/// </para>
/// </summary>
public sealed class IngestWorkflowTests
{
    [Fact]
    public async Task AValidEmployeeCopyReachesTheEngineAndIsRecordedAsOneImmutableVersion()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);

        var outcome = result.Value!;
        Assert.Equal(ExtractionRecordedKind.T4ExtractionRecorded, outcome.Recorded);
        Assert.True(outcome.IsEngineAccepted);
        Assert.Empty(outcome.EngineErrors);

        Assert.Equal(DocumentClassification.T4Slip, outcome.Document.AutomatedClassification);
        Assert.Equal(DocumentClassification.T4Slip, outcome.Document.EffectiveClassification);
        Assert.False(outcome.Document.IsClassificationOverridden);
        Assert.Equal(1, outcome.Document.ExtractionVersionCount);
        Assert.Equal(1, outcome.Document.LatestVersion);

        // Nothing a machine produced arrives accepted.
        Assert.Equal(
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            outcome.Document.AcceptanceState);

        var stored = Assert.Single(harness.Store.Versions);
        Assert.Equal(IngestHarness.DocumentId, stored.DocumentId);
        Assert.Equal(1, stored.Version);
        Assert.Null(stored.PrecedingVersionId);
        Assert.Equal(IngestHarness.Instant, stored.RecordedAtUtc);
    }

    [Fact]
    public async Task TheEngineProducesTheSlipAndTheMappingRatherThanThisModule()
    {
        // The whole point of the module: it calls the existing chain, and the
        // chain is what decides. If the mapping provider were bypassed, the
        // processor would never have been reached and no version would exist.
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.True(result.Value!.IsEngineAccepted);

        // And the same payload, handed straight to the engine, agrees.
        var processor = new T4CaseProcessor(new EmbeddedT4LineMapProvider());
        var direct = ExtractionPayloadBuilder.Build(
            IngestHarness.ValidT4Result(),
            payload => processor.Process(
                ExtractionEnvelopeValidatorProbe.Validate(payload, IngestHarness.TaxYear)));

        Assert.True(direct.IsSuccess);
        Assert.NotEmpty(direct.Value!.Mapping.Lines);
    }

    [Fact]
    public async Task TheStoredVersionKeepsEveryNonSinBoxExactlyAsExtracted()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();

        var stored = Assert.Single(harness.Store.Versions);

        // Box 12 is absent because the type cannot represent it; the rest are
        // present, in order, exactly as the document showed them.
        Assert.Equal(
            new[] { ("14", "50000.00"), ("45", "1") },
            stored.BoxValues.Select(box => (box.Code, box.Value!)).ToArray());
    }

    [Fact]
    public async Task AMissingBoxFiftyFourIsNeitherRequiredNorInvented()
    {
        var harness = IngestHarness.Create();

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);

        var stored = Assert.Single(harness.Store.Versions);

        // No module added, defaulted, or synthesized the payroll account number.
        Assert.DoesNotContain(stored.BoxValues, box => box.Code == "54");
    }

    [Fact]
    public async Task AMissingAddressReachesReviewAndIsNotSubstituted()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(
            employerAddress: null, employeeAddress: "   ");

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.True(result.Value!.IsEngineAccepted);

        var stored = Assert.Single(harness.Store.Versions);
        Assert.Null(stored.EmployerAddress);
        Assert.Equal("   ", stored.EmployeeAddress);
    }

    [Fact]
    public async Task ARerunAppendsASecondVersionAndLeavesTheFirstByteUnchanged()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();
        var first = Assert.Single(harness.Store.Versions);
        var firstId = first.ExtractionVersionId;
        var firstBoxCount = first.BoxValues.Count;

        var second = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, second.Code);
        Assert.Equal(2, harness.Store.Versions.Count);

        var earlier = harness.Store.Versions[0];
        Assert.Same(first, earlier);
        Assert.Equal(firstId, earlier.ExtractionVersionId);
        Assert.Equal(1, earlier.Version);
        Assert.Equal(firstBoxCount, earlier.BoxValues.Count);

        var later = harness.Store.Versions[1];
        Assert.Equal(2, later.Version);
        Assert.Equal(firstId, later.PrecedingVersionId);
    }

    [Fact]
    public async Task TheYearComesFromTheStoredCaseAndFromNowhereElse()
    {
        var harness = IngestHarness.Create();

        // Every channel a request could carry a year on is represented by the
        // one thing the command can hold — and it holds no year at all.
        Assert.DoesNotContain(
            typeof(ExtractDocumentCommand).GetProperties(),
            property => property.Name.Contains("Year", StringComparison.OrdinalIgnoreCase));

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.Equal(IngestHarness.TaxYear, harness.ObservedEnvelopeYear);
    }

    [Fact]
    public async Task ADocumentYearThatDisagreesWithTheCaseFailsClosedThroughTheExistingGate()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(year: IngestHarness.TaxYear - 1);

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);

        var outcome = result.Value!;
        Assert.False(outcome.IsEngineAccepted);

        // The existing validator's own coherence error, verbatim.
        Assert.Contains(
            outcome.EngineErrors,
            error => error.Field == ExtractionFields.PayloadYear);

        // Neither year was coerced: the version records what the document showed
        // being refused, and no processed extraction exists.
        Assert.Equal(
            ExtractionRecordedKind.T4ExtractionRecorded,
            outcome.Recorded);
        Assert.All(
            Assert.Single(harness.Store.Versions).FieldOutcomes,
            fieldOutcome => Assert.Equal(FieldOutcomeKind.EngineRejected, fieldOutcome.Kind));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task AnAdjacentCaseYearFailsClosedRatherThanFallingBack(int offset)
    {
        var harness = IngestHarness.Create(taxYear: IngestHarness.TaxYear + offset);

        // The document still shows the case's year, so the only thing wrong is
        // that the year itself is unsupported.
        harness.Provider.T4Result =
            IngestHarness.ValidT4Result(year: IngestHarness.TaxYear + offset);

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.False(result.Value!.IsEngineAccepted);
        Assert.Contains(
            result.Value.EngineErrors,
            error => error.Field == ExtractionFields.TaxYear);
    }

    [Fact]
    public async Task AMissingBoxFortyFiveStillFailsThroughTheEngine()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(includeDentalBenefits: false);

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.False(result.Value!.IsEngineAccepted);

        // The engine's own message, not a re-worded one.
        Assert.Contains(
            result.Value.EngineErrors,
            error => error.Field == "Box 45");
    }

    [Fact]
    public async Task DuplicateBoxCodesReachTheParserSoItsOwnRejectionFires()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(duplicateEmploymentIncome: true);

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.False(result.Value!.IsEngineAccepted);

        // Nothing collapsed, deduplicated, reordered, or dropped the box: the
        // parser saw both and refused.
        Assert.Contains(
            result.Value.EngineErrors,
            error => error.Field == "Box 14" &&
                     error.Message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryEngineErrorBecomesExactlyOneFieldOutcomeInOrder()
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(
            includeSocialInsuranceNumber: false,
            includeDentalBenefits: false);

        var result = await harness.ExtractAsync();

        var errors = result.Value!.EngineErrors;
        var outcomes = Assert.Single(harness.Store.Versions).FieldOutcomes;

        Assert.NotEmpty(errors);
        Assert.Equal(errors.Count, outcomes.Count);
        Assert.Equal(
            errors.Select(error => error.Field).ToArray(),
            outcomes.Select(outcome => outcome.Field).ToArray());
        Assert.All(
            outcomes,
            outcome => Assert.Equal(FieldOutcomeKind.EngineRejected, outcome.Kind));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ConfidenceZeroAndOneFollowTheSamePath(int confidence)
    {
        var harness = IngestHarness.Create();
        harness.Provider.Confidence = confidence;
        harness.Provider.T4Result = IngestHarness.ValidT4Result(confidence: confidence);

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.True(result.Value!.IsEngineAccepted);
        Assert.Equal(ExtractionRecordedKind.T4ExtractionRecorded, result.Value.Recorded);

        // Transported, never judged, and never a gate.
        Assert.Equal(confidence, Assert.Single(harness.Store.Versions).Provenance!.Confidence);
        Assert.Equal(
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            result.Value.Document.AcceptanceState);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public async Task AnOutOfRangeConfidenceFailsClosedThroughTheExistingValidator(double confidence)
    {
        var harness = IngestHarness.Create();
        harness.Provider.T4Result = IngestHarness.ValidT4Result(confidence: (decimal)confidence);

        var result = await harness.ExtractAsync();

        Assert.False(result.Value!.IsEngineAccepted);
        Assert.Contains(
            result.Value.EngineErrors,
            error => error.Field == ExtractionFields.Confidence);
    }

    [Fact]
    public async Task AnUnrecognizedDocumentProducesOneAuditEventAndNothingElse()
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.Unsupported;

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.Equal(ExtractionRecordedKind.NothingRecorded, result.Value!.Recorded);

        Assert.Empty(harness.Store.Versions);
        Assert.Empty(harness.Store.MedicalVersions);
        Assert.Empty(harness.Store.DuplicateCandidates);

        var auditEvent = Assert.Single(harness.Store.AuditEvents);
        Assert.Equal(ExtractionAuditKind.ExtractionRefused, auditEvent.Kind);

        // No payload, no envelope, and therefore no processed extraction.
        Assert.False(harness.Provider.T4Requested);
        Assert.False(harness.Provider.MedicalRequested);
        Assert.Null(harness.ObservedEnvelopeYear);
    }

    [Fact]
    public async Task AStoredOverrideSelectsThePathAndTheAutomatedValueIsRetained()
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.Unsupported;
        harness.Store.AddOverride(new ClassificationOverrideRecord(
            Guid.NewGuid(),
            IngestHarness.DocumentId,
            DocumentClassification.Unsupported,
            DocumentClassification.T4Slip,
            ExtractionActorKind.Accountant,
            Guid.NewGuid(),
            IngestHarness.Instant));

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.Equal(ExtractionRecordedKind.T4ExtractionRecorded, result.Value!.Recorded);

        var stored = Assert.Single(harness.Store.Versions);
        Assert.Equal(
            DocumentClassification.Unsupported,
            stored.Classification.AutomatedClassification);
        Assert.Equal(
            DocumentClassification.T4Slip,
            stored.Classification.EffectiveClassification);
        Assert.True(stored.Classification.IsOverridden);
    }

    [Fact]
    public async Task TheSameDocumentCaseInstantAndProviderProduceEqualResults()
    {
        var first = await IngestHarness.Create().ExtractAsync();
        var second = await IngestHarness.Create().ExtractAsync();

        Assert.Equal(first.Code, second.Code);
        Assert.Equal(first.Value!.Recorded, second.Value!.Recorded);
        Assert.Equal(first.Value.EngineErrors.Count, second.Value.EngineErrors.Count);
        Assert.Equal(first.Value.Document.ToString(), second.Value.Document.ToString());
        Assert.Equal(first.Value.ToString(), second.Value.ToString());
    }

    [Fact]
    public async Task EveryStoredRowCarriesTheOneExistingRetentionBoundary()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();

        Assert.All(
            harness.Store.RetentionBoundaries,
            boundary => Assert.Equal(
                RetentionPolicy.ComputeRetentionUntilUtc(IngestHarness.TaxYear),
                boundary));
    }

    [Fact]
    public async Task AnAuditEventCarriesOnlyStructuralFacts()
    {
        var harness = IngestHarness.Create();

        await harness.ExtractAsync();

        var auditEvent = Assert.Single(harness.Store.AuditEvents);
        Assert.Equal(ExtractionAuditKind.ExtractionRecorded, auditEvent.Kind);
        Assert.Equal(ExtractionActorKind.Accountant, auditEvent.ActorKind);
        Assert.Equal(IngestHarness.ActorReference, auditEvent.ActorReference);
        Assert.Equal(IngestHarness.Instant, auditEvent.OccurredAtUtc);
        Assert.Equal(1, auditEvent.Sequence);
    }

}

// ============================================================== the harness

/// <summary>
/// The shared in-memory harness for every T04 ingest suite.
/// <para>
/// It doubles exactly the four ports this module declares and nothing else. The
/// engine, the envelope gate, the payload builder, the projection, the duplicate
/// detector, and the medical validator are all the real ones, because the thing
/// worth proving is what happens when they run.
/// </para>
/// </summary>
internal sealed class IngestHarness
{
    internal const int TaxYear = 2025;
    internal const string PlantedSin = "123456789";
    internal const string SourceMarker = "MARKER-9F2C-SOURCE-BYTES";

    internal static readonly DateTimeOffset Instant =
        new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    internal static readonly TrustedFirmId Firm =
        TrustedFirmId.FromGuid(new Guid("11111111-1111-1111-1111-111111111111"));

    internal static readonly TrustedFirmId OtherFirm =
        TrustedFirmId.FromGuid(new Guid("22222222-2222-2222-2222-222222222222"));

    internal static readonly Guid CaseId = new("33333333-3333-3333-3333-333333333333");
    internal static readonly Guid DocumentId = new("44444444-4444-4444-4444-444444444444");
    internal static readonly Guid ParticipantId = new("55555555-5555-5555-5555-555555555555");
    internal static readonly Guid ActorReference = new("66666666-6666-6666-6666-666666666666");

    internal const string ContentKey = "content-key-for-the-stored-source";

    private IngestHarness(
        StubIntakeStore intake,
        RecordingExtractionStore store,
        StubContentReader content,
        StubDocumentIntelligenceProvider provider,
        RecordingIngress ingress,
        ExtractionIngestWorkflow workflow)
    {
        Intake = intake;
        Store = store;
        Content = content;
        Provider = provider;
        Ingress = ingress;
        Workflow = workflow;
    }

    internal StubIntakeStore Intake { get; }

    internal RecordingExtractionStore Store { get; }

    internal StubContentReader Content { get; }

    internal StubDocumentIntelligenceProvider Provider { get; }

    internal RecordingIngress Ingress { get; }

    internal ExtractionIngestWorkflow Workflow { get; }

    internal int? ObservedEnvelopeYear => Ingress.LastTaxYear;

    internal static IngestHarness Create(
        int taxYear = TaxYear,
        string? contentKey = null,
        IReadOnlyList<byte>? sourceBytes = null,
        TimeSpan? providerCallLimit = null,
        TrustedFirmId? storeFirm = null)
    {
        var key = contentKey ?? ContentKey;
        var intake = new StubIntakeStore();
        intake.Add(StoredCase(Firm, CaseId, taxYear, DocumentId, key));

        var store = new RecordingExtractionStore(storeFirm ?? Firm, CaseId);
        var content = new StubContentReader(Firm, CaseId, DocumentId, key, sourceBytes);
        var provider = new StubDocumentIntelligenceProvider
        {
            Classification = DocumentClassification.T4Slip,
            T4Result = ValidT4Result(year: taxYear),
            MedicalResult = ValidMedicalResult(),
        };
        var ingress = new RecordingIngress();

        var workflow = new ExtractionIngestWorkflow(
            intake,
            store,
            content,
            provider,
            ingress,
            new T4CaseProcessor(new EmbeddedT4LineMapProvider()),
            new SequentialIdentifierSource(),
            new ExtractionWorkflowOptions
            {
                ProviderCallLimit = providerCallLimit ?? TimeSpan.FromSeconds(30),
            });

        return new IngestHarness(intake, store, content, provider, ingress, workflow);
    }

    internal static IngestHarness CreateWithCallLimit(TimeSpan callLimit) =>
        Create(providerCallLimit: callLimit);

    /// <summary>
    /// A stored case whose content key the reader does not recognize. The read
    /// fails closed rather than returning bytes on the strength of the other
    /// three parts of the scope.
    /// </summary>
    internal static IngestHarness CreateWithMismatchedContentKey()
    {
        var intake = new StubIntakeStore();
        intake.Add(StoredCase(Firm, CaseId, TaxYear, DocumentId, "a-key-the-reader-never-issued"));

        var store = new RecordingExtractionStore(Firm, CaseId);
        var content = new StubContentReader(Firm, CaseId, DocumentId, ContentKey, content: null);
        var provider = new StubDocumentIntelligenceProvider
        {
            Classification = DocumentClassification.T4Slip,
            T4Result = ValidT4Result(),
        };
        var ingress = new RecordingIngress();

        return new IngestHarness(
            intake,
            store,
            content,
            provider,
            ingress,
            new ExtractionIngestWorkflow(
                intake,
                store,
                content,
                provider,
                ingress,
                new T4CaseProcessor(new EmbeddedT4LineMapProvider()),
                new SequentialIdentifierSource(),
                new ExtractionWorkflowOptions
                {
                    ProviderCallLimit = TimeSpan.FromSeconds(30),
                }));
    }

    /// <summary>
    /// An extraction store that belongs to another firm, so every read through
    /// it is absent — the answer a case that does not exist produces.
    /// </summary>
    internal static IngestHarness CreateWithForeignExtractionStore() =>
        Create(storeFirm: OtherFirm);

    internal static ExtractionIngestWorkflow CreateWithOptions(ExtractionWorkflowOptions options) =>
        new(
            new StubIntakeStore(),
            new RecordingExtractionStore(Firm, CaseId),
            new StubContentReader(Firm, CaseId, DocumentId, ContentKey, content: null),
            new StubDocumentIntelligenceProvider(),
            new RecordingIngress(),
            new T4CaseProcessor(new EmbeddedT4LineMapProvider()),
            new SequentialIdentifierSource(),
            options);

    /// <summary>
    /// Builds the workflow with exactly one collaborator absent, so the guard on
    /// each constructor argument is exercised individually.
    /// </summary>
    internal static ExtractionIngestWorkflow CreateWithAbsentCollaborator(int absent) =>
        new(
            absent == 0 ? null! : new StubIntakeStore(),
            absent == 1 ? null! : new RecordingExtractionStore(Firm, CaseId),
            absent == 2
                ? null!
                : new StubContentReader(Firm, CaseId, DocumentId, ContentKey, content: null),
            absent == 3 ? null! : new StubDocumentIntelligenceProvider(),
            absent == 4 ? null! : new RecordingIngress(),
            absent == 5 ? null! : new T4CaseProcessor(new EmbeddedT4LineMapProvider()),
            absent == 6 ? null! : new SequentialIdentifierSource(),
            absent == 7
                ? null!
                : new ExtractionWorkflowOptions { ProviderCallLimit = TimeSpan.FromSeconds(30) });

    internal Task<ExtractionResult<DocumentExtractionOutcome>> ExtractAsync(
        TrustedFirmId? firm = null,
        Guid? caseId = null,
        Guid? documentId = null,
        Guid? actorReference = null,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default) =>
        Workflow.ExtractAsync(
            firm ?? Firm,
            actorReference ?? ActorReference,
            new ExtractDocumentCommand
            {
                CaseId = caseId ?? CaseId,
                DocumentId = documentId ?? DocumentId,
            },
            nowUtc ?? Instant,
            cancellationToken);

    internal static StoredIntakeCase StoredCase(
        TrustedFirmId firm,
        Guid caseId,
        int taxYear,
        Guid documentId,
        string contentKey) =>
        new(
            firm,
            caseId,
            ParticipantId,
            "Casey Taxpayer",
            taxYear,
            CaseStatus.InformationGathering,
            ConcurrencyVersion: 1,
            new[] { new IntakeExpectedItemRecord(Guid.NewGuid(), DocumentCategory.T4Slip, 0) },
            new IntakeLinkRecord(
                Guid.NewGuid(),
                new IntakeTokenDigest(new byte[32]),
                Instant,
                Instant.AddDays(1),
                SpentAtUtc: null,
                RevokedAtUtc: null),
            Array.Empty<IntakeAnswerVersionRecord>(),
            new[]
            {
                new IntakeDocumentRecord(
                    documentId,
                    Guid.NewGuid(),
                    DocumentCategory.T4Slip,
                    Version: 1,
                    contentKey,
                    IntakeMediaKind.Pdf,
                    ByteCount: 1024,
                    new byte[32],
                    Instant),
            },
            Array.Empty<IntakeUploadAttemptRecord>(),
            Array.Empty<IntakeSubmissionRecord>(),
            ReviewAssociation: null);

    /// <summary>
    /// A legitimate 2025 employee copy: Box 12, Box 45, both display names, and
    /// income boxes — and deliberately <b>no Box 54</b>, exactly as the CRA
    /// instructs employers to issue it.
    /// </summary>
    internal static ProviderT4Result ValidT4Result(
        int? year = TaxYear,
        string? employerName = "Acme Manufacturing",
        string? employeeName = "Casey Taxpayer",
        string? employerAddress = "12 Main Street",
        string? employeeAddress = "9 Bay Road",
        string employmentIncome = "50000.00",
        bool includeSocialInsuranceNumber = true,
        bool includeDentalBenefits = true,
        bool includeEmployerAccountNumber = false,
        bool duplicateEmploymentIncome = false,
        decimal? confidence = null)
    {
        var boxes = new List<ProviderBoxValue?>();

        if (includeSocialInsuranceNumber)
        {
            boxes.Add(new ProviderBoxValue
            {
                Code = T4BoxCodes.SocialInsuranceNumber,
                Value = PlantedSin,
            });
        }

        boxes.Add(new ProviderBoxValue { Code = "14", Value = employmentIncome });

        if (duplicateEmploymentIncome)
        {
            boxes.Add(new ProviderBoxValue { Code = "14", Value = employmentIncome });
        }

        if (includeDentalBenefits)
        {
            boxes.Add(new ProviderBoxValue { Code = "45", Value = "1" });
        }

        if (includeEmployerAccountNumber)
        {
            boxes.Add(new ProviderBoxValue { Code = "54", Value = "123456789RP0001" });
        }

        return new ProviderT4Result
        {
            Year = year,
            EmployerName = employerName,
            EmployeeName = employeeName,
            EmployerAddress = employerAddress,
            EmployeeAddress = employeeAddress,
            Boxes = boxes,
            Confidence = confidence,
        };
    }

    internal static ProviderMedicalResult ValidMedicalResult(
        string? serviceDate = "2025-03-14",
        string? grossAmount = "$1,120.50",
        string? currency = "CAD",
        string? providerName = "Bay Street Dental",
        string? patientName = "Casey Taxpayer",
        string? receiptReference = "R-4471",
        decimal? confidence = null) =>
        new()
        {
            ServiceDateAsPrinted = serviceDate,
            GrossAmountAsPrinted = grossAmount,
            CurrencyCodeAsPrinted = currency,
            ProviderDisplayName = providerName,
            PatientDisplayName = patientName,
            ReceiptReference = receiptReference,
            Confidence = confidence,
        };
}

/// <summary>
/// Lets a test run the existing gate over a payload without going through the
/// workflow, so "the engine agrees" can be asserted independently.
/// </summary>
internal static class ExtractionEnvelopeValidatorProbe
{
    internal static ValidatedExtraction? Validate(T4RawInput payload, int taxYear)
    {
        var result = DocumentExtraction.Services.ExtractionEnvelopeValidator.Validate(
            new ExtractionEnvelope
            {
                DocumentId = IngestHarness.DocumentId.ToString("D"),
                Kind = ExtractedDocumentKind.T4Slip,
                FirmId = IngestHarness.Firm.ToCanonicalString(),
                CaseId = IngestHarness.CaseId.ToString("D"),
                ParticipantId = IngestHarness.ParticipantId.ToString("D"),
                TaxYear = taxYear,
                Provenance = new ExtractionProvenance
                {
                    SourceReference = IngestHarness.DocumentId.ToString("D"),
                },
                T4Payload = payload,
            });

        return result.Value;
    }
}

/// <summary>
/// Deterministic identifiers. A workflow that minted its own random values could
/// not be asserted for the properties that matter.
/// </summary>
internal sealed class SequentialIdentifierSource : IOpaqueIdentifierSource
{
    private int _next;

    public Guid NewIdentifier()
    {
        _next++;
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), _next);
        bytes[15] = 0x0A;
        return new Guid(bytes);
    }
}

/// <summary>
/// The intake store, doubled for exactly the one member the ingest workflow
/// calls. Every other member throws, which is the assertion that the workflow
/// reaches for nothing else.
/// </summary>
internal sealed class StubIntakeStore : IIntakeStore
{
    private readonly Dictionary<(Guid Firm, Guid Case), StoredIntakeCase> _cases = new();

    internal int FindCallCount { get; private set; }

    internal void Add(StoredIntakeCase storedCase) =>
        _cases[(storedCase.Firm.Value, storedCase.CaseId)] = storedCase;

    public Task<StoredIntakeCase?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        FindCallCount++;

        return Task.FromResult(
            _cases.TryGetValue((firm.Value, caseId), out var storedCase) ? storedCase : null);
    }

    public Task<IntakeCaseList> ListCasesAsync(TrustedFirmId f, int m, CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> CreateAsync(NewIntakeCase i, CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeLinkScope?> FindLinkScopeAsync(
        IntakeTokenDigest d, DateTimeOffset n, CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> RedeemLinkAsync(
        IntakeLinkScope s, NewIntakeSession n, IntakeAuditEventRecord a, CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeClientScope?> FindClientScopeAsync(
        IntakeTokenDigest d, DateTimeOffset n, CancellationToken c) =>
        throw new NotSupportedException();

    public Task<StoredIntakeCase?> FindForClientAsync(
        IntakeClientScope s, CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> ReissueLinkAsync(
        TrustedFirmId f,
        Guid i,
        long v,
        IntakeLinkRecord r,
        IReadOnlyList<IntakeAuditEventRecord> a,
        CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> RevokeLinkAsync(
        TrustedFirmId f,
        Guid i,
        long v,
        DateTimeOffset r,
        IntakeAuditEventRecord a,
        CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> AppendAnswersAsync(
        IntakeClientScope s,
        long v,
        DateTimeOffset r,
        IReadOnlyList<IntakeAnswerVersionRecord> answers,
        IReadOnlyList<IntakeAuditEventRecord> a,
        CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
        IntakeClientScope s,
        long v,
        DateTimeOffset r,
        IntakeUploadAttemptRecord attempt,
        IntakeDocumentRecord? document,
        IntakeAuditEventRecord a,
        CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> AppendSubmissionAsync(
        IntakeClientScope s,
        long v,
        DateTimeOffset r,
        IntakeSubmissionRecord submission,
        IReadOnlyList<IntakeAuditEventRecord> a,
        CancellationToken c) =>
        throw new NotSupportedException();

    public Task<IntakeStoreWriteCode> AssociateReviewAsync(
        TrustedFirmId f,
        Guid i,
        long v,
        IntakeReviewAssociationRecord association,
        IntakeAuditEventRecord a,
        CancellationToken c) =>
        throw new NotSupportedException();
}

/// <summary>
/// An append-only in-memory extraction store, scoped to exactly one firm and
/// case. A read for any other scope returns null, which is what makes the
/// foreign-firm assertions mean something.
/// </summary>
internal sealed class RecordingExtractionStore : IExtractionStore
{
    private readonly TrustedFirmId _firm;
    private readonly Guid _caseId;
    private readonly List<ExtractionVersion> _versions = new();
    private readonly List<MedicalEvidenceVersion> _medicalVersions = new();
    private readonly List<ExtractionCorrectionRecord> _corrections = new();
    private readonly List<ClassificationOverrideRecord> _overrides = new();
    private readonly List<DuplicateCandidateRecord> _candidates = new();
    private readonly List<DuplicateResolutionRecord> _resolutions = new();
    private readonly List<ExtractionAcceptanceRecord> _acceptances = new();
    private readonly List<ExtractionAuditEventRecord> _auditEvents = new();
    private readonly List<DateTimeOffset> _retentionBoundaries = new();

    internal RecordingExtractionStore(TrustedFirmId firm, Guid caseId)
    {
        _firm = firm;
        _caseId = caseId;
    }

    internal ExtractionStoreWriteCode NextWriteCode { get; set; } =
        ExtractionStoreWriteCode.Success;

    internal IReadOnlyList<ExtractionVersion> Versions => _versions;

    internal IReadOnlyList<MedicalEvidenceVersion> MedicalVersions => _medicalVersions;

    internal IReadOnlyList<DuplicateCandidateRecord> DuplicateCandidates => _candidates;

    internal IReadOnlyList<ExtractionAuditEventRecord> AuditEvents => _auditEvents;

    internal IReadOnlyList<DateTimeOffset> RetentionBoundaries => _retentionBoundaries;

    internal int WriteCount =>
        _versions.Count + _medicalVersions.Count + _candidates.Count + _auditEvents.Count;

    internal void AddOverride(ClassificationOverrideRecord record) => _overrides.Add(record);

    internal void AddVersion(ExtractionVersion version) => _versions.Add(version);

    internal void AddMedicalVersion(MedicalEvidenceVersion version) =>
        _medicalVersions.Add(version);

    public Task<ExtractionStoreWriteCode> AppendExtractionVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        ExtractionVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, auditEvent, retentionUntilUtc, () => _versions.Add(version));

    public Task<ExtractionStoreWriteCode> AppendMedicalEvidenceVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        MedicalEvidenceVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, auditEvent, retentionUntilUtc, () => _medicalVersions.Add(version));

    public Task<ExtractionStoreWriteCode> AppendCorrectionAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionCorrectionRecord correction,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, auditEvent, retentionUntilUtc, () => _corrections.Add(correction));

    public Task<ExtractionStoreWriteCode> AppendClassificationOverrideAsync(
        TrustedFirmId firm,
        Guid caseId,
        ClassificationOverrideRecord classificationOverride,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(
            firm,
            caseId,
            auditEvent,
            retentionUntilUtc,
            () => _overrides.Add(classificationOverride));

    public Task<ExtractionStoreWriteCode> AppendDuplicateCandidateAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateCandidateRecord candidate,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, auditEvent, retentionUntilUtc, () => _candidates.Add(candidate));

    public Task<ExtractionStoreWriteCode> AppendDuplicateResolutionAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateResolutionRecord resolution,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, auditEvent, retentionUntilUtc, () => _resolutions.Add(resolution));

    public Task<ExtractionStoreWriteCode> AppendAcceptanceAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAcceptanceRecord acceptance,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, auditEvent, retentionUntilUtc, () => _acceptances.Add(acceptance));

    public Task<ExtractionStoreWriteCode> AppendAuditEventAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord auditEvent,
        DateTimeOffset retentionUntilUtc,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, auditEvent, retentionUntilUtc, () => { });

    public Task<StoredDocumentExtraction?> FindDocumentAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!InScope(firm, caseId))
        {
            return Task.FromResult<StoredDocumentExtraction?>(null);
        }

        var known = _versions.Any(version => version.DocumentId == documentId) ||
                    _medicalVersions.Any(version => version.DocumentId == documentId) ||
                    _overrides.Any(record => record.DocumentId == documentId) ||
                    _candidates.Any(record => record.DocumentId == documentId);

        return Task.FromResult(known ? Document(documentId) : null);
    }

    public Task<StoredCaseExtractions?> FindCaseAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        if (!InScope(firm, caseId))
        {
            return Task.FromResult<StoredCaseExtractions?>(null);
        }

        var documentIds = _versions.Select(version => version.DocumentId)
            .Concat(_medicalVersions.Select(version => version.DocumentId))
            .Concat(_overrides.Select(record => record.DocumentId))
            .Concat(_candidates.Select(record => record.DocumentId))
            .Distinct()
            .OrderBy(documentId => documentId)
            .ToArray();

        return Task.FromResult<StoredCaseExtractions?>(
            new StoredCaseExtractions(
                caseId,
                documentIds.Select(Document).ToArray(),
                _acceptances
                    .Where(record => record.Scope == ExtractionAcceptanceScope.Case)
                    .ToArray(),
                _auditEvents));
    }

    private StoredDocumentExtraction Document(Guid documentId) =>
        new(
            documentId,
            IngestHarness.ParticipantId,
            _versions.Where(version => version.DocumentId == documentId).ToArray(),
            _medicalVersions.Where(version => version.DocumentId == documentId).ToArray(),
            _corrections.Where(record => record.DocumentId == documentId).ToArray(),
            _overrides.Where(record => record.DocumentId == documentId).ToArray(),
            _candidates.Where(record => record.DocumentId == documentId).ToArray(),
            _resolutions
                .Where(resolution => _candidates.Any(candidate =>
                    candidate.CandidateId == resolution.CandidateId &&
                    candidate.DocumentId == documentId))
                .ToArray(),
            _acceptances.Where(record => record.DocumentId == documentId).ToArray());

    private Task<ExtractionStoreWriteCode> Append(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord auditEvent,
        DateTimeOffset retentionUntilUtc,
        Action append)
    {
        if (!InScope(firm, caseId))
        {
            return Task.FromResult(ExtractionStoreWriteCode.NotFound);
        }

        if (NextWriteCode != ExtractionStoreWriteCode.Success)
        {
            return Task.FromResult(NextWriteCode);
        }

        append();
        _auditEvents.Add(auditEvent);
        _retentionBoundaries.Add(retentionUntilUtc);

        return Task.FromResult(ExtractionStoreWriteCode.Success);
    }

    private bool InScope(TrustedFirmId firm, Guid caseId) =>
        firm.Equals(_firm) && caseId == _caseId;
}

/// <summary>
/// A content reader that honours the full four-part scope and returns nothing
/// for every mismatch, exactly as the port requires.
/// </summary>
internal sealed class StubContentReader : IExtractionContentReader
{
    private readonly TrustedFirmId _firm;
    private readonly Guid _caseId;
    private readonly Guid _documentId;
    private readonly string _contentKey;
    private readonly byte[] _content;

    internal StubContentReader(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey,
        IReadOnlyList<byte>? content)
    {
        _firm = firm;
        _caseId = caseId;
        _documentId = documentId;
        _contentKey = contentKey;
        _content = content?.ToArray()
            ?? System.Text.Encoding.UTF8.GetBytes(IngestHarness.SourceMarker);
    }

    internal int OpenCallCount => OpenCalls.Count;

    internal List<ExtractionSourceLease> Leases { get; } = new();

    internal List<(TrustedFirmId Firm, Guid CaseId, Guid DocumentId, string ContentKey)>
        OpenCalls { get; } = new();

    internal int? RefuseOpenCall { get; set; }

    internal byte[]? SecondOpenContent { get; set; }

    public Task<ExtractionSourceLease?> OpenAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        OpenCalls.Add((firm, caseId, documentId, contentKey));

        if (!firm.Equals(_firm) ||
            caseId != _caseId ||
            documentId != _documentId ||
            !string.Equals(contentKey, _contentKey, StringComparison.Ordinal) ||
            OpenCallCount == RefuseOpenCall)
        {
            return Task.FromResult<ExtractionSourceLease?>(null);
        }

        var source = OpenCallCount == 2 && SecondOpenContent is not null
            ? SecondOpenContent
            : _content;

        var lease = ExtractionSourceLease.Create(new MemoryStream(source, writable: false));
        Leases.Add(lease);

        return Task.FromResult<ExtractionSourceLease?>(lease);
    }
}

/// <summary>
/// A provider stub whose every failure mode the port declares can be switched
/// on. It reads the stream it is handed, so a test can prove the lease was live
/// when the call ran.
/// </summary>
internal sealed class StubDocumentIntelligenceProvider : IDocumentIntelligenceProvider
{
    internal DocumentClassification? Classification { get; set; } =
        DocumentClassification.T4Slip;

    internal decimal? Confidence { get; set; }

    internal string? ProviderLabel { get; set; } = "deterministic-test-adapter";

    internal ProviderT4Result? T4Result { get; set; }

    internal ProviderMedicalResult? MedicalResult { get; set; }

    internal bool ThrowOnClassify { get; set; }

    internal bool ThrowOnExtract { get; set; }

    internal bool CancelOnExtract { get; set; }

    internal bool HangOnExtract { get; set; }

    internal bool ReturnNoClassification { get; set; }

    internal bool ClassificationRequested { get; private set; }

    internal bool T4Requested { get; private set; }

    internal bool MedicalRequested { get; private set; }

    internal int BytesSeen { get; private set; }

    internal void Reset()
    {
        ClassificationRequested = false;
        T4Requested = false;
        MedicalRequested = false;
    }

    public async Task<ProviderClassificationResult?> ClassifyAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ClassificationRequested = true;
        await DrainAsync(source, cancellationToken).ConfigureAwait(false);

        if (ThrowOnClassify)
        {
            throw new InvalidOperationException("The adapter failed.");
        }

        if (ReturnNoClassification)
        {
            return null;
        }

        return new ProviderClassificationResult
        {
            Classification = Classification,
            Confidence = Confidence,
            ProviderLabel = ProviderLabel,
        };
    }

    public async Task<ProviderT4Result?> ExtractT4Async(
        Stream source,
        CancellationToken cancellationToken)
    {
        T4Requested = true;
        await DrainAsync(source, cancellationToken).ConfigureAwait(false);
        await FailIfConfiguredAsync(cancellationToken).ConfigureAwait(false);

        return T4Result;
    }

    public async Task<ProviderMedicalResult?> ExtractMedicalAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        MedicalRequested = true;
        await DrainAsync(source, cancellationToken).ConfigureAwait(false);
        await FailIfConfiguredAsync(cancellationToken).ConfigureAwait(false);

        return MedicalResult;
    }

    private async Task FailIfConfiguredAsync(CancellationToken cancellationToken)
    {
        if (ThrowOnExtract)
        {
            throw new InvalidOperationException("The adapter failed.");
        }

        if (CancelOnExtract)
        {
            throw new OperationCanceledException();
        }

        if (HangOnExtract)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DrainAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        BytesSeen = (int)buffer.Length;
    }
}

/// <summary>
/// The real ingress, wrapped so a test can observe the one thing that matters
/// about the envelope: the year it carried.
/// </summary>
internal sealed class RecordingIngress : IDocumentExtractionIngress
{
    private readonly DocumentExtractionIngress _inner = new();

    internal int? LastTaxYear { get; private set; }

    internal int SubmitCount { get; private set; }

    public Task<ParseResult<ValidatedExtraction>> SubmitAsync(
        ExtractionEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        SubmitCount++;
        LastTaxYear = envelope.TaxYear;

        return _inner.SubmitAsync(envelope, cancellationToken);
    }
}
