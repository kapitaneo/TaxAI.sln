using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Tests.Production;

public sealed class LayoutRecognitionTests
{
    [Theory]
    [InlineData("T4", DocumentClassification.T4Slip)]
    [InlineData("T4 Statement of Remuneration Paid", DocumentClassification.T4Slip)]
    [InlineData("Medical expense receipt", DocumentClassification.MedicalExpenseEvidence)]
    [InlineData("Invoice", DocumentClassification.Unsupported)]
    [InlineData("T5", DocumentClassification.Unsupported)]
    [InlineData("likely T4", DocumentClassification.Unsupported)]
    [InlineData("", DocumentClassification.Unsupported)]
    public async Task OnlyExplicitSupportedTitlesAreRecognized(string title, DocumentClassification expected)
    {
        using var wire = new ProviderWire { Title = title };
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderClassificationResult>(await wire.Invoke(provider, "classify"));
        Assert.Equal(expected, result.Classification);
        Assert.Null(result.Confidence);
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Fact]
    public async Task MultipleSupportedTitlesAreAmbiguous()
    {
        using var wire = new ProviderWire
        {
            TransformLayout = layout =>
            {
                layout["paragraphs"] = new[] { "T4", "Medical expense receipt" }.Select(title =>
                    new { role = "title", content = title, boundingRegions = ProviderWire.Geometry() });
                return layout;
            },
        };
        using var provider = wire.Provider();
        Assert.Equal(DocumentClassification.Unsupported,
            (await provider.ClassifyAsync(wire.Source, CancellationToken.None))!.Classification);
    }

    [Theory]
    [InlineData("apiVersion")]
    [InlineData("modelId")]
    [InlineData("pages")]
    [InlineData("paragraphs")]
    public async Task MissingStructureNeverProducesFields(string property)
    {
        using var wire = new ProviderWire { TransformLayout = layout => { layout.Remove(property); return layout; } };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Fact]
    public async Task MissingTitleGeometryRefuses()
    {
        using var wire = new ProviderWire { TransformLayout = layout =>
        { layout["paragraphs"] = new[] { new { role = "title", content = "T4" } }; return layout; } };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }
}
