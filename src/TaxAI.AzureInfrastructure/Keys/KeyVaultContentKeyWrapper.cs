using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace TaxAI.AzureInfrastructure.Keys;

/// <summary>
/// Wraps and unwraps one 32-byte content data key with a customer-managed Key
/// Vault key (spec 016 FR-04, AC-038/AC-039 support, AC-045).
/// <para>
/// The configured key is <b>versionless</b>: a wrap always uses the vault's
/// current version, and the returned <see cref="WrappedContentKey"/> records the
/// exact versioned key identifier Key Vault reports. An unwrap is sent only to
/// that recorded version, so ciphertext wrapped before a rotation stays
/// decryptable for as long as its version is retained, and no arbitrary version
/// is ever tried. A wrapped key that names another vault, another key or no
/// version at all is refused before any call is made.
/// </para>
/// <para>
/// Every failure — a wrong identity, a missing or disabled key, a refused
/// operation, an untrusted TLS certificate, a mismatched response — becomes
/// one fixed <see cref="CryptographicException"/> that names nothing. The data
/// key, the wrapped bytes and the service's error text never leave this type.
/// No key is ever exported: Key Vault performs the private-key operation.
/// </para>
/// <para>
/// In T03 the wrapper is consumed by the non-sensitive wrap/unwrap capability
/// probe; the T04 source envelope consumes the same type for per-content keys.
/// </para>
/// </summary>
public sealed class KeyVaultContentKeyWrapper
{
    /// <summary>The only data-key length this wrapper accepts or returns.</summary>
    public const int ContentKeyBytes = 32;

    /// <summary>The one approved wrapping algorithm.</summary>
    public static readonly KeyWrapAlgorithm Algorithm = KeyWrapAlgorithm.RsaOaep256;

    internal const string RefusalMessage = "The content key operation was refused.";

    private readonly Uri _keyUri;
    private readonly string _vaultHost;
    private readonly string _keyName;
    private readonly TokenCredential _credential;
    private readonly CryptographyClientOptions _options;

    /// <summary>Creates the production wrapper for a versionless key identifier.</summary>
    public KeyVaultContentKeyWrapper(Uri versionlessKeyUri, TokenCredential credential)
        : this(versionlessKeyUri, credential, new CryptographyClientOptions())
    {
    }

    /// <summary>
    /// Creates a wrapper over explicit client options. Tests use this seam to
    /// supply a controlled transport; the production registration does not.
    /// </summary>
    public KeyVaultContentKeyWrapper(
        Uri versionlessKeyUri,
        TokenCredential credential,
        CryptographyClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(versionlessKeyUri);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);

        if (!KeyIdentifier.TryReadVersionless(versionlessKeyUri, out var vaultHost, out var keyName))
        {
            throw new ArgumentException(
                "The wrapping key must be a versionless Key Vault key identifier.",
                nameof(versionlessKeyUri));
        }

