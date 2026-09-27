using System.Text.Json;
using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace TaxAI.Portal.Production;

/// <summary>
/// The closed production runtime profile read from configuration (spec 016
/// FR-14).
/// <para>
/// Every accepted key is listed in <see cref="SettingNames"/>, and the same
/// set is the contract of <c>infra/azure/contracts/runtime-configuration.schema.json</c>
/// and the app settings emitted by <c>infra/azure/modules/runtime-configuration.bicep</c>.
/// Any other key under the production prefix — an extra endpoint, destination,
/// switch or override — is an unapproved entry and refuses admission. There is
/// no key that enables production by itself.
/// </para>
/// <para>
/// The evidence manifest is read from one setting as JSON. It is infrastructure
/// evidence only and never carries a secret. The configuration digest covers
/// every other production key and value, so the manifest cannot authorize a
/// configuration it was not issued for.
/// </para>
/// </summary>
internal static class ProductionConfiguration
{
    internal const string Section = "TaxAi:Production";
    internal const string Prefix = Section + ":";

    internal const string EnvironmentSetting = Prefix + "Environment";
    internal const string PrimaryRegionSetting = Prefix + "PrimaryRegion";
    internal const string RecoveryRegionSetting = Prefix + "RecoveryRegion";
    internal const string TenantIdSetting = Prefix + "TenantId";
    internal const string SubscriptionIdSetting = Prefix + "SubscriptionId";
    internal const string ResourceGroupSetting = Prefix + "ResourceGroup";
    internal const string ArtifactVersionSetting = Prefix + "ArtifactVersion";

    internal const string ManagedIdentityClientIdSetting = Prefix + "ManagedIdentity:ClientId";
    internal const string ManagedIdentityPrincipalIdSetting = Prefix + "ManagedIdentity:PrincipalId";
    internal const string ManagedIdentityResourceIdSetting = Prefix + "ManagedIdentity:ResourceId";

    internal const string DocumentIntelligencePrefix = Prefix + "DocumentIntelligence:";
    internal const string DocumentIntelligenceApiVersionSetting = DocumentIntelligencePrefix + "ApiVersion";
    internal const string DocumentIntelligenceModelSetting = DocumentIntelligencePrefix + "Model";
    internal const string DocumentIntelligenceFeaturesSetting = DocumentIntelligencePrefix + "Features";

    internal const string SourceStoragePrefix = Prefix + "SourceStorage:";
    internal const string KeyVaultPrefix = Prefix + "KeyVault:";
    internal const string SqlPrefix = Prefix + "Sql:";

    internal const string MonitoringWorkspaceSetting = Prefix + "Monitoring:WorkspaceResourceId";
    internal const string MonitoringRegionSetting = Prefix + "Monitoring:Region";
    internal const string MonitoringRetentionDaysSetting = Prefix + "Monitoring:RetentionDays";

    internal const string SourceEnvelopeKeyUriSetting = Prefix + "SourceEnvelope:KeyUri";

    internal const string DataProtectionStorageEndpointSetting = Prefix + "DataProtection:StorageEndpoint";
    internal const string DataProtectionStorageResourceIdSetting = Prefix + "DataProtection:StorageResourceId";
    internal const string DataProtectionKeyRingContainerSetting = Prefix + "DataProtection:KeyRingContainer";
    internal const string DataProtectionKeyUriSetting = Prefix + "DataProtection:KeyUri";
    internal const string DataProtectionApplicationDiscriminatorSetting =
        Prefix + "DataProtection:ApplicationDiscriminator";

    internal const string RetentionFinalizedSourceDaysSetting = Prefix + "Retention:FinalizedSourceDays";
    internal const string RetentionRejectedSourceDaysSetting = Prefix + "Retention:RejectedSourceDays";
    internal const string RetentionDuplicateSourceDaysSetting = Prefix + "Retention:DuplicateSourceDays";
    internal const string RetentionAbandonedSourceDaysSetting = Prefix + "Retention:AbandonedSourceDays";
    internal const string RetentionStagedUploadHoursSetting = Prefix + "Retention:StagedUploadHours";

