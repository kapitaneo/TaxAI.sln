using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxCases.Models;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — the real Development portal composition over HTTP. The SQL
/// adapters are proved together in Persistence.Tests; these tests substitute
/// only those process-external ports and keep the handlers, authorization,
/// antiforgery, workflows, processor, deterministic provider, encrypted source
/// adapter, component projection, and review assembly real.
/// </summary>
[Collection("Extraction network observation")]
public sealed class DocumentExtractionAcceptanceTests
{
    [Fact]
    public async Task HttpJourneyCorrectsResolvesOverridesAcceptsAndFinalizes()
    {
        using var network = new ExtractionNetworkAttemptListener();
        await network.VerifyCaptureAndResetAsync();
        await using var world = ExtractionPortalWorld.Create();
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = world.Intake.FirmOne.CreateAnonymousClient();
        await world.Intake.FirmOne.SignInAsync(accountant);

        using (var scope = world.Intake.FirmOne
                   .Resolve<IServiceScopeFactory>().CreateScope())
        {
            Assert.IsType<ExtractionIngestWorkflow>(
                scope.ServiceProvider.GetRequiredService<IExtractionIngestWorkflow>());
            Assert.IsType<ExtractionReviewWorkflow>(
                scope.ServiceProvider.GetRequiredService<IExtractionReviewWorkflow>());
            Assert.Equal(
                "DeterministicDocumentIntelligenceProvider",
                scope.ServiceProvider
                    .GetRequiredService<IDocumentIntelligenceProvider>()
                    .GetType().Name);
            Assert.Equal(
                "DevelopmentEncryptedIntakeContentStore",
                scope.ServiceProvider.GetRequiredService<IIntakeContentStore>()
                    .GetType().Name);
        }

        var captures = new List<ExtractionHttpCapture>();
        captures.Add(await ExtractionPortalHttp.ExtractAsync(
            accountant, seeded.CaseId, seeded.FirstT4DocumentId));
        captures.Add(await ExtractionPortalHttp.ExtractAsync(
            accountant, seeded.CaseId, seeded.SecondT4DocumentId));
        captures.Add(await ExtractionPortalHttp.ExtractAsync(
            accountant, seeded.CaseId, seeded.MedicalDocumentId));

        var duplicateOwner = (await world.Extractions.FindDocumentAsync(
            seeded.Firm,
            seeded.CaseId,
            seeded.SecondT4DocumentId,
            CancellationToken.None))!;
        var candidate = Assert.Single(duplicateOwner.DuplicateCandidates);
        Assert.Empty(duplicateOwner.DuplicateResolutions);

        captures.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.SecondT4DocumentId,
            $"/intake-cases/{seeded.CaseId:D}/extractions/duplicates/{candidate.CandidateId:D}",
            new Dictionary<string, string>
            {
                ["resolution"] = nameof(DuplicateResolutionKind.DistinctDocuments),
            }));
        captures.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.FirstT4DocumentId,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}/corrections",
            new Dictionary<string, string>
            {
                ["field"] = nameof(CorrectionField.T4BoxValue),
                ["boxCode"] = "14",
                ["correctedValue"] = "51000.00",
            }));

        captures.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.MedicalDocumentId,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.MedicalDocumentId:D}/classification",
            new Dictionary<string, string>
            {
                ["targetClassification"] = nameof(DocumentClassification.T4Slip),
            }));
        var disagreement = captures[^1].Body;
        Assert.Contains("Automated classification", disagreement, StringComparison.Ordinal);
        Assert.Contains("MedicalExpenseEvidence", disagreement, StringComparison.Ordinal);
        Assert.Contains("T4Slip", disagreement, StringComparison.Ordinal);

        captures.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.MedicalDocumentId,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.MedicalDocumentId:D}/classification",
            new Dictionary<string, string>
            {
                ["targetClassification"] =
                    nameof(DocumentClassification.MedicalExpenseEvidence),
            }));

        foreach (var documentId in new[]
                 {
                     seeded.FirstT4DocumentId,
                     seeded.SecondT4DocumentId,
                     seeded.MedicalDocumentId,
                 })
        {
            captures.Add(await ExtractionPortalHttp.PostReviewMutationAsync(
                accountant,
                seeded.CaseId,
                documentId,
                $"/intake-cases/{seeded.CaseId:D}/extractions/{documentId:D}/accept",
                new Dictionary<string, string>()));
        }

        var beforeFinalization = await world.Intake.Store.FindAsync(
            seeded.Firm,
            seeded.CaseId,
            CancellationToken.None);
        Assert.Null(beforeFinalization!.ReviewAssociation);
        Assert.Empty(world.Reviews.All);

        var finalized = await ExtractionPortalHttp.FinalizeAsync(
            accountant,
            seeded.CaseId,
            seeded.FirstT4DocumentId);
        captures.Add(finalized);

        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        Assert.Contains("Accountant review", finalized.Body, StringComparison.Ordinal);
        Assert.Contains("51000.00", finalized.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ExtractionPortalWorld.ProviderMarker,
            finalized.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ExtractionPortalWorld.PatientMarker,
            finalized.Body,
            StringComparison.Ordinal);

        var record = Assert.Single(world.Reviews.All);
        Assert.Equal(2, record.Slips.Count);
        var associated = await world.Intake.Store.FindAsync(
            seeded.Firm,
            seeded.CaseId,
            CancellationToken.None);
        Assert.Equal(record.RecordId, associated!.ReviewAssociation!.RecordId);

        var medicalPage = await ExtractionPortalHttp.GetAsync(
            accountant,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.MedicalDocumentId:D}");
        captures.Add(medicalPage);
        Assert.Contains(
            ExtractionPortalWorld.ProviderMarker,
            medicalPage.Body,
            StringComparison.Ordinal);
        Assert.Contains(
            ExtractionPortalWorld.PatientMarker,
            medicalPage.Body,
            StringComparison.Ordinal);
        Assert.Contains("Medical expense evidence", medicalPage.Body, StringComparison.Ordinal);
        Assert.Contains(
            "No tax treatment is determined here.",
            medicalPage.Body,
            StringComparison.Ordinal);

        var corrected = (await world.Extractions.FindDocumentAsync(
            seeded.Firm,
            seeded.CaseId,
            seeded.FirstT4DocumentId,
            CancellationToken.None))!;
        Assert.Equal(2, corrected.Versions.Count);
        Assert.Single(corrected.Corrections);
        Assert.Equal("51000.00", corrected.Versions[^1].BoxValues.Single(
            box => box.Code == "14").Value);
        Assert.Single((await world.Extractions.FindDocumentAsync(
            seeded.Firm,
            seeded.CaseId,
            seeded.SecondT4DocumentId,
            CancellationToken.None))!.DuplicateResolutions);

        var allHttp = string.Join("\n", captures.Select(capture => capture.All));
        Assert.DoesNotContain(
            ExtractionPortalWorld.PlantedSin,
            allHttp,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ExtractionPortalWorld.SourceMarker,
            allHttp,
            StringComparison.Ordinal);

        // TestServer requests use an in-process transport. Observe runtime
        // HTTP, DNS, and socket attempts as well: the registration inventory
        // alone cannot detect a provider constructing its own network client.
        Assert.Empty(network.Attempts);
        Assert.DoesNotContain(
            world.Intake.FirmOne.RegisteredServiceNames(),
            name => name.Contains("IHttpClientFactory", StringComparison.Ordinal) ||
                    name.Contains("SocketsHttpHandler", StringComparison.Ordinal));
    }
}

