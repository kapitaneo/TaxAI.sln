using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
// ReSharper disable once RedundantUsingDirective
using Microsoft.AspNetCore.Components;

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

    private static readonly string TenancyRoot =
        Path.Combine(RepositoryRoot, "src", "Tenancy");

    private static readonly string PersistenceRoot =
        Path.Combine(RepositoryRoot, "src", "Persistence");

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
    [InlineData("TargetFramework", "net10.0")]
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

        Assert.Equal("10.0.10", version);
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
    [InlineData("xunit", "2.9.3")]
    [InlineData("xunit.runner.visualstudio", "3.1.5")]
    [InlineData("Microsoft.NET.Test.Sdk", "18.8.1")]
    [InlineData("coverlet.collector", "10.0.1")]
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
        Assert.Equal(
            new[] { "net10.0" },
            Elements(TestProject, "TargetFramework")
                .Select(element => element.Value));

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

        var mvcTestingVersion = Assert.Single(
            Elements(TestProject, "PackageReference")
                .Where(element =>
                    element.Attribute("Include")?.Value ==
                    "Microsoft.AspNetCore.Mvc.Testing")
                .Select(element => element.Attribute("Version")?.Value));
        Assert.Equal("10.0.10", mvcTestingVersion);
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
    public void ThePortalsPublicSurfaceIsExactlyProgramAndTheApprovedComponents()
    {
        // Replaces spec 010's "the portal's only public type is Program",
        // superseded by spec 014 T01: the Razor compiler emits every component as a
        // public type, and a component's parameter has to be public to be settable.
        //
        // An exact set is strictly stronger than the single entry it replaces would
        // be if merely relaxed. It still forbids every public type the old rule
        // forbade — no public endpoint, guard, identity, authorization, content, or
        // ticket-store type — and additionally names which five components and four
        // page-model records were approved. Anything else is a failure.
        Assert.Equal(
            new[]
            {
                "Program",
                "TaxAI.Portal.Components.App",
                "TaxAI.Portal.Components.Layout.PortalLayout",
                "TaxAI.Portal.Components.Pages.ReviewBoxModel",
                "TaxAI.Portal.Components.Pages.ReviewLineModel",
                "TaxAI.Portal.Components.Pages.ReviewPage",
                "TaxAI.Portal.Components.Pages.ReviewPageModel",
                "TaxAI.Portal.Components.Pages.ReviewSlipModel",
                "TaxAI.Portal.Components.Routes",

                // The Razor compiler emits a type for _Imports.razor too. It is a
                // using list, declares no member, and renders nothing.
                "TaxAI.Portal.Components._Imports",
            },
            typeof(Program).Assembly
                .GetExportedTypes()
                .Select(type => type.FullName!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void NoPublicPortalTypeIsAnEndpointGuardOrIdentitySurface()
    {
        // The point of the exact set above, named so the failure is legible: the
        // host's security-relevant types stay internal.
        var publicNames = typeof(Program).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToArray();

        foreach (var forbidden in new[]
                 {
                     "ReviewEndpoints",
                     "FirmEndpoints",
                     "PortalAuthorization",
                     "PortalIdentity",
                     "PortalContent",
                     "PortalStartupGuards",
                     "SessionTicketStore",
                     "ModuleRegistration",
                 })
        {
            Assert.DoesNotContain(forbidden, publicNames);
        }
    }

    [Fact]
    public void TheReviewPageExposesExactlyOneRequiredModelParameter()
    {
        // One closed parameter, required, and no catch-all attribute dictionary —
        // an attribute splat would be an unbounded way for values to reach markup.
        var component = typeof(Program).Assembly
            .GetType("TaxAI.Portal.Components.Pages.ReviewPage", throwOnError: true)!;

        var parameters = component
            .GetProperties()
            .Where(property => property
                .GetCustomAttributes()
                .Any(attribute => attribute.GetType().Name == "ParameterAttribute"))
            .ToArray();

        var parameter = Assert.Single(parameters);

        Assert.Equal("Model", parameter.Name);
        Assert.Equal(
            "TaxAI.Portal.Components.Pages.ReviewPageModel",
            parameter.PropertyType.FullName);

        Assert.Contains(
            parameter.GetCustomAttributes(),
            attribute => attribute.GetType().Name == "EditorRequiredAttribute");

        Assert.DoesNotContain(
            component.GetProperties().SelectMany(property => property.GetCustomAttributes()),
            attribute => attribute.GetType().Name == "CascadingParameterAttribute");

        foreach (var property in parameters)
        {
            var parameterAttribute = property
                .GetCustomAttributes()
                .Single(attribute => attribute.GetType().Name == "ParameterAttribute");

            Assert.False(
                (bool)parameterAttribute.GetType()
                    .GetProperty("CaptureUnmatchedValues")!
                    .GetValue(parameterAttribute)!,
                "No component parameter may capture unmatched values.");
        }
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

    // ---------------------------------------------------- spec 011 AC-141–143

    [Fact]
    public void NewModuleInstructions_StateTheirExactBoundaries() // spec 011 AC-141
    {
        var tenancy = NormalizeWhitespace(
            File.ReadAllText(Path.Combine(TenancyRoot, "CLAUDE.md")));
        var persistence = NormalizeWhitespace(
            File.ReadAllText(Path.Combine(PersistenceRoot, "CLAUDE.md")));

        foreach (var required in new[]
                 {
                     "module boundary",
                     "production project references are exactly",
                     "CaseReview",
                     "TaxLogicEngine",
                     "PII posture",
                     "Forbidden dependencies",
                     "No tax-year policy of its own",
                 })
        {
            Assert.Contains(required, tenancy, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var required in new[]
                 {
                     "module boundary",
                     "only production project reference is exactly `Tenancy`",
                     "Microsoft.EntityFrameworkCore.SqlServer",
                     "Microsoft.EntityFrameworkCore.Design",
                     "PII posture",
                     "Forbidden dependencies",
                     "No tax-year policy of its own",
                 })
        {
            Assert.Contains(required, persistence, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory] // spec 011 AC-142
    [InlineData("Tenancy")]
    [InlineData("Persistence")]
    public void NewModuleAgentsBridges_ArePointersNotRulebooks(string module)
    {
        var agents = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", module, "AGENTS.md"));

        Assert.Contains("pointer", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("../../AGENTS.md", agents, StringComparison.Ordinal);
        Assert.Contains("../../CLAUDE.md", agents, StringComparison.Ordinal);
        Assert.Contains("(CLAUDE.md)", agents, StringComparison.Ordinal);

        Assert.True(
            agents.Split('\n').Length < 15,
            $"{module}/AGENTS.md must stay a pointer, not a rulebook.");
    }

    [Fact]
    public void PortalInstructions_StateTheNarrowedBoundaryAndGate() // spec 011 AC-143
    {
        var claude = NormalizeWhitespace(
            File.ReadAllText(Path.Combine(PortalRoot, "CLAUDE.md")));

        foreach (var required in new[]
                 {
                     "exactly four",
                     "TaxLogicEngine",
                     "CaseProcessing",
                     "Tenancy",
                     "Persistence",
                     "no SIN in any form",
                     "taxai_firm_id",
                     "read only from the application-issued",
                     "provider-supplied",
                     "never trusted",
                     "revokes the session server-side",
                     "computes no tax and implements no CRA mapping",
                     "No tax-year policy",
                     "single approved taxpayer-data surface",
                     "taxpayer display name",
                     "employer display name",
                     "opaque `recordId` route key",
                     "further approved `tax-domain` specification",
                 })
        {
            Assert.Contains(required, claude, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var gatedSurface in new[]
                 {
                     "uploading a document",
                     "mutating or deleting a stored package",
                     "provisioning a firm or membership",
                     "expanding the browser allowlist",
                     "adding a second taxpayer-data surface",
                 })
        {
            Assert.Contains(gatedSurface, claude, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PortalInstructions_RecordTheDevelopmentOnlyDeploymentRefusal()
    {
        var claude = NormalizeWhitespace(
            File.ReadAllText(Path.Combine(PortalRoot, "CLAUDE.md")));

        Assert.Contains(
            "this build starts in Development only",
            claude,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "may not be deployed anywhere that can receive real taxpayer data",
            claude,
            StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeWhitespace(string value) =>
        Regex.Replace(value, @"\s+", " ");

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
