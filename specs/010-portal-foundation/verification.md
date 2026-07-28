# Verification: Portal Foundation (spec 010)

**Feature class: `platform`. Tax/CRA semantic impact: none.**

AC-by-AC record for [prd.md](prd.md), implemented through
[architecture.md](architecture.md) and [tasks.md](tasks.md). Every criterion
below is either covered by a named xUnit test or by recorded command output.

**Result: all 50 acceptance criteria pass.**

## Commands

```
dotnet build TaxAI.sln --no-incremental
```

> Build succeeded. **0 Error(s), 4 Warning(s).** All four warnings are
> pre-existing in `TaxLogicEngine.Tests` (`CS0649` ×2, `xUnit2008` ×2). **Zero
> warnings originate from `TaxAI.Portal` or `TaxAI.Portal.Tests`.**

```
dotnet test TaxAI.sln
```

> Exit code 0. **2571 passed, 0 failed, 0 skipped**, across all six test
> projects, with no assembly skipped.

| Project | Before spec 010 | After spec 010 |
|---|---|---|
| `TaxLogicEngine.Tests` | 261 | 261 |
| `TaxCases.Tests` | 337 | 337 |
| `DocumentExtraction.Tests` | 673 | 673 |
| `CaseProcessing.Tests` | 594 | 594 |
| `CaseReview.Tests` | 482 | 482 |
| `TaxAI.Portal.Tests` | — | **224** (new) |
| **Total** | 2347 | **2571** |

Every pre-existing count is identical before and after: no test was removed,
skipped, renamed, or weakened.

```
dotnet run --project src/TaxAI.Portal
```

> `Now listening on: http://localhost:5199` / `Application started.`
> `GET /` → 200, `text/html; charset=utf-8`, 252 bytes.
> `GET /health` → 200, `text/plain; charset=utf-8`, 7 bytes.
> **No `Server` header emitted** — the one check `TestServer` cannot make,
> because it does not use Kestrel.

## Structure and solution (FR-1 – FR-5)

| AC | Criterion | Evidence |
|---|---|---|
| 1 | Portal project exists with the web SDK | `ProjectStructureTests.PortalProject_UsesTheAspNetCoreWebSdk` |
| 2 | `net8.0`, `Nullable`, `ImplicitUsings` | `ProjectStructureTests.PortalProject_DeclaresTheApprovedProperty` (3 cases) |
| 3 | Zero `PackageReference` in the portal | `ProjectStructureTests.PortalProject_DeclaresNoPackageReference` |
| 4 | Test tooling pinned to the repository standard | `ProjectStructureTests.TestProject_PinsTheRepositoryStandardToolingVersion` (4 cases); `TestProject_AddsOnlyTheInProcessWebTestHost` proves the only addition is `Microsoft.AspNetCore.Mvc.Testing` |
| 5 | Each new project declared exactly once at its approved path | `ProjectStructureTests.Solution_DeclaresTheProjectExactlyOnceAtItsApprovedPath` (2 cases). **Presence-and-uniqueness; no total count asserted anywhere.** |
| 6 | Existing solution entries untouched | `SolutionAndRegressionTests.EveryPreExistingSolutionDeclarationSurvivedTheAppend` + `EveryPreExistingProjectKeptItsConfigurationRows` (10 cases each), `TheTwoNewProjectsAreDeclaredExactlyOnceEach`. `git diff` on `TaxAI.sln`: **28 insertions, 0 deletions.** |
| 7 | Build succeeds | `dotnet build TaxAI.sln --no-incremental` → exit 0, 0 errors |
| 8 | Portal production references are exactly two | `ProjectStructureTests.PortalProject_ReferencesExactlyCaseProcessingAndTaxLogicEngine`; `PortalBoundaryTests.TheCompiledPortal_ReferencesExactlyCaseProcessingAndTaxLogicEngine` checks the compiled assembly |
| 9 | Test project references only the portal | `ProjectStructureTests.TestProject_ReferencesOnlyThePortal` |
| 10 | No reverse reference | `PortalBoundaryTests.NoPreExistingProjectReferencesThePortal` (10 cases — every pre-existing project tree) |
| 11 | No pre-existing file changed | `git diff --name-only 48fefb7 origin/master` (48fefb7 = last master commit before spec 010). **No path under `src/` or `tests/` for TaxLogicEngine, TaxCases, DocumentExtraction, CaseProcessing, or CaseReview appears.** Changed set is `TaxAI.sln`, `specs/010-portal-foundation/**`, `src/TaxAI.Portal/**`, `tests/TaxAI.Portal.Tests/**` only. |
| 12 | No speculative project | `SolutionAndRegressionTests.NoPlaceholderProjectExistsForAFutureModule` (Clients, Firms, Documents, Intake, Review). `CaseReview` is excluded from that list: spec 009 shipped it, so it is a real module. |

