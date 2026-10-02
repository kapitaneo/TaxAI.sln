using System.Net;
using System.Text.RegularExpressions;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxAI.Portal.Tests.EndToEnd;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Extraction;

/// <summary>
/// Spec 017 T03 — the fixed notice for an in-scope refused correction or case
/// finalization (FR-42 to FR-44), over the real portal composition.
/// <para>
/// Two kinds of proof. The first drives the real workflows, deterministic
/// provider, and encrypted Development reader, and shows that a refusal renders
/// the page again with one fixed notice, mutates nothing, and reflects nothing
/// the caller posted. The second substitutes only the two workflow ports with a
/// stub that returns a chosen closed result code, and pins the complete
/// operation/result table: exactly two operations and two codes have a notice,
/// and every other combination keeps the existing empty <c>404</c>.
/// </para>
/// </summary>
public sealed class WorkflowFeedbackTests
{
    private const string PlantedBoxCode = "PLANTED-CODE-017-T03";

    private const string PlantedValue = "PLANTED-VALUE-017-T03";

    private const string ArbitraryNotice = "ARBITRARY-NOTICE-017-T03";

    private static readonly Regex NoticeElement = new(
        "<p class=\"notice\" role=\"alert\">([^<]*)</p>",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex AntiforgeryValue = new(
        "(?<=name=\"__RequestVerificationToken\" value=\")[^\"]+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex NoticeConstant = new(
        "internal const string (\\w+) =\\s*\"([^\"]*)\";",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly IReadOnlyDictionary<string, string> NoticeTexts = ReadNoticeTexts();

    // ----------------------------------------------------------------- AC-74

    [Fact] // AC-74, AC-78, FR-44
    public async Task AnInScopeInvalidCorrectionRendersTheFixedNoticeAndAppendsNothing()
    {
        await using var world = ExtractionPortalWorld.Create();
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = await SignedInAsync(world);
        await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);

        var reviewPath = $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}";
        var before = await ExtractionPortalHttp.GetAsync(accountant, reviewPath);
        var stateBefore = await SnapshotAsync(world, seeded);

        // A medical field named against a T4 document: in scope, and refused by
        // the existing workflow as Invalid.
        var refused = await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.FirstT4DocumentId,
            reviewPath + "/corrections",
            new Dictionary<string, string>
            {
                ["field"] = nameof(CorrectionField.MedicalCurrency),
                ["boxCode"] = PlantedBoxCode,
                ["correctedValue"] = PlantedValue,
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(NoticeTexts["CorrectionRefusedText"], SingleNotice(refused.Body));
        AssertRefusalCarriesNoData(refused);
        Assert.Equal(WithoutNotice(before.Body), WithoutNotice(refused.Body));
        Assert.Contains(
            refused.Headers,
            header => header.StartsWith("Cache-Control: ", StringComparison.Ordinal) &&
                      header.Contains("no-store", StringComparison.Ordinal));

        Assert.Equal(stateBefore, await SnapshotAsync(world, seeded));
        Assert.Empty(world.Reviews.All);
    }

    [Fact] // AC-74, AC-78, FR-44
    public async Task AnInScopeConflictingCorrectionRendersTheFixedNoticeAndAppendsNothing()
    {
        await using var world = ExtractionPortalWorld.Create();
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = await SignedInAsync(world);
        await FinalizeFirstT4Async(world, accountant, seeded);

        var reviewPath = $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}";
        var before = await ExtractionPortalHttp.GetAsync(accountant, reviewPath);
        var stateBefore = await SnapshotAsync(world, seeded);

        // A correction to a finalized case: the existing workflow answers Conflict.
        var refused = await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.FirstT4DocumentId,
            reviewPath + "/corrections",
            new Dictionary<string, string>
            {
                ["field"] = nameof(CorrectionField.EmployerDisplayName),
                ["correctedValue"] = PlantedValue,
            });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(NoticeTexts["CorrectionConflictText"], SingleNotice(refused.Body));
        AssertRefusalCarriesNoData(refused);
        Assert.Equal(WithoutNotice(before.Body), WithoutNotice(refused.Body));

