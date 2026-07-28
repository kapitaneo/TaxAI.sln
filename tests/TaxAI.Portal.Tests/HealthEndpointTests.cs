using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TaxAI.Portal.Tests;

/// <summary>
/// T02 — proves <c>GET /health</c> answers with a fixed literal and nothing
/// else, and that an unmatched route in Production discloses nothing.
/// <para>
/// The expected body is written here, independently of
/// <c>PortalContent</c>. Comparing the response to the same constant that
/// produced it would assert only that a variable equals itself.
/// </para>
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    /// <summary>The health body, spelled out rather than imported.</summary>
    private const string ExpectedHealthBody = "Healthy";

    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

    public void Dispose() => _factory.Dispose();

    // ------------------------------------------------------------------ AC-23

    [Fact]
    public async Task HealthReturnsTheFixedLiteralWithTheHealthyStatus() // AC-23
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedHealthBody, await response.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    public async Task HealthIsGetOnly()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync("/health", content: null);

        // No input surface: the endpoint answers GET and nothing else.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task HealthDisclosesNothingAboutTheSystem() // AC-23, supports AC-28
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        // A probe endpoint that describes the system is a disclosure surface.
        foreach (var disclosure in new[]
                 {
                     "Production",
                     "Development",
                     "Version",
                     "net8.0",
                     Environment.MachineName,
                     Environment.UserName,
                     ":\\",
                     "/src/",
                 })
        {
            Assert.DoesNotContain(disclosure, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(ExpectedHealthBody.Length, body.Length);
    }

    // ------------------------------------------------------------------ AC-29

    [Theory] // AC-29
    [InlineData("/does-not-exist")]
    [InlineData("/health/extra")]
    [InlineData("/healthz")]
    public async Task AnUnmatchedRouteInProductionReturns404WithNoDiagnosticDetail(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        foreach (var leak in new[]
                 {
                     "at TaxAI.Portal",
                     "Exception",
                     "StackTrace",
                     ":\\",
                     "src/TaxAI.Portal",
                     ".cs:line",
                 })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RouteMatchingIsCaseInsensitive()
    {
        // ASP.NET Core routing is case-insensitive by default. Recording it as a
        // known, intended property rather than leaving it for someone to
        // discover as a surprise: /Health is the health endpoint, not a miss.
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/Health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedHealthBody, await response.Content.ReadAsStringAsync());
    }

    // Deliberately no assertion here about GET / being unmapped. T03 maps it,
    // and T03 does not own this file — an assertion about a route this task does
    // not own would make a later task's correct change look like a regression it
    // could not fix. The full endpoint inventory is AC-26, owned by T03.
}
