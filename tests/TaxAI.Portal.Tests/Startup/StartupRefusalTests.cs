using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxAI.Portal.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Startup;

/// <summary>
/// Supplies the deployment configuration this test assembly's hosts need, the
/// same way a real deployment supplies it: through a configuration provider
/// outside source control.
/// <para>
/// The portal now requires a database connection string in <b>every</b>
/// environment, and a configured key ring outside Development. No tracked file
/// in this repository may carry either, so the values arrive here as environment
/// variables set before any host is built. A module initializer is used rather
/// than a fixture because the suites that start a host — the shell, health,
/// composition-root, and security suites — construct
/// <c>WebApplicationFactory&lt;Program&gt;</c> directly and share no base type.
/// </para>
/// <para>
/// The connection string is syntactically valid, requires encryption, does not
/// disable certificate validation, and is <b>never opened</b>: registering the
/// SQL Server provider opens no connection, and no test in this project
/// exercises storage.
/// </para>
/// </summary>
internal static class PortalTestDeploymentConfiguration
{
    internal const string ConnectionString =
        "Server=(localdb)\\TaxAiPortalTests;Database=TaxAiPortalTests;" +
        "Encrypt=True;TrustServerCertificate=False;Integrated Security=True";

    internal const string KeyRingLocation = "test-key-ring-location";

    internal const string KeyProtectionIdentifier = "test-key-protection-identifier";

    internal const string ConnectionStringKey = "ConnectionStrings:TaxAiDatabase";

    internal const string KeyRingLocationKey = "TaxAi:DataProtection:KeyRingLocation";

    internal const string KeyProtectionIdentifierKey = "TaxAi:DataProtection:KeyProtectionIdentifier";

    [ModuleInitializer]
    internal static void Supply()
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__TaxAiDatabase", ConnectionString);
        Environment.SetEnvironmentVariable(
            "TaxAi__DataProtection__KeyRingLocation", KeyRingLocation);
        Environment.SetEnvironmentVariable(
            "TaxAi__DataProtection__KeyProtectionIdentifier", KeyProtectionIdentifier);
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__PublicOrigin", "https://intake.test.invalid/");
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__LinkLifetime", "01:00:00");
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__ClientSessionIdleLifetime", "00:15:00");
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__MaximumFileBytes", "1048576");
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__MaximumFilesPerRequest", "1");
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__DigestKey", SyntheticKey(0x11));
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__FingerprintKey", SyntheticKey(0x22));
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__ContentEncryptionKey", SyntheticKey(0x33));
        Environment.SetEnvironmentVariable(
            "TaxAi__Intake__DevelopmentContentRoot",
            Path.Combine(
                Path.GetTempPath(),
                $"taxai-intake-content-tests-{Environment.ProcessId}"));

        foreach (var (name, value) in new[]
                 {
                     ("PublicPermitLimit", "20"),
                     ("PublicWindow", "00:01:00"),
                     ("SessionPermitLimit", "60"),
                     ("SessionWindow", "00:01:00"),
                     ("AnswerPermitLimit", "20"),
                     ("AnswerWindow", "00:01:00"),
                     ("UploadPermitLimit", "10"),
                     ("UploadWindow", "00:01:00"),
                     ("UploadConcurrencyLimit", "2"),
                     ("SubmissionPermitLimit", "10"),
                     ("SubmissionWindow", "00:01:00"),
                 })
        {
            Environment.SetEnvironmentVariable($"TaxAi__Intake__{name}", value);
        }
    }

    private static string SyntheticKey(byte value) =>
        Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>
/// T17 — proves the host fails <b>closed</b>: a misconfigured deployment refuses
/// to start rather than starting unprotected.
/// <para>
/// Every expected message is spelled out here, independently of the production
/// constants. Comparing a refusal to the constant that produced it would assert
/// only that a variable equals itself, and the portal grants this project no
/// internals access.
/// </para>
/// <para>
/// A refusal is proved by <b>starting a host</b>, not by reading source: the
/// checks run between building the container and mapping the routes, so a check
/// that was accidentally unreachable would still look right in a source scan.
/// </para>
/// </summary>
public sealed class StartupRefusalTests
{
    private const string ExpectedMissingConnectionString =
        "A database connection string is required in every environment.";

    private const string ExpectedUnprotectedTransport =
        "The database connection string must require encrypted transport and must not disable server certificate validation.";

    private const string ExpectedUnconfiguredKeyRing =
        "A data protection key ring must be configured outside Development.";

    private const string ExpectedUnregisteredKeyRing =
        "A durable data protection key ring must be registered outside Development. Configuration values alone do not satisfy this requirement.";

