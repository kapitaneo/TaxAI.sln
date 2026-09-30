using DocumentIntelligence.Production;
using ExtractionWorkflow.Contracts;
using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Contracts;
using Tenancy.Services;

namespace TaxAI.Portal.Production;

/// <summary>Rechecks the current exact release, both assurances, identity and protected resources on every call.</summary>
internal sealed class ExtractionAvailabilityGate(
    IServiceProvider services, IConfiguration configuration, IWebHostEnvironment environment,
    TimeProvider clock) : IExtractionAvailabilityGate
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (PortalStartupGuards.RunsInDevelopment(environment.EnvironmentName)) return true;
        if (environment.EnvironmentName != Environments.Production) return false;
        try
        {
            var snapshot = ProductionConfiguration.Read(configuration);
            var options = snapshot.Options;
            var configured = ExtractionConfiguration.ProductionOptions(configuration);
            if (configured is null || snapshot.HasUnapprovedEntry || snapshot.ManifestInvalid ||
                options.ArtifactVersion != ProductionAdmissionGate.RunningArtifactVersion() ||
                ExtractionConfiguration.ProductionShapeRefusals(configuration).Count != 0 ||
                !services.GetRequiredService<ProductionConfigurationValidator>().Validate(options,
                    configuration.GetConnectionString(PortalStartupGuards.DatabaseConnectionStringName)).IsAdmitted ||
                !ProductionConfigurationValidator.TryCreateReleaseBinding(options, snapshot.ConfigurationDigest, out var release) ||
                !ProductionEvidencePolicy.Evaluate(snapshot.Manifest, release!, clock.GetUtcNow()).IsAdmitted ||
                !ProductionConfigurationValidator.TryCreateManagedIdentityBinding(options, out var binding) ||
                !ProductionConfigurationValidator.TryReadProbeDeadline(options, out var deadline)) return false;

            var provider = services.GetService<IDocumentIntelligenceProvider>() as AzureDocumentIntelligenceProvider;
            if (provider is null || !Matches(provider.Options, configured)) return false;
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(deadline);
            if (await services.GetRequiredService<IManagedIdentityBindingProbe>()
                .VerifyAsync(binding!, bounded.Token).ConfigureAwait(false) != ManagedIdentityBindingOutcome.Verified) return false;
            var resources = await services.GetRequiredService<IApprovedResourceProbe>()
                .ProbeAsync(bounded.Token).ConfigureAwait(false);
            return resources.Refusals.Count == 0 && !bounded.IsCancellationRequested &&
                await provider.IsReachableAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (Exception) { return false; }
    }

    internal static bool Matches(AzureDocumentIntelligenceOptions actual, AzureDocumentIntelligenceOptions expected) =>
        actual.Endpoint == expected.Endpoint && actual.ResourceId == expected.ResourceId &&
        actual.Region == expected.Region && actual.ManagedIdentityClientId == expected.ManagedIdentityClientId &&
        actual.ApiVersion == expected.ApiVersion && actual.Model == expected.Model && actual.Features == expected.Features &&
        actual.MaximumSourceBytes == expected.MaximumSourceBytes && actual.MaximumAttempts == expected.MaximumAttempts &&
        actual.OperationDeadline == expected.OperationDeadline && actual.CleanupDeadline == expected.CleanupDeadline;
}
