# Tasks 014 — Client Intake and Document Upload Flow

Read `prd.md` and `architecture.md` first.

This file is read-only once the human approves the plan. Never check a box in
it. Completion is recorded only in `status/<TaskID>.done`, containing the real
PR number and final implementation commit SHA.

## Plan shape

- **Feature class:** `tax-domain`
- **Implementation task count:** 4
- **Parallel implementation:** none
- **Wave 0:** T01 freezes every new public signature and delivers the complete
  Blazor/review migration; it is not a contract-only task
- **Production tasks:** T01, T02, T03
- **Final integration/security task:** T04, justified by the cross-module
  session/tenant/content/review proof across 99 acceptance criteria
- **Microtasks:** none; components, CSS, DTOs, endpoints, migration,
  documentation, tests, and wiring stay with their substantial owner

## Global implementation rules

1. A human must explicitly name one TaskID. Never select the first or next
   task.
2. Before a task starts, every task in every earlier wave must have its `.done`
   marker. T01 additionally requires the completed spec 011, spec 012
   Accountant Review Screen, and spec 013 marker sets already present on
   `master`.
3. Change only the exact files listed for that task. Cross-wave reuse of a file
   is intentional; no same-wave siblings exist.
4. Tests ship with the behavior they prove. T04 adds only cross-module
   acceptance/security proof, not missing unit tests from an earlier task.
5. Preserve the immutable historical spec 001–013 artifacts.
6. Do not edit this `tasks.md`, merge, approve your own PR, apply a migration to
   a database, provision Azure, or deploy.
7. Every task must pass all three commands before its marker is created:

   ```powershell
   dotnet build TaxAI.sln
   dotnet test TaxAI.sln
   pwsh ./scripts/Invoke-SolutionTests.ps1
   ```

   The PowerShell gate is authoritative. Raise test floors when tests are
   added; never lower one.

## Wave 0 — contract plus storage-neutral Blazor/review migration

### T01 — Freeze intake contracts and migrate the portal/review UI foundation

