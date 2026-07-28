using Tenancy.Models;

namespace Tenancy.Tests.Models;

public sealed class SlipTypeCodeTests
{
    [Fact]
    public void IsRecognized_AcceptsTheSingleStructuralCode()
    {
        Assert.True(SlipTypeCodes.IsRecognized(SlipTypeCodes.T4));
        Assert.Equal(new[] { SlipTypeCodes.T4 }, SlipTypeCodes.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("t4")]
    [InlineData("T4 ")]
    [InlineData(" T4")]
    [InlineData("T5")]
    [InlineData("Other")]
    public void IsRecognized_RefusesEveryOtherValueWithoutNormalization(string? code)
    {
        Assert.False(SlipTypeCodes.IsRecognized(code));
    }
}
