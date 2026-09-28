using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TaxAI.AzureInfrastructure.Protection;
using TaxAI.Portal.Production;
using Tenancy.Contracts;

namespace TaxAI.Portal;

/// <summary>
/// The host's identity wiring: an external OpenID Connect challenge that ends in
/// a local cookie carrying nothing but an opaque key into the server-side
/// session.
/// <para>
/// <b>Nothing the provider says about tenancy is believed.</b> The token is used
/// for exactly two facts — issuer and subject — and the local principal is then
/// built from this repository's own identity tables. A provider that asserts a
/// firm claim is asserting something it has no standing to assert, and a portal
/// that honoured it would let whoever controls the identity provider read
/// another firm's taxpayer data.
/// </para>
/// <para>
/// The firm decision has exactly three outcomes and the application never picks
/// one: exactly one active membership issues the claim; zero or several issue
/// <b>no</b> claim at all. "Pick the first" is how a member of two firms ends up
/// looking at the wrong one's returns.
/// </para>
/// <para>
/// This file is authentication wiring only. It holds no tax logic, no CRA
/// concept, no taxpayer value, and no monetary value; the only identifiers it
/// handles are opaque GUIDs.
/// </para>
/// </summary>
internal static class PortalIdentity
{
    /// <summary>The local cookie scheme — the one the application authenticates with.</summary>
    internal const string CookieScheme = "TaxAiSession";

    /// <summary>The external scheme, used only to challenge and to be called back.</summary>
    internal const string ExternalScheme = "TaxAiOidc";

    /// <summary>
    /// The one tenancy claim. It is issued by this file and re-materialized from
    /// the session record on every request; a claim of this name arriving from
    /// anywhere else is discarded.
    /// </summary>
    internal const string FirmClaimType = "taxai_firm_id";

    /// <summary>The local user identifier, carried on the live principal only.</summary>
    internal const string UserClaimType = "taxai_user_id";

    /// <summary>
    /// Raw OIDC claim names. Inbound claim mapping is off, so the token's own
    /// names survive and this host reads the two it is entitled to read.
    /// </summary>
    internal const string IssuerClaimType = "iss";

    internal const string SubjectClaimType = "sub";

    internal const string SignInPath = "/sign-in";

    internal const string SignOutPath = "/sign-out";

    internal const string CallbackPath = "/signin-oidc";

    internal const string SignedOutCallbackPath = "/signout-callback-oidc";

    /// <summary>Where a completed challenge lands. A constant, never a request value.</summary>
    internal const string PostSignInPath = "/";

    internal const string CookieName = "taxai.session";

    internal const string AuthoritySetting = "TaxAi:Oidc:Authority";

    internal const string ClientIdSetting = "TaxAi:Oidc:ClientId";

    internal const string ClientSecretSetting = "TaxAi:Oidc:ClientSecret";

    /// <summary>
    /// The developer-local key ring folder, under the content root and ignored by
    /// source control. Development only: outside it,
    /// <see cref="PortalStartupGuards"/> requires a registered durable key ring
    /// and refuses to start without one, and in Production (spec 016 T03) the
    /// admission gate accepts only the protected Azure key ring below.
    /// </summary>
    internal const string DeveloperKeyRingFolder = "DataProtection-Keys";

    /// <summary>
    /// Fixed and non-interpolated. A sign-in that cannot be resolved must fail
    /// without saying which of the several reasons applied — "no such subject"
    /// and "that account is disabled" are different sentences only to an
    /// attacker.
    /// </summary>
    internal const string UnresolvedPrincipalMessage =
        "The authenticated subject does not resolve to an active local user.";

    /// <summary>
    /// Also fixed and non-interpolated: a refusal reaches a console and often a
    /// support ticket, and may name what is missing without quoting it.
    /// </summary>
    internal const string UnconfiguredIdentityProviderMessage =
        "An OpenID Connect authority and client identifier are required in every environment.";

