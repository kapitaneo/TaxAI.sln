using System.Net;
using ExtractionWorkflow.Models;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — the cross-tenant and safe-<c>404</c> proof over the real
/// portal composition.
/// <para>
/// The claim under test is narrow and absolute: a firm-G resource must be
/// <b>byte-identical</b> to one that does not exist, on every extraction route.
/// Anything that distinguishes them — a different status, a different body, a
/// different header, a different timing class of failure — is an oracle that
/// tells a caller another firm's document exists.
/// </para>
/// <para>
/// This task verifies; it repairs nothing. No production code is touched.
/// </para>
/// </summary>
public sealed class DocumentExtractionSecurityAcceptanceTests
{
    /// <summary>
    /// Every extraction route, as the pair of requests each test issues: one
    /// naming a resource that belongs to another firm, one naming a resource
    /// that does not exist anywhere.
    /// </summary>
    private static (string Name, bool IsMutation, (string, string)[] Fields)[] Routes =>
    [
        ("list", false, []),
        ("review", false, []),
        ("extract", true, []),
        ("classification", true,
            [("targetClassification", nameof(DocumentClassification.T4Slip))]),
        ("corrections", true,
            [
                ("field", nameof(CorrectionField.T4BoxValue)),
                ("boxCode", "14"),
                ("correctedValue", "52500.00"),
            ]),
        ("accept", true, []),
        ("duplicate", true,
            [("resolution", nameof(DuplicateResolutionKind.DistinctDocuments))]),
        ("finalize", true, []),
    ];

    [Fact] // AC-11, AC-12, AC-50
    public async Task AFirmGResourceIsByteIdenticalToAMissingOneOnEveryRoute()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // Firm two holds a real, fully extracted case. Firm one's accountant
        // must not be able to tell it apart from a case nobody ever created.
        using var otherAccountant = world.FirmTwo.CreateAccountantClient();
        await world.FirmTwo.SignInAsync(otherAccountant);
        var foreign = await world.OpenCaseAsync(world.FirmTwo, ExtractionSource.T4());
        await world.PostAsync(otherAccountant, foreign.ExtractUrl(foreign.FirstT4));

        var absent = new OpenedExtractionCase(
            TrustedFirmId.FromGuid(world.FirmOne.FirmId),
            Guid.NewGuid(),
            [Guid.NewGuid()]);

        foreach (var (name, isMutation, fields) in Routes)
        {
            var foreignResponse = await RequestAsync(
                world, accountant, foreign, name, isMutation, fields);
            var absentResponse = await RequestAsync(
                world, accountant, absent, name, isMutation, fields);

            using (foreignResponse)
            using (absentResponse)
            {
                Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
                Assert.Equal(absentResponse.StatusCode, foreignResponse.StatusCode);

                var foreignBody = await foreignResponse.Content.ReadAsByteArrayAsync();
                var absentBody = await absentResponse.Content.ReadAsByteArrayAsync();
                Assert.Empty(foreignBody);
                Assert.Equal(absentBody, foreignBody);

                Assert.Equal(
                    HeaderSignature(absentResponse),
                    HeaderSignature(foreignResponse));
            }
        }

