using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace TaxAI.AzureInfrastructure.Keys;

/// <summary>
/// Loads the purpose-separated intake credential material from Key Vault by
/// managed identity (spec 016 FR-04: production credential material is not a
/// plaintext application setting).
/// <para>
/// Exactly two secrets are read, each by its fixed name: the intake token
/// digest key and the upload fingerprint key. The existing credential algorithms
/// consume them unchanged; this type only decides where the material comes from.
/// A secret that is missing, disabled, not yet valid, expired, not a canonical
/// 32-byte base64url value, or equal to the other secret is refused, and a
/// refusal is a closed outcome with no material. The secret values, their
/// identifiers and the service's error text never appear in a result's
/// <see cref="object.ToString"/>, an exception or a log.
/// </para>
/// <para>
/// [ASSUMED] The secret names are fixed rather than configurable, so the closed
/// production profile gains no new setting. The current version of each secret
/// is read; replacing a version invalidates outstanding one-time links and
/// client sessions at the next restart and is a planned, announced procedure
/// (docs/operations/key-rotation.md), never a silent rotation.
/// </para>
/// </summary>
public sealed class KeyVaultRuntimeMaterialProvider
{
    /// <summary>The Key Vault secret holding the intake token digest key.</summary>
    public const string DigestKeySecretName = "taxai-intake-digest-key";

    /// <summary>The Key Vault secret holding the upload fingerprint key.</summary>
    public const string FingerprintKeySecretName = "taxai-intake-fingerprint-key";

    private const int MaterialBytes = 32;
    private const int EncodedLength = 43;

    private readonly SecretClient _client;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the production provider for a vault endpoint.</summary>
    public KeyVaultRuntimeMaterialProvider(
        Uri vaultUri,
        TokenCredential credential,
        TimeProvider timeProvider)
        : this(vaultUri, credential, timeProvider, new SecretClientOptions())
    {
    }

    /// <summary>
    /// Creates a provider over explicit client options. Tests use this seam to
    /// supply a controlled transport; the production composition does not.
    /// </summary>
    public KeyVaultRuntimeMaterialProvider(
        Uri vaultUri,
        TokenCredential credential,
        TimeProvider timeProvider,
        SecretClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(vaultUri);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _client = new SecretClient(vaultUri, credential, options);
        _timeProvider = timeProvider;
    }

    /// <summary>Loads both secrets, or returns a closed refusal.</summary>
    public async Task<RuntimeMaterial> LoadAsync(CancellationToken cancellationToken)
    {
        string? digest;
        string? fingerprint;
        try
        {
            digest = await ReadAsync(DigestKeySecretName, cancellationToken).ConfigureAwait(false);
            fingerprint = await ReadAsync(FingerprintKeySecretName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (
            failure is RequestFailedException or
                AuthenticationFailedException or
                CredentialUnavailableException or
                HttpRequestException or
                InvalidOperationException or
                OperationCanceledException)
        {
            return RuntimeMaterial.Refused(RuntimeMaterialOutcome.Unavailable);
        }

        if (digest is null ||
            fingerprint is null ||
            string.Equals(digest, fingerprint, StringComparison.Ordinal))
        {
            return RuntimeMaterial.Refused(RuntimeMaterialOutcome.Invalid);
        }

        return new RuntimeMaterial(RuntimeMaterialOutcome.Loaded, digest, fingerprint);
    }

    public override string ToString() => nameof(KeyVaultRuntimeMaterialProvider);

    /// <summary>
    /// Returns the canonical value, or <see langword="null"/> when the secret
    /// is present but not usable.
    /// </summary>
    private async Task<string?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        var response = await _client
            .GetSecretAsync(name, version: null, cancellationToken)
            .ConfigureAwait(false);
        var secret = response.Value;
        var now = _timeProvider.GetUtcNow();

        if (secret?.Properties.Enabled != true ||
            (secret.Properties.NotBefore is { } notBefore && notBefore > now) ||
            (secret.Properties.ExpiresOn is { } expiresOn && expiresOn <= now) ||
            !IsCanonicalMaterial(secret.Value))
        {
            return null;
        }

        return secret.Value;
    }

    private static bool IsCanonicalMaterial(string? candidate)
    {
        if (candidate is not { Length: EncodedLength } ||
            !candidate.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return false;
        }

        var bytes = new byte[MaterialBytes + 2];
        try
        {
            var normalized = candidate.Replace('-', '+').Replace('_', '/') + "=";
            if (!Convert.TryFromBase64String(normalized, bytes, out var written) ||
                written != MaterialBytes)
            {
                return false;
            }

            var canonical = Convert.ToBase64String(bytes, 0, MaterialBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return string.Equals(canonical, candidate, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

/// <summary>The closed outcome of loading runtime credential material.</summary>
public enum RuntimeMaterialOutcome
{
    Loaded = 1,
    Unavailable = 2,
    Invalid = 3,
}

/// <summary>
/// Intake credential material as loaded from Key Vault. Only the provider can
/// create a loaded instance; a refusal carries no material. The values are the
/// canonical encodings the existing intake configuration validates, and the
/// <see cref="ToString"/> is structural.
/// </summary>
public sealed class RuntimeMaterial
{
    internal RuntimeMaterial(RuntimeMaterialOutcome outcome, string? digestKey, string? fingerprintKey)
    {
        Outcome = outcome;
        DigestKey = digestKey;
        FingerprintKey = fingerprintKey;
    }

    public RuntimeMaterialOutcome Outcome { get; }

    public bool IsLoaded => Outcome == RuntimeMaterialOutcome.Loaded;

    public string? DigestKey { get; }

    public string? FingerprintKey { get; }

    /// <summary>A refusal with no material.</summary>
    public static RuntimeMaterial Refused(RuntimeMaterialOutcome outcome)
    {
        if (outcome == RuntimeMaterialOutcome.Loaded || !Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        return new RuntimeMaterial(outcome, null, null);
    }

    public override string ToString() => nameof(RuntimeMaterial);
}
