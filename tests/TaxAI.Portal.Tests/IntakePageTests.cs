using System.Net;
using System.Text.RegularExpressions;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxAI.Portal.Tests.Identity;
using TaxCases.Models;
using Tenancy.Models;

namespace TaxAI.Portal.Tests;

public sealed class IntakePageTests
{
    [Fact]
    public async Task CreateUsesR1ScopeAndRendersTheRawLinkOnce()
    {
        var accountant = new IntakePortalHarness.AccountantWorkflow();
        var created = new CreatedIntakeCase(
            Guid.NewGuid(),
            IntakePortalHarness.Secret(0x61),
            IntakePortalHarness.Now.AddHours(1));
        accountant.CreateResult = IntakeResult<CreatedIntakeCase>.Success(created);

        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient(followRedirects: false);
        await fixture.SignInAsync(client);

        using var formPage = await client.GetAsync("/intake-cases/new");
        Assert.Equal(HttpStatusCode.OK, formPage.StatusCode);
        var formHtml = await formPage.Content.ReadAsStringAsync();
        var token = IntakePortalHarness.AntiforgeryToken(formHtml);

        using var response = await client.PostAsync(
            "/intake-cases",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["primaryTaxpayerDisplayName"] = "Synthetic Client",
                    ["taxYear"] = "2025",
                    ["expectedT4Count"] = "1",
                    ["expectedMedicalExpenseCount"] = "0",
                }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var expectedUrl =
            $"https://intake.test.invalid/client-intake/link/{created.LinkCredential.Reveal()}";
        Assert.Single(
            Regex.Matches(html, Regex.Escape(expectedUrl)).Cast<Match>());
        Assert.Contains($"/intake-cases/{created.CaseId:D}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic Client", html, StringComparison.Ordinal);

        var call = Assert.Single(accountant.CreateCalls);
        Assert.Equal(fixture.Membership.FirmId, call.Firm.Value);
        Assert.Equal(fixture.User.UserId, call.Actor);
        Assert.InRange(
            call.Now,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal("Synthetic Client", call.Command.PrimaryTaxpayerDisplayName);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ProgressRendersProjectionAndNeverRecoversTheRawLink()
    {
        var caseId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var accountant = new IntakePortalHarness.AccountantWorkflow
        {
            ProgressResult = IntakeResult<AccountantIntakeView>.Success(
                IntakePortalHarness.AccountantView(
                    ReviewHandoffState.Available,
                    recordId)),
        };

        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient();
        await fixture.SignInAsync(client);

        using var response = await client.GetAsync($"/intake-cases/{caseId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Synthetic Client", html, StringComparison.Ordinal);
        Assert.Contains("1 of 2 answered", html, StringComparison.Ordinal);
        Assert.Contains("Version 3", html, StringComparison.Ordinal);
        Assert.Contains("T4 1", html, StringComparison.Ordinal);
        Assert.Contains("Ready", html, StringComparison.Ordinal);
        Assert.Contains($"/reviews/{recordId:D}", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            IntakePortalHarness.Secret(0x61).Reveal(),
            html,
            StringComparison.Ordinal);

        var call = Assert.Single(accountant.ProgressCalls);
        Assert.Equal(fixture.Membership.FirmId, call.Firm.Value);
        Assert.Equal(caseId, call.CaseId);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task MissingAndForeignAccountantCasesUseTheSameEmpty404()
    {
        var accountant = new IntakePortalHarness.AccountantWorkflow
        {
            ProgressResult =
                IntakeResult<AccountantIntakeView>.Failure(
                    IntakeResultCode.NotFound),
        };
        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient();
        await fixture.SignInAsync(client);

        using var first = await client.GetAsync(
            $"/intake-cases/{Guid.NewGuid():D}");
        using var second = await client.GetAsync(
            $"/intake-cases/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(string.Empty, await first.Content.ReadAsStringAsync());
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await second.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.ToString(), second.Headers.ToString());
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(
            "no-referrer",
            Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "nosniff",
            Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains(
            "default-src 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
    }
}

internal sealed class IntakePortalHarness : IDisposable
{
    private static readonly Regex Antiforgery = new(
        "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    internal static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private readonly WebApplicationFactory<Program> _host;

    private IntakePortalHarness(
        WebApplicationFactory<Program> host,
        PortalAuthorizationHarness.IdentityDirectory directory,
        PortalAuthorizationHarness.SessionStore sessions,
        PortalUser user,
        FirmMembership membership,
        AccountantWorkflow accountant,
        ClientWorkflow client)
    {
        _host = host;
        Directory = directory;
        Sessions = sessions;
        User = user;
        Membership = membership;
        Accountant = accountant;
        Client = client;
    }

    internal PortalAuthorizationHarness.IdentityDirectory Directory { get; }
    internal PortalAuthorizationHarness.SessionStore Sessions { get; }
    internal PortalUser User { get; }
    internal FirmMembership Membership { get; }
    internal AccountantWorkflow Accountant { get; }
    internal ClientWorkflow Client { get; }

    internal static IntakePortalHarness Create(
        AccountantWorkflow? accountant = null,
        ClientWorkflow? client = null,
        Action<IWebHostBuilder>? configure = null)
    {
        accountant ??= new AccountantWorkflow();
        client ??= new ClientWorkflow();
        var directory = new PortalAuthorizationHarness.IdentityDirectory();
        var sessions = new PortalAuthorizationHarness.SessionStore();
        var user = directory.AddUser();
        var membership = directory.AddMembership(
            user.UserId,
            "Synthetic Firm");

        var host = PortalAuthorizationHarness
            .CreateHost(directory, sessions)
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAccountantIntakeWorkflow>();
                    services.RemoveAll<IClientIntakeWorkflow>();
                    services.AddSingleton<IAccountantIntakeWorkflow>(accountant);
                    services.AddSingleton<IClientIntakeWorkflow>(client);
                });
                configure?.Invoke(builder);
            });

        return new IntakePortalHarness(
            host,
            directory,
            sessions,
            user,
            membership,
            accountant,
            client);
    }

    internal HttpClient CreateAccountantClient(bool followRedirects = true) =>
        PortalAuthorizationHarness.CreateClient(_host, followRedirects);

    internal HttpClient CreateClient(bool followRedirects = true) =>
        PortalAuthorizationHarness.CreateClient(_host, followRedirects);

    internal Task SignInAsync(HttpClient client) =>
        PortalAuthorizationHarness.SignInAsync(client);

    internal static string AntiforgeryToken(string html)
    {
        var match = Antiforgery.Match(html);
        Assert.True(match.Success, "Expected a rendered antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    internal static IntakeSecret Secret(byte fill)
    {
        var encoded = Convert.ToBase64String(
                Enumerable.Repeat(fill, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        Assert.True(IntakeSecret.TryParse(encoded, out var secret));
        return secret!;
    }

    internal static ClientIntakeView ClientView(
        ExpectedDocumentStatus status = ExpectedDocumentStatus.Requested) =>
        new(
            2025,
            new[]
            {
                new IntakeQuestionView(
                    DocumentCategory.T4Slip,
                    "Are the requested T4 documents available now?",
                    new[]
                    {
                        AvailabilityAnswerCode.AvailableNow,
                        AvailabilityAnswerCode.WillProvideLater,
                        AvailabilityAnswerCode.Unavailable,
                    },
                    AvailabilityAnswerCode.AvailableNow),
            },
            new[]
            {
                new ExpectedDocumentView(
                    DocumentCategory.T4Slip,
                    1,
                    status),
            },
            2,
            true);

    internal static AccountantIntakeView AccountantView(
        ReviewHandoffState handoff = ReviewHandoffState.NotReady,
        Guid? reviewRecordId = null) =>
        new(
            "Synthetic Client",
            2025,
            CaseStatus.InformationGathering,
            handoff == ReviewHandoffState.Available
                ? ReadinessState.Ready
                : ReadinessState.NotReady,
            1,
            1,
            new[]
            {
                new ExpectedDocumentView(
                    DocumentCategory.T4Slip,
                    1,
                    ExpectedDocumentStatus.Ready),
                new ExpectedDocumentView(
                    DocumentCategory.MedicalExpenseEvidence,
                    1,
                    ExpectedDocumentStatus.Missing),
            },
            1,
            2,
            3,
            true,
            IntakeLinkState.Spent,
            Now.AddMinutes(-20),
            Now.AddHours(1),
            null,
            Now.AddMinutes(-15),
            handoff,
            reviewRecordId);

    public void Dispose() => _host.Dispose();

    internal sealed class AccountantWorkflow : IAccountantIntakeWorkflow
    {
        internal IntakeResult<CreatedIntakeCase> CreateResult { get; set; } =
            IntakeResult<CreatedIntakeCase>.Failure(IntakeResultCode.Invalid);

        internal IntakeResult<AccountantIntakeView> ProgressResult { get; set; } =
            IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<IssuedIntakeLink> ReissueResult { get; set; } =
            IntakeResult<IssuedIntakeLink>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<AccountantIntakeView> RevokeResult { get; set; } =
            IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<AccountantIntakeView> AssociationResult { get; set; } =
            IntakeResult<AccountantIntakeView>.Failure(IntakeResultCode.NotFound);

        internal List<(TrustedFirmId Firm, Guid Actor, CreateIntakeCaseCommand Command, DateTimeOffset Now)>
            CreateCalls { get; } = new();

        internal List<(TrustedFirmId Firm, Guid CaseId, DateTimeOffset Now)>
            ProgressCalls { get; } = new();

        internal List<Guid> ReissueCases { get; } = new();
        internal List<Guid> RevokeCases { get; } = new();
        internal List<(Guid CaseId, Guid RecordId)> AssociationCalls { get; } = new();

        public Task<IntakeResult<CreatedIntakeCase>> CreateAsync(
            TrustedFirmId firm,
            Guid accountantActorReference,
            CreateIntakeCaseCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            CreateCalls.Add(
                (firm, accountantActorReference, command, nowUtc));
            return Task.FromResult(CreateResult);
        }

        public Task<IntakeResult<AccountantIntakeView>> GetProgressAsync(
            TrustedFirmId firm,
            Guid caseId,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            ProgressCalls.Add((firm, caseId, nowUtc));
            return Task.FromResult(ProgressResult);
        }

        public Task<IntakeResult<IssuedIntakeLink>> ReissueLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid accountantActorReference,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            ReissueCases.Add(caseId);
            return Task.FromResult(ReissueResult);
        }

        public Task<IntakeResult<AccountantIntakeView>> RevokeLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid accountantActorReference,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            RevokeCases.Add(caseId);
            return Task.FromResult(RevokeResult);
        }

        public Task<IntakeResult<AccountantIntakeView>> AssociateReviewAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid recordId,
            Guid accountantActorReference,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            AssociationCalls.Add((caseId, recordId));
            return Task.FromResult(AssociationResult);
        }
    }

    internal sealed class ClientWorkflow : IClientIntakeWorkflow
    {
        internal IntakeResult<IntakeLinkInspection> InspectionResult { get; set; } =
            IntakeResult<IntakeLinkInspection>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<RedeemedIntakeSession> RedemptionResult { get; set; } =
            IntakeResult<RedeemedIntakeSession>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<ClientIntakeView> GetResult { get; set; } =
            IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<ClientIntakeView> AnswersResult { get; set; } =
            IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<ClientIntakeView> UploadResult { get; set; } =
            IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);

        internal IntakeResult<ClientIntakeView> SubmissionResult { get; set; } =
            IntakeResult<ClientIntakeView>.Failure(IntakeResultCode.NotFound);

        internal List<IntakeSecret> InspectionCalls { get; } = new();
        internal List<IntakeRedemptionProof> RedemptionCalls { get; } = new();
        internal List<IntakeSecret> GetCalls { get; } = new();
        internal List<SaveIntakeAnswersCommand> AnswerCalls { get; } = new();
        internal List<(IntakeUploadCommand Command, byte[] Content)> UploadCalls { get; } = new();
        internal int SubmissionCalls { get; private set; }

        public Task<IntakeResult<IntakeLinkInspection>> InspectLinkAsync(
            IntakeSecret credential,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            InspectionCalls.Add(credential);
            return Task.FromResult(InspectionResult);
        }

        public Task<IntakeResult<RedeemedIntakeSession>> RedeemLinkAsync(
            IntakeRedemptionProof proof,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            RedemptionCalls.Add(proof);
            return Task.FromResult(RedemptionResult);
        }

        public Task<IntakeResult<ClientIntakeView>> GetAsync(
            IntakeSecret sessionCredential,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            GetCalls.Add(sessionCredential);
            return Task.FromResult(GetResult);
        }

        public Task<IntakeResult<ClientIntakeView>> SaveAnswersAsync(
            IntakeSecret sessionCredential,
            SaveIntakeAnswersCommand command,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            AnswerCalls.Add(command);
            return Task.FromResult(AnswersResult);
        }

        public async Task<IntakeResult<ClientIntakeView>> UploadAsync(
            IntakeSecret sessionCredential,
            IntakeUploadCommand command,
            Stream content,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            UploadCalls.Add((command, buffer.ToArray()));
            return UploadResult;
        }

        public Task<IntakeResult<ClientIntakeView>> SubmitAsync(
            IntakeSecret sessionCredential,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            SubmissionCalls++;
            return Task.FromResult(SubmissionResult);
        }
    }

}
