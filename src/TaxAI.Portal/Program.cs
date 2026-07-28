using TaxAI.Portal;

var builder = WebApplication.CreateBuilder(args);

// Probing the app should leak nothing about it. TestServer does not use Kestrel,
// so this is verified by source assertion rather than over the wire.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Set unconditionally, and deliberately not inside an IsDevelopment() branch:
// WebApplication.CreateBuilder enables these in Development only, so a
// config-free host would ship to Production with an unvalidated container and a
// mis-wiring would surface on first request rather than at startup.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// The one and only place modules are wired in. Every Add* registration lives
// behind this call, in ModuleRegistration.cs.
builder.Services.AddTaxAiModules();

var app = builder.Build();

// A probe that describes the system is a disclosure surface, so the body is a
// fixed literal and nothing else: no version, environment, dependency status,
// or timestamp.
app.MapGet("/health", () => Results.Text(PortalContent.HealthBody, PortalContent.PlainTextContentType));

app.Run();

/// <summary>
/// Public solely so the in-process test host can find the entry point. It
/// declares no public member of its own, and it is the portal's only public
/// type.
/// </summary>
public partial class Program
{
}
