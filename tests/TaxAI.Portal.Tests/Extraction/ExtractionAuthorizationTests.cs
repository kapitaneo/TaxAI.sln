using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using TaxAI.Portal.Tests.Support;

namespace TaxAI.Portal.Tests.Extraction;

public sealed class ExtractionAuthorizationTests :
    IClassFixture<WebApplicationFactory<Program>>,
    IDisposable
{
    private readonly WebApplicationFactory<Program> _host;

    public ExtractionAuthorizationTests(
        WebApplicationFactory<Program> factory) =>
        _host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(
                services => services.AddApprovedDurableDataProtection());
        });

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData("GET", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions")]
    [InlineData("GET", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions/22222222-2222-4222-8222-222222222222")]
    [InlineData("POST", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions/22222222-2222-4222-8222-222222222222/extract")]
    [InlineData("POST", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions/22222222-2222-4222-8222-222222222222/classification")]
    [InlineData("POST", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions/22222222-2222-4222-8222-222222222222/corrections")]
    [InlineData("POST", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions/22222222-2222-4222-8222-222222222222/accept")]
    [InlineData("POST", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions/duplicates/33333333-3333-4333-8333-333333333333")]
    [InlineData("POST", "/intake-cases/11111111-1111-4111-8111-111111111111/extractions/finalize")]
    public async Task AnonymousRequestsNeverReachAnExtractionHandler(
        string method,
        string path)
    {
        using var client = _host.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
            });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new FormUrlEncodedContent([]);
        }

        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(
            "DevelopmentEncryptedIntakeContentStore",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NoAnonymousOrClientRouteCanInvokeExtraction()
    {
        var sourceRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TaxAI.Portal");
        var extraction = File.ReadAllText(
            Path.Combine(sourceRoot, "ExtractionEndpoints.cs"));
        var client = File.ReadAllText(
            Path.Combine(sourceRoot, "ClientIntakeEndpoints.cs"));

        Assert.DoesNotContain("AllowAnonymous", extraction, StringComparison.Ordinal);
        Assert.DoesNotContain("Extraction", client, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IExtractionContentReader",
            client,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
