using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentExtraction.Models;
using DocumentExtraction.Services;

namespace DocumentExtraction.Tests.Boundaries;

/// <summary>
/// T04 — proves the module can never become a tax-truth producer or acquire a
/// forbidden dependency. Covers AC-24, AC-26, AC-27, and the reference arms of
/// AC-29/AC-30 by scanning the solution, both project files, the compiled
/// assembly's references, and the module's handwritten source.
/// <para>
/// These assertions are deliberately about <b>presence and absence</b>, never
/// about a total project count: pinning the solution to "exactly N projects"
/// is what blocked spec 007's own Wave 0 and would block the next module too.
/// </para>
/// </summary>
public sealed class DocumentExtractionBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ModuleRoot =
        Path.Combine(RepositoryRoot, "src", "DocumentExtraction");

    // ------------------------------------------------------- positive: structure

    [Fact]
    public void SolutionAndProjects_HaveTheApprovedPathsAndDirection()
    {
        var solutionPath = Path.Combine(RepositoryRoot, "TaxAI.sln");
        var moduleProject = Path.Combine(ModuleRoot, "DocumentExtraction.csproj");
        var testProject = Path.Combine(
            RepositoryRoot, "tests", "DocumentExtraction.Tests", "DocumentExtraction.Tests.csproj");

        Assert.True(File.Exists(solutionPath));
        Assert.True(File.Exists(moduleProject));
        Assert.True(File.Exists(testProject));

        // Presence-and-uniqueness, never a total count: the solution must stay free
        // to declare later modules' projects.
        var projectLines = File.ReadAllText(solutionPath)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

        foreach (var declaration in new[]
                 {
                     "\"DocumentExtraction\", \"src\\DocumentExtraction\\DocumentExtraction.csproj\"",
                     "\"DocumentExtraction.Tests\", \"tests\\DocumentExtraction.Tests\\DocumentExtraction.Tests.csproj\"",
                 })
        {
            Assert.Equal(1, projectLines.Count(line => line.Contains(declaration, StringComparison.Ordinal)));
        }

        Assert.Equal(new[] { "../TaxLogicEngine/TaxLogicEngine.csproj" }, ProjectReferences(moduleProject));
    }

    [Fact]
    public void ProductionProject_DeclaresNoPackageFrameworkOrContentReference()
    {
        var project = XDocument.Load(Path.Combine(ModuleRoot, "DocumentExtraction.csproj"));

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);
        foreach (var forbidden in new[] { "PackageReference", "FrameworkReference", "EmbeddedResource", "Content" })
        {
            Assert.DoesNotContain(
                project.Descendants(),
                element => element.Name.LocalName == forbidden);
        }
    }

    [Fact]
    public void TheSupportedYearPolicy_LivesOnlyInSupportedExtractionTaxYears()
    {
        Assert.Equal(new[] { 2025 }, SupportedExtractionTaxYears.All);
        Assert.True(SupportedExtractionTaxYears.IsSupported(2025));
        Assert.False(SupportedExtractionTaxYears.IsSupported(2024));
        Assert.False(SupportedExtractionTaxYears.IsSupported(2026));

        // No other production file may hardcode the year in executable code, so
        // widening support stays a one-place change. Doc comments are exempt.
        foreach (var file in ProductionSourceFiles())
        {
            if (Path.GetFileName(file) == "SupportedExtractionTaxYears.cs")
            {
                continue;
            }

            var offending = ExecutableLines(file)
                .Where(line => line.Contains("2025", StringComparison.Ordinal))
                .ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} hardcodes the supported year outside SupportedExtractionTaxYears: "
                + string.Join(" | ", offending));
        }
    }

    [Fact]
    public void Wave0InstructionFiles_ExistWithTheirRequiredContentAndPointers()
    {
        var moduleClaude = File.ReadAllText(Path.Combine(ModuleRoot, "CLAUDE.md"));
        foreach (var required in new[]
                 {
                     "DocumentExtraction → TaxLogicEngine",
                     "presence",
                     "omission",
                     "2025",
                     "fail",
                 })
        {
            Assert.Contains(required, moduleClaude, StringComparison.OrdinalIgnoreCase);
        }

        var agents = File.ReadAllText(Path.Combine(ModuleRoot, "AGENTS.md"));
        Assert.Contains("pointer", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CLAUDE.md", agents, StringComparison.Ordinal);
    }

    // ------------------------------------- negative: references, both directions

    [Fact]
    public void AC29_TheModuleReferencesNoTaxCasesProjectOrAssembly()
    {
        Assert.DoesNotContain(
            "TaxCases",
            File.ReadAllText(Path.Combine(ModuleRoot, "DocumentExtraction.csproj")),
            StringComparison.OrdinalIgnoreCase);

        var nonFrameworkReferences = typeof(ValidatedExtraction).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("System", StringComparison.Ordinal) &&
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "TaxLogicEngine" }, nonFrameworkReferences);
    }

    [Fact]
    public void AC29_NeitherTaxLogicEngineNorTaxCasesReferencesThisModule()
    {
        foreach (var module in new[] { "TaxLogicEngine", "TaxCases" })
        {
            var root = Path.Combine(RepositoryRoot, "src", module);

            Assert.DoesNotContain(
                "DocumentExtraction",
                File.ReadAllText(Path.Combine(root, $"{module}.csproj")),
                StringComparison.OrdinalIgnoreCase);

            var reverseReferences = HandwrittenSourceFiles(root)
                .Where(path => File.ReadAllText(path).Contains("DocumentExtraction", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(RepositoryRoot, path))
                .ToArray();

            Assert.True(
                reverseReferences.Length == 0,
                $"{module} must not reference DocumentExtraction: {string.Join(", ", reverseReferences)}");
        }
    }

    // ------------------------------------------- negative: forbidden source usage

    public static TheoryData<string, string> ForbiddenProductionPatterns()
    {
        var data = new TheoryData<string, string>
        {
            // AC-24 — no SIN representation and no RecipientIdentifier construction.
            { "RecipientIdentifier / SIN representation", @"\bRecipientIdentifier\b|\bSin\b|\bSocialInsurance" },

            // No masking helper of any kind: PII here is handled by omission.
            { "masking helper", @"\bSafeDiagnosticText\b|\bMask(?:ed|ing)?\s*\(|\bRedact" },

            // AC-26 — no tax truth: no engine parser/mapper/rounding, no CRA line
            // numbers, no box->line mapping, no mapping-resource read.
            { "engine parser/mapper/rounding", @"\bT4SlipParser\b|\bT4LineMapper\b|\bT5SlipParser\b|\bT5LineMapper\b|\bMoneyRounding\b" },
            { "CRA line mapping", @"\bTaxLineEntry\b|\bT4MappingResult\b|\bT4LineMap\b|\bBoxLineMapping\b|\bLineNumber\b" },
            { "mapping resource read", @"TaxLogicEngine\.Mappings|GetManifestResourceStream|\.json\b" },
            { "rounding / money math", @"\bMath\.Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b" },

            // AC-27 — determinism: no clock, I/O, environment, or provider access.
            { "system clock", @"DateTime\.(?:Now|UtcNow)|DateTimeOffset\.(?:Now|UtcNow)|\bStopwatch\b|TimeProvider" },
            { "filesystem I/O", @"\bSystem\.IO\b|\b(?:File|Directory|Path)\s*\.|\bFileStream\b" },
            { "network I/O", @"\bSystem\.Net\b|\bHttpClient\b|\bWebRequest\b|\bSocket\b" },
            { "database", @"\bDbContext\b|\bDbSet\s*<|\bSqlConnection\b|\bSystem\.Data\b|EntityFrameworkCore" },
            { "environment / current user", @"\bEnvironment\.\b|\bClaimsPrincipal\b|\bCurrentUser\b|\bThread\.CurrentPrincipal\b" },
            { "OCR / LLM / cloud / email", @"\bOcr\b|\bTesseract\b|\bOpenAi\b|\bAnthropic\b|\bLlm\b|\bSmtp\b|\bMailMessage\b|\bAmazon\b|\bAzure\b|\bS3\b" },
            { "ASP.NET Core", @"Microsoft\.AspNetCore|\bControllerBase\b|\bWebApplication\b" },

            // AC-30 — T5 is deferred by approved scope and must not be consumed.
            { "T5 consumption", @"\bT5\w*\b" },

            // Non-determinism from generated values.
            { "generated identifier", @"\bGuid\.NewGuid\b|\bRandom\b" },
        };

        return data;
    }

    [Theory]
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSource_ContainsNoForbiddenDependencyOrBehavior(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Select(path => new { Path = Path.GetFileName(path), Text = string.Join("\n", ExecutableLines(path)) })
            .Where(source => regex.IsMatch(source.Text))
            .Select(source => source.Path)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Forbidden {boundary} pattern /{pattern}/ found in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AC26_NoCraLineNumberLiteralAppearsInTheModule()
    {
        // CRA T1 line numbers are five digits (e.g. 10100 employment income,
        // 12000 taxable dividends). None may be hardcoded here — they live only in
        // TaxLogicEngine's JSON config.
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
    public void AC24_ThePublicSurfaceDeclaresNoSinPropertyOrSinType()
    {
        var publicTypes = typeof(ValidatedExtraction).Assembly.GetExportedTypes();

        Assert.DoesNotContain(publicTypes, type =>
            type.Name.Contains("Sin", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("SocialInsurance", StringComparison.OrdinalIgnoreCase));

        foreach (var type in publicTypes)
        {
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.Name.Contains("Sin", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("SocialInsurance", StringComparison.OrdinalIgnoreCase));

            // Nor may any public member expose the engine's SIN value object.
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.PropertyType.Name.Contains("RecipientIdentifier", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AC30_NoT5KindExistsAndTheKindSetIsExactlyT4SlipAndGenericEvidence()
    {
        Assert.Equal(new[] { "T4Slip", "GenericEvidence" }, Enum.GetNames<ExtractedDocumentKind>());
        Assert.DoesNotContain("T5", Enum.GetNames<ExtractedDocumentKind>(), StringComparer.OrdinalIgnoreCase);

        // default(ExtractedDocumentKind) must not be a defined value, so an
        // unset kind fails closed rather than silently meaning "T4".
        Assert.False(Enum.IsDefined(default(ExtractedDocumentKind)));
    }

    [Fact]
    public void TheForbiddenPatternScan_ActuallyMatchesSomething_SoItCannotPassVacuously()
    {
        // Guards the scanner itself: if ProductionSourceFiles() or ExecutableLines()
        // silently returned nothing, every pattern above would pass for free.
        var files = ProductionSourceFiles();
        Assert.Equal(8, files.Length);

        var allExecutable = string.Join("\n", files.SelectMany(ExecutableLines));
        Assert.Contains("ExtractionEnvelopeValidator", allExecutable, StringComparison.Ordinal);
        Assert.Contains("T4Payload", allExecutable, StringComparison.Ordinal);

        // A pattern that should match proves the regex path works end to end.
        Assert.Matches(new Regex(@"\bT4RawInput\b"), allExecutable);
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
    /// Source lines excluding comments. Documentation legitimately names forbidden
    /// concepts while explaining why they are forbidden — scanning raw text would
    /// make honest comments unwritable, so only executable code is scanned.
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
