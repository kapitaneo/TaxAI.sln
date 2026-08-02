using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClientIntake.Contracts;
using Persistence.Repositories;
using Persistence.Services;
using Tenancy.Contracts;

namespace Persistence.Tests.Composition;

public sealed class PersistenceRegistrationTests
{
    private const string ValidConnectionString =
        "Server=localhost;Database=TaxAiDevelopment;Encrypt=True;" +
        "TrustServerCertificate=False;Integrated Security=True";

    [Fact]
    public void RegistrationReturnsTheOriginalCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddTaxAiPersistence(ValidConnectionString);

        Assert.Same(services, returned);
    }

    [Fact]
    public void RegistrationAddsTheContextAndEveryTenancyPortWithApprovedLifetimes()
    {
        var services = new ServiceCollection();

        services.AddTaxAiPersistence(ValidConnectionString);

        AssertDescriptor<TaxAiDbContext, TaxAiDbContext>(
            services,
            ServiceLifetime.Scoped);
        AssertDescriptor<IReviewRecordRepository, SqlReviewRecordRepository>(
            services,
            ServiceLifetime.Scoped);
        AssertDescriptor<IIntakeStore, SqlIntakeStore>(
            services,
            ServiceLifetime.Scoped);
        AssertDescriptor<IAuthenticationSessionStore, SqlAuthenticationSessionStore>(
            services,
            ServiceLifetime.Scoped);
        AssertDescriptor<IIdentityDirectory, SqlIdentityDirectory>(
            services,
            ServiceLifetime.Scoped);
        AssertDescriptor<IReviewRecordPurgeService, SqlReviewRecordPurgeService>(
            services,
            ServiceLifetime.Scoped);
        AssertDescriptor<IOpaqueIdentifierSource, GuidOpaqueIdentifierSource>(
            services,
            ServiceLifetime.Singleton);
        AssertDescriptor<IDevelopmentFixtureSeeder, DevelopmentFixtureSeeder>(
            services,
            ServiceLifetime.Scoped);
    }

    [Fact]
    public void RegisteredContextUsesSqlServerWithoutOpeningAConnection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaxAiPersistence(ValidConnectionString);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TaxAiDbContext>();

        Assert.Equal(
            "Microsoft.EntityFrameworkCore.SqlServer",
            context.Database.ProviderName);
        Assert.Equal(
            System.Data.ConnectionState.Closed,
            context.Database.GetDbConnection().State);
    }

    [Fact]
    public void RegisteredServicesResolveFromOneValidatedScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaxAiPersistence(ValidConnectionString);

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        using var scope = provider.CreateScope();

        Assert.IsType<SqlReviewRecordRepository>(
            scope.ServiceProvider.GetRequiredService<IReviewRecordRepository>());
        Assert.IsType<SqlIntakeStore>(
            scope.ServiceProvider.GetRequiredService<IIntakeStore>());
        Assert.IsType<SqlAuthenticationSessionStore>(
            scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>());
        Assert.IsType<SqlIdentityDirectory>(
            scope.ServiceProvider.GetRequiredService<IIdentityDirectory>());
        Assert.IsType<SqlReviewRecordPurgeService>(
            scope.ServiceProvider.GetRequiredService<IReviewRecordPurgeService>());
        Assert.IsType<GuidOpaqueIdentifierSource>(
            scope.ServiceProvider.GetRequiredService<IOpaqueIdentifierSource>());
        Assert.IsType<DevelopmentFixtureSeeder>(
            scope.ServiceProvider.GetRequiredService<IDevelopmentFixtureSeeder>());
    }

    [Fact]
    public void RegistrationAddsNoHostedOrBackgroundService()
    {
        var services = new ServiceCollection();

        services.AddTaxAiPersistence(ValidConnectionString);

        Assert.DoesNotContain(
            services,
            descriptor => string.Equals(
                descriptor.ServiceType.FullName,
                "Microsoft.Extensions.Hosting.IHostedService",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OpaqueIdentifierSourceMintsNonEmptyDistinctValues()
    {
        var source = new GuidOpaqueIdentifierSource();

        var first = source.NewIdentifier();
        var second = source.NewIdentifier();

        Assert.NotEqual(Guid.Empty, first);
        Assert.NotEqual(Guid.Empty, second);
        Assert.NotEqual(first, second);
        Assert.Equal(nameof(GuidOpaqueIdentifierSource), source.ToString());
    }

    [Fact]
    public void NullCollectionIsRefusedBeforeRegistration()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(
            () => services.AddTaxAiPersistence(ValidConnectionString));
    }

    private static void AssertDescriptor<TService, TImplementation>(
        IServiceCollection services,
        ServiceLifetime lifetime)
    {
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(TService));

        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Equal(lifetime, descriptor.Lifetime);
    }
}
