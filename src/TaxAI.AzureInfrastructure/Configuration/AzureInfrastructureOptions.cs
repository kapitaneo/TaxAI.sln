namespace TaxAI.AzureInfrastructure.Configuration;

/// <summary>
/// The production runtime profile as it was supplied, before validation.
/// <para>
/// Every value is the raw configured text and every default is <c>null</c>.
/// Nothing here is permissive by omission: a missing value is not "use the
/// platform default", it is a refusal decided by
/// <see cref="ProductionConfigurationValidator"/>. There is deliberately no
/// boolean that enables production and no free-form destination list.
/// </para>
/// <para>
/// This type holds infrastructure identifiers only. It holds no secret,
/// credential, connection string or taxpayer value, and its
/// <see cref="ToString"/> is structural so a configured value cannot reach a
/// diagnostic by accident.
/// </para>
/// </summary>
public sealed class AzureInfrastructureOptions
{
    /// <summary>Must be exactly <c>Production</c>.</summary>
    public string? Environment { get; init; }

    /// <summary>Must be exactly <c>canadacentral</c>.</summary>
    public string? PrimaryRegion { get; init; }

    /// <summary>Must be exactly <c>canadaeast</c>, the recovery-only region.</summary>
    public string? RecoveryRegion { get; init; }

    public string? TenantId { get; init; }

    public string? SubscriptionId { get; init; }

    public string? ResourceGroup { get; init; }

    /// <summary>The exact artifact version the release evidence names.</summary>
    public string? ArtifactVersion { get; init; }

    public ManagedIdentitySettings ManagedIdentity { get; init; } = new();

    public DocumentIntelligenceSettings DocumentIntelligence { get; init; } = new();

    /// <summary>
    /// The private Canadian source account. Spec 016 T04 builds the source store,
    /// reader and purger from its endpoint (the HTTPS Blob service root) and the
    /// fixed <c>taxai-source</c> container; the container name is not configurable.
    /// </summary>
    public PrivateDependencySettings SourceStorage { get; init; } = new();

    public PrivateDependencySettings KeyVault { get; init; } = new();

    public PrivateDependencySettings Sql { get; init; } = new();

    public MonitoringSettings Monitoring { get; init; } = new();

    public SourceEnvelopeSettings SourceEnvelope { get; init; } = new();

    public DataProtectionSettings DataProtection { get; init; } = new();

    public RetentionSettings Retention { get; init; } = new();

    public ResilienceSettings Resilience { get; init; } = new();

    /// <summary>
    /// Spec 016 T06: the recovery profile, the two copies of the independent
    /// control ledger and the backup windows its retention must outlive.
    /// </summary>
    public RecoverySettings Recovery { get; init; } = new();

    public override string ToString() => nameof(AzureInfrastructureOptions);
}

/// <summary>The user-assigned managed identity the host must run as.</summary>
public sealed class ManagedIdentitySettings
{
    public string? ClientId { get; init; }

    public string? PrincipalId { get; init; }

    public string? ResourceId { get; init; }

    public override string ToString() => nameof(ManagedIdentitySettings);
}

/// <summary>
/// One private Azure dependency: its resource-specific endpoint, the Azure
/// resource that endpoint must belong to, that resource's region, and its
/// network posture. The endpoint is accepted only when it is the resource's own
/// host name, so a hostname alone never stands in for resource or region proof.
/// </summary>
public sealed class PrivateDependencySettings
{
    public string? Endpoint { get; init; }

    public string? ResourceId { get; init; }

    public string? Region { get; init; }

    /// <summary>Must be exactly <c>Disabled</c>.</summary>
    public string? PublicNetworkAccess { get; init; }

    /// <summary>Must be exactly <c>Required</c>.</summary>
    public string? PrivateEndpoint { get; init; }

    public override string ToString() => nameof(PrivateDependencySettings);
}

/// <summary>The one approved Canada Central Document Intelligence resource.</summary>
public sealed class DocumentIntelligenceSettings
{
    /// <summary>The resource-specific custom-subdomain endpoint and its binding.</summary>
    public PrivateDependencySettings Resource { get; init; } = new();

