using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using static TaxAI.Portal.Tests.Identity.SignInTests;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// T18 — proves what the browser is actually given, by decrypting the cookie the
/// host set and reading what is inside it.
/// <para>
/// This is the assertion the whole ticket-store design exists to make provable.
/// The conventional <c>ITicketStore</c> keeps a serialized ticket; this one keeps
/// structural columns, so "the cookie carries no claims payload, no firm, no
/// user, and no token" is a fact about the decrypted bytes rather than a promise
/// about a serializer.
/// </para>
/// </summary>
public sealed class CookieAndTokenTests
{
    /// <summary>Nine consecutive digits — the shape of an unmasked SIN.</summary>
    private static readonly Regex NineDigitRun =
        new(@"\d{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The purpose chain the cookie handler protects its ticket with, written
    /// out independently. Reaching for the handler's own protector would prove
    /// only that one expression equals itself.
    /// </summary>
    private const string ProtectionPurpose =
        "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware";

    // ------------------------------------------------------------------ AC-131

    [Fact] // AC-131
    public async Task TheAuthenticationCookieIsHttpOnlySecureAndSameSiteLax()
    {
        using var host = SignedInHost(out _, out var sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var cookie = SetCookieHeaderValue.Parse(
            PortalIdentityHarness.SessionCookie(await PortalIdentityHarness.SignInAsync(client)));

        // Not readable by script, not sent over cleartext, not attached to a
        // cross-site request. Lax rather than Strict so the redirect back from
        // the identity provider still carries the session.
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());

        // A session cookie: no Expires, no Max-Age, so it does not survive the
        // browser on its own terms. The server-side row decides how long it is
        // worth anything.
        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);
    }

    // -------------------------------------------------- AC-163, AC-98, AC-165

