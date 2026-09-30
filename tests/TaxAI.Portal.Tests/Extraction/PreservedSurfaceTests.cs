using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Extraction;

public sealed class PreservedSurfaceTests
{
    private const string T05Merge = "736f10d";
    private const string T06Merge = "33ca918";

    private const string IntakeConfigurationPath = "src/TaxAI.Portal/IntakeConfiguration.cs";

    /// <summary>
    /// Spec 016 T01, human-authorized: the exact lines the production-shape
    /// change removed from the T05 version of IntakeConfiguration.cs.
    /// </summary>
    private static readonly string[] ApprovedSpec016IntakeRemovals =
    {
        "            !TryKey(",
    };

    /// <summary>
    /// SHA-256 of the exact lines spec 016 T01 added to IntakeConfiguration.cs,
    /// joined by LF. Any further addition or edit to that file changes this
    /// digest and fails until a new change is separately authorized.
    /// </summary>
    private const string ApprovedSpec016IntakeAdditionsSha256 =
        "b8824ceef756c896c5a1dc9fad2f5b5fa86be9dd86e553509c35b2f54d56a9dc";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task Spec012ReviewSurfaceIsByteIdenticalToTheT05Merge()
    {
        await AssertUnchangedFromT05Async(
            "src/TaxAI.Portal/ReviewEndpoints.cs",
            "src/TaxAI.Portal/Components/Pages/ReviewPage.razor",
            "src/TaxAI.Portal/Components/Pages/ReviewPage.razor.css",
            "src/TaxAI.Portal/Components/Pages/ReviewPageModel.cs");
    }

    [Fact]
    public async Task ExistingIntakeTransportPoliciesCookieAndComponentsAreByteIdentical()
    {
        await AssertUnchangedFromT05Async(
            "src/TaxAI.Portal/ClientIntakeCookie.cs",
            "src/TaxAI.Portal/ClientIntakeEndpoints.cs",
            // IntakeConfiguration.cs is no longer byte-identical: spec 016 T01
            // made the human-authorized production-shape change. It is still
            // guarded, line-exactly, by the two spec 016 tests below.
            "src/TaxAI.Portal/IntakeEndpoints.cs",
            "src/TaxAI.Portal/IntakeHttpSecurity.cs",
            "src/TaxAI.Portal/Components/Pages/ClientIntakePage.razor",
            "src/TaxAI.Portal/Components/Pages/ClientLinkConfirmationPage.razor",
            "src/TaxAI.Portal/Components/Pages/IntakeCaseCreatePage.razor",
            "src/TaxAI.Portal/Components/Pages/IntakeCaseIssuedPage.razor",
            "src/TaxAI.Portal/Components/Pages/IntakeCaseProgressPage.razor",
            "src/TaxAI.Portal/Components/Shared/ClientQuestionnairePanel.razor",
            "src/TaxAI.Portal/Components/Shared/ClientQuestionnairePanel.razor.js",
            "src/TaxAI.Portal/Components/Shared/IntakeStatusBadge.razor",
            "src/TaxAI.Portal/wwwroot/intake.css");
    }