[CollectionDefinition("Extraction network observation", DisableParallelization = true)]
public sealed class ExtractionNetworkObservationCollection;

internal sealed class ExtractionNetworkAttemptListener : EventListener
{
    private readonly ConcurrentQueue<string> _attempts = new();

    internal IReadOnlyList<string> Attempts => _attempts.ToArray();

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name is "System.Net.Http" or "System.Net.NameResolution" or "System.Net.Sockets")
        {
            EnableEvents(source, EventLevel.Verbose);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        // HttpClient also emits RequestStart for the TestServer transport.
        // Real localhost networking remains covered by DNS/socket events.
        if (data.EventSource.Name == "System.Net.Http" &&
            data.EventName == "RequestStart" &&
            data.PayloadNames is { } names && data.Payload is { } values &&
            names.IndexOf("host") is var hostIndex && hostIndex >= 0 &&
            string.Equals(values[hostIndex] as string, "localhost", StringComparison.Ordinal))
        {
            return;
        }

        if (data.EventName is "RequestStart" or "ResolutionStart" or "ConnectStart" or "SendTo")
        {
            // Record structural event names only, never destinations or data.
            _attempts.Enqueue($"{data.EventSource.Name}:{data.EventName}");
        }
    }

    internal async Task VerifyCaptureAndResetAsync()
    {
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = (_, _) => throw new InvalidOperationException("Test connection blocked."),
        };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("http://127.0.0.1:1/egress-observer-control"));
        Assert.Contains("System.Net.Http:RequestStart", Attempts);
        _attempts.Clear();
    }
}

