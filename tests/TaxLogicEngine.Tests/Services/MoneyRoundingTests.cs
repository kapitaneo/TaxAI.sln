using System.Globalization;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

public class MoneyRoundingTests
{
    // Amounts are passed as strings and parsed with the invariant culture so the
    // decimal midpoints are exact (a double literal like 100.005 would not be).
    [Theory]
    [InlineData("100.005", "100.01")]   // AC2 — the canonical half-up case
    [InlineData("-100.005", "-100.01")] // away from zero on the negative side too
    [InlineData("2.345", "2.35")]       // half-up, not banker's (which gives 2.34)
    [InlineData("0.125", "0.13")]       // half-up, not banker's (which gives 0.12)
    [InlineData("1.234", "1.23")]       // below midpoint rounds down
    [InlineData("1.2349", "1.23")]      // below midpoint rounds down
    [InlineData("12.34", "12.34")]      // already at cents, unchanged
    [InlineData("0", "0.00")]           // zero
    public void ToCents_RoundsHalfUpAwayFromZero(string input, string expected)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        var want = decimal.Parse(expected, CultureInfo.InvariantCulture);

        Assert.Equal(want, MoneyRounding.ToCents(value));
    }

    [Fact]
    public void ToCents_ResultHasTwoDecimalPlaces()
    {
        var result = MoneyRounding.ToCents(100.005m);

        // Scale (number of decimal digits) is the 4th element of decimal.GetBits.
        var scale = (decimal.GetBits(result)[3] >> 16) & 0xFF;
        Assert.Equal(MoneyRounding.CurrencyDecimals, scale);
    }

    [Fact]
    public void ToCents_IsDeterministic()
    {
        Assert.Equal(MoneyRounding.ToCents(3.14159m), MoneyRounding.ToCents(3.14159m));
    }
}
