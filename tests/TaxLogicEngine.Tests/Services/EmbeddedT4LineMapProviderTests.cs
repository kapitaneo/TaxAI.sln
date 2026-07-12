using System.Text;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Verifies the real embedded provider (spec 004, T06): the shipped 2025 T4 config
/// loads and resolves, and every fail-closed path — a year with no embedded resource
/// (AC5), malformed JSON, and a config whose declared year does not match the request
/// (AC6) — returns a failure rather than a partial or wrong-year map.
/// </summary>
public class EmbeddedT4LineMapProviderTests
{
    private static readonly EmbeddedT4LineMapProvider Provider = new();

    private static Stream StreamOf(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

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

        Assert.Equal(new[] { "10100" }, map.LinesFor("14"));
        Assert.Equal(new[] { "43700" }, map.LinesFor("22"));
        Assert.Equal(new[] { "24900" }, map.LinesFor("39"));
        Assert.Null(map.LinesFor("16")); // deferred box stays unmapped
    }

    [Fact]
    public void TryGetForYear_UnknownYear_FailsClosedWithMissingConfig()
    {
        var result = Provider.TryGetForYear(2099);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, e => e.Field == "TaxYear");
    }

    [Fact]
    public void ParseConfig_MalformedJson_FailsClosed()
    {
        using var stream = StreamOf("{ this is not valid json ");

        var result = EmbeddedT4LineMapProvider.ParseConfig(stream, 2025);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, e => e.Field == "Config");
    }

    [Fact]
    public void ParseConfig_NullPayload_FailsClosed()
    {
        using var stream = StreamOf("null");

        var result = EmbeddedT4LineMapProvider.ParseConfig(stream, 2025);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Field == "Config");
    }

    [Fact]
    public void ParseConfig_DeclaredYearMismatch_FailsClosed()
    {
        // Requested 2025 but the config declares 2024 — must never be applied to the
        // wrong year (CLAUDE.md: never fall back to an adjacent year).
        using var stream = StreamOf("""{ "taxYear": 2024, "mappings": [ { "boxCode": "14", "lineNumbers": ["10100"] } ] }""");

        var result = EmbeddedT4LineMapProvider.ParseConfig(stream, 2025);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, e => e.Field == "TaxYear");
    }
}
