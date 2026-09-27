using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using TaxAI.AzureInfrastructure.Configuration;
using Tenancy.Contracts;

namespace TaxAI.AzureInfrastructure.Tests.Configuration;

/// <summary>
/// Spec 016 T01 — the managed-identity binding probe (AC-140, AC-149, AC-150).
/// <para>
/// The probe is exercised twice over: through a controlled
/// <see cref="TokenCredential"/> for every claim rule, and through the real
/// <see cref="ManagedIdentityCredential"/> over a controlled HTTP transport so
/// the production credential path, its requested scope and its destinations
/// are observed rather than assumed. No test reaches a network: the transport
/// answers in memory and records every request.
/// </para>
/// </summary>
public sealed class ManagedIdentityBindingTests
{
    private static readonly Guid Tenant = Guid.Parse(ProductionConfigurationTests.Tenant);
    private static readonly Guid Client = Guid.Parse(ProductionConfigurationTests.ClientId);
    private static readonly Guid Principal = Guid.Parse(ProductionConfigurationTests.PrincipalId);

    private static ManagedIdentityBinding Binding(Guid? client = null) =>
        new(Tenant, client ?? Client, Principal, ProductionConfigurationTests.IdentityResourceId);

    // The platform credential keeps a process-wide token cache per client
    // identity, so each real-credential case uses its own synthetic client.
    private static Guid UniqueClient() => Guid.NewGuid();

