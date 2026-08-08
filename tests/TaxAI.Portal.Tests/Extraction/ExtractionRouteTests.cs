using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Support;

namespace TaxAI.Portal.Tests.Extraction;

public sealed class ExtractionRouteTests :
    IClassFixture<WebApplicationFactory<Program>>,
    IDisposable
{
    private readonly WebApplicationFactory<Program> _host;

    public ExtractionRouteTests(WebApplicationFactory<Program> factory) =>
        _host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(
                services => services.AddApprovedDurableDataProtection());
        });

    public void Dispose() => _host.Dispose();

    public static TheoryData<string, string, bool> Routes() => new()
    {
        {
            "/intake-cases/{caseId:guid}/extractions",
            "GET",
            false
        },
        {
            "/intake-cases/{caseId:guid}/extractions/{documentId:guid}",
            "GET",
            false
        },
        {
            "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract",
            "POST",
            true
        },
        {
            "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification",
            "POST",
            true
        },
        {
            "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections",
            "POST",
            true
        },
        {
            "/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept",
            "POST",
            true
        },
        {
            "/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}",
            "POST",
            true
        },
        {
            "/intake-cases/{caseId:guid}/extractions/finalize",
            "POST",
            true
        },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryRouteIsFirmScopedAndEveryMutationRequiresAntiforgery(
        string pattern,
        string method,
        bool requiresAntiforgery)
    {
        var endpoint = Assert.Single(
            Endpoints(),
            item => string.Equals(
                item.RoutePattern.RawText,
                pattern,
                StringComparison.Ordinal));

        Assert.Equal(
            new[] { method },
            endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>()
                .HttpMethods);
        Assert.Equal(
            "TaxAiFirmScoped",
            endpoint.Metadata.GetRequiredMetadata<IAuthorizeData>().Policy);

        var antiforgery = endpoint.Metadata.FirstOrDefault(item =>
            item.GetType().GetInterfaces().Any(contract =>
                contract.FullName ==
                "Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata"));
        Assert.Equal(requiresAntiforgery, antiforgery is not null);
        if (requiresAntiforgery)
        {
            Assert.True(
                (bool)antiforgery!.GetType()
                    .GetProperty("RequiresValidation")!
                    .GetValue(antiforgery)!);
        }
    }

    [Fact]
    public void ExtractionAddsExactlyTheTwoApprovedApplicationReadSurfaces()
    {
        var reads = Endpoints()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText!.Contains(
                    "/extractions",
                    StringComparison.Ordinal) &&
                endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>()
                    .HttpMethods.Contains("GET", StringComparer.Ordinal))
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "/intake-cases/{caseId:guid}/extractions",
                "/intake-cases/{caseId:guid}/extractions/{documentId:guid}",
            },
            reads);
    }

    private RouteEndpoint[] Endpoints()
    {
        using var client = _host.CreateClient();
        return _host.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
    }
}