    /// <summary>
    /// Spec 016 T01 (human-authorized narrow exception). IntakeConfiguration.cs
    /// may differ from the T05 merge only by the approved production-shape
    /// change: exactly one removed line and exactly the approved added lines.
    /// Anything else, including any later edit, fails here.
    /// </summary>
    [Fact]
    public async Task IntakeConfigurationDiffersFromTheT05MergeOnlyByTheApprovedSpec016Change()
    {
        var (removed, added) = await DiffFromT05Async(IntakeConfigurationPath);

        Assert.Equal(ApprovedSpec016IntakeRemovals, removed);
        Assert.Equal(
            ApprovedSpec016IntakeAdditionsSha256,
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", added)))));

        // The additions carry no source, logging, network, SIN or bypass token.
        var addedText = string.Join("\n", added);
        foreach (var forbidden in new[]
                 {
                     "File.", "Directory.", "Stream", "ILogger", "Console.", "HttpClient",
                     "GetEnvironmentVariable", "Sin", "SocialInsurance", "Mask",
                     "Bypass", "Skip", "Disable", "IExtractionContentReader",
                     "IIntakeContentStore", "RetentionPolicy", "TrustedFirmId",
                 })
        {
            Assert.DoesNotContain(forbidden, addedText, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Spec 016 T01: the approved production configuration surface, asserted
    /// separately from the diff. Every Spec 014 setting, bound, key rule and
    /// the Development content-root rule are still present; the only additions
    /// are the production-shape refusal and the Development-only content key.
    /// </summary>
    [Fact]
    public void IntakeConfigurationExposesExactlyTheApprovedSpec016ProductionSurface()
    {
        var source = string.Join(
            "\n",
            File.ReadAllLines(Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "IntakeConfiguration.cs")));
        var type = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.IntakeConfiguration",
            throwOnError: true)!;

        var methods = type
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Encode",
                "IsWithin",
                "ProductionShapeRefusals",
                "Require",
                "RequireDevelopmentRoot",
                "TryContentEncryptionKey",
                "TryKey",
                "TryOrigin",
                "TryPositiveDuration",
                "TryPositiveInt",
                "TryPositiveLong",
                "TryRates",
            },
            methods);

        var shape = type.GetMethod(
            "ProductionShapeRefusals",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.True(shape.IsAssembly);
        Assert.Equal(
            typeof(IReadOnlyList<Tenancy.Models.ProductionAdmissionRefusal>),
            shape.ReturnType);

        // Every Spec 014 bound is still read and enforced exactly as before.
        foreach (var preserved in new[]
                 {
                     "!TryOrigin(configuration[PublicOriginSetting], out var publicOrigin) ||",
                     "!TryKey(configuration[DigestKeySetting], out var digestKey) ||",
                     "!TryKey(configuration[FingerprintKeySetting], out var fingerprintKey) ||",
                     "digestKey.AsSpan().SequenceEqual(fingerprintKey) ||",
                     "digestKey.AsSpan().SequenceEqual(contentEncryptionKey) ||",
                     "fingerprintKey.AsSpan().SequenceEqual(contentEncryptionKey) ||",
                     "!TryRates(configuration, out var rates))",
                     "contentRoot = RequireDevelopmentRoot(",
                     "IsWithin(root, environment.WebRootPath))",
                     "\"Client intake configuration is missing or invalid.\";",
                 })
        {
            Assert.Contains(preserved, source, StringComparison.Ordinal);
        }

        // The content key stays mandatory in Development, and the only refusal
        // the production shape can produce is the local-store refusal.
        Assert.Contains("Environments.Development,", source, StringComparison.Ordinal);
        Assert.Contains("return TryKey(candidate, out key);", source, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(source, @"ProductionAdmissionRefusal\.\w+"));
        Assert.Contains(
            "ProductionAdmissionRefusal.LocalSourceStoreConfigured",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractionSurfaceCannotReturnOrNameSourceContent()
    {
        var source = PortalFile("ExtractionEndpoints.cs");
        var constants = typeof(Program).Assembly
            .GetType("TaxAI.Portal.ExtractionEndpoints", throwOnError: true)!
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Path", StringComparison.Ordinal))
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .ToArray();

        Assert.Equal(8, constants.Length);
        foreach (var path in constants)
        {
            Assert.DoesNotContain("preview", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("download", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("print", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("email", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("export", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("content", path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("source", path, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("Results.File", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Results.Bytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Results.Stream", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalFile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStreamResult", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IExtractionContentReader", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReaderIsNotResolvableAsAGeneralOrBackgroundService()
    {
        var registration = PortalFile("ModuleRegistration.cs");
        var allPortalCode = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "TaxAI.Portal"),
                "*",
                SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(
            "AddScoped<IExtractionContentReader",
            registration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddSingleton<IExtractionContentReader",
            registration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddTransient<IExtractionContentReader",
            registration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            allPortalCode,
            source => source.Contains("IHostedService", StringComparison.Ordinal));
        Assert.DoesNotContain(
            allPortalCode,
            source => source.Contains("BackgroundService", StringComparison.Ordinal));
        Assert.DoesNotContain(
            allPortalCode,
            source => source.Contains("PeriodicTimer", StringComparison.Ordinal));
        Assert.DoesNotContain(
            allPortalCode,
            source => source.Contains("System.Threading.Channels", StringComparison.Ordinal));
        Assert.DoesNotContain(
            allPortalCode,
            source => source.Contains("Channel<", StringComparison.Ordinal));
        Assert.DoesNotContain(
            allPortalCode,
            source => source.Contains("IExtractionContentReader", StringComparison.Ordinal) &&
                      !source.Contains(
                          "DevelopmentEncryptedIntakeContentStore",
                          StringComparison.Ordinal));
    }

    [Fact]
    public void StaticSsrAndStaticAssetPostureRemainClosed()
    {
        var portalRoot = Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");
        var program = File.ReadAllText(Path.Combine(portalRoot, "Program.cs"));
        var extractionComponents = Directory
            .EnumerateFiles(
                Path.Combine(portalRoot, "Components"),
                "Extraction*.razor",
                SearchOption.AllDirectories)
            .Concat(
                new[]
                {
                    Path.Combine(
                        portalRoot,
                        "Components",
                        "Shared",
                        "MedicalEvidencePanel.razor"),
                    Path.Combine(
                        portalRoot,
                        "Components",
                        "Shared",
                        "DuplicateCandidateNotice.razor"),
                    Path.Combine(
                        portalRoot,
                        "Components",
                        "Shared",
                        "ClassificationDisagreementNotice.razor"),
                })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Equal(1, Count(program, "app.UseStaticFiles();"));
        Assert.DoesNotContain(
            "MapStaticAssets(",
            ExecutableLines(program),
            StringComparison.Ordinal);
        Assert.All(
            extractionComponents,
            source =>
            {
                Assert.DoesNotContain("@page ", source, StringComparison.Ordinal);
                Assert.DoesNotContain("@rendermode ", source, StringComparison.Ordinal);
            });

        var app = File.ReadAllText(
            Path.Combine(portalRoot, "Components", "App.razor"));
        var routes = File.ReadAllText(
            Path.Combine(portalRoot, "Components", "Routes.razor"));
        Assert.DoesNotContain("@rendermode ", ExecutableLines(app), StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode ", ExecutableLines(routes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task T06DidNotIntroduceT07VerificationArtifacts()
    {
        // This is a historical wave-ownership assertion. Inspect T06's tree,
        // so the required T07 artifacts can exist in later working trees.
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in new[]
                 {
                     "ls-tree", "-r", "--name-only", T06Merge, "--",
                     "specs/015-t4-and-medical-document-extraction/verification.md",
                     "tests",
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await standardError);

        var paths = (await standardOutput).Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("tests/TaxAI.Portal.Tests/Extraction/ExtractionRouteTests.cs", paths);
        Assert.DoesNotContain(
            "specs/015-t4-and-medical-document-extraction/verification.md",
            paths);
        Assert.DoesNotContain(
            paths,
            path => Path.GetFileName(path).Contains(
                "CrossModule",
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AssertUnchangedFromT05Async(params string[] paths)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("diff");
        process.StartInfo.ArgumentList.Add("--exit-code");
        process.StartInfo.ArgumentList.Add("--no-ext-diff");
        process.StartInfo.ArgumentList.Add(T05Merge);
        process.StartInfo.ArgumentList.Add("--");
        foreach (var path in paths)
        {
            process.StartInfo.ArgumentList.Add(path);
        }

        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"Preserved surface differs from {T05Merge}.{Environment.NewLine}" +
            await standardOutput +
            await standardError);
    }

    private static async Task<(string[] Removed, string[] Added)> DiffFromT05Async(string path)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in new[] { "diff", "-U0", "--no-ext-diff", "--no-color", T05Merge, "--", path })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await standardError);

        var lines = (await standardOutput).Split('\n');
        return (
            lines.Where(line => line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
                .Select(line => line[1..].TrimEnd('\r'))
                .ToArray(),
            lines.Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
                .Select(line => line[1..].TrimEnd('\r'))
                .ToArray());
    }

    private static string PortalFile(string name) =>
        File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", name));

    private static string ExecutableLines(string source) =>
        Regex.Replace(
            source,
            @"@\*.*?\*@|<!--.*?-->|//.*?$|/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline |
            RegexOptions.Multiline |
            RegexOptions.CultureInvariant);

    private static int Count(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
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
            throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