- [ ] **T01** — Add the real `ClientIntake` and `ClientIntake.Tests` projects
  with every public signature from `architecture.md`, using
  `NotImplementedException` for new workflow behavior; establish one Blazor Web
  App foundation in `TaxAI.Portal`; and replace only the successful
  `ReviewEndpoints.cs` `StringBuilder` renderer with a strongly typed,
  statically server-rendered component and local CSS.

  **Outcome:** the solution has one approved intake contract for later tasks,
  and the existing review screen has completed its behavior-preserving Blazor
  migration. No intake route or backend behavior is exposed.

  **Implementation requirements:**

  1. Add `ClientIntake` targeting exactly `net10.0`, nullable/implicit usings
     enabled, with exact project references to `TaxCases`, `Tenancy`, and
     `TaxLogicEngine` and no package reference.
  2. Declare exactly the enums, records, ports, workflow classes,
     constructors, method signatures, JSON omissions, and safe `ToString()`
     surfaces in the architecture. Do not add optional overloads, firm-omitting
     case methods, generic repositories, content reads, or a JSON API.
  3. Make every new workflow constructor/method throw
     `NotImplementedException`. Nothing in the portal resolves or invokes those
     stubs in this task.
  4. Add both projects to `TaxAI.sln`, nest them in the existing solution
     folders, and register the new test assembly in the test manifest without
     asserting a total project count. The test project uses exactly the current
     approved net10 xunit/test-SDK/runner/coverlet package matrix and references
     only `ClientIntake`.
  5. Register the framework Blazor Web App services/endpoints in the existing
     composition root. Static SSR is the default. Register support for
     Interactive Server but apply no global render mode and add no
     WebAssembly/npm/SPA/third-party asset.
  6. Keep the existing anonymous shell and health bodies/routes byte-identical.
  7. Keep `GET /reviews/{recordId:guid}`,
     `PortalAuthorization.FirmScopedPolicy`, response headers, trusted firm
     extraction, browser-safety guard, closed projection, collection order, and
     exactly one `FindAsync` lookup with the request cancellation token.
  8. Remove only the endpoint `Render` method, `StringBuilder`, append-based
     markup, inline style assembly, and their now-unused imports. Return
     `RazorComponentResult<ReviewPage>` on the existing success branch.
  9. Pass the component one exact typed review model containing only tax year,
     package version, slips, display names, boxes/invariant values,
     lines/invariant values, source boxes, and unmapped codes. Pass no
     persistence entity or identifier.
  10. Render populated and every existing empty state, preserve invariant
      positive/negative/zero/four-decimal strings without arithmetic, and rely
      on Razor contextual encoding.
  11. Place shared document/layout markup in Razor components and reusable
      styling in tracked local/global or component-scoped CSS. Do not issue a
      third-party request.
  12. Preserve empty `404`, authorization refusal, session revocation,
      no-cache, fixed exception response, and no-SIN behavior before any
      taxpayer component renders.
  13. Replace invalidated exact portal source/route/component assertions with
      tighter exact allowlists; do not delete or broadly weaken them.
  14. Prove the isolated task changes no production file under `Persistence` or
      `Tenancy`, no review/persistence public contract, no existing migration or
      model snapshot, and no database schema.
  15. Update active root guidance to list the real ClientIntake projects, the
      Blazor/review migration delivered by T01, the still-unimplemented intake
      backend/routes, and the unchanged deployment/out-of-scope gates.
  16. Create `status/T01.done` only after the complete gate is green, then stop.

  **Acceptance coverage in this task:**

  - exact public/JSON contract and boundary graph;
  - AC-89 through AC-96 and AC-98 through AC-99;
  - AC-92 through AC-95's exact review query/refusal/allowlist/fidelity cases;
  - existing spec 012 authorization, missing/foreign equivalence, immutable
    version, no-mutation, amount, order, encoding, empty-state, no-cache,
    no-SIN, and revocation suites;
  - absence of intake behavior, manual screen HTML, inline endpoint CSS,
    global interactivity, WebAssembly, npm, and schema drift.

  **Owned files — ClientIntake contract:**

  - `src/ClientIntake/AGENTS.md` *(new)*
  - `src/ClientIntake/CLAUDE.md` *(new)*
  - `src/ClientIntake/ClientIntake.csproj` *(new)*
  - `src/ClientIntake/Models/IntakeEnums.cs` *(new)*
  - `src/ClientIntake/Models/IntakeCommands.cs` *(new)*
  - `src/ClientIntake/Models/IntakeViews.cs` *(new)*
  - `src/ClientIntake/Models/IntakeStorageRecords.cs` *(new)*
  - `src/ClientIntake/Contracts/IAccountantIntakeWorkflow.cs` *(new)*
  - `src/ClientIntake/Contracts/IClientIntakeWorkflow.cs` *(new)*
  - `src/ClientIntake/Contracts/IIntakeStore.cs` *(new)*
  - `src/ClientIntake/Contracts/IIntakeContentStore.cs` *(new)*
  - `src/ClientIntake/Contracts/IIntakeSecretSource.cs` *(new)*
  - `src/ClientIntake/Services/AccountantIntakeWorkflow.cs` *(new)*
  - `src/ClientIntake/Services/ClientIntakeWorkflow.cs` *(new)*

  **Owned files — Blazor foundation and review production:**

  - `src/TaxAI.Portal/Program.cs`
  - `src/TaxAI.Portal/ModuleRegistration.cs`
  - `src/TaxAI.Portal/ReviewEndpoints.cs`
  - `src/TaxAI.Portal/CLAUDE.md`
  - `src/TaxAI.Portal/Components/_Imports.razor` *(new)*
  - `src/TaxAI.Portal/Components/App.razor` *(new)*
  - `src/TaxAI.Portal/Components/Routes.razor` *(new)*
  - `src/TaxAI.Portal/Components/Layout/PortalLayout.razor` *(new)*
  - `src/TaxAI.Portal/Components/Layout/PortalLayout.razor.css` *(new)*
  - `src/TaxAI.Portal/Components/Pages/ReviewPage.razor` *(new)*
  - `src/TaxAI.Portal/Components/Pages/ReviewPage.razor.css` *(new)*
  - `src/TaxAI.Portal/Components/Pages/ReviewPageModel.cs` *(new)*
  - `src/TaxAI.Portal/wwwroot/portal.css` *(new)*

  **Owned files — contract and portal tests/gate:**

  - `tests/ClientIntake.Tests/ClientIntake.Tests.csproj` *(new)*
  - `tests/ClientIntake.Tests/ContractTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Models/ContractOmissionTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Boundaries/ClientIntakeBoundaryTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/ReviewPageTests.cs`
  - `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
  - `tests/TaxAI.Portal.Tests/ShellPageTests.cs`
  - `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
  - `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
  - `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
  - `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
  - `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
  - `tests/TaxAI.Portal.Tests/Identity/SessionRevocationTests.cs`
  - `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs`
  - `tests/TaxAI.Portal.Tests/Upgrade/RouteAuthenticationBaselineTests.cs`
  - `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`
  - `TaxAI.sln`
  - `scripts/test-projects.json`

  **Owned files — active guidance:**

  - `README.md`
  - `CLAUDE.md`
  - `NEXT.md`

  **Owned completion evidence:**

  - `specs/014-client-intake-and-document-upload-flow/status/T01.done` *(new,
    only after the complete gate passes)*

  **Forbidden changes:**

  - any unlisted file;
  - every production file under `src/Persistence`, `src/Tenancy`,
    `src/TaxCases`, `src/CaseReview`, `src/CaseProcessing`,
    `src/DocumentExtraction`, and `src/TaxLogicEngine`;
  - every existing migration/model snapshot and every database schema shape;
  - any intake endpoint, workflow implementation, content adapter, or
    production/deployment setting.

  — **depends on:** none

