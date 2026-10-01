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
        //
        // Spec 016 T01 narrows this from the raw project text to the declared
        // package names: the host now references the TaxAI.AzureInfrastructure
        // project, whose name contains "Azure", while the Azure SDK itself stays
        // in that library. The host still declares no Azure package.
        Assert.DoesNotContain(
            Elements(PortalProject, "PackageReference")
                .Select(element => element.Attribute("Include")!.Value),
            package => package.Contains(forbidden, StringComparison.OrdinalIgnoreCase));

        // Positive control: the scanned set is the real package list.
        Assert.Contains(
            "Microsoft.AspNetCore.Authentication.OpenIdConnect",
            Elements(PortalProject, "PackageReference")
                .Select(element => element.Attribute("Include")!.Value));
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
                "../ClientIntake/ClientIntake.csproj",
                "../DocumentIntelligence/DocumentIntelligence.csproj",
                "../ExtractionWorkflow/ExtractionWorkflow.csproj",
                "../Persistence/Persistence.csproj",
                "../TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj",
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
        // Each approved type is asserted individually. The rule below still
        // forbids public host/security types, while avoiding a permanent total
        // that would make a later approved component retroactively fail T06.
        var exported = typeof(Program).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .ToArray();

        foreach (var approved in new[]
                 {
                 "Program",
                 "TaxAI.Portal.Components.App",
                 "TaxAI.Portal.Components.Layout.PortalLayout",
                 "TaxAI.Portal.Components.Pages.ClientIntakePage",
                 "TaxAI.Portal.Components.Pages.ClientLinkConfirmationPage",
                 "TaxAI.Portal.Components.Pages.IntakeCaseCreatePage",
                 "TaxAI.Portal.Components.Pages.IntakeCaseIssuedPage",
                 "TaxAI.Portal.Components.Pages.IntakeCaseProgressPage",
                 "TaxAI.Portal.Components.Pages.ExtractionFieldRowModel",
                 "TaxAI.Portal.Components.Pages.ExtractionListItemModel",
                 "TaxAI.Portal.Components.Pages.ExtractionListPage",
                 "TaxAI.Portal.Components.Pages.ExtractionListPageModel",
                 "TaxAI.Portal.Components.Pages.ExtractionReviewPage",
                 "TaxAI.Portal.Components.Pages.ExtractionReviewPageModel",
                 "TaxAI.Portal.Components.Pages.T4BoxDisplayModel",
                 "TaxAI.Portal.Components.Pages.T4ExtractionPanelModel",
                 "TaxAI.Portal.Components.Pages.MedicalEvidencePanelModel",
                 "TaxAI.Portal.Components.Pages.DuplicateCandidateModel",
                 "TaxAI.Portal.Components.Pages.ReviewBoxModel",
                 "TaxAI.Portal.Components.Pages.ReviewLineModel",
                 "TaxAI.Portal.Components.Pages.ReviewPage",
                 "TaxAI.Portal.Components.Pages.ReviewPageModel",
                 "TaxAI.Portal.Components.Pages.ReviewSlipModel",
                 "TaxAI.Portal.Components.Routes",
                 "TaxAI.Portal.Components.Shared.ClassificationDisagreementNotice",
                 "TaxAI.Portal.Components.Shared.ClientQuestionnairePanel",
                 "TaxAI.Portal.Components.Shared.DuplicateCandidateNotice",
                 "TaxAI.Portal.Components.Shared.ExpectedDocumentChecklist",
                 "TaxAI.Portal.Components.Shared.ExtractionFieldTable",
                 "TaxAI.Portal.Components.Shared.IntakeStatusBadge",
                 "TaxAI.Portal.Components.Shared.MedicalEvidencePanel",

                // The Razor compiler emits a type for _Imports.razor too. It is a
                // using list, declares no member, and renders nothing.
                "TaxAI.Portal.Components._Imports",
                 })
        {
            Assert.Equal(1, exported.Count(name =>
                string.Equals(name, approved, StringComparison.Ordinal)));
        }

        Assert.All(
            exported,
            name => Assert.True(
                string.Equals(name, "Program", StringComparison.Ordinal) ||
                name.StartsWith(
                    "TaxAI.Portal.Components.",
                    StringComparison.Ordinal),
                $"Unexpected public host type: {name}"));
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
                     "ExtractionEndpoints",
                     "IntakeEndpoints",
                     "ClientIntakeEndpoints",
                     "ClientIntakeCookie",
                     "IntakeHttpSecurity",
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
                     "production project references are exactly `ClientIntake`, `TaxCases`, and `Tenancy`",
                     "SqlIntakeStore",
                     "20260730000000_AddClientIntake",
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
                     "exactly five",
                     "TaxLogicEngine",
                     "CaseProcessing",
                     "ClientIntake",
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
                     "approved accountant/client intake flow",
                     "exactly six firm-scoped accountant routes",
                     "exactly six anonymous client routes",
                     "sole exception is the raw uploaded source byte stream",
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
                     "Reading/downloading raw content",
                     "mutating or deleting a stored review package",
                     "provisioning a firm or membership",
                     "expanding either browser allowlist",
                     "adding another taxpayer-data surface",
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

    [Fact] // spec 016 T03
    public void KeyProtectionInstructions_StateTheSliceTheyDeliver()
    {
        var infrastructure = NormalizeWhitespace(File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "TaxAI.AzureInfrastructure", "CLAUDE.md")));
        var portal = NormalizeWhitespace(File.ReadAllText(Path.Combine(PortalRoot, "CLAUDE.md")));
        var persistence = NormalizeWhitespace(File.ReadAllText(Path.Combine(PersistenceRoot, "CLAUDE.md")));

        foreach (var required in new[]
                 {
                     "Keys/KeyVaultContentKeyWrapper.cs",
                     "Keys/KeyVersionUsageGuard.cs",
                     "Keys/KeyVaultRuntimeMaterialProvider.cs",
                     "Protection/AzureDataProtectionConfiguration.cs",
                     "Configuration/ApprovedResourceProbe.cs",
                     "exactly one project and nine packages",
                     "No key leaves Key Vault",
                 })
        {
            Assert.Contains(required, infrastructure, StringComparison.Ordinal);
        }

        foreach (var required in new[]
                 {
                     "Spec 016 T03: key protection in Production",
                     "declares no Azure package",
                     "never loaded over",
                     "No new `TaxAi:Production:*` setting exists",
                     "Production admission is still refused",
                 })
        {
            Assert.Contains(required, portal, StringComparison.Ordinal);
        }

        Assert.Contains("Spec 016 T03 tightens `SqlConnectionStringGuard`", persistence, StringComparison.Ordinal);
        Assert.Contains("no cloud SDK, credential or key dependency", persistence, StringComparison.Ordinal);
    }

    [Fact] // spec 016 T06
    public void RecoveryInstructions_StateTheSliceTheyDeliver()
    {
        var portal = NormalizeWhitespace(File.ReadAllText(Path.Combine(PortalRoot, "CLAUDE.md")));
        var persistence = NormalizeWhitespace(File.ReadAllText(Path.Combine(PersistenceRoot, "CLAUDE.md")));
        var tenancy = NormalizeWhitespace(File.ReadAllText(Path.Combine(TenancyRoot, "CLAUDE.md")));

        foreach (var claim in new[]
                 {
                     "Spec 016 T06", "RecoveryOrchestrator", "database incarnation", "quarantine",
                     "RegionalRecovery", "never fails over", "manually activated Canada East recovery host",
                 })
        {
            Assert.Contains(claim, portal, StringComparison.Ordinal);
        }

        foreach (var claim in new[] { "Spec 016 T06", "SqlRecoveryReconciler", "no migration", "Expired credentials" })
        {
            Assert.Contains(claim, persistence, StringComparison.Ordinal);
        }

        foreach (var claim in new[] { "Spec 016 T06", "IRecoveryControlStore", "RecoveryReleasePolicy", "no firm-scope exemption" })
        {
            Assert.Contains(claim, tenancy, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("admits real taxpayer", portal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // spec 016 T03: the host still carries no Azure SDK package.
    public void PortalProject_GainsNoPackageFromTheKeyProtectionSlice()
    {
        var packages = XDocument.Load(PortalProject)
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")!.Value)
            .ToArray();

        Assert.Equal(new[] { "Microsoft.AspNetCore.Authentication.OpenIdConnect" }, packages);
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
