using TaxLogicEngine.Models;
using Tenancy.Models;
using Tenancy.Services;

namespace Tenancy.Tests.Services;

public sealed class UtcInstantGuardTests
{
    [Fact]
    public void ZeroOffsetInstantIsAccepted()
    {
        var instant = new DateTimeOffset(
            new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var error = UtcInstantGuard.Validate(instant, TenancyFields.EvaluationInstant);

        Assert.Null(error);
    }

    [Fact]
    public void ZeroOffsetInstantCreatedFromUnspecifiedDateTimeIsAccepted()
    {
        var unspecified = new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var instant = new DateTimeOffset(unspecified, TimeSpan.Zero);

        var error = UtcInstantGuard.Validate(instant, TenancyFields.EvaluationInstant);

        Assert.Null(error);
    }

    [Fact]
    public void DefaultInstantIsAcceptedBecauseItsOffsetIsZero()
    {
        var error = UtcInstantGuard.Validate(
            default,
            TenancyFields.EvaluationInstant);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(5.5)]
    [InlineData(-3.5)]
    public void NonZeroOffsetIsRejectedWithoutConversion(double offsetHours)
    {
        var offset = TimeSpan.FromHours(offsetHours);
        var instant = new DateTimeOffset(2033, 1, 1, 0, 0, 0, offset);
        var original = instant;

        var error = UtcInstantGuard.Validate(
            instant,
            TenancyFields.EvaluationInstant);

        Assert.Equal(
            new ValidationError(
                TenancyFields.EvaluationInstant,
                TenancyErrors.InstantNotUtc),
            error);
        Assert.Equal(original, instant);
        Assert.Equal(offset, instant.Offset);
    }

    [Fact]
    public void RejectionUsesTheSuppliedFieldAndFixedMessage()
    {
        var instant = new DateTimeOffset(
            2033,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.FromMinutes(30));

        var error = UtcInstantGuard.Validate(
            instant,
            TenancyFields.CreatedAtUtc);

        Assert.NotNull(error);
        Assert.Equal(TenancyFields.CreatedAtUtc, error.Field);
        Assert.Equal(TenancyErrors.InstantNotUtc, error.Message);
    }
}
