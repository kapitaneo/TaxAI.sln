using System.Net;
using ClientIntake.Models;

namespace TaxAI.Portal.Tests;

public sealed class IntakeAuthorizationTests
{
    [Fact]
    public async Task AnonymousAccountantRequestsNeverReachTheWorkflow()
    {
        var accountant = new IntakePortalHarness.AccountantWorkflow
        {
            ProgressResult = IntakeResult<AccountantIntakeView>.Success(
                IntakePortalHarness.AccountantView()),
        };
        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient(followRedirects: false);

        using var response = await client.GetAsync(
            $"/intake-cases/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(accountant.ProgressCalls);
    }

    [Fact]
    public async Task RevokedMembershipIsRefusedOnTheNextOperation()
    {
        var accountant = new IntakePortalHarness.AccountantWorkflow
        {
            ProgressResult = IntakeResult<AccountantIntakeView>.Success(
                IntakePortalHarness.AccountantView()),
        };
        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient(followRedirects: false);
        await fixture.SignInAsync(client);
        var path = $"/intake-cases/{Guid.NewGuid():D}";

        using var first = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Single(accountant.ProgressCalls);

        fixture.Directory.SetMembershipActive(
            fixture.Membership.MembershipId,
            isActive: false);
        using var refused = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Request refused.", await refused.Content.ReadAsStringAsync());
        Assert.Single(accountant.ProgressCalls);
    }

    [Fact]
    public async Task AccountantMutationWithoutAntiforgeryCreatesNothing()
    {
        var accountant = new IntakePortalHarness.AccountantWorkflow
        {
            CreateResult = IntakeResult<CreatedIntakeCase>.Success(
                new CreatedIntakeCase(
                    Guid.NewGuid(),
                    IntakePortalHarness.Secret(0x29),
                    IntakePortalHarness.Now.AddHours(1))),
        };
        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient(followRedirects: false);
        await fixture.SignInAsync(client);

        using var response = await client.PostAsync(
            "/intake-cases",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["primaryTaxpayerDisplayName"] = "Synthetic Client",
                    ["taxYear"] = "2025",
                    ["expectedT4Count"] = "1",
                    ["expectedMedicalExpenseCount"] = "0",
                }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(accountant.CreateCalls);
    }
}
