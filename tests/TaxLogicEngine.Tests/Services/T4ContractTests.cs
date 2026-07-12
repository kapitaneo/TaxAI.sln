using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Contract tests for spec 003 (T4 slip parsing): they pin the public shape the
/// waves code against — the loose-input default is an empty box list, the
/// passthrough pair round-trips, and the box-code constants (incl. the
/// letter-suffixed 16A/17A) are spelled to match the CRA-printed box numbers.
/// Behavioural rules are covered by T4SlipParserTests and the T03–T05 suites.
/// </summary>
public class T4ContractTests
{
    [Fact]
    public void T4RawInput_Boxes_DefaultsToEmptyNotNull()
    {
        var input = new T4RawInput();

        Assert.NotNull(input.Boxes);
        Assert.Empty(input.Boxes);
    }

    [Fact]
    public void T4OtherInformation_CarriesCodeAndAmountVerbatim()
    {
        var pair = new T4OtherInformation("40", 123.45m);

        Assert.Equal("40", pair.Code);
        Assert.Equal(123.45m, pair.Amount);
    }

    [Theory]
    [InlineData(T4BoxCodes.SecondCppContributions, "16A")]
    [InlineData(T4BoxCodes.SecondQppContributions, "17A")]
    [InlineData(T4BoxCodes.SocialInsuranceNumber, "12")]
    [InlineData(T4BoxCodes.DentalBenefits, "45")]
    [InlineData(T4BoxCodes.EmployerAccountNumber, "54")]
    public void T4BoxCodes_MatchTheCraPrintedBoxNumbers(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }
}
