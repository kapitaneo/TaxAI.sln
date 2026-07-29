using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// T19 — proves both authorization stages reload identity state on every request
/// and fail closed without weakening the unscoped tenancy bootstrap.
/// </summary>
public sealed class MembershipVerificationTests
{
    [Fact] // AC-157, AC-160
    public async Task BootstrapAllowsANullActiveFirmAndReloadsIdentityEveryRequest()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var first = directory.AddMembership(user.UserId, "Example Firm One");
        var second = directory.AddMembership(user.UserId, "Example Firm Two");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);

        Assert.Null(Assert.Single(sessions.All).ActiveFirmId);
        directory.ResetCounters();

        using var firstResponse = await client.GetAsync("/firms");
        using var secondResponse = await client.GetAsync("/firms");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(2, directory.UserByIdLookupCount);
        Assert.Equal(2, directory.ActiveMembershipLookupCount);

        var body = await firstResponse.Content.ReadAsStringAsync();
        Assert.Contains("Example Firm One", body, StringComparison.Ordinal);
        Assert.Contains("Example Firm Two", body, StringComparison.Ordinal);
        Assert.Contains(PortalAuthorizationHarness.Canonical(first.MembershipId), body, StringComparison.Ordinal);
        Assert.Contains(PortalAuthorizationHarness.Canonical(second.MembershipId), body, StringComparison.Ordinal);
        Assert.DoesNotContain(PortalAuthorizationHarness.Canonical(first.FirmId), body, StringComparison.Ordinal);
        Assert.DoesNotContain(PortalAuthorizationHarness.Canonical(second.FirmId), body, StringComparison.Ordinal);
    }

    [Fact] // AC-157
    public async Task AnInactiveLocalUserIsRefusedOnTheNextBootstrapRequest()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var membership = directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);
        directory.SetUserActive(user.UserId, isActive: false);

        using var response = await client.GetAsync("/firms");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(
            PortalAuthorizationHarness.Canonical(membership.MembershipId),
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Theory] // AC-160
    [InlineData(1)]
    [InlineData(2)]
    public async Task AnIdentityStoreFailureUsesTheSameFixedRefusal(int failureValue)
    {
        var failure = (PortalAuthorizationHarness.DirectoryFailure)failureValue;
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);
        directory.UserByIdFailure = failure;

        using var response = await client.GetAsync("/firms");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await response.Content.ReadAsStringAsync());
    }

    [Fact] // AC-65, AC-186
    public async Task ADisabledMembershipIsRefusedByTheFullFirmScopedStage()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var membership = directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);

        var live = Assert.Single(sessions.All);
        Assert.Equal(membership.FirmId, live.ActiveFirmId);

        directory.SetMembershipActive(membership.MembershipId, isActive: false);

        using var response = await client.GetAsync(
            PortalAuthorizationHarness.FirmScopedTestPath);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await response.Content.ReadAsStringAsync());
        Assert.Null(live.RevokedAtUtc);
    }
}

/// <summary>
/// Shared T19 HTTP harness. It substitutes only the identity tables and session
/// rows; authentication, cookie handling, authorization, antiforgery, endpoint
/// code, and replacement-session issuance are production code.
/// </summary>
internal static class PortalAuthorizationHarness
{
    internal const string CookieScheme = "TaxAiSession";

    internal const string TestSignInPath = "/test-harness/t19-sign-in";

    internal const string FirmScopedTestPath = "/test-harness/t19-firm-scoped";

    internal const string Issuer = "https://identity.example.test/";

    internal const string Subject = "t19-subject-under-test";

    internal const string FixedRefusalBody = "Request refused.";

    internal enum DirectoryFailure
    {
        None,
        Throw,
        Timeout,
    }

