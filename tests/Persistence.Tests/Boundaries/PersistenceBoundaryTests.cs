using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Persistence.Tests.Boundaries;

/// <summary>
/// Persistence is the only project in this repository allowed to know EF Core
/// exists. Confining the ORM here is what turns "tax-domain code must not depend
/// on EF Core" into a fact about the reference graph rather than a rule someone
/// has to remember.
/// </summary>
public sealed class PersistenceBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string PersistenceRoot = Path.Combine(RepositoryRoot, "src", "Persistence");

    private static readonly string PersistenceProject =
        Path.Combine(PersistenceRoot, "Persistence.csproj");

    /// <summary>
    /// The baseline surface that every persistence implementation must retain.
    /// Later waves add files without invalidating this scanner guard.
    /// </summary>
    private static readonly string[] RequiredProductionFiles =
    {
        "AssemblyInfo.cs",
        "PersistenceRegistration.cs",
        "TaxAiDbContext.cs",
    };

    // -------------------------------------------------------- project shape

    [Fact] // AC-2
    public void TheProjectDeclaresTheApprovedSettings()
    {
        var project = XDocument.Load(PersistenceProject);

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);
        Assert.Equal("net8.0", Property(project, "TargetFramework"));
        Assert.Equal("enable", Property(project, "Nullable"));
    }

    [Fact] // AC-6
    public void TheProjectReferencesExactlyTenancy()
    {
        // It never names CaseReview, CaseProcessing, DocumentExtraction, or
        // TaxCases: taxpayer content reaches it only as an already-projected
        // Tenancy entity, so a tax module cannot leak into an EF model by
        // accident.
        Assert.Equal(new[] { "../Tenancy/Tenancy.csproj" }, ProjectReferences(PersistenceProject));
    }

    [Fact] // AC-8
    public void TheProjectUsesTheSqlServerProviderAndNoOther()
    {
        // One provider, pinned. A second would mean two answers about how a
        // decimal column round-trips, and the exact-round-trip refusal would then
        // depend on which one was configured.
        var packages = XDocument.Load(PersistenceProject)
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        Assert.Equal(
            new[] { "Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.SqlServer" },
            packages.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        Assert.All(packages.Values, version => Assert.Matches(@"^8\.0\.\d+$", version));

        foreach (var forbidden in new[] { "Npgsql", "MySql", "Oracle", "Sqlite", "Firebird" })
        {
            Assert.DoesNotContain(
                packages.Keys,
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact] // AC-7
    public void PersistenceIsTheOnlyProductionProjectThatReferencesEfCore()
    {
        // The whole point of the module. If a second production project ever
        // named EF Core, the dependency rule would be back to being a
        // convention.
        var offenders = Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .Select(directory => Path.Combine(directory, $"{Path.GetFileName(directory)}.csproj"))
            .Where(File.Exists)
            .Where(path => File.ReadAllText(path)
                .Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.Equal(new[] { "Persistence" }, offenders);
    }

    [Fact] // AC-9
    public void TheModuleTakesNoAspNetCoreDependency()
    {
        // The registration extension needs only the DI abstractions, which arrive
        // with EF Core. A storage module that knows about HTTP is a storage
        // module that will eventually read a request.
        Assert.DoesNotContain(
            "Microsoft.AspNetCore",
            File.ReadAllText(PersistenceProject),
            StringComparison.Ordinal);

        var offenders = ProductionSourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => entry.line.Contains("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.Empty(offenders);
    }

    // -------------------------------------------------------- forbidden tokens

    [Theory] // AC-14, AC-105, AC-112
    [InlineData("host", @"\bTaxAI\.Portal\b")]
    [InlineData("review module", @"\bCaseReview\b")]
    [InlineData("processing module", @"\bCaseProcessing\b")]
    [InlineData("extraction module", @"\bDocumentExtraction\b")]
    [InlineData("case module", @"\bTaxCases\b")]
    [InlineData("engine service", @"\bT4SlipParser\b")]
    [InlineData("engine service", @"\bT4LineMapper\b")]
    [InlineData("engine service", @"\bMoneyRounding\b")]
    [InlineData("masking", @"\bRecipientIdentifier\b")]
    [InlineData("tax year", @"\b(19|20)\d{2}\b")]
    [InlineData("CRA line", @"\b\d{5}\b")]
    public void ProductionSource_ContainsNoForbiddenToken(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var offenders = ProductionSourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => regex.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.True(offenders.Length == 0, $"{boundary} boundary crossed: {string.Join(" | ", offenders)}");
    }

    [Fact] // AC-124
    public void TheModuleCallsNoSchemaMutatingApi()
    {
        // Schema comes only from repository-owned migrations. A process that
        // reshapes a taxpayer database on boot is a process that can reshape it
        // wrongly, unattended, at the worst moment.
        foreach (var forbidden in new[] { "EnsureCreated", "EnsureDeleted", ".Migrate(", "MigrateAsync" })
        {
            var offenders = ProductionSourceFiles()
                .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
                .Where(entry => entry.line.Contains(forbidden, StringComparison.Ordinal))
                .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
                .ToArray();

            Assert.Empty(offenders);
        }
    }

    // -------------------------------------------------------------- guards

    [Fact]
    public void TheScanner_SeesTheRequiredProductionSurfaceAndStillMatchesKnownPatterns()
    {
        var files = ProductionSourceFiles();
        var fileNames = files.Select(Path.GetFileName).ToArray();

        Assert.All(
            RequiredProductionFiles,
            required => Assert.Contains(required, fileNames, StringComparer.Ordinal));
        Assert.All(files, path => Assert.False(IsBuildOutput(path)));
        Assert.Equal(files.Length, files.Distinct(StringComparer.Ordinal).Count());

        var allExecutable = string.Join("\n", files.SelectMany(ExecutableLines));

        Assert.True(
            allExecutable.Length > 200,
            $"Implausibly little executable source: {allExecutable.Length} chars.");

        Assert.Matches(new Regex(@"\bDbContext\b"), allExecutable);
        Assert.Matches(new Regex(@"\bAddTaxAiPersistence\b"), allExecutable);
    }

    [Fact]
    public void TheCommentFilter_RemovesDocumentationWithoutRemovingCode()
    {
        var context = Path.Combine(PersistenceRoot, "TaxAiDbContext.cs");
        var raw = File.ReadAllText(context);
        var executable = string.Join("\n", ExecutableLines(context));

        // The documentation names a forbidden concept while explaining it...
        Assert.Contains("migrate-on-startup", raw, StringComparison.Ordinal);
        // ...the filter removes it, so honest comments stay writable...
        Assert.DoesNotContain("migrate-on-startup", executable, StringComparison.Ordinal);
        // ...while the code itself survives.
        Assert.Contains("OnModelCreating", executable, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static string[] ProductionSourceFiles() =>
        Directory
            .EnumerateFiles(PersistenceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string Property(XDocument project, string name) =>
        project.Descendants().First(element => element.Name.LocalName == name).Value;

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
