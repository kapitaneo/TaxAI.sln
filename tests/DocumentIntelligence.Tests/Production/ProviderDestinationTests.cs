using DocumentIntelligence.Production;

namespace DocumentIntelligence.Tests.Production;

public sealed class ProviderDestinationTests
{
    [Theory]
    [InlineData("http://approved.cognitiveservices.azure.com/")]
    [InlineData("https://canadacentral.api.cognitive.microsoft.com/")]
    [InlineData("https://approved.cognitiveservices.azure.com.evil.invalid/")]
    [InlineData("https://another.cognitiveservices.azure.com/")]
    [InlineData("https://approved.cognitiveservices.azure.com/path")]
    [InlineData("https://approved.cognitiveservices.azure.com/?key=value")]
    [InlineData("https://user@approved.cognitiveservices.azure.com/")]
    [InlineData("https://approved.cognitiveservices.azure.com:444/")]
    public void UnapprovedEndpointIsRefused(string endpoint) =>
        Assert.Throws<ArgumentException>(() => AzureDocumentIntelligenceOptions.RequireValid(ProviderWire.Options(endpoint: endpoint)));

    [Theory]
    [InlineData("canadaeast")]
    [InlineData("eastus")]
    [InlineData("canadanorth")]
    public void NoRegionalFailover(string region) =>
        Assert.Throws<ArgumentException>(() => AzureDocumentIntelligenceOptions.RequireValid(ProviderWire.Options(region: region)));

    [Theory]
    [InlineData("prebuilt-tax.us.w2")]
    [InlineData("custom-model")]
    [InlineData("gpt")]
    public void NoAlternateModel(string model) =>
        Assert.Throws<ArgumentException>(() => AzureDocumentIntelligenceOptions.RequireValid(ProviderWire.Options(model: model)));

    [Fact]
    public void NoPreviewApi() => Assert.Throws<ArgumentException>(() =>
        AzureDocumentIntelligenceOptions.RequireValid(ProviderWire.Options(api: "2024-07-31-preview")));

    [Theory]
    [InlineData("https://foreign.invalid/result")]
    [InlineData("https://approved.cognitiveservices.azure.com/documentintelligence/documentModels/custom/analyzeResults/33333333-3333-4333-8333-333333333333?api-version=2024-11-30")]
    [InlineData("https://approved.cognitiveservices.azure.com/documentintelligence/documentModels/prebuilt-layout/analyzeResults/not-a-uuid?api-version=2024-11-30")]
    public async Task UntrustedOperationLocationIsNotFollowed(string location)
    {
        using var wire = new ProviderWire { Location = location };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal(new[] { "POST" }, wire.Methods);
    }

    [Fact]
    public async Task WithdrawnAvailabilitySendsNoSource()
    {
        using var wire = new ProviderWire();
        using var provider = wire.Provider(availability: new ProviderWire.Available(false));
        Assert.Null(await wire.Invoke(provider));
        Assert.Empty(wire.Methods);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void AttemptsAreExplicitAndFinite(int attempts) => Assert.Throws<ArgumentException>(() =>
        AzureDocumentIntelligenceOptions.RequireValid(ProviderWire.Options(attempts: attempts)));

    [Fact]
    public void CleanupMustFitInsideTheOperation() => Assert.Throws<ArgumentException>(() =>
        AzureDocumentIntelligenceOptions.RequireValid(ProviderWire.Options(operation: TimeSpan.FromSeconds(1), cleanup: TimeSpan.FromSeconds(1))));
}
