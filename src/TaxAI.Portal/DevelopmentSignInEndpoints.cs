using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Persistence;
using TaxAI.Portal.Components.Pages;

namespace TaxAI.Portal;

/// <summary>
/// The Development-only shortcut around the external identity provider
/// (spec 017 T01, decision D-1).
/// <para>
/// <b>It is not a second way to become a user.</b> The handler does exactly
/// what the OpenID Connect handler does once a token has been validated: it
/// signs in to the cookie scheme with a principal carrying an issuer and a
/// subject and nothing else. Everything after that is the existing path — the
/// cookie scheme's sign-in event resolves the local user through the identity
/// directory, issues the firm claim only when exactly one active membership
/// exists, and the ticket store writes the durable server-side session. Every
/// later request is revalidated by the same authorization stage as any other
/// session.
/// </para>
/// <para>
/// The issuer and subject are the fixed synthetic pair the development fixture
/// seeder writes. Nothing is read from the request: no user, firm, membership,
/// or return address. Where a completed sign-in lands is a compile-time
/// constant.
/// </para>
/// <para>
/// <see cref="Map"/> is called from the one environment-gated branch in
/// <c>Program.cs</c>. Outside Development neither route exists, so a request to
/// this path is a request to an unmapped path.
/// </para>
/// </summary>
internal static class DevelopmentSignInEndpoints
{
    internal const string SignInPath = "/development/sign-in";

    /// <summary>Where a completed sign-in lands. A constant, never a request value.</summary>
    internal const string SignedInPath = CaseListEndpoints.ListPath;

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Anonymous, and explicitly so: this is how a caller with no session
        // obtains one, exactly like the external challenge route.
        endpoints.MapGet(SignInPath, Page)
            .AllowAnonymous();
        endpoints.MapPost(SignInPath, (Delegate)SignInAsync)
            .AllowAnonymous()
            .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);
    }

    private static RazorComponentResult<DevelopmentSignInPage> Page() => new();

    private static async Task<IResult> SignInAsync(HttpContext context)
    {
        // The handler binds no form value, so the framework would not refuse an
        // invalid token on its behalf. The validation result is read here and a
        // request without a valid token creates no session.
        if (context.Features.Get<IAntiforgeryValidationFeature>() is not { IsValid: true })
        {
            return Results.Text(
                PortalAuthorization.RefusalBody,
                PortalContent.PlainTextContentType,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var externalIdentity = new ClaimsIdentity(PortalIdentity.ExternalScheme);
        externalIdentity.AddClaim(
            new Claim(PortalIdentity.IssuerClaimType, DevelopmentFixtureIdentity.Issuer));
        externalIdentity.AddClaim(
            new Claim(PortalIdentity.SubjectClaimType, DevelopmentFixtureIdentity.Subject));

        await context.SignInAsync(
            PortalIdentity.CookieScheme,
            new ClaimsPrincipal(externalIdentity),
            new AuthenticationProperties
            {
                AllowRefresh = true,
                IsPersistent = false,
            });

        return Results.Redirect(SignedInPath, permanent: false, preserveMethod: false);
    }
}