internal sealed class ExtractionPortalWorld : IAsyncDisposable
{
    internal const string PlantedSin = "123456789";
    internal const string SourceMarker = "SOURCE-BYTE-MARKER-015-T07";
    internal const string ProviderMarker = "PROVIDER-MARKER-015-T07";
    internal const string PatientMarker = "PATIENT-MARKER-015-T07";

    private ExtractionPortalWorld(
        IntakeWorld intake,
        PortalExtractionStore extractions,
        PortalReviewRepository reviews)
    {
        Intake = intake;
        Extractions = extractions;
        Reviews = reviews;
    }

    internal IntakeWorld Intake { get; }
    internal PortalExtractionStore Extractions { get; }
    internal PortalReviewRepository Reviews { get; }

    internal static ExtractionPortalWorld Create(
        Action<IWebHostBuilder>? configureCapture = null)
    {
        var extractions = new PortalExtractionStore();
        var reviews = new PortalReviewRepository();
        var intake = IntakeWorld.Create(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExtractionStore>();
                services.RemoveAll<IReviewRecordRepository>();
                services.AddSingleton<IExtractionStore>(extractions);
                services.AddSingleton<IReviewRecordRepository>(reviews);
            });
            configureCapture?.Invoke(builder);
        });
        extractions.Intake = intake.Store;
        return new ExtractionPortalWorld(intake, extractions, reviews);
    }

    internal async Task<SeededExtractionCase> SeedFirmOneAsync()
    {
        var sources = new[]
        {
            T4Source("first scan"),
            T4Source("second scan"),
            MedicalSource(),
        };
        return await SeedAsync(
            Intake.FirmOne,
            sources,
            t4Count: 2,
            medicalCount: 1);
    }

    internal async Task<SeededExtractionCase> SeedFirmTwoAsync() =>
        await SeedAsync(
            Intake.FirmTwo,
            [T4Source("foreign first scan"), T4Source("foreign second scan")],
            t4Count: 2,
            medicalCount: 0);

    private async Task<SeededExtractionCase> SeedAsync(
        IntakeFirmHost host,
        IReadOnlyList<byte[]> sources,
        int t4Count,
        int medicalCount)
    {
        var firm = TrustedFirmId.FromGuid(host.FirmId);
        var caseId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var now = new DateTimeOffset(2028, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var expected = new List<IntakeExpectedItemRecord>();
        var documents = new List<IntakeDocumentRecord>();
        var answerVersions = new List<IntakeAnswerVersionRecord>();
        var answerReferences = new List<IntakeSubmissionAnswerReference>();
        var documentReferences = new List<IntakeSubmissionDocumentReference>();

        for (var ordinal = 1; ordinal <= t4Count; ordinal++)
        {
            expected.Add(new IntakeExpectedItemRecord(
                Guid.NewGuid(), DocumentCategory.T4Slip, ordinal));
        }
        for (var ordinal = 1; ordinal <= medicalCount; ordinal++)
        {
            expected.Add(new IntakeExpectedItemRecord(
                Guid.NewGuid(), DocumentCategory.MedicalExpenseEvidence, ordinal));
        }

        foreach (var category in expected.Select(item => item.Category).Distinct())
        {
            answerVersions.Add(new IntakeAnswerVersionRecord(
                category,
                1,
                AvailabilityAnswerCode.AvailableNow,
                IntakeActorKind.Client,
                Guid.NewGuid(),
                now));
            answerReferences.Add(new IntakeSubmissionAnswerReference(category, 1));
        }

        using (var scope = host.Resolve<IServiceScopeFactory>().CreateScope())
        {
            var content = scope.ServiceProvider.GetRequiredService<IIntakeContentStore>();
            for (var index = 0; index < sources.Count; index++)
            {
                var expectedItem = expected[index];
                var documentId = Guid.NewGuid();
                using var stream = new MemoryStream(sources[index], writable: false);
                var staged = await content.StageAsync(
                    firm,
                    caseId,
                    stream,
                    1024 * 1024,
                    CancellationToken.None);
                documents.Add(new IntakeDocumentRecord(
                    documentId,
                    expectedItem.ExpectedItemId,
                    expectedItem.Category,
                    1,
                    staged.ContentKey,
                    IntakeMediaKind.Pdf,
                    staged.ByteCount,
                    staged.Fingerprint,
                    now));
                documentReferences.Add(
                    new IntakeSubmissionDocumentReference(documentId));
            }
        }

        var stored = new StoredIntakeCase(
            firm,
            caseId,
            participantId,
            "Synthetic Employee",
            2025,
            CaseStatus.InformationGathering,
            1,
            expected,
            new IntakeLinkRecord(
                Guid.NewGuid(),
                new IntakeTokenDigest(new byte[32]),
                now,
                now.AddHours(1),
                now,
                null),
            answerVersions,
            documents,
            [],
            [
                new IntakeSubmissionRecord(
                    1,
                    answerReferences,
                    documentReferences,
                    IntakeActorKind.Client,
                    Guid.NewGuid(),
                    now),
            ],
            null);
        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await Intake.Store.CreateAsync(
                new NewIntakeCase(
                    stored,
                    [
                        new IntakeAuditEventRecord(
                            Guid.NewGuid(),
                            1,
                            IntakeAuditKind.CaseCreated,
                            IntakeActorKind.Accountant,
                            Guid.NewGuid(),
                            null,
                            now),
                    ]),
                CancellationToken.None));

        var t4 = documents
            .Where(document => document.Category == DocumentCategory.T4Slip)
            .Select(document => document.DocumentId)
            .ToArray();
        var medical = documents
            .Where(document =>
                document.Category == DocumentCategory.MedicalExpenseEvidence)
            .Select(document => document.DocumentId)
            .ToArray();
        return new SeededExtractionCase(
            firm,
            caseId,
            t4.ElementAtOrDefault(0),
            t4.ElementAtOrDefault(1),
            medical.ElementAtOrDefault(0));
    }

    private static byte[] T4Source(string scan) => Encoding.UTF8.GetBytes(string.Join('\n',
        "TAXAI-DETERMINISTIC-DOCUMENT/1",
        "KIND: T4",
        "CONFIDENCE: 0.75",
        "YEAR: 2025",
        "EMPLOYER-NAME: Synthetic Employer",
        "EMPLOYEE-NAME: Synthetic Employee",
        $"BOX 12: {PlantedSin}",
        "BOX 14: 50000.00",
        "BOX 45: 1",
        $"SCAN: {scan}",
        SourceMarker));

    private static byte[] MedicalSource() => Encoding.UTF8.GetBytes(string.Join('\n',
        "TAXAI-DETERMINISTIC-DOCUMENT/1",
        "KIND: MEDICAL-EXPENSE-EVIDENCE",
        "CONFIDENCE: 0.50",
        "SERVICE-DATE: 14 mars 2025",
        "GROSS-AMOUNT: $120.50",
        "CURRENCY: CAD",
        $"PROVIDER-NAME: {ProviderMarker}",
        $"PATIENT-NAME: {PatientMarker}",
        "RECEIPT-REFERENCE: R-015-T07",
        SourceMarker));

    public async ValueTask DisposeAsync() => await Intake.DisposeAsync();
}

