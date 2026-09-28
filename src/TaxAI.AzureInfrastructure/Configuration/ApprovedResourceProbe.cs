using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.AspNetCore.DataProtection;
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
/// </summary>
public sealed class ApprovedResourceProbe : IApprovedResourceProbe
{
    /// <summary>[ASSUMED] The minimum approved RSA modulus for a wrapping key.</summary>
    public const int MinimumKeySizeBits = 3072;

    /// <summary>The fixed, non-sensitive purpose of the key-ring round trip.</summary>
    public const string KeyRingProbePurpose = "TaxAI.Production.KeyRingProbe.v1";

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

    /// <summary>Creates the production probe.</summary>
    public ApprovedResourceProbe(
        AzureInfrastructureOptions options,
        TokenCredential credential,
        IDataProtectionProvider keyRing)
        : this(options, credential, keyRing, new KeyClientOptions(), new CryptographyClientOptions())
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
        CryptographyClientOptions cryptographyOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(keyRing);
        ArgumentNullException.ThrowIfNull(keyOptions);
        ArgumentNullException.ThrowIfNull(cryptographyOptions);

        _options = options;
        _credential = credential;
        _keyRing = keyRing;
        _keyOptions = keyOptions;
        _cryptographyOptions = cryptographyOptions;
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

        return new ApprovedResourceProbeResult(findings);
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
    /// Protection, and an unusable configuration or unavailable vault refuses the
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
                _ => ProductionAdmissionRefusal.KeyVaultBindingInvalid,
            })
            .Distinct()
            .OrderBy(refusal => refusal)
            .ToArray();

    public override string ToString() => nameof(ApprovedResourceProbeResult);
}
