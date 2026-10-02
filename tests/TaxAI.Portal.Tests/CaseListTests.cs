using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ExtractionWorkflow.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxAI.Portal.Tests.EndToEnd;
using TaxAI.Portal.Tests.Support;
using TaxCases.Models;
using Tenancy.Models;

namespace TaxAI.Portal.Tests;

/// <summary>
/// Spec 017 T03 — the firm-scoped accountant case list and the accountant
/// navigation, over the real portal composition.
/// <para>
/// Two firms share one intake store, each behind its own real host, so a
/// cross-firm read here would be a real cross-firm read. The store is wrapped
/// only to record which firm each list read named and to fail on demand; every
/// handler, policy, session check, header, and component is production code.
/// </para>
/// </summary>
public sealed class CaseListTests
{
    private const string ListPath = "/intake-cases";

    private const string TruncationNotice =
        "Only the most recent cases are shown. Older cases are not listed.";

    private static readonly DateTimeOffset Epoch =
        new(2028, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static readonly Regex ProgressLink = new(
        "href=\"/intake-cases/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\"",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex DocumentsLink = new(
        "href=\"/intake-cases/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/extractions\"",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CaseRows = new(
        "<ol class=\"case-list\".*?</ol>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex AntiforgeryValue = new(
        "(?<=name=\"__RequestVerificationToken\" value=\")[^\"]+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // ---------------------------------------------------------- AC-58, AC-72

    [Fact] // AC-58
    public async Task TheListShowsTheFirmsCasesWithNamesYearsAndLinks()
    {
        await using var world = World.Create();
        var older = await world.SeedAsync(world.Intake.FirmOne, "Alder Synthetic", 2025, minute: 1);
        var newer = await world.SeedAsync(world.Intake.FirmOne, "Birch Synthetic", 2025, minute: 2);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);

        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Alder Synthetic", html, StringComparison.Ordinal);
        Assert.Contains("Birch Synthetic", html, StringComparison.Ordinal);

        // Newest first, each with a link to its progress and documents pages.
        Assert.Equal(
            new[] { newer.ToString("D"), older.ToString("D") },
            ProgressLink.Matches(html).Select(match => match.Groups[1].Value).ToArray());
        Assert.Equal(
            new[] { newer.ToString("D"), older.ToString("D") },
            DocumentsLink.Matches(html).Select(match => match.Groups[1].Value).ToArray());
        Assert.DoesNotContain(TruncationNotice, html, StringComparison.Ordinal);
    }

    [Fact] // AC-72
    public async Task TheRenderedTaxYearIsTheStoredTaxYear()
    {
        await using var world = World.Create();
        var caseId = await world.SeedAsync(world.Intake.FirmOne, "Cedar Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);

        var rows = Rows(await accountant.GetStringAsync(ListPath));

        var stored = Assert.Single(world.Intake.Store.All, item => item.CaseId == caseId);
        Assert.Matches(
            "Cedar Synthetic</strong>\\s*<span[^>]*>Tax year "
            + stored.TaxYear.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "</span>",
            rows);

        // No year is written into the new portal source or components, so the
        // rendered value can only have come from the stored case.
        var year = new Regex(@"\b(?:19|20)\d{2}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var file in NewAndChangedPortalFiles())
        {
            Assert.DoesNotMatch(year, ExecutableText(file));
        }
    }

    // ----------------------------------------------------------------- AC-59

    [Fact] // AC-59, FR-33
    public void TheBrowserModelIsExactlyTheApprovedAllowlist()
    {
        var row = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.Components.Pages.CaseListRowModel",
            throwOnError: true)!;
        var page = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.Components.Pages.CaseListPageModel",
            throwOnError: true)!;

        Assert.Equal(
            new[] { "CaseId:Guid", "PrimaryTaxpayerDisplayName:String", "TaxYear:Int32" },
            DeclaredProperties(row));
        Assert.Equal(
            new[] { "Cases:IReadOnlyList`1", "HasMore:Boolean" },
            DeclaredProperties(page));

        // The selector is for link targets only and is never serialized.
        Assert.Contains(
            row.GetProperty("CaseId")!.GetCustomAttributes(),
            attribute => attribute.GetType().Name == "JsonIgnoreAttribute");
    }

    [Fact] // FR-33
    public async Task TheListRendersNoOtherIdentifierInstantCountOrStatus()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmOne, "Elm Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);

        var html = await accountant.GetStringAsync(ListPath);

        foreach (var identifier in world.Intake.EveryServerIdentifier())
        {
            Assert.DoesNotContain(identifier, html, StringComparison.OrdinalIgnoreCase);
        }

        var rows = Rows(html);
        foreach (var absent in new[]
                 {
                     "UTC", "2028", nameof(CaseStatus.InformationGathering), "Ready",
                     "Uploaded", "Submitted", "Version",
                 })
        {
            Assert.DoesNotContain(absent, rows, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------- AC-60, AC-61, AC-62

    [Fact] // AC-60, AC-61
    public async Task AnotherFirmsCasesNeverAppearAndCannotBeInferred()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmOne, "Fir Synthetic", 2025, minute: 1);
        var foreign = await world.SeedAsync(world.Intake.FirmTwo, "Foreign Gum Synthetic", 2025, minute: 2);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);

        var before = await accountant.GetStringAsync(ListPath);

        Assert.DoesNotContain("Foreign Gum Synthetic", before, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign.ToString("D"), before, StringComparison.OrdinalIgnoreCase);
        Assert.Single(ProgressLink.Matches(before));

        // A case then created in the other firm changes nothing F can see.
        await world.SeedAsync(world.Intake.FirmTwo, "Foreign Hazel Synthetic", 2025, minute: 9);
        var after = await accountant.GetStringAsync(ListPath);

        Assert.Equal(Rows(before), Rows(after));
        Assert.DoesNotContain(TruncationNotice, after, StringComparison.Ordinal);
    }

    [Fact] // AC-62
    public async Task ACallerSuppliedFirmIsIgnored()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmOne, "Ironwood Synthetic", 2025, minute: 1);
        await world.SeedAsync(world.Intake.FirmTwo, "Foreign Juniper Synthetic", 2025, minute: 2);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);
        var foreignFirm = world.Intake.FirmTwo.FirmId.ToString("D");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ListPath}?firmId={foreignFirm}&firm={foreignFirm}&trustedFirmId={foreignFirm}");
        request.Headers.Add("X-Firm-Id", foreignFirm);
        request.Headers.Add("X-TaxAI-Firm", foreignFirm);
        request.Headers.Add("Cookie", $"firmId={foreignFirm}; taxai_firm_id={foreignFirm}");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["firmId"] = foreignFirm,
            ["taxai_firm_id"] = foreignFirm,
        });
        using var response = await accountant.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ironwood Synthetic", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreign Juniper Synthetic", html, StringComparison.Ordinal);
        Assert.DoesNotContain(foreignFirm, html, StringComparison.OrdinalIgnoreCase);

        // No list read named the other firm.
        Assert.Equal(new[] { world.Intake.FirmOne.FirmId }, world.Store.ListFirms);
    }

    // ---------------------------------------------------- AC-63, AC-64, AC-65

    [Fact] // AC-63
    public async Task AnUnauthenticatedRequestIsRefusedBeforeTheStore()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmOne, "Katsura Synthetic", 2025, minute: 1);
        using var anonymous = world.Intake.FirmOne.CreateAnonymousClient();

        using var response = await anonymous.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(
            "Katsura Synthetic",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Empty(world.Store.ListFirms);
    }

    [Fact] // AC-64
    public async Task ASessionWithNoActiveFirmIsRefusedBeforeTheStore()
    {
        await using var world = World.Create();
        var host = world.Intake.FirmOne;
        await world.SeedAsync(host, "Larch Synthetic", 2025, minute: 1);

        // Two active memberships: the sign-in issues no firm claim until one is
        // chosen, so the durable session has no active firm.
        host.Directory.AddMembership(host.User.UserId, "Second Synthetic Firm");
        using var accountant = await world.SignedInAsync(host);
        Assert.Null(Assert.Single(host.Sessions.All).ActiveFirmId);

        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Request refused.", await response.Content.ReadAsStringAsync());
        Assert.Empty(world.Store.ListFirms);
    }

    [Fact] // AC-65
    public async Task ARevokedSessionIsRefusedBeforeTheStore()
    {
        await using var world = World.Create();
        var host = world.Intake.FirmOne;
        await world.SeedAsync(host, "Maple Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(host);

        await host.Sessions.RevokeAsync(
            Assert.Single(host.Sessions.All),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(
            "Maple Synthetic",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        Assert.Empty(world.Store.ListFirms);
    }

    [Fact] // AC-65
    public async Task AnInactiveMembershipIsRefusedBeforeTheStore()
    {
        await using var world = World.Create();
        var host = world.Intake.FirmOne;
        await world.SeedAsync(host, "Nutmeg Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(host);

        host.Directory.SetMembershipActive(host.Membership.MembershipId, isActive: false);
        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Request refused.", await response.Content.ReadAsStringAsync());
        Assert.Empty(world.Store.ListFirms);
    }

    [Fact] // FR-36
    public async Task AnInactiveUserIsRefusedBeforeTheStore()
    {
        await using var world = World.Create();
        var host = world.Intake.FirmOne;
        await world.SeedAsync(host, "Oak Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(host);

        host.Directory.SetUserActive(host.User.UserId, isActive: false);
        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(world.Store.ListFirms);
    }

    // ----------------------------------------------------------------- AC-66

    [Fact] // AC-66
    public async Task AStoreFailureFailsClosedWithTheFixedErrorAndNoRow()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmOne, "Pine Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);
        world.Store.FailList = true;

        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Request could not be completed.", body);
        Assert.DoesNotContain(RecordingIntakeStore.FailureMarker, body, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- AC-67

    [Fact] // AC-67, FR-32
    public async Task SearchSortAndPagingInputChangeNothing()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmOne, "Quince Synthetic", 2025, minute: 1);
        await world.SeedAsync(world.Intake.FirmOne, "Rowan Synthetic", 2025, minute: 2);
        await world.SeedAsync(world.Intake.FirmOne, "Spruce Synthetic", 2025, minute: 3);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);

        var plain = await accountant.GetStringAsync(ListPath);
        var probed = await accountant.GetStringAsync(
            ListPath + "?search=Quince&q=Rowan&filter=Spruce&sort=asc&order=name"
            + "&page=2&skip=2&take=1&limit=1&maximumCases=1&offset=2");

        Assert.Equal(3, ProgressLink.Matches(plain).Count);
        Assert.Equal(Normalize(plain), Normalize(probed));
    }

    [Fact] // FR-32
    public void TheHandlerBindsNoRequestValue()
    {
        var source = ExecutableText(Path.Combine(
            RepositoryRoot, "src", "TaxAI.Portal", "CaseListEndpoints.cs"));

        foreach (var forbidden in new[]
                 {
                     "context.Request.", "FromQuery", "FromForm", "FromRoute", "FromHeader",
                     "FromBody", "RouteValues", "Query", "Headers", "Cookies", "ReadFormAsync",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // The mapped pattern has no route parameter, and the bound is a constant.
        Assert.Contains(
            "internal const string ListPath = \"/intake-cases\";",
            source,
            StringComparison.Ordinal);
        Assert.Contains("internal const int MaximumCases = 100;", source, StringComparison.Ordinal);
        Assert.Contains("PortalAuthorization.BootstrapStateFor(context)", source, StringComparison.Ordinal);
        Assert.Contains("PortalAuthorization.FirmScopedPolicy", source, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------- AC-69, AC-70

    [Fact] // AC-69
    public async Task OneCaseBeyondTheBoundRendersExactlyTheNewestMaximumAndTheNotice()
    {
        const int maximum = 100;
        await using var world = World.Create();
        var seeded = new List<Guid>();
        for (var minute = 1; minute <= maximum + 1; minute++)
        {
            seeded.Add(await world.SeedAsync(
                world.Intake.FirmOne, "Teak Synthetic", 2025, minute));
        }

        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);
        var html = await accountant.GetStringAsync(ListPath);

        var listed = ProgressLink.Matches(html).Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(maximum, listed.Length);
        Assert.Equal(
            seeded.Skip(1).Reverse().Select(caseId => caseId.ToString("D")).ToArray(),
            listed);
        Assert.DoesNotContain(seeded[0].ToString("D"), html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(TruncationNotice, html, StringComparison.Ordinal);

        // The bound is the host's constant, not a caller value.
        Assert.Equal(maximum, Assert.Single(world.Store.ListMaximums.Distinct()));
    }

    [Fact] // AC-70, FR-38
    public async Task AFirmWithNoCaseSeesTheEmptyStateAndACreateLink()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmTwo, "Foreign Upas Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);

        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No cases yet.", html, StringComparison.Ordinal);
        Assert.Contains("Create the first case", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/intake-cases/new\"", html, StringComparison.Ordinal);
        Assert.Empty(ProgressLink.Matches(html));
        Assert.DoesNotMatch(CaseRows, html);
    }

    // ----------------------------------------------------------------- AC-71

    [Fact] // AC-71, FR-37
    public async Task TheResponseIsHtmlWithTheIntakeSafetyHeaders()
    {
        await using var world = World.Create();
        await world.SeedAsync(world.Intake.FirmOne, "Viburnum Synthetic", 2025, minute: 1);
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);

        using var response = await accountant.GetAsync(ListPath);

        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            "no-referrer",
            Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "nosniff",
            Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self'", policy, StringComparison.Ordinal);

        // Local assets only, and nothing shaped like a JSON API.
        var html = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart(), StringComparison.Ordinal);
        foreach (var external in new[] { "http://", "https://", "//cdn", "<script" })
        {
            Assert.DoesNotContain(external, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ----------------------------------------------------------------- AC-80

    /// <summary>
    /// The baseline application inventory: every route the host mapped before
    /// this task, with the one method each answers.
    /// </summary>
    private static readonly string[] BaselineApplicationRoutes =
    {
        "GET /",
        "GET /client-intake",
        "GET /client-intake/link/{credential}",
        "GET /firms",
        "GET /health",
        "GET /intake-cases/new",
        "GET /intake-cases/{caseId:guid}",
        "GET /intake-cases/{caseId:guid}/extractions",
        "GET /intake-cases/{caseId:guid}/extractions/{documentId:guid}",
        "GET /reviews/{recordId:guid}",
        "GET /sign-in",
        "POST /client-intake/answers",
        "POST /client-intake/documents",
        "POST /client-intake/redeem",
        "POST /client-intake/submissions",
        "POST /firms/select",
        "POST /intake-cases",
        "POST /intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}",
        "POST /intake-cases/{caseId:guid}/extractions/finalize",
        "POST /intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept",
        "POST /intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification",
        "POST /intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections",
        "POST /intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract",
        "POST /intake-cases/{caseId:guid}/link/reissue",
        "POST /intake-cases/{caseId:guid}/link/revoke",
        "POST /intake-cases/{caseId:guid}/review-association",
        "POST /sign-out",
        "* /_blazor",
        "* /_blazor/disconnect/",
        "* /_blazor/initializers/",
        "* /_blazor/negotiate",
        "GET /_framework/opaque-redirect",
    };

    [Fact] // AC-80
    public void TheProductionInventoryIsTheBaselinePlusExactlyTheCaseList()
    {
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.AddApprovedDurableDataProtection());
        });
        using var client = host.CreateClient();

        Assert.Equal(
            Sorted(BaselineApplicationRoutes.Append("GET /intake-cases")),
            Inventory(host.Services));
    }

    [Fact] // AC-80
    public async Task TheDevelopmentInventoryAddsExactlyTheCaseListAndTheDevelopmentSignIn()
    {
        await using var world = World.Create();
        using var client = world.Intake.FirmOne.CreateAnonymousClient();

        Assert.Equal(
            Sorted(BaselineApplicationRoutes.Concat(new[]
            {
                "GET /intake-cases",
                "GET /development/sign-in",
                "POST /development/sign-in",
            })),
            Inventory(world.Intake.FirmOne.Resolve<IServiceProvider>()));
    }

    // ------------------------------------------------- FR-39, FR-40, AC-82

    [Fact] // FR-39
    public async Task EveryAccountantPageOffersTheCaseListTheCreatePageAndSignOut()
    {
        await using var world = World.Create();
        using var accountant = await world.SignedInAsync(world.Intake.FirmOne);
        var issued = await IntakeFlow.CreateCaseAsync(accountant, 1, 0);

        var list = await accountant.GetStringAsync(ListPath);
        var progress = await accountant.GetStringAsync($"/intake-cases/{issued.CaseId:D}");
        var documents = await accountant.GetStringAsync($"/intake-cases/{issued.CaseId:D}/extractions");

        foreach (var page in new[] { list, progress, documents })
        {
            Assert.Contains("aria-label=\"Accountant\"", page, StringComparison.Ordinal);
            Assert.Contains("href=\"/intake-cases\"", page, StringComparison.Ordinal);
            Assert.Contains("href=\"/intake-cases/new\"", page, StringComparison.Ordinal);
            Assert.Matches("<form method=\"post\" action=\"/sign-out\"[^>]*>\\s*<input type=\"hidden\" name=\"__RequestVerificationToken\"", page);
        }

        // The list reaches the case; the progress page reaches its documents.
        Assert.Contains($"href=\"/intake-cases/{issued.CaseId:D}\"", list, StringComparison.Ordinal);
        Assert.Contains($"href=\"/intake-cases/{issued.CaseId:D}/extractions\"", list, StringComparison.Ordinal);
        Assert.Contains($"href=\"/intake-cases/{issued.CaseId:D}/extractions\"", progress, StringComparison.Ordinal);
        Assert.Contains("Open case documents", progress, StringComparison.Ordinal);
    }

    [Fact] // FR-39
    public async Task TheRenderedSignOutFormRevokesTheSession()
    {
        await using var world = World.Create();
        var host = world.Intake.FirmOne;
        using var accountant = await world.SignedInAsync(host);

        var list = await accountant.GetStringAsync(ListPath);
        using var signedOut = await accountant.PostAsync(
            "/sign-out",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = IntakeFlow.AntiforgeryToken(list),
            }));

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        Assert.NotNull(Assert.Single(host.Sessions.All).RevokedAtUtc);

        using var refused = await accountant.GetAsync(ListPath);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact] // FR-40
    public void AccountantNavigationIsNeverInTheSharedLayoutOrAClientComponent()
    {
        var components = Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "Components");
        foreach (var relative in new[]
                 {
                     Path.Combine("Layout", "PortalLayout.razor"),
                     Path.Combine("Pages", "ClientIntakePage.razor"),
                     Path.Combine("Pages", "ClientLinkConfirmationPage.razor"),
                     Path.Combine("Shared", "ClientQuestionnairePanel.razor"),
                     Path.Combine("Shared", "ExpectedDocumentChecklist.razor"),
                     Path.Combine("Shared", "IntakeStatusBadge.razor"),
                     "App.razor",
                     "Routes.razor",
                 })
        {
            var markup = ExecutableText(Path.Combine(components, relative));
            Assert.DoesNotContain("AccountantNavigation", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("/intake-cases", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("/sign-out", markup, StringComparison.Ordinal);
        }
    }

    [Fact] // AC-82
    public void TheNewAndChangedComponentsStayPresentationOnly()
    {
        foreach (var file in NewAndChangedPortalFiles().Where(path =>
                     path.EndsWith(".razor", StringComparison.Ordinal)))
        {
            var markup = ExecutableText(file);
            foreach (var forbidden in new[]
                     {
                         "@page", "@inject", "@rendermode", "@onclick", "@onchange",
                         "CaptureUnmatchedValues", "HttpContext", "IIntakeStore",
                         "IExtractionStore", "OnInitialized", "OnParametersSet", "decimal",
                     })
            {
                Assert.DoesNotContain(forbidden, markup, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The navigation is parameterless and names only fixed routes.
        var navigation = ExecutableText(Path.Combine(
            RepositoryRoot, "src", "TaxAI.Portal", "Components", "Shared", "AccountantNavigation.razor"));
        Assert.DoesNotContain("[Parameter]", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("@(", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("@code", navigation, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static string Rows(string html)
    {
        var match = CaseRows.Match(html);
        Assert.True(match.Success, "Expected the rendered case rows.");
        return match.Value;
    }

    private static string Normalize(string html) =>
        AntiforgeryValue.Replace(html, "<token>");

    private static string[] DeclaredProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => $"{property.Name}:{property.PropertyType.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static string[] Sorted(IEnumerable<string> routes) =>
        routes.OrderBy(route => route, StringComparer.Ordinal).ToArray();

    private static string[] Inventory(IServiceProvider services) =>
        Sorted(services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                    ?? (IReadOnlyList<string>)new[] { "*" })
                .Select(method => $"{method} {endpoint.RoutePattern.RawText}")));

    private static string[] NewAndChangedPortalFiles()
    {
        var portal = Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");
        return new[]
        {
            Path.Combine(portal, "CaseListEndpoints.cs"),
            Path.Combine(portal, "Components", "Pages", "CaseListPage.razor"),
            Path.Combine(portal, "Components", "Pages", "CaseListPageModel.cs"),
            Path.Combine(portal, "Components", "Pages", "WorkflowNoticeKind.cs"),
            Path.Combine(portal, "Components", "Pages", "ExtractionListPage.razor"),
            Path.Combine(portal, "Components", "Pages", "ExtractionReviewPage.razor"),
            Path.Combine(portal, "Components", "Pages", "IntakeCaseProgressPage.razor"),
            Path.Combine(portal, "Components", "Shared", "AccountantNavigation.razor"),
            Path.Combine(portal, "Components", "Shared", "WorkflowNotice.razor"),
        };
    }

    /// <summary>Source with Razor and C# comments removed.</summary>
    private static string ExecutableText(string path) =>
        string.Join(
            "\n",
            Regex.Replace(
                    File.ReadAllText(path),
                    @"@\*.*?\*@",
                    string.Empty,
                    RegexOptions.Singleline | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1))
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line =>
                    line.Length > 0 &&
                    !line.StartsWith("//", StringComparison.Ordinal) &&
                    !line.StartsWith("*", StringComparison.Ordinal) &&
                    !line.StartsWith("/*", StringComparison.Ordinal)));

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

    // ------------------------------------------------------------- the world

    private sealed class World : IAsyncDisposable
    {
        private World(IntakeWorld intake, RecordingIntakeStore store)
        {
            Intake = intake;
            Store = store;
        }

        internal IntakeWorld Intake { get; }

        internal RecordingIntakeStore Store { get; }

        internal static World Create()
        {
            var recording = new RecordingIntakeStore();
            var extractions = new PortalExtractionStore();
            var intake = IntakeWorld.Create(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IIntakeStore>();
                    services.AddSingleton<IIntakeStore>(recording);

                    // The documents page reads the extraction store too; the
                    // real one is SQL Server, so the suite's in-memory one
                    // stands in exactly as it does in the extraction suites.
                    services.RemoveAll<IExtractionStore>();
                    services.AddSingleton<IExtractionStore>(extractions);
                }));
            recording.Inner = intake.Store;
            extractions.Intake = intake.Store;
            return new World(intake, recording);
        }

        internal async Task<HttpClient> SignedInAsync(IntakeFirmHost host)
        {
            var client = host.CreateAccountantClient();
            await host.SignInAsync(client);
            return client;
        }

        /// <summary>
        /// Stores one case directly, with its case-created audit event at a
        /// chosen instant, so ordering and the bound are deterministic.
        /// </summary>
        internal async Task<Guid> SeedAsync(
            IntakeFirmHost host,
            string displayName,
            int taxYear,
            int minute)
        {
            var caseId = Guid.NewGuid();
            var createdAtUtc = Epoch.AddMinutes(minute);
            var stored = new StoredIntakeCase(
                TrustedFirmId.FromGuid(host.FirmId),
                caseId,
                Guid.NewGuid(),
                displayName,
                taxYear,
                CaseStatus.InformationGathering,
                0,
                [new IntakeExpectedItemRecord(Guid.NewGuid(), DocumentCategory.T4Slip, 1)],
                new IntakeLinkRecord(
                    Guid.NewGuid(),
                    new IntakeTokenDigest(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray()),
                    createdAtUtc,
                    createdAtUtc.AddHours(1),
                    null,
                    null),
                [],
                [],
                [],
                [],
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
                                host.User.UserId,
                                null,
                                createdAtUtc),
                        ]),
                    CancellationToken.None));
            return caseId;
        }

        public ValueTask DisposeAsync() => Intake.DisposeAsync();
    }

    /// <summary>
    /// The shared in-memory intake store, wrapped only to observe the list read:
    /// which firm each call named, the bound it was given, and a failure on
    /// demand. Every other member passes straight through.
    /// </summary>
    private sealed class RecordingIntakeStore : IIntakeStore
    {
        internal const string FailureMarker = "LIST-FAILURE-MARKER-017-T03";

        private readonly object _gate = new();
        private readonly List<Guid> _listFirms = [];
        private readonly List<int> _listMaximums = [];

        internal IIntakeStore Inner { get; set; } = default!;

        internal bool FailList { get; set; }

        internal Guid[] ListFirms
        {
            get
            {
                lock (_gate)
                {
                    return _listFirms.ToArray();
                }
            }
        }

        internal int[] ListMaximums
        {
            get
            {
                lock (_gate)
                {
                    return _listMaximums.ToArray();
                }
            }
        }

        public Task<IntakeCaseList> ListCasesAsync(
            TrustedFirmId firm,
            int maximumCases,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _listFirms.Add(firm.Value);
                _listMaximums.Add(maximumCases);
            }

            return FailList
                ? Task.FromException<IntakeCaseList>(new InvalidOperationException(FailureMarker))
                : Inner.ListCasesAsync(firm, maximumCases, cancellationToken);
        }

        public Task<IntakeStoreWriteCode> CreateAsync(
            NewIntakeCase intakeCase, CancellationToken cancellationToken) =>
            Inner.CreateAsync(intakeCase, cancellationToken);

        public Task<IntakeLinkScope?> FindLinkScopeAsync(
            IntakeTokenDigest linkDigest, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Inner.FindLinkScopeAsync(linkDigest, nowUtc, cancellationToken);

        public Task<IntakeStoreWriteCode> RedeemLinkAsync(
            IntakeLinkScope linkScope,
            NewIntakeSession session,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            Inner.RedeemLinkAsync(linkScope, session, auditEvent, cancellationToken);

        public Task<IntakeClientScope?> FindClientScopeAsync(
            IntakeTokenDigest sessionDigest, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Inner.FindClientScopeAsync(sessionDigest, nowUtc, cancellationToken);

        public Task<StoredIntakeCase?> FindAsync(
            TrustedFirmId firm, Guid caseId, CancellationToken cancellationToken) =>
            Inner.FindAsync(firm, caseId, cancellationToken);

        public Task<StoredIntakeCase?> FindForClientAsync(
            IntakeClientScope scope, CancellationToken cancellationToken) =>
            Inner.FindForClientAsync(scope, cancellationToken);

        public Task<IntakeStoreWriteCode> ReissueLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            IntakeLinkRecord replacement,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            Inner.ReissueLinkAsync(
                firm, caseId, expectedConcurrencyVersion, replacement, auditEvents, cancellationToken);

        public Task<IntakeStoreWriteCode> RevokeLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            DateTimeOffset revokedAtUtc,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            Inner.RevokeLinkAsync(
                firm, caseId, expectedConcurrencyVersion, revokedAtUtc, auditEvent, cancellationToken);

        public Task<IntakeStoreWriteCode> AppendAnswersAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IReadOnlyList<IntakeAnswerVersionRecord> answers,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            Inner.AppendAnswersAsync(
                scope, expectedConcurrencyVersion, renewedIdleExpiresAtUtc, answers, auditEvents, cancellationToken);

        public Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IntakeUploadAttemptRecord attempt,
            IntakeDocumentRecord? document,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            Inner.AppendUploadAttemptAsync(
                scope, expectedConcurrencyVersion, renewedIdleExpiresAtUtc, attempt, document, auditEvent, cancellationToken);

        public Task<IntakeStoreWriteCode> AppendSubmissionAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IntakeSubmissionRecord submission,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            Inner.AppendSubmissionAsync(
                scope, expectedConcurrencyVersion, renewedIdleExpiresAtUtc, submission, auditEvents, cancellationToken);

        public Task<IntakeStoreWriteCode> AssociateReviewAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            IntakeReviewAssociationRecord association,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            Inner.AssociateReviewAsync(
                firm, caseId, expectedConcurrencyVersion, association, auditEvent, cancellationToken);
    }
}
