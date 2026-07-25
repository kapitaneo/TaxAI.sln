using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaxCases.Models;
using TaxCases.Services;
using TaxCases.Tests.Boundaries;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.EndToEnd;

/// <summary>
/// Final repository-level verification for spec 006 / T10.
/// </summary>
public sealed class RepositoryVerificationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly Type OutcomeResult = typeof(ParseResult<CaseOperationOutcome>);

    [Fact]
    public void SolutionContainsTheFourSpec006ProjectsAndAllReferencesResolve()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot, "TaxAI.sln"));
        var projectPattern = new Regex(
            "^Project\\(.*?\\) = \\\"(?<name>[^\\\"]+)\\\", \\\"(?<path>[^\\\"]+\\.csproj)\\\"",
            RegexOptions.Multiline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var projects = projectPattern.Matches(solution)
            .Select(match => new
            {
                Name = match.Groups["name"].Value,
                Path = match.Groups["path"].Value.Replace('\\', '/')
            })
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            (Name: "TaxCases", Path: "src/TaxCases/TaxCases.csproj"),
            (Name: "TaxCases.Tests", Path: "tests/TaxCases.Tests/TaxCases.Tests.csproj"),
            (Name: "TaxLogicEngine", Path: "src/TaxLogicEngine/TaxLogicEngine.csproj"),
            (Name: "TaxLogicEngine.Tests", Path: "tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj")
        };

        // Presence-and-uniqueness, not exclusivity: each spec 006 project must be
        // declared exactly once at its approved path, while the solution stays free to
        // declare later modules' projects. Every declared project — including newer
        // ones — must still resolve, which the loop below enforces.
        foreach (var (name, path) in expected)
        {
            var declared = Assert.Single(projects.Where(project => project.Name == name));
            Assert.Equal(path, declared.Path);
        }

        foreach (var project in projects)
        {
            var projectPath = Path.GetFullPath(Path.Combine(RepositoryRoot, project.Path));
            Assert.True(File.Exists(projectPath), $"Missing solution project: {project.Path}");

            foreach (var reference in ProjectReferences(projectPath))
            {
                var referencedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, reference));
                Assert.True(File.Exists(referencedPath), $"Unresolved reference {reference} from {project.Path}");
            }
        }

        Assert.Equal(
            ["../TaxLogicEngine/TaxLogicEngine.csproj"],
            ProjectReferences(Path.Combine(RepositoryRoot, "src", "TaxCases", "TaxCases.csproj")));
        Assert.Empty(ProjectReferences(Path.Combine(RepositoryRoot, "src", "TaxLogicEngine", "TaxLogicEngine.csproj")));
    }

    [Fact]
    public void PublicContractMatchesArchitectureManifestExactly()
    {
        string[] expectedTypes =
        [
            "TaxCases.Models.CaseChangeKind", "TaxCases.Models.CaseChangeRecord",
            "TaxCases.Models.CaseOperationOutcome", "TaxCases.Models.CaseParticipant",
            "TaxCases.Models.CaseStatus", "TaxCases.Models.ChecklistDescriptor",
            "TaxCases.Models.CreateTaxCaseRequest", "TaxCases.Models.DocumentCategory",
            "TaxCases.Models.EvidenceDescriptor", "TaxCases.Models.EvidenceRequirement",
            "TaxCases.Models.EvidenceRequirementDescriptor", "TaxCases.Models.IntakeAnswer",
            "TaxCases.Models.IntakeAnswerDescriptor", "TaxCases.Models.IntakeQuestion",
            "TaxCases.Models.IntakeQuestionScope", "TaxCases.Models.ParticipantDescriptor",
            "TaxCases.Models.ReadinessReason", "TaxCases.Models.ReadinessReasonKind",
            "TaxCases.Models.ReadinessState", "TaxCases.Models.ReceivedEvidence",
            "TaxCases.Models.RelationshipKind", "TaxCases.Models.RequirementChecklist",
            "TaxCases.Models.ReviewReadiness", "TaxCases.Models.TaxCase",
            "TaxCases.Services.CaseStatusTransitions", "TaxCases.Services.ReadinessCalculator",
            "TaxCases.Services.SupportedTaxYears"
        ];

        Assert.Equal(
            expectedTypes.Order(StringComparer.Ordinal),
            typeof(TaxCase).Assembly.GetExportedTypes().Select(type => type.FullName!).Order(StringComparer.Ordinal));

        AssertEnum<RelationshipKind>("PrimaryTaxpayer", "SpouseOrCommonLaw", "Dependant");
        AssertEnum<DocumentCategory>("T4Slip", "MedicalExpenseEvidence", "Other");
        AssertEnum<CaseStatus>("Intake", "InformationGathering", "InReview", "Completed", "Cancelled");
        AssertEnum<CaseChangeKind>("CaseCreated", "ParticipantAdded", "EvidenceAttached", "QuestionAnswered", "ChecklistConfirmed", "StatusChanged", "CaseReopened");
        AssertEnum<ReadinessState>("Ready", "NotReady");
        AssertEnum<ReadinessReasonKind>("ChecklistNotConfigured", "UnsatisfiedEvidenceRequirement", "UnansweredIntakeQuestion", "NoPrimaryTaxpayer");
        AssertEnum<IntakeQuestionScope>("Case", "Participant");

        AssertConstructor<CaseParticipant>(typeof(string), typeof(RelationshipKind), typeof(string), typeof(RecipientIdentifier));
        AssertConstructor<ReceivedEvidence>(typeof(string), typeof(DocumentCategory), typeof(string), typeof(int), typeof(string));
        AssertConstructor<EvidenceRequirement>(typeof(string), typeof(string), typeof(DocumentCategory), typeof(int));
        AssertConstructor<IntakeQuestion>(typeof(string), typeof(string), typeof(IntakeQuestionScope), typeof(string));
        AssertConstructor<IntakeAnswer>(typeof(string), typeof(string), typeof(string));
        AssertConstructor<RequirementChecklist>(typeof(IReadOnlyList<EvidenceRequirement>), typeof(IReadOnlyList<IntakeQuestion>));
        AssertConstructor<ReadinessReason>(typeof(ReadinessReasonKind), typeof(string));
        AssertConstructor<ReviewReadiness>(typeof(ReadinessState), typeof(IReadOnlyList<ReadinessReason>));
        AssertConstructor<CaseChangeRecord>(typeof(string), typeof(CaseChangeKind), typeof(string), typeof(string), typeof(string), typeof(CaseStatus?), typeof(CaseStatus?), typeof(string), typeof(string));
        AssertConstructor<CaseOperationOutcome>(typeof(TaxCase), typeof(CaseChangeRecord));
        AssertConstructor<CreateTaxCaseRequest>(typeof(string), typeof(string), typeof(int?), typeof(ParticipantDescriptor), typeof(string));
        AssertConstructor<ParticipantDescriptor>(typeof(string), typeof(RelationshipKind), typeof(string), typeof(string), typeof(string));
        AssertConstructor<EvidenceDescriptor>(typeof(string), typeof(string), typeof(string), typeof(int?), typeof(string), typeof(string));
        AssertConstructor<EvidenceRequirementDescriptor>(typeof(string), typeof(string), typeof(string), typeof(int?));
        AssertConstructor<IntakeAnswerDescriptor>(typeof(string), typeof(string), typeof(string));
        AssertConstructor<ChecklistDescriptor>(typeof(IReadOnlyList<EvidenceRequirementDescriptor>), typeof(IReadOnlyList<IntakeQuestion>));
        Assert.Empty(typeof(TaxCase).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        AssertProperties<CaseParticipant>(("ParticipantId", typeof(string)), ("Relationship", typeof(RelationshipKind)), ("DisplayName", typeof(string)), ("Sin", typeof(RecipientIdentifier)));
        AssertProperties<ReceivedEvidence>(("EvidenceId", typeof(string)), ("Category", typeof(DocumentCategory)), ("ParticipantId", typeof(string)), ("TaxYear", typeof(int)), ("SourceReference", typeof(string)));
        AssertProperties<EvidenceRequirement>(("RequirementId", typeof(string)), ("ParticipantId", typeof(string)), ("Category", typeof(DocumentCategory)), ("MinimumCount", typeof(int)));
        AssertProperties<IntakeQuestion>(("QuestionId", typeof(string)), ("Prompt", typeof(string)), ("Scope", typeof(IntakeQuestionScope)), ("ParticipantId", typeof(string)));
        AssertProperties<IntakeAnswer>(("QuestionId", typeof(string)), ("ParticipantId", typeof(string)), ("Answer", typeof(string)));
        AssertProperties<RequirementChecklist>(("EvidenceRequirements", typeof(IReadOnlyList<EvidenceRequirement>)), ("Questions", typeof(IReadOnlyList<IntakeQuestion>)));
        AssertProperties<ReadinessReason>(("Kind", typeof(ReadinessReasonKind)), ("Detail", typeof(string)));
        AssertProperties<ReviewReadiness>(("State", typeof(ReadinessState)), ("Reasons", typeof(IReadOnlyList<ReadinessReason>)));
        AssertProperties<CaseChangeRecord>(("CaseId", typeof(string)), ("Kind", typeof(CaseChangeKind)), ("ParticipantId", typeof(string)), ("EvidenceId", typeof(string)), ("QuestionId", typeof(string)), ("PreviousStatus", typeof(CaseStatus?)), ("ResultingStatus", typeof(CaseStatus?)), ("CorrelationReference", typeof(string)), ("Detail", typeof(string)));
        AssertProperties<CaseOperationOutcome>(("Case", typeof(TaxCase)), ("Change", typeof(CaseChangeRecord)));
        AssertProperties<CreateTaxCaseRequest>(("CaseId", typeof(string)), ("FirmId", typeof(string)), ("TaxYear", typeof(int?)), ("PrimaryTaxpayer", typeof(ParticipantDescriptor)), ("CorrelationReference", typeof(string)));
        AssertProperties<ParticipantDescriptor>(("ParticipantId", typeof(string)), ("Relationship", typeof(RelationshipKind)), ("DisplayName", typeof(string)), ("MaskedSin", typeof(string)), ("FirmId", typeof(string)));
        AssertProperties<EvidenceDescriptor>(("EvidenceId", typeof(string)), ("Category", typeof(string)), ("ParticipantId", typeof(string)), ("TaxYear", typeof(int?)), ("SourceReference", typeof(string)), ("FirmId", typeof(string)));
        AssertProperties<EvidenceRequirementDescriptor>(("RequirementId", typeof(string)), ("ParticipantId", typeof(string)), ("Category", typeof(string)), ("MinimumCount", typeof(int?)));
        AssertProperties<IntakeAnswerDescriptor>(("QuestionId", typeof(string)), ("ParticipantId", typeof(string)), ("Answer", typeof(string)));
        AssertProperties<ChecklistDescriptor>(("EvidenceRequirements", typeof(IReadOnlyList<EvidenceRequirementDescriptor>)), ("Questions", typeof(IReadOnlyList<IntakeQuestion>)));
        AssertProperties<TaxCase>(("CaseId", typeof(string)), ("FirmId", typeof(string)), ("TaxYear", typeof(int)), ("Status", typeof(CaseStatus)), ("Participants", typeof(IReadOnlyList<CaseParticipant>)), ("Evidence", typeof(IReadOnlyList<ReceivedEvidence>)), ("Checklist", typeof(RequirementChecklist)), ("Answers", typeof(IReadOnlyList<IntakeAnswer>)), ("Readiness", typeof(ReviewReadiness)));
        Assert.All(
            typeof(ParticipantDescriptor).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.Null(property.SetMethod));
        Assert.All(
            typeof(TaxCase).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.Null(property.SetMethod));

        AssertOperation(nameof(TaxCase.Create), true, typeof(CreateTaxCaseRequest));
        AssertOperation(nameof(TaxCase.AddParticipant), false, typeof(ParticipantDescriptor), typeof(string));
        AssertOperation(nameof(TaxCase.AttachEvidence), false, typeof(EvidenceDescriptor), typeof(string));
        AssertOperation(nameof(TaxCase.ConfirmChecklist), false, typeof(ChecklistDescriptor), typeof(string));
        AssertOperation(nameof(TaxCase.AnswerQuestion), false, typeof(IntakeAnswerDescriptor), typeof(string));
        AssertOperation(nameof(TaxCase.TransitionTo), false, typeof(CaseStatus), typeof(string));
        AssertOperation(nameof(TaxCase.Reopen), false, typeof(string), typeof(string));

        Assert.Equal(typeof(IReadOnlyCollection<int>), typeof(SupportedTaxYears).GetProperty(nameof(SupportedTaxYears.All))!.PropertyType);
        Assert.Equal(typeof(bool), typeof(SupportedTaxYears).GetMethod(nameof(SupportedTaxYears.IsSupported))!.ReturnType);
        Assert.Equal(typeof(bool), typeof(CaseStatusTransitions).GetMethod(nameof(CaseStatusTransitions.IsAllowed))!.ReturnType);
        Assert.Equal(typeof(ReviewReadiness), typeof(ReadinessCalculator).GetMethod(nameof(ReadinessCalculator.Calculate))!.ReturnType);
    }

    [Fact]
    public void UnsupportedValuesOwnershipAndReadinessMutatorsAreAbsent()
    {
        Assert.Equal([2025], SupportedTaxYears.All);
        Assert.False(SupportedTaxYears.IsSupported(2024));
        Assert.False(SupportedTaxYears.IsSupported(2026));
        Assert.Equal(["T4Slip", "MedicalExpenseEvidence", "Other"], Enum.GetNames<DocumentCategory>());
        Assert.Equal(["Intake", "InformationGathering", "InReview", "Completed", "Cancelled"], Enum.GetNames<CaseStatus>());
        Assert.DoesNotContain("Archived", Enum.GetNames<CaseStatus>());

        Assert.Null(typeof(TaxCase).GetProperty(nameof(TaxCase.FirmId))!.SetMethod);
        Assert.Null(typeof(TaxCase).GetProperty(nameof(TaxCase.Readiness))!.SetMethod);
        Assert.DoesNotContain(
            typeof(TaxCase).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static),
            method =>
                method.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Ownership", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("SetReadiness", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepresentativeCompleteCase_ReachesCompletedWithReadyState()
    {
        const string primaryId = "primary";
        const string spouseId = "spouse";
        var taxCase = Accepted(TaxCase.Create(new CreateTaxCaseRequest(
            "case-final", "firm-final", 2025,
            new ParticipantDescriptor(primaryId, RelationshipKind.PrimaryTaxpayer, "Primary", null, "firm-final"),
            "create")));

        taxCase = Accepted(taxCase.AddParticipant(
            new ParticipantDescriptor(spouseId, RelationshipKind.SpouseOrCommonLaw, "Spouse", null, "firm-final"),
            "spouse"));
        taxCase = Accepted(taxCase.AddParticipant(
            new ParticipantDescriptor("dependant", RelationshipKind.Dependant, "Dependant", null, "firm-final"),
            "dependant"));
        taxCase = Accepted(taxCase.ConfirmChecklist(
            new ChecklistDescriptor(
                [
                    new EvidenceRequirementDescriptor("primary-t4", primaryId, "T4Slip", 1),
                    new EvidenceRequirementDescriptor("spouse-medical", spouseId, "MedicalExpenseEvidence", 1)
                ],
                [new IntakeQuestion("family-question", "Confirm family information", IntakeQuestionScope.Case, null)]),
            "checklist"));
        taxCase = Accepted(taxCase.AttachEvidence(
            new EvidenceDescriptor("t4-1", "T4Slip", primaryId, 2025, "source-t4", "firm-final"),
            "t4"));
        taxCase = Accepted(taxCase.AttachEvidence(
            new EvidenceDescriptor("medical-1", "MedicalExpenseEvidence", spouseId, 2025, "source-medical", "firm-final"),
            "medical"));
        taxCase = Accepted(taxCase.AnswerQuestion(
            new IntakeAnswerDescriptor("family-question", null, "confirmed"),
            "answer"));

        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
        Assert.Equal(3, taxCase.Participants.Count);
        Assert.Equal(2, taxCase.Evidence.Count);
        Assert.Single(taxCase.Answers);

        taxCase = Accepted(taxCase.TransitionTo(CaseStatus.InformationGathering, "gathering"));
        taxCase = Accepted(taxCase.TransitionTo(CaseStatus.InReview, "review"));
        taxCase = Accepted(taxCase.TransitionTo(CaseStatus.Completed, "complete"));

        Assert.Equal(CaseStatus.Completed, taxCase.Status);
        Assert.Equal(ReadinessState.Ready, taxCase.Readiness.State);
    }

    [Fact]
    public void ForbiddenInfrastructureAndProviderDependenciesAreAbsent()
    {
        var taxCasesProject = Path.Combine(RepositoryRoot, "src", "TaxCases", "TaxCases.csproj");
        var project = XDocument.Load(taxCasesProject);
        Assert.Empty(project.Descendants().Where(element => element.Name.LocalName is "PackageReference" or "FrameworkReference"));

        var nonFrameworkReferences = typeof(TaxCase).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name != "System" && name != "netstandard" && !name.StartsWith("System.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["TaxLogicEngine"], nonFrameworkReferences);

        var sourceText = string.Join(
            "\n",
            Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", "TaxCases"), "*.cs", SearchOption.AllDirectories)
                .Where(IsHandwrittenSource)
                .Select(File.ReadAllText));

        foreach (var item in TaxCasesBoundaryTests.ForbiddenProductionPatterns())
        {
            var boundary = (string)item[0];
            var pattern = (string)item[1];
            Assert.False(
                Regex.IsMatch(sourceText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                $"Forbidden production boundary detected: {boundary}");
        }
    }

    [Fact]
    public void RepositoryHygiene_HasNoForbiddenTrackedFiles()
    {
        var tracked = RunGit("ls-files", "-z")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .ToArray();

        var forbidden = tracked.Where(path =>
            Regex.IsMatch(path, @"(^|/)(bin|obj|\.vs)(/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            path.EndsWith(".user", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("settings.local.json", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(path, @"(^|/)\.env($|\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Path.GetFileName(path).Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void TaxLogicEngineProductionAndTests_RemainedUnchangedThroughoutSpec006()
    {
        var introductionCommit = RunGit(
                "log", "--diff-filter=A", "--format=%H", "--",
                "specs/006-tax-case-core/prd.md")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(introductionCommit);

        var changed = RunGit(
                "diff", "--name-only", $"{introductionCommit[0]}^", "HEAD", "--",
                "src/TaxLogicEngine", "tests/TaxLogicEngine.Tests")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        Assert.Empty(changed);
    }

    [Fact]
    public void AllPrerequisiteMarkersAndAcceptanceCriteriaHavePassingEvidence()
    {
        for (var task = 1; task <= 9; task++)
        {
            var marker = Path.Combine(
                RepositoryRoot,
                "specs", "006-tax-case-core", "status", $"T{task:00}.done");
            Assert.True(File.Exists(marker), $"Missing prerequisite marker T{task:00}.done");
        }

        var verificationPath = Path.Combine(RepositoryRoot, "specs", "006-tax-case-core", "verification.md");
        Assert.True(File.Exists(verificationPath));
        // `.gitattributes` sets `* text=auto`, so this file is checked out with CRLF on
        // Windows. In .NET multiline mode `$` matches only before `\n`, never before `\r\n`,
        // so the row pattern below is matched against LF-normalised text.
        var verification = File.ReadAllText(verificationPath).Replace("\r\n", "\n");
        var rows = Regex.Matches(
            verification,
            @"^\| AC-(?<number>\d+) \| \*\*PASS\*\* \| (?<evidence>.+) \|$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.Equal(43, rows.Count);
        Assert.Equal(
            Enumerable.Range(1, 43),
            rows.Select(row => int.Parse(row.Groups["number"].Value)));
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Groups["evidence"].Value)));
        Assert.DoesNotContain("**FAIL**", verification, StringComparison.Ordinal);
    }

    private static TaxCase Accepted(ParseResult<CaseOperationOutcome> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
        return result.Value.Case;
    }

    private static void AssertEnum<TEnum>(params string[] expectedNames)
        where TEnum : struct, Enum
    {
        Assert.Equal(expectedNames, Enum.GetNames<TEnum>());
        Assert.Equal(
            Enumerable.Range(0, expectedNames.Length),
            Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value)));
    }

    private static void AssertConstructor<T>(params Type[] parameterTypes)
    {
        var constructors = typeof(T).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var constructor = Assert.Single(constructors);
        Assert.Equal(parameterTypes, constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertProperties<T>(params (string Name, Type Type)[] expected)
    {
        var actual = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken)
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static void AssertOperation(string name, bool isStatic, params Type[] parameterTypes)
    {
        var operation = typeof(TaxCase).GetMethod(name, parameterTypes);
        Assert.NotNull(operation);
        Assert.Equal(isStatic, operation!.IsStatic);
        Assert.Equal(OutcomeResult, operation.ReturnType);
    }

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsHandwrittenSource(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
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
            ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
