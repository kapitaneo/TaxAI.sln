using System.Text.Json.Serialization;
using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// The exact release a production host claims to be: the artifact version, the
/// digest of its complete production configuration, and the Azure subscription
/// that holds its approved inventory.
/// <para>
/// Evidence and approvals are valid only for the binding they name. A change to
/// any of the three is material drift, so evidence cannot be reused blindly for
/// a different build or configuration.
/// </para>
/// </summary>
public sealed class ProductionReleaseBinding
{
    public ProductionReleaseBinding(
        string artifactVersion,
        string configurationDigest,
        Guid subscriptionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDigest);

        ArtifactVersion = artifactVersion;
        ConfigurationDigest = configurationDigest;
        SubscriptionId = subscriptionId;
    }

    public string ArtifactVersion { get; }

    public string ConfigurationDigest { get; }

    [JsonIgnore]
    public Guid SubscriptionId { get; }

    /// <summary>Ordinal comparison of all three parts.</summary>
    public bool Matches(ProductionReleaseBinding other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(ArtifactVersion, other.ArtifactVersion, StringComparison.Ordinal) &&
               string.Equals(ConfigurationDigest, other.ConfigurationDigest, StringComparison.Ordinal) &&
               SubscriptionId == other.SubscriptionId;
    }

    public override string ToString() => nameof(ProductionReleaseBinding);
}

/// <summary>One named role's sign-off of one exact release binding.</summary>
public sealed class ProductionApproval
{
    public ProductionApproval(
        ProductionApprovalRole role,
        DateTimeOffset approvedAtUtc,
        ProductionReleaseBinding release)
    {
        ArgumentNullException.ThrowIfNull(release);

        Role = role;
        ApprovedAtUtc = approvedAtUtc;
        Release = release;
    }

    public ProductionApprovalRole Role { get; }

    public DateTimeOffset ApprovedAtUtc { get; }

    public ProductionReleaseBinding Release { get; }

    public override string ToString() => nameof(ProductionApproval);
}

/// <summary>
/// The deployment-evidence manifest for one exact release. It contains
/// infrastructure evidence states and approvals only — never a secret,
/// credential, connection string or taxpayer fact.
/// </summary>
public sealed class ProductionEvidenceManifest
{
    private readonly ProductionEvidenceItem[] _evidence;
    private readonly ProviderAssurance[] _assurances;
    private readonly ProductionApproval[] _approvals;

    public ProductionEvidenceManifest(
        ProductionReleaseBinding release,
        IEnumerable<ProductionEvidenceItem> evidence,
        IEnumerable<ProviderAssurance> assurances,
        IEnumerable<ProductionApproval> approvals)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(assurances);
        ArgumentNullException.ThrowIfNull(approvals);

        Release = release;
        _evidence = evidence.ToArray();
        _assurances = assurances.ToArray();
        _approvals = approvals.ToArray();

        if (_evidence.Any(item => item is null) ||
            _assurances.Any(item => item is null) ||
            _approvals.Any(item => item is null))
        {
            throw new ArgumentException("A manifest entry cannot be null.");
        }
    }

    public ProductionReleaseBinding Release { get; }

    public IReadOnlyList<ProductionEvidenceItem> Evidence => _evidence.ToArray();

    public IReadOnlyList<ProviderAssurance> Assurances => _assurances.ToArray();

    public IReadOnlyList<ProductionApproval> Approvals => _approvals.ToArray();

    public override string ToString() => nameof(ProductionEvidenceManifest);
}

/// <summary>
/// The managed identity a production host must run as: the Entra tenant, the
/// user-assigned identity's client and principal identifiers, and its Azure
/// resource identifier. All four are opaque infrastructure identifiers and are
/// omitted from serialization.
/// </summary>
public sealed class ManagedIdentityBinding
{
    public ManagedIdentityBinding(
        Guid tenantId,
        Guid clientId,
        Guid principalId,
        string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        if (tenantId == Guid.Empty || clientId == Guid.Empty || principalId == Guid.Empty)
        {
            throw new ArgumentException("A managed identity binding requires non-empty identifiers.");
        }

        TenantId = tenantId;
        ClientId = clientId;
        PrincipalId = principalId;
        ResourceId = resourceId;
    }

    [JsonIgnore]
    public Guid TenantId { get; }

    [JsonIgnore]
    public Guid ClientId { get; }

    [JsonIgnore]
    public Guid PrincipalId { get; }

    [JsonIgnore]
    public string ResourceId { get; }

    public override string ToString() => nameof(ManagedIdentityBinding);
}

/// <summary>
/// The outcome of verifying that a running host actually holds its configured
/// managed identity. <see cref="Unverified"/> is the zero value: an outcome
/// nobody produced never reads as verified.
/// </summary>
public enum ManagedIdentityBindingOutcome
{
    Unverified = 0,
    Verified = 1,
    Mismatched = 2,
    Unavailable = 3,
}

/// <summary>
/// Verifies a configured managed-identity binding against the identity the
/// platform actually issues to this process.
/// <para>
/// An implementation must use only a data-plane token request — never a
/// control-plane privilege — must return a closed outcome rather than any token,
/// claim or exception text, and must re-verify on every call: a previously
/// verified binding is not cached as a standing permission.
/// </para>
/// </summary>
public interface IManagedIdentityBindingProbe
{
    Task<ManagedIdentityBindingOutcome> VerifyAsync(
        ManagedIdentityBinding binding,
        CancellationToken cancellationToken);
}
