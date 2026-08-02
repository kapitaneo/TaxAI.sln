using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace TaxAI.Portal.Tests.Startup;

public sealed class IntakeConfigurationTests
{
    [Fact]
    public void CompleteDevelopmentConfigurationIsAccepted()
    {
        var root = NewRoot();
        try
        {
            var result = Require(
                ValidSettings(root),
                new TestEnvironment(Environments.Development, NewRoot()));

            Assert.NotNull(result);
            Assert.Equal("IntakeRuntimeConfiguration", result!.ToString());
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("http://intake.example.test/")]
    [InlineData("https://user@intake.example.test/")]
    [InlineData("https://intake.example.test/path")]
    [InlineData("https://intake.example.test/?query=1")]
    [InlineData("https://intake.example.test/#fragment")]
    public void NonCanonicalOrNonHttpsOriginIsRefused(string origin)
    {
        var settings = ValidSettings(NewRoot());
        settings["TaxAi:Intake:PublicOrigin"] = origin;

        AssertFixedRefusal(settings, Environments.Production);
    }

    [Fact]
    public void MissingSettingAndReusedKeysAreRefused()
    {
        var missing = ValidSettings(NewRoot());
        missing.Remove("TaxAi:Intake:MaximumFileBytes");
        AssertFixedRefusal(missing, Environments.Production);

        var reused = ValidSettings(NewRoot());
        reused["TaxAi:Intake:FingerprintKey"] = reused["TaxAi:Intake:DigestKey"];
        AssertFixedRefusal(reused, Environments.Production);
    }

    [Fact]
    public void DevelopmentRootMustBeAbsoluteAndOutsideWebRoot()
    {
        var webRoot = NewRoot();
        var nested = Path.Combine(webRoot, "intake");
        var settings = ValidSettings(nested);

        AssertFixedRefusal(
            settings,
            Environments.Development,
            webRoot);
    }

    [Fact]
    public void ProductionDoesNotAcceptOrRegisterADevelopmentRootRequirement()
    {
        var settings = ValidSettings(NewRoot());
        settings.Remove("TaxAi:Intake:DevelopmentContentRoot");

        Assert.NotNull(Require(
            settings,
            new TestEnvironment(Environments.Production, NewRoot())));
    }

    private static object? Require(
        IReadOnlyDictionary<string, string?> settings,
        IWebHostEnvironment environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var type = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.IntakeConfiguration",
            throwOnError: true)!;
        return type.GetMethod(
                "Require",
                BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [configuration, environment]);
    }

    private static void AssertFixedRefusal(
        IReadOnlyDictionary<string, string?> settings,
        string environmentName,
        string? webRoot = null)
    {
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Require(
                settings,
                new TestEnvironment(environmentName, webRoot ?? NewRoot())));
        var refusal = Assert.IsType<InvalidOperationException>(
            exception.InnerException);
        Assert.Equal(
            "Client intake configuration is missing or invalid.",
            refusal.Message);
    }

    private static Dictionary<string, string?> ValidSettings(string contentRoot) =>
        new(StringComparer.Ordinal)
        {
            ["TaxAi:Intake:PublicOrigin"] = "https://intake.example.test/",
            ["TaxAi:Intake:LinkLifetime"] = "2.00:00:00",
            ["TaxAi:Intake:ClientSessionIdleLifetime"] = "00:30:00",
            ["TaxAi:Intake:MaximumFileBytes"] = "1048576",
            ["TaxAi:Intake:MaximumFilesPerRequest"] = "4",
            ["TaxAi:Intake:DigestKey"] = Key(0x11),
            ["TaxAi:Intake:FingerprintKey"] = Key(0x22),
            ["TaxAi:Intake:ContentEncryptionKey"] = Key(0x33),
            ["TaxAi:Intake:DevelopmentContentRoot"] = contentRoot,
            ["TaxAi:Intake:PublicPermitLimit"] = "10",
            ["TaxAi:Intake:PublicWindow"] = "00:01:00",
            ["TaxAi:Intake:SessionPermitLimit"] = "20",
            ["TaxAi:Intake:SessionWindow"] = "00:01:00",
            ["TaxAi:Intake:AnswerPermitLimit"] = "10",
            ["TaxAi:Intake:AnswerWindow"] = "00:01:00",
            ["TaxAi:Intake:UploadPermitLimit"] = "5",
            ["TaxAi:Intake:UploadWindow"] = "00:01:00",
            ["TaxAi:Intake:UploadConcurrencyLimit"] = "2",
            ["TaxAi:Intake:SubmissionPermitLimit"] = "5",
            ["TaxAi:Intake:SubmissionWindow"] = "00:01:00",
        };

    private static string Key(byte value) =>
        Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string NewRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "taxai-intake-config-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class TestEnvironment(
        string environmentName,
        string webRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TaxAI.Portal.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = webRootPath;
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = NewRoot();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
