using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CaseProcessing.Models;
using CaseReview.Models;
using CaseReview.Services;
using TaxLogicEngine.Models;

namespace CaseReview.Tests;

/// <summary>
/// Pins the complete Wave-0 public contract from spec 009 architecture.md.
/// Later tasks may implement behavior but may not change this surface.
/// </summary>
public sealed class ContractTests
{
    private static readonly Assembly ProductionAssembly =
        typeof(AccountantReviewPackage).Assembly;

    [Fact]
    public void ProductionAssembly_ExposesExactlyTheNineContractedPublicTypes()
    {
        var expected = new[]
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
        };

        var actual = ProductionAssembly
            .GetTypes()
            .Where(type => type.IsPublic || type.IsNestedPublic)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SignOffDecision_HasExactNamesAndNumericValues()
    {
        var values = Enum
            .GetValues<SignOffDecision>()
            .Select(value => (value.ToString(), (int)value))
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("Approved", 1),
                ("ChangesRequested", 2),
                ("Rejected", 3),
            },
            values);
    }

    [Fact]
    public void EveryStatefulPublicClass_IsSealedNotARecordAndOwnsToString()
    {
        var statefulClasses = ProductionAssembly
            .GetTypes()
            .Where(type => type.IsPublic && type.IsClass && !(type.IsAbstract && type.IsSealed))
            .ToArray();

        Assert.Equal(5, statefulClasses.Length);

        foreach (var type in statefulClasses)
        {
            Assert.True(type.IsSealed, $"{type.FullName} must be sealed.");
            AssertNotARecord(type);

            var toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);
            Assert.NotNull(toString);
            Assert.Equal(type, toString!.DeclaringType);
        }
    }

    [Fact]
    public void StatefulPublicClasses_ExposeNoUncontractedDeclaredMethods()
    {
        AssertDeclaredMethodNames<ReviewedBoxAmount>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertDeclaredMethodNames<ReviewedSlip>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertDeclaredMethodNames<ReviewDecisionDescriptor>("ToString");
        AssertDeclaredMethodNames<ReviewSignOff>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertDeclaredMethodNames<AccountantReviewPackage>(
            "Equals",
            "Equals",
            "GetHashCode",
            "RecordDecision",
            "ToString");
    }

    [Fact]
    public void OutputTypes_HaveOnlyTheirExactInternalConstructors()
    {
        AssertInternalConstructor(
            typeof(ReviewedBoxAmount),
            typeof(string),
            typeof(decimal));

        AssertInternalConstructor(
            typeof(ReviewedSlip),
            typeof(string),
            typeof(int),
            typeof(string),
            typeof(T4Slip),
            typeof(IReadOnlyList<TaxLineEntry>),
            typeof(IReadOnlyList<ReviewedBoxAmount>),
            typeof(IReadOnlyList<string>));

        AssertInternalConstructor(
            typeof(ReviewSignOff),
            typeof(SignOffDecision),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(bool?));

        AssertInternalConstructor(
            typeof(AccountantReviewPackage),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(int),
            typeof(IReadOnlyList<ReviewedSlip>),
            typeof(ReviewSignOff));
    }

    [Fact]
    public void ReviewDecisionDescriptor_HasTheExactPublicConstructor()
    {
        var constructor = Assert.Single(typeof(ReviewDecisionDescriptor).GetConstructors());
        Assert.Equal(
            new[]
            {
                typeof(SignOffDecision),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(bool?),
            },
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        var nullability = constructor
            .GetParameters()
            .Select(parameter => new NullabilityInfoContext().Create(parameter).ReadState)
            .ToArray();

        Assert.Equal(
            new[]
            {
                NullabilityState.NotNull,
                NullabilityState.Nullable,
                NullabilityState.Nullable,
                NullabilityState.Nullable,
                NullabilityState.Nullable,
            },
            nullability);
    }

    [Fact]
    public void ReviewedBoxAmount_HasTheExactPropertyAndMethodContract()
    {
        var type = typeof(ReviewedBoxAmount);

        AssertGetOnlyProperty(type, nameof(ReviewedBoxAmount.BoxCode), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(type, nameof(ReviewedBoxAmount.Amount), typeof(decimal), jsonIgnored: true);
        Assert.Equal(2, PublicInstanceProperties(type).Length);
        AssertEquatableContract<ReviewedBoxAmount>();
    }

    [Fact]
    public void ReviewedSlip_HasTheExactPropertyAndMethodContract()
    {
        var type = typeof(ReviewedSlip);

        AssertGetOnlyProperty(type, nameof(ReviewedSlip.DocumentId), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(type, nameof(ReviewedSlip.TaxYear), typeof(int), jsonIgnored: false);
        AssertGetOnlyProperty(type, nameof(ReviewedSlip.SourceReference), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(type, nameof(ReviewedSlip.Slip), typeof(T4Slip), jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewedSlip.LineEntries),
            typeof(IReadOnlyList<TaxLineEntry>),
            jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewedSlip.BoxAmounts),
            typeof(IReadOnlyList<ReviewedBoxAmount>),
            jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewedSlip.UnmappedBoxCodes),
            typeof(IReadOnlyList<string>),
            jsonIgnored: true);

        Assert.Equal(7, PublicInstanceProperties(type).Length);
        AssertEquatableContract<ReviewedSlip>();
    }

    [Fact]
    public void ReviewDecisionDescriptor_HasTheExactPropertyContract()
    {
        var type = typeof(ReviewDecisionDescriptor);

        AssertGetOnlyProperty(
            type,
            nameof(ReviewDecisionDescriptor.Decision),
            typeof(SignOffDecision),
            jsonIgnored: false);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewDecisionDescriptor.ReviewerReference),
            typeof(string),
            jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewDecisionDescriptor.DecidedAtReference),
            typeof(string),
            jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewDecisionDescriptor.Note),
            typeof(string),
            jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewDecisionDescriptor.UnmappedBoxesAcknowledged),
            typeof(bool?),
            jsonIgnored: false);

        Assert.Equal(5, PublicInstanceProperties(type).Length);
        Assert.Null(type.GetInterface(typeof(IEquatable<>).Name));
    }

    [Fact]
    public void ReviewSignOff_HasTheExactPropertyAndMethodContract()
    {
        var type = typeof(ReviewSignOff);

        AssertGetOnlyProperty(type, nameof(ReviewSignOff.Decision), typeof(SignOffDecision), jsonIgnored: false);
        AssertGetOnlyProperty(type, nameof(ReviewSignOff.ReviewerReference), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(type, nameof(ReviewSignOff.DecidedAtReference), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(type, nameof(ReviewSignOff.Note), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(ReviewSignOff.UnmappedBoxesAcknowledged),
            typeof(bool?),
            jsonIgnored: false);

        Assert.Equal(5, PublicInstanceProperties(type).Length);
        AssertEquatableContract<ReviewSignOff>();
    }

    [Fact]
    public void AccountantReviewPackage_HasTheExactPropertyAndMethodContract()
    {
        var type = typeof(AccountantReviewPackage);

        AssertGetOnlyProperty(type, nameof(AccountantReviewPackage.FirmId), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(type, nameof(AccountantReviewPackage.CaseId), typeof(string), jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(AccountantReviewPackage.ParticipantId),
            typeof(string),
            jsonIgnored: true);
        AssertGetOnlyProperty(type, nameof(AccountantReviewPackage.TaxYear), typeof(int), jsonIgnored: false);
        AssertGetOnlyProperty(
            type,
            nameof(AccountantReviewPackage.ReviewedSlips),
            typeof(IReadOnlyList<ReviewedSlip>),
            jsonIgnored: true);
        AssertGetOnlyProperty(
            type,
            nameof(AccountantReviewPackage.SignOff),
            typeof(ReviewSignOff),
            jsonIgnored: true);

        Assert.Equal(6, PublicInstanceProperties(type).Length);
        AssertEquatableContract<AccountantReviewPackage>();

        var method = type.GetMethod(
            nameof(AccountantReviewPackage.RecordDecision),
            new[] { typeof(ReviewDecisionDescriptor) });

        Assert.NotNull(method);
        Assert.False(method!.IsStatic);
        Assert.Equal(typeof(ParseResult<AccountantReviewPackage>), method.ReturnType);
        Assert.Equal(
            NullabilityState.Nullable,
            new NullabilityInfoContext().Create(Assert.Single(method.GetParameters())).ReadState);
    }

    [Fact]
    public void ReviewFields_IsExactlyTheClosedThirteenConstantSet()
    {
        var actual = typeof(ReviewFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            (nameof(ReviewFields.CaseId), ReviewFields.CaseId),
            (nameof(ReviewFields.DecidedAtReference), ReviewFields.DecidedAtReference),
            (nameof(ReviewFields.Decision), ReviewFields.Decision),
            (nameof(ReviewFields.DocumentId), ReviewFields.DocumentId),
            (nameof(ReviewFields.Extraction), ReviewFields.Extraction),
            (nameof(ReviewFields.Extractions), ReviewFields.Extractions),
            (nameof(ReviewFields.FirmId), ReviewFields.FirmId),
            (nameof(ReviewFields.Note), ReviewFields.Note),
            (nameof(ReviewFields.ParticipantId), ReviewFields.ParticipantId),
            (nameof(ReviewFields.ReviewerReference), ReviewFields.ReviewerReference),
            (nameof(ReviewFields.SignOff), ReviewFields.SignOff),
            (nameof(ReviewFields.TaxYear), ReviewFields.TaxYear),
            (nameof(ReviewFields.UnmappedBoxAcknowledgement), ReviewFields.UnmappedBoxAcknowledgement),
        };

        Assert.Equal(expected, actual);
        Assert.All(actual, item => Assert.Equal(item.Name, item.Value));
        Assert.Empty(typeof(ReviewFields).GetProperties(BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void SupportedReviewTaxYears_IsExactlyThe2025OnlyContract()
    {
        Assert.Equal(new[] { 2025 }, SupportedReviewTaxYears.All);
        Assert.False(SupportedReviewTaxYears.IsSupported(2024));
        Assert.True(SupportedReviewTaxYears.IsSupported(2025));
        Assert.False(SupportedReviewTaxYears.IsSupported(2026));

        var type = typeof(SupportedReviewTaxYears);
        Assert.Equal(
            new[] { "All" },
            type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Select(property => property.Name)
                .ToArray());
        Assert.Equal(
            new[] { "IsSupported" },
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .ToArray());
    }

    [Fact]
    public void ReviewPackageAssembler_HasTheExactNullableStaticSignature()
    {
        var type = typeof(ReviewPackageAssembler);
        Assert.True(type.IsAbstract && type.IsSealed);

        var method = Assert.Single(
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        Assert.Equal(nameof(ReviewPackageAssembler.Assemble), method.Name);
        Assert.Equal(typeof(ParseResult<AccountantReviewPackage>), method.ReturnType);

        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(IReadOnlyList<ProcessedT4Extraction>), parameter.ParameterType);

        var nullability = new NullabilityInfoContext().Create(parameter);
        Assert.Equal(NullabilityState.Nullable, nullability.ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            Assert.Single(nullability.GenericTypeArguments).ReadState);
    }

    [Fact]
    public void SolutionAndProjects_DeclareOnlyTheApprovedCaseReviewEdges()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "TaxAI.sln"));

        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                "\"CaseReview\", \"src\\CaseReview\\CaseReview.csproj\""));
        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                "\"CaseReview.Tests\", \"tests\\CaseReview.Tests\\CaseReview.Tests.csproj\""));

        var production = XDocument.Load(
            Path.Combine(root, "src", "CaseReview", "CaseReview.csproj"));
        var productionReferences = Elements(production, "ProjectReference")
            .Select(element => NormalizePath(element.Attribute("Include")!.Value))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "../CaseProcessing/CaseProcessing.csproj",
                "../TaxLogicEngine/TaxLogicEngine.csproj",
            },
            productionReferences);

        foreach (var forbiddenItem in new[]
                 {
                     "PackageReference",
                     "FrameworkReference",
                     "EmbeddedResource",
                     "Content",
                 })
        {
            Assert.Empty(Elements(production, forbiddenItem));
        }

        var tests = XDocument.Load(
            Path.Combine(root, "tests", "CaseReview.Tests", "CaseReview.Tests.csproj"));
        var testReferences = Elements(tests, "ProjectReference")
            .Select(element => NormalizePath(element.Attribute("Include")!.Value))
            .ToArray();

        Assert.Equal(new[] { "../../src/CaseReview/CaseReview.csproj" }, testReferences);
    }

    [Fact]
    public void LocalInstructions_ContainTheApprovedBoundaryAndPointerOnlyBridge()
    {
        var root = FindRepositoryRoot();
        var claude = File.ReadAllText(Path.Combine(root, "src", "CaseReview", "CLAUDE.md"));
        var agents = File.ReadAllText(Path.Combine(root, "src", "CaseReview", "AGENTS.md"));

        Assert.Contains("CaseReview → CaseProcessing", claude, StringComparison.Ordinal);
        Assert.Contains("CaseReview → TaxLogicEngine", claude, StringComparison.Ordinal);
        Assert.Contains("TaxCases", claude, StringComparison.Ordinal);
        Assert.Contains("ProcessedT4Extraction", claude, StringComparison.Ordinal);
        Assert.Contains("AccountantReviewPackage", claude, StringComparison.Ordinal);
        Assert.Contains("UnmappedBoxesAcknowledged", claude, StringComparison.Ordinal);
        Assert.Contains("T4SlipBoxAmounts.Enumerate", claude, StringComparison.Ordinal);
        Assert.Contains("exactly tax year 2025", claude, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JsonIgnore", claude, StringComparison.Ordinal);
        Assert.Contains("RecipientIdentifier", claude, StringComparison.Ordinal);
        Assert.Contains("dotnet build TaxAI.sln --no-incremental", claude, StringComparison.Ordinal);
        Assert.Contains("dotnet test TaxAI.sln", claude, StringComparison.Ordinal);

        Assert.Contains("pointer only", agents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("../../AGENTS.md", agents, StringComparison.Ordinal);
        Assert.Contains("../../CLAUDE.md", agents, StringComparison.Ordinal);
        Assert.Contains("[CLAUDE.md](CLAUDE.md)", agents, StringComparison.Ordinal);
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static PropertyInfo[] PublicInstanceProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static void AssertInternalConstructor(Type type, params Type[] parameterTypes)
    {
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

        var constructor = Assert.Single(
            type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance));

        Assert.True(constructor.IsAssembly, $"{type.Name} constructor must be internal.");
        Assert.Equal(
            parameterTypes,
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    private static void AssertGetOnlyProperty(
        Type type,
        string name,
        Type propertyType,
        bool jsonIgnored)
    {
        var property = type.GetProperty(name);

        Assert.NotNull(property);
        Assert.Equal(propertyType, property!.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
        Assert.Equal(
            jsonIgnored,
            property.GetCustomAttribute<JsonIgnoreAttribute>() is not null);
    }

    private static void AssertEquatableContract<T>()
    {
        var type = typeof(T);
        Assert.Contains(typeof(IEquatable<T>), type.GetInterfaces());
        Assert.NotNull(type.GetMethod(nameof(IEquatable<T>.Equals), new[] { type }));
        Assert.Equal(type, type.GetMethod(nameof(object.Equals), new[] { typeof(object) })!.DeclaringType);
        Assert.Equal(type, type.GetMethod(nameof(GetHashCode), Type.EmptyTypes)!.DeclaringType);
    }

    private static void AssertNotARecord(Type type)
    {
        Assert.Null(
            type.GetMethod(
                "<Clone>$",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(
            type.GetProperty(
                "EqualityContract",
                BindingFlags.Instance | BindingFlags.NonPublic));
    }

    private static void AssertDeclaredMethodNames<T>(params string[] expected)
    {
        var actual = typeof(T)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal).ToArray(), actual);
    }

    private static IEnumerable<XElement> Elements(XDocument document, string localName) =>
        document.Descendants().Where(element => element.Name.LocalName == localName);

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;

        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }
}
