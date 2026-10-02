using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Persistence;
using TaxAI.Portal.Tests.Support;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// Spec 017 T01 (decision D-1) — the Development-only sign-in.
/// <para>
/// Two things are proven here. First, that it is only a shortcut around the
/// external identity provider: what it produces is the normal durable session,
/// the normal principal, and a caller subject to the normal per-request
/// revalidation, sign-out and revocation. Second, that it does not exist
/// outside Development: no route, no seeding, and no response that differs from
/// any other unmapped path.
/// </para>
/// </summary>
public sealed class DevelopmentSignInTests
{
    private const string SignInPath = "/development/sign-in";

    // Spec 017 T03: a completed sign-in now lands on the case list.
    private const string SignedInPath = "/intake-cases";

    /// <summary>A firm-scoped page that reads no store, used to probe revalidation.</summary>
    private const string ProtectedPagePath = "/intake-cases/new";

    private const string UnmappedPath = "/development/not-a-route";

    private const string PrincipalProbePath = "/test-harness/t017-principal";

    private const string SessionCookieName = "taxai.session";

    private const string AntiforgeryField = "__RequestVerificationToken";

    // ------------------------------------------------------- AC-14 to AC-17

    [Fact] // AC-14
    public async Task SignInCreatesExactlyOneDurableSessionForTheSeededUserAndFirm()
    {
        using var world = World.Create();
        using var client = world.CreateClient();

        using var response = await world.SignInAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SignedInPath, response.Headers.Location?.OriginalString);

        var session = Assert.Single(world.Sessions.All);
        Assert.Equal(world.User.UserId, session.UserId);
        Assert.Equal(world.Membership.FirmId, session.ActiveFirmId);
        Assert.Null(session.RevokedAtUtc);

        // The browser holds the existing opaque-key cookie and nothing else
        // about the caller: no identifier, issuer or subject is readable in it.
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(SessionCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        foreach (var forbidden in new[]
                 {
                     PortalAuthorizationHarness.Canonical(world.User.UserId),
                     PortalAuthorizationHarness.Canonical(world.Membership.FirmId),
                     PortalAuthorizationHarness.Canonical(session.SessionId),
                     DevelopmentFixtureIdentity.Subject,
                     "development-fixture",
                 })
        {
            Assert.DoesNotContain(forbidden, cookie, StringComparison.OrdinalIgnoreCase);
        }

        // The lookup that resolved the user was the directory's ordinary
        // issuer-and-subject lookup, with exactly the seeded pair.
        Assert.Equal(
            (DevelopmentFixtureIdentity.Issuer, DevelopmentFixtureIdentity.Subject),
            world.Directory.LastIssuerSubjectLookup);
    }

    [Fact] // AC-15
    public async Task ThePrincipalIsExactlyTheNormalLocalUserAndFirmClaims()
    {
        using var world = World.Create();
        using var client = world.CreateClient();
        using var signedIn = await world.SignInAsync(client);

        var claims = await client.GetStringAsync(PrincipalProbePath);

        Assert.Equal(
            "taxai_firm_id=" + PortalAuthorizationHarness.Canonical(world.Membership.FirmId) +
            "\ntaxai_user_id=" + PortalAuthorizationHarness.Canonical(world.User.UserId),
            claims);
    }

    [Fact] // AC-16
    public async Task EveryProtectedRequestStillPassesTheFullRevalidation()
    {
        using var world = World.Create();
        using var client = world.CreateClient();
        using var signedIn = await world.SignInAsync(client);
        var recordId = Guid.NewGuid();

        using (var allowed = await client.GetAsync($"/reviews/{recordId:D}"))
        {
            // Authorized, and the store was asked: the empty 404 is the store's
            // "no such record in this firm".
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
            Assert.Equal(1, world.Reviews.FindCalls);
        }

        world.Directory.SetMembershipActive(isActive: false);

        using var refused = await client.GetAsync($"/reviews/{recordId:D}");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Request refused.", await refused.Content.ReadAsStringAsync());
        Assert.Equal(1, world.Reviews.FindCalls);
    }

