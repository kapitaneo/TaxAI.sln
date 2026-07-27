using CaseProcessing.Services;
using DocumentExtraction.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxLogicEngine.Services;

namespace TaxAI.Portal.Tests;

/// <summary>
/// T02 — proves the composition root wires exactly the approved contracts, that
/// the container is validated strictly enough for a mis-wiring to fail at
/// startup rather than on first request, and that the untrusted-input ingress is
/// not registered.
/// <para>
/// The two validation flags are proved <b>behaviourally</b>, not by reading a
/// property off a builder. <c>WebApplication.CreateBuilder</c> turns
/// validate-on-build and validate-scopes on <i>in Development only</i>, and this
/// test host runs as Production — so a host that never configured them would
/// look fine to a naive assertion and ship unvalidated.
/// </para>
/// </summary>
public sealed class CompositionRootTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public CompositionRootTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

    public void Dispose() => _factory.Dispose();

    // ------------------------------------------------------------------ AC-17

    [Fact]
    public void TheHostStartsInProcess() // AC-17
    {
        var exception = Record.Exception(() =>
        {
            using var client = _factory.CreateClient();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void TheHostBindsNoNetworkPort() // AC-17
    {
        using var client = _factory.CreateClient();

        // TestServer runs the whole pipeline in memory. If this were Kestrel the
        // suite would be binding a real socket, which makes tests
        // environment-dependent and puts a listener in CI.
        var server = _factory.Services.GetRequiredService<IServer>();

        Assert.IsType<TestServer>(server);
    }

    // ------------------------------------------------------------------ AC-18

    [Fact]
    public void ScopeValidationIsOn_ResolvingAScopedServiceFromTheRootProviderThrows() // AC-18
    {
        // The probe is registered by this test, never by ModuleRegistration.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<ScopedProbe>()));

        using var client = factory.CreateClient();

        Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<ScopedProbe>());
    }

    [Fact]
    public void ValidateOnBuildIsOn_AnUnresolvableDependencyFailsAtStartup() // AC-18
    {
        // NeedsMissingDependency cannot be constructed: nothing registers
        // MissingDependency. With ValidateOnBuild off this would surface only
        // when something first asked for it — i.e. in production, on a request.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<NeedsMissingDependency>()));

        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.NotNull(exception);
        Assert.Contains(
            nameof(MissingDependency),
            Flatten(exception!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BothValidationFlagsAreSetOutsideAnyDevelopmentBranch() // AC-18
    {
        // Behaviour proves they are on here; this proves they are on
        // everywhere, by showing the configuration is not conditional.
        // Executable lines only, the convention every module's boundary suite
        // uses: Program.cs documents why the flags are NOT behind an
        // IsDevelopment() branch, and scanning raw text would make that comment
        // unwritable.
        var program = string.Join(
            "\n",
            ExecutableLines(Path.Combine(FindRepositoryRoot(), "src", "TaxAI.Portal", "Program.cs")));

        Assert.Contains("ValidateOnBuild = true", program, StringComparison.Ordinal);
        Assert.Contains("ValidateScopes = true", program, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDevelopment", program, StringComparison.Ordinal);
    }

    // --------------------------------------------------- AC-19, AC-20, AC-21

    [Fact]
    public void TheT4MappingPortResolvesToTheEmbeddedProvider() // AC-19
    {
        Assert.IsType<EmbeddedT4LineMapProvider>(
            _factory.Services.GetRequiredService<IT4LineMapProvider>());
    }

    [Fact]
    public void TheT5MappingPortResolvesToTheEmbeddedProvider() // AC-20
    {
        Assert.IsType<EmbeddedT5LineMapProvider>(
            _factory.Services.GetRequiredService<IT5LineMapProvider>());
    }

    [Fact]
    public void TheT4CoordinatorIsConstructibleFromTheContainer() // AC-21
    {
        // Registered to prove the container can build it. No endpoint resolves
        // or invokes it — the portal runs no tax logic.
        Assert.NotNull(_factory.Services.GetRequiredService<T4CaseProcessor>());
    }

    [Fact]
    public void TheProvidersAreSingletons()
    {
        // Two instances would mean two answers about which mapping config is
        // authoritative.
        Assert.Same(
            _factory.Services.GetRequiredService<IT4LineMapProvider>(),
            _factory.Services.GetRequiredService<IT4LineMapProvider>());

        Assert.Same(
            _factory.Services.GetRequiredService<IT5LineMapProvider>(),
            _factory.Services.GetRequiredService<IT5LineMapProvider>());

        Assert.Same(
            _factory.Services.GetRequiredService<T4CaseProcessor>(),
            _factory.Services.GetRequiredService<T4CaseProcessor>());
    }

    // ------------------------------------------------------------------ AC-24

    [Fact]
    public void TheUntrustedIngressPortIsNotRegistered() // AC-24
    {
        // Spec 007 shipped this signature deliberately without an
        // implementation. A null-returning lookup, not GetRequiredService, so
        // the assertion is about absence rather than about an exception type.
        Assert.Null(_factory.Services.GetService<IDocumentExtractionIngress>());
    }

    [Fact]
    public void ThePortalImplementsNoIngress() // AC-24
    {
        var implementations = typeof(Program).Assembly
            .GetTypes()
            .Where(type => typeof(IDocumentExtractionIngress).IsAssignableFrom(type) && !type.IsInterface)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(implementations);
    }

    [Fact]
    public void NoLineMapperIsRegistered()
    {
        // The mappers are constructed inside CaseProcessing, not resolved by the
        // host. Resolving one here would put a second construction path for tax
        // logic in the web layer.
        Assert.Null(_factory.Services.GetService<T4LineMapper>());
        Assert.Null(_factory.Services.GetService<T5LineMapper>());
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root containing TaxAI.sln.");
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);

            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
            }
        }

        return string.Join(" | ", messages);
    }

    // Test-only probes. Neither may enter ModuleRegistration.
    private sealed class ScopedProbe
    {
    }

    private sealed class MissingDependency
    {
    }

    private sealed class NeedsMissingDependency
    {
        public NeedsMissingDependency(MissingDependency dependency) => Dependency = dependency;

        public MissingDependency Dependency { get; }
    }
}
