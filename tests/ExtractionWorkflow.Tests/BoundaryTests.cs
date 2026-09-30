using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Tests;

/// <summary>
/// Spec 015 T02 — the two things prose cannot enforce: that the dependency
/// arrows never reverse, and that neither new project acquires an authority
/// that belongs to the engine.
/// <para>
/// Every source scan reads <b>executable lines only</b>. Both modules'
/// documentation legitimately names nearly every forbidden concept while
/// explaining why it is forbidden, so scanning raw text would make honest
/// comments unwritable. The filter guard at the bottom proves the filter
/// removes documentation without removing code.
/// </para>
/// <para>
/// Solution assertions are presence-and-uniqueness, never a total or closed
/// project set. Pinning the solution to a closed set is what blocked spec 007's
/// Wave 0 outright, recurred in spec 013, and blocked spec 014 T01.
/// </para>
/// </summary>
public sealed class BoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string WorkflowRoot =
        Path.Combine(RepositoryRoot, "src", "ExtractionWorkflow");

    private static readonly string IntelligenceRoot =
        Path.Combine(RepositoryRoot, "src", "DocumentIntelligence");

    private static readonly string WorkflowProject =
        Path.Combine(WorkflowRoot, "ExtractionWorkflow.csproj");

    private static readonly string IntelligenceProject =
        Path.Combine(IntelligenceRoot, "DocumentIntelligence.csproj");

    private static readonly string WorkflowTestProject = Path.Combine(
        RepositoryRoot, "tests", "ExtractionWorkflow.Tests", "ExtractionWorkflow.Tests.csproj");

    private static readonly string IntelligenceTestProject = Path.Combine(
        RepositoryRoot, "tests", "DocumentIntelligence.Tests", "DocumentIntelligence.Tests.csproj");

    /// <summary>
    /// The files each wave is required to have put in the coordination module,
    /// with the path each belongs at.
    /// <para>
    /// This is a <b>required set, not a total one</b>. The list it replaced was
    /// an exact inventory of everything under <c>src/ExtractionWorkflow/</c>,
    /// which meant the module's own next wave broke it by adding a file the plan
    /// had always called for. What is worth guarding is that each approved file
    /// is present exactly once where it belongs, and that nothing forbidden
    /// appears in any of them — both of which survive a later addition.
    /// </para>
    /// </summary>
    private static readonly (string Task, string RelativePath)[] RequiredWorkflowFiles =
    {
        ("T02", "Models/ExtractionEnums.cs"),
        ("T02", "Models/ExtractionWorkflowFields.cs"),
        ("T02", "Models/ExtractionWorkflowErrors.cs"),
        ("T02", "Models/ProviderResults.cs"),
        ("T02", "Models/ExtractionVersion.cs"),
        ("T02", "Models/ExtractionBoxValue.cs"),
        ("T02", "Models/MedicalEvidenceVersion.cs"),
        ("T02", "Contracts/IDocumentIntelligenceProvider.cs"),
        ("T02", "Contracts/IExtractionContentReader.cs"),
        ("T02", "Services/ExtractionPayloadBuilder.cs"),
        ("T03", "Contracts/IExtractionStore.cs"),
        ("T03", "Models/ExtractionStorageRecords.cs"),
    };

    /// <summary>
    /// The files that may name the box the module refuses to represent. Each is
    /// listed for one reason — it is where that refusal is implemented — rather
    /// than as a total inventory.
    /// </summary>
    private static readonly string[] FilesThatEnforceTheSinBoxInvariant =
    {
        "ExtractionBoxValue.cs",
        "ExtractionStorageRecords.cs",
    };

    private static readonly string[] ExpectedIntelligenceFiles =
    {
        "DeterministicDocumentIntelligenceProvider.cs",
        "DocumentIntelligenceOptions.cs",
        "DocumentIntelligenceRegistration.cs",
        "AnalyzeOperationLifecycle.cs", "AzureDocumentIntelligenceOptions.cs", "AzureDocumentIntelligenceProvider.cs",
        "DocumentLayoutRecognition.cs", "LayoutResultInterpreter.cs",
    };

    // ------------------------------------------------- the approved projects

    [Theory]
    [InlineData("ExtractionWorkflow")]
    [InlineData("DocumentIntelligence")]
    public void EachModuleProjectDeclaresTheApprovedSettings(string module)
    {
        var project = ProjectFor(module);

        Assert.True(File.Exists(project), $"{project} must exist.");
        Assert.Equal("Microsoft.NET.Sdk", XDocument.Load(project).Root!.Attribute("Sdk")!.Value);

        foreach (var (property, expected) in new[]
                 {
                     ("TargetFramework", "net10.0"),
                     ("Nullable", "enable"),
                     ("ImplicitUsings", "enable"),
                 })
        {
            Assert.Equal(
                new[] { expected },
                Elements(project, property).Select(element => element.Value).ToArray());
        }

        Assert.Empty(Elements(project, "TargetFrameworks"));
    }

    [Fact]
    public void TheCoordinationModuleReferencesExactlyWhatItsCodeConsumes()
    {
        // Not the architecture's eventual set: a reference no code names is an
        // unused reference, and the task that first consumes one adds it then.
        // T04 is the first task whose code names CaseProcessing (the existing T4
        // chain) and ClientIntake (the server-stored case), so both join here in
        // the same pass that consumes them. T05 is the first task whose code
        // names CaseReview (the existing review assembler), so it joins in the
        // same pass that consumes it.
        Assert.Equal(
            new[]
            {
                "../CaseProcessing/CaseProcessing.csproj",
                "../CaseReview/CaseReview.csproj",
                "../ClientIntake/ClientIntake.csproj",
                "../DocumentExtraction/DocumentExtraction.csproj",
                "../TaxLogicEngine/TaxLogicEngine.csproj",
                "../Tenancy/Tenancy.csproj",
            },
            ProjectReferences(WorkflowProject));
    }

    [Fact]
    public void EveryDeclaredReferenceIsActuallyNamedByCompiledCode()
    {
        var declared = ProjectReferences(WorkflowProject)
            .Select(reference => Path.GetFileNameWithoutExtension(reference))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var compiled = typeof(ExtractionVersion).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToHashSet(StringComparer.Ordinal);

        // A ProjectReference nothing names does not appear in the compiled
        // reference list at all, so this is what catches a speculative one.
        Assert.All(declared, name => Assert.Contains(name, compiled));
    }

    [Fact]
    public void TheCoordinationModuleDeclaresNoPackageOrFrameworkReference()
    {
        // Deterministic, provider-independent application logic. A package
        // reference here is how a module acquires a web, cloud, or ORM
        // dependency by accident.
        var project = XDocument.Load(WorkflowProject);

        foreach (var forbidden in new[] { "PackageReference", "FrameworkReference" })
        {
            Assert.DoesNotContain(
                project.Descendants(),
                element => element.Name.LocalName == forbidden);
        }
    }

    [Fact]
    public void TheAdapterReferencesOnlyTheModuleWhosePortItImplements()
    {
        Assert.Equal(
            new[] { "../ExtractionWorkflow/ExtractionWorkflow.csproj" },
            ProjectReferences(IntelligenceProject));
    }

    [Theory]
    [InlineData("Persistence")]
    [InlineData("TaxAI.Portal")]
    [InlineData("DocumentIntelligence")]
    public void TheCoordinationModuleReferencesNoInfrastructureOrHost(string forbidden)
    {
        // Reads the declared references, not the project file's raw text: the
        // comment beside the reference list names every forbidden project while
        // explaining why it is absent.
        Assert.DoesNotContain(
            ProjectReferences(WorkflowProject),
            reference => reference.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoDeterministicTaxModuleReferencesTheAdapterProject()
    {
        // The host is the single composition root and is the one project
        // entitled to name an adapter; it wires this one in a later task.
        // Nothing deterministic may depend on infrastructure at all.
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path =>
                !path.EndsWith("DocumentIntelligence.csproj", StringComparison.Ordinal) &&
                !path.EndsWith("TaxAI.Portal.csproj", StringComparison.Ordinal))
            .Where(path => ProjectReferences(path).Any(reference =>
                reference.Contains("DocumentIntelligence.csproj", StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("ExtractionWorkflow")]
    [InlineData("DocumentIntelligence")]
    public void NeitherProjectPairDeclaresAnInternalsVisibleToGrant(string module)
    {
        // The repository's existing grants are pre-existing and stay that way.
        // These contracts are exercised through their public surface.
        Assert.DoesNotContain(
            "InternalsVisibleTo", File.ReadAllText(ProjectFor(module)), StringComparison.Ordinal);

        Assert.DoesNotContain(
            "InternalsVisibleTo", File.ReadAllText(TestProjectFor(module)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ExtractionWorkflow")]
    [InlineData("DocumentIntelligence")]
    public void EachTestProjectUsesTheApprovedMatrixAndReferencesOnlyItsModule(string module)
    {
        var testProject = TestProjectFor(module);

        Assert.Equal(
            new[] { "net10.0" },
            Elements(testProject, "TargetFramework").Select(element => element.Value).ToArray());

        Assert.Equal(
            new[]
            {
                ("Microsoft.NET.Test.Sdk", "18.8.1"),
                ("coverlet.collector", "10.0.1"),
                ("xunit", "2.9.3"),
                ("xunit.runner.visualstudio", "3.1.5"),
            }.OrderBy(package => package.Item1, StringComparer.Ordinal).ToArray(),
            Elements(testProject, "PackageReference")
                .Select(element => (
                    element.Attribute("Include")!.Value,
                    element.Attribute("Version")!.Value))
                .OrderBy(package => package.Item1, StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(
            new[] { $"../../src/{module}/{module}.csproj" },
            ProjectReferences(testProject));
    }

    [Fact]
    public void TheCompiledCoordinationModuleNamesOnlyApprovedNonFrameworkAssemblies()
    {
        // The project file states intent; the assembly states what actually
        // compiled in. A subset assertion, because a ProjectReference nothing
        // names does not appear in the reference list at all.
        var referenced = typeof(ExtractionVersion).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.All(
            referenced,
            name => Assert.Contains(
                name,
                new[]
                {
                    "CaseProcessing",
                    "CaseReview",
                    "ClientIntake",
                    "DocumentExtraction",
                    "TaxCases",
                    "TaxLogicEngine",
                    "Tenancy",
                }));
    }

    // --------------------------------------------- solution and gate registration

    [Theory]
    [InlineData("\"ExtractionWorkflow\", \"src\\ExtractionWorkflow\\ExtractionWorkflow.csproj\"")]
    [InlineData("\"DocumentIntelligence\", \"src\\DocumentIntelligence\\DocumentIntelligence.csproj\"")]
    [InlineData("\"ExtractionWorkflow.Tests\", \"tests\\ExtractionWorkflow.Tests\\ExtractionWorkflow.Tests.csproj\"")]
    [InlineData("\"DocumentIntelligence.Tests\", \"tests\\DocumentIntelligence.Tests\\DocumentIntelligence.Tests.csproj\"")]
    public void TheSolutionDeclaresEachProjectExactlyOnceAtItsApprovedPath(string declaration)
    {
        var projectLines = File.ReadAllLines(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(projectLines);

        // Presence and uniqueness only. The solution must stay free to declare
        // later modules' projects without this test failing.
        Assert.Equal(
            1,
            projectLines.Count(line => line.Contains(declaration, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("ExtractionWorkflow.Tests")]
    [InlineData("DocumentIntelligence.Tests")]
    public void EachTestProjectIsRegisteredInTheFullSuiteGate(string project)
    {
        // A test project nobody registered is a project nobody would notice
        // failing to run.
        var manifest = File.ReadAllText(
            Path.Combine(RepositoryRoot, "scripts", "test-projects.json"));

        Assert.Contains($"\"{project}\"", manifest, StringComparison.Ordinal);
    }

    // ------------------------------- the line neither module may cross

    public static TheoryData<string, string> ForbiddenProductionPatterns() => new()
    {
        // Every tax judgement stays in the engine. The processor and the
        // assembler are invoked by later waves; the parser, the mapper, and the
        // rounding helper are reached only through them.
        { "engine parser, mapper, or rounding helper", @"\bT4SlipParser\b|\bT5SlipParser\b|\bT4LineMapper\b|\bT5LineMapper\b|\bMoneyRounding\b" },
        { "arithmetic on a tax value", @"\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b" },

        // No second mapping authority and no mapping-resource read.
        { "mapping table or resource read", @"TaxLogicEngine\.Mappings|\bGetManifestResourceStream\b|\bT4LineMap\b|\bT5LineMap\b" },

        // No tax-year policy of its own.
        { "tax-year policy", @"\bSupportedExtractionTaxYears\b|\bSupportedProcessingTaxYears\b|\bSupportedReviewTaxYears\b|\bSupportedTaxYears\b|\bCurrentYear\b" },

        // No confidence threshold, anywhere, in any spelling.
        { "confidence threshold", @"Threshold|MinimumConfidence|ConfidenceFloor|LowConfidence" },

        // No infrastructure. The ports are declared here and implemented elsewhere.
        { "ORM or database", @"\bDbContext\b|\bDbSet\b|EntityFrameworkCore|\bSqlConnection\b|\bIQueryable\b" },
        { "web or hosting", @"Microsoft\.AspNetCore|\bHttpContext\b|\bIActionResult\b|\bIResult\b|\bControllerBase\b|\bIHostedService\b" },
        { "cloud, mail, or logging provider", @"\bAzure\b|\bSerilog\b|OpenTelemetry|ApplicationInsights|\bSmtpClient\b|\bILogger\b" },
        { "network egress", @"\bHttpClient\b|\bWebRequest\b|\bSocket\b|\bDns\b|\bWebSocket\b" },

        // The engine's masked identifier stays the engine's, and no second
        // masking, redaction, or scrubbing path is created.
        { "masking or redaction helper", @"\bRecipientIdentifier\b|\bMask(?:ed|ing)?\b|\bRedact|\bScrub" },

        // Non-determinism these modules must receive as an argument instead.
        { "ambient clock, environment, or randomness", @"DateTimeOffset\s*\.\s*(?:Now|UtcNow)|DateTime\s*\.\s*(?:Now|UtcNow)|\bEnvironment\s*\.\s*(?:GetEnvironmentVariable|MachineName|UserName)|Guid\s*\.\s*NewGuid\s*\(|\bRandom\b" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSourceContainsNoForbiddenToken(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Where(path => !ApprovedProviderBoundaryException(path, boundary))
            .Select(path => new
            {
                Name = Path.GetFileName(path),
                Text = string.Join("\n", ExecutableLines(path)),
            })
            .Where(source => regex.IsMatch(source.Text))
            .Select(source => source.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Forbidden {boundary} pattern /{pattern}/ found in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void NoCraLineNumberLiteralAppearsInEitherModule()
    {
        // CRA T1 line numbers are five digits. None may be hardcoded here: every
        // line number in the system originates from the engine's embedded JSON
        // config, reached only through a provider.
        var craLineNumber = new Regex(@"\b\d{5}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => craLineNumber.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} contains a possible CRA line number: {string.Join(" | ", offending)}");
        }
    }

    [Fact]
    public void NoTaxYearLiteralAppearsInEitherModule()
    {
        // Year isolation belongs to the modules that own it. A year literal here
        // would be a second policy, and the two would drift.
        var year = new Regex(@"\b(?:19|20)\d{2}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => year.IsMatch(line) && !(Path.GetFileName(file) == "AzureDocumentIntelligenceOptions.cs" && line.Contains("ApprovedApiVersion = \"2024-11-30\"", StringComparison.Ordinal))).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} names a tax year: {string.Join(" | ", offending)}");
        }
    }

    [Fact]
    public void TheOnlyExecutableMentionsOfTheSinBoxAreTheInvariantsThatRefuseIt()
    {
        var mention = new Regex(
            @"SocialInsurance|\bSin\b|\bSIN\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var files = ProductionSourceFiles()
            .Where(path => ExecutableLines(path).Any(line => mention.IsMatch(line)))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Only where the refusal is implemented: the box value that cannot
        // represent the SIN box, and the correction record that refuses to name
        // it. Any other file mentioning it is a file doing something with it.
        Assert.All(
            files,
            file => Assert.Contains(file, FilesThatEnforceTheSinBoxInvariant));

        // And both refusals are actually there — a vanished invariant would
        // otherwise pass this for free.
        Assert.Equal(
            FilesThatEnforceTheSinBoxInvariant.OrderBy(name => name, StringComparer.Ordinal),
            files);
    }

    [Fact]
    public void TheCoordinationModuleImplementsNeitherOfItsOwnPorts()
    {
        // Ports are declared here and implemented by infrastructure. An
        // implementation appearing in this assembly would be the arrow reversing.
        var implementations = typeof(ExtractionVersion).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type =>
                typeof(IExtractionContentReader).IsAssignableFrom(type) ||
                typeof(IDocumentIntelligenceProvider).IsAssignableFrom(type))
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(implementations);
    }

    [Fact]
    public void NoNewTaxYearOrDocumentKindAuthorityIsDeclared()
    {
        // The envelope discriminator, the provenance record, and the supported
        // year list already exist and are reused unchanged.
        var declared = typeof(ExtractionVersion).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToArray();

        foreach (var existing in new[]
                 {
                     "ExtractionEnvelope",
                     "ValidatedExtraction",
                     "ExtractedDocumentKind",
                     "ExtractionProvenance",
                     "SupportedExtractionTaxYears",
                 })
        {
            Assert.DoesNotContain(existing, declared);
        }
    }

    // -------------------------------------------------------------- guards

    [Theory]
    [MemberData(nameof(RequiredWorkflowFileData))]
    public void EachRequiredWorkflowFileExistsExactlyOnceAtItsApprovedPath(
        string owningTask,
        string relativePath)
    {
        var expected = Path.Combine(
            WorkflowRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            File.Exists(expected),
            $"{owningTask} requires {relativePath}, which is not at its approved path.");

        // Exactly once: the same file name appearing under a second folder is a
        // second answer about where the module keeps that contract.
        Assert.Equal(
            1,
            ProductionSourceFiles(WorkflowRoot)
                .Count(path => string.Equals(
                    Path.GetFileName(path),
                    Path.GetFileName(relativePath),
                    StringComparison.Ordinal)));
    }

    public static TheoryData<string, string> RequiredWorkflowFileData()
    {
        var data = new TheoryData<string, string>();

        foreach (var (owningTask, relativePath) in RequiredWorkflowFiles)
        {
            data.Add(owningTask, relativePath);
        }

        return data;
    }

    [Fact]
    public void TheScannerSeesEveryProductionFileAndStillMatchesAKnownPattern()
    {
        // Without this, a broken walk or an over-eager comment filter would make
        // every negative assertion above pass for free. It asserts coverage of
        // the required files rather than a total inventory, so a later approved
        // file does not fail it merely by existing.
        var scanned = ProductionSourceFiles(WorkflowRoot)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.All(
            RequiredWorkflowFiles,
            required => Assert.Contains(Path.GetFileName(required.RelativePath), scanned));

        Assert.Equal(
            ExpectedIntelligenceFiles.OrderBy(name => name, StringComparer.Ordinal),
            ProductionSourceFiles(IntelligenceRoot).Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var allExecutable = string.Join("\n", ProductionSourceFiles().SelectMany(ExecutableLines));

        Assert.True(
            allExecutable.Length > 500,
            $"Implausibly little executable source: {allExecutable.Length} chars.");

        foreach (var mustMatch in new[]
                 {
                     @"\bTrustedFirmId\b",
                     @"\bT4RawInput\b",
                     @"\bJsonIgnore\b",
                     @"\bExtractionSourceLease\b",
                     @"\bIDocumentIntelligenceProvider\b",
                 })
        {
            Assert.Matches(new Regex(mustMatch), allExecutable);
        }
    }

    [Fact]
    public void TheCommentFilterRemovesDocumentationWithoutRemovingCode()
    {
        // The filter is what makes "executable lines only" safe. Prove both
        // directions on a file that deliberately documents forbidden concepts.
        var contentReader = Path.Combine(WorkflowRoot, "Contracts", "IExtractionContentReader.cs");
        var raw = File.ReadAllText(contentReader);
        var executable = string.Join("\n", ExecutableLines(contentReader));

        // The documentation names a forbidden concept while explaining it...
        Assert.Contains("anonymous", raw, StringComparison.Ordinal);
        // ...the filter removes it, so honest comments stay writable...
        Assert.DoesNotContain("anonymous", executable, StringComparison.Ordinal);
        // ...while the code itself survives.
        Assert.Contains("OpenAsync", executable, StringComparison.Ordinal);
    }

    [Theory] // spec 016 T05
    [InlineData("cloud, mail, or logging provider")]
    [InlineData("network egress")]
    public void TheProviderExceptionIsExactlyTheTwoTransportFilesAndEachNeedsIt(string boundary)
    {
        var pattern = ForbiddenProductionPatterns()
            .Select(row => ((string)row[0], (string)row[1]))
            .Single(row => row.Item1 == boundary).Item2;
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var matching = ProductionSourceFiles()
            .Where(path => regex.IsMatch(string.Join("\n", ExecutableLines(path))))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // No other file in either module is exempted or would need to be, and
        // each exempted transport file genuinely uses the exception.
        Assert.Equal(ProductionTransportFiles, matching);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Spec 016 T05: the production adapter's two transport files, the only
    /// files exempted from the cloud and network-egress token scans.
    /// </summary>
    private static readonly string[] ProductionTransportFiles =
        ["AnalyzeOperationLifecycle.cs", "AzureDocumentIntelligenceProvider.cs"];

    private static bool ApprovedProviderBoundaryException(string path, string boundary) =>
        path.StartsWith(IntelligenceRoot, StringComparison.Ordinal) &&
        boundary is "cloud, mail, or logging provider" or "network egress" &&
        ProductionTransportFiles.Contains(Path.GetFileName(path));
    private static string ProjectFor(string module) =>
        module == "ExtractionWorkflow" ? WorkflowProject : IntelligenceProject;

    private static string TestProjectFor(string module) =>
        module == "ExtractionWorkflow" ? WorkflowTestProject : IntelligenceTestProject;

    private static string[] ProductionSourceFiles() =>
        ProductionSourceFiles(WorkflowRoot)
            .Concat(ProductionSourceFiles(IntelligenceRoot))
            .ToArray();

    private static string[] ProductionSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

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