internal sealed record SeededExtractionCase(
    TrustedFirmId Firm,
    Guid CaseId,
    Guid FirstT4DocumentId,
    Guid SecondT4DocumentId,
    Guid MedicalDocumentId);

internal static class ExtractionPortalHttp
{
    internal static async Task<ExtractionHttpCapture> ExtractAsync(
        HttpClient client,
        Guid caseId,
        Guid documentId) =>
        await PostReviewMutationAsync(
            client,
            caseId,
            documentId,
            $"/intake-cases/{caseId:D}/extractions/{documentId:D}/extract",
            new Dictionary<string, string>());

    internal static async Task<ExtractionHttpCapture> FinalizeAsync(
        HttpClient client,
        Guid caseId,
        Guid tokenDocumentId) =>
        await PostReviewMutationAsync(
            client,
            caseId,
            tokenDocumentId,
            $"/intake-cases/{caseId:D}/extractions/finalize",
            new Dictionary<string, string>());

    internal static async Task<ExtractionHttpCapture> PostReviewMutationAsync(
        HttpClient client,
        Guid caseId,
        Guid tokenDocumentId,
        string path,
        IReadOnlyDictionary<string, string> values)
    {
        var page = await GetAsync(
            client,
            $"/intake-cases/{caseId:D}/extractions/{tokenDocumentId:D}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var form = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        form["__RequestVerificationToken"] = IntakeFlow.AntiforgeryToken(page.Body);
        using var response = await client.PostAsync(
            path,
            new FormUrlEncodedContent(form));
        var mutation = await CaptureAsync(response);
        if (response.Headers.Location is { } location &&
            (int)response.StatusCode is >= 300 and < 400)
        {
            var redirected = await GetAsync(client, location.ToString());
            return redirected with { Preceding = [page, mutation] };
        }
        return mutation with { Preceding = [page] };
    }

    internal static async Task<ExtractionHttpCapture> GetAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(path);
        return await CaptureAsync(response);
    }

