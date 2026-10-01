using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DocumentIntelligence;
using ExtractionWorkflow.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Secrets;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Content;
using TaxAI.AzureInfrastructure.Keys;
using TaxAI.Portal.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Production;

/// <summary>
/// Spec 016 T01 — the production admission gate (FR-14, AC-130–AC-152,
/// AC-168, AC-169, DEP-23).
/// <para>
/// Each case builds the <b>real</b> composition root for a Production host with
/// a complete synthetic production profile, a durable test key ring, the real
/// durable session store and a controlled identity probe, and evaluates the
/// real gate. The positive control proves every rule this slice owns can be
/// satisfied; what remains is exactly the three production adapters that later
/// slices supply, so no deployable host can be admitted by T01. Every other case
/// changes one thing and observes its own refusal.
/// </para>
/// <para>
/// Spec 016 T03 extends the positive control with the real key protection: the
/// intake credential material is loaded by the real
/// <see cref="KeyVaultRuntimeMaterialProvider"/> over a controlled in-memory
/// vault, the key ring is the real protected Azure key ring the portal
/// registers for Production, and a controlled approved-resource probe stands in
/// for the Key Vault data plane. Plaintext material, a substitute key ring and
/// each probe finding are then refused one at a time.
/// </para>
/// <para>
/// Spec 016 T04 makes the real Azure Blob source store and the concrete-only
/// Azure extraction reader part of the complete profile. A substituted store, a
/// removed reader, a reader exposed as a general service, an unusable source
/// endpoint or bound and each source-storage probe finding are refused.
/// </para>
/// <para>
/// Spec 016 T06 adds recovery. The complete profile carries the recovery
/// settings; the real Azure control ledger and evidence reader run over a
/// controlled pair of in-memory Canadian accounts holding the stored evidence;
/// the recovery inventory probe is controlled; and the orchestrator reconciles
/// over a recording reconciler. A missing ledger, stored evidence that is
/// absent, withdrawn or not approved, an unsafe recovery setting and each
/// inventory finding are refused, and an unreleased database quarantines startup.
/// </para>
/// <para>
/// All identifiers are synthetic and resolve to nothing. No network, cloud
/// resource, database connection or taxpayer value is involved.
/// </para>
/// </summary>
public sealed class ProductionStartupTests
{
    internal const string Tenant = "11111111-1111-4111-8111-111111111111";
    internal const string Subscription = "22222222-2222-4222-8222-222222222222";
    internal const string ClientId = "33333333-3333-4333-8333-333333333333";
    internal const string PrincipalId = "44444444-4444-4444-8444-444444444444";
    internal const string ResourceGroup = "rg-taxai-prod-cc";
    internal const string ResourcePrefix =
        "/subscriptions/" + Subscription + "/resourceGroups/" + ResourceGroup + "/providers/";

    private const string RefusalMessage =
        "Production startup is refused: the exact approved production configuration, registrations, identity and release evidence are not all present and valid.";

    internal const string ProductionConnectionString =
        "Server=tcp:sql-taxai-prod-cc.database.windows.net,1433;Database=taxai;" +
        "Encrypt=Strict;TrustServerCertificate=False;" +
        "Authentication=Active Directory Managed Identity;User Id=" + ClientId;

    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// What a complete profile still lacks. T04 replaced the source store and
    /// reader absences with the real Azure adapters, so only the production
    /// Document Intelligence adapter (T05) remains.
    /// </summary>
    private static readonly ProductionAdmissionRefusal[] LaterSliceAdapters =
    {
    };

    // ------------------------------------------------------------ settings

    internal static Dictionary<string, string?> ProductionProfile() => new(StringComparer.Ordinal)
    {
        ["TaxAi:Production:Environment"] = "Production",
        ["TaxAi:Production:PrimaryRegion"] = "canadacentral",
        ["TaxAi:Production:RecoveryRegion"] = "canadaeast",
        ["TaxAi:Production:TenantId"] = Tenant,
        ["TaxAi:Production:SubscriptionId"] = Subscription,
        ["TaxAi:Production:ResourceGroup"] = ResourceGroup,
        ["TaxAi:Production:ArtifactVersion"] = RunningArtifactVersion(),
        ["TaxAi:Production:ManagedIdentity:ClientId"] = ClientId,
        ["TaxAi:Production:ManagedIdentity:PrincipalId"] = PrincipalId,
        ["TaxAi:Production:ManagedIdentity:ResourceId"] =
            ResourcePrefix + "Microsoft.ManagedIdentity/userAssignedIdentities/id-taxai-portal-cc",
        ["TaxAi:Production:DocumentIntelligence:Endpoint"] = "https://di-taxai-prod-cc.cognitiveservices.azure.com/",
        ["TaxAi:Production:DocumentIntelligence:ResourceId"] =
            ResourcePrefix + "Microsoft.CognitiveServices/accounts/di-taxai-prod-cc",
        ["TaxAi:Production:DocumentIntelligence:Region"] = "canadacentral",
        ["TaxAi:Production:DocumentIntelligence:PublicNetworkAccess"] = "Disabled",
        ["TaxAi:Production:DocumentIntelligence:PrivateEndpoint"] = "Required",
        ["TaxAi:Production:DocumentIntelligence:ApiVersion"] = "2024-11-30",
        ["TaxAi:Production:DocumentIntelligence:Model"] = "prebuilt-layout",
        ["TaxAi:Production:DocumentIntelligence:Features"] = "keyValuePairs",
        ["TaxAi:Production:SourceStorage:Endpoint"] = "https://sttaxaisrccc.blob.core.windows.net/",
        ["TaxAi:Production:SourceStorage:ResourceId"] = ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxaisrccc",
        ["TaxAi:Production:SourceStorage:Region"] = "canadacentral",
        ["TaxAi:Production:SourceStorage:PublicNetworkAccess"] = "Disabled",
        ["TaxAi:Production:SourceStorage:PrivateEndpoint"] = "Required",
        ["TaxAi:Production:KeyVault:Endpoint"] = "https://kv-taxai-prod-cc.vault.azure.net/",
        ["TaxAi:Production:KeyVault:ResourceId"] = ResourcePrefix + "Microsoft.KeyVault/vaults/kv-taxai-prod-cc",
        ["TaxAi:Production:KeyVault:Region"] = "canadacentral",
        ["TaxAi:Production:KeyVault:PublicNetworkAccess"] = "Disabled",
        ["TaxAi:Production:KeyVault:PrivateEndpoint"] = "Required",
        ["TaxAi:Production:Sql:Endpoint"] = "sql-taxai-prod-cc.database.windows.net",
        ["TaxAi:Production:Sql:ResourceId"] = ResourcePrefix + "Microsoft.Sql/servers/sql-taxai-prod-cc",
        ["TaxAi:Production:Sql:Region"] = "canadacentral",
        ["TaxAi:Production:Sql:PublicNetworkAccess"] = "Disabled",
        ["TaxAi:Production:Sql:PrivateEndpoint"] = "Required",
        ["TaxAi:Production:Monitoring:WorkspaceResourceId"] =
            ResourcePrefix + "Microsoft.OperationalInsights/workspaces/log-taxai-prod-cc",
        ["TaxAi:Production:Monitoring:Region"] = "canadacentral",
        ["TaxAi:Production:Monitoring:RetentionDays"] = "30",
        ["TaxAi:Production:SourceEnvelope:KeyUri"] = "https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope",
        ["TaxAi:Production:DataProtection:StorageEndpoint"] = "https://sttaxaictlcc.blob.core.windows.net/",
        ["TaxAi:Production:DataProtection:StorageResourceId"] =
            ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxaictlcc",
        ["TaxAi:Production:DataProtection:KeyRingContainer"] = "data-protection",
        ["TaxAi:Production:DataProtection:KeyUri"] = "https://kv-taxai-prod-cc.vault.azure.net/keys/data-protection",
        ["TaxAi:Production:DataProtection:ApplicationDiscriminator"] = "taxai-portal-production",
        ["TaxAi:Production:Retention:FinalizedSourceDays"] = "30",
        ["TaxAi:Production:Retention:RejectedSourceDays"] = "7",
        ["TaxAi:Production:Retention:DuplicateSourceDays"] = "7",
        ["TaxAi:Production:Retention:AbandonedSourceDays"] = "30",
        ["TaxAi:Production:Retention:StagedUploadHours"] = "24",
        ["TaxAi:Production:Resilience:MaximumAttempts"] = "3",
        ["TaxAi:Production:Resilience:OperationDeadline"] = "00:02:00",
        ["TaxAi:Production:Resilience:ProbeDeadline"] = "00:00:15",
        ["TaxAi:Production:Recovery:Profile"] = "Primary",
        ["TaxAi:Production:Recovery:ControlLedger:Endpoint"] = "https://sttaxailedcc.blob.core.windows.net/",
        ["TaxAi:Production:Recovery:ControlLedger:ResourceId"] = ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxailedcc",
        ["TaxAi:Production:Recovery:ControlLedger:Region"] = "canadacentral",
        ["TaxAi:Production:Recovery:ControlLedger:PublicNetworkAccess"] = "Disabled",
        ["TaxAi:Production:Recovery:ControlLedger:PrivateEndpoint"] = "Required",
        ["TaxAi:Production:Recovery:ControlLedgerReplica:Endpoint"] = "https://sttaxailedce.blob.core.windows.net/",
        ["TaxAi:Production:Recovery:ControlLedgerReplica:ResourceId"] = ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxailedce",
        ["TaxAi:Production:Recovery:ControlLedgerReplica:Region"] = "canadaeast",
        ["TaxAi:Production:Recovery:ControlLedgerReplica:PublicNetworkAccess"] = "Disabled",
        ["TaxAi:Production:Recovery:ControlLedgerReplica:PrivateEndpoint"] = "Required",
        ["TaxAi:Production:Recovery:SqlPointInTimeRestoreDays"] = "35",
        ["TaxAi:Production:Recovery:BackupChainDays"] = "7",
        ["TaxAi:Production:Recovery:ControlLedgerRetentionDays"] = "90",
    };

