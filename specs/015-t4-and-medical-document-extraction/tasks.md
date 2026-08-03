# Tasks: T4 and Medical Document Extraction (spec 015)

**Feature class: `tax-domain`.** Read `prd.md` and `architecture.md` before
implementing any task.

**This file is read-only once approved.** Never edit it and never check a box in
it. Completion is recorded by creating
`specs/015-t4-and-medical-document-extraction/status/<TaskID>.done` containing
the PR number and commit SHA. Each task writes only its own marker, so parallel
branches never conflict.

A task is unblocked when every task in every earlier wave has a `.done` marker.

## Rules binding every task in this plan

1. **No scaffolding.** No task's primary output may be project shells, public
   signatures with `NotImplementedException` bodies, empty or speculative
   contracts, test-project shells, or registration without a coherent implemented
   behaviour. **Every contract, project, test project, solution entry, and
   manifest entry is introduced by the first task that implements and tests real
   behaviour using it.**
2. **No task may end with** a `NotImplementedException`, an empty project, or an
   unused public contract.
3. **Every production path is owned by exactly one task.** No file is edited by
   two tasks.
4. **Tests ship with the task**, never as a later lump.
5. **Floors are per-task.** A task that introduces a test project registers it in
   `scripts/test-projects.json` in the same pass; a task that adds tests to an
   existing project raises that project's `minimumTests` in the same pass. No
   task relies on a later task to reach its minimum. **No floor is ever
   lowered** — a falling count is the defect the gate exists to catch.
6. **`pwsh ./scripts/Invoke-SolutionTests.ps1` must be green** at the end of
   every task.

## Why this plan is a chain, not a fan-out

Spec 015 has **no contract wave by design**. That is the correct call — an empty
contract shell is scaffolding, and the PRD's planning constraint forbids it — but
it has a real consequence that should be visible before approval: **each task
must introduce the contracts it implements, so a later task cannot code against
a sibling's not-yet-existing types.** The dependency graph is therefore close to
serial, and the waves below are mostly one task each.

That is a deliberate trade: parallel throughput for the guarantee that no
half-built module ever exists on `master`. Every wave still merges before the
next begins, and every wave still leaves the repository in a coherent, fully
tested state — which a contract wave, by construction, does not.

---

## Wave 1

- [ ] **T01 — Employee-copy T4 parser semantics.** Apply the approved,
  CRA-verified `T4SlipParser` change: Box 54 is no longer a required box, and
  `EmployerAddress` / `EmployeeAddress` are no longer required non-blank fields.
  Box 12 and Box 45 remain required for 2025, `EmployerName` and `EmployeeName`
  remain required, and the tax-year rule is untouched — each unchanged in field
  name, message, and ordering. No public type, member, or signature changes, and
  **no value is ever invented** for a removed requirement: a missing Box 54 stays
  missing and `T4Slip.EmployerAccountNumber` is absent rather than
  placeholder-populated. Update the existing assertions that encode the two
  superseded rules, add positive and negative coverage (valid employee copy
  without Box 54 accepted; no synthesized Box 54; a 2025 T4 without Box 45 still
  fails through the engine; null and blank addresses both parse with no
  substitute supplied; Box 12 missing and year 2024 still fail identically), and
  record the supersession of the spec 003 `[CONFIRM]` decision with the
  canada.ca citation. Deleting the Box 54 `[InlineData]` row lowers the executed
  count by one, so this task's own new tests must more than replace it; raise
  `TaxLogicEngine.Tests`'s floor to the observed count in the same pass.
  **Isolated for review because it is the only task that changes merged
  tax-domain behaviour.** — files: `src/TaxLogicEngine/Services/T4SlipParser.cs`,
  `src/TaxLogicEngine/CLAUDE.md`,
  `tests/TaxLogicEngine.Tests/Services/T4SlipParserTests.cs`,
  `tests/TaxLogicEngine.Tests/Services/T4SlipParserEmployeeCopyTests.cs`,
  `scripts/test-projects.json` — depends on: none

---

## Wave 2

