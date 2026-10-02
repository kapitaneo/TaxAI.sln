using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ClientIntake.Contracts;
using ClientIntake.Models;
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
/// Spec 014 T04 — the integrated acceptance proof over the <b>real</b> portal
/// composition.
/// <para>
/// Only two collaborators are substituted, and both for the same reason: they are
/// the ones that need SQL Server. The intake metadata store and the review-record
/// repository are in-memory here; the relational adapter behind them is proved
/// against a real database in <c>Persistence.Tests</c>. Everything a request
/// actually touches — routing, rate limiting, authentication, R-1 authorization,
/// antiforgery, the cookie protector, the multipart transport, the real
/// <c>AccountantIntakeWorkflow</c> and <c>ClientIntakeWorkflow</c>, the real
/// cryptographic credential source, the real Development encrypted content
/// adapter, and every Razor component — is production code.
/// </para>
/// <para>
/// Every taxpayer-shaped value here is assembled at runtime and is obviously
/// synthetic.
/// </para>
/// </summary>
public sealed class ClientIntakeAcceptanceTests
{
    [Fact] // AC-1, AC-2, AC-3, AC-12, AC-20, AC-21, AC-33, AC-52, AC-55, AC-73
    public async Task AnAccountantCreatesACaseAClientCompletesItAndTheReviewOpens()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // ---------------------------------------------- AC-1: create and issue
        var issued = await IntakeFlow.CreateCaseAsync(
            accountant,
            expectedT4Count: 1,
            expectedMedicalCount: 1);

        Assert.Equal(2025, world.Store.Single().TaxYear);
        Assert.Equal(
            world.FirmOne.FirmId,
            world.Store.Single().Firm.Value);
        Assert.Equal(2, world.Store.Single().ExpectedItems.Count);

