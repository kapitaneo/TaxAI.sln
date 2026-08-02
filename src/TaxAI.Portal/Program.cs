using Microsoft.AspNetCore.Authentication;
using TaxAI.Portal;
using TaxAI.Portal.Components;
using Tenancy.Contracts;

var builder = WebApplication.CreateBuilder(args);

// The route to a review record carries one opaque persistence identifier.
// Framework request-start/completion and exception logs include request paths
// and exception messages by default, so anything below Critical could copy that
// identifier or a storage exception's data into a diagnostic sink. The portal
// deliberately emits no operational log; the one approved structural purge log
// remains owned by Persistence and is not reachable from an HTTP endpoint.
builder.Logging.SetMinimumLevel(LogLevel.Critical);

// Probing the app should leak nothing about it. TestServer does not use Kestrel,
// so this is verified by source assertion rather than over the wire.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Set unconditionally, and deliberately not inside an environment branch:
// WebApplication.CreateBuilder enables these in Development only, so a
// config-free host would ship to Production with an unvalidated container and a
// mis-wiring would surface on first request rather than at startup.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// Fail closed before anything is registered. The connection string is required
// in every environment and never appears in a tracked file; outside Development
// its transport requirements are checked here too. The guard compares the
// environment name ordinally rather than calling IsDevelopment(), so a security
// control is never written as a negated environment branch.
var databaseConnectionString = PortalStartupGuards.RequireDatabaseConnectionString(
    builder.Configuration,
    builder.Environment.EnvironmentName);

// The one and only place modules are wired in. Every Add* registration lives
// behind this call, in ModuleRegistration.cs.
builder.Services.AddTaxAiModules(
    builder.Configuration,
    builder.Environment,
    databaseConnectionString);

var app = builder.Build();

// The second half of the refusal, against the built application: the connection
// string is re-verified, and outside Development a registered durable key ring
// and the durable session store are both required. Both are read out of the
// container rather than out of configuration — two settings naming a key ring
// prove nothing about whether one exists. Registering SQL Server opens no
// connection, so this resolves the store to inspect it without touching the
// database.
PortalStartupGuards.RequireSafeStartup(
    app.Services,
    app.Configuration,
    app.Environment.EnvironmentName);

// Every escaping exception becomes one fixed structural response in every
// environment. The framework's stock exception-handler middleware logs the
// original exception before it renders its branch; this application cannot use
// that default because a storage exception may contain taxpayer content or the
// opaque route selector. This boundary deliberately writes no diagnostic sink.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception failure)
    {
        context.Response.Clear();
        context.Response.StatusCode = failure is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;
        context.Response.ContentType = PortalContent.PlainTextContentType;
        await context.Response.WriteAsync(PortalAuthorization.ErrorBody);
    }
});

// Identity runs before routing decides anything, and authorization after. Both
// are called explicitly rather than left to the implicit ordering, so the one
// place to read the pipeline is this file.
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// The tracked local stylesheets, and nothing else. Spec 014 replaced the review
// screen's inline assembled <style> block with component-scoped and global CSS,
// so those two files have to be reachable. Every asset is local and tracked in
// this repository: there is no CDN, remote font, remote image, or third-party
// request anywhere in this application.
//
// Middleware, deliberately NOT MapStaticAssets(). The endpoint-routing version
// registers a `{**path:file}` catch-all over the entire URL space, and that
// silently changes this host's refusal behaviour: every POST-only route starts
// answering GET with 404 instead of 405, because the catch-all matches the path
// before method selection can reject it. It also adds an endpoint that is neither
// explicitly anonymous nor authorization-required, which is the third state spec
// 011 forbids. Middleware serves the same two files and adds no route at all, so
// the host's endpoint inventory stays exact and closed.
app.UseStaticFiles();

// Server-rendered constant content: no view engine and no data binding. The shell
// truthfully points authenticated accountants at the one read-only capability and
// binds no data of any kind. Its body is byte-identical to spec 010's.
//
// Anonymous, and explicitly so. An endpoint that is anonymous because nobody
// remembered to protect it looks exactly like one that is anonymous by
// decision; only one of them survives review.
app.MapGet("/", () => Results.Content(PortalContent.ShellHtml, PortalContent.HtmlContentType))
   .AllowAnonymous();

// A probe that describes the system is a disclosure surface, so the body is a
// fixed literal and nothing else: no version, environment, dependency status,
// or timestamp. It keeps answering when storage is unavailable, because it
// reaches storage never.
app.MapGet("/health", () => Results.Text(PortalContent.HealthBody, PortalContent.PlainTextContentType))
   .AllowAnonymous();

// The outward challenge, and the only endpoint that names the external scheme.
// Where it lands afterwards is a compile-time constant: a return path taken
// from a request is an open redirect wearing a login page.
app.MapGet(
        PortalIdentity.SignInPath,
        () => Results.Challenge(
            new AuthenticationProperties { RedirectUri = PortalIdentity.PostSignInPath },
            new[] { PortalIdentity.ExternalScheme }))
   .AllowAnonymous();

// The two authenticated tenancy-bootstrap routes. They re-read the local user
// and active memberships on every request, and are the only protected routes
// allowed to run while the durable session has no active firm.
FirmEndpoints.Map(app);

// The Blazor Web App endpoint. Static server rendering only.
//
// No component in this project declares a @page directive, so this contributes no
// routable page — and with no interactive render mode configured, no circuit
// endpoint either. It adds no route at all today. It is the framework handle the
// component model is mounted on; the review screen reaches its component through
// RazorComponentResult from its own authorization-gated endpoint.
//
// Deliberately NOT .AddInteractiveServerRenderMode(). That call is what
// materializes the /_blazor SignalR endpoints, and nothing in this project is
// interactive, so they would expand this taxpayer-data host's endpoint surface
// while providing no current functionality. Spec 014 T03 adds it together with
// the first interactive island that needs it.
app.MapRazorComponents<App>();

// The portal's one taxpayer-data surface. Authorization revalidates the local
// user and active memberships before this endpoint can obtain the trusted firm;
// the handler then uses only Tenancy's already-firm-scoped read contract, and
// returns a statically server-rendered component rather than assembled markup.
ReviewEndpoints.Map(app);

// Sign-out revokes the server-side session and clears the cookie, in that
// order of importance. A cleared cookie alone leaves a copied pre-logout cookie
// replayable; a revoked row is refused by every application instance on the
// very next request.
app.MapPost(
        PortalIdentity.SignOutPath,
        () => Results.SignOut(
            new AuthenticationProperties(),
            new[] { PortalIdentity.CookieScheme }))
   .RequireAuthorization()
   .WithMetadata(FirmEndpoints.RequiredAntiforgeryMetadata);

// The one environment-gated behaviour this host has. Fixtures are obviously
// synthetic and are written by the module that owns the schema; the host only
// decides whether to ask. Awaited synchronously rather than through an async
// entry point, so seeding completes before the first request is accepted.
if (app.Environment.IsDevelopment())
{
    using var developmentScope = app.Services.CreateScope();

    developmentScope.ServiceProvider
        .GetRequiredService<IDevelopmentFixtureSeeder>()
        .SeedAsync(CancellationToken.None)
        .GetAwaiter()
        .GetResult();
}

app.Run();

/// <summary>
/// Public solely so the in-process test host can find the entry point. It
/// declares no public member of its own, and it is the portal's only public
/// type.
/// </summary>
public partial class Program
{
}
