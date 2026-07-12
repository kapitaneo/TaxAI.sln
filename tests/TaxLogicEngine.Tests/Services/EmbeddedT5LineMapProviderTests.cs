using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

public class EmbeddedT5LineMapProviderTests
{
    private static readonly EmbeddedT5LineMapProvider Provider = new();

    [Fact]
    public void TryGetForYear_2025_LoadsTheEmbeddedConfig()
    {
        var result = Provider.TryGetForYear(2025);

        Assert.True(result.IsSuccess);
        Assert.Equal(2025, result.Value!.TaxYear);
    }

    [Fact]
    public void TryGetForYear_2025_ResolvedLookupMatchesConfig()
    {
        var map = Provider.TryGetForYear(2025).Value!;

        Assert.Equal(new[] { "12000", "12010" }, map.LinesFor("11"));
        Assert.Equal(new[] { "12100" }, map.LinesFor("13"));
        Assert.Equal(new[] { "40425" }, map.LinesFor("26"));
        Assert.Null(map.LinesFor("18")); // deferred box stays unmapped
    }

    [Fact]
    public void TryGetForYear_UnknownYear_FailsClosed()
    {
        var result = Provider.TryGetForYear(1990);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, e => e.Field == "TaxYear");
    }
}
