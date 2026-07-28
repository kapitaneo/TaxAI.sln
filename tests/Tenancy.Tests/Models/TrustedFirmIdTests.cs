using Tenancy.Models;

namespace Tenancy.Tests.Models;

public sealed class TrustedFirmIdTests
{
    private static readonly Guid FirmGuid =
        Guid.Parse("12ab34cd-56ef-78ab-90cd-ef12ab34cd56");

    [Fact]
    public void FromGuid_PreservesTheResolvedValue()
    {
        var firmId = TrustedFirmId.FromGuid(FirmGuid);

        Assert.Equal(FirmGuid, firmId.Value);
        Assert.Equal("12ab34cd-56ef-78ab-90cd-ef12ab34cd56", firmId.ToCanonicalString());
    }

    [Fact]
    public void TryParse_AcceptsOnlyTheExactCanonicalRepresentation()
    {
        var candidate = FirmGuid.ToString("D");

        var parsed = TrustedFirmId.TryParse(candidate, out var firmId);

        Assert.True(parsed);
        Assert.NotNull(firmId);
        Assert.Equal(FirmGuid, firmId.Value);
        Assert.Equal(candidate, firmId.ToCanonicalString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-guid")]
    public void TryParse_RefusesValuesThatAreNotCanonicalGuidText(string? candidate)
    {
        var parsed = TrustedFirmId.TryParse(candidate, out var firmId);

        Assert.False(parsed);
        Assert.Null(firmId);
    }

    [Fact]
    public void TryParse_RefusesUppercaseRatherThanCaseFolding()
    {
        var candidate = FirmGuid.ToString("D").ToUpperInvariant();

        var parsed = TrustedFirmId.TryParse(candidate, out var firmId);

        Assert.False(parsed);
        Assert.Null(firmId);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    [InlineData("X")]
    public void TryParse_RefusesEveryNonCanonicalGuidFormat(string format)
    {
        var candidate = FirmGuid.ToString(format);

        var parsed = TrustedFirmId.TryParse(candidate, out var firmId);

        Assert.False(parsed);
        Assert.Null(firmId);
    }

    [Fact]
    public void TryParse_RefusesSurroundingWhitespaceRatherThanTrimming()
    {
        var candidate = $" {FirmGuid:D} ";

        var parsed = TrustedFirmId.TryParse(candidate, out var firmId);

        Assert.False(parsed);
        Assert.Null(firmId);
    }

    [Fact]
    public void EqualityAndHashCode_AreValueBased()
    {
        var first = TrustedFirmId.FromGuid(FirmGuid);
        var second = TrustedFirmId.FromGuid(FirmGuid);
        var different = TrustedFirmId.FromGuid(
            Guid.Parse("ab12cd34-ef56-78ab-90cd-12ef34ab56cd"));

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(different));
        Assert.False(first.Equals(null));
        Assert.False(first.Equals(FirmGuid));
    }

    [Fact]
    public void ToString_EmitsOnlyTheFixedTypeIdentity()
    {
        var firmId = TrustedFirmId.FromGuid(FirmGuid);

        Assert.Equal(nameof(TrustedFirmId), firmId.ToString());
        Assert.DoesNotContain(firmId.ToCanonicalString(), firmId.ToString(), StringComparison.Ordinal);
    }
}