    /// <summary>Must be exactly the GA <c>2024-11-30</c>.</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Must be exactly <c>prebuilt-layout</c>.</summary>
    public string? Model { get; init; }

    /// <summary>Must be exactly <c>keyValuePairs</c>.</summary>
    public string? Features { get; init; }

    public override string ToString() => nameof(DocumentIntelligenceSettings);
}

/// <summary>The Canada Central Log Analytics workspace and its retention.</summary>
public sealed class MonitoringSettings
{
    public string? WorkspaceResourceId { get; init; }

    public string? Region { get; init; }

    /// <summary>Must be exactly the approved 30-day default.</summary>
    public string? RetentionDays { get; init; }

    public override string ToString() => nameof(MonitoringSettings);
}

/// <summary>
/// The Key Vault key that wraps per-content source encryption keys. It must be a
/// versionless key identifier in the configured vault. Spec 016 T03 probes its
/// posture and wrap capability at admission; T04 wraps every source envelope's
/// fresh data key with it and records the exact version that wrapped it. The
/// envelope format itself is fixed in code, not configured.
/// </summary>
public sealed class SourceEnvelopeSettings
{
    public string? KeyUri { get; init; }

    public override string ToString() => nameof(SourceEnvelopeSettings);
}

/// <summary>
/// The shared protected Data Protection key ring: a private control storage
/// account separate from source content, the container holding the key ring,
/// the Key Vault wrapping key, and the production application discriminator.
/// Spec 016 T03 configures the production key ring from exactly these values
/// (key-ring blob <c>key-ring.xml</c> in the container) and the configured
/// managed identity; no other key-ring setting exists.
/// </summary>
public sealed class DataProtectionSettings
{
    public string? StorageEndpoint { get; init; }

    public string? StorageResourceId { get; init; }

    public string? KeyRingContainer { get; init; }

    public string? KeyUri { get; init; }

    public string? ApplicationDiscriminator { get; init; }

    public override string ToString() => nameof(DataProtectionSettings);
}

/// <summary>
/// The approved source lifecycle deadlines (spec 016 FR-09). They are policy,
/// not tuning: any other value is refused. Structured history keeps the
/// existing seven-year Tenancy boundary, which is not configurable here.
/// </summary>
public sealed class RetentionSettings
{
    public string? FinalizedSourceDays { get; init; }

    public string? RejectedSourceDays { get; init; }

    public string? DuplicateSourceDays { get; init; }

    public string? AbandonedSourceDays { get; init; }

    public string? StagedUploadHours { get; init; }

    public override string ToString() => nameof(RetentionSettings);
}

/// <summary>Finite retry and deadline bounds for dependency calls.</summary>
public sealed class ResilienceSettings
{
    public string? MaximumAttempts { get; init; }

    public string? OperationDeadline { get; init; }

    public string? ProbeDeadline { get; init; }

    public override string ToString() => nameof(ResilienceSettings);
}

/// <summary>
/// The recovery profile and the independent control ledger (spec 016 T06,
/// FR-10, FR-11). Every value is policy, not tuning:
/// <list type="bullet">
/// <item><see cref="Profile"/> is exactly <c>Primary</c> or
/// <c>RegionalRecovery</c>. The recovery profile runs the application with a
/// Canada East SQL database and failed-over Canada East source storage while
/// Document Intelligence stays the Canada Central resource.</item>
/// <item><see cref="ControlLedger"/> is the Canada Central copy and
/// <see cref="ControlLedgerReplica"/> the authoritative, independently
/// recoverable Canada East copy; two distinct private accounts.</item>
/// <item>SQL point-in-time retention is exactly 35 days, the backup-chain
/// allowance at least 7 days, and the ledger retention strictly longer than
/// their sum.</item>
/// </list>
/// </summary>
public sealed class RecoverySettings
{
    public string? Profile { get; init; }

    public PrivateDependencySettings ControlLedger { get; init; } = new();

    public PrivateDependencySettings ControlLedgerReplica { get; init; } = new();

    public string? SqlPointInTimeRestoreDays { get; init; }

    public string? BackupChainDays { get; init; }

    public string? ControlLedgerRetentionDays { get; init; }

    public override string ToString() => nameof(RecoverySettings);
}
