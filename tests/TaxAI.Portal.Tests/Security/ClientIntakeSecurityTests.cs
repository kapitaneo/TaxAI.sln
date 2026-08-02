using System.Net;
namespace TaxAI.Portal.Tests.Security;

public sealed class ClientIntakeSecurityTests
{
    [Fact]
    public async Task PublicLimitRunsBeforeCredentialParsingAndUsesFixed429()
    {
        var workflow = new IntakePortalHarness.ClientWorkflow();
        using var fixture = IntakePortalHarness.Create(client: workflow);
        using var client = fixture.CreateClient(followRedirects: false);

        HttpResponseMessage? limited = null;
        for (var attempt = 0; attempt < 21; attempt++)
        {
            limited?.Dispose();
            limited = await client.GetAsync(
                "/client-intake/link/not-a-canonical-credential");
        }

        using (var final = limited ??
            throw new InvalidOperationException("No rate-limit response."))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, final.StatusCode);
            Assert.Equal(
                "Too many requests.",
                await final.Content.ReadAsStringAsync());
            Assert.True(final.Headers.CacheControl?.NoStore);
            Assert.Equal(
                "no-referrer",
                Assert.Single(final.Headers.GetValues("Referrer-Policy")));
        }
        Assert.Empty(workflow.InspectionCalls);
    }

    [Fact]
    public async Task SessionLimitTreatsMissingAndPresentStateWithoutAnOracle()
    {
        using var fixture = IntakePortalHarness.Create();
        using var client = fixture.CreateClient(followRedirects: false);

        HttpResponseMessage? second = null;
        for (var attempt = 0; attempt < 61; attempt++)
        {
            second?.Dispose();
            second = await client.GetAsync("/client-intake");
        }

        using (var final = second ??
            throw new InvalidOperationException("No rate-limit response."))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, final.StatusCode);
            Assert.Equal(
                "Too many requests.",
                await final.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public void UploadBoundaryUsesStreamingPrimitivesAndNoBufferedFormSurface()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "TaxAI.Portal",
                "ClientIntakeEndpoints.cs"));

        Assert.Contains("MultipartReader", source, StringComparison.Ordinal);
        Assert.Contains("section.Body", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IFormFile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadFormAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".FileName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryStream", source, StringComparison.Ordinal);
    }

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
