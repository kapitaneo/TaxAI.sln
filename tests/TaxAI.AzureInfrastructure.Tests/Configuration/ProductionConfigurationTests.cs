using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Tests.Configuration;

/// <summary>
/// Spec 016 T01 — the structural production-profile validator (FR-14, AC-017,
/// AC-130–148, DEP-23).
/// <para>
/// One synthetic approved profile is the positive control; every refusal is
/// then proved individually by changing exactly one thing, so a validator that
/// refused everything, or one that stopped after its first failure, could not
/// pass. All identifiers are synthetic and resolve to nothing.
/// </para>
/// </summary>
public sealed class ProductionConfigurationTests
{
    internal const string Tenant = "11111111-1111-4111-8111-111111111111";
    internal const string Subscription = "22222222-2222-4222-8222-222222222222";
    internal const string ClientId = "33333333-3333-4333-8333-333333333333";
    internal const string PrincipalId = "44444444-4444-4444-8444-444444444444";
    internal const string ResourceGroup = "rg-taxai-prod-cc";
    internal const string ResourcePrefix =
        "/subscriptions/" + Subscription + "/resourceGroups/" + ResourceGroup + "/providers/";
    internal const string IdentityResourceId =
        ResourcePrefix + "Microsoft.ManagedIdentity/userAssignedIdentities/id-taxai-portal-cc";

    internal const string ConnectionString =
        "Server=tcp:sql-taxai-prod-cc.database.windows.net,1433;Database=taxai;" +
        "Encrypt=Strict;TrustServerCertificate=False;" +
        "Authentication=Active Directory Managed Identity;User Id=" + ClientId;

    private readonly ProductionConfigurationValidator _validator = new();

    internal static Dictionary<string, string?> ApprovedSettings() => new(StringComparer.Ordinal)
    {
        ["Environment"] = "Production",
        ["PrimaryRegion"] = "canadacentral",
        ["RecoveryRegion"] = "canadaeast",
        ["TenantId"] = Tenant,
        ["SubscriptionId"] = Subscription,
        ["ResourceGroup"] = ResourceGroup,
        ["ArtifactVersion"] = "1.0.0+synthetic",
        ["ManagedIdentity:ClientId"] = ClientId,
        ["ManagedIdentity:PrincipalId"] = PrincipalId,
        ["ManagedIdentity:ResourceId"] = IdentityResourceId,
        ["DocumentIntelligence:Endpoint"] = "https://di-taxai-prod-cc.cognitiveservices.azure.com/",
        ["DocumentIntelligence:ResourceId"] =
            ResourcePrefix + "Microsoft.CognitiveServices/accounts/di-taxai-prod-cc",
        ["DocumentIntelligence:Region"] = "canadacentral",
        ["DocumentIntelligence:PublicNetworkAccess"] = "Disabled",
        ["DocumentIntelligence:PrivateEndpoint"] = "Required",
        ["DocumentIntelligence:ApiVersion"] = "2024-11-30",
        ["DocumentIntelligence:Model"] = "prebuilt-layout",
        ["DocumentIntelligence:Features"] = "keyValuePairs",
        ["SourceStorage:Endpoint"] = "https://sttaxaisrccc.blob.core.windows.net/",
        ["SourceStorage:ResourceId"] = ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxaisrccc",
        ["SourceStorage:Region"] = "canadacentral",
        ["SourceStorage:PublicNetworkAccess"] = "Disabled",
        ["SourceStorage:PrivateEndpoint"] = "Required",
        ["KeyVault:Endpoint"] = "https://kv-taxai-prod-cc.vault.azure.net/",
        ["KeyVault:ResourceId"] = ResourcePrefix + "Microsoft.KeyVault/vaults/kv-taxai-prod-cc",
        ["KeyVault:Region"] = "canadacentral",
        ["KeyVault:PublicNetworkAccess"] = "Disabled",
        ["KeyVault:PrivateEndpoint"] = "Required",
        ["Sql:Endpoint"] = "sql-taxai-prod-cc.database.windows.net",
        ["Sql:ResourceId"] = ResourcePrefix + "Microsoft.Sql/servers/sql-taxai-prod-cc",
        ["Sql:Region"] = "canadacentral",
        ["Sql:PublicNetworkAccess"] = "Disabled",
        ["Sql:PrivateEndpoint"] = "Required",
        ["Monitoring:WorkspaceResourceId"] =
            ResourcePrefix + "Microsoft.OperationalInsights/workspaces/log-taxai-prod-cc",
        ["Monitoring:Region"] = "canadacentral",
        ["Monitoring:RetentionDays"] = "30",
        ["SourceEnvelope:KeyUri"] = "https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope",
        ["DataProtection:StorageEndpoint"] = "https://sttaxaictlcc.blob.core.windows.net/",
        ["DataProtection:StorageResourceId"] =
            ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxaictlcc",
        ["DataProtection:KeyRingContainer"] = "data-protection",
        ["DataProtection:KeyUri"] = "https://kv-taxai-prod-cc.vault.azure.net/keys/data-protection",
        ["DataProtection:ApplicationDiscriminator"] = "taxai-portal-production",
        ["Retention:FinalizedSourceDays"] = "30",
        ["Retention:RejectedSourceDays"] = "7",
        ["Retention:DuplicateSourceDays"] = "7",
        ["Retention:AbandonedSourceDays"] = "30",
        ["Retention:StagedUploadHours"] = "24",
        ["Resilience:MaximumAttempts"] = "3",
        ["Resilience:OperationDeadline"] = "00:02:00",
        ["Resilience:ProbeDeadline"] = "00:00:15",
        ["Recovery:Profile"] = "Primary",
        ["Recovery:ControlLedger:Endpoint"] = "https://sttaxailedcc.blob.core.windows.net/",
        ["Recovery:ControlLedger:ResourceId"] = ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxailedcc",
        ["Recovery:ControlLedger:Region"] = "canadacentral",
        ["Recovery:ControlLedger:PublicNetworkAccess"] = "Disabled",
        ["Recovery:ControlLedger:PrivateEndpoint"] = "Required",
        ["Recovery:ControlLedgerReplica:Endpoint"] = "https://sttaxailedce.blob.core.windows.net/",
        ["Recovery:ControlLedgerReplica:ResourceId"] = ResourcePrefix + "Microsoft.Storage/storageAccounts/sttaxailedce",
        ["Recovery:ControlLedgerReplica:Region"] = "canadaeast",
        ["Recovery:ControlLedgerReplica:PublicNetworkAccess"] = "Disabled",
        ["Recovery:ControlLedgerReplica:PrivateEndpoint"] = "Required",
        ["Recovery:SqlPointInTimeRestoreDays"] = "35",
        ["Recovery:BackupChainDays"] = "7",
        ["Recovery:ControlLedgerRetentionDays"] = "90",
    };

