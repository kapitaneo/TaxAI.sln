# Verification — Spec 015: T4 and Medical Document Extraction

**Task:** T07 — Cross-module verification.
**Scope:** integration proof only. T07 added no production code, declared no
contract, registered no project, changed no existing test, and raised no floor.

## What T07 added

Four test files, and these two documents:

| File | What it proves |
|---|---|
| `tests/Persistence.Tests/EndToEnd/DocumentExtractionPersistenceTests.cs` | The real extraction workflows driving the real `SqlExtractionStore`, `SqlIntakeStore`, and `SqlReviewRecordRepository` against a real relational database with the three real migrations applied, ending in a persisted review record and its one-time association. |
| `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionAcceptanceTests.cs` | The same journey over HTTP through the real portal composition, the real deterministic provider, and the real Development encrypted content reader — including a correction, a duplicate candidate and its resolution, and a classification override. |
| `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionSecurityAcceptanceTests.cs` | Cross-tenant and safe-`404` proof that a firm-G resource is byte-identical to a missing one on every extraction route, plus the anonymous, revoked-session, inactive-membership, and antiforgery refusals. |
| `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionPiiSweepTests.cs` | The capture sweep — a planted SIN, source-byte marker, provider label, and patient name, scanned across every HTTP response, browser model, URL, cookie, header, log record, exception message, and web-root file — plus the no-network-egress check. |

## How it was verified

**Substitution is limited to the three SQL-Server-backed collaborators.** In the
portal suite `IIntakeStore`, `IExtractionStore`, and `IReviewRecordRepository`
are in-memory; each one's relational counterpart is exercised for real in the
persistence suite. Everything a request touches — routing, authentication,
firm-scoped authorization, antiforgery, both extraction workflows, the
deterministic provider, the encrypted content reader, the whole engine chain,
and every Razor component — is production code.

In the persistence suite the document-intelligence provider and the content
reader are supplied by the test, because they are the module's two outward
seams; their real Development implementations are driven over HTTP in the portal
suite instead.

### Environment caveat — read this before trusting the run

**`pwsh ./scripts/Invoke-SolutionTests.ps1` was not run, and could not be.** The
container this task ran in has neither PowerShell nor the SDK `global.json` pins
(`10.0.302`, `rollForward: disable`); only SDK `10.0.112` could be obtained. The
suite was therefore run with `dotnet test TaxAI.sln` from a working directory
outside the repository, so the SDK pin did not apply and **no repository file was
modified to make the run possible**.

That substitution is exactly the hole `Invoke-SolutionTests.ps1` exists to close,
so this run is **not** the AC-44 gate. What it does establish is a
before-and-after comparison on an identical environment:

| Project | Baseline (`master`) | After T07 | Δ | Floor |
|---|---:|---:|---:|---:|
| TaxLogicEngine.Tests | 291 / 0 failed | 291 / 0 failed | 0 | 291 |
| DocumentExtraction.Tests | 676 / 4 failed | 676 / 4 failed | 0 | 676 |
| TaxCases.Tests | 340 / 4 failed | 340 / 4 failed | 0 | 340 |
| CaseProcessing.Tests | 609 / 0 failed | 609 / 0 failed | 0 | 609 |
| CaseReview.Tests | 482 / 0 failed | 482 / 0 failed | 0 | 482 |
| Tenancy.Tests | 289 / 0 failed | 289 / 0 failed | 0 | 289 |
| ClientIntake.Tests | 157 / 0 failed | 157 / 0 failed | 0 | 157 |
| ExtractionWorkflow.Tests | 356 / 0 failed | 356 / 0 failed | 0 | 356 |
| DocumentIntelligence.Tests | 65 / 0 failed | 65 / 0 failed | 0 | 65 |
| **Persistence.Tests** | 236 / 1 failed | **248** / 1 failed | **+12** | 236 |
| **TaxAI.Portal.Tests** | 641 / 5 failed | **659** / 5 failed | **+18** | 641 |

**The failure set is identical before and after: the same 14 tests, for the same
reasons, none of them T07's.** All 14 are environmental, in three groups:

1. **Windows line endings.** `.gitattributes` declares `* text=auto`, so files
   are stored LF and checked out CRLF on Windows and LF on Linux. Every guard
   that compares recorded file *bytes* therefore cannot match here — the four
   `TaxCases.Tests` and four `DocumentExtraction.Tests` repository-boundary
   guards, and `EfUpgradeBaselineTests.MigrationSetAndBytesMatchTheApprovedNet8BaselineExactly`.
2. **Windows path separators.** `SolutionAndRegressionTests.EverySolutionTestProjectIsRegisteredInTheFullSuiteGate`,
   `UpgradeBaselineContractTests.RepositoryGraphMigrationBytesAndFloorsMatchTheApprovedBaseline`,
   `UpgradeBaselineContractTests.UserDeclaredPublicAndInternalContractsMatchTheApprovedBaseline`,
   and `PortalSecurityTests.ThePortalShipsNoHandwrittenJavaScript` build
   comparison paths with `\`.
3. **The SDK pin.** `UpgradeBaselineContractTests.PinnedSdkAndEfToolRestoreAndResolveAtExactVersions`
   requires SDK `10.0.302`; `10.0.112` was what could be installed.

**Both floors are met without being raised.** `Persistence.Tests` runs 248
against a floor of 236 and `TaxAI.Portal.Tests` runs 659 against 641, so the gate
passes on the counts alone. `scripts/test-projects.json` is not in T07's file
list and was not touched; T07 adds no test project, so nothing there requires
registration.

**AC-44 remains unproven and must be run on a Windows host before merge.**

---

## Blocker — T07 cannot be merged until one T06-owned guard is amended

`TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.NoT07VerificationSurfaceWasIntroduced`
asserts that **this file does not exist**:

```csharp
Assert.False(File.Exists(Path.Combine(
    RepositoryRoot, "specs", "015-t4-and-medical-document-extraction",
    "verification.md")));