        // AC-12: the URL carries a fixed prefix and one opaque credential, and
        // no firm, case, participant, taxpayer, year, or session identifier.
        var url = new Uri(issued.ClientUrl);
        Assert.Equal("https://intake.test.invalid/", url.GetLeftPart(UriPartial.Authority) + "/");
        Assert.StartsWith("/client-intake/link/", url.AbsolutePath, StringComparison.Ordinal);
        Assert.Empty(url.Query);
        Assert.Empty(url.Fragment);
        foreach (var identifier in world.EveryServerIdentifier())
        {
            Assert.DoesNotContain(identifier, issued.ClientUrl, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------ AC-2: redeem, answer, upload, submit
        using var client = world.FirmOne.CreateAnonymousClient();
        var redemption = await IntakeFlow.RedeemAsync(client, issued.Credential);

        // AC-20/AC-21: the confirmation GET spent nothing, the redirect is clean,
        // and the cookie carries only a protected opaque key.
        Assert.Equal("/client-intake", redemption.Location);
        Assert.DoesNotContain(
            issued.Credential,
            redemption.CookieValue,
            StringComparison.Ordinal);
        foreach (var identifier in world.EveryServerIdentifier())
        {
            Assert.DoesNotContain(
                identifier,
                redemption.CookieValue,
                StringComparison.OrdinalIgnoreCase);
        }

        await IntakeFlow.SaveAnswersAsync(
            client,
            t4Answer: AvailabilityAnswerCode.AvailableNow,
            medicalAnswer: AvailabilityAnswerCode.AvailableNow);

        var t4 = await IntakeFlow.UploadAsync(
            client,
            DocumentCategory.T4Slip,
            ordinal: 1,
            IntakeMediaKind.Pdf,
            SyntheticSource.Pdf("synthetic t4 body"));
        Assert.Equal(HttpStatusCode.OK, t4.StatusCode);

        var medical = await IntakeFlow.UploadAsync(
            client,
            DocumentCategory.MedicalExpenseEvidence,
            ordinal: 1,
            IntakeMediaKind.Png,
            SyntheticSource.Png("synthetic medical body"));
        Assert.Equal(HttpStatusCode.OK, medical.StatusCode);

        var submitted = await IntakeFlow.SubmitAsync(client);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var submittedHtml = await submitted.Content.ReadAsStringAsync();
        Assert.Contains(
            "Latest immutable submission: version 1.",
            submittedHtml,
            StringComparison.Ordinal);

        // -------------------------------------------- AC-3: accountant progress
        using var progress = await accountant.GetAsync($"/intake-cases/{issued.CaseId:D}");
        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
        var progressHtml = await progress.Content.ReadAsStringAsync();

        Assert.Contains(IntakeFlow.DisplayName, progressHtml, StringComparison.Ordinal);
        Assert.Contains(">2025<", progressHtml, StringComparison.Ordinal);
        Assert.Contains("2 of 2 answered", progressHtml, StringComparison.Ordinal);
        Assert.Contains("Version 1", progressHtml, StringComparison.Ordinal);
        Assert.Contains(">Ready<", progressHtml, StringComparison.Ordinal);
        Assert.Contains("T4 1", progressHtml, StringComparison.Ordinal);
        Assert.Contains("Medical document 1", progressHtml, StringComparison.Ordinal);
        Assert.Contains(
            "awaiting an existing review record",
            progressHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/reviews/", progressHtml, StringComparison.Ordinal);

        // ------------------------------------- AC-73: the exact review handoff
        var recordId = world.Reviews.AddRecord(world.FirmOne.FirmId, issued.CaseId);
        var associated = await IntakeFlow.AssociateReviewAsync(
            accountant,
            issued.CaseId,
            recordId);

        Assert.Equal(HttpStatusCode.OK, associated.StatusCode);
        var associatedHtml = await associated.Content.ReadAsStringAsync();
        Assert.Contains($"/reviews/{recordId:D}", associatedHtml, StringComparison.Ordinal);
        Assert.Equal(0, world.Reviews.SaveCalls);

        // The unchanged spec 012 route still opens that exact record.
        using var review = await accountant.GetAsync($"/reviews/{recordId:D}");
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewHtml = await review.Content.ReadAsStringAsync();
        Assert.Contains("2025", reviewHtml, StringComparison.Ordinal);
        Assert.Contains("T4", reviewHtml, StringComparison.Ordinal);
        Assert.True(review.Headers.CacheControl?.NoStore);
    }

    [Fact] // AC-4, AC-5
    public async Task CaseCreationRefusesBeforeStorageWhenTheCallerHasNoTrustedFirm()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // A create request carrying another firm in the body: the field does not
        // exist on the command, so it is ignored, and the server firm is used.
        var page = await accountant.GetAsync("/intake-cases/new");
        var token = IntakeFlow.AntiforgeryToken(await page.Content.ReadAsStringAsync());
        page.Dispose();

        using var response = await accountant.PostAsync(
            "/intake-cases",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["primaryTaxpayerDisplayName"] = IntakeFlow.DisplayName,
                    ["taxYear"] = "2025",
                    ["expectedT4Count"] = "1",
                    ["expectedMedicalExpenseCount"] = "0",
                    ["firmId"] = world.FirmTwo.FirmId.ToString("D"),
                    ["trustedFirmId"] = world.FirmTwo.FirmId.ToString("D"),
                }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(world.FirmOne.FirmId, world.Store.Single().Firm.Value);
        Assert.Empty(world.Store.CasesFor(world.FirmTwo.FirmId));

        // And with the membership deactivated, authorization refuses before the
        // workflow — no case, no link, no audit event is created.
        world.FirmOne.Directory.SetMembershipActive(
            world.FirmOne.Membership.MembershipId,
            isActive: false);
        var before = world.Store.Count;

        using var refusedPage = await accountant.GetAsync("/intake-cases/new");
        Assert.Equal(HttpStatusCode.Forbidden, refusedPage.StatusCode);
        Assert.Equal(before, world.Store.Count);
    }

    [Theory] // AC-6, AC-7, AC-8, AC-9, AC-10
    [InlineData("", "2025", "1", "0")]
    [InlineData("   ", "2025", "1", "0")]
    [InlineData("Synthetic Intake Taxpayer", "2024", "1", "0")]
    [InlineData("Synthetic Intake Taxpayer", "2026", "1", "0")]
    [InlineData("Synthetic Intake Taxpayer", "", "1", "0")]
    [InlineData("Synthetic Intake Taxpayer", "2025", "0", "0")]
    [InlineData("Synthetic Intake Taxpayer", "2025", "-1", "0")]
    [InlineData("Synthetic Intake Taxpayer", "2025", "26", "0")]
    [InlineData("Synthetic Intake Taxpayer", "2025", "", "0")]
    public async Task InvalidCreateInputFailsClosedAndStoresNothing(
        string displayName,
        string taxYear,
        string t4Count,
        string medicalCount)
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        using var page = await accountant.GetAsync("/intake-cases/new");
        var token = IntakeFlow.AntiforgeryToken(await page.Content.ReadAsStringAsync());

        using var response = await accountant.PostAsync(
            "/intake-cases",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["primaryTaxpayerDisplayName"] = displayName,
                    ["taxYear"] = taxYear,
                    ["expectedT4Count"] = t4Count,
                    ["expectedMedicalExpenseCount"] = medicalCount,
                }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, world.Store.Count);
    }

    [Fact] // AC-87, AC-88
    public async Task SinShapedAndOverLongDisplayNamesAreRefusedWithoutEchoOrStorage()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var probes = new[]
        {
            string.Concat("046", "454", "286"),
            string.Join("-", "046", "454", "286"),
            string.Join(" ", "046", "454", "286"),
            // A Canadian SIN masks as three groups of three, not the 3-2-3
            // shape a US SSN uses.
            string.Join("-", "***", "***", "286"),
            string.Join("-", "xxx", "xxx", "286"),
            new string('N', PersistedSchema.DisplayNameMaxLength + 1),
        };

        foreach (var probe in probes)
        {
            using var page = await accountant.GetAsync("/intake-cases/new");
            var token = IntakeFlow.AntiforgeryToken(
                await page.Content.ReadAsStringAsync());

            using var response = await accountant.PostAsync(
                "/intake-cases",
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["__RequestVerificationToken"] = token,
                        ["primaryTaxpayerDisplayName"] = probe,
                        ["taxYear"] = "2025",
                        ["expectedT4Count"] = "1",
                        ["expectedMedicalExpenseCount"] = "0",
                    }));

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(
                probe,
                await response.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
            Assert.Equal(0, world.Store.Count);
        }

        // The exact boundary length is accepted.
        using var boundaryPage = await accountant.GetAsync("/intake-cases/new");
        var boundaryToken = IntakeFlow.AntiforgeryToken(
            await boundaryPage.Content.ReadAsStringAsync());
        using var accepted = await accountant.PostAsync(
            "/intake-cases",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = boundaryToken,
                    ["primaryTaxpayerDisplayName"] =
                        new string('N', PersistedSchema.DisplayNameMaxLength),
                    ["taxYear"] = "2025",
                    ["expectedT4Count"] = "1",
                    ["expectedMedicalExpenseCount"] = "0",
                }));

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(1, world.Store.Count);
        Assert.Equal(
            PersistedSchema.DisplayNameMaxLength,
            world.Store.Single().PrimaryTaxpayerDisplayName.Length);
    }

    [Fact] // AC-24, AC-25, AC-27
    public async Task TheClientPageIsAdaptiveAndRefusesAnUnrequestedCategory()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // A T4-only case.
        var issued = await IntakeFlow.CreateCaseAsync(
            accountant,
            expectedT4Count: 2,
            expectedMedicalCount: 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        using var page = await client.GetAsync("/client-intake");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains(
            "Are the requested T4 documents available now?",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "medical-expense documents available now",
            html,
            StringComparison.Ordinal);
        Assert.Contains("T4 1", html, StringComparison.Ordinal);
        Assert.Contains("T4 2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Medical document", html, StringComparison.Ordinal);

        // A medical answer on a T4-only case fails closed and stores nothing.
        var token = IntakeFlow.AntiforgeryToken(html);
        using var unrequestedAnswer = await client.PostAsync(
            "/client-intake/answers",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["medicalExpenseAnswer"] =
                        nameof(AvailabilityAnswerCode.AvailableNow),
                }));

        Assert.NotEqual(HttpStatusCode.OK, unrequestedAnswer.StatusCode);
        Assert.Empty(world.Store.Single().AnswerVersions);

        // So does a medical upload.
        using var unrequestedUpload = await IntakeFlow.UploadAsync(
            client,
            DocumentCategory.MedicalExpenseEvidence,
            ordinal: 1,
            IntakeMediaKind.Pdf,
            SyntheticSource.Pdf("unrequested category body"));

        Assert.Equal(HttpStatusCode.NotFound, unrequestedUpload.StatusCode);
        Assert.Empty(world.Store.Single().Documents);
    }

    [Fact] // AC-26
    public async Task EverySupportedAnswerCodeIsAcceptedAndEveryOtherValueIsRefused()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        foreach (var supported in Enum.GetValues<AvailabilityAnswerCode>())
        {
            using var response = await IntakeFlow.PostAnswerAsync(
                client,
                "t4Answer",
                supported.ToString());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.NotEmpty(world.Store.Single().AnswerVersions);
        var versionsBefore = world.Store.Single().AnswerVersions.Count;

        foreach (var rejected in new[]
                 {
                     "free text",
                     "Unknown",
                     "",
                     "availablenow",
                     "3",
                 })
        {
            using var response = await IntakeFlow.PostAnswerAsync(
                client,
                "t4Answer",
                rejected);
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(versionsBefore, world.Store.Single().AnswerVersions.Count);
    }

    [Theory] // AC-33, AC-35, AC-36, AC-34
    [InlineData("Pdf", true)]
    [InlineData("Jpeg", true)]
    [InlineData("Png", true)]
    public async Task EachConfiguredMediaKindIsAcceptedWhenItsSignatureMatches(
        string mediaKind,
        bool expectedAccepted)
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        var kind = Enum.Parse<IntakeMediaKind>(mediaKind);
        using var response = await IntakeFlow.UploadAsync(
            client,
            DocumentCategory.T4Slip,
            1,
            kind,
            SyntheticSource.For(kind, $"synthetic {mediaKind} body"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedAccepted, world.Store.Single().Documents.Count == 1);
        Assert.Equal(kind, world.Store.Single().Documents[0].MediaKind);
    }

    [Fact] // AC-34, AC-35, AC-36, AC-50
    public async Task EmptyUnsupportedAndSignatureMismatchedUploadsAreRejected()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        // Zero bytes.
        using (var empty = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   Array.Empty<byte>()))
        {
            Assert.NotEqual(HttpStatusCode.OK, empty.StatusCode);
        }

        // An archive and an executable, each declared as an allowed kind. The
        // attempt is recorded, and the response is the fixed invalid one.
        foreach (var disguised in new[]
                 {
                     new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x01, 0x02, 0x03, 0x04 },
                     new byte[] { 0x4d, 0x5a, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 },
                 })
        {
            using var response = await IntakeFlow.UploadAsync(
                client,
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                disguised);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(
                "Request was not accepted.",
                await response.Content.ReadAsStringAsync());
        }

        // A real PNG declared as a PDF: signature mismatch.
        using (var mismatch = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.Png("mismatched body")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        }

        // No document record exists, and the item projects rejected.
        var stored = world.Store.Single();
        Assert.Empty(stored.Documents);
        Assert.NotEmpty(stored.UploadAttempts);
        Assert.All(
            stored.UploadAttempts,
            attempt => Assert.Equal(IntakeUploadOutcome.Rejected, attempt.Outcome));

        using var page = await client.GetAsync("/client-intake");
        Assert.Contains(
            "Rejected",
            await page.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact] // AC-38, AC-39
    public async Task TheConfiguredByteAndFileCountLimitsAreEnforced()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        // The configured maximum for this test host is 1 MiB.
        const int Maximum = 1024 * 1024;
        using (var atLimit = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.PdfOfLength(Maximum)))
        {
            Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);
        }

        Assert.Single(world.Store.Single().Documents);
        Assert.Equal(Maximum, world.Store.Single().Documents[0].ByteCount);

        using (var overLimit = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.PdfOfLength(Maximum + 1)))
        {
            Assert.NotEqual(HttpStatusCode.OK, overLimit.StatusCode);
        }

        Assert.Single(world.Store.Single().Documents);

        // The configured per-request file count for this host is one.
        using var tooMany = await IntakeFlow.UploadTwoFilesAsync(client);
        Assert.NotEqual(HttpStatusCode.OK, tooMany.StatusCode);
        Assert.Single(world.Store.Single().Documents);
    }

    [Fact] // AC-41, AC-44
    public async Task ASecondIdenticalUploadIsRejectedAsADuplicate()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);
        var body = SyntheticSource.Pdf("duplicate detection body");

        using (var first = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   body))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await IntakeFlow.UploadAsync(
            client,
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Pdf,
            body);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(
            "Upload already received.",
            await second.Content.ReadAsStringAsync());
        Assert.Single(world.Store.Single().Documents);
    }

    [Fact] // AC-48, AC-49, AC-56, AC-82
    public async Task PerItemStatusFollowsTheOneFixedPrecedenceRule()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);

        // AC-48: nothing yet.
        using (var requested = await client.GetAsync("/client-intake"))
        {
            Assert.Contains(
                "Requested",
                await requested.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        await IntakeFlow.SaveAnswersAsync(
            client,
            AvailabilityAnswerCode.AvailableNow,
            medicalAnswer: null);

        // AC-49: uploaded but not yet in a submission.
        using (var upload = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.Pdf("uploaded status body")))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            Assert.Contains(
                "Uploaded",
                await upload.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        // AC-52: submitted, so ready.
        using (var submitted = await IntakeFlow.SubmitAsync(client))
        {
            var html = await submitted.Content.ReadAsStringAsync();
            Assert.Contains("Ready", html, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "changes that are not in the latest submission",
                html,
                StringComparison.Ordinal);
        }

        // AC-56/AC-82(b): a further valid upload leaves the item ready and marks
        // unsubmitted changes.
        using (var replacement = await IntakeFlow.UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.Pdf("post-ready replacement body")))
        {
            var html = await replacement.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
            Assert.Contains("Ready", html, StringComparison.Ordinal);
            Assert.Contains(
                "changes that are not in the latest submission",
                html,
                StringComparison.Ordinal);
        }

        // The immutable submission is unchanged.
        Assert.Single(world.Store.Single().Submissions);
        Assert.Equal(1, world.Store.Single().Submissions[0].Version);
    }

    [Fact] // AC-71, AC-72
    public async Task HandoffShowsNoReviewLinkWithoutBothReadinessAndAnExactRecord()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // AC-72: a record exists, but the intake is not ready.
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);
        var recordId = world.Reviews.AddRecord(world.FirmOne.FirmId, issued.CaseId);

        using (var notReady = await accountant.GetAsync(
                   $"/intake-cases/{issued.CaseId:D}"))
        {
            var html = await notReady.Content.ReadAsStringAsync();
            Assert.Contains(
                "must be ready before review handoff",
                html,
                StringComparison.Ordinal);
            Assert.DoesNotContain($"/reviews/{recordId:D}", html, StringComparison.Ordinal);
        }

        // AC-71: ready, but no association has been made.
        using var client = world.FirmOne.CreateAnonymousClient();
        await IntakeFlow.RedeemAsync(client, issued.Credential);
        await IntakeFlow.SaveAnswersAsync(
            client,
            AvailabilityAnswerCode.AvailableNow,
            null);
        (await IntakeFlow.UploadAsync(
            client,
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Pdf,
            SyntheticSource.Pdf("handoff body"))).Dispose();
        (await IntakeFlow.SubmitAsync(client)).Dispose();

        using var ready = await accountant.GetAsync($"/intake-cases/{issued.CaseId:D}");
        var readyHtml = await ready.Content.ReadAsStringAsync();
        Assert.Contains(
            "awaiting an existing review record",
            readyHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"/reviews/{recordId:D}", readyHtml, StringComparison.Ordinal);
        Assert.Equal(0, world.Reviews.SaveCalls);
    }

    [Fact] // AC-74, AC-86
    public async Task AnInvalidOrRepeatedHandoffAssociationIsRefusedSafely()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.ReadyCaseAsync(world, accountant);

        // Blank, malformed, missing, foreign-firm, and wrong-case selectors.
        var foreign = world.Reviews.AddRecord(world.FirmTwo.FirmId, issued.CaseId);
        var wrongCase = world.Reviews.AddRecord(world.FirmOne.FirmId, Guid.NewGuid());

        foreach (var candidate in new[]
                 {
                     string.Empty,
                     "not-a-guid",
                     Guid.Empty.ToString("D"),
                     Guid.NewGuid().ToString("D"),
                     foreign.ToString("D"),
                     wrongCase.ToString("D"),
                 })
        {
            using var response = await IntakeFlow.AssociateReviewRawAsync(
                accountant,
                issued.CaseId,
                candidate);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(world.Store.Single().ReviewAssociation);
        }

        // A valid association succeeds exactly once and is never replaced.
        var recordId = world.Reviews.AddRecord(world.FirmOne.FirmId, issued.CaseId);
        using (var first = await IntakeFlow.AssociateReviewAsync(
                   accountant,
                   issued.CaseId,
                   recordId))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        var replacementId = world.Reviews.AddRecord(world.FirmOne.FirmId, issued.CaseId);
        using var replaced = await IntakeFlow.AssociateReviewAsync(
            accountant,
            issued.CaseId,
            replacementId);

        Assert.Equal(HttpStatusCode.Conflict, replaced.StatusCode);
        Assert.Equal(recordId, world.Store.Single().ReviewAssociation!.RecordId);
        Assert.Equal(0, world.Reviews.SaveCalls);
    }

    [Fact] // AC-13, AC-14
    public async Task TheRawClientUrlIsShownOnceAndIsUnrecoverableAfterwards()
    {
        await using var world = IntakeWorld.Create();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);

        using var progress = await accountant.GetAsync(
            $"/intake-cases/{issued.CaseId:D}");
        var html = await progress.Content.ReadAsStringAsync();

        Assert.DoesNotContain(issued.Credential, html, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.ClientUrl, html, StringComparison.Ordinal);

        // Reissue returns a different credential and revokes the previous one.
        var reissued = await IntakeFlow.ReissueAsync(accountant, issued.CaseId);
        Assert.NotEqual(issued.Credential, reissued.Credential);

        using var stale = world.FirmOne.CreateAnonymousClient();
        using var refused = await stale.GetAsync(
            $"/client-intake/link/{issued.Credential}");
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }
}

