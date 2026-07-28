using Microsoft.Extensions.DependencyInjection;

namespace Persistence;

/// <summary>
/// This module's single registration extension, and the only supported way in.
/// <para>
/// The host calls exactly one of these per contributing module. That is what
/// keeps the SQL Server provider out of the portal's project file while leaving
/// the composition root single: the portal names no ORM type, declares no ORM
/// package, and still gets a fully wired storage layer.
/// </para>
/// <para>Wave 0 contract stage: behaviour is implemented in a later task.</para>
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
        string connectionString) => throw new NotImplementedException();
}
