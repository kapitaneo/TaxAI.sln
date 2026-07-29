using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Tenancy.Services;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// T19 — proves firm selection accepts only an opaque membership selector,
/// revokes the old session, and issues the selected firm through T18's existing
/// cookie/session flow.
/// </summary>
public sealed class FirmSelectionTests
{
    private const string ProtectionPurpose =
        "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware";

    private const string TicketKeyClaimType =
        "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    private static readonly Regex AntiforgeryToken = new(
        "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact] // AC-37
    public async Task AValidSelectionRevokesTheOldSessionAndIssuesANewSelectedSession()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var first = directory.AddMembership(user.UserId, "Example Firm One");
        var selected = directory.AddMembership(user.UserId, "Example Firm Two");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);

        var oldSession = Assert.Single(sessions.All);
        Assert.Null(oldSession.ActiveFirmId);

        var token = await ReadAntiforgeryTokenAsync(client);
        using var response = await SelectAsync(client, selected.MembershipId, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var rows = sessions.All;
        Assert.Equal(2, rows.Count);

        var revoked = Assert.Single(rows.Where(row => row.SessionId == oldSession.SessionId));
        var replacement = Assert.Single(rows.Where(row => row.SessionId != oldSession.SessionId));

        Assert.NotNull(revoked.RevokedAtUtc);
        Assert.Equal(user.UserId, replacement.UserId);
        Assert.Equal(selected.FirmId, replacement.ActiveFirmId);
        Assert.NotEqual(oldSession.SessionId, replacement.SessionId);
        Assert.Equal(replacement.CreatedAtUtc, replacement.LastSeenAtUtc);
        Assert.Equal(
            replacement.CreatedAtUtc.Add(SessionLifetimePolicy.AbsoluteLifetime),
            replacement.AbsoluteExpiresAtUtc);
        Assert.NotEqual(first.FirmId, replacement.ActiveFirmId);

        var setCookies = response.Headers
            .Where(header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value)
            .Where(value => value.StartsWith("taxai.session=", StringComparison.Ordinal))
            .ToArray();
        Assert.True(setCookies.Length >= 2);

        var protectedValue = SetCookieHeaderValue.Parse(setCookies[^1]).Value.ToString();
        var protector = host.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(ProtectionPurpose, "TaxAiSession", "v2");
        var cookieTicket = new TicketDataFormat(protector).Unprotect(protectedValue);
        var cookieClaim = Assert.Single(cookieTicket!.Principal.Claims);

        Assert.Equal(TicketKeyClaimType, cookieClaim.Type);
        Assert.Equal(
            PortalAuthorizationHarness.Canonical(replacement.SessionId),
            cookieClaim.Value);

        foreach (var forbidden in new[]
                 {
                     PortalAuthorizationHarness.Canonical(replacement.UserId),
                     PortalAuthorizationHarness.Canonical(replacement.ActiveFirmId!.Value),
                     PortalAuthorizationHarness.Issuer,
                     PortalAuthorizationHarness.Subject,
                 })
        {
            Assert.DoesNotContain(
                forbidden,
                protectedValue,
                StringComparison.OrdinalIgnoreCase);
        }

        using var firms = await client.GetAsync("/firms");
        Assert.Equal(HttpStatusCode.OK, firms.StatusCode);
    }

    [Fact] // AC-37
    public async Task SelectionRecoversIssuerAndSubjectFromTheLocalUserRow()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var selected = directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);
        directory.ResetCounters();

        var token = await ReadAntiforgeryTokenAsync(client);
        using var response = await SelectAsync(client, selected.MembershipId, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(directory.UserByIdLookupCount >= 2);
        Assert.True(directory.ActiveMembershipLookupCount >= 2);
        Assert.Equal(
            (PortalAuthorizationHarness.Issuer, PortalAuthorizationHarness.Subject),
            directory.LastIssuerSubjectLookup);
    }

    [Fact] // AC-60
    public async Task AForeignMembershipAndAMissingMembershipAreIndistinguishable()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        directory.AddMembership(user.UserId, "Example Firm One");

        var otherUserId = Guid.NewGuid();
        var foreign = directory.AddMembership(otherUserId, "Example Firm Two");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);
        var original = Assert.Single(sessions.All);

        var token = await ReadAntiforgeryTokenAsync(client);
        using var foreignResponse = await SelectAsync(
            client,
            foreign.MembershipId,
            token);

        token = await ReadAntiforgeryTokenAsync(client);
        using var missingResponse = await SelectAsync(
            client,
            Guid.NewGuid(),
            token);

        Assert.Equal(foreignResponse.StatusCode, missingResponse.StatusCode);
        Assert.Equal(
            await foreignResponse.Content.ReadAsByteArrayAsync(),
            await missingResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
        Assert.Single(sessions.All);
        Assert.Null(sessions.All.Single().RevokedAtUtc);
        Assert.Equal(original.SessionId, sessions.All.Single().SessionId);
    }

    [Fact] // AC-61
    public async Task ADisabledMembershipCannotCreateOrReplaceASession()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var active = directory.AddMembership(user.UserId, "Example Firm One");
        var disabled = directory.AddMembership(
            user.UserId,
            "Example Firm Two",
            isActive: false);

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);
        var original = Assert.Single(sessions.All);
        Assert.Equal(active.FirmId, original.ActiveFirmId);

        var token = await ReadAntiforgeryTokenAsync(client);
        using var response = await SelectAsync(client, disabled.MembershipId, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await response.Content.ReadAsStringAsync());
        Assert.Single(sessions.All);
        Assert.Null(sessions.All.Single().RevokedAtUtc);
    }

    [Fact] // AC-62, AC-130
    public async Task SelectionWithoutAntiforgeryIsRefusedBeforeSessionMutation()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        directory.AddMembership(user.UserId, "Example Firm One");
        var selected = directory.AddMembership(user.UserId, "Example Firm Two");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);
        var original = Assert.Single(sessions.All);

        using var response = await client.PostAsync(
            "/firms/select",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["membershipId"] = PortalAuthorizationHarness.Canonical(selected.MembershipId),
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(sessions.All);
        Assert.Equal(original.SessionId, sessions.All.Single().SessionId);
        Assert.Null(sessions.All.Single().RevokedAtUtc);
    }

    private static async Task<string> ReadAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/firms");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryToken.Match(body);

        Assert.True(match.Success, "The firms page must render an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> SelectAsync(
        HttpClient client,
        Guid membershipId,
        string token)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["membershipId"] = PortalAuthorizationHarness.Canonical(membershipId),
            ["__RequestVerificationToken"] = token,
        });
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-www-form-urlencoded");

        return client.PostAsync("/firms/select", content);
    }
}
