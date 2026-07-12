using System.Text.Json;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Mappings;

/// <summary>
/// Guards the shipped 2025 T4 mapping config (spec 004, T04): the embedded
/// resource deserializes, resolves through <see cref="T4LineMap.Create"/>, and maps
/// each box to its canada.ca-verified CRA line. The deferred boxes (calculation /
/// schedule / residence) are intentionally absent so they surface as unmapped.
/// The expected line numbers here are the test's own assertion of the CRA fact; the
/// production types hold no line-number literal (that rule is checked in T07).
/// </summary>
public class T4LineMap2025Tests
{
    private const string ResourceName = "TaxLogicEngine.Mappings.T4LineMap.2025.json";

    private static T4LineMap Resolved2025()
    {
        var assembly = typeof(T4LineMap).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");

        var config = JsonSerializer.Deserialize<T4LineMappingConfig>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = T4LineMap.Create(config);
        Assert.True(result.IsSuccess, $"2025 config did not resolve: {string.Join("; ", result.Errors.Select(e => $"{e.Field} {e.Message}"))}");
        return result.Value!;
    }

    [Fact]
    public void Config_Resolves_AndDeclares2025()
    {
        Assert.Equal(2025, Resolved2025().TaxYear);
    }

    [Theory]
    [InlineData("14", "10100")]
    [InlineData("18", "31200")]
    [InlineData("20", "20700")]
    [InlineData("22", "43700")]
    [InlineData("44", "21200")]
    [InlineData("52", "20600")]
    [InlineData("39", "24900")]
    [InlineData("41", "24900")]
    [InlineData("42", "10120")]
    [InlineData("43", "24400")]
    [InlineData("77", "22900")]
    public void MappedBox_RoutesToVerifiedLine(string boxCode, string expectedLine)
    {
        var lines = Resolved2025().LinesFor(boxCode);

        Assert.NotNull(lines);
        Assert.Equal(new[] { expectedLine }, lines);
    }

    [Theory]
    [InlineData("16")] // CPP → Schedule 8 / RC381 calculation
    [InlineData("17")] // QPP → Schedule 8 / RC381 calculation
    [InlineData("16A")]
    [InlineData("46")] // donations → Schedule 9
    [InlineData("55")] // PPIP → residence-dependent
    [InlineData("24")] // insurable earnings → no line
    public void DeferredBox_IsAbsentFromConfig(string boxCode)
    {
        Assert.Null(Resolved2025().LinesFor(boxCode));
    }

    [Fact]
    public void MappedBoxes_AreExactlyTheElementDirectMappings()
    {
        var codes = Resolved2025().BoxCodes;

        Assert.Equal(11, codes.Count);
    }
}
