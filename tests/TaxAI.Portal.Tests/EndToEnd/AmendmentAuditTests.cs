using System.Text.RegularExpressions;

namespace TaxAI.Portal.Tests.EndToEnd;

/// <summary>
/// Spec 011 AC-28, carried through spec 012 — a mechanical ledger for both
/// tables in the PRD's Amendments section. Every row names a live test that
/// proves its current replacement or preservation.
/// </summary>
public sealed class AmendmentAuditTests
{
    private const string ProjectStructure =
        "tests/TaxAI.Portal.Tests/ProjectStructureTests.cs";
    private const string CompositionRoot =
        "tests/TaxAI.Portal.Tests/CompositionRootTests.cs";
    private const string ShellPage =
        "tests/TaxAI.Portal.Tests/ShellPageTests.cs";
    private const string PortalBoundary =
        "tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs";
    private const string PortalSecurity =
        "tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs";
    private const string RepositoryVerification =
        "tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs";
    private const string SolutionRegression =
        "tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs";
    private const string HealthEndpoint =
        "tests/TaxAI.Portal.Tests/HealthEndpointTests.cs";
    private const string TenancyBoundary =
        "tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs";
    private const string PersistenceBoundary =
        "tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs";

    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string PrdPath = Path.Combine(
        RepositoryRoot,
        "specs",
        "011-tenant-identity-and-persistence-foundation",
        "prd.md");

    private static readonly Amendment[] RequirementAmendments =
    [
        A("FR-5 / AC-3", "FR-13", "The exact one-package set and its exact version replace an empty set.",
            P(ProjectStructure, "PortalProject_DeclaresExactlyTheOneApprovedPackage"),
            P(ProjectStructure, "PortalProject_PinsTheApprovedPackageToAnExactVersion")),
        A("FR-3 / AC-8 / AC-40", "FR-12", "The exact four inward references are allowed and every contract-reached module is still forbidden.",
            P(ProjectStructure, "PortalProject_ReferencesExactlyTheFourInwardModules"),
            P(ProjectStructure, "PortalProject_ReferencesNoModuleReachedThroughAContract")),
        A("FR-11 / AC-26", "FR-48", "The complete route and method inventory is exact; only one opaque record selector is allowed and tenant or taxpayer identifier names remain forbidden.",
            P(ShellPage, "TheHostMapsExactlyTheApprovedRoutes"),
            P(ShellPage, "OnlyTheReviewRouteTakesTheOpaqueRecordSelector"),
            P(ShellPage, "NoRouteParameterNamesATenantOrTaxpayerIdentifier")),
        A("FR-12 / AC-27", "FR-49", "Only the two approved form names and one opaque review selector are accepted.",
            P(PortalSecurity, "CallerSuppliedInputsAreTheTwoFormNamesAndOneOpaqueReviewSelector"),
            P(PortalSecurity, "RequestInputAppearsOnlyInItsExactApprovedFile")),
        A("FR-19 / AC-39", "FR-38–FR-49 and FR-63–FR-72", "Authentication is an exact file-and-scheme allowlist and every endpoint has explicit authorization posture.",
            P(PortalSecurity, "TheApprovedAuthenticationFilesAreExactlyTheEnumeratedSet"),
            P(ShellPage, "EveryEndpointIsEitherEnumeratedAnonymousOrAuthorizationRequired")),
        A("FR-20 — no persistence", "FR-16, FR-21, FR-33, FR-66", "Only named infrastructure seams are allowed while background work stays forbidden everywhere.",
            P(PortalSecurity, "ProductionSource_TakesNoInfrastructureDependency"),
            P(PortalSecurity, "ANarrowedInfrastructureConcernAppearsOnlyInItsOneApprovedFile")),
        A("FR-15 / AC-31", "FR-51", "The portal's monetary-token prohibition is preserved exactly.",
            P(PortalBoundary, "ProductionSource_ContainsNoForbiddenToken")),
        A("FR-16 / AC-32 / AC-36", "FR-50, FR-51", "SIN-bearing and tax-domain taxpayer types remain forbidden while the closed persisted review projection is allowed.",
            P(PortalSecurity, "ProductionSource_ContainsNoPiiToken")),
        A("FR-15 / AC-35", "FR-51", "The no-tax-year-policy condition remains forbidden across every portal production file.",
            P(PortalBoundary, "NoTaxYearLiteralAppearsInThePortal")),
        A("FR-18 / AC-44", "FR-61", "The gate is narrowed to an exact list of still-forbidden taxpayer-data surfaces.",
            P(ProjectStructure, "PortalInstructions_StateTheNarrowedBoundaryAndGate")),
        A("FR-13 / AC-28", "FR-57, AC-29, AC-132", "Fixed disclosure-free errors are preserved and extended to unmatched routes in every environment.",
            P(PortalSecurity, "AnUnmatchedRouteLeaksNoDiagnosticDetail"),
            P(HealthEndpoint, "HealthDisclosesNothingAboutTheSystem")),
        A("FR-21 / AC-42", "FR-52", "The client-side prohibition remains repository-wide, and the static-file surface is now pinned exactly rather than forbidden: spec 014 T01 moved the review stylesheet out of assembled inline markup, so exactly two tracked CSS files are served by one parameterless middleware call in one file, the web root's contents are an exact set, the endpoint-routing catch-all variant is refused outright, and no handwritten script ships.",
            P(PortalSecurity, "TheRepositoryHasNoClientSideStack"),
            P(PortalSecurity, "ThePortalShipsNoHandwrittenJavaScript"),
            P(PortalSecurity, "TheStaticFileSurfaceIsExactlyTheTrackedStylesheets")),
        A("FR-22 / AC-41", "FR-58", "Tracked configuration allows no connection-string section or non-local binding.",
            P(PortalSecurity, "TrackedConfiguration_CarriesNoSecret"),
            P(PortalSecurity, "TrackedConfiguration_BindsOnlyToLocalhost")),
    ];