// ---------------------------------------------------------------- the world

/// <summary>
/// Two firms over one shared in-memory intake store and one shared review
/// repository, each with its own real portal host. Sharing the store is what
/// makes a cross-firm read a real cross-firm read rather than a lookup in an
/// empty dictionary.
/// </summary>
internal sealed class IntakeWorld : IAsyncDisposable
{
    private IntakeWorld(
        InMemoryIntakeStore store,
        RecordingReviewRepository reviews,
        IntakeFirmHost firmOne,
        IntakeFirmHost firmTwo)
    {
        Store = store;
        Reviews = reviews;
        FirmOne = firmOne;
        FirmTwo = firmTwo;
    }

    internal InMemoryIntakeStore Store { get; }
    internal RecordingReviewRepository Reviews { get; }
    internal IntakeFirmHost FirmOne { get; }
    internal IntakeFirmHost FirmTwo { get; }

    internal static IntakeWorld Create(
        Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configure = null)
    {
        var store = new InMemoryIntakeStore();
        var reviews = new RecordingReviewRepository();
        return new IntakeWorld(
            store,
            reviews,
            IntakeFirmHost.Create(store, reviews, configure),
            IntakeFirmHost.Create(store, reviews, configure));
    }

    /// <summary>
    /// The directory the Development content adapter writes encrypted objects
    /// to. It is supplied by the same environment provider a deployment would
    /// use, and it is outside the web root by configuration guard.
    /// </summary>
    internal static string ContentRoot =>
        Environment.GetEnvironmentVariable(
            "TaxAi__Intake__DevelopmentContentRoot")
        ?? throw new InvalidOperationException(
            "The development content root is not configured.");

