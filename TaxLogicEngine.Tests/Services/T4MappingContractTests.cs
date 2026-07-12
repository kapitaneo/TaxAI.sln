using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Contract tests for spec 004 (T4 box→CRA line mapping): they pin the stable
/// public shape the waves code against — the mapper ctor guards its dependency, the
/// result type's Empty value is genuinely empty, and the config-rule record
/// round-trips. Per-method "throws NotImplementedException" checks were intentionally
/// dropped: they are transient (each becomes false as its method is implemented in a
/// later wave) and, spread across parallel tasks editing one file, caused a stale
/// assertion to survive a merge. Behavioural rules live in each task's own suite.
/// </summary>
public class T4MappingContractTests
{
    [Fact]
    public void Mapper_Ctor_RejectsNullProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new T4LineMapper(null!));
    }

    [Fact]
    public void MappingResult_Empty_IsEmpty()
    {
        Assert.Empty(T4MappingResult.Empty.Lines);
        Assert.Empty(T4MappingResult.Empty.UnmappedBoxCodes);
    }

    [Fact]
    public void T4BoxLineMapping_CarriesCodeAndLinesVerbatim()
    {
        var mapping = new T4BoxLineMapping("14", new[] { "10100" });

        Assert.Equal("14", mapping.BoxCode);
        Assert.Equal(new[] { "10100" }, mapping.LineNumbers);
    }
}
