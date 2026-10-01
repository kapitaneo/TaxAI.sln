using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Configuration;

/// <summary>
/// The structural production-profile validator (spec 016 FR-14).
/// <para>
/// It answers one question: does the supplied profile describe exactly the
/// approved Canadian production topology? Every rule is an allowlist of one
/// value or one bound resource, never a blocklist, and every failure is a
/// closed <see cref="ProductionAdmissionRefusal"/> that names what is wrong
/// without quoting the configured value.
/// </para>
/// <para>
/// All failures are accumulated rather than short-circuited, so each individual
/// refusal is independently observable (DEP-23). Passing this validator is
/// necessary and never sufficient: registrations, the managed-identity probe,
/// the exact release binding and the evidence manifest are separate gates.
/// </para>
/// <para>
/// An endpoint hostname alone never proves a resource or region. Each endpoint
/// is accepted only when it is the named resource's own host, that resource
/// belongs to the configured subscription and resource group, and the resource
/// is recorded in Canada Central with public network access disabled.
/// </para>
/// </summary>
public sealed class ProductionConfigurationValidator
{
    public const string ProductionEnvironment = "Production";
    public const string PrimaryRegion = "canadacentral";
    public const string RecoveryRegion = "canadaeast";
    public const string DocumentIntelligenceApiVersion = "2024-11-30";
    public const string DocumentIntelligenceModel = "prebuilt-layout";
    public const string DocumentIntelligenceFeatures = "keyValuePairs";
    public const string PublicNetworkAccessDisabled = "Disabled";
    public const string PrivateEndpointRequired = "Required";

    /// <summary>Spec 016 T06: the normal Canada Central deployment profile.</summary>
    public const string PrimaryProfile = "Primary";

    /// <summary>
    /// Spec 016 T06: manual Canada East application recovery. The SQL database
    /// and source storage are the Canada East ones; Document Intelligence, Key
    /// Vault and monitoring remain the Canada Central resources, so extraction
    /// stays unavailable while Canada Central is.
    /// </summary>
    public const string RegionalRecoveryProfile = "RegionalRecovery";

    public const int FinalizedSourceDays = 30;
    public const int RejectedSourceDays = 7;
    public const int DuplicateSourceDays = 7;
    public const int AbandonedSourceDays = 30;
    public const int StagedUploadHours = 24;
    public const int MonitoringRetentionDays = 30;

    /// <summary>
    /// [ASSUMED] Upper bounds on the finite resilience settings. They make an
    /// unbounded or effectively unbounded value impossible; the exact release
    /// values are chosen within them and recorded in the approved configuration.
    /// </summary>
    public const int MaximumAttemptsBound = 5;

    public static readonly TimeSpan OperationDeadlineBound = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan ProbeDeadlineBound = TimeSpan.FromMinutes(1);

    private const string CognitiveServicesType = "Microsoft.CognitiveServices/accounts";
    private const string StorageType = "Microsoft.Storage/storageAccounts";
    private const string KeyVaultType = "Microsoft.KeyVault/vaults";
    private const string SqlType = "Microsoft.Sql/servers";
    private const string WorkspaceType = "Microsoft.OperationalInsights/workspaces";
    private const string IdentityType = "Microsoft.ManagedIdentity/userAssignedIdentities";

