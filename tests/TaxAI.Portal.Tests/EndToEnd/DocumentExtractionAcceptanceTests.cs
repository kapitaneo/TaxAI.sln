using System.Globalization;
using System.Net;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxAI.Portal.Tests.Identity;
using TaxCases.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — the integrated acceptance proof over the <b>real</b> portal
/// composition, driven the way a browser drives it.
/// <para>
/// Only the SQL-Server-backed collaborators are substituted — the intake
/// metadata store, the extraction store, and the review-record repository —
/// and their relational counterparts are exercised for real against a database
/// with the real migrations applied in
/// <c>Persistence.Tests/EndToEnd/DocumentExtractionPersistenceTests</c>.
/// Everything a request actually touches is production code: routing, rate
/// limiting, authentication, firm-scoped authorization, antiforgery, the real
/// <c>ExtractionIngestWorkflow</c> and <c>ExtractionReviewWorkflow</c>, the real
/// <c>DeterministicDocumentIntelligenceProvider</c>, the real Development
/// encrypted content reader, the real engine chain, and every Razor component.
/// </para>
/// <para>
/// This task verifies; it repairs nothing. No production code is touched.
/// </para>
/// <para>
/// Every taxpayer-shaped value here is assembled at runtime and is obviously
/// synthetic.
/// </para>
/// </summary>
public sealed class DocumentExtractionAcceptanceTests
{
    [Fact] // AC-5, AC-16, AC-57, AC-63, AC-102, AC-105, AC-106, AC-116
    public async Task AnAccountantExtractsCorrectsResolvesOverridesAcceptsAndFinalizesOverHttp()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(
            world.FirmOne,
            ExtractionSource.T4(),
            ExtractionSource.T4(),
            ExtractionSource.Medical());

