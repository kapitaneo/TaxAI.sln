using System.Net;
using ClientIntake.Models;
using Microsoft.Net.Http.Headers;

namespace TaxAI.Portal.Tests;

public sealed class ClientIntakePageTests
{
    [Fact]
    public async Task LinkGetIsReadOnlyAndRedemptionCreatesProtectedCookieAndCleanRedirect()
    {
        var link = IntakePortalHarness.Secret(0x41);
        var session = IntakePortalHarness.Secret(0x42);
        var digest = new IntakeTokenDigest(
            Enumerable.Repeat((byte)0x51, 32).ToArray());
        var proof = new IntakeRedemptionProof(
            digest,
            IntakePortalHarness.Now.AddHours(1));
        var workflow = new IntakePortalHarness.ClientWorkflow
        {
            InspectionResult = IntakeResult<IntakeLinkInspection>.Success(
                new IntakeLinkInspection(2025, proof)),
            RedemptionResult = IntakeResult<RedeemedIntakeSession>.Success(
                new RedeemedIntakeSession(
                    session,
                    IntakePortalHarness.Now.AddMinutes(30))),
        };

        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);

        using var confirmation = await client.GetAsync(
            $"/client-intake/link/{link.Reveal()}");
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var confirmationHtml = await confirmation.Content.ReadAsStringAsync();
        Assert.DoesNotContain(link.Reveal(), confirmationHtml, StringComparison.Ordinal);
        Assert.Empty(workflow.RedemptionCalls);
        var antiforgery = IntakePortalHarness.AntiforgeryToken(confirmationHtml);
        var handle = HiddenValue(confirmationHtml, "redemptionHandle");
        Assert.NotEqual(link.Reveal(), handle);

        using var redeemed = await client.PostAsync(
            "/client-intake/redeem",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = antiforgery,
                    ["redemptionHandle"] = handle,
                }));

        Assert.Equal(HttpStatusCode.SeeOther, redeemed.StatusCode);
        Assert.Equal("/client-intake", redeemed.Headers.Location?.ToString());
        var cookie = SetCookieHeaderValue.Parse(
            Assert.Single(
                redeemed.Headers.GetValues("Set-Cookie"),
                value => value.StartsWith(
                    "__Secure-taxai.client-intake=",
                    StringComparison.Ordinal)));
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/client-intake", cookie.Path.ToString());
        Assert.DoesNotContain(session.Reveal(), cookie.Value.ToString(), StringComparison.Ordinal);
        Assert.Single(workflow.RedemptionCalls);
        Assert.Equal(proof, workflow.RedemptionCalls[0]);
    }

    [Fact]
    public async Task ClientPageRendersOnlyTheClosedProjectionAndPostsClosedAnswers()
    {
        var workflow = SuccessfulClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);
        await RedeemAsync(client);

        using var page = await client.GetAsync("/client-intake");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("2025 document intake", html, StringComparison.Ordinal);
        Assert.Contains(
            "Are the requested T4 documents available now?",
            html,
            StringComparison.Ordinal);
        Assert.Contains("T4 1", html, StringComparison.Ordinal);
        Assert.Contains("Requested", html, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.web.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic Client", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Firm", html, StringComparison.Ordinal);
        Assert.DoesNotContain("caseId", html, StringComparison.OrdinalIgnoreCase);

        var token = IntakePortalHarness.AntiforgeryToken(html);
        using var saved = await client.PostAsync(
            "/client-intake/answers",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["t4Answer"] =
                        nameof(AvailabilityAnswerCode.WillProvideLater),
                }));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var command = Assert.Single(workflow.AnswerCalls);
        var answer = Assert.Single(command.Answers!);
        Assert.Equal("T4Slip", answer.Category?.ToString());
        Assert.Equal(
            AvailabilityAnswerCode.WillProvideLater,
            answer.Answer);
    }

    [Fact]
    public async Task InvalidLinkSessionAndReplayShareTheEmpty404()
    {
        var workflow = new IntakePortalHarness.ClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);

        using var malformed = await client.GetAsync("/client-intake/link/not-a-token");
        using var unknown = await client.GetAsync(
            $"/client-intake/link/{IntakePortalHarness.Secret(0x33).Reveal()}");
        using var missingSession = await client.GetAsync("/client-intake");

        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(malformed.StatusCode, unknown.StatusCode);
        Assert.Equal(malformed.StatusCode, missingSession.StatusCode);
        Assert.Equal(string.Empty, await malformed.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await unknown.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await missingSession.Content.ReadAsStringAsync());
        Assert.Single(workflow.InspectionCalls);
    }

    internal static IntakePortalHarness.ClientWorkflow SuccessfulClientWorkflow()
    {
        var proof = new IntakeRedemptionProof(
            new IntakeTokenDigest(
                Enumerable.Repeat((byte)0x61, 32).ToArray()),
            IntakePortalHarness.Now.AddHours(1));
        var view = IntakePortalHarness.ClientView();
        return new IntakePortalHarness.ClientWorkflow
        {
            InspectionResult = IntakeResult<IntakeLinkInspection>.Success(
                new IntakeLinkInspection(2025, proof)),
            RedemptionResult = IntakeResult<RedeemedIntakeSession>.Success(
                new RedeemedIntakeSession(
                    IntakePortalHarness.Secret(0x72),
                    IntakePortalHarness.Now.AddMinutes(30))),
            GetResult = IntakeResult<ClientIntakeView>.Success(view),
            AnswersResult = IntakeResult<ClientIntakeView>.Success(view),
            UploadResult = IntakeResult<ClientIntakeView>.Success(view),
            SubmissionResult = IntakeResult<ClientIntakeView>.Success(view),
        };
    }

    internal static async Task RedeemAsync(HttpClient client)
    {
        var link = IntakePortalHarness.Secret(0x71);
        using var confirmation = await client.GetAsync(
            $"/client-intake/link/{link.Reveal()}");
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        var html = await confirmation.Content.ReadAsStringAsync();
        using var redeemed = await client.PostAsync(
            "/client-intake/redeem",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] =
                        IntakePortalHarness.AntiforgeryToken(html),
                    ["redemptionHandle"] =
                        HiddenValue(html, "redemptionHandle"),
                }));
        Assert.Equal(HttpStatusCode.SeeOther, redeemed.StatusCode);
    }

    private static string HiddenValue(string html, string name)
    {
        var marker = $"name=\"{name}\"";
        var nameAt = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(nameAt >= 0);
        var valueAt = html.IndexOf(
            "value=\"",
            nameAt,
            StringComparison.Ordinal);
        Assert.True(valueAt >= 0);
        valueAt += "value=\"".Length;
        var end = html.IndexOf('"', valueAt);
        Assert.True(end > valueAt);
        return WebUtility.HtmlDecode(html[valueAt..end]);
    }
}
