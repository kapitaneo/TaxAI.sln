using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxAI.Portal.Tests.Identity;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 017 T04 — what the local MVP did <b>not</b> change, and what its
/// documentation says.
/// <para>
/// A claim such as "no tax file changed" is a claim about the work of one
/// specification, so each comparison here runs from the planning baseline to
/// <b>the tree spec 017 produced</b>: the commit that recorded its final
/// completion marker, or — while that marker is not yet committed — the working
/// tree, untracked files included. The first form keeps the claim true forever;
/// the second makes it bite while the task is still being written. A later
/// specification that legitimately changes one of these files is therefore not
/// blocked by this one (the same bounding spec 007's repository suite uses).
/// </para>
/// <para>
/// The rules that are not historical — no startup migration, no nine-digit run
/// in the documentation, no lowered floor — are asserted against the working
/// tree, always.
/// </para>
/// </summary>
public sealed class LocalMvpRepositoryTests
{
    /// <summary>The planning baseline of spec 017 (merge of PR #152).</summary>
    private const string Baseline = "7ee2574e246d574fb328d185db56fb8a1894b64d";

    private const string SpecDirectory = "specs/017-local-end-to-end-accountant-mvp";

    private const string FinalMarker = SpecDirectory + "/status/T04.done";

    private const string DemoGuide = "docs/local-demo.md";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly Regex NineDigitRun = new(
        "[0-9]{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // -------------------------------------------- AC-85, AC-86: tax behaviour

    [Theory] // AC-85
    [InlineData("src/TaxLogicEngine")]
    [InlineData("src/TaxCases")]
    [InlineData("src/DocumentExtraction")]
    [InlineData("src/CaseProcessing")]
    [InlineData("src/CaseReview")]
    [InlineData("tests/TaxLogicEngine.Tests")]
    [InlineData("tests/TaxCases.Tests")]
    [InlineData("tests/DocumentExtraction.Tests")]
    [InlineData("tests/CaseProcessing.Tests")]
    [InlineData("tests/CaseReview.Tests")]
    public void NoTaxFileOrTaxTestChanged(string tree)
    {
        Assert.NotEmpty(TrackedAtBaseline(tree));
        Assert.Empty(ChangedSinceBaseline(tree));
    }

    [Fact] // AC-86
    public void TheRoundingHelperAndItsMidpointTestsAreUntouched()
    {
        Assert.Empty(ChangedSinceBaseline(
            "src/TaxLogicEngine/Services/MoneyRounding.cs",
            "tests/TaxLogicEngine.Tests/Services/MoneyRoundingTests.cs"));

        // The midpoint theory still covers both signs, exactly as at baseline.
        var tests = File.ReadAllText(Path.Combine(
            RepositoryRoot, "tests", "TaxLogicEngine.Tests", "Services", "MoneyRoundingTests.cs"));
        Assert.Contains("public void ToCents_RoundsHalfUpAwayFromZero(", tests, StringComparison.Ordinal);
        Assert.Matches("InlineData\\(\"-[0-9.]+\", \"-[0-9.]+\"\\)", tests);
        Assert.Matches("InlineData\\(\"[0-9.]+\", \"[0-9.]+\"\\)", tests);
    }

    // -------------------------- AC-35, AC-37, AC-79, AC-89, AC-90: preserved

    [Fact] // AC-35
    public void TheProductionValidatorLibraryIsUnchanged()
    {
        Assert.NotEmpty(TrackedAtBaseline("src/TaxAI.AzureInfrastructure"));
        Assert.Empty(ChangedSinceBaseline("src/TaxAI.AzureInfrastructure"));
    }

    [Fact] // AC-37
    public void TheUploadGateIsByteIdentical()
    {
        const string validator = "src/ClientIntake/Services/IntakeUploadValidator.cs";

        Assert.Equal(new[] { validator }, TrackedAtBaseline(validator));
        Assert.Empty(ChangedSinceBaseline(validator));
    }

    [Fact] // AC-79
    public void TheReviewSurfaceAndTheFixedShellContentAreByteIdentical()
    {
        var preserved = new[]
        {
            "src/TaxAI.Portal/ReviewEndpoints.cs",
            "src/TaxAI.Portal/Components/Pages/ReviewPage.razor",
            "src/TaxAI.Portal/Components/Pages/ReviewPage.razor.css",
            "src/TaxAI.Portal/Components/Pages/ReviewPageModel.cs",
            "src/TaxAI.Portal/PortalContent.cs",
        };

        Assert.Equal(preserved.Order(StringComparer.Ordinal), TrackedAtBaseline(preserved));
        Assert.Empty(ChangedSinceBaseline(preserved));
    }

    [Theory] // AC-79
    [InlineData("/health", "HealthBody", "PlainTextContentType")]
    [InlineData("/", "ShellHtml", "HtmlContentType")]
    public async Task TheFixedResponsesAreExactlyTheUnchangedConstants(
        string path,
        string bodyConstant,
        string contentTypeConstant)
    {
        using var host = PortalAuthorizationHarness.CreateHost(
            new PortalAuthorizationHarness.IdentityDirectory(),
            new PortalAuthorizationHarness.SessionStore());
        using var client = PortalAuthorizationHarness.CreateClient(host, followRedirects: false);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PortalConstant(contentTypeConstant), response.Content.Headers.ContentType?.ToString());
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(PortalConstant(bodyConstant)),
            await response.Content.ReadAsByteArrayAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory] // AC-89
    [InlineData("specs/016-canadian-azure-production-infrastructure")]
    [InlineData("infra/azure")]
    [InlineData("scripts/azure")]
    public void Spec016IsUntouchedAndGainedNoStatusMarker(string tree)
    {
        Assert.NotEmpty(TrackedAtBaseline(tree));
        Assert.Empty(ChangedSinceBaseline(tree));
    }