    /// <summary>
    /// Spec 016 T06: the manual Canada East application-recovery profile. SQL
    /// and the failed-over source account are the Canada East ones; Document
    /// Intelligence, Key Vault and monitoring stay Canada Central.
    /// </summary>
    internal static Dictionary<string, string?> RecoverySettings()
    {
        var settings = ApprovedSettings();
        settings["Recovery:Profile"] = "RegionalRecovery";
        settings["Sql:Region"] = "canadaeast";
        settings["SourceStorage:Region"] = "canadaeast";
        return settings;
    }

    internal static AzureInfrastructureOptions Options(IReadOnlyDictionary<string, string?> s) => new()
    {
        Environment = s.GetValueOrDefault("Environment"),
        PrimaryRegion = s.GetValueOrDefault("PrimaryRegion"),
        RecoveryRegion = s.GetValueOrDefault("RecoveryRegion"),
        TenantId = s.GetValueOrDefault("TenantId"),
        SubscriptionId = s.GetValueOrDefault("SubscriptionId"),
        ResourceGroup = s.GetValueOrDefault("ResourceGroup"),
        ArtifactVersion = s.GetValueOrDefault("ArtifactVersion"),
        ManagedIdentity = new ManagedIdentitySettings
        {
            ClientId = s.GetValueOrDefault("ManagedIdentity:ClientId"),
            PrincipalId = s.GetValueOrDefault("ManagedIdentity:PrincipalId"),
            ResourceId = s.GetValueOrDefault("ManagedIdentity:ResourceId"),
        },
        DocumentIntelligence = new DocumentIntelligenceSettings
        {
            Resource = Dependency(s, "DocumentIntelligence"),
            ApiVersion = s.GetValueOrDefault("DocumentIntelligence:ApiVersion"),
            Model = s.GetValueOrDefault("DocumentIntelligence:Model"),
            Features = s.GetValueOrDefault("DocumentIntelligence:Features"),
        },
        SourceStorage = Dependency(s, "SourceStorage"),
        KeyVault = Dependency(s, "KeyVault"),
        Sql = Dependency(s, "Sql"),
        Monitoring = new MonitoringSettings
        {
            WorkspaceResourceId = s.GetValueOrDefault("Monitoring:WorkspaceResourceId"),
            Region = s.GetValueOrDefault("Monitoring:Region"),
            RetentionDays = s.GetValueOrDefault("Monitoring:RetentionDays"),
        },
        SourceEnvelope = new SourceEnvelopeSettings
        {
            KeyUri = s.GetValueOrDefault("SourceEnvelope:KeyUri"),
        },
        DataProtection = new DataProtectionSettings
        {
            StorageEndpoint = s.GetValueOrDefault("DataProtection:StorageEndpoint"),
            StorageResourceId = s.GetValueOrDefault("DataProtection:StorageResourceId"),
            KeyRingContainer = s.GetValueOrDefault("DataProtection:KeyRingContainer"),
            KeyUri = s.GetValueOrDefault("DataProtection:KeyUri"),
            ApplicationDiscriminator = s.GetValueOrDefault("DataProtection:ApplicationDiscriminator"),
        },
        Retention = new RetentionSettings
        {
            FinalizedSourceDays = s.GetValueOrDefault("Retention:FinalizedSourceDays"),
            RejectedSourceDays = s.GetValueOrDefault("Retention:RejectedSourceDays"),
            DuplicateSourceDays = s.GetValueOrDefault("Retention:DuplicateSourceDays"),
            AbandonedSourceDays = s.GetValueOrDefault("Retention:AbandonedSourceDays"),
            StagedUploadHours = s.GetValueOrDefault("Retention:StagedUploadHours"),
        },
        Resilience = new ResilienceSettings
        {
            MaximumAttempts = s.GetValueOrDefault("Resilience:MaximumAttempts"),
            OperationDeadline = s.GetValueOrDefault("Resilience:OperationDeadline"),
            ProbeDeadline = s.GetValueOrDefault("Resilience:ProbeDeadline"),
        },
        Recovery = new RecoverySettings
        {
            Profile = s.GetValueOrDefault("Recovery:Profile"),
            ControlLedger = Dependency(s, "Recovery:ControlLedger"),
            ControlLedgerReplica = Dependency(s, "Recovery:ControlLedgerReplica"),
            SqlPointInTimeRestoreDays = s.GetValueOrDefault("Recovery:SqlPointInTimeRestoreDays"),
            BackupChainDays = s.GetValueOrDefault("Recovery:BackupChainDays"),
            ControlLedgerRetentionDays = s.GetValueOrDefault("Recovery:ControlLedgerRetentionDays"),
        },
    };

