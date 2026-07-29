using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Tenancy.Models;
using Tenancy.Services;
using static TaxAI.Portal.Tests.Identity.SignInTests;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// T18 — proves the two boundaries are enforced from the <b>session record</b>,
/// not from the cookie's own expiry.
/// <para>
/// A cookie's expiry is a value the client holds. A client that keeps presenting
/// an expired one must still be refused, and the only way to prove that is to
/// present one and watch the server refuse it.
/// </para>
/// <para>
/// The sessions here are planted with boundaries the test chooses rather than
/// waited for. Thirty minutes and eight hours are real durations; a suite that
/// waited them out would take eight and a half hours and would still be proving
/// less, because a planted boundary can be placed exactly on the edge.
/// </para>
/// </summary>
public sealed class SessionLifetimeTests
{
    // ----------------------------------------------------------------- AC-67

    [Fact] // AC-67
    public async Task AnIdleExpiredSessionIsRefusedAndRebuildsNoPrincipal()
    {
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;

        // Idle boundary passed; absolute boundary still far away. Only the idle
        // rule can be refusing this.
        var session = sessions.Seed(
            userId: Guid.NewGuid(),
            activeFirmId: Guid.NewGuid(),
            createdAtUtc: now.AddHours(-1),
            lastSeenAtUtc: now.AddMinutes(-31),
            idleExpiresAtUtc: now.AddMinutes(-1),
            absoluteExpiresAtUtc: now.AddHours(7));

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        Assert.Null(await PortalIdentityHarness.RetrieveTicketAsync(host, session.SessionId));
    }

    [Fact] // AC-67
    public async Task TheIdleBoundaryIsReachedNotMerelyPassed()
    {
        // Exclusive: a request arriving exactly on the boundary is refused. An
        // inclusive comparison would leave a session usable for one tick past
        // the moment it was declared over.
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;

        var session = sessions.Seed(
            userId: Guid.NewGuid(),
            activeFirmId: null,
            createdAtUtc: now.AddMinutes(-30),
            lastSeenAtUtc: now.AddMinutes(-30),
            idleExpiresAtUtc: now,
            absoluteExpiresAtUtc: now.AddHours(7));

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        Assert.False(SessionLifetimePolicy.Evaluate(session, session.IdleExpiresAtUtc).IsUsable);
        Assert.Null(await PortalIdentityHarness.RetrieveTicketAsync(host, session.SessionId));
    }

    // ----------------------------------------------------------------- AC-68

    [Fact] // AC-68
    public async Task AnAbsoluteExpiredSessionIsRefusedEvenWhileItsIdleWindowIsOpen()
    {
        // The case sliding expiration would otherwise hide: continuous activity
        // keeps the idle boundary ahead of now forever. If the absolute maximum
        // were not enforced independently, this session would never require
        // reauthentication.
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;

        var session = sessions.Seed(
            userId: Guid.NewGuid(),
            activeFirmId: Guid.NewGuid(),
            createdAtUtc: now.AddHours(-9),
            lastSeenAtUtc: now.AddMinutes(-1),
            idleExpiresAtUtc: now.AddMinutes(29),
            absoluteExpiresAtUtc: now.AddMinutes(-1));

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        Assert.Null(await PortalIdentityHarness.RetrieveTicketAsync(host, session.SessionId));
    }

