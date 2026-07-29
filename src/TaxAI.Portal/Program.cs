using Microsoft.AspNetCore.Authentication;
using TaxAI.Portal;
using Tenancy.Contracts;

var builder = WebApplication.CreateBuilder(args);

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

// Identity runs before routing decides anything, and authorization after. Both
// are called explicitly rather than left to the implicit ordering, so the one
// place to read the pipeline is this file.
app.UseAuthentication();
app.UseAuthorization();

// Server-rendered constant content: no view engine, no static-file middleware,
// no wwwroot, and no external asset. The shell states what this application is
// and that it serves no taxpayer data — it binds no data of any kind.
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

// Sign-out revokes the server-side session and clears the cookie, in that
// order of importance. A cleared cookie alone leaves a copied pre-logout cookie
// replayable; a revoked row is refused by every application instance on the
// very next request.
app.MapPost(
        PortalIdentity.SignOutPath,
        () => Results.SignOut(
            new AuthenticationProperties(),
            new[] { PortalIdentity.CookieScheme }))
   .RequireAuthorization();

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
