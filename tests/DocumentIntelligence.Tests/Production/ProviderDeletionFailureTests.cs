using System.Net;

namespace DocumentIntelligence.Tests.Production;

public sealed class ProviderDeletionFailureTests
{
    [Theory]
    [InlineData("classify")]
    [InlineData("t4")]
    public async Task ExhaustedDeletionNeverReleasesAResult(string method)
    {
        using var wire = new ProviderWire { DeleteStatus = HttpStatusCode.ServiceUnavailable };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider, method));
        Assert.Equal(3, wire.Methods.Count(method => method == "DELETE"));
        Assert.Single(wire.Methods, method => method == "POST");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(202)]
    [InlineData(302)]
    [InlineData(404)]
    public async Task OnlyExplicitDeleteAcknowledgementPermitsRelease(int status)
    {
        using var wire = new ProviderWire { DeleteStatus = (HttpStatusCode)status };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
    }

    [Fact]
    public async Task SuccessfulBoundedDeleteRetryPermitsRelease()
    {
        using var wire = new ProviderWire { DeleteFailures = 1 };
        using var provider = wire.Provider();
        Assert.NotNull(await wire.Invoke(provider));
        Assert.Equal(2, wire.Methods.Count(method => method == "DELETE"));
    }

    [Fact]
    public async Task NoKnownIdentifierCannotClaimExplicitDeletion()
    {
        using var wire = new ProviderWire { OmitLocation = true };
        using var provider = wire.Provider();
        Assert.Null(await wire.Invoke(provider));
        Assert.Equal(new[] { "POST" }, wire.Methods);
    }
}
