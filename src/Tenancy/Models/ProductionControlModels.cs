namespace Tenancy.Models;

/// <summary>
/// The twenty-four mandatory deployment-evidence subjects, DEP-01 through
/// DEP-24 of spec 016, in their approved order.
/// <para>
/// A subject names a check; it is never itself a result. The numeric value is
/// the DEP number, so a subject cannot be silently renumbered. There is no
/// zero member: an uninitialized subject is not a valid subject.
/// </para>
/// </summary>
public enum ProductionEvidenceSubject
{
    ThreatModelApproval = 1,
    InfrastructureReconstruction = 2,
    CanadianResourceInventory = 3,
    DocumentIntelligenceCombination = 4,
    ProcessorAssuranceApproval = 5,
    PrivateEndpointResolution = 6,
    PublicNetworkDisabled = 7,
    DefaultDenyOutbound = 8,
    UnapprovedEgressNonDelivery = 9,
    SourceByteNonDisclosure = 10,
    TelemetryProhibitedDataCapture = 11,
    AnalyzeResultDeletion = 12,
    FailedDeletionRefusal = 13,
    KeyRotationDrill = 14,
    SourceRetentionPurgeDrill = 15,
    StorageRepresentationPurge = 16,
    SqlRestoreDrill = 17,
    CanadaEastRecoveryDrill = 18,
    PostRestorePurgeReconciliation = 19,
    RevokedCredentialNonResurrection = 20,
    CrossTenantIsolation = 21,
    RepositoryTestGate = 22,
    StartupConfigurationRefusals = 23,
    ReleaseRoleApproval = 24,
}

/// <summary>
/// The observed state of one evidence item or assurance.
/// <para>
/// <see cref="Absent"/> is the zero value on purpose: a record nobody filled in
/// reads as missing, never as approved. Only <see cref="Approved"/> counts
/// toward admission; <see cref="Pending"/> is the honest state of evidence that
/// has not yet been observed against the exact resources.
/// </para>
/// </summary>
public enum ProductionEvidenceStatus
{
    Absent = 0,
    Invalid = 1,
    Unavailable = 2,
    Pending = 3,
    Approved = 4,
}

/// <summary>
/// The two provider assurance gaps that independently keep real-taxpayer
/// processing refused (AC-168, AC-169). Neither may be defaulted, inferred or
/// waived by an exception; each needs its own authoritative evidence.
/// </summary>
public enum ProviderAssuranceSubject
{
    InputCustomerManagedKeyCoverage = 1,
    RetentionDeletionClock = 2,
}

/// <summary>The two human roles that must each sign the exact release.</summary>
public enum ProductionApprovalRole
{
    ProductOwner = 1,
    SecurityPrivacy = 2,
}

/// <summary>
/// The closed vocabulary of reasons a production host is refused.
/// <para>
/// There is no "none" or "admitted" member: admission is the absence of every
/// refusal, not a code that can be supplied. The values are structural and
/// carry no configured value, identifier, endpoint or taxpayer content, so a
/// refusal can be reported without disclosing what was configured.
/// </para>
/// </summary>
public enum ProductionAdmissionRefusal
{
    EnvironmentNotProduction = 1,
    PrimaryRegionNotCanadaCentral = 2,
    RecoveryRegionNotCanadaEast = 3,
    DeploymentIdentityInvalid = 4,
    UnapprovedConfigurationEntry = 5,
    PreviewFeatureConfigured = 6,
    PublicEndpointConfigured = 7,
    DocumentIntelligenceConfigurationMissing = 8,
    DocumentIntelligenceVersionNotApproved = 9,
    DocumentIntelligenceBindingInvalid = 10,
    StorageConfigurationMissing = 11,
    StorageBindingInvalid = 12,
    KeyVaultConfigurationMissing = 13,
    KeyVaultBindingInvalid = 14,
    SqlConfigurationMissing = 15,
    SqlBindingInvalid = 16,
    SqlTransportUnprotected = 17,
    MonitoringConfigurationMissing = 18,
    MonitoringBindingInvalid = 19,
    ManagedIdentityBindingMissing = 20,
    ManagedIdentityBindingUnverified = 21,
    SourceEnvelopeEncryptionMissing = 22,
    DataProtectionConfigurationMissing = 23,
    RetentionOutsidePolicy = 24,
    ResilienceUnbounded = 25,
    DevelopmentProviderConfigured = 26,
    ProductionDocumentIntelligenceAdapterMissing = 27,
    LocalSourceStoreConfigured = 28,
    ProductionSourceStoreMissing = 29,
    ProductionSourceReaderMissing = 30,
    DurableDataProtectionMissing = 31,
    DurableSessionStoreMissing = 32,
    InfrastructureValidationUnregistered = 33,
    ReleaseBindingMismatch = 34,
    EvidenceManifestMissing = 35,
    EvidenceManifestInvalid = 36,
    MandatoryEvidenceIncomplete = 37,
    ProviderInputKeyAssuranceUnresolved = 38,
    ProviderRetentionClockAssuranceUnresolved = 39,
    ReleaseApprovalMissing = 40,
}