    internal static async Task<ExtractionHttpCapture> CaptureAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .SelectMany(pair => pair.Value.Select(value => $"{pair.Key}: {value}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new ExtractionHttpCapture(
            response.StatusCode,
            response.RequestMessage?.RequestUri?.ToString() ?? string.Empty,
            body,
            headers);
    }
}

internal sealed record ExtractionHttpCapture(
    HttpStatusCode StatusCode,
    string Url,
    string Body,
    IReadOnlyList<string> Headers)
{
    internal IReadOnlyList<ExtractionHttpCapture> Preceding { get; init; } = [];
    internal string All => string.Join("\n", [Url, Body, .. Headers, .. Preceding.Select(item => item.All)]);
    internal string Transport => string.Join("\n", [Url, .. Headers, .. Preceding.Select(item => item.Transport)]);
}

internal sealed class PortalReviewRepository : IReviewRecordRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid Firm, Guid Record), PersistedReviewRecord> _records = [];

    internal IReadOnlyList<PersistedReviewRecord> All
    {
        get
        {
            lock (_gate)
            {
                return _records.Values.ToArray();
            }
        }
    }

    public Task<ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm,
        PersistedReviewRecord record,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_records.ContainsKey((firm.Value, record.RecordId)))
            {
                return Task.FromResult(ParseResult<PersistedReviewRecord>.Failure(
                    [new ValidationError("record", "The record already exists.")]));
            }

            _records.Add((firm.Value, record.RecordId), record);
            return Task.FromResult(ParseResult<PersistedReviewRecord>.Success(record));
        }
    }

    public Task<PersistedReviewRecord?> FindAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _records.TryGetValue((firm.Value, recordId), out var record)
                    ? record
                    : null);
        }
    }
}