    [Theory] // AC-90, AC-44
    [InlineData("src/TaxAI.Portal/Production")]
    [InlineData("tests/TaxAI.Portal.Tests/Production")]
    [InlineData("src/DocumentIntelligence/Production")]
    public void TheProductionAdmissionCodeAndItsTestsAreUnmodified(string tree)
    {
        Assert.NotEmpty(TrackedAtBaseline(tree));
        Assert.Empty(ChangedSinceBaseline(tree));
    }

    [Fact] // AC-83
    public void NoProjectFileOrSolutionEntryChanged()
    {
        Assert.Contains("TaxAI.sln", TrackedAtBaseline("TaxAI.sln"));
        Assert.Contains(
            "src/TaxAI.Portal/TaxAI.Portal.csproj",
            TrackedAtBaseline("*.csproj"));

        // Byte identity is stronger than the criterion: no project reference,
        // package reference, property, or solution entry was added or removed.
        Assert.Empty(ChangedSinceBaseline("*.csproj", "TaxAI.sln", "global.json", ".config"));
    }

    // ---------------------------------------- AC-57: the SIN and PII guards

    /// <summary>
    /// Guard files this feature amended, each by an amendment the PRD's approved
    /// table or an authorized X-item names.
    /// </summary>
    private static readonly string[] AmendedGuardFiles =
    [
        "tests/ExtractionWorkflow.Tests/Ingest/IngestWorkflowTests.cs",
        "tests/Persistence.Tests/Composition/DevelopmentSeederTests.cs",
        "tests/Persistence.Tests/EndToEnd/ClientIntakeWorkflowPersistenceTests.cs",
        "tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeAcceptanceTests.cs",
        "tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs",
        "tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs",
    ];

