using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaxCases.Models;
using TaxCases.Services;

namespace TaxCases.Tests.Boundaries;

/// <summary>
/// Enforces the spec-006 module boundary, AC-34, AC-40, and the Wave-0
/// instruction contract without reaching into production implementation state.
/// </summary>
public sealed class TaxCasesBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void SolutionAndProjects_HaveTheApprovedPathsAndDirection()
    {
        var solutionPath = Path.Combine(RepositoryRoot, "TaxAI.sln");
        var taxCasesProject = Path.Combine(RepositoryRoot, "src", "TaxCases", "TaxCases.csproj");
        var engineProject = Path.Combine(RepositoryRoot, "src", "TaxLogicEngine", "TaxLogicEngine.csproj");
        var taxCasesTestsProject = Path.Combine(RepositoryRoot, "tests", "TaxCases.Tests", "TaxCases.Tests.csproj");
        var engineTestsProject = Path.Combine(RepositoryRoot, "tests", "TaxLogicEngine.Tests", "TaxLogicEngine.Tests.csproj");

        Assert.True(File.Exists(solutionPath));
        Assert.True(File.Exists(taxCasesProject));
        Assert.True(File.Exists(engineProject));
        Assert.True(File.Exists(taxCasesTestsProject));
        Assert.True(File.Exists(engineTestsProject));

        var solutionLines = File.ReadAllText(solutionPath).SplitLines().ToArray();
        string[] spec006Declarations =
        [
            "\"TaxCases\", \"src\\TaxCases\\TaxCases.csproj\"",
            "\"TaxLogicEngine\", \"src\\TaxLogicEngine\\TaxLogicEngine.csproj\"",
            "\"TaxCases.Tests\", \"tests\\TaxCases.Tests\\TaxCases.Tests.csproj\"",
            "\"TaxLogicEngine.Tests\", \"tests\\TaxLogicEngine.Tests\\TaxLogicEngine.Tests.csproj\""
        ];

        // Each spec 006 project is declared exactly once at its approved path. The
        // solution may also declare later modules' projects, so this deliberately
        // asserts presence-and-uniqueness rather than a total project count: spec 006's
        // boundary is that *its* four projects are correct, not that no others exist.
        foreach (var declaration in spec006Declarations)
        {
            Assert.Equal(
                1,
                solutionLines.Count(line =>
                    line.StartsWith("Project(\"", StringComparison.Ordinal) &&
                    line.Contains(declaration, StringComparison.Ordinal)));
        }

        Assert.Equal(
            ["../TaxLogicEngine/TaxLogicEngine.csproj"],
            ProjectReferences(taxCasesProject));
        Assert.Empty(ProjectReferences(engineProject));
        Assert.Equal(
            ["../../src/TaxCases/TaxCases.csproj"],
            ProjectReferences(taxCasesTestsProject));
        Assert.Equal(
            ["../../src/TaxLogicEngine/TaxLogicEngine.csproj"],
            ProjectReferences(engineTestsProject));
    }

    [Fact]
    public void TaxCasesAssembly_HasNoNonFrameworkDependencyExceptTaxLogicEngine()
    {
        var nonFrameworkReferences = typeof(TaxCase).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("System", StringComparison.Ordinal) &&
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["TaxLogicEngine"], nonFrameworkReferences);
    }

    [Fact]
    public void SupportedTaxYearPolicy_IsExactly2025AndLivesOnlyInSupportedTaxYears()
    {
        Assert.Equal([2025], SupportedTaxYears.All);
        Assert.True(SupportedTaxYears.IsSupported(2025));
        Assert.False(SupportedTaxYears.IsSupported(2024));
        Assert.False(SupportedTaxYears.IsSupported(2026));

        var filesContainingPolicyYear = ProductionSourceFiles()
            .Where(path => File.ReadAllText(path).Contains("2025", StringComparison.Ordinal))
            .Select(RelativeToTaxCases)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Services/SupportedTaxYears.cs"], filesContainingPolicyYear);
    }

    [Fact]
    public void WaveZeroInstructions_RetainPointersInventoryAndBindingRules()
    {
        var moduleRoot = Path.Combine(RepositoryRoot, "src", "TaxCases");
        var bridge = File.ReadAllText(Path.Combine(moduleRoot, "AGENTS.md"));
        var instructions = File.ReadAllText(Path.Combine(moduleRoot, "CLAUDE.md"));

        Assert.Contains("../../AGENTS.md", bridge, StringComparison.Ordinal);
        Assert.Contains("../../CLAUDE.md", bridge, StringComparison.Ordinal);
        Assert.Contains("[CLAUDE.md](CLAUDE.md)", bridge, StringComparison.Ordinal);
        Assert.Contains("pointer only", bridge, StringComparison.OrdinalIgnoreCase);

        string[] requiredInstructionText =
        [
            "dependency is inward",
            "ParseResult<T>",
            "ValidationError",
            "RecipientIdentifier",
            "exactly tax year 2025",
            "SafeDiagnosticText",
            "JsonIgnore",
            "deterministic",
            "provider-independent",
            "ASP.NET Core",
            "EF Core",
            "system-clock",
            "mapping-resource",
            "Implement only the explicit TaskID",
            "dotnet test TaxAI.sln",
            "never merge"
        ];

        foreach (var required in requiredInstructionText)
        {
            Assert.Contains(required, instructions, StringComparison.OrdinalIgnoreCase);
        }

        string[] publicInventory =
        [
            "RelationshipKind", "DocumentCategory", "CaseStatus", "CaseChangeKind",
            "ReadinessState", "ReadinessReasonKind", "IntakeQuestionScope",
            "CaseParticipant", "ReceivedEvidence", "EvidenceRequirement", "IntakeQuestion",
            "IntakeAnswer", "RequirementChecklist", "ReadinessReason", "ReviewReadiness",
            "CaseChangeRecord", "CaseOperationOutcome", "TaxCase", "CreateTaxCaseRequest",
            "ParticipantDescriptor", "EvidenceDescriptor", "EvidenceRequirementDescriptor",
            "IntakeAnswerDescriptor", "ChecklistDescriptor", "SupportedTaxYears",
            "CaseStatusTransitions", "ReadinessCalculator"
        ];

        foreach (var typeName in publicInventory)
        {
            Assert.Contains(typeName, instructions, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("2025")]
    [InlineData("SafeDiagnosticText")]
    [InlineData("JsonIgnore")]
    [InlineData("ASP.NET Core")]
    [InlineData("EF Core")]
    [InlineData("fail-closed")]
    [InlineData("dependency is inward")]
    public void LocalAgentsBridge_DoesNotDuplicateModulePolicy(string policyText)
    {
        var bridge = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "TaxCases", "AGENTS.md"));

        Assert.DoesNotContain(policyText, bridge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionProject_HasNoForbiddenSdkPackageResourceOrContentReference()
    {
        var projectPath = Path.Combine(RepositoryRoot, "src", "TaxCases", "TaxCases.csproj");
        var project = XDocument.Load(projectPath);

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);
        Assert.Empty(Elements(project, "PackageReference"));
        Assert.Empty(Elements(project, "FrameworkReference"));
        Assert.Empty(Elements(project, "EmbeddedResource"));
        Assert.Empty(Elements(project, "Content"));
        Assert.Equal(["../TaxLogicEngine/TaxLogicEngine.csproj"], ProjectReferences(projectPath));
    }

    [Fact]
    public void TaxLogicEngine_HasNoReverseTaxCasesDependency()
    {
        var engineRoot = Path.Combine(RepositoryRoot, "src", "TaxLogicEngine");
        var engineProject = Path.Combine(engineRoot, "TaxLogicEngine.csproj");

        Assert.Empty(ProjectReferences(engineProject));

        var reverseReferences = Directory
            .EnumerateFiles(engineRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsHandwrittenSource)
            .Where(path => File.ReadAllText(path).Contains("TaxCases", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(engineRoot, path))
            .ToArray();

        Assert.Empty(reverseReferences);
    }

    [Theory]
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSource_ContainsNoForbiddenDependencyOrBehavior(
        string boundary,
        string pattern)
    {
        var regex = new Regex(
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Select(path => new
            {
                Path = RelativeToTaxCases(path),
                Text = File.ReadAllText(path)
            })
            .Where(source => regex.IsMatch(source.Text))
            .Select(source => source.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Forbidden {boundary} pattern /{pattern}/ found in: {string.Join(", ", violations)}");
    }

    public static IEnumerable<object[]> ForbiddenProductionPatterns()
    {
        yield return ["ASP.NET Core/API host", @"Microsoft\.AspNetCore|\bWebApplication\b|\bControllerBase\b|\bApiController\b|\bMap(?:Get|Post|Put|Delete)\s*\("];
        yield return ["EF Core", @"Microsoft\.EntityFrameworkCore|\bDbContext\b|\bDbSet\s*<"];
        yield return ["SQL/database", @"Microsoft\.Data\.SqlClient|\bSystem\.Data\b|\bSql(?:Connection|Command|DataReader)\b|\bDb(?:Connection|Command|DataReader)\b|\bNpgsql\b|\bDapper\b|\bDatabase\b"];
        yield return ["filesystem I/O", @"\bSystem\.IO\b|\b(?:File|Directory|Path)\s*\.|\bFileStream\b|\bFileSystemWatcher\b"];
        yield return ["network I/O", @"\bSystem\.Net\b|\bHttpClient\b|\bWebRequest\b|\bSocket\b|\bDns\b|\bTcpClient\b|\bUdpClient\b"];
        yield return ["system clock", @"\bDateTime(?:Offset)?\s*\.\s*(?:Now|UtcNow|Today)\b|\bTimeProvider\s*\.\s*System\b"];
        yield return ["environment/current user", @"\bEnvironment\s*\.|\bClaimsPrincipal\b|\bWindowsIdentity\b|\bCurrentPrincipal\b|\bCurrentUser\b"];
        yield return ["OCR/LLM", @"\bOCR\b|\bLLM\b|\bOpenAI\b|\bAnthropic\b"];
        yield return ["cloud SDK", @"\bAzure\s*\.|\bAmazon\s*\.|\bAWS\b|\bGoogle\.Cloud\b|\bFirebase\b|\bCloud\b"];
        yield return ["email", @"\bEmail\b|\bSmtpClient\b|\bMailMessage\b|\bMailKit\b|\bMimeKit\b|\bSendGrid\b"];
        yield return ["generated identifiers/randomness", @"\bGuid\s*\.\s*NewGuid\b|\bUlid\s*\.\s*NewUlid\b|\bRandom\s*\.\s*Shared\b|\bnew\s+Random\b"];
        yield return ["money/tax calculation", @"\bdecimal\b|\bMoney\b|\bAmount\b|\bTaxableIncome\b|\bTaxCalculation\b|\bCalculateTax\b|\bTaxRate\b|\bTaxCredit\b|\bIncomeTax\b"];
        yield return ["CRA line/box mapping", @"\bCRA\b|\bBoxToLine\b|\bLineMap\b|\bCraLine\b|\bBox\d+\b|\bLine\d+\b|TaxLogicEngine\.Mappings|GetManifestResourceStream|\bEmbeddedResource\b"];
        yield return ["API/output DTO", @"\b[A-Za-z0-9_]*Dto\b|\bIHost\b|Host\s*\.\s*CreateDefaultBuilder"];
        yield return ["Archived lifecycle", @"\bArchived\b"];
    }

    private static string[] ProjectReferences(string projectPath) =>
        Elements(XDocument.Load(projectPath), "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<XElement> Elements(XDocument document, string localName) =>
        document.Descendants().Where(element => element.Name.LocalName == localName);

    private static IEnumerable<string> ProductionSourceFiles() =>
        Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "TaxCases"), "*.cs", SearchOption.AllDirectories)
            .Where(IsHandwrittenSource);

    private static bool IsHandwrittenSource(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string RelativeToTaxCases(string path) =>
        Path.GetRelativePath(Path.Combine(RepositoryRoot, "src", "TaxCases"), path).Replace('\\', '/');

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

internal static class BoundaryStringExtensions
{
    internal static IEnumerable<string> SplitLines(this string value) =>
        value.Split(["\r\n", "\n"], StringSplitOptions.None);
}