    /// <summary>
    /// Every server-side identifier a browser surface must never contain.
    /// </summary>
    internal IReadOnlyList<string> EveryServerIdentifier()
    {
        var identifiers = new List<string>
        {
            FirmOne.FirmId.ToString("D"),
            FirmTwo.FirmId.ToString("D"),
            FirmOne.User.UserId.ToString("D"),
            FirmOne.Membership.MembershipId.ToString("D"),
            FirmTwo.User.UserId.ToString("D"),
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

internal sealed class IntakeFirmHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _baseHost;
    private readonly WebApplicationFactory<Program> _host;
    private readonly List<string> _registeredServices;

    private IntakeFirmHost(
        WebApplicationFactory<Program> baseHost,
        WebApplicationFactory<Program> host,
        PortalAuthorizationHarness.IdentityDirectory directory,
        PortalAuthorizationHarness.SessionStore sessions,
        PortalUser user,
        FirmMembership membership,
        List<string> registeredServices)
    {
        _baseHost = baseHost;
        _host = host;
        Directory = directory;
        Sessions = sessions;
        User = user;
        Membership = membership;
        _registeredServices = registeredServices;
    }

    internal PortalAuthorizationHarness.IdentityDirectory Directory { get; }
    internal PortalAuthorizationHarness.SessionStore Sessions { get; }
    internal PortalUser User { get; }
    internal FirmMembership Membership { get; }
    internal Guid FirmId => Membership.FirmId;

    internal static IntakeFirmHost Create(
        InMemoryIntakeStore store,
        RecordingReviewRepository reviews,
        Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configure = null)
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var membership = directory.AddMembership(user.UserId, "Synthetic Firm");

        var registeredServices = new List<string>();
        var baseHost = PortalAuthorizationHarness.CreateHost(directory, sessions);
        var host = baseHost.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Only the two SQL-Server-backed collaborators are replaced. The
                // workflows, credential source, and content adapter stay real.
                services.RemoveAll<IIntakeStore>();
                services.RemoveAll<IReviewRecordRepository>();
                services.AddSingleton<IIntakeStore>(store);
                services.AddSingleton<IReviewRecordRepository>(reviews);

                // Captured after the composition root has run, so this is the
                // host's complete registration inventory.
                registeredServices.AddRange(
                    services.Select(descriptor => string.Join(
                        "|",
                        descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name,
                        descriptor.ImplementationType?.FullName ?? string.Empty,
                        descriptor.ImplementationInstance?.GetType().FullName
                            ?? string.Empty)));
            });
            configure?.Invoke(builder);
        });

        return new IntakeFirmHost(
            baseHost,
            host,
            directory,
            sessions,
            user,
            membership,
            registeredServices);
    }

    /// <summary>
    /// Every service type and implementation the built host registered. Used to
    /// prove that no extraction, parsing, mapping, or review-assembly service
    /// entered the runtime graph.
    /// </summary>
    internal IReadOnlyList<string> RegisteredServiceNames()
    {
        // Force the host to build if it has not already.
        _ = _host.Services;
        Assert.NotEmpty(_registeredServices);
        return _registeredServices;
    }

    internal HttpClient CreateAccountantClient() =>
        PortalAuthorizationHarness.CreateClient(_host, followRedirects: true);

    internal HttpClient CreateAnonymousClient() =>
        PortalAuthorizationHarness.CreateClient(_host, followRedirects: false);

    internal Task SignInAsync(HttpClient client) =>
        PortalAuthorizationHarness.SignInAsync(client);

    internal T Resolve<T>() where T : notnull =>
        _host.Services.GetRequiredService<T>();

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        _baseHost.Dispose();
        return ValueTask.CompletedTask;
    }
}