    [Fact] // AC-57
    public void EveryBaselineSinAndPiiGuardStillExistsUnderItsName()
    {
        var guards = BaselineGuards();

        // The inventory is real: it spans the engine, every module, and the host.
        Assert.True(guards.Length > 80, $"Only {guards.Length} baseline guards were found.");
        foreach (var project in new[]
                 {
                     "TaxLogicEngine.Tests", "TaxCases.Tests", "DocumentExtraction.Tests",
                     "CaseProcessing.Tests", "CaseReview.Tests", "Tenancy.Tests",
                     "Persistence.Tests", "ExtractionWorkflow.Tests", "TaxAI.Portal.Tests",
                 })
        {
            Assert.Contains(guards, guard => guard.File.StartsWith($"tests/{project}/", StringComparison.Ordinal));
        }

        var missing = guards
            .Where(guard => !Regex.IsMatch(
                ReadProduced(guard.File) ?? string.Empty,
                @"\b" + Regex.Escape(guard.Method) + @"\(",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .Select(guard => $"{guard.File}: {guard.Method}")
            .ToArray();

        Assert.True(missing.Length == 0, "Missing baseline guards: " + string.Join(" | ", missing));
    }

    [Fact] // AC-57
    public void NoGuardFileChangedExceptByAnApprovedAmendment()
    {
        var guardFiles = BaselineGuards().Select(guard => guard.File).Distinct(StringComparer.Ordinal).ToArray();
        var changed = ChangedSinceBaseline("tests")
            .Intersect(guardFiles, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(changed.Except(AmendedGuardFiles, StringComparer.Ordinal));

        // And nothing was skipped to make a guard pass.
        foreach (var file in guardFiles)
        {
            var text = ReadProduced(file) ?? string.Empty;
            Assert.DoesNotContain("Skip =", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Skip=", text, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------- AC-11: no startup migration

    [Fact] // AC-11
    public void NoProductionProjectMigratesOrCreatesTheSchema()
    {
        var sources = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) ||
                           path.EndsWith(".razor", StringComparison.Ordinal))
            .ToArray();

        Assert.True(sources.Length > 100);
        foreach (var source in sources)
        {
            var text = string.Join("\n", ExecutableLines(File.ReadAllLines(source)));
            foreach (var forbidden in new[] { "Migrate(", "MigrateAsync(", "EnsureCreated" })
            {
                Assert.False(
                    text.Contains(forbidden, StringComparison.Ordinal),
                    $"{Path.GetRelativePath(RepositoryRoot, source)} contains {forbidden}");
            }
        }
    }

    // ---------------------------------------- AC-81: no accountant upload

    private static readonly Type[] DocumentBodyTypes =
    [
        typeof(IFormFile), typeof(IFormFileCollection), typeof(IFormCollection),
        typeof(Stream), typeof(PipeReader), typeof(byte[]),
    ];

    [Fact] // AC-81
    public void NoFirmScopedRouteAcceptsADocumentAndTheOnlyMultipartRouteIsTheClientUpload()
    {
        using var host = PortalAuthorizationHarness.CreateHost(
            new PortalAuthorizationHarness.IdentityDirectory(),
            new PortalAuthorizationHarness.SessionStore());
        using var client = host.CreateClient();

        var endpoints = host.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var firmScoped = endpoints
            .Where(endpoint => endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Any(data => data.Policy == "TaxAiFirmScoped"))
            .ToArray();

        // The firm-scoped surface is found, and none of its handlers takes a
        // file, a raw body, or a whole form.
        Assert.True(firmScoped.Length >= 15, $"Only {firmScoped.Length} firm-scoped endpoints were found.");
        foreach (var endpoint in firmScoped)
        {
            var handler = endpoint.Metadata.GetMetadata<MethodInfo>();
            Assert.NotNull(handler);
            Assert.DoesNotContain(
                handler!.GetParameters(),
                parameter => DocumentBodyTypes.Any(type => type.IsAssignableFrom(parameter.ParameterType)));
        }

        // The one route that receives a document is the existing client one.
        Assert.Equal(
            new[] { "POST /client-intake/documents" },
            endpoints
                .Where(endpoint => endpoint.RoutePattern.RawText!.Contains("document", StringComparison.OrdinalIgnoreCase) &&
                                   !endpoint.RoutePattern.RawText.Contains("{documentId", StringComparison.Ordinal))
                .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                    .Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
                .ToArray());

        // And multipart parsing exists in exactly one portal file.
        var portal = Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");
        var multipartFiles = Directory
            .EnumerateFiles(portal, "*.*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) ||
                           path.EndsWith(".razor", StringComparison.Ordinal))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("MultipartReader", StringComparison.Ordinal) ||
                       text.Contains("IFormFile", StringComparison.Ordinal) ||
                       text.Contains("Request.Body", StringComparison.Ordinal) ||
                       text.Contains("BodyReader", StringComparison.Ordinal);
            })
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.Equal(new[] { "ClientIntakeEndpoints.cs" }, multipartFiles);
    }

    // ------------------------------------- AC-84, AC-87: what the feature added

    [Fact] // AC-84
    public void TheFeatureRegistersNoBackgroundInitiator()
    {
        var added = FeatureAdditions();
        Assert.Contains(added, file => file.Path == "src/TaxAI.Portal/CaseListEndpoints.cs");
        Assert.Contains(added, file => file.Path == "src/TaxAI.Portal/ModuleRegistration.cs");

        foreach (var file in added)
        {
            foreach (var forbidden in new[]
                     {
                         "AddHostedService", "IHostedService", "BackgroundService", "PeriodicTimer",
                         "System.Threading.Timer", "new Timer(", "Channel<", "Task.Run(",
                         "IHostApplicationLifetime", "QueueBackgroundWorkItem",
                     })
            {
                Assert.False(
                    file.AddedLines.Any(line => line.Contains(forbidden, StringComparison.Ordinal)),
                    $"{file.Path} adds {forbidden}");
            }
        }

        // And the composed Development host runs no hosted service of this
        // repository's own.
        using var host = PortalAuthorizationHarness.CreateHost(
            new PortalAuthorizationHarness.IdentityDirectory(),
            new PortalAuthorizationHarness.SessionStore());
        Assert.DoesNotContain(
            host.Services.GetServices<IHostedService>(),
            service =>
            {
                var owner = service.GetType().Assembly.GetName().Name!;
                return owner.StartsWith("TaxAI", StringComparison.Ordinal) ||
                       owner is "Persistence" or "Tenancy" or "ClientIntake" or "ExtractionWorkflow" or
                           "DocumentIntelligence" or "CaseProcessing" or "CaseReview" or
                           "DocumentExtraction" or "TaxCases" or "TaxLogicEngine";
            });
    }

    [Fact] // AC-87
    public void TheFeatureAddsNoCraLineMappingArithmeticOrRounding()
    {
        var added = FeatureAdditions();

        // Every production and tooling file the feature added or changed.
        foreach (var expected in new[]
                 {
                     "scripts/local/Start-LocalDemo.ps1",
                     "scripts/local/New-LocalDemoSamples.ps1",
                     "src/DocumentIntelligence/DeterministicDocumentIntelligenceProvider.cs",
                     "src/Persistence/Repositories/SqlIntakeStore.cs",
                     "src/TaxAI.Portal/DevelopmentSignInEndpoints.cs",
                     "src/TaxAI.Portal/ExtractionEndpoints.cs",
                     "src/TaxAI.Portal/Components/Pages/CaseListPage.razor",
                 })
        {
            Assert.Contains(added, file => file.Path == expected);
        }

        // A CRA line number is a bare five-digit literal. A printed sample
        // amount carries its cents, and is not one.
        var fiveDigitLiteral = new Regex(
            @"(?<![0-9.])[0-9]{5}(?![0-9.])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var numericType = new Regex(
            @"\b(decimal|double|float)\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var violations = new List<string>();

        foreach (var file in added)
        {
            foreach (var line in ExecutableLines(file.AddedLines))
            {
                if (fiveDigitLiteral.IsMatch(line) || numericType.IsMatch(line))
                {
                    violations.Add($"{file.Path}: {line.Trim()}");
                }

                foreach (var forbidden in new[]
                         {
                             "LineMap", "TaxLogicEngine.Mappings", "GetManifestResource", "T4LineMapper",
                             "T5LineMapper", "TaxLineEntry", "LineNumber", "MoneyRounding", "Math.",
                             "MidpointRounding", "Dictionary<string, decimal", "T4BoxAmounts",
                         })
                {
                    if (line.Contains(forbidden, StringComparison.Ordinal))
                    {
                        violations.Add($"{file.Path}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(" | ", violations));
    }

    // ----------------------------------------------- AC-92: the floors

    [Fact] // AC-92
    public void NoTestFloorWasLoweredAndNoProjectWasRemoved()
    {
        var baseline = Floors(Git("show", $"{Baseline}:scripts/test-projects.json"));
        var current = Floors(File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "test-projects.json")));

        Assert.NotEmpty(baseline);
        foreach (var (project, floor) in baseline)
        {
            Assert.True(current.ContainsKey(project), $"{project} was removed from the gate.");
            Assert.True(
                current[project] >= floor,
                $"{project} floor fell from {floor} to {current[project]}.");
        }

        // The floors this feature earned.
        Assert.True(current["TaxAI.Portal.Tests"] > baseline["TaxAI.Portal.Tests"]);
        Assert.True(current["Persistence.Tests"] > baseline["Persistence.Tests"]);
    }

    // --------------------------------- AC-55, AC-93: the documentation

    [Fact] // AC-55
    public void TheGuideTheReadmeAndTheSpecFolderCarryNoNineDigitRun()
    {
        var documents = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, SpecDirectory.Replace('/', Path.DirectorySeparatorChar)),
                "*",
                SearchOption.AllDirectories)
            .Append(Path.Combine(RepositoryRoot, "README.md"))
            .Append(Path.Combine(RepositoryRoot, "docs", "local-demo.md"))
            .ToArray();

        Assert.Contains(documents, path => path.EndsWith("verification.md", StringComparison.Ordinal));
        Assert.Contains(documents, path => path.EndsWith("prd.md", StringComparison.Ordinal));
        Assert.All(documents, path =>
        {
            Assert.True(File.Exists(path), $"{path} must exist.");
            var match = NineDigitRun.Match(File.ReadAllText(path));
            Assert.False(
                match.Success,
                $"{Path.GetRelativePath(RepositoryRoot, path)} contains a nine-digit run.");
        });
    }

    [Fact] // AC-93
    public void TheDemoGuideNamesTheOneCommandAndTheTwelveSteps()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "local-demo.md"));
        var normalized = Regex.Replace(guide, @"\s+", " ");

        Assert.Contains("pwsh ./scripts/local/Start-LocalDemo.ps1", guide, StringComparison.Ordinal);
        Assert.Contains("## Prerequisites", guide, StringComparison.Ordinal);
        Assert.Contains("/development/sign-in", guide, StringComparison.Ordinal);
        Assert.Contains("artifacts/local-demo", guide, StringComparison.Ordinal);

        // Exactly the twelve steps of the target flow, numbered in order.
        var steps = Regex.Matches(guide, @"^(?<number>[0-9]+)\. \*\*", RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(Enumerable.Range(1, 12), steps);

        foreach (var required in new[]
                 {
                     "synthetic",
                     "not viewable PDFs",
                     "Never use a real taxpayer document",
                     "is not production authorization",
                     "second browser",
                     "private window",
                 })
        {
            Assert.Contains(required, normalized, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // AC-93
    public void TheDemoGuideCarriesNoKeyCredentialOrConnectionString()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "local-demo.md"));

        foreach (var forbidden in new[]
                 {
                     "Server=", "Data Source=", "Initial Catalog=", "Integrated Security", "Password",
                     "Pwd=", "User Id=", "Encrypt=", "TrustServerCertificate=", "ClientSecret",
                     "client_secret", "BEGIN ", "AccountKey=", "SharedAccessSignature",
                 })
        {
            Assert.DoesNotContain(forbidden, guide, StringComparison.OrdinalIgnoreCase);
        }

        // No encoded 32-byte key, no long opaque token, and no nine-digit run.
        Assert.DoesNotMatch(@"[A-Za-z0-9+/]{43}=", guide);
        Assert.DoesNotMatch(@"[A-Za-z0-9_\-]{40,}", guide);
        Assert.DoesNotMatch(NineDigitRun, guide);
    }

    [Fact] // FR-49
    public void TheInstructionsAndRoadmapRecordSpecSeventeenAsCompleteWithItsGatesOpen()
    {
        var instructions = Normalize(File.ReadAllText(Path.Combine(RepositoryRoot, "CLAUDE.md")));
        foreach (var required in new[]
                 {
                     "Spec 017 (Local End-to-End Accountant MVP) is **complete**",
                     "docs/local-demo.md",
                     SpecDirectory + "/verification.md",
                     "AC-1 through AC-93",
                     "A local run is never production authorization",
                     "no real taxpayer document may be used locally",
                 })
        {
            Assert.Contains(required, instructions, StringComparison.OrdinalIgnoreCase);
        }

        var next = Normalize(File.ReadAllText(Path.Combine(RepositoryRoot, "NEXT.md")));
        foreach (var required in new[]
                 {
                     "**Spec 017 is complete.**",
                     "Spec 016 is frozen after T06",
                     "is not production authorization",
                     "accountant-side document upload",
                 })
        {
            Assert.Contains(required, next, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // the evidence record
    public void TheAcceptanceEvidenceCoversEveryCriterionAndRecordsTheLocalRun()
    {
        var path = Path.Combine(
            RepositoryRoot, SpecDirectory.Replace('/', Path.DirectorySeparatorChar), "verification.md");
        Assert.True(File.Exists(path), "Spec 017's acceptance evidence must be recorded before completion.");
        var text = File.ReadAllText(path);

        for (var criterion = 1; criterion <= 93; criterion++)
        {
            Assert.True(
                Regex.IsMatch(text, $@"\bAC-{criterion}\b(?![0-9a-z])"),
                $"AC-{criterion} has no evidence line.");
        }

        foreach (var required in new[]
                 {
                     "AC-76a", "AC-76b",
                     "scripts/Invoke-SolutionTests.ps1",
                     "scripts/local/Start-LocalDemo.ps1",
                     "## Local run",
                     Baseline[..7],
                 })
        {
            Assert.Contains(required, text, StringComparison.Ordinal);
        }
    }

    // ----------------------------------------------------------------- helpers

    private sealed record Guard(string File, string Method);

    private sealed record Addition(string Path, string[] AddedLines);

    /// <summary>
    /// "Sin" and "Pii" as whole words of a name: followed by an upper-case
    /// letter, a digit, an underscore, or the end of the name. That keeps
    /// <c>Single</c>, <c>Singleton</c> and <c>Sink</c> out of the inventory.
    /// </summary>
    private static readonly Regex GuardDeclaration = new(
        @"public (?:async )?(?:void|Task) (?<method>[A-Za-z0-9_]*(?:Sin(?![a-z])|NineDigit|Pii(?![a-z]))[A-Za-z0-9_]*)\(",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Every test method at the baseline whose name says it guards a SIN, a
    /// nine-digit value, or PII, with the file that declared it.
    /// </summary>
    private static Guard[] BaselineGuards() =>
        Lines(Git(
                "grep", "-n", "-E",
                @"public (async )?(void|Task) [A-Za-z0-9_]*(Sin|NineDigit|Pii)[A-Za-z0-9_]*\(",
                Baseline, "--", "tests"))
            .Select(line =>
            {
                // <commit>:<path>:<line>:<text>
                var parts = line.Split(':', 4);
                return (File: parts[1], Declaration: GuardDeclaration.Match(parts[3]));
            })
            .Where(candidate => candidate.Declaration.Success)
            .Select(candidate => new Guard(candidate.File, candidate.Declaration.Groups["method"].Value))
            .ToArray();

    /// <summary>
    /// The lines this feature added to each production and tooling file it
    /// added or changed. A new file contributes every line.
    /// </summary>
    private static Addition[] FeatureAdditions() =>
        ChangedSinceBaseline("src", "scripts/local")
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) ||
                           path.EndsWith(".razor", StringComparison.Ordinal) ||
                           path.EndsWith(".ps1", StringComparison.Ordinal))
            .Select(path => new Addition(
                path,
                Lines(Git("ls-files", "--", path)).Length == 0
                    // An untracked new file has no diff yet: every line is added.
                    ? File.ReadAllLines(Path.Combine(RepositoryRoot, path))
                    : Lines(Diff("-U0", "--no-ext-diff", "--no-color", "--", path))
                        .Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
                        .Select(line => line[1..])
                        .ToArray()))
            .ToArray();

    /// <summary>
    /// The commit that recorded spec 017's final completion marker, or null
    /// while that marker has not been committed.
    /// </summary>
    private static string? ProducedCommit() =>
        Lines(Git("log", "--diff-filter=A", "--format=%H", "--", FinalMarker)).FirstOrDefault();

    /// <summary>
    /// The paths under <paramref name="paths"/> that differ between the
    /// baseline and the tree spec 017 produced.
    /// </summary>
    private static string[] ChangedSinceBaseline(params string[] paths)
    {
        var changed = Lines(Diff(["--name-only", "--", .. paths]));
        if (ProducedCommit() is null)
        {
            // Still in the working tree: an untracked file is a change too.
            changed = changed
                .Concat(Lines(Git(["ls-files", "--others", "--exclude-standard", "--", .. paths])))
                .ToArray();
        }

        return changed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>git diff from the baseline to the tree spec 017 produced.</summary>
    private static string Diff(params string[] arguments) =>
        ProducedCommit() is { } produced
            ? Git(["diff", Baseline, produced, .. arguments])
            : Git(["diff", Baseline, .. arguments]);

    /// <summary>A file's content in the tree spec 017 produced, or null when absent.</summary>
    private static string? ReadProduced(string path)
    {
        if (ProducedCommit() is { } produced)
        {
            var (exitCode, output) = TryGit("show", $"{produced}:{path}");
            return exitCode == 0 ? output : null;
        }

        var onDisk = Path.Combine(RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(onDisk) ? File.ReadAllText(onDisk) : null;
    }

    private static string[] TrackedAtBaseline(params string[] paths) =>
        Lines(Git(["ls-tree", "-r", "--name-only", Baseline, "--", .. ExpandGlobs(paths)]))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>ls-tree takes paths, not globs; a glob is resolved through ls-files.</summary>
    private static string[] ExpandGlobs(string[] paths) =>
        paths
            .SelectMany(path => path.Contains('*', StringComparison.Ordinal)
                ? Lines(Git("ls-files", "--", path))
                : [path])
            .ToArray();

    private static Dictionary<string, int> Floors(string manifest)
    {
        using var document = JsonDocument.Parse(manifest);
        return document.RootElement
            .GetProperty("projects")
            .EnumerateArray()
            .ToDictionary(
                project => project.GetProperty("name").GetString()!,
                project => project.GetProperty("minimumTests").GetInt32(),
                StringComparer.Ordinal);
    }

    private static string PortalConstant(string name) =>
        (string)typeof(Program).Assembly
            .GetType("TaxAI.Portal.PortalContent", throwOnError: true)!
            .GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .GetRawConstantValue()!;

    /// <summary>Source lines with blank lines and whole-line comments removed.</summary>
    private static IEnumerable<string> ExecutableLines(IEnumerable<string> lines) =>
        lines.Where(line =>
        {
            var trimmed = line.Trim();
            return trimmed.Length > 0 &&
                   !trimmed.StartsWith("//", StringComparison.Ordinal) &&
                   !trimmed.StartsWith("*", StringComparison.Ordinal) &&
                   !trimmed.StartsWith("/*", StringComparison.Ordinal) &&
                   !trimmed.StartsWith("#", StringComparison.Ordinal) &&
                   !trimmed.StartsWith("@*", StringComparison.Ordinal);
        });

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ");

    private static string[] Lines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static string Git(params string[] arguments)
    {
        var (exitCode, output) = TryGit(arguments);
        Assert.True(exitCode == 0, $"git {string.Join(' ', arguments)} failed: {output}");
        return output;
    }

    private static (int ExitCode, string Output) TryGit(params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        // Quoted paths would break the path comparisons above.
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("core.quotepath=false");
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return process.ExitCode == 0
            ? (0, output.GetAwaiter().GetResult())
            : (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("The repository root was not found.");
    }
}
