using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TaxAI.Portal.Tests.Boundaries;

/// <summary>
/// T04 — proves the two things prose cannot enforce: that the dependency arrows
/// never reverse, and that the web layer stays on its side of the line. "UI must
/// not compute tax" is unenforceable convention until something binds it; these
/// scans are that binding.
/// <para>
/// Every scan reads <b>executable lines only</b>. The portal's documentation
/// legitimately names nearly every forbidden concept while explaining why it is
/// forbidden — scanning raw text would make honest comments unwritable. The
/// comment-filter guard at the bottom proves the filter removes documentation
/// without removing code.
/// </para>
/// <para>
/// Solution assertions elsewhere in this suite are presence-and-uniqueness,
/// never a total project count: pinning the solution to "exactly N projects"
/// blocked spec 007's Wave 0 outright and required PR #66 to unpick.
/// </para>
/// </summary>
public sealed class PortalBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string PortalRoot =
        Path.Combine(RepositoryRoot, "src", "TaxAI.Portal");

    /// <summary>
    /// The portal's complete production source set. Named rather than counted so
    /// the scanner guard can prove the walk saw each one.
    /// </summary>
    private static readonly string[] ExpectedProductionFiles =
    {
        // Spec 017 T03: the firm-scoped case list and its browser model.
        "CaseListEndpoints.cs",
        "CaseListPageModel.cs",
        "ClientIntakeCookie.cs",
        "ClientIntakeEndpoints.cs",
        "CryptographicIntakeCredentialSource.cs",
        "DevelopmentEncryptedIntakeContentStore.cs",
        "DevelopmentSignInEndpoints.cs",
        "ExtractionAvailabilityGate.cs",
        "ExtractionConfiguration.cs",
        "ExtractionEndpoints.cs",
        "ExtractionReviewPageModel.cs",
        "FirmEndpoints.cs",
        "IntakeConfiguration.cs",
        "IntakeEndpoints.cs",
        "IntakeHttpSecurity.cs",
        "ModuleRegistration.cs",
        "PortalAuthorization.cs",
        "PortalContent.cs",
        "PortalIdentity.cs",
        "PortalStartupGuards.cs",
        "ProductionAdmissionGate.cs",
        "ProductionConfiguration.cs",
        "Program.cs",
        "RecoveryOrchestrator.cs",
        "ReviewEndpoints.cs",
        "ReviewPageModel.cs",
        "SessionTicketStore.cs",
        "TrustedIngressConfiguration.cs",

        // Spec 017 T03: the closed refusal-notice vocabulary.
        "WorkflowNoticeKind.cs",
    };

    /// <summary>
    /// The portal's complete Razor component set, spec 014 T01.
    /// <para>
    /// Named rather than counted, and asserted exactly: a component that is not on
    /// this list is a screen nobody approved. The <c>.cs</c> scans above cannot see
    /// <c>.razor</c> files, so without this list every component would be exempt
    /// from every boundary rule in this class.
    /// </para>
    /// </summary>
    private static readonly string[] ExpectedComponentFiles =
    {
        // Spec 017 T03: accountant navigation, placed inside accountant pages
        // and never in the shared layout.
        "AccountantNavigation.razor",
        "App.razor",

        // Spec 017 T03: the firm-scoped case list.
        "CaseListPage.razor",
        "ClassificationDisagreementNotice.razor",
        "ClientIntakePage.razor",
        "ClientLinkConfirmationPage.razor",
        "ClientQuestionnairePanel.razor",
        "DuplicateCandidateNotice.razor",
        "ExpectedDocumentChecklist.razor",
        "ExtractionFieldTable.razor",
        "ExtractionListPage.razor",
        "ExtractionReviewPage.razor",
        "IntakeCaseCreatePage.razor",
        "IntakeCaseIssuedPage.razor",
        "IntakeCaseProgressPage.razor",
        "IntakeStatusBadge.razor",
        "MedicalEvidencePanel.razor",
        "PortalLayout.razor",
        "ReviewPage.razor",
        "Routes.razor",

        // Spec 017 T03: the fixed refusal notice.
        "WorkflowNotice.razor",
        "_Imports.razor",
    };

    /// <summary>
    /// Every pre-existing project tree. Frozen deliberately: this is the set
    /// that must not learn the portal's name, and a generic walk of
    /// <c>src/</c> would silently start including the portal itself.
    /// </summary>
    public static TheoryData<string, string> PreExistingProjects() => new()
    {
        { "src", "TaxLogicEngine" },
        { "src", "TaxCases" },
        { "src", "DocumentExtraction" },
        { "src", "CaseProcessing" },
        { "src", "CaseReview" },
        { "tests", "TaxLogicEngine.Tests" },
        { "tests", "TaxCases.Tests" },
        { "tests", "DocumentExtraction.Tests" },
        { "tests", "CaseProcessing.Tests" },
        { "tests", "CaseReview.Tests" },
    };

    // ------------------------------------------------ AC-10: no reverse arrow

    [Theory] // AC-10
    [MemberData(nameof(PreExistingProjects))]
    public void NoPreExistingProjectReferencesThePortal(string area, string project)
    {
        // Dependencies point inward. Nothing may depend on the host — not a
        // module, not a module's test tree. If one ever did, the layering would
        // be inverted and the "UI computes no tax" rule would have no meaning.
        var root = Path.Combine(RepositoryRoot, area, project);
        Assert.True(Directory.Exists(root), $"{root} must exist.");

        var projectFile = Path.Combine(root, $"{project}.csproj");
        Assert.True(File.Exists(projectFile), $"{projectFile} must exist.");

        Assert.DoesNotContain(
            "TaxAI.Portal",
            File.ReadAllText(projectFile),
            StringComparison.OrdinalIgnoreCase);

        var offending = HandwrittenSourceFiles(root)
            .Where(path => File.ReadAllText(path).Contains("TaxAI.Portal", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            offending.Length == 0,
            $"{project} must not reference TaxAI.Portal: {string.Join(", ", offending)}");
    }

    // --------------------------------------------- AC-40: compiled references

    [Fact]
    public void TheCompiledPortal_ReferencesExactlyTheFourInwardModules() // spec 011 AC-10, AC-13
    {
        // The project file states intent; the assembly states what actually
        // compiled in. A transitive leak — a type named in passing — would show
        // up here and nowhere else. Tenancy references the review module and
        // Persistence references Tenancy, yet neither of those may reach this
        // assembly's reference list, because the host names none of their types.
        //
        // The framework filter is wider than the class libraries' equivalent by
        // necessity: a web host references dozens of shared-framework
        // assemblies. Anything outside System*/Microsoft.*/netstandard is a real
        // dependency of ours.
        var nonFrameworkReferences = CompiledNonFrameworkReferences();

        Assert.Equal(
            new[]
            {
                "CaseProcessing",
                "ClientIntake",
                "DocumentIntelligence",
                "ExtractionWorkflow",
                "Persistence",
                "TaxAI.AzureInfrastructure",
                "TaxCases",
                "TaxLogicEngine",
                "Tenancy",
            },
            nonFrameworkReferences);
    }

    [Theory] // spec 011 AC-13
    [InlineData("CaseReview")]
    public void TheCompiledPortal_NamesNoModuleItReachesThroughAContract(string module)
    {
        Assert.DoesNotContain(module, CompiledNonFrameworkReferences());
    }

    [Fact]
    public void ThePortalProject_ReferencesNeitherTaxCasesNorCaseReview() // AC-40, spec 011 AC-13
    {
        var projectText = File.ReadAllText(Path.Combine(PortalRoot, "TaxAI.Portal.csproj"));

        // CaseReview exists and is merged; the portal still may not depend on
        // it. The one approved review screen consumes Persistence's deliberately
        // narrow Tenancy contract instead of reaching across the module boundary.
        foreach (var forbidden in new[] { "TaxCases", "CaseReview", "DocumentExtraction" })
        {
            Assert.DoesNotContain(forbidden, projectText, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            typeof(Program).Assembly.GetReferencedAssemblies(),
            reference =>
                reference.Name!.Equals("CaseReview", StringComparison.OrdinalIgnoreCase));
    }

    [Theory] // spec 011 AC-12
    [InlineData("EntityFrameworkCore")]
    [InlineData("DbContext")]
    [InlineData("DbSet")]
    [InlineData("SqlConnection")]
    [InlineData("System.Data")]
    [InlineData("Sqlite")]
    [InlineData("Npgsql")]
    public void ThePortalProject_DeclaresNoOrmDependency(string forbidden)
    {
        // The storage module exposes one registration extension, which is what
        // keeps the ORM out of this project file entirely. If it ever appears
        // here, the host has acquired a second way to reach the database — one
        // that answers to no repository and no trusted firm.
        Assert.DoesNotContain(
            forbidden,
            File.ReadAllText(Path.Combine(PortalRoot, "TaxAI.Portal.csproj")),
            StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------- AC-22: wiring lives in one place

    /// <summary>
    /// Anything shaped like a registration: <c>Add…</c> or <c>TryAdd…</c>
    /// followed by a capital. Deliberately wider than a list of the container's
    /// own method names, so a module's bespoke <c>AddSomething</c> extension
    /// cannot slip past by not being called <c>AddSingleton</c>.
    /// </summary>
    private static readonly Regex RegistrationVerb = new(
        @"\b(?:Add|TryAdd)[A-Z][A-Za-z0-9_]*",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// The exact registration-shaped tokens each production file may use.
    /// Replaces spec 010's "no registration outside the composition root, and
    /// exactly three inside it": an exact per-file allow-list still forbids
    /// every registration the old rule forbade, and additionally forbids an
    /// unapproved one from appearing <i>inside</i> the composition root.
    /// </summary>
    public static TheoryData<string, string[]> ApprovedRegistrationTokens() => new()
    {
        // AddServerHeader is an option assignment on Kestrel, not a
        // registration; it is enumerated rather than special-cased so nothing is
        // silently exempt. AddTaxAiModules is the single call into the
        // composition root.
        // No AddInteractiveServerRenderMode: spec 014 T01 maps the component
        // endpoint for static server rendering only, and the exact set here is
        // what stops interactivity being switched on without a diff.
        {
            "Program.cs",
            new[]
            {
                "AddInteractiveServerRenderMode",
                "AddServerHeader",
                "AddTaxAiModules",
            }
        },
        // Spec 017 T03: the case list maps one route and holds data-only
        // models; the notice vocabulary is an enum. None registers anything.
        { "CaseListEndpoints.cs", Array.Empty<string>() },
        { "CaseListPageModel.cs", Array.Empty<string>() },
        { "WorkflowNoticeKind.cs", Array.Empty<string>() },
        { "ClientIntakeCookie.cs", Array.Empty<string>() },
        { "ClientIntakeEndpoints.cs", Array.Empty<string>() },
        { "CryptographicIntakeCredentialSource.cs", Array.Empty<string>() },
        { "DevelopmentEncryptedIntakeContentStore.cs", new[] { "AddRange" } },

        // Spec 017 T01: the Development-only sign-in presents the seeded
        // issuer and subject to the existing cookie sign-in event. It registers
        // nothing; the two claims are the whole of what it adds.
        { "DevelopmentSignInEndpoints.cs", new[] { "AddClaim" } },
        { "ExtractionAvailabilityGate.cs", Array.Empty<string>() },
        { "ExtractionConfiguration.cs", Array.Empty<string>() },
        { "ExtractionEndpoints.cs", new[] { "AddEndpointFilter" } },
        { "ExtractionReviewPageModel.cs", Array.Empty<string>() },
        { "FirmEndpoints.cs", new[] { "AddClaim" } },
        { "IntakeConfiguration.cs", Array.Empty<string>() },
        { "IntakeEndpoints.cs", Array.Empty<string>() },
        {
            "IntakeHttpSecurity.cs",
            new[]
            {
                "AddFixedWindowPolicy",
                "AddIntakeRateLimiting",
                "AddPolicy",
                "AddRateLimiter",
                "AddUploadPolicy",
            }
        },
        {
            "ModuleRegistration.cs",
            new[]
            {
                "AddAntiforgery",
                "AddDocumentIntelligence",
                "AddIntakeRateLimiting",
                "AddInteractiveServerComponents",
                "AddPolicy",
                "AddPortalIdentity",
                "AddRazorComponents",
                "AddScoped",
                "AddSingleton",
                "AddTaxAiAzureInfrastructure",
                "AddTaxAiModules",
                "AddTaxAiPersistence",
            }
        },
        { "PortalAuthorization.cs", new[] { "AddClaim" } },
        { "PortalContent.cs", Array.Empty<string>() },
        { "PortalStartupGuards.cs", Array.Empty<string>() },

        // Spec 016 T01: the production gate and profile reader evaluate; they
        // register nothing.
        { "ProductionAdmissionGate.cs", Array.Empty<string>() },
        { "ProductionConfiguration.cs", Array.Empty<string>() },

        // Spec 016 T06: the recovery orchestrator resolves and reconciles; it
        // registers nothing. The composition root registers it.
        { "RecoveryOrchestrator.cs", Array.Empty<string>() },
        { "ReviewEndpoints.cs", Array.Empty<string>() },

        // The review page model is data only: it registers nothing and reaches
        // nothing.
        { "ReviewPageModel.cs", Array.Empty<string>() },

        // The host's own contributing module. AddClaim is the framework's
        // ClaimsIdentity method rather than a registration; it is enumerated
        // rather than special-cased so nothing is silently exempt.
        {
            "PortalIdentity.cs",
            new[]
            {
                "AddAuthentication",
                "AddAuthorization",
                "AddClaim",
                "AddCookie",
                "AddDataProtection",
                "AddOpenIdConnect",
                "AddOptions",
                "AddPortalIdentity",
                "AddSingleton",
            }
        },

        // The ticket store registers nothing at all. It builds a principal, and
        // AddClaim is the only registration-shaped token in it.
        { "SessionTicketStore.cs", new[] { "AddClaim" } },

        // Spec 016 T02: the trusted ingress policy configures the framework's
        // forwarded-header middleware in Production; it registers nothing.
        { "TrustedIngressConfiguration.cs", Array.Empty<string>() },
    };

    [Theory] // AC-22, spec 011 AC-31
    [MemberData(nameof(ApprovedRegistrationTokens))]
    public void EveryRegistrationTokenIsApprovedForItsFile(string file, string[] approved)
    {
        // Looked up by name across the tree rather than assumed to sit at the
        // project root: spec 014 put ReviewPageModel.cs under Components/Pages, and
        // the allow-list is keyed by file name so it stays comparable to
        // ExpectedProductionFiles.
        var path = Assert.Single(
            ProductionSourceFiles(),
            candidate => Path.GetFileName(candidate) == file);

        var used = RegistrationVerb
            .Matches(string.Join("\n", ExecutableLines(path)))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(approved.OrderBy(token => token, StringComparer.Ordinal).ToArray(), used);
    }

    [Fact] // AC-22
    public void TheAllowListCoversEveryProductionFile()
    {
        // Without this, adding a file would exempt it from the rule above
        // instead of failing.
        Assert.Equal(
            ExpectedProductionFiles,
            ApprovedRegistrationTokens()
                .Select(row => (string)row[0]!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact] // AC-22, FR-14
    public void TheCompositionRootCallsExactlyOneRegistrationExtensionPerContributingModule()
    {
        // The container's own verbs are how this file registers the three
        // in-process singletons; anything else it calls is another module's
        // single entry point. One per module or the ORM package ends up in this
        // project's file.
        var compositionRoot = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ModuleRegistration.cs")));

        var moduleExtensions = RegistrationVerb
            .Matches(compositionRoot)
            .Select(match => match.Value)
            .Where(token => token is not (
                "AddAntiforgery" or
                "AddIntakeRateLimiting" or
                "AddInteractiveServerComponents" or
                "AddPolicy" or
                "AddRazorComponents" or
                "AddSingleton" or
                "AddScoped" or
                "AddTransient" or
                "AddTaxAiModules"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToArray();

        // Four contributing modules, four extensions: document intelligence,
        // storage, the spec 016 Azure infrastructure validation boundary, and
        // this host's own identity wiring.
        //
        // AddRazorComponents is excluded above because it is a framework builder
        // call, not a contributing module's entry point — the same reason
        // AddAntiforgery and AddPolicy are excluded. It is still pinned exactly, by
        // the per-file allow-list.
        Assert.Equal(
            new[]
            {
                "AddDocumentIntelligence",
                "AddPortalIdentity",
                "AddTaxAiAzureInfrastructure",
                "AddTaxAiPersistence",
            },
            moduleExtensions);

        Assert.Equal(
            1,
            ModuleSourceFiles("TaxAI.AzureInfrastructure")
                .Sum(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"IServiceCollection\s+AddTaxAiAzureInfrastructure\s*\(",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)).Count));

        Assert.Equal(
            1,
            ModuleSourceFiles("DocumentIntelligence")
                .Sum(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"IServiceCollection\s+AddDeterministicDocumentIntelligence\s*\(",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)).Count));

        // Each of those extensions is declared exactly once, by the module that
        // owns it. Spec 017 T01: Persistence declares its one extension as
        // exactly two overloads — the original, and the one that states the
        // Development-local database allowance explicitly. The composition root
        // still makes exactly one call to it, proven immediately below.
        Assert.Equal(
            2,
            ModuleSourceFiles("Persistence")
                .Sum(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"IServiceCollection\s+AddTaxAiPersistence\s*\(",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)).Count));

        Assert.Equal(
            1,
            ProductionSourceFiles()
                .Sum(path => Regex.Matches(
                    string.Join("\n", ExecutableLines(path)),
                    @"IServiceCollection\s+AddPortalIdentity\s*\(",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)).Count));

        // The tax, intake, extraction-configuration, clock, and cookie singleton
        // registrations remain explicit and exact. Spec 016 T03 adds exactly one:
        // the Key Vault-loaded intake material the admission gate verifies.
        // Spec 016 T06 adds exactly one more: the Production-only recovery
        // orchestrator the admission gate runs before the host serves.
        Assert.Equal(
            14,
            new Regex(@"\bAddSingleton\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Matches(compositionRoot)
                .Count);
    }

    // ------------------------- AC-31 – AC-35: the line the portal may not cross

    public static TheoryData<string, string> ForbiddenProductionPatterns() => new()
    {
        // AC-31 — no tax computation, and no arithmetic that could become one.
        { "engine parser or mapper", @"\bT4SlipParser\b|\bT5SlipParser\b|\bT4LineMapper\b|\bT5LineMapper\b|\bT4SlipBoxAmounts\b|\bT5SlipBoxAmounts\b" },
        { "rounding / money math", @"\bMoneyRounding\b|\bMath\s*\.\s*Round\b|\bMidpointRounding\b|\bdouble\b|\bfloat\b|\bdecimal\b" },

        // AC-32 — no tax-domain model enters the portal. The one approved
        // persisted review projection is deliberately not one of these types.
        { "taxpayer data type", @"\bT5Slip\b|\bT4RawInput\b|\bT5RawInput\b|\bTaxLineEntry\b|\bT4MappingResult\b|\bT5MappingResult\b|\bRecipientIdentifier\b|\bValidatedExtraction\b|\bExtractionEnvelope\b|\bProcessedT4Extraction\b|\bTaxCase\b" },

        // AC-34 — no second mapping authority and no mapping-resource read.
        { "mapping table or resource read", @"TaxLogicEngine\.Mappings|\bGetManifestResourceStream\b|\bT4LineMap\b|\bT5LineMap\b|\bT4BoxCodes\b|\bT4OtherInformationCodes\b|\bDictionary\s*<" },

        // AC-35 — the portal declares no tax-year policy of its own.
        { "tax-year policy", @"\bSupportedProcessingTaxYears\b|\bSupportedReviewTaxYears\b|\bSupportedTaxYears\b" },
    };

    [Theory] // AC-31, AC-32, AC-34, AC-35
    [MemberData(nameof(ForbiddenProductionPatterns))]
    public void ProductionSource_ContainsNoForbiddenToken(string boundary, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var violations = ProductionSourceFiles()
            .Select(path => new { Name = Path.GetFileName(path), Text = string.Join("\n", ExecutableLines(path)) })
            .Where(source => regex.IsMatch(source.Text))
            .Select(source => source.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Forbidden {boundary} pattern /{pattern}/ found in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void NoCraLineNumberLiteralAppearsInThePortal() // AC-33
    {
        // CRA T1 line numbers are five digits. None may be hardcoded here —
        // every line number in the system originates from TaxLogicEngine's
        // embedded JSON config, reached only through a provider.
        var craLineNumber = new Regex(@"\b\d{5}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => craLineNumber.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} contains a possible CRA line number: {string.Join(" | ", offending)}");
        }
    }

    [Fact]
    public void NoTaxYearLiteralAppearsInThePortal() // AC-35
    {
        // Year isolation belongs to the modules that own it. A year literal here
        // would be a second policy, and the two would drift.
        var year = new Regex(@"\b(?:19|20)\d{2}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => year.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} names a tax year: {string.Join(" | ", offending)}");
        }
    }

    [Fact]
    public void TaxYearIsOnlyCarriedFromThePersistedReviewProjection() // spec 012 T01
    {
        var matching = ProductionSourceFiles()
            .Where(path => Regex.IsMatch(
                string.Join("\n", ExecutableLines(path)),
                @"\bTaxYear\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                // Spec 017 T03: the case list carries the stored tax year of
                // each case, read from the firm-scoped list and never set here.
                "CaseListEndpoints.cs",
                "CaseListPageModel.cs",
                "ClientIntakeEndpoints.cs",
                "ExtractionEndpoints.cs",
                "ExtractionReviewPageModel.cs",
                "ReviewEndpoints.cs",
            },
            matching);

        var review = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ReviewEndpoints.cs")));
        Assert.Contains("record.TaxYear", review, StringComparison.Ordinal);
    }

    [Fact]
    public void BoxCodesAreOnlyCarriedFromThePersistedReviewProjection() // AC-33, AC-34
    {
        // Displaying persisted labels is allowed; declaring a box→line rule in
        // the web layer would still make it a second mapping authority.
        var mappingConcept = new Regex(
            @"\bBoxToLine\b|\bLineNumber\b|\bCraLine\b|\bBox\s*\d+\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        foreach (var file in ProductionSourceFiles())
        {
            var offending = ExecutableLines(file).Where(line => mappingConcept.IsMatch(line)).ToArray();

            Assert.True(
                offending.Length == 0,
                $"{Path.GetFileName(file)} declares a box or CRA line rule: {string.Join(" | ", offending)}");
        }

        var boxCodeReaders = ProductionSourceFiles()
            .Where(path => Regex.IsMatch(
                string.Join("\n", ExecutableLines(path)),
                @"\bBoxCode\b",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "ExtractionEndpoints.cs", "ReviewEndpoints.cs" },
            boxCodeReaders);

        var review = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ReviewEndpoints.cs")));
        foreach (var persistedField in new[]
                 {
                     ".BoxCode",
                     ".LineCode",
                     ".SourceBoxCodes",
                     ".UnmappedBoxCodes",
                 })
        {
            Assert.Contains(persistedField, review, StringComparison.Ordinal);
        }

        var extraction = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ExtractionEndpoints.cs")));
        Assert.Contains(
            "ExtractionWorkflowFields.BoxCode",
            extraction,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LineCode", extraction, StringComparison.Ordinal);
    }

    // --------------------- spec 014 AC-90, AC-91, AC-96, AC-98: the components

    [Fact]
    public void EveryApprovedComponentExistsExactlyOnce() // AC-90
    {
        var components = ComponentFiles()
            .Select(Path.GetFileName)
            .ToArray();

        foreach (var approved in ExpectedComponentFiles)
        {
            _ = Assert.Single(
                components,
                name => string.Equals(
                    name,
                    approved,
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NoComponentDeclaresARenderModeAndNoGlobalModeIsApplied() // AC-90
    {
        // Static server rendering remains the default. Exactly one
        // presentation-only questionnaire island opts in.
        foreach (var component in ComponentFiles())
        {
            var markup = ComponentMarkup(component);
            if (Path.GetFileName(component) == "ClientQuestionnairePanel.razor")
            {
                Assert.Equal(
                    1,
                    Regex.Matches(
                        markup,
                        @"@rendermode\s+InteractiveServer",
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1)).Count);
            }
            else
            {
                Assert.DoesNotContain(
                    "@rendermode",
                    markup,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        var registration = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ModuleRegistration.cs")));
        var program = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "Program.cs")));

        Assert.Contains(
            "AddInteractiveServerComponents(",
            registration,
            StringComparison.Ordinal);
        Assert.Contains(
            "options => options.DetailedErrors = false",
            registration,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddInteractiveServerRenderMode(",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "options => options.DisableWebSocketCompression = true",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddInteractiveWebAssembly",
            registration + program,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@rendermode",
            ComponentMarkup(
                ComponentFiles().Single(path =>
                    Path.GetFileName(path) == "App.razor")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // spec 014 AC-89
    public void TheHostShipsNoClientScriptReference()
    {
        var scriptComponents = ComponentFiles()
            .Where(component => ComponentMarkup(component).Contains(
                "<script",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "App.razor", "ClientIntakePage.razor" },
            scriptComponents);
        Assert.Contains(
            "blazor.web.js",
            ComponentMarkup(ComponentFiles().Single(path =>
                Path.GetFileName(path) == "App.razor")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ComponentFiles(),
            component => ComponentMarkup(component).Contains(
                "blazor.server.js",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TaxpayerScreensAreEndpointGatedAndNotRoutable() // AC-90, AC-91
    {
        // The review screen is reached only through its authorization-gated minimal
        // endpoint. A @page directive would put it in the component router, where
        // the safe-404 and R-1 decisions no longer precede rendering.
        foreach (var component in ComponentFiles())
        {
            Assert.DoesNotContain(
                "@page",
                ComponentMarkup(component),
                StringComparison.OrdinalIgnoreCase);
        }

        var review = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ReviewEndpoints.cs")));

        Assert.Contains("RazorComponentResult<ReviewPage>", review, StringComparison.Ordinal);

        var extraction = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ExtractionEndpoints.cs")));
        Assert.Contains(
            "RazorComponentResult<ExtractionListPage>",
            extraction,
            StringComparison.Ordinal);
        Assert.Contains(
            "RazorComponentResult<ExtractionReviewPage>",
            extraction,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NoEndpointAssemblesScreenMarkupOrAnInlineStylesheet() // AC-91, AC-98
    {
        // The migration's whole point. The renderer, its StringBuilder, its
        // appended markup, and its inline <style> block are gone; the fixed empty
        // 404, fixed error, and constant shell/health writers are untouched.
        var review = string.Join(
            "\n",
            ExecutableLines(Path.Combine(PortalRoot, "ReviewEndpoints.cs")));

        foreach (var removed in new[]
                 {
                     "StringBuilder",
                     "<style",
                     "<!DOCTYPE",
                     "AppendEncoded",
                     "HtmlEncoder",
                     "<table",
                     "<article",
                 })
        {
            Assert.DoesNotContain(removed, review, StringComparison.OrdinalIgnoreCase);
        }

        // And exactly one production file still assembles markup: FirmEndpoints,
        // whose firm-selection page predates spec 014 and is not one of the screens
        // AC-91 enumerates. It is named here rather than exempted by silence, so a
        // second markup assembler cannot appear without failing this test.
        Assert.Equal(
            new[] { "FirmEndpoints.cs" },
            ProductionSourceFiles()
                .Where(path => string.Join("\n", ExecutableLines(path))
                    .Contains("StringBuilder", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void EveryComponentIsPresentationOnly() // AC-96
    {
        // A component may accept typed parameters and encode them. It may not be a
        // repository, a route handler, or an authorization boundary. @inject is the
        // token that would make it one.
        foreach (var component in ComponentFiles())
        {
            var executable = ComponentMarkup(component);

            foreach (var forbidden in new[]
                     {
                         "@inject",
                         "IReviewRecordRepository",
                         "IIntakeStore",
                         "IIntakeContentStore",
                         "IIntakeCredentialSource",
                         "IAccountantIntakeWorkflow",
                         "IClientIntakeWorkflow",
                         "IExtractionIngestWorkflow",
                         "IExtractionReviewWorkflow",
                         "IExtractionStore",
                         "PortalAuthorization",
                         "TrustedFirmId",
                         "PersistedReviewRecord",
                         "DbContext",
                         "HttpContext",
                         "T4CaseProcessor",
                         "T4SlipParser",
                         "T4LineMapper",
                         "AccountantReviewPackage",
                         "OnInitializedAsync",
                         "decimal",
                     })
            {
                Assert.DoesNotContain(forbidden, executable, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void NoComponentOrStylesheetIssuesAThirdPartyRequest() // AC-98
    {
        // Every asset is local and tracked in this repository. A remote script,
        // stylesheet, font, or image would be a third party inside a taxpayer-data
        // response.
        var assets = ComponentFiles()
            .Concat(StylesheetFiles())
            .ToArray();

        Assert.NotEmpty(assets);

        foreach (var asset in assets)
        {
            var text = File.ReadAllText(asset);

            foreach (var forbidden in new[]
                     {
                         "http://",
                         "https://",
                         "//cdn",
                         "@import url(",
                         "integrity=",
                         "crossorigin",
                     })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void TheReusableStylesLiveInTrackedLocalOrScopedCssOnly() // AC-98
    {
        Assert.Equal(
            new[]
            {
                // Spec 017 T03: component-scoped, because intake.css is pinned.
                "CaseListPage.razor.css",
                "PortalLayout.razor.css",
                "ReviewPage.razor.css",
                "intake.css",
                "portal.css",
            },
            StylesheetFiles()
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheHostAddsNoWebAssemblySpaOrNpmDependency() // AC-89
    {
        var projectText = File.ReadAllText(Path.Combine(PortalRoot, "TaxAI.Portal.csproj"));

        foreach (var forbidden in new[]
                 {
                     "WebAssembly",
                     "Microsoft.AspNetCore.Components.WebAssembly",
                     "SpaProxy",
                     "Microsoft.TypeScript",
                     "NodeServices",
                 })
        {
            Assert.DoesNotContain(forbidden, projectText, StringComparison.OrdinalIgnoreCase);
        }

        // No npm tree, and no second client build.
        foreach (var forbidden in new[] { "package.json", "package-lock.json", "node_modules" })
        {
            Assert.False(
                Directory.EnumerateFileSystemEntries(PortalRoot, forbidden, SearchOption.AllDirectories).Any(),
                $"{forbidden} must not exist in the portal.");
        }
    }

    // -------------------------------------------------------------- guards

    [Fact]
    public void TheScanner_SeesEveryProductionFileAndStillMatchesAKnownPattern()
    {
        // Without this, a broken walk or an over-eager comment filter would make
        // every negative assertion above pass for free.
        var files = ProductionSourceFiles();

        Assert.Equal(
            ExpectedProductionFiles,
            files.Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var allExecutable = string.Join("\n", files.SelectMany(ExecutableLines));

        Assert.True(
            allExecutable.Length > 500,
            $"Implausibly little executable source: {allExecutable.Length} chars.");

        // Patterns that must match, proving the regex path works end to end. If
        // these stopped matching, the forbidden-token scans would be scanning
        // nothing.
        Assert.Matches(new Regex(@"\bAddTaxAiModules\b"), allExecutable);
        Assert.Matches(new Regex(@"\bMapGet\b"), allExecutable);
        Assert.Matches(new Regex(@"\bIT4LineMapProvider\b"), allExecutable);
        Assert.Matches(new Regex(@"\bAddTaxAiPersistence\b"), allExecutable);
        Assert.Matches(new Regex(@"\bRequireSafeStartup\b"), allExecutable);
    }

    [Fact]
    public void TheCommentFilter_RemovesDocumentationWithoutRemovingCode()
    {
        // The filter is what makes "executable lines only" safe. Prove both
        // directions on a file that deliberately documents a forbidden concept.
        var compositionRoot = Path.Combine(PortalRoot, "ModuleRegistration.cs");
        var raw = File.ReadAllText(compositionRoot);
        var executable = string.Join("\n", ExecutableLines(compositionRoot));

        // The documentation names a forbidden concept while explaining it...
        Assert.Contains("IDocumentExtractionIngress", raw, StringComparison.Ordinal);
        // ...the filter removes it, so honest comments stay writable...
        Assert.DoesNotContain("IDocumentExtractionIngress", executable, StringComparison.Ordinal);
        // ...while the code itself survives.
        Assert.Contains("AddSingleton", executable, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static string[] ProductionSourceFiles() =>
        HandwrittenSourceFiles(PortalRoot).OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private static string[] ComponentFiles() =>
        HandwrittenFiles(PortalRoot, "*.razor").OrderBy(path => path, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// A component's executable markup, with Razor <c>@* … *@</c> comments removed
    /// as well as C# ones.
    /// <para>
    /// The components legitimately document why they carry no <c>@page</c> and no
    /// <c>@rendermode</c>. Scanning raw text would make those comments unwritable —
    /// exactly the problem the C# comment filter already solves for <c>.cs</c>
    /// files.
    /// </para>
    /// </summary>
    private static string ComponentMarkup(string path) =>
        string.Join(
            "\n",
            ExecutableLines(
                RazorComment.Replace(File.ReadAllText(path), string.Empty)
                    .Split('\n')));

    private static readonly Regex RazorComment = new(
        @"@\*.*?\*@",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static IEnumerable<string> ExecutableLines(IEnumerable<string> lines) =>
        lines
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

    private static string[] StylesheetFiles() =>
        HandwrittenFiles(PortalRoot, "*.css").OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private static string[] ModuleSourceFiles(string module) =>
        HandwrittenSourceFiles(Path.Combine(RepositoryRoot, "src", module))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] CompiledNonFrameworkReferences() =>
        typeof(Program).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name =>
                !name.Equals("netstandard", StringComparison.Ordinal) &&
                !name.StartsWith("System", StringComparison.Ordinal) &&
                !name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> HandwrittenSourceFiles(string root) =>
        HandwrittenFiles(root, "*.cs");

    private static IEnumerable<string> HandwrittenFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

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
}
