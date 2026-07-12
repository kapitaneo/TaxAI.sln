using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxCases.Models;
using TaxCases.Services;
using TaxLogicEngine.Models;

namespace TaxCases.Tests;

public sealed class ContractTests
{
    private static readonly Type OutcomeResult = typeof(ParseResult<CaseOperationOutcome>);

    [Fact]
    public void Assembly_ExportsExactlyTheApprovedPublicTypes()
    {
        var expected = new[]
        {
            "TaxCases.Models.CaseChangeKind",
            "TaxCases.Models.CaseChangeRecord",
            "TaxCases.Models.CaseOperationOutcome",
            "TaxCases.Models.CaseParticipant",
            "TaxCases.Models.CaseStatus",
            "TaxCases.Models.ChecklistDescriptor",
            "TaxCases.Models.CreateTaxCaseRequest",
            "TaxCases.Models.DocumentCategory",
            "TaxCases.Models.EvidenceDescriptor",
            "TaxCases.Models.EvidenceRequirement",
            "TaxCases.Models.EvidenceRequirementDescriptor",
            "TaxCases.Models.IntakeAnswer",
            "TaxCases.Models.IntakeAnswerDescriptor",
            "TaxCases.Models.IntakeQuestion",
            "TaxCases.Models.IntakeQuestionScope",
            "TaxCases.Models.ParticipantDescriptor",
            "TaxCases.Models.ReadinessReason",
            "TaxCases.Models.ReadinessReasonKind",
            "TaxCases.Models.ReadinessState",
            "TaxCases.Models.ReceivedEvidence",
            "TaxCases.Models.RelationshipKind",
            "TaxCases.Models.RequirementChecklist",
            "TaxCases.Models.ReviewReadiness",
            "TaxCases.Models.TaxCase",
            "TaxCases.Services.CaseStatusTransitions",
            "TaxCases.Services.ReadinessCalculator",
            "TaxCases.Services.SupportedTaxYears"
        };

        var actual = typeof(TaxCase).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Enums_HaveExactNamesAndNumericValues()
    {
        AssertEnum<RelationshipKind>("PrimaryTaxpayer", "SpouseOrCommonLaw", "Dependant");
        AssertEnum<DocumentCategory>("T4Slip", "MedicalExpenseEvidence", "Other");
        AssertEnum<CaseStatus>("Intake", "InformationGathering", "InReview", "Completed", "Cancelled");
        AssertEnum<CaseChangeKind>("CaseCreated", "ParticipantAdded", "EvidenceAttached", "QuestionAnswered", "ChecklistConfirmed", "StatusChanged", "CaseReopened");
        AssertEnum<ReadinessState>("Ready", "NotReady");
        AssertEnum<ReadinessReasonKind>("ChecklistNotConfigured", "UnsatisfiedEvidenceRequirement", "UnansweredIntakeQuestion", "NoPrimaryTaxpayer");
        AssertEnum<IntakeQuestionScope>("Case", "Participant");
    }

    [Fact]
    public void TaxCase_ExposesExactGetOnlyPropertiesAndOperationSignatures()
    {
        Assert.Empty(typeof(TaxCase).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        AssertProperty<string>(nameof(TaxCase.CaseId));
        AssertProperty<string>(nameof(TaxCase.FirmId));
        AssertProperty<int>(nameof(TaxCase.TaxYear));
        AssertProperty<CaseStatus>(nameof(TaxCase.Status));
        AssertProperty<IReadOnlyList<CaseParticipant>>(nameof(TaxCase.Participants));
        AssertProperty<IReadOnlyList<ReceivedEvidence>>(nameof(TaxCase.Evidence));
        AssertProperty<RequirementChecklist?>(nameof(TaxCase.Checklist));
        AssertProperty<IReadOnlyList<IntakeAnswer>>(nameof(TaxCase.Answers));
        AssertProperty<ReviewReadiness>(nameof(TaxCase.Readiness));

        AssertMethod(nameof(TaxCase.Create), true, typeof(CreateTaxCaseRequest));
        AssertMethod(nameof(TaxCase.AddParticipant), false, typeof(ParticipantDescriptor), typeof(string));
        AssertMethod(nameof(TaxCase.AttachEvidence), false, typeof(EvidenceDescriptor), typeof(string));
        AssertMethod(nameof(TaxCase.ConfirmChecklist), false, typeof(ChecklistDescriptor), typeof(string));
        AssertMethod(nameof(TaxCase.AnswerQuestion), false, typeof(IntakeAnswerDescriptor), typeof(string));
        AssertMethod(nameof(TaxCase.TransitionTo), false, typeof(CaseStatus), typeof(string));
        AssertMethod(nameof(TaxCase.Reopen), false, typeof(string), typeof(string));

        var ownershipMutators = typeof(TaxCase)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => !method.IsSpecialName)
            .Where(method =>
                method.Name.Contains("FirmId", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Ownership", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ownershipMutators);
    }

    [Fact]
    public void RecordsAndDescriptors_HaveParticipantScopedContractAndDefensiveCollections()
    {
        var requirements = new List<EvidenceRequirement>
        {
            new("requirement", "participant", DocumentCategory.T4Slip, 2)
        };
        var questions = new List<IntakeQuestion>
        {
            new("question", "Prompt", IntakeQuestionScope.Participant, "participant")
        };
        var checklist = new RequirementChecklist(requirements, questions);
        requirements.Clear();
        questions.Clear();

        Assert.Single(checklist.EvidenceRequirements);
        Assert.Single(checklist.Questions);
        Assert.Equal(2, checklist.EvidenceRequirements[0].MinimumCount);

        var descriptorRequirements = new List<EvidenceRequirementDescriptor>
        {
            new("requirement", "participant", "T4Slip", 2)
        };
        var descriptorQuestions = new List<IntakeQuestion>
        {
            new("question", "Prompt", IntakeQuestionScope.Case, null)
        };
        var descriptor = new ChecklistDescriptor(descriptorRequirements, descriptorQuestions);
        descriptorRequirements.Clear();
        descriptorQuestions.Clear();

        Assert.Single(descriptor.EvidenceRequirements!);
        Assert.Single(descriptor.Questions!);

        var reasons = new List<ReadinessReason>
        {
            new(ReadinessReasonKind.ChecklistNotConfigured, "detail")
        };
        var readiness = new ReviewReadiness(ReadinessState.NotReady, reasons);
        reasons.Clear();
        Assert.Single(readiness.Reasons);
    }

    [Fact]
    public void EveryPublicInstanceType_OwnsSafeToString()
    {
        var instanceTypes = typeof(TaxCase).Assembly
            .GetExportedTypes()
            .Where(type => type.IsClass && !(type.IsAbstract && type.IsSealed));

        foreach (var type in instanceTypes)
        {
            var toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);
            Assert.NotNull(toString);
            Assert.Equal(type, toString!.DeclaringType);
        }
    }

    [Fact]
    public void EveryPublicStringProperty_IsJsonIgnoredExceptMaskedSin()
    {
        var stringProperties = typeof(TaxCase).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.PropertyType == typeof(string))
            .ToArray();

        Assert.NotEmpty(stringProperties);
        foreach (var property in stringProperties)
        {
            if (property.DeclaringType == typeof(ParticipantDescriptor) &&
                property.Name == nameof(ParticipantDescriptor.MaskedSin))
            {
                Assert.Null(property.GetCustomAttribute<JsonIgnoreAttribute>());
                continue;
            }

            Assert.NotNull(property.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    public void MandatoryPiiMatrix_RetainsDomainValuesButProtectsJsonAndDiagnostics(string raw)
    {
        var participant = new ParticipantDescriptor(raw, RelationshipKind.PrimaryTaxpayer, raw, raw, raw);
        var request = new CreateTaxCaseRequest(raw, raw, 2025, participant, raw);
        var storedParticipant = new CaseParticipant(raw, RelationshipKind.PrimaryTaxpayer, raw, new RecipientIdentifier(raw));
        var evidence = new ReceivedEvidence(raw, DocumentCategory.T4Slip, raw, 2025, raw);
        var requirement = new EvidenceRequirement(raw, raw, DocumentCategory.T4Slip, 1);
        var question = new IntakeQuestion(raw, raw, IntakeQuestionScope.Participant, raw);
        var answer = new IntakeAnswer(raw, raw, raw);
        var reason = new ReadinessReason(ReadinessReasonKind.UnansweredIntakeQuestion, raw);
        var change = new CaseChangeRecord(raw, CaseChangeKind.CaseReopened, raw, raw, raw, CaseStatus.Completed, CaseStatus.InformationGathering, raw, raw);
        var invalidCategory = new EvidenceDescriptor(raw, raw, raw, 2025, raw, raw);
        var requirementDescriptor = new EvidenceRequirementDescriptor(raw, raw, raw, 1);
        var answerDescriptor = new IntakeAnswerDescriptor(raw, raw, raw);

        Assert.Equal(raw, request.CaseId);
        Assert.Equal(raw, request.FirmId);
        Assert.Equal(raw, participant.ParticipantId);
        Assert.Equal(raw, participant.DisplayName);
        Assert.Equal(raw, evidence.EvidenceId);
        Assert.Equal(raw, evidence.SourceReference);
        Assert.Equal(raw, requirement.RequirementId);
        Assert.Equal(raw, question.QuestionId);
        Assert.Equal(raw, question.Prompt);
        Assert.Equal(raw, answer.Answer);
        Assert.Equal(raw, invalidCategory.Category);
        Assert.Equal(raw, request.CorrelationReference);
        Assert.Equal(raw, change.Detail);

        object[] values =
        [
            participant, request, storedParticipant, evidence, requirement,
            question, answer, reason, change, invalidCategory,
            requirementDescriptor, answerDescriptor
        ];

        foreach (var value in values)
        {
            var json = JsonSerializer.Serialize(value, value.GetType());
            Assert.DoesNotContain(raw, json, StringComparison.Ordinal);

            var diagnostic = value.ToString()!;
            Assert.DoesNotContain(raw, diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("123 456 789", diagnostic, StringComparison.Ordinal);
        }

        Assert.Contains("***-**-789", participant.ToString(), StringComparison.Ordinal);
        Assert.Contains("***-**-789", change.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LocalInstructions_ExistBeforeImplementationWavesAndContainRequiredBoundaries()
    {
        var root = FindRepositoryRoot();
        var claude = File.ReadAllText(Path.Combine(root, "src", "TaxCases", "CLAUDE.md"));
        var agents = File.ReadAllText(Path.Combine(root, "src", "TaxCases", "AGENTS.md"));

        Assert.Contains("TaxCases → TaxLogicEngine", claude, StringComparison.Ordinal);
        Assert.Contains("exactly tax year 2025", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SafeDiagnosticText", claude, StringComparison.Ordinal);
        Assert.Contains("JsonIgnore", claude, StringComparison.Ordinal);
        Assert.Contains("fail", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ASP.NET Core", claude, StringComparison.Ordinal);
        Assert.Contains("EF Core", claude, StringComparison.Ordinal);

        Assert.Contains("../../AGENTS.md", agents, StringComparison.Ordinal);
        Assert.Contains("../../CLAUDE.md", agents, StringComparison.Ordinal);
        Assert.Contains("[CLAUDE.md](CLAUDE.md)", agents, StringComparison.Ordinal);
        Assert.DoesNotContain("Never merge", agents, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertEnum<TEnum>(params string[] names)
        where TEnum : struct, Enum
    {
        Assert.Equal(names, Enum.GetNames<TEnum>());
        Assert.Equal(Enumerable.Range(0, names.Length), Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value)));
    }

    private static void AssertProperty<T>(string name)
    {
        var property = typeof(TaxCase).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(typeof(T), property!.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
    }

    private static void AssertMethod(string name, bool isStatic, params Type[] parameterTypes)
    {
        var method = typeof(TaxCase).GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        Assert.Equal(isStatic, method!.IsStatic);
        Assert.Equal(OutcomeResult, method.ReturnType);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
