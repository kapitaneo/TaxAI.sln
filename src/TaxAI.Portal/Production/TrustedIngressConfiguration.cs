using Microsoft.AspNetCore.HttpOverrides;

namespace TaxAI.Portal.Production;

/// <summary>
/// Trusted regional-ingress handling for the Production host (spec 016 T02,
/// FR-02).
/// <para>
/// In production the only path to this application is client -> HTTPS ->
/// Canada Central Application Gateway WAF v2 -> HTTPS re-encryption -> the
/// private App Service. The App Service has public network access disabled for
/// both the site and SCM, so every request that reaches a worker was relayed by
/// that gateway and the platform front end. Each of those hops <b>appends</b>
/// what it observed to <c>X-Forwarded-For</c> and sets
/// <c>X-Forwarded-Proto</c>; anything to the left of the right-most entry was
/// supplied by the client and is not trusted.
/// </para>
/// <para>
/// This host therefore honours exactly one forwarded hop: the right-most
/// <c>X-Forwarded-For</c> address and the right-most <c>X-Forwarded-Proto</c>
/// scheme. A client-supplied forwarded value can never become the remote
/// address the intake rate-limit partition is keyed on. [ASSUMED] If the
/// platform front end is observed to append its own entry, the right-most
/// entry names the gateway rather than the client; the partition then degrades
/// to the credential/cookie component it already includes, and never trusts a
/// client-supplied address. The live per-worker probe records which it is.
/// </para>
/// <para>
/// <c>X-Forwarded-Host</c> and <c>X-Forwarded-Prefix</c> are deliberately not
/// honoured: the gateway preserves the approved public host name on the
/// backend leg, so the host a browser sees is the host this application sees,
/// and no request header can move the application's origin, cookie scope or
/// sign-in callback. The Spec 014 public origin stays a validated setting.
/// </para>
/// <para>
/// Development and every in-process verification host outside Production are
/// untouched: no forwarded header is honoured there, so no historical browser,
/// questionnaire or rate-limit behaviour changes. The platform's own
/// environment-driven forwarded-header middleware must be off in Production,
/// because running it as well would consume the trusted right-most hop and hand
/// this handler a client-supplied one.
/// </para>
/// </summary>
internal static class TrustedIngressConfiguration
{
    /// <summary>
    /// The host configuration key behind the platform's
    /// <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> switch.
    /// </summary>
    internal const string PlatformForwardedHeadersSetting = "FORWARDEDHEADERS_ENABLED";

    /// <summary>
    /// The one trusted hop: the gateway's appended entry.
    /// </summary>
    internal const int TrustedForwardLimit = 1;

    internal const string DoubleForwardingRefusalMessage =
        "Production startup is refused: forwarded headers must be handled only by the trusted ingress configuration.";

    /// <summary>
    /// The only headers this host honours from the trusted ingress.
    /// </summary>
    internal const ForwardedHeaders TrustedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    /// <summary>
    /// Applies trusted ingress handling to the pipeline. It must run before
    /// routing and rate limiting, so the partition and the request scheme are
    /// already the trusted values. Outside Production it adds nothing.
    /// </summary>
    internal static WebApplication UseTrustedIngress(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!AppliesTo(app.Environment.EnvironmentName))
        {
            return app;
        }

        RequireSingleForwardingHandler(app.Configuration);
        app.UseForwardedHeaders(CreateOptions());
        return app;
    }

    /// <summary>
    /// Trusted ingress applies to the Production environment only, compared
    /// ordinally so a renamed environment never inherits it.
    /// </summary>
    internal static bool AppliesTo(string environmentName) =>
        string.Equals(
            environmentName,
            Environments.Production,
            StringComparison.Ordinal);

    /// <summary>
    /// Refuses a Production host on which the platform's forwarded-header
    /// switch is on. The refusal names no configured value.
    /// </summary>
    internal static void RequireSingleForwardingHandler(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var platform = configuration[PlatformForwardedHeadersSetting];
        if (platform is not null &&
            !string.Equals(platform.Trim(), bool.FalseString, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(DoubleForwardingRefusalMessage);
        }
    }

    /// <summary>
    /// The fixed forwarded-header policy. The framework's loopback-only proxy
    /// defaults are cleared because the gateway and platform front end are not
    /// loopback peers; the trust boundary is the private-only App Service,
    /// enforced by infrastructure, and the single-hop limit.
    /// </summary>
    internal static ForwardedHeadersOptions CreateOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = TrustedHeaders,
            ForwardLimit = TrustedForwardLimit,
            RequireHeaderSymmetry = false,
        };

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        options.AllowedHosts.Clear();
        return options;
    }
}
