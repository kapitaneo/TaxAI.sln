using Microsoft.Extensions.DependencyInjection;
using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Contracts;

namespace TaxAI.AzureInfrastructure;

/// <summary>
/// The library's single registration extension, called only by the portal's
/// composition root.
/// <para>
/// In spec 016 T01 it registers the working production validation boundary:
/// the supplied profile, the structural validator and the managed-identity
/// binding probe. It registers no source store, reader, key-ring, provider or
/// telemetry adapter; those arrive with the slices that implement them, and
/// their absence keeps production admission refused.
/// </para>
/// </summary>
public static class AzureInfrastructureRegistration
{
    public static IServiceCollection AddTaxAiAzureInfrastructure(
        this IServiceCollection services,
        AzureInfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<ProductionConfigurationValidator>();
        services.AddSingleton<IManagedIdentityBindingProbe, ManagedIdentityBindingProbe>(
            _ => new ManagedIdentityBindingProbe());

        return services;
    }
}
