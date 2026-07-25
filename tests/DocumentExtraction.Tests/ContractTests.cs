using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using DocumentExtraction.Models;
using DocumentExtraction.Ports;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Tests;

/// <summary>
/// Reflection verification that the compiled public contract matches
/// specs/007-document-extraction-contract/architecture.md exactly, plus the
/// Wave-0 instruction-file checks. Behavioural validation is owned by later
/// waves; nothing here calls <see cref="ExtractionEnvelopeValidator.Validate"/>.
/// </summary>
public sealed class ContractTests
{
    private static readonly Assembly ProductionAssembly = typeof(ExtractionEnvelope).Assembly;

    // --- ExtractedDocumentKind ---

    [Fact]
    public void ExtractedDocumentKind_DefinesExactlyT4SlipOneAndGenericEvidenceTwo()
    {
        var names = Enum.GetNames<ExtractedDocumentKind>();

        Assert.Equal(2, names.Length);
        Assert.Contains("T4Slip", names);
        Assert.Contains("GenericEvidence", names);
        Assert.Equal(1, (int)ExtractedDocumentKind.T4Slip);
        Assert.Equal(2, (int)ExtractedDocumentKind.GenericEvidence);
    }

    [Fact]
    public void ExtractedDocumentKind_HasNoT5Value()
    {
        Assert.DoesNotContain(
            Enum.GetNames<ExtractedDocumentKind>(),
            name => name.Contains("T5", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractedDocumentKind_DefaultIsNotADefinedValue()
    {
        Assert.False(Enum.IsDefined(default(ExtractedDocumentKind)));
    }

    // --- ExtractionProvenance ---

    [Fact]
    public void ExtractionProvenance_HasExactContractShape()
    {
        var type = typeof(ExtractionProvenance);

        Assert.True(type.IsSealed);
        AssertNotARecord(type);
        AssertInitOnlyProperty(type, "SourceReference", typeof(string));
        AssertInitOnlyProperty(type, "ProviderLabel", typeof(string));
        AssertInitOnlyProperty(type, "Confidence", typeof(decimal?));
        Assert.Equal(3, InstanceProperties(type).Length);

        var instance = new ExtractionProvenance
        {
            SourceReference = "ref-1",
            ProviderLabel = "provider",
            Confidence = 0.5m,
        };
        Assert.Equal("ref-1", instance.SourceReference);
        Assert.Equal("provider", instance.ProviderLabel);
        Assert.Equal(0.5m, instance.Confidence);
    }

    // --- ExtractionEnvelope ---

    [Fact]
    public void ExtractionEnvelope_HasExactContractShape()
    {
        var type = typeof(ExtractionEnvelope);

        Assert.True(type.IsSealed);
        AssertNotARecord(type);
        AssertInitOnlyProperty(type, "DocumentId", typeof(string));
        AssertInitOnlyProperty(type, "Kind", typeof(ExtractedDocumentKind?));
        AssertInitOnlyProperty(type, "FirmId", typeof(string));
        AssertInitOnlyProperty(type, "CaseId", typeof(string));
        AssertInitOnlyProperty(type, "ParticipantId", typeof(string));
        AssertInitOnlyProperty(type, "TaxYear", typeof(int?));
        AssertInitOnlyProperty(type, "Provenance", typeof(ExtractionProvenance));
        AssertInitOnlyProperty(type, "T4Payload", typeof(T4RawInput));
        Assert.Equal(8, InstanceProperties(type).Length);

        var payload = new T4RawInput { Year = 2025 };
        var instance = new ExtractionEnvelope
        {
            DocumentId = "doc-1",
            Kind = ExtractedDocumentKind.T4Slip,
            FirmId = "firm-1",
            CaseId = "case-1",
            ParticipantId = "participant-1",
            TaxYear = 2025,
            Provenance = new ExtractionProvenance { SourceReference = "ref-1" },
            T4Payload = payload,
        };
        Assert.Equal("doc-1", instance.DocumentId);
        Assert.Same(payload, instance.T4Payload);
    }

    // --- ValidatedExtraction ---

    [Fact]
    public void ValidatedExtraction_HasExactContractShape()
    {
        var type = typeof(ValidatedExtraction);

        Assert.True(type.IsSealed);
        AssertNotARecord(type);
        AssertGetOnlyProperty(type, "DocumentId", typeof(string));
        AssertGetOnlyProperty(type, "Kind", typeof(ExtractedDocumentKind));
        AssertGetOnlyProperty(type, "FirmId", typeof(string));
        AssertGetOnlyProperty(type, "CaseId", typeof(string));
        AssertGetOnlyProperty(type, "ParticipantId", typeof(string));
        AssertGetOnlyProperty(type, "TaxYear", typeof(int));
        AssertGetOnlyProperty(type, "SourceReference", typeof(string));
        AssertGetOnlyProperty(type, "ProviderLabel", typeof(string));
        AssertGetOnlyProperty(type, "Confidence", typeof(decimal?));
        AssertGetOnlyProperty(type, "T4Payload", typeof(T4RawInput));
        Assert.Equal(10, InstanceProperties(type).Length);
    }

    [Fact]
    public void ValidatedExtraction_HasNoPublicConstructor()
    {
        Assert.Empty(typeof(ValidatedExtraction).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void ValidatedExtraction_IsConstructibleOnlyThroughItsInternalConstructor()
    {
        var payload = new T4RawInput { Year = 2025 };
        var instance = CreateValidatedExtraction(payload);

        Assert.Equal("doc-1", instance.DocumentId);
        Assert.Equal(ExtractedDocumentKind.T4Slip, instance.Kind);
        Assert.Equal("firm-1", instance.FirmId);
        Assert.Equal("case-1", instance.CaseId);
        Assert.Equal("participant-1", instance.ParticipantId);
        Assert.Equal(2025, instance.TaxYear);
        Assert.Equal("ref-1", instance.SourceReference);
        Assert.Equal("provider", instance.ProviderLabel);
        Assert.Equal(0.5m, instance.Confidence);
        Assert.Same(payload, instance.T4Payload);
    }

    // --- ExtractionFields ---

    [Fact]
    public void ExtractionFields_DeclaresExactlyTheClosedConstantSet()
    {
        var expected = new Dictionary<string, string>
        {
            ["Envelope"] = "Envelope",
            ["DocumentId"] = "DocumentId",
            ["Kind"] = "Kind",
            ["FirmId"] = "FirmId",
            ["CaseId"] = "CaseId",
            ["ParticipantId"] = "ParticipantId",
            ["TaxYear"] = "TaxYear",
            ["SourceReference"] = "SourceReference",
            ["Confidence"] = "Confidence",
            ["Payload"] = "Payload",
            ["PayloadYear"] = "PayloadYear",
            ["PayloadContent"] = "PayloadContent",
        };

        var type = typeof(ExtractionFields);
        Assert.True(type.IsAbstract && type.IsSealed); // static class

        var constants = type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.Equal(expected.Count, constants.Count);
        foreach (var (name, value) in expected)
        {
            Assert.True(constants.TryGetValue(name, out var actual), $"Missing constant {name}");
            Assert.Equal(value, actual);
        }
    }

    // --- IDocumentExtractionIngress ---

    [Fact]
    public void Ingress_DeclaresExactlyOneSubmitAsyncWithTheContractSignature()
    {
        var methods = typeof(IDocumentExtractionIngress).GetMethods();
        var method = Assert.Single(methods);

        Assert.Equal("SubmitAsync", method.Name);
        Assert.Equal(typeof(Task<ParseResult<ValidatedExtraction>>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ExtractionEnvelope), parameters[0].ParameterType);
        Assert.False(parameters[0].HasDefaultValue);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.True(parameters[1].HasDefaultValue);
    }

    [Fact]
    public void Ingress_HasNoImplementationInTheProductionAssembly()
    {
        var implementations = ProductionAssembly
            .GetTypes()
            .Where(t => !t.IsInterface && typeof(IDocumentExtractionIngress).IsAssignableFrom(t));

        Assert.Empty(implementations);
    }

    // --- ExtractionEnvelopeValidator / SupportedExtractionTaxYears shapes ---

    [Fact]
    public void Validator_DeclaresTheExactStaticValidateSignature()
    {
        var type = typeof(ExtractionEnvelopeValidator);
        Assert.True(type.IsAbstract && type.IsSealed); // static class

        var method = type.GetMethod("Validate", new[] { typeof(ExtractionEnvelope) });
        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
        Assert.Equal(typeof(ParseResult<ValidatedExtraction>), method.ReturnType);
    }

    [Fact]
    public void SupportedExtractionTaxYears_DeclaresTheExactPolicySurface()
    {
        var type = typeof(SupportedExtractionTaxYears);
        Assert.True(type.IsAbstract && type.IsSealed); // static class

        var all = type.GetProperty("All", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(all);
        Assert.Equal(typeof(IReadOnlyList<int>), all!.PropertyType);
        Assert.Null(all.SetMethod);

        var isSupported = type.GetMethod("IsSupported", new[] { typeof(int) });
        Assert.NotNull(isSupported);
        Assert.True(isSupported!.IsStatic);
        Assert.Equal(typeof(bool), isSupported.ReturnType);
    }

    // --- PII plumbing: [JsonIgnore] on payload and every caller-controlled string ---

    [Fact]
    public void JsonIgnore_CoversThePayloadAndEveryCallerControlledString()
    {
        AssertJsonIgnored(typeof(ExtractionEnvelope), "DocumentId", "FirmId", "CaseId", "ParticipantId", "T4Payload");
        AssertJsonIgnored(typeof(ExtractionProvenance), "SourceReference", "ProviderLabel");
        AssertJsonIgnored(
            typeof(ValidatedExtraction),
            "DocumentId", "FirmId", "CaseId", "ParticipantId", "SourceReference", "ProviderLabel", "T4Payload");
    }

    [Fact]
    public void JsonIgnore_DoesNotCoverStructuralFacts()
    {
        AssertNotJsonIgnored(typeof(ExtractionEnvelope), "Kind", "TaxYear", "Provenance");
        AssertNotJsonIgnored(typeof(ExtractionProvenance), "Confidence");
        AssertNotJsonIgnored(typeof(ValidatedExtraction), "Kind", "TaxYear", "Confidence");
    }

    // --- No record-generated ToString on any public type ---

    [Fact]
    public void EveryPublicClass_IsNotARecordAndOwnsItsToString()
    {
        var publicClasses = ProductionAssembly
            .GetTypes()
            .Where(t => t.IsPublic && t.IsClass && !(t.IsAbstract && t.IsSealed))
            .ToArray();

        Assert.NotEmpty(publicClasses);
        foreach (var type in publicClasses)
        {
            AssertNotARecord(type);
            var toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);
            Assert.NotNull(toString);
            Assert.Equal(type, toString!.DeclaringType);
        }
    }

    // --- Wave-0 instruction files ---

    [Fact]
    public void LocalClaudeMd_ExistsAndContainsTheRequiredModuleRules()
    {
        var root = FindRepositoryRoot();
        var claude = File.ReadAllText(Path.Combine(root, "src", "DocumentExtraction", "CLAUDE.md"));

        // Approved boundary and dependency direction.
        Assert.Contains("DocumentExtraction → TaxLogicEngine", claude, StringComparison.Ordinal);
        Assert.Contains("TaxCases", claude, StringComparison.Ordinal);
        // Public-contract inventory.
        Assert.Contains("ExtractedDocumentKind", claude, StringComparison.Ordinal);
        Assert.Contains("ExtractionEnvelope", claude, StringComparison.Ordinal);
        Assert.Contains("ValidatedExtraction", claude, StringComparison.Ordinal);
        Assert.Contains("ExtractionFields", claude, StringComparison.Ordinal);
        Assert.Contains("IDocumentExtractionIngress", claude, StringComparison.Ordinal);
        Assert.Contains("ExtractionEnvelopeValidator", claude, StringComparison.Ordinal);
        Assert.Contains("SupportedExtractionTaxYears", claude, StringComparison.Ordinal);
        Assert.Contains("No implementation ships in spec 007", claude, StringComparison.Ordinal);
        // PII-omission rules and presence-only attribution scope.
        Assert.Contains("omission", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JsonIgnore", claude, StringComparison.Ordinal);
        Assert.Contains("presence only", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RecipientIdentifier", claude, StringComparison.Ordinal);
        // 2025 policy.
        Assert.Contains("exactly tax year 2025", claude, StringComparison.OrdinalIgnoreCase);
        // Forbidden dependencies and fail-closed workflow.
        Assert.Contains("ASP.NET Core", claude, StringComparison.Ordinal);
        Assert.Contains("EF Core", claude, StringComparison.Ordinal);
        Assert.Contains("fails closed", claude, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalAgentsMd_IsPointerOnly()
    {
        var root = FindRepositoryRoot();
        var agents = File.ReadAllText(Path.Combine(root, "src", "DocumentExtraction", "AGENTS.md"));

        Assert.Contains("pointer only", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("../../AGENTS.md", agents, StringComparison.Ordinal);
        Assert.Contains("../../CLAUDE.md", agents, StringComparison.Ordinal);
        Assert.Contains("[CLAUDE.md](CLAUDE.md)", agents, StringComparison.Ordinal);
    }

    // --- helpers ---

    internal static ValidatedExtraction CreateValidatedExtraction(T4RawInput? payload) =>
        CreateValidatedExtraction("doc-1", "firm-1", "case-1", "participant-1", "ref-1", "provider", payload);

    internal static ValidatedExtraction CreateValidatedExtraction(
        string documentId,
        string firmId,
        string caseId,
        string participantId,
        string sourceReference,
        string? providerLabel,
        T4RawInput? payload)
    {
        var constructor = Assert.Single(
            typeof(ValidatedExtraction).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        return (ValidatedExtraction)constructor.Invoke(new object?[]
        {
            documentId,
            ExtractedDocumentKind.T4Slip,
            firmId,
            caseId,
            participantId,
            2025,
            sourceReference,
            providerLabel,
            0.5m,
            payload,
        });
    }

    private static PropertyInfo[] InstanceProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

    private static void AssertInitOnlyProperty(Type type, string name, Type propertyType)
    {
        var property = type.GetProperty(name);
        Assert.NotNull(property);
        Assert.Equal(propertyType, property!.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.NotNull(property.SetMethod);
        Assert.Contains(
            typeof(IsExternalInit),
            property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
    }

    private static void AssertGetOnlyProperty(Type type, string name, Type propertyType)
    {
        var property = type.GetProperty(name);
        Assert.NotNull(property);
        Assert.Equal(propertyType, property!.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
    }

    private static void AssertJsonIgnored(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            var property = type.GetProperty(name);
            Assert.NotNull(property);
            Assert.NotNull(property!.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }

    private static void AssertNotJsonIgnored(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            var property = type.GetProperty(name);
            Assert.NotNull(property);
            Assert.Null(property!.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }

    private static void AssertNotARecord(Type type)
    {
        // Records emit a compiler-generated "<Clone>$" method and an EqualityContract
        // property; their generated ToString() prints every property value — the exact
        // PII leak the omission contract forbids.
        Assert.Null(type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(type.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