## Wave 1 — secure workflow, persistence, and content backend

### T02 — Implement the complete intake backend and additive infrastructure

- [ ] **T02** — Replace the Wave 0 intake stubs with the complete
  server-derived case/link/session/questionnaire/checklist/upload/submission/
  status/audit/handoff backend; implement the scoped SQL store and one additive
  migration; add the development-only encrypted streaming content adapter and
  required fail-closed configuration; wire all backend services without mapping
  any user route.

  **Outcome:** all intake behavior is available through tested in-process
  contracts and infrastructure, but no client/accountant intake UI or HTTP
  surface exists yet.

  **Implementation requirements:**

  1. Preserve every T01 public signature exactly. Implement constructor,
     command, option, secret, digest, collection-copy, result, and safe
     `ToString()` guards without adding a public overload.
  2. Implement accountant case creation with the server-supplied
     `TrustedFirmId`, 2025 only, one generated primary participant, no SIN,
     explicit 0–25 category counts with at least one positive, and rejection of
     `Other`/undefined categories.
  3. Reuse `TaxpayerTextGuard` and the approved maximum, then reject compact,
     hyphenated, spaced, and masked SIN-shaped display text before constructing
     `TaxCase`. Never echo, mask, persist, or audit rejected input.
  4. Use `TaxCase.Create`, `ConfirmChecklist`,
     `TransitionTo(InformationGathering)`, `AnswerQuestion`,
     `AttachEvidence`, and `TaxCase.Readiness`; create no second case or
     readiness authority.
  5. Implement cryptographically random identifiers/256-bit canonical
     credentials, purpose-separated keyed one-way digests, finite link/session
     policy, raw-link one-time return, read-only inspection, atomic redemption,
     single active link, reissue/revoke session invalidation, replay refusal,
     and safe structural results.
  6. Append immutable answer versions; reject null/duplicate/unrequested/
     undefined answers; create immutable monotonically versioned submission
     snapshots with exact server-resolved answer/document references and
     concurrency refusal.
  7. Derive expected-item status with the fixed precedence and derive
     unsubmitted changes separately. Reconstruct readiness from only the latest
     submission through `TaxCases`.
  8. Validate non-empty configured-bounded PDF/JPEG/PNG streams by signature
     and media kind, resolve only a server-owned expected category/ordinal, and
     never read a document value.
  9. Stage source bytes outside web root in a versioned authenticated encrypted
     streaming envelope, with opaque keys and case-derived keyed whole-file
     fingerprint. Add no content-read/browser API.
  10. Implement compensating removal after content/store failure and the
      same-case unique fingerprint race so exactly one concurrent duplicate
      wins. Other cases/firms remain non-correlatable and non-oracular.
  11. Append only safe structural upload attempts and audit events; invalid
      public tokens create none. Never store raw token/session value,
      filename, content, prompt, display name in audit, fingerprint in audit, or
      any SIN representation.
  12. Implement one exact immutable review association after
      `IReviewRecordRepository.FindAsync(trustedFirm, recordId)` confirms the
      existing record's case. Never call `SaveAsync`, create a review record,
      select latest, or replace an association.
  13. Add only the architecture's intake tables, composite firm/case keys,
      digest exemptions, check constraints, unique constraints, and
      concurrency token in migration `20260730000000_AddClientIntake`. Do not
      edit the initial migration or any existing review/identity table.
  14. Keep exactly two firm-omitting store operations: digest link scope and
      digest session scope. Every scoped read/write revalidates the same live
      session or trusted firm and returns foreign as missing.
  15. Add `ClientIntake` to the portal reference set. Add `ClientIntake` and
      the explicit inward `TaxCases` type reference to Persistence alongside
      Tenancy, because the store records use the existing category/lifecycle
      enums; Persistence must not invoke or map `TaxCase`. Add no EF reference
      outside Persistence and no ASP.NET reference to ClientIntake.
  16. Validate all lifetime, size/count, rate/concurrency, digest,
      fingerprint, encryption, and Development content-root configuration at
      startup with no compiled insecure fallback. Also require a canonical
      absolute HTTPS public origin with no user info/non-root path/query/
      fragment; never build the one-time URL from request `Host` or forwarded
      headers. Register the content adapter only in Development/test and retain
      non-Development refusal.
  17. Add no mapped intake route, component, manual HTML, raw-document
      preview/download, extraction, parsing, tax processing, calculation,
      review construction, Azure provider, retention/purge, or deployment.
  18. Update active root guidance from the T01 intake-contract state to the
      implemented but not-yet-HTTP-exposed backend, exact dependency graph,
      additive schema/content boundary, and continuing exclusions.
  19. Run the complete gate and create `status/T02.done` only after it passes,
      then stop.

  **Acceptance coverage in this task:**

  - backend portions of AC-1 through AC-88;
  - atomic case/checklist/link creation and rollback;
  - exact year/category/count/display-name boundaries;
  - one-way digest, entropy, expiry, replay, reissue, revocation, idle/absolute
    session bounds, and store failures;
  - adaptive closed question model, immutable answer/submission versions, and
    concurrent next-version refusal;
  - signature, byte/count, malformed/empty, encrypted content, cleanup,
    same-case/cross-case/concurrent duplicate behavior;
  - all five statuses, precedence, counts, category isolation, readiness, and
    unsubmitted changes;
  - composite tenant schema, only two bootstrap exemptions, immutable audit,
    no-SIN SQL/content metadata/diagnostics;
  - exact review association and no review manufacture;
  - startup refusal and module dependency direction.

  **Owned files — ClientIntake production:**

  - `src/ClientIntake/Models/IntakeCommands.cs`
  - `src/ClientIntake/Models/IntakeViews.cs`
  - `src/ClientIntake/Models/IntakeStorageRecords.cs`
  - `src/ClientIntake/Services/AccountantIntakeWorkflow.cs`
  - `src/ClientIntake/Services/ClientIntakeWorkflow.cs`
  - `src/ClientIntake/Services/IntakeCaseFactory.cs` *(new)*
  - `src/ClientIntake/Services/IntakeDisplayNameGuard.cs` *(new)*
  - `src/ClientIntake/Services/IntakeQuestionnairePolicy.cs` *(new)*
  - `src/ClientIntake/Services/IntakeStateReplayer.cs` *(new)*
  - `src/ClientIntake/Services/IntakeProjection.cs` *(new)*
  - `src/ClientIntake/Services/IntakeUploadValidator.cs` *(new)*
  - `src/ClientIntake/Services/IntakeWorkflowOptionsGuard.cs` *(new)*
  - `src/ClientIntake/CLAUDE.md`

  **Owned files — Persistence production/schema:**

  - `src/Persistence/Persistence.csproj`
  - `src/Persistence/PersistenceRegistration.cs`
  - `src/Persistence/CLAUDE.md`
  - `src/Persistence/Entities/IntakeEntities.cs` *(new)*
  - `src/Persistence/Configurations/IntakeModelConfiguration.cs` *(new)*
  - `src/Persistence/Repositories/SqlIntakeStore.cs` *(new)*
  - `src/Persistence/Migrations/20260730000000_AddClientIntake.cs` *(new)*
  - `src/Persistence/Migrations/20260730000000_AddClientIntake.Designer.cs`
    *(new)*
  - `src/Persistence/Migrations/TaxAiDbContextModelSnapshot.cs`

  **Owned files — portal backend infrastructure/composition:**

  - `src/TaxAI.Portal/TaxAI.Portal.csproj`
  - `src/TaxAI.Portal/Program.cs`
  - `src/TaxAI.Portal/ModuleRegistration.cs`
  - `src/TaxAI.Portal/PortalStartupGuards.cs`
  - `src/TaxAI.Portal/IntakeConfiguration.cs` *(new)*
  - `src/TaxAI.Portal/CryptographicIntakeSecretSource.cs` *(new)*
  - `src/TaxAI.Portal/DevelopmentEncryptedIntakeContentStore.cs` *(new)*
  - `src/TaxAI.Portal/CLAUDE.md`

  **Owned files — ClientIntake tests:**

  - `tests/ClientIntake.Tests/ContractTests.cs`
  - `tests/ClientIntake.Tests/Models/ContractOmissionTests.cs`
  - `tests/ClientIntake.Tests/Boundaries/ClientIntakeBoundaryTests.cs`
  - `tests/ClientIntake.Tests/Services/AccountantIntakeWorkflowTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Services/ClientIntakeWorkflowTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Services/IntakeCaseFactoryTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Services/IntakeDisplayNameGuardTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Services/IntakeProjectionTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Services/IntakeUploadValidatorTests.cs` *(new)*
  - `tests/ClientIntake.Tests/EndToEnd/BackendAcceptanceTests.cs` *(new)*
  - `tests/ClientIntake.Tests/Support/InMemoryIntakeStore.cs` *(new)*
  - `tests/ClientIntake.Tests/Support/SyntheticIntakeFixtures.cs` *(new)*

  **Owned files — Persistence tests:**

  - `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`
  - `tests/Persistence.Tests/Composition/PersistenceRegistrationTests.cs`
  - `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`
  - `tests/Persistence.Tests/Repositories/IntakeStoreTests.cs` *(new)*
  - `tests/Persistence.Tests/Repositories/IntakeStoreTenantIsolationTests.cs`
    *(new)*
  - `tests/Persistence.Tests/Repositories/IntakeStoreConcurrencyTests.cs`
    *(new)*
  - `tests/Persistence.Tests/Schema/MigrationTests.cs`
  - `tests/Persistence.Tests/Schema/ModelShapeTests.cs`
  - `tests/Persistence.Tests/Schema/PiiSchemaTests.cs`
  - `tests/Persistence.Tests/Schema/TenantIsolationSchemaTests.cs`
  - `tests/Persistence.Tests/EndToEnd/PiiAtRestTests.cs`
  - `tests/Persistence.Tests/Support/SyntheticFixtures.cs`

  **Owned files — portal infrastructure tests/gate:**

  - `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
  - `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
  - `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
  - `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
  - `tests/TaxAI.Portal.Tests/Security/IntakeCredentialSourceTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs`
  - `tests/TaxAI.Portal.Tests/Startup/IntakeConfigurationTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/Storage/DevelopmentEncryptedIntakeContentStoreTests.cs`
    *(new)*
  - `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
  - `scripts/test-projects.json`

  **Owned files — active guidance:**

  - `README.md`
  - `CLAUDE.md`
  - `NEXT.md`

  **Owned completion evidence:**

  - `specs/014-client-intake-and-document-upload-flow/status/T02.done` *(new,
    only after the complete gate passes)*

  **Forbidden changes:**

  - any unlisted file;
  - every production file in `TaxCases`, `Tenancy`, `CaseReview`,
    `CaseProcessing`, `DocumentExtraction`, and `TaxLogicEngine`;
  - T01's review component, review endpoint, review DTO, and CSS;
  - the initial migration, existing review/identity schema, automatic migration,
    production content provider, upload/read route, and any UI component.

  — **depends on:** T01

## Wave 2 — secure component UI, progress, upload transport, and handoff

### T03 — Deliver the client intake and accountant progress experience

- [ ] **T03** — Map the complete accountant/client HTTP flow over the T02
  backend; render every successful screen through strongly typed Blazor
  components; add the one narrow presentation-only Interactive Server island,
  neutral-name streaming upload transport, accountant progress, and exact
  review handoff.

  **Outcome:** an accountant can create and monitor a case, deliver a one-time
  secure link, and hand off a ready intake; a client can redeem, answer, upload
  T4/medical documents, and submit without learning firm/case identifiers.

  **Implementation requirements:**

  1. Map only the exact accountant/client routes listed in the architecture.
     Preserve root, health, identity/firm, sign-out, and review routes.
  2. Require the existing firm-scoped policy on every accountant route. Derive
     firm only from `PortalAuthorization` request state and never bind a firm
     member.
  3. Use one shared empty-`404` writer for every invalid/expired/revoked/spent/
     replayed/foreign/missing/inconsistent client resource, and byte-identical
     missing/foreign accountant cases. Render no taxpayer component on refusal.
  4. Implement the read-only confirmation GET, fixed-action antiforgery
     redemption POST, protected digest proof, atomic replay mapping, clean
     redirect, and protected secure HTTP-only scoped session cookie. Never put
     raw credential or trusted scope into the form action/body/cookie.
  5. Configure required finite public/session/answer/upload/submission
     rate/concurrency policies. Evaluate rate limits before validity, use safe
     keyed/non-logging partitions, emit one fixed `429`, and mutate nothing
     when limited.
  6. Make every state change explicit non-GET with antiforgery. GET/HEAD do not
     touch session or state.
  7. Return successful create, one-time link, confirmation, client intake,
     checklist/questionnaire/upload, accountant progress, and handoff markup
     through typed Razor components. No endpoint/application service may build
     those screens with string concatenation, `StringBuilder`, inline style
     assembly, or raw markup.
  8. Keep review and accountant screens static SSR. Apply
     `@rendermode InteractiveServer` only to the client questionnaire panel
     that performs local closed-code reveal. The panel invokes no workflow or
     authorization service, and its serialized state contains only the client
     allowlist.
  9. Use a tracked collocated/local upload module to send the literal filename
     `upload.bin` and antiforgery header. Parse with `MultipartReader`, never
     `IFormFile`/`ReadFormAsync`, discard any received filename immediately,
     enforce configured count/bytes, and stream to the workflow.
  10. Re-enter accountant R-1 or client-session validation on every operation,
      including after an Interactive Server circuit connected. A revoked
      session/membership/link or failed store causes the next POST to refuse
      before mutation/render.
  11. Apply the exact client and accountant browser allowlists, contextual
      encoding, local-only CSP, no-referrer, no-store, nosniff, no third-party
      requests, no request-body logging, and no source-content response.
  12. Render all five expected-item statuses, category ordinals, question
      completion, latest immutable submission, unsubmitted changes, link state,
      readiness, safe instants, and handoff state from the backend projection;
      do not recalculate them in Razor.
  13. Show the raw client URL only on successful create/reissue. Progress cannot
      recover it.
  14. Associate only the exact submitted review record through the workflow.
      Show the existing review URL only for Ready + verified association; show
      the fixed awaiting state otherwise; never create a review record.
  15. Keep authorization, session, upload validation/fingerprint, status,
      readiness, audit, repository, and tax behavior outside every component.
      Boundary tests inspect injections, namespaces, parameters, event handlers,
      and render modes.
  16. Move all reusable layout/checklist/status markup into components and all
      reusable styles into local/global or component-scoped CSS. Add no
      third-party asset.
  17. Amend every invalidated exact portal route/source/capability assertion to
      the new closed allowlist. Preserve all unrelated spec 011–013 behavior.
  18. Update active root guidance to describe the now-shipped intake and Blazor
      screens, exact raw-content/no-SIN exception, review handoff, and continuing
      no-deployment/no-retention/no-extraction/no-tax gates without claiming
      final AC verification before T04.
  19. Add no Persistence/Tenancy/domain production change, migration, schema
      change, OCR, extraction, tax calculation, T1, filing, Azure, production
      deployment, retention, raw-document view/download, T5, or other category.
  20. Run the complete gate and create `status/T03.done` only after it passes,
      then stop.

  **Acceptance coverage in this task:**

  - HTTP/component portions of AC-1 through AC-3 and AC-4 through AC-88;
  - exact route/method/auth/antiforgery/rate-limit/safe-refusal matrix;
  - client URL/cookie/HTML/state identifier omission and one-time link display;
  - adaptive category UI, expected checklist, all statuses, upload success/
    failure/duplicate messages, immutable submission progress;
  - R-1 and client-session revocation on the next operation, including a live
    presentation circuit;
  - browser DTO/HTML/URL/header/log/filename/no-SIN tests;
  - exact progress and review-handoff states;
  - AC-89 through AC-91 and AC-96 through AC-98 for all new screens;
  - unchanged migrated review query/render security from T01.

  **Owned files — portal production/endpoints:**

  - `src/TaxAI.Portal/Program.cs`
  - `src/TaxAI.Portal/ModuleRegistration.cs`
  - `src/TaxAI.Portal/PortalContent.cs`
  - `src/TaxAI.Portal/IntakeConfiguration.cs`
  - `src/TaxAI.Portal/IntakeEndpoints.cs` *(new)*
  - `src/TaxAI.Portal/ClientIntakeEndpoints.cs` *(new)*
  - `src/TaxAI.Portal/ClientIntakeCookie.cs` *(new)*
  - `src/TaxAI.Portal/IntakeHttpSecurity.cs` *(new)*
  - `src/TaxAI.Portal/Components/App.razor`
  - `src/TaxAI.Portal/Components/Pages/IntakeCaseCreatePage.razor` *(new)*
  - `src/TaxAI.Portal/Components/Pages/IntakeCaseIssuedPage.razor` *(new)*
  - `src/TaxAI.Portal/Components/Pages/IntakeCaseProgressPage.razor` *(new)*
  - `src/TaxAI.Portal/Components/Pages/ClientLinkConfirmationPage.razor`
    *(new)*
  - `src/TaxAI.Portal/Components/Pages/ClientIntakePage.razor` *(new)*
  - `src/TaxAI.Portal/Components/Shared/ClientQuestionnairePanel.razor` *(new)*
  - `src/TaxAI.Portal/Components/Shared/ClientQuestionnairePanel.razor.js`
    *(new)*
  - `src/TaxAI.Portal/Components/Shared/ExpectedDocumentChecklist.razor`
    *(new)*
  - `src/TaxAI.Portal/Components/Shared/IntakeStatusBadge.razor` *(new)*
  - `src/TaxAI.Portal/wwwroot/intake.css` *(new)*
  - `src/TaxAI.Portal/CLAUDE.md`

  **Owned files — portal HTTP/component/security tests:**

  - `tests/TaxAI.Portal.Tests/IntakePageTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/ClientIntakePageTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/UploadEndpointTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/IntakeAuthorizationTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/ReviewHandoffTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/BlazorBoundaryTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/Identity/ClientSessionRevocationTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/Identity/SessionRevocationTests.cs`
  - `tests/TaxAI.Portal.Tests/Security/ClientIntakeSecurityTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
  - `tests/TaxAI.Portal.Tests/ShellPageTests.cs`
  - `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
  - `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
  - `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
  - `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs`
  - `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
  - `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
  - `tests/TaxAI.Portal.Tests/Upgrade/RouteAuthenticationBaselineTests.cs`
  - `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`
  - `scripts/test-projects.json`

  **Owned files — active guidance:**

  - `README.md`
  - `CLAUDE.md`
  - `NEXT.md`

  **Owned completion evidence:**

  - `specs/014-client-intake-and-document-upload-flow/status/T03.done` *(new,
    only after the complete gate passes)*

  **Forbidden changes:**

  - any unlisted file;
  - every production file under `ClientIntake`, `Persistence`, `Tenancy`,
    `TaxCases`, `CaseReview`, `CaseProcessing`, `DocumentExtraction`, and
    `TaxLogicEngine`;
  - `ReviewEndpoints.cs`, `ReviewPage.razor`, `ReviewPageModel.cs`, and the
    existing review contract;
  - every migration/model snapshot/schema file;
  - any raw-content read path, client-side authority/business rule, global
    render mode, WebAssembly/npm/third-party asset, or excluded feature.

  — **depends on:** T02

## Wave 3 — final cross-module acceptance and security proof

### T04 — Verify the integrated workflow and record spec completion

- [ ] **T04** — Add only cross-module end-to-end acceptance/security tests,
  execute and record AC-1 through AC-99 against the merged T01–T03 result,
  update active repository guidance, and mark spec 014 complete.

  **Why this is a real boundary:** T01 proves a storage-neutral UI migration,
  T02 proves module/relational/content behavior, and T03 proves HTTP/component
  behavior with controlled collaborators. Only after they merge can one test
  exercise the real portal composition/workflow/content adapter and, in the
  Persistence suite that already has the approved internal test seam, the real
  workflow/relational adapter. Together they prove tenant/session/content/review
  integration without widening production internals or requiring SQL Server.
  T04 changes no product code and is not a corrective catch-all.

  **Implementation requirements:**

  1. Add a cross-module synthetic happy path: R-1 accountant creates one 2025
     case, receives one link, client confirms/redeems, saves applicable answers,
     uploads synthetic signature-valid T4 and medical files, submits, accountant
     sees exact progress, exact existing review record is associated, and the
     unchanged review route opens only after readiness.
  2. Exercise two firms/cases plus missing/foreign/malformed/replayed/expired/
     revoked resources and prove identical safe refusals, no unscoped read, no
     cross-case duplicate oracle, and no content/metadata mutation.
  3. Exercise accountant and client revocation/store failure between page render
     and next operation, proving no component/circuit authorization cache.
  4. Prove raw link one-way storage, neutral filename, encrypted outside-root
     bytes, scoped fingerprint, immutable answers/submissions/audit/association,
     exact status/readiness replay, and no raw-content read route.
  5. Scan all new browser models, rendered HTML/page state, routes, headers,
     cookies, SQL metadata, content metadata, audit, logs, exceptions,
     diagnostics, filenames, and fixtures for every forbidden SIN shape and
     identifier. Explicitly exclude raw synthetic source bytes from any
     SIN-free claim.
  6. Prove no OCR/LLM/extraction/processing/mapping/calculation/review creation,
     `SaveAsync`, T1, filing, Azure, production provider/deployment, or retention
     rule entered the source/reference/runtime graph.
  7. Run schema/migration pending-model checks without applying a database
     migration, verify the initial migration and existing review tables remain
     unchanged, and verify only the intake migration is additive.
  8. Record a pass/fail evidence line for AC-1 through AC-99 in
     `verification.md`, including named tests/commands and the exact test totals
     reported by the authoritative gate.
  9. Update README/root instructions/NEXT from their spec 013 state to describe
     exactly the implemented Blazor/intake capability and continuing deployment,
     retention, no-SIN, raw-content, and out-of-scope gates.
  10. Run `git diff --check`, build, raw test, and the authoritative PowerShell
      gate. Do not create `T04.done` or claim completion if any project is
      missing, failed, skipped, below its floor, or any AC lacks evidence.
  11. Create `status/T04.done` only after all evidence is green, then stop.

  **Owned files — final cross-module tests/gate:**

  - `tests/Persistence.Tests/EndToEnd/ClientIntakeWorkflowPersistenceTests.cs`
    *(new)*
  - `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeAcceptanceTests.cs` *(new)*
  - `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeSecurityAcceptanceTests.cs`
    *(new)*
  - `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`
  - `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
  - `scripts/test-projects.json`

  **Owned files — active guidance/evidence:**

  - `README.md`
  - `CLAUDE.md`
  - `NEXT.md`
  - `specs/014-client-intake-and-document-upload-flow/verification.md` *(new)*
  - `specs/014-client-intake-and-document-upload-flow/status/T04.done` *(new,
    only after every gate passes)*

  **Forbidden changes:**

  - any unlisted file;
  - every production C# project/file, component, CSS/JavaScript asset,
    project/solution reference, package, migration/model snapshot/schema,
    earlier spec artifact, configuration behavior, or implementation contract;
  - weakening, deleting, skipping, filtering, or lowering any existing test.

  — **depends on:** T03

## Acceptance and dependency summary

| Wave | Task | Merge boundary | Depends on |
|---|---|---|---|
| 0 | T01 | exact intake contract + complete Blazor/review migration | none |
| 1 | T02 | complete secure backend, SQL metadata, encrypted dev content | T01 |
| 2 | T03 | complete accountant/client component and HTTP experience | T02 |
| 3 | T04 | cross-module AC-1…AC-99 proof and active guidance | T03 |

Four task IDs, four sequential waves, three production merge units, and one
test/evidence-only final merge unit. No task exists solely for markup, CSS,
DTOs, endpoints, migration, documentation, or wiring.
