using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaxAI.Portal.Tests;

/// <summary>
/// T01 — proves spec 010's Wave 0 contract: the two projects exist at their
/// approved paths with the approved settings, the dependency arrows point
/// inward, and the portal's instruction files carry the boundary and the gate
/// that bind every later task.
/// <para>
/// Every solution assertion is about <b>presence and uniqueness</b>, never about
/// a total project count. Pinning the solution to "exactly N projects" is what
/// blocked spec 007's Wave 0 outright and required PR #66 to unpick; the root
/// <c>CLAUDE.md</c> forbids it.
/// </para>
/// </summary>
public sealed class ProjectStructureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string PortalRoot =
        Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");

    private static readonly string PortalProject =
        Path.Combine(PortalRoot, "TaxAI.Portal.csproj");

    private static readonly string TestProject = Path.Combine(
        RepositoryRoot, "tests", "TaxAI.Portal.Tests", "TaxAI.Portal.Tests.csproj");

    // ------------------------------------------------------ AC-1, AC-2, AC-3

    [Fact]
    public void PortalProject_UsesTheAspNetCoreWebSdk() // AC-1
    {
        Assert.True(File.Exists(PortalProject), $"{PortalProject} must exist.");

        Assert.Equal(
            "Microsoft.NET.Sdk.Web",
            XDocument.Load(PortalProject).Root!.Attribute("Sdk")!.Value);
    }

    [Theory] // AC-2
    [InlineData("TargetFramework", "net8.0")]
    [InlineData("Nullable", "enable")]
    [InlineData("ImplicitUsings", "enable")]
    public void PortalProject_DeclaresTheApprovedProperty(string property, string expected)
    {
        var values = XDocument.Load(PortalProject)
            .Descendants()
            .Where(element => element.Name.LocalName == property)
            .Select(element => element.Value)
            .ToArray();

        Assert.Equal(new[] { expected }, values);
    }

    [Fact]
    public void PortalProject_DeclaresExactlyTheOneApprovedPackage() // spec 011 AC-11
    {
        // Replaces spec 010's "the portal declares no PackageReference at all",
        // superseded by FR-13: OpenID Connect is the one protocol handler that
        // is not in the shared framework, so authentication cannot be reached
        // without it.
        //
        // An exact one-entry set is strictly stronger than a bare emptiness
        // assertion would be if it were merely relaxed: it still forbids every
        // package the old rule forbade, and additionally names which single
        // addition was approved rather than permitting "at most a few".
        Assert.Equal(
            new[] { "Microsoft.AspNetCore.Authentication.OpenIdConnect" },
            Elements(PortalProject, "PackageReference")
                .Select(element => element.Attribute("Include")!.Value)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void PortalProject_PinsTheApprovedPackageToAnExactVersion() // spec 011 AC-11
    {
        // Pinned exactly, not floated. A floating range on the component that
        // validates identity tokens means the code deciding who a caller is can
        // change between two builds of the same commit.
        var version = Assert.Single(
            Elements(PortalProject, "PackageReference")
                .Where(element =>
                    element.Attribute("Include")?.Value ==
                    "Microsoft.AspNetCore.Authentication.OpenIdConnect")
                .Select(element => element.Attribute("Version")?.Value));

        Assert.NotNull(version);
        Assert.Matches(new Regex(@"^8\.0\.\d+$"), version!);
    }

    [Theory] // spec 011 AC-11
    [InlineData("EntityFrameworkCore")]
    [InlineData("Sqlite")]
    [InlineData("SqlClient")]
    [InlineData("Azure")]
    [InlineData("Serilog")]
    [InlineData("ApplicationInsights")]
    [InlineData("OpenTelemetry")]
    public void PortalProject_DeclaresNoOtherPackage(string forbidden)
    {
        // The exact-set assertion above already forbids these; naming the ones
        // most likely to be reached for makes the failure legible rather than a
        // set mismatch nobody reads.
        Assert.DoesNotContain(
            forbidden,
            File.ReadAllText(PortalProject),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortalProject_DeclaresNoInternalsVisibleToGrant()
    {
        // The repository's three grants are pre-existing and stay that way;
        // neither new project adds one. Response bodies are asserted against
        // literals written independently in this project instead.
        Assert.DoesNotContain(
            "InternalsVisibleTo",
            File.ReadAllText(PortalProject),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "InternalsVisibleTo",
            File.ReadAllText(TestProject),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ AC-4

    [Theory] // AC-4
    [InlineData("xunit", "2.5.3")]
    [InlineData("xunit.runner.visualstudio", "2.5.3")]
    [InlineData("Microsoft.NET.Test.Sdk", "17.8.0")]
    [InlineData("coverlet.collector", "6.0.0")]
    public void TestProject_PinsTheRepositoryStandardToolingVersion(string package, string expected)
    {
        Assert.True(File.Exists(TestProject), $"{TestProject} must exist.");

        var versions = Elements(TestProject, "PackageReference")
            .Where(element => element.Attribute("Include")?.Value == package)
            .Select(element => element.Attribute("Version")!.Value)
            .ToArray();

        Assert.Equal(new[] { expected }, versions);
    }

    [Fact]
    public void TestProject_AddsOnlyTheInProcessWebTestHost()
    {
        // One new package enters the repository, in a test project, and it binds
        // no network port. Anything else here would be a dependency the plan did
        // not approve.
        var extra = Elements(TestProject, "PackageReference")
            .Select(element => element.Attribute("Include")!.Value)
            .Except(new[]
            {
                "xunit",
                "xunit.runner.visualstudio",
                "Microsoft.NET.Test.Sdk",
                "coverlet.collector",
            })
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Microsoft.AspNetCore.Mvc.Testing" }, extra);
    }

    // ------------------------------------------------------------------ AC-5

    [Theory] // AC-5
    [InlineData("\"TaxAI.Portal\", \"src\\TaxAI.Portal\\TaxAI.Portal.csproj\"")]
    [InlineData("\"TaxAI.Portal.Tests\", \"tests\\TaxAI.Portal.Tests\\TaxAI.Portal.Tests.csproj\"")]
    public void Solution_DeclaresTheProjectExactlyOnceAtItsApprovedPath(string declaration)
    {
        var projectLines = SolutionProjectLines();

        Assert.NotEmpty(projectLines);

        // Presence-and-uniqueness only: the solution must stay free to declare
        // later modules' projects without this test failing.
        Assert.Equal(
            1,
            projectLines.Count(line => line.Contains(declaration, StringComparison.Ordinal)));
    }

    [Fact]
    public void Solution_StillDeclaresEveryPreExistingProjectExactlyOnce()
    {
        // The two blocks were appended; nothing was rewritten on the way past.
        var projectLines = SolutionProjectLines();

        foreach (var declaration in new[]
                 {
                     "\"TaxLogicEngine\", \"src\\TaxLogicEngine\\TaxLogicEngine.csproj\"",
                     "\"TaxLogicEngine.Tests\", \"tests\\TaxLogicEngine.Tests\\TaxLogicEngine.Tests.csproj\"",
                     "\"TaxCases\", \"src\\TaxCases\\TaxCases.csproj\"",
                     "\"TaxCases.Tests\", \"tests\\TaxCases.Tests\\TaxCases.Tests.csproj\"",
                     "\"DocumentExtraction\", \"src\\DocumentExtraction\\DocumentExtraction.csproj\"",
                     "\"DocumentExtraction.Tests\", \"tests\\DocumentExtraction.Tests\\DocumentExtraction.Tests.csproj\"",
                     "\"CaseProcessing\", \"src\\CaseProcessing\\CaseProcessing.csproj\"",
                     "\"CaseProcessing.Tests\", \"tests\\CaseProcessing.Tests\\CaseProcessing.Tests.csproj\"",
                     "\"CaseReview\", \"src\\CaseReview\\CaseReview.csproj\"",
                     "\"CaseReview.Tests\", \"tests\\CaseReview.Tests\\CaseReview.Tests.csproj\"",
                 })
        {
            Assert.Equal(
                1,
                projectLines.Count(line => line.Contains(declaration, StringComparison.Ordinal)));
        }
    }

    // ------------------------------------------------------------ AC-8, AC-9

    [Fact]
    public void PortalProject_ReferencesExactlyTheFourInwardModules() // spec 011 AC-10
    {
        // Replaces spec 010's two-entry assertion (superseded by FR-12). An
        // exact four-entry set is strictly stronger than the pair it replaces:
        // it still forbids everything the old one forbade, and now also names
        // which additions were approved.
        Assert.Equal(
            new[]
            {
                "../CaseProcessing/CaseProcessing.csproj",
                "../Persistence/Persistence.csproj",
                "../TaxLogicEngine/TaxLogicEngine.csproj",
                "../Tenancy/Tenancy.csproj",
            },
            ProjectReferences(PortalProject));
    }

    [Theory] // spec 011 AC-10, AC-13
    [InlineData("TaxCases")]
    [InlineData("CaseReview")]
    [InlineData("DocumentExtraction")]
    public void PortalProject_ReferencesNoModuleReachedThroughAContract(string module)
    {
        // The storage layer's own dependencies arrive behind its contracts. A
        // direct reference here would let the host name a taxpayer-data type,
        // which is exactly what the gate stops.
        Assert.DoesNotContain(
            module,
            File.ReadAllText(PortalProject),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestProject_ReferencesOnlyThePortal() // AC-9
    {
        Assert.Equal(
            new[] { "../../src/TaxAI.Portal/TaxAI.Portal.csproj" },
            ProjectReferences(TestProject));
    }

    // --------------------------------------------------- the public surface

    [Fact]
    public void ThePortalsOnlyPublicTypeIsProgram()
    {
        // Program is public solely so the in-process test host can find the
        // entry point. Everything else the portal declares stays internal.
        Assert.Equal(
            new[] { "Program" },
            typeof(Program).Assembly
                .GetExportedTypes()
                .Select(type => type.FullName!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Program_DeclaresNoPublicMemberOfItsOwn()
    {
        const BindingFlags declared =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        Assert.Empty(typeof(Program).GetMethods(declared));
        Assert.Empty(typeof(Program).GetProperties(declared));
        Assert.Empty(typeof(Program).GetFields(declared));
        Assert.Empty(typeof(Program).GetEvents(declared));

        // At most the implicit parameterless constructor.
        Assert.All(
            typeof(Program).GetConstructors(declared),
            constructor => Assert.Empty(constructor.GetParameters()));
    }

    // ---------------------------------------------------------- AC-44, AC-45

    [Theory] // AC-44
    [InlineData("TaxLogicEngine")]
    [InlineData("CaseProcessing")]
    [InlineData("inward")]
    [InlineData("no tax")]
    [InlineData("CRA")]
    [InlineData("tax-year")]
    [InlineData("taxpayer data")]
    [InlineData("PII")]
    [InlineData("input")]
    [InlineData("Class A")]
    [InlineData("tax-domain")]
    public void PortalInstructions_StateTheBoundaryAndTheGate(string required)
    {
        var claude = File.ReadAllText(Path.Combine(PortalRoot, "CLAUDE.md"));

        Assert.Contains(required, claude, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortalInstructions_ForbidTaxpayerDataSurfacesUntilTheClassAQuestionsAreAnswered() // AC-44
    {
        var claude = File.ReadAllText(Path.Combine(PortalRoot, "CLAUDE.md"));

        // The gate names each surface it stops, so nobody has to infer the list.
        foreach (var surface in new[]
                 {
                     "renders taxpayer data",
                     "accepts user input",
                     "uploads a document",
                     "persists data",
                     "session",
                     "identity",
                     "separate specification",
                 })
        {
            Assert.Contains(surface, claude, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PortalAgentsBridge_IsAPointerNotARulebook() // AC-45
    {
        var agents = File.ReadAllText(Path.Combine(PortalRoot, "AGENTS.md"));

        Assert.Contains("pointer", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("../../AGENTS.md", agents, StringComparison.Ordinal);
        Assert.Contains("../../CLAUDE.md", agents, StringComparison.Ordinal);
        Assert.Contains("(CLAUDE.md)", agents, StringComparison.Ordinal);

        Assert.True(
            agents.Split('\n').Length < 15,
            "AGENTS.md must stay a pointer, not a rulebook.");
    }

    // ---------------------------------------------------------------- helpers

    private static string[] SolutionProjectLines() =>
        File.ReadAllLines(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

    private static IEnumerable<XElement> Elements(string projectPath, string name) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == name);

    private static string[] ProjectReferences(string projectPath) =>
        Elements(projectPath, "ProjectReference")
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
