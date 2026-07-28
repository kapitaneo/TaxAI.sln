using Tenancy.Models;
using Tenancy.Services;
using TaxLogicEngine.Models;

namespace Tenancy.Tests.Services;

public sealed class TaxpayerTextGuardTests
{
    private const int MaxLength = 12;

    [Fact]
    public void NullValue_IsAccepted()
    {
        var error = TaxpayerTextGuard.Validate(
            null,
            MaxLength,
            TenancyFields.TaxpayerDisplayName);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Test Name")]
    [InlineData("12345678")]
    [InlineData("1234-5678")]
    public void ValueWithinLengthWithoutForbiddenDigitRun_IsAccepted(string value)
    {
        var error = TaxpayerTextGuard.Validate(
            value,
            MaxLength,
            TenancyFields.TaxpayerDisplayName);

        Assert.Null(error);
    }

    [Fact]
    public void ValueAtDeclaredColumnLength_IsAcceptedUnchanged()
    {
        var value = new string('A', MaxLength);

        var error = TaxpayerTextGuard.Validate(
            value,
            MaxLength,
            TenancyFields.EmployerDisplayName);

        Assert.Null(error);
        Assert.Equal(MaxLength, value.Length);
    }

    [Fact]
    public void ValueExceedingDeclaredColumnLength_IsRefusedWithoutTruncation()
    {
        var value = new string('A', MaxLength + 1);

        var error = TaxpayerTextGuard.Validate(
            value,
            MaxLength,
            TenancyFields.TaxpayerDisplayName);

        Assert.Equal(
            new ValidationError(
                TenancyFields.TaxpayerDisplayName,
                TenancyErrors.TextTooLong),
            error);
        Assert.Equal(MaxLength + 1, value.Length);
    }

    [Fact]
    public void NineConsecutiveDigits_AreRefused()
    {
        var value = $"Name {new string('1', 9)}";

        var error = TaxpayerTextGuard.Validate(
            value,
            value.Length,
            TenancyFields.TaxpayerDisplayName);

        Assert.Equal(
            new ValidationError(
                TenancyFields.TaxpayerDisplayName,
                TenancyErrors.TextContainsDigitRun),
            error);
    }

    [Fact]
    public void NineConsecutiveUnicodeDigits_AreRefused()
    {
        var value = new string('\u0661', 9);

        var error = TaxpayerTextGuard.Validate(
            value,
            value.Length,
            TenancyFields.EmployerDisplayName);

        Assert.Equal(
            new ValidationError(
                TenancyFields.EmployerDisplayName,
                TenancyErrors.TextContainsDigitRun),
            error);
    }

    [Fact]
    public void DigitRunsSeparatedByNonDigits_AreAccepted()
    {
        var value = $"{new string('2', 8)}-{new string('3', 8)}";

        var error = TaxpayerTextGuard.Validate(
            value,
            value.Length,
            TenancyFields.EmployerDisplayName);

        Assert.Null(error);
    }
}