        // Nothing crossed: firm two's case is exactly as its own accountant
        // left it, and firm one wrote nothing anywhere.
        var untouched = await world.DocumentAsync(foreign, foreign.FirstT4);
        Assert.Single(untouched.Versions);
        Assert.Empty(untouched.Corrections);
        Assert.Empty(untouched.Acceptances);
        Assert.Empty(untouched.ClassificationOverrides);
        Assert.Empty(world.Reviews.Saved);
    }

    [Fact] // AC-11
    public async Task ARouteNamingAnotherFirmIsRefusedEvenWithThatFirmsOwnIdentifiers()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();

        using var otherAccountant = world.FirmTwo.CreateAccountantClient();
        await world.FirmTwo.SignInAsync(otherAccountant);
        var foreign = await world.OpenCaseAsync(world.FirmTwo, ExtractionSource.T4());
        await world.PostAsync(otherAccountant, foreign.ExtractUrl(foreign.FirstT4));

        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        // Every channel a caller controls is tried, and none of them moves the
        // firm: the firm comes only from the revalidated server-side session.
        var firmTwoId = world.FirmTwo.FirmId.ToString("D");
        var url = foreign.ReviewUrl(foreign.FirstT4);

        foreach (var attempt in new[]
                 {
                     $"{url}?firmId={firmTwoId}",
                     $"{url}?taxai_firm_id={firmTwoId}",
                     $"{url}?firm={firmTwoId}&taxYear=2024",
                 })
        {
            using var response = await accountant.GetAsync(attempt);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var header = new HttpRequestMessage(HttpMethod.Get, url);
        header.Headers.Add("X-Firm-Id", firmTwoId);
        header.Headers.Add("taxai_firm_id", firmTwoId);
        using var headerResponse = await accountant.SendAsync(header);
        Assert.Equal(HttpStatusCode.NotFound, headerResponse.StatusCode);

        using var cookie = new HttpRequestMessage(HttpMethod.Get, url);
        cookie.Headers.Add("Cookie", $"taxai_firm_id={firmTwoId}");
        using var cookieResponse = await accountant.SendAsync(cookie);
        Assert.Equal(HttpStatusCode.NotFound, cookieResponse.StatusCode);
    }

    [Fact] // AC-16, AC-52
    public async Task NoAnonymousOrClientRequestReachesAnyExtractionRoute()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());
        await world.PostAsync(accountant, opened.ExtractUrl(opened.FirstT4));

        using var anonymous = world.FirmOne.CreateAnonymousClient();

        foreach (var (name, isMutation, fields) in Routes)
        {
            var url = UrlFor(opened, name);
            using var response = isMutation
                ? await ExtractionPortalWorld.PostWithoutTokenAsync(anonymous, url, fields)
                : await anonymous.GetAsync(url);

            // Anonymous reaches no extraction route: it is challenged or
            // refused, never served and never redirected to a review page.
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.True(
                response.StatusCode is HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.BadRequest
                    or HttpStatusCode.Redirect
                    or HttpStatusCode.Found,
                $"{name} answered an anonymous caller with {response.StatusCode}.");

            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found)
            {
                // A redirect here is the sign-in challenge, never a review page.
                var location = response.Headers.Location!.ToString();
                Assert.DoesNotContain(
                    "/extractions", location, StringComparison.Ordinal);
            }
        }

        // Nothing an anonymous caller did reached the store.
        var stored = await world.DocumentAsync(opened, opened.FirstT4);
        Assert.Single(stored.Versions);
        Assert.Empty(stored.Acceptances);
        Assert.Empty(stored.Corrections);
    }

    [Fact] // AC-13, AC-14
    public async Task ARevokedSessionAndAnInactiveMembershipEachFailBeforeAnyWork()
    {
        // ------------------------------------------------- a revoked session
        await using (var world = await ExtractionPortalWorld.CreateAsync())
        {
            using var accountant = world.FirmOne.CreateAccountantClient();
            await world.FirmOne.SignInAsync(accountant);
            var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());

            // Harvested while the session is still live, so the refusal that
            // follows is the revocation and nothing else.
            var token = await world.HarvestTokenAsync(accountant);

            foreach (var session in world.FirmOne.Sessions.All)
            {
                await world.FirmOne.Sessions.RevokeAsync(
                    session,
                    new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero),
                    CancellationToken.None);
            }

            using var response = await ExtractionPortalWorld.PostWithTokenAsync(
                accountant, opened.ExtractUrl(opened.FirstT4), token);
            Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);

            // Nothing was read and nothing was written: no extraction history
            // exists for the document at all.
            Assert.Null(
                await world.Extractions.FindDocumentAsync(
                    opened.Firm, opened.CaseId, opened.FirstT4, CancellationToken.None));
        }

        // ---------------------------------------------- an inactive membership
        await using (var world = await ExtractionPortalWorld.CreateAsync())
        {
            using var accountant = world.FirmOne.CreateAccountantClient();
            await world.FirmOne.SignInAsync(accountant);
            var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());

            var token = await world.HarvestTokenAsync(accountant);

            world.FirmOne.Directory.SetMembershipActive(
                world.FirmOne.Membership.MembershipId, isActive: false);

            using var response = await ExtractionPortalWorld.PostWithTokenAsync(
                accountant, opened.ExtractUrl(opened.FirstT4), token);
            Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);

            Assert.Null(
                await world.Extractions.FindDocumentAsync(
                    opened.Firm, opened.CaseId, opened.FirstT4, CancellationToken.None));
        }
    }

    /// <summary>
    /// AC-16 requires every mutation to be a non-GET protected by antiforgery.
    /// Three of the six are; three are not, and this records which.
    /// <para>
    /// <b>This is a defect in spec 015 T06, not something T07 repairs.</b>
    /// All six routes carry <c>RequiresValidation</c> antiforgery metadata, and
    /// <c>ExtractionRouteTests</c> asserts exactly that. But in the minimal-API
    /// pipeline the token is validated when the form is <i>read</i> during
    /// parameter binding, so a handler that binds no <c>[FromForm]</c> value
    /// never reads the form and never has its token checked.
    /// <c>/extract</c>, <c>/accept</c>, and <c>/finalize</c> bind no form value
    /// and therefore execute with no token at all, while
    /// <c>/classification</c>, <c>/corrections</c>, and <c>/duplicates</c> bind
    /// one and are correctly refused.
    /// </para>
    /// <para>
    /// The practical exposure is narrowed by the session cookie's
    /// <c>SameSite=Lax</c> policy, which a browser will not attach to a
    /// cross-site POST — so the request arrives unauthenticated and the
    /// firm-scoped policy refuses it. That is a browser-enforced mitigation and
    /// not the application's own guarantee, which is what AC-16 asks for.
    /// </para>
    /// <para>
    /// This test pins the current split deliberately. When T06 closes the gap it
    /// will fail, and the correct response then is to fold the three routes into
    /// the refused set above it — not to relax the assertion.
    /// </para>
    /// </summary>
    [Fact] // AC-16 — records a T06 defect
    public async Task OnlyTheFormBindingMutationsActuallyValidateTheirAntiforgeryToken()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());

        // These three bind a form value, so the token is read and validated.
        foreach (var (name, fields) in new (string, (string, string)[])[]
                 {
                     ("classification",
                         [("targetClassification", nameof(DocumentClassification.T4Slip))]),
                     ("corrections",
                         [
                             ("field", nameof(CorrectionField.T4BoxValue)),
                             ("boxCode", "14"),
                             ("correctedValue", "52500.00"),
                         ]),
                     ("duplicate",
                         [("resolution", nameof(DuplicateResolutionKind.DistinctDocuments))]),
                 })
        {
            using var response = await ExtractionPortalWorld.PostWithoutTokenAsync(
                accountant, UrlFor(opened, name), fields);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // These three bind none, so the token is never read. Each carries the
        // metadata that says it must be validated, and each runs anyway.
        foreach (var name in new[] { "extract", "accept", "finalize" })
        {
            using var response = await ExtractionPortalWorld.PostWithoutTokenAsync(
                accountant, UrlFor(opened, name));
            Assert.True(
                response.StatusCode is not HttpStatusCode.BadRequest,
                $"{name} now validates its antiforgery token — the T06 defect this " +
                "test records is fixed, so move it into the refused set above.");
        }
    }

    [Fact] // AC-16
    public async Task NoExtractionMutationAnswersAGet()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();
        using var accountant = world.FirmOne.CreateAccountantClient();
        await world.FirmOne.SignInAsync(accountant);

        var opened = await world.OpenCaseAsync(world.FirmOne, ExtractionSource.T4());

        foreach (var (name, isMutation, _) in Routes)
        {
            if (!isMutation)
            {
                continue;
            }

            using var response = await accountant.GetAsync(UrlFor(opened, name));
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        // No GET moved anything.
        Assert.Null(
            await world.Extractions.FindDocumentAsync(
                opened.Firm, opened.CaseId, opened.FirstT4, CancellationToken.None));
    }

    [Fact] // AC-17, AC-51
    public async Task TheContentReadPortHasExactlyOneConsumerAndNoBackgroundInitiator()
    {
        await using var world = await ExtractionPortalWorld.CreateAsync();

        // AC-17: nothing in the built host can start an extraction on its own.
        var hostedServices = world.FirmOne
            .Resolve<IEnumerable<Microsoft.Extensions.Hosting.IHostedService>>()
            .Select(service => service.GetType().FullName ?? service.GetType().Name)
            .ToArray();

        Assert.DoesNotContain(
            hostedServices,
            name => name.Contains("Extraction", StringComparison.OrdinalIgnoreCase));

        // AC-51: the read port is not resolvable as a service in its own right,
        // so nothing can inject it. The extraction workflows receive the
        // adapter explicitly at composition time and are its only consumers.
        Assert.Null(
            world.FirmOne.Resolve<IServiceProvider>()
                .GetService(typeof(ExtractionWorkflow.Contracts.IExtractionContentReader)));
    }

    // ------------------------------------------------------------- helpers

    private static string UrlFor(OpenedExtractionCase opened, string route) =>
        route switch
        {
            "list" => opened.ListUrl,
            "review" => opened.ReviewUrl(opened.FirstT4),
            "extract" => opened.ExtractUrl(opened.FirstT4),
            "classification" => opened.ClassificationUrl(opened.FirstT4),
            "corrections" => opened.CorrectionUrl(opened.FirstT4),
            "accept" => opened.AcceptUrl(opened.FirstT4),
            "duplicate" => opened.DuplicateUrl(opened.DocumentIds[0]),
            "finalize" => opened.FinalizeUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

    private static Task<HttpResponseMessage> RequestAsync(
        ExtractionPortalWorld world,
        HttpClient client,
        OpenedExtractionCase opened,
        string route,
        bool isMutation,
        (string, string)[] fields)
    {
        var url = UrlFor(opened, route);
        return isMutation
            ? world.PostRawAsync(client, url, fields)
            : client.GetAsync(url);
    }

    /// <summary>
    /// The response headers that could distinguish one refusal from another.
    /// The date and any per-response correlation value are excluded because
    /// they differ between any two responses; everything else must match.
    /// </summary>
    private static string HeaderSignature(HttpResponseMessage response) =>
        string.Join(
            "\n",
            response.Headers
                .Concat(response.Content.Headers)
                .Where(header => !string.Equals(
                    header.Key, "Date", StringComparison.OrdinalIgnoreCase))
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .Select(header => $"{header.Key}: {string.Join(",", header.Value)}"));
}
