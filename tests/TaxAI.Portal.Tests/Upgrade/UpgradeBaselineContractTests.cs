using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace TaxAI.Portal.Tests.Upgrade;

/// <summary>
/// Spec 013 T01: freezes repository paths/references, migration bytes, test
/// floors, embedded resources, and user-declared public/internal contracts
/// independently of the target framework and package versions that later tasks
/// intentionally change.
/// </summary>
public sealed class UpgradeBaselineContractTests
{
    private const string BaselineCommit =
        "13097dada6b1aae070fc0006d48a6862ce9904f8";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string BaselineRoot = Path.Combine(
        RepositoryRoot,
        "specs",
        "013-dotnet-10-platform-upgrade",
        "baselines");

    /// <summary>
    /// Project references added since the spec 013 baseline, each approved by
    /// the specification that added it. One reviewable line per reference, never
    /// a closed set of everything a project may ever reference.
    /// </summary>
    private static readonly (string Project, string Reference)[] ApprovedPostBaselineReferences =
    [
        // Spec 014 — the client intake contract.
        ("Persistence", "src/ClientIntake/ClientIntake.csproj"),
        ("Persistence", "src/TaxCases/TaxCases.csproj"),
        ("TaxAI.Portal", "src/ClientIntake/ClientIntake.csproj"),

        // Spec 015 T06 — accountant extraction-review coordination and the
        // deterministic Development adapter.
        ("TaxAI.Portal", "src/DocumentIntelligence/DocumentIntelligence.csproj"),
        ("TaxAI.Portal", "src/ExtractionWorkflow/ExtractionWorkflow.csproj"),

        // Spec 015 T03 — the extraction store's contracts and storage records.
        ("Persistence", "src/ExtractionWorkflow/ExtractionWorkflow.csproj"),

        // Spec 016 T01 — the production admission gate consumes the Azure
        // infrastructure library's validator and identity probe.
        ("TaxAI.Portal", "src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj"),
    ];

    /// <summary>
    /// Namespaces whose types may appear in a baseline assembly after the
    /// baseline was taken, each approved by the specification that added them.
    /// The baseline's own signatures stay compared byte-for-byte; this governs
    /// only what may be added beside them.
    /// </summary>
    private static readonly string[] ApprovedPostBaselinePersistencePrefixes =
    [
        // Spec 014 — client intake metadata.
        "Persistence.Configurations.Intake",
        "Persistence.Entities.Intake",
        "Persistence.Migrations.AddClientIntake",
        "Persistence.Repositories.SqlIntakeStore",

        // Spec 015 T03 — append-only extraction persistence.
        "Persistence.Configurations.Extraction",
        "Persistence.Configurations.MedicalEvidence",
        "Persistence.Entities.Extraction",
        "Persistence.Entities.MedicalEvidence",
        "Persistence.Migrations.AddDocumentExtraction",
        "Persistence.Repositories.SqlExtractionStore",

        // Spec 016 T04 — the consumed source registry, named exactly.
        "Persistence.Configurations.SourceRegistrationConfiguration",
        "Persistence.Entities.SourceRegistrationEntity",
        "Persistence.Migrations.AddSourceRegistry",
        "Persistence.Repositories.SqlSourceRegistryStore",

        // Spec 016 T06 — the recovered database's reconciler, named exactly. It
        // adds no entity, configuration or migration.
        "Persistence.Services.SqlRecoveryReconciler",
    ];

    /// <summary>
    /// Spec 016 T01 — the production-control vocabulary, evidence shapes,
    /// identity-probe port and evidence policy added beside Tenancy's baseline.
    /// Each type is named exactly; the baseline signatures stay compared
    /// byte-for-byte.
    /// </summary>
    private static readonly string[] ApprovedPostBaselineTenancyTypes =
    [
        "Tenancy.Contracts.IManagedIdentityBindingProbe",
        "Tenancy.Contracts.ManagedIdentityBinding",
        "Tenancy.Contracts.ManagedIdentityBindingOutcome",
        "Tenancy.Contracts.ProductionApproval",
        "Tenancy.Contracts.ProductionEvidenceManifest",
        "Tenancy.Contracts.ProductionReleaseBinding",
        "Tenancy.Models.ProductionAdmissionRefusal",
        "Tenancy.Models.ProductionApprovalRole",
        "Tenancy.Models.ProductionEvidenceItem",
        "Tenancy.Models.ProductionEvidenceStatus",
        "Tenancy.Models.ProductionEvidenceSubject",
        "Tenancy.Models.ProductionReadiness",
        "Tenancy.Models.ProviderAssurance",
        "Tenancy.Models.ProviderAssuranceSubject",
        "Tenancy.Services.ProductionEvidencePolicy",

        // Spec 016 T06 — the consumed recovery control ledger, evidence reader
        // and reconciler ports, their structural models and the pure policy.
        "Tenancy.Contracts.IProductionEvidenceReader",
        "Tenancy.Contracts.IRecoveryControlStore",
        "Tenancy.Contracts.IRecoveryReconciler",
        "Tenancy.Contracts.ProductionEvidenceDocument",
        "Tenancy.Contracts.RecoveryAuthorizationAttestation",
        "Tenancy.Contracts.RecoveryAuthorizedMembership",
        "Tenancy.Contracts.RecoveryControlRecord",
        "Tenancy.Contracts.RecoveryControlSnapshot",
        "Tenancy.Models.DatabaseIncarnation",
        "Tenancy.Models.ProductionEvidenceDocumentStatus",
        "Tenancy.Models.RecoveredDataExtent",
        "Tenancy.Models.RecoveryAccessInvalidation",
        "Tenancy.Models.RecoveryControlAppendOutcome",
        "Tenancy.Models.RecoveryControlReadStatus",
        "Tenancy.Models.RecoveryControlRecordKind",
        "Tenancy.Models.RecoveryMembershipReconciliation",
        "Tenancy.Models.RecoveryQuarantineReason",
        "Tenancy.Models.RecoveryReleaseFacts",
        "Tenancy.Models.RecoveryReleasePolicy",
        "Tenancy.Models.RecoveryReplayOutcome",
        "Tenancy.Models.RecoveryRetentionWindow",
    ];

