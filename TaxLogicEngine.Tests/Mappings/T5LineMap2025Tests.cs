using System.Text.Json;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Mappings;

/// <summary>
/// Verifies the embedded 2025 config deserializes and carries the CRA-anchored
/// mappings (see TaxLogicEngine/Mappings/README.md for the cited sources).
/// </summary>
public class T5LineMap2025Tests
{
    private const string ResourceName = "TaxLogicEngine.Mappings.T5LineMap.2025.json";

    private static T5LineMappingConfig LoadConfig()
    {
        using var stream = typeof(T5BoxCodes).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<T5LineMappingConfig>(stream, options)!;
    }

    [Fact]
    public void Config_IsEmbedded_AndDeserializes_For2025()
    {
        var config = LoadConfig();

        Assert.Equal(2025, config.TaxYear);
        Assert.NotEmpty(config.Mappings);
    }

    [Fact]
    public void Config_BuildsAValidT5LineMap()
    {
        var result = T5LineMap.Create(LoadConfig());

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("11", "12000", "12010")] // taxable other-than-eligible dividends -> both lines
    [InlineData("25", "12000")]          // taxable eligible dividends
    [InlineData("13", "12100")]          // interest
    [InlineData("14", "12100")]          // other income
    [InlineData("15", "12100")]          // foreign income
    [InlineData("17", "12100")]          // royalties
    [InlineData("19", "12100")]          // accrued income / annuities
    [InlineData("12", "40425")]          // dividend tax credit (other than eligible)
    [InlineData("26", "40425")]          // dividend tax credit (eligible)
    public void Config_MapsBox_ToExpectedCraLines(string boxCode, params string[] expectedLines)
    {
        var map = T5LineMap.Create(LoadConfig()).Value!;

        Assert.Equal(expectedLines, map.LinesFor(boxCode));
    }

    [Theory]
    [InlineData("10")] // actual dividends - informational
    [InlineData("24")] // actual eligible dividends - informational
    [InlineData("16")] // foreign tax paid - calculated (T2209), deferred
    [InlineData("18")] // capital gains dividends - Schedule 3 / inclusion rate, deferred
    public void Config_DoesNotMap_InformationalOrCalculatedBoxes(string boxCode)
    {
        var map = T5LineMap.Create(LoadConfig()).Value!;

        Assert.Null(map.LinesFor(boxCode));
    }
}
