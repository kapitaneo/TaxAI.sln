using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CaseProcessing.Services;
using CaseReview.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using DocumentIntelligence;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Persistence;
using TaxAI.Portal.Tests.Identity;
using TaxCases.Models;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 017 T04 — the complete local accountant workflow, driven over HTTP
/// against the real Development composition exactly as a person with two
/// browsers would drive it.
/// <para>
/// Every request a journey sends goes to an address the application itself
/// offered: an <c>href</c>, a form <c>action</c>, a script <c>src</c>, or a
/// redirect <c>Location</c> taken from a response already received. The only
/// addresses given from outside are the two a person is given: the Development
/// sign-in address the local tool prints, and the one-time client link shown
/// once on the issued page. A request to any other address fails the test, so
/// a missing link is a failure and not something a test quietly works around.
/// </para>
/// <para>
/// Sources reach the application only as a browser's upload would: a multipart
/// <c>POST</c> to the address the tracked upload module names, through the
/// unchanged signature gate, streaming and encryption. Only the SQL-backed
/// collaborators are substituted, as in the spec 015 portal suite; their
/// relational counterparts carry the same journey in
/// <c>Persistence.Tests</c>.
/// </para>
/// <para>
/// The recipient-identifier value is assembled at run time. No nine-digit
/// literal exists in this file.
/// </para>
/// </summary>
public sealed class LocalMvpVerticalSliceTests
{
    /// <summary>The address <c>Start-LocalDemo.ps1</c> prints.</summary>
    private const string DevelopmentSignInAddress = "/development/sign-in";

    private const string Signature = "%PDF-1.7";

    private const string Header = DeterministicDocumentIntelligenceProvider.DocumentHeader;

    private const string TaxpayerName = "Synthetic Demo Taxpayer";

    private const string EmployerName = "Synthetic Employer Ltd.";

    private const string EmployeeName = "Synthetic Employee";

    private const string MedicalProviderName = "Synthetic Dental Clinic";

    private const string MedicalReceiptReference = "R-SYNTHETIC-DEMO";

    private const string MedicalGrossAmount = "120.50";

    private const string CorrectedBoxCode = "14";

    private const string CorrectedBoxValue = "51000.00";

    private const string AntiforgeryField = "__RequestVerificationToken";

    // ---------------------------------------------------------- AC-1, AC-5