// ------------------------------------------------------------------- helpers

internal sealed record IssuedCase(
    Guid CaseId,
    string Credential,
    string ClientUrl);

internal sealed record RedemptionResult(
    string Location,
    string CookieValue);

/// <summary>
/// Drives the real HTTP surface. Nothing here reaches into the container: every
/// step is the request a browser would make.
/// </summary>
internal static class IntakeFlow
{
    internal const string DisplayName = "Synthetic Intake Taxpayer";

    private static readonly Regex Antiforgery = new(
        "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ClientUrl = new(
        @"https://intake\.test\.invalid/client-intake/link/([A-Za-z0-9\-_]{43})",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CaseLink = new(
        @"/intake-cases/([0-9a-fA-F\-]{36})",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    internal static string AntiforgeryToken(string html)
    {
        var match = Antiforgery.Match(html);
        Assert.True(match.Success, "Expected a rendered antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    internal static async Task<IssuedCase> CreateCaseAsync(
        HttpClient accountant,
        int expectedT4Count,
        int expectedMedicalCount)
    {
        using var page = await accountant.GetAsync("/intake-cases/new");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        using var response = await accountant.PostAsync(
            "/intake-cases",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["primaryTaxpayerDisplayName"] = DisplayName,
                    ["taxYear"] = "2025",
                    ["expectedT4Count"] =
                        expectedT4Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["expectedMedicalExpenseCount"] =
                        expectedMedicalCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Parse(await response.Content.ReadAsStringAsync());
    }

    internal static async Task<IssuedCase> ReissueAsync(
        HttpClient accountant,
        Guid caseId)
    {
        using var progress = await accountant.GetAsync($"/intake-cases/{caseId:D}");
        var token = AntiforgeryToken(await progress.Content.ReadAsStringAsync());

        using var response = await accountant.PostAsync(
            $"/intake-cases/{caseId:D}/link/reissue",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Parse(await response.Content.ReadAsStringAsync());
    }

    internal static async Task<HttpResponseMessage> RevokeAsync(
        HttpClient accountant,
        Guid caseId)
    {
        using var progress = await accountant.GetAsync($"/intake-cases/{caseId:D}");
        var token = AntiforgeryToken(await progress.Content.ReadAsStringAsync());

        return await accountant.PostAsync(
            $"/intake-cases/{caseId:D}/link/revoke",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                }));
    }

    internal static async Task<HttpResponseMessage> AssociateReviewAsync(
        HttpClient accountant,
        Guid caseId,
        Guid recordId) =>
        await AssociateReviewRawAsync(accountant, caseId, recordId.ToString("D"));

    internal static async Task<HttpResponseMessage> AssociateReviewRawAsync(
        HttpClient accountant,
        Guid caseId,
        string recordId)
    {
        using var progress = await accountant.GetAsync($"/intake-cases/{caseId:D}");
        var token = AntiforgeryToken(await progress.Content.ReadAsStringAsync());

        return await accountant.PostAsync(
            $"/intake-cases/{caseId:D}/review-association",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["recordId"] = recordId,
                }));
    }

    internal static async Task<RedemptionResult> RedeemAsync(
        HttpClient client,
        string credential)
    {
        using var confirmation = await client.GetAsync(
            $"/client-intake/link/{credential}");
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var html = await confirmation.Content.ReadAsStringAsync();

        // The raw credential never appears in the confirmation body, action, or
        // hidden field.
        Assert.DoesNotContain(credential, html, StringComparison.Ordinal);

        using var response = await client.PostAsync(
            "/client-intake/redeem",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = AntiforgeryToken(html),
                    ["redemptionHandle"] = HiddenValue(html, "redemptionHandle"),
                }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "__Secure-taxai.client-intake=",
                StringComparison.Ordinal));

