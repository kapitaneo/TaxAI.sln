using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Keys;

namespace TaxAI.AzureInfrastructure.Protection;

/// <summary>
/// The shared, protected production Data Protection key ring (spec 016 FR-04,
/// AC-047): one Canadian private Blob key-ring repository, protected by a Key
/// Vault key, reached by managed identity, and isolated by the production
/// application discriminator.
/// <para>
/// Every production worker configured from the same profile reads and writes
/// the same key ring, so a cookie or antiforgery token protected on one worker
/// is readable on another. Durable server-side sessions, not cookie decryption
/// alone, still decide whether a caller is signed in.
/// </para>
/// <para>
/// Each key-ring key is wrapped by the Key Vault key's current version and
/// records that exact version, so historical key-ring entries stay readable
/// after the Key Vault key rotates. Retiring a Key Vault key version that the
/// key ring still references is refused by <see cref="KeyVersionUsageGuard"/>.
/// No key-ring key is ever written anywhere unencrypted: the Blob repository
/// receives only the Key Vault-wrapped form.
/// </para>
/// <para>
/// This type configures the Data Protection abstractions only; the portal's
/// ASP.NET Core hosting stays in the portal.
/// </para>
/// </summary>
public static class AzureDataProtectionConfiguration
{
    /// <summary>[ASSUMED] The single key-ring blob inside the approved container.</summary>
    public const string KeyRingBlobName = "key-ring.xml";

    /// <summary>
    /// The key-ring key lifetime. It matches the FR-04 90-day active-key
    /// rotation target rather than restating a separate policy.
    /// </summary>
    public static readonly TimeSpan KeyLifetime = KeyVersionUsageGuard.RotationTarget;

    private const string BlobSuffix = ".blob.core.windows.net";

    private const string RepositoryTypeName =
        "Azure.Extensions.AspNetCore.DataProtection.Blobs.AzureBlobXmlRepository";

    private const string EncryptorTypeName =
        "Azure.Extensions.AspNetCore.DataProtection.Keys.AzureKeyVaultXmlEncryptor";

    /// <summary>
    /// Reads the key-ring settings, and the managed identity that reaches them,
    /// from a production profile, or returns <see langword="false"/> when any
    /// part is missing or malformed. The structural validator remains the
    /// authority on the complete profile.
    /// </summary>
    public static bool TryCreateSettings(
        AzureInfrastructureOptions options,
        out ProtectedKeyRingSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(options);

        settings = null;
        var protection = options.DataProtection;
        if (!TryReadBlobOrigin(protection.StorageEndpoint, out var origin) ||
            !IsContainerName(protection.KeyRingContainer) ||
            protection.KeyUri is null ||
            !Uri.TryCreate(protection.KeyUri, UriKind.Absolute, out var keyUri) ||
            !KeyIdentifier.TryReadVersionless(keyUri, out _, out _) ||
            !IsDiscriminator(protection.ApplicationDiscriminator) ||
            !Guid.TryParseExact(options.ManagedIdentity.ClientId, "D", out var clientId) ||
            clientId == Guid.Empty)
        {
            return false;
        }

        settings = new ProtectedKeyRingSettings(
            new Uri(origin!, $"{protection.KeyRingContainer}/{KeyRingBlobName}"),
            keyUri,
            protection.ApplicationDiscriminator!,
            clientId);
        return true;
    }

    /// <summary>
    /// Configures the production key ring over the platform managed identity
    /// the settings name, and no other credential.
    /// </summary>
    public static IDataProtectionBuilder ProtectWithAzureKeyRing(
        this IDataProtectionBuilder builder,
        ProtectedKeyRingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return builder.ProtectWithAzureKeyRing(
            settings,
            new ManagedIdentityCredential(
                new ManagedIdentityCredentialOptions(
                    ManagedIdentityId.FromUserAssignedClientId(settings.ClientId.ToString("D")))),
            new BlobClientOptions(),
            new CryptographyClientOptions());
    }

