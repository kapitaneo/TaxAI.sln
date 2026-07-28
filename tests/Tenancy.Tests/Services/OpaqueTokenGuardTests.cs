using Tenancy.Models;
using Tenancy.Services;
using TaxLogicEngine.Models;

namespace Tenancy.Tests.Services;

public sealed class OpaqueTokenGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_RefusesMissingTokens(string? value)
    {
        var result = OpaqueTokenGuard.Validate(value, PersistedSchema.CaseIdMaxLength, TenancyFields.CaseId);

        Assert.Equal(
            new ValidationError(TenancyFields.CaseId, TenancyErrors.TokenEmpty),
            result);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Z")]
    [InlineData("a")]
    [InlineData("z")]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("-")]
    [InlineData("_")]
    [InlineData("Case_ABC-xyz-01234_5678")]
    public void Validate_AcceptsOnlyThePermittedAsciiTokenCharacters(string value)
    {
        var result = OpaqueTokenGuard.Validate(value, value.Length, TenancyFields.CaseId);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_AcceptsATokenAtTheMaximumLength()
    {
        var value = new string('A', PersistedSchema.CaseIdMaxLength);

        var result = OpaqueTokenGuard.Validate(
            value,
            PersistedSchema.CaseIdMaxLength,
            TenancyFields.CaseId);

        Assert.Null(result);
    }

    [Fact]
    public void Validate_RefusesATokenLongerThanTheMaximumWithoutTruncatingIt()
    {
        var value = new string('A', PersistedSchema.CaseIdMaxLength + 1);

        var result = OpaqueTokenGuard.Validate(
            value,
            PersistedSchema.CaseIdMaxLength,
            TenancyFields.CaseId);

        Assert.Equal(
            new ValidationError(TenancyFields.CaseId, TenancyErrors.TokenTooLong),
            result);
        Assert.Equal(PersistedSchema.CaseIdMaxLength + 1, value.Length);
    }

    [Theory]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("token\t")]
    [InlineData("token\r\n")]
    public void Validate_RefusesWhitespaceWithoutTrimming(string value)
    {
        var result = OpaqueTokenGuard.Validate(value, value.Length, TenancyFields.ParticipantId);

        Assert.Equal(
            new ValidationError(
                TenancyFields.ParticipantId,
                TenancyErrors.TokenCharacterNotPermitted),
            result);
    }

    [Theory]
    [InlineData("token/segment")]
    [InlineData("token.segment")]
    [InlineData("token:segment")]
    [InlineData("token+segment")]
    [InlineData("token\\segment")]
    [InlineData("token\0segment")]
    public void Validate_RefusesPunctuationAndControlCharactersOutsideTheAllowlist(string value)
    {
        var result = OpaqueTokenGuard.Validate(value, value.Length, TenancyFields.CaseId);

        Assert.Equal(
            new ValidationError(TenancyFields.CaseId, TenancyErrors.TokenCharacterNotPermitted),
            result);
    }

    [Theory]
    [InlineData("\u00E9")]
    [InlineData("e\u0301")]
    [InlineData("\u0661")]
    [InlineData("\uFF21")]
    public void Validate_RefusesUnicodeLookalikesWithoutNormalization(string value)
    {
        var result = OpaqueTokenGuard.Validate(value, value.Length, TenancyFields.CaseId);

        Assert.Equal(
            new ValidationError(TenancyFields.CaseId, TenancyErrors.TokenCharacterNotPermitted),
            result);
    }

    [Fact]
    public void Validate_PreservesTheSuppliedFieldOnFailure()
    {
        const string field = "OpaqueField";

        var result = OpaqueTokenGuard.Validate("not permitted", 32, field);

        Assert.Equal(new ValidationError(field, TenancyErrors.TokenCharacterNotPermitted), result);
    }
}