    internal const string ResilienceMaximumAttemptsSetting = Prefix + "Resilience:MaximumAttempts";
    internal const string ResilienceOperationDeadlineSetting = Prefix + "Resilience:OperationDeadline";
    internal const string ResilienceProbeDeadlineSetting = Prefix + "Resilience:ProbeDeadline";

    internal const string EvidenceManifestSetting = Prefix + "EvidenceManifest";

    private const string EndpointName = "Endpoint";
    private const string ResourceIdName = "ResourceId";
    private const string RegionName = "Region";
    private const string PublicNetworkAccessName = "PublicNetworkAccess";
    private const string PrivateEndpointName = "PrivateEndpoint";

    /// <summary>
    /// Every approved production key except the evidence manifest. The set is
    /// closed: the configuration digest covers exactly these keys.
    /// </summary>
    internal static readonly IReadOnlyList<string> SettingNames = new[]
    {
        EnvironmentSetting,
        PrimaryRegionSetting,
        RecoveryRegionSetting,
        TenantIdSetting,
        SubscriptionIdSetting,
        ResourceGroupSetting,
        ArtifactVersionSetting,
        ManagedIdentityClientIdSetting,
        ManagedIdentityPrincipalIdSetting,
        ManagedIdentityResourceIdSetting,
    }
    .Concat(DependencyKeys(DocumentIntelligencePrefix))
    .Concat(new[]
    {
        DocumentIntelligenceApiVersionSetting,
        DocumentIntelligenceModelSetting,
        DocumentIntelligenceFeaturesSetting,
    })
    .Concat(DependencyKeys(SourceStoragePrefix))
    .Concat(DependencyKeys(KeyVaultPrefix))
    .Concat(DependencyKeys(SqlPrefix))
    .Concat(new[]
    {
        MonitoringWorkspaceSetting,
        MonitoringRegionSetting,
        MonitoringRetentionDaysSetting,
        SourceEnvelopeKeyUriSetting,
        DataProtectionStorageEndpointSetting,
        DataProtectionStorageResourceIdSetting,
        DataProtectionKeyRingContainerSetting,
        DataProtectionKeyUriSetting,
        DataProtectionApplicationDiscriminatorSetting,
        RetentionFinalizedSourceDaysSetting,
        RetentionRejectedSourceDaysSetting,
        RetentionDuplicateSourceDaysSetting,
        RetentionAbandonedSourceDaysSetting,
        RetentionStagedUploadHoursSetting,
        ResilienceMaximumAttemptsSetting,
        ResilienceOperationDeadlineSetting,
        ResilienceProbeDeadlineSetting,
    })
    .ToArray();

    /// <summary>
    /// Reads the production profile. Never throws for missing or malformed
    /// values: those become refusals when the snapshot is validated.
    /// </summary>
    internal static ProductionConfigurationSnapshot Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var approved = new HashSet<string>(
            SettingNames.Append(EvidenceManifestSetting),
            StringComparer.OrdinalIgnoreCase);

        var supplied = configuration
            .GetSection(Section)
            .AsEnumerable()
            .Where(entry => entry.Value is not null)
            .ToArray();

        var hasUnapprovedEntry = supplied.Any(entry => !approved.Contains(entry.Key));

        var digest = ProductionEvidencePolicy.ComputeConfigurationDigest(
            SettingNames.Select(name =>
                new KeyValuePair<string, string?>(name, configuration[name])));

        var manifestText = configuration[EvidenceManifestSetting];
        var manifest = ReadManifest(manifestText, out var manifestInvalid);

