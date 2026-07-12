using TaxLogicEngine.Models;

namespace TaxLogicEngine.Tests.Models;

public class RecipientIdentifierTests
{
    private const string Sin = "123456789";

    [Fact]
    public void Masked_ForNineDigitSin_ShowsOnlyLastThreeInSinLayout()
    {
        // AC3
        var id = new RecipientIdentifier(Sin);

        Assert.Equal("***-**-789", id.Masked);
    }

    [Fact]
    public void ToString_IsMasked_AndNeverContainsTheFullValueOrLeadingDigits()
    {
        var id = new RecipientIdentifier(Sin);

        Assert.Equal(id.Masked, id.ToString());
        Assert.DoesNotContain(Sin, id.ToString());
        Assert.DoesNotContain("123456", id.ToString()); // first six must not appear
    }

    [Theory]
    [InlineData("123-456-789")]
    [InlineData("123 456 789")]
    [InlineData(" 123456789 ")]
    public void Masked_StripsSeparators_ThenMasksToSinLayout(string formatted)
    {
        var id = new RecipientIdentifier(formatted);

        Assert.Equal("***-**-789", id.Masked);
    }

    [Fact]
    public void Masked_ForNonNineDigitBusinessNumber_MasksAllButLastThree()
    {
        // 15-char BN + program account, contains letters -> generic masking
        var id = new RecipientIdentifier("123456789RC0001");

        Assert.Equal("************001", id.Masked);
        Assert.DoesNotContain("123456789", id.Masked);
    }

    [Fact]
    public void Masked_ForVeryShortValue_MasksCompletely()
    {
        var id = new RecipientIdentifier("12");

        Assert.Equal("**", id.Masked);
    }

    [Fact]
    public void Equality_IgnoresSeparators()
    {
        var a = new RecipientIdentifier("123-45-6789");
        var b = new RecipientIdentifier("123456789");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DiffersForDifferentDigits()
    {
        var a = new RecipientIdentifier("123456789");
        var b = new RecipientIdentifier("987654321");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" - ")]
    public void Constructor_RejectsEmptyOrSeparatorOnlyInput(string? value)
    {
        Assert.Throws<ArgumentException>(() => new RecipientIdentifier(value!));
    }
}