    /// <summary>
    /// Configures the key ring over explicit client options. Tests use this seam
    /// to supply controlled transports; the production composition does not.
    /// </summary>
    public static IDataProtectionBuilder ProtectWithAzureKeyRing(
        this IDataProtectionBuilder builder,
        ProtectedKeyRingSettings settings,
        TokenCredential credential,
        BlobClientOptions blobOptions,
        CryptographyClientOptions keyOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(blobOptions);
        ArgumentNullException.ThrowIfNull(keyOptions);

        return builder
            .SetApplicationName(settings.ApplicationDiscriminator)
            .SetDefaultKeyLifetime(KeyLifetime)
            .PersistKeysToAzureBlobStorage(
                new BlobClient(settings.KeyRingBlobUri, credential, blobOptions))
            .ProtectKeysWithAzureKeyVault(
                settings.KeyUri,
                new KeyResolver(credential, keyOptions));
    }

    /// <summary>
    /// True only when the built container holds exactly the approved production
    /// key ring: the Azure Blob repository, the Azure Key Vault encryptor, and
    /// the expected application discriminator. Any other repository or
    /// encryptor — including a durable one from elsewhere — is not approved.
    /// </summary>
    public static bool IsApprovedKeyRing(
        IServiceProvider services,
        string? expectedDiscriminator)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!IsDiscriminator(expectedDiscriminator) ||
            services.GetService<IDataProtectionProvider>() is null)
        {
            return false;
        }

        var keyManagement = services.GetService<IOptions<KeyManagementOptions>>()?.Value;
        var protection = services.GetService<IOptions<DataProtectionOptions>>()?.Value;

        return IsApprovedComponent(
                   keyManagement?.XmlRepository,
                   RepositoryTypeName,
                   typeof(AzureStorageBlobDataProtectionBuilderExtensions)) &&
               IsApprovedComponent(
                   keyManagement?.XmlEncryptor,
                   EncryptorTypeName,
                   typeof(AzureDataProtectionKeyVaultKeyBuilderExtensions)) &&
               string.Equals(
                   protection?.ApplicationDiscriminator,
                   expectedDiscriminator,
                   StringComparison.Ordinal);
    }

    private static bool IsApprovedComponent(object? component, string typeName, Type packageAnchor) =>
        component is not null &&
        string.Equals(component.GetType().FullName, typeName, StringComparison.Ordinal) &&
        component.GetType().Assembly == packageAnchor.Assembly;

    private static bool TryReadBlobOrigin(string? candidate, out Uri? origin)
    {
        origin = null;
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !uri.IsDefaultPort ||
            uri.HostNameType != UriHostNameType.Dns ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.IdnHost.EndsWith(BlobSuffix, StringComparison.OrdinalIgnoreCase) ||
            uri.IdnHost.Length <= BlobSuffix.Length)
        {
            return false;
        }

        origin = uri;
        return true;
    }

    private static bool IsContainerName(string? candidate) =>
        candidate is { Length: >= 3 and <= 63 } &&
        (char.IsAsciiLetterLower(candidate[0]) || char.IsAsciiDigit(candidate[0])) &&
        (char.IsAsciiLetterLower(candidate[^1]) || char.IsAsciiDigit(candidate[^1])) &&
        !candidate.Contains("--", StringComparison.Ordinal) &&
        candidate.All(character =>
            char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    private static bool IsDiscriminator(string? candidate) =>
        candidate is { Length: > 0 and <= 64 } &&
        candidate.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}

/// <summary>
/// The validated key-ring location, wrapping key, discriminator and managed
/// identity. It holds infrastructure identifiers only and its
/// <see cref="ToString"/> is structural.
/// </summary>
public sealed class ProtectedKeyRingSettings
{
    internal ProtectedKeyRingSettings(
        Uri keyRingBlobUri,
        Uri keyUri,
        string applicationDiscriminator,
        Guid clientId)
    {
        KeyRingBlobUri = keyRingBlobUri;
        KeyUri = keyUri;
        ApplicationDiscriminator = applicationDiscriminator;
        ClientId = clientId;
    }

    public Uri KeyRingBlobUri { get; }

    public Uri KeyUri { get; }

    public string ApplicationDiscriminator { get; }

    /// <summary>The user-assigned managed identity that reaches the key ring.</summary>
    public Guid ClientId { get; }

    public override string ToString() => nameof(ProtectedKeyRingSettings);
}
