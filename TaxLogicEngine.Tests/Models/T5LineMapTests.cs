using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class T5LineMapTests
{
    private static T5LineMappingConfig Config(params BoxLineMapping[] mappings) =>
        new(2025, mappings);

    [Fact]
    public void Create_ValidConfig_BuildsLookup_IncludingOneBoxToManyLines()
    {
        var result = T5LineMap.Create(Config(
            new BoxLineMapping("13", new[] { "12100" }),
            new BoxLineMapping("11", new[] { "12000", "12010" })));

        Assert.True(result.IsSuccess);
        var map = result.Value!;
        Assert.Equal(2025, map.TaxYear);
        Assert.Equal(new[] { "12100" }, map.LinesFor("13"));
        Assert.Equal(new[] { "12000", "12010" }, map.LinesFor("11"));
    }

    [Fact]
    public void LinesFor_UnknownBox_ReturnsNull()
    {
        var map = T5LineMap.Create(Config(new BoxLineMapping("13", new[] { "12100" }))).Value!;

        Assert.Null(map.LinesFor("99"));
    }

    [Fact]
    public void Create_DuplicateBoxCode_Fails()
    {
        var result = T5LineMap.Create(Config(
            new BoxLineMapping("13", new[] { "12100" }),
            new BoxLineMapping("13", new[] { "12000" })));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 13");
    }

    [Fact]
    public void Create_EmptyLineList_Fails()
    {
        var result = T5LineMap.Create(Config(new BoxLineMapping("13", Array.Empty<string>())));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 13");
    }

    [Fact]
    public void Create_BlankBoxCode_Fails()
    {
        var result = T5LineMap.Create(Config(new BoxLineMapping("  ", new[] { "12100" })));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "BoxCode");
    }

    [Fact]
    public void Create_BlankLineNumber_Fails()
    {
        var result = T5LineMap.Create(Config(new BoxLineMapping("13", new[] { "12100", " " })));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Box 13");
    }

    [Fact]
    public void ConfigRecords_HoldValues()
    {
        var mapping = new BoxLineMapping("11", new[] { "12000", "12010" });
        var config = new T5LineMappingConfig(2025, new[] { mapping });

        Assert.Equal("11", config.Mappings[0].BoxCode);
        Assert.Equal(2025, config.TaxYear);
    }
}
