using System.Reflection;
using System.Text.Json.Serialization;
using CaseProcessing.Models;
using CaseProcessing.Services;
using DocumentExtraction.Models;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseProcessing.Tests;

/// <summary>
/// Reflection verification that the compiled public contract matches
/// specs/008-t4-case-processor/architecture.md exactly, plus the Wave-0
/// instruction-file checks. Behavioural verification is owned by later waves;
/// nothing here calls <see cref="T4CaseProcessor.Process"/> or
/// <c>PayloadStructureGuard.Inspect</c>, and nothing asserts their Wave-0
/// unimplemented state — T02 and T03 must be able to implement them without
/// touching this file.
/// </summary>
public sealed class ContractTests
{
    private static readonly Assembly ProductionAssembly = typeof(ProcessedT4Extraction).Assembly;

    // --- The public surface as a whole ---

    [Fact]
    public void ProductionAssembly_ExposesExactlyTheContractedPublicTypes()
    {
        var expected = new[]
        {
            "CaseProcessing.Models.ProcessedT4Extraction",
            "CaseProcessing.Models.T4ProcessingFields",
            "CaseProcessing.Services.SupportedProcessingTaxYears",
            "CaseProcessing.Services.T4CaseProcessor",
        };

        var actual = ProductionAssembly
            .GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .Select(t => t.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), actual);
    }

    [Fact]
    public void EveryPublicClass_IsSealedNotARecordAndOwnsItsToString()
    {
        var publicClasses = ProductionAssembly
            .GetTypes()
            .Where(t => t.IsPublic && t.IsClass && !(t.IsAbstract && t.IsSealed)) // exclude static classes
            .ToArray();

        Assert.NotEmpty(publicClasses);
        foreach (var type in publicClasses)
        {
            Assert.True(type.IsSealed, $"{type.FullName} must be sealed.");
            AssertNotARecord(type);

            var toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);
            Assert.NotNull(toString);
            Assert.Equal(type, toString!.DeclaringType);
        }
    }

    [Fact]
    public void NoPublicType_IsARecord()
    {
        foreach (var type in ProductionAssembly.GetTypes().Where(t => t.IsPublic))
        {
            AssertNotARecord(type);
        }
    }

    // --- ProcessedT4Extraction ---

    [Fact]
    public void ProcessedT4Extraction_HasExactContractShape()
    {
        var type = typeof(ProcessedT4Extraction);

        Assert.True(type.IsSealed);
        AssertNotARecord(type);
        AssertGetOnlyProperty(type, "DocumentId", typeof(string));
        AssertGetOnlyProperty(type, "FirmId", typeof(string));
        AssertGetOnlyProperty(type, "CaseId", typeof(string));
        AssertGetOnlyProperty(type, "ParticipantId", typeof(string));
        AssertGetOnlyProperty(type, "TaxYear", typeof(int));
        AssertGetOnlyProperty(type, "SourceReference", typeof(string));
        AssertGetOnlyProperty(type, "Slip", typeof(T4Slip));
        AssertGetOnlyProperty(type, "Mapping", typeof(T4MappingResult));
        Assert.Equal(8, InstanceProperties(type).Length);
    }

    [Fact]
    public void ProcessedT4Extraction_HasNoPublicConstructor()
    {
        Assert.Empty(typeof(ProcessedT4Extraction).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void ProcessedT4Extraction_HasExactlyOneInternalConstructorWithTheContractSignature()
    {
        var constructor = Assert.Single(
            typeof(ProcessedT4Extraction).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        Assert.True(constructor.IsAssembly, "The constructor must be internal, not private or protected.");

        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Equal(
            new[]
            {
                typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(int), typeof(string), typeof(T4Slip), typeof(T4MappingResult),
            },
            parameterTypes);
    }

    [Fact]
    public void ProcessedT4Extraction_IsConstructibleOnlyThroughItsInternalConstructor()
    {
        var slip = BuildSlip();
        var mapping = new T4MappingResult(
            new[] { new TaxLineEntry("line-a", 1.00m, new[] { "14" }) },
            new[] { "99" });

        var result = CreateResult(
            "doc-1", "firm-1", "case-1", "participant-1", "store/ref-1", slip, mapping);

        Assert.Equal("doc-1", result.DocumentId);
        Assert.Equal("firm-1", result.FirmId);
        Assert.Equal("case-1", result.CaseId);
        Assert.Equal("participant-1", result.ParticipantId);
        Assert.Equal(2025, result.TaxYear);
        Assert.Equal("store/ref-1", result.SourceReference);
        Assert.Same(slip, result.Slip);
        Assert.Same(mapping, result.Mapping);
    }

    [Fact]
    public void ProcessedT4Extraction_DoesNotExposeTheRawPayload()
    {
        // PRD requirement 21: once parsed, the raw T4RawInput's role is over. Keeping
        // it would be a second, unmasked carrier of the Box 12 SIN.
        var forbidden = new[] { typeof(T4RawInput), typeof(T4RawBox), typeof(IReadOnlyList<T4RawBox>) };

        foreach (var property in InstanceProperties(typeof(ProcessedT4Extraction)))
        {
            Assert.DoesNotContain(property.PropertyType, forbidden);
        }
    }

    [Fact]
    public void ProcessedT4Extraction_ToStringEmitsStructuralFactsOnly()
    {
        var result = CreateResult(
            "doc-1", "firm-1", "case-1", "participant-1", "store/ref-1",
            BuildSlip(),
            new T4MappingResult(
                new[] { new TaxLineEntry("line-a", 12.34m, new[] { "14" }) },
                new[] { "99" }));

        var text = result.ToString();

        Assert.Contains("TaxYear: 2025", text, StringComparison.Ordinal);
        Assert.Contains("LineEntries: 1", text, StringComparison.Ordinal);
        Assert.Contains("UnmappedBoxes: 1", text, StringComparison.Ordinal);
        Assert.Contains("HasSin: True", text, StringComparison.Ordinal);

        // Never a caller value, an identity string, an amount, or a SIN in any form.
        Assert.DoesNotContain("doc-1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("firm-1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("case-1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("participant-1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("store/ref-1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Employer Ltd", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Employee Name", text, StringComparison.Ordinal);
        Assert.DoesNotContain("12.34", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line-a", text, StringComparison.Ordinal);
        Assert.DoesNotContain("99", text, StringComparison.Ordinal);
    }

    // --- PII plumbing: [JsonIgnore] on every caller string, plus Slip and Mapping ---

    [Fact]
    public void JsonIgnore_CoversEveryCallerStringAndBothEngineObjects()
    {
        AssertJsonIgnored(
            typeof(ProcessedT4Extraction),
            "DocumentId", "FirmId", "CaseId", "ParticipantId", "SourceReference", "Slip", "Mapping");
    }

    [Fact]
    public void JsonIgnore_DoesNotCoverTaxYear()
    {
        // A four-digit year is a structural fact, not caller-controlled data — the
        // one serializable property.
        AssertNotJsonIgnored(typeof(ProcessedT4Extraction), "TaxYear");
    }

    [Fact]
    public void EveryProcessedResultPropertyExceptTaxYear_IsJsonIgnored()
    {
        foreach (var property in InstanceProperties(typeof(ProcessedT4Extraction)))
        {
            var ignored = property.GetCustomAttribute<JsonIgnoreAttribute>() is not null;
            Assert.Equal(property.Name != nameof(ProcessedT4Extraction.TaxYear), ignored);
        }
    }

    // --- T4ProcessingFields ---

    [Fact]
    public void T4ProcessingFields_DeclaresExactlyTheClosedConstantSet()
    {
        var expected = new Dictionary<string, string>
        {
            ["Extraction"] = "Extraction",
            ["Kind"] = "Kind",
            ["Payload"] = "Payload",
            ["PayloadBoxes"] = "PayloadBoxes",
            ["PayloadBoxCode"] = "PayloadBoxCode",
            ["TaxYear"] = "TaxYear",
            ["SlipYear"] = "SlipYear",
        };

        var type = typeof(T4ProcessingFields);
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

    [Fact]
    public void T4ProcessingFields_ContainsNoCraLineNumberOrBoxCode()
    {
        // The closed set names fields, never tax data. A digit anywhere here would
        // mean a CRA line number or box code had leaked into C#.
        foreach (var value in AllProcessingFieldValues())
        {
            Assert.DoesNotContain(value, char.IsDigit);
        }
    }

    // --- SupportedProcessingTaxYears ---

    [Fact]
    public void SupportedProcessingTaxYears_DeclaresTheExactPolicySurface()
    {
        var type = typeof(SupportedProcessingTaxYears);
        Assert.True(type.IsAbstract && type.IsSealed); // static class

        var all = type.GetProperty("All", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(all);
        Assert.Equal(typeof(IReadOnlyList<int>), all!.PropertyType);
        Assert.Null(all.SetMethod);

        var isSupported = type.GetMethod("IsSupported", new[] { typeof(int) });
        Assert.NotNull(isSupported);
        Assert.True(isSupported!.IsStatic);
        Assert.Equal(typeof(bool), isSupported.ReturnType);

        // Exactly the two contracted members — the year policy lives in one place and
        // exposes nothing else (no clock read, no resource inspection).
        const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        Assert.Equal(new[] { "All" }, type.GetProperties(PublicStatic).Select(p => p.Name).ToArray());
        Assert.Equal(
            new[] { "IsSupported" },
            type.GetMethods(PublicStatic).Where(m => !m.IsSpecialName).Select(m => m.Name).ToArray());
        Assert.Empty(type.GetFields(PublicStatic));
    }

    // --- PayloadStructureGuard (internal seam guard) ---

    [Fact]
    public void PayloadStructureGuard_IsInternalAndNotPartOfThePublicSurface()
    {
        var type = ProductionAssembly.GetType("CaseProcessing.Services.PayloadStructureGuard");

        Assert.NotNull(type);
        Assert.False(type!.IsPublic, "The seam guard must stay off the public surface.");
        Assert.True(type.IsAbstract && type.IsSealed); // static class
    }

    [Fact]
    public void PayloadStructureGuard_DeclaresTheExactInspectSignature()
    {
        var method = typeof(PayloadStructureGuard).GetMethod(
            "Inspect",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            new[] { typeof(T4RawInput) },
            modifiers: null);

        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
        Assert.True(method.IsAssembly, "Inspect must be internal.");
        Assert.Equal(typeof(IReadOnlyList<ValidationError>), method.ReturnType);
    }

    // --- T4CaseProcessor ---

    [Fact]
    public void T4CaseProcessor_DeclaresExactlyOnePublicConstructorTakingTheMapProvider()
    {
        var constructor = Assert.Single(typeof(T4CaseProcessor).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(IT4LineMapProvider), parameter.ParameterType);
    }

    [Fact]
    public void T4CaseProcessor_DeclaresTheExactProcessSignature()
    {
        var type = typeof(T4CaseProcessor);
        Assert.True(type.IsSealed);

        var method = type.GetMethod("Process", new[] { typeof(ValidatedExtraction) });
        Assert.NotNull(method);
        Assert.False(method!.IsStatic);
        Assert.Equal(typeof(ParseResult<ProcessedT4Extraction>), method.ReturnType);

        var parameter = Assert.Single(method.GetParameters());
        var nullability = new NullabilityInfoContext().Create(parameter);
        Assert.Equal(NullabilityState.Nullable, nullability.WriteState);
    }

    [Fact]
    public void T4CaseProcessor_ExposesNoOtherPublicBehaviour()
    {
        var declared = typeof(T4CaseProcessor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Process", "ToString" }, declared);
        Assert.Empty(typeof(T4CaseProcessor).GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void T4CaseProcessor_RejectsANullMapProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new T4CaseProcessor(null!));
    }

    // --- Wave-0 instruction files ---

    [Fact]
    public void LocalClaudeMd_ExistsAndContainsTheRequiredModuleRules()
    {
        var claude = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CaseProcessing", "CLAUDE.md"));

        // Approved boundary and dependency direction.
        Assert.Contains("CaseProcessing → DocumentExtraction", claude, StringComparison.Ordinal);
        Assert.Contains("CaseProcessing → TaxLogicEngine", claude, StringComparison.Ordinal);
        Assert.Contains("TaxCases", claude, StringComparison.Ordinal);
        // Public-contract inventory.
        Assert.Contains("ProcessedT4Extraction", claude, StringComparison.Ordinal);
        Assert.Contains("T4ProcessingFields", claude, StringComparison.Ordinal);
        Assert.Contains("SupportedProcessingTaxYears", claude, StringComparison.Ordinal);
        Assert.Contains("PayloadStructureGuard", claude, StringComparison.Ordinal);
        Assert.Contains("T4CaseProcessor", claude, StringComparison.Ordinal);
        Assert.Contains("internal constructor", claude, StringComparison.OrdinalIgnoreCase);
        // The omission → masking seam.
        Assert.Contains("omission", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("masking", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JsonIgnore", claude, StringComparison.Ordinal);
        Assert.Contains("RecipientIdentifier", claude, StringComparison.Ordinal);
        // 2025 policy.
        Assert.Contains("exactly tax year 2025", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no retry", claude, StringComparison.OrdinalIgnoreCase);
        // Forbidden dependencies.
        Assert.Contains("ASP.NET Core", claude, StringComparison.Ordinal);
        Assert.Contains("EF Core", claude, StringComparison.Ordinal);
        Assert.Contains("no CRA line-number literal", claude, StringComparison.OrdinalIgnoreCase);
        // Fail-closed workflow.
        Assert.Contains("fails closed", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet test TaxAI.sln", claude, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalAgentsMd_IsPointerOnly()
    {
        var agents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CaseProcessing", "AGENTS.md"));

        Assert.Contains("pointer only", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("../../AGENTS.md", agents, StringComparison.Ordinal);
        Assert.Contains("../../CLAUDE.md", agents, StringComparison.Ordinal);
        Assert.Contains("[CLAUDE.md](CLAUDE.md)", agents, StringComparison.Ordinal);
    }

    // --- shared helpers (also used by the PII matrix) ---

    internal static IEnumerable<string> AllProcessingFieldValues() =>
        typeof(T4ProcessingFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

    internal static T4Slip BuildSlip(
        string employerName = "Employer Ltd",
        string? employerAddress = "1 Employer Way",
        string employeeName = "Employee Name",
        string? employeeAddress = "2 Employee Street",
        string? employerAccountNumber = "000000000RP0001",
        string sin = "000000000") =>
        new()
        {
            Year = 2025,
            EmployerName = employerName,
            EmployerAddress = employerAddress,
            EmployeeName = employeeName,
            EmployeeAddress = employeeAddress,
            EmployerAccountNumber = employerAccountNumber,
            DentalBenefitCode = "1",
            Sin = new RecipientIdentifier(sin),
        };

    internal static ProcessedT4Extraction CreateResult(
        string documentId,
        string firmId,
        string caseId,
        string participantId,
        string sourceReference,
        T4Slip slip,
        T4MappingResult mapping)
    {
        var constructor = Assert.Single(
            typeof(ProcessedT4Extraction).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        return (ProcessedT4Extraction)constructor.Invoke(new object?[]
        {
            documentId, firmId, caseId, participantId, 2025, sourceReference, slip, mapping,
        });
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

    private static PropertyInfo[] InstanceProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

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
}