- [ ] **T02 — Extraction foundation and deterministic in-process intelligence.**
  Introduce `src/ExtractionWorkflow/` and `src/DocumentIntelligence/` as a
  **working vertical slice**, carrying only the contracts this task implements
  and tests. Deliver: the document classification/extraction port and its
  deterministic in-process Development/test adapter, computing classification and
  T4/medical field extraction from the source bytes alone; the extraction-only
  content-read port and its lease type, whose four-part firm/case/document/
  content-key scope is non-optional and which returns nothing for any mismatch;
  the safe extraction result shapes (`ExtractionVersion`, `ExtractionBoxValue`,
  `MedicalEvidenceVersion`, `CurrencyDeclaration`, `FieldValidationOutcome`,
  `ClassificationOutcome`, the closed field/error/enum vocabularies); and the
  in-memory raw `T4RawInput` payload assembly whose Box 12 exists only for the
  duration of one call and is never returned on a result shape. `ExtractionBoxValue`
  must **refuse** `T4BoxCodes.SocialInsuranceNumber` at construction and
  `CorrectionField` must declare no Box 12 member — type invariants, not later
  filters. Register both projects and both test projects in the solution and the
  manifest with real observed floors. Tests must prove: determinism over the same
  bytes; an unrecognizable document classifies as `Unsupported` rather than
  guessing; provider throw, cancellation, empty result, null box list, and a box
  with a null code each fail closed with nothing partial and nothing thrown to the
  caller; the adapter completes with outbound network access blocked; no project
  declares an OCR/LLM/document-intelligence/cloud/HTTP-client `PackageReference`;
  every public type is a sealed class with a hand-written structural `ToString()`;
  every identifier, taxpayer-text, and content-key property is `[JsonIgnore]`; a
  planted SIN reaches no result shape, `ToString()`, error message, or
  serialization; and each new project is asserted exactly once at its approved
  path, never as part of a closed project set. **Nothing in either project may be
  a stub at completion.** — files: `TaxAI.sln`, `scripts/test-projects.json`,
  `src/ExtractionWorkflow/ExtractionWorkflow.csproj`,
  `src/ExtractionWorkflow/CLAUDE.md`,
  `src/ExtractionWorkflow/Models/ExtractionWorkflowFields.cs`,
  `src/ExtractionWorkflow/Models/ExtractionWorkflowErrors.cs`,
  `src/ExtractionWorkflow/Models/ExtractionEnums.cs`,
  `src/ExtractionWorkflow/Models/ProviderResults.cs`,
  `src/ExtractionWorkflow/Models/ExtractionVersion.cs`,
  `src/ExtractionWorkflow/Models/ExtractionBoxValue.cs`,
  `src/ExtractionWorkflow/Models/MedicalEvidenceVersion.cs`,
  `src/ExtractionWorkflow/Contracts/IDocumentIntelligenceProvider.cs`,
  `src/ExtractionWorkflow/Contracts/IExtractionContentReader.cs`,
  `src/ExtractionWorkflow/Services/ExtractionPayloadBuilder.cs`,
  `src/DocumentIntelligence/DocumentIntelligence.csproj`,
  `src/DocumentIntelligence/CLAUDE.md`,
  `src/DocumentIntelligence/DeterministicDocumentIntelligenceProvider.cs`,
  `src/DocumentIntelligence/DocumentIntelligenceOptions.cs`,
  `src/DocumentIntelligence/DocumentIntelligenceRegistration.cs`,
  `tests/ExtractionWorkflow.Tests/ExtractionWorkflow.Tests.csproj`,
  `tests/ExtractionWorkflow.Tests/Models/ResultShapeTests.cs`,
  `tests/ExtractionWorkflow.Tests/Models/SinInvariantTests.cs`,
  `tests/ExtractionWorkflow.Tests/Services/ExtractionPayloadBuilderTests.cs`,
  `tests/ExtractionWorkflow.Tests/BoundaryTests.cs`,
  `tests/DocumentIntelligence.Tests/DocumentIntelligence.Tests.csproj`,
  `tests/DocumentIntelligence.Tests/DeterministicProviderTests.cs`,
  `tests/DocumentIntelligence.Tests/FailClosedTests.cs`,
  `tests/DocumentIntelligence.Tests/NoEgressTests.cs` — depends on: none
  *(sequenced after T01 only so the parser change stays isolated for review and
  the two tasks do not both edit `scripts/test-projects.json`)*

---

## Wave 3

