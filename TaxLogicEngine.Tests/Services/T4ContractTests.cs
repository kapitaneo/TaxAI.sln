using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace TaxLogicEngine.Tests.Services;

/// <summary>
/// Wave 0 contract tests for spec 003 (T4 slip parsing). They pin the public
/// shape the later waves code against: the parser signature exists but is not yet
/// implemented, the loose-input default is an empty box list, and the two
/// letter-suffixed box codes (16A/17A) are spelled correctly. Behavioural rules
/// are covered by T02+.
/// </summary>
public class T4ContractTests
{
    [Fact]
    public void Parse_AtContractStage_ThrowsNotImplemented()
    {
        var parser = new T4SlipParser();

        Assert.Throws<NotImplementedException>(() => parser.Parse(new T4RawInput()));
    }

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