    private static readonly Amendment[] ShippedAssertionAmendments =
    [
        A("`PortalProject_DeclaresNoPackageReference`", "exact approved OIDC package",
            "The replacement fixes both the exact package set and its exact pinned version.",
            P(ProjectStructure, "PortalProject_DeclaresExactlyTheOneApprovedPackage"),
            P(ProjectStructure, "PortalProject_PinsTheApprovedPackageToAnExactVersion")),
        A("`PortalProject_ReferencesExactlyCaseProcessingAndTaxLogicEngine`", "exact four-entry inward reference set",
            "The replacement fixes the allowed set and separately forbids all contract-reached modules.",
            P(ProjectStructure, "PortalProject_ReferencesExactlyTheFourInwardModules"),
            P(ProjectStructure, "PortalProject_ReferencesNoModuleReachedThroughAContract")),
        A("`TestProject_AddsOnlyTheInProcessWebTestHost`", "exact extra-package set",
            "The preserved name now checks the complete exact extra-package set, not just one present package.",
            P(ProjectStructure, "TestProject_AddsOnlyTheInProcessWebTestHost")),
        A("`PortalInstructions_StateTheBoundaryAndTheGate`", "narrowed gate text",
            "The replacement enumerates every retained gate and the new identity and revocation invariants.",
            P(ProjectStructure, "PortalInstructions_StateTheNarrowedBoundaryAndGate")),
        A("`ThePortalsOnlyPublicTypeIsProgram`", "exact exported-type set plus the approved component allowlist",
            "Spec 014 T01's Blazor foundation makes components public by necessity — the Razor compiler emits them as public types and a parameter must be public to be settable. The replacement still forbids every public type the single-entry rule forbade, names exactly which nine are approved, separately forbids a public endpoint, guard, identity, content, or ticket-store type, and pins the review component's one required parameter.",
            P(ProjectStructure, "ThePortalsPublicSurfaceIsExactlyProgramAndTheApprovedComponents"),
            P(ProjectStructure, "NoPublicPortalTypeIsAnEndpointGuardOrIdentitySurface"),
            P(ProjectStructure, "TheReviewPageExposesExactlyOneRequiredModelParameter")),
        A("`BothValidationFlagsAreSetOutsideAnyDevelopmentBranch`", "every `IsDevelopment` occurrence",
            "Validation remains unconditional and every environment branch is constrained to an approved purpose.",
            P(CompositionRoot, "BothValidationFlagsAreSetOutsideAnyConditionalBlock"),
            P(CompositionRoot, "EveryIsDevelopmentOccurrenceGuardsAnApprovedConcern"),
            P(CompositionRoot, "TheEnvironmentTokenIsNeverUsedToTurnOffARequirement")),
        A("`TheUntrustedIngressPortIsNotRegistered`", "Preserved unchanged",
            "All four original composition prohibitions remain live.",
            P(CompositionRoot, "TheUntrustedIngressPortIsNotRegistered"),
            P(CompositionRoot, "ThePortalImplementsNoIngress"),
            P(CompositionRoot, "NoLineMapperIsRegistered"),
            P(CompositionRoot, "TheProvidersAreSingletons")),
        A("`TheHostMapsExactlyTwoGetEndpoints`", "exact new route inventory",
            "The complete route-method set is exact and the one opaque review parameter is constrained by name and GUID policy.",
            P(ShellPage, "TheHostMapsExactlyTheApprovedRoutes"),
            P(ShellPage, "EachApprovedRouteAnswersExactlyItsOneMethod"),
            P(ShellPage, "OnlyTheReviewRouteTakesTheOpaqueRecordSelector"),
            P(ShellPage, "NoRouteParameterNamesATenantOrTaxpayerIdentifier")),
        A("`TheRootReturnsTheFixedShellDocument`", "Intentionally revised and still byte-for-byte fixed",
            "The truthful root copy changed once; the exact body, dynamic-value, asset, and method assertions remain live.",
            P(ShellPage, "TheRootReturnsTheFixedShellDocument"),
            P(ShellPage, "TheShellCarriesNoDynamicValue"),
            P(ShellPage, "TheShellReferencesNoExternalAsset"),
            P(ShellPage, "TheRootIsGetOnly")),
        A("`PortalBoundaryTests.cs` (T04) | `ExpectedProductionFiles`", "new exact production-file list",
            "The source allowlist is exact and the scanner guard still proves complete traversal.",
            P(PortalBoundary, "TheAllowListCoversEveryProductionFile"),
            P(PortalBoundary, "TheScanner_SeesEveryProductionFileAndStillMatchesAKnownPattern")),
        A("`TheCompiledPortal_ReferencesExactlyCaseProcessingAndTaxLogicEngine`", "exact new non-framework referenced-assembly set",
            "The compiled set is exact and contract-reached modules are explicitly excluded.",
            P(PortalBoundary, "TheCompiledPortal_ReferencesExactlyTheFourInwardModules"),
            P(PortalBoundary, "TheCompiledPortal_NamesNoModuleItReachesThroughAContract")),
        A("`EveryServiceRegistrationLivesInTheCompositionRoot`", "exactly the approved set of per-module registration extensions",
            "Every registration token is file-scoped and each contributing module has one exact extension.",
            P(PortalBoundary, "EveryRegistrationTokenIsApprovedForItsFile"),
            P(PortalBoundary, "TheCompositionRootCallsExactlyOneRegistrationExtensionPerContributingModule")),
        A("`ForbiddenProductionPatterns` rows", "Preserved, and extended",
            "The original portal bans remain and matching module-specific bans cover Tenancy and Persistence.",
            P(PortalBoundary, "ProductionSource_ContainsNoForbiddenToken"),
            P(TenancyBoundary, "ProductionSource_ContainsNoForbiddenToken"),
            P(PersistenceBoundary, "ProductionSource_ContainsNoForbiddenToken")),
        A("`AuthenticationPatterns`", "exact allow-list",
            "Authentication tokens are confined to an exact file set and every endpoint has explicit access metadata.",
            P(PortalSecurity, "EveryAuthenticationTokenLivesInItsApprovedFile"),
            P(PortalSecurity, "TheApprovedAuthenticationFilesAreExactlyTheEnumeratedSet"),
            P(ShellPage, "EveryEndpointIsEitherEnumeratedAnonymousOrAuthorizationRequired")),
        A("`InputBindingPatterns`", "exact allow-list",
            "Only two form names and one opaque GUID review selector are allowed.",
            P(PortalSecurity, "RequestInputAppearsOnlyInItsExactApprovedFile"),
            P(PortalSecurity, "CallerSuppliedInputsAreTheTwoFormNamesAndOneOpaqueReviewSelector")),
        A("`InfrastructurePatterns` \"file system\" row", "developer-local Data Protection key ring",
            "File-system access is reduced to one exact file and one exact development-only purpose.",
            P(PortalSecurity, "ANarrowedInfrastructureConcernAppearsOnlyInItsOneApprovedFile"),
            P(PortalSecurity, "TheOneApprovedFileSystemUseIsTheDeveloperKeyRing")),
        A("`InfrastructurePatterns` \"database / ORM\" row", "Preserved",
            "The database and ORM ban remains executable over every portal production file.",
            P(PortalSecurity, "ProductionSource_TakesNoInfrastructureDependency")),
        A("`PiiPatterns` rows", "Preserved and strengthened",
            "The prohibited-PII scan remains live while the two approved display-name fields are confined to the closed review projection.",
            P(PortalSecurity, "ProductionSource_ContainsNoPiiToken"),
            P(PortalSecurity, "ApprovedDisplayPiiIsReadOnlyByTheClosedReviewProjection")),
        A("`NeitherResponseBodyContainsANineDigitRun`", "Preserved and extended",
            "Response, source, logging, configuration, and client-stack guards all remain live.",
            P(PortalSecurity, "NeitherResponseBodyContainsANineDigitRun"),
            P(PortalSecurity, "ProductionSource_LogsNoRequestContent"),
            P(PortalSecurity, "TrackedConfiguration_CarriesNoSecret"),
            P(PortalSecurity, "TheRepositoryHasNoClientSideStack")),
        A("`PortalSecurityTests.cs` (T05) | `ExpectedProductionFiles`", "new exact list",
            "The security scanner proves it traversed the complete exact production-file list.",
            P(PortalSecurity, "TheScanner_SeesEveryProductionFileAndStillMatchesAKnownPattern")),
        A("`PublicSurfaceSnapshots`", "All preserved unchanged",
            "Public surfaces, reference sets, package sets, resources, and internals grants remain exact.",
            P(SolutionRegression, "TheModulePublicSurfaceIsUnchanged"),
            P(SolutionRegression, "TheModuleKeptItsExactReferenceSet"),
            P(SolutionRegression, "TheModuleStillDeclaresNoPackageOrFrameworkReference"),
            P(SolutionRegression, "TheEmbeddedMappingResourcesKeepTheirExactLogicalNames")),
        A("`ThePortalNamesNoSpec009Type`", "Preserved",
            "The original source and project scan remains live unchanged.",
            P(SolutionRegression, "ThePortalNamesNoSpec009Type")),
        A("`TheCompiledPortalDoesNotReferenceCaseReview`", "Preserved",
            "The original compiled-assembly exclusion remains live unchanged.",
            P(SolutionRegression, "TheCompiledPortalDoesNotReferenceCaseReview")),
        A("`TheReadmeMakesNoClaimThePortalCannotBack`", "exact capability inventory",
            "Shipped capabilities and every unshipped surface are asserted separately and exactly.",
            P(RepositoryVerification, "TheReadmeNamesTheFoundationCapabilitiesThatShip"),
            P(RepositoryVerification, "TheReadmeExplicitlyDisclaimsEveryUnshippedSurface")),
        A("`TheReadmeDocumentsEveryProjectTreeThatExists`", "Extended with the new trees",
            "The tree, delivered-scope, and resolved-versus-reserved roadmap assertions remain live.",
            P(RepositoryVerification, "TheReadmeDocumentsEveryProjectTreeThatExists"),
            P(RepositoryVerification, "TheRootInstructionsRecordTheDeliveredScope"),
            P(RepositoryVerification, "TheForwardRoadmapRecordsTheDeliveredReviewAndReservedIngress")),
        A("`TheReadmeSaysPlainlyThatNoTaxpayerDataIsRendered`", "Replaced by the exact first-surface inventory",
            "The one rendered surface, no-SIN boundary, no-count, root-rule, and untouched-module assertions remain live.",
            P(RepositoryVerification, "TheReadmeDefinesTheOneRenderedSurfaceAndNoSinBoundary"),
            P(RepositoryVerification, "TheReadmeAssertsNoProjectCount"),
            P(RepositoryVerification, "TheRootInstructionsStillForbidATotalProjectCount"),
            P(RepositoryVerification, "NoExistingModuleInstructionFileWasRewrittenForTheHost")),
        A("`HealthEndpointTests.cs` | Every assertion", "Preserved unchanged",
            "The fixed body, GET-only, disclosure, unmatched-route, and route-case assertions remain live.",
            P(HealthEndpoint, "HealthReturnsTheFixedLiteralWithTheHealthyStatus"),
            P(HealthEndpoint, "HealthIsGetOnly"),
            P(HealthEndpoint, "HealthDisclosesNothingAboutTheSystem"),
            P(HealthEndpoint, "AnUnmatchedRouteInProductionReturns404WithNoDiagnosticDetail"),
            P(HealthEndpoint, "RouteMatchingIsCaseInsensitive")),
        A("`ShellPageTests.cs` | Every assertion", "Preserved",
            "Every non-inventory shell assertion remains live and byte-exact.",
            P(ShellPage, "TheRootReturnsTheFixedShellDocument"),
            P(ShellPage, "TheShellCarriesNoDynamicValue"),
            P(ShellPage, "TheShellReferencesNoExternalAsset"),
            P(ShellPage, "TheRootIsGetOnly")),
    ];

