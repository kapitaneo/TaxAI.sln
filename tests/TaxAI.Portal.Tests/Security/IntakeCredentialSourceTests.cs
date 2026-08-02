using System.Reflection;
using ClientIntake.Contracts;
using ClientIntake.Models;

namespace TaxAI.Portal.Tests.Security;

public sealed class IntakeCredentialSourceTests
{
    [Fact]
    public void CredentialsAndIdentifiersAreHighEntropyStructuralValues()
    {
        var source = CreateSource(0x31);

        var firstCredential = source.CreateCredential();
        var secondCredential = source.CreateCredential();
        var firstIdentifier = source.CreateIdentifier();
        var secondIdentifier = source.CreateIdentifier();

        Assert.NotEqual(firstCredential.Reveal(), secondCredential.Reveal());
        Assert.Equal(43, firstCredential.Reveal().Length);
        Assert.True(IntakeSecret.TryParse(firstCredential.Reveal(), out _));
        Assert.NotEqual(Guid.Empty, firstIdentifier);
        Assert.NotEqual(firstIdentifier, secondIdentifier);
        Assert.Equal("CryptographicIntakeCredentialSource", source.ToString());
        Assert.DoesNotContain(firstCredential.Reveal(), source.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DigestsAreDeterministicPurposeAndKeySeparated()
    {
        var firstSource = CreateSource(0x41);
        var secondSource = CreateSource(0x42);
        var credential = firstSource.CreateCredential();

        var bootstrap = firstSource.Digest(
            credential,
            IntakeTokenPurpose.BootstrapLink);
        var repeated = firstSource.Digest(
            credential,
            IntakeTokenPurpose.BootstrapLink);
        var session = firstSource.Digest(
            credential,
            IntakeTokenPurpose.ClientSession);
        var otherKey = secondSource.Digest(
            credential,
            IntakeTokenPurpose.BootstrapLink);

        Assert.Equal(bootstrap, repeated);
        Assert.NotEqual(bootstrap, session);
        Assert.NotEqual(bootstrap, otherKey);
        Assert.Equal(32, bootstrap.Bytes.Count);
        Assert.Equal(nameof(IntakeTokenDigest), bootstrap.ToString());
    }

    [Fact]
    public void UndefinedPurposeIsRefused()
    {
        var source = CreateSource(0x51);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            source.Digest(source.CreateCredential(), (IntakeTokenPurpose)99));
    }

    private static IIntakeCredentialSource CreateSource(byte keyByte)
    {
        var type = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.CryptographicIntakeCredentialSource",
            throwOnError: true)!;
        return (IIntakeCredentialSource)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [Enumerable.Repeat(keyByte, 32).ToArray()],
            culture: null)!;
    }
}