    /// <summary>
    /// Spec 016 T01 — the four existing startup-guard predicates the production
    /// admission gate shares, now internal rather than private. Named per method
    /// so no other PortalStartupGuards addition is approved.
    /// </summary>
    private static readonly string[] ApprovedPostBaselineStartupGuardMethods =
    [
        "HasDurableSessionStore",
        "HasRegisteredDurableKeyRing",
        "RunsInDevelopment",
        "RunsInProcessTestServer",

        // Spec 016 T03: the configured-key-ring predicate that admits the
        // production profile's protected Azure key ring in Production.
        "HasConfiguredKeyRing",

        // Spec 016 T06: the durable recovery-reconciler predicate the admission
        // gate applies, decided by implementing assembly like the session store.
        "HasDurableRecoveryReconciler",
    ];

    /// <summary>
    /// Spec 016 T03: the one internal composition method added beside the
    /// unchanged <c>AddTaxAiModules</c> signature. It takes the production
    /// runtime-material loader explicitly, so tests never contact a vault. Named
    /// exactly, so no other ModuleRegistration addition is approved.
    /// </summary>
    private static readonly string[] ApprovedPostBaselineCompositionMethods =
    [
        "RegisterTaxAiModules",
    ];

    [Fact]
    public void RepositoryGraphMigrationBytesAndFloorsMatchTheApprovedBaseline()
    {
        var actual = CaptureRepositoryContract();
        var path = Path.Combine(BaselineRoot, "repository-contract.json");

        var baseline = Read<RepositoryContract>(path);

        Assert.Equal(BaselineCommit, baseline.BaselineCommit);
        Assert.Equal("TaxAI.sln", baseline.Solution);
        Assert.Equal(3172, baseline.TotalPassedTests);
        Assert.Equal(11, baseline.WarningBudget.Values.Sum());

        var solution = File.ReadAllText(
            Path.Combine(RepositoryRoot, baseline.Solution));

        foreach (var project in baseline.Projects)
        {
            Assert.Equal(
                1,
                CountOccurrences(
                    solution,
                    $"\"{project.Path.Replace('/', '\\')}\""));

            var current = Assert.Single(
                actual.Projects,
                candidate =>
                    string.Equals(
                        candidate.Path,
                        project.Path,
                        StringComparison.Ordinal));

            Assert.Equal(project.Name, current.Name);
            Assert.Equal(project.AssemblyName, current.AssemblyName);
            // Every reference the baseline recorded is still declared. This is
            // the half that must never loosen: a baseline reference that
            // disappeared would be a dependency the upgrade quietly dropped.
            Assert.All(
                project.ProjectReferences,
                reference => Assert.Contains(reference, current.ProjectReferences));

            var additiveReferences = current.ProjectReferences
                .Except(project.ProjectReferences, StringComparer.Ordinal)
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToArray();

            // Each post-baseline reference is approved individually rather than
            // as an exact set. The exact spelling meant every later approved
            // module change failed a historical .NET 10 baseline test that has
            // nothing to say about it; naming each one keeps the review — an
            // unapproved reference still fails here — without freezing the
            // graph against additions nobody has proposed yet.
            Assert.All(
                additiveReferences,
                reference => Assert.Contains(
                    (project.Name, reference),
                    ApprovedPostBaselineReferences));
        }

        foreach (var (name, floor) in baseline.TestFloors)
        {
            Assert.True(
                actual.TestFloors.TryGetValue(name, out var currentFloor),
                $"{name} is missing from scripts/test-projects.json.");
            Assert.True(
                currentFloor >= floor,
                $"{name} floor fell from {floor} to {currentFloor}.");
        }

        Assert.Equal(
            baseline.MigrationSha256
                .Where(entry => entry.Key.Contains(
                    "InitialPersistence",
                    StringComparison.Ordinal))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal),
            actual.MigrationSha256
                .Where(entry => entry.Key.Contains(
                    "InitialPersistence",
                    StringComparison.Ordinal))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal));

        foreach (var absentPath in baseline.PathsThatMustRemainAbsent)
        {
            Assert.False(
                File.Exists(Path.Combine(RepositoryRoot, absentPath)) ||
                Directory.Exists(Path.Combine(RepositoryRoot, absentPath)),
                $"{absentPath} must not be introduced by the platform upgrade.");
        }
    }

    [Fact]
    public void UserDeclaredPublicAndInternalContractsMatchTheApprovedBaseline()
    {
        var path = Path.Combine(BaselineRoot, "public-contracts.json");

        var baseline = Read<PublicContractBaseline>(path);

        Assert.Equal(BaselineCommit, baseline.BaselineCommit);

        // The spec 013 baseline records the assemblies that existed when it was
        // taken, and it is an immutable historical artifact. A module added by a
        // later spec is captured and named here instead of being written into it.
        //
        // Replaces spec 013's "the assembly name sets are equal", superseded by
        // spec 014 T01. Every baseline assembly's signatures, friend assemblies,
        // and embedded resources are still compared byte-for-byte below, so the
        // historical guarantee is untouched.
        var baselineNames = baseline.Assemblies
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        var actual = CapturePublicContracts(baselineNames);

        Assert.Equal(
            baseline.Assemblies.Select(assembly => assembly.Name),
            actual.Assemblies.Select(assembly => assembly.Name));

        // Each post-baseline module is asserted individually, exactly once at its
        // approved path.
        //
        // Spec 014 T01 wrote this as one exact set — "the modules added since the
        // baseline are exactly [ClientIntake]" — which reads as a tighter rule but
        // is the closed-set trap this repository has now hit three times: it makes
        // every approved future module break a historical .NET 10 baseline test
        // that has nothing to say about it. Spec 015 states the rule directly: a
        // repository test asserts its own projects exactly once at their approved
        // paths and never a closed total. Each entry below is one reviewable line,
        // added by the spec that introduced the module, and an unapproved module
        // is caught where approval actually lives — the toolchain preflight's
        // closed registry, which no project may skip.
        var solutionText = File.ReadAllText(Path.Combine(RepositoryRoot, "TaxAI.sln"));
        var productionProjects = ProductionProjectNames();

        foreach (var (module, projectPath) in new[]
                 {
                     ("ClientIntake", "src/ClientIntake/ClientIntake.csproj"),
                     ("ExtractionWorkflow", "src/ExtractionWorkflow/ExtractionWorkflow.csproj"),
                     ("DocumentIntelligence", "src/DocumentIntelligence/DocumentIntelligence.csproj"),
                     ("TaxAI.AzureInfrastructure", "src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj"),
                 })
        {
            // Genuinely later than the baseline: a module named here must not be
            // one the baseline already froze, or this list would quietly become a
            // way to exempt a baseline assembly from the comparison above.
            Assert.DoesNotContain(module, baselineNames);

            Assert.Equal(
                1,
                productionProjects.Count(candidate =>
                    string.Equals(candidate, module, StringComparison.Ordinal)));

            Assert.Equal(
                1,
                CountOccurrences(solutionText, $"\"{projectPath.Replace('/', '\\')}\""));
        }

        // The baseline's own production projects are all still declared. Open,
        // not closed: it fails if one disappeared, never because one was added.
        Assert.All(
            baseline.Assemblies.Select(assembly => assembly.Name),
            name => Assert.Contains(name, productionProjects));

        foreach (var expected in baseline.Assemblies)
        {
            var current = Assert.Single(
                actual.Assemblies,
                assembly =>
                    string.Equals(
                        assembly.Name,
                        expected.Name,
                        StringComparison.Ordinal));

            Assert.Equal(expected.FriendAssemblies, current.FriendAssemblies);
            Assert.Equal(expected.EmbeddedResources, current.EmbeddedResources);

            if (string.Equals(expected.Name, "Persistence", StringComparison.Ordinal))
            {
                Assert.All(
                    expected.Signatures,
                    signature => Assert.Contains(signature, current.Signatures));
                var persistenceAdditions = current.Signatures
                    .Except(expected.Signatures, StringComparer.Ordinal)
                    .ToArray();
                Assert.NotEmpty(persistenceAdditions);

                // Each approved namespace is one reviewable entry. An addition
                // outside them — a leaked identity type, a second store, a
                // review-model change — still fails here.
                Assert.All(
                    persistenceAdditions,
                    signature => Assert.True(
                        ApprovedPostBaselinePersistencePrefixes.Any(prefix =>
                            signature.Contains(prefix, StringComparison.Ordinal)),
                        $"Unapproved Persistence addition: {signature}"));
                continue;
            }

            if (string.Equals(expected.Name, "Tenancy", StringComparison.Ordinal))
            {
                // Every baseline Tenancy signature survives unchanged, and each
                // addition belongs to exactly one approved spec 016 type.
                Assert.All(
                    expected.Signatures,
                    signature => Assert.Contains(signature, current.Signatures));
                var tenancyAdditions = current.Signatures
                    .Except(expected.Signatures, StringComparer.Ordinal)
                    .ToArray();
                Assert.NotEmpty(tenancyAdditions);
                Assert.All(
                    tenancyAdditions,
                    signature => Assert.True(
                        ApprovedPostBaselineTenancyTypes.Any(type =>
                            signature.Contains($"|{type}|", StringComparison.Ordinal) ||
                            signature.EndsWith($"|{type}", StringComparison.Ordinal) ||
                            signature.Contains($"|{type}|base=", StringComparison.Ordinal)),
                        $"Unapproved Tenancy addition: {signature}"));
                continue;
            }

            if (!string.Equals(expected.Name, "TaxAI.Portal", StringComparison.Ordinal))
            {
                Assert.Equal(expected.Signatures, current.Signatures);
                continue;
            }

            var historicalShell = Assert.Single(
                expected.Signatures,
                signature =>
                    signature.StartsWith(
                        "F|TaxAI.Portal.PortalContent|",
                        StringComparison.Ordinal) &&
                    signature.Contains("|ShellHtml|", StringComparison.Ordinal));
            var currentShell = Assert.Single(
                current.Signatures,
                signature =>
                    signature.StartsWith(
                        "F|TaxAI.Portal.PortalContent|",
                        StringComparison.Ordinal) &&
                    signature.Contains("|ShellHtml|", StringComparison.Ordinal));

            Assert.NotEqual(historicalShell, currentShell);

            var unchangedHistoricalSignatures = expected.Signatures
                .Except(new[] { historicalShell }, StringComparer.Ordinal)
                .ToArray();
            Assert.All(
                unchangedHistoricalSignatures,
                signature => Assert.Contains(signature, current.Signatures));

            var additiveSignatures = current.Signatures
                .Except(unchangedHistoricalSignatures, StringComparer.Ordinal)
                .Except(new[] { currentShell }, StringComparer.Ordinal)
                .ToArray();

            // Replaces spec 013's "exactly three additive signatures, all internal
            // and all on ReviewEndpoints", superseded by spec 014 T01: the Blazor
            // foundation's components and page models are necessarily public,
            // because the Razor compiler emits components as public types and a
            // component parameter must be public to be settable.
            //
            // The replacement is a namespace allow-list rather than a count, and it
            // is strictly stronger than dropping the rule: every additive signature
            // must belong to one of the named review/component namespaces, so an
            // additive public type anywhere else in the host — a new endpoint, a new
            // guard, a leaked identity or persistence type — still fails here.
            Assert.NotEmpty(additiveSignatures);

            var approvedAdditivePrefixes = new[]
            {
                "TaxAI.Portal.ReviewEndpoints",
                "TaxAI.Portal.Components.App",
                "TaxAI.Portal.Components.Routes",
                "TaxAI.Portal.Components._Imports",
                "TaxAI.Portal.Components.Layout.PortalLayout",
                "TaxAI.Portal.Components.Pages.ReviewPage",
                "TaxAI.Portal.Components.Pages.ReviewPageModel",
                "TaxAI.Portal.Components.Pages.ReviewSlipModel",
                "TaxAI.Portal.Components.Pages.ReviewBoxModel",
                "TaxAI.Portal.Components.Pages.ReviewLineModel",
                "TaxAI.Portal.Components.Pages.ClientIntakePage",
                "TaxAI.Portal.Components.Pages.ClientLinkConfirmationPage",
                "TaxAI.Portal.Components.Pages.IntakeCaseCreatePage",
                "TaxAI.Portal.Components.Pages.IntakeCaseIssuedPage",
                "TaxAI.Portal.Components.Pages.IntakeCaseProgressPage",
                "TaxAI.Portal.Components.Pages.DuplicateCandidateModel",
                "TaxAI.Portal.Components.Pages.Extraction",
                "TaxAI.Portal.Components.Pages.MedicalEvidencePanelModel",
                "TaxAI.Portal.Components.Pages.T4BoxDisplayModel",
                "TaxAI.Portal.Components.Pages.T4ExtractionPanelModel",
                "TaxAI.Portal.Components.Shared.ClassificationDisagreementNotice",
                "TaxAI.Portal.Components.Shared.ClientQuestionnairePanel",
                "TaxAI.Portal.Components.Shared.DuplicateCandidateNotice",
                "TaxAI.Portal.Components.Shared.ExpectedDocumentChecklist",
                "TaxAI.Portal.Components.Shared.ExtractionFieldTable",
                "TaxAI.Portal.Components.Shared.IntakeStatusBadge",
                "TaxAI.Portal.Components.Shared.MedicalEvidencePanel",
                "TaxAI.Portal.ClientIntakeCookie",
                "TaxAI.Portal.ClientIntakeEndpoints",
                "TaxAI.Portal.CryptographicIntakeCredentialSource",
                "TaxAI.Portal.DevelopmentEncryptedIntakeContentStore",
                "TaxAI.Portal.ExtractionConfiguration",
                "TaxAI.Portal.ExtractionEndpoints",
                "TaxAI.Portal.ExtractionRuntimeConfiguration",
                "TaxAI.Portal.IntakeConfiguration",
                "TaxAI.Portal.IntakeEndpoints",
                "TaxAI.Portal.IntakeHttpSecurity",
                "TaxAI.Portal.IntakeRateConfiguration",
                "TaxAI.Portal.IntakeRuntimeConfiguration",

                // Spec 016 T01 — the production profile reader and admission gate.
                "TaxAI.Portal.Production.ExtractionAvailabilityGate",
                "TaxAI.Portal.Production.ProductionAdmissionGate",
                "TaxAI.Portal.Production.ProductionConfiguration",

                // Spec 016 T02 — the fixed single-hop trusted ingress policy.
                "TaxAI.Portal.Production.TrustedIngressConfiguration",

                // Spec 016 T06 — startup recovery orchestration and its closed result.
                "TaxAI.Portal.Production.RecoveryOrchestrator",
                "TaxAI.Portal.Production.RecoveryOrchestration",

                // SDK-generated, not user-declared. Referencing the Razor component
                // model makes the toolchain embed these marker attributes into the
                // assembly; no source file in this repository declares either, and
                // neither carries behaviour.
                "Microsoft.CodeAnalysis.EmbeddedAttribute",
                "Microsoft.Extensions.Validation.Embedded.ValidatableTypeAttribute",
                "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=9",
            };

            Assert.All(
                additiveSignatures,
                signature => Assert.True(
                    approvedAdditivePrefixes.Any(prefix =>
                        signature.Contains(prefix, StringComparison.Ordinal)) ||
                    IsApprovedStartupGuardMethod(signature) ||
                    IsApprovedCompositionMethod(signature),
                    $"Unapproved additive signature: {signature}"));

            // The endpoint's own additions stay internal; only the component tree
            // is public.
            Assert.All(
                additiveSignatures.Where(signature => signature.Contains(
                    "TaxAI.Portal.ReviewEndpoints",
                    StringComparison.Ordinal)),
                signature => Assert.DoesNotContain(
                    "|public|",
                    signature,
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void BaselineRecordsTheExactPreUpgradeVersionsWithoutEnforcingThem()
    {
        var baseline = Read<RepositoryContract>(
            Path.Combine(BaselineRoot, "repository-contract.json"));

        Assert.All(
            baseline.Projects,
            project => Assert.Equal(new[] { "net8.0" }, project.TargetFrameworks));

        Assert.Contains(
            baseline.Projects,
            project =>
                project.Path == "src/TaxAI.Portal/TaxAI.Portal.csproj" &&
                project.PackageReferences.Single().Version == "8.0.0");

        Assert.Contains(
            baseline.Projects,
            project =>
                project.Path == "src/Persistence/Persistence.csproj" &&
                project.PackageReferences.All(package => package.Version == "8.0.0"));

        // These values are evidence of the rollback baseline. Later tasks
        // intentionally replace them, so the executable invariants above do not
        // compare current TFMs or package versions to this historical record.
        Assert.All(
            baseline.Projects.Where(project => project.Kind == "test"),
            project =>
            {
                Assert.Contains(
                    project.PackageReferences,
                    package =>
                        package.Name == "Microsoft.NET.Test.Sdk" &&
                        package.Version == "17.8.0");
                Assert.Contains(
                    project.PackageReferences,
                    package =>
                        package.Name == "xunit" &&
                        package.Version == "2.5.3");
            });
    }

    [Fact]
    public void ToolchainManifestsPinOnlyExactStableApprovedVersions()
    {
        using var globalJson = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot, "global.json")));
        var globalRoot = globalJson.RootElement;

        Assert.Equal(
            new[] { "sdk" },
            globalRoot.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        var sdk = globalRoot.GetProperty("sdk");
        Assert.Equal(
            new[] { "allowPrerelease", "rollForward", "version" },
            sdk.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        var sdkVersion = sdk.GetProperty("version").GetString();
        Assert.Equal("10.0.302", sdkVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+$", sdkVersion!);
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        using var toolManifest = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    RepositoryRoot,
                    ".config",
                    "dotnet-tools.json")));
        var manifestRoot = toolManifest.RootElement;

        Assert.Equal(
            new[] { "isRoot", "tools", "version" },
            manifestRoot.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(1, manifestRoot.GetProperty("version").GetInt32());
        Assert.True(manifestRoot.GetProperty("isRoot").GetBoolean());

        var tools = manifestRoot.GetProperty("tools")
            .EnumerateObject()
            .ToArray();
        var dotnetEf = Assert.Single(tools);
        Assert.Equal("dotnet-ef", dotnetEf.Name);
        Assert.Equal(
            new[] { "commands", "rollForward", "version" },
            dotnetEf.Value.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        var toolVersion = dotnetEf.Value.GetProperty("version").GetString();
        Assert.Equal("10.0.10", toolVersion);
        Assert.Matches(@"^\d+\.\d+\.\d+$", toolVersion!);
        Assert.Equal(
            new[] { "dotnet-ef" },
            dotnetEf.Value.GetProperty("commands")
                .EnumerateArray()
                .Select(command => command.GetString()));
        Assert.False(dotnetEf.Value.GetProperty("rollForward").GetBoolean());
    }

    [Fact]
    public async Task PinnedSdkAndEfToolRestoreAndResolveAtExactVersions()
    {
        var sdkVersion = await RunDotnetAsync("--version");
        AssertCommandSucceeded("dotnet --version", sdkVersion);
        Assert.Equal("10.0.302", sdkVersion.StandardOutput.Trim());

        var restore = await RunDotnetAsync("tool", "restore");
        AssertCommandSucceeded("dotnet tool restore", restore);

        var toolVersion = await RunDotnetAsync(
            "tool",
            "run",
            "dotnet-ef",
            "--version");
        AssertCommandSucceeded(
            "dotnet tool run dotnet-ef --version",
            toolVersion);

        var versionLines = toolVersion.StandardOutput
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(
                '\n',
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("10.0.10", versionLines[^1]);
    }

    private static RepositoryContract CaptureRepositoryContract()
    {
        var solutionPath = Path.Combine(RepositoryRoot, "TaxAI.sln");
        var projects = SolutionProjects(solutionPath)
            .Select(entry => ProjectContract(entry.Name, entry.Path))
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ToArray();

        var manifestPath = Path.Combine(
            RepositoryRoot,
            "scripts",
            "test-projects.json");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var floors = manifest.RootElement
            .GetProperty("projects")
            .EnumerateArray()
            .ToDictionary(
                element => element.GetProperty("name").GetString()!,
                element => element.GetProperty("minimumTests").GetInt32(),
                StringComparer.Ordinal);

        var migrationRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Persistence",
            "Migrations");

        var migrationHashes = Directory
            .EnumerateFiles(migrationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => RelativePath(path),
                Sha256,
                StringComparer.Ordinal);

        return new RepositoryContract(
            BaselineCommit,
            "2026-07-29",
            "TaxAI.sln",
            projects,
            floors,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["TaxLogicEngine.Tests"] = 261,
                ["TaxCases.Tests"] = 337,
                ["DocumentExtraction.Tests"] = 673,
                ["CaseProcessing.Tests"] = 594,
                ["CaseReview.Tests"] = 482,
                ["TaxAI.Portal.Tests"] = 403,
                ["Tenancy.Tests"] = 289,
                ["Persistence.Tests"] = 133,
            },
            3172,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["CS0649"] = 2,
                ["xUnit2008"] = 2,
                ["xUnit1031"] = 6,
                ["xUnit2013"] = 1,
            },
            migrationHashes,
            [
                ".github",
                "Directory.Build.props",
                "Directory.Build.targets",
                "Directory.Packages.props",
                "Dockerfile",
                "docker-compose.yml",
                "azure-pipelines.yml",
            ]);
    }

    private static ProjectBaseline ProjectContract(string name, string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var absolutePath = Path.Combine(
            RepositoryRoot,
            normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        var document = XDocument.Load(absolutePath);

        var targetFrameworks = Elements(document, "TargetFramework")
            .Concat(
                Elements(document, "TargetFrameworks")
                    .SelectMany(value => value.Split(
                        ';',
                        StringSplitOptions.TrimEntries |
                        StringSplitOptions.RemoveEmptyEntries)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var projectReferences = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")!.Value)
            .Select(reference => Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(absolutePath)!, reference)))
            .Select(RelativePath)
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

        var packages = document
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => new PackageBaseline(
                element.Attribute("Include")!.Value,
                element.Attribute("Version")?.Value ??
                    element.Elements().Single(child =>
                        child.Name.LocalName == "Version").Value,
                element.Attribute("PrivateAssets")?.Value ??
                    element.Elements().SingleOrDefault(child =>
                        child.Name.LocalName == "PrivateAssets")?.Value))
            .OrderBy(package => package.Name, StringComparer.Ordinal)
            .ToArray();

        var assemblyName =
            Elements(document, "AssemblyName").SingleOrDefault() ??
            Path.GetFileNameWithoutExtension(absolutePath);

        return new ProjectBaseline(
            name,
            normalizedPath,
            normalizedPath.StartsWith("tests/", StringComparison.Ordinal)
                ? "test"
                : "production",
            assemblyName,
            targetFrameworks,
            projectReferences,
            packages);
    }

    /// <summary>
    /// Every production project the solution declares, by name.
    /// </summary>
    private static string[] ProductionProjectNames() =>
        SolutionProjects(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Where(project =>
                project.Path.StartsWith("src", StringComparison.Ordinal) &&
                project.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(project => project.Name)
            .ToArray();

    /// <summary>
    /// Captures the public and internal contract of exactly the assemblies the
    /// baseline records.
    /// </summary>
    /// <remarks>
    /// Scoped rather than exhaustive on purpose. A later spec's module is not in
    /// the spec 013 baseline, and loading it here would also fail outright: this
    /// test assembly does not reference that module's own dependencies, so
    /// resolving its types against the default load context throws.
    /// </remarks>
    private static PublicContractBaseline CapturePublicContracts(
        IReadOnlySet<string> baselineAssemblyNames)
    {
        var projects = SolutionProjects(Path.Combine(RepositoryRoot, "TaxAI.sln"))
            .Where(project =>
                project.Path.StartsWith("src", StringComparison.Ordinal) &&
                project.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                baselineAssemblyNames.Contains(project.Name))
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var assemblies = new List<AssemblyContract>();

        foreach (var project in projects)
        {
            var projectPath = Path.Combine(RepositoryRoot, project.Path);
            var document = XDocument.Load(projectPath);
            var targetFramework =
                Elements(document, "TargetFramework").Single();
            var assemblyName =
                Elements(document, "AssemblyName").SingleOrDefault() ??
                Path.GetFileNameWithoutExtension(projectPath);
            var assemblyPath = Path.Combine(
                Path.GetDirectoryName(projectPath)!,
                "bin",
                configuration,
                targetFramework,
                $"{assemblyName}.dll");

            Assert.True(
                File.Exists(assemblyPath),
                $"{assemblyPath} was not found. Build TaxAI.sln before running this contract test.");

            var assembly = AssemblyLoadContext.Default
                .LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

            var friendAssemblies = assembly
                .GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName.Split(',')[0])
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var resources = assembly
                .GetManifestResourceNames()
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var signatures = assembly
                .DefinedTypes
                .Where(IncludeType)
                .SelectMany(TypeSignatures)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            assemblies.Add(
                new AssemblyContract(
                    assembly.GetName().Name!,
                    friendAssemblies,
                    resources,
                    signatures));
        }

        return new PublicContractBaseline(
            BaselineCommit,
            assemblies
                .OrderBy(assembly => assembly.Name, StringComparer.Ordinal)
                .ToArray());
    }

    private static IEnumerable<string> TypeSignatures(TypeInfo type)
    {
        yield return
            $"T|{Accessibility(type)}|{TypeShape(type)}|{TypeName(type)}" +
            $"|base={TypeName(type.BaseType)}" +
            $"|interfaces={string.Join(',', type.ImplementedInterfaces.Select(TypeName).OrderBy(value => value, StringComparer.Ordinal))}" +
            $"|generic={TypeGenericConstraints(type)}" +
            $"|attributes={Attributes(type)}";

        foreach (var constructor in type.DeclaredConstructors
                     .Where(IncludeMethodBase))
        {
            yield return
                $"C|{TypeName(type)}|{Accessibility(constructor)}" +
                $"|({Parameters(constructor.GetParameters())})" +
                $"|attributes={Attributes(constructor)}";
        }

        foreach (var field in type.DeclaredFields
                     .Where(IncludeField))
        {
            var constant = field.IsLiteral
                ? $"|constant={InvariantValue(field.GetRawConstantValue())}"
                : string.Empty;
            yield return
                $"F|{TypeName(type)}|{Accessibility(field)}" +
                $"|{MemberShape(field)}|{TypeName(field.FieldType)}|{field.Name}" +
                $"{constant}|attributes={Attributes(field)}";
        }

        foreach (var property in type.DeclaredProperties
                     .Where(IncludeProperty))
        {
            yield return
                $"P|{TypeName(type)}|{TypeName(property.PropertyType)}|{property.Name}" +
                $"|index=({Parameters(property.GetIndexParameters())})" +
                $"|get={AccessorAccessibility(property.GetMethod)}" +
                $"|set={AccessorAccessibility(property.SetMethod)}" +
                $"|attributes={Attributes(property)}";
        }

        foreach (var @event in type.DeclaredEvents
                     .Where(IncludeEvent))
        {
            yield return
                $"E|{TypeName(type)}|{TypeName(@event.EventHandlerType)}|{@event.Name}" +
                $"|add={AccessorAccessibility(@event.AddMethod)}" +
                $"|remove={AccessorAccessibility(@event.RemoveMethod)}" +
                $"|attributes={Attributes(@event)}";
        }

        foreach (var method in type.DeclaredMethods
                     .Where(IncludeMethod))
        {
            yield return
                $"M|{TypeName(type)}|{Accessibility(method)}" +
                $"|{MemberShape(method)}|{TypeName(method.ReturnType)}" +
                $"|{method.Name}{GenericSignature(method)}" +
                $"|({Parameters(method.GetParameters())})" +
                $"|attributes={Attributes(method)}";
        }
    }

    private static bool IsApprovedStartupGuardMethod(string signature) =>
        signature.StartsWith("M|TaxAI.Portal.PortalStartupGuards|internal|", StringComparison.Ordinal) &&
        ApprovedPostBaselineStartupGuardMethods.Any(method =>
            signature.Contains($"|{method}|(", StringComparison.Ordinal));

    private static bool IsApprovedCompositionMethod(string signature) =>
        signature.StartsWith("M|TaxAI.Portal.ModuleRegistration|internal|static|", StringComparison.Ordinal) &&
        ApprovedPostBaselineCompositionMethods.Any(method =>
            signature.Contains($"|{method}|(", StringComparison.Ordinal));

    private static bool IncludeType(TypeInfo type) =>
        !type.IsNestedPrivate &&
        !HasCompilerGeneratedAttribute(type);

    private static bool IncludeMethod(MethodInfo method) =>
        IncludeMethodBase(method) &&
        !(method.IsSpecialName &&
          (method.Name.StartsWith("get_", StringComparison.Ordinal) ||
           method.Name.StartsWith("set_", StringComparison.Ordinal) ||
           method.Name.StartsWith("add_", StringComparison.Ordinal) ||
           method.Name.StartsWith("remove_", StringComparison.Ordinal)));

    private static bool IncludeMethodBase(MethodBase method) =>
        !method.IsPrivate &&
        !HasCompilerGeneratedAttribute(method);

    private static bool IncludeField(FieldInfo field) =>
        !field.IsPrivate &&
        !HasCompilerGeneratedAttribute(field);

    private static bool IncludeProperty(PropertyInfo property) =>
        (property.GetMethod is { IsPrivate: false } ||
         property.SetMethod is { IsPrivate: false }) &&
        !HasCompilerGeneratedAttribute(property);

    private static bool IncludeEvent(EventInfo @event) =>
        (@event.AddMethod is { IsPrivate: false } ||
         @event.RemoveMethod is { IsPrivate: false }) &&
        !HasCompilerGeneratedAttribute(@event);

    private static bool HasCompilerGeneratedAttribute(MemberInfo member) =>
        member.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType == typeof(CompilerGeneratedAttribute));

    private static string TypeShape(Type type)
    {
        if (type.IsEnum)
        {
            return $"enum:{TypeName(Enum.GetUnderlyingType(type))}";
        }

        if (type.IsInterface)
        {
            return "interface";
        }

        if (type.IsValueType)
        {
            return type.IsByRefLike ? "ref-struct" : "struct";
        }

        if (typeof(MulticastDelegate).IsAssignableFrom(type.BaseType))
        {
            return "delegate";
        }

        return string.Join(
            ',',
            new[]
            {
                type.IsAbstract && type.IsSealed ? "static" : null,
                type.IsAbstract && !type.IsSealed ? "abstract" : null,
                type.IsSealed && !type.IsAbstract ? "sealed" : null,
                "class",
            }.Where(value => value is not null));
    }

    private static string MemberShape(FieldInfo field) =>
        string.Join(
            ',',
            new[]
            {
                field.IsStatic ? "static" : null,
                field.IsLiteral ? "const" : null,
                field.IsInitOnly ? "readonly" : null,
            }.Where(value => value is not null));

    private static string MemberShape(MethodInfo method) =>
        string.Join(
            ',',
            new[]
            {
                method.IsStatic ? "static" : null,
                method.IsAbstract ? "abstract" : null,
                method.IsVirtual && !method.IsAbstract ? "virtual" : null,
                method.GetBaseDefinition() != method ? "override" : null,
            }.Where(value => value is not null));

    private static string Accessibility(Type type)
    {
        if (!type.IsNested)
        {
            return type.IsPublic ? "public" : "internal";
        }

        if (type.IsNestedPublic) return "public";
        if (type.IsNestedFamily) return "protected";
        if (type.IsNestedAssembly) return "internal";
        if (type.IsNestedFamORAssem) return "protected-internal";
        if (type.IsNestedFamANDAssem) return "private-protected";
        return "private";
    }

    private static string Accessibility(MethodBase method)
    {
        if (method.IsPublic) return "public";
        if (method.IsFamily) return "protected";
        if (method.IsAssembly) return "internal";
        if (method.IsFamilyOrAssembly) return "protected-internal";
        if (method.IsFamilyAndAssembly) return "private-protected";
        return "private";
    }

    private static string Accessibility(FieldInfo field)
    {
        if (field.IsPublic) return "public";
        if (field.IsFamily) return "protected";
        if (field.IsAssembly) return "internal";
        if (field.IsFamilyOrAssembly) return "protected-internal";
        if (field.IsFamilyAndAssembly) return "private-protected";
        return "private";
    }

    private static string AccessorAccessibility(MethodInfo? accessor) =>
        accessor is null ? "none" : Accessibility(accessor);

    private static string Parameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(
            ',',
            parameters.Select(parameter =>
                $"{ParameterModifier(parameter)}{TypeName(parameter.ParameterType)} {parameter.Name}" +
                (parameter.HasDefaultValue
                    ? $"={InvariantValue(parameter.DefaultValue)}"
                    : string.Empty)));

    private static string ParameterModifier(ParameterInfo parameter)
    {
        if (parameter.GetCustomAttributesData().Any(attribute =>
                attribute.AttributeType.FullName ==
                "System.Runtime.CompilerServices.IsReadOnlyAttribute"))
        {
            return "in ";
        }

        if (parameter.IsOut)
        {
            return "out ";
        }

        if (parameter.ParameterType.IsByRef)
        {
            return "ref ";
        }

        return string.Empty;
    }

    private static string GenericSignature(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition)
        {
            return string.Empty;
        }

        return
            $"<{string.Join(',', method.GetGenericArguments().Select(argument => argument.Name))}>" +
            $" where {string.Join(';', method.GetGenericArguments().Select(GenericConstraint))}";
    }

    private static string TypeGenericConstraints(Type type) =>
        type.IsGenericTypeDefinition
            ? string.Join(
                ';',
                type.GetGenericArguments().Select(GenericConstraint))
            : "<none>";

    private static string GenericConstraint(Type parameter)
    {
        var constraints = parameter
            .GetGenericParameterConstraints()
            .Select(TypeName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var attributes = parameter.GenericParameterAttributes;

        if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
        {
            constraints.Insert(0, "class");
        }
        if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
        {
            constraints.Insert(0, "struct");
        }
        if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint))
        {
            constraints.Add("new()");
        }

        return $"{parameter.Name}:{string.Join(',', constraints)}";
    }

    private static string TypeName(Type? type)
    {
        if (type is null)
        {
            return "none";
        }

        if (type.IsByRef)
        {
            return $"{TypeName(type.GetElementType())}&";
        }

        if (type.IsPointer)
        {
            return $"{TypeName(type.GetElementType())}*";
        }

        if (type.IsArray)
        {
            return $"{TypeName(type.GetElementType())}[{new string(',', type.GetArrayRank() - 1)}]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        var definitionName = type.GetGenericTypeDefinition().FullName!;
        definitionName = definitionName[..definitionName.IndexOf('`')].Replace('+', '.');

        return
            $"{definitionName}<{string.Join(',', type.GetGenericArguments().Select(TypeName))}>";
    }

    private static string Attributes(MemberInfo member) =>
        string.Join(
            ',',
            member
                .GetCustomAttributesData()
                .Where(attribute =>
                    attribute.AttributeType != typeof(CompilerGeneratedAttribute) &&
                    attribute.AttributeType.FullName is not
                        "System.Runtime.CompilerServices.NullableAttribute" and not
                        "System.Runtime.CompilerServices.NullableContextAttribute" and not
                        "System.Diagnostics.DebuggerStepThroughAttribute" and not
                        "System.Runtime.CompilerServices.AsyncStateMachineAttribute" and not
                        "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute" and not
                        "System.Runtime.CompilerServices.IteratorStateMachineAttribute")
                .Select(AttributeSignature)
                .OrderBy(value => value, StringComparer.Ordinal));

    private static string AttributeSignature(CustomAttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments
            .Select(AttributeValue)
            .Concat(
                attribute.NamedArguments
                    .OrderBy(argument => argument.MemberName, StringComparer.Ordinal)
                    .Select(argument =>
                        $"{argument.MemberName}={AttributeValue(argument.TypedValue)}"));

        return
            $"{attribute.AttributeType.FullName}({string.Join(';', arguments)})";
    }

    private static string AttributeValue(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> values)
        {
            return $"[{string.Join(',', values.Select(AttributeValue))}]";
        }

        if (argument.Value is Type type)
        {
            return TypeName(type);
        }

        return argument.ArgumentType.IsEnum
            ? $"{TypeName(argument.ArgumentType)}:{InvariantValue(argument.Value)}"
            : InvariantValue(argument.Value);
    }

    private static string InvariantValue(object? value) =>
        value switch
        {
            null => "null",
            string text => JsonSerializer.Serialize(text),
            char character => JsonSerializer.Serialize(character),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(
                value,
                System.Globalization.CultureInfo.InvariantCulture) ?? "null",
        };

    private static (string Name, string Path)[] SolutionProjects(string solutionPath)
    {
        var projects = new List<(string Name, string Path)>();

        foreach (var line in File.ReadLines(solutionPath)
                     .Where(line => line.StartsWith("Project(\"", StringComparison.Ordinal)))
        {
            var separator = line.IndexOf(" = ", StringComparison.Ordinal);
            var values = line[(separator + 3)..]
                .Split(',')
                .Select(value => value.Trim().Trim('"'))
                .ToArray();

            if (values.Length >= 2 && values[1].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                projects.Add((values[0], values[1]));
            }
        }

        return projects.ToArray();
    }

    private static IEnumerable<string> Elements(XDocument document, string name) =>
        document
            .Descendants()
            .Where(element => element.Name.LocalName == name)
            .Select(element => element.Value);

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

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new InvalidOperationException(
                $"Could not deserialize {path}.");

    private static async Task<CommandResult> RunDotnetAsync(
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Could not start dotnet {string.Join(' ', arguments)}.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet {string.Join(' ', arguments)} exceeded two minutes.");
        }

        return new CommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static void AssertCommandSucceeded(
        string command,
        CommandResult result) =>
        Assert.True(
            result.ExitCode == 0,
            $"{command} exited {result.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{result.StandardOutput}" +
            $"stderr:{Environment.NewLine}{result.StandardError}");

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

    private sealed record RepositoryContract(
        string BaselineCommit,
        string CapturedOn,
        string Solution,
        ProjectBaseline[] Projects,
        Dictionary<string, int> TestFloors,
        Dictionary<string, int> TestTotals,
        int TotalPassedTests,
        Dictionary<string, int> WarningBudget,
        Dictionary<string, string> MigrationSha256,
        string[] PathsThatMustRemainAbsent);

    private sealed record ProjectBaseline(
        string Name,
        string Path,
        string Kind,
        string AssemblyName,
        string[] TargetFrameworks,
        string[] ProjectReferences,
        PackageBaseline[] PackageReferences);

    private sealed record PackageBaseline(
        string Name,
        string Version,
        string? PrivateAssets);

    private sealed record PublicContractBaseline(
        string BaselineCommit,
        AssemblyContract[] Assemblies);

    private sealed record AssemblyContract(
        string Name,
        string[] FriendAssemblies,
        string[] EmbeddedResources,
        string[] Signatures);

    private sealed record CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