        _keyUri = versionlessKeyUri;
        _vaultHost = vaultHost!;
        _keyName = keyName!;
        _credential = credential;
        _options = options;
    }

    /// <summary>Wraps a data key under the key's current version.</summary>
    public async Task<WrappedContentKey> WrapAsync(
        ReadOnlyMemory<byte> contentKey,
        CancellationToken cancellationToken)
    {
        if (contentKey.Length != ContentKeyBytes)
        {
            throw new ArgumentException(
                "The content key must be exactly 32 bytes.",
                nameof(contentKey));
        }

        WrapResult result;
        try
        {
            var client = new CryptographyClient(_keyUri, _credential, _options);
            result = await client
                .WrapKeyAsync(Algorithm, contentKey.ToArray(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (IsServiceFailure(failure, cancellationToken))
        {
            throw new CryptographicException(RefusalMessage);
        }

        if (result.Algorithm != Algorithm ||
            result.EncryptedKey is not { Length: > 0 } ||
            !TryReadOwnVersion(result.KeyId, out var version))
        {
            throw new CryptographicException(RefusalMessage);
        }

        return new WrappedContentKey(version!, result.Algorithm.ToString(), result.EncryptedKey);
    }

    /// <summary>
    /// Unwraps a data key with exactly the version that wrapped it. The caller
    /// owns the returned buffer and should clear it when finished.
    /// </summary>
    public async Task<byte[]> UnwrapAsync(
        WrappedContentKey wrapped,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrapped);

        if (!string.Equals(wrapped.Algorithm, Algorithm.ToString(), StringComparison.Ordinal) ||
            !TryReadOwnVersion(wrapped.KeyVersionId.AbsoluteUri, out var version) ||
            version! != wrapped.KeyVersionId)
        {
            throw new CryptographicException(RefusalMessage);
        }

        UnwrapResult result;
        try
        {
            var client = new CryptographyClient(version, _credential, _options);
            result = await client
                .UnwrapKeyAsync(Algorithm, wrapped.Ciphertext.ToArray(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (IsServiceFailure(failure, cancellationToken))
        {
            throw new CryptographicException(RefusalMessage);
        }

        if (result.Key is not { Length: ContentKeyBytes } ||
            !TryReadOwnVersion(result.KeyId, out var reported) ||
            reported! != version)
        {
            if (result.Key is not null)
            {
                CryptographicOperations.ZeroMemory(result.Key);
            }

            throw new CryptographicException(RefusalMessage);
        }

        return result.Key;
    }

    /// <summary>
    /// The non-sensitive capability check: a fresh random value is wrapped and
    /// unwrapped, compared in fixed time and cleared. It carries no taxpayer or
    /// production key material and returns only whether the round trip held.
    /// </summary>
    public async Task<bool> VerifyCapabilityAsync(CancellationToken cancellationToken)
    {
        var probe = RandomNumberGenerator.GetBytes(ContentKeyBytes);
        byte[]? recovered = null;
        try
        {
            var wrapped = await WrapAsync(probe, cancellationToken).ConfigureAwait(false);
            recovered = await UnwrapAsync(wrapped, cancellationToken).ConfigureAwait(false);
            return CryptographicOperations.FixedTimeEquals(probe, recovered);
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(probe);
            if (recovered is not null)
            {
                CryptographicOperations.ZeroMemory(recovered);
            }
        }
    }

    public override string ToString() => nameof(KeyVaultContentKeyWrapper);

    private bool TryReadOwnVersion(string? candidate, out Uri? version)
    {
        version = null;
        return candidate is not null &&
               Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
               KeyIdentifier.TryReadVersioned(parsed, out var host, out var name, out _) &&
               string.Equals(host, _vaultHost, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(name, _keyName, StringComparison.Ordinal) &&
               (version = parsed) is not null;
    }

    private static bool IsServiceFailure(Exception failure, CancellationToken cancellationToken) =>
        failure is RequestFailedException or
            AuthenticationFailedException or
            CredentialUnavailableException or
            HttpRequestException or
            InvalidOperationException or
            NotSupportedException or
            FormatException or
            CryptographicException ||
        (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}

/// <summary>
/// One wrapped content data key: the exact Key Vault key version that wrapped
/// it, the algorithm and the wrapped bytes. It never holds the plaintext key,
/// and its <see cref="ToString"/> is structural.
/// </summary>
public sealed class WrappedContentKey
{
    private readonly byte[] _ciphertext;

    public WrappedContentKey(Uri keyVersionId, string algorithm, byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(keyVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentNullException.ThrowIfNull(ciphertext);

        KeyVersionId = keyVersionId;
        Algorithm = algorithm;
        _ciphertext = ciphertext.ToArray();
    }

    /// <summary>The versioned key identifier Key Vault reported for the wrap.</summary>
    public Uri KeyVersionId { get; }

    public string Algorithm { get; }

    public ReadOnlyMemory<byte> Ciphertext => _ciphertext;

    public override string ToString() => nameof(WrappedContentKey);
}

/// <summary>Strict parsing of Key Vault key identifiers.</summary>
internal static class KeyIdentifier
{
    private const string VaultSuffix = ".vault.azure.net";

    internal static bool TryReadVersionless(Uri candidate, out string? vaultHost, out string? keyName)
    {
        vaultHost = null;
        keyName = null;
        var segments = ReadSegments(candidate, out var host);
        if (segments is not { Length: 3 } || !IsKeyName(segments[2]))
        {
            return false;
        }

        vaultHost = host;
        keyName = segments[2];
        return true;
    }

    internal static bool TryReadVersioned(
        Uri candidate,
        out string? vaultHost,
        out string? keyName,
        out string? keyVersion)
    {
        vaultHost = null;
        keyName = null;
        keyVersion = null;
        var segments = ReadSegments(candidate, out var host);
        if (segments is not { Length: 4 } ||
            !IsKeyName(segments[2]) ||
            segments[3].Length != 32 ||
            !segments[3].All(char.IsAsciiHexDigit))
        {
            return false;
        }

        vaultHost = host;
        keyName = segments[2];
        keyVersion = segments[3];
        return true;
    }

    private static string[]? ReadSegments(Uri candidate, out string? host)
    {
        host = null;
        if (!candidate.IsAbsoluteUri ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !candidate.IsDefaultPort ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            candidate.HostNameType != UriHostNameType.Dns ||
            !candidate.IdnHost.EndsWith(VaultSuffix, StringComparison.OrdinalIgnoreCase) ||
            candidate.IdnHost.Length <= VaultSuffix.Length)
        {
            return null;
        }

        var segments = candidate.AbsolutePath.Split('/');
        if (segments.Length < 3 ||
            segments[0].Length != 0 ||
            !string.Equals(segments[1], "keys", StringComparison.Ordinal))
        {
            return null;
        }

        host = candidate.IdnHost;
        return segments;
    }

    private static bool IsKeyName(string candidate) =>
        candidate.Length is > 0 and <= 127 &&
        candidate.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
