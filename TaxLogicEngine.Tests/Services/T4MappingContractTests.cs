using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Wave 0 contract tests for spec 004 (T4 box→CRA line mapping). They pin the
/// public shape the later waves code against: the mapper/provider/resolver/flattener
/// signatures exist but are not yet implemented, and the result type's Empty value is
/// genuinely empty. Behavioural rules are covered by T02+.
/// </summary>
public class T4MappingContractTests
{
    private static T4Slip MinimalSlip() => new()
    {
        Year = 2025,
        EmployerName = "ACME CORP",
        EmployeeName = "DOE, JANE",
        DentalBenefitCode = "1",
    };

    [Fact]
    public void Mapper_Map_AtContractStage_ThrowsNotImplemented()
    {
        var mapper = new T4LineMapper(new EmbeddedT4LineMapProvider());

        Assert.Throws<NotImplementedException>(() => mapper.Map(Array.Empty<T4Slip>()));
    }

    [Fact]
    public void Mapper_Ctor_RejectsNullProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new T4LineMapper(null!));
    }

    [Fact]
    public void Provider_TryGetForYear_AtContractStage_ThrowsNotImplemented()
    {
        Assert.Throws<NotImplementedException>(() => new EmbeddedT4LineMapProvider().TryGetForYear(2025));
    }

    [Fact]
    public void SlipBoxAmounts_Enumerate_AtContractStage_ThrowsNotImplemented()
    {
        Assert.Throws<NotImplementedException>(() => T4SlipBoxAmounts.Enumerate(MinimalSlip()));
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