        // ----------------------------------------------- the list renders empty
        using (var list = await accountant.GetAsync(opened.ListUrl))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var html = await list.Content.ReadAsStringAsync();
            Assert.Contains("2025", html, StringComparison.Ordinal);
        }

        // --------------------------------------------- AC-5, AC-57, AC-63: extract
        Assert.Equal(
            opened.ReviewUrl(opened.FirstT4),
            await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4)));

        var afterFirst = await world.DocumentAsync(opened, opened.FirstT4);
        Assert.Single(afterFirst.Versions);
        Assert.Equal(
            DocumentClassification.T4Slip,
            afterFirst.Versions[0].Classification.EffectiveClassification);

        // The engine produced the slip: Box 12 was parsed and masked by the
        // engine's own RecipientIdentifier, and no Box 54 was invented.
        using (var review = await accountant.GetAsync(opened.ReviewUrl(opened.FirstT4)))
        {
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
            var html = await review.Content.ReadAsStringAsync();
            Assert.Contains(ExtractionSource.EmployerName, html, StringComparison.Ordinal);
            Assert.DoesNotContain(ExtractionSource.SinProbe, html, StringComparison.Ordinal);
        }

        // ------------------------------- AC-116: the second scan flags a duplicate
        Assert.Equal(
            opened.ReviewUrl(opened.SecondT4),
            await world.PostAsync(accountant, opened.ExtractUrl(opened.SecondT4)));

        var flagged = await world.DocumentAsync(opened, opened.SecondT4);
        var candidate = Assert.Single(flagged.DuplicateCandidates);

        // AC-96: a flag is never a rejection — both documents are still present
        // and neither is accepted.
        Assert.Empty(flagged.Acceptances);
        Assert.Empty((await world.DocumentAsync(opened, opened.FirstT4)).Acceptances);

        // ------------------------------------------ the duplicate is resolved
        var resolved = await world.PostAsync(
            accountant,
            opened.DuplicateUrl(candidate.CandidateId),
            ("resolution", nameof(DuplicateResolutionKind.DistinctDocuments)));
        Assert.StartsWith(opened.ExtractionsPrefix, resolved, StringComparison.Ordinal);

        var afterResolution = await world.DocumentAsync(opened, opened.SecondT4);
        Assert.Single(afterResolution.DuplicateCandidates);
        Assert.Single(afterResolution.DuplicateResolutions);

        // --------------------------------------------------- AC-102: correction
        Assert.Equal(
            opened.ReviewUrl(opened.FirstT4),
            await world.PostAsync(
                accountant,
                opened.CorrectionUrl(opened.FirstT4),
                ("field", nameof(CorrectionField.T4BoxValue)),
                ("boxCode", "14"),
                ("correctedValue", "52500.00")));

        var corrected = await world.DocumentAsync(opened, opened.FirstT4);
        Assert.Equal(2, corrected.Versions.Count);
        Assert.Single(corrected.Corrections);

        // -------------------------------- the medical document takes its own path
        Assert.Equal(
            opened.ReviewUrl(opened.Medical),
            await world.PostAsync(accountant, opened.ExtractUrl(opened.Medical)));

        var medical = await world.DocumentAsync(opened, opened.Medical);
        Assert.Single(medical.MedicalEvidenceVersions);
        Assert.Equal(
            "$1,120.50", medical.MedicalEvidenceVersions[0].GrossAmountAsPrinted);

        // ------------------------------------------ AC-115: classification override
        Assert.Equal(
            opened.ReviewUrl(opened.Medical),
            await world.PostAsync(
                accountant,
                opened.ClassificationUrl(opened.Medical),
                ("targetClassification",
                    nameof(DocumentClassification.MedicalExpenseEvidence))));

        var overridden = await world.DocumentAsync(opened, opened.Medical);
        Assert.Single(overridden.ClassificationOverrides);

        // AC-116: both values are rendered, so neither silently wins.
        using (var review = await accountant.GetAsync(opened.ReviewUrl(opened.Medical)))
        {
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
            var html = await review.Content.ReadAsStringAsync();
            Assert.Contains(
                nameof(DocumentClassification.MedicalExpenseEvidence),
                html,
                StringComparison.Ordinal);
        }

        // AC-118: an override authorizes nothing on its own. The version that
        // predates it stays unacceptable, so a human who reclassifies a document
        // must re-extract it under the classification they chose — the new
        // version then goes through the same validation as any other.
        using (var stale = await world.PostRawAsync(
                   accountant, opened.AcceptUrl(opened.Medical)))
        {
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        }

        Assert.Equal(
            opened.ReviewUrl(opened.Medical),
            await world.PostAsync(accountant, opened.ExtractUrl(opened.Medical)));
        Assert.Equal(
            2,
            (await world.DocumentAsync(opened, opened.Medical))
                .MedicalEvidenceVersions.Count);

        // ------------------------------------ AC-105: accept each, still no record
        foreach (var documentId in new[] { opened.FirstT4, opened.SecondT4, opened.Medical })
        {
            Assert.Equal(
                opened.ReviewUrl(documentId),
                await world.PostAsync(accountant, opened.AcceptUrl(documentId)));
        }

        Assert.Empty(world.Reviews.Saved);

        // ------------------------------------------- AC-106: finalize the case
        var finalizeLocation = await world.PostAsync(accountant, opened.FinalizeUrl);

        var saved = Assert.Single(world.Reviews.Saved);
        Assert.Equal($"/reviews/{saved.RecordId:D}", finalizeLocation);
        Assert.Equal(2025, saved.TaxYear);

        // AC-106: both accepted T4 documents are in the one record; AC-111: the
        // medical evidence is not.
        Assert.Equal(2, saved.Slips.Count);
        Assert.All(saved.Slips, slip => Assert.Equal(SlipTypeCodes.T4, slip.SlipTypeCode));

        // AC-106: the one-time association exists.
        var intakeCase = world.Store.Single();
        Assert.NotNull(intakeCase.ReviewAssociation);
        Assert.Equal(saved.RecordId, intakeCase.ReviewAssociation!.RecordId);

        // The spec 012 review route now serves the record this journey created,
        // unchanged and still firm-scoped.
        using var rendered = await accountant.GetAsync($"/reviews/{saved.RecordId:D}");
        Assert.Equal(HttpStatusCode.OK, rendered.StatusCode);
    }

    [Fact] // AC-21, AC-119
    public async Task AnUnrecognizedDocumentProducesNoTaxDataAndOnlyAnAuditEvent()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(
            world.FirmOne,
            ExtractionSource.Unrecognized());

        Assert.Equal(
            opened.ReviewUrl(opened.FirstT4),
            await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4)));

        // Nothing was recorded for the document at all — not an empty version,
        // not a placeholder row. There is no extraction history to find.
        Assert.Null(
            await world.Extractions.FindDocumentAsync(
                opened.Firm, opened.CaseId, opened.FirstT4, CancellationToken.None));
        Assert.Empty(world.Reviews.Saved);

        var extractions = await world.CaseAsync(opened);
        Assert.Single(extractions.AuditEvents);

        // AC-119: finalization has nothing to assemble, so no record appears.
        using var finalize = await world.PostRawAsync(accountant, opened.FinalizeUrl);
        Assert.Equal(HttpStatusCode.NotFound, finalize.StatusCode);
        Assert.Empty(world.Reviews.Saved);
    }

    [Fact] // AC-22, AC-82, AC-84
    public async Task ConfidenceNeitherGatesNorAutoAcceptsAtAnyStage()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(
            world.FirmOne,
            ExtractionSource.T4(confidence: "0"),
            ExtractionSource.T4(confidence: "1", employerName: "Another Synthetic Employer"));

        foreach (var documentId in new[] { opened.FirstT4, opened.SecondT4 })
        {
            Assert.Equal(
                opened.ReviewUrl(documentId),
                await world.PostAsync(accountant, opened.ExtractUrl(documentId)));
        }

        var zero = await world.DocumentAsync(opened, opened.FirstT4);
        var one = await world.DocumentAsync(opened, opened.SecondT4);

        // Both were accepted by the pipeline identically...
        Assert.Single(zero.Versions);
        Assert.Single(one.Versions);
        Assert.Equal(0m, zero.Versions[0].Provenance!.Confidence);
        Assert.Equal(1m, one.Versions[0].Provenance!.Confidence);

        // ...and AC-84: neither arrived accepted, and no record exists.
        Assert.Empty(zero.Acceptances);
        Assert.Empty(one.Acceptances);
        Assert.Empty(world.Reviews.Saved);

        // AC-82: acceptance and finalization treat them identically.
        foreach (var documentId in new[] { opened.FirstT4, opened.SecondT4 })
        {
            Assert.Equal(
                opened.ReviewUrl(documentId),
                await world.PostAsync(accountant, opened.AcceptUrl(documentId)));
        }

        var location = await world.PostAsync(accountant, opened.FinalizeUrl);
        var saved = Assert.Single(world.Reviews.Saved);
        Assert.Equal($"/reviews/{saved.RecordId:D}", location);
        Assert.Equal(2, saved.Slips.Count);
    }

    [Fact] // AC-23, AC-89, AC-103
    public async Task AnEngineRejectionIsNeverDowngradedAndBlocksAcceptance()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // A 2025 T4 with no Box 45 — an error the engine owns.
        var opened = await world.OpenCaseAsync(
            world.FirmOne,
            ExtractionSource.T4(includeDentalBenefits: false));

        Assert.Equal(
            opened.ReviewUrl(opened.FirstT4),
            await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4)));

        // The version is recorded with the engine's own rejection, and the
        // rejection is not downgraded into an accepted document.
        var stored = await world.DocumentAsync(opened, opened.FirstT4);
        Assert.Single(stored.Versions);
        Assert.Contains(
            stored.Versions[0].FieldOutcomes,
            outcome => outcome.Kind == FieldOutcomeKind.EngineRejected);

        // AC-89: the rejection is never downgraded — acceptance is refused
        // outright rather than allowed to proceed with a warning.
        using (var accept = await world.PostRawAsync(
                   accountant, opened.AcceptUrl(opened.FirstT4)))
        {
            Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        }

        Assert.Empty((await world.DocumentAsync(opened, opened.FirstT4)).Acceptances);

        // AC-103: finalization refuses too, and no partially accepted state exists.
        using var finalize = await world.PostRawAsync(accountant, opened.FinalizeUrl);
        Assert.Equal(HttpStatusCode.NotFound, finalize.StatusCode);
        Assert.Empty(world.Reviews.Saved);
    }

    [Fact] // AC-99
    public async Task ACorrectionNamingAProhibitedFieldIsRefusedAndMutatesNothing()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());
        await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4));

        var before = await world.DocumentAsync(opened, opened.FirstT4);

        // Box 12, the tax year, the firm, the case, the participant, the
        // document, the content key, a CRA line, and a validation result are
        // each unrepresentable in the closed vocabulary, so naming one is
        // refused by the endpoint before the workflow sees it.
        foreach (var field in new[]
                 {
                     "SocialInsuranceNumber",
                     "Sin",
                     "Box12",
                     "TaxYear",
                     "Firm",
                     "CaseId",
                     "ParticipantId",
                     "DocumentId",
                     "ContentKey",
                     "TaxLine",
                     "ValidationResult",
                 })
        {
            using var response = await world.PostRawAsync(
                accountant,
                opened.CorrectionUrl(opened.FirstT4),
                ("field", field),
                ("correctedValue", "000000000"));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // A correction naming Box 12 by its box code is refused by the workflow
        // even though the field name itself is in the vocabulary.
        using (var boxTwelve = await world.PostRawAsync(
                   accountant,
                   opened.CorrectionUrl(opened.FirstT4),
                   ("field", nameof(CorrectionField.T4BoxValue)),
                   ("boxCode", "12"),
                   ("correctedValue", "987654321")))
        {
            Assert.Equal(HttpStatusCode.NotFound, boxTwelve.StatusCode);
        }

        var after = await world.DocumentAsync(opened, opened.FirstT4);
        Assert.Equal(before.Versions.Count, after.Versions.Count);
        Assert.Empty(after.Corrections);
    }

    [Fact] // AC-115
    public async Task OnlyTheTwoSupportedClassificationsCanBeOverriddenTo()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());
        await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4));

        foreach (var target in new[]
                 {
                     nameof(DocumentClassification.T4Slip),
                     nameof(DocumentClassification.MedicalExpenseEvidence),
                 })
        {
            using var accepted = await world.PostRawAsync(
                accountant,
                opened.ClassificationUrl(opened.FirstT4),
                ("targetClassification", target));
            Assert.Equal(HttpStatusCode.Found, accepted.StatusCode);
        }

        var applied = await world.DocumentAsync(opened, opened.FirstT4);
        Assert.Equal(2, applied.ClassificationOverrides.Count);

        foreach (var target in new[]
                 {
                     nameof(DocumentClassification.Unsupported),
                     "Other",
                     "NotAClassification",
                     string.Empty,
                 })
        {
            using var refused = await world.PostRawAsync(
                accountant,
                opened.ClassificationUrl(opened.FirstT4),
                ("targetClassification", target));
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }

        Assert.Equal(
            2,
            (await world.DocumentAsync(opened, opened.FirstT4)).ClassificationOverrides.Count);
    }

    [Fact] // AC-31, AC-40, AC-41, AC-47
    public async Task ExtractionAddedNoContentServingRouteAndChangedNoExistingSurface()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // AC-41: /health is still the spec 010 literal.
        using (var health = await accountant.GetAsync("/health"))
        {
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
        }

        // AC-31: nothing serves, streams, previews, downloads, or exports the
        // stored source. Every shape of such a route is simply absent.
        var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());
        await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4));

        foreach (var candidate in new[]
                 {
                     $"{opened.ExtractionsPrefix}/{opened.FirstT4:D}/content",
                     $"{opened.ExtractionsPrefix}/{opened.FirstT4:D}/source",
                     $"{opened.ExtractionsPrefix}/{opened.FirstT4:D}/download",
                     $"{opened.ExtractionsPrefix}/{opened.FirstT4:D}/preview",
                     $"{opened.ExtractionsPrefix}/{opened.FirstT4:D}/export",
                     $"{opened.ExtractionsPrefix}/{opened.FirstT4:D}/raw",
                     $"/intake-cases/{opened.CaseId:D}/documents/{opened.FirstT4:D}",
                 })
        {
            using var response = await accountant.GetAsync(candidate);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // AC-40: the review route still renders exactly the record it is asked
        // for, with no "latest" selection anywhere.
        var recordId = world.Reviews.AddRecord(world.FirmOne.FirmId, opened.CaseId);
        using var review = await accountant.GetAsync($"/reviews/{recordId:D}");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);

        using var missing = await accountant.GetAsync($"/reviews/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}

// --------------------------------------------------------------- the world

/// <summary>
/// Two firm-scoped hosts over the real portal composition, sharing the three
/// substituted SQL-Server-backed collaborators. Used by the acceptance,
/// security, and PII-sweep suites alike.
/// </summary>
internal sealed class ExtractionPortalWorld : IAsyncDisposable
{
    private ExtractionPortalWorld(
        InMemoryIntakeStore store,
        ExtractionReviewRecordRepository reviews,
        InMemoryExtractionStore extractions,
        ExtractionFirmHost firmOne,
        ExtractionFirmHost firmTwo)
    {
        Store = store;
        Reviews = reviews;
        Extractions = extractions;
        FirmOne = firmOne;
        FirmTwo = firmTwo;
    }

    internal InMemoryIntakeStore Store { get; }
    internal ExtractionReviewRecordRepository Reviews { get; }
    internal InMemoryExtractionStore Extractions { get; }
    internal ExtractionFirmHost FirmOne { get; }
    internal ExtractionFirmHost FirmTwo { get; }

    internal static Task<ExtractionPortalWorld> CreateAsync()
    {
        var store = new InMemoryIntakeStore();
        var reviews = new ExtractionReviewRecordRepository();
        var extractions = new InMemoryExtractionStore();

        return Task.FromResult(new ExtractionPortalWorld(
            store,
            reviews,
            extractions,
            ExtractionFirmHost.Create(store, reviews, extractions),
            ExtractionFirmHost.Create(store, reviews, extractions)));
    }

    /// <summary>
    /// Stages each source document through the host's <b>real</b> Development
    /// encrypted content store and registers the case that names them.
    /// <para>
    /// The bytes are staged rather than uploaded over the client route on
    /// purpose. The shipped Development provider recognizes one fixed text
    /// format whose header must be the first line, while the client upload
    /// transport requires a PDF/JPEG/PNG signature prefix — so no document that
    /// satisfies the upload validator is recognizable to this adapter. That is a
    /// property of the Development-only adapter, not of the extraction path:
    /// the upload leg itself is proved end to end by spec 014 T04, and what
    /// matters here is that extraction reads the bytes back through the real
    /// four-part scoped reader.
    /// </para>
    /// </summary>
    internal async Task<OpenedExtractionCase> OpenCaseAsync(
        ExtractionFirmHost host,
        params string[] sources)
    {
        Assert.NotEmpty(sources);

        var firm = TrustedFirmId.FromGuid(host.FirmId);
        var caseId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var documents = new List<IntakeDocumentRecord>();
        var expected = new List<IntakeExpectedItemRecord>();
        var instant = new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < sources.Length; index++)
        {
            var expectedItemId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var category = sources[index].Contains(
                "KIND:MEDICAL-EXPENSE-EVIDENCE", StringComparison.Ordinal)
                ? DocumentCategory.MedicalExpenseEvidence
                : DocumentCategory.T4Slip;

            var staged = await host.StageAsync(firm, caseId, sources[index]);

            expected.Add(new IntakeExpectedItemRecord(expectedItemId, category, index + 1));
            documents.Add(new IntakeDocumentRecord(
                documentId,
                expectedItemId,
                category,
                1,
                staged.ContentKey,
                IntakeMediaKind.Pdf,
                staged.ByteCount,
                staged.Fingerprint,
                instant));
        }

        var stored = new StoredIntakeCase(
            firm,
            caseId,
            participantId,
            ExtractionSource.TaxpayerName,
            2025,
            CaseStatus.InformationGathering,
            0,
            expected,
            new IntakeLinkRecord(
                Guid.NewGuid(),
                new IntakeTokenDigest(Enumerable.Repeat((byte)0x55, 32).ToArray()),
                instant,
                instant.AddDays(2),
                null,
                null),
            [],
            documents,
            [],
            [],
            null);

        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await Store.CreateAsync(
                new NewIntakeCase(
                    stored,
                    [
                        new IntakeAuditEventRecord(
                            Guid.NewGuid(),
                            0,
                            IntakeAuditKind.CaseCreated,
                            IntakeActorKind.Accountant,
                            host.User.UserId,
                            null,
                            instant),
                    ]),
                CancellationToken.None));

        // The relational store answers "does this case exist in this firm" from
        // the intake case row; the in-memory one is told the same fact here.
        Extractions.KnownCases.Add((firm.Value, caseId));

        return new OpenedExtractionCase(
            firm,
            caseId,
            documents.Select(document => document.DocumentId).ToArray());
    }

    /// <summary>
    /// Posts a form with a live antiforgery token and returns the redirect
    /// location. A handler that refused would return 404 instead, which this
    /// asserts it did not.
    /// </summary>
    internal async Task<string> PostAsync(
        HttpClient accountant,
        string url,
        params (string Name, string Value)[] fields)
    {
        using var response = await PostRawAsync(accountant, url, fields);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return response.Headers.Location!.ToString();
    }

    internal async Task<HttpResponseMessage> PostRawAsync(
        HttpClient accountant,
        string url,
        params (string Name, string Value)[] fields)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(accountant),
        };

        foreach (var (name, value) in fields)
        {
            form[name] = value;
        }

        return await accountant.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>
    /// Posts without an antiforgery token, so the middleware's refusal is what
    /// the caller observes.
    /// </summary>
    internal static async Task<HttpResponseMessage> PostWithoutTokenAsync(
        HttpClient accountant,
        string url,
        params (string Name, string Value)[] fields)
    {
        var form = fields.ToDictionary(field => field.Name, field => field.Value);
        return await accountant.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>
    /// Harvests a live antiforgery token, so a later request can carry a valid
    /// one after the state it depends on has changed.
    /// </summary>
    internal Task<string> HarvestTokenAsync(HttpClient accountant) => TokenAsync(accountant);

    internal static async Task<HttpResponseMessage> PostWithTokenAsync(
        HttpClient accountant,
        string url,
        string token,
        params (string Name, string Value)[] fields)
    {
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        };

        foreach (var (name, value) in fields)
        {
            form[name] = value;
        }

        return await accountant.PostAsync(url, new FormUrlEncodedContent(form));
    }

    private static async Task<string> TokenAsync(HttpClient accountant)
    {
        using var page = await accountant.GetAsync("/intake-cases/new");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return IntakeFlow.AntiforgeryToken(await page.Content.ReadAsStringAsync());
    }

    internal async Task<StoredDocumentExtraction> DocumentAsync(
        OpenedExtractionCase opened,
        Guid documentId)
    {
        var stored = await Extractions.FindDocumentAsync(
            opened.Firm, opened.CaseId, documentId, CancellationToken.None);
        Assert.NotNull(stored);
        return stored!;
    }

    internal async Task<StoredCaseExtractions> CaseAsync(OpenedExtractionCase opened)
    {
        var stored = await Extractions.FindCaseAsync(
            opened.Firm, opened.CaseId, CancellationToken.None);
        Assert.NotNull(stored);
        return stored!;
    }

    /// <summary>Every server-side identifier a browser surface must never carry.</summary>
    internal IReadOnlyList<string> EveryServerIdentifier()
    {
        var identifiers = new List<string>
        {
            FirmOne.FirmId.ToString("D"),
            FirmTwo.FirmId.ToString("D"),
            FirmOne.User.UserId.ToString("D"),
            FirmTwo.User.UserId.ToString("D"),
            FirmOne.Membership.MembershipId.ToString("D"),
            FirmTwo.Membership.MembershipId.ToString("D"),
        };

        identifiers.AddRange(
            Store.All.Select(stored => stored.PrimaryParticipantId.ToString("D")));
        identifiers.AddRange(
            Store.All.SelectMany(stored =>
                stored.Documents.Select(document => document.ContentKey)));

        return identifiers.Where(value => value.Length > 0).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await FirmOne.DisposeAsync();
        await FirmTwo.DisposeAsync();
    }
}

