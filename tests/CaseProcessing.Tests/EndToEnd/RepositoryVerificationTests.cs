using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CaseProcessing.Models;
using CaseProcessing.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseProcessing.Tests.EndToEnd;

/// <summary>
/// T06 — repository-level verification for spec 008. Proves the whole solution
/// still holds together after four modules: every expected project is declared
/// once at its approved path, every declared reference resolves, dependencies
/// point inward with no reverse edge, the public contract matches
/// <c>architecture.md</c>, and one representative extraction still runs end to
/// end.
/// <para>
/// Every solution assertion is <b>presence-and-uniqueness</b>. There is
/// deliberately no total project count anywhere in this file: spec 006 shipped
/// one and it blocked spec 007's Wave 0 outright, requiring PR #66 to unpick.
/// The root <c>CLAUDE.md</c> now forbids it.
/// </para>
/// </summary>
public sealed class RepositoryVerificationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// The eight projects spec 008 leaves behind, their approved solution paths,
    /// and their exact reference sets. Adding a ninth module must not require
    /// editing this table — nothing here counts projects.
    /// </summary>
    private static readonly (string Name, string Directory, string[] References)[] ExpectedProjects =
    {
        ("TaxLogicEngine", "src", Array.Empty<string>()),
        ("TaxCases", "src", new[] { "../TaxLogicEngine/TaxLogicEngine.csproj" }),
        ("DocumentExtraction", "src", new[] { "../TaxLogicEngine/TaxLogicEngine.csproj" }),
        ("CaseProcessing", "src", new[] { "../DocumentExtraction/DocumentExtraction.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" }),
        ("TaxLogicEngine.Tests", "tests", new[] { "../../src/TaxLogicEngine/TaxLogicEngine.csproj" }),
        ("TaxCases.Tests", "tests", new[] { "../../src/TaxCases/TaxCases.csproj" }),
        ("DocumentExtraction.Tests", "tests", new[] { "../../src/DocumentExtraction/DocumentExtraction.csproj" }),
        ("CaseProcessing.Tests", "tests", new[] { "../../src/CaseProcessing/CaseProcessing.csproj" }),
    };

    /// <summary>The three suites that predate spec 008 and must be untouched by it.</summary>
    private static readonly string[] PreExistingTrees =
    {
        "src/TaxLogicEngine",
        "tests/TaxLogicEngine.Tests",
        "src/TaxCases",
        "tests/TaxCases.Tests",
        "src/DocumentExtraction",
        "tests/DocumentExtraction.Tests",
    };

    // -------------------------------------------------- positive: the solution

    [Theory]
    [InlineData("TaxLogicEngine", "src")]
    [InlineData("TaxCases", "src")]
    [InlineData("DocumentExtraction", "src")]
    [InlineData("CaseProcessing", "src")]
    [InlineData("TaxLogicEngine.Tests", "tests")]
    [InlineData("TaxCases.Tests", "tests")]
    [InlineData("DocumentExtraction.Tests", "tests")]
    [InlineData("CaseProcessing.Tests", "tests")]
    public void EveryExpectedProject_IsDeclaredExactlyOnceAtItsApprovedPath(string name, string directory)
    {
        var declaration = $"\"{name}\", \"{directory}\\{name}\\{name}.csproj\"";

        var matches = SolutionProjectLines()
            .Count(line => line.Contains(declaration, StringComparison.Ordinal));

        Assert.Equal(1, matches);
        Assert.True(
            File.Exists(Path.Combine(RepositoryRoot, directory, name, $"{name}.csproj")),
            $"{name} is declared but its project file is missing.");
    }

    [Fact]
    public void EveryDeclaredProjectAndReference_Resolves()
    {
        foreach (var (name, directory, _) in ExpectedProjects)
        {
            var projectPath = Path.Combine(RepositoryRoot, directory, name, $"{name}.csproj");
            Assert.True(File.Exists(projectPath), $"{name} does not exist at its approved path.");

            foreach (var reference in ProjectReferences(projectPath))
            {
                var resolved = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(projectPath)!, reference.Replace('/', Path.DirectorySeparatorChar)));

                Assert.True(File.Exists(resolved), $"{name} declares an unresolvable reference: {reference}");
            }
        }
    }

    [Fact]
    public void EveryProject_DeclaresExactlyItsApprovedReferenceSet()
    {
        foreach (var (name, directory, expected) in ExpectedProjects)
        {
            var actual = ProjectReferences(Path.Combine(RepositoryRoot, directory, name, $"{name}.csproj"));

            Assert.Equal(expected.OrderBy(r => r, StringComparer.Ordinal).ToArray(), actual);
        }
    }

    [Fact]
    public void DependenciesPointInwardWithNoReverseEdge()
    {
        // TaxLogicEngine is the innermost module and depends on nothing. Nothing
        // depends on CaseProcessing, the outermost.
        Assert.Empty(ProjectReferences(Path.Combine(RepositoryRoot, "src", "TaxLogicEngine", "TaxLogicEngine.csproj")));

        foreach (var (name, directory, references) in ExpectedProjects)
        {
            // The module and its own test project name themselves, obviously.
            if (name.StartsWith("CaseProcessing", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain(references, reference => reference.Contains("CaseProcessing", StringComparison.Ordinal));

            var sources = HandwrittenSourceFiles(Path.Combine(RepositoryRoot, directory, name))
                .Where(path => File.ReadAllText(path).Contains("CaseProcessing", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(RepositoryRoot, path))
                .ToArray();

            Assert.True(sources.Length == 0, $"{name} must not reference CaseProcessing: {string.Join(", ", sources)}");
        }
    }

    // ------------------------------------ positive: the contract and one flow

    [Fact]
    public void ThePublicContract_MatchesArchitectureMd()
    {
        var exported = typeof(T4CaseProcessor).Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "CaseProcessing.Models.ProcessedT4Extraction",
                "CaseProcessing.Models.T4ProcessingFields",
                "CaseProcessing.Services.SupportedProcessingTaxYears",
                "CaseProcessing.Services.T4CaseProcessor",
            },
            exported);

        // T4CaseProcessor(IT4LineMapProvider) and
        // ParseResult<ProcessedT4Extraction> Process(ValidatedExtraction?).
        var constructor = Assert.Single(typeof(T4CaseProcessor).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(IT4LineMapProvider), parameter.ParameterType);

        var process = typeof(T4CaseProcessor).GetMethod(nameof(T4CaseProcessor.Process))!;
        Assert.Equal(typeof(ParseResult<ProcessedT4Extraction>), process.ReturnType);
        Assert.Equal(typeof(ValidatedExtraction), Assert.Single(process.GetParameters()).ParameterType);

        // The seam guard stays off the public surface.
        Assert.DoesNotContain(exported, name => name.Contains("PayloadStructureGuard", StringComparison.Ordinal));
    }

    [Fact]
    public void ARepresentativeExtraction_StillRunsEndToEnd()
    {
        var envelope = RepresentativeEnvelope();

        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(validated.IsSuccess);

        var result = new T4CaseProcessor(new EmbeddedT4LineMapProvider()).Process(validated.Value);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal(2025, result.Value!.TaxYear);
        Assert.Equal(2025, result.Value.Slip.Year);
        Assert.NotEmpty(result.Value.Mapping.Lines);

        // The line entries are the engine's, not this module's.
        var slip = new T4SlipParser().Parse(envelope.T4Payload!).Value!;
        var mapped = new T4LineMapper(new EmbeddedT4LineMapProvider()).Map(new[] { slip }).Value!;

        Assert.Equal(
            mapped.Lines.Select(line => (line.LineNumber, line.Amount)).ToArray(),
            result.Value.Mapping.Lines.Select(line => (line.LineNumber, line.Amount)).ToArray());
    }

    // ------------------------------------------------------------- negative

    [Fact]
    public void NoProjectDeclaresAnUnexpectedReference()
    {
        var approved = ExpectedProjects
            .SelectMany(project => project.References.Select(reference => (project.Name, reference)))
            .ToHashSet();

        foreach (var (name, directory, _) in ExpectedProjects)
        {
            foreach (var reference in ProjectReferences(Path.Combine(RepositoryRoot, directory, name, $"{name}.csproj")))
            {
                Assert.Contains((name, reference), approved);
            }
        }
    }

    [Fact]
    public void NoTaxCasesCouplingExistsInEitherDirection()
    {
        Assert.DoesNotContain(
            "TaxCases",
            File.ReadAllText(Path.Combine(RepositoryRoot, "src", "CaseProcessing", "CaseProcessing.csproj")),
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            typeof(T4CaseProcessor).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.Contains("TaxCases", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            "CaseProcessing",
            File.ReadAllText(Path.Combine(RepositoryRoot, "src", "TaxCases", "TaxCases.csproj")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheModuleConsumesNoT5Type()
    {
        var offending = ModuleSourceFiles()
            .Where(path => ExecutableLines(path).Any(line => Regex.IsMatch(line, @"\bT5\w*\b")))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(offending.Length == 0, $"T5 named in: {string.Join(", ", offending)}");
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    public void NoUnsupportedYearIsEverAccepted(int taxYear)
    {
        Assert.False(SupportedProcessingTaxYears.IsSupported(taxYear));

        // Rejected upstream by spec 007's gate...
        var envelope = RepresentativeEnvelope(taxYear);
        Assert.True(ExtractionEnvelopeValidator.Validate(envelope).IsFailure);

        // ...and independently by this module, reached through the internal
        // constructor because spec 007 will not mint an off-year extraction.
        var offYear = ExtractionWithYear(taxYear);
        var result = new T4CaseProcessor(new EmbeddedT4LineMapProvider()).Process(offYear);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(T4ProcessingFields.TaxYear, Assert.Single(result.Errors).Field);
    }

    [Fact]
    public void ProcessedT4Extraction_HasNoPublicConstructor()
    {
        Assert.Empty(typeof(ProcessedT4Extraction).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var constructor = Assert.Single(
            typeof(ProcessedT4Extraction).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));

        Assert.True(constructor.IsAssembly, "The only constructor must be internal.");
    }

    [Fact]
    public void NoForbiddenBoundaryTokenSurvivesInTheModule()
    {
        var forbidden = new (string Name, string Pattern)[]
        {
            ("system clock", @"DateTime\s*\.\s*(?:Now|UtcNow)|DateTimeOffset\s*\.\s*(?:Now|UtcNow)"),
            ("filesystem", @"\bSystem\.IO\b|\b(?:File|Directory|FileStream)\s*\."),
            ("network", @"\bHttpClient\b|\bSystem\.Net\b"),
            ("database", @"\bDbContext\b|\bSqlConnection\b|EntityFrameworkCore"),
            ("ASP.NET Core", @"Microsoft\.AspNetCore"),
            ("generated identifier", @"\bGuid\s*\.\s*NewGuid\b"),
            ("money math", @"\bMoneyRounding\b|\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b"),
            ("CRA line literal", @"\b\d{5}\b"),
            ("mapping resource", @"TaxLogicEngine\.Mappings|GetManifestResourceStream"),
            ("masking helper", @"\bSafeDiagnosticText\b|new\s+RecipientIdentifier\b"),
        };

        foreach (var (name, pattern) in forbidden)
        {
            var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

            var offending = ModuleSourceFiles()
                .Where(path => ExecutableLines(path).Any(line => regex.IsMatch(line)))
                .Select(Path.GetFileName)
                .ToArray();

            Assert.True(offending.Length == 0, $"Forbidden {name} token in: {string.Join(", ", offending)}");
        }
    }

    // ------------------------------------------------- AC-43 — no regression

    [Fact]
    public void ThePreExistingModuleTrees_AreUntouchedBySpec008()
    {
        // The behavioural half of AC-43 is the solution run recorded in
        // verification.md: TaxLogicEngine.Tests 261, TaxCases.Tests 337, and
        // DocumentExtraction.Tests 673 — the same counts spec 007 recorded, all
        // green. What is mechanically checkable here is that spec 008 left no
        // trace in those trees at all.
        foreach (var tree in PreExistingTrees)
        {
            var root = Path.Combine(RepositoryRoot, tree.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(root), $"{tree} must exist.");

            var contaminated = HandwrittenSourceFiles(root)
                .Where(path => File.ReadAllText(path).Contains("CaseProcessing", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(RepositoryRoot, path))
                .ToArray();

            Assert.True(
                contaminated.Length == 0,
                $"{tree} references spec 008's module: {string.Join(", ", contaminated)}");
        }
    }

    [Fact]
    public void RepositoryHygiene_BuildOutputAndLocalSettingsAreIgnored()
    {
        // Build output exists on disk after any build, so its presence proves
        // nothing. What is checkable here is that .gitignore covers it; that
        // nothing of the sort is actually *tracked* is verified by `git ls-files`
        // and recorded in verification.md.
        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));

        foreach (var required in new[] { "[Bb]in/", "[Oo]bj/", ".vs/", "*.user", ".claude/settings.local.json" })
        {
            Assert.Contains(required, gitignore, StringComparison.Ordinal);
        }
    }

    // -------------------------------------------------------------- guards

    [Fact]
    public void TheVerificationRecord_CarriesARowForEveryAcceptanceCriterion()
    {
        // The record is part of the deliverable, so its completeness is a test,
        // not a promise. AC-1 through AC-43 must each appear as a table row with
        // a verdict.
        var verification = File.ReadAllText(
            Path.Combine(RepositoryRoot, "specs", "008-t4-case-processor", "verification.md"));

        for (var criterion = 1; criterion <= 43; criterion++)
        {
            Assert.True(
                Regex.IsMatch(verification, $@"^\|\s*{criterion}\s*\|", RegexOptions.Multiline),
                $"verification.md has no row for AC-{criterion}.");
        }

        // No row may be left unresolved.
        Assert.DoesNotContain("TODO", verification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheScanner_SeesTheRealTreesAndStillMatchesAKnownPattern()
    {
        // Without this, every absence assertion above would pass for free on a
        // broken file walk.
        var moduleFiles = ModuleSourceFiles();
        Assert.Equal(5, moduleFiles.Length);

        Assert.NotEmpty(SolutionProjectLines());

        foreach (var tree in PreExistingTrees)
        {
            var files = HandwrittenSourceFiles(
                Path.Combine(RepositoryRoot, tree.Replace('/', Path.DirectorySeparatorChar))).ToArray();

            Assert.True(files.Length > 0, $"The walk found no sources under {tree}.");
        }

        var allExecutable = string.Join("\n", moduleFiles.SelectMany(ExecutableLines));
        Assert.Matches(new Regex(@"\bT4SlipParser\b"), allExecutable);
        Assert.Matches(new Regex(@"\bValidatedExtraction\b"), allExecutable);
    }

    // ------------------------------------------------------------------ helpers

    private static ExtractionEnvelope RepresentativeEnvelope(int taxYear = 2025) =>
        new()
        {
            DocumentId = "doc-verification-1",
            Kind = ExtractedDocumentKind.T4Slip,
            FirmId = "firm-verification-1",
            CaseId = "case-verification-1",
            ParticipantId = "participant-verification-1",
            TaxYear = taxYear,
            Provenance = new ExtractionProvenance { SourceReference = "blob://verification-1" },
            T4Payload = RepresentativePayload(taxYear),
        };

    private static T4RawInput RepresentativePayload(int taxYear = 2025) =>
        new()
        {
            Year = taxYear,
            EmployerName = "ACME CORP",
            EmployerAddress = "1 WAY ST, OTTAWA ON",
            EmployeeName = "DOE, JANE",
            EmployeeAddress = "2 HOME AVE, OTTAWA ON",
            Boxes = new List<T4RawBox>
            {
                new(T4BoxCodes.SocialInsuranceNumber, "046454286"),
                new(T4BoxCodes.EmployerAccountNumber, "12345678RP0001"),
                new(T4BoxCodes.DentalBenefits, "1"),
                new(T4BoxCodes.EmploymentIncome, "52000.00"),
                new(T4BoxCodes.IncomeTaxDeducted, "8000.00"),
            },
        };

    private static ValidatedExtraction ExtractionWithYear(int taxYear)
    {
        var constructor = typeof(ValidatedExtraction)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single();

        return (ValidatedExtraction)constructor.Invoke(new object?[]
        {
            "doc-verification-1",
            ExtractedDocumentKind.T4Slip,
            "firm-verification-1",
            "case-verification-1",
            "participant-verification-1",
            taxYear,
            "blob://verification-1",
            null,
            null,
            RepresentativePayload(),
        });
    }

    private static string[] SolutionProjectLines() =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

    private static string[] ModuleSourceFiles() =>
        HandwrittenSourceFiles(Path.Combine(RepositoryRoot, "src", "CaseProcessing"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> HandwrittenSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
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
