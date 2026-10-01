# Tasks: Local End-to-End Accountant MVP

## Scope and approval

Feature class: **`tax-domain`**, `Tax/CRA semantic impact: none`. The
[PRD](prd.md) is authoritative; the [architecture](architecture.md) records the
design. Baseline: `7ee2574`. **Four tasks in four sequential waves.** The plan was
approved by the human on 2026-10-01; implementation begins with T01 only.

The human's explicit four-task decomposition overrides the generic
contract-only Wave 0 pattern for this spec (precedent: spec 016). Each task
ships a contract together with its working consumer and its tests.

- A human explicitly names one TaskID per `/implement`. No automatic next task,
  no batching, no self-approval, no merge.
- Every earlier wave must be merged and have its `.done` marker.
- Only the exact files listed for a task may change. A necessary unlisted file
  is a stop-and-ask, not a fix.
- Guard amendments follow the PRD's approved table and its conditions: the
  guard is kept and re-pinned to the exact approved change, never deleted or
  loosened to a pattern, and **existing test method names are kept** so
  `AmendmentAuditTests.cs` does not change.
- Each task runs `dotnet build TaxAI.sln` and
  `pwsh ./scripts/Invoke-SolutionTests.ps1`, raises the floors it earned in
  `scripts/test-projects.json`, and lowers none.
- No tax file, no spec 016 file, no `infra/azure/` or `scripts/azure/` file, no
  project file, and `TaxAI.sln` appear in any task.
- No infrastructure-hardening, Azure, recovery, deployment, or operations task
  exists. Spec 016 T07–T10 are not resumed.
- This file is read-only once approved. Completion is
  `specs/017-local-end-to-end-accountant-mvp/status/<TaskID>.done` with the PR
  number and commit SHA.

## Guards outside the approved table — authorized by the human on 2026-10-01

The PRD's approved table was compiled by reading, not by running. Planning
found further guards the feature trips. The human authorized X-1 to X-7
**exactly as described here**, each owned by the task that causes the change.
Existing `AmendmentAuditTests` method names are preserved. Any other unexpected
guard or file remains stop-and-ask.

| # | Guard (file) | Owner | Authorized scope |
|---|---|---|---|
| X-1 | `tests/TaxAI.Portal.Tests/ShellPageTests.cs` (`TheHostMapsExactlyTheApprovedRoutes`, `EachApprovedRouteAnswersExactlyItsOneMethod`, `CaseSelectorsAppearOnlyOnTheApprovedAccountantRoutes`) | T03 | only the new `GET /intake-cases` route: exact inventory plus exactly that entry. Its host is `Production`, so the Development sign-in stays absent from it unchanged. |
| X-2 | `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs` (`UserDeclaredPublicAndInternalContractsMatchTheApprovedBaseline`) | T01, T03 | only the approved additive signatures, each named exactly. T01: the `Persistence.PersistenceRegistration` overload, `Persistence.DevelopmentFixtureIdentity`, the connection-guard overload, `TaxAI.Portal.DevelopmentSignInEndpoints`, `Components.Pages.DevelopmentSignInPage`, and a shared session-creation helper if T01 extracts one. T03: `TaxAI.Portal.CaseListEndpoints`, `Components.Pages.CaseListPage`, `CaseListPageModel`, `CaseListRowModel`, `WorkflowNoticeKind`, `Components.Shared.AccountantNavigation`, `Components.Shared.WorkflowNotice`. |
| X-3 | `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs` (`ThePortalsPublicSurfaceIsExactlyProgramAndTheApprovedComponents`) | T01, T03 | only the approved new Razor/public types named in X-2 |
| X-4 | `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs` (`TheModuleExportsOnlyItsRegistrationExtension`, `TheRegistrationEntryPointRemainsTheSingleExtensionMethod`) | T01 | only the approved registration overload and the constants class |
| X-5 | Existing assertions inside already-authorized guard files: `PortalSecurityTests.cs` (`TheApprovedAuthenticationFilesAreExactlyTheEnumeratedSet`, `EveryAuthenticationTokenLivesInItsApprovedFile`, `RequestInputAppearsOnlyInItsExactApprovedFile`) and `CompositionRootTests.cs` (the `SignInAsync` confinement, `TheApprovedConcernVocabularyIsExactlyFour`) | T01 | only the Development authentication additions: each enumerated set plus exactly the Development sign-in file; the concern vocabulary becomes exactly five |
| X-6 | The five `IIntakeStore` test doubles: `tests/ClientIntake.Tests/Support/InMemoryIntakeStore.cs`, `tests/ExtractionWorkflow.Tests/Ingest/IngestWorkflowTests.cs`, `tests/Persistence.Tests/EndToEnd/ClientIntakeWorkflowPersistenceTests.cs`, `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeAcceptanceTests.cs`, `tests/TaxAI.Portal.Tests/Extraction/ContentReaderTests.cs` | T03 | compile-forced implementation of the new list member only; no unrelated assertion changes |
| X-7 | `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionAcceptanceTests.cs` | T04 | only if the end-to-end harness genuinely needs the additive helper; if not needed, the file is not edited |

