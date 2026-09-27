using System.Reflection;
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
using TaxAI.AzureInfrastructure.Configuration;
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

    private static readonly ProductionAdmissionRefusal[] LaterSliceAdapters =
    {
        ProductionAdmissionRefusal.ProductionDocumentIntelligenceAdapterMissing,
        ProductionAdmissionRefusal.ProductionSourceStoreMissing,
        ProductionAdmissionRefusal.ProductionSourceReaderMissing,
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
        settings["TaxAi:Intake:DigestKey"] = Key(0x11);
        settings["TaxAi:Intake:FingerprintKey"] = Key(0x22);
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
        using var provider = Scenario.Complete().Build();

        Assert.NotNull(provider.GetService<ProductionConfigurationValidator>());
        Assert.NotNull(provider.GetService<IManagedIdentityBindingProbe>());
        Assert.Null(provider.GetService<IDocumentIntelligenceProvider>());
        Assert.Null(provider.GetService<IExtractionContentReader>());
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
            // AC-143
            ["durable key ring omitted"] = (Scenario.Complete() with { DurableDataProtection = false },
                ProductionAdmissionRefusal.DurableDataProtectionMissing),
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

    [Fact]
    public void RequireAdmissionRefusesADeployableHostWithOneFixedMessageThatEchoesNothing()
    {
        var scenario = Scenario.Complete()
            .Set("TaxAi:Production:SourceStorage:Endpoint", "https://planted-host.example/");
        using var provider = scenario.Build();
        var configuration = scenario.Configuration();

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
    public void RequireAdmissionRefusesTheCompleteT01ProfileBecauseNoProductionAdapterExists()
    {
        var scenario = Scenario.Complete();
        using var provider = scenario.Build();

        var failure = Assert.Throws<TargetInvocationException>(() => RequireAdmission(
            provider,
            scenario.Configuration(),
            "Production"));

        Assert.Equal(RefusalMessage, failure.InnerException?.Message);
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

    private static ProductionAdmissionRefusal[] Evaluate(Scenario scenario)
    {
        using var provider = scenario.Build();
        return EvaluateProvider(provider, scenario.Configuration(), scenario.EnvironmentName, scenario.EvaluatedAt);
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

        public bool DurableDataProtection { get; init; } = true;

        public bool RegisterDevelopmentProvider { get; init; }

        public bool RegisterLocalStore { get; init; }

        public bool DriftAfterManifest { get; init; }

        public Func<string, string?> ManifestFactory { get; init; } = digest => Manifest(digest);

        public static Scenario Complete() => new();

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
            var settings = new Dictionary<string, string?>(Settings, StringComparer.Ordinal)
            {
                ["TaxAi:Production:EvidenceManifest"] = ManifestFactory(digest),
            };

            if (DriftAfterManifest)
            {
                settings["TaxAi:Production:Resilience:MaximumAttempts"] = "2";
            }

            return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        }

        public ServiceProvider Build()
        {
            var configuration = Configuration();
            var services = new ServiceCollection();
            var environment = new TestEnvironment(EnvironmentName);
            services.AddSingleton<IWebHostEnvironment>(environment);
            services.AddLogging();

            typeof(Program).Assembly
                .GetType("TaxAI.Portal.ModuleRegistration", throwOnError: true)!
                .GetMethod("AddTaxAiModules", BindingFlags.Static | BindingFlags.NonPublic)!
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
                });

            services.AddSingleton<IServer, DeployableServer>();

            services.AddSingleton<TimeProvider>(new FixedClock(EvaluatedAt));

            if (DurableDataProtection)
            {
                services.AddApprovedDurableDataProtection();
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

            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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
