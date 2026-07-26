using CaseProcessing.Services;

namespace CaseProcessing.Tests.Services;

/// <summary>
/// The module-local tax-year policy: exactly 2025 is supported. Adjacent years
/// fail closed — there is no fallback, no coercion, and no system-clock read.
/// </summary>
public sealed class SupportedProcessingTaxYearsTests
{
    [Fact]
    public void All_ContainsExactly2025()
    {
        Assert.Equal(new[] { 2025 }, SupportedProcessingTaxYears.All);
    }

    [Fact]
    public void IsSupported_2025_IsTrue()
    {
        Assert.True(SupportedProcessingTaxYears.IsSupported(2025));
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(2023)]
    [InlineData(2030)]
    [InlineData(0)]
    [InlineData(-2025)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void IsSupported_AnyOtherYear_IsFalse(int taxYear)
    {
        Assert.False(SupportedProcessingTaxYears.IsSupported(taxYear));
    }

    [Fact]
    public void All_IsStableAcrossReads()
    {
        // The policy is a fixed list, not a computed or clock-dependent value.
        Assert.Equal(SupportedProcessingTaxYears.All, SupportedProcessingTaxYears.All);
        Assert.Same(SupportedProcessingTaxYears.All, SupportedProcessingTaxYears.All);
    }

    [Fact]
    public void EverySupportedYear_IsAcceptedByIsSupported()
    {
        // Guards against All and IsSupported drifting apart.
        Assert.All(SupportedProcessingTaxYears.All, year => Assert.True(SupportedProcessingTaxYears.IsSupported(year)));
    }
}