    private static Dictionary<string, string?> HostSettings()
    {
        var settings = ProductionProfile();
        settings["ConnectionStrings:TaxAiDatabase"] = ProductionConnectionString;
        settings["TaxAi:Oidc:Authority"] = "https://identity.test.invalid/";
        settings["TaxAi:Oidc:ClientId"] = "taxai-t01-tests";
        settings["TaxAi:Extraction:Provider"] = "AzureDocumentIntelligence";
        settings["TaxAi:Extraction:ContentReader"] = "AzureBlobSourceReader";
        settings["TaxAi:Extraction:ProviderCallLimit"] = "00:01:00";
        settings["TaxAi:Extraction:MaximumSourceBytes"] = "1048576";
        settings["TaxAi:Intake:PublicOrigin"] = "https://intake.test.invalid/";
        settings["TaxAi:Intake:LinkLifetime"] = "01:00:00";
        settings["TaxAi:Intake:ClientSessionIdleLifetime"] = "00:15:00";
        settings["TaxAi:Intake:MaximumFileBytes"] = "1048576";
        settings["TaxAi:Intake:MaximumFilesPerRequest"] = "1";
        foreach (var (name, value) in new[]
                 {
                     ("PublicPermitLimit", "20"), ("PublicWindow", "00:01:00"),
                     ("SessionPermitLimit", "60"), ("SessionWindow", "00:01:00"),
                     ("AnswerPermitLimit", "20"), ("AnswerWindow", "00:01:00"),
                     ("UploadPermitLimit", "10"), ("UploadWindow", "00:01:00"),
                     ("UploadConcurrencyLimit", "2"),
                     ("SubmissionPermitLimit", "10"), ("SubmissionWindow", "00:01:00"),
                 })
        {
            settings[$"TaxAi:Intake:{name}"] = value;
        }

        return settings;
    }

    // ----------------------------------------------------- evidence manifest