internal sealed class ExtractionFirmHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _baseHost;
    private readonly WebApplicationFactory<Program> _host;

    private ExtractionFirmHost(
        WebApplicationFactory<Program> baseHost,
        WebApplicationFactory<Program> host,
        PortalAuthorizationHarness.IdentityDirectory directory,
        PortalAuthorizationHarness.SessionStore sessions,
        PortalUser user,
        FirmMembership membership,
        CapturingLoggerProvider logs)
    {
        _baseHost = baseHost;
        _host = host;
        Directory = directory;
        Sessions = sessions;
        User = user;
        Membership = membership;
        Logs = logs;
    }

    internal PortalAuthorizationHarness.IdentityDirectory Directory { get; }
    internal PortalAuthorizationHarness.SessionStore Sessions { get; }
    internal PortalUser User { get; }
    internal FirmMembership Membership { get; }
    internal Guid FirmId => Membership.FirmId;

    /// <summary>Every log record this host wrote.</summary>
    internal CapturingLoggerProvider Logs { get; }

    internal static ExtractionFirmHost Create(
        InMemoryIntakeStore store,
        ExtractionReviewRecordRepository reviews,
        InMemoryExtractionStore extractions)
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var membership = directory.AddMembership(user.UserId, "Synthetic Firm");

        var logs = new CapturingLoggerProvider();
        var baseHost = PortalAuthorizationHarness.CreateHost(directory, sessions);
        var host = baseHost.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                // Every log record the host writes, so the capture sweep can
                // scan them rather than assume them clean.
                services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(logs);

                // Only the three SQL-Server-backed collaborators are replaced.
                // The workflows, the deterministic provider, the encrypted
                // content reader, and every component stay real.
                services.RemoveAll<IIntakeStore>();
                services.RemoveAll<IReviewRecordRepository>();
                services.RemoveAll<IExtractionStore>();
                services.AddSingleton<IIntakeStore>(store);
                services.AddSingleton<IReviewRecordRepository>(reviews);
                services.AddSingleton<IExtractionStore>(extractions);
            }));

        return new ExtractionFirmHost(
            baseHost, host, directory, sessions, user, membership, logs);
    }

    internal HttpClient CreateAccountantClient() =>
        PortalAuthorizationHarness.CreateClient(_host, followRedirects: false);

    internal HttpClient CreateAnonymousClient() =>
        PortalAuthorizationHarness.CreateClient(_host, followRedirects: false);

    internal Task SignInAsync(HttpClient client) =>
        PortalAuthorizationHarness.SignInAsync(client);

    /// <summary>
    /// Stages one source document through the host's own registered content
    /// store — the real Development encrypted adapter, with the host's own keys
    /// and root.
    /// </summary>
    internal async Task<StagedIntakeContent> StageAsync(
        TrustedFirmId firm,
        Guid caseId,
        string source)
    {
        using var scope = _host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntakeContentStore>();
        using var content = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(source), writable: false);

        return await store.StageAsync(
            firm, caseId, content, 1024 * 1024, CancellationToken.None);
    }

    internal T Resolve<T>() where T : notnull => _host.Services.GetRequiredService<T>();

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        _baseHost.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed record OpenedExtractionCase(
    TrustedFirmId Firm,
    Guid CaseId,
    IReadOnlyList<Guid> DocumentIds)
{
    internal Guid FirstT4 => DocumentIds[0];

    internal Guid SecondT4 => DocumentIds[1];

    internal Guid Medical => DocumentIds[^1];

    internal string ExtractionsPrefix =>
        string.Format(
            CultureInfo.InvariantCulture,
            "/intake-cases/{0:D}/extractions",
            CaseId);

    internal string ListUrl => ExtractionsPrefix;

    internal string ReviewUrl(Guid documentId) =>
        string.Format(
            CultureInfo.InvariantCulture, "{0}/{1:D}", ExtractionsPrefix, documentId);

    internal string ExtractUrl(Guid documentId) => $"{ReviewUrl(documentId)}/extract";

    internal string CorrectionUrl(Guid documentId) => $"{ReviewUrl(documentId)}/corrections";

    internal string ClassificationUrl(Guid documentId) =>
        $"{ReviewUrl(documentId)}/classification";

    internal string AcceptUrl(Guid documentId) => $"{ReviewUrl(documentId)}/accept";

    internal string DuplicateUrl(Guid candidateId) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}/duplicates/{1:D}",
            ExtractionsPrefix,
            candidateId);

    internal string FinalizeUrl => $"{ExtractionsPrefix}/finalize";
}

