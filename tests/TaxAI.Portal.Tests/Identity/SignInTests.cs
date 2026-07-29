using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TaxAI.Portal.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// Supplies the identity-provider configuration every host in this assembly now
/// needs, the same way <c>PortalTestDeploymentConfiguration</c> supplies the
/// connection string: through a configuration provider outside source control.
/// <para>
/// The portal refuses to start without an authority and a client identifier,
/// because the only way into it is an external challenge and a host that cannot
/// challenge cannot authenticate anybody. Neither value is a secret and neither
/// appears in a tracked file; the authority is never contacted, because no test
/// in this project completes a real handshake.
/// </para>
/// <para>
/// A module initializer rather than a fixture, because the suites that start a
/// host construct <c>WebApplicationFactory&lt;Program&gt;</c> directly and share
/// no base type.
/// </para>
/// </summary>
internal static class PortalTestIdentityConfiguration
{
    internal const string Authority = "https://identity.example.test/";

    internal const string ClientId = "taxai-portal-under-test";

    [ModuleInitializer]
    internal static void Supply()
    {
        Environment.SetEnvironmentVariable("TaxAi__Oidc__Authority", Authority);
        Environment.SetEnvironmentVariable("TaxAi__Oidc__ClientId", ClientId);
    }
}

/// <summary>
/// T18 — proves what a completed sign-in produces: a principal built from this
/// repository's own identity tables, a firm claim only when the answer is
/// unambiguous, and nothing at all carried over from the identity provider.
/// <para>
/// Every scheme name, claim name, and route is written out here as an
/// independent literal. The portal grants this project no internals access, and
/// comparing a response to the constant that produced it would assert only that
/// a variable equals itself.
/// </para>
/// <para>
/// The suite drives the <b>production</b> cookie scheme and the
/// <b>production</b> ticket store. Only the two things a test cannot supply are
/// substituted: the identity tables and the session rows, which in a deployment
/// are SQL Server. The resolution path, the claim decisions, the cookie, and the
/// session lifetime are all the real ones.
/// </para>
/// </summary>
public sealed class SignInTests
{
    // -------------------------------------------------- AC-36: exactly one firm

    [Fact] // AC-36
    public async Task ExactlyOneActiveMembershipIssuesTheFirmClaimAndScopesTheSession()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        var firmId = directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        // The session record is the authority, and it is scoped.
        var session = Assert.Single(sessions.All);
        Assert.Equal(user.UserId, session.UserId);
        Assert.Equal(firmId, session.ActiveFirmId);