    [Fact] // AC-1, AC-5
    public async Task TheT4FlowCompletesFromSignInToTheReviewOutputThroughRenderedLinksAndForms()
    {
        await using var world = World.Create();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = StandardBoxes(NewRecipientIdentifier()),
            Correction = (CorrectedBoxCode, CorrectedBoxValue),
        });

        // The redirect after finalization was followed to the review output.
        Assert.Equal(HttpStatusCode.OK, journey.Review!.Status);
        Assert.Matches(@"^/reviews/[0-9a-f\-]{36}$", journey.Review.Address);
        Assert.Contains("Review package", journey.Review.Body, StringComparison.Ordinal);

        var rendered = RenderedReview.Parse(journey.Review.Body);
        Assert.Contains($"{CorrectedBoxCode}={CorrectedBoxValue}", rendered.Boxes);
        Assert.DoesNotContain(rendered.Boxes, box => box.EndsWith("=50000.00", StringComparison.Ordinal));
        Assert.NotEmpty(rendered.Lines);

        // The correction is a second immutable version, not an edit.
        var stored = await world.FindDocumentAsync(journey.T4DocumentId);
        Assert.Equal(2, stored!.Versions.Count);
        Assert.Single(stored.Corrections);
        var record = Assert.Single(world.Portal.Reviews.All);
        Assert.Single(record.Slips);
        Assert.Equal(record.RecordId, world.StoredCase.ReviewAssociation!.RecordId);

        // AC-5: every step was reached, and each by an address the application
        // offered. The browsers refuse any other address, so reaching the end
        // is itself the proof; the steps are listed so a shortened journey
        // cannot pass.
        Assert.Equal(
            new[]
            {
                "GET /development/sign-in",
                "POST /development/sign-in",
                "GET /intake-cases",
                "GET /intake-cases/new",
                "POST /intake-cases",
                "GET /intake-cases/{case}",
                "GET /intake-cases/{case}/extractions",
                "GET /intake-cases/{case}/extractions/{document}",
                "POST /intake-cases/{case}/extractions/{document}/extract",
                "POST /intake-cases/{case}/extractions/{document}/corrections",
                "POST /intake-cases/{case}/extractions/{document}/accept",
                "POST /intake-cases/{case}/extractions/finalize",
                "GET /reviews/{record}",
            },
            journey.Accountant.Steps());
        Assert.Equal(
            new[]
            {
                "GET /client-intake/link/{credential}",
                "POST /client-intake/redeem",
                "GET /client-intake",
                "POST /client-intake/answers",
                "GET /Components/Shared/ClientQuestionnairePanel.razor.js",
                "POST /client-intake/documents",
                "POST /client-intake/submissions",
            },
            journey.Client.Steps());

        // The only addresses supplied from outside the application.
        Assert.Equal(new[] { DevelopmentSignInAddress }, journey.Accountant.GivenAddresses);
        Assert.Matches(
            @"^/client-intake/link/[A-Za-z0-9\-_]{43}$",
            Assert.Single(journey.Client.GivenAddresses));
    }

    [Fact] // AC-1: the sign-in is the Development one and the session is the durable one.
    public async Task TheJourneyRunsOnTheRealDevelopmentCompositionAndTheDurableSessionPath()
    {
        await using var world = World.Create();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = StandardBoxes(NewRecipientIdentifier()),
        });

        Assert.Equal(HttpStatusCode.OK, journey.Review!.Status);
        Assert.Equal("Development", world.Host.Resolve<IHostEnvironment>().EnvironmentName);

        using var scope = world.Host.Resolve<IServiceScopeFactory>().CreateScope();
        Assert.IsType<DeterministicDocumentIntelligenceProvider>(
            scope.ServiceProvider.GetRequiredService<IDocumentIntelligenceProvider>());
        Assert.Equal(
            "ExtractionIngestWorkflow",
            scope.ServiceProvider.GetRequiredService<IExtractionIngestWorkflow>().GetType().Name);
        Assert.Equal(
            "ExtractionReviewWorkflow",
            scope.ServiceProvider.GetRequiredService<IExtractionReviewWorkflow>().GetType().Name);

        // One durable session, for the seeded user and firm, created by the
        // ordinary directory lookup of exactly the seeded pair.
        var session = Assert.Single(world.Host.Sessions.All);
        Assert.Equal(world.Directory.User.UserId, session.UserId);
        Assert.Equal(world.Directory.Membership.FirmId, session.ActiveFirmId);
        Assert.Equal(
            (DevelopmentFixtureIdentity.Issuer, DevelopmentFixtureIdentity.Subject),
            world.Directory.LastIssuerSubjectLookup);
        Assert.Equal(world.Directory.Membership.FirmId, world.StoredCase.Firm.Value);
    }

    [Fact] // AC-5: the case list is how a case is selected.
    public async Task TheCaseListOffersTheCreatedCaseByNameAndStoredYear()
    {
        await using var world = World.Create();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = StandardBoxes(NewRecipientIdentifier()),
        });

        var emptyList = journey.Accountant.Exchanges.First(exchange =>
            exchange is { Method: "GET", Address: "/intake-cases" });
        Assert.Contains("No cases yet.", emptyList.Body, StringComparison.Ordinal);

        var list = journey.Accountant.Exchanges.Last(exchange =>
            exchange is { Method: "GET", Address: "/intake-cases" });
        Assert.Matches($"<strong[^>]*>{Regex.Escape(TaxpayerName)}</strong>", list.Body);
        Assert.Matches(
            FormattableString.Invariant($"<span[^>]*>Tax year {world.StoredCase.TaxYear}</span>"),
            list.Body);
    }

    // ---------------------------------------------------------- AC-2, AC-3

    [Fact] // AC-2
    public async Task TheMedicalFlowCompletesAndStaysOutOfTheReviewRecord()
    {
        await using var world = World.Create();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = StandardBoxes(NewRecipientIdentifier()),
            IncludeMedical = true,
            AcceptMedical = true,
        });

        // The extraction-review page of the medical document shows the neutral
        // evidence panel.
        var panel = journey.MedicalReviewPage!.Body;
        Assert.Matches("<h2 id=\"medical-evidence-heading\"[^>]*>Medical expense evidence</h2>", panel);
        Assert.Contains("No tax treatment is determined here.", panel, StringComparison.Ordinal);
        Assert.Contains(MedicalProviderName, panel, StringComparison.Ordinal);
        Assert.Contains(MedicalReceiptReference, panel, StringComparison.Ordinal);
        Assert.Contains(MedicalGrossAmount, panel, StringComparison.Ordinal);

        var medical = await world.FindDocumentAsync(journey.MedicalDocumentId);
        Assert.Single(medical!.MedicalEvidenceVersions);
        Assert.Empty(medical.Versions);
        Assert.Single(medical.Acceptances);

        // The T4 was finalized, and nothing medical reached the review record
        // or the review output.
        Assert.Equal(HttpStatusCode.OK, journey.Review!.Status);
        var record = Assert.Single(world.Portal.Reviews.All);
        var slip = Assert.Single(record.Slips);
        Assert.Equal("T4", slip.SlipTypeCode);

        foreach (var surface in new[] { journey.Review.Body, record.ToString(), slip.ToString() })
        {
            foreach (var medicalValue in new[]
                     {
                         MedicalProviderName, MedicalReceiptReference, MedicalGrossAmount,
                         "Medical", "medical", "2025-03-14",
                     })
            {
                Assert.DoesNotContain(medicalValue, surface, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain(
            RenderedReview.Parse(journey.Review.Body).Boxes,
            box => box.Contains(MedicalGrossAmount, StringComparison.Ordinal));
    }

    [Fact] // AC-3
    public async Task AnUnacceptedMedicalDocumentDoesNotGateFinalization()
    {
        var recipientIdentifier = NewRecipientIdentifier();

        await using var without = World.Create();
        var baseline = await Journey.RunAsync(without, new JourneyPlan
        {
            T4Boxes = StandardBoxes(recipientIdentifier),
        });

        await using var with = World.Create();
        var journey = await Journey.RunAsync(with, new JourneyPlan
        {
            T4Boxes = StandardBoxes(recipientIdentifier),
            IncludeMedical = true,
            AcceptMedical = false,
        });

        var medical = await with.FindDocumentAsync(journey.MedicalDocumentId);
        Assert.Single(medical!.MedicalEvidenceVersions);
        Assert.Empty(medical.Acceptances);

        // Finalization succeeded, and produced exactly what it produces when no
        // medical document exists at all.
        Assert.Equal(HttpStatusCode.OK, journey.Review!.Status);
        Assert.Equal(HttpStatusCode.OK, baseline.Review!.Status);
        Assert.Equal(baseline.Review.Body, journey.Review.Body);

        var record = Assert.Single(with.Portal.Reviews.All);
        var baselineRecord = Assert.Single(without.Portal.Reviews.All);
        Assert.Equal(baselineRecord.Slips.Count, record.Slips.Count);
        Assert.Single(record.Slips);
    }

    // -------------------------------------------------------- AC-47, AC-48

    [Fact] // AC-47
    public async Task AnEmptySlipFailsClosedThroughTheBridge()
    {
        await using var world = World.Create();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = [],
            ExpectAcceptanceRefused = true,
        });

        AssertAcceptanceRefusedAndNothingProduced(world, journey);
        var stored = await world.FindDocumentAsync(journey.T4DocumentId);
        Assert.NotNull(stored);
        Assert.Empty(stored!.Acceptances);
    }

    [Fact] // AC-48
    public async Task AMissingRequiredBoxFailsClosedWithoutNamingTheBoxOrTheIdentifier()
    {
        await using var world = World.Create();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            // Every standard box except the recipient identifier.
            T4Boxes = StandardBoxes(NewRecipientIdentifier()).Where(box => box.Code != "12").ToArray(),
            ExpectAcceptanceRefused = true,
        });

        AssertAcceptanceRefusedAndNothingProduced(world, journey);

        // No response of the journey — the refusal, the page rendered before it,
        // or the page rendered after it — names the box or the identifier.
        Assert.NotEmpty(journey.Accountant.Exchanges);
        foreach (var exchange in journey.Accountant.Exchanges.Concat(journey.Client.Exchanges))
        {
            var text = Opaque.Remove(exchange.Everything);
            Assert.DoesNotMatch(SinWord, text);
            Assert.DoesNotMatch(BoxTwelve, text);
            Assert.DoesNotContain("social insurance", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recipient identifier", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // -------------------------------------------------------- AC-49, AC-88

    [Fact] // AC-88
    public async Task TheReviewOutputEqualsWhatTheExistingChainProducesForTheCorrectedPayload()
    {
        await using var world = World.Create();
        var recipientIdentifier = NewRecipientIdentifier();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = StandardBoxes(recipientIdentifier),
            Correction = (CorrectedBoxCode, CorrectedBoxValue),
        });

        var corrected = StandardBoxes(recipientIdentifier)
            .Select(box => box.Code == CorrectedBoxCode ? (box.Code, CorrectedBoxValue) : box)
            .ToArray();
        var expected = ExistingChain(world, corrected);
        var rendered = RenderedReview.Parse(journey.Review!.Body);

        Assert.NotEmpty(expected.Boxes);
        Assert.NotEmpty(expected.Lines);
        Assert.Equal(expected.Boxes, rendered.Boxes);
        Assert.Equal(expected.Lines, rendered.Lines);
    }

    [Fact] // AC-49
    public async Task AZeroAmountAndNoOptionalBoxRenderExactlyWhatTheExistingChainProduces()
    {
        await using var world = World.Create();
        (string Code, string Value)[] boxes =
        [
            ("12", NewRecipientIdentifier()),
            ("14", "0.00"),
            ("45", "1"),
        ];
        var journey = await Journey.RunAsync(world, new JourneyPlan { T4Boxes = boxes });

        Assert.Equal(HttpStatusCode.OK, journey.Review!.Status);
        var expected = ExistingChain(world, boxes);
        var rendered = RenderedReview.Parse(journey.Review.Body);

        Assert.Equal(expected.Boxes, rendered.Boxes);
        Assert.Equal(expected.Lines, rendered.Lines);
        Assert.Single(Assert.Single(world.Portal.Reviews.All).Slips);
    }

    // -------------------------------------------------------- AC-56, AC-73

    [Fact] // AC-56 (the HTTP, log, and record half; the relational half is in Persistence.Tests)
    public async Task ThePlantedIdentifierReachesNoResponseAddressCookieHeaderLogOrRecord()
    {
        var logs = new ClientIntakeSecurityAcceptanceTests.RecordingLoggerProvider();
        await using var world = World.Create(logs);
        var planted = NewRecipientIdentifier();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = StandardBoxes(planted),
            Correction = (CorrectedBoxCode, CorrectedBoxValue),
            IncludeMedical = true,
            AcceptMedical = true,
        });
        Assert.Equal(HttpStatusCode.OK, journey.Review!.Status);

        var stored = world.StoredCase;
        var extractions = await world.Portal.Extractions.FindCaseAsync(
            stored.Firm, stored.CaseId, CancellationToken.None);
        var surfaces = journey.Accountant.Exchanges
            .Concat(journey.Client.Exchanges)
            .Select(exchange => exchange.Everything)
            .Concat(logs.Messages)
            .Concat(world.Portal.Intake.Store.Audit(stored.Firm.Value, stored.CaseId).Select(audit => audit.ToString()))
            .Concat(extractions!.AuditEvents.Select(audit => audit.ToString()))
            .Concat(extractions.Documents.SelectMany(document =>
                document.Versions.Select(version => version.ToString())))
            .Concat(world.Portal.Reviews.All.Select(record => record.ToString()))
            .Append(stored.ToString())
            .ToArray();

        // The capture is real: it holds the pages, both session cookies, and
        // the host's own log lines.
        Assert.True(journey.Accountant.Exchanges.Count > 15);
        Assert.Contains(surfaces, surface => surface.Contains("Set-Cookie: taxai.session=", StringComparison.Ordinal));
        Assert.Contains(surfaces, surface => surface.Contains("Set-Cookie: __Secure-taxai.client-intake=", StringComparison.Ordinal));
        Assert.NotEmpty(logs.Messages);

        var grouped = $"{planted[..3]}-{planted[3..6]}-{planted[6..]}";
        var spaced = $"{planted[..3]} {planted[3..6]} {planted[6..]}";
        var maskedOrPartial = new Regex(
            @"[*#•xX]{2,}[ \-]?[*#•xX]{0,3}[ \-]?(?:" +
            Regex.Escape(planted[^4..]) + "|" + Regex.Escape(planted[^3..]) + @")(?![0-9])",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        // Control: removing opaque material does not hide a rendering. A
        // rendered value, a masked one, and a partial one are each still found.
        Assert.Matches(NineDigitRun, Opaque.Remove($"<td>{planted}</td>"));
        Assert.Matches(NineDigitRun, Opaque.Remove($"/intake-cases/{Guid.NewGuid():D}?value={planted}"));
        Assert.Matches(maskedOrPartial, Opaque.Remove($"<td>***-***-{planted[^3..]}</td>"));
        Assert.Matches(maskedOrPartial, Opaque.Remove($"<td>XXXXX{planted[^4..]}</td>"));

        Assert.All(surfaces, surface =>
        {
            // The value itself and its separator variants, against the raw text.
            Assert.DoesNotContain(planted, surface, StringComparison.Ordinal);
            Assert.DoesNotContain(grouped, surface, StringComparison.Ordinal);
            Assert.DoesNotContain(spaced, surface, StringComparison.Ordinal);
            Assert.DoesNotContain(Header, surface, StringComparison.Ordinal);

            // A masked, partial, or any other nine-digit rendering. Random
            // opaque material — antiforgery tokens, protected cookies, the
            // one-time credential, opaque identifiers — is removed first: a run
            // of digits inside such a value is chance, not a rendering.
            var text = Opaque.Remove(surface);
            Assert.DoesNotMatch(NineDigitRun, text);
            Assert.DoesNotMatch(maskedOrPartial, text);
            Assert.DoesNotContain(planted[..6], text, StringComparison.Ordinal);
            Assert.DoesNotContain(planted[^6..], text, StringComparison.Ordinal);
        });
    }

    [Fact] // AC-73
    public async Task NoClientResponseOffersAnAccountantAddressOrControl()
    {
        await using var world = World.Create();
        var journey = await Journey.RunAsync(world, new JourneyPlan
        {
            T4Boxes = StandardBoxes(NewRecipientIdentifier()),
            IncludeMedical = true,
            AcceptMedical = true,
        });
        var caseId = world.StoredCase.CaseId.ToString("D");

        Assert.True(journey.Client.Exchanges.Count >= 8);
        foreach (var exchange in journey.Client.Exchanges)
        {
            foreach (var forbidden in new[]
                     {
                         "/intake-cases", "/reviews", "/sign-out", "/development",
                         "aria-label=\"Accountant\"", caseId,
                     })
            {
                Assert.DoesNotContain(forbidden, exchange.Everything, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ----------------------------------------------------------------- AC-4

    [Fact] // AC-4
    public void ThisSuiteNeverStagesContentDirectly()
    {
        // The forbidden names are assembled here so this scan does not find
        // itself.
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "tests", "TaxAI.Portal.Tests", "EndToEnd", "LocalMvpVerticalSliceTests.cs"));

        foreach (var forbidden in new[]
                 {
                     "Stage" + "Async",
                     "Content" + "Store",
                     "Staged" + "Intake",
                     "Content" + "Reader",
                     "SeedFirm" + "OneAsync",
                     "Content" + "Root",
                     "Content" + "Key",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // And the source reaches the host by the multipart route, once.
        Assert.Single(Regex.Matches(source, @"new MultipartFormDataContent\("));
    }

    // ---------------------------------------------------------------- sources

    private static (string Code, string Value)[] StandardBoxes(string recipientIdentifier) =>
    [
        ("12", recipientIdentifier),
        ("14", "50000.00"),
        ("45", "1"),
    ];

    /// <summary>The shape the local sample generator writes.</summary>
    private static byte[] T4Source(IReadOnlyList<(string Code, string Value)> boxes) =>
        Encoding.UTF8.GetBytes(string.Join(
            '\n',
            new[]
            {
                Signature,
                Header,
                "KIND: T4",
                "YEAR: 2025",
                $"EMPLOYER-NAME: {EmployerName}",
                $"EMPLOYEE-NAME: {EmployeeName}",
            }.Concat(boxes.Select(box => $"BOX {box.Code}: {box.Value}"))) + "\n");

    private static byte[] MedicalSource() => Encoding.UTF8.GetBytes(string.Join(
        '\n',
        Signature,
        Header,
        "KIND: MEDICAL-EXPENSE-EVIDENCE",
        "SERVICE-DATE: 2025-03-14",
        $"GROSS-AMOUNT: {MedicalGrossAmount}",
        "CURRENCY: CAD",
        $"PROVIDER-NAME: {MedicalProviderName}",
        $"PATIENT-NAME: {EmployeeName}",
        $"RECEIPT-REFERENCE: {MedicalReceiptReference}") + "\n");

    /// <summary>Assembled at run time; never a literal in a tracked file.</summary>
    private static string NewRecipientIdentifier() =>
        string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(0, 10)));

    // --------------------------------------------------------------- the chain

    /// <summary>
    /// Invokes the existing validation and tax chain directly on a payload: the
    /// spec 007 gate, the host's own <see cref="T4CaseProcessor"/>, and the
    /// spec 009 assembler. Nothing here computes, maps, or rounds; the amounts
    /// are formatted exactly as the review endpoint formats a stored amount.
    /// </summary>
    private static (string[] Boxes, string[] Lines) ExistingChain(
        World world,
        IReadOnlyList<(string Code, string Value)> boxes)
    {
        var stored = world.StoredCase;
        var validated = ExtractionEnvelopeValidator.Validate(new ExtractionEnvelope
        {
            DocumentId = Guid.NewGuid().ToString("D"),
            Kind = ExtractedDocumentKind.T4Slip,
            FirmId = stored.Firm.ToCanonicalString(),
            CaseId = stored.CaseId.ToString("D"),
            ParticipantId = stored.PrimaryParticipantId.ToString("D"),
            TaxYear = stored.TaxYear,
            Provenance = new ExtractionProvenance
            {
                SourceReference = Guid.NewGuid().ToString("D"),
                ProviderLabel = "direct-chain-comparison",
            },
            T4Payload = new T4RawInput
            {
                Year = stored.TaxYear,
                EmployerName = EmployerName,
                EmployeeName = EmployeeName,
                Boxes = boxes.Select(box => new T4RawBox(box.Code, box.Value)).ToArray(),
            },
        });
        Assert.True(validated.IsSuccess, string.Join("; ", validated.Errors.Select(error => error.Field)));

        var processed = world.Host.Resolve<T4CaseProcessor>().Process(validated.Value!);
        Assert.True(processed.IsSuccess, string.Join("; ", processed.Errors.Select(error => error.Field)));

        var assembled = ReviewPackageAssembler.Assemble([processed.Value]);
        Assert.True(assembled.IsSuccess);
        var slip = Assert.Single(assembled.Value!.ReviewedSlips);

        return (
            slip.BoxAmounts
                .Select(box => $"{box.BoxCode}={box.Amount.ToString(null, CultureInfo.InvariantCulture)}")
                .ToArray(),
            slip.LineEntries
                .Select(line => $"{line.LineNumber}={line.Amount.ToString(null, CultureInfo.InvariantCulture)}")
                .ToArray());
    }

    private static void AssertAcceptanceRefusedAndNothingProduced(World world, JourneyResult journey)
    {
        // Acceptance keeps its existing response: the empty safe 404.
        Assert.Equal(HttpStatusCode.NotFound, journey.Acceptance!.Status);
        Assert.Empty(journey.Acceptance.Body);

        // Finalizing a case with no accepted T4 is refused with the fixed
        // notice, and nothing is produced.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, journey.Finalization!.Status);
        Assert.Null(journey.Review);
        Assert.Empty(world.Portal.Reviews.All);
        Assert.Null(world.StoredCase.ReviewAssociation);
    }

    // ---------------------------------------------------------------- patterns

    private static readonly Regex NineDigitRun = new(
        "[0-9]{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex SinWord = new(
        @"\bSIN\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static readonly Regex BoxTwelve = new(
        @"\bbox[\s\-_]*12\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Removes random opaque material before a pattern scan: Blazor's protected
    /// prerender markers, and any unbroken run of thirty-two or more
    /// identifier or token characters (an opaque identifier, an antiforgery
    /// token, a protected cookie, the one-time credential).
    /// </summary>
    private static class Opaque
    {
        private static readonly Regex BlazorMarker = new(
            "<!--Blazor:.*?-->",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));

        private static readonly Regex Token = new(
            @"[A-Za-z0-9_\-]{32,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        internal static string Remove(string text) =>
            Token.Replace(BlazorMarker.Replace(text, "<opaque>"), "<opaque>");
    }

    // ------------------------------------------------------------ the journey

    private sealed class JourneyPlan
    {
        internal required IReadOnlyList<(string Code, string Value)> T4Boxes { get; init; }

        internal (string BoxCode, string Value)? Correction { get; init; }

        internal bool IncludeMedical { get; init; }

        internal bool AcceptMedical { get; init; }

        internal bool ExpectAcceptanceRefused { get; init; }
    }

    private sealed class JourneyResult
    {
        internal required Browser Accountant { get; init; }

        internal required Browser Client { get; init; }

        internal Guid T4DocumentId { get; init; }

        internal Guid MedicalDocumentId { get; init; }

        internal Exchange? MedicalReviewPage { get; init; }

        internal Exchange? Acceptance { get; init; }

        internal Exchange? Finalization { get; init; }

        /// <summary>The review output, when finalization redirected to it.</summary>
        internal Exchange? Review { get; init; }
    }

    private static class Journey
    {
        internal static async Task<JourneyResult> RunAsync(World world, JourneyPlan plan)
        {
            var accountant = new Browser(world.Host.CreateAnonymousClient());
            var client = new Browser(world.Host.CreateAnonymousClient());

            // 1-2. Sign in through the Development sign-in. It lands on the
            // case list.
            var signIn = await accountant.OpenGivenAsync(DevelopmentSignInAddress);
            var list = await accountant.SubmitAsync(signIn.Form("/development/sign-in"));
            Assert.Equal("/intake-cases", list.Address);

            // 3. From the case list, create a case.
            var create = await accountant.FollowAsync(list.Link("Create the first case"));
            var form = create.Form("/intake-cases");
            var issued = await accountant.SubmitAsync(form, new Dictionary<string, string>
            {
                ["primaryTaxpayerDisplayName"] = TaxpayerName,
                ["expectedT4Count"] = "1",
                ["expectedMedicalExpenseCount"] = plan.IncludeMedical ? "1" : "0",
            });
            Assert.Equal(HttpStatusCode.OK, issued.Status);

            // 4. The one-time client link is displayed once; it is the only
            // address a person copies.
            var clientLink = new Uri(issued.Text("one-time-link")).PathAndQuery;

            // 5. The client, in a second browser, redeems the link, answers,
            // uploads, and submits.
            var confirmation = await client.OpenGivenAsync(clientLink);
            var intake = await client.SubmitAsync(confirmation.Form("/client-intake/redeem"));
            Assert.Equal("/client-intake", intake.Address);

            var answers = intake.Form("/client-intake/answers");
            var answered = await client.SubmitAsync(
                answers,
                answers.RadioGroups.ToDictionary(
                    group => group,
                    _ => nameof(ClientIntake.Models.AvailabilityAnswerCode.AvailableNow)));
            Assert.Equal(HttpStatusCode.OK, answered.Status);

            var module = await client.FollowAsync(answered.Script("ClientQuestionnairePanel.razor.js"));
            var uploadAddress = ModuleConstant(module.Body, "uploadEndpoint");
            var antiforgeryHeader = ModuleConstant(module.Body, "antiforgeryHeader");
            client.Offer(uploadAddress);

            await UploadAsync(
                client, answered, uploadAddress, antiforgeryHeader,
                DocumentCategory.T4Slip, T4Source(plan.T4Boxes));
            if (plan.IncludeMedical)
            {
                await UploadAsync(
                    client, answered, uploadAddress, antiforgeryHeader,
                    DocumentCategory.MedicalExpenseEvidence, MedicalSource());
            }

            var submitted = await client.SubmitAsync(answered.Form("/client-intake/submissions"));
            Assert.Equal(HttpStatusCode.OK, submitted.Status);

            // 6. The accountant returns: case list, the case, its documents.
            var progress = await accountant.FollowAsync(issued.Link("Open intake progress"));
            var cases = await accountant.FollowAsync(progress.Link("Cases"));
            var documents = await accountant.FollowAsync(cases.Link("Documents"));
            var t4Address = documents.DocumentLink("Declared T4 1");

            // 6-7. Extract, and review the extraction.
            var t4Page = await accountant.FollowAsync(t4Address);
            t4Page = await accountant.SubmitAsync(t4Page.Form("/extract"));
            Assert.Equal(HttpStatusCode.OK, t4Page.Status);
            Assert.Equal(t4Address, t4Page.Address);

            // 8-9. Correct one non-identifier value; the chain runs again.
            if (plan.Correction is { } correction)
            {
                var correctionForm = t4Page.Form("/corrections");
                Assert.Contains(nameof(CorrectionField.T4BoxValue), correctionForm.Options("field"));
                t4Page = await accountant.SubmitAsync(correctionForm, new Dictionary<string, string>
                {
                    ["field"] = nameof(CorrectionField.T4BoxValue),
                    ["boxCode"] = correction.BoxCode,
                    ["correctedValue"] = correction.Value,
                });
                Assert.Equal(HttpStatusCode.OK, t4Page.Status);
                Assert.Matches(
                    $"<td[^>]*>{Regex.Escape(correction.BoxCode)}</td><td[^>]*>{Regex.Escape(correction.Value)}</td>",
                    t4Page.Body);
            }

            // 10. Accept.
            var acceptance = await accountant.SubmitAsync(t4Page.Form("/accept"));
            Assert.Equal(
                plan.ExpectAcceptanceRefused ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                acceptance.Status);

            Exchange? medicalPage = null;
            if (plan.IncludeMedical)
            {
                medicalPage = await accountant.FollowAsync(documents.DocumentLink("Declared medical document 1"));
                medicalPage = await accountant.SubmitAsync(medicalPage.Form("/extract"));
                Assert.Equal(HttpStatusCode.OK, medicalPage.Status);
                if (plan.AcceptMedical)
                {
                    medicalPage = await accountant.SubmitAsync(medicalPage.Form("/accept"));
                    Assert.Equal(HttpStatusCode.OK, medicalPage.Status);
                }
            }

            // 11-12. Back to the case documents, finalize, and follow the
            // redirect to the review output.
            var back = await accountant.FollowAsync(t4Page.Link("Back to case documents"));
            var finalization = await accountant.SubmitAsync(back.Form("/extractions/finalize"));

            var stored = world.StoredCase;
            return new JourneyResult
            {
                Accountant = accountant,
                Client = client,
                T4DocumentId = DocumentOf(stored, DocumentCategory.T4Slip),
                MedicalDocumentId = DocumentOf(stored, DocumentCategory.MedicalExpenseEvidence),
                MedicalReviewPage = medicalPage,
                Acceptance = acceptance,
                Finalization = finalization,
                Review = finalization.Address.StartsWith("/reviews/", StringComparison.Ordinal)
                    ? finalization
                    : null,
            };
        }

        /// <summary>
        /// Sends one source as the tracked upload module sends it: the rendered
        /// upload form's own category, ordinal and token, the literal neutral
        /// file name, and the antiforgery header.
        /// </summary>
        private static async Task UploadAsync(
            Browser client,
            Exchange page,
            string uploadAddress,
            string antiforgeryHeader,
            DocumentCategory category,
            byte[] source)
        {
            var form = page.UploadForm(category.ToString());

            var multipart = new MultipartFormDataContent($"taxai-{Guid.NewGuid():N}");
            multipart.Add(new StringContent(form.Hidden["category"]), "category");
            multipart.Add(new StringContent(form.Hidden["ordinal"]), "ordinal");
            multipart.Add(new StringContent("Pdf"), "declaredMediaKind");
            multipart.Add(
                new StringContent(source.Length.ToString(CultureInfo.InvariantCulture)),
                "declaredLength");
            var file = new ByteArrayContent(source);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(file, "document", "upload.bin");

            var uploaded = await client.SendAsync(
                HttpMethod.Post,
                uploadAddress,
                multipart,
                (antiforgeryHeader, form.Hidden[AntiforgeryField]));
            Assert.Equal(HttpStatusCode.OK, uploaded.Status);
        }

        private static string ModuleConstant(string module, string name)
        {
            var match = Regex.Match(
                module,
                "const " + Regex.Escape(name) + " = \"(?<value>[^\"]+)\";",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            Assert.True(match.Success, $"The upload module must declare {name}.");
            return match.Groups["value"].Value;
        }

        private static Guid DocumentOf(ClientIntake.Models.StoredIntakeCase stored, DocumentCategory category) =>
            stored.Documents
                .Where(document => document.Category == category)
                .Select(document => document.DocumentId)
                .SingleOrDefault();
    }

    // ------------------------------------------------------------ the browser

    /// <summary>One request and its response, as a browser would have seen it.</summary>
    private sealed record Exchange(
        string Method,
        string Address,
        HttpStatusCode Status,
        IReadOnlyList<string> Headers,
        string Body)
    {
        private static readonly RegexOptions Options =
            RegexOptions.CultureInvariant | RegexOptions.Singleline;

        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

        /// <summary>Address, headers (cookies included) and body.</summary>
        internal string Everything => string.Join("\n", [$"{Method} {Address}", .. Headers, Body]);

        /// <summary>Every address this response offers.</summary>
        internal IEnumerable<string> Offered =>
            Regex.Matches(Body, "\\b(?:href|action|src)=\"(?<address>[^\"]*)\"", Options, Limit)
                .Select(match => WebUtility.HtmlDecode(match.Groups["address"].Value));

        /// <summary>The address of the one link whose text is exactly <paramref name="text"/>.</summary>
        internal string Link(string text)
        {
            var links = Regex.Matches(Body, "<a\\b[^>]*\\bhref=\"(?<address>[^\"]*)\"[^>]*>(?<text>.*?)</a>", Options, Limit)
                .Where(match => string.Equals(match.Groups["text"].Value.Trim(), text, StringComparison.Ordinal))
                .Select(match => WebUtility.HtmlDecode(match.Groups["address"].Value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.True(links.Length == 1, $"Expected exactly one '{text}' link on {Address}; found {links.Length}.");
            return links[0];
        }

        /// <summary>The review link of the listed document with this label.</summary>
        internal string DocumentLink(string label)
        {
            var item = Assert.Single(
                Regex.Matches(Body, "<li\\b[^>]*>(?<item>.*?)</li>", Options, Limit),
                match => Regex.IsMatch(
                    match.Groups["item"].Value,
                    $"<strong[^>]*>{Regex.Escape(label)}</strong>",
                    Options,
                    Limit));
            return (this with { Body = item.Groups["item"].Value }).Link("Review extraction");
        }

        internal string Script(string fileName) =>
            Assert.Single(
                Regex.Matches(Body, "<script\\b[^>]*\\bsrc=\"(?<address>[^\"]*)\"", Options, Limit)
                    .Select(match => WebUtility.HtmlDecode(match.Groups["address"].Value)),
                address => address.EndsWith("/" + fileName, StringComparison.Ordinal));

        /// <summary>The text of the one element carrying this class.</summary>
        internal string Text(string cssClass)
        {
            var match = Assert.Single(Regex.Matches(
                Body, "<p class=\"" + Regex.Escape(cssClass) + "\"[^>]*>(?<text>[^<]*)</p>", Options, Limit));
            return WebUtility.HtmlDecode(match.Groups["text"].Value.Trim());
        }

        /// <summary>The one rendered form whose action ends with <paramref name="actionEnding"/>.</summary>
        internal RenderedForm Form(string actionEnding)
        {
            var forms = Forms()
                .Where(form => form.Action is not null && form.Action.EndsWith(actionEnding, StringComparison.Ordinal))
                .ToArray();
            Assert.True(forms.Length == 1, $"Expected exactly one form posting to …{actionEnding} on {Address}; found {forms.Length}.");
            return forms[0];
        }

        /// <summary>The rendered upload form of the first open slot in a category.</summary>
        internal RenderedForm UploadForm(string category) =>
            Forms().First(form =>
                form.IsUpload &&
                form.Hidden.TryGetValue("category", out var value) &&
                string.Equals(value, category, StringComparison.Ordinal));

        private IEnumerable<RenderedForm> Forms() =>
            Regex.Matches(Body, "<form\\b(?<attributes>[^>]*)>(?<content>.*?)</form>", Options, Limit)
                .Select(match => RenderedForm.Parse(match.Groups["attributes"].Value, match.Groups["content"].Value));
    }

    private sealed class RenderedForm
    {
        private static readonly RegexOptions Flags =
            RegexOptions.CultureInvariant | RegexOptions.Singleline;

        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

        private readonly string _content;

        private RenderedForm(string? action, bool isUpload, Dictionary<string, string> hidden, string content)
        {
            Action = action;
            IsUpload = isUpload;
            Hidden = hidden;
            _content = content;
        }

        internal string? Action { get; }

        internal bool IsUpload { get; }

        /// <summary>The hidden inputs as rendered, the antiforgery token included.</summary>
        internal IReadOnlyDictionary<string, string> Hidden { get; }

        internal string[] RadioGroups =>
            Inputs()
                .Where(input => Attribute(input, "type") == "radio")
                .Select(input => Attribute(input, "name")!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        internal string[] Options(string selectName)
        {
            var select = Assert.Single(Regex.Matches(
                _content,
                "<select\\b[^>]*\\bname=\"" + Regex.Escape(selectName) + "\"[^>]*>(?<options>.*?)</select>",
                Flags,
                Limit));
            return Regex.Matches(select.Groups["options"].Value, "<option\\b[^>]*\\bvalue=\"(?<value>[^\"]*)\"", Flags, Limit)
                .Select(match => WebUtility.HtmlDecode(match.Groups["value"].Value))
                .ToArray();
        }

        internal static RenderedForm Parse(string attributes, string content)
        {
            var hidden = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var input in Regex.Matches(content, "<input\\b[^>]*>", Flags, Limit).Select(match => match.Value))
            {
                if (Attribute(input, "type") == "hidden" && Attribute(input, "name") is { } name)
                {
                    hidden[name] = Attribute(input, "value") ?? string.Empty;
                }
            }

            return new RenderedForm(
                Attribute(attributes, "action"),
                attributes.Contains("data-intake-upload", StringComparison.Ordinal),
                hidden,
                content);
        }

        private IEnumerable<string> Inputs() =>
            Regex.Matches(_content, "<input\\b[^>]*>", Flags, Limit).Select(match => match.Value);

        private static string? Attribute(string markup, string name)
        {
            var match = Regex.Match(markup, "\\b" + Regex.Escape(name) + "=\"(?<value>[^\"]*)\"", Flags, Limit);
            return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
        }
    }

    /// <summary>
    /// A browser reduced to what the proof needs: its own cookie container, no
    /// automatic redirects, a record of every exchange, and one rule — it only
    /// requests an address it was given or one a received response offered.
    /// </summary>
    private sealed class Browser(HttpClient client)
    {
        private static readonly Regex Identifier = new(
            "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        private readonly HashSet<string> _offered = new(StringComparer.Ordinal);
        private readonly List<string> _given = [];
        private readonly List<Exchange> _exchanges = [];

        internal IReadOnlyList<Exchange> Exchanges => _exchanges;

        internal IReadOnlyList<string> GivenAddresses => _given;

        /// <summary>Opens an address a person was given from outside the application.</summary>
        internal Task<Exchange> OpenGivenAsync(string address)
        {
            _given.Add(address);
            _offered.Add(address);
            return FollowAsync(address);
        }

        /// <summary>
        /// Records an address the application offered other than through
        /// markup: the constant its tracked upload module declares.
        /// </summary>
        internal void Offer(string address) => _offered.Add(address);

        internal Task<Exchange> FollowAsync(string address) =>
            SendAsync(HttpMethod.Get, address, content: null);

        internal Task<Exchange> SubmitAsync(
            RenderedForm form,
            IReadOnlyDictionary<string, string>? entered = null)
        {
            var values = form.Hidden.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var (name, value) in entered ?? new Dictionary<string, string>())
            {
                values[name] = value;
            }

            return SendAsync(HttpMethod.Post, form.Action!, new FormUrlEncodedContent(values));
        }

        internal async Task<Exchange> SendAsync(
            HttpMethod method,
            string address,
            HttpContent? content,
            (string Name, string Value)? header = null)
        {
            Assert.True(
                _offered.Contains(address),
                $"{method} {address} was requested, but no response offered that address.");

            using var request = new HttpRequestMessage(method, address) { Content = content };
            if (header is { } extra)
            {
                request.Headers.Add(extra.Name, extra.Value);
            }

            using var response = await client.SendAsync(request);
            var exchange = new Exchange(
                method.Method,
                address,
                response.StatusCode,
                response.Headers
                    .Concat(response.Content.Headers)
                    .SelectMany(pair => pair.Value.Select(value => $"{pair.Key}: {value}"))
                    .ToArray(),
                await response.Content.ReadAsStringAsync());
            _exchanges.Add(exchange);

            foreach (var offered in exchange.Offered)
            {
                _offered.Add(offered);
            }

            if ((int)response.StatusCode is >= 300 and < 400 &&
                response.Headers.Location is { } location)
            {
                var target = location.OriginalString;
                _offered.Add(target);
                return await FollowAsync(target);
            }

            return exchange;
        }

        /// <summary>
        /// The distinct steps taken, in order, with opaque selectors named
        /// rather than shown. Static assets other than the upload module are
        /// not requested, and a repeated step is listed once.
        /// </summary>
        internal string[] Steps() =>
            _exchanges
                .Select(exchange => $"{exchange.Method} {Shape(exchange.Address)}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        private static string Shape(string address)
        {
            if (address.StartsWith("/client-intake/link/", StringComparison.Ordinal))
            {
                return "/client-intake/link/{credential}";
            }

            if (address.StartsWith("/reviews/", StringComparison.Ordinal))
            {
                return "/reviews/{record}";
            }

            var index = 0;
            return Identifier.Replace(address, _ => index++ == 0 ? "{case}" : "{document}");
        }
    }

    /// <summary>The boxes and tax lines of the one slip on a review page, in rendered order.</summary>
    private sealed record RenderedReview(string[] Boxes, string[] Lines)
    {
        internal static RenderedReview Parse(string html)
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Singleline;
            var limit = TimeSpan.FromSeconds(1);
            // Component-scoped styles add a scope attribute to every element,
            // so no tag is matched as closed immediately after its name.
            var boxes = Section(html, ">Boxes and values</h3>", ">Tax lines</h3>");
            var lines = Section(html, ">Tax lines</h3>", ">Unmapped codes</h3>");

            return new RenderedReview(
                Regex.Matches(boxes, "<tr[^>]*><td[^>]*>(?<code>[^<]*)</td><td class=\"amount\"[^>]*>(?<amount>[^<]*)</td></tr>", options, limit)
                    .Select(match => $"{match.Groups["code"].Value}={match.Groups["amount"].Value}")
                    .ToArray(),
                Regex.Matches(lines, "<tr[^>]*>\\s*<td[^>]*>(?<code>[^<]*)</td>\\s*<td class=\"amount\"[^>]*>(?<amount>[^<]*)</td>", options, limit)
                    .Select(match => $"{match.Groups["code"].Value}={match.Groups["amount"].Value}")
                    .ToArray());
        }

        private static string Section(string html, string start, string end)
        {
            var from = html.IndexOf(start, StringComparison.Ordinal);
            var to = html.IndexOf(end, StringComparison.Ordinal);
            Assert.True(from >= 0 && to > from, "The review page must render one slip's sections.");
            Assert.Equal(from, html.LastIndexOf(start, StringComparison.Ordinal));
            return html[from..to];
        }
    }

    // -------------------------------------------------------------- the world

    /// <summary>
    /// The spec 015 portal world — real Development composition, in-memory
    /// stand-ins for the three SQL-backed stores — with one difference: the
    /// identity directory holds what a seeded Development database holds, so
    /// the product's own Development sign-in is the way in.
    /// </summary>
    private sealed class World : IAsyncDisposable
    {
        private World(ExtractionPortalWorld portal, SeededDirectory directory)
        {
            Portal = portal;
            Directory = directory;
        }

        internal ExtractionPortalWorld Portal { get; }

        internal SeededDirectory Directory { get; }

        internal IntakeFirmHost Host => Portal.Intake.FirmOne;

        /// <summary>The one case a journey created.</summary>
        internal ClientIntake.Models.StoredIntakeCase StoredCase => Portal.Intake.Store.Single();

        internal static World Create(ILoggerProvider? logs = null)
        {
            var directory = new SeededDirectory();
            var portal = ExtractionPortalWorld.Create(builder =>
            {
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IIdentityDirectory>(directory));
                if (logs is not null)
                {
                    builder.ConfigureLogging(logging => logging.AddProvider(logs));
                }
            });
            return new World(portal, directory);
        }

        internal Task<StoredDocumentExtraction?> FindDocumentAsync(Guid documentId) =>
            Portal.Extractions.FindDocumentAsync(
                StoredCase.Firm, StoredCase.CaseId, documentId, CancellationToken.None);

        public ValueTask DisposeAsync() => Portal.DisposeAsync();
    }

    /// <summary>
    /// The identity directory as a seeded Development database presents it:
    /// one user with the fixed synthetic issuer and subject, and one active
    /// membership in one firm.
    /// </summary>
    private sealed class SeededDirectory : IIdentityDirectory
    {
        private static readonly DateTimeOffset Timestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly Firm _firm = PortalAuthorizationHarness.Construct<Firm>(
            Guid.NewGuid(), "Example Firm A", Timestamp, Timestamp);

        internal SeededDirectory()
        {
            User = PortalAuthorizationHarness.Construct<PortalUser>(
                Guid.NewGuid(),
                DevelopmentFixtureIdentity.Issuer,
                DevelopmentFixtureIdentity.Subject,
                true,
                Timestamp,
                Timestamp);
            Membership = PortalAuthorizationHarness.Construct<FirmMembership>(
                Guid.NewGuid(), _firm.FirmId, User.UserId, true, Timestamp, Timestamp);
        }

        internal PortalUser User { get; }

        internal FirmMembership Membership { get; }

        internal (string Issuer, string Subject)? LastIssuerSubjectLookup { get; private set; }

        public Task<PortalUser?> FindUserAsync(string issuer, string subject, CancellationToken cancellationToken)
        {
            LastIssuerSubjectLookup = (issuer, subject);
            return Task.FromResult(
                string.Equals(User.Issuer, issuer, StringComparison.Ordinal) &&
                string.Equals(User.Subject, subject, StringComparison.Ordinal)
                    ? User
                    : null);
        }

        public Task<PortalUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(User.UserId == userId ? User : null);

        public Task<IReadOnlyList<FirmMembership>> FindActiveMembershipsAsync(
            Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FirmMembership>>(
                Membership.UserId == userId ? [Membership] : []);

        public Task<FirmMembership?> FindMembershipAsync(
            Guid userId, Guid membershipId, CancellationToken cancellationToken) =>
            Task.FromResult(
                Membership.UserId == userId && Membership.MembershipId == membershipId
                    ? Membership
                    : null);

        public Task<IReadOnlyList<Firm>> FindFirmsAsync(
            IReadOnlyList<Guid> firmIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Firm>>(
                firmIds.Contains(_firm.FirmId) ? [_firm] : []);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }
}