`AmendmentAuditTests.cs` is **not** edited. `PreservedSurfaceTests.cs` changes
only for `IntakeCaseProgressPage.razor`, because the case-list route lives in a
new file.

### Final decisions recorded (human, 2026-10-01)

- **User secrets.** No `UserSecretsId` is added to `TaxAI.Portal.csproj`.
  `Start-LocalDemo.ps1` uses an explicit local-demo secrets id and injects the
  resolved values into the launched host process as environment variables.
  Normal test hosts must not automatically consume a developer's local secret
  store.
- **Case ordering.** Newest first comes from the immutable stored case-created
  audit instant; no schema migration is added solely for a creation column.
  Ordering is deterministic: created instant descending, then a stable opaque
  case-id tie-breaker. Still firm-scoped and capped at 100.
- **Refusal notices.** The closed vocabulary is exactly: correction + `Invalid`;
  correction + `Conflict`; finalization + `Invalid`; finalization + `Conflict`.
  Fixed, value-free text only. `422` for `Invalid` and `409` for `Conflict`,
  with page re-render. Workflow `NotFound`, missing or foreign resources,
  session, membership, and scope failures, and other existence-sensitive
  states keep the existing empty safe `404`. No request value may select
  arbitrary notice text.
- **Development sign-in.** The route must not invent or handcraft a parallel
  authentication cookie or session model. It reuses the same durable-session
  creation and subsequent session/membership revalidation path as the normal
  OIDC sign-in. If necessary, T01 may extract the existing session-creation
  logic into one shared helper used by both the normal OIDC sign-in and the
  Development-only sign-in. The route is only a shortcut around the external
  identity provider and is not mapped outside Development.

## Waves, dependencies and file counts

| Wave | Task | Depends on | Production | Tooling | Tests/guards | Docs/other | Total |
|---|---|---|---:|---:|---:|---:|---:|
| 1 | T01 | none | 10 | 1 | 14 | 6 | 31 |
| 2 | T02 | T01 | 1 | 2 | 4 | 4 | 11 |
| 3 | T03 | T02 | 16 | 0 | 20 | 6 | 42 |
| 4 | T04 | T03 | 0 | 0 | 4 | 7 | 11 |

Counts include the authorized X-item guards and each task's marker; X-7 is not
counted because no edit is expected.

## Wave 1

