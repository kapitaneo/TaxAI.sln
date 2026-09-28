using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using TaxAI.AzureInfrastructure.Keys;

namespace TaxAI.AzureInfrastructure.Tests.Keys;

/// <summary>
/// Spec 016 T03 — the Key Vault content-key wrapper and the runtime credential
/// material provider (FR-04; AC-039, AC-045, AC-048 support).
/// <para>
/// Every case runs the real Azure SDK clients over <see cref="ControlledKeyVault"/>,
/// an in-memory Key Vault data plane that performs <b>actual</b> RSA-OAEP key
/// wrapping with real RSA keys, answers the SDK's authentication challenge,
/// enforces the caller's token and records every request. No test reaches a
/// network, and every key, secret and token is synthetic.
/// </para>
/// </summary>
public sealed class KeyWrappingTests
{
    private const string SourceKey = "source-envelope";

    // ------------------------------------------------------- wrap / unwrap

    [Fact]
    public async Task AContentKeyWrapsUnderTheCurrentVersionAndUnwrapsToTheSameBytes()
    {
        var vault = new ControlledKeyVault();
        var version = vault.AddKey(SourceKey);
        var wrapper = vault.Wrapper(SourceKey);
        var contentKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = await wrapper.WrapAsync(contentKey, CancellationToken.None);
        var recovered = await wrapper.UnwrapAsync(wrapped, CancellationToken.None);

        Assert.Equal(contentKey, recovered);
        Assert.Equal(vault.VersionId(SourceKey, version), wrapped.KeyVersionId.AbsoluteUri);
        Assert.Equal("RSA-OAEP-256", wrapped.Algorithm);
        Assert.Equal(384, wrapped.Ciphertext.Length);
        Assert.Contains(vault.Requests, request => request.EndsWith("/unwrapkey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // AC-039: the wrapped form carries no plaintext key.
    public async Task TheWrappedFormNeverContainsThePlaintextKey()
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);
        var contentKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = await vault.Wrapper(SourceKey).WrapAsync(contentKey, CancellationToken.None);

        Assert.False(Contains(wrapped.Ciphertext.ToArray(), contentKey));
        Assert.Equal(nameof(WrappedContentKey), wrapped.ToString());
        Assert.DoesNotContain(Convert.ToBase64String(contentKey), wrapped.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoWrapsOfTheSameKeyProduceDifferentCiphertext()
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);
        var wrapper = vault.Wrapper(SourceKey);
        var contentKey = RandomNumberGenerator.GetBytes(32);

        var first = await wrapper.WrapAsync(contentKey, CancellationToken.None);
        var second = await wrapper.WrapAsync(contentKey, CancellationToken.None);

        Assert.NotEqual(first.Ciphertext.ToArray(), second.Ciphertext.ToArray());
    }

    [Fact] // AC-045
    public async Task CiphertextWrappedBeforeARotationUnwrapsWithItsOwnVersionAfterIt()
    {
        var vault = new ControlledKeyVault();
        var original = vault.AddKey(SourceKey);
        var wrapper = vault.Wrapper(SourceKey);
        var contentKey = RandomNumberGenerator.GetBytes(32);
        var retained = await wrapper.WrapAsync(contentKey, CancellationToken.None);

        var rotated = vault.Rotate(SourceKey);
        var fresh = await wrapper.WrapAsync(contentKey, CancellationToken.None);

        Assert.Equal(vault.VersionId(SourceKey, rotated), fresh.KeyVersionId.AbsoluteUri);
        Assert.Equal(contentKey, await wrapper.UnwrapAsync(retained, CancellationToken.None));
        Assert.Equal(contentKey, await wrapper.UnwrapAsync(fresh, CancellationToken.None));
        Assert.Contains(
            vault.Requests,
            request => request.Contains($"/keys/{SourceKey}/{original}/unwrapkey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // Retirement is why the usage guard exists: a disabled version fails closed.
    public async Task AnUnwrapWhoseVersionWasDisabledIsRefused()
    {
        var vault = new ControlledKeyVault();
        var original = vault.AddKey(SourceKey);
        var wrapper = vault.Wrapper(SourceKey);
        var retained = await wrapper.WrapAsync(RandomNumberGenerator.GetBytes(32), CancellationToken.None);
        vault.Rotate(SourceKey);
        vault.Disable(SourceKey, original);

        var failure = await Assert.ThrowsAsync<CryptographicException>(
            () => wrapper.UnwrapAsync(retained, CancellationToken.None));

        AssertFixedRefusal(failure);
    }

    [Fact]
    public async Task AWrongIdentityIsRefusedWithAFixedMessage()
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);
        var intruder = vault.Wrapper(SourceKey, token: "synthetic-foreign-identity-token");

        var failure = await Assert.ThrowsAsync<CryptographicException>(
            () => intruder.WrapAsync(RandomNumberGenerator.GetBytes(32), CancellationToken.None));

        AssertFixedRefusal(failure);
        Assert.DoesNotContain("foreign", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ControlledKeyVault.Host, failure.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AWrongIdentityCannotUnwrapCiphertextWrappedByTheApprovedOne()
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);
        var wrapped = await vault.Wrapper(SourceKey).WrapAsync(RandomNumberGenerator.GetBytes(32), CancellationToken.None);

        await Assert.ThrowsAsync<CryptographicException>(
            () => vault.Wrapper(SourceKey, token: "synthetic-foreign-identity-token")
                .UnwrapAsync(wrapped, CancellationToken.None));
    }

    public static TheoryData<string> ForeignKeyVersions() => new()
    {
        // Another key in the same vault.
        $"https://{ControlledKeyVault.Host}/keys/data-protection/{new string('a', 32)}",
        // The same key name in another vault.
        $"https://kv-other-vault.vault.azure.net/keys/{SourceKey}/{new string('a', 32)}",
        // No version: an unwrap never picks one.
        $"https://{ControlledKeyVault.Host}/keys/{SourceKey}",
        // A malformed version.
        $"https://{ControlledKeyVault.Host}/keys/{SourceKey}/not-a-version",
    };

    [Theory]
    [MemberData(nameof(ForeignKeyVersions))]
    public async Task AWrappedKeyNamingAnotherKeyOrVaultIsRefusedBeforeAnyCall(string keyVersion)
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);
        vault.AddKey("data-protection");
        var wrapped = new WrappedContentKey(new Uri(keyVersion), "RSA-OAEP-256", new byte[384]);

        await Assert.ThrowsAsync<CryptographicException>(
            () => vault.Wrapper(SourceKey).UnwrapAsync(wrapped, CancellationToken.None));

        Assert.Empty(vault.Requests);
    }