    internal static WebApplicationFactory<Program> CreateHost(
        IdentityDirectory directory,
        SessionStore sessions) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddApprovedDurableDataProtection();
                services.AddSingleton<IIdentityDirectory>(directory);
                services.AddSingleton<IAuthenticationSessionStore>(sessions);
                services.AddScoped<IDevelopmentFixtureSeeder>(_ => new SilentSeeder());
                services.AddSingleton<IStartupFilter, HarnessStartupFilter>();
            });
        });

    internal static HttpClient CreateClient(
        WebApplicationFactory<Program> host,
        bool followRedirects = true) =>
        host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = followRedirects,
            HandleCookies = true,
        });

    internal static async Task SignInAsync(HttpClient client)
    {
        using var response = await client.GetAsync(TestSignInPath);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    internal static string Canonical(Guid identifier) =>
        identifier.ToString("D", CultureInfo.InvariantCulture);

    internal static T Construct<T>(params object?[] arguments) =>
        (T)typeof(T)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(constructor => constructor.GetParameters().Length == arguments.Length)
            .Invoke(arguments);

    internal sealed class HarnessStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, proceed) =>
                {
                    if (context.Request.Path.Equals(TestSignInPath, StringComparison.Ordinal))
                    {
                        var identity = new ClaimsIdentity("TestExternalProvider");
                        identity.AddClaim(new Claim("iss", Issuer));
                        identity.AddClaim(new Claim("sub", Subject));

                        await context.SignInAsync(
                            CookieScheme,
                            new ClaimsPrincipal(identity));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                        return;
                    }

                    if (context.Request.Path.Equals(
                            FirmScopedTestPath,
                            StringComparison.Ordinal))
                    {
                        var authentication = await context.AuthenticateAsync(CookieScheme);
                        if (!authentication.Succeeded || authentication.Principal is null)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return;
                        }

                        context.User = authentication.Principal;

                        var authorization =
                            context.RequestServices.GetRequiredService<IAuthorizationService>();
                        var decision = await authorization.AuthorizeAsync(
                            context.User,
                            context,
                            "TaxAiFirmScoped");

                        if (!decision.Succeeded)
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "text/plain; charset=utf-8";
                            await context.Response.WriteAsync(FixedRefusalBody);
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                        return;
                    }

                    await proceed();
                });

                next(app);
            };
    }

    internal sealed class SilentSeeder : IDevelopmentFixtureSeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal sealed class IdentityDirectory : IIdentityDirectory
    {
        private static readonly DateTimeOffset Timestamp =
            new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly List<PortalUser> _users = new();

        private readonly List<FirmMembership> _memberships = new();

        private readonly List<Firm> _firms = new();

        internal DirectoryFailure UserByIdFailure { get; set; }

        internal DirectoryFailure MembershipFailure { get; set; }

        internal int UserByIdLookupCount { get; private set; }

        internal int ActiveMembershipLookupCount { get; private set; }

        internal (string Issuer, string Subject)? LastIssuerSubjectLookup { get; private set; }

        internal PortalUser AddUser(bool isActive = true)
        {
            var user = Construct<PortalUser>(
                Guid.NewGuid(),
                Issuer,
                Subject,
                isActive,
                Timestamp,
                Timestamp);
            _users.Add(user);
            return user;
        }

        internal FirmMembership AddMembership(
            Guid userId,
            string displayName,
            bool isActive = true,
            Guid? firmId = null,
            Guid? membershipId = null)
        {
            var selectedFirmId = firmId ?? Guid.NewGuid();
            if (_firms.All(firm => firm.FirmId != selectedFirmId))
            {
                _firms.Add(Construct<Firm>(
                    selectedFirmId,
                    displayName,
                    Timestamp,
                    Timestamp));
            }

            var membership = Construct<FirmMembership>(
                membershipId ?? Guid.NewGuid(),
                selectedFirmId,
                userId,
                isActive,
                Timestamp,
                Timestamp);
            _memberships.Add(membership);
            return membership;
        }

        internal void SetUserActive(Guid userId, bool isActive)
        {
            var index = _users.FindIndex(user => user.UserId == userId);
            var current = _users[index];
            _users[index] = Construct<PortalUser>(
                current.UserId,
                current.Issuer,
                current.Subject,
                isActive,
                current.CreatedAtUtc,
                Timestamp);
        }

        internal void SetMembershipActive(Guid membershipId, bool isActive)
        {
            var index = _memberships.FindIndex(
                membership => membership.MembershipId == membershipId);
            var current = _memberships[index];
            _memberships[index] = Construct<FirmMembership>(
                current.MembershipId,
                current.FirmId,
                current.UserId,
                isActive,
                current.CreatedAtUtc,
                Timestamp);
        }

        internal void RemoveMembership(Guid membershipId) =>
            _memberships.RemoveAll(
                membership => membership.MembershipId == membershipId);

        internal void ResetCounters()
        {
            UserByIdLookupCount = 0;
            ActiveMembershipLookupCount = 0;
            LastIssuerSubjectLookup = null;
        }

        public Task<PortalUser?> FindUserAsync(
            string issuer,
            string subject,
            CancellationToken cancellationToken)
        {
            LastIssuerSubjectLookup = (issuer, subject);
            return Task.FromResult(_users.SingleOrDefault(user =>
                string.Equals(user.Issuer, issuer, StringComparison.Ordinal) &&
                string.Equals(user.Subject, subject, StringComparison.Ordinal)));
        }

        public Task<PortalUser?> FindUserByIdAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            UserByIdLookupCount++;
            return FailureOr(
                UserByIdFailure,
                _users.SingleOrDefault(user => user.UserId == userId));
        }

        public Task<IReadOnlyList<FirmMembership>> FindActiveMembershipsAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            ActiveMembershipLookupCount++;
            return FailureOr<IReadOnlyList<FirmMembership>>(
                MembershipFailure,
                _memberships
                    .Where(membership =>
                        membership.UserId == userId &&
                        membership.IsActive)
                    .ToArray());
        }

        public Task<FirmMembership?> FindMembershipAsync(
            Guid userId,
            Guid membershipId,
            CancellationToken cancellationToken) =>
            FailureOr(
                MembershipFailure,
                _memberships.SingleOrDefault(membership =>
                    membership.UserId == userId &&
                    membership.MembershipId == membershipId));

        public Task<IReadOnlyList<Firm>> FindFirmsAsync(
            IReadOnlyList<Guid> firmIds,
            CancellationToken cancellationToken) =>
            FailureOr<IReadOnlyList<Firm>>(
                MembershipFailure,
                _firms.Where(firm => firmIds.Contains(firm.FirmId)).ToArray());

        private static Task<T> FailureOr<T>(DirectoryFailure failure, T value) =>
            failure switch
            {
                DirectoryFailure.Throw =>
                    Task.FromException<T>(new InvalidOperationException("Unavailable.")),
                DirectoryFailure.Timeout =>
                    Task.FromException<T>(new TaskCanceledException("Timed out.")),
                _ => Task.FromResult(value),
            };
    }

    internal sealed class SessionStore : IAuthenticationSessionStore
    {
        private readonly ConcurrentDictionary<Guid, AuthenticationSession> _sessions = new();

        internal IReadOnlyList<AuthenticationSession> All =>
            _sessions.Values.OrderBy(session => session.CreatedAtUtc).ToArray();

        public Task<AuthenticationSession> CreateAsync(
            Guid userId,
            Guid? activeFirmId,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken)
        {
            var created = AuthenticationSession.Create(
                Guid.NewGuid(),
                userId,
                activeFirmId,
                createdAtUtc);
            Assert.True(created.IsSuccess);
            _sessions[created.Value!.SessionId] = created.Value;
            return Task.FromResult(created.Value);
        }

        public Task<AuthenticationSession?> FindAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _sessions.TryGetValue(sessionId, out var session) &&
                !session.RevokedAtUtc.HasValue
                    ? session
                    : null);

        public Task<AuthenticationSession?> TouchAsync(
            AuthenticationSession session,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken)
        {
            var boundaries = SessionLifetimePolicy.Touch(session, atUtc);
            if (boundaries.IsFailure)
            {
                return Task.FromResult<AuthenticationSession?>(null);
            }

            var touched = Construct<AuthenticationSession>(
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
            _sessions[session.SessionId] = Construct<AuthenticationSession>(
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
    }
}
