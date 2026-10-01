using Microsoft.Extensions.DependencyInjection;
using Persistence;
using Persistence.Services;

namespace Persistence.Tests.Composition;

/// <summary>
/// Spec 017 T01 (decision D-4) — the Development-local database allowance.
/// <para>
/// The strict guard is the default and stays the only rule a deployable host
/// can reach. The allowance is one explicit opt-in, and even with it an
/// unvalidated certificate is accepted for a loopback data source alone, with
/// encryption still explicit and every other rule still in force.
/// </para>
/// </summary>
public sealed class DevelopmentLoopbackConnectionTests
{
    private const string Suffix =
        "Database=TaxAiLocalDemo;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";

    [Theory] // AC-28
    [InlineData(@"(localdb)\MSSQLLocalDB")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("localhost,1433")]
    [InlineData(@"localhost\SQLEXPRESS")]
    [InlineData("tcp:localhost,1433")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1,14330")]
    [InlineData("::1")]
    [InlineData(".")]
    [InlineData(@".\SQLEXPRESS")]
    [InlineData("(local)")]
    public void ALoopbackDataSourceIsAcceptedOnlyWithTheExplicitAllowance(string dataSource)
    {
        var connectionString = $"Server={dataSource};{Suffix}";

        Assert.Same(
            connectionString,
            SqlConnectionStringGuard.RequireValid(
                connectionString,
                allowDevelopmentLoopbackCertificateTrust: true));

        // AC-31: the same string is refused on the default path, through both
        // the one-argument guard and an explicit false.
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(connectionString));
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(
                connectionString,
                allowDevelopmentLoopbackCertificateTrust: false));
    }

    [Theory] // AC-32
    [InlineData("sql.example.test")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.20,1433")]
    [InlineData("localhost.example.test")]
    [InlineData("notlocalhost")]
    [InlineData("tcp:sql.example.test,1433")]
    [InlineData(@"sql.example.test\localhost")]
    [InlineData(@"(localdb)\")]
    [InlineData(@"(localdb)\Instance,sql.example.test")]
    [InlineData("taxai.database.windows.net")]
    public void ANonLoopbackDataSourceIsRefusedEvenWithTheAllowance(string dataSource)
    {
        var failure = Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(
                $"Server={dataSource};{Suffix}",
                allowDevelopmentLoopbackCertificateTrust: true));

        Assert.DoesNotContain(dataSource, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // AC-33
    [InlineData("")]
    [InlineData("Encrypt=False;")]
    [InlineData("Encrypt=Optional;")]
    public void ExplicitEncryptionIsStillRequiredOnTheLoopbackPath(string encryption)
    {
        var connectionString =
            @"Server=(localdb)\MSSQLLocalDB;Database=TaxAiLocalDemo;Integrated Security=True;" +
            encryption +
            "TrustServerCertificate=True";

        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(
                connectionString,
                allowDevelopmentLoopbackCertificateTrust: true));
    }

    [Fact] // AC-34
    public void AHostNameOverrideIsStillRefusedOnTheLoopbackPath()
    {
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(
                $"Server=localhost;HostNameInCertificate=sql.example.test;{Suffix}",
                allowDevelopmentLoopbackCertificateTrust: true));
    }

    [Theory]
    [InlineData("Persist Security Info=True;")]
    [InlineData("Authentication=Active Directory Default;")]
    [InlineData("Authentication=Active Directory Interactive;")]
    public void EveryOtherRuleStillAppliesOnTheLoopbackPath(string unsafeSetting)
    {
        Assert.Throws<ArgumentException>(
            () => SqlConnectionStringGuard.RequireValid(
                "Server=localhost;Database=TaxAiLocalDemo;Encrypt=True;TrustServerCertificate=True;" +
                unsafeSetting,
                allowDevelopmentLoopbackCertificateTrust: true));
    }

    [Fact]
    public void TheAllowanceChangesNothingForAValidatedCertificate()
    {
        const string strict =
            "Server=sql.example.test;Database=TaxAi;Encrypt=True;TrustServerCertificate=False";

        Assert.Same(
            strict,
            SqlConnectionStringGuard.RequireValid(
                strict,
                allowDevelopmentLoopbackCertificateTrust: true));
    }

    [Fact] // AC-31
    public void TheOriginalRegistrationRefusesTheLocalShape()
    {
        Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddTaxAiPersistence($"Server=localhost;{Suffix}"));
        Assert.Throws<ArgumentException>(
            () => new ServiceCollection().AddTaxAiPersistence(
                $"Server=localhost;{Suffix}",
                allowDevelopmentLoopbackCertificateTrust: false));
    }

    [Fact] // AC-28
    public void TheAllowanceOverloadRegistersTheSameServicesAsTheOriginal()
    {
        var strict = new ServiceCollection().AddTaxAiPersistence(
            "Server=localhost;Database=TaxAiLocalDemo;Integrated Security=True;Encrypt=True");
        var local = new ServiceCollection().AddTaxAiPersistence(
            $"Server=localhost;{Suffix}",
            allowDevelopmentLoopbackCertificateTrust: true);

        Assert.Equal(
            strict.Select(Describe).OrderBy(text => text, StringComparer.Ordinal),
            local.Select(Describe).OrderBy(text => text, StringComparer.Ordinal));
    }

    [Fact]
    public void TheModuleReadsNoEnvironmentToDecideTheAllowance()
    {
        var root = FindRepositoryRoot();
        var guard = File.ReadAllText(Path.Combine(
            root, "src", "Persistence", "Services", "SqlConnectionStringGuard.cs"));
        var registration = File.ReadAllText(Path.Combine(
            root, "src", "Persistence", "PersistenceRegistration.cs"));

        foreach (var forbidden in new[]
                 {
                     "IHostEnvironment", "IWebHostEnvironment", "ASPNETCORE_ENVIRONMENT",
                     "DOTNET_ENVIRONMENT", "IsDevelopment", "EnvironmentName",
                 })
        {
            Assert.DoesNotContain(forbidden, guard, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, registration, StringComparison.Ordinal);
        }

        // The guard itself reads no process state at all, and the migration
        // tool's opt-in is one explicit argument rather than an ambient switch.
        Assert.DoesNotContain("Environment.", guard, StringComparison.Ordinal);
        Assert.Contains("\"--development-loopback\"", registration, StringComparison.Ordinal);
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        $"{descriptor.ServiceType.FullName}|{descriptor.Lifetime}|" +
        $"{descriptor.ImplementationType?.FullName ?? "factory"}";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }
}
