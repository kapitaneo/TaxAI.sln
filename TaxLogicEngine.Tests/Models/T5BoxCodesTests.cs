using System.Reflection;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class T5BoxCodesTests
{
    private static IReadOnlyList<string> AllCodes() =>
        typeof(T5BoxCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    [Fact]
    public void BoxCodes_AreAllDistinct()
    {
        var codes = AllCodes();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData(T5BoxCodes.OtherThanEligibleDividendsActual, "10")]
    [InlineData(T5BoxCodes.InterestFromCanadianSources, "13")]
    [InlineData(T5BoxCodes.RecipientIdentificationNumber, "22")]
    [InlineData(T5BoxCodes.RecipientType, "23")]
    [InlineData(T5BoxCodes.EligibleDividendsActual, "24")]
    [InlineData(T5BoxCodes.ForeignCurrency, "27")]
    [InlineData(T5BoxCodes.EquityLinkedNotesInterest, "30")]
    public void BoxCode_HasExpectedValue(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BoxCodes_AreNotCraLineNumbers_JustBoxCodes()
    {
        // Sanity guard for FR8: box codes are the 10-30 range, never a CRA line
        // number (those come from JSON mapping config, not here).
        foreach (var code in AllCodes())
        {
            Assert.True(int.TryParse(code, out var n) && n is >= 10 and <= 30, $"Unexpected box code '{code}'.");
        }
    }
}