```

It was correct when T06 wrote it: it proved T06 stayed in its lane and shipped no
T07 behaviour. But it is bounded to the repository's future rather than to T06's
own range, so **it fails the moment T07 does the one thing T07 exists to do.**
Creating `verification.md` is the first item in T07's file list; this guard makes
that deliverable and a green suite mutually exclusive.

This is structurally the same defect AC-45's third amendment describes for the
spec 006 and spec 007 historical guards — a guard whose comparison ends at
`HEAD` instead of at the end of the specification it protects.

**It is a defect in T06 and T07 may not fix it:**
`PreservedSurfaceTests.cs` is not in T07's file list, and T07 changes no existing
test. The fix is one line, and it belongs to whoever owns T06:

- delete the `verification.md` existence assertion, which has served its purpose
  now that T07 has run; **or**
- bound it to T06's own range, the way AC-45's third amendment bounds the other
  two — assert that the file did not exist *at T06's completion commit*.

The second assertion in that test — that no test file is named `*CrossModule*` —
still passes and should be kept. T07's four files are named
`DocumentExtraction*`.

Verified by removing `verification.md` and re-running the guard: it passes.
Nothing else about T07 trips it.

---

## Findings

T07 repairs nothing. The blocker above and both findings below are recorded
and referred back, not fixed.

### Finding 1 — three extraction mutations never validate their antiforgery token (AC-16 **NOT MET**, defect in T06)

All six extraction mutation routes carry `RequiresValidation` antiforgery
metadata, and `ExtractionRouteTests.EveryRouteIsFirmScopedAndEveryMutationRequiresAntiforgery`
asserts exactly that. But in the minimal-API pipeline the token is validated when
the form is **read** during parameter binding, so a handler that binds no
`[FromForm]` value never reads the form and never has its token checked.

Measured, posting with no token at all:

| Route | Binds a form value | Without a token |
|---|---|---|
| `POST …/extract` | no | **`302 Found` — executed** |
| `POST …/accept` | no | **`302 Found` — executed** |
| `POST …/extractions/finalize` | no | **`302 Found` — executed** |
| `POST …/classification` | yes | `400 Bad Request` |
| `POST …/corrections` | yes | `400 Bad Request` |
| `POST …/extractions/duplicates/{id}` | yes | `400 Bad Request` |
| `POST /intake-cases` (spec 014 control) | yes | `400 Bad Request` |

AC-16 requires that "every mutation is a non-GET protected by antiforgery".
Three of the six are not, and one of them — `finalize` — is the step that creates
the persisted review record.

**Mitigation, which is real but is not the criterion.** The accountant session
cookie is `SameSite=Lax` (`src/TaxAI.Portal/PortalIdentity.cs:188`), and a
browser does not attach a `Lax` cookie to a cross-site `POST`. A classic
cross-site forgery therefore arrives unauthenticated and is refused by the
firm-scoped policy. That is a browser-enforced defence, not the application's own
guarantee, and it does not cover a same-site attacker.

Recorded by
`DocumentExtractionSecurityAcceptanceTests.OnlyTheFormBindingMutationsActuallyValidateTheirAntiforgeryToken`,
which pins the current split deliberately. **When T06 closes the gap that test
will fail**, and the correct response then is to move the three routes into the
refused set — not to relax the assertion.

### Finding 2 — no client-uploaded document is recognizable to the shipped provider (observation, not a defect)

`IntakeUploadValidator` requires an uploaded document to begin with a PDF, JPEG,
or PNG signature. `DeterministicDocumentIntelligenceProvider` requires the first
line of substance to be the literal `TAXAI-DETERMINISTIC-DOCUMENT/1`, and PNG and
JPEG magic bytes are not even valid UTF-8. No byte sequence satisfies both, so a
document uploaded through the client route always classifies `Unsupported`.

This is a property of the **Development-only** adapter, not of the extraction
path: the whole point of FR-37 … FR-40 is that no real provider ships, and a real
one would read a PDF. It is recorded here only so a reader is not surprised, and
so nobody later reads a green suite as proof that the uploaded-document path
works end to end. The upload leg itself is proved by spec 014 T04; T07's portal
suite stages source bytes through the host's own real content store and then
drives extraction from there.

---

## Acceptance criteria

`T07` means the criterion is proved by a test this task added; any other
reference names the suite that proves it.

### Reuse and single authority

| AC | Verdict | Evidence |
|---|---|---|
| AC-1 — one ingress implementation, no second envelope/validator/provenance type | MET | `ExtractionWorkflow.Tests/BoundaryTests.NoNewTaxYearOrDocumentKindAuthorityIsDeclared`, `TheCoordinationModuleImplementsNeitherOfItsOwnPorts` |
| AC-2 — medical reuses `GenericEvidence` / `MedicalExpenseEvidence` | MET | `BoundaryTests.NoNewTaxYearOrDocumentKindAuthorityIsDeclared`; T07 medical journey uses `DocumentCategory.MedicalExpenseEvidence` unchanged |
| AC-3 — no new tax authority outside `TaxLogicEngine` | MET | `BoundaryTests.NoCraLineNumberLiteralAppearsInEitherModule`, `ProductionSourceContainsNoForbiddenToken` |
| AC-4 — no arithmetic or rounding; `decimal` only for confidence | MET | `BoundaryTests.ProductionSourceContainsNoForbiddenToken` |
| AC-5 — the engine chain is invoked, not reimplemented | MET | `IngestWorkflowTests.TheEngineProducesTheSlipAndTheMappingRatherThanThisModule`; **T07** `DocumentExtractionPersistenceTests.AT4DocumentIsExtractedCorrectedAcceptedAndFinalizedIntoAPersistedReviewRecord` |
| AC-6 — engine errors surface verbatim and in order | MET | `IngestWorkflowTests.EveryEngineErrorBecomesExactlyOneFieldOutcomeInOrder`, `IngestPiiTests.EngineErrorsAreSurfacedVerbatimAndStillCarryNoSin` |

### Tax year and year isolation

| AC | Verdict | Evidence |
|---|---|---|
| AC-7 — the year comes from the stored case, every supplied value ignored | MET | `IngestWorkflowTests.TheYearComesFromTheStoredCaseAndFromNowhereElse`; **T07** `DocumentExtractionSecurityAcceptanceTests.ARouteNamingAnotherFirmIsRefusedEvenWithThatFirmsOwnIdentifiers` (supplies `taxYear` in a query string and it is ignored) |
| AC-8 — document year mismatch fails closed, neither year coerced | MET | `IngestWorkflowTests.ADocumentYearThatDisagreesWithTheCaseFailsClosedThroughTheExistingGate`; **T07** `DocumentExtractionPersistenceTests.NoAdjacentYearCanBeStoredAndADocumentYearMismatchFailsClosed` |
| AC-9 — no adjacent-year fallback | MET, **and stronger than specified** | `IngestWorkflowTests.AnAdjacentCaseYearFailsClosedRatherThanFallingBack`; **T07** proved the relational schema refuses a non-2025 case year outright (`CK_IntakeCases_TaxYear`), so a 2024 or 2026 case cannot be presented to extraction at all |
| AC-10 — no new year authority declared | MET | `BoundaryTests.NoTaxYearLiteralAppearsInEitherModule`, `NoNewTaxYearOrDocumentKindAuthorityIsDeclared` |

### Scope, tenancy, and safe failure

| AC | Verdict | Evidence |
|---|---|---|
| AC-11 — firm scope is server-derived; a bound firm field is refused | MET | `ScopeAndTenancyTests.NoCommandOrViewCanCarryAScopeACallerSupplied`; **T07** `…SecurityAcceptanceTests.ARouteNamingAnotherFirmIsRefusedEvenWithThatFirmsOwnIdentifiers` (query, header, and cookie channels all tried) |
| AC-12 — foreign-firm document is byte-identical to a missing one | MET | **T07** `…SecurityAcceptanceTests.AFirmGResourceIsByteIdenticalToAMissingOneOnEveryRoute` — status, body bytes, and header signature compared on all eight routes |
| AC-13 — revoked session fails before any work | MET | **T07** `…SecurityAcceptanceTests.ARevokedSessionAndAnInactiveMembershipEachFailBeforeAnyWork` — no extraction history exists afterwards |
| AC-14 — inactive membership fails before any work | MET | same test, second half |
| AC-15 — no firm-omitting taxpayer-data member | MET | `ScopeAndTenancyTests.EveryTaxpayerDataMemberTakesATrustedFirmWithNoOmittingOverload`, `SqlExtractionStoreTests.EveryTaxpayerDataMemberTakesATrustedFirmFirstWithNoOmittingOverload` |
| AC-16 — every mutation is a non-GET protected by antiforgery; no anonymous path | **NOT MET** | **Finding 1.** The non-GET and no-anonymous halves hold (**T07** `…SecurityAcceptanceTests.NoExtractionMutationAnswersAGet`, `NoAnonymousOrClientRequestReachesAnyExtractionRoute`). The antiforgery half fails for `/extract`, `/accept`, and `/finalize` |
| AC-17 — no background initiator | MET | `ScopeAndTenancyTests.TheIngestContractDeclaresNoMemberABackgroundInitiatorCouldUse`; **T07** `…SecurityAcceptanceTests.TheContentReadPortHasExactlyOneConsumerAndNoBackgroundInitiator` (the built host's `IHostedService` inventory) |

### Untrusted input and fail-closed behaviour

| AC | Verdict | Evidence |
|---|---|---|
| AC-18 — provider that throws fails closed | MET | `IngestFailClosedTests.AProviderThatThrowsDuringClassificationStoresNothing`, `…DuringExtractionStoresNothing`; **T07** `DocumentExtractionPersistenceTests.AProviderThatThrowsOrReturnsNothingStoresNoPartialState` against the real store |
| AC-19 — provider timeout fails closed | MET | `IngestFailClosedTests.AProviderThatCancelsStoresNothingAndThrowsNothingToTheCaller`, `AProviderThatNeverReturnsIsBoundedByTheConfiguredCallLimit` |
| AC-20 — malformed provider response fails closed | MET | `IngestFailClosedTests.AnEmptyClassificationResultFailsClosed`, `ANullBoxCollectionFailsClosedBeforeTheEngineIsReached`, `ABoxWithNoCodeFailsClosedBeforeTheEngineIsReached` |
| AC-21 — unclassified document produces no tax data, audit event only | MET | `IngestWorkflowTests.AnUnrecognizedDocumentProducesOneAuditEventAndNothingElse`; **T07** `DocumentExtractionPersistenceTests.AnUnrecognizedDocumentPersistsAnAuditEventAndNothingElse` (every written row is in `ExtractionAuditEvents`) and `DocumentExtractionAcceptanceTests.AnUnrecognizedDocumentProducesNoTaxDataAndOnlyAnAuditEvent` |
| AC-22 — confidence never gates, at any stage | MET | `IngestWorkflowTests.ConfidenceZeroAndOneFollowTheSamePath`; **T07** `DocumentExtractionAcceptanceTests.ConfidenceNeitherGatesNorAutoAcceptsAtAnyStage` drives `0m` and `1m` through extraction, document acceptance, and case acceptance |
| AC-23 — out-of-range confidence fails closed through the **existing** validator | MET | `IngestWorkflowTests.AnOutOfRangeConfidenceFailsClosedThroughTheExistingValidator` |
| AC-24 — duplicate box codes still reach the parser | MET | `IngestWorkflowTests.DuplicateBoxCodesReachTheParserSoItsOwnRejectionFires`, `DeterministicProviderTests.ADuplicatedBoxIsCarriedTwiceSoTheEngineCanRefuseIt` |
| AC-25 — determinism | MET | `IngestWorkflowTests.TheSameDocumentCaseInstantAndProviderProduceEqualResults`, `DeterministicProviderTests.TheSameBytesAlwaysProduceTheSameAnswer` |
| AC-26 — no clock, environment, current-user, or `HttpContext` read | MET | `ScopeAndTenancyTests.TheWorkflowNamesNoClockEnvironmentCurrentUserOrHttpContext` |

### PII

| AC | Verdict | Evidence |
|---|---|---|
| AC-27 — no SIN in any structured surface, end to end | MET | **T07** `DocumentExtractionPiiSweepTests.NoPlantedMarkerReachesAnyResponseUrlCookieHeaderLogOrWebRootFile` (responses, browser models, URLs, cookies, headers, logs, exceptions) and `DocumentExtractionPersistenceTests.NoColumnOfAnyExtractionRowCarriesASinASourceByteOrAProviderPayload` (every column of all ten extraction tables) |
| AC-28 — no new SIN representation | MET | `SinInvariantTests.NoPublicMemberNamesASinOrAWayToDisguiseOne`, `TheModuleNeverNamesTheEnginesMaskedIdentifier` |
| AC-29 — raw payload omitted from every output | MET | `IngestPiiTests.TheRawPayloadNeverLeavesTheOperation`, `SinInvariantTests.SerializingAnyResultShapeEmitsNoNineDigitRun`; **T07** `DocumentExtractionPersistenceTests.NoDefensiveRenderingOfAResultOrAStoredFactCarriesTaxpayerText` |
| AC-30 — no source byte escapes | MET | `IngestPiiTests.ThePlantedSourceMarkerReachesNothingTheWorkflowProduces`; **T07** both sweeps carry a planted `MARKER-…-SOURCE-BYTES` |
| AC-31 — no content-serving route | MET | `PreservedSurfaceTests.ExtractionSurfaceCannotReturnOrNameSourceContent`; **T07** `DocumentExtractionAcceptanceTests.ExtractionAddedNoContentServingRouteAndChangedNoExistingSurface` and `DocumentExtractionPiiSweepTests.NoFileUnderTheWebRootEverHoldsSourceContent` (seven candidate paths, plus the content key itself, all `404`) |
| AC-32 — fixed, non-interpolated error messages | MET | `SinInvariantTests.NoErrorMessageCanCarryAValue`, `EveryDeclaredErrorMessageIsAFixedConstantWithNoFormattingHole` |

### Immutability and audit

| AC | Verdict | Evidence |
|---|---|---|
| AC-33 — append-only history | MET | `SqlExtractionStoreTests.TheContractDeclaresNoUpdateOrDeleteMember`, `TheStoreItselfIssuesNoUpdateOrDeleteCall`; **T07** `DocumentExtractionPersistenceTests.EveryStoredFactIsAppendedAndNoEarlierRowIsRewritten` compares every row before and after five further facts |
| AC-34 — audit is structural only | MET | `SqlExtractionStoreTests.AnAuditRowCarriesOnlyStructuralFacts`; **T07** `DocumentExtractionPersistenceTests.EveryAppendedAuditRowIsStructuralOnly` scans the audit table for ten forbidden values |

### Boundary and dependency direction

| AC | Verdict | Evidence |
|---|---|---|
| AC-35 — no provider SDK in any project | MET | `NoEgressTests.NoProjectInTheRepositoryDeclaresAProviderOrTransportPackage`, `NoDeterministicTaxModuleReferencesTheAdapterProject` |
| AC-36 — deterministic modules stay clean | MET | `BoundaryTests.TheCoordinationModuleReferencesNoInfrastructureOrHost`, `TheCompiledCoordinationModuleNamesOnlyApprovedNonFrameworkAssemblies` |
| AC-37 — `Persistence` the only EF project, portal the only host | MET | `PersistenceBoundaryTests`, `PortalBoundaryTests`, `ProjectStructureTests` |
| AC-38 — no production provider ships | MET | `ExtractionStartupGuardTests.DeployableNonDevelopmentStartupRefuses`, `FailClosedTests.RegistrationRefusesAHostThatIsNotDevelopmentOrTest` |
| AC-39 — startup fails closed on invalid provider/reader configuration | MET | `ExtractionStartupGuardTests.MissingEmptyAndInvalidProviderOrReaderRefuses`, `InvalidProviderBoundsAndLabelRefuse` |

### Preserved behaviour

| AC | Verdict | Evidence |
|---|---|---|
| AC-40 — the spec 012 review route is untouched | MET | `PreservedSurfaceTests`; **T07** `DocumentExtractionAcceptanceTests.ExtractionAddedNoContentServingRouteAndChangedNoExistingSurface`, and the main journey renders a record the extraction flow itself created through that unchanged route |
| AC-41 — spec 014 surfaces untouched; `/health` byte-identical | MET | `PreservedSurfaceTests.ExistingIntakeTransportPoliciesCookieAndComponentsAreByteIdentical`; **T07** asserts `/health` returns the literal `Healthy` |
| AC-42 — schema change is additive only | MET | `MigrationTests.ExactlyOneInitialAndOneAdditiveMigrationAndOneSnapshotExist`, `AdditiveMigrationCreatesAndDropsOnlyTheIntakeTables`; **T07** applies all three real migrations to a real database in every persistence test |
| AC-43 — existing contracts unchanged except the three approved parser changes | MET | `UpgradeBaselineContractTests.UserDeclaredPublicAndInternalContractsMatchTheApprovedBaseline` — **fails in this environment on Windows path separators only** (see caveat); must be confirmed green on a Windows host |
| AC-44 — full suite green under `Invoke-SolutionTests.ps1` | **NOT VERIFIED** | The gate script could not run here (no PowerShell, no SDK `10.0.302`). `dotnet test TaxAI.sln` shows +30 tests, all new tests passing, and an unchanged failure set of 14 environmental failures. **Run the gate on Windows before merge.** |
| AC-45 / AC-45a — only the authorized existing test files modified | MET | `git status` for T07 shows four new files and nothing else; T01 owns the four files this criterion names |
| AC-46 — no total or closed project-set assertion | MET | `BoundaryTests.TheSolutionDeclaresEachProjectExactlyOnceAtItsApprovedPath`; T07 adds no repository-level assertion at all |
| AC-47 — static asset middleware unchanged | MET | `PreservedSurfaceTests.StaticSsrAndStaticAssetPostureRemainClosed` |

### Source-byte read (FR-31 … FR-36)

| AC | Verdict | Evidence |
|---|---|---|
| AC-48 — `IIntakeContentStore` still has exactly its two members | MET | `PreservedSurfaceTests`; `ExtractionPayloadBuilderTests.TheReadPortRequiresAllFourScopeArgumentsWithNoWayToOmitOne` |
| AC-49 — the read port requires the full four-part scope | MET | `ExtractionPayloadBuilderTests.TheReadPortRequiresAllFourScopeArgumentsWithNoWayToOmitOne` |
| AC-50 — a content key alone reads nothing | MET | `ContentReaderTests.FourPartStoredScopeIsRequiredBeforeContentIsOpened`, `MatchingForgedMetadataCannotBypassFirmAndCaseKeyDerivation`; **T07** `DocumentExtractionPersistenceTests.AContentKeyAloneReadsNothingOutsideItsOwnFourPartScope` |
| AC-51 — the extraction workflow is the port's only consumer | MET | `PreservedSurfaceTests.ReaderIsNotResolvableAsAGeneralOrBackgroundService`; **T07** `…SecurityAcceptanceTests.TheContentReadPortHasExactlyOneConsumerAndNoBackgroundInitiator` resolves the built container and gets null |
| AC-52 — a read requires a live firm-scoped accountant request | MET | **T07** `…SecurityAcceptanceTests.NoAnonymousOrClientRequestReachesAnyExtractionRoute` and `ARevokedSessionAndAnInactiveMembershipEachFailBeforeAnyWork` cover all four cases |
| AC-53 — the source-byte invariant holds verbatim | MET | **T07** `DocumentExtractionPiiSweepTests` (every response, model, log, header, and web-root file) and `DocumentExtractionPersistenceTests` (every SQL column) |

### No external provider (FR-37 … FR-40)

| AC | Verdict | Evidence |
|---|---|---|
| AC-54 — no taxpayer-content network egress | MET | `NoEgressTests.TheWholeFlowCompletesWithoutPullingInAnyNetworkingAssembly`, `TheTrackerWouldNoticeAnAssemblyBeingPulledIn`; **T07** `DocumentExtractionPiiSweepTests.TheWholeFlowCompletesWithNoOutboundNetworkCallAttempted` records outbound HTTP diagnostics across the whole HTTP journey |
| AC-55 — no provider payload stored or logged | MET | `NoEgressTests.NoExecutableLineInEitherModuleLogsOrRetainsAProviderPayload`; **T07** `DocumentExtractionPersistenceTests` asserts no column holds the source text, and that the only provider-derived value stored is the existing spec 007 `ExtractionProvenance.ProviderLabel` |
| AC-56 — the adapter project carries no SDK | MET | `NoEgressTests.TheAdapterProjectDeclaresOnlyTheDependencyInjectionAbstraction` |

### Box 12 / SIN (FR-41 … FR-47)

| AC | Verdict | Evidence |
|---|---|---|
| AC-57 — Box 12 reaches the parser in memory | MET | `SinInvariantTests.ThePlantedSinReachesTheEnginePayloadAndOnlyThere`; **T07** both journeys plant a SIN in Box 12 and produce a slip |
| AC-58 — the raw SIN outlives nothing | MET | `IngestPiiTests.ThePlantedSinReachesNoStoredBoxValue`, `…NoAuditRow`, `…NoProjectionOrOutcome`; **T07** both sweeps |
| AC-59 — no route accepts a SIN; vocabulary excludes Box 12 | MET | `ExtractionAllowlistTests.CorrectionVocabularyIsClosedAndHasNoSinMember`; **T07** `DocumentExtractionAcceptanceTests.ACorrectionNamingAProhibitedFieldIsRefusedAndMutatesNothing` submits eleven prohibited field names plus Box 12 by box code, all refused |
| AC-60 — a rejected SIN blocks acceptance, no digits in the error | MET | `IngestWorkflowTests`, `SinInvariantTests.NoErrorMessageCanCarryAValue` |
| AC-61 — correction re-extracts rather than reusing a stored payload | MET | `CorrectionChainTests.CorrectionRereadsSourceAndRerunsTheExistingGateAndEngine` |
| AC-62 — no SIN-presence signal | MET | `SinInvariantTests.NoPublicMemberCarriesASinPresenceSignal`; **T07** `DocumentExtractionPersistenceTests` asserts no `ExtractionBoxValues` row carries box code `12`, and the sweep scans for four presence-flag spellings |
| AC-63 — a valid employee copy without Box 54 is accepted | MET | `IngestWorkflowTests.AMissingBoxFiftyFourIsNeitherRequiredNorInvented`; **T07** both journeys use a Box-54-less employee copy throughout |
| AC-64 — no fake Box 54 invented | MET | `IngestWorkflowTests.AMissingBoxFiftyFourIsNeitherRequiredNorInvented`, `DeterministicProviderTests.AnEmployeeCopyWithNoBoxFiftyFourExtractsWithoutOne` |
| AC-65 — a 2025 T4 without Box 45 fails through the engine | MET | `IngestWorkflowTests.AMissingBoxFortyFiveStillFailsThroughTheEngine`; **T07** `DocumentExtractionAcceptanceTests.AnEngineRejectionIsNeverDowngradedAndBlocksAcceptance` |
| AC-66 — missing addresses do not block review and are not invented | MET | `IngestWorkflowTests.AMissingAddressReachesReviewAndIsNotSubstituted` |
| AC-67 — Box 12 and the tax-year rule unchanged | MET | `IngestWorkflowTests.ADocumentYearThatDisagreesWithTheCaseFailsClosedThroughTheExistingGate`; T01's `T4SlipParserTests` |
| AC-68 — the supersession is recorded | MET | `src/TaxLogicEngine/CLAUDE.md` records the spec 003 `[CONFIRM]` Box 54 decision as superseded by spec 015 with the canada.ca citation |

### Medical evidence (FR-54 … FR-61)

| AC | Verdict | Evidence |
|---|---|---|
| AC-69 — the T4-only review contracts unchanged | MET | `UpgradeBaselineContractTests` (see the AC-43 caveat); `ReviewRecordCreationTests.AcceptedMedicalDocumentNeverEntersAT4ReviewRecord` |
| AC-70 — the medical model is a closed field set | MET | `ResultShapeTests.TheMedicalModelIsExactlyTheApprovedClosedFieldSet` |
| AC-71 — prohibited medical content unrepresentable | MET | `ResultShapeTests.TheMedicalModelCannotRepresentProhibitedContentOrATaxJudgement` |
| AC-72 — no eligibility or claim assertion | MET | same test; `MedicalEvidenceValidatorTests.NoMemberOfTheValidatorAssertsAnythingAboutTax` |
| AC-73 — no medical tax computation | MET | `BoundaryTests.ProductionSourceContainsNoForbiddenToken`, `MedicalEvidenceValidatorTests.NoMemberOfTheValidatorAssertsAnythingAboutTax` |
| AC-74 — amounts and dates stay as printed | MET | `MedicalEvidenceValidatorTests.AReceiptIsRecordedAsANeutralEvidenceVersionWithNothingNormalized`; **T07** `DocumentExtractionPersistenceTests` round-trips `$1,120.50` and `14 March 2025` through a real database unchanged |
| AC-75 — no new mapping config or slip type | MET | `BoundaryTests.NoNewTaxYearOrDocumentKindAuthorityIsDeclared` |
| AC-76 — medical fields render only on the new surface | MET | `ExtractionAllowlistTests.FullyPopulatedMedicalProjectionEmitsOnlyTheClosedAllowlist` |
| AC-77 — the spec 012 allowlist unchanged | MET | `PreservedSurfaceTests`; **T07** asserts the medical markers reach no other response |
| AC-78 — the new surface has its own closed allowlist | MET | `ExtractionAllowlistTests.BrowserModelsHaveExactlyTheirApprovedMembers`; **T07** `DocumentExtractionPiiSweepTests.TheBrowserModelCarriesOnlyItsAllowlistedMembers` checks every server identifier against both rendered pages |
| AC-79 — provider and patient names never leave the accountant surface | MET | `IngestPiiTests.ProviderAndPatientNamesReachNoAuditRowOrOutcomeRendering`; **T07** the sweep asserts both markers appear in no URL, cookie, header, log, or exception |
| AC-80 — medical fields persist as immutable versions | MET | `SqlExtractionStoreTests.TwoMedicalExtractionsProduceTwoImmutableVersions`; **T07** `DocumentExtractionPersistenceTests.MedicalEvidenceIsExtractedAndVersionedBesideTheT4WithoutEnteringTheRecord` |

### Confidence (FR-66 … FR-68)

| AC | Verdict | Evidence |
|---|---|---|
| AC-81 — no threshold exists anywhere | MET | `BoundaryTests.ProductionSourceContainsNoForbiddenToken` |
| AC-82 — confidence does not gate acceptance | MET | **T07** `DocumentExtractionAcceptanceTests.ConfidenceNeitherGatesNorAutoAcceptsAtAnyStage` — both documents produce a record |
| AC-83 — confidence never described as accuracy | MET | `BoundaryTests.ProductionSourceContainsNoForbiddenToken` |
| AC-84 — confidence never auto-accepts | MET | **T07** same test — confidence `1m` still requires an explicit accountant action, and no record exists before it |

### Field-level validation (FR-69 … FR-72)

| AC | Verdict | Evidence |
|---|---|---|
| AC-85 — T4 validation is the engine's, surfaced per field | MET | `IngestWorkflowTests.EveryEngineErrorBecomesExactlyOneFieldOutcomeInOrder` |
| AC-86 — no new T4 rule | MET | `BoundaryTests.ProductionSourceContainsNoForbiddenToken` |
| AC-87 — a missing medical date or amount blocks acceptance | MET | `MedicalEvidenceValidatorTests.AnUnextractableDateBlocksAcceptanceAndIsNotInvented`, `AnUnextractableAmountBlocksAcceptanceAndIsNotInvented` |
| AC-88 — a missing optional medical field does not block | MET | `MedicalEvidenceValidatorTests.MissingOptionalFieldsAreSurfacedWithoutBlockingAndWithoutJudgement` |
| AC-89 — no rule forces acceptance of an engine rejection | MET | **T07** `DocumentExtractionAcceptanceTests.AnEngineRejectionIsNeverDowngradedAndBlocksAcceptance` — acceptance refused, no `Acceptances` row |

### Duplicate detection (FR-73 … FR-78)

| AC | Verdict | Evidence |
|---|---|---|
| AC-90 — byte-identical duplicate prevention unchanged | MET | `PreservedSurfaceTests.ExistingIntakeTransportPoliciesCookieAndComponentsAreByteIdentical` |
| AC-91 — semantic duplicates are firm and case scoped | MET | `DuplicateDetectorTests.OnlyTheLatestVersionOfAnAlreadyExtractedDocumentIsCompared`; **T07** `DocumentExtractionPersistenceTests.AForeignFirmSeesExactlyWhatAMissingResourceProduces` |
| AC-92 — duplicate identity never uses the SIN | MET | `DuplicateDetectorTests.TheComparisonNeverUsesTheSocialInsuranceNumberBox` |
| AC-93 — T4 duplicate comparison ignores Box 54 | MET | `DuplicateDetectorTests.TwoScansOfTheSameEmployeeCopyAreProbableDuplicatesWithoutBoxFiftyFour` |
| AC-94 — two distinct same-employer T4s are not duplicates | MET | `DuplicateDetectorTests.TwoDistinctSameEmployerSlipsAreNotDuplicates` |
| AC-95 — medical duplicate comparison uses only the approved fields | MET | `DuplicateDetectorTests.TwoIdenticalReceiptsAreProbableDuplicates`, `TwoReceiptsDifferingInAmountAreNot` |
| AC-96 — a duplicate is never auto-resolved | MET | `DuplicateDetectorTests.ASecondScanIsFlaggedAndNeitherDocumentIsDiscarded`; **T07** `DocumentExtractionAcceptanceTests` asserts both documents remain present and unaccepted while the flag stands |
| AC-97 — duplicate relationships are opaque and immutable | MET | `DuplicateDetectorTests.ADeterminationCarriesOnlyOpaqueIdentifiers`, `SqlExtractionStoreTests.AResolutionLeavesItsDeterminationRowUntouched`; **T07** `DocumentExtractionPersistenceTests.EveryStoredFactIsAppendedAndNoEarlierRowIsRewritten` |

### Corrections (FR-79 … FR-85)

| AC | Verdict | Evidence |
|---|---|---|
| AC-98 — the correction vocabulary is closed | MET | `CorrectionVocabularyTests.CorrectionVocabularyIsExactlyTheApprovedSet` |
| AC-99 — prohibited corrections are refused | MET | `CorrectionVocabularyTests.ProhibitedTargetsHaveNoCorrectionField`, `CorrectionChainTests.EveryBoxTwelveCorrectionChannelRefusesWithoutMutation`; **T07** `DocumentExtractionAcceptanceTests.ACorrectionNamingAProhibitedFieldIsRefusedAndMutatesNothing` over HTTP |
| AC-100 — each correction creates a complete immutable version | MET | `SqlExtractionStoreTests.ACorrectionRecordsTheCompleteImmutableFact`; **T07** `DocumentExtractionPersistenceTests` asserts the prior version's identifier and instant are unchanged |
| AC-101 — no SIN in a correction record | MET | `ReviewPiiTests.SinNeverAppearsInCorrectionHistoryOrPersistedReviewRecord`; **T07** the SQL column sweep covers `ExtractionCorrections` |
| AC-102 — correction re-runs the whole chain | MET | `CorrectionChainTests.CorrectionRereadsSourceAndRerunsTheExistingGateAndEngine`; **T07** both journeys correct Box 14 and get a second version |
| AC-103 — a correction cannot bypass a rejection | MET | `CorrectionChainTests.EngineInvalidCorrectionCreatesAwaitingVersionButCannotBeAccepted`; **T07** `DocumentExtractionAcceptanceTests.AnEngineRejectionIsNeverDowngradedAndBlocksAcceptance` |
| AC-104 — correction requires an explicit accountant action | MET | **T07** every journey shows `Corrections` empty until the correction POST is made |

### Review-record creation (FR-86 … FR-91)

| AC | Verdict | Evidence |
|---|---|---|
| AC-105 — no record before case-level acceptance | MET | `ReviewRecordCreationTests.ExplicitFinalizationSavesExactlyOneVersionedRecordThenAssociatesIt`; **T07** both journeys assert the record store is empty after every document is individually accepted |
| AC-106 — case-level acceptance creates exactly one versioned record plus the association | MET | **T07** `DocumentExtractionPersistenceTests.AT4DocumentIsExtractedCorrectedAcceptedAndFinalizedIntoAPersistedReviewRecord` (real repository, real association row) and `DocumentExtractionAcceptanceTests` (two accepted T4 documents in one record) |
| AC-107 — only the latest accepted version of each document is assembled | MET | `ReviewRecordCreationTests.FinalRecordUsesLatestAcceptedCorrectionNotSupersededVersion` |
| AC-108 — assembler invariants unrelaxed | MET | `ReviewRecordCreationTests.FinalizationFailsClosedWhenReplayNoLongerMatchesAcceptedVersion` |
| AC-109 — no post-finalization workflow | MET | `ReviewRecordCreationTests.PostFinalizationCorrectionOverrideAndAcceptanceAllRefuse`, `SecondFinalizationFailsClosedWithoutAnotherSaveOrAssociation`; **T07** `DocumentExtractionPersistenceTests.ASecondFinalizationOfTheSameCaseFailsClosedAndLeavesOneRecord` |
| AC-110 — the review route gains no latest selection | MET | `PreservedSurfaceTests`; **T07** `DocumentExtractionAcceptanceTests` requests a specific record and gets exactly it, and a random identifier `404`s |
| AC-111 — medical evidence never enters the T4 record | MET | `ReviewPiiTests.MedicalNamesNeverEnterT4ReviewPersistence`; **T07** `DocumentExtractionPersistenceTests.MedicalEvidenceIsExtractedAndVersionedBesideTheT4WithoutEnteringTheRecord` scans every review column for four medical markers |

### Retention and deployment refusal (FR-92 … FR-94)

| AC | Verdict | Evidence |
|---|---|---|
| AC-112 — new taxpayer data inherits the seven-year exclusive boundary | MET | `IngestWorkflowTests.EveryStoredRowCarriesTheOneExistingRetentionBoundary`, `SqlExtractionStoreTests.EveryAppendedRowCarriesTheExclusiveSevenYearBoundary`; **T07** asserts the persisted review record's `RetentionUntilUtc` equals `RetentionPolicy.ComputeRetentionUntilUtc(2025)` |
| AC-113 — retention never described as a CRA requirement | MET | `BoundaryTests.ProductionSourceContainsNoForbiddenToken` |
| AC-114 — production use remains blocked | MET | `ExtractionStartupGuardTests.DeployableNonDevelopmentStartupRefuses` |

### Classification override (FR-95 … FR-98)

| AC | Verdict | Evidence |
|---|---|---|
| AC-115 — override targets are exactly the two supported paths | MET | `ClassificationOverrideTests.OnlyTheTwoSupportedPathsCanBeOverrideTargets`; **T07** `DocumentExtractionAcceptanceTests.OnlyTheTwoSupportedClassificationsCanBeOverriddenTo` accepts two and refuses `Unsupported`, `Other`, an unrecognized value, and empty |
| AC-116 — disagreement is surfaced; absent an override the automated value selects the path | MET | `ClassificationOverrideTests.OverrideRetainsAutomatedClassificationBesideHumanDecision`; **T07** the review page renders both values |
| AC-117 — an override is immutable, scoped, and retains the original | MET | `SqlExtractionStoreTests.AnOverriddenClassificationRetainsTheAutomatedOne`; **T07** `DocumentExtractionPersistenceTests.EveryStoredFactIsAppendedAndNoEarlierRowIsRewritten` |
| AC-118 — an override bypasses no validation | MET | `ClassificationOverrideTests.OverrideAloneCannotAcceptAnExtractionFromTheOldPath`; **T07** `DocumentExtractionAcceptanceTests` shows the pre-override version stays unacceptable until the document is re-extracted under the chosen classification |
| AC-119 — `Other` never becomes tax input by default | MET | `ClassificationOverrideTests.PolicyRetainsUnsupportedAutomatedOutcomeWithoutTurningItIntoAcceptance`; **T07** `DocumentExtractionAcceptanceTests.AnUnrecognizedDocumentProducesNoTaxDataAndOnlyAnAuditEvent` |

---

## Summary

- **117 of 119 criteria: MET.**
- **AC-16: NOT MET** — three extraction mutations carry antiforgery metadata that
  is never validated. A defect in T06, referred back, partially mitigated by the
  session cookie's `SameSite=Lax` policy. See Finding 1.
- **AC-44: NOT VERIFIED** — the gate script could not run in this environment.
  Run `pwsh ./scripts/Invoke-SolutionTests.ps1` on a Windows host before merge.
- **AC-43** relies on a baseline guard that fails here on Windows path separators
  only; confirm it green in the same Windows run.
- **One T06-owned guard must be amended before this merges** — see the blocker
  at the top of this document. Until then the suite carries one structural
  failure that is the direct consequence of T07 producing its own deliverable.

The gates the PRD leaves closed are unchanged. No raw-content preview or
download, no production provider, no retention or purge path, and no deployment
authorization ships. The one raw-byte exception remains the authenticated
encrypted source object outside the web root; it may contain a SIN, it is never
described as SIN-free, and T07 confirmed no route, log, diagnostic, or SQL column
reaches it.
