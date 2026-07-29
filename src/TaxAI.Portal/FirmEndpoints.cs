using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Tenancy.Contracts;

namespace TaxAI.Portal;

/// <summary>
/// The authenticated tenancy-bootstrap surface.
/// <para>
/// It renders firm display names and opaque membership selectors only. A firm
/// identifier is accepted from no request channel and is never written into a
/// URL. The selection endpoint treats its one selector as unverified until it
/// has been resolved against the authenticated session user's freshly loaded
/// active memberships.
/// </para>
/// </summary>
internal static class FirmEndpoints
{
    internal const string FirmsPath = "/firms";

    internal const string SelectFirmPath = "/firms/select";

    internal const string AntiforgeryFieldName = "__RequestVerificationToken";

    internal const string AntiforgeryCookieName = "taxai.antiforgery";

    internal static IAntiforgeryMetadata RequiredAntiforgeryMetadata { get; } =
        new RequiredAntiforgery();

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(FirmsPath, ListAsync)
            .RequireAuthorization(PortalAuthorization.BootstrapPolicy);

        endpoints.MapPost(SelectFirmPath, SelectAsync)
            .RequireAuthorization(PortalAuthorization.BootstrapPolicy)
            .WithMetadata(RequiredAntiforgeryMetadata);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IIdentityDirectory directory,
        IAntiforgery antiforgery)
    {
        var state = PortalAuthorization.BootstrapStateFor(context);
        if (state is null)
        {
            return Refused();
        }

        try
        {
            var firmIds = state.ActiveMemberships
                .Select(membership => membership.FirmId)
                .Distinct()
                .ToArray();
            var firms = await directory.FindFirmsAsync(
                Array.AsReadOnly(firmIds),
                context.RequestAborted);

            if (firms.Count != firmIds.Length ||
                firms.Select(firm => firm.FirmId).Distinct().Count() != firms.Count ||
                firms.Any(firm => !firmIds.Contains(firm.FirmId)))
            {
                return Refused();
            }

            var byId = firms.ToDictionary(firm => firm.FirmId);
            if (state.ActiveMemberships.Any(membership =>
                    !byId.ContainsKey(membership.FirmId)))
            {
                return Refused();
            }

            var tokens = antiforgery.GetAndStoreTokens(context);
            if (string.IsNullOrEmpty(tokens.RequestToken) ||
                !string.Equals(
                    tokens.FormFieldName,
                    AntiforgeryFieldName,
                    StringComparison.Ordinal))
            {
                return Refused();
            }

            return Results.Content(
                Render(state.ActiveMemberships, byId, tokens.RequestToken),
                PortalContent.HtmlContentType);
        }
        catch (Exception)
        {
            return Refused();
        }
    }

    private static async Task<IResult> SelectAsync(
        HttpContext context,
        IIdentityDirectory directory,
        [FromForm(Name = "membershipId")] Guid membershipId)
    {
        var state = PortalAuthorization.BootstrapStateFor(context);
        if (state is null || membershipId == Guid.Empty)
        {
            return Refused();
        }

        try
        {
            var activeMatches = state.ActiveMemberships
                .Where(membership =>
                    membership.MembershipId == membershipId &&
                    membership.UserId == state.User.UserId &&
                    membership.IsActive)
                .ToArray();
            if (activeMatches.Length != 1)
            {
                return Refused();
            }

            var selected = await directory.FindMembershipAsync(
                state.User.UserId,
                membershipId,
                context.RequestAborted);
            if (selected is null ||
                !selected.IsActive ||
                selected.UserId != state.User.UserId ||
                selected.MembershipId != membershipId ||
                selected.FirmId == Guid.Empty ||
                selected.FirmId != activeMatches[0].FirmId ||
                string.IsNullOrWhiteSpace(state.User.Issuer) ||
                string.IsNullOrWhiteSpace(state.User.Subject))
            {
                return Refused();
            }

            // Recover the two external identity keys from the local row, then
            // pass them through T18's local resolution semantics once more.
            // Neither value can be supplied by this request.
            var resolved = await directory.FindUserAsync(
                state.User.Issuer,
                state.User.Subject,
                context.RequestAborted);
            if (resolved is null ||
                !resolved.IsActive ||
                resolved.UserId != state.User.UserId ||
                !string.Equals(
                    resolved.Issuer,
                    state.User.Issuer,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    resolved.Subject,
                    state.User.Subject,
                    StringComparison.Ordinal))
            {
                return Refused();
            }

            var issuanceMemberships = await directory.FindActiveMembershipsAsync(
                resolved.UserId,
                context.RequestAborted);
            var issuanceSelection = issuanceMemberships
                .Where(membership =>
                    membership.IsActive &&
                    membership.UserId == resolved.UserId &&
                    membership.MembershipId == membershipId &&
                    membership.FirmId == selected.FirmId)
                .ToArray();
            if (issuanceSelection.Length != 1)
            {
                return Refused();
            }

            // The first call revokes the current durable session. The second
            // runs T18's existing cookie sign-in event and ticket store through
            // a fresh handler scope. A fresh handler has no old session key to
            // renew, so the ticket store must create a new row and emits the
            // same protected key-only cookie shape T18 established.
            await context.SignOutAsync(PortalIdentity.CookieScheme);
            await IssueReplacementAsync(
                context,
                resolved.Issuer,
                resolved.Subject,
                membershipId);

            return Results.NoContent();
        }
        catch (Exception)
        {
            return Results.Text(
                PortalAuthorization.ErrorBody,
                PortalContent.PlainTextContentType,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task IssueReplacementAsync(
        HttpContext context,
        string issuer,
        string subject,
        Guid membershipId)
    {
        var externalIdentity = new ClaimsIdentity(PortalIdentity.ExternalScheme);
        externalIdentity.AddClaim(
            new Claim(PortalIdentity.IssuerClaimType, issuer));
        externalIdentity.AddClaim(
            new Claim(PortalIdentity.SubjectClaimType, subject));

        var properties = new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = false,
        };
        properties.Items[PortalAuthorization.SelectedMembershipProperty] =
            PortalIdentity.Canonical(membershipId);

        var originalServices = context.RequestServices;
        using var scope = originalServices
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope();

        // Sharing the feature collection shares the current response and its
        // Set-Cookie headers. The separate scoped handler provider is the
        // important part: unlike the handler that authenticated and revoked the
        // old key, it starts with no cached session key.
        var issuanceContext = new DefaultHttpContext(context.Features)
        {
            RequestServices = scope.ServiceProvider,
        };

        try
        {
            await issuanceContext.SignInAsync(
                PortalIdentity.CookieScheme,
                new ClaimsPrincipal(externalIdentity),
                properties);
        }
        finally
        {
            // RequestServices lives in the shared feature collection, so restore
            // the real request provider before the temporary scope is disposed.
            context.RequestServices = originalServices;
        }
    }

    private static string Render(
        IReadOnlyList<Tenancy.Models.FirmMembership> memberships,
        IReadOnlyDictionary<Guid, Tenancy.Models.Firm> firms,
        string requestToken)
    {
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html>\n");
        html.Append("<html lang=\"en\">\n");
        html.Append("<head><meta charset=\"utf-8\" /><title>Select firm</title></head>\n");
        html.Append("<body>\n");
        html.Append("<h1>Select firm</h1>\n");
        html.Append("<ul>\n");

        foreach (var membership in memberships)
        {
            var membershipId = PortalIdentity.Canonical(membership.MembershipId);
            var displayName = HtmlEncoder.Default.Encode(
                firms[membership.FirmId].DisplayName);

            html.Append("<li>");
            html.Append(displayName);
            html.Append(" <span>");
            html.Append(membershipId);
            html.Append("</span>");
            html.Append("<form method=\"post\" action=\"/firms/select\">");
            html.Append("<input type=\"hidden\" name=\"membershipId\" value=\"");
            html.Append(membershipId);
            html.Append("\" />");
            html.Append("<input type=\"hidden\" name=\"");
            html.Append(AntiforgeryFieldName);
            html.Append("\" value=\"");
            html.Append(HtmlEncoder.Default.Encode(requestToken));
            html.Append("\" />");
            html.Append("<button type=\"submit\">Select</button>");
            html.Append("</form>");
            html.Append("</li>\n");
        }

        html.Append("</ul>\n");
        html.Append("</body>\n");
        html.Append("</html>\n");

        return html.ToString();
    }

    private static IResult Refused() =>
        Results.Text(
            PortalAuthorization.RefusalBody,
            PortalContent.PlainTextContentType,
            statusCode: StatusCodes.Status403Forbidden);

    private sealed class RequiredAntiforgery : IAntiforgeryMetadata
    {
        public bool RequiresValidation => true;
    }
}
