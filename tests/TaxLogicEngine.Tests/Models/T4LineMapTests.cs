using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

/// <summary>
/// Unit tests for <see cref="T4LineMap"/> config resolution (spec 004, T03):
/// a valid config resolves and looks up; each fail-closed rule (blank code, empty
/// line list, blank line, duplicate code, and the T4-specific unknown-code check —
/// FR6/FR7/FR8, ACs 7/8/9) returns a structured error and no map.
/// </summary>
public class T4LineMapTests
{
    private static T4LineMappingConfig Config(params T4BoxLineMapping[] mappings) =>
        new(2025, mappings);

    [Fact]
    public void Create_ValidConfig_ResolvesAndLooksUp()
    {
        var result = T4LineMap.Create(Config(
            new T4BoxLineMapping("14", new[] { "10100" }),
            new T4BoxLineMapping("39", new[] { "24900" })));

        Assert.True(result.IsSuccess);
        var map = result.Value!;
        Assert.Equal(2025, map.TaxYear);
        Assert.Equal(new[] { "10100" }, map.LinesFor("14"));
        Assert.Equal(new[] { "24900" }, map.LinesFor("39"));
        Assert.Null(map.LinesFor("18")); // present box, not in this config → miss
    }

    [Fact]
    public void Create_RecognizedOtherInformationCode_IsAccepted()
    {
        var result = T4LineMap.Create(Config(new T4BoxLineMapping("40", new[] { "10400" })));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_UnknownBoxCode_FailsClosed()
    {
        var result = T4LineMap.Create(Config(new T4BoxLineMapping("999", new[] { "10100" })));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Box 999");
    }

    [Fact]
    public void Create_BlankBoxCode_FailsClosed()
    {
        var result = T4LineMap.Create(Config(new T4BoxLineMapping("  ", new[] { "10100" })));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "BoxCode");
    }

    [Fact]
    public void Create_EmptyLineList_FailsClosed()
    {
        var result = T4LineMap.Create(Config(new T4BoxLineMapping("14", Array.Empty<string>())));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Box 14");
    }

    [Fact]
    public void Create_BlankLineNumber_FailsClosed()
    {
        var result = T4LineMap.Create(Config(new T4BoxLineMapping("14", new[] { " " })));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Box 14");
    }

    [Fact]
    public void Create_DuplicateBoxCode_FailsClosed()
    {
        var result = T4LineMap.Create(Config(
            new T4BoxLineMapping("14", new[] { "10100" }),
            new T4BoxLineMapping("14", new[] { "10100" })));

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Field == "Box 14" && e.Message.Contains("more than once"));
    }

    [Fact]
    public void Create_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => T4LineMap.Create(null!));
    }

    [Fact]
    public void OtherInformationCodes_All_ContainsTheVerified2025Set()
    {
        // Spot-check the load-bearing codes the mapping config actually uses.
        Assert.Contains("39", T4OtherInformationCodes.All);
        Assert.Contains("41", T4OtherInformationCodes.All);
        Assert.Contains("77", T4OtherInformationCodes.All);
        Assert.DoesNotContain("999", T4OtherInformationCodes.All);
    }
}
