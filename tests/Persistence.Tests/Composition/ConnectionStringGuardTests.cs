using Persistence.Services;

namespace Persistence.Tests.Composition;

public sealed class ConnectionStringGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbsentConnectionStringIsRefused(string? connectionString)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(connectionString));

        Assert.Equal("connectionString", exception.ParamName);
        if (!string.IsNullOrEmpty(connectionString))
        {
            Assert.DoesNotContain(connectionString, exception.Message);
        }
    }

    [Fact]
    public void MalformedConnectionStringIsRefusedWithoutEchoingIt()
    {
        const string malformed = "SecretSentinel";

        var exception = Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(malformed));

        Assert.DoesNotContain(malformed, exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("Server=localhost;Database=TaxAiDevelopment")]
    [InlineData("Server=localhost;Database=TaxAiDevelopment;Encrypt=False")]
    public void ConnectionStringThatDoesNotExplicitlyRequireEncryptionIsRefused(
        string connectionString)
    {
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(connectionString));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("yes")]
    public void DisabledCertificateValidationIsRefused(string disabledValue)
    {
        var connectionString =
            "Server=localhost;Database=TaxAiDevelopment;Encrypt=True;" +
            $"TrustServerCertificate={disabledValue}";

        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(connectionString));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("Mandatory")]
    [InlineData("Strict")]
    public void RequiredEncryptionWithCertificateValidationIsAccepted(
        string encryptionValue)
    {
        var connectionString =
            "Server=localhost;Database=TaxAiDevelopment;" +
            $"Encrypt={encryptionValue};TrustServerCertificate=False";

        var result = SqlConnectionStringGuard.RequireValid(connectionString);

        Assert.Same(connectionString, result);
    }
}