internal sealed class PortalExtractionStore : IExtractionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid Firm, Guid Case), State> _states = [];

    internal IIntakeStore Intake { get; set; } = default!;

    public async Task<ExtractionStoreWriteCode> AppendExtractionVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        ExtractionVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, state =>
        {
            if (state.Versions.Any(item =>
                    item.DocumentId == version.DocumentId &&
                    item.Version == version.Version))
            {
                return false;
            }
            state.Participants[version.DocumentId] = participantId;
            state.Versions.Add(version);
            return true;
        }, cancellationToken);

    public async Task<ExtractionStoreWriteCode> AppendMedicalEvidenceVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        MedicalEvidenceVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, state =>
        {
            if (state.Medical.Any(item =>
                    item.DocumentId == version.DocumentId &&
                    item.Version == version.Version))
            {
                return false;
            }
            state.Participants[version.DocumentId] = participantId;
            state.Medical.Add(version);
            return true;
        }, cancellationToken);

    public async Task<ExtractionStoreWriteCode> AppendCorrectionAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionCorrectionRecord correction,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, state =>
        {
            if (state.Corrections.Any(item => item.CorrectionId == correction.CorrectionId))
            {
                return false;
            }
            state.Corrections.Add(correction);
            return true;
        }, cancellationToken);

    public async Task<ExtractionStoreWriteCode> AppendClassificationOverrideAsync(
        TrustedFirmId firm,
        Guid caseId,
        ClassificationOverrideRecord classificationOverride,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, state =>
        {
            if (state.Overrides.Any(item => item.OverrideId == classificationOverride.OverrideId))
            {
                return false;
            }
            state.Overrides.Add(classificationOverride);
            return true;
        }, cancellationToken);

    public async Task<ExtractionStoreWriteCode> AppendDuplicateCandidateAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateCandidateRecord candidate,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, state =>
        {
            if (state.Candidates.Any(item => item.CandidateId == candidate.CandidateId))
            {
                return false;
            }
            state.Candidates.Add(candidate);
            return true;
        }, cancellationToken);

    public async Task<ExtractionStoreWriteCode> AppendDuplicateResolutionAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateResolutionRecord resolution,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, state =>
        {
            if (state.Resolutions.Any(item => item.CandidateId == resolution.CandidateId))
            {
                return false;
            }
            state.Resolutions.Add(resolution);
            return true;
        }, cancellationToken);

    public async Task<ExtractionStoreWriteCode> AppendAcceptanceAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAcceptanceRecord acceptance,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, state =>
        {
            if (acceptance.Scope == ExtractionAcceptanceScope.Case &&
                state.Acceptances.Any(item =>
                    item.Scope == ExtractionAcceptanceScope.Case))
            {
                return false;
            }
            state.Acceptances.Add(acceptance);
            return true;
        }, cancellationToken);

    public async Task<ExtractionStoreWriteCode> AppendAuditEventAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord auditEvent,
        DateTimeOffset retentionUntilUtc,
        CancellationToken cancellationToken) =>
        await AppendAsync(firm, caseId, auditEvent, _ => true, cancellationToken);

    public async Task<StoredDocumentExtraction?> FindDocumentAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var found = await FindCaseAsync(firm, caseId, cancellationToken);
        return found?.Documents.SingleOrDefault(document =>
            document.DocumentId == documentId);
    }

    public async Task<StoredCaseExtractions?> FindCaseAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        if (await Intake.FindAsync(firm, caseId, cancellationToken) is null)
        {
            return null;
        }

        lock (_gate)
        {
            var state = StateFor(firm, caseId);
            var documentIds = state.Versions.Select(item => item.DocumentId)
                .Concat(state.Medical.Select(item => item.DocumentId))
                .Concat(state.Corrections.Select(item => item.DocumentId))
                .Concat(state.Overrides.Select(item => item.DocumentId))
                .Concat(state.Candidates.Select(item => item.DocumentId))
                .Concat(state.Acceptances
                    .Where(item => item.DocumentId is not null)
                    .Select(item => item.DocumentId!.Value))
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            return new StoredCaseExtractions(
                caseId,
                documentIds.Select(documentId => Document(state, documentId)).ToArray(),
                state.Acceptances.Where(item =>
                    item.Scope == ExtractionAcceptanceScope.Case).ToArray(),
                state.Audits.ToArray());
        }
    }

    private async Task<ExtractionStoreWriteCode> AppendAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord audit,
        Func<State, bool> append,
        CancellationToken cancellationToken)
    {
        if (await Intake.FindAsync(firm, caseId, cancellationToken) is null)
        {
            return ExtractionStoreWriteCode.NotFound;
        }
        lock (_gate)
        {
            var state = StateFor(firm, caseId);
            if (!append(state))
            {
                return ExtractionStoreWriteCode.Conflict;
            }
            state.Audits.Add(audit);
            return ExtractionStoreWriteCode.Success;
        }
    }

    private State StateFor(TrustedFirmId firm, Guid caseId)
    {
        if (!_states.TryGetValue((firm.Value, caseId), out var state))
        {
            state = new State();
            _states.Add((firm.Value, caseId), state);
        }
        return state;
    }

    private static StoredDocumentExtraction Document(State state, Guid documentId) =>
        new(
            documentId,
            state.Participants.GetValueOrDefault(documentId),
            state.Versions.Where(item => item.DocumentId == documentId).ToArray(),
            state.Medical.Where(item => item.DocumentId == documentId).ToArray(),
            state.Corrections.Where(item => item.DocumentId == documentId).ToArray(),
            state.Overrides.Where(item => item.DocumentId == documentId).ToArray(),
            state.Candidates.Where(item => item.DocumentId == documentId).ToArray(),
            state.Resolutions.Where(resolution => state.Candidates.Any(candidate =>
                candidate.DocumentId == documentId &&
                candidate.CandidateId == resolution.CandidateId)).ToArray(),
            state.Acceptances.Where(item => item.DocumentId == documentId).ToArray());

    private sealed class State
    {
        internal Dictionary<Guid, Guid> Participants { get; } = [];
        internal List<ExtractionVersion> Versions { get; } = [];
        internal List<MedicalEvidenceVersion> Medical { get; } = [];
        internal List<ExtractionCorrectionRecord> Corrections { get; } = [];
        internal List<ClassificationOverrideRecord> Overrides { get; } = [];
        internal List<DuplicateCandidateRecord> Candidates { get; } = [];
        internal List<DuplicateResolutionRecord> Resolutions { get; } = [];
        internal List<ExtractionAcceptanceRecord> Acceptances { get; } = [];
        internal List<ExtractionAuditEventRecord> Audits { get; } = [];
    }
}