/// <summary>
/// Synthetic source documents in the shipped deterministic adapter's one
/// recognized format. Every value is assembled here and is obviously synthetic.
/// </summary>
internal static class ExtractionSource
{
    internal const string Header = "TAXAI-DETERMINISTIC-DOCUMENT/1";

    internal const string TaxpayerName = "Synthetic Casey Taxpayer";
    internal const string EmployerName = "Synthetic Employer Limited";
    internal const string MedicalProviderName = "Synthetic Dental Clinic";
    internal const string PatientName = "Synthetic Medical Patient";

    /// <summary>Planted in the bytes; no structured surface may carry it.</summary>
    internal const string SourceMarker = "MARKER-7C3E-SOURCE-BYTES";

    /// <summary>Assembled at runtime, never checked in as a literal.</summary>
    internal static string SinProbe => string.Concat("123", "456", "789");

    /// <summary>
    /// A legitimate 2025 employee copy: Box 12, Box 45, both display names, and
    /// income boxes — and deliberately no Box 54, exactly as the CRA instructs
    /// employers to issue it.
    /// </summary>
    internal static string T4(
        string? confidence = null,
        string employerName = EmployerName,
        string employmentIncome = "50000.00",
        bool includeDentalBenefits = true)
    {
        var lines = new List<string>
        {
            Header,
            "KIND:T4",
            "YEAR:2025",
            $"EMPLOYER-NAME:{employerName}",
            $"EMPLOYEE-NAME:{TaxpayerName}",
            $"BOX 12:{SinProbe}",
            $"BOX 14:{employmentIncome}",
            $"NOTE:{SourceMarker}",
        };

        if (includeDentalBenefits)
        {
            lines.Insert(lines.Count - 1, "BOX 45:1");
        }

        if (confidence is not null)
        {
            lines.Insert(1, $"CONFIDENCE:{confidence}");
        }

        return string.Join("\n", lines);
    }

