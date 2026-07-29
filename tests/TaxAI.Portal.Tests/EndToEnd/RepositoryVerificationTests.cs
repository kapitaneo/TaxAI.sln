using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 010 T07 plus spec 011 T20 — the documentation gate. Proves the
/// repository's own description of itself is true: the README describes the
/// repository that exists, names the identity/persistence capabilities that now
/// ship, disclaims every taxpayer-data and deployment capability that does not,
/// and leaves the five pre-existing modules' instructions untouched.
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
    [InlineData("src/Tenancy/")]
    [InlineData("src/Persistence/")]
    [InlineData("src/TaxAI.Portal/")]
    [InlineData("tests/TaxLogicEngine.Tests/")]
    [InlineData("tests/TaxCases.Tests/")]
    [InlineData("tests/DocumentExtraction.Tests/")]
    [InlineData("tests/CaseProcessing.Tests/")]
    [InlineData("tests/CaseReview.Tests/")]
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
                 })
        {
            Assert.Contains(capability, Readme, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheReadmeExplicitlyDisclaimsEveryUnshippedSurface() // spec 011 AC-145
    {
        foreach (var disclaimer in new[]
                 {
                     "No taxpayer-data screen ships.",
                     "No document upload ships.",
                     "No OCR or LLM adapter ships.",
                     "No production deployment configuration ships.",
                     "No production provisioning ships",
                 })
        {
            Assert.Contains(disclaimer, Readme, StringComparison.OrdinalIgnoreCase);
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
                Readme,
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
    public void TheReadmeSaysPlainlyThatNoTaxpayerDataIsRendered()
    {
        Assert.Contains("no taxpayer data", Readme, StringComparison.OrdinalIgnoreCase);
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
    public void TheForwardRoadmapRecordsTheResolvedAndReservedQuestions() // spec 011 AC-147
    {
        var next = NormalizeWhitespace(
            File.ReadAllText(Path.Combine(RepositoryRoot, "NEXT.md")));

        foreach (var required in new[]
                 {
                     "Spec 011",
                     "question A",
                     "question B",
                     "SIN part of question C",
                     "question D",
                     "question E",
                     "Question F",
                     "remains reserved",
                     "Rendering taxpayer data remains a separate `tax-domain` specification",
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
