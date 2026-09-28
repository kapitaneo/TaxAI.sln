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

    // ------------------------------------------------ spec 016 T03 (AC-048)

    private const string ProductionShape =
        "Server=tcp:sql-taxai-prod-cc.database.windows.net,1433;Database=taxai;" +
        "Encrypt=Strict;TrustServerCertificate=False;" +
        "Authentication=Active Directory Managed Identity;User Id=33333333-3333-4333-8333-333333333333";

    [Theory]
    [InlineData(ProductionShape)]
    [InlineData("Server=tcp:sql-taxai-prod-cc.database.windows.net,1433;Database=taxai;Encrypt=Strict;Authentication=Active Directory MSI")]
    [InlineData("Server=localhost;Database=TaxAiDevelopment;Encrypt=True;Integrated Security=True")]
    [InlineData("Server=localhost;Database=TaxAiDevelopment;Encrypt=True;Authentication=SqlPassword;User ID=dev;Password=synthetic-dev-only")]
    public void TheManagedIdentityProductionShapeAndTheDevelopmentShapesAreAccepted(string connectionString)
    {
        Assert.Same(connectionString, SqlConnectionStringGuard.RequireValid(connectionString));
    }

    public static TheoryData<string, string> RefusedCredentialModes() => new()
    {
        { "Active Directory Password", "The database connection string must not use a stored, interactive or chained credential." },
        { "Active Directory Service Principal", "The database connection string must not use a stored, interactive or chained credential." },
        { "Active Directory Interactive", "The database connection string must not use a stored, interactive or chained credential." },
        { "Active Directory Device Code Flow", "The database connection string must not use a stored, interactive or chained credential." },
        { "Active Directory Default", "The database connection string must not use a stored, interactive or chained credential." },
        { "Active Directory Integrated", "The database connection string must not use a stored, interactive or chained credential." },
        { "Active Directory Workload Identity", "The database connection string must not use a stored, interactive or chained credential." },
    };

    [Theory] // No stored secret, prompt or credential chain.
    [MemberData(nameof(RefusedCredentialModes))]
    public void EveryEntraModeButTheManagedIdentityIsRefused(string mode, string expectedMessage)
    {
        var connectionString = ProductionShape.Replace(
            "Active Directory Managed Identity",
            mode,
            StringComparison.Ordinal);

        var exception = Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(connectionString));

        Assert.StartsWith(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sql-taxai-prod-cc", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AManagedIdentityCarryingAPasswordIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(ProductionShape + ";Password=SecretSentinel"));
    }

    [Fact]
    public void AManagedIdentityCombinedWithIntegratedSecurityIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(ProductionShape + ";Integrated Security=True"));
    }

    [Fact] // AC-048: no host-name override may weaken certificate validation.
    public void AHostNameInCertificateOverrideIsRefused()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(
                ProductionShape + ";HostNameInCertificate=planted.example"));

        Assert.StartsWith(
            "The database connection string must validate the server certificate.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("planted", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PersistedSecurityInformationIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(ProductionShape + ";Persist Security Info=True"));
    }

    [Theory]
    [InlineData("Optional")]
    [InlineData("False")]
    public void AnOptionalEncryptionModeIsRefusedEvenForTheManagedIdentity(string encrypt)
    {
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(
                ProductionShape.Replace("Encrypt=Strict", $"Encrypt={encrypt}", StringComparison.Ordinal)));
    }
}