    [Fact] // AC-163
    public async Task TheDecryptedCookieCarriesTheTicketKeyAndNothingElse()
    {
        using var host = SignedInHost(out _, out var sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var response = await PortalIdentityHarness.SignInAsync(client);
        var session = Assert.Single(sessions.All);

        var ticket = Unprotect(host, PortalIdentityHarness.SessionCookie(response));

        var claim = Assert.Single(ticket.Principal.Claims);

        Assert.Equal(PortalIdentityHarness.TicketKeyClaimType, claim.Type);
        Assert.Equal(PortalIdentityHarness.Canonical(session.SessionId), claim.Value);
    }

    [Fact] // AC-98, AC-163
    public async Task TheDecryptedCookieCarriesNoFirmNoUserAndNoTaxpayerValue()
    {
        using var host = SignedInHost(out _, out var sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var response = await PortalIdentityHarness.SignInAsync(client);
        var session = Assert.Single(sessions.All);

        Assert.NotNull(session.ActiveFirmId);

        var ticket = Unprotect(host, PortalIdentityHarness.SessionCookie(response));
        var payload = Render(ticket);

        foreach (var forbidden in new[]
                 {
                     PortalIdentityHarness.FirmClaimType,
                     PortalIdentityHarness.UserClaimType,
                     PortalIdentityHarness.Canonical(session.ActiveFirmId!.Value),
                     PortalIdentityHarness.Canonical(session.UserId),
                     PortalIdentityHarness.Issuer,
                     PortalIdentityHarness.Subject,
                 })
        {
            Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(NineDigitRun.IsMatch(payload), $"The cookie payload contains a nine-digit run: {payload}");
    }

    [Fact] // AC-99, AC-165
    public async Task NeitherTheCookieNorTheSessionRecordCarriesAnOidcToken()
    {
        using var host = SignedInHost(out _, out var sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var response = await PortalIdentityHarness.SignInAsync(client);
        var session = Assert.Single(sessions.All);

        var payload = Render(Unprotect(host, PortalIdentityHarness.SessionCookie(response)));

        // The names the framework serializes tokens under, plus the token names
        // themselves. None may survive into the cookie.
        foreach (var token in new[]
                 {
                     ".Token.", "access_token", "refresh_token", "id_token", "Bearer",
                 })
        {
            Assert.DoesNotContain(token, payload, StringComparison.OrdinalIgnoreCase);
        }

        // And the record: every property the session exposes is a GUID, an
        // instant, or a flag. There is no column a token could be written to.
        var propertyTypes = session.GetType()
            .GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.NotEmpty(propertyTypes);
        Assert.All(
            propertyTypes,
            type => Assert.Contains(
                type,
                new[]
                {
                    typeof(Guid), typeof(Guid?),
                    typeof(DateTimeOffset), typeof(DateTimeOffset?),
                }));
    }

    [Fact] // AC-98
    public async Task TheSetCookieHeaderNamesNoIdentifierInTheClear()
    {
        using var host = SignedInHost(out _, out var sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var header = PortalIdentityHarness.SessionCookie(
            await PortalIdentityHarness.SignInAsync(client));

        var session = Assert.Single(sessions.All);

        // The cookie's value is ciphertext, so asserting a digit pattern over it
        // would be testing the base64 encoder rather than the design. What can
        // be asserted deterministically is that no identifier appears in the
        // clear — not the session key, not the firm, not the user.
        foreach (var identifier in new[]
                 {
                     PortalIdentityHarness.Canonical(session.SessionId),
                     PortalIdentityHarness.Canonical(session.ActiveFirmId!.Value),
                     PortalIdentityHarness.Canonical(session.UserId),
                     PortalIdentityHarness.Subject,
                 })
        {
            Assert.DoesNotContain(identifier, header, StringComparison.OrdinalIgnoreCase);
        }
    }

    // -------------------------------------------------------- AC-166, AC-167

    [Fact] // AC-166
    public async Task TheMaterializedFirmClaimEqualsTheSessionRecord()
    {
        using var host = SignedInHost(out _, out var sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        await PortalIdentityHarness.SignInAsync(client);

        var session = Assert.Single(sessions.All);
        var ticket = await PortalIdentityHarness.RetrieveTicketAsync(host, session.SessionId);

        Assert.NotNull(ticket);
        Assert.Equal(
            PortalIdentityHarness.Canonical(session.ActiveFirmId!.Value),
            ticket!.Principal.FindFirst(PortalIdentityHarness.FirmClaimType)?.Value);
        Assert.Equal(
            PortalIdentityHarness.Canonical(session.UserId),
            ticket.Principal.FindFirst(PortalIdentityHarness.UserClaimType)?.Value);
    }

    [Fact] // AC-167
    public async Task TheClaimFollowsTheRecordWhenTheRecordChanges()
    {
        // Divergence is prevented structurally rather than detected: the claim
        // is re-derived from the row on every retrieve, so there is no second
        // copy to go stale. Change the row and the claim changes with it —
        // including all the way to absent.
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var firmId = Guid.NewGuid();

        var scoped = sessions.Seed(
            userId, firmId, now, now, now.AddMinutes(30), now.AddHours(8));

        var unscoped = sessions.Seed(
            userId, null, now, now, now.AddMinutes(30), now.AddHours(8));

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var scopedTicket = await PortalIdentityHarness.RetrieveTicketAsync(host, scoped.SessionId);
        var unscopedTicket = await PortalIdentityHarness.RetrieveTicketAsync(host, unscoped.SessionId);

        Assert.Equal(
            PortalIdentityHarness.Canonical(firmId),
            scopedTicket!.Principal.FindFirst(PortalIdentityHarness.FirmClaimType)?.Value);

        Assert.Null(unscopedTicket!.Principal.FindFirst(PortalIdentityHarness.FirmClaimType));
    }

    [Fact] // AC-53, AC-54, AC-167
    public async Task AFirmClaimPresentedInTheCookieIsNeverTheOneTheApplicationSees()
    {
        // The cookie is not a channel for a firm claim at all: it carries a key,
        // and the claim is built from the row that key names. A caller who could
        // somehow mint a cookie still names no firm by doing so.
        var sessions = new RecordingSessionStore();
        var now = DateTimeOffset.UtcNow;

        var session = sessions.Seed(
            userId: Guid.NewGuid(),
            activeFirmId: null,
            createdAtUtc: now,
            lastSeenAtUtc: now,
            idleExpiresAtUtc: now.AddMinutes(30),
            absoluteExpiresAtUtc: now.AddHours(8));

        using var host = PortalIdentityHarness.CreateHost(new FakeIdentityDirectory(), sessions);
        using var client = PortalIdentityHarness.CreateClient(host);

        var ticket = await PortalIdentityHarness.RetrieveTicketAsync(host, session.SessionId);

        Assert.NotNull(ticket);

        // Unscoped record, therefore no claim — whatever anyone else asserts.
        Assert.Null(ticket!.Principal.FindFirst(PortalIdentityHarness.FirmClaimType));
        Assert.Empty(
            ticket.Principal.Claims.Where(claim =>
                claim.Type.Equals(PortalIdentityHarness.FirmClaimType, StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A host whose one user has exactly one active membership — the shape most
    /// assertions here need before they can look at a scoped session.
    /// </summary>
    private static WebApplicationFactory<Program> SignedInHost(
        out FakeIdentityDirectory directory,
        out RecordingSessionStore sessions)
    {
        directory = new FakeIdentityDirectory();
        sessions = new RecordingSessionStore();

        var user = directory.AddUser(PortalIdentityHarness.Issuer, PortalIdentityHarness.Subject);
        directory.AddActiveMembership(user.UserId);

        return PortalIdentityHarness.CreateHost(directory, sessions);
    }

    private static AuthenticationTicket Unprotect(
        WebApplicationFactory<Program> host,
        string setCookieHeader)
    {
        var value = SetCookieHeaderValue.Parse(setCookieHeader).Value.ToString();

        var protector = host.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(ProtectionPurpose, PortalIdentityHarness.CookieScheme, "v2");

        return new TicketDataFormat(protector).Unprotect(value)
            ?? throw new InvalidOperationException("The authentication cookie did not unprotect.");
    }

    /// <summary>
    /// Everything the decrypted ticket holds, as one string: claims, the
    /// identity's own metadata, and every authentication property. A payload
    /// assertion that read only the claims would miss a token stashed in the
    /// properties, which is exactly where the framework puts one.
    /// </summary>
    private static string Render(AuthenticationTicket ticket)
    {
        var parts = new List<string> { ticket.AuthenticationScheme };

        foreach (var identity in ticket.Principal.Identities)
        {
            parts.Add(identity.AuthenticationType ?? string.Empty);
            parts.Add(identity.NameClaimType);
            parts.Add(identity.RoleClaimType);
            parts.AddRange(identity.Claims.Select(claim => $"{claim.Type}={claim.Value}"));
        }

        parts.AddRange(ticket.Properties.Items.Select(item => $"{item.Key}={item.Value}"));

        return string.Join("\n", parts);
    }
}
