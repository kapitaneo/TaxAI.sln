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
        "PortalStartupGuards.cs",
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
    public void TheCompiledPortal_ReferencesExactlyTheFourInwardModules() // spec 011 AC-10, AC-13
    {
        // The project file states intent; the assembly states what actually
        // compiled in. A transitive leak — a type named in passing — would show
        // up here and nowhere else. Tenancy references the review module and
        // Persistence references Tenancy, yet neither of those may reach this
        // assembly's reference list, because the host names none of their types.
        //
        // The framework filter is wider than the class libraries' equivalent by
        // necessity: a web host references dozens of shared-framework
        // assemblies. Anything outside System*/Microsoft.*/netstandard is a real
        // dependency of ours.
        var nonFrameworkReferences = CompiledNonFrameworkReferences();

        Assert.Equal(
            new[] { "CaseProcessing", "Persistence", "TaxLogicEngine", "Tenancy" },
            nonFrameworkReferences);
    }

    [Theory] // spec 011 AC-13
    [InlineData("TaxCases")]
    [InlineData("CaseReview")]
    [InlineData("DocumentExtraction")]
    public void TheCompiledPortal_NamesNoModuleItReachesThroughAContract(string module)
    {
        Assert.DoesNotContain(module, CompiledNonFrameworkReferences());
    }

    [Fact]
    public void ThePortalProject_ReferencesNeitherTaxCasesNorCaseReview() // AC-40, spec 011 AC-13
    {
        var projectText = File.ReadAllText(Path.Combine(PortalRoot, "TaxAI.Portal.csproj"));

        // CaseReview exists and is merged; the portal still may not depend on
        // it. An AccountantReviewPackage is taxpayer data, and rendering it is
        // exactly what the gate stops.
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

    [Theory] // spec 011 AC-12
    [InlineData("EntityFrameworkCore")]
    [InlineData("DbContext")]
    [InlineData("DbSet")]
    [InlineData("SqlConnection")]
    [InlineData("System.Data")]
    [InlineData("Sqlite")]
    [InlineData("Npgsql")]
    public void ThePortalProject_DeclaresNoOrmDependency(string forbidden)
    {
        // The storage module exposes one registration extension, which is what
        // keeps the ORM out of this project file entirely. If it ever appears
        // here, the host has acquired a second way to reach the database — one
        // that answers to no repository and no trusted firm.
        Assert.DoesNotContain(
            forbidden,
            File.ReadAllText(Path.Combine(PortalRoot, "TaxAI.Portal.csproj")),
            StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------- AC-22: wiring lives in one place

    /// <summary>
    /// Anything shaped like a registration: <c>Add…</c> or <c>TryAdd…</c>
    /// followed by a capital. Deliberately wider than a list of the container's
    /// own method names, so a module's bespoke <c>AddSomething</c> extension
    /// cannot slip past by not being called <c>AddSingleton</c>.
    /// </summary>
    private static readonly Regex RegistrationVerb = new(
        @"\b(?:Add|TryAdd)[A-Z][A-Za-z0-9_]*",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The exact registration-shaped tokens each production file may use.
    /// Replaces spec 010's "no registration outside the composition root, and
    /// exactly three inside it": an exact per-file allow-list still forbids
    /// every registration the old rule forbade, and additionally forbids an
    /// unapproved one from appearing <i>inside</i> the composition root.
    /// </summary>
    public static TheoryData<string, string[]> ApprovedRegistrationTokens() => new()
    {
        // AddServerHeader is an option assignment on Kestrel, not a
        // registration; it is enumerated rather than special-cased so nothing is
        // silently exempt. AddTaxAiModules is the single call into the
        // composition root.
        { "Program.cs", new[] { "AddServerHeader", "AddTaxAiModules" } },
        { "ModuleRegistration.cs", new[] { "AddSingleton", "AddTaxAiModules", "AddTaxAiPersistence" } },
        { "PortalContent.cs", Array.Empty<string>() },
        { "PortalStartupGuards.cs", Array.Empty<string>() },
    };

    [Theory] // AC-22, spec 011 AC-31
    [MemberData(nameof(ApprovedRegistrationTokens))]
    public void EveryRegistrationTokenIsApprovedForItsFile(string file, string[] approved)
    {
        var path = Path.Combine(PortalRoot, file);
        Assert.True(File.Exists(path), $"{path} must exist.");

        var used = RegistrationVerb
            .Matches(string.Join("\n", ExecutableLines(path)))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(approved.OrderBy(token => token, StringComparer.Ordinal).ToArray(), used);
    }

    [Fact] // AC-22
    public void TheAllowListCoversEveryProductionFile()
    {
        // Without this, adding a file would exempt it from the rule above
        // instead of failing.
        Assert.Equal(
            ExpectedProductionFiles,
            ApprovedRegistrationTokens()
                .Select(row => (string)row[0]!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact] // AC-22, FR-14
    public void TheCompositionRootCallsExactlyOneRegistrationExtensionPerContributingModule()
    {
        // The container's own verbs are how this file registers the three
        // in-process singletons; anything else it calls is another module's
        // single entry point. One per module or the ORM package ends up in this
        // project's file.
        var compositionRoot = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ModuleRegistration.cs")));

        var moduleExtensions = RegistrationVerb
            .Matches(compositionRoot)
            .Select(match => match.Value)
            .Where(token => token is not ("AddSingleton" or "AddScoped" or "AddTransient" or "AddTaxAiModules"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "AddTaxAiPersistence" }, moduleExtensions);

        // Each of those extensions is declared exactly once, by the module that
        // owns it.
        Assert.Equal(
            1,
            ModuleSourceFiles("Persistence")
                .Sum(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"IServiceCollection\s+AddTaxAiPersistence\s*\(",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)).Count));

        // And the three spec 010 singletons are still exactly three.
        Assert.Equal(
            3,
            new Regex(@"\bAddSingleton\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Matches(compositionRoot)
                .Count);
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
        Assert.Matches(new Regex(@"\bAddTaxAiPersistence\b"), allExecutable);
        Assert.Matches(new Regex(@"\bRequireSafeStartup\b"), allExecutable);
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

    private static string[] ModuleSourceFiles(string module) =>
        HandwrittenSourceFiles(Path.Combine(RepositoryRoot, "src", module))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] CompiledNonFrameworkReferences() =>
        typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
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
