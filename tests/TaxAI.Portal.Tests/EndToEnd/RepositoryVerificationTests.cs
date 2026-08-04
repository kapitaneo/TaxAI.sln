using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 010 T07, spec 011 T20, and spec 012 T01 — the documentation gate. Proves the
/// repository's own description of itself is true: the README describes the
/// repository that exists, names the identity/persistence capabilities that now
/// ship, names the one review surface, disclaims every broader taxpayer-data and
/// deployment capability that does not, and leaves the five pre-existing
/// modules' instructions untouched.
/// <para>
/// A README that describes a repository which is not there is the same
/// correctness hazard spec 005 fixed once already, and it is the one kind of
/// defect the compiler and every other suite here will never catch.
/// </para>
/// </summary>
public sealed class RepositoryVerificationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string Readme => File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));

    private static string RootInstructions => File.ReadAllText(Path.Combine(RepositoryRoot, "CLAUDE.md"));

    // ------------------------- AC-46: no existing instruction file was touched

    [Theory] // AC-46
    [InlineData("TaxLogicEngine")]
    [InlineData("TaxCases")]
    [InlineData("DocumentExtraction")]
    [InlineData("CaseProcessing")]
    [InlineData("CaseReview")]
    public void NoExistingModuleInstructionFileWasRewrittenForTheHost(string module)
    {
        // No module-boundary rule may be relaxed or reinterpreted to make room
        // for a web layer. If the host needed one of these loosened, the host
        // would be in the wrong place.
        var moduleRoot = Path.Combine(RepositoryRoot, "src", module);

        foreach (var instruction in new[] { "CLAUDE.md", "AGENTS.md" })
        {
            var path = Path.Combine(moduleRoot, instruction);
            Assert.True(File.Exists(path), $"{module}/{instruction} must still exist.");

            // The portal is not mentioned in any of them: these files predate it
            // and were not edited by spec 010.
            Assert.DoesNotContain(
                "TaxAI.Portal",
                File.ReadAllText(path),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------ AC-47: the README describes what is there

    [Theory] // AC-47
    [InlineData("src/TaxLogicEngine/")]
    [InlineData("src/TaxCases/")]
    [InlineData("src/DocumentExtraction/")]
    [InlineData("src/CaseProcessing/")]
    [InlineData("src/CaseReview/")]
    [InlineData("src/ClientIntake/")]
    [InlineData("src/Tenancy/")]
    [InlineData("src/Persistence/")]
    [InlineData("src/TaxAI.Portal/")]
    [InlineData("tests/TaxLogicEngine.Tests/")]
    [InlineData("tests/TaxCases.Tests/")]
    [InlineData("tests/DocumentExtraction.Tests/")]
    [InlineData("tests/CaseProcessing.Tests/")]
    [InlineData("tests/CaseReview.Tests/")]
    [InlineData("tests/ClientIntake.Tests/")]
    [InlineData("tests/Tenancy.Tests/")]
    [InlineData("tests/Persistence.Tests/")]
    [InlineData("tests/TaxAI.Portal.Tests/")]
    public void TheReadmeDocumentsEveryProjectTreeThatExists(string tree)
    {
        // The README's project block is indented, so match the directory name
        // rather than the full path prefix.
        var directory = tree.TrimEnd('/').Split('/').Last();

        Assert.Contains(directory, Readme, StringComparison.Ordinal);
    }

    [Fact] // AC-47
    public void TheReadmeDocumentsHowToRunTheApplication()
    {
        Assert.Contains("dotnet run --project src/TaxAI.Portal", Readme, StringComparison.Ordinal);
    }

    [Fact] // AC-47
    public void TheReadmeAssertsNoProjectCount()
    {
        // The stale "Solution (two projects)" claim is gone, and no count
        // replaces it. A count in prose goes stale exactly as silently as a
        // count in a test — and root CLAUDE.md forbids the latter.
        Assert.DoesNotContain("two projects", Readme, StringComparison.OrdinalIgnoreCase);

        foreach (var count in new[]
                 {
                     "one project", "three projects", "four projects", "five projects",
                     "six projects", "seven projects", "eight projects", "nine projects",
                     "ten projects", "eleven projects", "twelve projects",
                 })
        {
            Assert.DoesNotContain(count, Readme, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(@"\b\d+\s+projects\b", Readme);
    }

    // ---------------- spec 011 AC-145: exact capability and exclusion inventory

    [Fact]
    public void TheReadmeNamesTheFoundationCapabilitiesThatShip() // spec 011 AC-145
    {
        // Replaces spec 010's TheReadmeMakesNoClaimThePortalCannotBack. The old
        // blanket ban became false when spec 011 shipped identity and storage;
        // the replacement is an explicit allowlist, not a general permission to
        // claim that the portal is a taxpayer-data product surface.
        foreach (var capability in new[]
                 {
                     "OIDC sign-in",
                     "Revocable sessions",
                     "Tenancy and firm scoping",
                     "Database persistence",
                     "Retention and purge",
                     "firm-scoped read-only accountant review screen",
                     "Client intake experience",
                 })
        {
            Assert.Contains(capability, Readme, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheReadmeExplicitlyDisclaimsEveryUnshippedSurface() // spec 011 AC-145
    {
        var readme = NormalizeWhitespace(Readme);

        foreach (var disclaimer in new[]
                 {
                     "No taxpayer listing or unapproved taxpayer-data surface",
                     "No raw-content preview, read, or download surface",
                     "encrypted opaque source object outside web root",
                     "No OCR or LLM adapter ships.",
                     "No production deployment configuration ships.",
                     "No production provisioning ships",
                 })
        {
            Assert.Contains(disclaimer, readme, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var unsupportedClaim in new[]
                 {
                     "renders taxpayer records",
                     "upload your documents",
                     "extracts text from uploaded",
                     "ready for production deployment",
                     "provisions firms",
                     "creates production administrators",
                 })
        {
            Assert.DoesNotContain(
                unsupportedClaim,
                readme,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // AC-48
    public void TheReadmeClaimsNoWorkingOcrOrLlmIntegration()
    {
        // "OCR" may legitimately appear: DocumentExtraction exists precisely to
        // be the boundary that future OCR/LLM adapters will sit behind, and
        // describing it is not claiming it works. What the README may not do is
        // imply an adapter ships — so the disclaimer must be explicit and the
        // claiming phrasings must be absent.
        var readme = Readme;

        // Note the phrasings below are deliberately AFFIRMATIVE. "OCR/LLM
        // integration" itself is not forbidden, because the README's own
        // disclaimer sentence uses those exact words to say the portal has
        // none — a substring test cannot tell a claim from its negation, so it
        // must test only wording that could not appear in a disclaimer.
        Assert.Contains("No adapter implementation ships", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No OCR or LLM adapter ships", readme, StringComparison.OrdinalIgnoreCase);

        foreach (var claim in new[]
                 {
                     "extracts text from",
                     "reads your slips",
                     "scan a document",
                     "powered by AI",
                     "automatically imports",
                 })
        {
            Assert.DoesNotContain(claim, readme, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // AC-48
    public void TheReadmeDefinesTheOneRenderedSurfaceAndNoSinBoundary()
    {
        var readme = NormalizeWhitespace(Readme);

        foreach (var required in new[]
                 {
                     "one firm-scoped read-only accountant review screen",
                     "one exact immutable package version",
                     "closed allowlist",
                     "no SIN in any form",
                 })
        {
            Assert.Contains(required, readme, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------ AC-49: root instructions record the layout

    [Theory] // AC-49
    [InlineData("src/TaxAI.Portal/")]
    [InlineData("tests/TaxAI.Portal.Tests/")]
    [InlineData("src/Tenancy/")]
    [InlineData("src/Persistence/")]
    [InlineData("tests/Tenancy.Tests/")]
    [InlineData("tests/Persistence.Tests/")]
    public void TheRootInstructionsListTheNewTree(string tree)
    {
        Assert.Contains(tree, RootInstructions, StringComparison.Ordinal);
    }

    [Theory] // spec 011 AC-146
    [InlineData("Spec 011")]
    [InlineData("Tenant Identity and Persistence Foundation")]
    [InlineData("TaxLogicEngine")]
    [InlineData("CaseProcessing")]
    [InlineData("Tenancy")]
    [InlineData("Persistence")]
    [InlineData("no SIN in any form")]
    [InlineData("Spec 012")]
    [InlineData("same empty `404`")]
    [InlineData("taxpayer and employer display names")]
    [InlineData("narrowed, never deleted")]
    [InlineData("tax-domain")]
    public void TheRootInstructionsRecordTheDeliveredScope(string required)
    {
        Assert.Contains(required, RootInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // AC-49
    public void TheRootInstructionsStillForbidATotalProjectCount()
    {
        // The rule that cost PR #66 to unpick must survive every rewrite of this
        // section.
        Assert.Contains(
            "Never assert a total project count",
            RootInstructions,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheForwardRoadmapRecordsTheDeliveredReviewAndReservedIngress() // spec 011 AC-147
    {
        var next = NormalizeWhitespace(
            File.ReadAllText(Path.Combine(RepositoryRoot, "NEXT.md")));

        foreach (var required in new[]
                 {
                     "Spec 011",
                     "Spec 012",
                     "first taxpayer-data surface is complete",
                     "exact browser allowlist",
                     "separately approved intake surface",
                     "additional taxpayer-data surface",
                     "`tax-domain` specification",
                     "Question F",
                     "remains reserved",
                 })
        {
            Assert.Contains(required, next, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DocumentationRecordsTheProductRetentionDecision()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(RepositoryRoot, "README.md"),
                     Path.Combine(RepositoryRoot, "CLAUDE.md"),
                     Path.Combine(RepositoryRoot, "src", "Tenancy", "CLAUDE.md"),
                     Path.Combine(RepositoryRoot, "src", "Persistence", "CLAUDE.md"),
                 })
        {
            var text = NormalizeWhitespace(File.ReadAllText(path));

            Assert.Contains("seven-year", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("product decision", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not a CRA fact", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UserFacingDocumentationRecordsTheDevelopmentOnlyReleaseConstraint()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(RepositoryRoot, "README.md"),
                     Path.Combine(RepositoryRoot, "NEXT.md"),
                     Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "CLAUDE.md"),
                 })
        {
            var text = NormalizeWhitespace(File.ReadAllText(path));

            Assert.Contains(
                "starts in Development only",
                text,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "may not be deployed anywhere that can receive real taxpayer data",
                text,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------ spec 014 AC-99: the migration is storage-neutral

    /// <summary>
    /// Proves T01 changed nothing that owns stored data.
    /// <para>
    /// The task's claim is that a UI migration touched no storage. That is
    /// checkable rather than assertable in prose: the files that define the
    /// relational model and the persisted contracts either differ from
    /// <c>master</c> or they do not.
    /// </para>
    /// </summary>
    [Fact]
    public void TheReviewUiMigrationChangedNoPersistenceOrTenancyProductionFile()
    {
        // Every migration and model snapshot still exists exactly once, and the
        // review contract still declares the same two firm-scoped members.
        var migrations = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "Persistence"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutput(path))
            .Where(path =>
                path.Contains("Migrations", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(migrations);

        Assert.Single(migrations, path =>
            Path.GetFileName(path) == "20260730000000_AddClientIntake.cs");
        Assert.Single(migrations, path =>
            Path.GetFileName(path) == "20260730000000_AddClientIntake.Designer.cs");

        // Persistence receives only the approved inward references.
        Assert.Equal(
            new[]
            {
                "../ClientIntake/ClientIntake.csproj",
                "../ExtractionWorkflow/ExtractionWorkflow.csproj",
                "../TaxCases/TaxCases.csproj",
                "../Tenancy/Tenancy.csproj",
            },
            XDocument
                .Load(Path.Combine(RepositoryRoot, "src", "Persistence", "Persistence.csproj"))
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToArray());

        // And Tenancy's, still exactly CaseReview and TaxLogicEngine.
        Assert.Equal(
            new[] { "../CaseReview/CaseReview.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" },
            XDocument
                .Load(Path.Combine(RepositoryRoot, "src", "Tenancy", "Tenancy.csproj"))
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheHostReferencesTheIntakeContractAndNothingBeyondIt()
    {
        var portalProject = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "TaxAI.Portal.csproj"));

        Assert.Contains("ClientIntake", portalProject, StringComparison.Ordinal);
        Assert.Contains(
            typeof(Program).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.Equals("ClientIntake", StringComparison.OrdinalIgnoreCase));

        // The exact production reference set after spec 014.
        Assert.Equal(
            new[]
            {
                "../CaseProcessing/CaseProcessing.csproj",
                "../ClientIntake/ClientIntake.csproj",
                "../Persistence/Persistence.csproj",
                "../TaxLogicEngine/TaxLogicEngine.csproj",
                "../Tenancy/Tenancy.csproj",
            },
            XDocument
                .Load(Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "TaxAI.Portal.csproj"))
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToArray());
    }

    // ------------------- spec 014 T04: the guidance records a completed spec

    [Fact] // spec 014 AC-85
    public void TheRootInstructionsRecordSpecFourteenAsCompleteWithItsEvidence()
    {
        var instructions = NormalizeWhitespace(RootInstructions);

        foreach (var required in new[]
                 {
                     "Spec 014 (Client Intake and Document Upload Flow) is **complete**",
                     "verification.md",
                     "AC-1 through AC-99",
                 })
        {
            Assert.Contains(required, instructions, StringComparison.OrdinalIgnoreCase);
        }

        // The gate is recorded as narrowed, never deleted.
        foreach (var gate in new[]
                 {
                     "no raw-content",
                     "deployment refusal are unchanged",
                     "never** described as SIN-free",
                     "remains SIN-free in every form",
                 })
        {
            Assert.Contains(gate, instructions, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // spec 014 AC-85
    public void TheAcceptanceEvidenceFileExistsAndCoversEveryCriterion()
    {
        var verification = Path.Combine(
            RepositoryRoot,
            "specs",
            "014-client-intake-and-document-upload-flow",
            "verification.md");

        Assert.True(
            File.Exists(verification),
            "Spec 014's acceptance evidence must be recorded before completion.");

        var text = File.ReadAllText(verification);

        // Every criterion has its own evidence line. A missing one is the defect
        // this check exists to catch.
        for (var criterion = 1; criterion <= 99; criterion++)
        {
            Assert.Contains(
                $"AC-{criterion} ",
                text,
                StringComparison.Ordinal);
        }

        // And the file names the authoritative gate rather than asserting a
        // green run in prose alone.
        Assert.Contains(
            "scripts/Invoke-SolutionTests.ps1",
            text,
            StringComparison.Ordinal);
    }

    [Fact] // spec 014 AC-85
    public void TheForwardRoadmapRecordsSpecFourteenAsCompleteAndTheGatesAsOpen()
    {
        var next = NormalizeWhitespace(
            File.ReadAllText(Path.Combine(RepositoryRoot, "NEXT.md")));

        Assert.Contains(
            "**Spec 014 is complete.**",
            next,
            StringComparison.OrdinalIgnoreCase);

        // Completion of a spec never relaxes a gate.
        foreach (var gate in new[]
                 {
                     "Question F",
                     "remains reserved",
                     "raw-document view/download",
                     "another human-approved `tax-domain` specification",
                     "may not be deployed anywhere that can receive real taxpayer data",
                 })
        {
            Assert.Contains(gate, next, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsUnderBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ guard

    [Fact]
    public void TheDocumentationFilesAreAllPresentAndNonTrivial()
    {
        // Without this, a file emptied or renamed would make every "does not
        // contain" assertion above pass for free.
        foreach (var (path, minimumLength) in new[]
                 {
                     (Path.Combine(RepositoryRoot, "README.md"), 1500),
                     (Path.Combine(RepositoryRoot, "CLAUDE.md"), 5000),
                     (Path.Combine(RepositoryRoot, "NEXT.md"), 2000),
                     (Path.Combine(RepositoryRoot, "src", "Tenancy", "CLAUDE.md"), 2000),
                     (Path.Combine(RepositoryRoot, "src", "Tenancy", "AGENTS.md"), 100),
                     (Path.Combine(RepositoryRoot, "src", "Persistence", "CLAUDE.md"), 2000),
                     (Path.Combine(RepositoryRoot, "src", "Persistence", "AGENTS.md"), 100),
                     (Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "CLAUDE.md"), 1500),
                     (Path.Combine(RepositoryRoot, "src", "TaxAI.Portal", "AGENTS.md"), 100),
                     (Path.Combine(RepositoryRoot, "src", "ClientIntake", "CLAUDE.md"), 2000),
                     (Path.Combine(RepositoryRoot, "src", "ClientIntake", "AGENTS.md"), 100),
                 })
        {
            Assert.True(File.Exists(path), $"{path} must exist.");

            var length = File.ReadAllText(path).Length;

            Assert.True(
                length >= minimumLength,
                $"{Path.GetFileName(path)} is implausibly short ({length} chars); "
                + "the assertions above would pass vacuously.");
        }
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

    private static string NormalizeWhitespace(string value) =>
        Regex.Replace(value, @"\s+", " ");
}