    [Fact] // AC-68
    public void ActivityNeverMovesTheAbsoluteBoundary()
    {
        // Proved against the store the host actually calls: whatever instant is
        // handed to a renewal, the absolute boundary that comes back is the one
        // the session was created with.
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;

        var session = sessions.Seed(
            userId: Guid.NewGuid(),
            activeFirmId: null,
            createdAtUtc: now.AddHours(-1),
            lastSeenAtUtc: now.AddMinutes(-20),
            idleExpiresAtUtc: now.AddMinutes(10),
            absoluteExpiresAtUtc: now.AddHours(7));

        var absolute = session.AbsoluteExpiresAtUtc;

        for (var step = 0; step < 5; step++)
        {
            var touched = sessions
                .TouchAsync(sessions.Find(session.SessionId)!, DateTimeOffset.UtcNow, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.NotNull(touched);
            Assert.Equal(absolute, touched!.AbsoluteExpiresAtUtc);
            Assert.True(touched.IdleExpiresAtUtc <= absolute);
        }
    }

    // -------------------------------------------- renewal reaches the real store

    [Fact]
    public async Task ARequestPastTheHalfwayPointRenewsTheSessionThroughTheStore()
    {
        // The cookie handler renews once activity has consumed more of the window
        // than remains. This proves the renewal lands on the durable session
        // rather than only on the cookie — which is what makes "advance the idle
        // boundary" a server-side fact.
        var directory = new FakeIdentityDirectory();
        var sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddActiveMembership(user.UserId);

        using var host = PortalIdentityHarness.CreateHost(directory, sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        var created = Assert.Single(sessions.All);
        var now = DateTimeOffset.UtcNow;

        // Replace the row with one whose window is mostly spent, so the very
        // next authenticated request crosses the handler's renewal threshold.
        var stale = sessions.Seed(
            user.UserId,
            created.ActiveFirmId,
            created.CreatedAtUtc,
            lastSeenAtUtc: now.AddMinutes(-25),
            idleExpiresAtUtc: now.AddMinutes(5),
            absoluteExpiresAtUtc: created.AbsoluteExpiresAtUtc);

        var before = sessions.TouchCount;

        var ticket = await PortalIdentityHarness.RetrieveTicketAsync(host, stale.SessionId);

        // Compared to whole seconds: authentication properties are carried as
        // RFC 1123 strings, so a sub-second component does not survive the
        // round trip. That is the framework's wire format, not a rounding
        // decision this repository gets to make — and the boundary the server
        // enforces is the record's, which keeps its full precision.
        Assert.NotNull(ticket);
        Assert.Equal(ToWholeSeconds(stale.LastSeenAtUtc), ticket!.Properties.IssuedUtc);
        Assert.Equal(ToWholeSeconds(stale.IdleExpiresAtUtc), ticket.Properties.ExpiresUtc);

        // And a renewal against that key really does reach TouchAsync.
        await PortalIdentityHarness
            .TicketStore(host)
            .RenewAsync(PortalIdentityHarness.Canonical(stale.SessionId), ticket);

        Assert.Equal(before + 1, sessions.TouchCount);
        Assert.Equal(
            stale.AbsoluteExpiresAtUtc,
            sessions.Find(stale.SessionId)!.AbsoluteExpiresAtUtc);
    }

    [Fact]
    public async Task RenewingAnUnknownKeyCreatesNothing()
    {
        // A renewal of something that no longer exists is a creation, and a
        // creation here would resurrect a session that was revoked a moment ago.
        var sessions = new RecordingSessionStore();

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness
            .TicketStore(host)
            .RenewAsync(
                PortalIdentityHarness.Canonical(Guid.NewGuid()),
                EmptyTicket());

        Assert.Empty(sessions.All);
        Assert.Equal(0, sessions.TouchCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-key")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{11111111-1111-1111-1111-111111111111}")]
    public async Task AMalformedKeyResolvesNothingAndCreatesNothing(string key)
    {
        // The braced form is deliberately in the list: it round-trips through
        // Guid.Parse but is not the canonical form the record is keyed by, and
        // accepting a second spelling of an identifier compared ordinally is how
        // two spellings of one session appear.
        var sessions = new RecordingSessionStore();

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var store = PortalIdentityHarness.TicketStore(host);

        Assert.Null(await store.RetrieveAsync(key));

        await store.RenewAsync(key, EmptyTicket());
        await store.RemoveAsync(key);

        Assert.Empty(sessions.All);
    }

    private static DateTimeOffset ToWholeSeconds(DateTimeOffset instant) =>
        new(
            instant.Year, instant.Month, instant.Day,
            instant.Hour, instant.Minute, instant.Second,
            instant.Offset);

    private static AuthenticationTicket EmptyTicket() =>
        new(
            new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()),
            new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow },
            PortalIdentityHarness.CookieScheme);
}