- [ ] **T03 — Append-only extraction persistence.** Declare `IExtractionStore` in
  `ExtractionWorkflow` and implement it as `SqlExtractionStore` in `Persistence`,
  in this one task, so the port ships implemented and exercised rather than
  declared and idle. Add the storage record shapes for correction history,
  classification overrides, duplicate determinations and decisions, and the
  structural audit vocabulary — a **parallel** `ExtractionAuditKind`, never new
  members on the merged `IntakeAuditKind`. Deliver the EF entities,
  configurations, and the single additive migration
  `20260803000000_AddDocumentExtraction`, creating only tables backing shapes that
  now exist; add the `ExtractionWorkflow` reference to `Persistence` and wire the
  store into the existing registration. The migration must not alter the initial
  or `AddClientIntake` migrations or any existing review, identity, or intake
  table. Tests, against a real relational database with the real migrations
  applied, must prove: append-only behaviour (a second version leaves the prior
  one byte-unchanged; no member updates or deletes in place); firm-scoped reads
  where a foreign-firm row is indistinguishable from a missing one; every
  taxpayer-data member takes a non-optional `TrustedFirmId` with no omitting
  overload; no column can hold a SIN, a source byte, a file name, a provider
  payload, or audit free text; every row carries the exclusive seven-year
  `RetentionUntilUtc` from the **existing** `RetentionPolicy` with no second
  retention authority declared; and the pending-model check is clean. Raise
  `Persistence.Tests`'s floor in the same pass. — files:
  `src/ExtractionWorkflow/Contracts/IExtractionStore.cs`,
  `src/ExtractionWorkflow/Models/ExtractionStorageRecords.cs`,
  `src/Persistence/Entities/ExtractionEntities.cs`,
  `src/Persistence/Configurations/ExtractionModelConfiguration.cs`,
  `src/Persistence/Repositories/SqlExtractionStore.cs`,
  `src/Persistence/TaxAiDbContext.cs`,
  `src/Persistence/PersistenceRegistration.cs`,
  `src/Persistence/Persistence.csproj`,
  `src/Persistence/Migrations/20260803000000_AddDocumentExtraction.cs`,
  `src/Persistence/Migrations/20260803000000_AddDocumentExtraction.Designer.cs`,
  `src/Persistence/Migrations/TaxAiDbContextModelSnapshot.cs`,
  `tests/Persistence.Tests/Repositories/SqlExtractionStoreTests.cs`,
  `tests/Persistence.Tests/Schema/ExtractionSchemaTests.cs`,
  `tests/Persistence.Tests/Schema/ExtractionMigrationTests.cs`,
  `scripts/test-projects.json` — depends on: T02

---

## Wave 4

- [ ] **T04 — Firm-scoped extraction and duplicate workflow.** Implement the
  accountant-initiated ingest workflow and the single implementation of spec 007's
  `IDocumentExtractionIngress`. Verify the four-part firm/case/document/content-key
  scope against server-stored state via `IIntakeStore.FindAsync` **before** opening
  a content lease; classify; build the `ExtractionEnvelope` with the server-derived
  `StoredIntakeCase.TaxYear`; call the **existing** `ExtractionEnvelopeValidator`
  and `T4CaseProcessor` unchanged and surface engine errors verbatim and in order;
  run neutral medical extraction and its structural validation; detect probable
  duplicates within the same firm and case only; append the immutable version, its
  non-SIN box values, its per-field outcomes, and one structural audit event.
  Tests must cover: the server-derived year beats a tax year supplied in a route
  value, query string, form field, header, and cookie; a document/case year
  mismatch and both adjacent years fail closed with neither value coerced; foreign-
  firm and missing documents produce byte-identical empty outcomes; a revoked
  session and an inactive membership each fail before any lease, provider call, or
  store operation; provider throw, timeout, empty result, null box list, and null
  box code each fail closed with nothing stored and nothing thrown; an unclassified
  document with no override produces no `T4RawInput`, no `ValidatedExtraction`, no
  `ProcessedT4Extraction`, no medical version, and only an audit event; confidence
  `0m` and `1m` follow identical paths and no threshold constant exists anywhere;
  duplicate box codes still reach the parser so the engine's own rejection fires;
  determinism over the same document, case, instant, and stub; no clock,
  environment, current-user, or `HttpContext` read; a planted SIN reaches no stored
  box value, audit row, projection, or error message; medical structural validation
  blocks acceptance on a missing date or amount and surfaces missing optional
  fields without judging them; SIN and SIN-derived values are never duplicate
  inputs; Box 54 is not required for comparison; and two distinct same-employer T4s
  are not duplicates. Raise `ExtractionWorkflow.Tests`'s floor in the same pass. —
  files: `src/ExtractionWorkflow/Contracts/IExtractionIngestWorkflow.cs`,
  `src/ExtractionWorkflow/Services/DocumentExtractionIngress.cs`,
  `src/ExtractionWorkflow/Services/ExtractionIngestWorkflow.cs`,
  `src/ExtractionWorkflow/Services/MedicalEvidenceValidator.cs`,
  `src/ExtractionWorkflow/Services/DuplicateCandidateDetector.cs`,
  `src/ExtractionWorkflow/Services/ExtractionProjection.cs`,
  `src/ExtractionWorkflow/Services/ExtractionWorkflowOptionsGuard.cs`,
  `src/ExtractionWorkflow/Models/ExtractionCommands.cs`,
  `src/ExtractionWorkflow/Models/ExtractionViews.cs`,
  `tests/ExtractionWorkflow.Tests/Ingest/IngestWorkflowTests.cs`,
  `tests/ExtractionWorkflow.Tests/Ingest/IngestFailClosedTests.cs`,
  `tests/ExtractionWorkflow.Tests/Ingest/ScopeAndTenancyTests.cs`,
  `tests/ExtractionWorkflow.Tests/Ingest/DuplicateDetectorTests.cs`,
  `tests/ExtractionWorkflow.Tests/Ingest/MedicalEvidenceValidatorTests.cs`,
  `tests/ExtractionWorkflow.Tests/Ingest/IngestPiiTests.cs`,
  `scripts/test-projects.json` — depends on: T03

