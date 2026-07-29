using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CaseProcessing.Models;
using CaseReview.Models;
using CaseReview.Services;
using TaxLogicEngine.Models;

namespace CaseReview.Tests.Boundaries;

/// <summary>
/// T07 boundary evidence for AC-34, AC-39 through AC-49, and AC-54.
/// Project, source, reflection, and IL checks keep CaseReview a deterministic
/// review boundary rather than a second tax or mapping authority.
/// </summary>
public sealed class CaseReviewBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string ModuleRoot =
        Path.Combine(RepositoryRoot, "src", "CaseReview");

    private static readonly Assembly ProductionAssembly =
        typeof(AccountantReviewPackage).Assembly;

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

    private static readonly string[] ExistingProjectFiles =
    {
        "src/TaxLogicEngine/TaxLogicEngine.csproj",
        "src/TaxCases/TaxCases.csproj",
        "src/DocumentExtraction/DocumentExtraction.csproj",
        "src/CaseProcessing/CaseProcessing.csproj",
        "tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj",
        "tests/TaxCases.Tests/TaxCases.Tests.csproj",
        "tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj",
        "tests/CaseProcessing.Tests/CaseProcessing.Tests.csproj",
    };

    // ---------------------------------------------------------- project boundary

    [Fact]
    public void SolutionDeclaresBothSpecProjectsExactlyOnceAtApprovedPaths() // AC-49
    {
        var solutionPath = Path.Combine(RepositoryRoot, "TaxAI.sln");
        var projectLines = File.ReadAllLines(solutionPath)
            .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(projectLines);

        foreach (var declaration in new[]
                 {
                     "\"CaseReview\", \"src\\CaseReview\\CaseReview.csproj\"",
                     "\"CaseReview.Tests\", \"tests\\CaseReview.Tests\\CaseReview.Tests.csproj\"",
                 })
        {
            Assert.Equal(
                1,
                projectLines.Count(line =>
                    line.Contains(declaration, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void ProductionProjectReferencesExactlyCaseProcessingAndTaxLogicEngine() // AC-46
    {
        Assert.Equal(
            new[]
            {
                "../CaseProcessing/CaseProcessing.csproj",
                "../TaxLogicEngine/TaxLogicEngine.csproj",
            },
            ProjectReferences(Path.Combine(ModuleRoot, "CaseReview.csproj")));
    }

    [Fact]
    public void ProductionProjectHasNoPackageFrameworkContentOrResourceItem() // AC-46
    {
        var project = XDocument.Load(Path.Combine(ModuleRoot, "CaseReview.csproj"));

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")!.Value);

        foreach (var forbidden in new[]
                 {
                     "PackageReference",
                     "FrameworkReference",
                     "EmbeddedResource",
                     "Content",
                 })
        {
            Assert.DoesNotContain(
                project.Descendants(),
                element => element.Name.LocalName == forbidden);
        }

        Assert.Empty(ProductionAssembly.GetManifestResourceNames());
    }

    [Fact]
    public void CompiledAssemblyReferencesExactlyTheTwoApprovedModules() // AC-46, AC-47
    {
        var nonFrameworkReferences = ProductionAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("System", StringComparison.Ordinal) &&
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "CaseProcessing", "TaxLogicEngine" }, nonFrameworkReferences);
        Assert.DoesNotContain("DocumentExtraction", nonFrameworkReferences);
        Assert.DoesNotContain("TaxCases", nonFrameworkReferences);
    }

    [Fact]
    public void NoExistingProductionOrTestProjectReferencesCaseReview() // AC-47
    {
        Assert.Equal(8, ExistingProjectFiles.Length);

        foreach (var relativePath in ExistingProjectFiles)
        {
            var projectPath = Path.Combine(
                RepositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(projectPath), $"{relativePath} must exist.");

            var reverseReferences = ProjectReferences(projectPath)
                .Where(reference =>
                    reference.Contains("CaseReview", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.True(
                reverseReferences.Length == 0,
                $"{relativePath} must not reference CaseReview: "
                + string.Join(", ", reverseReferences));

            var projectRoot = Path.GetDirectoryName(projectPath)!;
            var reverseSourceReferences = HandwrittenSourceFiles(projectRoot)
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
                $"{relativePath} source must not reference CaseReview: "
                + string.Join(", ", reverseSourceReferences));
        }
    }

    [Fact]
    public void LoadedUpstreamAssembliesDoNotReferenceCaseReview() // AC-47
    {
        var upstreamAssemblies = new[]
        {
            typeof(T4Slip).Assembly,
            Assembly.Load(new AssemblyName("DocumentExtraction")),
            typeof(ProcessedT4Extraction).Assembly,
        };

        Assert.Equal(
            new[] { "CaseProcessing", "DocumentExtraction", "TaxLogicEngine" },
            upstreamAssemblies
                .Select(assembly => assembly.GetName().Name!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        foreach (var assembly in upstreamAssemblies)
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name!.Equals(
                    "CaseReview",
                    StringComparison.Ordinal));
        }
    }

    // ----------------------------------------------------------- public contract

    [Fact]
    public void PublicSurfaceIsExactlyTheNineContractedTypes()
    {
        var actual = ProductionAssembly
            .GetTypes()
            .Where(type => type.IsPublic || type.IsNestedPublic)
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
            actual);
    }

    [Fact]
    public void PublicContractMatchesTheArchitectureManifest()
    {
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

        AssertInternalConstructor(
            typeof(ReviewedBoxAmount),
            new[] { typeof(string), typeof(decimal) },
            new[] { NullabilityState.NotNull, NullabilityState.NotNull });
        AssertInternalConstructor(
            typeof(ReviewedSlip),
            new[]
            {
                typeof(string),
                typeof(int),
                typeof(string),
                typeof(T4Slip),
                typeof(IReadOnlyList<TaxLineEntry>),
                typeof(IReadOnlyList<ReviewedBoxAmount>),
                typeof(IReadOnlyList<string>),
            },
            Enumerable.Repeat(NullabilityState.NotNull, 7).ToArray());
        AssertInternalConstructor(
            typeof(ReviewSignOff),
            new[]
            {
                typeof(SignOffDecision),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(bool?),
            },
            new[]
            {
                NullabilityState.NotNull,
                NullabilityState.NotNull,
                NullabilityState.NotNull,
                NullabilityState.Nullable,
                NullabilityState.Nullable,
            });
        AssertInternalConstructor(
            typeof(AccountantReviewPackage),
            new[]
            {
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(int),
                typeof(IReadOnlyList<ReviewedSlip>),
                typeof(ReviewSignOff),
            },
            new[]
            {
                NullabilityState.NotNull,
                NullabilityState.NotNull,
                NullabilityState.NotNull,
                NullabilityState.NotNull,
                NullabilityState.NotNull,
                NullabilityState.Nullable,
            });

        AssertDescriptorConstructor();
        AssertExactProperties();
        AssertExactMethods();
        AssertExactStaticContracts();
    }

    [Fact]
    public void ContractHasOnePackageLevelAcknowledgementAndNoPerCodeDisputeSurface() // AC-54
    {
        var publicTypes = ProductionAssembly
            .GetTypes()
            .Where(type => type.IsPublic)
            .ToArray();

        var acknowledgementProperties = publicTypes
            .SelectMany(type => type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            .Where(property =>
                property.Name.Contains("Acknowledg", StringComparison.OrdinalIgnoreCase))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
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

        Assert.DoesNotContain(
            typeof(ReviewedSlip).GetProperties(),
            property =>
                property.Name.Contains(
                    "Acknowledg",
                    StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(ReviewedBoxAmount).GetProperties(),
            property =>
                property.Name.Contains(
                    "Acknowledg",
                    StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------- year boundary

    [Fact]
    public void SupportedYearPolicyIsExactly2025AndLivesInOneSourceFile()
    {
        Assert.Equal(new[] { 2025 }, SupportedReviewTaxYears.All);
        Assert.False(SupportedReviewTaxYears.IsSupported(2024));
        Assert.True(SupportedReviewTaxYears.IsSupported(2025));
        Assert.False(SupportedReviewTaxYears.IsSupported(2026));

        foreach (var file in ProductionSourceFiles())
        {
            if (Path.GetFileName(file) == "SupportedReviewTaxYears.cs")
            {
                continue;
            }

            var yearLiterals = ExecutableLines(file)
                .Where(line => Regex.IsMatch(
                    line,
                    @"\b(?:19|20)\d{2}\b",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)))
                .ToArray();

            Assert.True(
                yearLiterals.Length == 0,
                $"{Path.GetFileName(file)} names a tax year outside the policy: "
                + string.Join(" | ", yearLiterals));
        }
    }

    [Fact]
    public void AssemblerSourceAndIlContainUnconditionalPerElementYearGuards() // AC-43
    {
        var assemblerPath = Path.Combine(
            ModuleRoot,
            "Services",
            "ReviewPackageAssembler.cs");
        var executable = string.Join("\n", ExecutableLines(assemblerPath));

        var loop = Regex.Match(
            executable,
            @"foreach\s*\(\s*var\s+extraction\s+in\s+materialized\s*\)\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.True(loop.Success, "The assembler must inspect every materialized element.");

        var body = loop.Groups["body"].Value;
        Assert.Matches(
            new Regex(
                @"taxYearMismatch\s*\|=\s*extraction\s*\.\s*TaxYear\s*!=\s*first\s*\.\s*TaxYear\s*;",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            body);
        Assert.Matches(
            new Regex(
                @"unsupportedTaxYear\s*\|=\s*!\s*SupportedReviewTaxYears\s*\.\s*IsSupported\s*\(\s*extraction\s*\.\s*TaxYear\s*\)\s*;",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            body);
        Assert.Single(
            Regex.Matches(
                executable,
                @"SupportedReviewTaxYears\s*\.\s*IsSupported\s*\(",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1))
                .Cast<Match>());
        Assert.True(
            loop.Index < executable.IndexOf(
                "if (errors.Count > 0)",
                StringComparison.Ordinal));
        Assert.True(
            loop.Index < executable.IndexOf(
                ".Select(BuildReviewedSlip)",
                StringComparison.Ordinal));

        var assemble = typeof(ReviewPackageAssembler).GetMethod(
            nameof(ReviewPackageAssembler.Assemble),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(assemble);

        var referencedMethods = IlInstructions(assemble!)
            .Where(instruction => instruction.Member is MethodBase)
            .Select(instruction => (MethodBase)instruction.Member!)
            .ToArray();

        Assert.True(
            referencedMethods.Count(method =>
                method.DeclaringType == typeof(ProcessedT4Extraction) &&
                method.Name == $"get_{nameof(ProcessedT4Extraction.TaxYear)}") >= 3,
            "Compiled assembly must read each element year for comparison and policy.");
        Assert.Equal(
            1,
            referencedMethods.Count(method =>
                method.DeclaringType == typeof(SupportedReviewTaxYears) &&
                method.Name == nameof(SupportedReviewTaxYears.IsSupported)));
    }

    // ------------------------------------------- fixed PII-safe error construction

    [Fact]
    public void EveryValidationFieldIsClosedAndEveryMessageIsAFixedConstant() // AC-40
    {
        var errorSources = ProductionSourceFiles()
            .Select(path => new
            {
                Name = Path.GetFileName(path),
                Text = string.Join("\n", ExecutableLines(path)),
            })
            .Where(source => source.Text.Contains(
                "new ValidationError",
                StringComparison.Ordinal))
            .OrderBy(source => source.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "AccountantReviewPackage.Decisions.cs",
                "ReviewPackageAssembler.cs",
            },
            errorSources.Select(source => source.Name).ToArray());

        var text = string.Join("\n", errorSources.Select(source => source.Text));
        Assert.DoesNotContain("$\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FormattableString", text, StringComparison.Ordinal);

        var messageConstants = errorSources
            .SelectMany(source => Regex.Matches(
                source.Text,
                @"private\s+const\s+string\s+(?<name>[A-Za-z_]\w*)\s*=\s*""(?:[^""\\]|\\.)*""\s*;",
                RegexOptions.Singleline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .Select(match => match.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(17, messageConstants.Length);
        Assert.Equal(messageConstants.Length, messageConstants.Distinct().Count());

        var directErrors = Regex.Matches(
            text,
            @"new\s+ValidationError\s*\(\s*ReviewFields\s*\.\s*(?<field>[A-Za-z_]\w*)\s*,\s*(?<message>[A-Za-z_]\w*)\s*\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var fixedFailureCalls = Regex.Matches(
            text,
            @"(?<!\.)\bFailure\s*\(\s*ReviewFields\s*\.\s*(?<field>[A-Za-z_]\w*)\s*,\s*(?<message>[A-Za-z_]\w*)\s*\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var privateHelpers = Regex.Matches(
            text,
            @"private\s+static\s+ParseResult<AccountantReviewPackage>\s+Failure\s*\(\s*string\s+field\s*,\s*string\s+message\s*\)\s*=>\s*ParseResult<AccountantReviewPackage>\s*\.\s*Failure\s*\(\s*new\s+ValidationError\s*\(\s*field\s*,\s*message\s*\)\s*\)\s*;",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.Equal(2, privateHelpers.Count);
        Assert.Equal(
            directErrors.Count + privateHelpers.Count,
            Regex.Matches(
                text,
                @"new\s+ValidationError\s*\(",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)).Count);
        Assert.Equal(
            fixedFailureCalls.Count + privateHelpers.Count,
            Regex.Matches(
                text,
                @"(?<!\.)\bFailure\s*\(",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)).Count);

        var usedFields = directErrors
            .Concat(fixedFailureCalls)
            .Select(match => match.Groups["field"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var closedFields = typeof(ReviewFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(closedFields, usedFields);

        var usedMessages = directErrors
            .Concat(fixedFailureCalls)
            .Select(match => match.Groups["message"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(messageConstants, usedMessages);
    }

    // -------------------------------------- no tax truth / forbidden dependencies

    public static TheoryData<string, string> ForbiddenProductionPatterns() => new()
    {
        { "clock", @"DateTime\s*\.\s*(?:Now|UtcNow)|DateTimeOffset\s*\.\s*(?:Now|UtcNow)|\bStopwatch\b|\bTimeProvider\b" },
        { "filesystem", @"\bSystem\.IO\b|\b(?:File|Directory|Path|FileStream|StreamReader|StreamWriter)\s*\." },
        { "network", @"\bSystem\.Net\b|\bHttpClient\b|\bWebRequest\b|\bSocket\b|\bWebSocket\b" },
        { "database / ORM / SQL", @"\bDbContext\b|\bDbSet\s*<|\bSqlConnection\b|\bSystem\.Data\b|EntityFrameworkCore|\bSELECT\b|\bINSERT\b|\bUPDATE\b|\bDELETE\b" },
        { "environment / current user", @"\bEnvironment\s*\.|\bClaimsPrincipal\b|\bCurrentUser\b|\bThread\s*\.\s*CurrentPrincipal\b" },
        { "OCR / LLM / cloud / email", @"\bOcr\b|\bTesseract\b|\bOpenAi\b|\bAnthropic\b|\bLlm\b|\bSmtp\b|\bMailMessage\b|\bAmazon\b|\bAzure\b|\bS3\b" },
        { "ASP.NET Core", @"Microsoft\.AspNetCore|\bControllerBase\b|\bWebApplication\b|\bIActionResult\b" },
        { "generated identifier", @"\bGuid\s*\.\s*NewGuid\b|\bnew\s+Random\b|\bRandomNumberGenerator\b" },
        { "TaxCases coupling", @"\bTaxCases\b" },
        { "direct DocumentExtraction coupling", @"\bDocumentExtraction\b" },
        { "mapping authority", @"\bT4LineMappingConfig\b|\bT4BoxLineMapping\b|\bT4LineMap\b|\bIT4LineMapProvider\b|\bT4BoxCodes\b|\bT4OtherInformationCodes\b|\bDictionary\s*<" },
        { "mapping resource", @"TaxLogicEngine\.Mappings|GetManifestResourceStream|T4LineMap\.\d{4}\.json|\.json\b" },
        { "tax-producing parser or mapper", @"\bT4SlipParser\b|\bT4LineMapper\b|\bEmbeddedT4LineMapProvider\b" },
        { "rounding or floating point", @"\bMoneyRounding\b|\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b" },
        { "masking helper", @"\bSafeDiagnosticText\b|\bMask(?:ed|ing)?\s*\(|\bRedact|\bScrub|\bSanitiz" },
        { "RecipientIdentifier construction", @"new\s+RecipientIdentifier\b" },
        { "T5 consumption", @"\bT5\w*\b" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSourceContainsNoForbiddenDependencyOrBehavior(
        string boundary,
        string pattern) // AC-34, AC-39, AC-44, AC-48
    {
        var regex = new Regex(
            pattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
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
            $"Forbidden {boundary} pattern /{pattern}/ found in: "
            + string.Join(", ", violations));
    }

    [Fact]
    public void SourceContainsNoCraLineOrBoxCodeLiteralAndNoMoneyArithmetic() // AC-34, AC-44
    {
        var executable = string.Join(
            "\n",
            ProductionSourceFiles().SelectMany(ExecutableLines));

        Assert.DoesNotMatch(
            new Regex(
                @"\b\d{5}\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
        Assert.DoesNotMatch(
            new Regex(
                @"""(?:\d{2})""",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
        Assert.DoesNotMatch(
            new Regex(
                @"\b\d{2}\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);

        Assert.DoesNotMatch(
            new Regex(
                @"(?:\bAmount\b|\bamount\b)[^;\r\n]*(?<![=!<>])(?:\+|-|\*|/|%)(?![=>])|(?<![=!<>])(?:\+|-|\*|/|%)(?![=>])[^;\r\n]*(?:\bAmount\b|\bamount\b)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
        Assert.DoesNotMatch(
            new Regex(
                @"(?:BoxAmounts|LineEntries)\s*\.\s*(?:Sum|Average|Aggregate|Min|Max)\s*\(",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            executable);
    }

    [Fact]
    public void CompiledAssemblyContainsNoForbiddenTaxOrInfrastructureReference() // AC-34, AC-44, AC-48
    {
        var instructions = ProductionMethods()
            .SelectMany(IlInstructions)
            .ToArray();
        var members = instructions
            .Where(instruction => instruction.Member is not null)
            .Select(instruction => instruction.Member!)
            .ToArray();
        var memberNames = members
            .Select(MemberDisplayName)
            .ToArray();

        foreach (var forbidden in new[]
                 {
                     "MoneyRounding",
                     "Math.Round",
                     "MidpointRounding",
                     "T4SlipParser",
                     "T4LineMapper",
                     "IT4LineMapProvider",
                     "EmbeddedT4LineMapProvider",
                     "T4LineMappingConfig",
                     "SafeDiagnosticText",
                     "System.IO.",
                     "System.Net.",
                     "System.Data.",
                     "EntityFrameworkCore",
                     "DateTime.get_Now",
                     "DateTime.get_UtcNow",
                     "DateTimeOffset.get_Now",
                     "DateTimeOffset.get_UtcNow",
                     "Environment.",
                     "Guid.NewGuid",
                 })
        {
            Assert.DoesNotContain(
                memberNames,
                name => name.Contains(forbidden, StringComparison.Ordinal));
        }

        var engineServiceCalls = members
            .OfType<MethodBase>()
            .Where(method => method.DeclaringType?.Namespace ==
                             "TaxLogicEngine.Services")
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "T4SlipBoxAmounts.Enumerate" }, engineServiceCalls);

        Assert.DoesNotContain(
            members.OfType<ConstructorInfo>(),
            constructor => constructor.DeclaringType?.FullName ==
                           "TaxLogicEngine.Models.RecipientIdentifier");

        var forbiddenDecimalOperations = new HashSet<string>(
            new[]
            {
                "Add",
                "Subtract",
                "Multiply",
                "Divide",
                "Remainder",
                "op_Addition",
                "op_Subtraction",
                "op_Multiply",
                "op_Division",
                "op_Modulus",
                "Round",
            },
            StringComparer.Ordinal);

        Assert.DoesNotContain(
            members.OfType<MethodBase>(),
            method =>
                method.DeclaringType == typeof(decimal) &&
                forbiddenDecimalOperations.Contains(method.Name));

        Assert.NotEmpty(instructions);
        Assert.Contains(
            instructions,
            instruction =>
                instruction.Member is MethodBase method &&
                method.DeclaringType?.FullName ==
                "TaxLogicEngine.Services.T4SlipBoxAmounts" &&
                method.Name == "Enumerate");
    }

    [Fact]
    public void CompiledAssemblyContainsNoCraLineBoxCodeOrMappingResourceLiteral() // AC-44
    {
        var literals = ProductionTypes()
            .SelectMany(DeclaredStringLiterals)
            .ToArray();

        Assert.NotEmpty(literals);
        Assert.DoesNotContain(
            literals,
            literal => Regex.IsMatch(
                literal,
                @"\b\d{5}\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)));
        Assert.DoesNotContain(
            literals,
            literal => Regex.IsMatch(
                literal,
                @"^\d{2}$",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)));
        Assert.DoesNotContain(
            literals,
            literal =>
                literal.Contains("TaxLogicEngine.Mappings", StringComparison.Ordinal) ||
                literal.Contains(".json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssemblyDeclaresNoSinCarrierOrMaskingHelper() // AC-39
    {
        var moduleTypes = ProductionTypes();
        Assert.NotEmpty(moduleTypes);

        foreach (var type in moduleTypes)
        {
            Assert.False(LooksLikeSinCarrier(type.Name), $"{type.Name} looks like a SIN type.");
            Assert.DoesNotContain("Mask", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Redact", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Scrub", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Sanitiz", type.Name, StringComparison.OrdinalIgnoreCase);

            foreach (var member in type.GetMembers(
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                Assert.False(
                    member is PropertyInfo && LooksLikeSinCarrier(member.Name),
                    $"{type.Name}.{member.Name} looks like a SIN property.");
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.NonPublic |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain("Mask", method.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Redact", method.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Scrub", method.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Sanitiz", method.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // -------------------------------------------------------------- scanner guard

    [Fact]
    public void ScannerVisitsEveryProductionFileAndFindsKnownAllowedTokens()
    {
        var files = ProductionSourceFiles();

        Assert.Equal(
            ExpectedProductionFiles.OrderBy(name => name, StringComparer.Ordinal),
            files.Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        var executable = string.Join("\n", files.SelectMany(ExecutableLines));

        Assert.True(
            executable.Length > 8000,
            $"Implausibly little executable source was scanned: {executable.Length} chars.");
        Assert.Contains(nameof(AccountantReviewPackage), executable, StringComparison.Ordinal);
        Assert.Contains(nameof(ReviewPackageAssembler), executable, StringComparison.Ordinal);
        Assert.Contains(nameof(ReviewFields.UnmappedBoxAcknowledgement), executable, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"T4SlipBoxAmounts\s*\.\s*Enumerate",
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

    [Fact]
    public void CommentFilterRemovesExplanatoryTextWithoutRemovingCode()
    {
        var policyPath = Path.Combine(
            ModuleRoot,
            "Services",
            "SupportedReviewTaxYears.cs");
        var raw = File.ReadAllText(policyPath);
        var executable = string.Join("\n", ExecutableLines(policyPath));

        Assert.Contains("clock read", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("clock read", executable, StringComparison.Ordinal);
        Assert.Contains("All.Contains(taxYear)", executable, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertDescriptorConstructor()
    {
        var constructor = Assert.Single(
            typeof(ReviewDecisionDescriptor).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance));

        Assert.Equal(
            new[]
            {
                typeof(SignOffDecision),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(bool?),
            },
            constructor.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray());
        Assert.Equal(
            new[]
            {
                NullabilityState.NotNull,
                NullabilityState.Nullable,
                NullabilityState.Nullable,
                NullabilityState.Nullable,
                NullabilityState.Nullable,
            },
            constructor.GetParameters()
                .Select(parameter =>
                    new NullabilityInfoContext().Create(parameter).ReadState)
                .ToArray());
    }

    private static void AssertExactProperties()
    {
        AssertProperties(
            typeof(ReviewedBoxAmount),
            new PropertyContract(nameof(ReviewedBoxAmount.BoxCode), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewedBoxAmount.Amount), typeof(decimal), true, NullabilityState.NotNull));
        AssertProperties(
            typeof(ReviewedSlip),
            new PropertyContract(nameof(ReviewedSlip.DocumentId), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewedSlip.TaxYear), typeof(int), false, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewedSlip.SourceReference), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewedSlip.Slip), typeof(T4Slip), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewedSlip.LineEntries), typeof(IReadOnlyList<TaxLineEntry>), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewedSlip.BoxAmounts), typeof(IReadOnlyList<ReviewedBoxAmount>), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewedSlip.UnmappedBoxCodes), typeof(IReadOnlyList<string>), true, NullabilityState.NotNull));
        AssertProperties(
            typeof(ReviewDecisionDescriptor),
            new PropertyContract(nameof(ReviewDecisionDescriptor.Decision), typeof(SignOffDecision), false, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewDecisionDescriptor.ReviewerReference), typeof(string), true, NullabilityState.Nullable),
            new PropertyContract(nameof(ReviewDecisionDescriptor.DecidedAtReference), typeof(string), true, NullabilityState.Nullable),
            new PropertyContract(nameof(ReviewDecisionDescriptor.Note), typeof(string), true, NullabilityState.Nullable),
            new PropertyContract(nameof(ReviewDecisionDescriptor.UnmappedBoxesAcknowledged), typeof(bool?), false, NullabilityState.Nullable));
        AssertProperties(
            typeof(ReviewSignOff),
            new PropertyContract(nameof(ReviewSignOff.Decision), typeof(SignOffDecision), false, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewSignOff.ReviewerReference), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewSignOff.DecidedAtReference), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(ReviewSignOff.Note), typeof(string), true, NullabilityState.Nullable),
            new PropertyContract(nameof(ReviewSignOff.UnmappedBoxesAcknowledged), typeof(bool?), false, NullabilityState.Nullable));
        AssertProperties(
            typeof(AccountantReviewPackage),
            new PropertyContract(nameof(AccountantReviewPackage.FirmId), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(AccountantReviewPackage.CaseId), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(AccountantReviewPackage.ParticipantId), typeof(string), true, NullabilityState.NotNull),
            new PropertyContract(nameof(AccountantReviewPackage.TaxYear), typeof(int), false, NullabilityState.NotNull),
            new PropertyContract(nameof(AccountantReviewPackage.ReviewedSlips), typeof(IReadOnlyList<ReviewedSlip>), true, NullabilityState.NotNull),
            new PropertyContract(nameof(AccountantReviewPackage.SignOff), typeof(ReviewSignOff), true, NullabilityState.Nullable));
    }

    private static void AssertExactMethods()
    {
        Assert.Equal(
            new[] { typeof(IEquatable<ReviewedBoxAmount>) },
            typeof(ReviewedBoxAmount).GetInterfaces());
        Assert.Equal(
            new[] { typeof(IEquatable<ReviewedSlip>) },
            typeof(ReviewedSlip).GetInterfaces());
        Assert.Empty(typeof(ReviewDecisionDescriptor).GetInterfaces());
        Assert.Equal(
            new[] { typeof(IEquatable<ReviewSignOff>) },
            typeof(ReviewSignOff).GetInterfaces());
        Assert.Equal(
            new[] { typeof(IEquatable<AccountantReviewPackage>) },
            typeof(AccountantReviewPackage).GetInterfaces());

        AssertStatefulClass<ReviewedBoxAmount>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertStatefulClass<ReviewedSlip>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertStatefulClass<ReviewDecisionDescriptor>("ToString");
        AssertStatefulClass<ReviewSignOff>(
            "Equals",
            "Equals",
            "GetHashCode",
            "ToString");
        AssertStatefulClass<AccountantReviewPackage>(
            "Equals",
            "Equals",
            "GetHashCode",
            "RecordDecision",
            "ToString");

        AssertEquatable<ReviewedBoxAmount>();
        AssertEquatable<ReviewedSlip>();
        AssertEquatable<ReviewSignOff>();
        AssertEquatable<AccountantReviewPackage>();

        var recordDecision = typeof(AccountantReviewPackage).GetMethod(
            nameof(AccountantReviewPackage.RecordDecision),
            new[] { typeof(ReviewDecisionDescriptor) });
        Assert.NotNull(recordDecision);
        Assert.Equal(
            typeof(ParseResult<AccountantReviewPackage>),
            recordDecision!.ReturnType);
        Assert.False(recordDecision.IsStatic);
        Assert.Equal(
            NullabilityState.Nullable,
            new NullabilityInfoContext()
                .Create(Assert.Single(recordDecision.GetParameters()))
                .ReadState);
    }

    private static void AssertExactStaticContracts()
    {
        Assert.True(typeof(ReviewFields).IsAbstract && typeof(ReviewFields).IsSealed);
        Assert.True(
            typeof(SupportedReviewTaxYears).IsAbstract &&
            typeof(SupportedReviewTaxYears).IsSealed);
        Assert.True(
            typeof(ReviewPackageAssembler).IsAbstract &&
            typeof(ReviewPackageAssembler).IsSealed);

        var fields = typeof(ReviewFields)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
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
            },
            fields);
        Assert.All(fields, field => Assert.Equal(field.Name, field.Value));
        Assert.Empty(typeof(ReviewFields).GetProperties(
            BindingFlags.Public | BindingFlags.Static));

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

        var assemble = Assert.Single(PublicDeclaredMethods(typeof(ReviewPackageAssembler)));
        Assert.Equal(nameof(ReviewPackageAssembler.Assemble), assemble.Name);
        Assert.True(assemble.IsStatic);
        Assert.Equal(typeof(ParseResult<AccountantReviewPackage>), assemble.ReturnType);

        var parameter = Assert.Single(assemble.GetParameters());
        Assert.Equal(typeof(IReadOnlyList<ProcessedT4Extraction>), parameter.ParameterType);
        var nullability = new NullabilityInfoContext().Create(parameter);
        Assert.Equal(NullabilityState.Nullable, nullability.ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            Assert.Single(nullability.GenericTypeArguments).ReadState);
    }

    private static void AssertInternalConstructor(
        Type type,
        Type[] parameterTypes,
        NullabilityState[] nullabilityStates)
    {
        Assert.Empty(type.GetConstructors(
            BindingFlags.Public | BindingFlags.Instance));

        var constructor = Assert.Single(type.GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.True(constructor.IsAssembly, $"{type.Name} constructor must be internal.");
        Assert.Equal(
            parameterTypes,
            constructor.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray());
        Assert.Equal(
            nullabilityStates,
            constructor.GetParameters()
                .Select(parameter =>
                    new NullabilityInfoContext().Create(parameter).ReadState)
                .ToArray());
    }

    private static void AssertProperties(
        Type type,
        params PropertyContract[] expected)
    {
        Assert.Equal(
            expected.Select(contract => contract.Name).OrderBy(name => name, StringComparer.Ordinal),
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        foreach (var contract in expected)
        {
            var property = type.GetProperty(
                contract.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.NotNull(property);
            Assert.Equal(contract.Type, property!.PropertyType);
            Assert.NotNull(property.GetMethod);
            Assert.Null(property.SetMethod);
            Assert.Equal(
                contract.JsonIgnored,
                property.GetCustomAttribute<JsonIgnoreAttribute>() is not null);
            Assert.Equal(
                contract.Nullability,
                new NullabilityInfoContext().Create(property).ReadState);
        }
    }

    private static void AssertStatefulClass<T>(params string[] methodNames)
    {
        var type = typeof(T);
        Assert.True(type.IsSealed);
        Assert.False(type.IsAbstract);
        Assert.Null(type.GetMethod(
            "<Clone>$",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Null(type.GetProperty(
            "EqualityContract",
            BindingFlags.NonPublic | BindingFlags.Instance));

        Assert.Equal(
            methodNames.OrderBy(name => name, StringComparer.Ordinal),
            PublicDeclaredMethods(type)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        var toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);
        Assert.NotNull(toString);
        Assert.Equal(type, toString!.DeclaringType);
    }

    private static void AssertEquatable<T>()
    {
        var type = typeof(T);
        Assert.Contains(typeof(IEquatable<T>), type.GetInterfaces());
        Assert.NotNull(type.GetMethod(nameof(IEquatable<T>.Equals), new[] { type }));
        Assert.Equal(
            type,
            type.GetMethod(nameof(object.Equals), new[] { typeof(object) })!.DeclaringType);
        Assert.Equal(
            type,
            type.GetMethod(nameof(GetHashCode), Type.EmptyTypes)!.DeclaringType);
    }

    private static MethodInfo[] PublicDeclaredMethods(Type type) =>
        type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

    private static Type[] ProductionTypes() =>
        ProductionAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "CaseReview",
                StringComparison.Ordinal) == true)
            .ToArray();

    private static MethodBase[] ProductionMethods() =>
        ProductionTypes()
            .SelectMany(type =>
                type.GetMethods(
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance |
                        BindingFlags.Static |
                        BindingFlags.DeclaredOnly)
                    .Cast<MethodBase>()
                    .Concat(type.GetConstructors(
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance |
                        BindingFlags.Static)))
            .ToArray();

    private static string[] ProductionSourceFiles() =>
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

    private static string[] ProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> DeclaredStringLiterals(Type type)
    {
        const BindingFlags Flags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(Flags))
        {
            if (field.IsLiteral &&
                field.FieldType == typeof(string) &&
                field.GetRawConstantValue() is string value)
            {
                yield return value;
            }
        }

        foreach (var instruction in type.GetMethods(Flags)
                     .Cast<MethodBase>()
                     .Concat(type.GetConstructors(Flags))
                     .SelectMany(IlInstructions))
        {
            if (instruction.StringLiteral is not null)
            {
                yield return instruction.StringLiteral;
            }
        }
    }

    private static IEnumerable<IlInstruction> IlInstructions(MethodBase method)
    {
        byte[]? bytes;
        try
        {
            bytes = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        if (bytes is null)
        {
            yield break;
        }

        var module = method.Module;
        var typeArguments = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : Type.EmptyTypes;
        var methodArguments = method.IsGenericMethod
            ? method.GetGenericArguments()
            : Type.EmptyTypes;
        var position = 0;

        while (position < bytes.Length)
        {
            var offset = position;
            short key;

            if (bytes[position] == 0xFE && position + 1 < bytes.Length)
            {
                key = unchecked((short)(0xFE00 | bytes[position + 1]));
                position += 2;
            }
            else
            {
                key = bytes[position];
                position++;
            }

            if (!OpCodesByValue.TryGetValue(key, out var opCode))
            {
                throw new InvalidOperationException(
                    $"Unknown IL opcode 0x{key:X4} in {method.Name} at {offset}.");
            }

            MemberInfo? member = null;
            string? literal = null;

            if (opCode.OperandType is
                OperandType.InlineMethod or
                OperandType.InlineField or
                OperandType.InlineType or
                OperandType.InlineTok)
            {
                var token = BitConverter.ToInt32(bytes, position);
                member = module.ResolveMember(token, typeArguments, methodArguments);
            }
            else if (opCode.OperandType == OperandType.InlineString)
            {
                literal = module.ResolveString(BitConverter.ToInt32(bytes, position));
            }

            yield return new IlInstruction(opCode, member, literal);
            position += OperandSize(opCode, bytes, position);
        }
    }

    private static int OperandSize(
        OpCode opCode,
        byte[] bytes,
        int operandPosition) =>
        opCode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or
            OperandType.ShortInlineI or
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch =>
                4 + (4 * BitConverter.ToInt32(bytes, operandPosition)),
            _ => 4,
        };

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null))
            .OfType<OpCode>()
            .ToDictionary(opCode => opCode.Value);

    private static string MemberDisplayName(MemberInfo member) =>
        $"{member.DeclaringType?.FullName}.{member.Name}";

    private static bool LooksLikeSinCarrier(string name) =>
        name.Equals("Sin", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SocialInsurance", StringComparison.OrdinalIgnoreCase);

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

    private sealed record PropertyContract(
        string Name,
        Type Type,
        bool JsonIgnored,
        NullabilityState Nullability);

    private sealed record IlInstruction(
        OpCode OpCode,
        MemberInfo? Member,
        string? StringLiteral);
}
