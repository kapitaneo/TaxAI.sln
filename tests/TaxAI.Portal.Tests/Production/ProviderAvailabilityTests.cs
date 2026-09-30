using System.Net;
using Azure.Core;
using DocumentIntelligence.Production;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxAI.Portal.Tests.EndToEnd;

namespace TaxAI.Portal.Tests.Production;

[Collection("Extraction network observation")]
public sealed class ProviderAvailabilityTests
{
    [Fact]
    public async Task UnavailableProviderReturnsSafe503OnlyAfterScopeAndRecovers()
    {
        var gate = new MutableAvailability();
        await using var world = ExtractionPortalWorld.Create(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IExtractionAvailabilityGate>(); services.AddSingleton<IExtractionAvailabilityGate>(gate); }));
        var seeded = await world.SeedFirmOneAsync();
        var foreign = await world.SeedFirmTwoAsync();
        using var accountant = world.Intake.FirmOne.CreateAnonymousClient();
        await world.Intake.FirmOne.SignInAsync(accountant);

        var refused = await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("Document extraction is temporarily unavailable.", refused.Body);
        Assert.Contains(refused.Headers, header => header.Contains("no-store", StringComparison.Ordinal));
        var checkedBeforeForeign = gate.Calls;
        var missing = await ExtractionPortalHttp.PostReviewMutationAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId,
            $"/intake-cases/{foreign.CaseId:D}/extractions/{foreign.FirstT4DocumentId:D}/extract", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(missing.Body);
        Assert.Equal(checkedBeforeForeign, gate.Calls);
        var read = await ExtractionPortalHttp.GetAsync(accountant,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        gate.Available = true;
        var recovered = await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task RealWorkflowCannotAcceptAnOperationWhoseDeletionFailed()
    {
        var wire = new ControlledT4Transport(deletionSucceeds: false);
        using var client = new HttpClient(wire);
        using var provider = ControlledProvider(client);
        await using var world = ExtractionPortalWorld.Create(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IDocumentIntelligenceProvider>(); services.AddSingleton<IDocumentIntelligenceProvider>(provider); }));
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = world.Intake.FirmOne.CreateAnonymousClient();
        await world.Intake.FirmOne.SignInAsync(accountant);
        var extraction = await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        Assert.NotEqual(HttpStatusCode.OK, extraction.StatusCode);
        Assert.Equal(1, wire.Submissions);
        Assert.Equal(2, wire.Deletions);
        var stored = await world.Extractions.FindDocumentAsync(seeded.Firm, seeded.CaseId, seeded.FirstT4DocumentId, CancellationToken.None);
        Assert.True(stored is null || stored.Versions.Count == 0);
        var acceptance = await ExtractionPortalHttp.PostReviewMutationAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId,
            $"/intake-cases/{seeded.CaseId:D}/extractions/{seeded.FirstT4DocumentId:D}/accept", new Dictionary<string, string>());
        Assert.NotEqual(HttpStatusCode.OK, acceptance.StatusCode);
        Assert.Empty(world.Reviews.All);
    }

    [Fact]
    public async Task RealWorkflowExtractsABilingualEmployeeCopyWithoutBoxFiftyFour()
    {
        var wire = new ControlledT4Transport(deletionSucceeds: true);
        using var client = new HttpClient(wire);
        using var provider = ControlledProvider(client);
        await using var world = ExtractionPortalWorld.Create(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IDocumentIntelligenceProvider>(); services.AddSingleton<IDocumentIntelligenceProvider>(provider); }));
        var seeded = await world.SeedFirmOneAsync();
        using var accountant = world.Intake.FirmOne.CreateAnonymousClient();
        await world.Intake.FirmOne.SignInAsync(accountant);

        var extraction = await ExtractionPortalHttp.ExtractAsync(accountant, seeded.CaseId, seeded.FirstT4DocumentId);
        Assert.Equal(HttpStatusCode.OK, extraction.StatusCode);
        Assert.Equal(2, wire.Submissions);
        Assert.Equal(2, wire.Deletions);
        var stored = await world.Extractions.FindDocumentAsync(seeded.Firm, seeded.CaseId, seeded.FirstT4DocumentId, CancellationToken.None);
        var version = Assert.Single(stored!.Versions);
        Assert.Equal(DocumentClassification.T4Slip, version.Classification.AutomatedClassification);
        Assert.Equal(new[] { "14", "45" }, version.BoxValues.Select(box => box.Code).OrderBy(code => code, StringComparer.Ordinal));
        // Outcomes record only engine errors: none means the existing chain
        // accepted the employee copy with no Box 54 and the required Box 12/45.
        Assert.Empty(version.FieldOutcomes);
        Assert.DoesNotContain(ExtractionPortalWorld.PlantedSin, extraction.All, StringComparison.Ordinal);
    }

    internal sealed class MutableAvailability : IExtractionAvailabilityGate
    {
        internal bool Available;
        internal int Calls;
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) { Calls++; return Task.FromResult(Available); }
    }
    private sealed class SyntheticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("synthetic", DateTimeOffset.MaxValue);
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
    /// <summary>
    /// A controlled provider transport returning one synthetic bilingual CRA T4
    /// (25) employee copy: no Box 54, planted synthetic Box 12, printed Box 45.
    /// </summary>
    private sealed class ControlledT4Transport(bool deletionSucceeds) : HttpMessageHandler
    {
        internal int Submissions, Deletions;
        private static object[] Geometry() => [new { pageNumber = 1, polygon = new[] { 0, 0, 1, 0, 1, 1, 0, 1 } }];
        private static object Pair(string key, string value) => new
        {
            key = new { content = key, boundingRegions = Geometry() },
            value = new { content = value, boundingRegions = Geometry() },
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Submissions++;
                var response = new HttpResponseMessage(HttpStatusCode.Accepted);
                response.Headers.Add("Operation-Location", "https://approved.cognitiveservices.azure.com/documentintelligence/documentModels/prebuilt-layout/analyzeResults/33333333-3333-4333-8333-333333333333?api-version=2024-11-30");
                return Task.FromResult(response);
            }
            if (request.Method == HttpMethod.Delete)
            {
                Deletions++;
                return Task.FromResult(new HttpResponseMessage(deletionSucceeds ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "succeeded", analyzeResult = new
                    {
                        apiVersion = "2024-11-30", modelId = "prebuilt-layout", pages = new[] { new { pageNumber = 1 } },
                        paragraphs = new[] { "T4", "Statement of Remuneration Paid", "État de la rémunération payée" }
                            .Select(content => new { content, boundingRegions = Geometry() }),
                        keyValuePairs = new[]
                        {
                            Pair("Year – Année", "2025"),
                            Pair("Employer's name – Nom de l'employeur", "Synthetic Employer"),
                            Pair("Last name (in capital letters) – Nom de famille (en lettres moulées)", "EMPLOYEE"),
                            Pair("First name – Prénom", "Synthetic"),
                            Pair("Social insurance number – Numéro d'assurance sociale", ExtractionPortalWorld.PlantedSin),
                            Pair("Employment income – Revenus d'emploi", "50000.00"),
                            Pair("Employer-offered dental benefits – Prestations dentaires offertes par l'employeur", "1"),
                        },
                    },
                })),
            });
        }
    }

    private static AzureDocumentIntelligenceProvider ControlledProvider(HttpClient client) => new(new()
    {
        Endpoint = "https://approved.cognitiveservices.azure.com/",
        ResourceId = "/subscriptions/11111111-1111-4111-8111-111111111111/resourceGroups/synthetic/providers/Microsoft.CognitiveServices/accounts/approved",
        ManagedIdentityClientId = "22222222-2222-4222-8222-222222222222",
        Region = "canadacentral", ApiVersion = "2024-11-30", Model = "prebuilt-layout", Features = "keyValuePairs",
        MaximumSourceBytes = 1024 * 1024, MaximumAttempts = 2,
        OperationDeadline = TimeSpan.FromSeconds(5), CleanupDeadline = TimeSpan.FromSeconds(2),
    }, new MutableAvailability { Available = true }, new SyntheticCredential(), client);
}
