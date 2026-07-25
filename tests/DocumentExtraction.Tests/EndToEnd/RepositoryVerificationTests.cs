using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentExtraction.Models;
using DocumentExtraction.Ports;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Tests.EndToEnd;

/// <summary>
/// T05 — final repository-level verification for spec 007. Proves the six
/// solution projects and their reference directions, the public contract
/// against architecture.md, representative accepted flows, and the full
/// reference/token arms of AC-29 and AC-30.
/// <para>
/// <b>[ASSUMED]</b> The solution assertions are presence-and-uniqueness, not a
/// total project count. tasks.md's "no unexpected project" phrasing would pin
/// the solution at six projects and block the next module — the same defect
/// that stalled this spec's own Wave 0 (fixed in PR #66). No acceptance
/// criterion requires exclusivity: AC-29 is about <i>references</i> and
/// dependency direction, AC-30 about reused types. Reference exclusivity is
/// therefore asserted strictly, project-count exclusivity deliberately is not.
/// </para>
/// </summary>
public sealed class RepositoryVerificationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly (string Name, string Path)[] ExpectedProjects =
    {
        ("DocumentExtraction", "src/DocumentExtraction/DocumentExtraction.csproj"),
        ("DocumentExtraction.Tests", "tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj"),
        ("TaxCases", "src/TaxCases/TaxCases.csproj"),
        ("TaxCases.Tests", "tests/TaxCases.Tests/TaxCases.Tests.csproj"),
        ("TaxLogicEngine", "src/TaxLogicEngine/TaxLogicEngine.csproj"),
        ("TaxLogicEngine.Tests", "tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj"),
    };

    // ------------------------------------------------- solution and references

    [Fact]
    public void AllSixExpectedProjectsAreDeclaredOnceAndEveryReferenceResolves()
    {
        var declared = DeclaredProjects();

        foreach (var (name, path) in ExpectedProjects)
        {
            var match = Assert.Single(declared.Where(project => project.Name == name));
            Assert.Equal(path, match.Path);

            var projectPath = Path.GetFullPath(Path.Combine(RepositoryRoot, path));
            Assert.True(File.Exists(projectPath), $"Missing solution project: {path}");
        }

        // Every declared project resolves — including any added after spec 007.
        foreach (var project in declared)
        {
            var projectPath = Path.GetFullPath(Path.Combine(RepositoryRoot, project.Path));
            Assert.True(File.Exists(projectPath), $"Unresolvable solution project: {project.Path}");

            foreach (var reference in ProjectReferences(projectPath))
            {
                var referenced = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(projectPath)!, reference));
                Assert.True(File.Exists(referenced), $"Unresolved reference {reference} from {project.Path}");
            }
        }
    }

    [Fact]
    public void AC29_EveryProjectReferenceIsExactlyTheApprovedSetAndPointsInward()
    {
        // Reference exclusivity IS asserted strictly: this is the dependency-direction
        // guarantee AC-29 actually makes.
        AssertReferences("src/DocumentExtraction/DocumentExtraction.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj");
        AssertReferences("src/TaxCases/TaxCases.csproj", "../TaxLogicEngine/TaxLogicEngine.csproj");
        AssertReferences("src/TaxLogicEngine/TaxLogicEngine.csproj");
        AssertReferences(
            "tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj",
            "../../src/DocumentExtraction/DocumentExtraction.csproj");
        AssertReferences("tests/TaxCases.Tests/TaxCases.Tests.csproj", "../../src/TaxCases/TaxCases.csproj");
        AssertReferences(
            "tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj",
            "../../src/TaxLogicEngine/TaxLogicEngine.csproj");
    }

    [Fact]
    public void AC29_NoReverseDependencyOnDocumentExtractionExistsAnywhere()
    {
        foreach (var module in new[] { "src/TaxLogicEngine", "src/TaxCases", "tests/TaxLogicEngine.Tests", "tests/TaxCases.Tests" })
        {
            var root = Path.Combine(RepositoryRoot, module);
            var offenders = HandwrittenSource(root)
                .Where(path => File.ReadAllText(path).Contains("DocumentExtraction", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(RepositoryRoot, path))
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                $"{module} must not reference DocumentExtraction: {string.Join(", ", offenders)}");
        }
    }

    [Fact]
    public void AC29_TheModuleAssemblyReferencesOnlyTaxLogicEngine()
    {
        var nonFramework = typeof(ValidatedExtraction).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("System", StringComparison.Ordinal) &&
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "TaxLogicEngine" }, nonFramework);
    }

    // ---------------------------------------- public contract vs architecture.md

    [Fact]
    public void PublicContractMatchesTheArchitectureManifestExactly()
    {
        string[] expected =
        {
            "DocumentExtraction.Models.ExtractedDocumentKind",
            "DocumentExtraction.Models.ExtractionEnvelope",
            "DocumentExtraction.Models.ExtractionFields",
            "DocumentExtraction.Models.ExtractionProvenance",
            "DocumentExtraction.Models.ValidatedExtraction",
            "DocumentExtraction.Ports.IDocumentExtractionIngress",
            "DocumentExtraction.Services.ExtractionEnvelopeValidator",
            "DocumentExtraction.Services.SupportedExtractionTaxYears",
        };

        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            typeof(ValidatedExtraction).Assembly.GetExportedTypes()
                .Select(type => type.FullName!)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NoPublicValidatedExtractionConstructorExists()
    {
        Assert.Empty(typeof(ValidatedExtraction).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        // Internal, and therefore reachable only from the validator.
        var internalConstructor = Assert.Single(
            typeof(ValidatedExtraction).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.True(internalConstructor.IsAssembly);
    }

    [Fact]
    public void TheIngressPortDeclaresExactlyTheApprovedAsyncSignatureAndHasNoProductionImplementor()
    {
        var method = Assert.Single(typeof(IDocumentExtractionIngress).GetMethods());
        Assert.Equal("SubmitAsync", method.Name);
        Assert.Equal(typeof(Task<ParseResult<ValidatedExtraction>>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ExtractionEnvelope), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.True(parameters[1].HasDefaultValue);

        Assert.Empty(typeof(ValidatedExtraction).Assembly.GetTypes()
            .Where(type => type.IsClass && typeof(IDocumentExtractionIngress).IsAssignableFrom(type)));
    }

    // ------------------------------------------------------ AC-30: reused types

    [Fact]
    public void AC30_ThePayloadAndResultTypesAreTheEnginesOwnAndNothingIsDuplicated()
    {
        var engineAssembly = typeof(T4RawInput).Assembly;

        // The payload is the engine's T4RawInput — no new slip payload type.
        Assert.Equal(
            typeof(T4RawInput),
            typeof(ValidatedExtraction).GetProperty(nameof(ValidatedExtraction.T4Payload))!.PropertyType);
        Assert.Same(engineAssembly, typeof(T4RawInput).Assembly);

        // Validation returns the engine's ParseResult<T> carrying its ValidationError.
        var validate = typeof(ExtractionEnvelopeValidator).GetMethod(nameof(ExtractionEnvelopeValidator.Validate))!;
        Assert.Equal(typeof(ParseResult<ValidatedExtraction>), validate.ReturnType);
        Assert.Same(engineAssembly, typeof(ParseResult<ValidatedExtraction>).GetGenericTypeDefinition().Assembly);
        Assert.Same(engineAssembly, typeof(ValidationError).Assembly);

        // No exported type name collides with an engine type — nothing was duplicated.
        var engineNames = engineAssembly.GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        var collisions = typeof(ValidatedExtraction).Assembly.GetExportedTypes()
            .Select(type => type.Name)
            .Where(engineNames.Contains)
            .ToArray();

        Assert.True(collisions.Length == 0, $"Duplicated reused type(s): {string.Join(", ", collisions)}");
    }

    [Fact]
    public void AC30_NoT5KindOrT5TokenExistsInTheModule()
    {
        Assert.Equal(new[] { "T4Slip", "GenericEvidence" }, Enum.GetNames<ExtractedDocumentKind>());

        var t5 = new Regex(@"\bT5\w*\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        foreach (var file in ModuleSource())
        {
            Assert.False(
                t5.IsMatch(string.Join("\n", ExecutableLines(file))),
                $"{Path.GetFileName(file)} consumes a T5 type.");
        }
    }

    [Fact]
    public void AC26_AC27_NoForbiddenBoundaryTokenSurvivesInTheModule()
    {
        (string Boundary, string Pattern)[] forbidden =
        {
            ("masking helper", @"\bSafeDiagnosticText\b|\bRecipientIdentifier\b|\bRedact"),
            ("engine parser/mapper/rounding", @"\bT4SlipParser\b|\bT4LineMapper\b|\bMoneyRounding\b|\bMath\.Round\b"),
            ("CRA line mapping", @"\bTaxLineEntry\b|\bT4MappingResult\b|\bLineNumber\b|TaxLogicEngine\.Mappings"),
            ("system clock", @"DateTime\.(?:Now|UtcNow)|DateTimeOffset\.(?:Now|UtcNow)"),
            ("filesystem / network / database", @"\bSystem\.IO\b|\bHttpClient\b|\bDbContext\b|\bSystem\.Data\b"),
            ("environment / provider", @"\bEnvironment\.\b|\bOcr\b|\bSmtp\b|Microsoft\.AspNetCore|EntityFrameworkCore"),
            ("non-determinism", @"\bGuid\.NewGuid\b|\bRandom\b|\bdouble\b|\bfloat\b"),
        };

        var executable = string.Join("\n", ModuleSource().SelectMany(ExecutableLines));
        Assert.Contains("ExtractionEnvelopeValidator", executable, StringComparison.Ordinal);

        foreach (var (boundary, pattern) in forbidden)
        {
            Assert.False(
                Regex.IsMatch(executable, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                $"Forbidden {boundary} token found in the module.");
        }
    }

    // -------------------------------------------------- representative flows

    [Fact]
    public void ARepresentativeT4FlowIsAcceptedAndCarriesNoTaxTruth()
    {
        var payload = new T4RawInput
        {
            Year = 2025,
            EmployerName = "Employer Ltd",
            EmployeeName = "Employee",
            Boxes = new[] { new T4RawBox("14", "50000.00"), new T4RawBox("12", "123456789") },
        };

        var result = ExtractionEnvelopeValidator.Validate(Envelope(ExtractedDocumentKind.T4Slip, payload));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal(2025, result.Value!.TaxYear);
        Assert.Same(payload, result.Value.T4Payload);
        Assert.IsType<ValidatedExtraction>(result.Value);
        Assert.DoesNotContain("123456789", result.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARepresentativeGenericEvidenceFlowIsAccepted()
    {
        var result = ExtractionEnvelopeValidator.Validate(
            Envelope(ExtractedDocumentKind.GenericEvidence, payload: null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.T4Payload);
        Assert.Equal("store/ref-1", result.Value.SourceReference);
    }

    [Fact]
    public void NoUnsupportedYearIsEverAccepted()
    {
        Assert.Equal(new[] { 2025 }, SupportedExtractionTaxYears.All);

        foreach (var year in new[] { 2023, 2024, 2026, 2027 })
        {
            Assert.False(SupportedExtractionTaxYears.IsSupported(year));

            var payload = new T4RawInput { Year = year, EmployerName = "Employer Ltd" };
            Assert.False(
                ExtractionEnvelopeValidator.Validate(
                    Envelope(ExtractedDocumentKind.T4Slip, payload, taxYear: year)).IsSuccess,
                $"Year {year} must never be accepted.");
        }
    }

    // ------------------------------------------------------- repository hygiene

    [Fact]
    public void NoForbiddenFileIsTrackedAnywhereInTheRepository()
    {
        var tracked = RunGit("ls-files", "-z")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();

        Assert.NotEmpty(tracked);

        var forbidden = tracked.Where(path =>
            Regex.IsMatch(path, @"(^|/)(bin|obj|\.vs)(/|$)", RegexOptions.IgnoreCase) ||
            path.EndsWith(".user", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("settings.local.json", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(path, @"(^|/)\.env($|\.)", RegexOptions.IgnoreCase) ||
            Path.GetFileName(path).Contains("secret", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Spec007ChangedNothingUnderTaxLogicEngineOrTaxCasesProduction()
    {
        var specCommit = RunGit(
                "log", "--diff-filter=A", "--format=%H", "--",
                "specs/007-document-extraction-contract/prd.md")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(specCommit);

        var changed = RunGit(
                "diff", "--name-only", $"{specCommit[0]}^", "HEAD", "--",
                "src/TaxLogicEngine", "src/TaxCases")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Empty(changed);
    }

    // ------------------------------------------------------------------ helpers

    private static ExtractionEnvelope Envelope(
        ExtractedDocumentKind kind,
        T4RawInput? payload,
        int? taxYear = 2025) => new()
        {
            DocumentId = "doc-1",
            Kind = kind,
            FirmId = "firm-1",
            CaseId = "case-1",
            ParticipantId = "participant-1",
            TaxYear = taxYear,
            Provenance = new ExtractionProvenance { SourceReference = "store/ref-1" },
            T4Payload = payload,
        };

    private static (string Name, string Path)[] DeclaredProjects()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot, "TaxAI.sln"));
        var pattern = new Regex(
            "^Project\\(.*?\\) = \"(?<name>[^\"]+)\", \"(?<path>[^\"]+\\.csproj)\"",
            RegexOptions.Multiline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        return pattern.Matches(solution)
            .Select(match => (
                Name: match.Groups["name"].Value,
                Path: match.Groups["path"].Value.Replace('\\', '/')))
            .ToArray();
    }

    private static void AssertReferences(string projectRelativePath, params string[] expected) =>
        Assert.Equal(
            expected,
            ProjectReferences(Path.Combine(RepositoryRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar))));

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

    private static string[] ModuleSource() =>
        HandwrittenSource(Path.Combine(RepositoryRoot, "src", "DocumentExtraction"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] HandwrittenSource(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>Executable lines only: doc comments legitimately name forbidden concepts.</summary>
    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string RunGit(params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
        return output;
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