/// <summary>
/// One mandatory deployment-evidence item as recorded for an exact release.
/// It carries the check's subject, state and validity window only: no resource
/// identifier, endpoint, credential or taxpayer fact.
/// </summary>
public sealed class ProductionEvidenceItem
{
    public ProductionEvidenceItem(
        ProductionEvidenceSubject subject,
        ProductionEvidenceStatus status,
        DateTimeOffset observedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Subject = subject;
        Status = status;
        ObservedAtUtc = observedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public ProductionEvidenceSubject Subject { get; }

    public ProductionEvidenceStatus Status { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public override string ToString() => nameof(ProductionEvidenceItem);
}

/// <summary>
/// The recorded state of one provider assurance gap. Its only approving state is
/// <see cref="ProductionEvidenceStatus.Approved"/>, backed by authoritative
/// documentation and/or contractual evidence outside this type.
/// </summary>
public sealed class ProviderAssurance
{
    public ProviderAssurance(
        ProviderAssuranceSubject subject,
        ProductionEvidenceStatus status,
        DateTimeOffset observedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        Subject = subject;
        Status = status;
        ObservedAtUtc = observedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public ProviderAssuranceSubject Subject { get; }

    public ProductionEvidenceStatus Status { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public override string ToString() => nameof(ProviderAssurance);
}

/// <summary>
/// The result of evaluating production admission: the closed, ordered, distinct
/// set of refusals. Admission is true only when that set is empty.
/// </summary>
public sealed class ProductionReadiness
{
    private readonly ProductionAdmissionRefusal[] _refusals;

    private ProductionReadiness(ProductionAdmissionRefusal[] refusals)
    {
        _refusals = refusals;
    }

    /// <summary>
    /// Builds a readiness result from any number of refusals, de-duplicated and
    /// in numeric order so two evaluations of the same state compare equal.
    /// </summary>
    public static ProductionReadiness FromRefusals(
        IEnumerable<ProductionAdmissionRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        var closed = refusals.ToArray();
        if (closed.Any(refusal => !Enum.IsDefined(refusal)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusals),
                "A refusal must be a member of the closed vocabulary.");
        }

        return new ProductionReadiness(
            closed.Distinct().OrderBy(refusal => (int)refusal).ToArray());
    }

    /// <summary>Combines several partial results into one.</summary>
    public static ProductionReadiness Combine(
        params ProductionReadiness[] results)
    {
        ArgumentNullException.ThrowIfNull(results);

        return FromRefusals(results.SelectMany(result =>
        {
            ArgumentNullException.ThrowIfNull(result);
            return result.Refusals;
        }));
    }

    public IReadOnlyList<ProductionAdmissionRefusal> Refusals => _refusals.ToArray();

    public bool IsAdmitted => _refusals.Length == 0;

    public override string ToString() => nameof(ProductionReadiness);
}
