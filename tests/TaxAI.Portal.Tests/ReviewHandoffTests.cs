using System.Net;
using ClientIntake.Models;

namespace TaxAI.Portal.Tests;

public sealed class ReviewHandoffTests
{
    [Theory]
    [InlineData(ReviewHandoffState.NotReady)]
    [InlineData(ReviewHandoffState.AwaitingReviewRecord)]
    [InlineData(ReviewHandoffState.Unavailable)]
    public async Task ReviewLinkIsAbsentForEveryNonAvailableState(
        ReviewHandoffState state)
    {
        var accountant = new IntakePortalHarness.AccountantWorkflow
        {
            ProgressResult = IntakeResult<AccountantIntakeView>.Success(
                IntakePortalHarness.AccountantView(
                    state,
                    Guid.NewGuid())),
        };
        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient();
        await fixture.SignInAsync(client);

        using var response = await client.GetAsync(
            $"/intake-cases/{Guid.NewGuid():D}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("/reviews/", html, StringComparison.Ordinal);
        if (state == ReviewHandoffState.AwaitingReviewRecord)
        {
            Assert.Contains(
                "awaiting an existing review record",
                html,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AssociationPostsOnlyTheExactSubmittedRecord()
    {
        var caseId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var available = IntakePortalHarness.AccountantView(
            ReviewHandoffState.Available,
            recordId);
        var accountant = new IntakePortalHarness.AccountantWorkflow
        {
            ProgressResult = IntakeResult<AccountantIntakeView>.Success(
                IntakePortalHarness.AccountantView(
                    ReviewHandoffState.AwaitingReviewRecord)),
            AssociationResult = IntakeResult<AccountantIntakeView>.Success(
                available),
        };
        using var fixture = IntakePortalHarness.Create(accountant: accountant);
        using var client = fixture.CreateAccountantClient();
        await fixture.SignInAsync(client);

        using var progress = await client.GetAsync(
            $"/intake-cases/{caseId:D}");
        var token = IntakePortalHarness.AntiforgeryToken(
            await progress.Content.ReadAsStringAsync());
        using var associated = await client.PostAsync(
            $"/intake-cases/{caseId:D}/review-association",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["recordId"] = recordId.ToString("D"),
                }));

        Assert.Equal(HttpStatusCode.OK, associated.StatusCode);
        Assert.Equal(
            (caseId, recordId),
            Assert.Single(accountant.AssociationCalls));
        Assert.Contains(
            $"/reviews/{recordId:D}",
            await associated.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }
}
