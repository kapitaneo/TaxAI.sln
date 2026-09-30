using ExtractionWorkflow.Models;

namespace DocumentIntelligence.Tests.Production;

public sealed class LayoutResultInterpreterTests
{
    [Fact]
    public async Task CopiesPrintedValuesAndDuplicatesWithoutRepairOrThreshold()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair("Year", "2025"));
        wire.Pairs.Add(ProviderWire.Pair("Employer's name", "  Synthetic employer  "));
        wire.Pairs.Add(ProviderWire.Pair("Box 14", "$1,234.567", 1.7m));
        wire.Pairs.Add(ProviderWire.Pair("Box 14", "-2,00", -0.2m));
        wire.Pairs.Add(ProviderWire.Pair("999", "unchanged"));
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderT4Result>(await wire.Invoke(provider));
        Assert.Equal(2025, result.Year);
        Assert.Equal("  Synthetic employer  ", result.EmployerName);
        Assert.Equal(new[] { "14", "14", "999" }, result.Boxes!.Select(box => box!.Code));
        Assert.Equal(new[] { "$1,234.567", "-2,00", "unchanged" }, result.Boxes!.Select(box => box!.Value));
        Assert.Equal(new decimal?[] { 1.7m, -0.2m, null }, result.Boxes!.Select(box => box!.Confidence));
        Assert.Null(result.Confidence);
        Assert.Null(result.EmployeeName);
    }

    [Theory]
    [InlineData("Year")]
    [InlineData("Employer's name")]
    [InlineData("Employee's name")]
    public async Task AmbiguousScalarFieldsRefuseInsteadOfPickingOne(string key)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair(key, "2025"));
        wire.Pairs.Add(ProviderWire.Pair(key, "2025"));
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal("DELETE", wire.Methods.Last());
    }

    [Fact]
    public async Task MedicalFieldsAreClosedAndAsPrinted()
    {
        using var wire = new ProviderWire { Title = "Medical expense receipt" };
        wire.Pairs.Add(ProviderWire.Pair("Service date", "March 4, 2025"));
        wire.Pairs.Add(ProviderWire.Pair("Gross amount", " CAD $ 5.005 "));
        wire.Pairs.Add(ProviderWire.Pair("Currency", "CAD"));
        wire.Pairs.Add(ProviderWire.Pair("Provider name", "Synthetic practice"));
        wire.Pairs.Add(ProviderWire.Pair("Patient name", "Synthetic person"));
        wire.Pairs.Add(ProviderWire.Pair("Receipt reference", "sample"));
        wire.Pairs.Add(ProviderWire.Pair("Diagnosis", "must not be projected"));
        using var provider = wire.Provider();
        var result = Assert.IsType<ProviderMedicalResult>(await wire.Invoke(provider, "medical"));
        Assert.Equal("March 4, 2025", result.ServiceDateAsPrinted);
        Assert.Equal(" CAD $ 5.005 ", result.GrossAmountAsPrinted);
        Assert.Equal("CAD", result.CurrencyCodeAsPrinted);
        Assert.Null(result.Confidence);
        Assert.DoesNotContain("must not", result.ToString());
        Assert.DoesNotContain("must not", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("20O5")]
    [InlineData(" 2025 ")]
    [InlineData("2025/2026")]
    public async Task UnrepresentablePrintedYearIsNeverRepaired(string year)
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(ProviderWire.Pair("Year", year));
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }

    [Fact]
    public async Task FieldAssociationAcrossPagesRefuses()
    {
        using var wire = new ProviderWire();
        wire.Pairs.Add(new
        {
            key = new { content = "Box 14", boundingRegions = ProviderWire.Geometry() },
            value = new { content = "5", boundingRegions = ProviderWire.Geometry(2) },
        });
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }
}