    internal static string Manifest(
        string digest,
        Func<int, string>? evidenceStatus = null,
        string? inputKeyStatus = "Approved",
        string? retentionClockStatus = "Approved",
        bool productOwner = true,
        bool securityPrivacy = true,
        string? artifactVersion = null)
    {
        var version = artifactVersion ?? RunningArtifactVersion();
        object Release() => new { artifactVersion = version, configurationDigest = digest, subscriptionId = Subscription };

        var assurances = new List<object>();
        if (inputKeyStatus is not null)
        {
            assurances.Add(new
            {
                subject = "InputCustomerManagedKeyCoverage",
                status = inputKeyStatus,
                observedAtUtc = Now.AddDays(-1),
                expiresAtUtc = Now.AddDays(30),
            });
        }

        if (retentionClockStatus is not null)
        {
            assurances.Add(new
            {
                subject = "RetentionDeletionClock",
                status = retentionClockStatus,
                observedAtUtc = Now.AddDays(-1),
                expiresAtUtc = Now.AddDays(30),
            });
        }

        var approvals = new List<object>();
        if (productOwner)
        {
            approvals.Add(new { role = "ProductOwner", approvedAtUtc = Now.AddHours(-1), artifactVersion = version, configurationDigest = digest, subscriptionId = Subscription });
        }

        if (securityPrivacy)
        {
            approvals.Add(new { role = "SecurityPrivacy", approvedAtUtc = Now.AddHours(-1), artifactVersion = version, configurationDigest = digest, subscriptionId = Subscription });
        }

        var release = Release();
        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            artifactVersion = version,
            configurationDigest = digest,
            subscriptionId = Subscription,
            evidence = Enumerable.Range(1, 24)
                .Select(number => (object)new
                {
                    subject = $"DEP-{number:00}",
                    status = evidenceStatus?.Invoke(number) ?? "Approved",
                    observedAtUtc = Now.AddDays(-1),
                    expiresAtUtc = Now.AddDays(30),
                })
                .ToArray(),
            assurances,
            approvals,
            release,
        });
    }

    // ------------------------------------------------------------- the gate

    [Fact] // AC-152 positive control; the T01 boundary
    public void OnlyTheLaterSliceProductionAdaptersRemainWhenEveryT01RuleIsSatisfied()
    {
        var result = Evaluate(Scenario.Complete());

        Assert.Equal(LaterSliceAdapters, result);
    }

    [Fact]
    public void TheCompleteProfileUsesTheRealRegisteredValidatorAndDurableComponents()
    {
        var (provider, configuration) = Scenario.Complete().BuildHost();
        using var _ = provider;

        Assert.NotNull(provider.GetService<ProductionConfigurationValidator>());
        Assert.NotNull(provider.GetService<IManagedIdentityBindingProbe>());
        Assert.NotNull(provider.GetService<IApprovedResourceProbe>());
        Assert.IsType<DocumentIntelligence.Production.AzureDocumentIntelligenceProvider>(provider.GetService<IDocumentIntelligenceProvider>());
        Assert.Null(provider.GetService<IExtractionContentReader>());

        // T03: the real protected Azure key ring and the vault-loaded material.
        Assert.True(TaxAI.AzureInfrastructure.Protection.AzureDataProtectionConfiguration.IsApprovedKeyRing(
            provider,
            "taxai-portal-production"));
        var material = provider.GetRequiredService<RuntimeMaterial>();
        Assert.True(material.IsLoaded);
        Assert.Equal(Key(0x11), configuration["TaxAi:Intake:DigestKey"]);
        Assert.Equal(Key(0x22), configuration["TaxAi:Intake:FingerprintKey"]);
    }

    [Fact] // T04: the complete profile registers the real source adapters, and nothing more.
    public void TheCompleteProfileRegistersTheRealAzureSourceAdaptersOnly()
    {
        var (provider, _) = Scenario.Complete().BuildHost();
        using var _ = provider;
        using var scope = provider.CreateScope();

        Assert.IsType<AzureIntakeContentStore>(scope.ServiceProvider.GetRequiredService<ClientIntake.Contracts.IIntakeContentStore>());
        Assert.NotNull(scope.ServiceProvider.GetService<AzureExtractionContentReader>());
        Assert.Null(scope.ServiceProvider.GetService<IExtractionContentReader>());
        Assert.NotNull(provider.GetService<ClientIntake.Contracts.ISourceObjectPurger>());
        Assert.Null(provider.GetService(typeof(Program).Assembly.GetType("TaxAI.Portal.DevelopmentEncryptedIntakeContentStore", throwOnError: true)!));
    }

    [Theory] // T04: an unusable source endpoint or bound registers no source adapter.
    [InlineData("TaxAi:Production:SourceStorage:Endpoint", "https://sttaxaisrccc.file.core.windows.net/")]
    [InlineData("TaxAi:Production:SourceEnvelope:KeyUri", null)]
    [InlineData("TaxAi:Production:ManagedIdentity:ClientId", null)]
    [InlineData("TaxAi:Extraction:MaximumSourceBytes", "0")]
    public void AnUnusableSourceSettingRegistersNoSourceAdapter(string key, string? value)
    {
        var (provider, _) = Scenario.Complete().Set(key, value).BuildHost();
        using var _ = provider;
        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<ClientIntake.Contracts.IIntakeContentStore>());
        Assert.Null(scope.ServiceProvider.GetService<AzureExtractionContentReader>());
        var result = Evaluate(Scenario.Complete().Set(key, value));
        Assert.Contains(ProductionAdmissionRefusal.ProductionSourceStoreMissing, result);
        Assert.Contains(ProductionAdmissionRefusal.ProductionSourceReaderMissing, result);
    }

    [Fact] // T03: the production loader is handed the profile, not a free-form destination.
    public void TheRuntimeMaterialLoaderReceivesTheProductionProfileAndConnection()
    {
        AzureInfrastructureOptions? received = null;
        string? connection = null;
        var scenario = Scenario.Complete() with
        {
            Loader = (options, databaseConnectionString) =>
            {
                received = options;
                connection = databaseConnectionString;
                return ControlledSecrets.Load();
            },
        };

        var (provider, _) = scenario.BuildHost();
        provider.Dispose();

        Assert.Equal("https://kv-taxai-prod-cc.vault.azure.net/", received?.KeyVault.Endpoint);
        Assert.Equal(ClientId, received?.ManagedIdentity.ClientId);
        Assert.Equal(ProductionConnectionString, connection);
    }

    [Fact] // T03: plaintext material is never loaded over, and the gate refuses it.
    public void PlaintextIntakeKeyMaterialIsNotReplacedAndIsRefused()
    {
        var loads = 0;
        var scenario = Scenario.Complete()
            .Set("TaxAi:Intake:DigestKey", Key(0x33))
            .Set("TaxAi:Intake:FingerprintKey", Key(0x44)) with
        {
            Loader = (_, _) =>
            {
                loads++;
                return ControlledSecrets.Load();
            },
        };

        var (provider, configuration) = scenario.BuildHost();
        using var _ = provider;

        Assert.Equal(0, loads);
        Assert.Null(provider.GetService<RuntimeMaterial>());
        Assert.Equal(Key(0x33), configuration["TaxAi:Intake:DigestKey"]);
        Assert.Contains(
            ProductionAdmissionRefusal.UnapprovedConfigurationEntry,
            EvaluateProvider(provider, configuration, "Production"));
    }

    public static TheoryData<string> RefusedMaterialLoads() => new()
    {
        "unavailable",
        "invalid",
        "wrong identity",
    };

    [Theory] // T03: a refused load leaves no material, so startup refuses.
    [MemberData(nameof(RefusedMaterialLoads))]
    public void ARefusedMaterialLoadRefusesStartupWithTheFixedIntakeMessage(string scenario)
    {
        Func<AzureInfrastructureOptions, string, RuntimeMaterial> loader = scenario switch
        {
            "unavailable" => (_, _) => RuntimeMaterial.Refused(RuntimeMaterialOutcome.Unavailable),
            "invalid" => (_, _) => RuntimeMaterial.Refused(RuntimeMaterialOutcome.Invalid),
            _ => (_, _) => ControlledSecrets.Load(token: "synthetic-foreign-identity-token"),
        };

        var failure = Assert.Throws<TargetInvocationException>(
            () => (Scenario.Complete() with { Loader = loader }).BuildHost());

        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal("Client intake configuration is missing or invalid.", failure.InnerException!.Message);
    }

    [Fact] // T03: an unmatched configured value is not the vault material.
    public void MaterialThatDiffersFromTheLoadedMaterialIsRefused()
    {
        var (provider, configuration) = Scenario.Complete().BuildHost();
        using var _ = provider;
        configuration["TaxAi:Intake:DigestKey"] = Key(0x55);

        Assert.Contains(
            ProductionAdmissionRefusal.UnapprovedConfigurationEntry,
            EvaluateProvider(provider, configuration, "Production"));
    }

    [Fact] // T03: the resource probe runs only after the identity is verified.
    public void TheResourceProbeIsNotRunForAnUnverifiedIdentity()
    {
        var probe = new ControlledResourceProbe(new ApprovedResourceProbeResult(Array.Empty<ApprovedResourceFinding>()), hangs: false);
        var scenario = Scenario.Complete() with
        {
            ProbeOutcome = ManagedIdentityBindingOutcome.Mismatched,
            ResourceProbe = probe,
        };

        var result = Evaluate(scenario);

        Assert.Contains(ProductionAdmissionRefusal.ManagedIdentityBindingUnverified, result);
        Assert.Equal(0, probe.Calls);
    }

    public static TheoryData<string, ProductionAdmissionRefusal> AdmissionRefusals()
    {
        var data = new TheoryData<string, ProductionAdmissionRefusal>();
        foreach (var (name, (_, expected)) in Scenarios())
        {
            data.Add(name, expected);
        }

        return data;
    }

    [Theory] // DEP-23: each individual startup/configuration refusal.
    [MemberData(nameof(AdmissionRefusals))]
    public void EachIndividualAdmissionRuleRefusesIndependently(
        string scenario,
        ProductionAdmissionRefusal expected)
    {
        var result = Evaluate(Scenarios()[scenario].Scenario);

        Assert.Contains(expected, result);
        if (!LaterSliceAdapters.Contains(expected))
        {
            Assert.NotEqual(LaterSliceAdapters, result);
        }
    }

    private static Dictionary<string, (Scenario Scenario, ProductionAdmissionRefusal Expected)> Scenarios() =>
        new(StringComparer.Ordinal)
        {
            // Spec 016 T06: recovery registrations, stored evidence and settings.
            ["control ledger unregistered"] = (Scenario.Complete() with { RemoveLedger = true },
                ProductionAdmissionRefusal.InfrastructureValidationUnregistered),
            ["recovery inventory probe unregistered"] = (Scenario.Complete() with { RemoveInventoryProbe = true },
                ProductionAdmissionRefusal.InfrastructureValidationUnregistered),
            ["stored evidence absent"] = (Scenario.Complete() with { StoredEvidence = "absent" },
                ProductionAdmissionRefusal.EvidenceManifestMissing),
            ["stored evidence withdrawn"] = (Scenario.Complete() with { StoredEvidence = "withdrawn" },
                ProductionAdmissionRefusal.EvidenceManifestInvalid),
            ["stored evidence malformed"] = (Scenario.Complete() with { StoredEvidence = "malformed" },
                ProductionAdmissionRefusal.EvidenceManifestInvalid),
            ["stored evidence has a pending item"] = (Scenario.Complete() with { StoredEvidence = "pending" },
                ProductionAdmissionRefusal.MandatoryEvidenceIncomplete),
            ["stored evidence unreachable"] = (Scenario.Complete() with { ControlUnreachable = true },
                ProductionAdmissionRefusal.EvidenceManifestInvalid),
            ["control ledger mutable"] = (Scenario.Complete() with { InventoryFindings = [ApprovedResourceFinding.ControlLedgerNotApproved] },
                ProductionAdmissionRefusal.StorageBindingInvalid),
            ["control ledger unreachable"] = (Scenario.Complete() with { InventoryFindings = [ApprovedResourceFinding.ControlLedgerUnavailable] },
                ProductionAdmissionRefusal.StorageBindingInvalid),
            ["unknown recovery profile"] = (Scenario.Complete().Set("TaxAi:Production:Recovery:Profile", "Anywhere"),
                ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast),
            ["control ledger outside Canada East"] = (Scenario.Complete().Set("TaxAi:Production:Recovery:ControlLedgerReplica:Region", "eastus"),
                ProductionAdmissionRefusal.StorageBindingInvalid),
            ["ledger retention not beyond backups"] = (Scenario.Complete().Set("TaxAi:Production:Recovery:ControlLedgerRetentionDays", "42"),
                ProductionAdmissionRefusal.RetentionOutsidePolicy),
            ["seven-year SQL backup"] = (Scenario.Complete().Set("TaxAi:Production:Recovery:SqlPointInTimeRestoreDays", "2555"),
                ProductionAdmissionRefusal.RetentionOutsidePolicy),
            ["recovery DI failover"] = (Scenario.Complete().Set("TaxAi:Production:Recovery:Profile", "RegionalRecovery")
                    .Set("TaxAi:Production:Sql:Region", "canadaeast").Set("TaxAi:Production:SourceStorage:Region", "canadaeast")
                    .Set("TaxAi:Production:DocumentIntelligence:Region", "canadaeast"),
                ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid),

            // AC-130
            ["host environment Staging"] = (Scenario.Complete() with { EnvironmentName = "Staging" },
                ProductionAdmissionRefusal.EnvironmentNotProduction),
            ["profile environment omitted"] = (Scenario.Complete().Set("TaxAi:Production:Environment", null),
                ProductionAdmissionRefusal.EnvironmentNotProduction),
            // AC-131
            ["primary outside Canada Central"] = (Scenario.Complete().Set("TaxAi:Production:PrimaryRegion", "eastus"),
                ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral),
            // AC-132
            ["Development provider registered"] = (Scenario.Complete() with { RegisterDevelopmentProvider = true },
                ProductionAdmissionRefusal.DevelopmentProviderConfigured),
            ["Development provider configured"] = (Scenario.Complete().Set("TaxAi:Extraction:Provider", "Deterministic"),
                ProductionAdmissionRefusal.DevelopmentProviderConfigured),
            // AC-133
            ["production DI adapter not configured"] = (Scenario.Complete().Set("TaxAi:Extraction:Provider", null),
                ProductionAdmissionRefusal.ProductionDocumentIntelligenceAdapterMissing),
            // AC-134, AC-017
            ["preview DI API version"] = (Scenario.Complete().Set("TaxAi:Production:DocumentIntelligence:ApiVersion", "2024-07-31-preview"),
                ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved),
            ["preview feature anywhere"] = (Scenario.Complete().Set("TaxAi:Production:DocumentIntelligence:Features", "keyValuePairs-preview"),
                ProductionAdmissionRefusal.PreviewFeatureConfigured),
            // AC-135
            ["DI endpoint without matching resource"] = (Scenario.Complete().Set("TaxAi:Production:DocumentIntelligence:Endpoint", "https://canadacentral.api.cognitive.microsoft.com/"),
                ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid),
            ["DI resource in another region"] = (Scenario.Complete().Set("TaxAi:Production:DocumentIntelligence:Region", "canadaeast"),
                ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid),
            // AC-136 – AC-139
            ["Storage omitted"] = (Scenario.Complete().Set("TaxAi:Production:SourceStorage:Endpoint", null),
                ProductionAdmissionRefusal.StorageConfigurationMissing),
            ["Key Vault omitted"] = (Scenario.Complete().Set("TaxAi:Production:KeyVault:Endpoint", null),
                ProductionAdmissionRefusal.KeyVaultConfigurationMissing),
            ["SQL omitted"] = (Scenario.Complete().Set("TaxAi:Production:Sql:Endpoint", null),
                ProductionAdmissionRefusal.SqlConfigurationMissing),
            ["SQL connection omitted"] = (Scenario.Complete().Set("ConnectionStrings:TaxAiDatabase", null),
                ProductionAdmissionRefusal.SqlConfigurationMissing),
            ["monitoring omitted"] = (Scenario.Complete()
                    .Set("TaxAi:Production:Monitoring:WorkspaceResourceId", null)
                    .Set("TaxAi:Production:Monitoring:Region", null)
                    .Set("TaxAi:Production:Monitoring:RetentionDays", null),
                ProductionAdmissionRefusal.MonitoringConfigurationMissing),
            // AC-140
            ["managed identity omitted"] = (Scenario.Complete().Set("TaxAi:Production:ManagedIdentity:ClientId", null),
                ProductionAdmissionRefusal.ManagedIdentityBindingMissing),
            ["issued identity mismatched"] = (Scenario.Complete() with { ProbeOutcome = ManagedIdentityBindingOutcome.Mismatched },
                ProductionAdmissionRefusal.ManagedIdentityBindingUnverified),
            ["probe never verified"] = (Scenario.Complete() with { ProbeOutcome = ManagedIdentityBindingOutcome.Unverified },
                ProductionAdmissionRefusal.ManagedIdentityBindingUnverified),
            ["probe unregistered"] = (Scenario.Complete() with { RemoveProbe = true },
                ProductionAdmissionRefusal.ManagedIdentityBindingUnverified),
            // AC-150: an unavailable identity has no fallback.
            ["identity unavailable"] = (Scenario.Complete() with { ProbeOutcome = ManagedIdentityBindingOutcome.Unavailable },
                ProductionAdmissionRefusal.ManagedIdentityBindingUnverified),
            ["identity probe exceeds its deadline"] = (Scenario.Complete().Set("TaxAi:Production:Resilience:ProbeDeadline", "00:00:01") with { ProbeHangs = true },
                ProductionAdmissionRefusal.ManagedIdentityBindingUnverified),
            // AC-141
            ["source envelope omitted"] = (Scenario.Complete().Set("TaxAi:Production:SourceEnvelope:KeyUri", null),
                ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing),
            // AC-142
            ["local filesystem store registered"] = (Scenario.Complete() with { RegisterLocalStore = true },
                ProductionAdmissionRefusal.LocalSourceStoreConfigured),
            ["local store root configured"] = (Scenario.Complete().Set("TaxAi:Intake:DevelopmentContentRoot", "/srv/taxai-local"),
                ProductionAdmissionRefusal.LocalSourceStoreConfigured),
            ["local store reader configured"] = (Scenario.Complete().Set("TaxAi:Extraction:ContentReader", "DevelopmentEncryptedIntakeContentStore"),
                ProductionAdmissionRefusal.LocalSourceStoreConfigured),
            // T04: exactly the Azure source store and concrete-only reader.
            ["source store substituted"] = (Scenario.Complete() with { SubstituteSourceStore = true },
                ProductionAdmissionRefusal.ProductionSourceStoreMissing),
            ["source store unregistered"] = (Scenario.Complete() with { RemoveSourceStore = true },
                ProductionAdmissionRefusal.ProductionSourceStoreMissing),
            ["source reader unregistered"] = (Scenario.Complete() with { RemoveSourceReader = true },
                ProductionAdmissionRefusal.ProductionSourceReaderMissing),
            ["source reader exposed as a general service"] = (Scenario.Complete() with { ExposeGeneralReader = true },
                ProductionAdmissionRefusal.LocalSourceStoreConfigured),
            ["source reader unbounded"] = (Scenario.Complete().Set("TaxAi:Extraction:MaximumSourceBytes", null),
                ProductionAdmissionRefusal.ResilienceUnbounded),
            ["source endpoint not a blob service"] = (Scenario.Complete().Set("TaxAi:Production:SourceStorage:Endpoint", "https://sttaxaisrccc.file.core.windows.net/"),
                ProductionAdmissionRefusal.ProductionSourceStoreMissing),
            ["source account not approved"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.SourceStorageNotApproved),
                ProductionAdmissionRefusal.StorageBindingInvalid),
            ["source account unavailable"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.SourceStorageUnavailable),
                ProductionAdmissionRefusal.StorageBindingInvalid),
            // AC-143
            ["durable key ring omitted"] = (Scenario.Complete().Set("TaxAi:Production:DataProtection:StorageEndpoint", null),
                ProductionAdmissionRefusal.DurableDataProtectionMissing),
            // T03: a durable ring that is not the protected Azure ring.
            ["substitute durable key ring"] = (Scenario.Complete() with { SubstituteDataProtection = true },
                ProductionAdmissionRefusal.DurableDataProtectionMissing),
            // T03: approved-resource probe findings, one at a time.
            ["source envelope key not approved"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.SourceEnvelopeKeyNotApproved),
                ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing),
            ["source envelope key cannot wrap"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.SourceEnvelopeWrapFailed),
                ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing),
            ["key ring key not approved"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.DataProtectionKeyNotApproved),
                ProductionAdmissionRefusal.DataProtectionConfigurationMissing),
            ["key ring key cannot wrap"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.DataProtectionWrapFailed),
                ProductionAdmissionRefusal.DataProtectionConfigurationMissing),
            ["key ring does not round trip"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.KeyRingRoundTripFailed),
                ProductionAdmissionRefusal.DurableDataProtectionMissing),
            ["key vault unavailable"] = (Scenario.Complete().WithFindings(ApprovedResourceFinding.KeyVaultUnavailable),
                ProductionAdmissionRefusal.KeyVaultBindingInvalid),
            ["resource probe exceeds its deadline"] = (Scenario.Complete().Set("TaxAi:Production:Resilience:ProbeDeadline", "00:00:01") with { ResourceProbeHangs = true },
                ProductionAdmissionRefusal.KeyVaultBindingInvalid),
            ["resource probe unregistered"] = (Scenario.Complete() with { RemoveResourceProbe = true },
                ProductionAdmissionRefusal.InfrastructureValidationUnregistered),
            ["plaintext intake key material"] = (Scenario.Complete().Set("TaxAi:Intake:DigestKey", Key(0x33)).Set("TaxAi:Intake:FingerprintKey", Key(0x44)),
                ProductionAdmissionRefusal.UnapprovedConfigurationEntry),
            ["key ring configuration omitted"] = (Scenario.Complete().Set("TaxAi:Production:DataProtection:KeyUri", null),
                ProductionAdmissionRefusal.DataProtectionConfigurationMissing),
            // AC-144
            ["unencrypted SQL transport"] = (Scenario.Complete().Set("ConnectionStrings:TaxAiDatabase", ProductionConnectionString.Replace("Encrypt=Strict", "Encrypt=False")),
                ProductionAdmissionRefusal.SqlTransportUnprotected),
            ["unvalidated SQL certificate"] = (Scenario.Complete().Set("ConnectionStrings:TaxAiDatabase", ProductionConnectionString.Replace("TrustServerCertificate=False", "TrustServerCertificate=True")),
                ProductionAdmissionRefusal.SqlTransportUnprotected),
            // AC-145
            ["retention outside policy"] = (Scenario.Complete().Set("TaxAi:Production:Retention:FinalizedSourceDays", "365"),
                ProductionAdmissionRefusal.RetentionOutsidePolicy),
            // AC-146
            ["unbounded retries"] = (Scenario.Complete().Set("TaxAi:Production:Resilience:MaximumAttempts", "1000"),
                ProductionAdmissionRefusal.ResilienceUnbounded),
            ["no deadline"] = (Scenario.Complete().Set("TaxAi:Production:Resilience:OperationDeadline", null),
                ProductionAdmissionRefusal.ResilienceUnbounded),
            ["unbounded provider call"] = (Scenario.Complete().Set("TaxAi:Extraction:ProviderCallLimit", "1.00:00:00"),
                ProductionAdmissionRefusal.ResilienceUnbounded),
            // AC-147
            ["public dependency endpoint"] = (Scenario.Complete().Set("TaxAi:Production:SourceStorage:PublicNetworkAccess", "Enabled"),
                ProductionAdmissionRefusal.PublicEndpointConfigured),
            // AC-148
            ["arbitrary outbound destination"] = (Scenario.Complete().Set("TaxAi:Production:Egress:AllowedHost", "https://attacker.example/"),
                ProductionAdmissionRefusal.UnapprovedConfigurationEntry),
            ["production enable switch"] = (Scenario.Complete().Set("TaxAi:Production:EnableProduction", "true"),
                ProductionAdmissionRefusal.UnapprovedConfigurationEntry),
            // Registrations and release binding
            ["validator unregistered"] = (Scenario.Complete() with { RemoveValidator = true },
                ProductionAdmissionRefusal.InfrastructureValidationUnregistered),
            ["artifact version drift"] = (Scenario.Complete().Set("TaxAi:Production:ArtifactVersion", "0.0.0-other"),
                ProductionAdmissionRefusal.ReleaseBindingMismatch),
            ["configuration drift after evidence"] = (Scenario.Complete() with { DriftAfterManifest = true },
                ProductionAdmissionRefusal.ReleaseBindingMismatch),
            // AC-151
            ["manifest missing"] = (Scenario.Complete() with { ManifestFactory = _ => null },
                ProductionAdmissionRefusal.EvidenceManifestMissing),
            ["manifest malformed"] = (Scenario.Complete() with { ManifestFactory = _ => "{not json" },
                ProductionAdmissionRefusal.EvidenceManifestInvalid),
            ["manifest unknown status"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, number => number == 3 ? "Waived" : "Approved") },
                ProductionAdmissionRefusal.EvidenceManifestInvalid),
            ["one DEP item pending"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, number => number == 18 ? "Pending" : "Approved") },
                ProductionAdmissionRefusal.MandatoryEvidenceIncomplete),
            ["DEP-23 itself unavailable"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, number => number == 23 ? "Unavailable" : "Approved") },
                ProductionAdmissionRefusal.MandatoryEvidenceIncomplete),
            // AC-168, AC-169
            ["input CMK assurance unresolved"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, inputKeyStatus: "Pending") },
                ProductionAdmissionRefusal.ProviderInputKeyAssuranceUnresolved),
            ["input CMK assurance absent"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, inputKeyStatus: null) },
                ProductionAdmissionRefusal.ProviderInputKeyAssuranceUnresolved),
            ["retention clock assurance unresolved"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, retentionClockStatus: "Unavailable") },
                ProductionAdmissionRefusal.ProviderRetentionClockAssuranceUnresolved),
            ["retention clock assurance absent"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, retentionClockStatus: null) },
                ProductionAdmissionRefusal.ProviderRetentionClockAssuranceUnresolved),
            // AC-152: both roles, exact release.
            ["Product Owner approval missing"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, productOwner: false) },
                ProductionAdmissionRefusal.ReleaseApprovalMissing),
            ["Security/Privacy approval missing"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, securityPrivacy: false) },
                ProductionAdmissionRefusal.ReleaseApprovalMissing),
            ["evidence for another artifact"] = (Scenario.Complete() with { ManifestFactory = digest => Manifest(digest, artifactVersion: "9.9.9") },
                ProductionAdmissionRefusal.ReleaseBindingMismatch),
        };

    [Theory] // AC-144: the storage owner refuses independently of the gate.
    [InlineData("Encrypt=False")]
    [InlineData("TrustServerCertificate=True")]
    public void PersistenceIndependentlyRefusesAnUnprotectedConnectionAtRegistration(string unsafeSetting)
    {
        var connection = unsafeSetting.StartsWith("Encrypt", StringComparison.Ordinal)
            ? ProductionConnectionString.Replace("Encrypt=Strict", unsafeSetting)
            : ProductionConnectionString.Replace("TrustServerCertificate=False", unsafeSetting);

        Assert.ThrowsAny<ArgumentException>(() =>
            Persistence.PersistenceRegistration.AddTaxAiPersistence(new ServiceCollection(), connection));
    }

    [Fact] // AC-168, AC-169: each gap is independent of the other and of DEP-05.
    public void BothProviderAssuranceGapsRefuseIndependentlyEvenWithEveryDepItemApproved()
    {
        var both = Evaluate(Scenario.Complete() with
        {
            ManifestFactory = digest => Manifest(digest, inputKeyStatus: "Pending", retentionClockStatus: "Pending"),
        });

        Assert.Contains(ProductionAdmissionRefusal.ProviderInputKeyAssuranceUnresolved, both);
        Assert.Contains(ProductionAdmissionRefusal.ProviderRetentionClockAssuranceUnresolved, both);
        Assert.DoesNotContain(ProductionAdmissionRefusal.MandatoryEvidenceIncomplete, both);
    }

    public static TheoryData<string> WithdrawnApprovals() => new()
    {
        "processor input-key assurance withdrawn",   // AC-082
        "retention/deletion-clock assumption invalid", // AC-083
        "DEP-04 resource/API/model evidence pending",
        "Security/Privacy approval withdrawn",
    };

    [Theory] // spec 016 T05 AC-082, AC-083: re-evaluated per operation by the real gate.
    [MemberData(nameof(WithdrawnApprovals))]
    public async Task AWithdrawnApprovalBlocksNewExtractionBeforeAnyDependencyIsContacted(string withdrawal)
    {
        var probe = new ControlledResourceProbe(new ApprovedResourceProbeResult(Array.Empty<ApprovedResourceFinding>()), hangs: false);
        Func<string, string?> manifest = withdrawal switch
        {
            "processor input-key assurance withdrawn" => digest => Manifest(digest, inputKeyStatus: "Pending"),
            "retention/deletion-clock assumption invalid" => digest => Manifest(digest, retentionClockStatus: "Unavailable"),
            "DEP-04 resource/API/model evidence pending" => digest => Manifest(digest, number => number == 4 ? "Pending" : "Approved"),
            _ => digest => Manifest(digest, securityPrivacy: false),
        };
        var (provider, _) = (Scenario.Complete() with { ManifestFactory = manifest, ResourceProbe = probe }).BuildHost();
        using var owner = provider;

        var gate = provider.GetRequiredService<ExtractionWorkflow.Contracts.IExtractionAvailabilityGate>();

        Assert.False(await gate.IsAvailableAsync(CancellationToken.None));
        Assert.Equal(0, probe.Calls);
    }

    [Fact] // spec 016 T05: the positive control for the theory above.
    public async Task CompleteEvidenceReachesTheDependencyProbesAndAFindingStillBlocks()
    {
        var probe = new ControlledResourceProbe(
            new ApprovedResourceProbeResult(new[] { ApprovedResourceFinding.KeyVaultUnavailable }), hangs: false);
        var (provider, _) = (Scenario.Complete() with { ResourceProbe = probe }).BuildHost();
        using var owner = provider;

        var gate = provider.GetRequiredService<ExtractionWorkflow.Contracts.IExtractionAvailabilityGate>();

        // The evidence passed, so the dependency probe ran; its finding blocks
        // without the adapter's reachability probe ever being attempted.
        Assert.False(await gate.IsAvailableAsync(CancellationToken.None));
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public void RequireAdmissionRefusesADeployableHostWithOneFixedMessageThatEchoesNothing()
    {
        var scenario = Scenario.Complete()
            .Set("TaxAi:Production:SourceStorage:Endpoint", "https://planted-host.example/");
        var (provider, configuration) = scenario.BuildHost();
        using var _ = provider;

        var failure = Assert.Throws<TargetInvocationException>(() => RequireAdmission(
            provider,
            configuration,
            "Production"));

        Assert.Equal(RefusalMessage, failure.InnerException?.Message);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        foreach (var planted in new[] { "planted-host", Subscription, ClientId, "sql-taxai-prod-cc" })
        {
            Assert.DoesNotContain(planted, failure.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CompleteControlledProfileHasNoMissingAdapterRefusalAfterT05()
    {
        var (provider, configuration) = Scenario.Complete().BuildHost();
        using var owner = provider;
        Assert.Null(Record.Exception(() => RequireAdmission(provider, configuration, "Production")));
        // Synthetic approvals and controlled probes are a test, never deployed evidence.
        Assert.IsType<DocumentIntelligence.Production.AzureDocumentIntelligenceProvider>(
            provider.GetRequiredService<IDocumentIntelligenceProvider>());
    }
    [Fact] // Spec 016 T06: the complete profile holds the real Azure recovery adapters.
    public void TheCompleteProfileRegistersTheRealRecoveryControlAdapters()
    {
        var services = new ServiceCollection();
        typeof(Program).Assembly
            .GetType("TaxAI.Portal.ModuleRegistration", throwOnError: true)!
            .GetMethod("RegisterTaxAiModules", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[]
            {
                services,
                Scenario.Complete().Configuration(),
                new TestEnvironment("Production"),
                ProductionConnectionString,
                (Func<AzureInfrastructureOptions, string, RuntimeMaterial>)((_, _) => ControlledSecrets.Load()),
            });
        services.AddSingleton<TimeProvider>(new FixedClock(Now));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore>(provider.GetRequiredService<IRecoveryControlStore>());
        Assert.IsType<TaxAI.AzureInfrastructure.Control.AzureProductionEvidenceReader>(provider.GetRequiredService<IProductionEvidenceReader>());
        Assert.NotNull(provider.GetService<IRecoveryInventoryProbe>());
        Assert.NotNull(provider.GetService(OrchestratorType));
        using var scope = provider.CreateScope();
        Assert.Equal(
            typeof(Persistence.PersistenceRegistration).Assembly,
            scope.ServiceProvider.GetRequiredService<IRecoveryReconciler>().GetType().Assembly);
    }

    [Fact] // Spec 016 T06: the Canada East recovery profile is admitted, DI staying Canada Central.
    public void TheSignedRegionalRecoveryProfileIsAdmitted()
    {
        var recovery = Scenario.Complete()
            .Set("TaxAi:Production:Recovery:Profile", "RegionalRecovery")
            .Set("TaxAi:Production:Sql:Region", "canadaeast")
            .Set("TaxAi:Production:SourceStorage:Region", "canadaeast");

        Assert.Equal(LaterSliceAdapters, Evaluate(recovery));
    }

    [Fact] // AC-115, AC-116: admitted configuration, unreleased database: startup is quarantined.
    public void AnUnreleasedDatabaseQuarantinesAnOtherwiseAdmittedHost()
    {
        var scenario = Scenario.Complete() with
        {
            Reconciler = new RecordingReconciler { Extent = RecoveredDataExtent.Observed(true, Now.AddHours(-1)) },
        };
        var (provider, configuration) = scenario.BuildHost();
        using var _ = provider;

        Assert.Empty(EvaluateProvider(provider, configuration, "Production"));
        var failure = Assert.Throws<TargetInvocationException>(() => RequireAdmission(provider, configuration, "Production"));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(
            "Production startup is quarantined: recovery reconciliation for this database has not completed.",
            failure.InnerException!.Message);
    }

    [Fact] // AC-116, AC-165: an invalid security configuration never reaches reconciliation.
    public void AnInvalidConfigurationIsRefusedBeforeAnyReconciliation()
    {
        var reconciler = new RecordingReconciler();
        var scenario = Scenario.Complete().Set("TaxAi:Production:KeyVault:PublicNetworkAccess", "Enabled") with { Reconciler = reconciler };
        var (provider, configuration) = scenario.BuildHost();
        using var _ = provider;

        var failure = Assert.Throws<TargetInvocationException>(() => RequireAdmission(provider, configuration, "Production"));

        Assert.Equal(RefusalMessage, failure.InnerException!.Message);
        Assert.Empty(reconciler.Calls);
    }

    [Fact] // First production start: genesis is written and the host starts.
    public void AFirstStartOnAPristineDatabaseWritesGenesisAndStarts()
    {
        var scenario = Scenario.Complete();
        var (provider, configuration) = scenario.BuildHost();
        using var _ = provider;

        Assert.Null(Record.Exception(() => RequireAdmission(provider, configuration, "Production")));
        Assert.Equal(1, scenario.Accounts.Count(ControlledControlAccounts.EastHost, TaxAI.AzureInfrastructure.Control.AzureRecoveryControlStore.LedgerContainerName));
    }

    [Fact]
    public void DevelopmentHostsAreNeverEvaluatedAndNeverAdmitted()
    {
        // Development never admits production data: the gate is simply not the
        // path it runs, and nothing in Development can make a deployable host
        // pass. The production profile is not even read.
        var services = new ServiceCollection();
        services.AddSingleton<IServer, DeployableServer>();
        using var provider = services.BuildServiceProvider();

        Assert.Null(Record.Exception(() => RequireAdmission(
            provider,
            new ConfigurationBuilder().Build(),
            "Development")));
    }

    [Fact]
    public void AnInProcessTestServerHostIsNotEvaluatedButIsNeverAdmitted()
    {
        // TestServer is a verification surface that cannot be deployed. Starting
        // one in Production does not run the gate — and the same container,
        // evaluated, is refused, so a started test host is never authorization.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.AddApprovedDurableDataProtection());
        });
        using var client = factory.CreateClient();

        Assert.IsType<TestServer>(factory.Services.GetRequiredService<IServer>());

        var result = EvaluateProvider(
            factory.Services,
            factory.Services.GetRequiredService<IConfiguration>(),
            "Production");

        Assert.Contains(ProductionAdmissionRefusal.EnvironmentNotProduction, result);
        Assert.Contains(ProductionAdmissionRefusal.EvidenceManifestMissing, result);
        Assert.Contains(ProductionAdmissionRefusal.ProductionSourceStoreMissing, result);
    }

    [Fact]
    public void TheGateReadsTheRegisteredClockForEvidenceValidity()
    {
        // Evidence valid at the fixed instant is expired a year later.
        var later = Scenario.Complete() with { EvaluatedAt = Now.AddYears(1) };

        Assert.Contains(ProductionAdmissionRefusal.MandatoryEvidenceIncomplete, Evaluate(later));
    }

    // -------------------------------------------------------------- helpers

    internal static string RunningArtifactVersion() =>
        (string)typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionAdmissionGate", throwOnError: true)!
            .GetMethod("RunningArtifactVersion", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null)!;

    internal static string Digest(IConfiguration configuration)
    {
        var snapshot = typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionConfiguration", throwOnError: true)!
            .GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { configuration })!;

        return (string)snapshot.GetType()
            .GetProperty("ConfigurationDigest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(snapshot)!;
    }

    private static readonly Type OrchestratorType = typeof(Program).Assembly
        .GetType("TaxAI.Portal.Production.RecoveryOrchestrator", throwOnError: true)!;

    private static ProductionAdmissionRefusal[] Evaluate(Scenario scenario)
    {
        var (provider, configuration) = scenario.BuildHost();
        using var _ = provider;
        return EvaluateProvider(provider, configuration, scenario.EnvironmentName, scenario.EvaluatedAt);
    }

    private static ProductionAdmissionRefusal[] EvaluateProvider(
        IServiceProvider provider,
        IConfiguration configuration,
        string environmentName,
        DateTimeOffset? at = null)
    {
        var readiness = (ProductionReadiness)typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionAdmissionGate", throwOnError: true)!
            .GetMethod("Evaluate", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { provider, configuration, environmentName, at ?? Now })!;

        return readiness.Refusals.ToArray();
    }

    private static void RequireAdmission(
        IServiceProvider provider,
        IConfiguration configuration,
        string environmentName) =>
        typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionAdmissionGate", throwOnError: true)!
            .GetMethod("RequireAdmission", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { provider, configuration, environmentName });

    private static string Key(byte value) =>
        Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>One complete, synthetic Production host description.</summary>
    private sealed record Scenario
    {
        public IReadOnlyDictionary<string, string?> Settings { get; init; } = HostSettings();

        public string EnvironmentName { get; init; } = "Production";

        public DateTimeOffset EvaluatedAt { get; init; } = Now;

        public ManagedIdentityBindingOutcome ProbeOutcome { get; init; } = ManagedIdentityBindingOutcome.Verified;

        public bool ProbeHangs { get; init; }

        public bool RemoveProbe { get; init; }

        public bool RemoveValidator { get; init; }

        /// <summary>
        /// T03: replaces the real protected Azure key ring with the durable test
        /// ring, which the startup guard accepts and the gate does not.
        /// </summary>
        public bool SubstituteDataProtection { get; init; }

        public Func<AzureInfrastructureOptions, string, RuntimeMaterial> Loader { get; init; } =
            (_, _) => ControlledSecrets.Load();

        public ControlledResourceProbe? ResourceProbe { get; init; }

        public ApprovedResourceFinding[] Findings { get; init; } = Array.Empty<ApprovedResourceFinding>();

        public bool ResourceProbeHangs { get; init; }

        public bool RemoveResourceProbe { get; init; }

        public bool RegisterDevelopmentProvider { get; init; }

        public bool RegisterLocalStore { get; init; }

        public bool SubstituteSourceStore { get; init; }

        public bool RemoveSourceStore { get; init; }

        public bool RemoveSourceReader { get; init; }

        public bool ExposeGeneralReader { get; init; }

        public bool DriftAfterManifest { get; init; }

        public Func<string, string?> ManifestFactory { get; init; } = digest => Manifest(digest);

        /// <summary>T06: the controlled Canadian control accounts this host reads.</summary>
        public ControlledControlAccounts Accounts { get; init; } = new();

        /// <summary>T06: what the release process stored for this binding.</summary>
        public string StoredEvidence { get; init; } = "same";

        public bool ControlUnreachable { get; init; }

        public bool RemoveLedger { get; init; }

        public bool RemoveInventoryProbe { get; init; }

        public ApprovedResourceFinding[] InventoryFindings { get; init; } = Array.Empty<ApprovedResourceFinding>();

        /// <summary>T06: the recovered database; pristine unless a scenario says otherwise.</summary>
        public RecordingReconciler Reconciler { get; init; } = new() { Extent = RecoveredDataExtent.Observed(false, null) };

        public static Scenario Complete() => new();

        public Scenario WithFindings(params ApprovedResourceFinding[] findings) =>
            this with { Findings = findings };

        public Scenario Set(string key, string? value)
        {
            var settings = new Dictionary<string, string?>(Settings, StringComparer.Ordinal)
            {
                [key] = value,
            };
            return this with { Settings = settings };
        }

        public IConfiguration Configuration()
        {
            var digest = Digest(new ConfigurationBuilder().AddInMemoryCollection(Settings).Build());
            var manifest = ManifestFactory(digest);
            var settings = new Dictionary<string, string?>(Settings, StringComparer.Ordinal)
            {
                ["TaxAi:Production:EvidenceManifest"] = manifest,
            };

            // T06: the release process stores the same evidence in Canada.
            var release = new ProductionReleaseBinding(
                Settings.GetValueOrDefault("TaxAi:Production:ArtifactVersion") ?? "unset",
                digest,
                Guid.Parse(Subscription));
            switch (StoredEvidence)
            {
                case "same" when manifest is not null:
                    Accounts.PublishEvidence(release, manifest);
                    break;
                case "withdrawn" when manifest is not null:
                    Accounts.PublishEvidence(release, manifest);
                    Accounts.PublishEvidence(release, "{}", "withdrawn/");
                    break;
                case "malformed":
                    Accounts.PublishEvidence(release, "{not json");
                    break;
                case "pending":
                    Accounts.PublishEvidence(release, Manifest(digest, number => number == 17 ? "Pending" : "Approved"));
                    break;
            }

            if (ControlUnreachable)
            {
                Accounts.Unreachable.Add(ControlledControlAccounts.CentralHost);
                Accounts.Unreachable.Add(ControlledControlAccounts.EastHost);
            }

            if (DriftAfterManifest)
            {
                settings["TaxAi:Production:Resilience:MaximumAttempts"] = "2";
            }

            return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        }

        /// <summary>
        /// Builds the host exactly as the composition root would, and returns the
        /// configuration it actually ended up with, so the gate sees the same
        /// configuration the host was built from.
        /// </summary>
        public (ServiceProvider Provider, IConfiguration Configuration) BuildHost()
        {
            var configuration = Configuration();
            var services = new ServiceCollection();
            var environment = new TestEnvironment(EnvironmentName);
            services.AddSingleton<IWebHostEnvironment>(environment);
            services.AddLogging();

            typeof(Program).Assembly
                .GetType("TaxAI.Portal.ModuleRegistration", throwOnError: true)!
                .GetMethod("RegisterTaxAiModules", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[]
                {
                    services,
                    configuration,
                    new TestEnvironment("Production"),
                    // Registration always receives the approved string: Persistence
                    // independently refuses an unprotected one at registration
                    // (proved separately), and the gate must still see and refuse
                    // whatever configuration actually holds.
                    ProductionConnectionString,
                    Loader,
                });

            services.AddSingleton<IServer, DeployableServer>();

            services.AddSingleton<TimeProvider>(new FixedClock(EvaluatedAt));

            if (SubstituteDataProtection)
            {
                services.AddApprovedDurableDataProtection();
            }

            services.RemoveAll<IApprovedResourceProbe>();
            if (!RemoveResourceProbe)
            {
                services.AddSingleton<IApprovedResourceProbe>(
                    ResourceProbe ?? new ControlledResourceProbe(new ApprovedResourceProbeResult(Findings), ResourceProbeHangs));
            }

            // T06: the real Azure recovery adapters over the controlled accounts,
            // a controlled inventory probe, and the orchestrator reconciling the
            // recording reconciler. The durable reconciler stays registered.
            var clock = new FixedClock(EvaluatedAt);
            services.RemoveAll<IRecoveryControlStore>();
            services.RemoveAll<IProductionEvidenceReader>();
            services.RemoveAll<IRecoveryInventoryProbe>();
            services.RemoveAll(OrchestratorType);
            if (!RemoveLedger)
            {
                var store = Accounts.Store(clock);
                services.AddSingleton<IRecoveryControlStore>(store);
                services.AddSingleton<IProductionEvidenceReader>(Accounts.Reader());
                var reconciler = Reconciler;
                var scopes = new ServiceCollection();
                scopes.AddScoped<IRecoveryReconciler>(_ => reconciler);
                var scopeProvider = scopes.BuildServiceProvider();
                services.AddSingleton(OrchestratorType, provider => Activator.CreateInstance(
                    OrchestratorType,
                    scopeProvider.GetRequiredService<IServiceScopeFactory>(),
                    store,
                    clock,
                    provider.GetRequiredService<AzureInfrastructureOptions>())!);
            }

            if (!RemoveInventoryProbe)
            {
                services.AddSingleton<IRecoveryInventoryProbe>(new ControlledInventoryProbe(new ApprovedResourceProbeResult(InventoryFindings)));
            }

            if (RemoveProbe)
            {
                services.RemoveAll<IManagedIdentityBindingProbe>();
            }
            else
            {
                services.AddSingleton<IManagedIdentityBindingProbe>(
                    new ControlledProbe(ProbeOutcome, ProbeHangs));
            }

            if (RemoveValidator)
            {
                services.RemoveAll<ProductionConfigurationValidator>();
            }

            if (RegisterDevelopmentProvider)
            {
                services.AddDeterministicDocumentIntelligence(
                    DocumentIntelligenceOptions.RequireValid(new DocumentIntelligenceOptions
                    {
                        MaximumSourceBytes = 1024,
                        ProviderLabel = "t01-development-substitute",
                    }),
                    isDevelopmentOrTestHost: true);
            }

            if (RegisterLocalStore)
            {
                var localStore = typeof(Program).Assembly.GetType(
                    "TaxAI.Portal.DevelopmentEncryptedIntakeContentStore",
                    throwOnError: true)!;
                services.AddScoped(localStore, _ => throw new InvalidOperationException("never resolved"));
            }

            if (SubstituteSourceStore)
            {
                services.RemoveAll<ClientIntake.Contracts.IIntakeContentStore>();
                services.AddScoped<ClientIntake.Contracts.IIntakeContentStore, SubstituteContentStore>();
            }

            if (RemoveSourceStore)
            {
                services.RemoveAll<ClientIntake.Contracts.IIntakeContentStore>();
            }

            if (RemoveSourceReader)
            {
                services.RemoveAll<AzureExtractionContentReader>();
            }

            if (ExposeGeneralReader)
            {
                services.AddScoped<IExtractionContentReader>(provider => provider.GetRequiredService<AzureExtractionContentReader>());
            }

            return (services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }), configuration);
        }
    }

    /// <summary>A store that is not the approved Azure source store.</summary>
    private sealed class SubstituteContentStore : ClientIntake.Contracts.IIntakeContentStore
    {
        public Task<ClientIntake.Models.StagedIntakeContent> StageAsync(TrustedFirmId firm, Guid caseId, Stream content, long maximumBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteIfExistsAsync(TrustedFirmId firm, Guid caseId, string contentKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A controlled approved-resource probe: returns a fixed result, or waits
    /// until the gate's deadline cancels it.
    /// </summary>
    internal sealed class ControlledResourceProbe(ApprovedResourceProbeResult result, bool hangs) : IApprovedResourceProbe
    {
        public int Calls { get; private set; }

        public async Task<ApprovedResourceProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (hangs)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return result;
        }
    }

    /// <summary>A controlled recovery inventory probe returning a fixed result.</summary>
    internal sealed class ControlledInventoryProbe(ApprovedResourceProbeResult result) : IRecoveryInventoryProbe
    {
        public Task<ApprovedResourceProbeResult> ProbeRecoveryInventoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    /// <summary>
    /// Loads intake material with the real Key Vault provider over an in-memory
    /// secrets endpoint that answers the SDK challenge, accepts one bearer token
    /// and serves the two fixed synthetic secrets. Nothing reaches a network.
    /// </summary>
    internal static class ControlledSecrets
    {
        internal const string ApprovedToken = "synthetic-application-token";

        internal static RuntimeMaterial Load(string token = ApprovedToken)
        {
            var options = new SecretClientOptions
            {
                Transport = new HttpClientTransport(new HttpClient(new Handler())),
            };
            options.Retry.MaxRetries = 0;

            return new KeyVaultRuntimeMaterialProvider(
                    new Uri("https://kv-taxai-prod-cc.vault.azure.net/"),
                    new FixedToken(token),
                    new FixedClock(Now),
                    options)
                .LoadAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Headers.Authorization is null)
                {
                    var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    challenge.Headers.TryAddWithoutValidation(
                        "WWW-Authenticate",
                        $"Bearer authorization=\"https://login.microsoftonline.com/{Tenant}\", resource=\"https://vault.azure.net\"");
                    return Task.FromResult(challenge);
                }

                if (request.Headers.Authorization.Parameter != ApprovedToken)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent("{\"error\":{\"code\":\"Forbidden\",\"message\":\"synthetic\"}}", Encoding.UTF8, "application/json"),
                    });
                }

                var name = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
                var value = name switch
                {
                    "taxai-intake-digest-key" => Key(0x11),
                    "taxai-intake-fingerprint-key" => Key(0x22),
                    _ => null,
                };
                if (value is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("{\"error\":{\"code\":\"SecretNotFound\",\"message\":\"synthetic\"}}", Encoding.UTF8, "application/json"),
                    });
                }

                var body = JsonSerializer.Serialize(new
                {
                    value,
                    id = $"https://kv-taxai-prod-cc.vault.azure.net/secrets/{name}/{new string('b', 32)}",
                    attributes = new { enabled = true, created = 1_700_000_000, updated = 1_700_000_000 },
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
        }

        private sealed class FixedToken(string token) : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                new(token, DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                new(GetToken(requestContext, cancellationToken));
        }
    }

    private sealed class ControlledProbe(ManagedIdentityBindingOutcome outcome, bool hangs) : IManagedIdentityBindingProbe
    {
        public async Task<ManagedIdentityBindingOutcome> VerifyAsync(
            ManagedIdentityBinding binding,
            CancellationToken cancellationToken)
        {
            Assert.Equal(Guid.Parse(ClientId), binding.ClientId);
            Assert.Equal(Guid.Parse(PrincipalId), binding.PrincipalId);
            Assert.Equal(Guid.Parse(Tenant), binding.TenantId);

            if (hangs)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return outcome;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TaxAI.Portal.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DeployableServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
