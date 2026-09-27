using System.Reflection;
using System.Runtime.CompilerServices;
using ExtractionWorkflow.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace TaxAI.Portal.Tests.Extraction;

internal static class ExtractionTestDeploymentConfiguration
{
    [ModuleInitializer]
    internal static void Supply()
    {
        Environment.SetEnvironmentVariable(
            "TaxAi__Extraction__Provider",
            "Deterministic");
        Environment.SetEnvironmentVariable(
            "TaxAi__Extraction__ContentReader",
            "DevelopmentEncryptedIntakeContentStore");
        Environment.SetEnvironmentVariable(
            "TaxAi__Extraction__ProviderCallLimit",
            "00:00:05");
        Environment.SetEnvironmentVariable(
            "TaxAi__Extraction__MaximumSourceBytes",
            "1048576");
        Environment.SetEnvironmentVariable(
            "TaxAi__Extraction__ProviderLabel",
            "test-deterministic");
    }
}

public sealed class ExtractionStartupGuardTests
{
    private const string InvalidMessage =
        "Document extraction configuration is missing or invalid.";

    private const string NonDevelopmentMessage =
        "Document extraction is unavailable outside Development until an approved production provider and content reader are supplied.";

    private const string AdmissionRefusalMessage =
        "Production startup is refused: the exact approved production configuration, registrations, identity and release evidence are not all present and valid.";

    private const string UnconfiguredKeyRingMessage =
        "A data protection key ring must be configured outside Development.";

    [Theory]
    [InlineData("TaxAi:Extraction:Provider", null)]
    [InlineData("TaxAi:Extraction:Provider", "")]
    [InlineData("TaxAi:Extraction:Provider", "Cloud")]
    [InlineData("TaxAi:Extraction:ContentReader", null)]
    [InlineData("TaxAi:Extraction:ContentReader", "")]
    [InlineData("TaxAi:Extraction:ContentReader", "FileSystem")]
    public void MissingEmptyAndInvalidProviderOrReaderRefuses(
        string setting,
        string? value)
    {
        var settings = ValidExtractionSettings();
        settings[setting] = value;

        var failure = Assert.Throws<TargetInvocationException>(
            () => RequireExtraction(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(settings)
                    .Build(),
                new TestEnvironment("Development")));

        Assert.Equal(InvalidMessage, failure.InnerException?.Message);
    }

