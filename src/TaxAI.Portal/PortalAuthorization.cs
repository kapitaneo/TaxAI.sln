using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal;

/// <summary>
/// The two per-request authorization stages.
/// <para>
/// Cookie authentication has already loaded the durable session and evaluated
/// both lifetime boundaries before either stage can run. This type then reloads
/// the local user and active memberships on every request. The bootstrap stage
/// deliberately permits a session with no active firm; the firm-scoped stage
/// additionally requires exactly one active membership matching the firm claim
/// rebuilt from that same session row.
/// </para>
/// <para>
/// No decision survives the request. Store failures, cancellations, malformed
/// server-derived claims, missing rows, inactive rows, and mismatches all take
/// the same fixed refusal path.
/// </para>
/// </summary>
internal sealed class PortalAuthorization :
    AuthorizationHandler<PortalAuthorization.StageRequirement>,
    IAuthorizationMiddlewareResultHandler
{
    internal const string BootstrapPolicy = "TaxAiTenancyBootstrap";

    internal const string FirmScopedPolicy = "TaxAiFirmScoped";

    internal const string RefusalBody = "Request refused.";

    internal const string ErrorBody = "Request could not be completed.";

    internal const string SelectedMembershipProperty = ".taxai.selected-membership";

    internal static IAuthorizationRequirement BootstrapRequirement { get; } =
        new StageRequirement(requiresFirmScope: false);

    internal static IAuthorizationRequirement FirmScopedRequirement { get; } =
        new StageRequirement(requiresFirmScope: true);

    private static readonly object RequestStateKey = new();

    private static readonly AuthorizationMiddlewareResultHandler DefaultResultHandler = new();

    private readonly IIdentityDirectory _directory;

    public PortalAuthorization(IIdentityDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        _directory = directory;
    }

    /// <summary>
    /// Adds the selected active firm only after T18's original sign-in event has
    /// rebuilt the local principal from issuer + subject. The selector is an
    /// internal authentication property, removed before the ticket store sees
    /// the ticket; the wrapper reloads the user and active memberships again so
    /// a concurrent disable fails closed.
    /// </summary>
    internal static void ConfigureSelectionIssuance(CookieAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var resolveLocalPrincipal = options.Events.OnSigningIn;

        options.Events.OnSigningIn = async context =>
        {
            if (!context.Properties.Items.Remove(
                    SelectedMembershipProperty,
                    out var selectedMembershipValue))
            {
                await resolveLocalPrincipal(context);
                return;
            }

            var issuer = SingleClaim(context.Principal, PortalIdentity.IssuerClaimType);
            var subject = SingleClaim(context.Principal, PortalIdentity.SubjectClaimType);

            if (!TryParseCanonical(selectedMembershipValue, out var membershipId) ||
                string.IsNullOrWhiteSpace(issuer) ||
                string.IsNullOrWhiteSpace(subject))
            {
                throw new InvalidOperationException(PortalIdentity.UnresolvedPrincipalMessage);
            }

            await resolveLocalPrincipal(context);

            if (!TryReadIdentifier(
                    context.Principal,
                    PortalIdentity.UserClaimType,
                    out var userId))
            {
                throw new InvalidOperationException(PortalIdentity.UnresolvedPrincipalMessage);
            }

            var directory =
                context.HttpContext.RequestServices.GetRequiredService<IIdentityDirectory>();
            var user = await directory.FindUserByIdAsync(
                userId,
                context.HttpContext.RequestAborted);
            if (user is null ||
                !user.IsActive ||
                user.UserId != userId ||
                !string.Equals(user.Issuer, issuer, StringComparison.Ordinal) ||
                !string.Equals(user.Subject, subject, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(PortalIdentity.UnresolvedPrincipalMessage);
            }

            var memberships = await directory.FindActiveMembershipsAsync(
                userId,
                context.HttpContext.RequestAborted);
            var selected = memberships
                .Where(membership =>
                    membership.IsActive &&
                    membership.UserId == userId &&
                    membership.MembershipId == membershipId &&
                    membership.FirmId != Guid.Empty)
                .ToArray();
            if (selected.Length != 1)
            {
                throw new InvalidOperationException(PortalIdentity.UnresolvedPrincipalMessage);
            }

            var identity = context.Principal?.Identities.SingleOrDefault(candidate =>
                candidate.IsAuthenticated &&
                string.Equals(
                    candidate.AuthenticationType,
                    PortalIdentity.CookieScheme,
                    StringComparison.Ordinal));
            if (identity is null)
            {
                throw new InvalidOperationException(PortalIdentity.UnresolvedPrincipalMessage);
            }

            foreach (var existing in identity
                         .FindAll(PortalIdentity.FirmClaimType)
                         .ToArray())
            {
                identity.RemoveClaim(existing);
            }

            identity.AddClaim(
                new Claim(
                    PortalIdentity.FirmClaimType,
                    PortalIdentity.Canonical(selected[0].FirmId)));
        };
    }

    internal static BootstrapState? BootstrapStateFor(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(RequestStateKey, out var value)
            ? value as BootstrapState
            : null;
    }

    /// <summary>
    /// Writes one constant response for every authorization refusal. Challenges
    /// still go through the cookie handler so an unauthenticated caller receives
    /// its fixed 401 response rather than being redirected to an identity
    /// provider.
    /// </summary>
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = PortalContent.PlainTextContentType;
            await context.Response.WriteAsync(RefusalBody);
            return;
        }

        await DefaultResultHandler.HandleAsync(
            next,
            context,
            policy,
            authorizeResult);
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StageRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext ||
            !IsSessionPrincipal(context.User) ||
            !TryReadIdentifier(
                context.User,
                PortalIdentity.UserClaimType,
                out var userId))
        {
            return;
        }

        try
        {
            var user = await _directory.FindUserByIdAsync(
                userId,
                httpContext.RequestAborted);
            if (user is null ||
                !user.IsActive ||
                user.UserId == Guid.Empty ||
                user.UserId != userId)
            {
                return;
            }

            var loaded = await _directory.FindActiveMembershipsAsync(
                userId,
                httpContext.RequestAborted);
            var active = loaded
                .Where(membership =>
                    membership.IsActive &&
                    membership.MembershipId != Guid.Empty &&
                    membership.FirmId != Guid.Empty &&
                    membership.UserId == userId)
                .ToArray();

            if (active.Length != loaded.Count)
            {
                return;
            }

            var state = new BootstrapState(user, Array.AsReadOnly(active));

            if (requirement.RequiresFirmScope)
            {
                if (!TryReadIdentifier(
                        context.User,
                        PortalIdentity.FirmClaimType,
                        out var activeFirmId))
                {
                    return;
                }

                var matching = active.Where(membership =>
                    membership.FirmId == activeFirmId).ToArray();
                if (matching.Length != 1)
                {
                    return;
                }

                state = state.WithTrustedFirm(activeFirmId);
            }

            httpContext.Items[RequestStateKey] = state;
            context.Succeed(requirement);
        }
        catch (Exception)
        {
            // A timeout is frequently represented as an
            // OperationCanceledException. It is deliberately caught with every
            // other storage failure: no cached or stale authorization result is
            // available as a fallback.
        }
    }

    private static bool IsSessionPrincipal(ClaimsPrincipal principal)
    {
        var authenticated = principal.Identities
            .Where(identity => identity.IsAuthenticated)
            .ToArray();

        return authenticated.Length == 1 &&
               string.Equals(
                   authenticated[0].AuthenticationType,
                   PortalIdentity.CookieScheme,
                   StringComparison.Ordinal);
    }

    private static string? SingleClaim(ClaimsPrincipal? principal, string claimType)
    {
        var claims = principal?.FindAll(claimType).ToArray() ?? Array.Empty<Claim>();

        return claims.Length == 1 ? claims[0].Value : null;
    }

    private static bool TryReadIdentifier(
        ClaimsPrincipal? principal,
        string claimType,
        out Guid identifier) =>
        TryParseCanonical(SingleClaim(principal, claimType), out identifier);

    private static bool TryParseCanonical(string? value, out Guid identifier)
    {
        if (Guid.TryParseExact(value, "D", out identifier) &&
            identifier != Guid.Empty &&
            string.Equals(
                value,
                PortalIdentity.Canonical(identifier),
                StringComparison.Ordinal))
        {
            return true;
        }

        identifier = Guid.Empty;
        return false;
    }

    internal sealed class StageRequirement : IAuthorizationRequirement
    {
        internal StageRequirement(bool requiresFirmScope)
        {
            RequiresFirmScope = requiresFirmScope;
        }

        internal bool RequiresFirmScope { get; }
    }

    internal sealed class BootstrapState
    {
        internal BootstrapState(
            PortalUser user,
            IReadOnlyList<FirmMembership> activeMemberships,
            Guid? trustedFirmId = null)
        {
            User = user;
            ActiveMemberships = activeMemberships;
            TrustedFirmId = trustedFirmId;
        }

        internal PortalUser User { get; }

        internal IReadOnlyList<FirmMembership> ActiveMemberships { get; }

        internal Guid? TrustedFirmId { get; }

        internal BootstrapState WithTrustedFirm(Guid trustedFirmId) =>
            new(User, ActiveMemberships, trustedFirmId);
    }
}
