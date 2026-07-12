using TaxCases.Services;

namespace TaxCases.Tests.Services;

public sealed class SupportedTaxYearsTests
{
    [Fact]
    public void All_ContainsOnly2025()
    {
        Assert.Equal([2025], SupportedTaxYears.All);
    }

    [Theory]
    [InlineData(2025, true)]
    [InlineData(2024, false)]
    [InlineData(2026, false)]
    [InlineData(0, false)]
    public void IsSupported_UsesFixed2025Policy(int year, bool expected)
    {
        Assert.Equal(expected, SupportedTaxYears.IsSupported(year));
    }
}
