using System.Net;
using Microsoft.Net.Http.Headers;
using static TaxAI.Portal.Tests.Identity.SignInTests;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// T18 — proves sign-out is a fact in the session table rather than a cookie
/// that was cleared, and that an unavailable store fails closed without taking
/// the two constant routes down with it.
/// <para>
/// Clearing a browser cookie leaves a copied pre-logout cookie replayable. The
/// interesting test is therefore not "the response clears the cookie" — it is
/// "the cookie captured beforehand is worth nothing afterwards".
/// </para>
/// </summary>
public sealed class SessionRevocationTests
{
    // -------------------------------------------------- AC-69, AC-172, AC-174

    [Fact] // AC-172
    public async Task SignOutRevokesTheServerSideSessionAndClearsTheCookie()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        var session = Assert.Single(sessions.All);
        Assert.Null(session.RevokedAtUtc);

        using var signOut = await client.PostAsync("/sign-out", content: null);

        Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);

        // The security-bearing half.
        Assert.NotNull(sessions.Find(session.SessionId)!.RevokedAtUtc);

        // The convenience half: the response deletes the cookie.
        var deletion = PortalIdentityHarness.SessionCookie(signOut);

        Assert.Contains("expires=", deletion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // AC-69
    public async Task AfterSignOutTheClientIsUnauthenticated()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        using (var first = await client.PostAsync("/sign-out", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        // Same client, same cookie jar. The protected route now refuses.
        using var second = await client.PostAsync("/sign-out", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact] // AC-174
    public async Task ACopiedPreLogoutCookieCannotReuseARevokedSession()
    {
        // The whole reason revocation lives in a table. This client never signs
        // out; it simply replays a cookie captured before somebody else did.
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var signedIn = PortalIdentityHarness.CreateClient(host);

        var captured = ExtractCookiePair(
            PortalIdentityHarness.SessionCookie(await PortalIdentityHarness.SignInAsync(signedIn)));

        using (var signOut = await signedIn.PostAsync("/sign-out", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
        }

        // A second, independent client presenting the captured cookie — with no
        // cookie jar of its own, so the header under test is the only one sent.
        using var replay = PortalIdentityHarness.CreateClient(host, handleCookies: false);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/sign-out");
        request.Headers.Add("Cookie", captured);

        using var response = await replay.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] // AC-173, AC-175
    public async Task AnUnknownOrRevokedKeyResolvesNothingAndReCreatesNothing()
    {
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;

        var revoked = sessions.Seed(
            userId: Guid.NewGuid(),
            activeFirmId: Guid.NewGuid(),
            createdAtUtc: now.AddMinutes(-1),
            lastSeenAtUtc: now.AddMinutes(-1),
            idleExpiresAtUtc: now.AddMinutes(29),
            absoluteExpiresAtUtc: now.AddHours(7),
            revokedAtUtc: now);

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var store = PortalIdentityHarness.TicketStore(host);
        var unknown = Guid.NewGuid();

        Assert.Null(await store.RetrieveAsync(PortalIdentityHarness.Canonical(revoked.SessionId)));
        Assert.Null(await store.RetrieveAsync(PortalIdentityHarness.Canonical(unknown)));

        // Removing an unknown key is a no-op, not an insert.
        await store.RemoveAsync(PortalIdentityHarness.Canonical(unknown));

        Assert.Single(sessions.All);
        Assert.Null(sessions.Find(unknown));

        // And a revoked row stays revoked at the instant it was revoked.
        Assert.Equal(now, sessions.Find(revoked.SessionId)!.RevokedAtUtc);
    }

    // -------------------------------------------------------- AC-176, AC-177

    [Fact] // AC-176
    public async Task AnUnavailableStoreRefusesTheProtectedRouteAndEstablishesNoPrincipal()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        // The cookie is valid and the session row exists. Only the store's
        // availability changes.
        sessions.FailsEveryCall = true;

        using var response = await client.PostAsync("/sign-out", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // A refusal that explains itself is a refusal that describes the
        // database it could not reach.
        foreach (var leak in new[] { "unavailable", "Exception", "StackTrace", ".cs:line", ":\\" })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // AC-177
    public async Task AnUnavailableStoreDoesNotBreakTheConstantRoutes()
    {
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        sessions.FailsEveryCall = true;

        // Both are compile-time constants that reach storage never. They must
        // keep answering while everything behind them is down.
        using var health = await client.GetAsync("/health");
        using var shell = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        Assert.Contains(
            "Authenticated accountants can create client intakes, monitor progress, and review stored packages.",
            await shell.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact] // AC-176
    public async Task AnUnavailableStoreRebuildsNoTicket()
    {
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;

        var session = sessions.Seed(
            userId: Guid.NewGuid(),
            activeFirmId: Guid.NewGuid(),
            createdAtUtc: now,
            lastSeenAtUtc: now,
            idleExpiresAtUtc: now.AddMinutes(30),
            absoluteExpiresAtUtc: now.AddHours(8));

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var store = PortalIdentityHarness.TicketStore(host);
        var key = PortalIdentityHarness.Canonical(session.SessionId);

        // The positive control first: without it this could pass against a store
        // that resolves nothing at all.
        Assert.NotNull(await store.RetrieveAsync(key));

        sessions.FailsEveryCall = true;

        Assert.Null(await store.RetrieveAsync(key));
    }

    // ------------------------------------------------- the protected route only

    [Fact]
    public async Task SignOutWithNoSessionIsRefusedInPlace()
    {
        // Refused where it stands: a 401, not a redirect out to an identity
        // provider. A redirect would also make the framework read a return-url
        // query value, and this host reads no request input at all.
        var sessions = new RecordingSessionStore();

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host, followRedirects: false);

        using var response = await client.PostAsync("/sign-out", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(sessions.All);
    }

    [Fact]
    public async Task SignOutIsPostOnly()
    {
        var sessions = new RecordingSessionStore();

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        using var response = await client.GetAsync("/sign-out");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task SignInIsGetOnly()
    {
        var sessions = new RecordingSessionStore();

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        using var response = await client.PostAsync("/sign-in", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// The <c>name=value</c> pair from a <c>Set-Cookie</c> header — what a
    /// browser, or whoever copied one out of a browser, would send back.
    /// </summary>
    private static string ExtractCookiePair(string setCookieHeader) =>
        SetCookieHeaderValue.Parse(setCookieHeader) is { } cookie
            ? $"{cookie.Name}={cookie.Value}"
            : throw new InvalidOperationException("Unparseable Set-Cookie header.");
}
