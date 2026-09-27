using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Tenancy.Contracts;

namespace TaxAI.AzureInfrastructure.Configuration;

/// <summary>
/// Verifies that the running host actually holds its configured user-assigned
/// managed identity (spec 016 AC-140, AC-149, AC-150).
/// <para>
/// The probe requests one <b>data-plane</b> token for Key Vault through
/// <see cref="ManagedIdentityCredential"/> and compares the issued token's
/// tenant, object, application and managed-identity resource claims with the
/// configured binding. It calls no Azure Resource Manager endpoint and needs no
/// control-plane role: issuing a token proves which identity the platform gave
/// this process, and nothing more is required to bind configuration to it.
/// </para>
/// <para>
/// There is exactly one credential and no chain. A failed or unavailable
/// managed identity is <see cref="ManagedIdentityBindingOutcome.Unavailable"/>;
/// it never falls back to a developer, environment, CLI or interactive
/// credential. Every call re-verifies, so a binding that was valid at startup
/// and is later invalidated fails the next verification.
/// </para>
/// <para>
/// The token, its claims and any exception text never leave this method. The
/// result is only the closed outcome.
/// </para>
/// </summary>
public sealed class ManagedIdentityBindingProbe : IManagedIdentityBindingProbe
{
    /// <summary>A data-plane scope. Deliberately not the management scope.</summary>
    public const string TokenScope = "https://vault.azure.net/.default";

    private readonly Func<ManagedIdentityBinding, TokenCredential> _credentialFactory;

    /// <summary>
    /// Creates the production probe: the platform managed-identity credential for
    /// the configured user-assigned client identifier, and nothing else.
    /// </summary>
    public ManagedIdentityBindingProbe()
        : this(binding => new ManagedIdentityCredential(
            new ManagedIdentityCredentialOptions(
                ManagedIdentityId.FromUserAssignedClientId(binding.ClientId.ToString("D")))))
    {
    }

    /// <summary>
    /// Creates a probe over an explicit credential factory. Tests use this seam to
    /// supply a controlled transport; the production registration does not.
    /// </summary>
    public ManagedIdentityBindingProbe(
        Func<ManagedIdentityBinding, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _credentialFactory = credentialFactory;
    }

    public async Task<ManagedIdentityBindingOutcome> VerifyAsync(
        ManagedIdentityBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);

        AccessToken token;
        try
        {
            var credential = _credentialFactory(binding);
            token = await credential
                .GetTokenAsync(new TokenRequestContext(new[] { TokenScope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is
            AuthenticationFailedException or
            CredentialUnavailableException or
            Azure.RequestFailedException or
            OperationCanceledException or
            InvalidOperationException or
            ArgumentException)
        {
            return ManagedIdentityBindingOutcome.Unavailable;
        }

        return Matches(token.Token, binding)
            ? ManagedIdentityBindingOutcome.Verified
            : ManagedIdentityBindingOutcome.Mismatched;
    }

    public override string ToString() => nameof(ManagedIdentityBindingProbe);

    private static bool Matches(string? token, ManagedIdentityBinding binding)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var segments = token.Split('.');
        if (segments.Length != 3)
        {
            return false;
        }

        try
        {
            using var claims = JsonDocument.Parse(DecodeSegment(segments[1]));
            var root = claims.RootElement;

            return root.ValueKind == JsonValueKind.Object &&
                   ClaimIs(root, "tid", binding.TenantId) &&
                   ClaimIs(root, "oid", binding.PrincipalId) &&
                   (ClaimIs(root, "appid", binding.ClientId) || ClaimIs(root, "azp", binding.ClientId)) &&
                   root.TryGetProperty("xms_mirid", out var resource) &&
                   resource.ValueKind == JsonValueKind.String &&
                   string.Equals(resource.GetString(), binding.ResourceId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception failure) when (failure is JsonException or FormatException)
        {
            return false;
        }
    }

    private static bool ClaimIs(JsonElement root, string claim, Guid expected) =>
        root.TryGetProperty(claim, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        Guid.TryParse(value.GetString(), out var actual) &&
        actual == expected;

    private static byte[] DecodeSegment(string segment)
    {
        var normalized = segment.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            0 => normalized,
            _ => throw new FormatException("Malformed token segment."),
        };

        return Convert.FromBase64String(normalized);
    }
}