    [Fact]
    public void EveryRequirementAmendmentNamesALiveStrictlyNarrowerProof()
    {
        var rows = AmendmentRows(
            "### Spec 010 PRD requirements superseded",
            "### Shipped test assertions superseded");

        AssertCompleteLedger(rows, RequirementAmendments);
    }

    [Fact]
    public void EveryShippedAssertionAmendmentHasANamedReplacementOrPreservation()
    {
        var rows = AmendmentRows(
            "### Shipped test assertions superseded",
            "### Spec 012's draft PRD");

        AssertCompleteLedger(rows, ShippedAssertionAmendments);
    }

    [Fact]
    public void NoSupersededAssertionRowCanDisappearWithoutFailingTheAudit()
    {
        Assert.Equal(13, RequirementAmendments.Length);
        Assert.Equal(28, ShippedAssertionAmendments.Length);
        Assert.Equal(
            RequirementAmendments.Length + ShippedAssertionAmendments.Length,
            RequirementAmendments
                .Concat(ShippedAssertionAmendments)
                .Select(amendment => amendment.RowKey)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void VerificationRecordsEveryAcceptanceCriterionExactlyOnce()
    {
        var verification = File.ReadAllLines(Path.Combine(
            RepositoryRoot,
            "specs",
            "011-tenant-identity-and-persistence-foundation",
            "verification.md"));
        var criterion = new Regex(
            @"^\|\s*AC-(?<number>\d+)\s*\|",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        var recorded = verification
            .Select(line => criterion.Match(line))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups["number"].Value))
            .ToArray();

        Assert.Equal(Enumerable.Range(1, 191), recorded);
    }

    private static void AssertCompleteLedger(
        IReadOnlyList<string> rows,
        IReadOnlyList<Amendment> ledger)
    {
        Assert.Equal(ledger.Count, rows.Count);

        foreach (var row in rows)
        {
            var amendment = Assert.Single(
                ledger,
                candidate =>
                    row.Contains(candidate.RowKey, StringComparison.Ordinal));

            Assert.False(string.IsNullOrWhiteSpace(amendment.ReplacementName));
            Assert.Matches(
                new Regex(
                    @"\b(exact|only|every|no|unchanged|preserv|forbid|narrow|fixed|allow|constrain|reduce|remain)\w*\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)),
                amendment.WhyNarrower);
            Assert.NotEmpty(amendment.Proofs);

            foreach (var proof in amendment.Proofs)
            {
                AssertLiveTestProof(proof);
            }
        }

        foreach (var amendment in ledger)
        {
            Assert.Single(
                rows,
                row => row.Contains(amendment.RowKey, StringComparison.Ordinal));
        }
    }

    private static void AssertLiveTestProof(TestProof proof)
    {
        var lines = File.ReadAllLines(Absolute(proof.RelativePath));
        var methodLine = Array.FindIndex(
            lines,
            line =>
                Regex.IsMatch(
                    line,
                    $@"^\s*public\s+(?:async\s+)?(?:void|Task)\s+{Regex.Escape(proof.Method)}\s*\(",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)));

        Assert.True(
            methodLine >= 0,
            $"{proof.RelativePath} does not contain the named replacement test {proof.Method}.");

        var previousMethod = Array.FindLastIndex(
            lines,
            methodLine - 1,
            line =>
                Regex.IsMatch(
                    line,
                    @"^\s*public\s+(?:async\s+)?(?:void|Task)\s+\w+\s*\(",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)));
        var attributeStart = previousMethod < 0 ? 0 : previousMethod + 1;
        var attributes = string.Join("\n", lines[attributeStart..methodLine]);
        Assert.True(
            attributes.Contains("[Fact", StringComparison.Ordinal) ||
            attributes.Contains("[Theory", StringComparison.Ordinal),
            $"{proof.RelativePath}.{proof.Method} is not an executable xUnit test.");

        var nextTest = Array.FindIndex(
            lines,
            methodLine + 1,
            line =>
                Regex.IsMatch(
                    line,
                    @"^\s*\[(?:Fact|Theory)",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)));
        var end = nextTest < 0 ? lines.Length : nextTest;
        var body = string.Join("\n", lines[methodLine..end]);

