using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Identity;
using TaxAI.Portal.Tests.Support;
using Tenancy.Contracts;

namespace TaxAI.Portal.Tests.Startup;

/// <summary>
/// Spec 017 T01 — the local setup tool, and the settings it produces.
/// <para>
/// The tool is run for real, with its settings redirected into a temporary
/// file and its database step skipped: no test writes into a developer's
/// user-secrets store, and no test needs a SQL Server. What it generates is
/// then handed to the real host, which is the only meaningful proof that the
/// generated settings are the ones the host requires.
/// </para>
/// </summary>
public sealed class LocalDemoToolingTests : IDisposable
{
    private const string ConnectionStringKey = "ConnectionStrings:TaxAiDatabase";

    private const string ContentRootKey = "TaxAi:Intake:DevelopmentContentRoot";

    private static readonly string[] GeneratedKeys =
    {
        "TaxAi:Intake:DigestKey",
        "TaxAi:Intake:FingerprintKey",
        "TaxAi:Intake:ContentEncryptionKey",
    };

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ScriptPath =
        Path.Combine(RepositoryRoot, "scripts", "local", "Start-LocalDemo.ps1");

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), $"taxai-local-demo-tests-{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_workspace, "settings.json");

    private string ContentRoot => Path.Combine(_workspace, "content");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    // ---------------------------------------------------------------- AC-6

    [Fact] // AC-6, AC-28
    public async Task TheGeneratedSettingsStartARealDevelopmentHost()
    {
        var settings = Setup().Settings;

        using var host = CreateHost("Development", settings);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        // The host built: the connection string, intake and extraction
        // validation, and the startup guards all accepted what the tool wrote.
        using var health = await client.GetAsync("/health");
        using var signIn = await client.GetAsync("/development/sign-in");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact] // AC-6: every generated value is one the host actually reads.
    public void TheGeneratedSettingsAreExactlyTheApprovedSet()
    {
        var settings = Setup().Settings;

        Assert.Equal(
            new[]
            {
                "ConnectionStrings:TaxAiDatabase",
                "TaxAi:Extraction:ContentReader",
                "TaxAi:Extraction:MaximumSourceBytes",
                "TaxAi:Extraction:Provider",
                "TaxAi:Extraction:ProviderCallLimit",
                "TaxAi:Extraction:ProviderLabel",
                "TaxAi:Intake:AnswerPermitLimit",
                "TaxAi:Intake:AnswerWindow",
                "TaxAi:Intake:ClientSessionIdleLifetime",
                "TaxAi:Intake:ContentEncryptionKey",
                "TaxAi:Intake:DevelopmentContentRoot",
                "TaxAi:Intake:DigestKey",
                "TaxAi:Intake:FingerprintKey",
                "TaxAi:Intake:LinkLifetime",
                "TaxAi:Intake:MaximumFileBytes",
                "TaxAi:Intake:MaximumFilesPerRequest",
                "TaxAi:Intake:PublicOrigin",
                "TaxAi:Intake:PublicPermitLimit",
                "TaxAi:Intake:PublicWindow",
                "TaxAi:Intake:SessionPermitLimit",
                "TaxAi:Intake:SessionWindow",
                "TaxAi:Intake:SubmissionPermitLimit",
                "TaxAi:Intake:SubmissionWindow",
                "TaxAi:Intake:UploadConcurrencyLimit",
                "TaxAi:Intake:UploadPermitLimit",
                "TaxAi:Intake:UploadWindow",
                "TaxAi:Oidc:Authority",
                "TaxAi:Oidc:ClientId",
            },
            settings.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // Loopback, HTTPS, and the local deterministic provider only. No
        // production profile key and no data-protection override is written.
        Assert.Equal("https://localhost:7199/", settings["TaxAi:Intake:PublicOrigin"]);
        Assert.Equal("Deterministic", settings["TaxAi:Extraction:Provider"]);
        Assert.EndsWith(".invalid/", settings["TaxAi:Oidc:Authority"], StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(ContentRoot), settings[ContentRootKey]);
        Assert.True(Directory.Exists(settings[ContentRootKey]));

        var connection = settings[ConnectionStringKey];
        Assert.Contains(@"Server=(localdb)\", connection, StringComparison.Ordinal);
        Assert.Contains("Encrypt=True", connection, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", connection, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------- AC-7 and AC-8

    [Fact] // AC-7
    public void ASecondRunKeepsTheKeysAndTheContentRoot()
    {
        var first = Setup().Settings;
        var second = Setup().Settings;

        foreach (var name in GeneratedKeys.Append(ContentRootKey).Append(ConnectionStringKey))
        {
            Assert.Equal(first[name], second[name]);
        }
    }

    [Fact] // AC-7: regeneration is explicit.
    public void OnlyAnExplicitResetRegeneratesTheKeys()
    {
        var first = Setup().Settings;
        File.WriteAllText(Path.Combine(first[ContentRootKey], "stored-object.bin"), "x");

        var reset = Setup("-Reset", "-Force").Settings;

        foreach (var name in GeneratedKeys)
        {
            Assert.NotEqual(first[name], reset[name]);
        }

        // The content the old keys protected went with them.
        Assert.Empty(Directory.EnumerateFileSystemEntries(reset[ContentRootKey]));
    }

    [Fact] // AC-8
    public void TheThreeGeneratedKeysAreDistinctCanonicalThirtyTwoByteValues()
    {
        var settings = Setup().Settings;
        var keys = GeneratedKeys.Select(name => settings[name]).ToArray();

        Assert.Equal(3, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key =>
        {
            Assert.Matches("^[A-Za-z0-9_-]{43}$", key);
            Assert.Equal(
                32,
                Convert.FromBase64String(key.Replace('-', '+').Replace('_', '/') + "=").Length);
        });
    }

    // ------------------------------------------------------- AC-9 and AC-10

    [Fact] // AC-9
    public void SetupLeavesNothingNewInTheWorkingTree()
    {
        var before = Git("status", "--porcelain", "--untracked-files=all");

        _ = Setup();

        Assert.Equal(before, Git("status", "--porcelain", "--untracked-files=all"));

        // The tool's default locations inside the repository are ignored, and
        // its default settings store is outside the repository altogether.
        Assert.Contains(
            "artifacts/local-demo/content",
            Git("check-ignore", "artifacts/local-demo/content/object.bin"),
            StringComparison.Ordinal);
        var script = File.ReadAllText(ScriptPath);
        Assert.Contains("Join-Path $env:APPDATA 'Microsoft' 'UserSecrets'", script, StringComparison.Ordinal);
        Assert.Contains("Join-Path $HOME '.microsoft' 'usersecrets'", script, StringComparison.Ordinal);
    }

    [Fact] // AC-10
    public void TheToolPrintsNoKeyNoConnectionValueAndNoNineDigitRun()
    {
        var (settings, output) = Setup();

        foreach (var name in GeneratedKeys.Append(ConnectionStringKey).Append(ContentRootKey))
        {
            Assert.DoesNotContain(settings[name], output, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("(localdb)", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Integrated Security", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"\d{9}", output);

        // It does say where to go next.
        Assert.Contains("https://localhost:7199/development/sign-in", output, StringComparison.Ordinal);
    }

    [Fact] // AC-10: the tracked tool itself holds nothing sensitive.
    public void TheTrackedToolHoldsNoKeyCredentialOrNineDigitLiteral()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.DoesNotMatch(@"\d{9}", script);
        Assert.DoesNotMatch("['\"][A-Za-z0-9_-]{43}['\"]", script);
        foreach (var forbidden in new[] { "Password=", "Pwd=", "User Id=", "ClientSecret", "AccountKey" })
        {
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        }

        // Keys come from the operating system's random source at run time.
        Assert.Contains("RandomNumberGenerator]::GetBytes(32)", script, StringComparison.Ordinal);
    }

    // ----------------------------------------------------- AC-12 and AC-13

    [Fact] // AC-12
    public void EveryTrackedLaunchAddressIsLoopbackAndOneIsHttps()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "TaxAI.Portal", "Properties", "launchSettings.json")));
        var addresses = document.RootElement
            .GetProperty("profiles")
            .EnumerateObject()
            .SelectMany(profile => profile.Value.GetProperty("applicationUrl").GetString()!.Split(';'))
            .Select(address => new Uri(address))
            .ToArray();

        Assert.All(addresses, address => Assert.True(address.IsLoopback));
        Assert.Contains(addresses, address => address.Scheme == Uri.UriSchemeHttps);

        // The address the tool prints and the intake origin it generates are
        // the HTTPS launch address.
        Assert.Contains(addresses, address => address.AbsoluteUri == "https://localhost:7199/");
    }

    [Theory] // AC-13
    [InlineData("TaxAi:Oidc:Authority")]
    [InlineData("TaxAi:Oidc:ClientId")]
    public void TheIdentityProviderRequirementIsNotRelaxedInDevelopment(string missing)
    {
        var settings = Setup().Settings;
        settings[missing] = string.Empty;

        using var host = CreateHost("Development", settings);
        var refusal = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(
            "An OpenID Connect authority and client identifier are required in every environment.",
            Flatten(refusal),
            StringComparison.Ordinal);
    }

    // ----------------------------------------------------- AC-29 and AC-30

    [Theory] // AC-29, AC-30
    [InlineData("Production")]
    [InlineData("Staging")]
    public void TheLocalConnectionShapeIsRefusedOutsideDevelopment(string environment)
    {
        var settings = Setup().Settings;

        using var host = CreateHost(environment, settings);
        var refusal = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(
            "The database connection string must require encrypted transport and must not disable server certificate validation.",
            Flatten(refusal),
            StringComparison.Ordinal);
        Assert.DoesNotContain("localdb", Flatten(refusal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // Decision recorded 2026-10-01: no test host reads a developer's secret store.
    public void ThePortalProjectDeclaresNoUserSecretsIdentifier()
    {
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "TaxAI.Portal", "TaxAI.Portal.csproj"));

        Assert.DoesNotContain("UserSecretsId", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserSecrets", project, StringComparison.OrdinalIgnoreCase);

        // The settings reach the launched host as its own environment only.
        var script = File.ReadAllText(ScriptPath);
        Assert.Contains("Set-Item -LiteralPath \"Env:$variable\"", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath \"Env:$variable\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("user-secrets set", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // FR-2: migration is the tool's explicit act, through the pinned local tool.
    public void MigrationsAreAppliedByTheToolAndNeverByTheApplication()
    {
        var script = File.ReadAllText(ScriptPath);

        Assert.Contains("dotnet tool restore", script, StringComparison.Ordinal);
        Assert.Contains(
            "dotnet ef database update --project $persistenceProject -- --development-loopback",
            script,
            StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- helpers

    private (Dictionary<string, string> Settings, string Output) Setup(params string[] extra)
    {
        var arguments = new[]
            {
                "-NoProfile", "-NonInteractive", "-File", ScriptPath,
                "-SetupOnly", "-SkipDatabase",
                "-SettingsPath", SettingsPath,
                "-ContentRoot", ContentRoot,
            }
            .Concat(extra)
            .ToArray();

        var (exitCode, output) = Run("pwsh", arguments);

        Assert.True(exitCode == 0, $"The local setup tool failed ({exitCode}).");

        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(SettingsPath))!;

        return (settings, output);
    }

    private static WebApplicationFactory<Program> CreateHost(
        string environment,
        IReadOnlyDictionary<string, string> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);

            // Added last, so the generated settings replace the ones this test
            // assembly supplies to every other host through its environment.
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    settings.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))));
            foreach (var (name, value) in settings)
            {
                builder.UseSetting(name, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddApprovedDurableDataProtection();

                // Seeding would open the database; nothing else here does.
                services.AddScoped<IDevelopmentFixtureSeeder>(
                    _ => new PortalAuthorizationHarness.SilentSeeder());
            });
        });

    private static string Git(params string[] arguments)
    {
        var (exitCode, output) = Run("git", arguments);

        Assert.True(exitCode is 0 or 1, $"git {arguments[0]} failed ({exitCode}).");

        return output;
    }

    private static (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(3)), $"{fileName} did not finish.");

        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string Flatten(Exception exception)
    {
        var text = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            text.AppendLine(current.Message);
        }

        return text.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }
}