- [ ] **T01** — Local startup and Development identity/session: the one-command
  local tool (database, migrations, settings, HTTPS loopback start), the
  Development-loopback `TrustServerCertificate` allowance, the seeded synthetic
  user and membership, and the Development-only sign-in route through the
  existing cookie sign-in event — depends on: none

  Requirements: FR-1 to FR-20 (FR-25 to FR-27 sample generation is T02).
  Acceptance: AC-6 to AC-10, AC-12 to AC-34.

  Production files:
  - `src/Persistence/Services/SqlConnectionStringGuard.cs` — internal overload
    with the loopback allowance; default path unchanged.
  - `src/Persistence/PersistenceRegistration.cs` — additive public overload;
    design-time factory accepts the explicit `--development-loopback` argument.
  - `src/Persistence/DevelopmentFixtureIdentity.cs` — **new**; synthetic issuer
    and subject constants.
  - `src/Persistence/Services/DevelopmentFixtureSeeder.cs` — one user, one
    active membership, idempotent.
  - `src/Tenancy/Contracts/IDevelopmentFixtureSeeder.cs` — documentation text
    only; signature unchanged.
  - `src/TaxAI.Portal/DevelopmentSignInEndpoints.cs` — **new**.
  - `src/TaxAI.Portal/Components/Pages/DevelopmentSignInPage.razor` — **new**.
  - `src/TaxAI.Portal/Program.cs` — map the sign-in under the ordinal
    Development check.
  - `src/TaxAI.Portal/ModuleRegistration.cs` — pass the Development flag to the
    single persistence registration call; the region between
    `AddTaxAiAzureInfrastructure(` and `AddScoped<IAccountantIntakeWorkflow` is
    not reordered.
  - `src/TaxAI.Portal/Properties/launchSettings.json` — HTTPS loopback profile.

  Tooling:
  - `scripts/local/Start-LocalDemo.ps1` — **new**.

  New tests:
  - `tests/Persistence.Tests/Composition/DevelopmentLoopbackConnectionTests.cs`
    — AC-28, AC-31 to AC-34.
  - `tests/TaxAI.Portal.Tests/Identity/DevelopmentSignInTests.cs` — AC-14 to
    AC-17, AC-19 to AC-27.
  - `tests/TaxAI.Portal.Tests/Startup/LocalDemoToolingTests.cs` — runs the tool
    with `-SetupOnly -SettingsPath <temp>`; AC-6 to AC-10, AC-12, AC-13,
    AC-29, AC-30.

  Guard amendments (approved table):
  - `tests/Persistence.Tests/Composition/ConnectionStringGuardTests.cs`
  - `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`
  - `tests/Persistence.Tests/Composition/DevelopmentSeederTests.cs` — AC-18.
  - `tests/TaxAI.Portal.Tests/Upgrade/RouteAuthenticationBaselineTests.cs` —
    only if its host environment exposes the Development route.
  - `tests/TaxAI.Portal.Tests/BlazorBoundaryTests.cs`
  - `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
  - `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs` (also X-5)
  - `tests/TaxAI.Portal.Tests/CompositionRootTests.cs` (also X-5)

  Guard amendments (authorized X-items): X-2
  `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`; X-3
  `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`; X-4
  `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`; X-5 inside
  `PortalSecurityTests.cs` and `CompositionRootTests.cs` (listed above).

  Docs/other: `CLAUDE.md`, `src/TaxAI.Portal/CLAUDE.md` (route counts, the
  Development sign-in, the gate still forbidding every other surface),
  `src/Persistence/CLAUDE.md`, `src/Tenancy/CLAUDE.md` (seeder text),
  `scripts/test-projects.json`,
  `specs/017-local-end-to-end-accountant-mvp/status/T01.done`.

  Notes: the sign-in success redirect is the existing create page
  (`/intake-cases/new`) until T03. `TaxAI.Portal.csproj` is not edited.

## Wave 2

- [ ] **T02** — Real client upload to deterministic extraction: the leading
  `%PDF-` tolerance in the Development provider, the local sample generator,
  and proof through the real multipart upload route — depends on: T01

  Requirements: FR-21 to FR-29. Acceptance: AC-36, AC-38 to AC-46, AC-50 to
  AC-54.

  Production files:
  - `src/DocumentIntelligence/DeterministicDocumentIntelligenceProvider.cs`

  Tooling:
  - `scripts/local/New-LocalDemoSamples.ps1` — **new**; Box 12 built at run
    time; no nine-digit literal; prints no value.
  - `scripts/local/Start-LocalDemo.ps1` — call the generator (shared with T01).

  New tests:
  - `tests/DocumentIntelligence.Tests/DeterministicBridgeTests.cs` — AC-40 to
    AC-43, AC-45.
  - `tests/TaxAI.Portal.Tests/EndToEnd/UploadExtractionBridgeTests.cs` — real
    multipart `POST /client-intake/documents`, then extract: AC-36, AC-38,
    AC-39, AC-44, AC-46.
  - `tests/TaxAI.Portal.Tests/Startup/LocalDemoSampleTests.cs` — runs the
    generator into a temporary directory: AC-50 to AC-54.

  Guard amendments (approved table):
  - `tests/DocumentIntelligence.Tests/DeterministicProviderTests.cs` — only if
    an existing assertion states a `%PDF-` line followed by the header is
    `Unsupported`; otherwise untouched.

  Docs/other: `CLAUDE.md`, `src/DocumentIntelligence/CLAUDE.md`,
  `scripts/test-projects.json`,
  `specs/017-local-end-to-end-accountant-mvp/status/T02.done`.

  Notes: `src/ClientIntake/Services/IntakeUploadValidator.cs` and
  `src/DocumentIntelligence/Production/*` are not in this task and must stay
  byte-identical.

## Wave 3

- [ ] **T03** — Accountant case list, navigation, and workflow feedback: the
  firm-scoped list read and route, accountant navigation on accountant pages
  only, and the fixed notices for an in-scope refused correction or
  finalization — depends on: T02

  Requirements: FR-30 to FR-45. Acceptance: AC-58 to AC-72, AC-74 to AC-78
  (including AC-76a and AC-76b), AC-80, AC-82.

  Production files:
  - `src/ClientIntake/Contracts/IIntakeStore.cs` — `ListCasesAsync`.
  - `src/ClientIntake/Models/IntakeCaseListModels.cs` — **new**.
  - `src/Persistence/Repositories/SqlIntakeStore.cs` — firm-leading query; no
    migration.
  - `src/TaxAI.Portal/CaseListEndpoints.cs` — **new**.
  - `src/TaxAI.Portal/Components/Pages/CaseListPage.razor` — **new**.
  - `src/TaxAI.Portal/Components/Pages/CaseListPage.razor.css` — **new**.
  - `src/TaxAI.Portal/Components/Pages/CaseListPageModel.cs` — **new**.
  - `src/TaxAI.Portal/Components/Pages/WorkflowNoticeKind.cs` — **new**.
  - `src/TaxAI.Portal/Components/Shared/AccountantNavigation.razor` — **new**.
  - `src/TaxAI.Portal/Components/Shared/WorkflowNotice.razor` — **new**.
  - `src/TaxAI.Portal/Components/Pages/ExtractionListPage.razor`
  - `src/TaxAI.Portal/Components/Pages/ExtractionReviewPage.razor`
  - `src/TaxAI.Portal/Components/Pages/IntakeCaseProgressPage.razor` —
    line-exact approved diff: navigation and the documents link only.
  - `src/TaxAI.Portal/ExtractionEndpoints.cs` — the refusal branch of
    `CorrectAsync` and `FinalizeAsync` only.
  - `src/TaxAI.Portal/Program.cs` — map the case list (shared with T01).
  - `src/TaxAI.Portal/DevelopmentSignInEndpoints.cs` — redirect constant becomes
    the case list (shared with T01).

  New tests:
  - `tests/Persistence.Tests/Repositories/IntakeCaseListTests.cs` — real store:
    firm isolation, order, bound.
  - `tests/TaxAI.Portal.Tests/CaseListTests.cs` — AC-58 to AC-67, AC-69 to
    AC-72.
  - `tests/TaxAI.Portal.Tests/Extraction/WorkflowFeedbackTests.cs` — AC-74 to
    AC-78, AC-76a, AC-76b.

  Guard amendments (approved table):
  - `tests/ClientIntake.Tests/ContractTests.cs` — AC-68.
  - `tests/TaxAI.Portal.Tests/Upgrade/RouteAuthenticationBaselineTests.cs` —
    AC-80.
  - `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs` —
    `IntakeCaseProgressPage.razor` only.
  - `tests/TaxAI.Portal.Tests/BlazorBoundaryTests.cs`
  - `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
  - `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
  - `tests/TaxAI.Portal.Tests/Identity/DevelopmentSignInTests.cs` — redirect
    target (this spec's own T01 test).
  - `tests/TaxAI.Portal.Tests/Extraction/ExtractionAuthorizationTests.cs` and
    `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionSecurityAcceptanceTests.cs`
    — only assertions of an empty `404` for an **in-scope refused correction or
    finalization**; every missing, foreign, and other-route assertion stays
    unmodified. If neither file contains such an assertion, neither is touched.

  Guard amendments (authorized X-items): X-1 `ShellPageTests.cs`; X-2
  `UpgradeBaselineContractTests.cs`; X-3 `ProjectStructureTests.cs`; X-6 the
  five `IIntakeStore` test doubles.

  Docs/other: `CLAUDE.md`, `src/TaxAI.Portal/CLAUDE.md` (the case list recorded
  in the narrowed gate; route counts), `src/ClientIntake/CLAUDE.md`,
  `src/Persistence/CLAUDE.md`, `scripts/test-projects.json`,
  `specs/017-local-end-to-end-accountant-mvp/status/T03.done`.

  Notes: `IntakeEndpoints.cs`, `IntakeCaseCreatePage.razor`,
  `IntakeCaseIssuedPage.razor`, `wwwroot/intake.css`, `PortalLayout.razor`,
  `PortalContent.cs`, and the spec 012 review files are not in this task and
  must stay byte-identical. Extract, classification, accept, and duplicate
  routes keep their current responses.

## Wave 4 — end-to-end verification

- [ ] **T04** — Complete HTTP vertical-slice verification and the demo guide:
  drive sign-in to review output over real HTTP with the real multipart upload,
  prove SIN confinement and unchanged tax behaviour, record the human-runnable
  demo, and document it. No product behaviour changes — depends on: T03

  Requirements: FR-46 to FR-49. Acceptance: AC-1 to AC-5, AC-11, AC-35, AC-37,
  AC-47 to AC-49, AC-55 to AC-57, AC-73, AC-79, AC-81, AC-83 to AC-93.

  New tests:
  - `tests/TaxAI.Portal.Tests/EndToEnd/LocalMvpVerticalSliceTests.cs` — AC-1 to
    AC-5, AC-47 to AC-49, AC-56, AC-73, AC-88. Uses only rendered links and
    forms; names no content-store type.
  - `tests/TaxAI.Portal.Tests/EndToEnd/LocalMvpRepositoryTests.cs` — baseline
    comparisons against `7ee2574` and scans: AC-11, AC-35, AC-37, AC-55, AC-57,
    AC-79, AC-81, AC-83 to AC-87, AC-89, AC-90, AC-92, AC-93.
  - `tests/Persistence.Tests/EndToEnd/LocalMvpPersistenceTests.cs` — the real
    workflows through the real stores with the real migrations: list read and
    planted-value confinement over every written column (the relational half of
    AC-56).

  Guard amendments (approved table):
  - `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs` — README
    capability inventory.

  Guard amendments (authorized X-items): X-7, only if the harness genuinely
  needs the additive helper.

  Docs/other:
  - `docs/local-demo.md` — **new**; prerequisites, the one command, the twelve
    steps, the synthetic-samples and not-production statements.
  - `README.md`, `NEXT.md`, `CLAUDE.md`
  - `specs/017-local-end-to-end-accountant-mvp/verification.md` — **new**;
    AC-by-AC evidence and the record of one real local run.
  - `scripts/test-projects.json`
  - `specs/017-local-end-to-end-accountant-mvp/status/T04.done`

  Definition of Done for the spec (human-runnable): on a developer machine,
  `scripts/local/Start-LocalDemo.ps1` starts the application; the developer
  signs in, creates or selects a case, opens the client link in a second
  browser session, uploads the generated T4, then extracts, reviews, corrects,
  accepts, finalizes, and reaches `/reviews/{recordId}`. T04 performs this run
  and records it in `verification.md` without any key, connection string, or
  nine-digit value. If the run cannot be performed, T04 has no `.done` marker.

## Acceptance traceability (single owner)

| Criteria | Owner |
|---|---|
| AC-6 to AC-10, AC-12 to AC-34 | T01 |
| AC-36, AC-38 to AC-46, AC-50 to AC-54 | T02 |
| AC-58 to AC-72, AC-74 to AC-78, AC-76a, AC-76b, AC-80, AC-82 | T03 |
| AC-1 to AC-5, AC-11, AC-35, AC-37, AC-47 to AC-49, AC-55 to AC-57, AC-73, AC-79, AC-81, AC-83 to AC-93 | T04 |

## Sequentially shared paths

| Path | Tasks | Reason |
|---|---|---|
| `scripts/local/Start-LocalDemo.ps1` | T01, T02 | T02 adds the sample-generation call |
| `src/TaxAI.Portal/Program.cs` | T01, T03 | each maps its own endpoints |
| `src/TaxAI.Portal/DevelopmentSignInEndpoints.cs` | T01, T03 | redirect target moves to the case list |
| `tests/TaxAI.Portal.Tests/Identity/DevelopmentSignInTests.cs` | T01, T03 | same redirect |
| `src/Persistence/CLAUDE.md`, `src/TaxAI.Portal/CLAUDE.md` | T01, T03 | each records its own boundary |
| Portal guard files (`BlazorBoundaryTests`, `PortalBoundaryTests`, `PortalSecurityTests`, `RouteAuthenticationBaselineTests`, `UpgradeBaselineContractTests`, `ProjectStructureTests`) | T01, T03 | each adds exactly its own files, components, and routes |
| `CLAUDE.md`, `scripts/test-projects.json` | T01 to T04 | current state and floors |

No two tasks share a wave, so no sibling shares a path.

## Review-size assessment

T01 and T03 are the two substantial tasks; most of their file count is guard
re-pinning, not behaviour. T02 is small. T04 changes no product behaviour. A
fifth task is not justified: splitting T01 (startup from identity) would leave
a startup task that cannot be demonstrated, and splitting T03 (list from
feedback) would have two tasks re-pinning the same six guard files.
