using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CaseProcessing.Models;
using CaseProcessing.Services;

namespace CaseProcessing.Tests.Boundaries;

/// <summary>
/// T05 — proves the module cannot become a second mapping authority or acquire a
/// forbidden dependency. Covers AC-40, AC-41, and AC-42 by scanning the solution,
/// both project files, the compiled assembly's references, the other modules'
/// trees, and this module's handwritten source.
/// <para>
/// Every solution assertion is about <b>presence and uniqueness</b>, never about a
/// total project count. Pinning the solution to "exactly N projects" is what
/// blocked spec 007's Wave 0 outright and required PR #66 to unpick; the root
/// <c>CLAUDE.md</c> now forbids it.
/// </para>
/// <para>
/// Scans read <b>executable lines only</b>. This module's documentation
/// legitimately names almost every forbidden concept while explaining why it is
/// forbidden, so scanning raw text would make honest comments unwritable.
/// </para>
/// </summary>
public sealed class CaseProcessingBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ModuleRoot = Path.Combine(RepositoryRoot, "src", "CaseProcessing");

    /// <summary>
    /// The module's complete production source set. Named rather than counted so
    /// the scanner guard can prove the walk saw each one.
    /// </summary>
    private static readonly string[] ExpectedProductionFiles =
    {
        "PayloadStructureGuard.cs",
        "ProcessedT4Extraction.cs",
        "SupportedProcessingTaxYears.cs",
        "T4CaseProcessor.cs",
        "T4ProcessingFields.cs",
    };

    // ------------------------------------------------------- positive: structure

    [Fact]
    public void SolutionDeclaresBothProjectsExactlyOnceAtTheirApprovedPaths()
    {
        var solutionPath = Path.Combine(RepositoryRoot, "TaxAI.sln");
        var moduleProject = Path.Combine(ModuleRoot, "CaseProcessing.csproj");
        var testProject = Path.Combine(
            RepositoryRoot, "tests", "CaseProcessing.Tests", "CaseProcessing.Tests.csproj");

        Assert.True(File.Exists(solutionPath));
        Assert.True(File.Exists(moduleProject));
        Assert.True(File.Exists(testProject));

        var projectLines = File.ReadAllText(solutionPath)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(projectLines);

        // Presence-and-uniqueness only: the solution must stay free to declare
        // later modules' projects without this test failing.
        foreach (var declaration in new[]
                 {
                     "\"CaseProcessing\", \"src\\CaseProcessing\\CaseProcessing.csproj\"",
                     "\"CaseProcessing.Tests\", \"tests\\CaseProcessing.Tests\\CaseProcessing.Tests.csproj\"",
                 })
        {
            Assert.Equal(1, projectLines.Count(line => line.Contains(declaration, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void ProductionProject_ReferencesExactlyDocumentExtractionAndTaxLogicEngine() // AC-40
    {
        Assert.Equal(
            new[] { "../DocumentExtraction/DocumentExtraction.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" },
            ProjectReferences(Path.Combine(ModuleRoot, "CaseProcessing.csproj")));
    }

    [Fact]
    public void ProductionProject_DeclaresNoPackageFrameworkResourceOrContentReference()
    {
        var project = XDocument.Load(Path.Combine(ModuleRoot, "CaseProcessing.csproj"));

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);

        foreach (var forbidden in new[] { "PackageReference", "FrameworkReference", "EmbeddedResource", "Content" })
        {
            Assert.Empty(project.Descendants().Where(element => element.Name.LocalName == forbidden));
        }
    }

    [Fact]
    public void CompiledAssembly_ReferencesExactlyTheTwoApprovedModules() // AC-40
    {
        // The project file states intent; the assembly states what actually
        // compiled in.
        var nonFrameworkReferences = typeof(T4CaseProcessor).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("System", StringComparison.Ordinal) &&
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "DocumentExtraction", "TaxLogicEngine" }, nonFrameworkReferences);
    }

    [Fact]
    public void TheSupportedYearPolicy_LivesOnlyInSupportedProcessingTaxYears()
    {
        Assert.Equal(new[] { 2025 }, SupportedProcessingTaxYears.All);
        Assert.True(SupportedProcessingTaxYears.IsSupported(2025));
        Assert.False(SupportedProcessingTaxYears.IsSupported(2024));
        Assert.False(SupportedProcessingTaxYears.IsSupported(2026));

        // No other production file may name the year in executable code, so
        // widening support stays a one-place change rather than a hunt.
        foreach (var file in ProductionSourceFiles())
        {
            if (Path.GetFileName(file) == "SupportedProcessingTaxYears.cs")
            {
                continue;
            }

            var offending = ExecutableLines(file)
                .Where(line => Regex.IsMatch(line, @"\b(?:19|20)\d{2}\b"))
                .ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} names a tax year outside SupportedProcessingTaxYears: "
                + string.Join(" | ", offending));
        }
    }

    [Fact]
    public void Wave0InstructionFiles_ExistWithTheirRequiredContentAndPointers()
    {
        var moduleClaude = File.ReadAllText(Path.Combine(ModuleRoot, "CLAUDE.md"));

        foreach (var required in new[]
                 {
                     "CaseProcessing → TaxLogicEngine",
                     "DocumentExtraction",
                     "TaxCases",
                     "omission",
                     "masking",
                     "RecipientIdentifier",
                     "2025",
                     "fail",
                     "verbatim",
                 })
        {
            Assert.Contains(required, moduleClaude, StringComparison.OrdinalIgnoreCase);
        }

        var agents = File.ReadAllText(Path.Combine(ModuleRoot, "AGENTS.md"));
        Assert.Contains("pointer", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CLAUDE.md", agents, StringComparison.Ordinal);

        // Pointer-only: the bridge defines no rules of its own.
        Assert.True(agents.Split('\n').Length < 15, "AGENTS.md must stay a pointer, not a rulebook.");
    }

    // ------------------------------------- negative: references, both directions

    [Fact]
    public void TheModuleReferencesNoTaxCasesProjectOrAssembly() // AC-41
    {
        Assert.DoesNotContain(
            "TaxCases",
            File.ReadAllText(Path.Combine(ModuleRoot, "CaseProcessing.csproj")),
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            typeof(T4CaseProcessor).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.Contains("TaxCases", StringComparison.OrdinalIgnoreCase));

        var offending = ProductionSourceFiles()
            .Where(path => ExecutableLines(path).Any(line => line.Contains("TaxCases", StringComparison.Ordinal)))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.True(offending.Length == 0, $"TaxCases named in: {string.Join(", ", offending)}");
    }

    [Theory]
    [InlineData("src", "TaxLogicEngine")]
    [InlineData("src", "TaxCases")]
    [InlineData("src", "DocumentExtraction")]
    [InlineData("tests", "TaxLogicEngine.Tests")]
    [InlineData("tests", "TaxCases.Tests")]
    [InlineData("tests", "DocumentExtraction.Tests")]
    public void NoOtherModuleReferencesCaseProcessing(string area, string project) // AC-41
    {
        // Dependencies point inward. Nothing may depend on this module — not the
        // engine, not Tax Case Core, not the ingestion contract, and not their
        // test trees.
        var root = Path.Combine(RepositoryRoot, area, project);
        Assert.True(Directory.Exists(root), $"{root} must exist.");

        var projectFile = Path.Combine(root, $"{project}.csproj");
        Assert.True(File.Exists(projectFile));
        Assert.DoesNotContain("CaseProcessing", File.ReadAllText(projectFile), StringComparison.OrdinalIgnoreCase);

        var reverseReferences = HandwrittenSourceFiles(root)
            .Where(path => File.ReadAllText(path).Contains("CaseProcessing", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            reverseReferences.Length == 0,
            $"{project} must not reference CaseProcessing: {string.Join(", ", reverseReferences)}");
    }

    // ------------------------------------------- negative: forbidden source usage

    public static TheoryData<string, string> ForbiddenProductionPatterns() => new()
    {
        // AC-42 — determinism: no clock, I/O, environment, or generated value.
        { "system clock", @"DateTime\s*\.\s*(?:Now|UtcNow)|DateTimeOffset\s*\.\s*(?:Now|UtcNow)|\bStopwatch\b|\bTimeProvider\b" },
        { "filesystem I/O", @"\bSystem\.IO\b|\b(?:File|Directory|Path|FileStream|StreamReader)\s*\." },
        { "network I/O", @"\bSystem\.Net\b|\bHttpClient\b|\bWebRequest\b|\bSocket\b|\bWebSocket\b" },
        { "database / ORM", @"\bDbContext\b|\bDbSet\s*<|\bSqlConnection\b|\bSystem\.Data\b|EntityFrameworkCore|\bSELECT\s+\*" },
        { "environment / current user", @"\bEnvironment\s*\.|\bClaimsPrincipal\b|\bCurrentUser\b|\bThread\.CurrentPrincipal\b" },
        { "OCR / LLM / cloud / email", @"\bOcr\b|\bTesseract\b|\bOpenAi\b|\bAnthropic\b|\bLlm\b|\bSmtp\b|\bMailMessage\b|\bAmazon\b|\bAzure\b|\bS3\b" },
        { "ASP.NET Core", @"Microsoft\.AspNetCore|\bControllerBase\b|\bWebApplication\b|\bIActionResult\b" },
        { "generated identifier", @"\bGuid\s*\.\s*NewGuid\b|\bnew\s+Random\b|\bRandomNumberGenerator\b" },

        // No second mapping authority: the box→line table lives in
        // TaxLogicEngine's JSON config and is reached only through the provider.
        { "box→line table", @"\bT4LineMappingConfig\b|\bT4BoxLineMapping\b|\bT4LineMap\s*\.\s*Create\b|\bDictionary\s*<|\bT4BoxCodes\b|\bT4OtherInformationCodes\b" },
        { "mapping resource read", @"TaxLogicEngine\.Mappings|GetManifestResourceStream|\.json\b|\bEmbeddedT4LineMapProvider\b" },

        // No arithmetic and no rounding: MoneyRounding is reused only indirectly,
        // through the parser and mapper.
        { "rounding / money math", @"\bMoneyRounding\b|\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b|\bdecimal\b" },

        // PII: no second masking helper and no SIN conversion of our own.
        { "masking helper", @"\bSafeDiagnosticText\b|\bMask(?:ed|ing)?\s*\(|\bRedact|\bScrub|\bSanitiz" },
        { "RecipientIdentifier construction", @"new\s+RecipientIdentifier\b" },

        // T5 is not this module's concern in either direction.
        { "T5 consumption", @"\bT5\w*\b" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSource_ContainsNoForbiddenDependencyOrBehavior(string boundary, string pattern) // AC-42
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Select(path => new { Name = Path.GetFileName(path), Text = string.Join("\n", ExecutableLines(path)) })
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
        // one in a result originates from the injected map.
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
    public void TheModuleDeclaresNoSinPropertyAndNoSinType()
    {
        // Reading T4Slip.Sin is the one permitted SIN representation; declaring a
        // member or type of our own would create a second carrier.
        var moduleTypes = typeof(T4CaseProcessor).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("CaseProcessing", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(moduleTypes);

        foreach (var type in moduleTypes)
        {
            Assert.DoesNotContain("SocialInsurance", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.False(
                Regex.IsMatch(type.Name, @"(?<![A-Za-z])Sin(?![a-z])"),
                $"{type.Name} looks like a SIN type.");

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.False(
                    Regex.IsMatch(property.Name, @"(?<![A-Za-z])Sin(?![a-z])") ||
                    property.Name.Contains("SocialInsurance", StringComparison.OrdinalIgnoreCase),
                    $"{type.Name}.{property.Name} looks like a SIN property.");

                Assert.DoesNotContain(
                    "RecipientIdentifier", property.PropertyType.Name, StringComparison.Ordinal);
            }
        }
    }

    // -------------------------------------------------------------- scanner guard

    [Fact]
    public void TheScanner_SeesEveryProductionFileAndStillMatchesAKnownPattern()
    {
        // Without this, a broken walk or an over-eager comment filter would make
        // every assertion above pass for free.
        var files = ProductionSourceFiles();

        Assert.Equal(
            ExpectedProductionFiles,
            files.Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var allExecutable = string.Join("\n", files.SelectMany(ExecutableLines));

        Assert.True(allExecutable.Length > 2000, $"Implausibly little executable source: {allExecutable.Length} chars.");
        Assert.Contains("T4CaseProcessor", allExecutable, StringComparison.Ordinal);
        Assert.Contains("PayloadStructureGuard", allExecutable, StringComparison.Ordinal);
        Assert.Contains(nameof(T4ProcessingFields.PayloadBoxCode), allExecutable, StringComparison.Ordinal);

        // Patterns that must match, proving the regex path works end to end: if
        // these stopped matching, the forbidden-pattern scan would be scanning
        // nothing.
        Assert.Matches(new Regex(@"\bT4SlipParser\b"), allExecutable);
        Assert.Matches(new Regex(@"\bIT4LineMapProvider\b"), allExecutable);
    }

    [Fact]
    public void TheCommentFilter_RemovesDocumentationWithoutRemovingCode()
    {
        // The filter is what makes "executable lines only" safe. Prove both
        // directions on a file that deliberately documents forbidden concepts.
        var guard = Path.Combine(ModuleRoot, "Services", "PayloadStructureGuard.cs");
        var raw = File.ReadAllText(guard);
        var executable = string.Join("\n", ExecutableLines(guard));

        // The documentation names a forbidden concept while explaining it...
        Assert.Contains("T4RawBox.Value", raw, StringComparison.Ordinal);
        // ...and the filter removes it, so honest comments stay writable.
        Assert.DoesNotContain("T4RawBox.Value", executable, StringComparison.Ordinal);
        // ...while the code itself survives.
        Assert.Contains("IsNullOrWhiteSpace", executable, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static string[] ProductionSourceFiles() =>
        HandwrittenSourceFiles(ModuleRoot).OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> HandwrittenSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Source lines excluding comments. This module's documentation legitimately
    /// names nearly every forbidden concept while explaining why it is forbidden,
    /// so scanning raw text would make honest comments unwritable.
    /// </summary>
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
