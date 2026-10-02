using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Support;

namespace TaxAI.Portal.Tests;

/// <summary>
/// Proves <c>GET /</c> remains the fixed shell document byte-for-byte, then
/// asserts the complete route, method, authorization, and antiforgery inventory
/// with exactly one constrained opaque review selector.
/// <para>
/// The expected document is spelled out here, independently of
/// <c>PortalContent</c>. Comparing the response to the constant that produced it
/// would assert only that a variable equals itself.
/// </para>
/// </summary>
public sealed class ShellPageTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>
    /// The shell document, written out rather than imported. Explicit <c>\n</c>
    /// escapes, matching how the production constant is built — the repository
    /// normalizes line endings on checkout, so an embedded newline here would
    /// make this assertion pass or fail depending on the machine.
    /// </summary>
    private const string ExpectedShellHtml =
        "<!DOCTYPE html>\n" +
        "<html lang=\"en\">\n" +
        "<head>\n" +
        "<meta charset=\"utf-8\" />\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n" +
        "<title>TaxAI</title>\n" +
        "</head>\n" +
        "<body>\n" +
        "<h1>TaxAI</h1>\n" +
        "<p>Authenticated accountants can create client intakes, monitor progress, and review stored packages.</p>\n" +
        "<p><a href=\"/sign-in\">Sign in</a></p>\n" +
        "</body>\n" +
        "</html>\n";

    private readonly WebApplicationFactory<Program> _factory;

    /// <remarks>
    /// This suite starts a <b>Production</b> host, so per decision B-6 it must
    /// register an approved durable Data Protection provider: outside
    /// Development the host refuses to start on the framework's machine-local
    /// key-ring fallback. The registration is additive — every assertion below,
    /// including the byte-for-byte shell document, is unchanged.
    /// </remarks>
    public ShellPageTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.AddApprovedDurableDataProtection());
        });

    public void Dispose() => _factory.Dispose();

    // ------------------------------------------------------------------ AC-25

    [Fact]
    public async Task TheRootReturnsTheFixedShellDocument() // AC-25
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal(ExpectedShellHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheShellCarriesNoDynamicValue() // AC-25
    {
        using var client = _factory.CreateClient();

        // Two requests, byte-identical. A timestamp, request id, environment
        // name, or nonce would show up as a difference.
        var first = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        var second = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Equal(first, second);

        foreach (var dynamicValue in new[]
                 {
                     Environment.MachineName,
                     Environment.UserName,
                     "Production",
                     "Development",
                     DateTime.UtcNow.Year.ToString(),
                 })
        {
            Assert.DoesNotContain(dynamicValue, first, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TheShellReferencesNoExternalAsset()
    {
        using var client = _factory.CreateClient();

        var body = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // Nothing to fetch means no client-side stack and no third-party origin.
        foreach (var external in new[] { "http://", "https://", "//cdn", "<script", "<link", "<img", "url(" })
        {
            Assert.DoesNotContain(external, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TheRootIsGetOnly()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync("/", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // -------------------------------------- spec 011 AC-127 – AC-130: the routes

    /// <summary>
    /// The host's complete route inventory, with the exact method each answers.
    /// <para>
    /// Replaces spec 010's "exactly two GET endpoints", superseded by FR-48. An
    /// exact route-and-method inventory is strictly stronger than a count plus a
    /// blanket "GET only": the old pair could not have named which routes exist,
    /// and could not have distinguished a new GET route from a renamed one.
    /// </para>
    /// <para>
    /// The OpenID Connect callback paths are deliberately absent: they are
    /// handled by the authentication middleware and are mapped as endpoints
    /// never, which is why no route below carries one.
    /// </para>
    /// </summary>
    public static TheoryData<string, string> RouteInventory() => new()
    {
        { "/", "GET" },
        { "/health", "GET" },
        { "/sign-in", "GET" },
        { "/firms", "GET" },
        { "/firms/select", "POST" },
        { "/intake-cases/new", "GET" },

        // Spec 017 T03: the firm-scoped case list. It shares its path with the
        // existing create POST, so a route is identified by path and method.
        { "/intake-cases", "GET" },
        { "/intake-cases", "POST" },
        { "/intake-cases/{caseId:guid}", "GET" },
        { "/intake-cases/{caseId:guid}/link/reissue", "POST" },
        { "/intake-cases/{caseId:guid}/link/revoke", "POST" },
        { "/intake-cases/{caseId:guid}/review-association", "POST" },
        { "/intake-cases/{caseId:guid}/extractions", "GET" },
        { "/intake-cases/{caseId:guid}/extractions/{documentId:guid}", "GET" },
        { "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract", "POST" },
        { "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification", "POST" },
        { "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections", "POST" },
        { "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept", "POST" },
        { "/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}", "POST" },
        { "/intake-cases/{caseId:guid}/extractions/finalize", "POST" },
        { "/client-intake/link/{credential}", "GET" },
        { "/client-intake/redeem", "POST" },
        { "/client-intake", "GET" },
        { "/client-intake/answers", "POST" },
        { "/client-intake/documents", "POST" },
        { "/client-intake/submissions", "POST" },
        { "/reviews/{recordId:guid}", "GET" },
        { "/sign-out", "POST" },
    };

    /// <summary>
    /// The framework's own endpoints, contributed by
    /// <c>MapRazorComponents&lt;App&gt;()</c> in spec 014 T01. Exactly one, and it
    /// is the form-post redirect helper the component result model uses — not a
    /// circuit, not a page, and not a route this project declares.
    /// <para>
    /// The four <c>/_blazor</c> SignalR endpoints are deliberately <b>absent</b>.
    /// They appear only when an interactive render mode is configured, and this
    /// host configures none: nothing in the portal is interactive, so a circuit
    /// endpoint would expand a taxpayer-data host's surface while providing no
    /// current functionality. Spec 014 T03 introduces the first interactive island
    /// and the registration that goes with it.
    /// </para>
    /// <para>
    /// Enumerated exactly rather than filtered out by prefix. A pattern match on
    /// <c>/_</c> would let any future framework endpoint in silently, and the point
    /// of this inventory is that every reachable path on a taxpayer-data host is
    /// named in one place.
    /// </para>
    /// <para>
    /// There is deliberately <b>no catch-all</b> here. <c>MapStaticAssets()</c>
    /// would have added <c>{**path:file}</c> over the whole URL space, which turns
    /// every POST-only route's <c>405</c> into a <c>404</c>; the host uses
    /// static-file middleware instead, which adds no endpoint at all.
    /// </para>
    /// </summary>
    private static readonly string[] FrameworkComponentEndpoints =
    {
        "/_blazor",
        "/_blazor/disconnect/",
        "/_blazor/initializers/",
        "/_blazor/negotiate",
        "/_framework/opaque-redirect",
    };

    [Fact] // spec 011 AC-127, spec 014 AC-89
    public void TheHostMapsExactlyTheApprovedRoutes()
    {
        var endpoints = MappedRouteEndpoints();
        foreach (var row in RouteInventory())
        {
            var pattern = (string)row[0]!;
            var method = (string)row[1]!;
            Assert.Equal(
                1,
                endpoints.Count(endpoint =>
                    string.Equals(
                        endpoint.RoutePattern.RawText,
                        pattern,
                        StringComparison.Ordinal) &&
                    AnswersExactly(endpoint, method)));

            // And nothing else is mapped at that path: every endpoint sharing
            // the pattern is itself an inventory row.
            Assert.Equal(
                RouteInventory().Count(candidate =>
                    string.Equals(
                        (string)candidate[0]!,
                        pattern,
                        StringComparison.Ordinal)),
                endpoints.Count(endpoint =>
                    string.Equals(
                        endpoint.RoutePattern.RawText,
                        pattern,
                        StringComparison.Ordinal)));
        }

        foreach (var pattern in FrameworkComponentEndpoints)
        {
            Assert.Equal(
                1,
                endpoints.Count(endpoint =>
                    string.Equals(
                        endpoint.RoutePattern.RawText,
                        pattern,
                        StringComparison.Ordinal)));
        }
    }

    [Fact] // spec 014 AC-89
    public void TheHostMapsNoCatchAllAndNoRoutableComponentPage()
    {
        // A catch-all is how a static-asset or component-router mapping quietly
        // takes over the URL space and changes refusal behaviour everywhere else.
        // No component in this project declares @page, so the component router
        // contributes no page route either.
        foreach (var endpoint in MappedRouteEndpoints())
        {
            Assert.DoesNotContain(
                "**",
                endpoint.RoutePattern.RawText!,
                StringComparison.Ordinal);
        }

        // And every static asset is served by middleware, so no asset is an
        // endpoint.
        Assert.DoesNotContain(
            MappedRouteEndpoints(),
            endpoint => endpoint.RoutePattern.RawText!.EndsWith(".css", StringComparison.Ordinal));
    }

    [Theory] // spec 011 AC-127
    [MemberData(nameof(RouteInventory))]
    public void EachApprovedRouteAnswersExactlyItsOneMethod(string pattern, string method)
    {
        // Spec 017 T03: one path may carry two approved methods, each on its
        // own endpoint. Exactly one endpoint answers this row's method, and it
        // answers no other.
        var endpoint = Assert.Single(
            MappedRouteEndpoints(),
            candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    pattern,
                    StringComparison.Ordinal) &&
                (candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                    .Contains(method, StringComparer.Ordinal) ?? false));

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;

        Assert.NotNull(methods);
        Assert.Equal(new[] { method }, methods!.ToArray());
    }

    [Fact] // spec 011 AC-128
    public void OnlyTheReviewRouteTakesTheOpaqueRecordSelector()
    {
        var parameterized = ApplicationRouteEndpoints()
            .Where(endpoint => endpoint.RoutePattern.Parameters.Count > 0)
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "/client-intake/link/{credential}",
                "/intake-cases/{caseId:guid}",
                "/intake-cases/{caseId:guid}/extractions",
                "/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}",
                "/intake-cases/{caseId:guid}/extractions/finalize",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract",
                "/intake-cases/{caseId:guid}/link/reissue",
                "/intake-cases/{caseId:guid}/link/revoke",
                "/intake-cases/{caseId:guid}/review-association",
                "/reviews/{recordId:guid}",
            },
            parameterized.Where(pattern =>
                RouteInventory().Any(row =>
                    string.Equals(
                        (string)row[0]!,
                        pattern,
                        StringComparison.Ordinal))));

        var constrained = ApplicationRouteEndpoints()
            .Where(endpoint => endpoint.RoutePattern.RawText!.Contains(
                ":guid",
                StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.RoutePattern.Parameters)
            .ToArray();
        Assert.All(
            constrained,
            parameter => Assert.Equal(
                new[] { "guid" },
                parameter.ParameterPolicies.Select(policy => policy.Content)));
    }

    [Fact] // spec 014 AC-89
    public void TheFrameworkComponentEndpointsAreParameterlessAndCarryNoTaxpayerPath()
    {
        var framework = MappedRouteEndpoints()
            .Where(endpoint => FrameworkComponentEndpoints.Contains(
                endpoint.RoutePattern.RawText!,
                StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(FrameworkComponentEndpoints.Length, framework.Length);

        // No route parameter, so none can carry a firm, case, record, or taxpayer
        // selector.
        foreach (var endpoint in framework)
        {
            Assert.Empty(endpoint.RoutePattern.Parameters);
        }
    }

    [Fact] // spec 014 AC-89, AC-90
    public void NoInteractiveServerCircuitEndpointIsMapped()
    {
        // The narrowing, asserted rather than assumed. Interactive Server is not
        // registered in T01, so the framework materializes no SignalR circuit
        // endpoint on the host that serves taxpayer data. T03 introduces both the
        // first interactive island and the registration it needs.
        var circuitEndpoints = MappedRouteEndpoints()
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .Where(pattern => pattern.StartsWith("/_blazor", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "/_blazor/negotiate",
                "/_blazor",
                "/_blazor/disconnect/",
                "/_blazor/initializers/",
            },
            circuitEndpoints);
    }

    [Theory] // spec 011 AC-128, AC-129
    [InlineData("firm")]
    [InlineData("participant")]
    [InlineData("session")]
    [InlineData("package")]
    [InlineData("taxpayer")]
    public void NoRouteParameterNamesATenantOrTaxpayerIdentifier(string identifierWord)
    {
        // The one record selector is a TaxAI-generated opaque lookup key. It is
        // never tenant identity; firm scope still comes only from the live
        // server-side session.
        foreach (var endpoint in MappedRouteEndpoints())
        {
            Assert.DoesNotContain(
                endpoint.RoutePattern.Parameters,
                parameter => parameter.Name.Contains(
                    identifierWord,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void CaseSelectorsAppearOnlyOnTheApprovedAccountantRoutes()
    {
        Assert.Equal(
            new[]
            {
                "/intake-cases/{caseId:guid}",
                "/intake-cases/{caseId:guid}/extractions",
                "/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}",
                "/intake-cases/{caseId:guid}/extractions/finalize",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract",
                "/intake-cases/{caseId:guid}/link/reissue",
                "/intake-cases/{caseId:guid}/link/revoke",
                "/intake-cases/{caseId:guid}/review-association",
            },
            ApplicationRouteEndpoints()
                .Where(endpoint => endpoint.RoutePattern.Parameters.Any(
                    parameter => parameter.Name == "caseId"))
                .Select(endpoint => endpoint.RoutePattern.RawText!)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact] // spec 011 AC-130
    public void EveryEndpointIsEitherEnumeratedAnonymousOrAuthorizationRequired()
    {
        // The third state — neither marked anonymous nor requiring
        // authorization — is the one that looks fine and is not. An endpoint
        // that is public because nobody protected it reads exactly like one that
        // is public by decision.
        var anonymous = new[]
        {
            "/",
            "/client-intake",
            "/client-intake/answers",
            "/client-intake/documents",
            "/client-intake/link/{credential}",
            "/client-intake/redeem",
            "/client-intake/submissions",
            "/health",
            "/sign-in",
        };

        foreach (var endpoint in ApplicationRouteEndpoints())
        {
            var pattern = endpoint.RoutePattern.RawText!;
            var allowsAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var requiresAuthorization = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;

            if (anonymous.Contains(pattern, StringComparer.Ordinal))
            {
                Assert.True(allowsAnonymous, $"{pattern} must be explicitly anonymous.");
                Assert.False(requiresAuthorization, $"{pattern} must not also require authorization.");
                continue;
            }

            Assert.True(requiresAuthorization, $"{pattern} must require authorization.");
            Assert.False(allowsAnonymous, $"{pattern} must not also allow anonymous.");
        }
    }

    [Theory] // spec 011 AC-130
    [InlineData("/firms/select")]
    [InlineData("/intake-cases")]
    [InlineData("/intake-cases/{caseId:guid}/link/reissue")]
    [InlineData("/intake-cases/{caseId:guid}/link/revoke")]
    [InlineData("/intake-cases/{caseId:guid}/review-association")]
    [InlineData("/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract")]
    [InlineData("/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification")]
    [InlineData("/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections")]
    [InlineData("/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept")]
    [InlineData("/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}")]
    [InlineData("/intake-cases/{caseId:guid}/extractions/finalize")]
    [InlineData("/client-intake/redeem")]
    [InlineData("/client-intake/answers")]
    [InlineData("/client-intake/documents")]
    [InlineData("/client-intake/submissions")]
    [InlineData("/sign-out")]
    public void EveryStateChangingRouteRequiresAntiforgery(string pattern)
    {
        // Spec 017 T03 (human-authorized 2026-10-01): a path may also carry a
        // GET, so the state-changing endpoint is the one that does not answer
        // GET. Exactly one such endpoint exists per listed path.
        var endpoint = Assert.Single(
            MappedRouteEndpoints(),
            candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    pattern,
                    StringComparison.Ordinal) &&
                !AnswersExactly(candidate, "GET"));

        var metadata = endpoint.Metadata.FirstOrDefault(item =>
            item.GetType().GetInterfaces().Any(contract =>
                contract.FullName ==
                "Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata"));

        Assert.NotNull(metadata);
        Assert.True((bool)metadata!.GetType().GetProperty("RequiresValidation")!.GetValue(metadata)!);
    }

    // ---------------------------------------------------------------- helpers

    private static bool AnswersExactly(RouteEndpoint endpoint, string method) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods is { } methods &&
        methods.SequenceEqual(new[] { method }, StringComparer.Ordinal);

    /// <summary>
    /// The host's complete endpoint inventory. The client is created first
    /// because endpoints materialize when the application starts.
    /// </summary>
    private RouteEndpoint[] MappedRouteEndpoints()
    {
        using var client = _factory.CreateClient();

        return _factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
    }

    /// <summary>
    /// This application's own routes: the complete inventory minus the exactly
    /// enumerated framework component endpoints. Subtracting a named set rather
    /// than filtering a prefix keeps the framework surface pinned too.
    /// </summary>
    private RouteEndpoint[] ApplicationRouteEndpoints() =>
        MappedRouteEndpoints()
            .Where(endpoint => !FrameworkComponentEndpoints.Contains(
                endpoint.RoutePattern.RawText!,
                StringComparer.Ordinal))
            .ToArray();
}
