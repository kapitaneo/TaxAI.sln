using System.Reflection;
using ClientIntake.Contracts;
using DocumentIntelligence;
using ExtractionWorkflow.Contracts;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Content;
using TaxAI.AzureInfrastructure.Control;
using TaxAI.AzureInfrastructure.Keys;
using TaxAI.AzureInfrastructure.Protection;
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
/// <para>
/// Spec 016 T03 adds the real key protection: the registered key ring must be
/// exactly the protected Azure key ring with the approved discriminator, the
/// intake credential material must be the material loaded from Key Vault (never
/// a plaintext setting), and, once the managed identity is verified, the
/// approved-resource probe must find both Key Vault keys enabled,
/// non-exportable, purge-protected and actually wrapping, and the shared key
/// ring round-tripping.
/// </para>
/// <para>
/// Spec 016 T04 replaces two absences with real adapters: the intake port must
/// resolve to exactly the Azure Blob source store, and exactly the Azure Blob
/// extraction reader must be registered as its concrete type (never as a
/// general reader service). The approved-resource probe additionally requires
/// the source account and container posture. The production Document
/// Intelligence adapter is still absent, so no deployable host is admitted.
/// </para>
/// <para>
/// Spec 016 T06 adds recovery. The container must hold exactly the Azure
/// recovery control ledger and evidence reader, the durable recovery
/// reconciler and the recovery orchestrator; the release evidence stored in the
/// Canadian evidence store for this exact binding must be present, not
/// withdrawn, and admitted by the same policy as the configured manifest; and
/// the approved-resource probe also requires the immutable control containers.
/// Only once every one of those checks has passed does
/// <see cref="RecoveryOrchestrator"/> run: a database incarnation the
/// independent ledger does not release is reconciled first, and the host does
/// not start while it stays quarantined. Invalid security configuration is
/// therefore refused before any reconciliation (AC-116, AC-165).
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

        // Spec 016 T06: only a fully admitted host reconciles, and it serves
        // nothing until its database incarnation is released.
        RecoveryOrchestrator.RequireRelease(
            services,
            ProductionConfiguration.Read(configuration).Options);
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
            .Concat(RegistrationRefusals(services, snapshot.Options, configuration))
            .Concat(RuntimeMaterialRefusals(services, configuration))
            .Concat(EvidenceRefusals(snapshot, evaluatedAtUtc))
            .Concat(RecoveryRegistrationRefusals(services))
            .Concat(StoredEvidenceRefusals(services, snapshot, evaluatedAtUtc))
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
        IServiceProvider services,
        AzureInfrastructureOptions options,
        IConfiguration configuration)
    {
        var registered = services.GetService<IServiceProviderIsService>();

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        // No production Document Intelligence adapter is approved in T01. The
        // deterministic provider is a Development substitute; any other
        // registration is not an approved adapter yet. Both refuse.
        var intelligence = TryResolve<IDocumentIntelligenceProvider>(provider);
        if (intelligence is null)
        {
            yield return ProductionAdmissionRefusal.ProductionDocumentIntelligenceAdapterMissing;
        }
        else if (intelligence is DeterministicDocumentIntelligenceProvider)
        {
            yield return ProductionAdmissionRefusal.DevelopmentProviderConfigured;
        }
        else if (intelligence is not DocumentIntelligence.Production.AzureDocumentIntelligenceProvider production ||
            ExtractionConfiguration.ProductionOptions(configuration) is not { } expected ||
            !ExtractionAvailabilityGate.Matches(production.Options, expected) ||
            TryResolve<IExtractionAvailabilityGate>(provider) is not ExtractionAvailabilityGate)
        {
            yield return ProductionAdmissionRefusal.ProductionDocumentIntelligenceAdapterMissing;
        }

        // The Development encrypted filesystem store in a production container,
        // or behind the intake port, is a local-store refusal. Spec 016 T04: the
        // intake port must resolve to exactly the Azure Blob source store; any
        // other store is not the approved one.
        if (registered?.IsService(typeof(DevelopmentEncryptedIntakeContentStore)) == true)
        {
            yield return ProductionAdmissionRefusal.LocalSourceStoreConfigured;
        }

        var store = registered?.IsService(typeof(IIntakeContentStore)) == true
            ? TryResolve<IIntakeContentStore>(provider)
            : null;
        if (store is DevelopmentEncryptedIntakeContentStore)
        {
            yield return ProductionAdmissionRefusal.LocalSourceStoreConfigured;
        }
        else if (store is not AzureIntakeContentStore)
        {
            yield return ProductionAdmissionRefusal.ProductionSourceStoreMissing;
        }

        // The reader is deliberately never a general service: its only consumer
        // is the extraction workflow, which the composition root hands the
        // concrete reader. Spec 016 T04: exactly the Azure Blob reader, as its
        // concrete type, is the approved production reader.
        if (registered?.IsService(typeof(IExtractionContentReader)) == true)
        {
            yield return ProductionAdmissionRefusal.LocalSourceStoreConfigured;
        }

        if (registered?.IsService(typeof(AzureExtractionContentReader)) != true ||
            TryResolve<AzureExtractionContentReader>(provider) is null)
        {
            yield return ProductionAdmissionRefusal.ProductionSourceReaderMissing;
        }

        // Durable is necessary; the exact protected Azure key ring with the
        // approved discriminator is required (T03). A durable ring from anywhere
        // else is not the approved one.
        if (!PortalStartupGuards.HasRegisteredDurableKeyRing(services) ||
            !AzureDataProtectionConfiguration.IsApprovedKeyRing(
                services,
                options.DataProtection.ApplicationDiscriminator))
        {
            yield return ProductionAdmissionRefusal.DurableDataProtectionMissing;
        }

        if (!PortalStartupGuards.HasDurableSessionStore(services))
        {
            yield return ProductionAdmissionRefusal.DurableSessionStoreMissing;
        }
    }

    /// <summary>
    /// Spec 016 T06: exactly the Azure control ledger and evidence reader, the
    /// durable reconciler and the orchestrator. Anything else, or nothing, is an
    /// unregistered validation: recovery cannot be proven without them.
    /// </summary>
    private static IEnumerable<ProductionAdmissionRefusal> RecoveryRegistrationRefusals(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        if (TryResolve<IRecoveryControlStore>(provider) is not AzureRecoveryControlStore ||
            TryResolve<IProductionEvidenceReader>(provider) is not AzureProductionEvidenceReader ||
            TryResolve<RecoveryOrchestrator>(provider) is null ||
            !PortalStartupGuards.HasDurableRecoveryReconciler(services))
        {
            yield return ProductionAdmissionRefusal.InfrastructureValidationUnregistered;
        }
    }

    /// <summary>
    /// Spec 016 T06: the evidence the release process stored for this exact
    /// binding in the Canadian evidence store is read and held to the same
    /// schema and policy as the configured manifest. Absent is missing;
    /// withdrawn, malformed or unreadable is invalid, never a pass.
    /// </summary>
    private static IEnumerable<ProductionAdmissionRefusal> StoredEvidenceRefusals(
        IServiceProvider services,
        ProductionConfigurationSnapshot snapshot,
        DateTimeOffset evaluatedAtUtc)
    {
        if (!ProductionConfigurationValidator.TryCreateReleaseBinding(
                snapshot.Options,
                snapshot.ConfigurationDigest,
                out var release) ||
            !ProductionConfigurationValidator.TryReadProbeDeadline(snapshot.Options, out var deadline))
        {
            return Array.Empty<ProductionAdmissionRefusal>();
        }

        if (services.GetService<IProductionEvidenceReader>() is not { } reader)
        {
            return new[] { ProductionAdmissionRefusal.EvidenceManifestMissing };
        }

        ProductionEvidenceDocument document;
        using (var cancellation = new CancellationTokenSource(deadline))
        {
            try
            {
                document = reader.ReadAsync(release!, cancellation.Token).GetAwaiter().GetResult();
            }
            catch (Exception failure) when (failure is OperationCanceledException or InvalidOperationException)
            {
                return new[] { ProductionAdmissionRefusal.EvidenceManifestInvalid };
            }
        }

        if (document.Status == ProductionEvidenceDocumentStatus.Absent)
        {
            return new[] { ProductionAdmissionRefusal.EvidenceManifestMissing };
        }

        var stored = document.Status == ProductionEvidenceDocumentStatus.Present
            ? ProductionConfiguration.ReadManifest(document.ManifestText, out var invalid)
            : null;
        if (stored is null)
        {
            return new[] { ProductionAdmissionRefusal.EvidenceManifestInvalid };
        }

        return ProductionEvidencePolicy
            .Evaluate(stored, release!, evaluatedAtUtc.ToUniversalTime())
            .Refusals;
    }

    /// <summary>
    /// Resolves a registration for inspection. A factory that cannot construct
    /// its adapter is an absent adapter, never a crash of the gate.
    /// </summary>
    private static T? TryResolve<T>(IServiceProvider provider)
        where T : class
    {
        try
        {
            return provider.GetService<T>();
        }
        catch (Exception failure) when (failure is
            InvalidOperationException or
            ArgumentException or
            UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Spec 016 T03: the intake credential material the host runs with must be
    /// exactly the material the composition root loaded from Key Vault. A
    /// plaintext digest or fingerprint setting, a missing load, or a value that
    /// differs from the loaded one is an unapproved configuration entry.
    /// </summary>
    private static IEnumerable<ProductionAdmissionRefusal> RuntimeMaterialRefusals(
        IServiceProvider services,
        IConfiguration configuration)
    {
        var material = services.GetService<RuntimeMaterial>();
        if (material is not { IsLoaded: true } ||
            !string.Equals(
                configuration[IntakeConfiguration.DigestKeySetting],
                material.DigestKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                configuration[IntakeConfiguration.FingerprintKeySetting],
                material.FingerprintKey,
                StringComparison.Ordinal))
        {
            yield return ProductionAdmissionRefusal.UnapprovedConfigurationEntry;
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
    /// the validated probe deadline. Only a verified identity goes on to the
    /// approved-resource probe (T03), under its own deadline of the same bound.
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
            ? ResourceRefusals(services, deadline)
            : new[] { ProductionAdmissionRefusal.ManagedIdentityBindingUnverified };
    }

    /// <summary>
    /// The approved-resource probe as refusals. An unregistered probe is a
    /// missing validation; a stalled or failed probe is unverifiable, never a
    /// pass.
    /// </summary>
    private static IEnumerable<ProductionAdmissionRefusal> ResourceRefusals(
        IServiceProvider services,
        TimeSpan deadline)
    {
        var probe = services.GetService<IApprovedResourceProbe>();
        if (probe is null)
        {
            return new[] { ProductionAdmissionRefusal.InfrastructureValidationUnregistered };
        }

        IReadOnlyList<ProductionAdmissionRefusal> resources;
        using (var cancellation = new CancellationTokenSource(deadline))
        {
            try
            {
                resources = probe
                    .ProbeAsync(cancellation.Token)
                    .GetAwaiter()
                    .GetResult()
                    .Refusals;
            }
            catch (Exception failure) when (failure is
                OperationCanceledException or
                InvalidOperationException)
            {
                return ApprovedResourceProbeResult.Unverifiable.Refusals;
            }
        }

        // Spec 016 T06: the immutable control containers, under their own
        // deadline of the same bound. An unregistered inventory probe is a
        // missing validation; a stalled one is an unverifiable storage binding.
        var inventory = services.GetService<IRecoveryInventoryProbe>();
        if (inventory is null)
        {
            return resources.Append(ProductionAdmissionRefusal.InfrastructureValidationUnregistered).ToArray();
        }

        using (var cancellation = new CancellationTokenSource(deadline))
        {
            try
            {
                return resources
                    .Concat(inventory
                        .ProbeRecoveryInventoryAsync(cancellation.Token)
                        .GetAwaiter()
                        .GetResult()
                        .Refusals)
                    .ToArray();
            }
            catch (Exception failure) when (failure is
                OperationCanceledException or
                InvalidOperationException)
            {
                return resources.Append(ProductionAdmissionRefusal.StorageBindingInvalid).ToArray();
            }
        }
    }
}
