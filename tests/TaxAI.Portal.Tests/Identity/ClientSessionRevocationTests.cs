using System.Net;
using ClientIntake.Models;

namespace TaxAI.Portal.Tests.Identity;

public sealed class ClientSessionRevocationTests
{
    [Fact]
    public async Task RevokedSessionIsRefusedOnTheNextReadAndPost()
    {
        var workflow = ClientIntakePageTests.SuccessfulClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);
        await ClientIntakePageTests.RedeemAsync(client);

        using var first = await client.GetAsync("/client-intake");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var token = IntakePortalHarness.AntiforgeryToken(
            await first.Content.ReadAsStringAsync());

        workflow.GetResult =
            IntakeResult<ClientIntakeView>.Failure(
                IntakeResultCode.NotFound);
        workflow.AnswersResult =
            IntakeResult<ClientIntakeView>.Failure(
                IntakeResultCode.NotFound);

        using var refusedRead = await client.GetAsync("/client-intake");
        using var refusedPost = await client.PostAsync(
            "/client-intake/answers",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["t4Answer"] =
                        nameof(AvailabilityAnswerCode.AvailableNow),
                }));

        Assert.Equal(HttpStatusCode.NotFound, refusedRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, refusedPost.StatusCode);
        Assert.Equal(string.Empty, await refusedRead.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await refusedPost.Content.ReadAsStringAsync());
    }
}
