using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CaseProcessing.Services;
using CaseReview.Models;
using CaseReview.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseReview.Tests.EndToEnd;

/// <summary>
/// T08 repository-level verification for spec 009. These tests verify the
/// complete project graph, exact public boundary, representative accepted and
/// rejected public flows, unsupported-year isolation, forbidden behavior, and
/// the completeness of the AC-1 through AC-54 evidence record.
/// <para>
/// Solution checks are presence-and-uniqueness checks. They never assert a
/// total project count, so adding a later module does not invalidate spec 009.
/// </para>
/// </summary>
public sealed class RepositoryVerificationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ModuleRoot =
        Path.Combine(RepositoryRoot, "src", "CaseReview");

    private static readonly (string Name, string Directory, string[] References)[]
        ExpectedProjects =
        {
            ("TaxLogicEngine", "src", Array.Empty<string>()),
            ("TaxCases", "src", new[] { "../TaxLogicEngine/TaxLogicEngine.csproj" }),
            ("DocumentExtraction", "src", new[] { "../TaxLogicEngine/TaxLogicEngine.csproj" }),
            ("CaseProcessing", "src", new[] { "../DocumentExtraction/DocumentExtraction.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" }),
            ("CaseReview", "src", new[] { "../CaseProcessing/CaseProcessing.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj" }),
            ("TaxLogicEngine.Tests", "tests", new[] { "../../src/TaxLogicEngine/TaxLogicEngine.csproj" }),
            ("TaxCases.Tests", "tests", new[] { "../../src/TaxCases/TaxCases.csproj" }),
            ("DocumentExtraction.Tests", "tests", new[] { "../../src/DocumentExtraction/DocumentExtraction.csproj" }),
            ("CaseProcessing.Tests", "tests", new[] { "../../src/CaseProcessing/CaseProcessing.csproj" }),
            ("CaseReview.Tests", "tests", new[] { "../../src/CaseReview/CaseReview.csproj" }),
        };

    private static readonly string[] PreExistingTrees =
    {
        "src/TaxLogicEngine",
        "tests/TaxLogicEngine.Tests",
        "src/TaxCases",
        "tests/TaxCases.Tests",
        "src/DocumentExtraction",
        "tests/DocumentExtraction.Tests",
        "src/CaseProcessing",
        "tests/CaseProcessing.Tests",
    };

    private static readonly string[] ExpectedProductionFiles =
    {
        "AccountantReviewPackage.Decisions.cs",
        "AccountantReviewPackage.cs",
        "ReviewDecisionDescriptor.cs",
        "ReviewedBoxAmount.cs",
        "ReviewedSlip.cs",
        "ReviewFields.cs",
        "ReviewPackageAssembler.cs",
        "ReviewSignOff.cs",
        "SignOffDecision.cs",
        "SupportedReviewTaxYears.cs",
    };

    // ---------------------------------------------------------- solution graph

    [Fact]
    public void SpecProjectsAreDeclaredExactlyOnceAtApprovedPaths()
    {
        foreach (var (name, directory) in new[]
                 {
                     ("CaseReview", "src"),
                     ("CaseReview.Tests", "tests"),
                 })
        {
            var declaration =
                $"\"{name}\", \"{directory}\\{name}\\{name}.csproj\"";

            Assert.Single(
                SolutionProjectLines(),
                line => line.Contains(declaration, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryDeclaredSolutionProjectAndProjectReferenceResolves()
    {
        var declarations = SolutionProjects();
        Assert.NotEmpty(declarations);

        foreach (var (name, relativePath) in declarations)
        {
            var projectPath = Path.Combine(
                RepositoryRoot,
                relativePath.Replace('\\', Path.DirectorySeparatorChar));

            Assert.True(
                File.Exists(projectPath),
                $"{name} is declared at a missing path: {relativePath}");

            foreach (var reference in ProjectReferences(projectPath))
            {
                var resolved = Path.GetFullPath(
                    Path.Combine(
                        Path.GetDirectoryName(projectPath)!,
                        reference.Replace('/', Path.DirectorySeparatorChar)));

                Assert.True(
                    File.Exists(resolved),
                    $"{name} has an unresolvable project reference: {reference}");
            }
        }
    }

    [Fact]
    public void EveryKnownProjectHasItsExactApprovedReferenceSet()
    {
        foreach (var (name, directory, expectedReferences) in ExpectedProjects)
        {
            var projectPath = Path.Combine(
                RepositoryRoot,
                directory,
                name,
                $"{name}.csproj");

            Assert.True(File.Exists(projectPath), $"{name} must exist.");
            Assert.Equal(
                expectedReferences.OrderBy(
                    reference => reference,
                    StringComparer.Ordinal),
                ProjectReferences(projectPath));
        }
    }

    [Fact]
    public void CaseReviewReferencesExactlyItsTwoApprovedCompiledModules()
    {
        var nonFrameworkReferences = typeof(AccountantReviewPackage).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("System", StringComparison.Ordinal) &&
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "CaseProcessing", "TaxLogicEngine" }, nonFrameworkReferences);
    }

    [Fact]
    public void NoPreExistingProjectOrSourceTreeReferencesCaseReview()
    {
        Assert.Equal(PreExistingTrees.Length, PreExistingTrees.Distinct().Count());

        foreach (var tree in PreExistingTrees)
        {
            var root = Path.Combine(
                RepositoryRoot,
                tree.Replace('/', Path.DirectorySeparatorChar));
            var projectName = Path.GetFileName(root);
            var projectPath = Path.Combine(root, $"{projectName}.csproj");

            Assert.True(Directory.Exists(root), $"{tree} must exist.");
            Assert.True(File.Exists(projectPath), $"{tree} project must exist.");
            Assert.DoesNotContain(
                ProjectReferences(projectPath),
                reference => reference.Contains(
                    "CaseReview",
                    StringComparison.OrdinalIgnoreCase));

            var reverseSourceReferences = HandwrittenSourceFiles(root)
                .Where(path => ExecutableLines(path).Any(line =>
                    Regex.IsMatch(
                        line,
                        @"\bCaseReview\b",
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1))))
                .Select(path => Path.GetRelativePath(RepositoryRoot, path))
                .ToArray();

            Assert.True(
                reverseSourceReferences.Length == 0,
                $"{tree} source references CaseReview: "
                + string.Join(", ", reverseSourceReferences));
        }
    }

    // ---------------------------------------------------------- exact contract

    [Fact]
    public void PublicContractMatchesArchitectureManifest()
    {
        var assembly = typeof(AccountantReviewPackage).Assembly;
        var exported = assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "CaseReview.Models.AccountantReviewPackage",
                "CaseReview.Models.ReviewDecisionDescriptor",
                "CaseReview.Models.ReviewFields",
                "CaseReview.Models.ReviewSignOff",
                "CaseReview.Models.ReviewedBoxAmount",
                "CaseReview.Models.ReviewedSlip",
                "CaseReview.Models.SignOffDecision",
                "CaseReview.Services.ReviewPackageAssembler",
                "CaseReview.Services.SupportedReviewTaxYears",
            },
            exported);

        Assert.Equal(
            new[]
            {
                ("Approved", 1),
                ("ChangesRequested", 2),
                ("Rejected", 3),
            },
            Enum.GetValues<SignOffDecision>()
                .Select(value => (value.ToString(), (int)value))
                .ToArray());

        AssertPublicPropertyNames<ReviewedBoxAmount>("Amount", "BoxCode");
        AssertPublicPropertyNames<ReviewedSlip>(
            "BoxAmounts",
            "DocumentId",
            "LineEntries",
            "Slip",
            "SourceReference",
            "TaxYear",
            "UnmappedBoxCodes");
        AssertPublicPropertyNames<ReviewDecisionDescriptor>(
            "DecidedAtReference",
            "Decision",
            "Note",
            "ReviewerReference",
            "UnmappedBoxesAcknowledged");
        AssertPublicPropertyNames<ReviewSignOff>(
            "DecidedAtReference",
            "Decision",
            "Note",
            "ReviewerReference",
            "UnmappedBoxesAcknowledged");
        AssertPublicPropertyNames<AccountantReviewPackage>(
            "CaseId",
            "FirmId",
            "ParticipantId",
            "ReviewedSlips",
            "SignOff",
            "TaxYear");

        AssertPublicMethodNames<ReviewedBoxAmount>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertPublicMethodNames<ReviewedSlip>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertPublicMethodNames<ReviewDecisionDescriptor>("ToString");
        AssertPublicMethodNames<ReviewSignOff>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertPublicMethodNames<AccountantReviewPackage>(
            "Equals",
            "Equals",
            "GetHashCode",
            "RecordDecision",
            "ToString");

        Assert.Equal(
            new[] { "All" },
            typeof(SupportedReviewTaxYears)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            new[] { "IsSupported" },
            PublicDeclaredMethods(typeof(SupportedReviewTaxYears))
                .Select(method => method.Name)
                .ToArray());

        var assemble = Assert.Single(
            PublicDeclaredMethods(typeof(ReviewPackageAssembler)));
        Assert.Equal(nameof(ReviewPackageAssembler.Assemble), assemble.Name);
        Assert.Equal(
            typeof(ParseResult<AccountantReviewPackage>),
            assemble.ReturnType);
    }

    [Fact]
    public void ReviewOutputTypesHaveNoPublicConstructor()
    {
        foreach (var type in new[]
                 {
                     typeof(ReviewedBoxAmount),
                     typeof(ReviewedSlip),
                     typeof(ReviewSignOff),
                     typeof(AccountantReviewPackage),
                 })
        {
            Assert.Empty(type.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));

            var constructor = Assert.Single(type.GetConstructors(
                BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.True(
                constructor.IsAssembly,
                $"{type.Name}'s only constructor must be internal.");
        }

        Assert.Single(typeof(ReviewDecisionDescriptor).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void ContractHasNoPerCodeAcknowledgementOrMappedDisputeMember()
    {
        var publicTypes = typeof(AccountantReviewPackage).Assembly
            .GetExportedTypes();
        var acknowledgementProperties = publicTypes
            .SelectMany(type => type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            .Where(property =>
                property.Name.Contains(
                    "Acknowledg",
                    StringComparison.OrdinalIgnoreCase))
            .Select(property =>
                $"{property.DeclaringType!.Name}.{property.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "ReviewDecisionDescriptor.UnmappedBoxesAcknowledged",
                "ReviewSignOff.UnmappedBoxesAcknowledged",
            },
            acknowledgementProperties);

        Assert.All(
            publicTypes.SelectMany(type => type.GetMembers(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)),
            member =>
            {
                Assert.DoesNotContain(
                    "Dispute",
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "Disposition",
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "MappedAmount",
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "PerCode",
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "PerSlip",
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    // ----------------------------------------------------- representative flows

    [Fact]
    public void RepresentativePublicPipelineAssemblesAndSignsExactContent()
    {
        var envelope = ReviewPipelineFixture.Envelope(
            documentId: "repository-document",
            firmId: "repository-firm",
            caseId: "repository-case",
            participantId: "repository-participant",
            sourceReference: "repository-source");

        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(validated.IsSuccess);

        var processed = new T4CaseProcessor(
                new EmbeddedT4LineMapProvider())
            .Process(validated.Value);
        Assert.True(processed.IsSuccess);

        var assembled = ReviewPackageAssembler.Assemble(
            new[] { processed.Value });
        Assert.True(assembled.IsSuccess);

        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.Approved,
            "repository-reviewer",
            "opaque-decided-at",
            note: null,
            unmappedBoxesAcknowledged: null);
        var signed = assembled.Value!.RecordDecision(descriptor);

        Assert.True(signed.IsSuccess);
        Assert.Empty(signed.Errors);
        Assert.Null(assembled.Value.SignOff);
        Assert.NotNull(signed.Value!.SignOff);
        Assert.Equal(SignOffDecision.Approved, signed.Value.SignOff!.Decision);
        Assert.Equal(2025, signed.Value.TaxYear);
        Assert.Single(signed.Value.ReviewedSlips);
        Assert.Same(
            processed.Value!.Slip,
            signed.Value.ReviewedSlips[0].Slip);
        ReviewPipelineFixture.AssertLinesEqual(
            processed.Value.Mapping.Lines,
            signed.Value.ReviewedSlips[0].LineEntries);
    }

    [Fact]
    public void RepresentativeUnmappedApprovalWithoutAcknowledgementFailsClosed()
    {
        var envelope = ReviewPipelineFixture.Envelope();
        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(validated.IsSuccess);

        var processed = new T4CaseProcessor(
                ReviewPipelineFixture.EmptyProvider())
            .Process(validated.Value);
        Assert.True(processed.IsSuccess);
        Assert.NotEmpty(processed.Value!.Mapping.UnmappedBoxCodes);

        var assembled = ReviewPackageAssembler.Assemble(
            new[] { processed.Value });
        Assert.True(assembled.IsSuccess);

        var rejected = assembled.Value!.RecordDecision(
            new ReviewDecisionDescriptor(
                SignOffDecision.Approved,
                "repository-reviewer",
                "opaque-decided-at",
                note: null,
                unmappedBoxesAcknowledged: null));

        Assert.True(rejected.IsFailure);
        Assert.Null(rejected.Value);
        Assert.Null(assembled.Value.SignOff);
        Assert.Equal(
            ReviewFields.UnmappedBoxAcknowledgement,
            Assert.Single(rejected.Errors).Field);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    public void UnsupportedYearsAreRejectedBeforeReviewAndByReviewPolicy(int taxYear)
    {
        Assert.False(SupportedReviewTaxYears.IsSupported(taxYear));

        var envelope = EnvelopeForYear(taxYear);
        var validated = ExtractionEnvelopeValidator.Validate(envelope);

        Assert.True(validated.IsFailure);
        Assert.Null(validated.Value);
    }

    // -------------------------------------------------------- forbidden behavior

    [Fact]
    public void CaseReviewSourceContainsNoForbiddenDependencyOrTaxToken()
    {
        var forbiddenPatterns = new (string Name, string Pattern)[]
        {
            ("clock", @"DateTime\s*\.\s*(?:Now|UtcNow)|DateTimeOffset\s*\.\s*(?:Now|UtcNow)|\bTimeProvider\b"),
            ("filesystem", @"\bSystem\.IO\b|\b(?:File|Directory|FileStream)\s*\."),
            ("network", @"\bSystem\.Net\b|\bHttpClient\b|\bWebRequest\b|\bSocket\b"),
            ("database / SQL", @"\bDbContext\b|\bSqlConnection\b|\bSystem\.Data\b|EntityFrameworkCore|\bSELECT\b"),
            ("environment / current user", @"\bEnvironment\s*\.|\bClaimsPrincipal\b|\bCurrentUser\b"),
            ("OCR / LLM / cloud / email", @"\bOcr\b|\bTesseract\b|\bOpenAi\b|\bAnthropic\b|\bLlm\b|\bSmtp\b|\bAmazon\b|\bAzure\b"),
            ("ASP.NET Core", @"Microsoft\.AspNetCore|\bControllerBase\b|\bWebApplication\b"),
            ("generated identifier", @"\bGuid\s*\.\s*NewGuid\b|\bnew\s+Random\b|\bRandomNumberGenerator\b"),
            ("TaxCases", @"\bTaxCases\b"),
            ("direct DocumentExtraction", @"\bDocumentExtraction\b"),
            ("tax parser / mapper", @"\bT4SlipParser\b|\bT4LineMapper\b|\bIT4LineMapProvider\b|\bEmbeddedT4LineMapProvider\b"),
            ("mapping table", @"\bT4LineMap\b|\bT4LineMappingConfig\b|\bT4BoxCodes\b|\bDictionary\s*<"),
            ("mapping resource", @"TaxLogicEngine\.Mappings|GetManifestResourceStream|\.json\b"),
            ("rounding / floating point", @"\bMoneyRounding\b|\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b"),
            ("PII helper", @"\bSafeDiagnosticText\b|new\s+RecipientIdentifier\b|\bRedact|\bScrub|\bSanitiz"),
            ("T5", @"\bT5\w*\b"),
            ("CRA line literal", @"\b\d{5}\b"),
            ("box-code literal", @"""(?:\d{2})"""),
        };

        var sources = ModuleSourceFiles()
            .Select(path => new
            {
                Name = Path.GetFileName(path),
                Text = string.Join("\n", ExecutableLines(path)),
            })
            .ToArray();
        var allExecutable = string.Join(
            "\n",
            sources.Select(source => source.Text));

        foreach (var (name, pattern) in forbiddenPatterns)
        {
            var regex = new Regex(
                pattern,
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            var offending = sources
                .Where(source => regex.IsMatch(source.Text))
                .Select(source => source.Name)
                .ToArray();

            Assert.True(
                offending.Length == 0,
                $"Forbidden {name} token found in: "
                + string.Join(", ", offending));
        }

        Assert.Single(
            Regex.Matches(
                    allExecutable,
                    @"T4SlipBoxAmounts\s*\.\s*Enumerate",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1))
                .Cast<Match>());
    }

    // -------------------------------------------------------- record and hygiene

    [Fact]
    public void VerificationRecordHasAPassRowForEveryAcceptanceCriterion()
    {
        var verification = File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "specs",
                "009-accountant-review-package",
                "verification.md"));

        for (var criterion = 1; criterion <= 54; criterion++)
        {
            Assert.True(
                Regex.IsMatch(
                    verification,
                    $@"^\|\s*{criterion}\s*\|\s*pass\s*\|",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)),
                $"verification.md has no pass row for AC-{criterion}.");
        }

        Assert.DoesNotContain(
            "TODO",
            verification,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("54 of 54 pass", verification, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryHygieneRulesCoverBuildAndLocalOnlyArtifacts()
    {
        var gitignore = File.ReadAllText(
            Path.Combine(RepositoryRoot, ".gitignore"));

        foreach (var required in new[]
                 {
                     "[Bb]in/",
                     "[Oo]bj/",
                     ".vs/",
                     "*.user",
                     ".claude/settings.local.json",
                 })
        {
            Assert.Contains(required, gitignore, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VerificationScannerVisitsEveryCaseReviewProductionFile()
    {
        var files = ModuleSourceFiles();

        Assert.Equal(
            ExpectedProductionFiles.OrderBy(
                name => name,
                StringComparer.Ordinal),
            files.Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.NotEmpty(SolutionProjectLines());

        foreach (var tree in PreExistingTrees)
        {
            Assert.NotEmpty(
                HandwrittenSourceFiles(
                    Path.Combine(
                        RepositoryRoot,
                        tree.Replace('/', Path.DirectorySeparatorChar))));
        }

        var executable = string.Join(
            "\n",
            files.SelectMany(ExecutableLines));
        Assert.True(
            executable.Length > 8000,
            $"Implausibly little CaseReview source was scanned: {executable.Length}.");
        Assert.Matches(
            new Regex(
                @"ReviewPackageAssembler",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
        Assert.Matches(
            new Regex(
                @"SupportedReviewTaxYears\s*\.\s*IsSupported",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
    }

    // ------------------------------------------------------------------ helpers

    private static ExtractionEnvelope EnvelopeForYear(int taxYear)
    {
        var source = ReviewPipelineFixture.Envelope();
        var sourcePayload = source.T4Payload!;
        var payload = new T4RawInput
        {
            Year = taxYear,
            EmployerName = sourcePayload.EmployerName,
            EmployerAddress = sourcePayload.EmployerAddress,
            EmployeeName = sourcePayload.EmployeeName,
            EmployeeAddress = sourcePayload.EmployeeAddress,
            Boxes = sourcePayload.Boxes,
        };

        return new ExtractionEnvelope
        {
            DocumentId = source.DocumentId,
            Kind = source.Kind,
            FirmId = source.FirmId,
            CaseId = source.CaseId,
            ParticipantId = source.ParticipantId,
            TaxYear = taxYear,
            Provenance = source.Provenance,
            T4Payload = payload,
        };
    }

    private static void AssertPublicPropertyNames<T>(params string[] expected)
    {
        var actual = typeof(T)
            .GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            actual);
    }

    private static void AssertPublicMethodNames<T>(params string[] expected)
    {
        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            PublicDeclaredMethods(typeof(T))
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static MethodInfo[] PublicDeclaredMethods(Type type) =>
        type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

    private static (string Name, string Path)[] SolutionProjects()
    {
        var expression = new Regex(
            @"^Project\(""[^""]+""\)\s*=\s*""(?<name>[^""]+)"",\s*""(?<path>[^""]+\.csproj)""",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        return SolutionProjectLines()
            .Select(line => expression.Match(line))
            .Where(match => match.Success)
            .Select(match => (
                match.Groups["name"].Value,
                match.Groups["path"].Value))
            .ToArray();
    }

    private static string[] SolutionProjectLines() =>
        File.ReadAllLines(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element =>
                element.Attribute("Include")!.Value.Replace('\\', '/'))
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

    private static string[] ModuleSourceFiles() =>
        HandwrittenSourceFiles(ModuleRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> HandwrittenSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