    /// <summary>
    /// This host's own single registration extension, called once from the
    /// composition root.
    /// </summary>
    internal static IServiceCollection AddPortalIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Required in every environment, before anything is registered. The only
        // way into this application is an external challenge, so a host with no
        // identity provider cannot authenticate anybody — and the handler
        // validates its own options on every request, which would turn a missing
        // setting into a failure on each call rather than one at startup.
        var authority = configuration[AuthoritySetting];
        var clientId = configuration[ClientIdSetting];

        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(UnconfiguredIdentityProviderMessage);
        }

        // The environment-sensitive behaviour here only ever adds a key ring; it
        // never removes a control. Development gets its local key ring. Spec 016
        // T03: Production gets the shared Canadian Blob key ring protected by the
        // Key Vault key and reached by the managed identity, configured from the
        // closed production profile. A profile that does not describe one
        // registers none, and the host is refused at startup (decision B-3).
        // Every other environment registers none and is refused as before.
        if (environment.IsDevelopment())
        {
            // Written on one line so the only file-system expression in this
            // project is a single, greppable site rather than a construction
            // spread across three.
            var developerKeyRing = new DirectoryInfo(Path.Combine(environment.ContentRootPath, DeveloperKeyRingFolder));

            services.AddDataProtection().PersistKeysToFileSystem(developerKeyRing);
        }
        else if (string.Equals(environment.EnvironmentName, Environments.Production, StringComparison.Ordinal))
        {
            if (AzureDataProtectionConfiguration.TryCreateSettings(
                    ProductionConfiguration.Read(configuration).Options,
                    out var keyRing))
            {
                services.AddDataProtection().ProtectWithAzureKeyRing(keyRing!);
            }
        }

        // Singleton because the cookie handler's options hold one instance for
        // the process; it opens its own scope per operation rather than
        // capturing a scoped store.
        services.AddSingleton<SessionTicketStore>();

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;

                // Deliberately the cookie scheme and not the external one: a
                // protected request that arrives without a session must be
                // refused where it stands, not bounced through an identity
                // provider. Only /sign-in challenges outward, and it names the
                // external scheme explicitly.
                options.DefaultChallengeScheme = CookieScheme;
                options.DefaultSignInScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, ConfigureCookie)
            .AddOpenIdConnect(
                ExternalScheme,
                options => ConfigureOpenIdConnect(options, authority, clientId, configuration[ClientSecretSetting]));

        // The ticket store is attached here rather than inside ConfigureCookie
        // because it is a service, and this is the seam that resolves one
        // without the options delegate reaching into a provider.
        services
            .AddOptions<CookieAuthenticationOptions>(CookieScheme)
            .Configure<SessionTicketStore>((options, store) => options.SessionStore = store);

        services.AddAuthorization();

        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = CookieName;

        // Not readable by script, not sent over cleartext, not sent on a
        // cross-site request. Lax rather than Strict so the redirect back from
        // the identity provider still carries the session.
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.IsEssential = true;

        // The cookie's own window mirrors the module that owns the policy rather
        // than restating it. A second duration written here would be a second
        // policy, and the two would drift.
        options.ExpireTimeSpan = Tenancy.Services.SessionLifetimePolicy.IdleTimeout;
        options.SlidingExpiration = true;

        options.LoginPath = SignInPath;

        // Resolution happens on sign-in rather than inside the OIDC event, so
        // there is exactly one place a local principal is built, whichever
        // scheme signed the caller in.
        options.Events.OnSigningIn = ResolveLocalPrincipalAsync;

        // Refuse in place instead of redirecting. A redirect would make the
        // framework read a return-url query value, and this host reads no
        // request input at all.
        options.Events.OnRedirectToLogin = context => Refuse(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => Refuse(context, StatusCodes.Status403Forbidden);
        options.Events.OnRedirectToLogout = context => Refuse(context, StatusCodes.Status200OK);
        options.Events.OnRedirectToReturnUrl = context => Refuse(context, StatusCodes.Status200OK);
    }

    private static void ConfigureOpenIdConnect(
        OpenIdConnectOptions options,
        string authority,
        string clientId,
        string? clientSecret)
    {
        options.Authority = authority;
        options.ClientId = clientId;
        options.ClientSecret = clientSecret;

        options.CallbackPath = CallbackPath;
        options.SignedOutCallbackPath = SignedOutCallbackPath;
        options.SignInScheme = CookieScheme;

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.RequireHttpsMetadata = true;

        // The three that keep provider material out of this process. SaveTokens
        // is the important one: with it on, the access, refresh, and id tokens
        // are serialized into the authentication properties and follow the
        // ticket wherever it goes.
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;

        // The minimum that identifies a subject. Every additional scope is a
        // profile field this repository would then be holding.
        options.Scope.Clear();
        options.Scope.Add(OpenIdConnectScope.OpenId);

        options.Events.OnTokenValidated = DiscardProviderSuppliedClaims;
    }

    /// <summary>
    /// Replaces the provider's principal with one built from scratch, carrying
    /// issuer and subject and nothing else.
    /// <para>
    /// Written as construction rather than as filtering on purpose. A filter has
    /// to enumerate what to drop and is wrong the day the provider adds a claim
    /// nobody listed; building a fresh identity is wrong on no day.
    /// </para>
    /// </summary>
    private static Task DiscardProviderSuppliedClaims(TokenValidatedContext context)
    {
        var identity = new ClaimsIdentity(ExternalScheme);

        CopyClaim(identity, IssuerClaimType, context.Principal?.FindFirst(IssuerClaimType)?.Value);
        CopyClaim(identity, SubjectClaimType, context.Principal?.FindFirst(SubjectClaimType)?.Value);

        context.Principal = new ClaimsPrincipal(identity);

        return Task.CompletedTask;
    }

    /// <summary>
    /// The single place a local principal comes into existence. It runs for every
    /// sign-in to the cookie scheme, so no alternate route can produce a
    /// principal that skipped the directory.
    /// </summary>
    private static async Task ResolveLocalPrincipalAsync(CookieSigningInContext context)
    {
        var directory = context.HttpContext.RequestServices.GetRequiredService<IIdentityDirectory>();

        context.Principal = await BuildLocalPrincipalAsync(
            directory,
            context.Principal?.FindFirst(IssuerClaimType)?.Value,
            context.Principal?.FindFirst(SubjectClaimType)?.Value,
            context.HttpContext.RequestAborted);
    }

    private static async Task<ClaimsPrincipal> BuildLocalPrincipalAsync(
        IIdentityDirectory directory,
        string? issuer,
        string? subject,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException(UnresolvedPrincipalMessage);
        }

        var user = await directory.FindUserAsync(issuer, subject, cancellationToken);

        // There is no registration surface in this repository, so an
        // unrecognized or disabled subject is not a new user — it is a sign-in
        // that must not complete.
        if (user is null || !user.IsActive)
        {
            throw new InvalidOperationException(UnresolvedPrincipalMessage);
        }

        var memberships = await directory.FindActiveMembershipsAsync(user.UserId, cancellationToken);

        var identity = new ClaimsIdentity(CookieScheme);
        identity.AddClaim(new Claim(UserClaimType, Canonical(user.UserId)));

        // Re-filtered rather than trusted: the directory already returns active
        // memberships, and a second check here costs nothing while removing the
        // possibility that a future implementation quietly widens the meaning of
        // "active".
        var active = memberships.Where(membership => membership.IsActive).ToArray();

        if (active.Length == 1)
        {
            identity.AddClaim(new Claim(FirmClaimType, Canonical(active[0].FirmId)));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// The canonical, invariant-culture GUID form — the same form the trusted
    /// firm identifier parses. Anything else would be a second spelling of an
    /// identifier that is compared ordinally.
    /// </summary>
    internal static string Canonical(Guid identifier) =>
        identifier.ToString("D", CultureInfo.InvariantCulture);

    private static void CopyClaim(ClaimsIdentity identity, string claimType, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            identity.AddClaim(new Claim(claimType, value));
        }
    }

    /// <remarks>
    /// The context type is fully qualified: the OpenID Connect namespace also
    /// declares a <c>RedirectContext</c>, and the two are not the same thing.
    /// </remarks>
    private static Task Refuse(
        Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context,
        int statusCode)
    {
        context.Response.StatusCode = statusCode;

        return Task.CompletedTask;
    }
}