        Assert.Matches(
            new Regex(
                @"\bAssert\w*(?:\.|\s*\()",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)),
            body);
    }

    private static IReadOnlyList<string> AmendmentRows(string startHeading, string endHeading)
    {
        var prd = File.ReadAllText(PrdPath);
        var start = prd.IndexOf(startHeading, StringComparison.Ordinal);
        var end = prd.IndexOf(endHeading, start + startHeading.Length, StringComparison.Ordinal);

        Assert.True(start >= 0, $"Missing PRD heading: {startHeading}");
        Assert.True(end > start, $"Missing PRD heading after {startHeading}: {endHeading}");

        return prd[start..end]
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line =>
                line.StartsWith("|", StringComparison.Ordinal) &&
                !line.Contains("---", StringComparison.Ordinal) &&
                !line.Contains("Spec 010 obligation", StringComparison.Ordinal) &&
                !line.Contains("File (spec 010 task)", StringComparison.Ordinal))
            .ToArray();
    }

    private static Amendment A(
        string rowKey,
        string replacementName,
        string whyNarrower,
        params TestProof[] proofs) =>
        new(rowKey, replacementName, whyNarrower, proofs);

    private static TestProof P(string relativePath, string method) =>
        new(relativePath, method);

    private static string Absolute(string relativePath) =>
        Path.Combine(
            RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

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

    private sealed record Amendment(
        string RowKey,
        string ReplacementName,
        string WhyNarrower,
        IReadOnlyList<TestProof> Proofs);

    private sealed record TestProof(string RelativePath, string Method);
}
