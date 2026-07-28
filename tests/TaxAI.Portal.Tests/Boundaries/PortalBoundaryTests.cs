using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaxAI.Portal.Tests.Boundaries;

/// <summary>
/// T04 — proves the two things prose cannot enforce: that the dependency arrows
/// never reverse, and that the web layer stays on its side of the line. "UI must
/// not compute tax" is unenforceable convention until something binds it; these
/// scans are that binding.
/// <para>
/// Every scan reads <b>executable lines only</b>. The portal's documentation
/// legitimately names nearly every forbidden concept while explaining why it is
/// forbidden — scanning raw text would make honest comments unwritable. The
/// comment-filter guard at the bottom proves the filter removes documentation
/// without removing code.
/// </para>
/// <para>
/// Solution assertions elsewhere in this suite are presence-and-uniqueness,
/// never a total project count: pinning the solution to "exactly N projects"
/// blocked spec 007's Wave 0 outright and required PR #66 to unpick.
/// </para>
/// </summary>
public sealed class PortalBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string PortalRoot =
        Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");

    /// <summary>
    /// The portal's complete production source set. Named rather than counted so
    /// the scanner guard can prove the walk saw each one.
    /// </summary>
    private static readonly string[] ExpectedProductionFiles =
    {
        "ModuleRegistration.cs",
        "PortalContent.cs",
        "Program.cs",
    };

    /// <summary>
    /// Every pre-existing project tree. Frozen deliberately: this is the set
    /// that must not learn the portal's name, and a generic walk of
    /// <c>src/</c> would silently start including the portal itself.
    /// </summary>
    public static TheoryData<string, string> PreExistingProjects() => new()
    {
        { "src", "TaxLogicEngine" },
        { "src", "TaxCases" },
        { "src", "DocumentExtraction" },
        { "src", "CaseProcessing" },
        { "src", "CaseReview" },
        { "tests", "TaxLogicEngine.Tests" },
        { "tests", "TaxCases.Tests" },
        { "tests", "DocumentExtraction.Tests" },
        { "tests", "CaseProcessing.Tests" },
        { "tests", "CaseReview.Tests" },
    };

    // ------------------------------------------------ AC-10: no reverse arrow

    [Theory] // AC-10
    [MemberData(nameof(PreExistingProjects))]
    public void NoPreExistingProjectReferencesThePortal(string area, string project)
    {
        // Dependencies point inward. Nothing may depend on the host — not a
        // module, not a module's test tree. If one ever did, the layering would
        // be inverted and the "UI computes no tax" rule would have no meaning.
        var root = Path.Combine(RepositoryRoot, area, project);
        Assert.True(Directory.Exists(root), $"{root} must exist.");

        var projectFile = Path.Combine(root, $"{project}.csproj");
        Assert.True(File.Exists(projectFile), $"{projectFile} must exist.");

        Assert.DoesNotContain(
            "TaxAI.Portal",
            File.ReadAllText(projectFile),
            StringComparison.OrdinalIgnoreCase);

        var offending = HandwrittenSourceFiles(root)
            .Where(path => File.ReadAllText(path).Contains("TaxAI.Portal", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            offending.Length == 0,
            $"{project} must not reference TaxAI.Portal: {string.Join(", ", offending)}");
    }

    // --------------------------------------------- AC-40: compiled references

    [Fact]
    public void TheCompiledPortal_ReferencesExactlyCaseProcessingAndTaxLogicEngine() // AC-40
    {
        // The project file states intent; the assembly states what actually
        // compiled in. A transitive DocumentExtraction leak — a type named in
        // passing — would show up here and nowhere else.
        //
        // The framework filter is wider than the class libraries' equivalent by
        // necessity: a web host references dozens of shared-framework
        // assemblies. Anything outside System*/Microsoft.*/netstandard is a real
        // dependency of ours.
        var nonFrameworkReferences = typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "CaseProcessing", "TaxLogicEngine" }, nonFrameworkReferences);
    }

    [Fact]
    public void ThePortalProject_ReferencesNeitherTaxCasesNorCaseReview() // AC-40
    {
        var projectText = File.ReadAllText(Path.Combine(PortalRoot, "TaxAI.Portal.csproj"));

        // CaseReview exists and is merged; the portal still may not depend on
        // it. An AccountantReviewPackage is taxpayer data, and rendering it is
        // exactly what the FR-18 gate stops.
        foreach (var forbidden in new[] { "TaxCases", "CaseReview", "DocumentExtraction" })
        {
            Assert.DoesNotContain(forbidden, projectText, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            typeof(Program).Assembly.GetReferencedAssemblies(),
            reference =>
                reference.Name!.Equals("TaxCases", StringComparison.OrdinalIgnoreCase) ||
                reference.Name!.Equals("CaseReview", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------- AC-22: wiring lives in one place

    [Fact]
    public void EveryServiceRegistrationLivesInTheCompositionRoot() // AC-22
    {
        var registration = new Regex(
            @"\bAddSingleton\b|\bAddScoped\b|\bAddTransient\b|\bTryAdd\w*\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var offending = ProductionSourceFiles()
            .Where(path => Path.GetFileName(path) != "ModuleRegistration.cs")
            .Where(path => ExecutableLines(path).Any(line => registration.IsMatch(line)))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(
            offending.Length == 0,
            $"Registrations must live only in ModuleRegistration.cs, found in: {string.Join(", ", offending)}");

        // And the composition root must actually contain some, so this test
        // cannot pass by the registrations having quietly disappeared.
        var compositionRoot = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ModuleRegistration.cs")));

        Assert.Equal(3, registration.Matches(compositionRoot).Count);
    }

    // ------------------------- AC-31 – AC-35: the line the portal may not cross

    public static TheoryData<string, string> ForbiddenProductionPatterns() => new()
    {
        // AC-31 — no tax computation, and no arithmetic that could become one.
        { "engine parser or mapper", @"\bT4SlipParser\b|\bT5SlipParser\b|\bT4LineMapper\b|\bT5LineMapper\b|\bT4SlipBoxAmounts\b|\bT5SlipBoxAmounts\b" },
        { "rounding / money math", @"\bMoneyRounding\b|\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b|\bdecimal\b" },

        // AC-32 — no taxpayer data type is even named, let alone rendered.
        { "taxpayer data type", @"\bT4Slip\b|\bT5Slip\b|\bT4RawInput\b|\bT5RawInput\b|\bTaxLineEntry\b|\bT4MappingResult\b|\bT5MappingResult\b|\bRecipientIdentifier\b|\bValidatedExtraction\b|\bExtractionEnvelope\b|\bProcessedT4Extraction\b|\bTaxCase\b" },

        // AC-34 — no second mapping authority and no mapping-resource read.
        { "mapping table or resource read", @"TaxLogicEngine\.Mappings|\bGetManifestResourceStream\b|\bT4LineMap\b|\bT5LineMap\b|\bT4BoxCodes\b|\bT4OtherInformationCodes\b|\bDictionary\s*<" },

        // AC-35 — the portal declares no tax-year policy of its own.
        { "tax-year policy", @"\bSupportedProcessingTaxYears\b|\bSupportedReviewTaxYears\b|\bSupportedTaxYears\b|\bTaxYear\b" },
    };

    [Theory] // AC-31, AC-32, AC-34, AC-35
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSource_ContainsNoForbiddenToken(string boundary, string pattern)
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
    public void NoCraLineNumberLiteralAppearsInThePortal() // AC-33
    {
        // CRA T1 line numbers are five digits. None may be hardcoded here —
        // every line number in the system originates from TaxLogicEngine's
        // embedded JSON config, reached only through a provider.
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
    public void NoTaxYearLiteralAppearsInThePortal() // AC-35
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
    public void ThePortalDeclaresNoBoxCodeOrCraLineConcept() // AC-33, AC-34
    {
        // A box→line table would make the web layer a second mapping authority.
        var boxOrLine = new Regex(
            @"\bBoxCode\b|\bBoxToLine\b|\bLineNumber\b|\bCraLine\b|\bBox\s*\d+\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => boxOrLine.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} names a box or CRA line concept: {string.Join(" | ", offending)}");
        }
    }

    // -------------------------------------------------------------- guards

    [Fact]
    public void TheScanner_SeesEveryProductionFileAndStillMatchesAKnownPattern()
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

        // Patterns that must match, proving the regex path works end to end. If
        // these stopped matching, the forbidden-token scans would be scanning
        // nothing.
        Assert.Matches(new Regex(@"\bAddTaxAiModules\b"), allExecutable);
        Assert.Matches(new Regex(@"\bMapGet\b"), allExecutable);
        Assert.Matches(new Regex(@"\bIT4LineMapProvider\b"), allExecutable);
    }

    [Fact]
    public void TheCommentFilter_RemovesDocumentationWithoutRemovingCode()
    {
        // The filter is what makes "executable lines only" safe. Prove both
        // directions on a file that deliberately documents a forbidden concept.
        var compositionRoot = Path.Combine(PortalRoot, "ModuleRegistration.cs");
        var raw = File.ReadAllText(compositionRoot);
        var executable = string.Join("\n", ExecutableLines(compositionRoot));

        // The documentation names a forbidden concept while explaining it...
        Assert.Contains("IDocumentExtractionIngress", raw, StringComparison.Ordinal);
        // ...the filter removes it, so honest comments stay writable...
        Assert.DoesNotContain("IDocumentExtractionIngress", executable, StringComparison.Ordinal);
        // ...while the code itself survives.
        Assert.Contains("AddSingleton", executable, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static string[] ProductionSourceFiles() =>
        HandwrittenSourceFiles(PortalRoot).OrderBy(path => path, StringComparer.Ordinal).ToArray();

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