    [Fact]
    public async Task AnUnapprovedAlgorithmIsRefusedBeforeAnyCall()
    {
        var vault = new ControlledKeyVault();
        var version = vault.AddKey(SourceKey);
        var wrapped = new WrappedContentKey(
            new Uri(vault.VersionId(SourceKey, version)),
            "RSA1_5",
            new byte[384]);

        await Assert.ThrowsAsync<CryptographicException>(
            () => vault.Wrapper(SourceKey).UnwrapAsync(wrapped, CancellationToken.None));

        Assert.Empty(vault.Requests);
    }

    [Fact]
    public async Task TamperedCiphertextIsRefused()
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);
        var wrapper = vault.Wrapper(SourceKey);
        var wrapped = await wrapper.WrapAsync(RandomNumberGenerator.GetBytes(32), CancellationToken.None);
        var tampered = wrapped.Ciphertext.ToArray();
        tampered[10] ^= 0xFF;

        await Assert.ThrowsAsync<CryptographicException>(() => wrapper.UnwrapAsync(
            new WrappedContentKey(wrapped.KeyVersionId, wrapped.Algorithm, tampered),
            CancellationToken.None));
    }

    [Fact]
    public async Task AMissingKeyIsRefused()
    {
        var vault = new ControlledKeyVault();

        await Assert.ThrowsAsync<CryptographicException>(
            () => vault.Wrapper(SourceKey).WrapAsync(RandomNumberGenerator.GetBytes(32), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public async Task OnlyA32ByteContentKeyIsWrapped(int length)
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);

        await Assert.ThrowsAsync<ArgumentException>(
            () => vault.Wrapper(SourceKey).WrapAsync(new byte[length], CancellationToken.None));
        Assert.Empty(vault.Requests);
    }

    public static TheoryData<string> UnapprovedKeyIdentifiers() => new()
    {
        "http://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope",
        "https://kv-taxai-prod-cc.vault.azure.net:8443/keys/source-envelope",
        $"https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope/{new string('a', 32)}",
        "https://kv-taxai-prod-cc.vault.azure.net/secrets/source-envelope",
        "https://kv-taxai-prod-cc.planted.example/keys/source-envelope",
        "https://10.0.0.4/keys/source-envelope",
        "https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope?api-version=7.5",
        "https://kv-taxai-prod-cc.vault.azure.net/keys/source_envelope",
    };

    [Theory]
    [MemberData(nameof(UnapprovedKeyIdentifiers))]
    public void OnlyAVersionlessHttpsKeyVaultKeyIdentifierIsAccepted(string identifier)
    {
        Assert.Throws<ArgumentException>(
            () => new KeyVaultContentKeyWrapper(new Uri(identifier), new StaticCredential("t")));
    }

    [Fact]
    public async Task TheCapabilityProbeHoldsForAWorkingKeyAndFailsClosedOtherwise()
    {
        var vault = new ControlledKeyVault();
        vault.AddKey(SourceKey);

        Assert.True(await vault.Wrapper(SourceKey).VerifyCapabilityAsync(CancellationToken.None));
        Assert.False(await vault.Wrapper(SourceKey, token: "wrong").VerifyCapabilityAsync(CancellationToken.None));
        Assert.False(await vault.Wrapper("missing-key").VerifyCapabilityAsync(CancellationToken.None));
    }

    [Fact] // AC-048: an untrusted service certificate fails closed.
    public async Task AnUntrustedKeyVaultCertificateFailsClosedOnTheDefaultTransport()
    {
        using var listener = new UntrustedTlsListener(ControlledKeyVault.Host);
        var handler = new SocketsHttpHandler
        {
            // Only the TCP destination is redirected to the local listener; the
            // TLS handshake still validates the Key Vault host name against the
            // default trust store, exactly as the production transport does.
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(IPAddress.Loopback, listener.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        var options = new CryptographyClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
        };
        options.Retry.MaxRetries = 0;
        var wrapper = new KeyVaultContentKeyWrapper(
            new Uri($"https://{ControlledKeyVault.Host}/keys/{SourceKey}"),
            new StaticCredential(ControlledKeyVault.ApprovedToken),
            options);

        var failure = await Assert.ThrowsAsync<CryptographicException>(
            () => wrapper.WrapAsync(RandomNumberGenerator.GetBytes(32), CancellationToken.None));

        AssertFixedRefusal(failure);
        Assert.True(listener.HandshakeAttempted, "The client never reached the TLS handshake.");
        Assert.False(listener.RequestReceived, "A request crossed an untrusted TLS session.");
    }

    [Fact]
    public void TheWrapperAndItsResultsAreStructurallyRendered()
    {
        var wrapper = new KeyVaultContentKeyWrapper(
            new Uri($"https://{ControlledKeyVault.Host}/keys/{SourceKey}"),
            new StaticCredential("t"));

        Assert.Equal(nameof(KeyVaultContentKeyWrapper), wrapper.ToString());
        Assert.Equal(32, KeyVaultContentKeyWrapper.ContentKeyBytes);
        Assert.Equal("RSA-OAEP-256", KeyVaultContentKeyWrapper.Algorithm.ToString());
    }

    // --------------------------------------------- runtime credential material

    [Fact]
    public async Task BothIntakeSecretsLoadFromTheVaultByTheApprovedIdentity()
    {
        var vault = new ControlledKeyVault();
        var digest = CanonicalMaterial(0x11);
        var fingerprint = CanonicalMaterial(0x22);
        vault.SetSecret(KeyVaultRuntimeMaterialProvider.DigestKeySecretName, digest);
        vault.SetSecret(KeyVaultRuntimeMaterialProvider.FingerprintKeySecretName, fingerprint);

        var material = await vault.MaterialProvider().LoadAsync(CancellationToken.None);

        Assert.Equal(RuntimeMaterialOutcome.Loaded, material.Outcome);
        Assert.True(material.IsLoaded);
        Assert.Equal(digest, material.DigestKey);
        Assert.Equal(fingerprint, material.FingerprintKey);
        Assert.Equal(nameof(RuntimeMaterial), material.ToString());
        Assert.All(vault.Requests, request => Assert.Contains("/secrets/taxai-intake-", request, StringComparison.Ordinal));
    }

    private static readonly Dictionary<string, (Action<ControlledKeyVault> Arrange, RuntimeMaterialOutcome Expected)> MaterialScenarios = BuildMaterialScenarios();

    private static Dictionary<string, (Action<ControlledKeyVault>, RuntimeMaterialOutcome)> BuildMaterialScenarios()
    {
        var digest = CanonicalMaterial(0x11);
        var fingerprint = CanonicalMaterial(0x22);
        var now = ControlledKeyVault.Now;
        const string digestName = KeyVaultRuntimeMaterialProvider.DigestKeySecretName;
        const string fingerprintName = KeyVaultRuntimeMaterialProvider.FingerprintKeySecretName;
        return new(StringComparer.Ordinal)
        {
            ["digest missing"] = (v => v.SetSecret(fingerprintName, fingerprint), RuntimeMaterialOutcome.Unavailable),
            ["fingerprint missing"] = (v => v.SetSecret(digestName, digest), RuntimeMaterialOutcome.Unavailable),
            ["digest disabled"] = (v => { v.SetSecret(digestName, digest, enabled: false); v.SetSecret(fingerprintName, fingerprint); }, RuntimeMaterialOutcome.Invalid),
            ["digest expired"] = (v => { v.SetSecret(digestName, digest, expires: now.AddMinutes(-1)); v.SetSecret(fingerprintName, fingerprint); }, RuntimeMaterialOutcome.Invalid),
            ["digest not yet valid"] = (v => { v.SetSecret(digestName, digest, notBefore: now.AddMinutes(1)); v.SetSecret(fingerprintName, fingerprint); }, RuntimeMaterialOutcome.Invalid),
            ["digest not canonical"] = (v => { v.SetSecret(digestName, digest + "="); v.SetSecret(fingerprintName, fingerprint); }, RuntimeMaterialOutcome.Invalid),
            ["fingerprint too short"] = (v => { v.SetSecret(digestName, digest); v.SetSecret(fingerprintName, fingerprint[..40]); }, RuntimeMaterialOutcome.Invalid),
            ["fingerprint not base64url"] = (v => { v.SetSecret(digestName, digest); v.SetSecret(fingerprintName, "+" + fingerprint[1..]); }, RuntimeMaterialOutcome.Invalid),
            ["purposes share material"] = (v => { v.SetSecret(digestName, digest); v.SetSecret(fingerprintName, digest); }, RuntimeMaterialOutcome.Invalid),
        };
    }

    public static TheoryData<string> UnusableMaterial() => new(MaterialScenarios.Keys);

    [Theory]
    [MemberData(nameof(UnusableMaterial))]
    public async Task UnusableMaterialIsRefusedWithNoMaterial(string scenario)
    {
        var (arrange, expected) = MaterialScenarios[scenario];
        var vault = new ControlledKeyVault();
        arrange(vault);

        var material = await vault.MaterialProvider().LoadAsync(CancellationToken.None);

        Assert.True(expected == material.Outcome, scenario);
        Assert.False(material.IsLoaded);
        Assert.Null(material.DigestKey);
        Assert.Null(material.FingerprintKey);
    }

    [Fact]
    public async Task AWrongIdentityLoadsNoMaterial()
    {
        var vault = new ControlledKeyVault();
        vault.SetSecret(KeyVaultRuntimeMaterialProvider.DigestKeySecretName, CanonicalMaterial(0x11));
        vault.SetSecret(KeyVaultRuntimeMaterialProvider.FingerprintKeySecretName, CanonicalMaterial(0x22));

        var material = await vault.MaterialProvider(token: "synthetic-foreign-identity-token").LoadAsync(CancellationToken.None);

        Assert.Equal(RuntimeMaterialOutcome.Unavailable, material.Outcome);
        Assert.Null(material.DigestKey);
    }

    [Fact]
    public void ARefusalCanNeverBeFabricatedAsLoaded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeMaterial.Refused(RuntimeMaterialOutcome.Loaded));
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeMaterial.Refused((RuntimeMaterialOutcome)99));
        Assert.Null(typeof(RuntimeMaterial).GetConstructors().SingleOrDefault());
    }

    // -------------------------------------------------------------- helpers

    internal static string CanonicalMaterial(byte fill) =>
        Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static void AssertFixedRefusal(CryptographicException failure)
    {
        Assert.Equal("The content key operation was refused.", failure.Message);
        Assert.Null(failure.InnerException);
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        Enumerable.Range(0, haystack.Length - needle.Length + 1)
            .Any(offset => haystack.AsSpan(offset, needle.Length).SequenceEqual(needle));

    /// <summary>
    /// A local TLS listener presenting a self-signed certificate for the Key
    /// Vault host name, which the default trust store does not trust.
    /// </summary>
    private sealed class UntrustedTlsListener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate;
        private readonly Task _accept;

        public UntrustedTlsListener(string hostName)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={hostName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(hostName);
            request.CertificateExtensions.Add(names.Build());
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            _certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
            _listener.Start();
            _accept = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public bool HandshakeAttempted { get; private set; }

        public bool RequestReceived { get; private set; }

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    HandshakeAttempted = true;
                    using var tls = new SslStream(client.GetStream());
                    try
                    {
                        await tls.AuthenticateAsServerAsync(_certificate);
                        var buffer = new byte[1];
                        RequestReceived = await tls.ReadAsync(buffer) > 0;
                    }
                    catch (Exception failure) when (failure is IOException or System.Security.Authentication.AuthenticationException)
                    {
                        // The client rejected the certificate: the expected outcome.
                    }
                }
            }
            catch (Exception failure) when (failure is ObjectDisposedException or SocketException)
            {
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _certificate.Dispose();
        }
    }
}

