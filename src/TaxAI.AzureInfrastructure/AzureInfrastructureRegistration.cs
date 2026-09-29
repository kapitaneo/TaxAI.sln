using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using ClientIntake.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Content;
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
/// <see cref="Keys.KeyVaultRuntimeMaterialProvider"/>.
/// </para>
/// <para>
/// Spec 016 T04 adds the working source adapters: the write-only Blob
/// <see cref="IIntakeContentStore"/>, the scoped source-object purger, and the
/// extraction-only reader, registered <b>as its concrete type only</b> — never
/// as a general <c>IExtractionContentReader</c> service — so the extraction
/// workflow is its only consumer. They are registered only when the profile
/// yields a usable managed identity, source endpoint and wrapping key and the
/// supplied content settings are usable; otherwise nothing is registered and
/// admission refuses the missing store and reader. It registers no provider or
/// telemetry adapter.
/// </para>
/// </summary>
public static class AzureInfrastructureRegistration
{
    public static IServiceCollection AddTaxAiAzureInfrastructure(
        this IServiceCollection services,
        AzureInfrastructureOptions options,
        SourceContentSettings content)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(content);

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

        if (content.IsUsable &&
            TryCreateWorkloadCredential(options, out var workload) &&
            TryCreateSourceContainer(options, workload!, out var container) &&
            TryReadVersionlessKey(options.SourceEnvelope.KeyUri, out var envelopeKey))
        {
            var wrapper = new KeyVaultContentKeyWrapper(envelopeKey!, workload!);
            var purger = new AzureSourceObjectPurger(container!);
            var fingerprintKey = content.FingerprintKey;
            var maximumSourceBytes = content.MaximumSourceBytes;

            services.AddSingleton(wrapper);
            services.AddSingleton<ISourceObjectPurger>(purger);
            services.AddScoped<AzureIntakeContentStore>(provider => new AzureIntakeContentStore(
                container!,
                wrapper,
                provider.GetRequiredService<ISourceRegistryStore>(),
                purger,
                fingerprintKey,
                provider.GetRequiredService<TimeProvider>()));
            services.AddScoped<IIntakeContentStore>(provider =>
                provider.GetRequiredService<AzureIntakeContentStore>());
            services.AddScoped<AzureExtractionContentReader>(provider => new AzureExtractionContentReader(
                container!,
                wrapper,
                provider.GetRequiredService<ISourceRegistryStore>(),
                maximumSourceBytes));
        }

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

    /// <summary>
    /// The private source container client over the workload credential, or
    /// <see langword="false"/> when the profile's source endpoint is not an
    /// HTTPS Blob service root. No SAS, shared key or connection string is ever
    /// accepted: the only credential is the managed identity.
    /// </summary>
    public static bool TryCreateSourceContainer(
        AzureInfrastructureOptions options,
        TokenCredential credential,
        out BlobContainerClient? container)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);

        container = null;
        if (!TryReadBlobServiceRoot(options.SourceStorage.Endpoint, out var root))
        {
            return false;
        }

        container = new BlobServiceClient(root, credential)
            .GetBlobContainerClient(AzureIntakeContentStore.SourceContainerName);
        return true;
    }

    internal static bool TryReadBlobServiceRoot(string? candidate, out Uri? root)
    {
        root = null;
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !uri.IsDefaultPort ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.HostNameType != UriHostNameType.Dns ||
            !uri.IdnHost.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        root = uri;
        return true;
    }

    private static bool TryReadVersionlessKey(string? candidate, out Uri? key)
    {
        key = null;
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !KeyIdentifier.TryReadVersionless(uri, out _, out _))
        {
            return false;
        }

        key = uri;
        return true;
    }
}

/// <summary>
/// The two values the source adapters take from the host's already-validated
/// configuration: the production fingerprint key (loaded from Key Vault) and
/// the extraction source bound. An unusable value registers no adapter; it is
/// never replaced by a default.
/// </summary>
public sealed class SourceContentSettings
{
    private readonly byte[] _fingerprintKey;

    public SourceContentSettings(ReadOnlySpan<byte> fingerprintKey, long maximumSourceBytes)
    {
        _fingerprintKey = fingerprintKey.ToArray();
        MaximumSourceBytes = maximumSourceBytes;
        IsUsable = _fingerprintKey.Length == 32 &&
                   _fingerprintKey.Any(value => value != 0) &&
                   maximumSourceBytes > 0;
    }

    public bool IsUsable { get; }

    public ReadOnlyMemory<byte> FingerprintKey => _fingerprintKey;

    public long MaximumSourceBytes { get; }

    public override string ToString() => nameof(SourceContentSettings);
}
