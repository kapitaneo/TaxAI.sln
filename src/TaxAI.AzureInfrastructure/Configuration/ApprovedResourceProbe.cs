using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.DataProtection;
using TaxAI.AzureInfrastructure.Content;
using TaxAI.AzureInfrastructure.Control;
using TaxAI.AzureInfrastructure.Keys;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Configuration;

/// <summary>
/// Verifies the actual approved key protection a production host depends on
/// (spec 016 FR-04, FR-14; AC-042, AC-043, AC-047).
/// </summary>
public interface IApprovedResourceProbe
{
    Task<ApprovedResourceProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Verifies the recovery inventory a production host depends on (spec 016 T06,
/// AC-103, AC-115): the immutable control-ledger, attestation and evidence
/// containers in both control accounts. Kept separate from
/// <see cref="IApprovedResourceProbe"/> so the key and source checks keep their
/// exact contract; the admission gate requires both.
/// </summary>
public interface IRecoveryInventoryProbe
{
    Task<ApprovedResourceProbeResult> ProbeRecoveryInventoryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The data-plane probe of the approved Key Vault keys and the protected key
/// ring, run by the production admission gate after the managed identity is
/// verified.
/// <para>
/// For the source-envelope key and the separate Data Protection key it reads
/// the key as the application identity sees it and requires an enabled,
/// non-exportable RSA key of at least 3072 bits that permits wrap and unwrap
/// and whose recovery level shows soft delete <b>with purge protection</b>.
/// It then proves each key actually wraps and unwraps a fresh random value
/// (<see cref="KeyVaultContentKeyWrapper.VerifyCapabilityAsync"/>), and proves
/// the registered key ring round-trips a random value — which loads the shared
/// Blob key ring and unwraps its keys through Key Vault.
/// </para>
/// <para>
/// It uses only data-plane operations the application identity is granted:
/// no Azure Resource Manager call, no rotation-policy or vault-property read and
/// no key export. Vault RBAC mode, the 90-day rotation policy and the SQL TDE
/// protector are verified by the reviewed templates and the operator-run
/// rotation drill, which hold those privileges; the application does not.
/// Nothing probed is taxpayer data, and a result carries closed findings only.
/// </para>
/// <para>
/// Spec 016 T04 adds the source store (AC-025, AC-028, AC-029): through the
/// Blob data plane it requires the source account to be general-purpose v2
/// with geo-zone-redundant storage, and the private source container to allow
/// no public access and carry no immutability policy, legal hold or
/// version-level immutability. It writes, lists and reads no object. Soft
/// delete, versioning, snapshots, the source CMK and the private endpoint are
/// account-management facts the application identity cannot read; the
/// reviewed template and <c>scripts/azure/Test-SourceProtection.ps1</c> verify them.
/// </para>
/// <para>
/// Spec 016 T06 adds the recovery inventory (AC-103, AC-115): in each control
/// account the ledger, attestation and evidence containers must allow no
/// public access, carry a time-based immutability policy and no legal hold.
/// Both copies are required in the primary profile. In the Canada East
/// recovery profile only the authoritative Canada East copy is required; an
/// unreachable Canada Central copy is tolerated because that region is what
/// is being recovered from, but a reachable one must still be approved. The
/// recovery profile also accepts the failed-over source account's
/// single-region redundancy. The retention length is an account-management
/// fact the application cannot read; the template and the recovery drill
/// verify it.
/// </para>
/// </summary>
public sealed class ApprovedResourceProbe : IApprovedResourceProbe, IRecoveryInventoryProbe
{
    /// <summary>[ASSUMED] The minimum approved RSA modulus for a wrapping key.</summary>
    public const int MinimumKeySizeBits = 3072;

    /// <summary>The fixed, non-sensitive purpose of the key-ring round trip.</summary>
    public const string KeyRingProbePurpose = "TaxAI.Production.KeyRingProbe.v1";

    /// <summary>The approved source account SKU (spec 016 FR-03, AC-025).</summary>
    public const string ApprovedSourceSku = "Standard_GZRS";

    /// <summary>The approved source account kind: general-purpose v2.</summary>
    public const string ApprovedSourceAccountKind = "StorageV2";

    /// <summary>Spec 016 T06: the source redundancy after a Canada East storage failover.</summary>
    public static IReadOnlyList<string> FailedOverSourceSkus { get; } = ["Standard_LRS", "Standard_ZRS"];

    /// <summary>Spec 016 T06: the immutable containers every control account must hold.</summary>
    public static IReadOnlyList<string> ControlContainerNames { get; } =
    [
        AzureRecoveryControlStore.LedgerContainerName,
        AzureRecoveryControlStore.AttestationContainerName,
        AzureProductionEvidenceReader.EvidenceContainerName,
    ];

    private static readonly string[] PurgeProtectedRecoveryLevels =
    {
        "Recoverable",
        "CustomizedRecoverable",
    };

    private readonly AzureInfrastructureOptions _options;
    private readonly TokenCredential _credential;
    private readonly IDataProtectionProvider _keyRing;
    private readonly KeyClientOptions _keyOptions;
    private readonly CryptographyClientOptions _cryptographyOptions;
    private readonly BlobClientOptions _blobOptions;

    /// <summary>Creates the production probe.</summary>
    public ApprovedResourceProbe(
        AzureInfrastructureOptions options,
        TokenCredential credential,
        IDataProtectionProvider keyRing)
        : this(
            options,
            credential,
            keyRing,
            new KeyClientOptions(),
            new CryptographyClientOptions(),
            new BlobClientOptions())
    {
    }

    /// <summary>
    /// Creates a probe over explicit client options. Tests use this seam to
    /// supply controlled transports; the production registration does not.
    /// </summary>
    public ApprovedResourceProbe(
        AzureInfrastructureOptions options,
        TokenCredential credential,
        IDataProtectionProvider keyRing,
        KeyClientOptions keyOptions,
        CryptographyClientOptions cryptographyOptions,
        BlobClientOptions blobOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(keyRing);
        ArgumentNullException.ThrowIfNull(keyOptions);
        ArgumentNullException.ThrowIfNull(cryptographyOptions);
        ArgumentNullException.ThrowIfNull(blobOptions);

        _options = options;
        _credential = credential;
        _keyRing = keyRing;
        _keyOptions = keyOptions;
        _cryptographyOptions = cryptographyOptions;
        _blobOptions = blobOptions;
    }

    public async Task<ApprovedResourceProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var findings = new List<ApprovedResourceFinding>();

        if (!TryReadVault(_options.KeyVault.Endpoint, out var vault) ||
            !TryReadKey(_options.SourceEnvelope.KeyUri, vault!, out var sourceKey) ||
            !TryReadKey(_options.DataProtection.KeyUri, vault!, out var protectionKey))
        {
            findings.Add(ApprovedResourceFinding.ConfigurationUnusable);
            return new ApprovedResourceProbeResult(findings);
        }

        var client = new KeyClient(vault, _credential, _keyOptions);

        await ProbeKeyAsync(
            client,
            sourceKey!,
            ApprovedResourceFinding.SourceEnvelopeKeyNotApproved,
            ApprovedResourceFinding.SourceEnvelopeWrapFailed,
            findings,
            cancellationToken).ConfigureAwait(false);

        await ProbeKeyAsync(
            client,
            protectionKey!,
            ApprovedResourceFinding.DataProtectionKeyNotApproved,
            ApprovedResourceFinding.DataProtectionWrapFailed,
            findings,
            cancellationToken).ConfigureAwait(false);

        if (!await RoundTripsKeyRingAsync(cancellationToken).ConfigureAwait(false))
        {
            findings.Add(ApprovedResourceFinding.KeyRingRoundTripFailed);
        }

        var source = await ProbeSourceStorageAsync(cancellationToken).ConfigureAwait(false);
        if (source is not null)
        {
            findings.Add(source.Value);
        }

        return new ApprovedResourceProbeResult(findings);
    }

    public async Task<ApprovedResourceProbeResult> ProbeRecoveryInventoryAsync(CancellationToken cancellationToken)
    {
        var findings = new List<ApprovedResourceFinding>();
        var recovering = ProductionConfigurationValidator.IsRegionalRecovery(_options);
        await ProbeControlAccountAsync(
            _options.Recovery.ControlLedger.Endpoint,
            required: !recovering,
            findings,
            cancellationToken).ConfigureAwait(false);
        await ProbeControlAccountAsync(
            _options.Recovery.ControlLedgerReplica.Endpoint,
            required: true,
            findings,
            cancellationToken).ConfigureAwait(false);

        return new ApprovedResourceProbeResult(findings);
    }

    /// <summary>
    /// The source account and container posture rules (T04). Public so they are
    /// tested directly and applied identically everywhere.
    /// </summary>
    /// <remarks>
    /// The account facts are the raw service header values, because the Blob
    /// SDK's closed SKU enumeration has no geo-zone-redundant member.
    /// </remarks>
    public static bool IsApprovedSourceStorage(
        string? skuName,
        string? accountKind,
        string? hierarchicalNamespace,
        BlobContainerProperties container) =>
        IsApprovedSourceStorage(skuName, accountKind, hierarchicalNamespace, container, recovering: false);

    /// <summary>
    /// The source posture for a profile. [ASSUMED] A customer-managed storage
    /// failover leaves the account single-region in Canada East until geo
    /// redundancy is re-enabled, so the Canada East recovery profile also
    /// accepts <see cref="FailedOverSourceSkus"/>; every other rule is unchanged.
    /// </summary>
    public static bool IsApprovedSourceStorage(
        string? skuName,
        string? accountKind,
        string? hierarchicalNamespace,
        BlobContainerProperties container,
        bool recovering)
    {
        ArgumentNullException.ThrowIfNull(container);

        return string.Equals(accountKind, ApprovedSourceAccountKind, StringComparison.Ordinal) &&
               (string.Equals(skuName, ApprovedSourceSku, StringComparison.Ordinal) ||
                (recovering && FailedOverSourceSkus.Contains(skuName, StringComparer.Ordinal))) &&
               !string.Equals(hierarchicalNamespace, "true", StringComparison.OrdinalIgnoreCase) &&
               container.PublicAccess is null or PublicAccessType.None &&
               container.HasImmutabilityPolicy != true &&
               container.HasLegalHold != true &&
               container.HasImmutableStorageWithVersioning != true;
    }

    /// <summary>
    /// The key posture rules, applied to a key as Key Vault reports it. Public
    /// so the rules are tested directly and applied identically everywhere.
    /// </summary>
    public static IReadOnlyList<KeyPostureViolation> AssessKey(KeyVaultKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var violations = new List<KeyPostureViolation>();

        if (key.Properties.Enabled != true)
        {
            violations.Add(KeyPostureViolation.Disabled);
        }

        if (key.KeyType != KeyType.Rsa && key.KeyType != KeyType.RsaHsm)
        {
            violations.Add(KeyPostureViolation.TypeNotApproved);
        }
        else if ((key.Key?.N?.Length ?? 0) * 8 < MinimumKeySizeBits)
        {
            violations.Add(KeyPostureViolation.SizeBelowMinimum);
        }

        if (!key.KeyOperations.Contains(KeyOperation.WrapKey) ||
            !key.KeyOperations.Contains(KeyOperation.UnwrapKey))
        {
            violations.Add(KeyPostureViolation.WrapOperationsMissing);
        }

        if (!PurgeProtectedRecoveryLevels.Contains(key.Properties.RecoveryLevel, StringComparer.Ordinal))
        {
            violations.Add(KeyPostureViolation.PurgeProtectionDisabled);
        }

        if (key.Properties.Exportable != false)
        {
            violations.Add(KeyPostureViolation.Exportable);
        }

        return violations;
    }

    public override string ToString() => nameof(ApprovedResourceProbe);

    private async Task ProbeKeyAsync(
        KeyClient client,
        Uri keyUri,
        ApprovedResourceFinding notApproved,
        ApprovedResourceFinding wrapFailed,
        List<ApprovedResourceFinding> findings,
        CancellationToken cancellationToken)
    {
        KeyVaultKey key;
        try
        {
            var name = keyUri.AbsolutePath.Split('/')[2];
            key = (await client.GetKeyAsync(name, version: null, cancellationToken).ConfigureAwait(false)).Value;
        }
        catch (RequestFailedException failure) when (failure.Status is 403 or 404)
        {
            findings.Add(notApproved);
            return;
        }
        catch (Exception failure) when (IsUnavailable(failure, cancellationToken))
        {
            if (!findings.Contains(ApprovedResourceFinding.KeyVaultUnavailable))
            {
                findings.Add(ApprovedResourceFinding.KeyVaultUnavailable);
            }

            return;
        }

        if (AssessKey(key).Count > 0)
        {
            findings.Add(notApproved);
            return;
        }

        var wrapper = new KeyVaultContentKeyWrapper(keyUri, _credential, _cryptographyOptions);
        if (!await wrapper.VerifyCapabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            findings.Add(wrapFailed);
        }
    }

    private async Task<ApprovedResourceFinding?> ProbeSourceStorageAsync(CancellationToken cancellationToken)
    {
        if (!AzureInfrastructureRegistration.TryReadBlobServiceRoot(_options.SourceStorage.Endpoint, out var root))
        {
            return ApprovedResourceFinding.ConfigurationUnusable;
        }

        var capture = new AccountHeaderCapture();
        var options = new BlobClientOptions { Transport = _blobOptions.Transport };
        options.Retry.MaxRetries = _blobOptions.Retry.MaxRetries;
        options.Retry.NetworkTimeout = _blobOptions.Retry.NetworkTimeout;
        options.AddPolicy(capture, HttpPipelinePosition.PerCall);
        var container = new BlobServiceClient(root, _credential, options)
            .GetBlobContainerClient(AzureIntakeContentStore.SourceContainerName);
        try
        {
            try
            {
                await container.GetAccountInfoAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The SDK cannot name a GZRS SKU; the captured header can.
            }

            var properties = await container.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return capture.Captured &&
                   IsApprovedSourceStorage(
                       capture.SkuName,
                       capture.AccountKind,
                       capture.HierarchicalNamespace,
                       properties.Value,
                       ProductionConfigurationValidator.IsRegionalRecovery(_options))
                ? null
                : ApprovedResourceFinding.SourceStorageNotApproved;
        }
        catch (RequestFailedException failure) when (failure.Status is 403 or 404)
        {
            return ApprovedResourceFinding.SourceStorageNotApproved;
        }
        catch (Exception failure) when (IsUnavailable(failure, cancellationToken))
        {
            return ApprovedResourceFinding.SourceStorageUnavailable;
        }
    }

    /// <summary>
    /// The control container posture (T06): no public access, a time-based
    /// immutability policy and no legal hold. Public so it is tested directly.
    /// </summary>
    public static bool IsApprovedControlContainer(BlobContainerProperties container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return container.PublicAccess is null or PublicAccessType.None &&
               container.HasImmutabilityPolicy == true &&
               container.HasLegalHold != true;
    }

    private async Task ProbeControlAccountAsync(
        string? endpoint,
        bool required,
        List<ApprovedResourceFinding> findings,
        CancellationToken cancellationToken)
    {
        if (!AzureInfrastructureRegistration.TryReadBlobServiceRoot(endpoint, out var root))
        {
            findings.Add(ApprovedResourceFinding.ConfigurationUnusable);
            return;
        }

        var options = new BlobClientOptions { Transport = _blobOptions.Transport };
        options.Retry.MaxRetries = _blobOptions.Retry.MaxRetries;
        options.Retry.NetworkTimeout = _blobOptions.Retry.NetworkTimeout;
        var service = new BlobServiceClient(root, _credential, options);

        foreach (var name in ControlContainerNames)
        {
            try
            {
                var properties = await service
                    .GetBlobContainerClient(name)
                    .GetPropertiesAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!IsApprovedControlContainer(properties.Value))
                {
                    findings.Add(ApprovedResourceFinding.ControlLedgerNotApproved);
                    return;
                }
            }
            catch (RequestFailedException failure) when (failure.Status is 403 or 404)
            {
                findings.Add(ApprovedResourceFinding.ControlLedgerNotApproved);
                return;
            }
            catch (Exception failure) when (IsUnavailable(failure, cancellationToken))
            {
                if (required)
                {
                    findings.Add(ApprovedResourceFinding.ControlLedgerUnavailable);
                }

                return;
            }
        }
    }

    private async Task<bool> RoundTripsKeyRingAsync(CancellationToken cancellationToken)
    {
        var probe = RandomNumberGenerator.GetBytes(32);
        try
        {
            // The key ring API is synchronous; it runs off the caller's thread so
            // the gate's deadline still bounds a stalled repository.
            return await Task.Run(
                    () =>
                    {
                        var protector = _keyRing.CreateProtector(KeyRingProbePurpose);
                        var recovered = protector.Unprotect(protector.Protect(probe));
                        try
                        {
                            return CryptographicOperations.FixedTimeEquals(probe, recovered);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(recovered);
                        }
                    },
                    cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (
            IsUnavailable(failure, cancellationToken) ||
            failure is CryptographicException or OperationCanceledException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(probe);
        }
    }

    private static bool IsUnavailable(Exception failure, CancellationToken cancellationToken) =>
        failure is RequestFailedException or
            AuthenticationFailedException or
            CredentialUnavailableException or
            HttpRequestException or
            InvalidOperationException or
            AggregateException ||
        (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>Records the account headers of a successful account-information response.</summary>
    private sealed class AccountHeaderCapture : HttpPipelineSynchronousPolicy
    {
        internal bool Captured { get; private set; }

        internal string? SkuName { get; private set; }

        internal string? AccountKind { get; private set; }

        internal string? HierarchicalNamespace { get; private set; }

        public override void OnReceivedResponse(HttpMessage message)
        {
            if (message.Response.Status == 200 &&
                message.Response.Headers.TryGetValue("x-ms-sku-name", out var sku) &&
                message.Response.Headers.TryGetValue("x-ms-account-kind", out var kind))
            {
                message.Response.Headers.TryGetValue("x-ms-is-hns-enabled", out var hns);
                SkuName = sku;
                AccountKind = kind;
                HierarchicalNamespace = hns;
                Captured = true;
            }
        }

        public override string ToString() => nameof(AccountHeaderCapture);
    }

    private static bool TryReadVault(string? candidate, out Uri? vault)
    {
        vault = null;
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !uri.IsDefaultPort ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !uri.IdnHost.EndsWith(".vault.azure.net", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        vault = uri;
        return true;
    }

    private static bool TryReadKey(string? candidate, Uri vault, out Uri? key)
    {
        key = null;
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !KeyIdentifier.TryReadVersionless(uri, out var host, out _) ||
            !string.Equals(host, vault.IdnHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        key = uri;
        return true;
    }
}

/// <summary>The closed findings of the approved-resource probe.</summary>
public enum ApprovedResourceFinding
{
    ConfigurationUnusable = 1,
    KeyVaultUnavailable = 2,
    SourceEnvelopeKeyNotApproved = 3,
    SourceEnvelopeWrapFailed = 4,
    DataProtectionKeyNotApproved = 5,
    DataProtectionWrapFailed = 6,
    KeyRingRoundTripFailed = 7,
    SourceStorageNotApproved = 8,
    SourceStorageUnavailable = 9,

    /// <summary>Spec 016 T06: a control container is public, mutable, held or missing.</summary>
    ControlLedgerNotApproved = 10,

    /// <summary>Spec 016 T06: a required control account could not be reached.</summary>
    ControlLedgerUnavailable = 11,
}

/// <summary>The closed ways a Key Vault key misses the approved posture.</summary>
public enum KeyPostureViolation
{
    Disabled = 1,
    TypeNotApproved = 2,
    SizeBelowMinimum = 3,
    WrapOperationsMissing = 4,
    PurgeProtectionDisabled = 5,
    Exportable = 6,
}

/// <summary>
/// A probe result: approved only when no finding applies. Its findings map onto
/// the closed admission-refusal vocabulary here, once, so every consumer refuses
/// the same way.
/// </summary>
public sealed class ApprovedResourceProbeResult
{
    public ApprovedResourceProbeResult(IEnumerable<ApprovedResourceFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        Findings = findings.Distinct().OrderBy(finding => finding).ToArray();
        if (Findings.Any(finding => !Enum.IsDefined(finding)))
        {
            throw new ArgumentOutOfRangeException(nameof(findings));
        }
    }

    /// <summary>
    /// The result of a probe that could not complete — it stalled past its
    /// deadline or could not be constructed. It is never a pass.
    /// </summary>
    public static ApprovedResourceProbeResult Unverifiable { get; } =
        new(new[] { ApprovedResourceFinding.KeyVaultUnavailable });

    public bool IsApproved => Findings.Count == 0;

    public IReadOnlyList<ApprovedResourceFinding> Findings { get; }

    /// <summary>
    /// The findings as admission refusals: a source-envelope key problem refuses
    /// source envelope encryption, a Data Protection key problem refuses the
    /// key-ring configuration, a failed key-ring round trip refuses durable Data
    /// Protection, an unapproved or unreachable source account refuses the storage
    /// binding (T04), and an unusable configuration or unavailable vault refuses the
    /// Key Vault binding.
    /// </summary>
    public IReadOnlyList<ProductionAdmissionRefusal> Refusals =>
        Findings.Select(finding => finding switch
            {
                ApprovedResourceFinding.SourceEnvelopeKeyNotApproved or
                ApprovedResourceFinding.SourceEnvelopeWrapFailed =>
                    ProductionAdmissionRefusal.SourceEnvelopeEncryptionMissing,
                ApprovedResourceFinding.DataProtectionKeyNotApproved or
                ApprovedResourceFinding.DataProtectionWrapFailed =>
                    ProductionAdmissionRefusal.DataProtectionConfigurationMissing,
                ApprovedResourceFinding.KeyRingRoundTripFailed =>
                    ProductionAdmissionRefusal.DurableDataProtectionMissing,
                ApprovedResourceFinding.SourceStorageNotApproved or
                ApprovedResourceFinding.SourceStorageUnavailable or
                ApprovedResourceFinding.ControlLedgerNotApproved or
                ApprovedResourceFinding.ControlLedgerUnavailable =>
                    ProductionAdmissionRefusal.StorageBindingInvalid,
                _ => ProductionAdmissionRefusal.KeyVaultBindingInvalid,
            })
            .Distinct()
            .OrderBy(refusal => refusal)
            .ToArray();

    public override string ToString() => nameof(ApprovedResourceProbeResult);
}
