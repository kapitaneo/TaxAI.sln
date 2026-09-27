using System.Reflection;
using ClientIntake.Contracts;
using DocumentIntelligence;
using ExtractionWorkflow.Contracts;
using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace TaxAI.Portal.Production;

/// <summary>
/// The spec 016 production admission gate (FR-14): the enforced replacement for
/// the unconditional non-Development refusal.
/// <para>
/// A deployable host is admitted only when <b>every</b> one of these holds at
/// once: the host environment is exactly Production; the closed production
/// profile passes <see cref="ProductionConfigurationValidator"/>; the intake and
/// extraction settings have the production shape; the container holds the
/// approved production registrations and no Development substitute; the running
/// artifact is the exact release the profile names; the managed identity the
/// platform actually issues matches the configured binding; and the evidence
/// manifest proves all twenty-four DEP items, both provider assurances and both
/// role approvals for this exact artifact and configuration digest.
/// </para>
/// <para>
/// All refusals are accumulated so each is independently observable (DEP-23),
/// and the thrown message is one fixed sentence that quotes no configured value.
/// There is no switch, environment rename or operator flag that skips a check.
/// </para>
/// <para>
/// <b>As of T01 no deployable host can be admitted.</b> No approved production
/// Document Intelligence adapter, source store or source reader exists yet, so
/// their absence is itself a refusal rather than a placeholder or a permissive
/// default. Later slices replace each absence with a real, verified
/// registration; the provider assurance gaps keep admission refused until
/// authoritative evidence resolves them.
/// </para>
/// </summary>
internal static class ProductionAdmissionGate
{
    internal const string RefusalMessage =
        "Production startup is refused: the exact approved production configuration, registrations, identity and release evidence are not all present and valid.";

    /// <summary>
    /// Runs the gate for a built host. Development hosts never admit production
    /// data and are not evaluated; the in-process TestServer is a verification
    /// surface that cannot be deployed and is never admitted either. Every other
    /// host is evaluated and refused unless admitted.
    /// </summary>
    internal static void RequireAdmission(
        IServiceProvider services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentName);

        if (PortalStartupGuards.RunsInDevelopment(environmentName) ||
            PortalStartupGuards.RunsInProcessTestServer(services))
        {
            return;
        }

        var readiness = Evaluate(
            services,
            configuration,
            environmentName,
            services.GetRequiredService<TimeProvider>().GetUtcNow());

