using System.Net;
using System.Security.Cryptography;
using System.Text;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxAI.AzureInfrastructure.Configuration;
using TaxAI.AzureInfrastructure.Keys;
using TaxAI.AzureInfrastructure.Protection;
using TaxAI.AzureInfrastructure.Tests.Keys;

namespace TaxAI.AzureInfrastructure.Tests.Protection;

/// <summary>
/// Spec 016 T03 — the shared, protected production Data Protection key ring
/// (FR-04, AC-047).
/// <para>
/// Each "worker" is an independent service provider configured exactly as the
/// portal configures production: the real Azure Blob key-ring repository and the
/// real Azure Key Vault key encryptor, over a controlled in-memory Blob store and
/// the controlled Key Vault from <see cref="KeyWrappingTests"/>. A payload
/// protected on one worker is read on another; the key ring written to storage
/// holds only Key Vault-wrapped keys; rotation keeps historical key-ring entries
/// readable; and nothing reaches a network.
/// </para>
/// </summary>
public sealed class DataProtectionTests
{
    /// <summary>The cookie authentication handler's protector purposes.</summary>
    private static readonly string[] SessionCookiePurposes =
    {
        "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
        "TaxAiSession",
        "v2",
    };

    private const string Discriminator = "taxai-portal-production";

    // ------------------------------------------- one shared key ring (AC-047)

    [Fact]
    public void ASessionProtectedOnOneWorkerIsReadOnAnother()
    {
        var environment = new SharedEnvironment();
        using var first = environment.Worker();
        using var second = environment.Worker();
        var payload = Encoding.UTF8.GetBytes("synthetic-opaque-session-ticket-key");

        var protectedPayload = Protector(first).Protect(payload);

        Assert.Equal(payload, Protector(second).Unprotect(protectedPayload));
        Assert.Equal(payload, Protector(first).Unprotect(Protector(second).Protect(payload)));
    }