    private const string CognitiveServicesSuffix = ".cognitiveservices.azure.com";
    private const string BlobSuffix = ".blob.core.windows.net";
    private const string KeyVaultSuffix = ".vault.azure.net";
    private const string SqlSuffix = ".database.windows.net";

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex ResourceIdPattern = new(
        "^/subscriptions/(?<subscription>[0-9a-fA-F-]{36})/resourceGroups/(?<group>[A-Za-z0-9._()-]{1,90})" +
        "/providers/(?<namespace>[A-Za-z0-9.]+)/(?<type>[A-Za-z0-9]+)/(?<name>[A-Za-z0-9-]{1,64})$",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    private static readonly Regex ResourceGroupPattern = new(
        "^[A-Za-z0-9._()-]{0,89}[A-Za-z0-9_()-]$",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    private static readonly Regex KeyNamePattern = new(
        "^[A-Za-z0-9-]{1,127}$",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    private static readonly Regex ContainerPattern = new(
        "^[a-z0-9](?!.*--)[a-z0-9-]{1,61}[a-z0-9]$",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    private static readonly Regex DiscriminatorPattern = new(
        "^[A-Za-z0-9._-]{1,64}$",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    private static readonly Regex ArtifactVersionPattern = new(
        "^[A-Za-z0-9.+_-]{1,128}$",
        RegexOptions.CultureInvariant,
        PatternTimeout);

    /// <summary>
    /// Validates <paramref name="options"/> and the application database
    /// connection string against the approved production profile.
    /// </summary>
    public ProductionReadiness Validate(
        AzureInfrastructureOptions options,
        string? databaseConnectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        var refusals = new List<ProductionAdmissionRefusal>();

        if (!IsExactly(options.Environment, ProductionEnvironment))
        {
            refusals.Add(ProductionAdmissionRefusal.EnvironmentNotProduction);
        }

        if (!IsExactly(options.PrimaryRegion, PrimaryRegion))
        {
            refusals.Add(ProductionAdmissionRefusal.PrimaryRegionNotCanadaCentral);
        }

        if (!IsExactly(options.RecoveryRegion, RecoveryRegion))
        {
            refusals.Add(ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast);
        }

        var deployment = ReadDeployment(options);
        if (deployment is null)
        {
            refusals.Add(ProductionAdmissionRefusal.DeploymentIdentityInvalid);
        }

        if (AllValues(options).Any(value =>
                value.Contains("preview", StringComparison.OrdinalIgnoreCase)))
        {
            refusals.Add(ProductionAdmissionRefusal.PreviewFeatureConfigured);
        }

        if (TryCreateManagedIdentityBinding(options, out _) is false)
        {
            refusals.Add(ProductionAdmissionRefusal.ManagedIdentityBindingMissing);
        }

        ValidateDocumentIntelligence(options.DocumentIntelligence, deployment, refusals);

        // Spec 016 T06: an unknown profile is never silently the primary one.
        var recovering = IsExactly(options.Recovery.Profile, RegionalRecoveryProfile);
        if (!recovering && !IsExactly(options.Recovery.Profile, PrimaryProfile))
        {
            refusals.Add(ProductionAdmissionRefusal.RecoveryRegionNotCanadaEast);
        }

        var dataRegion = recovering ? RecoveryRegion : PrimaryRegion;

        var sourceStorage = ValidateDependency(
            options.SourceStorage,
            deployment,
            StorageType,
            BlobSuffix,
            dataRegion,
            endpointIsHostName: false,
            ProductionAdmissionRefusal.StorageConfigurationMissing,
            ProductionAdmissionRefusal.StorageBindingInvalid,
            refusals);

        var keyVault = ValidateDependency(
            options.KeyVault,
            deployment,
            KeyVaultType,
            KeyVaultSuffix,
            PrimaryRegion,
            endpointIsHostName: false,
            ProductionAdmissionRefusal.KeyVaultConfigurationMissing,
            ProductionAdmissionRefusal.KeyVaultBindingInvalid,
            refusals);

        var sqlHost = ValidateDependency(
            options.Sql,
            deployment,
            SqlType,
            SqlSuffix,
            dataRegion,
            endpointIsHostName: true,
            ProductionAdmissionRefusal.SqlConfigurationMissing,
            ProductionAdmissionRefusal.SqlBindingInvalid,
            refusals);

        ValidateSqlConnection(
            databaseConnectionString,
            sqlHost,
            options.ManagedIdentity.ClientId,
            refusals);

        ValidateMonitoring(options.Monitoring, deployment, refusals);

        var sourceKey = ReadVersionlessKey(options.SourceEnvelope.KeyUri, keyVault);
        if (sourceKey is null)
        {
            refusals.Add(ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing);
        }

        ValidateDataProtection(
            options.DataProtection,
            deployment,
            sourceStorage,
            keyVault,
            sourceKey,
            refusals);

        ValidateRecovery(options, deployment, sourceStorage, refusals);

        if (!IsInteger(options.Retention.FinalizedSourceDays, FinalizedSourceDays) ||
            !IsInteger(options.Retention.RejectedSourceDays, RejectedSourceDays) ||
            !IsInteger(options.Retention.DuplicateSourceDays, DuplicateSourceDays) ||
            !IsInteger(options.Retention.AbandonedSourceDays, AbandonedSourceDays) ||
            !IsInteger(options.Retention.StagedUploadHours, StagedUploadHours))
        {
            refusals.Add(ProductionAdmissionRefusal.RetentionOutsidePolicy);
        }

        if (!TryReadAttempts(options.Resilience.MaximumAttempts, out _) ||
            !TryReadBoundedDuration(
                options.Resilience.OperationDeadline,
                OperationDeadlineBound,
                out _) ||
            !TryReadProbeDeadline(options, out _))
        {
            refusals.Add(ProductionAdmissionRefusal.ResilienceUnbounded);
        }

        return ProductionReadiness.FromRefusals(refusals);
    }

    /// <summary>
    /// Builds the managed-identity binding from a profile, or returns
    /// <see langword="false"/> when any part is missing or malformed.
    /// </summary>
    public static bool TryCreateManagedIdentityBinding(
        AzureInfrastructureOptions options,
        out ManagedIdentityBinding? binding)
    {
        ArgumentNullException.ThrowIfNull(options);

        binding = null;
        var deployment = ReadDeployment(options);
        if (deployment is null ||
            !TryReadGuid(options.ManagedIdentity.ClientId, out var clientId) ||
            !TryReadGuid(options.ManagedIdentity.PrincipalId, out var principalId) ||
            ReadResource(options.ManagedIdentity.ResourceId, deployment, IdentityType) is null)
        {
            return false;
        }

        binding = new ManagedIdentityBinding(
            deployment.TenantId,
            clientId,
            principalId,
            options.ManagedIdentity.ResourceId!);
        return true;
    }

    /// <summary>The validated finite probe deadline.</summary>
    public static bool TryReadProbeDeadline(
        AzureInfrastructureOptions options,
        out TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(options);

        return TryReadBoundedDuration(
            options.Resilience.ProbeDeadline,
            ProbeDeadlineBound,
            out deadline);
    }

    /// <summary>
    /// The validated release binding claimed by a profile, bound to the digest
    /// of its complete configuration.
    /// </summary>
    public static bool TryCreateReleaseBinding(
        AzureInfrastructureOptions options,
        string configurationDigest,
        out ProductionReleaseBinding? release)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDigest);

        release = null;
        var deployment = ReadDeployment(options);
        if (deployment is null)
        {
            return false;
        }

        release = new ProductionReleaseBinding(
            options.ArtifactVersion!,
            configurationDigest,
            deployment.SubscriptionId);
        return true;
    }

    private static void ValidateDocumentIntelligence(
        DocumentIntelligenceSettings settings,
        Deployment? deployment,
        List<ProductionAdmissionRefusal> refusals)
    {
        if (IsBlank(settings.ApiVersion) && IsBlank(settings.Model) &&
            IsBlank(settings.Features) && IsUnconfigured(settings.Resource))
        {
            refusals.Add(ProductionAdmissionRefusal.DocumentIntelligenceConfigurationMissing);
            return;
        }

        _ = ValidateDependency(
            settings.Resource,
            deployment,
            CognitiveServicesType,
            CognitiveServicesSuffix,
            PrimaryRegion,
            endpointIsHostName: false,
            ProductionAdmissionRefusal.DocumentIntelligenceConfigurationMissing,
            ProductionAdmissionRefusal.DocumentIntelligenceBindingInvalid,
            refusals);

        if (!IsExactly(settings.ApiVersion, DocumentIntelligenceApiVersion) ||
            !IsExactly(settings.Model, DocumentIntelligenceModel) ||
            !IsExactly(settings.Features, DocumentIntelligenceFeatures))
        {
            refusals.Add(ProductionAdmissionRefusal.DocumentIntelligenceVersionNotApproved);
        }
    }

    /// <summary>
    /// Validates one private dependency and returns its bound host name, or
    /// <see langword="null"/> when it is not a valid binding.
    /// </summary>
    private static string? ValidateDependency(
        PrivateDependencySettings settings,
        Deployment? deployment,
        string resourceType,
        string hostSuffix,
        string expectedRegion,
        bool endpointIsHostName,
        ProductionAdmissionRefusal missing,
        ProductionAdmissionRefusal invalidBinding,
        List<ProductionAdmissionRefusal> refusals)
    {
        if (IsBlank(settings.Endpoint) || IsBlank(settings.ResourceId) ||
            IsBlank(settings.Region) || IsBlank(settings.PublicNetworkAccess) ||
            IsBlank(settings.PrivateEndpoint))
        {
            refusals.Add(missing);
            return null;
        }

        if (!IsExactly(settings.PublicNetworkAccess, PublicNetworkAccessDisabled) ||
            !IsExactly(settings.PrivateEndpoint, PrivateEndpointRequired))
        {
            refusals.Add(ProductionAdmissionRefusal.PublicEndpointConfigured);
        }

        var host = endpointIsHostName
            ? ReadHostName(settings.Endpoint!, refusals)
            : ReadHttpsOrigin(settings.Endpoint!, refusals);

        var resourceName = deployment is null
            ? null
            : ReadResource(settings.ResourceId, deployment, resourceType);

        if (host is null ||
            resourceName is null ||
            !IsExactly(settings.Region, expectedRegion) ||
            !string.Equals(host, resourceName + hostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            refusals.Add(invalidBinding);
            return null;
        }

        return host;
    }

    /// <summary>
    /// Spec 016 T06: the two control-ledger copies are distinct private Canadian
    /// accounts (the Canada Central copy and the authoritative Canada East
    /// copy), neither of which is the source account or the Data Protection
    /// key-ring account; SQL point-in-time retention is exactly the 35-day
    /// maximum; and the ledger is retained strictly longer than the longest
    /// recoverable backup window.
    /// </summary>
    private static void ValidateRecovery(
        AzureInfrastructureOptions options,
        Deployment? deployment,
        string? sourceStorageHost,
        List<ProductionAdmissionRefusal> refusals)
    {
        var recovery = options.Recovery;

        var ledger = ValidateDependency(
            recovery.ControlLedger,
            deployment,
            StorageType,
            BlobSuffix,
            PrimaryRegion,
            endpointIsHostName: false,
            ProductionAdmissionRefusal.StorageConfigurationMissing,
            ProductionAdmissionRefusal.StorageBindingInvalid,
            refusals);

        var replica = ValidateDependency(
            recovery.ControlLedgerReplica,
            deployment,
            StorageType,
            BlobSuffix,
            RecoveryRegion,
            endpointIsHostName: false,
            ProductionAdmissionRefusal.StorageConfigurationMissing,
            ProductionAdmissionRefusal.StorageBindingInvalid,
            refusals);

        var scratch = new List<ProductionAdmissionRefusal>();
        var keyRingHost = IsBlank(options.DataProtection.StorageEndpoint)
            ? null
            : ReadHttpsOrigin(options.DataProtection.StorageEndpoint!, scratch);

        if (ledger is not null && replica is not null &&
            new[] { ledger, replica, sourceStorageHost, keyRingHost }
                .Where(host => host is not null)
                .GroupBy(host => host, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            // One account holding two roles is one failure domain for both.
            refusals.Add(ProductionAdmissionRefusal.StorageBindingInvalid);
        }

        if (!TryReadRecoveryWindows(options, out _, out _, out _))
        {
            refusals.Add(ProductionAdmissionRefusal.RetentionOutsidePolicy);
        }
    }

    /// <summary>
    /// Spec 016 T06: the validated recovery windows: SQL point-in-time
    /// retention of exactly 35 days, a backup-chain allowance of at least the
    /// documented seven days, and a ledger retention strictly longer than both
    /// and within the approved bound.
    /// </summary>
    public static bool TryReadRecoveryWindows(
        AzureInfrastructureOptions options,
        out int pointInTimeRestoreDays,
        out int backupChainDays,
        out int controlLedgerRetentionDays)
    {
        ArgumentNullException.ThrowIfNull(options);

        var recovery = options.Recovery;
        backupChainDays = 0;
        controlLedgerRetentionDays = 0;
        return RecoveryRetentionWindow.TryReadDays(recovery.SqlPointInTimeRestoreDays, out pointInTimeRestoreDays) &&
               pointInTimeRestoreDays == RecoveryRetentionWindow.PointInTimeRestoreDays &&
               RecoveryRetentionWindow.TryReadDays(recovery.BackupChainDays, out backupChainDays) &&
               RecoveryRetentionWindow.TryReadDays(recovery.ControlLedgerRetentionDays, out controlLedgerRetentionDays) &&
               RecoveryRetentionWindow.LedgerOutlivesBackups(
                   controlLedgerRetentionDays,
                   pointInTimeRestoreDays,
                   backupChainDays);
    }

    /// <summary>True only for the exact Canada East recovery profile.</summary>
    public static bool IsRegionalRecovery(AzureInfrastructureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return IsExactly(options.Recovery.Profile, RegionalRecoveryProfile);
    }

    private static void ValidateSqlConnection(
        string? connectionString,
        string? sqlHost,
        string? managedIdentityClientId,
        List<ProductionAdmissionRefusal> refusals)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            refusals.Add(ProductionAdmissionRefusal.SqlConfigurationMissing);
            return;
        }

        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            refusals.Add(ProductionAdmissionRefusal.SqlConfigurationMissing);
            return;
        }

        var encrypt = Read(builder, "Encrypt");
        var trustServerCertificate = Read(builder, "TrustServerCertificate", "Trust Server Certificate");
        if (!(IsOneOf(encrypt, "Strict", "True", "Mandatory", "Yes")) ||
            (trustServerCertificate is not null && !IsOneOf(trustServerCertificate, "False", "No")))
        {
            refusals.Add(ProductionAdmissionRefusal.SqlTransportUnprotected);
        }

        var server = Read(builder, "Server", "Data Source", "Address", "Addr", "Network Address");
        var authentication = Read(builder, "Authentication");
        var userId = Read(builder, "User ID", "UserID", "UID", "User");
        var hasPassword = Read(builder, "Password", "PWD") is not null;
        var integrated = Read(builder, "Integrated Security", "Trusted_Connection");

        if (sqlHost is null ||
            !string.Equals(ReadSqlServerHost(server), sqlHost, StringComparison.OrdinalIgnoreCase) ||
            !IsOneOf(authentication, "Active Directory Managed Identity", "Active Directory MSI") ||
            !string.Equals(userId, managedIdentityClientId, StringComparison.OrdinalIgnoreCase) ||
            hasPassword ||
            (integrated is not null && !IsOneOf(integrated, "False", "No")))
        {
            refusals.Add(ProductionAdmissionRefusal.SqlBindingInvalid);
        }
    }

    private static void ValidateMonitoring(
        MonitoringSettings settings,
        Deployment? deployment,
        List<ProductionAdmissionRefusal> refusals)
    {
        if (IsBlank(settings.WorkspaceResourceId) && IsBlank(settings.Region) &&
            IsBlank(settings.RetentionDays))
        {
            refusals.Add(ProductionAdmissionRefusal.MonitoringConfigurationMissing);
            return;
        }

        if (deployment is null ||
            ReadResource(settings.WorkspaceResourceId, deployment, WorkspaceType) is null ||
            !IsExactly(settings.Region, PrimaryRegion) ||
            !IsInteger(settings.RetentionDays, MonitoringRetentionDays))
        {
            refusals.Add(ProductionAdmissionRefusal.MonitoringBindingInvalid);
        }
    }

    private static void ValidateDataProtection(
        DataProtectionSettings settings,
        Deployment? deployment,
        string? sourceStorageHost,
        string? keyVaultHost,
        string? sourceKeyName,
        List<ProductionAdmissionRefusal> refusals)
    {
        var scratch = new List<ProductionAdmissionRefusal>();
        var storageHost = IsBlank(settings.StorageEndpoint)
            ? null
            : ReadHttpsOrigin(settings.StorageEndpoint!, scratch);
        var storageName = deployment is null
            ? null
            : ReadResource(settings.StorageResourceId, deployment, StorageType);
        var keyName = ReadVersionlessKey(settings.KeyUri, keyVaultHost);

        if (scratch.Count > 0)
        {
            refusals.AddRange(scratch);
        }

        if (storageHost is null ||
            storageName is null ||
            !string.Equals(storageHost, storageName + BlobSuffix, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(storageHost, sourceStorageHost, StringComparison.OrdinalIgnoreCase) ||
            !Matches(ContainerPattern, settings.KeyRingContainer) ||
            keyName is null ||
            string.Equals(keyName, sourceKeyName, StringComparison.OrdinalIgnoreCase) ||
            !Matches(DiscriminatorPattern, settings.ApplicationDiscriminator))
        {
            refusals.Add(ProductionAdmissionRefusal.DataProtectionConfigurationMissing);
        }
    }

    /// <summary>
    /// Reads an <c>https://host/</c> origin. Plain HTTP and IP-literal hosts are
    /// public-endpoint refusals; anything else malformed is simply unbound.
    /// </summary>
    private static string? ReadHttpsOrigin(
        string candidate,
        List<ProductionAdmissionRefusal> refusals)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            refusals.Add(ProductionAdmissionRefusal.PublicEndpointConfigured);
            return null;
        }

        if (!uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(candidate, uri.GetLeftPart(UriPartial.Authority) + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.IdnHost;
    }

    private static string? ReadHostName(
        string candidate,
        List<ProductionAdmissionRefusal> refusals)
    {
        if (IPAddress.TryParse(candidate, out _))
        {
            refusals.Add(ProductionAdmissionRefusal.PublicEndpointConfigured);
            return null;
        }

        return Uri.CheckHostName(candidate) == UriHostNameType.Dns ? candidate : null;
    }

    private static string? ReadSqlServerHost(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return null;
        }

        var host = server.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        var comma = host.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            if (!string.Equals(host[(comma + 1)..].Trim(), "1433", StringComparison.Ordinal))
            {
                return null;
            }

            host = host[..comma];
        }

        return host.Trim();
    }

    /// <summary>
    /// Reads a versionless Key Vault key identifier in the configured vault and
    /// returns the key name.
    /// </summary>
    private static string? ReadVersionlessKey(string? candidate, string? keyVaultHost)
    {
        if (keyVaultHost is null ||
            string.IsNullOrWhiteSpace(candidate) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.IdnHost, keyVaultHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/');
        return segments.Length == 3 &&
               segments[0].Length == 0 &&
               string.Equals(segments[1], "keys", StringComparison.Ordinal) &&
               KeyNamePattern.IsMatch(segments[2])
            ? segments[2]
            : null;
    }

    /// <summary>Returns the resource name when the identifier is bound.</summary>
    private static string? ReadResource(
        string? candidate,
        Deployment deployment,
        string resourceType)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var match = ResourceIdPattern.Match(candidate);
        if (!match.Success ||
            !Guid.TryParseExact(match.Groups["subscription"].Value, "D", out var subscription) ||
            subscription != deployment.SubscriptionId ||
            !string.Equals(match.Groups["group"].Value, deployment.ResourceGroup, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                $"{match.Groups["namespace"].Value}/{match.Groups["type"].Value}",
                resourceType,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return match.Groups["name"].Value;
    }

    private static Deployment? ReadDeployment(AzureInfrastructureOptions options)
    {
        if (!TryReadGuid(options.TenantId, out var tenant) ||
            !TryReadGuid(options.SubscriptionId, out var subscription) ||
            !Matches(ResourceGroupPattern, options.ResourceGroup) ||
            !Matches(ArtifactVersionPattern, options.ArtifactVersion))
        {
            return null;
        }

        return new Deployment(tenant, subscription, options.ResourceGroup!);
    }

    private static bool TryReadAttempts(string? candidate, out int attempts) =>
        int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out attempts) &&
        attempts >= 1 &&
        attempts <= MaximumAttemptsBound;

    private static bool TryReadBoundedDuration(
        string? candidate,
        TimeSpan bound,
        out TimeSpan duration) =>
        TimeSpan.TryParseExact(candidate, "c", CultureInfo.InvariantCulture, out duration) &&
        duration > TimeSpan.Zero &&
        duration <= bound;

    private static bool TryReadGuid(string? candidate, out Guid value) =>
        Guid.TryParseExact(candidate, "D", out value) && value != Guid.Empty;

    private static bool IsInteger(string? candidate, int expected) =>
        int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
        value == expected;

    private static bool IsExactly(string? candidate, string expected) =>
        string.Equals(candidate, expected, StringComparison.Ordinal);

    private static bool IsOneOf(string? candidate, params string[] accepted) =>
        candidate is not null &&
        accepted.Any(value => string.Equals(candidate.Trim(), value, StringComparison.OrdinalIgnoreCase));

    private static bool IsBlank(string? candidate) => string.IsNullOrWhiteSpace(candidate);

    private static bool IsUnconfigured(PrivateDependencySettings settings) =>
        IsBlank(settings.Endpoint) && IsBlank(settings.ResourceId) && IsBlank(settings.Region) &&
        IsBlank(settings.PublicNetworkAccess) && IsBlank(settings.PrivateEndpoint);

    private static bool Matches(Regex pattern, string? candidate) =>
        candidate is not null && pattern.IsMatch(candidate);

    private static string? Read(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value is not null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static IEnumerable<string> AllValues(AzureInfrastructureOptions options)
    {
        var values = new[]
        {
            options.Environment, options.PrimaryRegion, options.RecoveryRegion,
            options.TenantId, options.SubscriptionId, options.ResourceGroup, options.ArtifactVersion,
            options.ManagedIdentity.ClientId, options.ManagedIdentity.PrincipalId,
            options.ManagedIdentity.ResourceId,
            options.DocumentIntelligence.ApiVersion, options.DocumentIntelligence.Model,
            options.DocumentIntelligence.Features,
            options.Monitoring.WorkspaceResourceId, options.Monitoring.Region,
            options.Monitoring.RetentionDays,
            options.SourceEnvelope.KeyUri,
            options.DataProtection.StorageEndpoint, options.DataProtection.StorageResourceId,
            options.DataProtection.KeyRingContainer, options.DataProtection.KeyUri,
            options.DataProtection.ApplicationDiscriminator,
            options.Recovery.Profile, options.Recovery.SqlPointInTimeRestoreDays,
            options.Recovery.BackupChainDays, options.Recovery.ControlLedgerRetentionDays,
        }
        .Concat(DependencyValues(options.DocumentIntelligence.Resource))
        .Concat(DependencyValues(options.SourceStorage))
        .Concat(DependencyValues(options.KeyVault))
        .Concat(DependencyValues(options.Sql))
        .Concat(DependencyValues(options.Recovery.ControlLedger))
        .Concat(DependencyValues(options.Recovery.ControlLedgerReplica));

        return values.Where(value => value is not null)!;
    }

    private static IEnumerable<string?> DependencyValues(PrivateDependencySettings settings) =>
        new[]
        {
            settings.Endpoint, settings.ResourceId, settings.Region,
            settings.PublicNetworkAccess, settings.PrivateEndpoint,
        };

    public override string ToString() => nameof(ProductionConfigurationValidator);

    private sealed class Deployment
    {
        internal Deployment(Guid tenantId, Guid subscriptionId, string resourceGroup)
        {
            TenantId = tenantId;
            SubscriptionId = subscriptionId;
            ResourceGroup = resourceGroup;
        }

        internal Guid TenantId { get; }

        internal Guid SubscriptionId { get; }

        internal string ResourceGroup { get; }

        public override string ToString() => nameof(Deployment);
    }
}
