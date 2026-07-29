using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// T06 — proves that adding a web host changed nothing about the five modules
/// that were already here: the solution's existing declarations survived the
/// append, every module's public surface and reference set is exactly what it
/// was, the embedded mapping resources are untouched, no new internals grant
/// appeared, and the portal took no dependency on spec 009.
/// <para>
/// Solution assertions are <b>presence-and-uniqueness</b>. No total project
/// count is asserted anywhere: pinning the solution to "exactly N projects"
/// blocked spec 007's Wave 0 outright and required PR #66 to unpick.
/// </para>
/// </summary>
public sealed class SolutionAndRegressionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// Every pre-existing solution declaration, with the GUID it had before this
    /// feature. Nothing here may change: the two new blocks were appended, not
    /// merged into the middle of the file.
    /// </summary>
    public static TheoryData<string, string, string> PreExistingDeclarations() => new()
    {
        { "TaxLogicEngine", @"src\TaxLogicEngine\TaxLogicEngine.csproj", "{0390423C-6E2F-426F-B33B-4E2ABB34A5C5}" },
        { "TaxLogicEngine.Tests", @"tests\TaxLogicEngine.Tests\TaxLogicEngine.Tests.csproj", "{2F692EB7-E9FA-4A1C-85E6-6A33110E0076}" },
        { "TaxCases", @"src\TaxCases\TaxCases.csproj", "{A5A7F602-8B8A-4B51-83F0-EF3A2F38B1C1}" },
        { "TaxCases.Tests", @"tests\TaxCases.Tests\TaxCases.Tests.csproj", "{C2D431A4-AB61-4F7E-93F6-BE970A2B4184}" },
        { "DocumentExtraction", @"src\DocumentExtraction\DocumentExtraction.csproj", "{63E522D5-4360-4E7F-B4B7-B896BD8AAEE4}" },
        { "DocumentExtraction.Tests", @"tests\DocumentExtraction.Tests\DocumentExtraction.Tests.csproj", "{CFBDCA0E-325E-4A78-A890-CED77163E24A}" },
        { "CaseProcessing", @"src\CaseProcessing\CaseProcessing.csproj", "{7B1C0A3E-9D4F-4C86-9E1B-2F5A6D3C8B70}" },
        { "CaseProcessing.Tests", @"tests\CaseProcessing.Tests\CaseProcessing.Tests.csproj", "{1D8E4F92-6A73-4B05-8C2D-9E7A1B4F60C3}" },
        { "CaseReview", @"src\CaseReview\CaseReview.csproj", "{C54E8270-7071-46BD-87C7-9561CED5A932}" },
        { "CaseReview.Tests", @"tests\CaseReview.Tests\CaseReview.Tests.csproj", "{93C57000-9BB3-4561-B79C-070824F626A0}" },
    };

    /// <summary>
    /// Each module's exact production reference set, as it stood before the
    /// portal existed.
    /// </summary>
    public static TheoryData<string, string[]> ModuleReferenceSets() => new()
    {
        { "TaxLogicEngine", Array.Empty<string>() },
        { "TaxCases", new[] { "../TaxLogicEngine/TaxLogicEngine.csproj" } },
        { "DocumentExtraction", new[] { "../TaxLogicEngine/TaxLogicEngine.csproj" } },
        { "CaseProcessing", new[] { "../DocumentExtraction/DocumentExtraction.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" } },
        { "CaseReview", new[] { "../CaseProcessing/CaseProcessing.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" } },
    };

    // ------------------------------------------------- AC-6: solution intact

    [Theory] // AC-6
    [MemberData(nameof(PreExistingDeclarations))]
    public void EveryPreExistingSolutionDeclarationSurvivedTheAppend(string name, string path, string guid)
    {
        var projectLines = SolutionProjectLines();

        // Presence-and-uniqueness, never a total count.
        Assert.Single(
            projectLines,
            line =>
                line.Contains($"\"{name}\", \"{path}\"", StringComparison.Ordinal) &&
                line.Contains(guid, StringComparison.Ordinal));
    }

    [Theory] // AC-6
    [MemberData(nameof(PreExistingDeclarations))]
    public void EveryPreExistingProjectKeptItsConfigurationRows(string name, string path, string guid)
    {
        _ = name;
        _ = path;

        var solution = File.ReadAllText(Path.Combine(RepositoryRoot, "TaxAI.sln"));

        // Six configurations, each with an ActiveCfg and a Build.0 row.
        foreach (var configuration in new[] { "Debug|Any CPU", "Debug|x64", "Debug|x86", "Release|Any CPU", "Release|x64", "Release|x86" })
        {
            Assert.Contains($"{guid}.{configuration}.ActiveCfg", solution, StringComparison.Ordinal);
            Assert.Contains($"{guid}.{configuration}.Build.0", solution, StringComparison.Ordinal);
        }
    }

    [Fact] // AC-6
    public void TheTwoNewProjectsAreDeclaredExactlyOnceEach()
    {
        var projectLines = SolutionProjectLines();

        foreach (var declaration in new[]
                 {
                     "\"TaxAI.Portal\", \"src\\TaxAI.Portal\\TaxAI.Portal.csproj\"",
                     "\"TaxAI.Portal.Tests\", \"tests\\TaxAI.Portal.Tests\\TaxAI.Portal.Tests.csproj\"",
                 })
        {
            Assert.Equal(
                1,
                projectLines.Count(line => line.Contains(declaration, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void EveryDeclaredSolutionProjectResolvesToAFileThatExists()
    {
        var declaration = new Regex(
            @"^Project\(""[^""]+""\)\s*=\s*""(?<name>[^""]+)"",\s*""(?<path>[^""]+\.csproj)""",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var declarations = SolutionProjectLines()
            .Select(line => declaration.Match(line))
            .Where(match => match.Success)
            .ToArray();

        Assert.NotEmpty(declarations);

        foreach (var match in declarations)
        {
            var path = Path.Combine(
                RepositoryRoot,
                match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar));

            Assert.True(
                File.Exists(path),
                $"{match.Groups["name"].Value} is declared at a missing path.");
        }
    }

    // -------------------------------------------- AC-12: no speculative project

    [Theory] // AC-12
    [InlineData("Clients")]
    [InlineData("Firms")]
    [InlineData("Documents")]
    [InlineData("Intake")]
    [InlineData("Review")]
    public void NoPlaceholderProjectExistsForAFutureModule(string module)
    {
        // CaseReview is deliberately NOT in this list: spec 009 shipped it, so
        // it is a real module, not a speculative shell. "Review" here means a
        // project literally named Review.csproj.
        var candidates = Directory
            .EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => Path.GetFileName(path).Equals($"{module}.csproj", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            candidates.Length == 0,
            $"A speculative {module} project exists: {string.Join(", ", candidates)}. "
            + "A real module's boundary is discovered from its contract; empty shells acquire gravity.");
    }

    // ------------------------------ AC-14: public surfaces are byte-for-byte as they were

    [Theory] // AC-14; spec 011 AC-25
    [MemberData(nameof(PublicSurfaceSnapshots))]
    public void TheModulePublicSurfaceIsUnchanged(string module, string[] expected)
    {
        // Recorded when the portal was added. If a later spec intentionally
        // changes one of these public surfaces, update the snapshot in that same
        // PR — a deliberate change to a public tax contract should be visible in
        // a diff, which is the entire point of pinning it here.
        var actual = LoadModule(module)
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    // -------------------------------- AC-15: embedded mapping resources untouched

    [Fact] // AC-15; spec 011 AC-26
    public void TheEmbeddedMappingResourcesKeepTheirExactLogicalNames()
    {
        // These strings are load-bearing: EmbeddedT4/T5LineMapProvider look the
        // configs up by exactly these names, and a rename would fail closed at
        // runtime rather than at build time.
        var resources = LoadModule("TaxLogicEngine").GetManifestResourceNames();

        Assert.Contains("TaxLogicEngine.Mappings.T4LineMap.2025.json", resources);
        Assert.Contains("TaxLogicEngine.Mappings.T5LineMap.2025.json", resources);
    }

    // --------------------------------------------- AC-16: no new internals grant

    [Fact] // AC-16
    public void TheRepositoryHasExactlyItsThreePreExistingInternalsGrants()
    {
        // Three grants, in TWO declaration forms. A scan that reads only
        // .csproj files misses the third and would report it as removed.
        var fromProjectFiles = ProductionProjectFiles()
            .Where(path => File.ReadAllText(path).Contains("InternalsVisibleTo", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "CaseProcessing", "CaseReview" }, fromProjectFiles);

        // Match the attribute, not the word. CaseProcessing's PayloadStructureGuard
        // documents the concept in a doc comment while granting nothing, and a
        // substring scan reports that as a grant.
        var grant = new Regex(
            @"\[\s*assembly\s*:\s*InternalsVisibleTo\s*\(",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var fromSource = ProductionSourceFiles()
            .Where(path => ExecutableLines(path).Any(line => grant.IsMatch(line)))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "src/TaxLogicEngine/Services/EmbeddedT4LineMapProvider.cs" },
            fromSource);
    }

    [Fact] // AC-16
    public void NeitherNewProjectDeclaresAnInternalsGrant()
    {
        foreach (var project in new[]
                 {
                     Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "TaxAI.Portal.csproj"),
                     Path.Combine(RepositoryRoot, "tests", "TaxAI.Portal.Tests", "TaxAI.Portal.Tests.csproj"),
                 })
        {
            Assert.DoesNotContain(
                "InternalsVisibleTo",
                File.ReadAllText(project),
                StringComparison.Ordinal);
        }

        var portalSource = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "TaxAI.Portal"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

        foreach (var file in portalSource)
        {
            Assert.DoesNotContain(
                "InternalsVisibleTo",
                File.ReadAllText(file),
                StringComparison.Ordinal);
        }
    }

    // ---------------------------------- module reference sets are as they were

    [Theory]
    [MemberData(nameof(ModuleReferenceSets))]
    public void TheModuleKeptItsExactReferenceSet(string module, string[] expected)
    {
        var project = Path.Combine(RepositoryRoot, "src", module, $"{module}.csproj");

        Assert.Equal(expected, ProjectReferences(project));
    }

    [Theory]
    [InlineData("TaxLogicEngine")]
    [InlineData("TaxCases")]
    [InlineData("DocumentExtraction")]
    [InlineData("CaseProcessing")]
    [InlineData("CaseReview")]
    public void TheModuleStillDeclaresNoPackageOrFrameworkReference(string module)
    {
        // Tax-domain code stays provider-independent. A package reference here
        // is how a module acquires a web, cloud, or ORM dependency by accident.
        var project = XDocument.Load(Path.Combine(RepositoryRoot, "src", module, $"{module}.csproj"));

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);

        foreach (var forbidden in new[] { "PackageReference", "FrameworkReference" })
        {
            Assert.DoesNotContain(
                project.Descendants(),
                element => element.Name.LocalName == forbidden);
        }
    }

    // ------------------------------------------ AC-50: no coupling to spec 009

    [Theory] // AC-50
    [InlineData("CaseReview")]
    [InlineData("AccountantReviewPackage")]
    [InlineData("ReviewedSlip")]
    [InlineData("ReviewSignOff")]
    [InlineData("ReviewPackageAssembler")]
    public void ThePortalNamesNoSpec009Type(string type)
    {
        // CaseReview exists and is merged — its existence is fine. Depending on
        // it is not: an AccountantReviewPackage and its reviewed slips are
        // taxpayer data, and rendering them is what the FR-18 gate stops. That
        // is the first screen spec's decision to make, not this one's.
        var portalProject = Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "TaxAI.Portal.csproj");

        Assert.DoesNotContain(type, File.ReadAllText(portalProject), StringComparison.Ordinal);

        foreach (var file in Directory
                     .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "TaxAI.Portal"), "*.cs", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(path)))
        {
            Assert.DoesNotContain(type, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact] // AC-50
    public void TheCompiledPortalDoesNotReferenceCaseReview()
    {
        Assert.DoesNotContain(
            typeof(Program).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.Contains("CaseReview", StringComparison.OrdinalIgnoreCase));
    }

    // ----------------------------------------- spec 011 AC-24: suite execution

    [Fact]
    public void EverySolutionTestProjectIsRegisteredInTheFullSuiteGate()
    {
        // This compares the test-project inventory only. It deliberately makes
        // no assertion about the solution's total project count.
        var solutionProjects = SolutionProjectLines()
            .Select(line => line.Split(',')[1].Trim().Trim('"'))
            .Where(path =>
                path.StartsWith(@"tests\", StringComparison.Ordinal) &&
                path.EndsWith(".Tests.csproj", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "test-projects.json")));
        var registered = manifest.RootElement
            .GetProperty("projects")
            .EnumerateArray()
            .Select(project => new
            {
                Name = project.GetProperty("name").GetString()!,
                Floor = project.GetProperty("minimumTests").GetInt32(),
            })
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(solutionProjects, registered.Select(project => project.Name));
        Assert.All(registered, project => Assert.True(
            project.Floor > 0,
            $"{project.Name} must have a positive execution floor."));
    }

    [Fact] // spec 011 AC-109
    public void NoNewModulePortalMigrationSeedOrFixtureHardCodesACraLineNumber()
    {
        var fiveDigitLiteral = new Regex(
            @"\b\d{5}\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var productionFiles = new[] { "Tenancy", "Persistence", "TaxAI.Portal" }
            .SelectMany(module => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", module),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path));
        var fixtureFiles = new[] { "Tenancy.Tests", "Persistence.Tests", "TaxAI.Portal.Tests" }
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "tests", project),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path =>
                path.Contains(
                    $"{Path.DirectorySeparatorChar}Support{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                Path.GetFileName(path).Contains("Fixture", StringComparison.Ordinal) ||
                Path.GetFileName(path).Contains("Seeder", StringComparison.Ordinal));
        var violations = productionFiles
            .Concat(fixtureFiles)
            .Distinct(StringComparer.Ordinal)
            .Select(path => new
            {
                Path = Path.GetRelativePath(RepositoryRoot, path),
                Lines = ExecutableLines(path).Where(line => fiveDigitLiteral.IsMatch(line)).ToArray(),
            })
            .Where(result => result.Lines.Length > 0)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Five-digit integer literals were found in the new production, migration, seed, or fixture surface: "
            + string.Join(
                " | ",
                violations.Select(result => $"{result.Path}: {string.Join(", ", result.Lines)}")));
    }

    // ---------------------------------------------------------------- guard

    [Fact]
    public void TheSnapshotsCoverEveryModuleAndAreNotEmpty()
    {
        // Without this, a snapshot silently reduced to an empty array — or a
        // module quietly dropped from the table — would make AC-14 pass while
        // checking nothing.
        var covered = PublicSurfaceSnapshots()
            .Select(row => (string)row[0])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "CaseProcessing", "CaseReview", "DocumentExtraction", "TaxCases", "TaxLogicEngine" },
            covered);

        foreach (var row in PublicSurfaceSnapshots())
        {
            var module = (string)row[0];
            var expected = (string[])row[1];

            Assert.NotEmpty(expected);
            Assert.All(expected, name => Assert.StartsWith($"{module}.", name, StringComparison.Ordinal));
        }
    }

    // --------------------------------------------------------------- snapshots

    public static TheoryData<string, string[]> PublicSurfaceSnapshots() => new()
    {
        {
            "TaxLogicEngine",
            new[]
            {
                "TaxLogicEngine.Models.BoxLineMapping",
                "TaxLogicEngine.Models.ParseResult`1",
                "TaxLogicEngine.Models.RecipientIdentifier",
                "TaxLogicEngine.Models.T4BoxCodes",
                "TaxLogicEngine.Models.T4BoxLineMapping",
                "TaxLogicEngine.Models.T4LineMap",
                "TaxLogicEngine.Models.T4LineMappingConfig",
                "TaxLogicEngine.Models.T4MappingResult",
                "TaxLogicEngine.Models.T4OtherInformation",
                "TaxLogicEngine.Models.T4OtherInformationCodes",
                "TaxLogicEngine.Models.T4RawBox",
                "TaxLogicEngine.Models.T4RawInput",
                "TaxLogicEngine.Models.T4Slip",
                "TaxLogicEngine.Models.T5BoxCodes",
                "TaxLogicEngine.Models.T5LineMap",
                "TaxLogicEngine.Models.T5LineMappingConfig",
                "TaxLogicEngine.Models.T5MappingResult",
                "TaxLogicEngine.Models.T5OtherInformation",
                "TaxLogicEngine.Models.T5RawBox",
                "TaxLogicEngine.Models.T5RawInput",
                "TaxLogicEngine.Models.T5Slip",
                "TaxLogicEngine.Models.TaxLineEntry",
                "TaxLogicEngine.Models.ValidationError",
                "TaxLogicEngine.Services.EmbeddedT4LineMapProvider",
                "TaxLogicEngine.Services.EmbeddedT5LineMapProvider",
                "TaxLogicEngine.Services.IT4LineMapProvider",
                "TaxLogicEngine.Services.IT5LineMapProvider",
                "TaxLogicEngine.Services.MoneyRounding",
                "TaxLogicEngine.Services.T4LineMapper",
                "TaxLogicEngine.Services.T4SlipBoxAmounts",
                "TaxLogicEngine.Services.T4SlipParser",
                "TaxLogicEngine.Services.T5LineMapper",
                "TaxLogicEngine.Services.T5SlipBoxAmounts",
                "TaxLogicEngine.Services.T5SlipParser",
            }
        },
        {
            "TaxCases",
            new[]
            {
                "TaxCases.Models.CaseChangeKind",
                "TaxCases.Models.CaseChangeRecord",
                "TaxCases.Models.CaseOperationOutcome",
                "TaxCases.Models.CaseParticipant",
                "TaxCases.Models.CaseStatus",
                "TaxCases.Models.ChecklistDescriptor",
                "TaxCases.Models.CreateTaxCaseRequest",
                "TaxCases.Models.DocumentCategory",
                "TaxCases.Models.EvidenceDescriptor",
                "TaxCases.Models.EvidenceRequirement",
                "TaxCases.Models.EvidenceRequirementDescriptor",
                "TaxCases.Models.IntakeAnswer",
                "TaxCases.Models.IntakeAnswerDescriptor",
                "TaxCases.Models.IntakeQuestion",
                "TaxCases.Models.IntakeQuestionScope",
                "TaxCases.Models.ParticipantDescriptor",
                "TaxCases.Models.ReadinessReason",
                "TaxCases.Models.ReadinessReasonKind",
                "TaxCases.Models.ReadinessState",
                "TaxCases.Models.ReceivedEvidence",
                "TaxCases.Models.RelationshipKind",
                "TaxCases.Models.RequirementChecklist",
                "TaxCases.Models.ReviewReadiness",
                "TaxCases.Models.TaxCase",
                "TaxCases.Services.CaseStatusTransitions",
                "TaxCases.Services.ReadinessCalculator",
                "TaxCases.Services.SupportedTaxYears",
            }
        },
        {
            "DocumentExtraction",
            new[]
            {
                "DocumentExtraction.Models.ExtractedDocumentKind",
                "DocumentExtraction.Models.ExtractionEnvelope",
                "DocumentExtraction.Models.ExtractionFields",
                "DocumentExtraction.Models.ExtractionProvenance",
                "DocumentExtraction.Models.ValidatedExtraction",
                "DocumentExtraction.Ports.IDocumentExtractionIngress",
                "DocumentExtraction.Services.ExtractionEnvelopeValidator",
                "DocumentExtraction.Services.SupportedExtractionTaxYears",
            }
        },
        {
            "CaseProcessing",
            new[]
            {
                "CaseProcessing.Models.ProcessedT4Extraction",
                "CaseProcessing.Models.T4ProcessingFields",
                "CaseProcessing.Services.SupportedProcessingTaxYears",
                "CaseProcessing.Services.T4CaseProcessor",
            }
        },
        {
            "CaseReview",
            new[]
            {
                "CaseReview.Models.AccountantReviewPackage",
                "CaseReview.Models.ReviewDecisionDescriptor",
                "CaseReview.Models.ReviewFields",
                "CaseReview.Models.ReviewSignOff",
                "CaseReview.Models.ReviewedBoxAmount",
                "CaseReview.Models.ReviewedSlip",
                "CaseReview.Models.SignOffDecision",
                "CaseReview.Services.ReviewPackageAssembler",
                "CaseReview.Services.SupportedReviewTaxYears",
            }
        },
    };

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Loads a module assembly by path. The portal test project references only
    /// the portal, and deliberately so — <c>TaxCases</c> and <c>CaseReview</c>
    /// are not on its reference graph and must not be added to it just to be
    /// inspected.
    /// </summary>
    private static Assembly LoadModule(string module)
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", module, "bin");

        var candidate = Directory.Exists(moduleRoot)
            ? Directory
                .EnumerateFiles(moduleRoot, $"{module}.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        Assert.True(
            candidate is not null,
            $"{module}.dll was not found under {moduleRoot}. Run `dotnet build TaxAI.sln` first.");

        var directory = Path.GetDirectoryName(candidate)!;
        var context = new AssemblyLoadContext($"{module}-surface-{Guid.NewGuid()}", isCollectible: false);

        context.Resolving += (loadContext, name) =>
        {
            var dependency = Path.Combine(directory, $"{name.Name}.dll");
            return File.Exists(dependency) ? loadContext.LoadFromAssemblyPath(dependency) : null;
        };

        return context.LoadFromAssemblyPath(candidate!);
    }

    private static string[] SolutionProjectLines() =>
        File.ReadAllLines(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

    private static string[] ProductionProjectFiles() =>
        new[] { "TaxLogicEngine", "TaxCases", "DocumentExtraction", "CaseProcessing", "CaseReview" }
            .Select(module => Path.Combine(RepositoryRoot, "src", module, $"{module}.csproj"))
            .ToArray();

    private static string[] ProductionSourceFiles() =>
        new[] { "TaxLogicEngine", "TaxCases", "DocumentExtraction", "CaseProcessing", "CaseReview" }
            .SelectMany(module => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", module), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .ToArray();

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .OrderBy(reference => reference, StringComparer.Ordinal)
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

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

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