    private static PrivateDependencySettings Dependency(
        IReadOnlyDictionary<string, string?> s,
        string name) => new()
        {
            Endpoint = s.GetValueOrDefault(name + ":Endpoint"),
            ResourceId = s.GetValueOrDefault(name + ":ResourceId"),
            Region = s.GetValueOrDefault(name + ":Region"),
            PublicNetworkAccess = s.GetValueOrDefault(name + ":PublicNetworkAccess"),
            PrivateEndpoint = s.GetValueOrDefault(name + ":PrivateEndpoint"),
        };

    // ------------------------------------------------------ positive control

    [Fact]
    public void TheApprovedSyntheticProfilePassesStructuralValidation()
    {
        var result = _validator.Validate(Options(ApprovedSettings()), ConnectionString);

        Assert.Empty(result.Refusals);
        Assert.True(result.IsAdmitted);
    }

    [Fact]
    public void AnEmptyProfileRefusesWithEveryMissingDependencyAndNoAffirmativeDefault()
    {
        var result = _validator.Validate(new AzureInfrastructureOptions(), null);

        Assert.False(result.IsAdmitted);
        foreach (var expected in new[]
                 {
                     ProductionAdmissionRefusal.EnvironmentNotProduction,
                     ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral,
                     ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast,
                     ProductionAdmissionRefusal.DeploymentIdentityInvalid,
                     ProductionAdmissionRefusal.ManagedIdentityBindingMissing,
                     ProductionAdmissionRefusal.DocumentIntelligenceConfigurationMissing,
                     ProductionAdmissionRefusal.StorageConfigurationMissing,
                     ProductionAdmissionRefusal.KeyVaultConfigurationMissing,
                     ProductionAdmissionRefusal.SqlConfigurationMissing,
                     ProductionAdmissionRefusal.MonitoringConfigurationMissing,
                     ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing,
                     ProductionAdmissionRefusal.DataProtectionConfigurationMissing,
                     ProductionAdmissionRefusal.RetentionOutsidePolicy,
                     ProductionAdmissionRefusal.ResilienceUnbounded,
                 })
        {
            Assert.Contains(expected, result.Refusals);
        }
    }