    [Fact] // AC-16
    public async Task ADisabledUserIsRefusedOnTheNextRequest()
    {
        using var world = World.Create();
        using var client = world.CreateClient();
        using var signedIn = await world.SignInAsync(client);

        world.Directory.SetUserActive(isActive: false);

        using var refused = await client.GetAsync(ProtectedPagePath);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact] // AC-17
    public async Task SignOutRevokesTheSessionAndACopiedCookieIsRefused()
    {
        using var world = World.Create();
        using var client = world.CreateClient();
        using var signedIn = await world.SignInAsync(client);
        var copiedCookie = Assert.Single(
                signedIn.Headers.GetValues("Set-Cookie"),
                value => value.StartsWith(SessionCookieName + "=", StringComparison.Ordinal))
            .Split(';')[0];

        var page = await client.GetStringAsync(ProtectedPagePath);
        using var signedOut = await client.PostAsync(
            "/sign-out",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [AntiforgeryField] = AntiforgeryToken(page),
            }));

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        Assert.NotNull(Assert.Single(world.Sessions.All).RevokedAtUtc);

        using var replayClient = world.CreateClient(handleCookies: false);
        using var replay = new HttpRequestMessage(HttpMethod.Get, ProtectedPagePath);
        replay.Headers.Add("Cookie", copiedCookie);
        using var replayed = await replayClient.SendAsync(replay);

        Assert.Equal(HttpStatusCode.Unauthorized, replayed.StatusCode);
    }

    // ------------------------------------------------------ AC-19 to AC-23

    [Fact]
    public void TheDevelopmentHostMapsExactlyTheGetAndThePostOfTheOnePath()
    {
        using var world = World.Create();

        Assert.Equal(
            new[] { "GET", "POST" },
            RouteMethods(world.Host.Services)
                .Where(route => route.Pattern == SignInPath)
                .Select(route => route.Method)
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToArray());
        Assert.DoesNotContain(
            RouteMethods(world.Host.Services),
            route => route.Pattern.StartsWith("/development", StringComparison.OrdinalIgnoreCase) &&
                     route.Pattern != SignInPath);
    }

    [Theory] // AC-19, AC-20
    [InlineData("Production")]
    [InlineData("Staging")]
    public void TheRouteIsAbsentFromTheEndpointInventoryOutsideDevelopment(string environment)
    {
        using var host = World.CreateNonDevelopmentHost(environment, new CountingSeeder());

        Assert.DoesNotContain(
            RouteMethods(host.Services),
            route => route.Pattern.Contains("development", StringComparison.OrdinalIgnoreCase));
    }

    [Theory] // AC-21
    [InlineData("Production", "GET")]
    [InlineData("Production", "POST")]
    [InlineData("Staging", "GET")]
    [InlineData("Staging", "POST")]
    public async Task OutsideDevelopmentThePathIsIndistinguishableFromAnUnmappedPath(
        string environment,
        string method)
    {
        using var host = World.CreateNonDevelopmentHost(environment, new CountingSeeder());
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

        var mapped = await CaptureAsync(client, method, SignInPath);
        var unmapped = await CaptureAsync(client, method, UnmappedPath);

        Assert.Equal(HttpStatusCode.NotFound, mapped.Status);
        Assert.Equal(unmapped.Status, mapped.Status);
        Assert.Equal(unmapped.Body, mapped.Body);
        Assert.Equal(unmapped.Headers, mapped.Headers);
        Assert.DoesNotContain(mapped.Headers, header =>
            header.StartsWith("Set-Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Theory] // AC-22
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NoFixtureIsSeededOutsideDevelopment(string environment)
    {
        var seeder = new CountingSeeder();
        using var host = World.CreateNonDevelopmentHost(environment, seeder);

        using var client = host.CreateClient();

        Assert.Equal(0, seeder.Calls);
    }

    [Fact] // AC-22 positive control
    public void TheFixtureIsSeededInDevelopment()
    {
        using var world = World.Create();
        using var client = world.CreateClient();

        Assert.Equal(1, world.Seeder.Calls);
    }

    [Fact] // AC-23
    public void NoMigrationInsertsAnIdentityOrAnyOtherRow()
    {
        var migrations = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "Persistence", "Migrations"),
                "*.cs")
            .ToArray();

        Assert.NotEmpty(migrations);
        foreach (var migration in migrations)
        {
            var text = File.ReadAllText(migration);
            foreach (var forbidden in new[]
                     {
                         "InsertData", "INSERT INTO", "HasData(", "UpdateData(",
                         DevelopmentFixtureIdentity.Subject, "development-fixture",
                     })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ------------------------------------------------------ AC-24 to AC-27

    [Theory] // AC-24
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-valid-antiforgery-token")]
    public async Task APostWithoutAValidAntiforgeryTokenCreatesNoSession(string? token)
    {
        using var world = World.Create();
        using var client = world.CreateClient();

        // The cookie half of the token pair is present; only the request token
        // is missing or wrong.
        _ = await client.GetStringAsync(SignInPath);
        var fields = new Dictionary<string, string>();
        if (token is not null)
        {
            fields[AntiforgeryField] = token;
        }

        using var response = await client.PostAsync(SignInPath, new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(world.Sessions.All);
        Assert.False(SetsSessionCookie(response));
        Assert.Null(world.Directory.LastIssuerSubjectLookup);
    }

    [Fact] // AC-25
    public async Task NoCallerSuppliedUserFirmOrReturnAddressIsHonoured()
    {
        using var world = World.Create();
        var foreignUser = Guid.NewGuid();
        var foreignFirm = Guid.NewGuid();
        using var client = world.CreateClient();
        var token = AntiforgeryToken(await client.GetStringAsync(SignInPath));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            SignInPath +
            $"?userId={foreignUser:D}&firmId={foreignFirm:D}&membershipId={Guid.NewGuid():D}" +
            "&returnUrl=https%3A%2F%2Fevil.example.test%2F&ReturnUrl=%2Freviews");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [AntiforgeryField] = token,
            ["userId"] = foreignUser.ToString("D"),
            ["firmId"] = foreignFirm.ToString("D"),
            ["taxai_firm_id"] = foreignFirm.ToString("D"),
            ["iss"] = "https://identity.example.test/",
            ["sub"] = "someone-else",
            ["returnUrl"] = "https://evil.example.test/",
        });
        request.Headers.Add("X-TaxAi-Firm-Id", foreignFirm.ToString("D"));
        request.Headers.Add("X-User-Id", foreignUser.ToString("D"));
        request.Headers.Add("X-Forwarded-Host", "evil.example.test");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SignedInPath, response.Headers.Location?.OriginalString);
        var session = Assert.Single(world.Sessions.All);
        Assert.Equal(world.User.UserId, session.UserId);
        Assert.Equal(world.Membership.FirmId, session.ActiveFirmId);
        Assert.Equal(
            (DevelopmentFixtureIdentity.Issuer, DevelopmentFixtureIdentity.Subject),
            world.Directory.LastIssuerSubjectLookup);
    }

    [Theory] // AC-26
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAbsentOrInactiveSeededUserFailsClosed(bool userExists)
    {
        using var world = World.Create(seedUser: userExists);
        if (userExists)
        {
            world.Directory.SetUserActive(isActive: false);
        }

        using var client = world.CreateClient();
        var token = AntiforgeryToken(await client.GetStringAsync(SignInPath));

        using var response = await client.PostAsync(
            SignInPath,
            new FormUrlEncodedContent(new Dictionary<string, string> { [AntiforgeryField] = token }));

        // The same fixed failure an external sign-in that resolves to no active
        // local user produces: nothing is created and nothing is explained.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Request could not be completed.", await response.Content.ReadAsStringAsync());
        Assert.Empty(world.Sessions.All);
        Assert.False(SetsSessionCookie(response));
    }

    [Fact] // AC-14: no firm claim is invented when the existing rule would issue none.
    public async Task ASeededUserWithNoActiveMembershipGetsASessionWithNoFirm()
    {
        using var world = World.Create();
        world.Directory.SetMembershipActive(isActive: false);
        using var client = world.CreateClient();

        using var response = await world.SignInAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(Assert.Single(world.Sessions.All).ActiveFirmId);

        using var refused = await client.GetAsync(ProtectedPagePath);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact] // AC-27
    public void TheSignInFileBuildsNoParallelAuthorizationModel()
    {
        var source = ExecutableText(Path.Combine(
            RepositoryRoot, "src", "TaxAI.Portal", "DevelopmentSignInEndpoints.cs"));

        foreach (var forbidden in new[]
                 {
                     // No scheme, policy, store, or session of its own.
                     "AddAuthentication", "AddScheme", "AddCookie", "AddPolicy", "AddAuthorization",
                     "IAuthenticationSessionStore", "SessionTicketStore", "ITicketStore",
                     "AuthenticationSession", "AuthenticationTicket", "IDataProtect",
                     // No local principal built here, and no tenancy claim issued here.
                     "FirmClaimType", "UserClaimType", "taxai_firm_id", "taxai_user_id",
                     "ClaimsIdentity(PortalIdentity.CookieScheme", "IIdentityDirectory",
                     // No request value of any kind.
                     "Request.Query", "Request.Form", "Request.Headers", "Request.Cookies",
                     "RouteValues", "ReadFormAsync", "FromForm", "FromQuery", "FromHeader",
                     "ReturnUrl", "RedirectUri",
                     // No environment decision of its own, and no cookie written by hand.
                     "IsDevelopment", "EnvironmentName", "Response.Cookies", "CookieOptions",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // Exactly two claims are written — issuer and subject — and the sign-in
        // goes to the existing cookie scheme exactly once.
        Assert.Equal(2, Regex.Matches(source, @"\.AddClaim\(").Count);
        Assert.Contains("PortalIdentity.IssuerClaimType, DevelopmentFixtureIdentity.Issuer", source, StringComparison.Ordinal);
        Assert.Contains("PortalIdentity.SubjectClaimType, DevelopmentFixtureIdentity.Subject", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"\bSignInAsync\(\s*PortalIdentity\.CookieScheme"));
        Assert.Contains("new ClaimsIdentity(PortalIdentity.ExternalScheme)", source, StringComparison.Ordinal);

        // The antiforgery result is enforced by the handler itself, and both
        // routes are declared anonymous explicitly.
        Assert.Contains("IAntiforgeryValidationFeature", source, StringComparison.Ordinal);
        Assert.Contains("FirmEndpoints.RequiredAntiforgeryMetadata", source, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(source, @"\.AllowAnonymous\(\)").Count);
    }

    [Fact] // AC-19, AC-20: the route is mapped in one place, behind the ordinal check.
    public void TheRouteIsMappedOnceBehindTheOrdinalDevelopmentComparison()
    {
        var portal = Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");
        var mappings = Directory
            .EnumerateFiles(portal, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (File: Path.GetFileName(path), Text: ExecutableText(path)))
            .Where(entry => entry.Text.Contains("DevelopmentSignInEndpoints.Map(", StringComparison.Ordinal))
            .ToArray();

        var program = Assert.Single(mappings);
        Assert.Equal("Program.cs", program.File);
        Assert.Single(Regex.Matches(program.Text, @"DevelopmentSignInEndpoints\.Map\("));
        Assert.Matches(
            new Regex(
                @"if \(app\.Environment\.IsDevelopment\(\) &&\s*" +
                @"PortalStartupGuards\.RunsInDevelopment\(app\.Environment\.EnvironmentName\)\)\s*" +
                @"\{\s*DevelopmentSignInEndpoints\.Map\(app\);\s*\}",
                RegexOptions.CultureInvariant),
            program.Text);
    }

    // ----------------------------------------------------------------- helpers

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static bool SetsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
        cookies.Any(value =>
            value.StartsWith(SessionCookieName + "=", StringComparison.Ordinal) &&
            !value.StartsWith(SessionCookieName + "=;", StringComparison.Ordinal));

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"" + AntiforgeryField + "\"[^>]*value=\"(?<token>[^\"]+)\"");
        Assert.True(match.Success, "The page must render an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    private static async Task<(HttpStatusCode Status, string Body, string[] Headers)> CaptureAsync(
        HttpClient client,
        string method,
        string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());
        }

        using var response = await client.SendAsync(request);
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Where(header => !string.Equals(header.Key, "Date", StringComparison.OrdinalIgnoreCase))
            .Select(header => $"{header.Key}: {string.Join(",", header.Value)}")
            .OrderBy(header => header, StringComparer.Ordinal)
            .ToArray();

        return (response.StatusCode, await response.Content.ReadAsStringAsync(), headers);
    }

    private static IEnumerable<(string Pattern, string Method)> RouteMethods(IServiceProvider services) =>
        services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "*" })
                .Select(method => ("/" + endpoint.RoutePattern.RawText!.TrimStart('/'), method)));

    private static string ExecutableText(string path) =>
        string.Join(
            "\n",
            File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line =>
                    line.Length > 0 &&
                    !line.StartsWith("//", StringComparison.Ordinal)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed class World : IDisposable
    {
        private World(
            WebApplicationFactory<Program> host,
            SeededDirectory directory,
            PortalAuthorizationHarness.SessionStore sessions,
            CountingReviewRepository reviews,
            CountingSeeder seeder)
        {
            Host = host;
            Directory = directory;
            Sessions = sessions;
            Reviews = reviews;
            Seeder = seeder;
        }

        internal WebApplicationFactory<Program> Host { get; }

        internal SeededDirectory Directory { get; }

        internal PortalAuthorizationHarness.SessionStore Sessions { get; }

        internal CountingReviewRepository Reviews { get; }

        internal CountingSeeder Seeder { get; }

        internal PortalUser User => Directory.User!;

        internal FirmMembership Membership => Directory.Membership!;

        internal static World Create(bool seedUser = true)
        {
            var directory = new SeededDirectory(seedUser);
            var sessions = new PortalAuthorizationHarness.SessionStore();
            var reviews = new CountingReviewRepository();
            var seeder = new CountingSeeder();
            var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                {
                    services.AddApprovedDurableDataProtection();
                    services.AddSingleton<IIdentityDirectory>(directory);
                    services.AddSingleton<IAuthenticationSessionStore>(sessions);
                    services.AddSingleton<IReviewRecordRepository>(reviews);
                    services.AddScoped<IDevelopmentFixtureSeeder>(_ => seeder);
                    services.AddSingleton<IStartupFilter, PrincipalProbeFilter>();
                });
            });

            return new World(host, directory, sessions, reviews, seeder);
        }

        /// <summary>
        /// A non-Development host keeps the real durable session store: the
        /// startup guard refuses any other, and no request here reaches it.
        /// </summary>
        internal static WebApplicationFactory<Program> CreateNonDevelopmentHost(
            string environment,
            CountingSeeder seeder) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.ConfigureTestServices(services =>
                {
                    services.AddApprovedDurableDataProtection();
                    services.AddScoped<IDevelopmentFixtureSeeder>(_ => seeder);
                });
            });

        internal HttpClient CreateClient(bool handleCookies = true) =>
            Host.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = handleCookies,
            });

        internal async Task<HttpResponseMessage> SignInAsync(HttpClient client)
        {
            var token = AntiforgeryToken(await client.GetStringAsync(SignInPath));

            return await client.PostAsync(
                SignInPath,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    [AntiforgeryField] = token,
                }));
        }

        public void Dispose() => Host.Dispose();
    }

    /// <summary>
    /// The identity directory as a seeded Development database presents it:
    /// one user with the fixed synthetic issuer and subject, one membership.
    /// </summary>
    private sealed class SeededDirectory : IIdentityDirectory
    {
        private static readonly DateTimeOffset Timestamp =
            new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly Firm _firm = PortalAuthorizationHarness.Construct<Firm>(
            Guid.NewGuid(), "Example Firm A", Timestamp, Timestamp);

        internal SeededDirectory(bool seedUser)
        {
            if (!seedUser)
            {
                return;
            }

            User = PortalAuthorizationHarness.Construct<PortalUser>(
                Guid.NewGuid(),
                DevelopmentFixtureIdentity.Issuer,
                DevelopmentFixtureIdentity.Subject,
                true,
                Timestamp,
                Timestamp);
            Membership = PortalAuthorizationHarness.Construct<FirmMembership>(
                Guid.NewGuid(), _firm.FirmId, User.UserId, true, Timestamp, Timestamp);
        }

        internal PortalUser? User { get; private set; }

        internal FirmMembership? Membership { get; private set; }

        internal (string Issuer, string Subject)? LastIssuerSubjectLookup { get; private set; }

        internal void SetUserActive(bool isActive) =>
            User = PortalAuthorizationHarness.Construct<PortalUser>(
                User!.UserId, User.Issuer, User.Subject, isActive, Timestamp, Timestamp);

        internal void SetMembershipActive(bool isActive) =>
            Membership = PortalAuthorizationHarness.Construct<FirmMembership>(
                Membership!.MembershipId,
                Membership.FirmId,
                Membership.UserId,
                isActive,
                Timestamp,
                Timestamp);

        public Task<PortalUser?> FindUserAsync(
            string issuer,
            string subject,
            CancellationToken cancellationToken)
        {
            LastIssuerSubjectLookup = (issuer, subject);

            return Task.FromResult(
                User is not null &&
                string.Equals(User.Issuer, issuer, StringComparison.Ordinal) &&
                string.Equals(User.Subject, subject, StringComparison.Ordinal)
                    ? User
                    : null);
        }

        public Task<PortalUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(User is not null && User.UserId == userId ? User : null);

        public Task<IReadOnlyList<FirmMembership>> FindActiveMembershipsAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FirmMembership>>(
                Membership is not null && Membership.UserId == userId && Membership.IsActive
                    ? new[] { Membership }
                    : Array.Empty<FirmMembership>());

        public Task<FirmMembership?> FindMembershipAsync(
            Guid userId,
            Guid membershipId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Membership is not null &&
                Membership.UserId == userId &&
                Membership.MembershipId == membershipId
                    ? Membership
                    : null);

        public Task<IReadOnlyList<Firm>> FindFirmsAsync(
            IReadOnlyList<Guid> firmIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Firm>>(
                firmIds.Contains(_firm.FirmId) ? new[] { _firm } : Array.Empty<Firm>());
    }

    private sealed class CountingSeeder : IDevelopmentFixtureSeeder
    {
        internal int Calls { get; private set; }

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingReviewRepository : IReviewRecordRepository
    {
        internal int FindCalls { get; private set; }

        public Task<ParseResult<PersistedReviewRecord>> SaveAsync(
            TrustedFirmId firm,
            PersistedReviewRecord record,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The read-only test repository was asked to save.");

        public Task<PersistedReviewRecord?> FindAsync(
            TrustedFirmId firm,
            Guid recordId,
            CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult<PersistedReviewRecord?>(null);
        }
    }

    /// <summary>
    /// Reports the claims of the session principal, so a test can prove what a
    /// Development sign-in produced without the product exposing them.
    /// </summary>
    private sealed class PrincipalProbeFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, proceed) =>
                {
                    if (!context.Request.Path.Equals(PrincipalProbePath, StringComparison.Ordinal))
                    {
                        await proceed(context);
                        return;
                    }

                    var authentication = await context.AuthenticateAsync("TaxAiSession");
                    if (!authentication.Succeeded || authentication.Principal is null)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    await context.Response.WriteAsync(string.Join(
                        "\n",
                        authentication.Principal.Claims
                            .Select(claim => $"{claim.Type}={claim.Value}")
                            .OrderBy(text => text, StringComparer.Ordinal)));
                });

                next(app);
            };
    }
}