        return new ProductionConfigurationSnapshot(
            ReadOptions(configuration),
            hasUnapprovedEntry,
            digest,
            manifest,
            manifestInvalid);
    }

    private static AzureInfrastructureOptions ReadOptions(IConfiguration configuration) =>
        new()
        {
            Environment = configuration[EnvironmentSetting],
            PrimaryRegion = configuration[PrimaryRegionSetting],
            RecoveryRegion = configuration[RecoveryRegionSetting],
            TenantId = configuration[TenantIdSetting],
            SubscriptionId = configuration[SubscriptionIdSetting],
            ResourceGroup = configuration[ResourceGroupSetting],
            ArtifactVersion = configuration[ArtifactVersionSetting],
            ManagedIdentity = new ManagedIdentitySettings
            {
                ClientId = configuration[ManagedIdentityClientIdSetting],
                PrincipalId = configuration[ManagedIdentityPrincipalIdSetting],
                ResourceId = configuration[ManagedIdentityResourceIdSetting],
            },
            DocumentIntelligence = new DocumentIntelligenceSettings
            {
                Resource = ReadDependency(configuration, DocumentIntelligencePrefix),
                ApiVersion = configuration[DocumentIntelligenceApiVersionSetting],
                Model = configuration[DocumentIntelligenceModelSetting],
                Features = configuration[DocumentIntelligenceFeaturesSetting],
            },
            SourceStorage = ReadDependency(configuration, SourceStoragePrefix),
            KeyVault = ReadDependency(configuration, KeyVaultPrefix),
            Sql = ReadDependency(configuration, SqlPrefix),
            Monitoring = new MonitoringSettings
            {
                WorkspaceResourceId = configuration[MonitoringWorkspaceSetting],
                Region = configuration[MonitoringRegionSetting],
                RetentionDays = configuration[MonitoringRetentionDaysSetting],
            },
            SourceEnvelope = new SourceEnvelopeSettings
            {
                KeyUri = configuration[SourceEnvelopeKeyUriSetting],
            },
            DataProtection = new DataProtectionSettings
            {
                StorageEndpoint = configuration[DataProtectionStorageEndpointSetting],
                StorageResourceId = configuration[DataProtectionStorageResourceIdSetting],
                KeyRingContainer = configuration[DataProtectionKeyRingContainerSetting],
                KeyUri = configuration[DataProtectionKeyUriSetting],
                ApplicationDiscriminator = configuration[DataProtectionApplicationDiscriminatorSetting],
            },
            Retention = new RetentionSettings
            {
                FinalizedSourceDays = configuration[RetentionFinalizedSourceDaysSetting],
                RejectedSourceDays = configuration[RetentionRejectedSourceDaysSetting],
                DuplicateSourceDays = configuration[RetentionDuplicateSourceDaysSetting],
                AbandonedSourceDays = configuration[RetentionAbandonedSourceDaysSetting],
                StagedUploadHours = configuration[RetentionStagedUploadHoursSetting],
            },
            Resilience = new ResilienceSettings
            {
                MaximumAttempts = configuration[ResilienceMaximumAttemptsSetting],
                OperationDeadline = configuration[ResilienceOperationDeadlineSetting],
                ProbeDeadline = configuration[ResilienceProbeDeadlineSetting],
            },
        };

    private static PrivateDependencySettings ReadDependency(
        IConfiguration configuration,
        string prefix) =>
        new()
        {
            Endpoint = configuration[prefix + EndpointName],
            ResourceId = configuration[prefix + ResourceIdName],
            Region = configuration[prefix + RegionName],
            PublicNetworkAccess = configuration[prefix + PublicNetworkAccessName],
            PrivateEndpoint = configuration[prefix + PrivateEndpointName],
        };

    private static IEnumerable<string> DependencyKeys(string prefix) =>
        new[]
        {
            prefix + EndpointName,
            prefix + ResourceIdName,
            prefix + RegionName,
            prefix + PublicNetworkAccessName,
            prefix + PrivateEndpointName,
        };

    /// <summary>
    /// Parses the manifest JSON into Tenancy's evidence shapes. Any structural
    /// problem makes the whole manifest invalid rather than partially trusted.
    /// </summary>
    private static ProductionEvidenceManifest? ReadManifest(
        string? text,
        out bool invalid)
    {
        invalid = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                RequiredInt(root, "schemaVersion") != 1)
            {
                throw new FormatException();
            }

            var release = ReadRelease(root);
            var evidence = RequiredArray(root, "evidence")
                .Select(item => new ProductionEvidenceItem(
                    ReadEvidenceSubject(RequiredString(item, "subject")),
                    ReadStatus(RequiredString(item, "status")),
                    RequiredInstant(item, "observedAtUtc"),
                    RequiredInstant(item, "expiresAtUtc")))
                .ToArray();
            var assurances = RequiredArray(root, "assurances")
                .Select(item => new ProviderAssurance(
                    RequiredEnum<ProviderAssuranceSubject>(item, "subject"),
                    ReadStatus(RequiredString(item, "status")),
                    RequiredInstant(item, "observedAtUtc"),
                    RequiredInstant(item, "expiresAtUtc")))
                .ToArray();
            var approvals = RequiredArray(root, "approvals")
                .Select(item => new ProductionApproval(
                    RequiredEnum<ProductionApprovalRole>(item, "role"),
                    RequiredInstant(item, "approvedAtUtc"),
                    ReadRelease(item)))
                .ToArray();

            return new ProductionEvidenceManifest(release, evidence, assurances, approvals);
        }
        catch (Exception failure) when (failure is
            JsonException or
            FormatException or
            ArgumentException or
            InvalidOperationException or
            KeyNotFoundException)
        {
            invalid = true;
            return null;
        }
    }

    private static ProductionReleaseBinding ReadRelease(JsonElement element)
    {
        var subscription = RequiredString(element, "subscriptionId");
        if (!Guid.TryParseExact(subscription, "D", out var subscriptionId))
        {
            throw new FormatException();
        }

        return new ProductionReleaseBinding(
            RequiredString(element, "artifactVersion"),
            RequiredString(element, "configurationDigest"),
            subscriptionId);
    }

    /// <summary>Accepts exactly <c>DEP-01</c> through <c>DEP-24</c>.</summary>
    private static ProductionEvidenceSubject ReadEvidenceSubject(string value)
    {
        if (value.Length != 6 ||
            !value.StartsWith("DEP-", StringComparison.Ordinal) ||
            !int.TryParse(
                value.AsSpan(4),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number) ||
            !Enum.IsDefined((ProductionEvidenceSubject)number))
        {
            throw new FormatException();
        }

        return (ProductionEvidenceSubject)number;
    }

    /// <summary>
    /// Only the four named states parse; <c>Absent</c> is never written, it is
    /// what a missing item means.
    /// </summary>
    private static ProductionEvidenceStatus ReadStatus(string value) => value switch
    {
        "Invalid" => ProductionEvidenceStatus.Invalid,
        "Unavailable" => ProductionEvidenceStatus.Unavailable,
        "Pending" => ProductionEvidenceStatus.Pending,
        "Approved" => ProductionEvidenceStatus.Approved,
        _ => throw new FormatException(),
    };

    private static TEnum RequiredEnum<TEnum>(JsonElement element, string name)
        where TEnum : struct, Enum
    {
        var value = RequiredString(element, name);
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) ||
            !Enum.IsDefined(parsed) ||
            !string.Equals(parsed.ToString(), value, StringComparison.Ordinal))
        {
            throw new FormatException();
        }

        return parsed;
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException();
        }

        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement element, string name) =>
        element.GetProperty(name).GetInt32();

    private static DateTimeOffset RequiredInstant(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetDateTimeOffset();
        if (value.Offset != TimeSpan.Zero)
        {
            throw new FormatException();
        }

        return value;
    }

    private static IEnumerable<JsonElement> RequiredArray(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException();
        }

        return value.EnumerateArray().ToArray();
    }
}

/// <summary>
/// The production profile as read. It carries infrastructure identifiers and a
/// digest only; its <see cref="ToString"/> is structural.
/// </summary>
internal sealed class ProductionConfigurationSnapshot
{
    internal ProductionConfigurationSnapshot(
        AzureInfrastructureOptions options,
        bool hasUnapprovedEntry,
        string configurationDigest,
        ProductionEvidenceManifest? manifest,
        bool manifestInvalid)
    {
        Options = options;
        HasUnapprovedEntry = hasUnapprovedEntry;
        ConfigurationDigest = configurationDigest;
        Manifest = manifest;
        ManifestInvalid = manifestInvalid;
    }

    internal AzureInfrastructureOptions Options { get; }

    internal bool HasUnapprovedEntry { get; }

    internal string ConfigurationDigest { get; }

    internal ProductionEvidenceManifest? Manifest { get; }

    internal bool ManifestInvalid { get; }

    public override string ToString() => nameof(ProductionConfigurationSnapshot);
}
