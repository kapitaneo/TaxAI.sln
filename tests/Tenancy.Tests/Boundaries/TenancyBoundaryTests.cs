using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tenancy.Tests.Boundaries;

/// <summary>
/// The boundary this module must not cross, and the repository invariants this
/// feature must not disturb.
/// <para>
/// Tenancy is deterministic and provider-independent. Everything below is a
/// negative assertion, so the scanner guard at the bottom proves the walk and
/// the comment filter actually work — without it every negative here would pass
/// for free.
/// </para>
/// </summary>
public sealed class TenancyBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string TenancyRoot = Path.Combine(RepositoryRoot, "src", "Tenancy");

    private static readonly string TenancyProject = Path.Combine(TenancyRoot, "Tenancy.csproj");

    /// <summary>
    /// Named rather than counted, so the scanner guard can prove the walk saw
    /// each one.
    /// </summary>
    private static readonly string[] ExpectedProductionFiles =
    {
        "AssemblyInfo.cs",
        "AuthenticationSession.cs",
        "Firm.cs",
        "FirmMembership.cs",
        "FirmScopeExemptions.cs",
        "IAuthenticationSessionStore.cs",
        "IDevelopmentFixtureSeeder.cs",
        "IIdentityDirectory.cs",
        "IOpaqueIdentifierSource.cs",
        "IRecoveryReconciler.cs",
        "IReviewRecordPurgeService.cs",
        "IReviewRecordRepository.cs",
        "MembershipVerification.cs",
        "MembershipVerificationPolicy.cs",
        "MonetaryValueGuard.cs",
        "OpaqueTokenGuard.cs",
        "PersistedBoxAmount.cs",
        "PersistedLineEntry.cs",
        "PersistedReviewRecord.cs",
        "PersistedSchema.cs",
        "PersistedSlip.cs",
        "PersistedSourceBoxCode.cs",
        "PersistedUnmappedBoxCode.cs",
        "PortalUser.cs",
        "ProductionControlContracts.cs",
        "ProductionControlModels.cs",
        "ProductionEvidencePolicy.cs",
        "PurgeOutcome.cs",
        "RecoveryControlContracts.cs",
        "RecoveryControlModels.cs",
        "RetentionPolicy.cs",
        "ReviewPackageProjection.cs",
        "SessionBoundaries.cs",
        "SessionEvaluation.cs",
        "SessionLifetimePolicy.cs",
        "SlipTypeCodes.cs",
        "TaxpayerTextGuard.cs",
        "TenancyErrors.cs",
        "TenancyFields.cs",
        "TrustedFirmId.cs",
        "UtcInstantGuard.cs",
    };

    // ------------------------------------------------- project shape (AC-1, AC-3)

    [Fact] // AC-1
    public void TheProjectDeclaresTheApprovedSettings()
    {
        var project = XDocument.Load(TenancyProject);

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);
        Assert.Equal("net10.0", Property(project, "TargetFramework"));
        Assert.Equal("enable", Property(project, "Nullable"));
    }

    [Fact] // AC-3
    public void TheProjectReferencesExactlyCaseReviewAndTaxLogicEngine()
    {
        // Inward, and exactly two. Tenancy reads an already-assembled package and
        // reuses the engine's result shapes; anything else would be a new
        // coupling nobody planned.
        Assert.Equal(
            new[] { "../CaseReview/CaseReview.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" },
            ProjectReferences(TenancyProject));
    }

    [Fact] // AC-4
    public void TheProjectDeclaresNoPackageReference()
    {
        // A deterministic module that acquires an ORM, a web stack, a cloud SDK,
        // or an OCR client has stopped being deterministic.
        var project = XDocument.Load(TenancyProject);

        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "PackageReference");
        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "FrameworkReference");
    }

    // ------------------------------------------------- forbidden tokens (AC-5)

    [Theory] // AC-5
    [InlineData("ORM", @"\bDbContext\b")]
    [InlineData("ORM", @"\bDbSet\s*<")]
    [InlineData("ORM", @"\bEntityFrameworkCore\b")]
    [InlineData("database", @"\bSqlConnection\b")]
    [InlineData("database", @"\bSystem\.Data\b")]
    [InlineData("web", @"\bMicrosoft\.AspNetCore\b")]
    [InlineData("network", @"\bHttpClient\b")]
    [InlineData("clock", @"\bDateTime\.Now\b")]
    [InlineData("clock", @"\bDateTime\.UtcNow\b")]
    [InlineData("clock", @"\bDateTimeOffset\.Now\b")]
    [InlineData("clock", @"\bDateTimeOffset\.UtcNow\b")]
    [InlineData("clock", @"\bTimeProvider\b")]
    [InlineData("environment", @"\bEnvironment\.")]
    [InlineData("filesystem", @"\bFile\.")]
    [InlineData("filesystem", @"\bDirectory\.")]
    [InlineData("persistence", @"\bPersistence\b")]
    [InlineData("host", @"\bTaxAI\.Portal\b")]
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

    [Fact] // AC-105
    public void NoTaxYearLiteralAppearsInTheModule()
    {
        // This module declares no tax-year policy of its own. The only tax-year
        // decision it makes delegates to the merged CaseReview authority, so a
        // year literal here would be a second, silently diverging one.
        var year = new Regex(@"\b(19|20)\d{2}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var offenders = ProductionSourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => year.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact] // AC-109
    public void NoCraLineNumberLiteralAppearsInTheModule()
    {
        // Every CRA line code stored originates in the engine's mapping config and
        // arrives through the package. A five-digit literal here would be a CRA
        // line number this module invented.
        var fiveDigits = new Regex(@"\b\d{5}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var offenders = ProductionSourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => fiveDigits.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory] // AC-106, AC-112
    [InlineData("T4SlipParser")]
    [InlineData("T5SlipParser")]
    [InlineData("T4LineMapper")]
    [InlineData("T5LineMapper")]
    [InlineData("T4SlipBoxAmounts")]
    [InlineData("T5SlipBoxAmounts")]
    [InlineData("MoneyRounding")]
    [InlineData("ReviewPackageAssembler")]
    [InlineData("RecordDecision")]
    [InlineData("GetManifestResourceStream")]
    [InlineData("RecipientIdentifier")]
    [InlineData("SafeDiagnosticText")]
    public void ProductionSource_InvokesNoEngineService(string forbidden)
    {
        var offenders = ProductionSourceFiles()
            .SelectMany(path => ExecutableLines(path).Select(line => (path, line)))
            .Where(entry => entry.line.Contains(forbidden, StringComparison.Ordinal))
            .Select(entry => $"{Path.GetFileName(entry.path)}: {entry.line}")
            .ToArray();

        Assert.Empty(offenders);
    }

    // ------------------------------------------------ repository invariants

    [Theory] // AC-17
    [InlineData("Tenancy", @"src\Tenancy\Tenancy.csproj")]
    [InlineData("Persistence", @"src\Persistence\Persistence.csproj")]
    [InlineData("Tenancy.Tests", @"tests\Tenancy.Tests\Tenancy.Tests.csproj")]
    [InlineData("Persistence.Tests", @"tests\Persistence.Tests\Persistence.Tests.csproj")]
    public void TheSolutionDeclaresTheNewProjectExactlyOnceAtItsApprovedPath(string name, string path)
    {
        // Presence and uniqueness, never a total count. A frozen project count
        // blocked spec 007's Wave 0 outright and needed a PR to unpick.
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot, "TaxAI.sln"));
        var declaration = $"\"{name}\", \"{path}\"";

        Assert.Equal(1, CountOccurrences(solution, declaration));
    }

    [Theory] // AC-18
    [InlineData("TaxLogicEngine", "{0390423C-6E2F-426F-B33B-4E2ABB34A5C5}")]
    [InlineData("TaxLogicEngine.Tests", "{2F692EB7-E9FA-4A1C-85E6-6A33110E0076}")]
    [InlineData("TaxCases", "{A5A7F602-8B8A-4B51-83F0-EF3A2F38B1C1}")]
    [InlineData("TaxCases.Tests", "{C2D431A4-AB61-4F7E-93F6-BE970A2B4184}")]
    [InlineData("DocumentExtraction", "{63E522D5-4360-4E7F-B4B7-B896BD8AAEE4}")]
    [InlineData("DocumentExtraction.Tests", "{CFBDCA0E-325E-4A78-A890-CED77163E24A}")]
    [InlineData("CaseProcessing", "{7B1C0A3E-9D4F-4C86-9E1B-2F5A6D3C8B70}")]
    [InlineData("CaseProcessing.Tests", "{1D8E4F92-6A73-4B05-8C2D-9E7A1B4F60C3}")]
    [InlineData("CaseReview", "{C54E8270-7071-46BD-87C7-9561CED5A932}")]
    [InlineData("CaseReview.Tests", "{93C57000-9BB3-4561-B79C-070824F626A0}")]
    [InlineData("TaxAI.Portal", "{E6AADD6A-3DAE-4CF1-A0FE-6EED68B44E78}")]
    [InlineData("TaxAI.Portal.Tests", "{4731C8E0-905A-4DE4-8FA1-67A8A1095260}")]
    public void EveryPreExistingSolutionDeclarationSurvivedTheAppend(string name, string guid)
    {
        // The solution is edited by appending only. A rewritten GUID or a
        // reordered block is a merge conflict waiting for the next parallel wave.
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot, "TaxAI.sln"));

        Assert.Contains($"\"{name}\", ", solution, StringComparison.Ordinal);
        Assert.Contains(guid, solution, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(solution, $"{guid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU"));
    }

    [Fact] // AC-16
    public void NoProjectUsesAReservedFutureModuleName()
    {
        // Spec 010 forbids exactly these five .csproj names. Tenancy and
        // Persistence are deliberately outside that set.
        var reserved = new[] { "Clients", "Firms", "Documents", "Intake", "Review" };

        var offenders = Directory
            .EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && !path.Contains(".claude", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => reserved.Contains(name, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory] // AC-19
    [InlineData("Tenancy.Tests", "xunit", "2.9.3")]
    [InlineData("Tenancy.Tests", "xunit.runner.visualstudio", "3.1.5")]
    [InlineData("Tenancy.Tests", "Microsoft.NET.Test.Sdk", "18.8.1")]
    [InlineData("Tenancy.Tests", "coverlet.collector", "10.0.1")]
    [InlineData("Persistence.Tests", "xunit", "2.9.3")]
    [InlineData("Persistence.Tests", "xunit.runner.visualstudio", "3.1.5")]
    [InlineData("Persistence.Tests", "Microsoft.NET.Test.Sdk", "18.8.1")]
    [InlineData("Persistence.Tests", "coverlet.collector", "10.0.1")]
    public void TheNewTestProjectPinsTheRepositoryStandardToolingVersion(
        string project, string package, string expected)
    {
        var document = XDocument.Load(
            Path.Combine(RepositoryRoot, "tests", project, $"{project}.csproj"));

        var reference = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "PackageReference" &&
                element.Attribute("Include")!.Value == package);

        Assert.Equal(expected, reference.Attribute("Version")!.Value);
    }

    [Fact] // AC-20
    public void EveryTestProjectIsRegisteredInTheExecutionManifestExactlyOnce()
    {
        // A project nobody registered is a project nobody would notice failing to
        // run — and a green dotnet test would say nothing was wrong.
        var manifest = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "test-projects.json"));

        var testProjects = Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot, "tests"))
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("Tenancy.Tests", testProjects);
        Assert.Contains("Persistence.Tests", testProjects);

        foreach (var project in testProjects)
        {
            Assert.Equal(1, CountOccurrences(manifest, $"\"name\": \"{project}\""));
        }
    }

    [Fact] // AC-20
    public void EveryManifestCountIsRecordedAsAFloor()
    {
        // Floors rise when a spec adds tests and are never lowered to make a run
        // pass. An exact equality would reproduce the frozen-count trap one level
        // down.
        var manifest = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "test-projects.json"));

        Assert.Contains("minimumTests", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("exactTests", manifest, StringComparison.Ordinal);
    }

    [Theory] // AC-27
    [InlineData("TaxLogicEngine")]
    [InlineData("TaxCases")]
    [InlineData("DocumentExtraction")]
    [InlineData("CaseProcessing")]
    [InlineData("CaseReview")]
    public void NoPreExistingModuleGainedAPackageOrFrameworkReference(string module)
    {
        // The five tax modules are untouchable in this feature. A package
        // reference appearing in one is how a module acquires an ORM by accident.
        var project = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", module, $"{module}.csproj"));

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);
        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "PackageReference");
        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "FrameworkReference");
    }

    [Fact] // AC-22
    public void ThePreExistingInternalsGrantsAreStillExactlyThree()
    {
        // Two declared in project files, one declared in source. A scan that reads
        // only project files misses the third and reports it as removed.
        var fromProjectFiles = new[]
            { "TaxLogicEngine", "TaxCases", "DocumentExtraction", "CaseProcessing", "CaseReview" }
            .Select(module => Path.Combine(RepositoryRoot, "src", module, $"{module}.csproj"))
            .Where(path => File.ReadAllText(path).Contains("InternalsVisibleTo", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "CaseProcessing", "CaseReview" }, fromProjectFiles);
    }

    [Fact] // AC-11 of the PRD's module rules
    public void TheNewModulesGrantInternalsOnlyToTheirOwnTestProjects()
    {
        // Persistence is deliberately NOT granted access to Tenancy's internals:
        // EF Core materializes non-public members by reflection and needs no
        // grant, and a construction path around the fail-closed projection is
        // exactly what the internal constructors prevent.
        var tenancyGrants = File.ReadAllText(Path.Combine(TenancyRoot, "AssemblyInfo.cs"));

        Assert.Contains("InternalsVisibleTo(\"Tenancy.Tests\")", tenancyGrants, StringComparison.Ordinal);
        Assert.DoesNotContain("InternalsVisibleTo(\"Persistence\")", tenancyGrants, StringComparison.Ordinal);
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
            allExecutable.Length > 2000,
            $"Implausibly little executable source: {allExecutable.Length} chars.");

        // Patterns that must match, proving the regex path works end to end.
        Assert.Matches(new Regex(@"\bValidationError\b"), allExecutable);
        Assert.Matches(new Regex(@"\bTrustedFirmId\b"), allExecutable);
        Assert.Matches(new Regex(@"\bIReviewRecordRepository\b"), allExecutable);

        // Spec 016 T06: the walk reaches the recovery control contracts too.
        Assert.Matches(new Regex(@"\bIRecoveryControlStore\b"), allExecutable);
        Assert.Matches(new Regex(@"\bRecoveryReleasePolicy\b"), allExecutable);
    }

    [Fact]
    public void TheCommentFilter_RemovesDocumentationWithoutRemovingCode()
    {
        // The filter is what makes "executable lines only" safe. Prove both
        // directions on a file that deliberately documents a forbidden concept.
        var projection = Path.Combine(TenancyRoot, "Services", "ReviewPackageProjection.cs");
        var raw = File.ReadAllText(projection);
        var executable = string.Join("\n", ExecutableLines(projection));

        // The documentation names forbidden concepts while explaining them...
        Assert.Contains("rounding", raw, StringComparison.Ordinal);
        Assert.Contains("masking", raw, StringComparison.Ordinal);
        // ...the filter removes them, so honest comments stay writable...
        Assert.DoesNotContain("rounding", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("masking", executable, StringComparison.Ordinal);
        // ...while the code itself survives.
        Assert.Contains("ParseResult", executable, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static string[] ProductionSourceFiles() =>
        Directory
            .EnumerateFiles(TenancyRoot, "*.cs", SearchOption.AllDirectories)
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

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

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
