using DocumentExtraction.Services;

namespace DocumentExtraction.Tests.Services;

/// <summary>
/// The module-local tax-year policy: exactly 2025 is supported. Missing or
/// adjacent years fail closed — there is no fallback and no coercion.
/// </summary>
public sealed class SupportedExtractionTaxYearsTests
{
    [Fact]
    public void All_ContainsExactly2025()
    {
        Assert.Equal(new[] { 2025 }, SupportedExtractionTaxYears.All);
    }

    [Fact]
    public void IsSupported_2025_IsTrue()
    {
        Assert.True(SupportedExtractionTaxYears.IsSupported(2025));
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(2023)]
    [InlineData(2030)]
    [InlineData(0)]
    [InlineData(-2025)]
    [InlineData(int.MaxValue)]
    public void IsSupported_AnyOtherYear_IsFalse(int taxYear)
    {
        Assert.False(SupportedExtractionTaxYears.IsSupported(taxYear));
    }
}
