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

    private static readonly string PersistenceTestProject = Path.Combine(
        RepositoryRoot,
        "tests",
        "Persistence.Tests",
        "Persistence.Tests.csproj");

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
        Assert.Equal("net10.0", Property(project, "TargetFramework"));
        Assert.Equal("enable", Property(project, "Nullable"));
    }

    [Fact] // AC-6
    public void TheProjectReferencesExactlyTheApprovedModules()
    {
        Assert.Equal(
            new[]
            {
                "../ClientIntake/ClientIntake.csproj",
                "../ExtractionWorkflow/ExtractionWorkflow.csproj",
                "../TaxCases/TaxCases.csproj",
                "../Tenancy/Tenancy.csproj",
            },
            ProjectReferences(PersistenceProject));
    }

    [Fact] // AC-8
    public void TheProjectUsesTheSqlServerProviderAndNoOther()
    {
        // One provider, pinned. A second would mean two answers about how a
        // decimal column round-trips, and the exact-round-trip refusal would then
        // depend on which one was configured.
        var project = XDocument.Load(PersistenceProject);
        var packageReferences = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToArray();
        var packages = packageReferences
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        Assert.Equal(
            new[] { "Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.SqlServer" },
            packages.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        Assert.All(packages.Values, version => Assert.Equal("10.0.10", version));

        var design = Assert.Single(
            packageReferences,
            element =>
                element.Attribute("Include")!.Value ==
                "Microsoft.EntityFrameworkCore.Design");
        Assert.Equal("all", design.Attribute("PrivateAssets")?.Value);

        foreach (var forbidden in new[] { "Npgsql", "MySql", "Oracle", "Sqlite", "Firebird" })
        {
            Assert.DoesNotContain(
                packages.Keys,
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheTestProjectTargetsNet10AndPinsTheApprovedPackagesExactly()
    {
        var project = XDocument.Load(PersistenceTestProject);

        Assert.Equal("net10.0", Property(project, "TargetFramework"));

        var packages = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        var expectedPackages = new[]
        {
            "coverlet.collector",
            "Microsoft.EntityFrameworkCore.Sqlite",
            "Microsoft.NET.Test.Sdk",
            "SQLitePCLRaw.bundle_e_sqlite3",
            "xunit",
            "xunit.runner.visualstudio",
        };
        Assert.Equal(
            expectedPackages.OrderBy(name => name, StringComparer.Ordinal),
            packages.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("10.0.1", packages["coverlet.collector"]);
        Assert.Equal("10.0.10", packages["Microsoft.EntityFrameworkCore.Sqlite"]);
        Assert.Equal("18.8.1", packages["Microsoft.NET.Test.Sdk"]);
        Assert.Equal("2.1.12", packages["SQLitePCLRaw.bundle_e_sqlite3"]);
        Assert.Equal("2.9.3", packages["xunit"]);
        Assert.Equal("3.1.5", packages["xunit.runner.visualstudio"]);
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
    [InlineData("engine service", @"\bT4SlipParser\b")]
    [InlineData("engine service", @"\bT4LineMapper\b")]
    [InlineData("engine service", @"\bMoneyRounding\b")]
    [InlineData("masking", @"\bRecipientIdentifier\b")]
    [InlineData("reflection bypass", @"\bSystem\.Reflection\b")]
    [InlineData("reflection bypass", @"\bActivator\.CreateInstance\b")]
    [InlineData("reflection bypass", @"\bBindingFlags\.(Instance|NonPublic)\b")]
    [InlineData("reflection bypass", @"\b(GetConstructors?|ConstructorInfo|InvokeMember)\b")]
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

    [Fact]
    public void TheSupportedIntakeYearAppearsOnlyInItsSchemaConstraint()
    {
        var year = new Regex(@"\b2025\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var offenders = ProductionSourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => year.IsMatch(entry.line))
            .Select(entry => (File: Path.GetFileName(entry.path), entry.line))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "20260730000000_AddClientIntake.Designer.cs",
                "20260730000000_AddClientIntake.cs",
                "20260803000000_AddDocumentExtraction.Designer.cs",
                "IntakeModelConfiguration.cs",
                "TaxAiDbContextModelSnapshot.cs",
            },
            offenders.Select(entry => entry.File).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(offenders, entry => Assert.Contains("[TaxYear] = 2025", entry.line));
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

    // ------------------------------------------ spec 016 T03: SQL protection

    [Theory] // T03: key and credential handling belong to the Azure library, never here.
    [InlineData("cloud SDK", @"\bAzure\.(Identity|Core|Security|Storage|Extensions)\b")]
    [InlineData("credential", @"\b(ManagedIdentityCredential|DefaultAzureCredential|ClientSecretCredential|TokenCredential)\b")]
    [InlineData("key vault", @"\bKeyVault\w*|\bCryptographyClient\b|\bSecretClient\b")]
    [InlineData("TLS bypass", @"ServerCertificateCustomValidationCallback|DangerousAcceptAnyServerCertificateValidator|TrustServerCertificate\s*=\s*true")]
    public void ProductionSource_HoldsNoKeyCredentialOrTransportBypass(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var offenders = ProductionSourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => regex.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.True(offenders.Length == 0, $"{boundary} boundary crossed: {string.Join(" | ", offenders)}");

        // Positive control: each pattern recognizes a planted line.
        Assert.Matches(regex, boundary switch
        {
            "cloud SDK" => "using Azure.Identity;",
            "credential" => "new DefaultAzureCredential()",
            "key vault" => "new SecretClient(uri, credential)",
            _ => "TrustServerCertificate = true",
        });
    }

    [Fact] // T03: the connection guard names exactly the approved authentication modes.
    public void TheConnectionGuardAcceptsOnlyTheApprovedAuthenticationModes()
    {
        var guard = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PersistenceRoot, "Services", "SqlConnectionStringGuard.cs")));

        var named = Regex.Matches(guard, @"SqlAuthenticationMethod\.(?<mode>\w+)")
            .Select(match => match.Groups["mode"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(mode => mode, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "ActiveDirectoryMSI", "ActiveDirectoryManagedIdentity", "NotSpecified", "SqlPassword" },
            named);
        Assert.Contains("HostNameInCertificate", guard, StringComparison.Ordinal);
        Assert.Contains("TrustServerCertificate", guard, StringComparison.Ordinal);
        Assert.Contains("PersistSecurityInfo", guard, StringComparison.Ordinal);
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
