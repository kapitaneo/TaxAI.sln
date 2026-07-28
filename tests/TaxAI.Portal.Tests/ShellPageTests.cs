using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace TaxAI.Portal.Tests;

/// <summary>
/// T03 — proves <c>GET /</c> returns the fixed shell document byte-for-byte, and
/// that the host's complete endpoint inventory is exactly two GET routes with no
/// route parameter between them (AC-26).
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

    public ShellPageTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

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

    // ------------------------------------------------------------------ AC-26

    [Fact]
    public void TheHostMapsExactlyTwoGetEndpoints() // AC-26
    {
        var endpoints = MappedRouteEndpoints();

        Assert.Equal(2, endpoints.Length);

        Assert.Equal(
            new[] { "/", "/health" },
            endpoints
                .Select(endpoint => endpoint.RoutePattern.RawText!)
                .OrderBy(pattern => pattern, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void EveryMappedEndpointIsGetOnly() // AC-26
    {
        foreach (var endpoint in MappedRouteEndpoints())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;

            Assert.NotNull(methods);
            Assert.Equal(new[] { "GET" }, methods!.ToArray());
        }
    }

    [Fact]
    public void NoMappedEndpointTakesARouteParameter() // AC-26
    {
        // A route parameter is an input surface, and v1 has none.
        foreach (var endpoint in MappedRouteEndpoints())
        {
            Assert.Empty(endpoint.RoutePattern.Parameters);
        }
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