    internal static string Token(
        Guid? tenant = null,
        Guid? principal = null,
        Guid? client = null,
        string? resourceId = null,
        bool includeResource = true)
    {
        var claims = new Dictionary<string, object>
        {
            ["tid"] = (tenant ?? Tenant).ToString("D"),
            ["oid"] = (principal ?? Principal).ToString("D"),
            ["appid"] = (client ?? Client).ToString("D"),
            ["aud"] = "https://vault.azure.net",
        };
        if (includeResource)
        {
            claims["xms_mirid"] = resourceId ?? ProductionConfigurationTests.IdentityResourceId;
        }

        return string.Join(
            '.',
            Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}"),
            Encode(JsonSerializer.Serialize(claims)),
            Encode("synthetic-signature"));
    }

    private static string Encode(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    // ------------------------------------------------ claim rules (AC-140)

    [Fact]
    public async Task AMatchingIssuedIdentityIsVerified()
    {
        var probe = new ManagedIdentityBindingProbe(_ => new FixedCredential(Token()));

        Assert.Equal(
            ManagedIdentityBindingOutcome.Verified,
            await probe.VerifyAsync(Binding(), CancellationToken.None));
    }

    public static TheoryData<string> MismatchedTokens() => new()
    {
        Token(tenant: Guid.Parse("99999999-9999-4999-8999-999999999999")),
        Token(principal: Guid.Parse("99999999-9999-4999-8999-999999999999")),
        Token(client: Guid.Parse("99999999-9999-4999-8999-999999999999")),
        Token(resourceId: ProductionConfigurationTests.ResourcePrefix +
            "Microsoft.ManagedIdentity/userAssignedIdentities/id-someone-else"),
        Token(includeResource: false),
        "not-a-jwt",
        "a.b.c",
        "a.bm90LWpzb24.c",
        string.Empty,
    };

    [Theory]
    [MemberData(nameof(MismatchedTokens))]
    public async Task AnyIssuedIdentityOtherThanTheBindingIsMismatched(string token)
    {
        var probe = new ManagedIdentityBindingProbe(_ => new FixedCredential(token));

        Assert.Equal(
            ManagedIdentityBindingOutcome.Mismatched,
            await probe.VerifyAsync(Binding(), CancellationToken.None));
    }

    public static TheoryData<Exception> CredentialFailures() => new()
    {
        new CredentialUnavailableException("synthetic"),
        new AuthenticationFailedException("synthetic"),
        new Azure.RequestFailedException("synthetic"),
        new OperationCanceledException(),
    };

    [Theory] // AC-150: no insecure fallback when the identity is unavailable.
    [MemberData(nameof(CredentialFailures))]
    public async Task AnUnavailableIdentityIsUnavailableAndNeverFallsBack(Exception failure)
    {
        var credential = new FailingCredential(failure);
        var probe = new ManagedIdentityBindingProbe(_ => credential);

        Assert.Equal(
            ManagedIdentityBindingOutcome.Unavailable,
            await probe.VerifyAsync(Binding(), CancellationToken.None));
        Assert.Equal(1, credential.Calls);
    }

    [Fact] // AC-149: a posture invalidated after a verified check fails closed.
    public async Task EveryCallReverifiesSoALaterInvalidationFailsClosed()
    {
        var credential = new SequenceCredential(
            Token(),
            Token(principal: Guid.Parse("99999999-9999-4999-8999-999999999999")));
        var probe = new ManagedIdentityBindingProbe(_ => credential);

        Assert.Equal(
            ManagedIdentityBindingOutcome.Verified,
            await probe.VerifyAsync(Binding(), CancellationToken.None));
        Assert.Equal(
            ManagedIdentityBindingOutcome.Mismatched,
            await probe.VerifyAsync(Binding(), CancellationToken.None));
        Assert.Equal(2, credential.Calls);
    }

    [Fact]
    public async Task TheProbeRequestsOnlyTheDataPlaneScope()
    {
        var credential = new FixedCredential(Token());
        var probe = new ManagedIdentityBindingProbe(_ => credential);

        await probe.VerifyAsync(Binding(), CancellationToken.None);

        Assert.Equal(new[] { "https://vault.azure.net/.default" }, credential.Scopes);
        Assert.DoesNotContain(
            credential.Scopes,
            scope => scope.Contains("management", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------- the real credential over a controlled transport

    [Fact]
    public async Task TheRealManagedIdentityCredentialIsVerifiedOverAControlledTransport()
    {
        var client = UniqueClient();
        var transport = new RecordingHandler(Token(client: client));
        var probe = new ManagedIdentityBindingProbe(binding => RealCredential(binding, transport));

        var outcome = await probe.VerifyAsync(Binding(client), CancellationToken.None);

        Assert.Equal(ManagedIdentityBindingOutcome.Verified, outcome);
        Assert.NotEmpty(transport.Requests);
        Assert.All(transport.Requests, request =>
        {
            Assert.DoesNotContain("management.azure.com", request, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("vault.azure.net", request, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(client.ToString("D"), request, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task TheRealCredentialRefusingOverTheTransportIsUnavailable()
    {
        var transport = new RecordingHandler(token: null);
        var probe = new ManagedIdentityBindingProbe(binding => RealCredential(binding, transport));

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var outcome = await probe.VerifyAsync(Binding(UniqueClient()), deadline.Token);

        Assert.Equal(ManagedIdentityBindingOutcome.Unavailable, outcome);
        Assert.NotEmpty(transport.Requests);
    }

    [Fact]
    public async Task TheRealCredentialIssuingAForeignIdentityIsMismatched()
    {
        var client = UniqueClient();
        var transport = new RecordingHandler(
            Token(client: client, principal: Guid.Parse("99999999-9999-4999-8999-999999999999")));
        var probe = new ManagedIdentityBindingProbe(binding => RealCredential(binding, transport));

        Assert.Equal(
            ManagedIdentityBindingOutcome.Mismatched,
            await probe.VerifyAsync(Binding(client), CancellationToken.None));
    }

    [Fact]
    public void TheProbeIsStructurallyRenderedAndHasAParameterlessProductionConstructor()
    {
        Assert.Equal(nameof(ManagedIdentityBindingProbe), new ManagedIdentityBindingProbe().ToString());
        Assert.Equal(nameof(ManagedIdentityBinding), Binding().ToString());
    }

    private static TokenCredential RealCredential(
        ManagedIdentityBinding binding,
        RecordingHandler handler)
    {
        var options = new ManagedIdentityCredentialOptions(
            ManagedIdentityId.FromUserAssignedClientId(binding.ClientId.ToString("D")))
        {
            Transport = new HttpClientTransport(new HttpClient(handler)),
        };
        options.Retry.MaxRetries = 0;
        options.Retry.NetworkTimeout = TimeSpan.FromSeconds(5);

        return new ManagedIdentityCredential(options);
    }

    // ------------------------------------------------------------ doubles

    private sealed class FixedCredential(string token) : TokenCredential
    {
        public List<string> Scopes { get; } = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes.AddRange(requestContext.Scopes);
            return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    private sealed class SequenceCredential(params string[] tokens) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(tokens[Math.Min(Calls++, tokens.Length - 1)], DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    private sealed class FailingCredential(Exception failure) : TokenCredential
    {
        public int Calls { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            throw failure;
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>
    /// Answers every managed-identity request in memory and records its URL.
    /// A null token answers with a refusal instead.
    /// </summary>
    private sealed class RecordingHandler(string? token) : HttpMessageHandler
    {
        private readonly List<string> _requests = new();

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request.RequestUri!.ToString());
            }

            if (token is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        "{\"error\":\"invalid_request\",\"error_description\":\"synthetic refusal\"}",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            var expiresOn = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var body = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["access_token"] = token,
                ["client_id"] = ProductionConfigurationTests.ClientId,
                ["expires_in"] = "3599",
                ["expires_on"] = expiresOn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ext_expires_in"] = "3599",
                ["not_before"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["resource"] = "https://vault.azure.net",
                ["token_type"] = "Bearer",
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