## Regression — existing behaviour stays green (FR-23)

| AC | Criterion | Evidence |
|---|---|---|
| 13 | Full suite green, same test set | `dotnet test TaxAI.sln` → 2571 passed / 0 failed / 0 skipped. Per-project counts unchanged (table above). |
| 14 | No parser, mapper, or public-surface change | `SolutionAndRegressionTests.TheModulePublicSurfaceIsUnchanged` (5 cases; 34/27/8/4/9 exported types), guarded by `TheSnapshotsCoverEveryModuleAndAreNotEmpty` |
| 15 | Embedded mapping resources untouched | `SolutionAndRegressionTests.TheEmbeddedMappingResourcesKeepTheirExactLogicalNames` |
| 16 | No new internals grant | `SolutionAndRegressionTests.TheRepositoryHasExactlyItsThreePreExistingInternalsGrants` + `NeitherNewProjectDeclaresAnInternalsGrant` |

## Host behaviour (FR-6 – FR-14)

| AC | Criterion | Evidence |
|---|---|---|
| 17 | Host starts in-process, no port bound | `CompositionRootTests.TheHostStartsInProcess`, `TheHostBindsNoNetworkPort` (resolved `IServer` is `TestServer`) |
| 18 | Container validated on build, with scopes | `CompositionRootTests.ScopeValidationIsOn_ResolvingAScopedServiceFromTheRootProviderThrows`, `ValidateOnBuildIsOn_AnUnresolvableDependencyFailsAtStartup`, `BothValidationFlagsAreSetOutsideAnyDevelopmentBranch` |
| 19 | `IT4LineMapProvider` resolves | `CompositionRootTests.TheT4MappingPortResolvesToTheEmbeddedProvider` |
| 20 | `IT5LineMapProvider` resolves | `CompositionRootTests.TheT5MappingPortResolvesToTheEmbeddedProvider` |
| 21 | `T4CaseProcessor` is constructible | `CompositionRootTests.TheT4CoordinatorIsConstructibleFromTheContainer` |
| 22 | Registrations live in one file | `PortalBoundaryTests.EveryServiceRegistrationLivesInTheCompositionRoot` (also asserts the composition root holds exactly three) |
| 23 | Health endpoint | `HealthEndpointTests.HealthReturnsTheFixedLiteralWithTheHealthyStatus` — expected body written independently of `PortalContent` |
| 24 | Ingress port not registered | `CompositionRootTests.TheUntrustedIngressPortIsNotRegistered`, `ThePortalImplementsNoIngress` |
| 25 | Shell page | `ShellPageTests.TheRootReturnsTheFixedShellDocument`, `TheShellCarriesNoDynamicValue`, `TheShellReferencesNoExternalAsset` |
| 26 | Exactly two routes, GET, no route parameter | `ShellPageTests.TheHostMapsExactlyTwoGetEndpoints`, `EveryMappedEndpointIsGetOnly`, `NoMappedEndpointTakesARouteParameter` |
| 27 | No input binding anywhere | `PortalSecurityTests.ProductionSource_ReadsNoRequestInput` (7 categories) |
| 28 | Health response discloses nothing | `PortalSecurityTests.TheHealthBodyDisclosesNothingAboutTheSystem` — asserts the body contains **no digit at all** |
| 29 | Unknown route in Production → 404, no detail | `HealthEndpointTests.AnUnmatchedRouteInProductionReturns404WithNoDiagnosticDetail` (3 cases) |
| 30 | No developer exception page outside Development | `PortalSecurityTests.AnyDeveloperExceptionPageIsConfinedToDevelopment`, `AnUnmatchedRouteLeaksNoDiagnosticDetail` |

