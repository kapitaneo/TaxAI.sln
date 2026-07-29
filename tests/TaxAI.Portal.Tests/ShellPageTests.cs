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
/// asserts spec 011's complete route, method, authorization, and antiforgery
/// inventory with no route parameter anywhere.
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
        "<p>This application serves no taxpayer data.</p>\n" +
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
        { "/sign-out", "POST" },
    };

    [Fact] // spec 011 AC-127
    public void TheHostMapsExactlyTheApprovedRoutes()
    {
        Assert.Equal(
            RouteInventory()
                .Select(row => (string)row[0]!)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .ToArray(),
            MappedRouteEndpoints()
                .Select(endpoint => endpoint.RoutePattern.RawText!)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .ToArray());
    }

    [Theory] // spec 011 AC-127
    [MemberData(nameof(RouteInventory))]
    public void EachApprovedRouteAnswersExactlyItsOneMethod(string pattern, string method)
    {
        var endpoint = Assert.Single(
            MappedRouteEndpoints().Where(candidate =>
                string.Equals(candidate.RoutePattern.RawText, pattern, StringComparison.Ordinal)));

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;

        Assert.NotNull(methods);
        Assert.Equal(new[] { method }, methods!.ToArray());
    }

    [Fact] // spec 011 AC-128
    public void NoMappedEndpointTakesARouteParameter()
    {
        // A route parameter is an input surface. The firm-scoped surfaces this
        // host will grow take their one selector in a form body, never in a
        // path, so this stays absolute rather than becoming an allow-list.
        foreach (var endpoint in MappedRouteEndpoints())
        {
            Assert.Empty(endpoint.RoutePattern.Parameters);
        }
    }

    [Theory] // spec 011 AC-128, AC-129
    [InlineData("firm")]
    [InlineData("case")]
    [InlineData("participant")]
    [InlineData("document")]
    [InlineData("session")]
    [InlineData("record")]
    [InlineData("package")]
    [InlineData("taxpayer")]
    public void NoRouteParameterNamesAnIdentifierOfAnyKind(string identifierWord)
    {
        // /firms is the approved bootstrap resource name. What remains absolute
        // is that no route parameter can carry any identifier, including on
        // that route or its selection action.
        foreach (var endpoint in MappedRouteEndpoints())
        {
            Assert.DoesNotContain(
                endpoint.RoutePattern.Parameters,
                parameter => parameter.Name.Contains(
                    identifierWord,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact] // spec 011 AC-130
    public void EveryEndpointIsEitherEnumeratedAnonymousOrAuthorizationRequired()
    {
        // The third state — neither marked anonymous nor requiring
        // authorization — is the one that looks fine and is not. An endpoint
        // that is public because nobody protected it reads exactly like one that
        // is public by decision.
        var anonymous = new[] { "/", "/health", "/sign-in" };

        foreach (var endpoint in MappedRouteEndpoints())
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
    [InlineData("/sign-out")]
    public void EveryStateChangingRouteRequiresAntiforgery(string pattern)
    {
        var endpoint = Assert.Single(
            MappedRouteEndpoints().Where(candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    pattern,
                    StringComparison.Ordinal)));

        var metadata = endpoint.Metadata.FirstOrDefault(item =>
            item.GetType().GetInterfaces().Any(contract =>
                contract.FullName ==
                "Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata"));

        Assert.NotNull(metadata);
        Assert.True((bool)metadata!.GetType().GetProperty("RequiresValidation")!.GetValue(metadata)!);
    }

    // ---------------------------------------------------------------- helpers

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
}
