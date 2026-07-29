using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Persistence.Repositories;
using Persistence.Services;
using Tenancy.Contracts;

namespace Persistence;

/// <summary>
/// This module's single registration extension, and the only supported way in.
/// <para>
/// The host calls exactly one of these per contributing module. That is what
/// keeps the SQL Server provider out of the portal's project file while leaving
/// the composition root single: the portal names no ORM type, declares no ORM
/// package, and still gets a fully wired storage layer.
/// </para>
/// </summary>
public static class PersistenceRegistration
{
    /// <summary>
    /// Registers the context and the implementations of Tenancy's ports.
    /// <para>
    /// The connection string arrives from configuration providers outside source
    /// control — environment, user secrets, or a key vault. No tracked file in
    /// this repository declares one.
    /// </para>
    /// </summary>
    public static IServiceCollection AddTaxAiPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        var validatedConnectionString =
            SqlConnectionStringGuard.RequireValid(connectionString);

        services.AddDbContext<TaxAiDbContext>(
            options => options.UseSqlServer(validatedConnectionString));

        services.AddScoped<IReviewRecordRepository, SqlReviewRecordRepository>();
        services.AddScoped<IAuthenticationSessionStore, SqlAuthenticationSessionStore>();
        services.AddScoped<IIdentityDirectory, SqlIdentityDirectory>();
        services.AddScoped<IReviewRecordPurgeService, SqlReviewRecordPurgeService>();
        services.AddSingleton<IOpaqueIdentifierSource, GuidOpaqueIdentifierSource>();
        services.AddScoped<IDevelopmentFixtureSeeder, DevelopmentFixtureSeeder>();

        return services;
    }
}
