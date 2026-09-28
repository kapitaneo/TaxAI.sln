using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxAI.Portal.Tests.Support;
using System.Reflection;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T02 — trusted regional ingress in the Portal (FR-02: AC-021,
/// AC-023, AC-024).
/// <para>
/// Behind the Canada Central WAF v2 gateway the Portal honours exactly one
/// appended forwarded hop - the right-most client address and scheme - and
/// nothing a client supplies to its left. That is proved on the fixed policy
/// itself, through the framework middleware it configures, and through the real
/// Portal pipeline: the intake rate-limit partition follows the trusted address
/// and cannot be escaped by a spoofed one. Development, and every other
/// environment, honours no forwarded header at all, so no historical browser,
/// questionnaire or rate-limit behaviour changes.
/// </para>
/// </summary>
public sealed class TrustedIngressTests
{
    private const string PublicLinkPath = "/client-intake/link/not-a-canonical-credential";

    // The approved public-link policy admits 20 requests per window; the 21st
    // in one partition is the fixed 429.
    private const int PublicPermits = 20;

    [Fact]
    public void TheFixedPolicyHonoursOnlyTheRightMostAddressAndScheme()
    {
        var options = Ingress.CreateOptions();

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.False(options.RequireHeaderSymmetry);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.AllowedHosts);
        Assert.False(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
        Assert.False(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedPrefix));
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    [InlineData("Staging", false)]
    [InlineData("production", false)]
    [InlineData("Production ", false)]
    [InlineData("", false)]
    public void TrustedIngressAppliesToTheProductionEnvironmentOnly(string environment, bool applies) =>
        Assert.Equal(applies, Ingress.AppliesTo(environment));

    [Fact]
    public async Task ASpoofedLeftMostEntryIsIgnoredAndTheTrustedHopIsHonoured()
    {
        var context = await ApplyAsync(
            new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "198.51.100.66, 203.0.113.10:50123",
                ["X-Forwarded-Proto"] = "http, https",
                ["X-Forwarded-Host"] = "attacker.example",
                ["X-Forwarded-Prefix"] = "/elsewhere",
            });

        Assert.Equal(IPAddress.Parse("203.0.113.10"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
        Assert.True(context.Request.IsHttps);
        Assert.Equal("portal.example.test", context.Request.Host.Host);
        Assert.Equal(PathString.Empty, context.Request.PathBase);
    }

    [Fact] // AC-154 at runtime: a plaintext-forwarded scheme is never upgraded.
    public async Task APlaintextForwardedSchemeIsNotTreatedAsHttps()
    {
        var context = await ApplyAsync(
            new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "203.0.113.10",
                ["X-Forwarded-Proto"] = "https, http",
            });

        Assert.Equal("http", context.Request.Scheme);
        Assert.False(context.Request.IsHttps);
    }

    [Fact]
    public void TheProductionHostRefusesToRunThePlatformForwardingSwitchAsWell()
    {
        foreach (var value in new[] { "true", "True", "1", " yes " })
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [Ingress.PlatformForwardedHeadersSetting] = value,
                })
                .Build();

            var failure = Assert.Throws<InvalidOperationException>(
                () => Ingress.RequireSingleForwardingHandler(configuration));
            Assert.Equal(Ingress.DoubleForwardingRefusalMessage, failure.Message);
            Assert.DoesNotContain(value.Trim(), failure.Message, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var value in new[] { null, "false", "False", " false " })
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [Ingress.PlatformForwardedHeadersSetting] = value,
                })
                .Build();

            Ingress.RequireSingleForwardingHandler(configuration);
        }
    }

    [Fact] // The real Production pipeline: the partition follows the trusted hop.
    public async Task InProductionEachTrustedClientAddressHasItsOwnRateLimitPartition()
    {
        using var fixture = CreateHost("Production");
        using var client = fixture.CreateClient(followRedirects: false);

        for (var attempt = 0; attempt <= PublicPermits; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PublicLinkPath);
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{attempt + 1}");
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await client.SendAsync(request);

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact] // The real Production pipeline: a spoofed left-most entry buys nothing.
    public async Task InProductionASpoofedLeftMostAddressCannotEscapeTheRateLimit()
    {
        using var fixture = CreateHost("Production");
        using var client = fixture.CreateClient(followRedirects: false);

        HttpStatusCode last = default;
        for (var attempt = 0; attempt <= PublicPermits; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PublicLinkPath);
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{attempt + 1}, 203.0.113.200");
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await client.SendAsync(request);
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact] // Development honours no forwarded header: historical behaviour is unchanged.
    public async Task OutsideProductionForwardedHeadersAreIgnored()
    {
        using var fixture = CreateHost(null);
        using var client = fixture.CreateClient(followRedirects: false);

        HttpStatusCode last = default;
        for (var attempt = 0; attempt <= PublicPermits; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PublicLinkPath);
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{attempt + 1}");
            using var response = await client.SendAsync(request);
            last = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact] // AC-024: the questionnaire island and its transport are untouched.
    public async Task TheInteractiveQuestionnaireSurfaceIsUnchangedBehindTrustedIngress()
    {
        using var fixture = CreateHost("Production");
        using var client = fixture.CreateClient(followRedirects: false);

        // The questionnaire island's circuit negotiation still answers through
        // the trusted hop in Production.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var program = File.ReadAllText(Path.Combine(InfrastructurePlanHarness.RepositoryRoot, "src", "TaxAI.Portal", "Program.cs"));
        Assert.Contains("options => options.DisableWebSocketCompression = true", program, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"AddInteractiveServerRenderMode\("));

        var ingress = File.ReadAllText(Path.Combine(
            InfrastructurePlanHarness.RepositoryRoot, "src", "TaxAI.Portal", "Production", "TrustedIngressConfiguration.cs"));
        foreach (var forbidden in new[] { "Blazor", "RenderMode", "WebSocket", "Antiforgery", "Cookie", "Cors", "Origin =" })
        {
            Assert.DoesNotContain(forbidden, ingress, StringComparison.Ordinal);
        }

        var appService = InfrastructurePlanHarness.Read("modules/app-service.bicep");
        Assert.Contains("webSocketsEnabled: true", appService, StringComparison.Ordinal);
        Assert.Contains("clientAffinityEnabled: true", appService, StringComparison.Ordinal);
        Assert.Contains(
            "cookieBasedAffinity: 'Enabled'",
            InfrastructurePlanHarness.Read("modules/application-gateway.bicep"),
            StringComparison.Ordinal);
    }

    [Fact] // Program applies trusted ingress once, after the intake headers, before routing.
    public void ProgramAppliesTrustedIngressBeforeRoutingAndRateLimiting()
    {
        var lines = File.ReadAllLines(Path.Combine(InfrastructurePlanHarness.RepositoryRoot, "src", "TaxAI.Portal", "Program.cs"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        var ingress = Array.IndexOf(lines, "app.UseTrustedIngress();");
        Assert.True(ingress > 0);
        Assert.Single(lines, line => line.Contains("UseTrustedIngress", StringComparison.Ordinal));
        Assert.True(Array.IndexOf(lines, "app.Use(IntakeHttpSecurity.ApplyResponseHeadersAsync);") < ingress);
        Assert.True(Array.IndexOf(lines, "app.UseRouting();") > ingress);
        Assert.True(Array.IndexOf(lines, "app.UseRateLimiter();") > ingress);
        Assert.DoesNotContain(lines, line => line.Contains("UseForwardedHeaders", StringComparison.Ordinal));
    }

    private static async Task<HttpContext> ApplyAsync(IDictionary<string, string> headers)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.40.0.20");
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("portal.example.test");
        foreach (var header in headers)
        {
            context.Request.Headers[header.Key] = header.Value;
        }

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(Ingress.CreateOptions()));
        await middleware.Invoke(context);
        return context;
    }

    private static IngressHost CreateHost(string? environment) =>
        environment is null
            ? new IngressHost(null, IntakePortalHarness.Create())
            : new IngressHost(
                new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment(environment);
                    builder.ConfigureTestServices(services => services.AddApprovedDurableDataProtection());
                }),
                null);

    /// <summary>
    /// Production uses the unmodified composition root with the approved test
    /// key ring, exactly as the composition-root suite does; Development uses
    /// the intake harness. Public-link rate limiting runs before any credential
    /// parsing or storage in both.
    /// </summary>
    private sealed class IngressHost : IDisposable
    {
        private readonly WebApplicationFactory<Program>? _production;
        private readonly IntakePortalHarness? _development;

        internal IngressHost(WebApplicationFactory<Program>? production, IntakePortalHarness? development)
        {
            _production = production;
            _development = development;
        }

        internal HttpClient CreateClient(bool followRedirects) =>
            _production is not null
                ? _production.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost"),
                    AllowAutoRedirect = followRedirects,
                })
                : _development!.CreateClient(followRedirects);

        public void Dispose()
        {
            _production?.Dispose();
            _development?.Dispose();
        }
    }

    /// <summary>
    /// The internal Portal type, reached by reflection as the other production
    /// suites reach theirs; the Portal exposes no internals to tests.
    /// </summary>
    private static class Ingress
    {
        private static readonly Type Type = typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.TrustedIngressConfiguration", throwOnError: true)!;

        internal static string PlatformForwardedHeadersSetting => Constant("PlatformForwardedHeadersSetting");

        internal static string DoubleForwardingRefusalMessage => Constant("DoubleForwardingRefusalMessage");

        internal static ForwardedHeadersOptions CreateOptions() =>
            (ForwardedHeadersOptions)Invoke("CreateOptions")!;

        internal static bool AppliesTo(string environment) =>
            (bool)Invoke("AppliesTo", environment)!;

        internal static void RequireSingleForwardingHandler(IConfiguration configuration) =>
            Invoke("RequireSingleForwardingHandler", configuration);

        private static string Constant(string name) =>
            (string)Type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

        private static object? Invoke(string name, params object[] arguments)
        {
            try
            {
                return Type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);
            }
            catch (TargetInvocationException failure) when (failure.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
                throw;
            }
        }
    }
}