        return new RedemptionResult(
            response.Headers.Location?.ToString() ?? string.Empty,
            cookie);
    }

    internal static async Task SaveAnswersAsync(
        HttpClient client,
        AvailabilityAnswerCode t4Answer,
        AvailabilityAnswerCode? medicalAnswer)
    {
        using var page = await client.GetAsync("/client-intake");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["t4Answer"] = t4Answer.ToString(),
        };

        if (medicalAnswer is { } medical)
        {
            form["medicalExpenseAnswer"] = medical.ToString();
        }

        using var response = await client.PostAsync(
            "/client-intake/answers",
            new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    internal static async Task<HttpResponseMessage> PostAnswerAsync(
        HttpClient client,
        string field,
        string value)
    {
        using var page = await client.GetAsync("/client-intake");
        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        return await client.PostAsync(
            "/client-intake/answers",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    [field] = value,
                }));
    }

    internal static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        DocumentCategory category,
        int ordinal,
        IntakeMediaKind mediaKind,
        byte[] body,
        string transportFilename = "upload.bin")
    {
        var token = await SessionTokenAsync(client);

        var multipart = new MultipartFormDataContent($"taxai-{Guid.NewGuid():N}");
        multipart.Add(new StringContent(category.ToString()), "category");
        multipart.Add(
            new StringContent(ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "ordinal");
        multipart.Add(new StringContent(mediaKind.ToString()), "declaredMediaKind");
        multipart.Add(
            new StringContent(body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "declaredLength");
        var file = new ByteArrayContent(body);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(file, "document", transportFilename);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/client-intake/documents")
        {
            Content = multipart,
        };
        request.Headers.Add("X-TaxAI-Antiforgery", token);
        return await client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> UploadTwoFilesAsync(HttpClient client)
    {
        var token = await SessionTokenAsync(client);

        var multipart = new MultipartFormDataContent($"taxai-{Guid.NewGuid():N}");
        multipart.Add(new StringContent(nameof(DocumentCategory.T4Slip)), "category");
        multipart.Add(new StringContent("1"), "ordinal");
        multipart.Add(new StringContent(nameof(IntakeMediaKind.Pdf)), "declaredMediaKind");
        var first = SyntheticSource.Pdf("first of two");
        var second = SyntheticSource.Pdf("second of two");
        multipart.Add(
            new StringContent(first.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "declaredLength");
        multipart.Add(new ByteArrayContent(first), "document", "upload.bin");
        multipart.Add(new ByteArrayContent(second), "document", "upload.bin");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/client-intake/documents")
        {
            Content = multipart,
        };
        request.Headers.Add("X-TaxAI-Antiforgery", token);
        return await client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> SubmitAsync(HttpClient client)
    {
        using var page = await client.GetAsync("/client-intake");
        var token = AntiforgeryToken(await page.Content.ReadAsStringAsync());

        return await client.PostAsync(
            "/client-intake/submissions",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                }));
    }

    /// <summary>Creates a case and drives it to Ready over HTTP.</summary>
    internal static async Task<IssuedCase> ReadyCaseAsync(
        IntakeWorld world,
        HttpClient accountant)
    {
        var issued = await CreateCaseAsync(accountant, 1, 0);
        using var client = world.FirmOne.CreateAnonymousClient();
        await RedeemAsync(client, issued.Credential);
        await SaveAnswersAsync(client, AvailabilityAnswerCode.AvailableNow, null);

        using (var upload = await UploadAsync(
                   client,
                   DocumentCategory.T4Slip,
                   1,
                   IntakeMediaKind.Pdf,
                   SyntheticSource.Pdf("ready case body")))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        using (var submitted = await SubmitAsync(client))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        return issued;
    }

    internal static string HiddenValue(string html, string name)
    {
        var marker = $"name=\"{name}\"";
        var nameAt = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(nameAt >= 0, $"Expected a hidden {name} field.");
        var valueAt = html.IndexOf("value=\"", nameAt, StringComparison.Ordinal);
        Assert.True(valueAt >= 0);
        valueAt += "value=\"".Length;
        var end = html.IndexOf('"', valueAt);
        Assert.True(end > valueAt);
        return WebUtility.HtmlDecode(html[valueAt..end]);
    }

    private static async Task<string> SessionTokenAsync(HttpClient client)
    {
        using var page = await client.GetAsync("/client-intake");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return AntiforgeryToken(await page.Content.ReadAsStringAsync());
    }

    private static IssuedCase Parse(string html)
    {
        var url = ClientUrl.Match(html);
        Assert.True(url.Success, "Expected exactly one rendered client URL.");
        var caseLink = CaseLink.Match(html);
        Assert.True(caseLink.Success, "Expected a rendered case link.");

        return new IssuedCase(
            Guid.Parse(caseLink.Groups[1].Value),
            url.Groups[1].Value,
            url.Value);
    }
}

/// <summary>
/// Signature-valid synthetic files assembled at runtime. These are not real
/// documents; the bodies exist only so the upload boundary has bytes with a
/// correct magic prefix to stream and encrypt.
/// </summary>
internal static class SyntheticSource
{
    internal static byte[] Pdf(string body) =>
        [.. "%PDF-1.7\n"u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(body)];

    internal static byte[] Png(string body) =>
    [
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
        .. System.Text.Encoding.UTF8.GetBytes(body),
    ];

    internal static byte[] Jpeg(string body) =>
        [0xff, 0xd8, 0xff, .. System.Text.Encoding.UTF8.GetBytes(body)];

    internal static byte[] For(IntakeMediaKind kind, string body) =>
        kind switch
        {
            IntakeMediaKind.Pdf => Pdf(body),
            IntakeMediaKind.Jpeg => Jpeg(body),
            IntakeMediaKind.Png => Png(body),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    internal static byte[] PdfOfLength(int length)
    {
        var prefix = "%PDF-1.7\n"u8.ToArray();
        Assert.True(length >= prefix.Length);
        var bytes = new byte[length];
        prefix.CopyTo(bytes, 0);
        for (var index = prefix.Length; index < length; index++)
        {
            bytes[index] = (byte)('a' + (index % 26));
        }

        return bytes;
    }
}

// -------------------------------------------------- substituted collaborators

/// <summary>
/// The intake metadata store, in memory. This is the only intake collaborator
/// substituted in this suite, and it is substituted solely because the real one
/// is <c>SqlIntakeStore</c> over SQL Server. Its relational counterpart is
/// exercised for real in <c>Persistence.Tests</c>.
/// </summary>
internal sealed class InMemoryIntakeStore : IIntakeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid Firm, Guid Case), StoredIntakeCase> _cases = [];
    private readonly List<SessionState> _sessions = [];
    private readonly Dictionary<(Guid Firm, Guid Case), List<IntakeAuditEventRecord>>
        _audits = [];

    /// <summary>Set to fail the next scoped read, proving fail-closed behaviour.</summary>
    internal bool FailNextRead { get; set; }

    internal IReadOnlyList<StoredIntakeCase> All
    {
        get
        {
            lock (_gate)
            {
                return _cases.Values.ToArray();
            }
        }
    }

    internal int Count => All.Count;

    internal StoredIntakeCase Single() => Assert.Single(All);

    internal IReadOnlyList<StoredIntakeCase> CasesFor(Guid firmId) =>
        All.Where(stored => stored.Firm.Value == firmId).ToArray();

    internal IReadOnlyList<IntakeAuditEventRecord> Audit(Guid firmId, Guid caseId)
    {
        lock (_gate)
        {
            return _audits.TryGetValue((firmId, caseId), out var events)
                ? events.ToArray()
                : [];
        }
    }

    /// <summary>Revokes every live client session, as an external actor would.</summary>
    internal void RevokeAllSessions(DateTimeOffset atUtc)
    {
        lock (_gate)
        {
            for (var index = 0; index < _sessions.Count; index++)
            {
                _sessions[index] = _sessions[index] with { RevokedAtUtc = atUtc };
            }
        }
    }

    public Task<IntakeStoreWriteCode> CreateAsync(
        NewIntakeCase intakeCase,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (intakeCase.Case.Firm.Value, intakeCase.Case.CaseId);
            if (_cases.ContainsKey(key))
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            _cases.Add(key, intakeCase.Case);
            _audits.Add(key, intakeCase.AuditEvents.ToList());
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeLinkScope?> FindLinkScopeAsync(
        IntakeTokenDigest linkDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailing();
        lock (_gate)
        {
            var match = _cases.Values.SingleOrDefault(candidate =>
                candidate.CurrentLink.CredentialDigest.Equals(linkDigest) &&
                candidate.CurrentLink.RevokedAtUtc is null &&
                candidate.CurrentLink.SpentAtUtc is null &&
                nowUtc < candidate.CurrentLink.ExpiresAtUtc);
            return Task.FromResult(match is null
                ? null
                : new IntakeLinkScope(
                    match.Firm,
                    match.CaseId,
                    linkDigest,
                    match.CurrentLink.ExpiresAtUtc));
        }
    }

    public Task<IntakeStoreWriteCode> RedeemLinkAsync(
        IntakeLinkScope linkScope,
        NewIntakeSession session,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (linkScope.Firm.Value, linkScope.CaseId);
            if (!_cases.TryGetValue(key, out var storedCase) ||
                !storedCase.CurrentLink.CredentialDigest.Equals(linkScope.LinkDigest) ||
                storedCase.CurrentLink.RevokedAtUtc is not null ||
                storedCase.CurrentLink.SpentAtUtc is not null ||
                session.CreatedAtUtc >= storedCase.CurrentLink.ExpiresAtUtc)
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            _sessions.Add(new SessionState(
                storedCase.Firm,
                storedCase.CaseId,
                session.SessionId,
                session.SessionDigest,
                session.AbsoluteExpiresAtUtc,
                session.IdleExpiresAtUtc,
                RevokedAtUtc: null));
            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                currentLink: storedCase.CurrentLink with
                {
                    SpentAtUtc = session.CreatedAtUtc,
                });
            AddAudits(key, [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeClientScope?> FindClientScopeAsync(
        IntakeTokenDigest sessionDigest,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailing();
        lock (_gate)
        {
            var session = _sessions.SingleOrDefault(candidate =>
                candidate.Digest.Equals(sessionDigest) &&
                candidate.RevokedAtUtc is null &&
                nowUtc < candidate.IdleExpiresAtUtc &&
                nowUtc < candidate.AbsoluteExpiresAtUtc);
            return Task.FromResult(session is null
                ? null
                : new IntakeClientScope(
                    session.Firm,
                    session.CaseId,
                    session.SessionId,
                    session.Digest,
                    session.AbsoluteExpiresAtUtc,
                    session.IdleExpiresAtUtc,
                    nowUtc));
        }
    }

    public Task<IntakeCaseList> ListCasesAsync(
        TrustedFirmId firm,
        int maximumCases,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailing();
        lock (_gate)
        {
            var newest = _cases.Values
                .Where(candidate => candidate.Firm.Value == firm.Value)
                .Select(candidate => new
                {
                    Case = candidate,
                    CreatedAtUtc = _audits[(candidate.Firm.Value, candidate.CaseId)]
                        .Where(audit => audit.Kind == IntakeAuditKind.CaseCreated)
                        .Min(audit => audit.OccurredAtUtc),
                })
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .ThenBy(candidate => candidate.Case.CaseId)
                .Take(maximumCases + 1)
                .Select(candidate => new IntakeCaseListEntry(
                    candidate.Case.CaseId,
                    candidate.Case.PrimaryTaxpayerDisplayName,
                    candidate.Case.TaxYear))
                .ToArray();
            return Task.FromResult(new IntakeCaseList(
                newest.Take(maximumCases).ToArray(),
                newest.Length > maximumCases));
        }
    }

    public Task<StoredIntakeCase?> FindAsync(
        TrustedFirmId firm,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailing();
        lock (_gate)
        {
            _cases.TryGetValue((firm.Value, caseId), out var storedCase);
            return Task.FromResult(storedCase);
        }
    }

    public Task<StoredIntakeCase?> FindForClientAsync(
        IntakeClientScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailing();
        lock (_gate)
        {
            return Task.FromResult(Live(scope)
                ? _cases.GetValueOrDefault((scope.Firm.Value, scope.CaseId))
                : null);
        }
    }

    public Task<IntakeStoreWriteCode> ReissueLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeLinkRecord replacement,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (firm.Value, caseId);
            if (!_cases.TryGetValue(key, out var storedCase) ||
                storedCase.ConcurrencyVersion != expectedConcurrencyVersion)
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            RevokeSessions(key, replacement.IssuedAtUtc);
            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                currentLink: replacement);
            AddAudits(key, auditEvents);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> RevokeLinkAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        DateTimeOffset revokedAtUtc,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (firm.Value, caseId);
            if (!_cases.TryGetValue(key, out var storedCase) ||
                storedCase.ConcurrencyVersion != expectedConcurrencyVersion ||
                storedCase.CurrentLink.RevokedAtUtc is not null)
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            RevokeSessions(key, revokedAtUtc);
            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                currentLink: storedCase.CurrentLink with
                {
                    RevokedAtUtc = revokedAtUtc,
                });
            AddAudits(key, [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AppendAnswersAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IReadOnlyList<IntakeAnswerVersionRecord> answers,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryMutation(
                    scope,
                    expectedConcurrencyVersion,
                    renewedIdleExpiresAtUtc,
                    out var storedCase))
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (answers.Any(answer => storedCase.AnswerVersions.Any(existing =>
                    existing.Category == answer.Category &&
                    existing.Version == answer.Version)))
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            StoreMutation(
                scope,
                Copy(
                    storedCase,
                    concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                    answers: [.. storedCase.AnswerVersions, .. answers]),
                renewedIdleExpiresAtUtc,
                auditEvents);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeUploadAttemptRecord attempt,
        IntakeDocumentRecord? document,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryMutation(
                    scope,
                    expectedConcurrencyVersion,
                    renewedIdleExpiresAtUtc,
                    out var storedCase))
            {
                if (document is not null &&
                    storedCase is not null &&
                    storedCase.Documents.Any(existing =>
                        existing.Fingerprint.SequenceEqual(document.Fingerprint)))
                {
                    return Task.FromResult(IntakeStoreWriteCode.Duplicate);
                }

                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (document is not null &&
                storedCase.Documents.Any(existing =>
                    existing.Fingerprint.SequenceEqual(document.Fingerprint)))
            {
                return Task.FromResult(IntakeStoreWriteCode.Duplicate);
            }

            StoreMutation(
                scope,
                Copy(
                    storedCase,
                    concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                    documents: document is null
                        ? storedCase.Documents
                        : [.. storedCase.Documents, document],
                    attempts: [.. storedCase.UploadAttempts, attempt]),
                renewedIdleExpiresAtUtc,
                [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AppendSubmissionAsync(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IntakeSubmissionRecord submission,
        IReadOnlyList<IntakeAuditEventRecord> auditEvents,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!TryMutation(
                    scope,
                    expectedConcurrencyVersion,
                    renewedIdleExpiresAtUtc,
                    out var storedCase))
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (storedCase.Submissions.Any(existing =>
                    existing.Version == submission.Version))
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            StoreMutation(
                scope,
                Copy(
                    storedCase,
                    concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                    submissions: [.. storedCase.Submissions, submission]),
                renewedIdleExpiresAtUtc,
                auditEvents);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    public Task<IntakeStoreWriteCode> AssociateReviewAsync(
        TrustedFirmId firm,
        Guid caseId,
        long expectedConcurrencyVersion,
        IntakeReviewAssociationRecord association,
        IntakeAuditEventRecord auditEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (firm.Value, caseId);
            if (!_cases.TryGetValue(key, out var storedCase))
            {
                return Task.FromResult(IntakeStoreWriteCode.NotFound);
            }

            if (storedCase.ConcurrencyVersion != expectedConcurrencyVersion ||
                storedCase.ReviewAssociation is not null)
            {
                return Task.FromResult(IntakeStoreWriteCode.Conflict);
            }

            _cases[key] = Copy(
                storedCase,
                concurrencyVersion: storedCase.ConcurrencyVersion + 1,
                association: association,
                replaceAssociation: true);
            AddAudits(key, [auditEvent]);
            return Task.FromResult(IntakeStoreWriteCode.Success);
        }
    }

    private void ThrowIfFailing()
    {
        if (!FailNextRead)
        {
            return;
        }

        FailNextRead = false;
        throw new InvalidOperationException("The intake store is unavailable.");
    }

    private bool Live(IntakeClientScope scope) =>
        _sessions.Any(session =>
            session.Firm.Equals(scope.Firm) &&
            session.CaseId == scope.CaseId &&
            session.SessionId == scope.SessionId &&
            session.Digest.Equals(scope.SessionDigest) &&
            session.RevokedAtUtc is null &&
            scope.EvaluationAtUtc < session.IdleExpiresAtUtc &&
            scope.EvaluationAtUtc < session.AbsoluteExpiresAtUtc);

    private bool TryMutation(
        IntakeClientScope scope,
        long expectedConcurrencyVersion,
        DateTimeOffset renewedIdleExpiresAtUtc,
        out StoredIntakeCase storedCase)
    {
        storedCase = null!;
        if (!Live(scope) ||
            !_cases.TryGetValue((scope.Firm.Value, scope.CaseId), out var existing))
        {
            return false;
        }

        storedCase = existing;
        return storedCase.ConcurrencyVersion == expectedConcurrencyVersion &&
            renewedIdleExpiresAtUtc <= scope.AbsoluteExpiresAtUtc;
    }

    private void StoreMutation(
        IntakeClientScope scope,
        StoredIntakeCase replacement,
        DateTimeOffset renewedIdleExpiresAtUtc,
        IReadOnlyList<IntakeAuditEventRecord> audits)
    {
        _cases[(scope.Firm.Value, scope.CaseId)] = replacement;
        var index = _sessions.FindIndex(session =>
            session.SessionId == scope.SessionId &&
            session.Firm.Equals(scope.Firm) &&
            session.CaseId == scope.CaseId);
        _sessions[index] = _sessions[index] with
        {
            IdleExpiresAtUtc = renewedIdleExpiresAtUtc,
        };
        AddAudits((scope.Firm.Value, scope.CaseId), audits);
    }

    private void RevokeSessions(
        (Guid Firm, Guid Case) key,
        DateTimeOffset revokedAtUtc)
    {
        for (var index = 0; index < _sessions.Count; index++)
        {
            var session = _sessions[index];
            if (session.Firm.Value == key.Firm && session.CaseId == key.Case)
            {
                _sessions[index] = session with { RevokedAtUtc = revokedAtUtc };
            }
        }
    }

    private void AddAudits(
        (Guid Firm, Guid Case) key,
        IReadOnlyList<IntakeAuditEventRecord> events)
    {
        var target = _audits[key];
        foreach (var audit in events)
        {
            target.Add(audit with { Sequence = target.Count + 1 });
        }
    }

    private static StoredIntakeCase Copy(
        StoredIntakeCase source,
        long? concurrencyVersion = null,
        IntakeLinkRecord? currentLink = null,
        IReadOnlyList<IntakeAnswerVersionRecord>? answers = null,
        IReadOnlyList<IntakeDocumentRecord>? documents = null,
        IReadOnlyList<IntakeUploadAttemptRecord>? attempts = null,
        IReadOnlyList<IntakeSubmissionRecord>? submissions = null,
        IntakeReviewAssociationRecord? association = null,
        bool replaceAssociation = false) =>
        new(
            source.Firm,
            source.CaseId,
            source.PrimaryParticipantId,
            source.PrimaryTaxpayerDisplayName,
            source.TaxYear,
            source.CaseStatus,
            concurrencyVersion ?? source.ConcurrencyVersion,
            source.ExpectedItems,
            currentLink ?? source.CurrentLink,
            answers ?? source.AnswerVersions,
            documents ?? source.Documents,
            attempts ?? source.UploadAttempts,
            submissions ?? source.Submissions,
            replaceAssociation ? association : source.ReviewAssociation);

    private sealed record SessionState(
        TrustedFirmId Firm,
        Guid CaseId,
        Guid SessionId,
        IntakeTokenDigest Digest,
        DateTimeOffset AbsoluteExpiresAtUtc,
        DateTimeOffset IdleExpiresAtUtc,
        DateTimeOffset? RevokedAtUtc);
}

/// <summary>
/// The existing firm-scoped review contract, in memory. It refuses to save: the
/// intake workflow must never create a review record, and a call here is the
/// failure this suite exists to catch.
/// </summary>
internal sealed class RecordingReviewRepository : IReviewRecordRepository
{
    private readonly Dictionary<(Guid FirmId, Guid RecordId), PersistedReviewRecord>
        _records = [];

    internal List<(Guid FirmId, Guid RecordId)> Reads { get; } = [];

    internal int SaveCalls { get; private set; }

    /// <summary>Adds one record through the test, never through the workflow.</summary>
    internal Guid AddRecord(Guid firmId, Guid caseId)
    {
        var recordId = Guid.NewGuid();
        _records[(firmId, recordId)] = ReviewRecordFactory.Create(
            firmId,
            recordId,
            caseId);
        return recordId;
    }

    public Task<TaxLogicEngine.Models.ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm,
        PersistedReviewRecord record,
        CancellationToken cancellationToken)
    {
        SaveCalls++;
        throw new InvalidOperationException(
            "The intake flow attempted to create a review record.");
    }

    public Task<PersistedReviewRecord?> FindAsync(
        TrustedFirmId firm,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        Reads.Add((firm.Value, recordId));
        return Task.FromResult(
            _records.TryGetValue((firm.Value, recordId), out var record)
                ? record
                : null);
    }
}

/// <summary>
/// Builds one persisted review record naming an exact intake case. The values
/// are obviously synthetic and carry no SIN in any form.
/// </summary>
internal static class ReviewRecordFactory
{
    internal static PersistedReviewRecord Create(
        Guid firmId,
        Guid recordId,
        Guid caseId)
    {
        var slipId = Guid.NewGuid();
        var lineEntryId = Guid.NewGuid();

        var sourceCode = Construct<PersistedSourceBoxCode>(
            Guid.NewGuid(),
            firmId,
            lineEntryId,
            0,
            "B_A");
        var lineEntry = Construct<PersistedLineEntry>(
            lineEntryId,
            firmId,
            slipId,
            0,
            "L_A",
            2.0050m,
            Array.AsReadOnly<PersistedSourceBoxCode>([sourceCode]));
        var boxAmount = Construct<PersistedBoxAmount>(
            Guid.NewGuid(),
            firmId,
            slipId,
            0,
            "B_A",
            -1.005m);
        var unmappedCode = Construct<PersistedUnmappedBoxCode>(
            Guid.NewGuid(),
            firmId,
            slipId,
            0,
            "B_B");
        var slip = Construct<PersistedSlip>(
            slipId,
            firmId,
            recordId,
            0,
            SlipTypeCodes.T4,
            Guid.NewGuid(),
            IntakeFlow.DisplayName,
            "Synthetic Employer",
            Array.AsReadOnly<PersistedBoxAmount>([boxAmount]),
            Array.AsReadOnly<PersistedLineEntry>([lineEntry]),
            Array.AsReadOnly<PersistedUnmappedBoxCode>([unmappedCode]));

        return Construct<PersistedReviewRecord>(
            recordId,
            firmId,
            caseId.ToString("D"),
            Guid.NewGuid().ToString("D"),
            2025,
            1,
            new DateTimeOffset(new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
            new DateTimeOffset(new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
            new DateTimeOffset(new DateTime(2037, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Array.AsReadOnly<PersistedSlip>([slip]));
    }

    private static T Construct<T>(params object?[] arguments) =>
        (T)typeof(T)
            .GetConstructors(
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
            .Single(constructor => constructor.GetParameters().Length == arguments.Length)
            .Invoke(arguments);
}