---

## Wave 5

- [ ] **T05 — Corrections, overrides, acceptance, and T4 review creation.**
  Implement the review workflow. A correction is refused unless its field is in the
  closed vocabulary; each accepted correction appends an immutable version carrying
  opaque actor reference, actor kind, supplied instant, field identifier, prior
  value, corrected value, and preceding version, then **re-reads the source**,
  rebuilds the effective payload including Box 12 in memory only, and re-runs the
  complete existing validation, parser, mapping, processing, and review chain in
  order. Classification override is explicit, immutable, firm/case/document scoped,
  retains the original, appends a structural audit event, and accepts only the two
  supported paths. Duplicate resolution is an explicit accountant decision that
  never discards a document. Per-document acceptance and case-level finalization
  are separate explicit actions; **only finalization** calls
  `ReviewPackageAssembler.Assemble` → `ReviewPackageProjection.Project` →
  `IReviewRecordRepository.SaveAsync` → `AssociateReviewAsync`, assembling only the
  latest explicitly accepted version of each accepted T4 document. Tests must
  cover: the correction vocabulary is exactly the approved set; corrections
  targeting Box 12, the tax year, firm/case/participant/document/content-key scope,
  a CRA line, and a validation result are each refused through every channel with
  nothing mutated and no version created; no SIN appears as a prior or corrected
  value; a correction leaving the payload engine-invalid refuses acceptance and
  leaves no partially accepted state; an override bypasses no validation and never
  converts `Other` into accepted tax input; **no review record exists before
  case-level acceptance even when every document is individually accepted**;
  finalization saves exactly one versioned record covering all accepted documents
  and creates the one-time association; superseded versions are excluded; two
  participants, two tax years, and duplicate document identifiers each fail closed
  through the existing assembler invariants unrelaxed; no reopen, replace,
  reassociate, delete, post-correct, or "latest" path exists and a second
  association fails closed; and medical evidence never enters the record. Raise
  `ExtractionWorkflow.Tests`'s floor in the same pass. — files:
  `src/ExtractionWorkflow/Contracts/IExtractionReviewWorkflow.cs`,
  `src/ExtractionWorkflow/Services/ExtractionReviewWorkflow.cs`,
  `src/ExtractionWorkflow/Services/ClassificationOverridePolicy.cs`,
  `src/ExtractionWorkflow/Services/DuplicateResolutionPolicy.cs`,
  `src/ExtractionWorkflow/Models/CorrectionCommands.cs`,
  `tests/ExtractionWorkflow.Tests/Review/CorrectionVocabularyTests.cs`,
  `tests/ExtractionWorkflow.Tests/Review/CorrectionChainTests.cs`,
  `tests/ExtractionWorkflow.Tests/Review/ClassificationOverrideTests.cs`,
  `tests/ExtractionWorkflow.Tests/Review/AcceptanceAndFinalizationTests.cs`,
  `tests/ExtractionWorkflow.Tests/Review/ReviewRecordCreationTests.cs`,
  `tests/ExtractionWorkflow.Tests/Review/ReviewPiiTests.cs`,
  `scripts/test-projects.json` — depends on: T04

---

## Wave 6