    [Fact]
    public void BothWorkersUseTheOneKeyRingBlobAndItHoldsOnlyWrappedKeys()
    {
        var environment = new SharedEnvironment();
        using var first = environment.Worker();
        using var second = environment.Worker();

        _ = Protector(first).Protect(new byte[] { 1, 2, 3 });
        _ = Protector(second).Unprotect(Protector(first).Protect(new byte[] { 4 }));

        var stored = Assert.Single(environment.Blobs.Blobs);
        Assert.Equal($"/data-protection/{AzureDataProtectionConfiguration.KeyRingBlobName}", stored.Key);

        var xml = Encoding.UTF8.GetString(stored.Value);
        Assert.Contains("<encryptedSecret", xml, StringComparison.Ordinal);
        Assert.Contains("AzureKeyVaultXmlDecryptor", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", xml, StringComparison.Ordinal);
        Assert.Contains($"https://{ControlledKeyVault.Host}/keys/data-protection/", xml, StringComparison.Ordinal);
        Assert.Contains(environment.Vault.Requests, request => request.EndsWith("/unwrapkey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnotherApplicationDiscriminatorCannotReadTheProductionPayload()
    {
        var environment = new SharedEnvironment();
        using var production = environment.Worker();
        using var foreign = environment.Worker(discriminator: "taxai-portal-synthetic");

        var protectedPayload = Protector(production).Protect(new byte[] { 1, 2, 3 });

        Assert.ThrowsAny<CryptographicException>(() => Protector(foreign).Unprotect(protectedPayload));
    }

    [Fact]
    public void AWorkerWithoutTheKeyVaultIdentityCannotReadTheKeyRing()
    {
        var environment = new SharedEnvironment();
        using var production = environment.Worker();
        var protectedPayload = Protector(production).Protect(new byte[] { 1, 2, 3 });
        using var intruder = environment.Worker(keyVaultToken: "synthetic-foreign-identity-token");

        Assert.ThrowsAny<Exception>(() => Protector(intruder).Unprotect(protectedPayload));
    }

    [Fact] // Historical key-ring entries stay readable after the Key Vault key rotates.
    public void KeyRingEntriesWrappedBeforeARotationStayReadableAfterIt()
    {
        var environment = new SharedEnvironment();
        var original = environment.DataProtectionKeyVersion;
        using var before = environment.Worker();
        var protectedPayload = Protector(before).Protect(new byte[] { 7, 7, 7 });

        environment.Vault.Rotate("data-protection");
        using var after = environment.Worker();

        Assert.Equal(new byte[] { 7, 7, 7 }, Protector(after).Unprotect(protectedPayload));
        Assert.Contains(
            environment.Vault.Requests,
            request => request.Contains($"/keys/data-protection/{original}/unwrapkey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // Why a still-needed version is never retired: disabling it breaks the ring.
    public void DisablingAVersionTheKeyRingStillNeedsFailsClosed()
    {
        var environment = new SharedEnvironment();
        using var before = environment.Worker();
        var protectedPayload = Protector(before).Protect(new byte[] { 9 });

        environment.Vault.Rotate("data-protection");
        environment.Vault.Disable("data-protection", environment.DataProtectionKeyVersion);
        using var after = environment.Worker();

        Assert.ThrowsAny<Exception>(() => Protector(after).Unprotect(protectedPayload));
    }

    [Fact]
    public void TheConfiguredKeyLifetimeIsTheNinetyDayRotationTarget()
    {
        var environment = new SharedEnvironment();
        using var worker = environment.Worker();

        var keyManagement = worker.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.Equal(TimeSpan.FromDays(90), keyManagement.NewKeyLifetime);
        Assert.Equal(KeyVersionUsageGuard.RotationTarget, AzureDataProtectionConfiguration.KeyLifetime);
    }

    // ------------------------------------------ the approved key-ring check

    [Fact]
    public void TheProductionConfigurationIsTheApprovedKeyRing()
    {
        var environment = new SharedEnvironment();
        using var worker = environment.Worker();

        Assert.True(AzureDataProtectionConfiguration.IsApprovedKeyRing(worker, Discriminator));
    }

    [Theory]
    [InlineData("taxai-portal-synthetic")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("has spaces")]
    public void AnyOtherDiscriminatorIsNotTheApprovedKeyRing(string? expected)
    {
        var environment = new SharedEnvironment();
        using var worker = environment.Worker();

        Assert.False(AzureDataProtectionConfiguration.IsApprovedKeyRing(worker, expected));
    }

    [Fact]
    public void ADurableRingFromAnyOtherComponentIsNotTheApprovedKeyRing()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(Discriminator);
        services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = new SubstituteRepository();
            options.XmlEncryptor = new SubstituteEncryptor();
        });
        using var provider = new Worker(services);

        Assert.False(AzureDataProtectionConfiguration.IsApprovedKeyRing(provider, Discriminator));
    }

    [Fact]
    public void ReplacingOnlyTheEncryptorOfTheAzureRingIsNotApproved()
    {
        var environment = new SharedEnvironment();
        var services = environment.Services();
        services.Configure<KeyManagementOptions>(options => options.XmlEncryptor = new SubstituteEncryptor());
        using var provider = new Worker(services);

        Assert.False(AzureDataProtectionConfiguration.IsApprovedKeyRing(provider, Discriminator));
    }

    [Fact]
    public void AContainerWithoutDataProtectionIsNotApproved()
    {
        using var provider = new Worker(new ServiceCollection());

        Assert.False(AzureDataProtectionConfiguration.IsApprovedKeyRing(provider, Discriminator));
    }

    // ----------------------------------------------------- settings parsing

    [Fact]
    public void TheApprovedProfileProducesTheKeyRingSettings()
    {
        Assert.True(AzureDataProtectionConfiguration.TryCreateSettings(Profile(), out var settings));

        Assert.Equal(
            $"https://sttaxaictlcc.blob.core.windows.net/data-protection/{AzureDataProtectionConfiguration.KeyRingBlobName}",
            settings!.KeyRingBlobUri.AbsoluteUri);
        Assert.Equal($"https://{ControlledKeyVault.Host}/keys/data-protection", settings.KeyUri.AbsoluteUri);
        Assert.Equal(Discriminator, settings.ApplicationDiscriminator);
        Assert.Equal(Guid.Parse("33333333-3333-4333-8333-333333333333"), settings.ClientId);
        Assert.Equal(nameof(ProtectedKeyRingSettings), settings.ToString());
    }

    public static TheoryData<string, string?> UnusableSettings() => new()
    {
        { "StorageEndpoint", null },
        { "StorageEndpoint", "http://sttaxaictlcc.blob.core.windows.net/" },
        { "StorageEndpoint", "https://sttaxaictlcc.blob.core.windows.net:444/" },
        { "StorageEndpoint", "https://sttaxaictlcc.blob.core.windows.net/data-protection" },
        { "StorageEndpoint", "https://sttaxaictlcc.planted.example/" },
        { "StorageEndpoint", "https://10.0.0.5/" },
        { "KeyRingContainer", null },
        { "KeyRingContainer", "Data-Protection" },
        { "KeyRingContainer", "dp" },
        { "KeyRingContainer", "data--protection" },
        { "KeyRingContainer", "-data-protection" },
        { "KeyUri", null },
        { "KeyUri", $"https://{ControlledKeyVault.Host}/keys/data-protection/{new string('a', 32)}" },
        { "KeyUri", "http://kv-taxai-prod-cc.vault.azure.net/keys/data-protection" },
        { "KeyUri", "https://kv-taxai-prod-cc.planted.example/keys/data-protection" },
        { "ApplicationDiscriminator", null },
        { "ApplicationDiscriminator", "" },
        { "ApplicationDiscriminator", "has spaces" },
        { "ApplicationDiscriminator", new string('a', 65) },
        { "ClientId", null },
        { "ClientId", "not-a-guid" },
        { "ClientId", "00000000-0000-0000-0000-000000000000" },
    };

    [Theory]
    [MemberData(nameof(UnusableSettings))]
    public void AnUnusableKeyRingSettingProducesNoSettings(string setting, string? value)
    {
        var protection = Profile().DataProtection;
        var changed = new DataProtectionSettings
        {
            StorageEndpoint = setting == "StorageEndpoint" ? value : protection.StorageEndpoint,
            StorageResourceId = protection.StorageResourceId,
            KeyRingContainer = setting == "KeyRingContainer" ? value : protection.KeyRingContainer,
            KeyUri = setting == "KeyUri" ? value : protection.KeyUri,
            ApplicationDiscriminator = setting == "ApplicationDiscriminator" ? value : protection.ApplicationDiscriminator,
        };

        Assert.False(AzureDataProtectionConfiguration.TryCreateSettings(
            new AzureInfrastructureOptions
            {
                ManagedIdentity = new ManagedIdentitySettings
                {
                    ClientId = setting == "ClientId" ? value : Profile().ManagedIdentity.ClientId,
                },
                DataProtection = changed,
            },
            out var settings));
        Assert.Null(settings);
    }

    // -------------------------------------------------------------- helpers

    internal static AzureInfrastructureOptions Profile() => new()
    {
        ManagedIdentity = new ManagedIdentitySettings { ClientId = "33333333-3333-4333-8333-333333333333" },
        DataProtection = new DataProtectionSettings
        {
            StorageEndpoint = "https://sttaxaictlcc.blob.core.windows.net/",
            StorageResourceId = "/subscriptions/22222222-2222-4222-8222-222222222222/resourceGroups/rg-taxai-prod-cc/providers/Microsoft.Storage/storageAccounts/sttaxaictlcc",
            KeyRingContainer = "data-protection",
            KeyUri = $"https://{ControlledKeyVault.Host}/keys/data-protection",
            ApplicationDiscriminator = Discriminator,
        },
    };

    private static IDataProtector Protector(IServiceProvider worker) =>
        worker.GetRequiredService<IDataProtectionProvider>().CreateProtector(SessionCookiePurposes);

    /// <summary>One Key Vault and one Blob store shared by every worker.</summary>
    internal sealed class SharedEnvironment
    {
        public SharedEnvironment()
        {
            DataProtectionKeyVersion = Vault.AddKey("data-protection");
        }

        public ControlledKeyVault Vault { get; } = new();

        public ControlledBlobStore Blobs { get; } = new();

        public string DataProtectionKeyVersion { get; }

        public ServiceCollection Services(
            string discriminator = Discriminator,
            string keyVaultToken = ControlledKeyVault.ApprovedToken)
        {
            var profile = Profile();
            var protection = profile.DataProtection;
            Assert.True(AzureDataProtectionConfiguration.TryCreateSettings(
                new AzureInfrastructureOptions
                {
                    ManagedIdentity = profile.ManagedIdentity,
                    DataProtection = new DataProtectionSettings
                    {
                        StorageEndpoint = protection.StorageEndpoint,
                        StorageResourceId = protection.StorageResourceId,
                        KeyRingContainer = protection.KeyRingContainer,
                        KeyUri = protection.KeyUri,
                        ApplicationDiscriminator = discriminator,
                    },
                },
                out var settings));

            var blobOptions = new BlobClientOptions
            {
                Transport = new HttpClientTransport(new HttpClient(Blobs, disposeHandler: false)),
            };
            blobOptions.Retry.MaxRetries = 0;

            var services = new ServiceCollection();
            services
                .AddDataProtection()
                .ProtectWithAzureKeyRing(
                    settings!,
                    new RoutingCredential(ControlledBlobStore.ApprovedToken, keyVaultToken),
                    blobOptions,
                    Vault.CryptographyOptions());
            return services;
        }

        public Worker Worker(
            string discriminator = Discriminator,
            string keyVaultToken = ControlledKeyVault.ApprovedToken) =>
            new(Services(discriminator, keyVaultToken));
    }

    /// <summary>One independently built worker container, disposed with the test.</summary>
    internal sealed class Worker(IServiceProvider provider) : IServiceProvider, IDisposable
    {
        public Worker(IServiceCollection services)
            : this(Container.Build(services))
        {
        }

        public object? GetService(Type serviceType) => provider.GetService(serviceType);

        public void Dispose() => (provider as IDisposable)?.Dispose();
    }

    /// <summary>Issues the storage token for storage scopes and the vault token otherwise.</summary>
    private sealed class RoutingCredential(string storageToken, string vaultToken) : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(
                requestContext.Scopes.Any(scope => scope.Contains("storage.azure.com", StringComparison.OrdinalIgnoreCase))
                    ? storageToken
                    : vaultToken,
                DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    private sealed class SubstituteRepository : IXmlRepository
    {
        public IReadOnlyCollection<System.Xml.Linq.XElement> GetAllElements() => Array.Empty<System.Xml.Linq.XElement>();

        public void StoreElement(System.Xml.Linq.XElement element, string friendlyName)
        {
        }
    }

    private sealed class SubstituteEncryptor : IXmlEncryptor
    {
        public EncryptedXmlInfo Encrypt(System.Xml.Linq.XElement plaintextElement) =>
            throw new NotSupportedException();
    }
}

/// <summary>
/// The default Microsoft.Extensions dependency-injection container.
/// <para>
/// This test project declares no container package (its project file is not
/// part of this slice), and the library under test must not acquire one it does
/// not use. The container is therefore loaded from the installed ASP.NET Core
/// shared framework that runs the portal - the same implementation production
/// uses - and invoked through its public build entry point.
/// </para>
/// </summary>
internal static class Container
{
    private static readonly Lazy<System.Reflection.MethodInfo> BuildMethod = new(() =>
    {
        var runtime = new DirectoryInfo(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        var shared = Path.Combine(runtime.Parent!.Parent!.FullName, "Microsoft.AspNetCore.App", runtime.Name);
        var assembly = System.Reflection.Assembly.LoadFrom(Path.Combine(shared, "Microsoft.Extensions.DependencyInjection.dll"));
        return assembly
            .GetType("Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions", throwOnError: true)!
            .GetMethod("BuildServiceProvider", new[] { typeof(IServiceCollection) })!;
    });

    internal static IServiceProvider Build(IServiceCollection services) =>
        (IServiceProvider)BuildMethod.Value.Invoke(null, new object[] { services })!;
}

/// <summary>
/// An in-memory Blob endpoint for one storage account: bearer-token only (no
/// shared key or SAS is ever accepted), conditional GET and PUT with ETags, and
/// no other operation. It records each request as "METHOD path".
/// </summary>
internal sealed class ControlledBlobStore : HttpMessageHandler
{
    internal const string Host = "sttaxaictlcc.blob.core.windows.net";
    internal const string ApprovedToken = "synthetic-storage-token";

    private readonly object _gate = new();
    private readonly Dictionary<string, (byte[] Content, string ETag)> _blobs = new(StringComparer.Ordinal);
    private readonly List<string> _requests = new();

    internal IReadOnlyDictionary<string, byte[]> Blobs
    {
        get
        {
            lock (_gate)
            {
                return _blobs.ToDictionary(entry => entry.Key, entry => entry.Value.Content.ToArray(), StringComparer.Ordinal);
            }
        }
    }

    internal IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.RequestUri!.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Unexpected destination.");
        }

        var path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath);
        lock (_gate)
        {
            _requests.Add($"{request.Method} {path}");
        }

        if (request.Headers.Authorization is not { Scheme: "Bearer" } authorization ||
            !string.Equals(authorization.Parameter, ApprovedToken, StringComparison.Ordinal) ||
            request.RequestUri.Query.Contains("sig=", StringComparison.OrdinalIgnoreCase))
        {
            return Error(HttpStatusCode.Forbidden, "AuthorizationFailure");
        }

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        lock (_gate)
        {
            _blobs.TryGetValue(path, out var existing);
            var exists = existing.Content is not null;

            if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            {
                if (!exists)
                {
                    return Error(HttpStatusCode.NotFound, "BlobNotFound");
                }

                if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == existing.ETag))
                {
                    return WithBlobHeaders(new HttpResponseMessage(HttpStatusCode.NotModified), existing.ETag);
                }

                var content = existing.Content!;
                var ranged = request.Headers.TryGetValues("x-ms-range", out _) || request.Headers.Range is not null;
                var response = new HttpResponseMessage(ranged ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(request.Method == HttpMethod.Head ? Array.Empty<byte>() : content),
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
                response.Content.Headers.ContentLength = content.Length;
                if (ranged)
                {
                    response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, content.Length - 1, content.Length);
                }

                return WithBlobHeaders(response, existing.ETag);
            }

            if (request.Method == HttpMethod.Put)
            {
                if (request.Headers.IfMatch.Any(tag => tag.Tag != existing.ETag) ||
                    (request.Headers.IfMatch.Count > 0 && !exists))
                {
                    return Error(HttpStatusCode.PreconditionFailed, "ConditionNotMet");
                }

                if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == "*") && exists)
                {
                    return Error(HttpStatusCode.Conflict, "BlobAlreadyExists");
                }

                var etag = $"\"0x{Guid.NewGuid():N}\"";
                _blobs[path] = (body ?? Array.Empty<byte>(), etag);
                return WithBlobHeaders(new HttpResponseMessage(HttpStatusCode.Created), etag);
            }

            return Error(HttpStatusCode.MethodNotAllowed, "UnsupportedHttpVerb");
        }
    }

    private static HttpResponseMessage WithBlobHeaders(HttpResponseMessage response, string etag)
    {
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        response.Headers.TryAddWithoutValidation("x-ms-version", "2025-01-05");
        response.Headers.TryAddWithoutValidation("x-ms-request-server-encrypted", "true");
        response.Headers.TryAddWithoutValidation("Last-Modified", "Sun, 27 Sep 2026 12:00:00 GMT");
        response.Content ??= new ByteArrayContent(Array.Empty<byte>());
        response.Content.Headers.TryAddWithoutValidation("Last-Modified", "Sun, 27 Sep 2026 12:00:00 GMT");
        return response;
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string code)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Error><Code>{code}</Code><Message>synthetic refusal</Message></Error>",
                Encoding.UTF8,
                "application/xml"),
        };
        response.Headers.TryAddWithoutValidation("x-ms-error-code", code);
        return response;
    }
}
