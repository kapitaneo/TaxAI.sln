using System.Net;
using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Identity;

/// <summary>
/// T19 — proves tenant identity cannot be supplied through an HTTP request and
/// that missing, foreign, disabled, and mismatched state share a safe refusal.
/// </summary>
public sealed class TenantIsolationAtTheHttpBoundaryTests
{
    private static readonly Regex AntiforgeryValue = new(
        @"(name=""__RequestVerificationToken"" value="")[^""]+("")",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact] // AC-56
    public async Task AQueryStringFirmIdentifierDoesNotChangeTheBootstrapResponse()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);

        using var baseline = await client.GetAsync("/firms");
        using var supplied = await client.GetAsync(
            $"/firms?firmId={PortalAuthorizationHarness.Canonical(Guid.NewGuid())}");

        Assert.Equal(baseline.StatusCode, supplied.StatusCode);
        Assert.Equal(
            WithoutAntiforgeryNonce(await baseline.Content.ReadAsStringAsync()),
            WithoutAntiforgeryNonce(await supplied.Content.ReadAsStringAsync()));
    }

    [Fact] // AC-57, AC-58
    public async Task HeaderAndCookieFirmIdentifiersDoNotChangeTheBootstrapResponse()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);

        using var baseline = await client.GetAsync("/firms");

        using var headerRequest = new HttpRequestMessage(HttpMethod.Get, "/firms");
        headerRequest.Headers.Add(
            "X-Firm-Id",
            PortalAuthorizationHarness.Canonical(Guid.NewGuid()));
        using var headerResponse = await client.SendAsync(headerRequest);

        using var cookieRequest = new HttpRequestMessage(HttpMethod.Get, "/firms");
        cookieRequest.Headers.Add(
            "Cookie",
            $"firmId={PortalAuthorizationHarness.Canonical(Guid.NewGuid())}");
        using var cookieResponse = await client.SendAsync(cookieRequest);

        var expected = WithoutAntiforgeryNonce(
            await baseline.Content.ReadAsStringAsync());
        Assert.Equal(baseline.StatusCode, headerResponse.StatusCode);
        Assert.Equal(baseline.StatusCode, cookieResponse.StatusCode);
        Assert.Equal(
            expected,
            WithoutAntiforgeryNonce(
                await headerResponse.Content.ReadAsStringAsync()));
        Assert.Equal(
            expected,
            WithoutAntiforgeryNonce(
                await cookieResponse.Content.ReadAsStringAsync()));
    }

    [Fact] // AC-158, AC-159
    public async Task MissingAndMismatchedMembershipsUseTheSameFirmScopedRefusal()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var membership = directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(host);

        await PortalAuthorizationHarness.SignInAsync(client);

        directory.RemoveMembership(membership.MembershipId);
        using var missing = await client.GetAsync(
            PortalAuthorizationHarness.FirmScopedTestPath);

        directory.AddMembership(
            user.UserId,
            "Different Example Firm",
            firmId: Guid.NewGuid(),
            membershipId: membership.MembershipId);
        using var mismatched = await client.GetAsync(
            PortalAuthorizationHarness.FirmScopedTestPath);

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(missing.StatusCode, mismatched.StatusCode);
        Assert.Equal(
            await missing.Content.ReadAsByteArrayAsync(),
            await mismatched.Content.ReadAsByteArrayAsync());
        Assert.Equal(
            PortalAuthorizationHarness.FixedRefusalBody,
            await missing.Content.ReadAsStringAsync());
    }

    [Fact] // AC-66
    public async Task AnUnauthenticatedFirmsRequestIsRefusedWithoutContent()
    {
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var membership = directory.AddMembership(user.UserId, "Example Firm");

        using var host = PortalAuthorizationHarness.CreateHost(directory, sessions);
        using var client = PortalAuthorizationHarness.CreateClient(
            host,
            followRedirects: false);

        using var response = await client.GetAsync("/firms");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("Example Firm", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            PortalAuthorizationHarness.Canonical(membership.MembershipId),
            body,
            StringComparison.Ordinal);
    }

    [Fact] // AC-59, AC-97
    public void ProductionReadsNoFirmIdentifierFromAnyRequestChannel()
    {
        var portalRoot = Path.Combine(FindRepositoryRoot(), "src", "TaxAI.Portal");
        var executable = string.Join(
            "\n",
            Directory.EnumerateFiles(portalRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .SelectMany(ExecutableLines));

        foreach (var pattern in new[]
                 {
                     @"Request\s*\.\s*Query",
                     @"Request\s*\.\s*Headers",
                     @"\bRouteData\b|\bGetRouteValue\b",
                     @"\[From(?:Query|Header|Route|Body)[^]]*firm",
                     @"\[FromForm[^]]*firm",
                 })
        {
            Assert.DoesNotMatch(
                new Regex(
                    pattern,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)),
                executable);
        }

        Assert.DoesNotMatch(
            new Regex(
                @"(?:MapGet|MapPost)\s*\(\s*""[^""]*\{[^""]*firm",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
    }

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string WithoutAntiforgeryNonce(string responseBody) =>
        AntiforgeryValue.Replace(responseBody, "$1<token>$2");

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
}
