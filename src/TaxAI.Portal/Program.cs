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

var app = builder.Build();

// Wave 0 deliberately registers no service and maps no route. The composition
// root is declared but not yet called (T02), and the two endpoints arrive in
// T02 and T03. A host that starts and serves nothing is honest; a host wired to
// a NotImplementedException would build clean and throw on run.
app.Run();

/// <summary>
/// Public solely so the in-process test host can find the entry point. It
/// declares no public member of its own, and it is the portal's only public
/// type.
/// </summary>
public partial class Program
{
}