    private const string ExpectedNonDurableSessionStore =
        "The registered session store must be the durable one outside Development.";

    // ----------------------------------------------------------------- AC-74

    [Theory] // AC-74
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void StartupRefusesWithoutAConnectionString(string environment)
    {
        // Required in every environment, Development included: a host with no
        // database is not a safe host running degraded, it is a host whose
        // storage layer is unconfigured.
        var refusal = StartupFailure(
            environment,
            configuration: new Dictionary<string, string?>
            {
                [PortalTestDeploymentConfiguration.ConnectionStringKey] = null,
            });

        Assert.Contains(ExpectedMissingConnectionString, refusal, StringComparison.Ordinal);
    }

    [Theory] // AC-74
    [InlineData("")]
    [InlineData("   ")]
    public void StartupRefusesABlankConnectionString(string blank)
    {
        var refusal = StartupFailure(
            "Production",
            configuration: new Dictionary<string, string?>
            {
                [PortalTestDeploymentConfiguration.ConnectionStringKey] = blank,
            });

        Assert.Contains(ExpectedMissingConnectionString, refusal, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- AC-75

    [Theory] // AC-75
    [InlineData("Server=db;Database=TaxAi;Integrated Security=True")]
    [InlineData("Server=db;Database=TaxAi;Encrypt=False;Integrated Security=True")]
    [InlineData("Server=db;Database=TaxAi;Encrypt=True;TrustServerCertificate=True")]
    public void StartupRefusesAnUnprotectedTransportOutsideDevelopment(string connectionString)
    {
        // Encryption that is merely defaulted is not encryption that was
        // required, and a trusted-anything certificate is an unauthenticated
        // channel wearing TLS.
        var refusal = StartupFailure(
            "Production",
            configuration: new Dictionary<string, string?>
            {
                [PortalTestDeploymentConfiguration.ConnectionStringKey] = connectionString,
            });

        Assert.Contains(ExpectedUnprotectedTransport, refusal, StringComparison.Ordinal);
    }

    [Fact] // AC-75
    public void StartupAcceptsAProtectedTransportOutsideDevelopment()
    {
        // The positive control: without it, a refusal test could pass because the
        // guard refuses everything.
        using var host = Host(
            "Production",
            services: collection => collection.AddApprovedDurableDataProtection());

        Assert.Null(Record.Exception(() =>
        {
            using var client = host.CreateClient();
        }));
    }

    // ----------------------------------------------------------------- AC-76

    public static TheoryData<string?, string?> UnconfiguredKeyRings() => new()
    {
        { null, null },
        { null, PortalTestDeploymentConfiguration.KeyProtectionIdentifier },
        { PortalTestDeploymentConfiguration.KeyRingLocation, null },
        { "", PortalTestDeploymentConfiguration.KeyProtectionIdentifier },
        { PortalTestDeploymentConfiguration.KeyRingLocation, "   " },
    };

    [Theory] // AC-76
    [MemberData(nameof(UnconfiguredKeyRings))]
    public void StartupRefusesAnUnconfiguredKeyRingOutsideDevelopment(string? location, string? protection)
    {
        // Both halves are required: a key ring nobody protects and a protector
        // with nothing to protect are each an unusable key ring.
        var refusal = StartupFailure(
            "Production",
            configuration: new Dictionary<string, string?>
            {
                [PortalTestDeploymentConfiguration.KeyRingLocationKey] = location,
                [PortalTestDeploymentConfiguration.KeyProtectionIdentifierKey] = protection,
            },
            services: collection => collection.AddApprovedDurableDataProtection());

        Assert.Contains(ExpectedUnconfiguredKeyRing, refusal, StringComparison.Ordinal);
    }

    // ------------------------------------------------- AC-76 as decided by B-6

    [Fact] // AC-76, B-6
    public void StartupRefusesKeyRingSettingsWithNoRegisteredProvider()
    {
        // The defect this case exists for: the settings are present and correct,
        // and there is no key ring behind them. Configuration values alone must
        // never satisfy the requirement — a host that starts here would protect
        // its cookies with whatever the framework fell back to.
        var refusal = StartupFailure("Production");

        Assert.Contains(ExpectedUnregisteredKeyRing, refusal, StringComparison.Ordinal);
    }

    [Fact] // AC-76, B-6
    public void StartupRefusesARegisteredProviderWithNoExplicitRepository()
    {
        // AddDataProtection() alone leaves KeyManagementOptions.XmlRepository
        // null while the key manager resolves a machine-local directory plus
        // DPAPI lazily. Null is therefore the local fallback, not an undecided
        // state, and must be read as a refusal.
        var refusal = StartupFailure(
            "Production",
            services: collection => collection.AddDataProtection());

        Assert.Contains(ExpectedUnregisteredKeyRing, refusal, StringComparison.Ordinal);
    }

    [Fact] // AC-76, B-6
    public void StartupRefusesADeveloperFileSystemKeyRing()
    {
        // An explicitly configured repository can still be local.
        // PersistKeysToFileSystem is the approved *developer* key ring, and a
        // key ring on one machine's disk is one no second instance can read.
        // The approved encryptor is left in place, so the repository is the only
        // thing this case can be failing on.
        var refusal = StartupFailure(
            "Production",
            services: collection =>
            {
                collection.AddApprovedDurableDataProtection();
                collection
                    .AddDataProtection()
                    .PersistKeysToFileSystem(
                        new DirectoryInfo(Path.Combine(Path.GetTempPath(), "taxai-refused-key-ring")));
            });

        Assert.Contains(ExpectedUnregisteredKeyRing, refusal, StringComparison.Ordinal);
    }

    [Fact] // AC-76, B-6
    public void StartupRefusesAnApprovedRepositoryWithNoEncryptor()
    {
        // The mirror image: a real remote repository holding keys nothing
        // encrypts. Both halves are required independently.
        var refusal = StartupFailure(
            "Production",
            services: collection =>
            {
                collection.AddApprovedDurableDataProtection();
                collection.Configure<KeyManagementOptions>(options => options.XmlEncryptor = null);
            });

        Assert.Contains(ExpectedUnregisteredKeyRing, refusal, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> RejectedFrameworkKeyRingComponents() => new()
    {
        { "repository", "Microsoft.AspNetCore.DataProtection.Repositories.EphemeralXmlRepository" },
        { "repository", "Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository" },
        { "repository", "Microsoft.AspNetCore.DataProtection.Repositories.RegistryXmlRepository" },
        { "encryptor", "Microsoft.AspNetCore.DataProtection.XmlEncryption.NullXmlEncryptor" },
        { "encryptor", "Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlEncryptor" },
        { "encryptor", "Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiNGXmlEncryptor" },
        { "encryptor", "Microsoft.AspNetCore.DataProtection.XmlEncryption.CertificateXmlEncryptor" },
    };

    [Theory] // B-6
    [MemberData(nameof(RejectedFrameworkKeyRingComponents))]
    public void EveryRejectedComponentIsDeclaredInTheFrameworkAssembly(string kind, string typeName)
    {
        // The guard rejects by assembly rather than by a list of type names, so
        // this proves the assembly rule really does cover each name B-6
        // enumerates — and would cover a tenth one a future framework adds.
        _ = kind;

        var frameworkAssembly = typeof(KeyManagementOptions).Assembly;
        var rejected = frameworkAssembly.GetType(typeName, throwOnError: false);

        Assert.True(rejected is not null, $"{typeName} must exist in {frameworkAssembly.GetName().Name}.");
        Assert.Equal(frameworkAssembly, rejected!.Assembly);
    }

    [Fact] // B-6
    public void TheApprovedTestProviderIsNotDeclaredInTheFrameworkAssembly()
    {
        // The other direction: the fake the fixtures rely on must be outside the
        // framework assembly, or every acceptance test below would be passing
        // for the wrong reason.
        var services = new ServiceCollection();
        services.AddApprovedDurableDataProtection();

        using var provider = services.BuildServiceProvider();
        var keyManagement = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.NotNull(provider.GetService<IDataProtectionProvider>());
        Assert.NotNull(keyManagement.XmlRepository);
        Assert.NotNull(keyManagement.XmlEncryptor);
        Assert.NotEqual(
            typeof(KeyManagementOptions).Assembly,
            keyManagement.XmlRepository!.GetType().Assembly);
        Assert.NotEqual(
            typeof(KeyManagementOptions).Assembly,
            keyManagement.XmlEncryptor!.GetType().Assembly);
    }

    [Fact] // AC-76, B-6
    public void StartupAcceptsTheApprovedDurableKeyRingOutsideDevelopment()
    {
        // The positive control. Without it the four refusals above would also
        // pass against a guard that refuses every Production host.
        using var host = Host(
            "Production",
            services: collection => collection.AddApprovedDurableDataProtection());

        Assert.Null(Record.Exception(() =>
        {
            using var client = host.CreateClient();
        }));
    }

    [Fact]
    public void DevelopmentIsNotSubjectToTheKeyRingRequirement()
    {
        // The requirement is scoped to deployed environments by design. Proving
        // the scope explicitly is what stops someone "fixing" a failing local run
        // by weakening the Production branch instead.
        //
        // Nothing is registered here beyond the seeder spy: no key-ring
        // settings, no Data Protection provider, no repository, no encryptor.
        // A Production host in this state is refused four cases above.
        using var host = Host(
            "Development",
            configuration: new Dictionary<string, string?>
            {
                [PortalTestDeploymentConfiguration.KeyRingLocationKey] = null,
                [PortalTestDeploymentConfiguration.KeyProtectionIdentifierKey] = null,
            },
            services: collection => collection.AddScoped<IDevelopmentFixtureSeeder>(
                _ => new SeederSpy()));

        Assert.Null(Record.Exception(() =>
        {
            using var client = host.CreateClient();
        }));

        // And it is functional, not merely started: a developer's local run must
        // survive the guard intact.
        using var developmentClient = host.CreateClient();

        Assert.Equal(
            "Healthy",
            developmentClient.GetStringAsync("/health").GetAwaiter().GetResult());
        Assert.Contains(
            "Authenticated accountants can create client intakes, monitor progress, and review stored packages.",
            developmentClient.GetStringAsync("/").GetAwaiter().GetResult(),
            StringComparison.Ordinal);
    }

    // --------------------------------------------------------- AC-178, AC-179

    [Fact] // AC-179
    public void StartupRefusesANonDurableSessionStoreOutsideDevelopment()
    {
        // An in-memory store makes "signed out" a fact known to one machine.
        var refusal = StartupFailure(
            "Production",
            services: collection =>
            {
                collection.AddApprovedDurableDataProtection();
                collection.AddScoped<IAuthenticationSessionStore, InMemorySessionStore>();
            });

        Assert.Contains(ExpectedNonDurableSessionStore, refusal, StringComparison.Ordinal);
    }

    [Fact] // AC-178
    public void TheProductionSessionStoreIsTheDurableImplementation()
    {
        using var host = Host(
            "Production",
            services: collection => collection.AddApprovedDurableDataProtection());
        using var client = host.CreateClient();
        using var scope = host.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();

        // Identified by the module that implements it, not by a type name: the
        // durable implementation is internal to that module, and this project
        // holds no grant to see it.
        Assert.Equal(
            typeof(Persistence.PersistenceRegistration).Assembly,
            store.GetType().Assembly);

        Assert.IsNotType<InMemorySessionStore>(store);
    }

    // ----------------------------------------------------------------- AC-77

    [Fact] // AC-77
    public void SeedingIsDisabledOutsideDevelopment()
    {
        var seeder = new SeederSpy();

        using var host = Host(
            "Production",
            services: collection =>
            {
                collection.AddApprovedDurableDataProtection();
                collection.AddScoped<IDevelopmentFixtureSeeder>(_ => seeder);
            });

        using var client = host.CreateClient();

        // Startup completed with the refusal's prerequisites satisfied, and the
        // seeder was still never asked. Fixture rows are a development
        // convenience; a deployed environment that grows one has grown a row
        // nobody filed.
        Assert.Equal(0, seeder.Invocations);
    }

    [Fact]
    public void SeedingRunsInDevelopment()
    {
        // The positive control for AC-77: without it, the assertion above would
        // also pass if the seeder were never invoked anywhere.
        var seeder = new SeederSpy();

        using var host = Host(
            "Development",
            services: collection => collection.AddScoped<IDevelopmentFixtureSeeder>(_ => seeder));

        using var client = host.CreateClient();

        Assert.Equal(1, seeder.Invocations);
    }

    // ------------------------------------------------------------- AC-31, guard

    [Fact]
    public void AStartedHostStillAnswersItsTwoConstantRoutes()
    {
        // The storage layer is wired in and the guards run; the two constant
        // routes are unchanged by both.
        using var host = Host(
            "Production",
            services: collection => collection.AddApprovedDurableDataProtection());
        using var client = host.CreateClient();

        Assert.Equal("Healthy", client.GetStringAsync("/health").GetAwaiter().GetResult());
        Assert.Contains(
            "Authenticated accountants can create client intakes, monitor progress, and review stored packages.",
            client.GetStringAsync("/").GetAwaiter().GetResult(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NoRefusalMessageEchoesAConfiguredValue()
    {
        // A refusal reaches a console, a crash report, and often a support
        // ticket. It may name what is wrong; it may never quote the value.
        var refusal = StartupFailure(
            "Production",
            configuration: new Dictionary<string, string?>
            {
                [PortalTestDeploymentConfiguration.ConnectionStringKey] =
                    "Server=secret-host;Database=SecretDatabase;Encrypt=False",
            });

        foreach (var value in new[]
                 {
                     "secret-host",
                     "SecretDatabase",
                     PortalTestDeploymentConfiguration.KeyRingLocation,
                     PortalTestDeploymentConfiguration.KeyProtectionIdentifier,
                 })
        {
            Assert.DoesNotContain(value, refusal, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------ spec 016 T01

    [Fact] // spec 016 T01
    public void ADeployableHostNowFacesTheDurableKeyRingAndSessionRefusalsToo()
    {
        // Before spec 016 a deployable non-Development host was refused before
        // these checks ran. They now apply to every non-Development host, so a
        // deployable one cannot reach the admission gate without them.
        var withSettingsOnly = GuardFailure(services => { });
        Assert.Equal(ExpectedUnregisteredKeyRing, withSettingsOnly);

        var withKeyRingOnly = GuardFailure(services => services.AddApprovedDurableDataProtection());
        Assert.Equal(ExpectedNonDurableSessionStore, withKeyRingOnly);
    }

    [Fact] // spec 016 T01: a started TestServer host is never production authorization.
    public void AStartedProductionTestServerIsStillRefusedByTheAdmissionGate()
    {
        using var host = Host(
            "Production",
            services: collection => collection.AddApprovedDurableDataProtection());
        using var client = host.CreateClient();

        var readiness = (ProductionReadiness)typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.ProductionAdmissionGate", throwOnError: true)!
            .GetMethod("Evaluate", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[]
            {
                host.Services,
                host.Services.GetRequiredService<IConfiguration>(),
                "Production",
                DateTimeOffset.UnixEpoch,
            })!;

        Assert.False(readiness.IsAdmitted);
        Assert.Contains(ProductionAdmissionRefusal.EvidenceManifestMissing, readiness.Refusals);
        Assert.Contains(ProductionAdmissionRefusal.ProductionSourceStoreMissing, readiness.Refusals);
    }

    private static string GuardFailure(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(new GuardEnvironment());
        services.AddSingleton<Microsoft.AspNetCore.Hosting.Server.IServer, DeployableServer>();
        register(services);
        using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var guard = typeof(Program).Assembly
            .GetType("TaxAI.Portal.PortalStartupGuards", throwOnError: true)!
            .GetMethod("RequireSafeStartup", BindingFlags.Static | BindingFlags.NonPublic)!;

        var failure = Assert.Throws<TargetInvocationException>(() =>
            guard.Invoke(null, new object[] { provider, configuration, "Production" }));
        return failure.InnerException!.Message;
    }

    private sealed class GuardEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TaxAI.Portal.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class DeployableServer : Microsoft.AspNetCore.Hosting.Server.IServer
    {
        public Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } =
            new Microsoft.AspNetCore.Http.Features.FeatureCollection();

        public Task StartAsync<TContext>(
            Microsoft.AspNetCore.Hosting.Server.IHttpApplication<TContext> application,
            CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    // ---------------------------------------------------------------- helpers

    private static WebApplicationFactory<Program> Host(
        string environment,
        IDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? services = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);

            if (configuration is not null)
            {
                builder.ConfigureAppConfiguration(
                    (_, sources) => sources.AddInMemoryCollection(configuration));
            }

            if (services is not null)
            {
                builder.ConfigureTestServices(services);
            }
        });

    private static string StartupFailure(
        string environment,
        IDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? services = null)
    {
        using var host = Host(environment, configuration, services);

        var exception = Record.Exception(() =>
        {
            using var client = host.CreateClient();
        });

        Assert.NotNull(exception);

        return Flatten(exception!);
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

    /// <summary>
    /// Records that it was asked, and writes nothing. The real seeder needs a
    /// database; what is under test here is whether the host asks at all.
    /// </summary>
    private sealed class SeederSpy : IDevelopmentFixtureSeeder
    {
        private int _invocations;

        public int Invocations => Volatile.Read(ref _invocations);

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A store that is not the durable one. Every member throws: the guard
    /// refuses on the implementing assembly, so a working double would only
    /// disguise a guard that had stopped looking.
    /// </summary>
    private sealed class InMemorySessionStore : IAuthenticationSessionStore
    {
        public Task<AuthenticationSession> CreateAsync(
            Guid userId,
            Guid? activeFirmId,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticationSession?> FindAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticationSession?> TouchAsync(
            AuthenticationSession session,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RevokeAsync(
            AuthenticationSession session,
            DateTimeOffset atUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