        // ...and the principal the application actually sees agrees with it.
        Assert.Equal(
            PortalIdentityHarness.Canonical(firmId),
            await PortalIdentityHarness.ReadFirmClaimAsync(host, session));
    }

    // ------------------------------------------- AC-63, AC-64: no firm is chosen

    [Fact] // AC-63
    public async Task ZeroActiveMembershipsLeavesTheSessionUnscopedAndIssuesNoClaim()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        var session = Assert.Single(sessions.All);

        Assert.Null(session.ActiveFirmId);
        Assert.Null(await PortalIdentityHarness.ReadFirmClaimAsync(host, session));
    }

    [Fact] // AC-64
    public async Task SeveralActiveMembershipsLeaveTheSessionUnscopedAndIssueNoClaim()
    {
        // The application chooses neither. "Pick the first" is how a member of
        // two firms ends up looking at the wrong one's returns, and an
        // alphabetical or most-recent tie-break is the same defect with a
        // rationale attached.
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        var first = directory.AddActiveMembership(user.UserId);
        var second = directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        var session = Assert.Single(sessions.All);

        Assert.NotEqual(first, second);
        Assert.Null(session.ActiveFirmId);
        Assert.Null(await PortalIdentityHarness.ReadFirmClaimAsync(host, session));
    }

    [Fact] // AC-63
    public async Task AnInactiveMembershipIsNotAnActiveOne()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddInactiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        Assert.Null(Assert.Single(sessions.All).ActiveFirmId);
    }

    // ------------------------------------- AC-55: the provider is never believed

    [Fact] // AC-55
    public async Task AProviderSuppliedFirmClaimIsDiscardedBeforeTheLocalPrincipalExists()
    {
        // The scenario the AC names: the token asserts F2, the user's only
        // active membership is F1. Whoever controls the identity provider must
        // not be able to name the firm whose data this session may read.
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        var ownFirmId = directory.AddActiveMembership(user.UserId);
        var providerFirmId = Guid.NewGuid();

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client, providerSuppliedFirmId: providerFirmId);

        var session = Assert.Single(sessions.All);

        Assert.NotEqual(providerFirmId, ownFirmId);
        Assert.Equal(ownFirmId, session.ActiveFirmId);

        var claim = await PortalIdentityHarness.ReadFirmClaimAsync(host, session);

        Assert.Equal(PortalIdentityHarness.Canonical(ownFirmId), claim);
        Assert.NotEqual(PortalIdentityHarness.Canonical(providerFirmId), claim);
    }

    [Fact] // AC-55, AC-63
    public async Task AProviderSuppliedFirmClaimCannotScopeAnOtherwiseUnscopedSession()
    {
        // The sharper case: the user has no membership at all, so an honoured
        // provider claim would be the only thing scoping the session. It must
        // leave nothing behind.
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client, providerSuppliedFirmId: Guid.NewGuid());

        Assert.Null(Assert.Single(sessions.All).ActiveFirmId);
    }

    // ----------------------------------------- the directory is the only source

    [Fact]
    public async Task AnUnknownSubjectSignsInNobodyAndCreatesNoSession()
    {
        // There is no registration surface in this repository, so an
        // unrecognized subject is not a new user. Fail closed: no session, no
        // cookie.
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var failure = await Record.ExceptionAsync(
            () => client.GetAsync(PortalIdentityHarness.TestSignInPath));

        Assert.NotNull(failure);
        Assert.Empty(sessions.All);
    }

    [Fact]
    public async Task AnInactiveUserSignsInNobodyAndCreatesNoSession()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(
            PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject, isActive: false);
        directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var failure = await Record.ExceptionAsync(
            () => client.GetAsync(PortalIdentityHarness.TestSignInPath));

        Assert.NotNull(failure);
        Assert.Empty(sessions.All);
    }

    [Fact]
    public async Task TheUserIsResolvedByIssuerAndSubjectOrdinally()
    {
        // Both halves of the key are used, and neither is case-folded: an
        // identity provider's subject is an opaque string, and "equal ignoring
        // case" is a different user.
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        PortalIdentityHarness.NextSignIn(host).Subject =
            PortalIdentityHarness.Subject.ToUpperInvariant();

        var failure = await Record.ExceptionAsync(
            () => client.GetAsync(PortalIdentityHarness.TestSignInPath));

        Assert.NotNull(failure);
        Assert.Empty(sessions.All);

        Assert.Equal(
            new[] { (PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject.ToUpperInvariant()) },
            directory.UserLookups);
    }

    // --------------------------------------- the schemes, and only these schemes

    [Fact]
    public void TheHostDeclaresExactlyTheCookieAndExternalSchemes()
    {
        using var host = PortalIdentityHarness.CreateHost(
            new FakeIdentityDirectory(), new RecordingSessionStore());
        using var client = PortalIdentityHarness.CreateClient(host);

        var schemes = host.Services
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync()
            .GetAwaiter()
            .GetResult()
            .Select(scheme => scheme.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "TaxAiOidc", "TaxAiSession" }, schemes);
    }

    [Fact]
    public void TheApplicationAuthenticatesWithTheCookieSchemeAndChallengesInPlace()
    {
        // The default challenge is deliberately the cookie scheme, not the
        // external one: a protected request that arrives without a session is
        // refused where it stands rather than bounced through an identity
        // provider, which is what lets a refusal be an unambiguous 401.
        using var host = PortalIdentityHarness.CreateHost(
            new FakeIdentityDirectory(), new RecordingSessionStore());
        using var client = PortalIdentityHarness.CreateClient(host);

        var options = host.Services
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;

        Assert.Equal("TaxAiSession", options.DefaultScheme);
        Assert.Equal("TaxAiSession", options.DefaultChallengeScheme);
        Assert.Equal("TaxAiSession", options.DefaultSignInScheme);
    }

    // -------------------------------------- AC-11, AC-98, AC-99, AC-165: the OIDC

    [Fact] // AC-99, AC-165
    public void TheExternalHandlerSavesNoToken()
    {
        // SaveTokens is the switch that decides whether the access, refresh, and
        // id tokens are serialized into the authentication properties and follow
        // the ticket into the cookie, the session row, and every log that ever
        // renders one. Off is the only setting this repository may ship.
        var options = PortalIdentityHarness.ExternalOptions();

        Assert.False(options.SaveTokens);
        Assert.False(options.GetClaimsFromUserInfoEndpoint);
        Assert.False(options.MapInboundClaims);
    }

    [Fact]
    public void TheExternalHandlerUsesTheAuthorizationCodeFlowWithPkce()
    {
        var options = PortalIdentityHarness.ExternalOptions();

        Assert.Equal("code", options.ResponseType);
        Assert.True(options.UsePkce);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Equal("TaxAiSession", options.SignInScheme);
        Assert.Equal("/signin-oidc", options.CallbackPath.Value);
    }

    [Fact]
    public void TheExternalHandlerRequestsTheMinimumScope()
    {
        // Every additional scope is a profile field this repository would then
        // be holding. The local user record stores issuer, subject, and active
        // state, and there is nothing for a wider scope to be stored in.
        Assert.Equal(new[] { "openid" }, PortalIdentityHarness.ExternalOptions().Scope.ToArray());
    }

    [Fact]
    public async Task SignInIsAnonymousAndChallengesTheExternalScheme()
    {
        using var host = PortalIdentityHarness.CreateExternallyConfiguredHost();
        using var client = PortalIdentityHarness.CreateClient(host, followRedirects: false);

        using var response = await client.GetAsync("/sign-in");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location!.ToString();

        Assert.StartsWith(PortalIdentityHarness.AuthorizationEndpoint, location, StringComparison.Ordinal);
        Assert.Contains("response_type=code", location, StringComparison.Ordinal);
        Assert.Contains("code_challenge", location, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- harness

    /// <summary>
    /// The shared harness for this spec's identity suites. It lives in one file
    /// on purpose: T18 owns exactly the files its task lists, and a new shared
    /// <c>Support/</c> file is not one of them.
    /// </summary>
    internal static class PortalIdentityHarness
    {
        internal const string CookieScheme = "TaxAiSession";

        internal const string ExternalScheme = "TaxAiOidc";

        internal const string FirmClaimType = "taxai_firm_id";

        internal const string UserClaimType = "taxai_user_id";

        internal const string CookieName = "taxai.session";

        /// <summary>The framework's own claim for the session-store key.</summary>
        internal const string TicketKeyClaimType =
            "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

        internal const string TestSignInPath = "/test-harness/sign-in";

        internal const string Issuer = "https://identity.example.test/";

        internal const string Subject = "subject-under-test";

        internal const string AuthorizationEndpoint = "https://identity.example.test/authorize";

        internal static string Canonical(Guid identifier) =>
            identifier.ToString("D", CultureInfo.InvariantCulture);

        /// <summary>
        /// A Development host. Development because the two ports below are
        /// substituted, and outside Development the host refuses to start unless
        /// the session store is the durable implementation — which is exactly the
        /// refusal <c>StartupRefusalTests</c> proves, and exactly what a test
        /// double is not.
        /// </summary>
        internal static WebApplicationFactory<Program> CreateHost(
            FakeIdentityDirectory directory,
            RecordingSessionStore sessions,
            IDictionary<string, string?>? configuration = null) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");

                if (configuration is not null)
                {
                    builder.ConfigureAppConfiguration(
                        (_, sources) => sources.AddInMemoryCollection(configuration));
                }

                builder.ConfigureTestServices(services =>
                {
                    // Keeps the developer key ring off disk for the duration of
                    // a test run; the guard's own key-ring cases live in
                    // StartupRefusalTests.
                    services.AddApprovedDurableDataProtection();

                    services.AddSingleton<IIdentityDirectory>(directory);
                    services.AddSingleton<IAuthenticationSessionStore>(sessions);
                    services.AddScoped<IDevelopmentFixtureSeeder>(_ => new SilentSeeder());

                    services.AddSingleton<PendingSignIn>();
                    services.AddSingleton<IStartupFilter, TestSignInStartupFilter>();
                });
            });

        /// <summary>
        /// The same host, additionally given a static OpenID Connect
        /// configuration so a challenge can be rendered without reaching a
        /// network.
        /// </summary>
        internal static WebApplicationFactory<Program> CreateExternallyConfiguredHost() =>
            CreateHost(
                new FakeIdentityDirectory(),
                new RecordingSessionStore(),
                configuration: null)
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(
                    services => services.Configure<OpenIdConnectOptions>(
                        ExternalScheme,
                        options => options.Configuration = new OpenIdConnectConfiguration
                        {
                            Issuer = Issuer,
                            AuthorizationEndpoint = AuthorizationEndpoint,
                            TokenEndpoint = "https://identity.example.test/token",
                        })));

        internal static OpenIdConnectOptions ExternalOptions()
        {
            using var host = CreateExternallyConfiguredHost();
            using var client = CreateClient(host);

            return host.Services
                .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(ExternalScheme);
        }

        /// <summary>
        /// An <c>https</c> base address, because the authentication cookie is
        /// issued <c>Secure</c> and a cookie container will not send a secure
        /// cookie back over cleartext. The test server does not care about the
        /// scheme; the client does.
        /// </summary>
        internal static HttpClient CreateClient(
            WebApplicationFactory<Program> host,
            bool followRedirects = true,
            bool handleCookies = true) =>
            host.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = followRedirects,
                HandleCookies = handleCookies,
            });

        internal static PendingSignIn NextSignIn(WebApplicationFactory<Program> host) =>
            host.Services.GetRequiredService<PendingSignIn>();

        /// <summary>
        /// Completes a sign-in through the production cookie scheme, and returns
        /// the response so a test can read the cookie the host set.
        /// </summary>
        internal static async Task<HttpResponseMessage> SignInAsync(
            HttpClient client,
            Guid? providerSuppliedFirmId = null)
        {
            var path = providerSuppliedFirmId is null
                ? TestSignInPath
                : $"{TestSignInPath}?{PendingSignIn.ProviderFirmQueryKey}={Canonical(providerSuppliedFirmId.Value)}";

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            return response;
        }

        /// <summary>
        /// The firm claim as the application would see it: rebuilt by the
        /// production ticket store from the session record, not read out of the
        /// cookie.
        /// </summary>
        internal static async Task<string?> ReadFirmClaimAsync(
            WebApplicationFactory<Program> host,
            AuthenticationSession session)
        {
            var ticket = await RetrieveTicketAsync(host, session.SessionId);

            return ticket?.Principal.FindFirst(FirmClaimType)?.Value;
        }

        internal static Task<AuthenticationTicket?> RetrieveTicketAsync(
            WebApplicationFactory<Program> host,
            Guid sessionId) =>
            TicketStore(host).RetrieveAsync(Canonical(sessionId));

        /// <summary>
        /// The production ticket store, resolved through the cookie options the
        /// host actually configured — so this reaches the same instance the
        /// handler uses rather than a second one built here.
        /// </summary>
        internal static ITicketStore TicketStore(WebApplicationFactory<Program> host) =>
            host.Services
                .GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
                .Get(CookieScheme)
                .SessionStore!;

        internal static string SessionCookie(HttpResponseMessage response) =>
            Assert.Single(
                response.Headers
                    .Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(header => header.Value)
                    .Where(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal)));
    }

    /// <summary>What the next call to the harness sign-in path should present.</summary>
    internal sealed class PendingSignIn
    {
        internal const string ProviderFirmQueryKey = "providerFirmId";

        internal string Issuer { get; set; } = PortalIdentityHarness.Issuer;

        internal string Subject { get; set; } = PortalIdentityHarness.Subject;
    }

    /// <summary>
    /// Inserts one test-only endpoint that signs a caller in to the
    /// <b>production</b> cookie scheme with an externally-shaped principal —
    /// issuer, subject, and optionally a firm claim the provider had no standing
    /// to assert.
    /// <para>
    /// This is how the suite exercises the real resolution path without standing
    /// up an identity provider. Everything downstream of
    /// <c>SignInAsync</c> — the local principal, the session row, the cookie, the
    /// ticket store — is production code.
    /// </para>
    /// </summary>
    internal sealed class TestSignInStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, proceed) =>
                {
                    if (!context.Request.Path.Equals(
                            PortalIdentityHarness.TestSignInPath, StringComparison.Ordinal))
                    {
                        await proceed();
                        return;
                    }

                    var pending = context.RequestServices.GetRequiredService<PendingSignIn>();

                    var identity = new ClaimsIdentity("TestExternalProvider");
                    identity.AddClaim(new Claim("iss", pending.Issuer));
                    identity.AddClaim(new Claim("sub", pending.Subject));

                    var providerFirmId =
                        context.Request.Query[PendingSignIn.ProviderFirmQueryKey].ToString();

                    if (!string.IsNullOrEmpty(providerFirmId))
                    {
                        identity.AddClaim(
                            new Claim(PortalIdentityHarness.FirmClaimType, providerFirmId));
                    }

                    await context.SignInAsync(
                        PortalIdentityHarness.CookieScheme, new ClaimsPrincipal(identity));

                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                });

                next(app);
            };
    }

    /// <summary>Records that it was asked, and writes nothing.</summary>
    internal sealed class SilentSeeder : IDevelopmentFixtureSeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// The three identity tables, in memory.
    /// <para>
    /// Its rows are constructed through <c>Tenancy</c>'s non-public
    /// constructors, exactly as the storage layer's materializer does. The
    /// constructors are internal so that only the module's own fail-closed
    /// projection can create a record; a test that needs a row to read is not
    /// the caller that seam exists to stop, and no production code here does
    /// anything of the kind.
    /// </para>
    /// </summary>
    internal sealed class FakeIdentityDirectory : IIdentityDirectory
    {
        private static readonly DateTimeOffset Timestamp =
            new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly List<PortalUser> _users = new();

        private readonly List<FirmMembership> _memberships = new();

        private readonly List<(string Issuer, string Subject)> _userLookups = new();

        internal IReadOnlyList<(string Issuer, string Subject)> UserLookups => _userLookups;

        internal PortalUser AddUser(string issuer, string subject, bool isActive = true)
        {
            var user = Construct<PortalUser>(
                Guid.NewGuid(), issuer, subject, isActive, Timestamp, Timestamp);

            _users.Add(user);

            return user;
        }

        internal Guid AddActiveMembership(Guid userId) => AddMembership(userId, isActive: true);

        internal Guid AddInactiveMembership(Guid userId) => AddMembership(userId, isActive: false);

        private Guid AddMembership(Guid userId, bool isActive)
        {
            var firmId = Guid.NewGuid();

            _memberships.Add(Construct<FirmMembership>(
                Guid.NewGuid(), firmId, userId, isActive, Timestamp, Timestamp));

            return firmId;
        }

        public Task<PortalUser?> FindUserAsync(
            string issuer, string subject, CancellationToken cancellationToken)
        {
            _userLookups.Add((issuer, subject));

            return Task.FromResult(_users.SingleOrDefault(user =>
                string.Equals(user.Issuer, issuer, StringComparison.Ordinal) &&
                string.Equals(user.Subject, subject, StringComparison.Ordinal)));
        }

        public Task<IReadOnlyList<FirmMembership>> FindActiveMembershipsAsync(
            Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FirmMembership>>(
                _memberships
                    .Where(membership => membership.UserId == userId && membership.IsActive)
                    .ToArray());

        public Task<FirmMembership?> FindMembershipAsync(
            Guid userId, Guid membershipId, CancellationToken cancellationToken) =>
            Task.FromResult(_memberships.SingleOrDefault(membership =>
                membership.UserId == userId && membership.MembershipId == membershipId));

        public Task<IReadOnlyList<Firm>> FindFirmsAsync(
            IReadOnlyList<Guid> firmIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Firm>>(Array.Empty<Firm>());

        internal static T Construct<T>(params object?[] arguments) =>
            (T)typeof(T)
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(constructor => constructor.GetParameters().Length == arguments.Length)
                .Invoke(arguments);
    }

    /// <summary>
    /// The session table, in memory, with the same fail-closed semantics the
    /// durable one has: a revoked row stays revoked, a removed row is never
    /// re-created, and every instance sees the same state.
    /// </summary>
    internal sealed class RecordingSessionStore : IAuthenticationSessionStore
    {
        private readonly ConcurrentDictionary<Guid, AuthenticationSession> _sessions = new();

        /// <summary>Turns the store into one that is simply unavailable.</summary>
        internal bool FailsEveryCall { get; set; }

        internal IReadOnlyList<AuthenticationSession> All => _sessions.Values.ToArray();

        internal int TouchCount { get; private set; }

        internal AuthenticationSession? Find(Guid sessionId) =>
            _sessions.TryGetValue(sessionId, out var session) ? session : null;

        /// <summary>Plants a row with boundaries a test chooses.</summary>
        internal AuthenticationSession Seed(
            Guid userId,
            Guid? activeFirmId,
            DateTimeOffset createdAtUtc,
            DateTimeOffset lastSeenAtUtc,
            DateTimeOffset idleExpiresAtUtc,
            DateTimeOffset absoluteExpiresAtUtc,
            DateTimeOffset? revokedAtUtc = null)
        {
            var sessionId = Guid.NewGuid();

            var session = FakeIdentityDirectory.Construct<AuthenticationSession>(
                sessionId,
                userId,
                activeFirmId,
                createdAtUtc,
                lastSeenAtUtc,
                idleExpiresAtUtc,
                absoluteExpiresAtUtc,
                revokedAtUtc);

            _sessions[sessionId] = session;

            return session;
        }

        public Task<AuthenticationSession> CreateAsync(
            Guid userId,
            Guid? activeFirmId,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken)
        {
            Refuse();

            var sessionId = Guid.NewGuid();

            // Through the module's own public construction seam, so the
            // boundaries are the real policy's and not this file's opinion.
            var created = AuthenticationSession.Create(sessionId, userId, activeFirmId, createdAtUtc);

            Assert.True(created.IsSuccess);

            _sessions[sessionId] = created.Value!;

            return Task.FromResult(created.Value!);
        }

        public Task<AuthenticationSession?> FindAsync(
            Guid sessionId, CancellationToken cancellationToken)
        {
            Refuse();

            return Task.FromResult(Find(sessionId));
        }

        public Task<AuthenticationSession?> TouchAsync(
            AuthenticationSession session,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Refuse();
            ArgumentNullException.ThrowIfNull(session);

            TouchCount++;

            var boundaries = SessionLifetimePolicy.Touch(session, atUtc);

            if (boundaries.IsFailure)
            {
                return Task.FromResult<AuthenticationSession?>(null);
            }

            var touched = FakeIdentityDirectory.Construct<AuthenticationSession>(
                session.SessionId,
                session.UserId,
                session.ActiveFirmId,
                session.CreatedAtUtc,
                atUtc,
                boundaries.Value!.IdleExpiresAtUtc,
                boundaries.Value.AbsoluteExpiresAtUtc,
                session.RevokedAtUtc);

            _sessions[session.SessionId] = touched;

            return Task.FromResult<AuthenticationSession?>(touched);
        }

        public Task RevokeAsync(
            AuthenticationSession session,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            Refuse();
            ArgumentNullException.ThrowIfNull(session);

            _sessions[session.SessionId] = FakeIdentityDirectory.Construct<AuthenticationSession>(
                session.SessionId,
                session.UserId,
                session.ActiveFirmId,
                session.CreatedAtUtc,
                session.LastSeenAtUtc,
                session.IdleExpiresAtUtc,
                session.AbsoluteExpiresAtUtc,
                atUtc);

            return Task.CompletedTask;
        }

        private void Refuse()
        {
            if (FailsEveryCall)
            {
                throw new InvalidOperationException("The session store is unavailable.");
            }
        }
    }
}