        if (!readiness.IsAdmitted)
        {
            throw new InvalidOperationException(RefusalMessage);
        }
    }

    /// <summary>
    /// Evaluates every admission rule against a built container and returns the
    /// complete refusal set. Never throws for a misconfiguration.
    /// </summary>
    internal static ProductionReadiness Evaluate(
        IServiceProvider services,
        IConfiguration configuration,
        string environmentName,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentName);

        var refusals = new List<ProductionAdmissionRefusal>();

        if (!string.Equals(
                environmentName,
                ProductionConfigurationValidator.ProductionEnvironment,
                StringComparison.Ordinal))
        {
            refusals.Add(ProductionAdmissionRefusal.EnvironmentNotProduction);
        }

        var snapshot = ProductionConfiguration.Read(configuration);
        if (snapshot.HasUnapprovedEntry)
        {
            refusals.Add(ProductionAdmissionRefusal.UnapprovedConfigurationEntry);
        }

        var validator = services.GetService<ProductionConfigurationValidator>();
        var structural = validator?.Validate(
            snapshot.Options,
            configuration.GetConnectionString(PortalStartupGuards.DatabaseConnectionStringName));
        if (structural is null)
        {
            refusals.Add(ProductionAdmissionRefusal.InfrastructureValidationUnregistered);
        }

        if (!string.Equals(
                snapshot.Options.ArtifactVersion,
                RunningArtifactVersion(),
                StringComparison.Ordinal))
        {
            refusals.Add(ProductionAdmissionRefusal.ReleaseBindingMismatch);
        }

        return ProductionReadiness.FromRefusals(refusals
            .Concat(structural?.Refusals ?? Array.Empty<ProductionAdmissionRefusal>())
            .Concat(IntakeConfiguration.ProductionShapeRefusals(configuration))
            .Concat(ExtractionConfiguration.ProductionShapeRefusals(configuration))
            .Concat(RegistrationRefusals(services))
            .Concat(EvidenceRefusals(snapshot, evaluatedAtUtc))
            .Concat(IdentityRefusals(services, snapshot.Options, structural)));
    }

    /// <summary>
    /// The informational version stamped on this build. The release pipeline
    /// sets it to the exact artifact version the evidence names.
    /// </summary>
    internal static string? RunningArtifactVersion() =>
        typeof(ProductionAdmissionGate).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

    /// <summary>
    /// Inspects actual registrations, not option strings (FR-14).
    /// </summary>
    private static IEnumerable<ProductionAdmissionRefusal> RegistrationRefusals(
        IServiceProvider services)
    {
        var registered = services.GetService<IServiceProviderIsService>();

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        // No production Document Intelligence adapter is approved in T01. The
        // deterministic provider is a Development substitute; any other
        // registration is not an approved adapter yet. Both refuse.
        var intelligence = provider.GetService<IDocumentIntelligenceProvider>();
        if (intelligence is null)
        {
            yield return ProductionAdmissionRefusal.ProductionDocumentIntelligenceAdapterMissing;
        }
        else if (intelligence is DeterministicDocumentIntelligenceProvider)
        {
            yield return ProductionAdmissionRefusal.DevelopmentProviderConfigured;
        }
        else
        {
            yield return ProductionAdmissionRefusal.ProductionDocumentIntelligenceAdapterMissing;
        }

        // The Development encrypted filesystem store is the only source store
        // this build contains. Its registration, or its use behind the intake
        // port, is a local-store refusal; no approved production store exists.
        if (registered?.IsService(typeof(DevelopmentEncryptedIntakeContentStore)) == true)
        {
            yield return ProductionAdmissionRefusal.LocalSourceStoreConfigured;
        }

        if (registered?.IsService(typeof(IIntakeContentStore)) != true)
        {
            yield return ProductionAdmissionRefusal.ProductionSourceStoreMissing;
        }
        else if (provider.GetService<IIntakeContentStore>() is DevelopmentEncryptedIntakeContentStore)
        {
            yield return ProductionAdmissionRefusal.LocalSourceStoreConfigured;
        }
        else
        {
            yield return ProductionAdmissionRefusal.ProductionSourceStoreMissing;
        }

        // The reader is deliberately never a general service; no approved
        // production reader is integrated in T01.
        if (registered?.IsService(typeof(IExtractionContentReader)) == true)
        {
            yield return ProductionAdmissionRefusal.LocalSourceStoreConfigured;
        }

        yield return ProductionAdmissionRefusal.ProductionSourceReaderMissing;

        if (!PortalStartupGuards.HasRegisteredDurableKeyRing(services))
        {
            yield return ProductionAdmissionRefusal.DurableDataProtectionMissing;
        }

        if (!PortalStartupGuards.HasDurableSessionStore(services))
        {
            yield return ProductionAdmissionRefusal.DurableSessionStoreMissing;
        }
    }

    private static IEnumerable<ProductionAdmissionRefusal> EvidenceRefusals(
        ProductionConfigurationSnapshot snapshot,
        DateTimeOffset evaluatedAtUtc)
    {
        if (snapshot.ManifestInvalid)
        {
            return new[] { ProductionAdmissionRefusal.EvidenceManifestInvalid };
        }

        if (!ProductionConfigurationValidator.TryCreateReleaseBinding(
                snapshot.Options,
                snapshot.ConfigurationDigest,
                out var expected))
        {
            return snapshot.Manifest is null
                ? new[]
                {
                    ProductionAdmissionRefusal.EvidenceManifestMissing,
                    ProductionAdmissionRefusal.ReleaseBindingMismatch,
                }
                : new[] { ProductionAdmissionRefusal.ReleaseBindingMismatch };
        }

        return ProductionEvidencePolicy
            .Evaluate(snapshot.Manifest, expected!, evaluatedAtUtc.ToUniversalTime())
            .Refusals;
    }

    /// <summary>
    /// Probes the managed identity only when the profile's binding is valid, so a
    /// malformed profile never causes a token request. The probe is bounded by
    /// the validated probe deadline.
    /// </summary>
    private static IEnumerable<ProductionAdmissionRefusal> IdentityRefusals(
        IServiceProvider services,
        AzureInfrastructureOptions options,
        ProductionReadiness? structural)
    {
        if (structural is null ||
            !ProductionConfigurationValidator.TryCreateManagedIdentityBinding(options, out var binding) ||
            !ProductionConfigurationValidator.TryReadProbeDeadline(options, out var deadline))
        {
            return Array.Empty<ProductionAdmissionRefusal>();
        }

        var probe = services.GetService<IManagedIdentityBindingProbe>();
        if (probe is null)
        {
            return new[] { ProductionAdmissionRefusal.ManagedIdentityBindingUnverified };
        }

        using var cancellation = new CancellationTokenSource(deadline);
        ManagedIdentityBindingOutcome outcome;
        try
        {
            outcome = probe
                .VerifyAsync(binding!, cancellation.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            outcome = ManagedIdentityBindingOutcome.Unavailable;
        }

        return outcome == ManagedIdentityBindingOutcome.Verified
            ? Array.Empty<ProductionAdmissionRefusal>()
            : new[] { ProductionAdmissionRefusal.ManagedIdentityBindingUnverified };
    }
}
