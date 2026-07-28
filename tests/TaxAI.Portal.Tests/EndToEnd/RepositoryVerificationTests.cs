namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// T07 — the final documentation gate. Proves the repository's own description
/// of itself is true: that the README describes the repository that exists,
/// claims nothing the portal cannot back, and that no existing module's
/// instruction file was rewritten to accommodate a web host.
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
    [InlineData("src/TaxAI.Portal/")]
    [InlineData("tests/TaxLogicEngine.Tests/")]
    [InlineData("tests/TaxCases.Tests/")]
    [InlineData("tests/DocumentExtraction.Tests/")]
    [InlineData("tests/CaseProcessing.Tests/")]
    [InlineData("tests/CaseReview.Tests/")]
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

    // ------------------------- AC-48: the README claims nothing the portal lacks

    [Theory] // AC-48
    [InlineData("authentication")]
    [InlineData("user account")]
    [InlineData("log in")]
    [InlineData("sign in")]
    [InlineData("document upload")]
    [InlineData("database")]
    [InlineData("deployment")]
    public void TheReadmeMakesNoClaimThePortalCannotBack(string capability)
    {
        // The README may name these only to say they do not exist. Every
        // occurrence must sit inside the section that says so.
        var readme = Readme;

        var index = readme.IndexOf(capability, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var disclaimer = readme.IndexOf(
                "### What the portal deliberately is not", StringComparison.Ordinal);

            Assert.True(
                disclaimer >= 0 && index > disclaimer,
                $"README mentions '{capability}' outside the section that disclaims it.");

            index = readme.IndexOf(capability, index + 1, StringComparison.OrdinalIgnoreCase);
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
    public void TheRootInstructionsListTheNewTree(string tree)
    {
        Assert.Contains(tree, RootInstructions, StringComparison.Ordinal);
    }

    [Theory] // AC-49
    [InlineData("Spec 010")]
    [InlineData("Portal Foundation")]
    [InlineData("composition root")]
    [InlineData("omission")]
    [InlineData("Class A")]
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
    public void TheForwardRoadmapRecordsWhatBlocksTheNextPortalFeature()
    {
        var next = File.ReadAllText(Path.Combine(RepositoryRoot, "NEXT.md"));

        Assert.Contains("Spec 010", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Class A", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("taxpayer data", next, StringComparison.OrdinalIgnoreCase);

        // The roadmap must no longer ask for the spec this feature delivered.
        Assert.DoesNotContain(
            "Create the **Portal Foundation** spec",
            next,
            StringComparison.OrdinalIgnoreCase);
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
}