        Assert.Equal(stateBefore, await SnapshotAsync(world, seeded));
        Assert.Single(world.Reviews.All);
    }

    // ----------------------------------------------------------------- AC-75

    [Fact] // AC-75, AC-78, FR-44
    public async Task AnInScopeInvalidFinalizationRendersTheFixedNoticeAndCreatesNothing()
    {
        await using var world = ExtractionPortalWorld.Create();
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = await SignedInAsync(world);

        // Extracted, never accepted: nothing can be finalized.
        await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        var before = await ExtractionPortalHttp.GetAsync(
            accountant, $"/intake-cases/{seeded.CaseId:D}/extractions");
        var stateBefore = await SnapshotAsync(world, seeded);

        var refused = await ExtractionPortalHttp.FinalizeAsync(
            accountant, seeded.CaseId, seeded.FirstT4DocumentId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(NoticeTexts["FinalizationRefusedText"], SingleNotice(refused.Body));
        AssertRefusalCarriesNoData(refused);
        Assert.Equal(WithoutNotice(before.Body), WithoutNotice(refused.Body));

        Assert.Equal(stateBefore, await SnapshotAsync(world, seeded));
        Assert.Empty(world.Reviews.All);
        Assert.Null((await world.Intake.Store.FindAsync(
            seeded.Firm, seeded.CaseId, CancellationToken.None))!.ReviewAssociation);
    }

    [Fact] // AC-75, AC-78, FR-44
    public async Task AnInScopeConflictingFinalizationRendersTheFixedNoticeAndCreatesNothing()
    {
        await using var world = ExtractionPortalWorld.Create();
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = await SignedInAsync(world);
        await FinalizeFirstT4Async(world, accountant, seeded);

        var before = await ExtractionPortalHttp.GetAsync(
            accountant, $"/intake-cases/{seeded.CaseId:D}/extractions");
        var stateBefore = await SnapshotAsync(world, seeded);
        var association = (await world.Intake.Store.FindAsync(
            seeded.Firm, seeded.CaseId, CancellationToken.None))!.ReviewAssociation;

        // Already finalized: the existing workflow answers Conflict.
        var refused = await ExtractionPortalHttp.FinalizeAsync(
            accountant, seeded.CaseId, seeded.FirstT4DocumentId);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(NoticeTexts["FinalizationConflictText"], SingleNotice(refused.Body));
        AssertRefusalCarriesNoData(refused);
        Assert.Equal(WithoutNotice(before.Body), WithoutNotice(refused.Body));

        Assert.Equal(stateBefore, await SnapshotAsync(world, seeded));
        Assert.Single(world.Reviews.All);
        Assert.Equal(
            association,
            (await world.Intake.Store.FindAsync(
                seeded.Firm, seeded.CaseId, CancellationToken.None))!.ReviewAssociation);
    }

    // ---------------------------------------------------------------- AC-76b

    [Fact] // AC-76b
    public async Task AnInScopeCorrectionTheRealWorkflowAnswersNotFoundKeepsTheEmpty404()
    {
        await using var world = ExtractionPortalWorld.Create();
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = await SignedInAsync(world);

        // An owned document with no extraction yet: in scope for the route, and
        // NotFound to the existing workflow.
        var refused = await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.FirstT4DocumentId,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}/corrections",
            new Dictionary<string, string>
            {
                ["field"] = nameof(CorrectionField.EmployerDisplayName),
                ["correctedValue"] = PlantedValue,
            });

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Equal(string.Empty, refused.Body);
    }

    // ------------------------------------- the complete operation/result table

    public static TheoryData<string, int, int, string?> OperationResultTable() => new()
    {
        // Exactly these four entries have a notice (FR-43).
        { "correction", (int)ExtractionResultCode.Invalid, 422, "CorrectionRefusedText" },
        { "correction", (int)ExtractionResultCode.Conflict, 409, "CorrectionConflictText" },
        { "finalize", (int)ExtractionResultCode.Invalid, 422, "FinalizationRefusedText" },
        { "finalize", (int)ExtractionResultCode.Conflict, 409, "FinalizationConflictText" },

        // AC-76b: NotFound keeps the empty 404 on both.
        { "correction", (int)ExtractionResultCode.NotFound, 404, null },
        { "finalize", (int)ExtractionResultCode.NotFound, 404, null },

        // AC-76a: the other four mutations are unchanged for every refusal.
        { "extract", (int)ExtractionResultCode.Invalid, 404, null },
        { "extract", (int)ExtractionResultCode.Conflict, 404, null },
        { "extract", (int)ExtractionResultCode.NotFound, 404, null },
        { "classification", (int)ExtractionResultCode.Invalid, 404, null },
        { "classification", (int)ExtractionResultCode.Conflict, 404, null },
        { "classification", (int)ExtractionResultCode.NotFound, 404, null },
        { "accept", (int)ExtractionResultCode.Invalid, 404, null },
        { "accept", (int)ExtractionResultCode.Conflict, 404, null },
        { "accept", (int)ExtractionResultCode.NotFound, 404, null },
        { "duplicate", (int)ExtractionResultCode.Invalid, 404, null },
        { "duplicate", (int)ExtractionResultCode.Conflict, 404, null },
        { "duplicate", (int)ExtractionResultCode.NotFound, 404, null },
    };

    [Theory] // AC-74, AC-75, AC-76a, AC-76b, FR-42, FR-43
    [MemberData(nameof(OperationResultTable))]
    public async Task EachInScopeRefusalAnswersExactlyItsApprovedResponse(
        string operation,
        int code,
        int expectedStatus,
        string? expectedNotice)
    {
        var workflow = new RefusingWorkflow((ExtractionResultCode)code);
        await using var world = StubbedWorld(workflow);
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = await SignedInAsync(world);

        var owned = Mutation(operation, seeded.CaseId, seeded.FirstT4DocumentId);
        var refused = await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant, seeded.CaseId, seeded.FirstT4DocumentId, owned.Path, owned.Values);

        // The request was in scope: it reached the workflow exactly once.
        Assert.Equal(1, workflow.Calls);
        Assert.Equal(expectedStatus, (int)refused.StatusCode);

        if (expectedNotice is null)
        {
            // Identical to the baseline refusal: the same empty 404 a missing
            // selector receives on the same route, header for header.
            var missing = Mutation(operation, Guid.NewGuid(), Guid.NewGuid());
            var baseline = await ExtractionPortalHttp.PostReviewMutationAsync(
                accountant, seeded.CaseId, seeded.FirstT4DocumentId, missing.Path, missing.Values);

            Assert.Equal(HttpStatusCode.NotFound, baseline.StatusCode);
            Assert.Equal(string.Empty, refused.Body);
            Assert.Equal(baseline.Body, refused.Body);
            Assert.Equal(baseline.Headers, refused.Headers);
            Assert.Empty(NoticeElement.Matches(refused.Body));
            return;
        }

        Assert.Equal(NoticeTexts[expectedNotice], SingleNotice(refused.Body));
        AssertRefusalCarriesNoData(refused);
    }

    // ----------------------------------------------------------------- AC-77

    [Fact] // AC-77, FR-43
    public void TheNoticeVocabularyIsExactlyTheFourApprovedEntries()
    {
        var kind = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.Components.Pages.WorkflowNoticeKind",
            throwOnError: true)!;

        Assert.True(kind.IsEnum);
        Assert.Equal(
            new[]
            {
                "None",
                "CorrectionRefused",
                "CorrectionConflict",
                "FinalizationRefused",
                "FinalizationConflict",
            },
            Enum.GetNames(kind));

        // One constant text per entry, and the component maps exactly those four.
        Assert.Equal(
            new[]
            {
                "CorrectionConflictText",
                "CorrectionRefusedText",
                "FinalizationConflictText",
                "FinalizationRefusedText",
            },
            NoticeTexts.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(4, NoticeTexts.Values.Distinct(StringComparer.Ordinal).Count());

        var component = NoticeSource();
        foreach (var entry in Enum.GetNames(kind).Skip(1))
        {
            Assert.Contains(
                $"WorkflowNoticeKind.{entry} => {entry}Text,",
                component,
                StringComparison.Ordinal);
        }

        Assert.Contains("_ => null,", component, StringComparison.Ordinal);

        // The selection lives in the two approved handlers and nowhere else.
        var endpoints = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "TaxAI.Portal", "ExtractionEndpoints.cs"));
        Assert.Equal(3, Regex.Matches(endpoints, @"\bRefusalOf\(").Count);
        Assert.Equal(2, Regex.Matches(endpoints, @"\bRefusalOf\(\s*result\.Code,").Count);
    }

    [Fact] // AC-77, FR-43
    public async Task NoRequestValueCanSelectOrSupplyANotice()
    {
        await using var world = ExtractionPortalWorld.Create();
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = await SignedInAsync(world);
        await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        var reviewPath = $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}";
        var selectors =
            $"?notice=CorrectionRefused&Notice=FinalizationConflict&kind=3&message={ArbitraryNotice}";

        // A read names a notice in the query: nothing is rendered.
        foreach (var path in new[]
                 {
                     reviewPath + selectors,
                     $"/intake-cases/{seeded.CaseId:D}/extractions" + selectors,
                 })
        {
            var page = await ExtractionPortalHttp.GetAsync(accountant, path);

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Empty(NoticeElement.Matches(page.Body));
            Assert.DoesNotContain(ArbitraryNotice, page.Body, StringComparison.Ordinal);
            Assert.All(
                NoticeTexts.Values,
                text => Assert.DoesNotContain(text, page.Body, StringComparison.Ordinal));
        }

        // A refused correction names a different notice and arbitrary text in
        // the query and the form: only the operation and result code decide.
        var refused = await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.FirstT4DocumentId,
            reviewPath + "/corrections" + selectors,
            new Dictionary<string, string>
            {
                ["field"] = nameof(CorrectionField.MedicalCurrency),
                ["correctedValue"] = PlantedValue,
                ["notice"] = "FinalizationConflict",
                ["Notice"] = ArbitraryNotice,
                ["kind"] = "4",
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(NoticeTexts["CorrectionRefusedText"], SingleNotice(refused.Body));
        Assert.DoesNotContain(ArbitraryNotice, refused.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            NoticeTexts["FinalizationConflictText"], refused.Body, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- AC-78

    [Fact] // AC-78, FR-43
    public void EveryNoticeTextIsAFixedValueFreeConstant()
    {
        var component = NoticeSource();

        // Nothing is interpolated, concatenated, or formatted into a notice.
        foreach (var forbidden in new[] { "$\"", "string.Format", "String.Format", " + ", "@(" })
        {
            Assert.DoesNotContain(forbidden, component, StringComparison.Ordinal);
        }

        // The only values the component can render are its own four constants.
        Assert.Equal(
            1,
            Regex.Matches(component, @"\[Parameter\]").Count);
        Assert.Contains("public WorkflowNoticeKind Kind { get; set; }", component, StringComparison.Ordinal);

        foreach (var text in NoticeTexts.Values)
        {
            Assert.DoesNotMatch(new Regex(@"[{}\d<>&]"), text);
            Assert.DoesNotMatch(new Regex(@"\bSIN\b", RegexOptions.IgnoreCase), text);
            foreach (var forbidden in new[]
                     {
                         "Box", "box", "social insurance", "amount", "employer", "employee",
                         "provider", "OCR", "exception", "SQL", "identifier",
                     })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// What a refusal response must never carry: anything the caller posted,
    /// the planted source values, or diagnostic text. The refusal's own
    /// transport (URL, headers) and body are inspected, not the pages before it.
    /// </summary>
    private static void AssertRefusalCarriesNoData(ExtractionHttpCapture refused)
    {
        var response = string.Join("\n", [refused.Url, refused.Body, .. refused.Headers]);

        foreach (var planted in new[]
                 {
                     PlantedBoxCode,
                     PlantedValue,
                     ArbitraryNotice,
                     ExtractionPortalWorld.PlantedSin,
                     ExtractionPortalWorld.SourceMarker,
                 })
        {
            Assert.DoesNotContain(planted, response, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(new Regex(@"\bSIN\b"), response);
        foreach (var forbidden in new[]
                 {
                     "Box 12", "Exception", "StackTrace", "   at ", "SELECT ", "INSERT ",
                     "Microsoft.EntityFrameworkCore", "ExtractionWorkflow.",
                 })
        {
            Assert.DoesNotContain(forbidden, response, StringComparison.Ordinal);
        }
    }

    private static string SingleNotice(string body) =>
        WebUtility.HtmlDecode(Assert.Single(NoticeElement.Matches(body)).Groups[1].Value);

    /// <summary>
    /// The page with the notice element, the per-response antiforgery value,
    /// and insignificant whitespace removed, so a refusal can be compared with
    /// the same page rendered before it.
    /// </summary>
    private static string WithoutNotice(string body) =>
        Regex.Replace(
            AntiforgeryValue.Replace(NoticeElement.Replace(body, string.Empty), "<token>"),
            @"\s+",
            string.Empty);

    private static (string Path, IReadOnlyDictionary<string, string> Values) Mutation(
        string operation,
        Guid caseId,
        Guid documentId)
    {
        var document = $"/intake-cases/{caseId:D}/extractions/{documentId:D}";
        return operation switch
        {
            "extract" => (document + "/extract", new Dictionary<string, string>()),
            "classification" => (
                document + "/classification",
                new Dictionary<string, string>
                {
                    ["targetClassification"] = nameof(DocumentClassification.T4Slip),
                }),
            "correction" => (
                document + "/corrections",
                new Dictionary<string, string>
                {
                    ["field"] = nameof(CorrectionField.EmployerDisplayName),
                    ["boxCode"] = PlantedBoxCode,
                    ["correctedValue"] = PlantedValue,
                }),
            "accept" => (document + "/accept", new Dictionary<string, string>()),
            "duplicate" => (
                $"/intake-cases/{caseId:D}/extractions/duplicates/{documentId:D}",
                new Dictionary<string, string>
                {
                    ["resolution"] = nameof(DuplicateResolutionKind.DistinctDocuments),
                }),
            "finalize" => (
                $"/intake-cases/{caseId:D}/extractions/finalize",
                new Dictionary<string, string>()),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private static async Task<HttpClient> SignedInAsync(ExtractionPortalWorld world)
    {
        var accountant = world.Intake.FirmOne.CreateAnonymousClient();
        await world.Intake.FirmOne.SignInAsync(accountant);
        return accountant;
    }

    /// <summary>Extracts and accepts the first T4, then finalizes the case.</summary>
    private static async Task FinalizeFirstT4Async(
        ExtractionPortalWorld world,
        HttpClient accountant,
        SeededExtractionCase seeded)
    {
        await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        var accepted = await ExtractionPortalHttp.PostReviewMutationAsync(
            accountant,
            seeded.CaseId,
            seeded.FirstT4DocumentId,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}/accept",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var finalized = await ExtractionPortalHttp.FinalizeAsync(
            accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        Assert.Equal(HttpStatusCode.OK, finalized.StatusCode);
        Assert.Single(world.Reviews.All);
    }

    /// <summary>
    /// Every append the extraction store holds for the case, as counts. A
    /// refusal must leave each one exactly as it was.
    /// </summary>
    private static async Task<string> SnapshotAsync(
        ExtractionPortalWorld world,
        SeededExtractionCase seeded)
    {
        var stored = (await world.Extractions.FindCaseAsync(
            seeded.Firm, seeded.CaseId, CancellationToken.None))!;
        return string.Join(
            "|",
            new[]
            {
                $"documents:{stored.Documents.Count}",
                $"versions:{stored.Documents.Sum(document => document.Versions.Count)}",
                $"medical:{stored.Documents.Sum(document => document.MedicalEvidenceVersions.Count)}",
                $"corrections:{stored.Documents.Sum(document => document.Corrections.Count)}",
                $"overrides:{stored.Documents.Sum(document => document.ClassificationOverrides.Count)}",
                $"candidates:{stored.Documents.Sum(document => document.DuplicateCandidates.Count)}",
                $"resolutions:{stored.Documents.Sum(document => document.DuplicateResolutions.Count)}",
                $"acceptances:{stored.Documents.Sum(document => document.Acceptances.Count)}",
                $"caseAcceptances:{stored.CaseAcceptances.Count}",
                $"audits:{stored.AuditEvents.Count}",
            });
    }

    private static ExtractionPortalWorld StubbedWorld(RefusingWorkflow workflow) =>
        ExtractionPortalWorld.Create(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IExtractionIngestWorkflow>();
                services.RemoveAll<IExtractionReviewWorkflow>();
                services.AddSingleton<IExtractionIngestWorkflow>(workflow);
                services.AddSingleton<IExtractionReviewWorkflow>(workflow);
            }));

    private static string NoticeSource() =>
        Regex.Replace(
            File.ReadAllText(Path.Combine(
                RepositoryRoot,
                "src",
                "TaxAI.Portal",
                "Components",
                "Shared",
                "WorkflowNotice.razor")),
            @"@\*.*?\*@",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private static IReadOnlyDictionary<string, string> ReadNoticeTexts() =>
        NoticeConstant.Matches(NoticeSource())
            .ToDictionary(
                match => match.Groups[1].Value,
                match => match.Groups[2].Value,
                StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }

    /// <summary>
    /// Both workflow ports, refusing every call with one closed result code.
    /// It records only that it was reached, which is the proof the request was
    /// in scope when the handler ran.
    /// </summary>
    private sealed class RefusingWorkflow : IExtractionIngestWorkflow, IExtractionReviewWorkflow
    {
        private readonly ExtractionResultCode _code;
        private int _calls;

        internal RefusingWorkflow(ExtractionResultCode code) => _code = code;

        internal int Calls => Volatile.Read(ref _calls);

        public Task<ExtractionResult<DocumentExtractionOutcome>> ExtractAsync(
            TrustedFirmId firm,
            Guid accountantActorReference,
            ExtractDocumentCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Refuse<DocumentExtractionOutcome>();

        public Task<ExtractionResult<DocumentExtractionOutcome>> CorrectAsync(
            TrustedFirmId firm,
            Guid accountantActorReference,
            ApplyExtractionCorrectionCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Refuse<DocumentExtractionOutcome>();

        public Task<ExtractionResult<DocumentExtractionView>> OverrideClassificationAsync(
            TrustedFirmId firm,
            Guid accountantActorReference,
            OverrideExtractionClassificationCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Refuse<DocumentExtractionView>();

        public Task<ExtractionResult<DocumentExtractionView>> ResolveDuplicateAsync(
            TrustedFirmId firm,
            Guid accountantActorReference,
            ResolveExtractionDuplicateCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Refuse<DocumentExtractionView>();

        public Task<ExtractionResult<DocumentExtractionView>> AcceptDocumentAsync(
            TrustedFirmId firm,
            Guid accountantActorReference,
            AcceptExtractionDocumentCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Refuse<DocumentExtractionView>();

        public Task<ExtractionResult<FinalizedExtractionReview>> FinalizeCaseAsync(
            TrustedFirmId firm,
            Guid accountantActorReference,
            FinalizeExtractionCaseCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Refuse<FinalizedExtractionReview>();

        private Task<ExtractionResult<TValue>> Refuse<TValue>()
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(ExtractionResult<TValue>.Failure(_code));
        }
    }
}
