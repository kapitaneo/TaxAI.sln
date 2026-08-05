using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TaxAI.Portal.Tests.Identity;

namespace TaxAI.Portal.Tests.Upgrade;

/// <summary>
/// Spec 013 T01: records the framework-neutral HTTP and authorization contract
/// that must survive .NET 10's cookie/API-endpoint behavior change.
/// </summary>
public sealed class RouteAuthenticationBaselineTests
{
    private const string BaselineCommit =
        "13097dada6b1aae070fc0006d48a6862ce9904f8";

    private static readonly Regex GuidValue = new(
        @"\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex AntiforgeryValue = new(
        @"(?<=name=""__RequestVerificationToken"" value="")[^""]+",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string BaselinePath = Path.Combine(
        RepositoryRoot,
        "specs",
        "013-dotnet-10-platform-upgrade",
        "baselines",
        "route-auth-contract.json");

    [Fact]
    public async Task RouteAndAuthenticationMatrixMatchesTheApprovedBaseline()
    {
        var actual = await CaptureAsync();

        var baseline = JsonSerializer.Deserialize<RouteAuthenticationContract>(
            File.ReadAllText(BaselinePath),
            JsonOptions) ?? throw new InvalidOperationException(
                $"Could not deserialize {BaselinePath}.");

        Assert.Equal(BaselineCommit, baseline.BaselineCommit);

        var legacyPaths = baseline.Endpoints
            .Select(endpoint => endpoint.Path)
            .ToHashSet(StringComparer.Ordinal);
        var actualLegacyEndpoints = actual.Endpoints
            .Where(endpoint => legacyPaths.Contains(endpoint.Path))
            .ToArray();

        Assert.Equal(
            JsonSerializer.Serialize(baseline.Endpoints, JsonOptions),
            JsonSerializer.Serialize(actualLegacyEndpoints, JsonOptions));

        // Post-baseline endpoints are verified by the specification that added
        // each one. The historical baseline remains an exact comparison above,
        // while this open collection permits a future approved route without
        // rewriting spec 013's history.
        var additive = actual.Endpoints
            .Where(endpoint => !legacyPaths.Contains(endpoint.Path))
            .OrderBy(endpoint => endpoint.Path, StringComparer.Ordinal)
            .ToArray();

        var frameworkPaths = new[]
        {
            "/_blazor",
            "/_blazor/disconnect/",
            "/_blazor/initializers/",
            "/_blazor/negotiate",
            "/_framework/opaque-redirect",
        };
        foreach (var frameworkPath in frameworkPaths)
        {
            var endpoint = Assert.Single(
                additive,
                candidate => string.Equals(
                    candidate.Path,
                    frameworkPath,
                    StringComparison.Ordinal));
            Assert.False(endpoint.Antiforgery);
            Assert.DoesNotContain("{", endpoint.Path, StringComparison.Ordinal);
        }

        var preservedPostBaselineApplications = new[]
        {
            new EndpointContract("/client-intake", ["GET"], "anonymous", false),
            new EndpointContract("/client-intake/answers", ["POST"], "anonymous", true),
            new EndpointContract("/client-intake/documents", ["POST"], "anonymous", true),
            new EndpointContract("/client-intake/link/{credential}", ["GET"], "anonymous", false),
            new EndpointContract("/client-intake/redeem", ["POST"], "anonymous", true),
            new EndpointContract("/client-intake/submissions", ["POST"], "anonymous", true),
            new EndpointContract("/intake-cases", ["POST"], "required", true),
            new EndpointContract("/intake-cases/new", ["GET"], "required", false),
            new EndpointContract("/intake-cases/{caseId:guid}", ["GET"], "required", false),
            new EndpointContract("/intake-cases/{caseId:guid}/link/reissue", ["POST"], "required", true),
            new EndpointContract("/intake-cases/{caseId:guid}/link/revoke", ["POST"], "required", true),
            new EndpointContract("/intake-cases/{caseId:guid}/review-association", ["POST"], "required", true),
            new EndpointContract("/reviews/{recordId:guid}", ["GET"], "required", false),
        };

        foreach (var expectedEndpoint in preservedPostBaselineApplications)
        {
            Assert.Equal(
                JsonSerializer.Serialize(expectedEndpoint, JsonOptions),
                JsonSerializer.Serialize(
                    Assert.Single(
                        additive,
                        candidate => string.Equals(
                            candidate.Path,
                            expectedEndpoint.Path,
                            StringComparison.Ordinal)),
                    JsonOptions));
        }

        var t06Applications = new[]
        {
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions",
                ["GET"],
                "required",
                false),
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}",
                ["GET"],
                "required",
                false),
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract",
                ["POST"],
                "required",
                true),
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification",
                ["POST"],
                "required",
                true),
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections",
                ["POST"],
                "required",
                true),
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept",
                ["POST"],
                "required",
                true),
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}",
                ["POST"],
                "required",
                true),
            new EndpointContract(
                "/intake-cases/{caseId:guid}/extractions/finalize",
                ["POST"],
                "required",
                true),
        };

        foreach (var expectedEndpoint in t06Applications)
        {
            Assert.Equal(
                JsonSerializer.Serialize(expectedEndpoint, JsonOptions),
                JsonSerializer.Serialize(
                    Assert.Single(
                        additive,
                        candidate => string.Equals(
                            candidate.Path,
                            expectedEndpoint.Path,
                            StringComparison.Ordinal)),
                    JsonOptions));
        }

        Assert.Equal(
            baseline.Responses.Select(ResponseKey),
            actual.Responses.Select(ResponseKey));

        foreach (var expected in baseline.Responses)
        {
            var current = Assert.Single(
                actual.Responses,
                response => ResponseKey(response) == ResponseKey(expected));

            if (expected is { Method: "GET", Path: "/" })
            {
                Assert.Equal(expected.Status, current.Status);
                Assert.Equal(expected.Location, current.Location);
                Assert.Equal(expected.WwwAuthenticate, current.WwwAuthenticate);
                Assert.Equal(expected.ContentType, current.ContentType);
                Assert.NotEqual(expected.BodySha256, current.BodySha256);
                continue;
            }

            Assert.Equal(expected, current);
        }

        var currentRootBodies = actual.Responses
            .Where(response => response is { Method: "GET", Path: "/" })
            .Select(response => response.BodySha256)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Single(currentRootBodies);
    }

    private static string ResponseKey(ResponseContract response) =>
        $"{response.AuthenticationState}|{response.Method}|{response.Path}";

    [Fact]
    public void BaselineCoversEveryRouteMethodAndAuthenticationState()
    {
        var baseline = JsonSerializer.Deserialize<RouteAuthenticationContract>(
            File.ReadAllText(BaselinePath),
            JsonOptions) ?? throw new InvalidOperationException(
                $"Could not deserialize {BaselinePath}.");

        Assert.Equal(
            new[]
            {
                "/",
                "/firms",
                "/firms/select",
                "/health",
                "/sign-in",
                "/sign-out",
            },
            baseline.Endpoints
                .Select(endpoint => endpoint.Path)
                .OrderBy(path => path, StringComparer.Ordinal));

        var requestCases = RequestCases();
        foreach (var state in new[] { "anonymous", "authenticated" })
        {
            foreach (var request in requestCases)
            {
                Assert.Contains(
                    baseline.Responses,
                    response =>
                        response.AuthenticationState == state &&
                        response.Method == request.Method &&
                        response.Path == request.Path);
            }
        }

        Assert.All(
            baseline.Responses,
            response =>
            {
                Assert.NotEmpty(response.BodySha256);
                Assert.NotNull(response.WwwAuthenticate);
                Assert.NotNull(response.Location);
            });
    }

    private static async Task<RouteAuthenticationContract> CaptureAsync()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness
            .CreateHost(directory, sessions)
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.Configure<OpenIdConnectOptions>(
                        "TaxAiOidc",
                        options =>
                        {
                            options.Configuration =
                                new OpenIdConnectConfiguration
                                {
                                    Issuer = "https://identity.example.test/",
                                    AuthorizationEndpoint =
                                        "https://identity.example.test/authorize",
                                    TokenEndpoint =
                                        "https://identity.example.test/token",
                                    EndSessionEndpoint =
                                        "https://identity.example.test/sign-out",
                                };
                        })));

        using var anonymous = PortalAuthorizationHarness.CreateClient(
            host,
            followRedirects: false);
        using var authenticated = PortalAuthorizationHarness.CreateClient(
            host,
            followRedirects: false);

        await PortalAuthorizationHarness.SignInAsync(authenticated);

        // Materialize endpoints before reading EndpointDataSource.
        using (var materialization = await anonymous.GetAsync("/health"))
        {
            Assert.Equal(HttpStatusCode.OK, materialization.StatusCode);
        }

        var endpoints = host.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => new EndpointContract(
                endpoint.RoutePattern.RawText!,
                // The framework's Blazor circuit endpoints declare no method
                // metadata at all, so this cannot be dereferenced unconditionally.
                // An empty method set is recorded rather than thrown on, and the
                // exact additive inventory below is what pins those endpoints.
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                        ?? [])
                    .OrderBy(method => method, StringComparer.Ordinal)
                    .ToArray(),
                endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                    ? "anonymous"
                    : endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
                        ? "required"
                        : "unspecified",
                endpoint.Metadata.Any(metadata =>
                    metadata.GetType().GetInterfaces().Any(contract =>
                        contract.FullName ==
                        "Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata"))))
            .OrderBy(endpoint => endpoint.Path, StringComparer.Ordinal)
            .ToArray();

        var responses = new List<ResponseContract>();
        foreach (var (state, client) in new[]
                 {
                     ("anonymous", anonymous),
                     ("authenticated", authenticated),
                 })
        {
            foreach (var requestCase in RequestCases())
            {
                using var request = new HttpRequestMessage(
                    new HttpMethod(requestCase.Method),
                    requestCase.Path);
                if (requestCase is { Method: "POST", Path: "/firms/select" })
                {
                    request.Content = new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["membershipId"] = Guid.Empty.ToString("D"),
                        });
                }

                using var response = await client.SendAsync(request);
                var body = NormalizeBody(
                    await response.Content.ReadAsStringAsync());

                responses.Add(
                    new ResponseContract(
                        state,
                        requestCase.Method,
                        requestCase.Path,
                        (int)response.StatusCode,
                        NormalizeLocation(response.Headers.Location),
                        string.Join(
                            ",",
                            response.Headers.WwwAuthenticate
                                .Select(value => value.ToString())
                                .OrderBy(value => value, StringComparer.Ordinal)),
                        response.Content.Headers.ContentType?.ToString() ??
                            string.Empty,
                        body.Length,
                        Convert.ToHexString(
                            SHA256.HashData(Encoding.UTF8.GetBytes(body)))));
            }
        }

        return new RouteAuthenticationContract(
            BaselineCommit,
            endpoints,
            responses
                .OrderBy(response => response.AuthenticationState, StringComparer.Ordinal)
                .ThenBy(response => response.Path, StringComparer.Ordinal)
                .ThenBy(response => response.Method, StringComparer.Ordinal)
                .ToArray());
    }

    private static RequestCase[] RequestCases() =>
    [
        new("GET", "/"),
        new("POST", "/"),
        new("GET", "/health"),
        new("POST", "/health"),
        new("GET", "/sign-in"),
        new("POST", "/sign-in"),
        new("GET", "/firms"),
        new("POST", "/firms"),
        new("POST", "/firms/select"),
        new("GET", "/firms/select"),
        new("POST", "/sign-out"),
        new("GET", "/sign-out"),
        new("GET", "/does-not-exist"),
    ];

    private static string NormalizeBody(string body) =>
        AntiforgeryValue.Replace(
            GuidValue.Replace(
                body.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n'),
                "{guid}"),
            "{antiforgery}");

    private static string NormalizeLocation(Uri? location)
    {
        if (location is null)
        {
            return string.Empty;
        }

        var absolute = location.IsAbsoluteUri
            ? location
            : new Uri(new Uri("https://localhost"), location);
        var prefix = location.IsAbsoluteUri
            ? $"{absolute.Scheme}://{absolute.Authority}{absolute.AbsolutePath}"
            : absolute.AbsolutePath;
        var queryKeys = QueryHelpers.ParseQuery(absolute.Query)
            .Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return queryKeys.Length == 0
            ? prefix
            : $"{prefix}?{string.Join('&', queryKeys)}";
    }

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

    private sealed record RouteAuthenticationContract(
        string BaselineCommit,
        EndpointContract[] Endpoints,
        ResponseContract[] Responses);

    private sealed record EndpointContract(
        string Path,
        string[] Methods,
        string Authorization,
        bool Antiforgery);

    private sealed record ResponseContract(
        string AuthenticationState,
        string Method,
        string Path,
        int Status,
        string Location,
        string WwwAuthenticate,
        string ContentType,
        int BodyLength,
        string BodySha256);

    private sealed record RequestCase(string Method, string Path);
}