- [ ] **T06 — Accountant extraction-review portal.** Expose the firm-scoped
  accountant routes — extraction start, classification override, correction,
  duplicate decision, per-document acceptance, and case final acceptance, plus the
  two read surfaces — each requiring an authenticated firm-scoped accountant with
  live session and membership revalidation, every mutation a non-GET protected by
  antiforgery, and every missing, foreign-firm, or inconsistent resource sharing
  one empty `404`. Implement `IExtractionContentReader` **beside the existing
  write-side adapter**, deriving its key from firm + case exactly as `StageAsync`
  does, so a mismatched scope cannot decrypt even if the metadata check were
  bypassed; register it so the ingest workflow is its only consumer. Wire both
  workflows, the store, the provider, and the reader into the single composition
  root with fail-closed startup validation. The new surface declares its **own**
  closed browser allowlists for T4 and medical content; provider and patient
  display names appear only here. Both the accountant-declared checklist category
  and the automated classification render when they disagree. Components are
  **static SSR by default**; declare `InteractiveServer` only on a component that
  genuinely requires client-side interaction, and justify it in the PR. Tests must
  cover: every route's authorization and antiforgery; no anonymous or client route
  reaches extraction; no hosted service, queue, or timer can resolve the reader; a
  content key paired with a different firm, case, or document reads nothing and
  returns the same empty `404`; no route returns, streams, redirects to, or
  references source content and no preview/download/print/email/export path exists;
  a planted byte marker appears in no response, log, metric, trace, SQL column, or
  web-root file; planted provider and patient names appear in no client-facing
  response, URL, cookie, header, log, or exception; projecting a fully populated
  object emits only allowlisted members; the spec 012 route with its allowlist,
  headers, order, single `FindAsync`, and `404` is identical before and after; the
  twelve intake routes, upload transport, rate policies, cookie scope, and
  `/health` are identical; `UseStaticFiles()` remains parameterless with
  `MapStaticAssets()` absent, no global render mode is added, and the framework
  endpoint inventory is unchanged; Production and Staging starts both refuse; and
  missing, empty, and invalid provider or reader configuration each refuse startup.
  Raise `TaxAI.Portal.Tests`'s floor in the same pass. — files:
  `src/TaxAI.Portal/ExtractionEndpoints.cs`,
  `src/TaxAI.Portal/ExtractionConfiguration.cs`,
  `src/TaxAI.Portal/DevelopmentEncryptedIntakeContentStore.cs`,
  `src/TaxAI.Portal/ModuleRegistration.cs`,
  `src/TaxAI.Portal/PortalStartupGuards.cs`,
  `src/TaxAI.Portal/Program.cs`,
  `src/TaxAI.Portal/TaxAI.Portal.csproj`,
  `src/TaxAI.Portal/Components/Routes.razor`,
  `src/TaxAI.Portal/Components/Pages/ExtractionListPage.razor`,
  `src/TaxAI.Portal/Components/Pages/ExtractionReviewPage.razor`,
  `src/TaxAI.Portal/Components/Pages/ExtractionReviewPageModel.cs`,
  `src/TaxAI.Portal/Components/Shared/ExtractionFieldTable.razor`,
  `src/TaxAI.Portal/Components/Shared/MedicalEvidencePanel.razor`,
  `src/TaxAI.Portal/Components/Shared/DuplicateCandidateNotice.razor`,
  `src/TaxAI.Portal/Components/Shared/ClassificationDisagreementNotice.razor`,
  `tests/TaxAI.Portal.Tests/Extraction/ExtractionRouteTests.cs`,
  `tests/TaxAI.Portal.Tests/Extraction/ExtractionAuthorizationTests.cs`,
  `tests/TaxAI.Portal.Tests/Extraction/ContentReaderTests.cs`,
  `tests/TaxAI.Portal.Tests/Extraction/ExtractionAllowlistTests.cs`,
  `tests/TaxAI.Portal.Tests/Extraction/ExtractionStartupGuardTests.cs`,
  `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs`,
  `scripts/test-projects.json` — depends on: T05

---

## Wave 7