/// <summary>A fixed bearer token, standing in for the platform managed identity.</summary>
internal sealed class StaticCredential(string token) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(token, DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}

/// <summary>
/// An in-memory Key Vault data plane for tests. It answers the SDK's
/// challenge-based authentication, accepts exactly one bearer token, stores
/// real RSA keys by version, performs real RSA-OAEP / RSA-OAEP-256 wrapping and
/// unwrapping, serves secrets, and records each request as "METHOD path".
/// </summary>
internal sealed class ControlledKeyVault : HttpMessageHandler
{
    internal const string Host = "kv-taxai-prod-cc.vault.azure.net";
    internal const string ApprovedToken = "synthetic-application-token";
    internal static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<KeyVersion>> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SecretEntry> _secrets = new(StringComparer.Ordinal);
    private readonly List<string> _requests = new();

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

    internal string VersionId(string name, string version) => $"https://{Host}/keys/{name}/{version}";

    internal string AddKey(
        string name,
        int size = 3072,
        string keyType = "RSA-HSM",
        bool enabled = true,
        string recoveryLevel = "Recoverable",
        bool? exportable = false,
        string[]? operations = null)
    {
        var version = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            if (!_keys.TryGetValue(name, out var versions))
            {
                versions = new List<KeyVersion>();
                _keys[name] = versions;
            }

            versions.Add(new KeyVersion(version, RSA.Create(size), keyType, recoveryLevel, exportable, operations ?? new[] { "wrapKey", "unwrapKey" })
            {
                Enabled = enabled,
            });
        }

