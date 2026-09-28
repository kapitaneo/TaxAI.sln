using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Keys;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure;

/// <summary>
/// The library's single registration extension, called only by the portal's
/// composition root.
/// <para>
/// In spec 016 T01 it registered the working production validation boundary:
/// the supplied profile, the structural validator and the managed-identity
/// binding probe. T03 adds the approved-resource probe that verifies the real
/// Key Vault key posture, wrap capability and the shared protected key ring.
/// The key ring itself is configured by the portal's identity wiring through
/// <see cref="Protection.AzureDataProtectionConfiguration"/>, and intake
/// credential material is loaded before registration by
/// <see cref="Keys.KeyVaultRuntimeMaterialProvider"/>. It registers no source
/// store, reader, provider or telemetry adapter; those arrive with the slices
/// that implement them, and their absence keeps production admission refused.
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

        // Resolved only by the admission gate, and only after the structural
        // profile and the issued identity are both verified.
        services.AddSingleton<IApprovedResourceProbe>(provider =>
            TryCreateWorkloadCredential(options, out var credential)
                ? new ApprovedResourceProbe(
                    options,
                    credential!,
                    provider.GetRequiredService<IDataProtectionProvider>())
                : throw new InvalidOperationException(
                    "The approved-resource probe requires a valid managed identity binding."));

        return services;
    }

    /// <summary>
    /// Loads the production intake credential material from the profile's vault
    /// with one bounded read by the profile's managed identity (spec 016 T03).
    /// A profile whose deployment identity, managed-identity binding, vault
    /// binding, public-access posture or probe deadline is not structurally
    /// valid is never contacted: the result is an unavailable refusal.
    /// </summary>
    public static RuntimeMaterial LoadProductionRuntimeMaterial(
        AzureInfrastructureOptions options,
        string? databaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        var structural = new ProductionConfigurationValidator()
            .Validate(options, databaseConnectionString);
        if (structural.Refusals.Any(refusal => refusal is
                ProductionAdmissionRefusal.DeploymentIdentityInvalid or
                ProductionAdmissionRefusal.ManagedIdentityBindingMissing or
                ProductionAdmissionRefusal.KeyVaultConfigurationMissing or
                ProductionAdmissionRefusal.KeyVaultBindingInvalid or
                ProductionAdmissionRefusal.PublicEndpointConfigured or
                ProductionAdmissionRefusal.ResilienceUnbounded) ||
            !ProductionConfigurationValidator.TryReadProbeDeadline(options, out var deadline) ||
            !TryCreateWorkloadCredential(options, out var credential))
        {
            return RuntimeMaterial.Refused(RuntimeMaterialOutcome.Unavailable);
        }

        using var cancellation = new CancellationTokenSource(deadline);
        return new KeyVaultRuntimeMaterialProvider(
                new Uri(options.KeyVault.Endpoint!),
                credential!,
                TimeProvider.System)
            .LoadAsync(cancellation.Token)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// The one workload credential: the platform managed identity for the
    /// configured user-assigned client identifier, with no chain and no
    /// developer, environment or interactive fallback. Returns
    /// <see langword="false"/> when the client identifier is not a valid,
    /// non-empty GUID, so a malformed profile never yields a credential.
    /// </summary>
    public static bool TryCreateWorkloadCredential(
        AzureInfrastructureOptions options,
        out TokenCredential? credential)
    {
        ArgumentNullException.ThrowIfNull(options);

        credential = null;
        if (!Guid.TryParseExact(options.ManagedIdentity.ClientId, "D", out var clientId) ||
            clientId == Guid.Empty)
        {
            return false;
        }

        credential = new ManagedIdentityCredential(
            new ManagedIdentityCredentialOptions(
                ManagedIdentityId.FromUserAssignedClientId(clientId.ToString("D"))));
        return true;
    }
}