## The line the portal may not cross (FR-15, FR-16)

| AC | Criterion | Evidence |
|---|---|---|
| 31 | No tax computation token | `PortalBoundaryTests.ProductionSource_ContainsNoForbiddenToken` — "engine parser or mapper", "rounding / money math" |
| 32 | No taxpayer data type | `PortalBoundaryTests.ProductionSource_ContainsNoForbiddenToken` — "taxpayer data type" |
| 33 | No CRA line-number literal | `PortalBoundaryTests.NoCraLineNumberLiteralAppearsInThePortal`, `ThePortalDeclaresNoBoxCodeOrCraLineConcept` |
| 34 | No mapping table or resource read | `PortalBoundaryTests.ProductionSource_ContainsNoForbiddenToken` — "mapping table or resource read" |
| 35 | No tax-year policy or year literal | `PortalBoundaryTests.NoTaxYearLiteralAppearsInThePortal`, plus the "tax-year policy" pattern |

## PII and security posture (FR-16, FR-19, FR-20, FR-22)

| AC | Criterion | Evidence |
|---|---|---|
| 36 | No PII token | `PortalSecurityTests.ProductionSource_ContainsNoPiiToken` (3 categories), `ProductionSource_ContainsNoNineDigitLiteral` |
| 37 | No response can carry taxpayer data | `PortalSecurityTests.NeitherResponseBodyContainsANineDigitRun` — reads the **live** bodies of `/` and `/health` |
| 38 | No request logging | `PortalSecurityTests.ProductionSource_LogsNoRequestContent` (3 categories) |
| 39 | No authentication or authorization surface | `PortalSecurityTests.ProductionSource_DeclaresNoAuthenticationSurface` (4 categories) |
| 40 | No infrastructure dependency | `PortalBoundaryTests.TheCompiledPortal_ReferencesExactlyCaseProcessingAndTaxLogicEngine`, `ThePortalProject_ReferencesNeitherTaxCasesNorCaseReview`, `PortalSecurityTests.ProductionSource_TakesNoInfrastructureDependency` (6 categories) |
| 41 | No secret, no non-local binding | `PortalSecurityTests.TrackedConfiguration_CarriesNoSecret`, `TrackedConfiguration_BindsOnlyToLocalhost` (2 files each) |
| 42 | No client-side stack | `PortalSecurityTests.TheRepositoryHasNoClientSideStack` |
| 43 | Repository hygiene | `git ls-files` matched against `bin/`, `obj/`, `.vs/`, `*.user`, `*.pfx`, `*.key`, `.env`, `settings.local.json` → **no matches**. `git status --porcelain` clean after each task. |

## Documentation and independence (FR-17, FR-18)