    // ---------------------------------------------- spec 016 T06: recovery

    [Fact]
    public void TheRegionalRecoveryProfilePassesWithCanadaEastDataAndCanadaCentralDocumentIntelligence()
    {
        var result = _validator.Validate(Options(RecoverySettings()), ConnectionString);

        Assert.Empty(result.Refusals);
        Assert.True(ProductionConfigurationValidator.IsRegionalRecovery(Options(RecoverySettings())));
        Assert.False(ProductionConfigurationValidator.IsRegionalRecovery(Options(ApprovedSettings())));
    }

    public static TheoryData<string, string?, ProductionAdmissionRefusal> RecoveryProfileRefusals() => new()
    {
        // No Document Intelligence failover, to Canada East or anywhere else.
        { "DocumentIntelligence:Region", "canadaeast", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        // The recovery profile's data really is the Canada East data.
        { "Sql:Region", "canadacentral", ProductionAdmissionRefusal.SqlBindingInvalid },
        { "SourceStorage:Region", "canadacentral", ProductionAdmissionRefusal.StorageBindingInvalid },
        // The vault and the monitoring workspace do not move.
        { "KeyVault:Region", "canadaeast", ProductionAdmissionRefusal.KeyVaultBindingInvalid },
        { "Monitoring:Region", "canadaeast", ProductionAdmissionRefusal.MonitoringBindingInvalid },
        // Recovery never relocates the policy primary region.
        { "PrimaryRegion", "canadaeast", ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral },
    };

    [Theory]
    [MemberData(nameof(RecoveryProfileRefusals))]
    public void TheRecoveryProfileRefusesEveryForbiddenFailover(
        string key,
        string? value,
        ProductionAdmissionRefusal expected)
    {
        var settings = RecoverySettings();
        settings[key] = value;

        Assert.Contains(expected, _validator.Validate(Options(settings), ConnectionString).Refusals);
    }

    [Fact]
    public void ThePrimaryProfileStillRefusesCanadaEastData()
    {
        var settings = ApprovedSettings();
        settings["Sql:Region"] = "canadaeast";
        settings["SourceStorage:Region"] = "canadaeast";

        var refusals = _validator.Validate(Options(settings), ConnectionString).Refusals;

        Assert.Contains(ProductionAdmissionRefusal.SqlBindingInvalid, refusals);
        Assert.Contains(ProductionAdmissionRefusal.StorageBindingInvalid, refusals);
    }

    public static TheoryData<string, string?, ProductionAdmissionRefusal> RecoverySettingRefusals() => new()
    {
        { "Recovery:Profile", null, ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast },
        { "Recovery:Profile", "primary", ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast },
        { "Recovery:Profile", "CanadaCentralRecovery", ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast },
        { "Recovery:ControlLedger:Endpoint", null, ProductionAdmissionRefusal.StorageConfigurationMissing },
        { "Recovery:ControlLedgerReplica:ResourceId", "", ProductionAdmissionRefusal.StorageConfigurationMissing },
        { "Recovery:ControlLedger:Region", "canadaeast", ProductionAdmissionRefusal.StorageBindingInvalid },
        { "Recovery:ControlLedgerReplica:Region", "canadacentral", ProductionAdmissionRefusal.StorageBindingInvalid },
        { "Recovery:ControlLedgerReplica:Region", "eastus", ProductionAdmissionRefusal.StorageBindingInvalid },
        { "Recovery:ControlLedger:Endpoint", "https://sttaxailedce.blob.core.windows.net/", ProductionAdmissionRefusal.StorageBindingInvalid },
        { "Recovery:ControlLedgerReplica:PublicNetworkAccess", "Enabled", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "Recovery:ControlLedger:PrivateEndpoint", "Optional", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "Recovery:SqlPointInTimeRestoreDays", "7", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Recovery:SqlPointInTimeRestoreDays", "36", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Recovery:SqlPointInTimeRestoreDays", null, ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Recovery:BackupChainDays", "6", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Recovery:ControlLedgerRetentionDays", "42", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Recovery:ControlLedgerRetentionDays", "366", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Recovery:ControlLedgerRetentionDays", "-90", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Recovery:ControlLedgerRetentionDays", "P90D", ProductionAdmissionRefusal.RetentionOutsidePolicy },
    };

    [Theory]
    [MemberData(nameof(RecoverySettingRefusals))]
    public void EachRecoverySettingRefusesIndependently(
        string key,
        string? value,
        ProductionAdmissionRefusal expected)
    {
        var settings = ApprovedSettings();
        settings[key] = value;

        var result = _validator.Validate(Options(settings), ConnectionString);

        Assert.False(result.IsAdmitted);
        Assert.Contains(expected, result.Refusals);
    }

    [Theory]
    [InlineData("Recovery:ControlLedger:Endpoint", "Recovery:ControlLedger:ResourceId", "sttaxaisrccc")]
    [InlineData("Recovery:ControlLedgerReplica:Endpoint", "Recovery:ControlLedgerReplica:ResourceId", "sttaxaictlcc")]
    public void AControlLedgerCopyThatSharesAnotherStorageRoleIsRefused(string endpoint, string resourceId, string account)
    {
        // Sharing the source or key-ring account would make them one failure
        // and one permission domain.
        var settings = ApprovedSettings();
        settings[endpoint] = $"https://{account}.blob.core.windows.net/";
        settings[resourceId] = ResourcePrefix + "Microsoft.Storage/storageAccounts/" + account;

        Assert.Contains(
            ProductionAdmissionRefusal.StorageBindingInvalid,
            _validator.Validate(Options(settings), ConnectionString).Refusals);
    }

    [Fact]
    public void TheRecoveryWindowsAreReadOnlyWhenTheLedgerOutlivesEveryBackup()
    {
        Assert.True(ProductionConfigurationValidator.TryReadRecoveryWindows(
            Options(ApprovedSettings()), out var restoreDays, out var chainDays, out var ledgerDays));
        Assert.Equal(35, restoreDays);
        Assert.Equal(7, chainDays);
        Assert.Equal(90, ledgerDays);
        Assert.True(TimeSpan.FromDays(ledgerDays) > TimeSpan.FromDays(restoreDays + chainDays));

        var equal = ApprovedSettings();
        equal["Recovery:ControlLedgerRetentionDays"] = "42";
        Assert.False(ProductionConfigurationValidator.TryReadRecoveryWindows(Options(equal), out _, out _, out _));
    }

    // ------------------------------------------- one change, one refusal

    public static TheoryData<string, string?, ProductionAdmissionRefusal> SingleSettingRefusals() => new()
    {
        // AC-130: the production path must say Production explicitly.
        { "Environment", null, ProductionAdmissionRefusal.EnvironmentNotProduction },
        { "Environment", "Staging", ProductionAdmissionRefusal.EnvironmentNotProduction },
        { "Environment", "production", ProductionAdmissionRefusal.EnvironmentNotProduction },
        // AC-131: primary outside Canada Central; recovery only Canada East.
        { "PrimaryRegion", "canadaeast", ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral },
        { "PrimaryRegion", "eastus", ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral },
        { "PrimaryRegion", null, ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral },
        { "RecoveryRegion", "westus", ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast },
        { "RecoveryRegion", "canadacentral", ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast },
        { "TenantId", "not-a-guid", ProductionAdmissionRefusal.DeploymentIdentityInvalid },
        { "SubscriptionId", "00000000-0000-0000-0000-000000000000", ProductionAdmissionRefusal.DeploymentIdentityInvalid },
        { "ArtifactVersion", null, ProductionAdmissionRefusal.DeploymentIdentityInvalid },
        { "ArtifactVersion", "1.0 with space", ProductionAdmissionRefusal.DeploymentIdentityInvalid },
        // AC-134: nonapproved or preview DI API version, model, feature.
        { "DocumentIntelligence:ApiVersion", "2023-07-31", ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved },
        { "DocumentIntelligence:ApiVersion", "2024-07-31-preview", ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved },
        { "DocumentIntelligence:ApiVersion", null, ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved },
        { "DocumentIntelligence:Model", "prebuilt-tax.us.w2", ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved },
        { "DocumentIntelligence:Model", "prebuilt-read", ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved },
        { "DocumentIntelligence:Features", "keyValuePairs,queryFields", ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved },
        // AC-017: any preview feature anywhere in the profile.
        { "DocumentIntelligence:ApiVersion", "2024-07-31-preview", ProductionAdmissionRefusal.PreviewFeatureConfigured },
        { "DocumentIntelligence:Features", "queryFields-preview", ProductionAdmissionRefusal.PreviewFeatureConfigured },
        // AC-135: endpoint without matching resource/region evidence.
        { "DocumentIntelligence:Endpoint", "https://canadacentral.api.cognitive.microsoft.com/", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:Endpoint", "https://di-other.cognitiveservices.azure.com/", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:Region", "eastus", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:Region", "canadaeast", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:ResourceId", ResourcePrefix + "Microsoft.CognitiveServices/accounts/di-other", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:ResourceId", "/subscriptions/99999999-9999-4999-8999-999999999999/resourceGroups/" + ResourceGroup + "/providers/Microsoft.CognitiveServices/accounts/di-taxai-prod-cc", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:ResourceId", ResourcePrefix + "Microsoft.Storage/storageAccounts/di-taxai-prod-cc", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:Endpoint", "https://di-taxai-prod-cc.cognitiveservices.azure.com/formrecognizer", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
        { "DocumentIntelligence:Endpoint", null, ProductionAdmissionRefusal.DocumentIntelligenceConfigurationMissing },
        // AC-136, AC-137, AC-138: omitted Storage, Key Vault and SQL.
        { "SourceStorage:Endpoint", null, ProductionAdmissionRefusal.StorageConfigurationMissing },
        { "SourceStorage:ResourceId", "", ProductionAdmissionRefusal.StorageConfigurationMissing },
        { "SourceStorage:Endpoint", "https://sttaxaiother.blob.core.windows.net/", ProductionAdmissionRefusal.StorageBindingInvalid },
        { "KeyVault:Endpoint", null, ProductionAdmissionRefusal.KeyVaultConfigurationMissing },
        { "KeyVault:Region", "eastus", ProductionAdmissionRefusal.KeyVaultBindingInvalid },
        { "Sql:Endpoint", null, ProductionAdmissionRefusal.SqlConfigurationMissing },
        { "Sql:Endpoint", "sql-other.database.windows.net", ProductionAdmissionRefusal.SqlBindingInvalid },
        // AC-139: omitted or invalid monitoring.
        { "Monitoring:WorkspaceResourceId", null, ProductionAdmissionRefusal.MonitoringBindingInvalid },
        { "Monitoring:Region", "eastus", ProductionAdmissionRefusal.MonitoringBindingInvalid },
        { "Monitoring:RetentionDays", "90", ProductionAdmissionRefusal.MonitoringBindingInvalid },
        // AC-140: omitted managed identity binding.
        { "ManagedIdentity:ClientId", null, ProductionAdmissionRefusal.ManagedIdentityBindingMissing },
        { "ManagedIdentity:PrincipalId", "not-a-guid", ProductionAdmissionRefusal.ManagedIdentityBindingMissing },
        { "ManagedIdentity:ResourceId", ResourcePrefix + "Microsoft.Storage/storageAccounts/id-taxai-portal-cc", ProductionAdmissionRefusal.ManagedIdentityBindingMissing },
        // AC-141: omitted source envelope encryption.
        { "SourceEnvelope:KeyUri", null, ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing },
        { "SourceEnvelope:KeyUri", "https://kv-other.vault.azure.net/keys/source-envelope", ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing },
        { "SourceEnvelope:KeyUri", "https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope/0123456789abcdef", ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing },
        { "SourceEnvelope:KeyUri", "https://kv-taxai-prod-cc.vault.azure.net/secrets/source-envelope", ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing },
        // AC-143 (configuration half): shared protected key ring.
        { "DataProtection:KeyUri", null, ProductionAdmissionRefusal.DataProtectionConfigurationMissing },
        { "DataProtection:KeyUri", "https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope", ProductionAdmissionRefusal.DataProtectionConfigurationMissing },
        { "DataProtection:StorageEndpoint", "https://sttaxaisrccc.blob.core.windows.net/", ProductionAdmissionRefusal.DataProtectionConfigurationMissing },
        { "DataProtection:KeyRingContainer", "Data_Protection", ProductionAdmissionRefusal.DataProtectionConfigurationMissing },
        { "DataProtection:ApplicationDiscriminator", null, ProductionAdmissionRefusal.DataProtectionConfigurationMissing },
        // AC-145: retention outside approved policy.
        { "Retention:FinalizedSourceDays", "31", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Retention:RejectedSourceDays", "0", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Retention:DuplicateSourceDays", "14", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Retention:AbandonedSourceDays", null, ProductionAdmissionRefusal.RetentionOutsidePolicy },
        { "Retention:StagedUploadHours", "48", ProductionAdmissionRefusal.RetentionOutsidePolicy },
        // AC-146: unbounded retries or no deadline.
        { "Resilience:MaximumAttempts", "0", ProductionAdmissionRefusal.ResilienceUnbounded },
        { "Resilience:MaximumAttempts", "6", ProductionAdmissionRefusal.ResilienceUnbounded },
        { "Resilience:MaximumAttempts", "-1", ProductionAdmissionRefusal.ResilienceUnbounded },
        { "Resilience:OperationDeadline", null, ProductionAdmissionRefusal.ResilienceUnbounded },
        { "Resilience:OperationDeadline", "00:00:00", ProductionAdmissionRefusal.ResilienceUnbounded },
        { "Resilience:OperationDeadline", "1.00:00:00", ProductionAdmissionRefusal.ResilienceUnbounded },
        { "Resilience:ProbeDeadline", "00:05:00", ProductionAdmissionRefusal.ResilienceUnbounded },
        // AC-147: public dependency endpoint substitution.
        { "SourceStorage:PublicNetworkAccess", "Enabled", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "KeyVault:PrivateEndpoint", "Optional", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "Sql:PublicNetworkAccess", "Enabled", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "DocumentIntelligence:PublicNetworkAccess", "Enabled", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "SourceStorage:Endpoint", "http://sttaxaisrccc.blob.core.windows.net/", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "KeyVault:Endpoint", "https://203.0.113.10/", ProductionAdmissionRefusal.PublicEndpointConfigured },
        { "Sql:Endpoint", "203.0.113.10", ProductionAdmissionRefusal.PublicEndpointConfigured },
        // AC-148: an arbitrary outbound destination is not the bound resource.
        { "SourceStorage:Endpoint", "https://attacker.example/", ProductionAdmissionRefusal.StorageBindingInvalid },
        { "KeyVault:Endpoint", "https://kv-taxai-prod-cc.privatelink.vaultcore.azure.net/", ProductionAdmissionRefusal.KeyVaultBindingInvalid },
        { "DocumentIntelligence:Endpoint", "https://di-taxai-prod-cc.cognitiveservices.azure.com:8443/", ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid },
    };

    [Theory] // DEP-23
    [MemberData(nameof(SingleSettingRefusals))]
    public void EachIndividualSettingChangeProducesItsRefusal(
        string setting,
        string? value,
        ProductionAdmissionRefusal expected)
    {
        var settings = ApprovedSettings();
        settings[setting] = value;

        var result = _validator.Validate(Options(settings), ConnectionString);

        Assert.False(result.IsAdmitted);
        Assert.Contains(expected, result.Refusals);
    }

    public static TheoryData<string, ProductionAdmissionRefusal> ConnectionRefusals() => new()
    {
        // AC-144: unencrypted or unvalidated SQL transport.
        { ConnectionString.Replace("Encrypt=Strict", "Encrypt=False"), ProductionAdmissionRefusal.SqlTransportUnprotected },
        { ConnectionString.Replace("Encrypt=Strict;", string.Empty), ProductionAdmissionRefusal.SqlTransportUnprotected },
        { ConnectionString.Replace("TrustServerCertificate=False", "TrustServerCertificate=True"), ProductionAdmissionRefusal.SqlTransportUnprotected },
        { ConnectionString.Replace("Encrypt=Strict", "Encrypt=Optional"), ProductionAdmissionRefusal.SqlTransportUnprotected },
        // Stored credentials and foreign servers are never accepted.
        { ConnectionString + ";Password=synthetic", ProductionAdmissionRefusal.SqlBindingInvalid },
        { ConnectionString.Replace("Authentication=Active Directory Managed Identity", "Authentication=SqlPassword"), ProductionAdmissionRefusal.SqlBindingInvalid },
        { ConnectionString.Replace("sql-taxai-prod-cc.database.windows.net", "sql-other.database.windows.net"), ProductionAdmissionRefusal.SqlBindingInvalid },
        { ConnectionString.Replace(",1433", ",14330"), ProductionAdmissionRefusal.SqlBindingInvalid },
        { ConnectionString.Replace("User Id=" + ClientId, "User Id=55555555-5555-4555-8555-555555555555"), ProductionAdmissionRefusal.SqlBindingInvalid },
        { ConnectionString + ";Integrated Security=True", ProductionAdmissionRefusal.SqlBindingInvalid },
        { "not a connection string ===", ProductionAdmissionRefusal.SqlBindingInvalid },
        { "Server=\"unterminated", ProductionAdmissionRefusal.SqlConfigurationMissing },
        { "   ", ProductionAdmissionRefusal.SqlConfigurationMissing },
    };

    [Theory] // AC-138, AC-144
    [MemberData(nameof(ConnectionRefusals))]
    public void EachUnsafeSqlConnectionProducesItsRefusal(
        string connectionString,
        ProductionAdmissionRefusal expected)
    {
        var result = _validator.Validate(Options(ApprovedSettings()), connectionString);

        Assert.Contains(expected, result.Refusals);
    }

    [Theory]
    [InlineData("Encrypt=True")]
    [InlineData("Encrypt=Mandatory")]
    public void TheOtherMandatoryEncryptionSpellingsAreAccepted(string encrypt)
    {
        var result = _validator.Validate(
            Options(ApprovedSettings()),
            ConnectionString.Replace("Encrypt=Strict", encrypt));

        Assert.True(result.IsAdmitted);
    }

    [Fact]
    public void RefusalsAccumulateRatherThanStoppingAtTheFirst()
    {
        var settings = ApprovedSettings();
        settings["PrimaryRegion"] = "eastus";
        settings["Retention:StagedUploadHours"] = "72";
        settings["KeyVault:PublicNetworkAccess"] = "Enabled";

        var result = _validator.Validate(Options(settings), ConnectionString);

        Assert.Equal(
            new[]
            {
                ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral,
                ProductionAdmissionRefusal.PublicEndpointConfigured,
                ProductionAdmissionRefusal.RetentionOutsidePolicy,
            },
            result.Refusals);
    }

    [Fact]
    public void TheApprovedPolicyConstantsAreTheSpecifiedValues()
    {
        Assert.Equal("Production", ProductionConfigurationValidator.ProductionEnvironment);
        Assert.Equal("canadacentral", ProductionConfigurationValidator.PrimaryRegion);
        Assert.Equal("canadaeast", ProductionConfigurationValidator.RecoveryRegion);
        Assert.Equal("2024-11-30", ProductionConfigurationValidator.DocumentIntelligenceApiVersion);
        Assert.Equal("prebuilt-layout", ProductionConfigurationValidator.DocumentIntelligenceModel);
        Assert.Equal("keyValuePairs", ProductionConfigurationValidator.DocumentIntelligenceFeatures);
        Assert.Equal(30, ProductionConfigurationValidator.FinalizedSourceDays);
        Assert.Equal(7, ProductionConfigurationValidator.RejectedSourceDays);
        Assert.Equal(7, ProductionConfigurationValidator.DuplicateSourceDays);
        Assert.Equal(30, ProductionConfigurationValidator.AbandonedSourceDays);
        Assert.Equal(24, ProductionConfigurationValidator.StagedUploadHours);
        Assert.Equal(30, ProductionConfigurationValidator.MonitoringRetentionDays);
    }

    [Fact]
    public void TheManagedIdentityBindingAndReleaseBindingAreDerivedFromTheValidProfile()
    {
        var options = Options(ApprovedSettings());

        Assert.True(ProductionConfigurationValidator.TryCreateManagedIdentityBinding(options, out var binding));
        Assert.Equal(Guid.Parse(Tenant), binding!.TenantId);
        Assert.Equal(Guid.Parse(ClientId), binding.ClientId);
        Assert.Equal(Guid.Parse(PrincipalId), binding.PrincipalId);
        Assert.Equal(IdentityResourceId, binding.ResourceId);

        Assert.True(ProductionConfigurationValidator.TryCreateReleaseBinding(options, "abc123", out var release));
        Assert.Equal("1.0.0+synthetic", release!.ArtifactVersion);
        Assert.Equal("abc123", release.ConfigurationDigest);
        Assert.Equal(Guid.Parse(Subscription), release.SubscriptionId);

        Assert.True(ProductionConfigurationValidator.TryReadProbeDeadline(options, out var deadline));
        Assert.Equal(TimeSpan.FromSeconds(15), deadline);
    }

    [Fact]
    public void NoRefusalOrToStringEchoesAConfiguredValue()
    {
        var settings = ApprovedSettings();
        settings["SourceStorage:Endpoint"] = "https://planted-secret-host.example/";
        var options = Options(settings);

        var result = _validator.Validate(options, ConnectionString + ";Password=planted-password");
        var rendered = string.Join(
            "|",
            result.Refusals.Select(refusal => refusal.ToString())
                .Append(result.ToString())
                .Append(options.ToString())
                .Append(options.SourceStorage.ToString())
                .Append(_validator.ToString()));

        Assert.DoesNotContain("planted", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Subscription, rendered, StringComparison.Ordinal);
    }
}
