using System.Text.RegularExpressions;
using CaseProcessing.Services;
using DocumentExtraction.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TaxAI.Portal.Tests.Support;
using TaxLogicEngine.Services;
using Tenancy.Contracts;

namespace TaxAI.Portal.Tests;

/// <summary>
/// T02 — proves the composition root wires exactly the approved contracts, that
/// the container is validated strictly enough for a mis-wiring to fail at
/// startup rather than on first request, and that the untrusted-input ingress is
/// not registered.
/// <para>
/// The two validation flags are proved <b>behaviourally</b>, not by reading a
/// property off a builder. <c>WebApplication.CreateBuilder</c> turns
/// validate-on-build and validate-scopes on <i>in Development only</i>, and this
/// test host runs as Production — so a host that never configured them would
/// look fine to a naive assertion and ship unvalidated.
/// </para>
/// </summary>
public sealed class CompositionRootTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    /// <remarks>
    /// This suite starts a <b>Production</b> host, so per decision B-6 it must
    /// register an approved durable Data Protection provider: outside
    /// Development the host refuses to start on the framework's machine-local
    /// key-ring fallback. The registration is additive — every assertion below
    /// is unchanged.
    /// </remarks>
    public CompositionRootTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services => services.AddApprovedDurableDataProtection());
        });

    public void Dispose() => _factory.Dispose();

    // ------------------------------------------------------------------ AC-17

    [Fact]
    public void TheHostStartsInProcess() // AC-17
    {
        var exception = Record.Exception(() =>
        {
            using var client = _factory.CreateClient();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void TheHostBindsNoNetworkPort() // AC-17
    {
        using var client = _factory.CreateClient();

        // TestServer runs the whole pipeline in memory. If this were Kestrel the
        // suite would be binding a real socket, which makes tests
        // environment-dependent and puts a listener in CI.
        var server = _factory.Services.GetRequiredService<IServer>();

        Assert.IsType<TestServer>(server);
    }

    // ------------------------------------------------------------------ AC-18

    [Fact]
    public void ScopeValidationIsOn_ResolvingAScopedServiceFromTheRootProviderThrows() // AC-18
    {
        // The probe is registered by this test, never by ModuleRegistration.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<ScopedProbe>()));

        using var client = factory.CreateClient();

        Assert.Throws<InvalidOperationException>(
            () => factory.Services.GetRequiredService<ScopedProbe>());
    }

    [Fact]
    public void ValidateOnBuildIsOn_AnUnresolvableDependencyFailsAtStartup() // AC-18
    {
        // NeedsMissingDependency cannot be constructed: nothing registers
        // MissingDependency. With ValidateOnBuild off this would surface only
        // when something first asked for it — i.e. in production, on a request.
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<NeedsMissingDependency>()));

        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.NotNull(exception);
        Assert.Contains(
            nameof(MissingDependency),
            Flatten(exception!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BothValidationFlagsAreSetOutsideAnyConditionalBlock() // AC-18, spec 011 AC-31
    {
        // Replaces spec 010's "Program.cs contains no IsDevelopment at all",
        // which stopped being expressible once fixture seeding became a real
        // development-only behaviour. The replacement is stronger: the old test
        // only proved the token was absent, so a flag placed inside `if (args
        // .Length > 0)` would have passed it. This one proves the flags are set
        // at the top level of the file, whatever the branch is keyed on.
        var lines = ExecutableLines(PortalFile("Program.cs")).ToArray();

        foreach (var flag in new[] { "ValidateOnBuild = true", "ValidateScopes = true" })
        {
            var index = Array.FindIndex(
                lines,
                line => line.Contains(flag, StringComparison.Ordinal));

            Assert.True(index >= 0, $"{flag} must be set.");

            Assert.False(
                IsInsideAConditionalBlock(lines, index),
                $"{flag} must not be set inside a conditional block.");
        }

        // A preprocessor directive would move the decision to compile time,
        // where no test in this suite can see it.
        Assert.DoesNotContain(
            lines,
            line => line.StartsWith("#if", StringComparison.Ordinal) ||
                    line.StartsWith("#else", StringComparison.Ordinal));
    }

    // ------------------------------------------------------ spec 016 T01

    [Fact] // spec 016 T01, FR-14
    public void ProgramInvokesTheProductionAdmissionGateUnconditionallyAfterTheStartupGuards()
    {
        // The gate replaces the unconditional refusal, so it must be as
        // unconditional as the refusal was: one top-level call, after the
        // durable key-ring and session checks and before any route is mapped.
        var lines = ExecutableLines(PortalFile("Program.cs")).ToArray();

        var gate = Array.FindIndex(
            lines,
            line => line.StartsWith("ProductionAdmissionGate.RequireAdmission(", StringComparison.Ordinal));
        var guards = Array.FindIndex(
            lines,
            line => line.StartsWith("PortalStartupGuards.RequireSafeStartup(", StringComparison.Ordinal));
        var firstRoute = Array.FindIndex(
            lines,
            line => line.StartsWith("app.Map", StringComparison.Ordinal));

        Assert.True(gate >= 0, "Program must invoke the production admission gate.");
        Assert.Single(lines, line => line.Contains("ProductionAdmissionGate.RequireAdmission(", StringComparison.Ordinal));
        Assert.False(IsInsideAConditionalBlock(lines, gate), "The admission gate must not be conditional.");
        Assert.True(guards >= 0 && guards < gate, "The gate must follow the startup guards.");
        Assert.True(firstRoute > gate, "The gate must run before any route is mapped.");
    }

    // ------------------------------------------------------ spec 016 T02

    [Fact] // spec 016 T02, FR-02: trusted ingress is unconditional in Program.
    public void ProgramAppliesTrustedIngressUnconditionallyBeforeRouting()
    {
        // The environment decision lives inside the ingress policy, keyed on
        // Production only; Program itself never branches around it, and it runs
        // after the admission gate and before routing and rate limiting.
        var lines = ExecutableLines(PortalFile("Program.cs")).ToArray();

        var ingress = Array.FindIndex(
            lines,
            line => line.StartsWith("app.UseTrustedIngress(", StringComparison.Ordinal));
        var gate = Array.FindIndex(
            lines,
            line => line.StartsWith("ProductionAdmissionGate.RequireAdmission(", StringComparison.Ordinal));
        var routing = Array.FindIndex(
            lines,
            line => line.StartsWith("app.UseRouting(", StringComparison.Ordinal));

        Assert.True(ingress >= 0, "Program must apply trusted ingress.");
        Assert.Single(lines, line => line.Contains("UseTrustedIngress(", StringComparison.Ordinal));
        Assert.False(IsInsideAConditionalBlock(lines, ingress), "Trusted ingress must not be conditional in Program.");
        Assert.True(gate >= 0 && gate < ingress, "Trusted ingress follows the admission gate.");
        Assert.True(routing > ingress, "Trusted ingress precedes routing.");
        Assert.DoesNotContain(lines, line => line.Contains("UseForwardedHeaders", StringComparison.Ordinal));

        var policy = ExecutableLines(PortalFile(Path.Combine("Production", "TrustedIngressConfiguration.cs"))).ToArray();
        Assert.Single(policy, line => line.Contains("Environments.", StringComparison.Ordinal));
        Assert.Contains(policy, line => line.Contains("Environments.Production,", StringComparison.Ordinal));
        Assert.Single(policy, line => line.Contains("app.UseForwardedHeaders(CreateOptions())", StringComparison.Ordinal));
    }

    [Fact] // spec 016 T01: real module composition in the Production branch.
    public void TheProductionHostRegistersTheRealInfrastructureValidationBoundary()
    {
        using var client = _factory.CreateClient();

        Assert.IsType<TaxAI.AzureInfrastructure.Configuration.ProductionConfigurationValidator>(
            _factory.Services.GetRequiredService<TaxAI.AzureInfrastructure.Configuration.ProductionConfigurationValidator>());
        Assert.IsType<TaxAI.AzureInfrastructure.Configuration.ManagedIdentityBindingProbe>(
            _factory.Services.GetRequiredService<IManagedIdentityBindingProbe>());
        Assert.NotNull(_factory.Services.GetRequiredService<TaxAI.AzureInfrastructure.Configuration.AzureInfrastructureOptions>());
    }

    [Fact] // spec 016 T01: the production boundary is keyed on Production only.
    public void TheInfrastructureRegistrationIsTheOnlyProductionKeyedBranchAndEnablesNothing()
    {
        var registration = ExecutableLines(PortalFile("ModuleRegistration.cs")).ToArray();

        // Spec 016 T03: the one Production comparison is captured once, in
        // runsInProduction, and keys exactly two branches: the vault-loaded
        // intake material (before intake validation reads it) and the
        // infrastructure registration. Neither enables or disables a control.
        var production = Array.FindIndex(
            registration,
            line => line.Contains("Environments.Production,", StringComparison.Ordinal));
        Assert.True(production >= 0);
        Assert.Single(registration, line => line.Contains("Environments.Production", StringComparison.Ordinal));
        Assert.Contains("var runsInProduction = string.Equals(", registration[production - 2], StringComparison.Ordinal);

        var branches = registration
            .Select((line, index) => (line, index))
            .Where(entry => entry.line == "if (runsInProduction)")
            .Select(entry => GuardedBlock(registration, entry.index))
            .ToArray();
        Assert.Equal(2, branches.Length);

        var material = Assert.Single(branches, block => block.Contains("SupplyProductionRuntimeMaterial(", StringComparison.Ordinal));
        Assert.Contains("services.AddSingleton(material);", material, StringComparison.Ordinal);
        Assert.DoesNotContain("AddTaxAiAzureInfrastructure(", material, StringComparison.Ordinal);

        var infrastructure = Assert.Single(branches, block => block.Contains("AddTaxAiAzureInfrastructure(", StringComparison.Ordinal));

        foreach (var block in branches)
        {
            foreach (var forbidden in ControlsNoEnvironmentBranchMayDisable)
            {
                Assert.DoesNotMatch(new Regex(forbidden, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), block);
            }

            Assert.DoesNotContain("IIntakeContentStore", block, StringComparison.Ordinal);
            Assert.DoesNotContain("IDocumentIntelligenceProvider", block, StringComparison.Ordinal);
            Assert.DoesNotContain("IExtractionContentReader", block, StringComparison.Ordinal);
        }

        Assert.NotEqual(material, infrastructure);

        // Spec 016 T04: the source adapters arrive only through the infrastructure
        // extension, handed the vault-loaded fingerprint key and the validated
        // source bound; the composition root names no Azure adapter type itself.
        Assert.Contains("new SourceContentSettings(", infrastructure, StringComparison.Ordinal);
        Assert.Contains("intake.FingerprintKey", infrastructure, StringComparison.Ordinal);
        Assert.Contains("ExtractionConfiguration.ProductionMaximumSourceBytes(configuration)", infrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain(registration, line =>
            line.Contains("AzureIntakeContentStore", StringComparison.Ordinal) ||
            line.Contains("AzureSourceObjectPurger", StringComparison.Ordinal));
    }

    [Fact] // spec 016 T03: the Production host registers the approved key protection.
    public void TheProductionHostRegistersTheApprovedResourceProbe()
    {
        using var client = _factory.CreateClient();

        // Registered, not resolved: resolving it builds the managed-identity
        // credential, which only a structurally valid profile may do.
        Assert.True(_factory.Services
            .GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(TaxAI.AzureInfrastructure.Configuration.IApprovedResourceProbe)));
    }

    [Fact] // spec 016 T06: recovery runs once at startup, only in Production.
    public void TheRecoveryOrchestratorIsProductionOnlyAndNotAHostedService()
    {
        using var client = _factory.CreateClient();
        var orchestrator = typeof(Program).Assembly
            .GetType("TaxAI.Portal.Production.RecoveryOrchestrator", throwOnError: true)!;

        // The orchestrator is registered in the Production branch only, and it
        // cannot be constructed without the approved control ledger: this host's
        // configuration names none, so none is registered and the gate refuses.
        Assert.True(_factory.Services.GetRequiredService<IServiceProviderIsService>().IsService(orchestrator));
        Assert.Null(_factory.Services.GetService<IRecoveryControlStore>());
        Assert.Throws<InvalidOperationException>(() => _factory.Services.GetService(orchestrator));
        Assert.DoesNotContain(
            _factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            service => service.GetType().Assembly == typeof(Program).Assembly);
        Assert.False(typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(orchestrator));

        var registration = string.Join("\n", ExecutableLines(PortalFile("ModuleRegistration.cs")));
        var infrastructure = registration[registration.IndexOf("services.AddTaxAiAzureInfrastructure(", StringComparison.Ordinal)..];
        Assert.Contains("new RecoveryOrchestrator(", infrastructure[..infrastructure.IndexOf("services.AddScoped<IAccountantIntakeWorkflow", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(registration, @"new RecoveryOrchestrator\("));

        // The gate runs it only after every admission rule has passed.
        var gate = string.Join("\n", ExecutableLines(Path.Combine(Path.GetDirectoryName(PortalFile("ModuleRegistration.cs"))!, "Production", "ProductionAdmissionGate.cs")));
        Assert.True(
            gate.IndexOf("throw new InvalidOperationException(RefusalMessage);", StringComparison.Ordinal) <
            gate.IndexOf("RecoveryOrchestrator.RequireRelease(", StringComparison.Ordinal));
    }

    // --------------------------------------------------------- spec 011 AC-32

    /// <summary>
    /// The complete vocabulary of environment-sensitive concerns this host is
    /// allowed to have. Four, enumerated, and no fifth: an environment branch is
    /// how a security control quietly stops applying in the one environment that
    /// matters.
    /// </summary>
    private static readonly (string Concern, string Pattern)[] ApprovedEnvironmentConcerns =
    {
        ("developer key ring", @"\bPersistKeysTo\w*\b|\bDeveloperKeyRing\b"),
        ("development fixture seeding", @"\bIDevelopmentFixtureSeeder\b|\bSeedAsync\b"),
        ("developer exception page", @"\bDeveloperExceptionPage\b"),
        ("startup refusal", @"\bRequireDatabaseConnectionString\b|\bRequireSafeStartup\b|\bthrow new InvalidOperationException\b"),

        // Spec 017 T01: mapping the Development-only sign-in. The branch maps
        // routes; it switches no control off, which the forbidden list below
        // still proves for this branch as for every other.
        ("development sign-in", @"\bDevelopmentSignInEndpoints\b"),
    };

    /// <summary>
    /// Controls an environment branch may never guard. Disabling any of these
    /// outside a developer's machine is the failure this list exists to make
    /// impossible to write by accident.
    /// </summary>
    private static readonly string[] ControlsNoEnvironmentBranchMayDisable =
    {
        @"\bAddAuthentication\b|\bUseAuthentication\b|\bSignInAsync\b",
        @"\bAddAuthorization\b|\bUseAuthorization\b|\bRequireAuthorization\b",
        @"\bAntiforgery\b",
        @"\bValidateOnBuild\b|\bValidateScopes\b",
    };

    [Fact] // spec 011 AC-32
    public void EveryIsDevelopmentOccurrenceGuardsAnApprovedConcern()
    {
        var occurrences = PortalProductionFiles()
            .SelectMany(path => ExecutableLines(path)
                .Select((line, index) => (Path: path, Line: line, Index: index)))
            .Where(entry => entry.Line.Contains("IsDevelopment", StringComparison.Ordinal))
            .ToArray();

        // The token exists somewhere, so this cannot pass by the branch having
        // been renamed out of the scan's reach.
        Assert.NotEmpty(occurrences);

        foreach (var occurrence in occurrences)
        {
            var guarded = GuardedBlock(ExecutableLines(occurrence.Path).ToArray(), occurrence.Index);

            var concerns = ApprovedEnvironmentConcerns
                .Where(concern => Regex.IsMatch(
                    guarded, concern.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                .Select(concern => concern.Concern)
                .ToArray();

            Assert.True(
                concerns.Length == 1,
                $"An IsDevelopment branch in {Path.GetFileName(occurrence.Path)} guards "
                + $"{concerns.Length} approved concerns; it must guard exactly one.");

            foreach (var forbidden in ControlsNoEnvironmentBranchMayDisable)
            {
                Assert.False(
                    Regex.IsMatch(guarded, forbidden, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                    $"An IsDevelopment branch in {Path.GetFileName(occurrence.Path)} guards /{forbidden}/.");
            }
        }
    }

    [Fact] // spec 011 AC-32
    public void TheEnvironmentTokenIsNeverUsedToTurnOffARequirement()
    {
        // The startup refusal is environment-sensitive in the other direction:
        // it is only ever ENABLED outside Development. It is written as an
        // ordinal comparison rather than an IsDevelopment() call precisely so
        // that a negated environment check never sits around a security control,
        // and this proves the file it lives in registers, maps, and uses
        // nothing — it can only refuse.
        var guards = string.Join("\n", ExecutableLines(PortalFile("PortalStartupGuards.cs")));

        Assert.DoesNotContain("IsDevelopment", guards, StringComparison.Ordinal);
        Assert.Contains("Environments.Development", guards, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", guards, StringComparison.Ordinal);

        foreach (var pattern in new[] { @"\bAdd[A-Z]\w*\s*[<(]", @"\bUse[A-Z]\w*\s*\(", @"\bMap[A-Z]\w*\s*\(" })
        {
            Assert.False(
                Regex.IsMatch(guards, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                $"The startup guards must not wire anything: /{pattern}/ matched.");
        }
    }

    [Fact]
    public void TheConditionalAnalysisActuallyDistinguishesTheTwoCases()
    {
        // Both assertions above are negative — they pass when nothing is found.
        // A brace walker that had stopped working would approve anything, so
        // prove it on a planted example that it must classify correctly.
        var planted = new[]
        {
            "var builder = Build();",
            "if (something)",
            "{",
            "options.ValidateOnBuild = true;",
            "}",
            "options.ValidateScopes = true;",
        };

        Assert.True(IsInsideAConditionalBlock(planted, 3));
        Assert.False(IsInsideAConditionalBlock(planted, 5));
        Assert.False(IsInsideAConditionalBlock(planted, 0));

        // And the guarded-block reader must return the branch, not the file.
        var guarded = GuardedBlock(planted, 1);

        Assert.Contains("ValidateOnBuild", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateScopes", guarded, StringComparison.Ordinal);
    }

    [Fact]
    public void TheApprovedConcernVocabularyIsExactlyFour()
    {
        // Without this, someone widening the list would widen the rule rather
        // than fail against it. Spec 017 T01 (human-authorized) adds exactly
        // one concern, so the vocabulary is now exactly five; the method keeps
        // its name because the amendment ledger refers to guards by name.
        Assert.Equal(
            new[]
            {
                "developer exception page",
                "developer key ring",
                "development fixture seeding",
                "development sign-in",
                "startup refusal",
            },
            ApprovedEnvironmentConcerns
                .Select(concern => concern.Concern)
                .OrderBy(concern => concern, StringComparer.Ordinal)
                .ToArray());
    }

    // --------------------------------------------------- AC-19, AC-20, AC-21

    [Fact]
    public void TheT4MappingPortResolvesToTheEmbeddedProvider() // AC-19
    {
        Assert.IsType<EmbeddedT4LineMapProvider>(
            _factory.Services.GetRequiredService<IT4LineMapProvider>());
    }

    [Fact]
    public void TheT5MappingPortResolvesToTheEmbeddedProvider() // AC-20
    {
        Assert.IsType<EmbeddedT5LineMapProvider>(
            _factory.Services.GetRequiredService<IT5LineMapProvider>());
    }

    [Fact]
    public void TheT4CoordinatorIsConstructibleFromTheContainer() // AC-21
    {
        // Registered to prove the container can build it. No endpoint resolves
        // or invokes it — the portal runs no tax logic.
        Assert.NotNull(_factory.Services.GetRequiredService<T4CaseProcessor>());
    }

    [Fact]
    public void TheProvidersAreSingletons()
    {
        // Two instances would mean two answers about which mapping config is
        // authoritative.
        Assert.Same(
            _factory.Services.GetRequiredService<IT4LineMapProvider>(),
            _factory.Services.GetRequiredService<IT4LineMapProvider>());

        Assert.Same(
            _factory.Services.GetRequiredService<IT5LineMapProvider>(),
            _factory.Services.GetRequiredService<IT5LineMapProvider>());

        Assert.Same(
            _factory.Services.GetRequiredService<T4CaseProcessor>(),
            _factory.Services.GetRequiredService<T4CaseProcessor>());
    }

    // ------------------------------------------- spec 011: the storage layer

    [Theory] // spec 011 AC-31
    [InlineData(typeof(IReviewRecordRepository))]
    [InlineData(typeof(IAuthenticationSessionStore))]
    [InlineData(typeof(IIdentityDirectory))]
    [InlineData(typeof(IReviewRecordPurgeService))]
    [InlineData(typeof(IOpaqueIdentifierSource))]
    [InlineData(typeof(IDevelopmentFixtureSeeder))]
    [InlineData(typeof(IRecoveryReconciler))] // spec 016 T06
    public void EveryTenancyPortResolvesFromTheContainer(Type port)
    {
        // The host calls one registration extension per contributing module, so
        // this is also the proof that the extension registered the whole module
        // and not a subset of it.
        using var scope = _factory.Services.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService(port);

        Assert.Equal(
            typeof(Persistence.PersistenceRegistration).Assembly,
            resolved.GetType().Assembly);
    }

    [Fact] // spec 011 AC-124
    public void ResolvingTheStorageLayerOpensNoConnectionAndCreatesNoSchema()
    {
        // Registering SQL Server is not connecting to it. Schema comes only from
        // migrations: a process that reshapes a taxpayer database on boot is a
        // process that can reshape it wrongly, unattended.
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReviewRecordRepository>());

        var program = string.Join("\n", ExecutableLines(PortalFile("Program.cs")));

        foreach (var schemaMutation in new[] { "EnsureCreated", "Migrate", "MigrateAsync" })
        {
            Assert.DoesNotContain(schemaMutation, program, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ AC-24

    [Fact]
    public void TheUntrustedIngressPortIsNotRegistered() // AC-24
    {
        // Spec 007 shipped this signature deliberately without an
        // implementation. A null-returning lookup, not GetRequiredService, so
        // the assertion is about absence rather than about an exception type.
        Assert.Null(_factory.Services.GetService<IDocumentExtractionIngress>());
    }

    [Fact]
    public void ThePortalImplementsNoIngress() // AC-24
    {
        var implementations = typeof(Program).Assembly
            .GetTypes()
            .Where(type => typeof(IDocumentExtractionIngress).IsAssignableFrom(type) && !type.IsInterface)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(implementations);
    }

    [Fact]
    public void NoLineMapperIsRegistered()
    {
        // The mappers are constructed inside CaseProcessing, not resolved by the
        // host. Resolving one here would put a second construction path for tax
        // logic in the web layer.
        Assert.Null(_factory.Services.GetService<T4LineMapper>());
        Assert.Null(_factory.Services.GetService<T5LineMapper>());
    }

    // ---------------------------------------------------------------- helpers

    private static string PortalFile(string name) =>
        Path.Combine(FindRepositoryRoot(), "src", "TaxAI.Portal", name);

    private static string[] PortalProductionFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(FindRepositoryRoot(), "src", "TaxAI.Portal"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// True when the line at <paramref name="index"/> sits inside a block opened
    /// by a conditional. Brace counting on executable lines is coarse, but it is
    /// the property that matters: a setting inside a conditional block is a
    /// setting that some environment does not get.
    /// </summary>
    private static bool IsInsideAConditionalBlock(string[] lines, int index)
    {
        var openBlocks = new Stack<bool>();
        var pendingConditional = false;

        for (var current = 0; current <= index; current++)
        {
            var line = lines[current];

            if (current < index && StartsAConditional(line))
            {
                pendingConditional = true;
            }

            foreach (var character in line)
            {
                if (character == '{')
                {
                    openBlocks.Push(pendingConditional);
                    pendingConditional = false;
                }
                else if (character == '}' && openBlocks.Count > 0)
                {
                    openBlocks.Pop();
                }
            }
        }

        return openBlocks.Contains(true);
    }

    private static bool StartsAConditional(string line) =>
        Regex.IsMatch(
            line,
            @"^\s*(?:\}\s*)?(?:if|else|switch|while|for|foreach)\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    /// <summary>
    /// The executable lines an environment branch at <paramref name="index"/>
    /// guards: the branch line itself plus its block, by brace depth.
    /// </summary>
    private static string GuardedBlock(string[] lines, int index)
    {
        var block = new List<string> { lines[index] };
        var depth = 0;
        var opened = false;

        for (var current = index; current < lines.Length; current++)
        {
            foreach (var character in lines[current])
            {
                if (character == '{')
                {
                    depth++;
                    opened = true;
                }
                else if (character == '}')
                {
                    depth--;
                }
            }

            if (current > index)
            {
                block.Add(lines[current]);
            }

            if (opened && depth <= 0)
            {
                break;
            }
        }

        return string.Join("\n", block);
    }

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
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the repository root containing TaxAI.sln.");
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);

            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
            }
        }

        return string.Join(" | ", messages);
    }

    // Test-only probes. Neither may enter ModuleRegistration.
    private sealed class ScopedProbe
    {
    }

    private sealed class MissingDependency
    {
    }

    private sealed class NeedsMissingDependency
    {
        public NeedsMissingDependency(MissingDependency dependency) => Dependency = dependency;

        public MissingDependency Dependency { get; }
    }
}