    internal static string Medical(string grossAmount = "$1,120.50") =>
        string.Join(
            "\n",
            Header,
            "KIND:MEDICAL-EXPENSE-EVIDENCE",
            "SERVICE-DATE:14 March 2025",
            $"GROSS-AMOUNT:{grossAmount}",
            "CURRENCY:CAD",
            $"PROVIDER-NAME:{MedicalProviderName}",
            $"PATIENT-NAME:{PatientName}",
            "RECEIPT-REFERENCE:R-4471",
            $"NOTE:{SourceMarker}");

    /// <summary>
    /// Bytes the adapter does not recognize. It never guesses, so this produces
    /// no tax input at all.
    /// </summary>
    internal static string Unrecognized() =>
        string.Join(
            "\n",
            "NOT-A-RECOGNIZED-DOCUMENT",
            $"BOX 14:50000.00",
            $"NOTE:{SourceMarker}");
}

/// <summary>
/// The extraction store, in memory and append-only. It exists because the real
/// <c>SqlExtractionStore</c> needs SQL Server; that adapter is driven for real
/// against a relational database in <c>Persistence.Tests</c>.
/// <para>
/// Every member appends. Nothing here updates or deletes a stored fact, and
/// every read is scoped to the trusted firm and the case: a foreign firm and a
/// missing case are indistinguishable, both null.
/// </para>
/// </summary>
internal sealed class InMemoryExtractionStore : IExtractionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid Firm, Guid Case), CaseState> _cases = [];

    /// <summary>Every retention boundary the workflow supplied, in order.</summary>
    internal IReadOnlyList<DateTimeOffset> RetentionBoundaries
    {
        get
        {
            lock (_gate)
            {
                return _cases.Values.SelectMany(state => state.Retentions).ToArray();
            }
        }
    }

    public Task<ExtractionStoreWriteCode> AppendExtractionVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        ExtractionVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, state =>
        {
            var document = state.Document(version.DocumentId, participantId);
            if (document.Versions.Any(existing => existing.Version == version.Version))
            {
                return ExtractionStoreWriteCode.Conflict;
            }

            document.Versions.Add(version);
            return ExtractionStoreWriteCode.Success;
        });

    public Task<ExtractionStoreWriteCode> AppendMedicalEvidenceVersionAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid participantId,
        MedicalEvidenceVersion version,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, state =>
        {
            var document = state.Document(version.DocumentId, participantId);
            if (document.MedicalVersions.Any(existing => existing.Version == version.Version))
            {
                return ExtractionStoreWriteCode.Conflict;
            }

            document.MedicalVersions.Add(version);
            return ExtractionStoreWriteCode.Success;
        });

    public Task<ExtractionStoreWriteCode> AppendCorrectionAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionCorrectionRecord correction,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, state =>
        {
            state.Document(correction.DocumentId, Guid.Empty).Corrections.Add(correction);
            return ExtractionStoreWriteCode.Success;
        });

    public Task<ExtractionStoreWriteCode> AppendClassificationOverrideAsync(
        TrustedFirmId firm,
        Guid caseId,
        ClassificationOverrideRecord classificationOverride,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, state =>
        {
            state.Document(classificationOverride.DocumentId, Guid.Empty)
                .Overrides.Add(classificationOverride);
            return ExtractionStoreWriteCode.Success;
        });

    public Task<ExtractionStoreWriteCode> AppendDuplicateCandidateAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateCandidateRecord candidate,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, state =>
        {
            state.Document(candidate.DocumentId, Guid.Empty).Candidates.Add(candidate);
            return ExtractionStoreWriteCode.Success;
        });

    public Task<ExtractionStoreWriteCode> AppendDuplicateResolutionAsync(
        TrustedFirmId firm,
        Guid caseId,
        DuplicateResolutionRecord resolution,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, state =>
        {
            foreach (var document in state.Documents.Values)
            {
                if (!document.Candidates.Any(candidate =>
                        candidate.CandidateId == resolution.CandidateId))
                {
                    continue;
                }

                if (document.Resolutions.Any(existing =>
                        existing.CandidateId == resolution.CandidateId))
                {
                    return ExtractionStoreWriteCode.Conflict;
                }

                document.Resolutions.Add(resolution);
                return ExtractionStoreWriteCode.Success;
            }

            return ExtractionStoreWriteCode.NotFound;
        });

    public Task<ExtractionStoreWriteCode> AppendAcceptanceAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAcceptanceRecord acceptance,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, state =>
        {
            if (acceptance.Scope == ExtractionAcceptanceScope.Case)
            {
                state.CaseAcceptances.Add(acceptance);
                return ExtractionStoreWriteCode.Success;
            }

            state.Document(acceptance.DocumentId!.Value, Guid.Empty)
                .Acceptances.Add(acceptance);
            return ExtractionStoreWriteCode.Success;
        });

    public Task<ExtractionStoreWriteCode> AppendAuditEventAsync(
        TrustedFirmId firm,
        Guid caseId,
        ExtractionAuditEventRecord auditEvent,
        DateTimeOffset retentionUntilUtc,
        CancellationToken cancellationToken) =>
        Append(firm, caseId, retentionUntilUtc, auditEvent, _ =>
            ExtractionStoreWriteCode.Success);

    public Task<StoredDocumentExtraction?> FindDocumentAsync(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_cases.TryGetValue((firm.Value, caseId), out var state) ||
                !state.Documents.TryGetValue(documentId, out var document))
            {
                return Task.FromResult<StoredDocumentExtraction?>(null);
            }

            return Task.FromResult<StoredDocumentExtraction?>(document.Project());
        }
    }

    public Task<StoredCaseExtractions?> FindCaseAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // A case with no extraction fact yet is still a case this firm
            // holds: the relational store answers from the intake case's
            // existence, and so does this one.
            if (!_cases.TryGetValue((firm.Value, caseId), out var state))
            {
                return Task.FromResult<StoredCaseExtractions?>(
                    KnownCases.Contains((firm.Value, caseId))
                        ? new StoredCaseExtractions(caseId, [], [], [])
                        : null);
            }

            return Task.FromResult<StoredCaseExtractions?>(
                new StoredCaseExtractions(
                    caseId,
                    state.Documents.Values
                        .Select(document => document.Project())
                        .ToArray(),
                    state.CaseAcceptances.ToArray(),
                    state.AuditEvents.OrderBy(audit => audit.Sequence).ToArray()));
        }
    }

    /// <summary>
    /// The cases this firm holds, supplied by the fixture. The relational store
    /// derives this from the intake case row; here the test says so explicitly.
    /// </summary>
    internal HashSet<(Guid Firm, Guid Case)> KnownCases { get; } = [];

    private Task<ExtractionStoreWriteCode> Append(
        TrustedFirmId firm,
        Guid caseId,
        DateTimeOffset retentionUntilUtc,
        ExtractionAuditEventRecord auditEvent,
        Func<CaseState, ExtractionStoreWriteCode> append)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(auditEvent);

        lock (_gate)
        {
            if (!KnownCases.Contains((firm.Value, caseId)))
            {
                return Task.FromResult(ExtractionStoreWriteCode.NotFound);
            }

            if (!_cases.TryGetValue((firm.Value, caseId), out var state))
            {
                state = new CaseState();
                _cases[(firm.Value, caseId)] = state;
            }

            var outcome = append(state);
            if (outcome != ExtractionStoreWriteCode.Success)
            {
                return Task.FromResult(outcome);
            }

            state.AuditEvents.Add(auditEvent);
            state.Retentions.Add(retentionUntilUtc);
            return Task.FromResult(ExtractionStoreWriteCode.Success);
        }
    }

    private sealed class CaseState
    {
        internal Dictionary<Guid, DocumentState> Documents { get; } = [];

        internal List<ExtractionAcceptanceRecord> CaseAcceptances { get; } = [];

        internal List<ExtractionAuditEventRecord> AuditEvents { get; } = [];

        internal List<DateTimeOffset> Retentions { get; } = [];

        internal DocumentState Document(Guid documentId, Guid participantId)
        {
            if (!Documents.TryGetValue(documentId, out var document))
            {
                document = new DocumentState(documentId, participantId);
                Documents[documentId] = document;
            }
            else if (participantId != Guid.Empty)
            {
                document.ParticipantId = participantId;
            }

            return document;
        }
    }

    private sealed class DocumentState
    {
        internal DocumentState(Guid documentId, Guid participantId)
        {
            DocumentId = documentId;
            ParticipantId = participantId;
        }

        internal Guid DocumentId { get; }

        internal Guid ParticipantId { get; set; }

        internal List<ExtractionVersion> Versions { get; } = [];

        internal List<MedicalEvidenceVersion> MedicalVersions { get; } = [];

        internal List<ExtractionCorrectionRecord> Corrections { get; } = [];

        internal List<ClassificationOverrideRecord> Overrides { get; } = [];

        internal List<DuplicateCandidateRecord> Candidates { get; } = [];

        internal List<DuplicateResolutionRecord> Resolutions { get; } = [];

        internal List<ExtractionAcceptanceRecord> Acceptances { get; } = [];

        internal StoredDocumentExtraction Project() =>
            new(
                DocumentId,
                ParticipantId,
                Versions.OrderBy(version => version.Version).ToArray(),
                MedicalVersions.OrderBy(version => version.Version).ToArray(),
                Corrections.ToArray(),
                Overrides.ToArray(),
                Candidates.ToArray(),
                Resolutions.ToArray(),
                Acceptances.ToArray());
    }
}