- [ ] **T07 — Cross-module verification.** Prove only what cannot be proved inside
  a single module, and **repair nothing**: this task adds no product code, fixes no
  missing unit test, declares no contract, registers no project, and raises no
  floor that an earlier task should have raised. If it finds such a gap, that gap
  is a defect in the earlier task and goes back to it. Deliver: end-to-end T4
  extraction driven through the real workflows, the real `SqlExtractionStore`,
  `SqlIntakeStore`, and `SqlReviewRecordRepository` against a real relational
  database with the real migrations applied, ending in a persisted review record
  and its one-time association; end-to-end neutral medical-evidence extraction and
  review alongside it; the same journey driven over HTTP through the real portal
  composition, both workflows, the real deterministic provider, and the real
  Development content reader, including a correction, a duplicate candidate and its
  resolution, and a classification override; cross-tenant and safe-`404` proof that
  a firm-G resource is byte-identical to a missing one on every route; a capture
  sweep that plants a SIN, a source-byte marker, a provider name, and a patient
  name and asserts their absence across every HTTP response, browser model, URL,
  cookie, header, log, metric, trace, exception message, audit row, and SQL column;
  no-network-egress verification for the whole flow; and full-solution verification
  via `pwsh ./scripts/Invoke-SolutionTests.ps1`. Record the per-criterion result
  for AC-1 … AC-119 in `verification.md` and update the repository `CLAUDE.md`
  current-state section. — files:
  `tests/Persistence.Tests/EndToEnd/DocumentExtractionPersistenceTests.cs`,
  `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionAcceptanceTests.cs`,
  `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionSecurityAcceptanceTests.cs`,
  `tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionPiiSweepTests.cs`,
  `CLAUDE.md`,
  `specs/015-t4-and-medical-document-extraction/verification.md` — depends on: T06

---

## Planning checks

**1 — No task's primary output is scaffolding.** Every task ships working,
tested behaviour. The two projects arrive in T02 already carrying a functioning
classifier/extractor, a scope-checked content-read contract, the result shapes,
and the in-memory payload builder. The removed contract-only task has no
replacement.

**2 — No task ends with a stub.** `NotImplementedException`, empty projects, and
unused public contracts are forbidden by rule 2 and named in the exit condition
of T02. Each declared port is implemented in the task that declares it:

| Port | Declared in | Implemented in | Consumed in |
|---|---|---|---|
| `IDocumentIntelligenceProvider` | T02 | T02 (`DeterministicDocumentIntelligenceProvider`) | T04 |
| `IExtractionContentReader` | T02 | T06 (portal, beside the write adapter) | T04 |
| `IExtractionStore` | T03 | T03 (`SqlExtractionStore`) | T04 |
| `IExtractionIngestWorkflow` | T04 | T04 | T05, T06 |
| `IExtractionReviewWorkflow` | T05 | T05 | T06 |

**One honest exception:** `IExtractionContentReader` is declared and fully
specified in T02 but its production implementation lands in T06, because the only
correct place for it is beside the existing encrypted write adapter in the portal.
T02 does not leave it idle — T02 ships a real in-memory implementation used by its
own tests, so the contract is exercised from the first task that declares it, and
T06 replaces the registration rather than filling a hole. This is the single seam
where declaration and production implementation are in different tasks, and it is
deliberate.

**3 — Every production path is owned by exactly one task.** No file appears in
two tasks. `scripts/test-projects.json` is edited by six tasks (T01–T06), but
each is in its own wave, so no two ever touch it concurrently; T07 does not touch
it at all.

**4 — Every task delivers coherent tested behaviour.** T01 a parser rule change,
T02 a working extractor, T03 a working store, T04 a working ingest, T05 a working
review and finalization, T06 a working accountant surface, T07 proof.

**5 — No parallel wave modifies the same files.** Every wave contains exactly one
task, so the constraint holds vacuously. Parallelism was the price of removing the
contract wave; see *Why this plan is a chain*.

**6 — T07 is integration-only.** Its file list contains no `src/` path and no
`scripts/` path, and its description forbids repairing an earlier task.

**7 — Reported in chat.**

**8 — Tasks that remain broad.** T02, T05, and T06 are each larger than the
others. Each was left whole deliberately:

- **T02** — splitting the deterministic adapter out would leave
  `IDocumentIntelligenceProvider` declared and unimplemented at the end of the
  first task, which is exactly the scaffolding this revision removes.
- **T05** — corrections, overrides, acceptance, and finalization are one workflow
  object over one store; splitting them would put two tasks in the same file.
- **T06** — routes, components, the reader, and the composition root all land in
  `ModuleRegistration.cs` and `Program.cs`; splitting them would put two tasks in
  those files.

No task is too small. T01 is the shortest and is deliberately isolated because it
is the only change to merged tax-domain behaviour.

## Not in any task

Tax calculation, T1 generation, NETFILE/EFILE, CRA submission, production Azure
provisioning, an external or cloud document-intelligence provider, raw-content
preview/download/export, SIN correction or display, a confidence threshold,
source-document purge, post-finalization correction, and batch or background
extraction. Each is out of scope per `prd.md`.
