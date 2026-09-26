using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.Extraction;

public sealed class PreservedSurfaceTests
{
    private const string T05Merge = "736f10d";
    private const string T06Merge = "33ca918";

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
            "src/TaxAI.Portal/IntakeConfiguration.cs",
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

    [Fact]
    public void ExtractionSurfaceCannotReturnOrNameSourceContent()
    {
        var source = PortalFile("ExtractionEndpoints.cs");
        var constants = typeof(Program).Assembly
            .GetType("TaxAI.Portal.ExtractionEndpoints", throwOnError: true)!
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
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
