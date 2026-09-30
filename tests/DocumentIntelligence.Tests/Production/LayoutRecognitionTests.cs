using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Tests.Production;

/// <summary>
/// T4 identity follows the printed CRA T4 (25) slip face: the "T4" designator
/// with the English and/or French slip title, corroborated by a printed
/// slip-face box field. Sources: spec 016 evidence/T05.md.
/// </summary>
public sealed class LayoutRecognitionTests
{
    public static TheoryData<string[]> OfficialIdentities() => new()
    {
        // English identity, printed as designator and title.
        new[] { "T4", "Statement of Remuneration Paid" },
        new[] { "T4 Statement of Remuneration Paid" },
        new[] { "T4 - Statement of Remuneration Paid" },
        // French identity.
        new[] { "T4", "État de la rémunération payée" },
        new[] { "T4 État de la rémunération payée" },
        new[] { "T4 - État de la rémunération payée" },
        // The bilingual face as printed, and as one merged paragraph.
        new[] { "T4", "Statement of Remuneration Paid", "État de la rémunération payée" },
        new[] { "T4 Statement of Remuneration Paid État de la rémunération payée" },
        // Presentation only: case, dash, and Unicode composition differences.
        new[] { "t4 – STATEMENT OF REMUNERATION PAID" },
        new[] { "T4", "État de la rémunération payée" },
    };

    [Theory]
    [MemberData(nameof(OfficialIdentities))]
    public async Task OfficialEnglishFrenchAndBilingualIdentitiesAreRecognized(string[] paragraphs)
    {
        using var wire = new ProviderWire { Paragraphs = paragraphs };
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderClassificationResult>(await wire.Invoke(provider, "classify"));
        Assert.Equal(DocumentClassification.T4Slip, result.Classification);
        Assert.Null(result.Confidence);
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    public static TheoryData<string[]> Lookalikes() => new()
    {
        // A title alone, or a designator alone, is not the slip.
        new[] { "Statement of Remuneration Paid" },
        new[] { "État de la rémunération payée" },
        new[] { "T4" },
        // Other CRA slips and the T4 Summary.
        new[] { "T4A", "Statement of Pension, Retirement, Annuity, and Other Income" },
        new[] { "T4A Statement of Remuneration Paid" },
        new[] { "T5", "Statement of Investment Income" },
        new[] { "T4 Summary", "Summary of Remuneration Paid" },
        new[] { "T4", "Statement of Remuneration Paid", "Summary of Remuneration Paid" },
        new[] { "T4", "Sommaire de la rémunération payée" },
        // Invented synonyms and near-misses are not the printed title.
        new[] { "T4", "Statement of Employment Income" },
        new[] { "T4", "Remuneration Statement" },
        new[] { "likely T4" },
        new[] { "Medical expense receipt" },
        new[] { "Invoice" },
        new[] { "" },
    };

    [Theory]
    [MemberData(nameof(Lookalikes))]
    public async Task LookalikeAndUnsupportedDocumentsAreRefused(string[] paragraphs)
    {
        using var wire = new ProviderWire { Paragraphs = paragraphs };
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderClassificationResult>(await wire.Invoke(provider, "classify"));
        Assert.Equal(DocumentClassification.Unsupported, result.Classification);
        using var extraction = new ProviderWire { Paragraphs = paragraphs };
        using var extractor = extraction.Provider();
        Assert.Null(await extraction.Invoke(extractor));
    }

    [Fact]
    public async Task TheTitleIsNotTheSoleProofWithoutAPrintedSlipField()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Clear();
        wire.Pairs.Add(ProviderWire.Pair("Protected B when completed", "yes"));
        using var provider = wire.Provider();
        Assert.Equal(DocumentClassification.Unsupported,
            (await provider.ClassifyAsync(wire.Source, CancellationToken.None))!.Classification);
    }

    [Theory]
    [InlineData("Statement of Remuneration Paid")]
    [InlineData("État de la rémunération payée")]
    public async Task ARepeatedSlipTitleIsSeveralSlipsAndNeverNarrowed(string title)
    {
        using var wire = new ProviderWire { Paragraphs = ["T4", title, "T4", title] };
        using var provider = wire.Provider();
        Assert.Equal(DocumentClassification.Unsupported,
            (await provider.ClassifyAsync(wire.Source, CancellationToken.None))!.Classification);
        using var extraction = new ProviderWire { Paragraphs = ["T4", title, "T4", title] };
        using var extractor = extraction.Provider();
        Assert.Null(await extraction.Invoke(extractor));
    }

    [Fact]
    public async Task NoLayoutIsEverClassifiedAsMedicalEvidence()
    {
        using var wire = new ProviderWire { Paragraphs = ["Medical expense receipt", "Reçu de frais médicaux"] };
        wire.Pairs.Add(ProviderWire.Pair("Patient name", "Synthetic person"));
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
    public async Task MissingIdentityGeometryRefuses()
    {
        using var wire = new ProviderWire { TransformLayout = layout =>
        { layout["paragraphs"] = new[] { new { content = "T4 Statement of Remuneration Paid" } }; return layout; } };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }
}