    [Theory]
    [InlineData("TaxAi:Extraction:ProviderCallLimit", "")]
    [InlineData("TaxAi:Extraction:ProviderCallLimit", "00:00:00")]
    [InlineData("TaxAi:Extraction:MaximumSourceBytes", "invalid")]
    [InlineData("TaxAi:Extraction:MaximumSourceBytes", "0")]
    [InlineData("TaxAi:Extraction:ProviderLabel", "")]
    [InlineData("TaxAi:Extraction:ProviderLabel", "   ")]
    public void InvalidProviderBoundsAndLabelRefuse(
        string setting,
        string value)
    {
        var settings = ValidExtractionSettings();
        settings[setting] = value;

        var failure = Assert.Throws<TargetInvocationException>(
            () => RequireExtraction(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(settings)
                    .Build(),
                new TestEnvironment("Development")));

        Assert.Equal(InvalidMessage, failure.InnerException?.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void DeployableNonDevelopmentStartupRefuses(string environmentName)
    {
        // Spec 016 T01 replaced the unconditional refusal with the enforced
        // production admission gate. A deployable host with the historical
        // extraction and intake settings is refused twice over: by the durable
        // key-ring requirement and by the gate, which no deployable host can
        // pass until the production adapters and evidence exist.
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(
            new TestEnvironment(environmentName));
        services.AddSingleton<IServer, DeployableServer>();
        services.AddSingleton(TimeProvider.System);
        using var provider = services.BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ValidHostSettings())
            .Build();

        var guard = typeof(Program).Assembly
            .GetType(
                "TaxAI.Portal.PortalStartupGuards",
                throwOnError: true)!
            .GetMethod(
                "RequireSafeStartup",
                BindingFlags.Static | BindingFlags.NonPublic)!;

        var failure = Assert.Throws<TargetInvocationException>(
            () => guard.Invoke(
                obj: null,
                new object[] { provider, configuration, environmentName }));

        Assert.Equal(UnconfiguredKeyRingMessage, failure.InnerException?.Message);

        var gate = typeof(Program).Assembly
            .GetType(
                "TaxAI.Portal.Production.ProductionAdmissionGate",
                throwOnError: true)!
            .GetMethod(
                "RequireAdmission",
                BindingFlags.Static | BindingFlags.NonPublic)!;

        var refusal = Assert.Throws<TargetInvocationException>(
            () => gate.Invoke(
                obj: null,
                new object[] { provider, configuration, environmentName }));

        Assert.Equal(AdmissionRefusalMessage, refusal.InnerException?.Message);

        // The historical extraction refusal itself is unchanged.
        var extraction = Assert.Throws<TargetInvocationException>(
            () => RequireExtraction(configuration, new TestEnvironment(environmentName)));
        Assert.Equal(NonDevelopmentMessage, extraction.InnerException?.Message);
    }

    [Fact]
    public void DevelopmentCompositionRootBuildsBothWorkflowsWithoutExposingTheReader()
    {
        var environment = new TestEnvironment("Development")
        {
            WebRootPath = Path.Combine(
                Path.GetTempPath(),
                "taxai-t06-composition-webroot"),
        };
        var settings = ValidHostSettings();
        settings["TaxAi:Intake:DevelopmentContentRoot"] = Path.Combine(
            Path.GetTempPath(),
            "taxai-t06-composition-content");
        settings["TaxAi:Oidc:Authority"] = "https://identity.test.invalid/";
        settings["TaxAi:Oidc:ClientId"] = "taxai-t06-tests";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        var registration = typeof(Program).Assembly
            .GetType(
                "TaxAI.Portal.ModuleRegistration",
                throwOnError: true)!
            .GetMethod(
                "AddTaxAiModules",
                BindingFlags.Static | BindingFlags.NonPublic)!;

        _ = registration.Invoke(
            obj: null,
            new object[]
            {
                services,
                configuration,
                environment,
                settings["ConnectionStrings:TaxAiDatabase"]!,
            });

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
            });
        using var scope = provider.CreateScope();

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IExtractionIngestWorkflow>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<IExtractionReviewWorkflow>());
        Assert.Null(
            scope.ServiceProvider.GetService<IExtractionContentReader>());
    }

    private static void RequireExtraction(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var method = typeof(Program).Assembly
            .GetType(
                "TaxAI.Portal.ExtractionConfiguration",
                throwOnError: true)!
            .GetMethod(
                "Require",
                BindingFlags.Static | BindingFlags.NonPublic)!;
        _ = method.Invoke(obj: null, new object[] { configuration, environment });
    }

    private static Dictionary<string, string?> ValidExtractionSettings() =>
        new(StringComparer.Ordinal)
        {
            ["TaxAi:Extraction:Provider"] = "Deterministic",
            ["TaxAi:Extraction:ContentReader"] =
                "DevelopmentEncryptedIntakeContentStore",
            ["TaxAi:Extraction:ProviderCallLimit"] = "00:00:05",
            ["TaxAi:Extraction:MaximumSourceBytes"] = "1048576",
            ["TaxAi:Extraction:ProviderLabel"] = "test-deterministic",
        };

    private static Dictionary<string, string?> ValidHostSettings()
    {
        var settings = ValidExtractionSettings();
        settings["ConnectionStrings:TaxAiDatabase"] =
            "Server=(localdb)\\TaxAiPortalTests;Database=TaxAiPortalTests;" +
            "Encrypt=True;TrustServerCertificate=False;Integrated Security=True";
        settings["TaxAi:Intake:PublicOrigin"] = "https://intake.test.invalid/";
        settings["TaxAi:Intake:LinkLifetime"] = "01:00:00";
        settings["TaxAi:Intake:ClientSessionIdleLifetime"] = "00:15:00";
        settings["TaxAi:Intake:MaximumFileBytes"] = "1048576";
        settings["TaxAi:Intake:MaximumFilesPerRequest"] = "1";
        settings["TaxAi:Intake:DigestKey"] = Key(0x11);
        settings["TaxAi:Intake:FingerprintKey"] = Key(0x22);
        settings["TaxAi:Intake:ContentEncryptionKey"] = Key(0x33);
        settings["TaxAi:Intake:PublicPermitLimit"] = "20";
        settings["TaxAi:Intake:PublicWindow"] = "00:01:00";
        settings["TaxAi:Intake:SessionPermitLimit"] = "60";
        settings["TaxAi:Intake:SessionWindow"] = "00:01:00";
        settings["TaxAi:Intake:AnswerPermitLimit"] = "20";
        settings["TaxAi:Intake:AnswerWindow"] = "00:01:00";
        settings["TaxAi:Intake:UploadPermitLimit"] = "10";
        settings["TaxAi:Intake:UploadWindow"] = "00:01:00";
        settings["TaxAi:Intake:UploadConcurrencyLimit"] = "2";
        settings["TaxAi:Intake:SubmissionPermitLimit"] = "10";
        settings["TaxAi:Intake:SubmissionWindow"] = "00:01:00";
        return settings;
    }

    private static string Key(byte value) =>
        Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class TestEnvironment(string environmentName) :
        IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TaxAI.Portal.Tests";
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class DeployableServer : IServer
    {
        public IFeatureCollection Features { get; } =
            new FeatureCollection();

        public Task StartAsync<TContext>(
            IHttpApplication<TContext> application,
            CancellationToken cancellationToken)
            where TContext : notnull =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