        return version;
    }

    internal string Rotate(string name)
    {
        KeyVersion current;
        lock (_gate)
        {
            current = _keys[name][^1];
        }

        return AddKey(name, current.Rsa.KeySize, current.KeyType, true, current.RecoveryLevel, current.Exportable, current.Operations);
    }

    internal void Disable(string name, string version)
    {
        lock (_gate)
        {
            _keys[name].Single(entry => entry.Version == version).Enabled = false;
        }
    }

    internal void SetSecret(
        string name,
        string value,
        bool enabled = true,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? expires = null)
    {
        lock (_gate)
        {
            _secrets[name] = new SecretEntry(value, enabled, notBefore, expires);
        }
    }

    internal KeyVaultContentKeyWrapper Wrapper(string keyName, string token = ApprovedToken) =>
        new(new Uri($"https://{Host}/keys/{keyName}"), new StaticCredential(token), CryptographyOptions());

    internal KeyVaultRuntimeMaterialProvider MaterialProvider(string token = ApprovedToken)
    {
        var options = new SecretClientOptions { Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)) };
        options.Retry.MaxRetries = 0;
        return new KeyVaultRuntimeMaterialProvider(new Uri($"https://{Host}/"), new StaticCredential(token), new FixedClock(Now), options);
    }

    internal CryptographyClientOptions CryptographyOptions()
    {
        var options = new CryptographyClientOptions { Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)) };
        options.Retry.MaxRetries = 0;
        return options;
    }

    internal Azure.Security.KeyVault.Keys.KeyClientOptions KeyOptions()
    {
        var options = new Azure.Security.KeyVault.Keys.KeyClientOptions { Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)) };
        options.Retry.MaxRetries = 0;
        return options;
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.RequestUri!.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Unexpected destination.");
        }

        if (request.Headers.Authorization is null)
        {
            var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            challenge.Headers.TryAddWithoutValidation(
                "WWW-Authenticate",
                "Bearer authorization=\"https://login.microsoftonline.com/11111111-1111-4111-8111-111111111111\", resource=\"https://vault.azure.net\"");
            return challenge;
        }

        var path = request.RequestUri.AbsolutePath.TrimEnd('/');
        lock (_gate)
        {
            _requests.Add($"{request.Method} {path}");
        }

        if (!string.Equals(request.Headers.Authorization.Parameter, ApprovedToken, StringComparison.Ordinal))
        {
            return Error(HttpStatusCode.Forbidden, "Forbidden");
        }

        var body = request.Content is null
            ? null
            : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        lock (_gate)
        {
            return segments switch
            {
                ["keys", var name] when request.Method == HttpMethod.Get => GetKey(name, null),
                ["keys", var name, var version] when request.Method == HttpMethod.Get => GetKey(name, version),
                ["keys", var name, var operation] when request.Method == HttpMethod.Post => Crypto(name, null, operation, body),
                ["keys", var name, var version, var operation] when request.Method == HttpMethod.Post => Crypto(name, version, operation, body),
                ["secrets", var name] when request.Method == HttpMethod.Get => GetSecret(name),
                ["secrets", var name, _] when request.Method == HttpMethod.Get => GetSecret(name),
                _ => Error(HttpStatusCode.NotFound, "NotFound"),
            };
        }
    }

    private HttpResponseMessage GetKey(string name, string? version)
    {
        var entry = Find(name, version);
        if (entry is null)
        {
            return Error(HttpStatusCode.NotFound, "KeyNotFound");
        }

        var parameters = entry.Rsa.ExportParameters(false);
        var attributes = new JsonObject
        {
            ["enabled"] = entry.Enabled,
            ["created"] = 1_700_000_000,
            ["updated"] = 1_700_000_000,
            ["recoveryLevel"] = entry.RecoveryLevel,
            ["recoverableDays"] = 90,
        };
        if (entry.Exportable is { } exportable)
        {
            attributes["exportable"] = exportable;
        }

        return Json(new JsonObject
        {
            ["key"] = new JsonObject
            {
                ["kid"] = VersionId(name, entry.Version),
                ["kty"] = entry.KeyType,
                ["key_ops"] = new JsonArray(entry.Operations.Select(operation => (JsonNode)operation).ToArray()),
                ["n"] = Encode(parameters.Modulus!),
                ["e"] = Encode(parameters.Exponent!),
            },
            ["attributes"] = attributes,
        });
    }

    private HttpResponseMessage Crypto(string name, string? version, string operation, JsonNode? body)
    {
        var entry = Find(name, version);
        if (entry is null)
        {
            return Error(HttpStatusCode.NotFound, "KeyNotFound");
        }

        if (!entry.Enabled)
        {
            return Error(HttpStatusCode.Forbidden, "Forbidden");
        }

        var padding = (string?)body?["alg"] switch
        {
            "RSA-OAEP-256" => RSAEncryptionPadding.OaepSHA256,
            "RSA-OAEP" => RSAEncryptionPadding.OaepSHA1,
            _ => null,
        };
        var value = (string?)body?["value"];
        if (padding is null || value is null)
        {
            return Error(HttpStatusCode.BadRequest, "BadParameter");
        }

        try
        {
            var input = Decode(value);
            var output = operation.ToLowerInvariant() switch
            {
                "wrapkey" when entry.Operations.Contains("wrapKey") => entry.Rsa.Encrypt(input, padding),
                "unwrapkey" when entry.Operations.Contains("unwrapKey") => entry.Rsa.Decrypt(input, padding),
                _ => null,
            };
            return output is null
                ? Error(HttpStatusCode.BadRequest, "BadParameter")
                : Json(new JsonObject { ["kid"] = VersionId(name, entry.Version), ["value"] = Encode(output) });
        }
        catch (CryptographicException)
        {
            return Error(HttpStatusCode.BadRequest, "BadParameter");
        }
    }

    private HttpResponseMessage GetSecret(string name)
    {
        if (!_secrets.TryGetValue(name, out var secret))
        {
            return Error(HttpStatusCode.NotFound, "SecretNotFound");
        }

        var attributes = new JsonObject
        {
            ["enabled"] = secret.Enabled,
            ["created"] = 1_700_000_000,
            ["updated"] = 1_700_000_000,
            ["recoveryLevel"] = "Recoverable",
        };
        if (secret.NotBefore is { } notBefore)
        {
            attributes["nbf"] = notBefore.ToUnixTimeSeconds();
        }

        if (secret.Expires is { } expires)
        {
            attributes["exp"] = expires.ToUnixTimeSeconds();
        }

        return Json(new JsonObject
        {
            ["value"] = secret.Value,
            ["id"] = $"https://{Host}/secrets/{name}/{new string('b', 32)}",
            ["attributes"] = attributes,
        });
    }

    private KeyVersion? Find(string name, string? version) =>
        _keys.TryGetValue(name, out var versions)
            ? version is null ? versions[^1] : versions.SingleOrDefault(entry => entry.Version == version)
            : null;

    private static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string code) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { error = new { code, message = "synthetic refusal" } }),
                Encoding.UTF8,
                "application/json"),
        };

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(normalized);
    }

    private sealed class KeyVersion(string version, RSA rsa, string keyType, string recoveryLevel, bool? exportable, string[] operations)
    {
        public string Version { get; } = version;
        public RSA Rsa { get; } = rsa;
        public string KeyType { get; } = keyType;
        public string RecoveryLevel { get; } = recoveryLevel;
        public bool? Exportable { get; } = exportable;
        public string[] Operations { get; } = operations;
        public bool Enabled { get; set; } = true;
    }

    private sealed record SecretEntry(string Value, bool Enabled, DateTimeOffset? NotBefore, DateTimeOffset? Expires);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
