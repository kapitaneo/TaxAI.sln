using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClientIntake.Contracts;

namespace ClientIntake.Tests.Boundaries;

/// <summary>
/// Spec 014 T01 — the two things prose cannot enforce: that the dependency arrows
/// never reverse, and that this module stays deterministic, provider-independent
/// application logic.
/// <para>
/// Every source scan reads <b>executable lines only</b>. This module's
/// documentation legitimately names nearly every forbidden concept while
/// explaining why it is forbidden, so scanning raw text would make honest
/// comments unwritable. The filter guard at the bottom proves the filter removes
/// documentation without removing code.
/// </para>
/// <para>
/// Solution assertions here are presence-and-uniqueness, never a total project
/// count. Pinning the solution to a closed project set is what blocked spec 007's
/// Wave 0 outright and required PR #66 to unpick, and the root CLAUDE.md forbids
/// it.
/// </para>
/// </summary>
public sealed class ClientIntakeBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ModuleRoot =
        Path.Combine(RepositoryRoot, "src", "ClientIntake");

    private static readonly string ModuleProject =
        Path.Combine(ModuleRoot, "ClientIntake.csproj");

    private static readonly string TestProject = Path.Combine(
        RepositoryRoot, "tests", "ClientIntake.Tests", "ClientIntake.Tests.csproj");

    /// <summary>
    /// The module's complete production source set. Named rather than counted so
    /// the scanner guard can prove the walk saw each one.
    /// </summary>
    private static readonly string[] ExpectedProductionFiles =
    {
        "AccountantIntakeWorkflow.cs",
        "ClientIntakeWorkflow.cs",
        "IAccountantIntakeWorkflow.cs",
        "IClientIntakeWorkflow.cs",
        "IIntakeContentStore.cs",
        "IIntakeCredentialSource.cs",
        "IIntakeStore.cs",
        "IntakeCaseFactory.cs",
        "IntakeCommands.cs",
        "IntakeDisplayNameGuard.cs",
        "IntakeEnums.cs",
        "IntakeProjection.cs",
        "IntakeQuestionnairePolicy.cs",
        "IntakeStateReplayer.cs",
        "IntakeStorageRecords.cs",
        "IntakeUploadValidator.cs",
        "IntakeViews.cs",
        "IntakeWorkflowOptionsGuard.cs",
        // Spec 016 T04: the source registry contract and its closed models.
        "SourceRegistryContracts.cs",
        "SourceRegistryModels.cs",
    };

    // ------------------------------------------------- the approved dependencies

    [Fact]
    public void TheModuleProjectDeclaresTheApprovedSettings()
    {
        Assert.True(File.Exists(ModuleProject), $"{ModuleProject} must exist.");

        Assert.Equal(
            "Microsoft.NET.Sdk",
            XDocument.Load(ModuleProject).Root!.Attribute("Sdk")!.Value);

        foreach (var (property, expected) in new[]
                 {
                     ("TargetFramework", "net10.0"),
                     ("Nullable", "enable"),
                     ("ImplicitUsings", "enable"),
                 })
        {
            Assert.Equal(
                new[] { expected },
                Elements(ModuleProject, property).Select(element => element.Value).ToArray());
        }

        Assert.Empty(Elements(ModuleProject, "TargetFrameworks"));
    }

    [Fact]
    public void TheModuleReferencesExactlyTheThreeInwardModules()
    {
        Assert.Equal(
            new[]
            {
                "../TaxCases/TaxCases.csproj",
                "../TaxLogicEngine/TaxLogicEngine.csproj",
                "../Tenancy/Tenancy.csproj",
            },
            ProjectReferences(ModuleProject));
    }

    [Theory]
    [InlineData("Persistence")]
    [InlineData("TaxAI.Portal")]
    [InlineData("CaseReview")]
    [InlineData("CaseProcessing")]
    [InlineData("DocumentExtraction")]
    public void TheModuleReferencesNoInfrastructureHostOrUnrelatedModule(string forbidden)
    {
        // Reads the declared references, not the project file's raw text: the
        // comment beside the reference list names every forbidden module while
        // explaining why it is absent, and a raw-text scan would make that comment
        // unwritable.
        Assert.DoesNotContain(
            ProjectReferences(ModuleProject),
            reference => reference.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheModuleDeclaresNoPackageOrFrameworkReference()
    {
        // Deterministic, provider-independent application logic. A package
        // reference here is how a module acquires a web, cloud, or ORM dependency
        // by accident.
        var project = XDocument.Load(ModuleProject);

        foreach (var forbidden in new[] { "PackageReference", "FrameworkReference" })
        {
            Assert.DoesNotContain(
                project.Descendants(),
                element => element.Name.LocalName == forbidden);
        }
    }

    [Fact]
    public void NeitherProjectDeclaresAnInternalsVisibleToGrant()
    {
        // The repository's three grants are pre-existing and stay that way. The
        // contract is exercised through its public surface instead.
        Assert.DoesNotContain(
            "InternalsVisibleTo",
            File.ReadAllText(ModuleProject),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "InternalsVisibleTo",
            File.ReadAllText(TestProject),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheTestProjectUsesTheApprovedMatrixAndReferencesOnlyTheModule()
    {
        Assert.Equal(
            new[] { "net10.0" },
            Elements(TestProject, "TargetFramework").Select(element => element.Value).ToArray());

        Assert.Equal(
            new[]
            {
                ("Microsoft.NET.Test.Sdk", "18.8.1"),
                ("coverlet.collector", "10.0.1"),
                ("xunit", "2.9.3"),
                ("xunit.runner.visualstudio", "3.1.5"),
            }.OrderBy(package => package.Item1, StringComparer.Ordinal).ToArray(),
            Elements(TestProject, "PackageReference")
                .Select(element => (
                    element.Attribute("Include")!.Value,
                    element.Attribute("Version")!.Value))
                .OrderBy(package => package.Item1, StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(
            new[] { "../../src/ClientIntake/ClientIntake.csproj" },
            ProjectReferences(TestProject));
    }

    [Fact]
    public void TheCompiledModuleNamesOnlyApprovedNonFrameworkAssemblies()
    {
        // The project file states intent; the assembly states what actually
        // compiled in. A subset assertion, because a ProjectReference nothing
        // names does not appear in the reference list at all.
        var referenced = typeof(IIntakeStore).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.All(
            referenced,
            name => Assert.Contains(
                name,
                new[] { "TaxCases", "TaxLogicEngine", "Tenancy" }));
    }

    // --------------------------------------------- solution and gate registration

    [Theory]
    [InlineData("\"ClientIntake\", \"src\\ClientIntake\\ClientIntake.csproj\"")]
    [InlineData("\"ClientIntake.Tests\", \"tests\\ClientIntake.Tests\\ClientIntake.Tests.csproj\"")]
    public void TheSolutionDeclaresTheProjectExactlyOnceAtItsApprovedPath(string declaration)
    {
        var projectLines = File.ReadAllLines(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(projectLines);

        // Presence and uniqueness only. The solution must stay free to declare
        // later modules' projects without this test failing.
        Assert.Equal(
            1,
            projectLines.Count(line => line.Contains(declaration, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheTestProjectIsRegisteredInTheFullSuiteGate()
    {
        // A test project nobody registered is a project nobody would notice
        // failing to run.
        var manifest = File.ReadAllText(
            Path.Combine(RepositoryRoot, "scripts", "test-projects.json"));

        Assert.Contains("\"ClientIntake.Tests\"", manifest, StringComparison.Ordinal);
    }

    // ------------------------------- the line this module may not cross

    public static TheoryData<string, string> ForbiddenProductionPatterns() => new()
    {
        // No tax computation, and no arithmetic that could become one.
        { "engine parser or mapper", @"\bT4SlipParser\b|\bT5SlipParser\b|\bT4LineMapper\b|\bT5LineMapper\b|\bT4CaseProcessor\b" },
        { "rounding / money math", @"\bMoneyRounding\b|\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b|\bdecimal\b" },

        // No taxpayer-data or extraction type enters this module.
        { "taxpayer data type", @"\bT4RawInput\b|\bT5RawInput\b|\bTaxLineEntry\b|\bRecipientIdentifier\b|\bValidatedExtraction\b|\bExtractionEnvelope\b|\bProcessedT4Extraction\b|\bAccountantReviewPackage\b" },

        // No infrastructure. The ports are declared here and implemented elsewhere.
        { "ORM or database", @"\bDbContext\b|\bDbSet\b|EntityFrameworkCore|\bSqlConnection\b|\bIQueryable\b" },
        { "web or hosting", @"Microsoft\.AspNetCore|\bHttpContext\b|\bIActionResult\b|\bIResult\b|\bControllerBase\b" },
        { "cloud, mail, or logging provider", @"\bAzure\b|\bSerilog\b|OpenTelemetry|ApplicationInsights|\bSmtpClient\b|\bILogger\b" },

        // No second mapping authority and no mapping-resource read.
        { "mapping table or resource read", @"TaxLogicEngine\.Mappings|\bGetManifestResourceStream\b|\bT4LineMap\b|\bT5LineMap\b" },

        // No tax-year policy of its own.
        { "tax-year policy", @"\bSupportedProcessingTaxYears\b|\bSupportedReviewTaxYears\b|\bSupportedTaxYears\b" },

        // PII: the module names no SIN concept in any form.
        { "SIN concept", @"\bSin\b|\bSIN\b|SocialInsurance|\bMask(?:ed|ing)?\b|\bRedact" },

        // Non-determinism the workflow must receive as an argument instead.
        { "ambient clock or randomness", @"DateTimeOffset\s*\.\s*(?:Now|UtcNow)|DateTime\s*\.\s*(?:Now|UtcNow)|Guid\s*\.\s*NewGuid\s*\(" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSourceContainsNoForbiddenToken(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Select(path => new
            {
                Name = Path.GetFileName(path),
                Text = string.Join("\n", ExecutableLines(path)),
            })
            .Where(source => regex.IsMatch(source.Text))
            .Select(source => source.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Forbidden {boundary} pattern /{pattern}/ found in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void NoCraLineNumberLiteralAppearsInTheModule()
    {
        // CRA T1 line numbers are five digits. None may be hardcoded here — every
        // line number in the system originates from TaxLogicEngine's embedded JSON
        // config, reached only through a provider.
        var craLineNumber = new Regex(@"\b\d{5}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => craLineNumber.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} contains a possible CRA line number: {string.Join(" | ", offending)}");
        }
    }

    [Fact]
    public void NoTaxYearLiteralAppearsInTheModule()
    {
        // Year isolation belongs to the modules that own it. A year literal here
        // would be a second policy, and the two would drift.
        var year = new Regex(@"\b(?:19|20)\d{2}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => year.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} names a tax year: {string.Join(" | ", offending)}");
        }
    }

    [Fact]
    public void TheModuleImplementsNoneOfItsOwnPorts()
    {
        // T01 declares ports and implements none of them. An implementation that
        // appeared here would be infrastructure inside the domain module.
        var implementations = typeof(IIntakeStore).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type =>
                typeof(IIntakeStore).IsAssignableFrom(type) ||
                typeof(IIntakeContentStore).IsAssignableFrom(type) ||
                typeof(IIntakeCredentialSource).IsAssignableFrom(type))
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(implementations);
    }

    // -------------------------------------------------------------- guards

    [Fact]
    public void TheScannerSeesEveryProductionFileAndStillMatchesAKnownPattern()
    {
        // Without this, a broken walk or an over-eager comment filter would make
        // every negative assertion above pass for free.
        var files = ProductionSourceFiles();

        Assert.Equal(
            ExpectedProductionFiles,
            files.Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var allExecutable = string.Join("\n", files.SelectMany(ExecutableLines));

        Assert.True(
            allExecutable.Length > 500,
            $"Implausibly little executable source: {allExecutable.Length} chars.");

        foreach (var mustMatch in new[]
                 {
                     @"\bIIntakeStore\b",
                     @"\bTrustedFirmId\b",
                     @"\bTaxCase\s*\.\s*Create\b",
                     @"\bJsonIgnore\b",
                     @"\bFixedTimeEquals\b",
                 })
        {
            Assert.Matches(new Regex(mustMatch), allExecutable);
        }
    }

    [Fact]
    public void TheCommentFilterRemovesDocumentationWithoutRemovingCode()
    {
        // The filter is what makes "executable lines only" safe. Prove both
        // directions on a file that deliberately documents forbidden concepts.
        var contentStore = Path.Combine(ModuleRoot, "Contracts", "IIntakeContentStore.cs");
        var raw = File.ReadAllText(contentStore);
        var executable = string.Join("\n", ExecutableLines(contentStore));

        // The documentation names a forbidden concept while explaining it...
        Assert.Contains("SIN", raw, StringComparison.Ordinal);
        // ...the filter removes it, so honest comments stay writable...
        Assert.DoesNotContain("SIN", executable, StringComparison.Ordinal);
        // ...while the code itself survives.
        Assert.Contains("StageAsync", executable, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static string[] ProductionSourceFiles() =>
        Directory.EnumerateFiles(ModuleRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static IEnumerable<XElement> Elements(string projectPath, string name) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == name);

    private static string[] ProjectReferences(string projectPath) =>
        Elements(projectPath, "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

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
}