| AC | Criterion | Evidence |
|---|---|---|
| 44 | Portal instructions carry the boundary and the gate | `ProjectStructureTests.PortalInstructions_StateTheBoundaryAndTheGate` (11 required phrases), `PortalInstructions_ForbidTaxpayerDataSurfacesUntilTheClassAQuestionsAreAnswered` (7 named surfaces) |
| 45 | Pointer-only bridge | `ProjectStructureTests.PortalAgentsBridge_IsAPointerNotARulebook` |
| 46 | No existing module instruction file changed | `RepositoryVerificationTests.NoExistingModuleInstructionFileWasRewrittenForTheHost` (5 modules × 2 files), corroborated by the AC-11 diff |
| 47 | README describes the actual repository | `RepositoryVerificationTests.TheReadmeDocumentsEveryProjectTreeThatExists` (12 cases), `TheReadmeDocumentsHowToRunTheApplication`, `TheReadmeAssertsNoProjectCount` |
| 48 | README claims nothing the portal lacks | `RepositoryVerificationTests.TheReadmeMakesNoClaimThePortalCannotBack` (7 cases), `TheReadmeClaimsNoWorkingOcrOrLlmIntegration`, `TheReadmeSaysPlainlyThatNoTaxpayerDataIsRendered` |
| 49 | Root instructions record the new layout | `RepositoryVerificationTests.TheRootInstructionsListTheNewTree` (2 cases), `TheRootInstructionsRecordTheDeliveredScope` (6 cases), `TheRootInstructionsStillForbidATotalProjectCount` |
| 50 | No coupling to spec 009 | `SolutionAndRegressionTests.ThePortalNamesNoSpec009Type` (5 cases), `TheCompiledPortalDoesNotReferenceCaseReview` |

## Non-vacuity checks

Negative assertions pass when they find nothing, so several were deliberately
made to fail before being trusted:

| What | How it was falsified | Result |
|---|---|---|
| AC-18 validation flags (T02) | Both flags temporarily set to `false` | All three assertions failed; flags restored |
| AC-26 endpoint inventory (T03) | A third route with a route parameter temporarily mapped | `TheHostMapsExactlyTwoGetEndpoints` and `NoMappedEndpointTakesARouteParameter` failed; route removed |
| AC-14 public-surface snapshot (T06) | One entry removed from the test's own snapshot table | The CaseProcessing case failed; entry restored |
| Regex-based scans (T05) | `EveryForbiddenPattern_ActuallyMatchesAViolatingLine` pairs 16 patterns with a line each must match | Permanent test |
| Scanner walk and comment filter (T04, T05, T06, T07) | Guards assert the walk sees exactly the expected files, that known patterns still match, that the filter strips comments without removing code, and that documentation files are non-trivially short | Permanent tests |

T04, T05, T06, and T07 own no production file, so their assertions were **not**
proved by mutating production code — that would have violated declared-file
ownership. The built-in guards are the equivalent protection, which is why
`tasks.md` required them.

## Notes and deferrals

- **No CI workflow exists** in this repository, so no CI configuration required
  updating and none was added. `README.md` and root `CLAUDE.md` are the only
  places build/test commands live; both were updated.
- **`Microsoft.AspNetCore.Mvc.Testing` 8.0.0** restored as planned; no version
  substitution was needed. It is the only package added by this feature, it
  lives in a test project, and it binds no network port.
- **Two documentation errors were found and corrected** while re-grounding the
  spec against `master` after spec 009 merged: the PRD and architecture claimed
  two `InternalsVisibleTo` grants and named the wrong pair (there are three, in
  two declaration forms, and `TaxLogicEngine`'s is a source-level attribute a
  `.csproj`-only scan would miss); and AC-50 was rewritten from "no CaseReview
  project is declared" — unsatisfiable once spec 009 shipped — to the surviving
  intent, no spec-009 *coupling*.
- **Known hazard, not fixed by this spec.** `dotnet test TaxAI.sln` returns
  **exit code 0 when a test assembly is skipped.** During T02 a Windows Smart App
  Control policy blocked `TaxCases.Tests.dll` and 337 tests silently did not run
  while the command still reported success; during T04 the same policy blocked
  `TaxAI.Portal.dll`. Every wave gate in this workflow trusts that exit code, so
  a wave could be merged on a green result that never ran part of the suite. A
  per-project test-count floor, or `--blame`, would make a skipped assembly loud.
  Recorded here because it is a workflow defect, not a spec 010 defect. Smart App
  Control was subsequently turned off on the build machine, and every suite in
  this record ran to completion.