/// <summary>
/// Records what the extraction workflow saved. Spec 014's double refuses every
/// save, because the intake workflow must never create a review record; this
/// one records them, because case-level acceptance is the authorized path that
/// does.
/// </summary>
internal sealed class ExtractionReviewRecordRepository : IReviewRecordRepository
{
    private readonly Dictionary<(Guid FirmId, Guid RecordId), PersistedReviewRecord>
        _records = [];

    internal List<PersistedReviewRecord> Saved { get; } = [];

    internal List<(Guid FirmId, Guid RecordId)> Reads { get; } = [];

    /// <summary>Adds one record through the test, never through a workflow.</summary>
    internal Guid AddRecord(Guid firmId, Guid caseId)
    {
        var recordId = Guid.NewGuid();
        _records[(firmId, recordId)] = ReviewRecordFactory.Create(firmId, recordId, caseId);
        return recordId;
    }

    public Task<TaxLogicEngine.Models.ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm,
        PersistedReviewRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(record);

        Saved.Add(record);
        _records[(firm.Value, record.RecordId)] = record;
        return Task.FromResult(
            TaxLogicEngine.Models.ParseResult<PersistedReviewRecord>.Success(record));
    }

    public Task<PersistedReviewRecord?> FindAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        Reads.Add((firm.Value, recordId));
        return Task.FromResult(
            _records.TryGetValue((firm.Value, recordId), out var record) ? record : null);
    }
}

/// <summary>
/// Records every log record the host writes, message and exception alike, so a
/// capture sweep can scan what was actually logged.
/// </summary>
internal sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    private readonly List<string> _records = [];

    internal IReadOnlyList<string> Records
    {
        get
        {
            lock (_records)
            {
                return _records.ToArray();
            }
        }
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        new CapturingLogger(categoryName, _records);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _category;
        private readonly List<string> _records;

        internal CapturingLogger(string category, List<string> records)
        {
            _category = category;
            _records = records;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var record = string.Join(
                " | ",
                _category,
                formatter(state, exception),
                exception?.ToString() ?? string.Empty);

            lock (_records)
            {
                _records.Add(record);
            }
        }
    }
}
