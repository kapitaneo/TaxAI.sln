# Tasks: Tax Case Core (spec 006)

**Feature class: `tax-domain`.** This plan implements the approved PRD through
the exact public contract in [architecture.md](architecture.md).

## Binding execution rules

- This file is **read-only once approved**. Never edit it or check a box.
- One explicitly named TaskID is implemented per branch/PR/review. A human
  merges each wave to `master` before the next wave begins.
- Every task may create or modify **only** the exact files in its declared file
  list. A task's own `.done` marker is part of that list. Files outside the list
  must not change.
- Every task includes positive and negative xUnit coverage. No existing test may
  be removed, skipped, weakened, or rewritten to make a task pass.
- Before completion of **every** task, run `dotnet test TaxAI.sln`; the full
  solution suite must pass. Create the task marker only after its task-specific
  tests, full suite, and all acceptance criteria pass. If any check fails, do
  not create the marker.
- A marker is `specs/006-tax-case-core/status/<TaskID>.done` and contains the PR
  number and commit SHA. Every task in every earlier wave must have its marker
  before a later wave starts.
- Rejectable operations use `ParseResult<CaseOperationOutcome>` and fail closed:
  failure has at least one `ValidationError`, no replacement case, no change
  record, and no mutation of the original case.
- No full SIN, CRA line/box logic, mapping-resource inspection, generated id,
  system clock, I/O, persistence, network, current-user, OCR, LLM, cloud, email,
  ASP.NET Core, EF Core, or SQL dependency may enter the module.

### Mandatory PII matrix for T01 and T08

Both tasks test `123456789` and `123-456-789` **separately** when embedded in
each of: `CaseId`, `FirmId`, `ParticipantId`, `DisplayName`, `EvidenceId`,
`SourceReference`, `RequirementId`, `QuestionId`, `Prompt`, `Answer`, invalid
category input, `CorrelationReference`, Reopen reason, and
`CaseChangeRecord.Detail`. T01 exercises constructed contract values and invokes
the internal redactor by reflection for operation-only inputs; T08 repeats the
matrix through the finished public operations, including actual validation,
readiness, change-record, and Reopen paths. In both tasks, raw in-memory values
remain readable where the contract requires them, defensive direct JSON contains
none of the raw caller strings, every public `ToString()`/error/readiness reason/
change-record diagnostic contains neither raw pattern, and any visible form is
only `***-**-789`. `SafeDiagnosticTextTests` additionally pins
`123 456 789` → `***-**-789` and mixed/multiple-pattern left-to-right behavior.

---

## Wave 0 — compiling public contract

- [ ] **T01 — Scaffold both projects and compile the complete public contract.**

  Create the `TaxCases` net8.0 class library and `TaxCases.Tests` xUnit project,
  add both to `TaxAI.sln`, and add the one-way production project reference
  `TaxCases → TaxLogicEngine`. Declare every namespace, enum value, record
  constructor, property, and public method exactly as specified by
  architecture.md. Implement value-record plumbing, defensive collection
  storage, the private `TaxCase` constructor/core clone helpers,
  `SupportedTaxYears`, and `CaseStatusTransitions`. The seven aggregate
  operations and `ReadinessCalculator.Calculate` compile with
  `NotImplementedException` bodies in their final files. Implement the internal
  deterministic `SafeDiagnosticText` helper and safe `ToString()`/`JsonIgnore`
  contract. Create the module-local instructions now, before implementation:
  `CLAUDE.md` documents the approved boundary, exact public contract, PII-safe
  diagnostics, 2025 policy, forbidden dependencies, and fail-closed workflow;
  local `AGENTS.md` is only a pointer to root `AGENTS.md`, root `CLAUDE.md`, and
  local `CLAUDE.md`. No public signature is deferred to a later wave.

  **Allowed files:**
  - modified: `TaxAI.sln`
  - created: `src/TaxCases/TaxCases.csproj`
  - created: `src/TaxCases/Models/RelationshipKind.cs`
  - created: `src/TaxCases/Models/DocumentCategory.cs`
  - created: `src/TaxCases/Models/CaseStatus.cs`
  - created: `src/TaxCases/Models/CaseChangeKind.cs`
  - created: `src/TaxCases/Models/ReadinessState.cs`
  - created: `src/TaxCases/Models/ReadinessReasonKind.cs`
  - created: `src/TaxCases/Models/IntakeQuestionScope.cs`
  - created: `src/TaxCases/Models/CaseParticipant.cs`
  - created: `src/TaxCases/Models/ReceivedEvidence.cs`
  - created: `src/TaxCases/Models/EvidenceRequirement.cs`
  - created: `src/TaxCases/Models/IntakeQuestion.cs`
  - created: `src/TaxCases/Models/IntakeAnswer.cs`
  - created: `src/TaxCases/Models/RequirementChecklist.cs`
  - created: `src/TaxCases/Models/ReadinessReason.cs`
  - created: `src/TaxCases/Models/ReviewReadiness.cs`
  - created: `src/TaxCases/Models/CaseChangeRecord.cs`
  - created: `src/TaxCases/Models/CaseOperationOutcome.cs`
  - created: `src/TaxCases/Models/CreateTaxCaseRequest.cs`
  - created: `src/TaxCases/Models/ParticipantDescriptor.cs`
  - created: `src/TaxCases/Models/EvidenceDescriptor.cs`
  - created: `src/TaxCases/Models/EvidenceRequirementDescriptor.cs`
  - created: `src/TaxCases/Models/IntakeAnswerDescriptor.cs`
  - created: `src/TaxCases/Models/ChecklistDescriptor.cs`
  - created: `src/TaxCases/Models/TaxCase.cs`
  - created: `src/TaxCases/Models/TaxCase.Create.cs`
  - created: `src/TaxCases/Models/TaxCase.Participants.cs`
  - created: `src/TaxCases/Models/TaxCase.Evidence.cs`
  - created: `src/TaxCases/Models/TaxCase.Intake.cs`
  - created: `src/TaxCases/Models/TaxCase.Lifecycle.cs`
  - created: `src/TaxCases/Services/SupportedTaxYears.cs`
  - created: `src/TaxCases/Services/CaseStatusTransitions.cs`
  - created: `src/TaxCases/Services/ReadinessCalculator.cs`
  - created: `src/TaxCases/Services/SafeDiagnosticText.cs`
  - created: `src/TaxCases/CLAUDE.md`
  - created: `src/TaxCases/AGENTS.md`
  - created: `tests/TaxCases.Tests/TaxCases.Tests.csproj`
  - created: `tests/TaxCases.Tests/ContractTests.cs`
  - created: `tests/TaxCases.Tests/Services/SupportedTaxYearsTests.cs`
  - created: `tests/TaxCases.Tests/Services/CaseStatusTransitionsTests.cs`
  - created: `tests/TaxCases.Tests/Services/SafeDiagnosticTextTests.cs`
  - created: `specs/006-tax-case-core/status/T01.done`

  **Depends on:** none.

  **Tests:** Positive xUnit tests instantiate every public record, resolve all
  exact property/method signatures by reflection, prove 2025 is supported, and
  prove every allowed lifecycle edge. They also prove the participant-scoped
  requirement/question contracts, that `ParticipantDescriptor` exposes only a
  masked SIN and has no public raw-SIN property, that every public non-static class or record with instance state
  owns a safe `ToString()`; enums and static service classes are excluded,
  and that **every caller-controlled public string property**
  (all ids plus free text and untrusted category strings) is `[JsonIgnore]` while
  its required in-memory getter remains available. Run the full mandatory PII
  matrix above at the contract/value level. Redactor tests cover compact,
  hyphenated, and space-separated forms, longer compact runs, multiple matches,
  and culture-independent ordinal behavior. Instruction tests prove local `CLAUDE.md` contains all
  required rules and local `AGENTS.md` is pointer-only.
  Negative xUnit tests prove 2024/2026 are
  unsupported; every forbidden transition (including `Intake → Completed`,
  plain `Completed → InformationGathering`, every transition from `Cancelled`,
  and every transition to `Archived` by proving no such enum value exists) is
  rejected; reflection proves `TaxCase` has no public constructor, no writable
  readiness/firm property, and no ownership-transfer method.

  **Mechanical acceptance:** `dotnet build TaxAI.sln` succeeds; the solution
  lists exactly four projects; `TaxCases.csproj` references only
  `TaxLogicEngine.csproj`; `TaxLogicEngine.csproj` does not reference TaxCases;
  contract reflection matches architecture.md exactly; reflection finds no
  caller-controlled serialized string lacking `[JsonIgnore]`; JSON and
  diagnostic outputs pass the mandatory PII matrix; the local instructions
  exist before T02 and contain no conflicting/duplicated policy; task tests and
  `dotnet test TaxAI.sln` pass. Only then create `T01.done`; otherwise create no
  marker.

---

## Wave 1 — creation and immutable ownership

- [ ] **T02 — Implement case creation, ownership, 2025 policy, and initial PII.**

  Implement `TaxCase.Create`: validate the request without throwing; require
  non-blank caller-supplied `CaseId` and `FirmId`, tax year 2025, and one valid
  `PrimaryTaxpayer`; reject missing or non-primary descriptors and invalid enum
  values; allow an absent SIN and convert a present non-blank SIN to the reused
  `RecipientIdentifier`; start in `Intake` with an unconfigured checklist; emit
  exactly one deterministic `CaseCreated` record. Firm ownership is get-only and
  inherited by contained state.

  **Allowed files:**
  - modified: `src/TaxCases/Models/TaxCase.Create.cs`
  - created: `tests/TaxCases.Tests/Models/TaxCaseCreateTests.cs`
  - created: `specs/006-tax-case-core/status/T02.done`

  **Depends on:** T01.

  **Tests:** Positive xUnit tests cover a minimal 2025 case, absent SIN, masked
  present SIN, initial status/checklist, firm ownership, and exact creation
  change record. Negative xUnit tests cover null request, blank/missing case id,
  blank/missing firm id, absent/2024/2026 year, missing primary, wrong/undefined
  relationship, blank participant id, blank-present SIN, and a firm-B primary
  descriptor on a firm-A request; empty input returns one error per missing
  field and never throws. Failures produce no case/record and never coerce a
  year.

  **Mechanical acceptance:** AC-1, AC-3, AC-4, AC-5, AC-13, AC-36, AC-38,
  AC-41, the creation arm of AC-12, and creation arms of AC-33/42 pass;
  `RecipientIdentifier` is the only
  stored SIN type; `dotnet test TaxAI.sln` passes. Only then create `T02.done`;
  otherwise create no marker.

---

## Wave 2 — independently owned state operations (parallel)

T03, T04, and T05 may run in parallel after T02. They use only Wave-0 contract
types, own disjoint production/test files, and do not reference a type or file
created by a sibling.

- [ ] **T03 — Implement participant membership, separation, and PII rules.**

  Implement `AddParticipant`: validate mutable status, id, defined relationship,
  uniqueness, exactly-one-primary/at-most-one-spouse invariants, optional same
  firm reference, optional SIN conversion/masking, and immutable replacement
  state. Dependants are structural and unlimited; no eligibility logic is added.

  **Allowed files:**
  - modified: `src/TaxCases/Models/TaxCase.Participants.cs`
  - created: `tests/TaxCases.Tests/Models/TaxCaseParticipantTests.cs`
  - created: `specs/006-tax-case-core/status/T03.done`

  **Depends on:** T01, T02.

  **Tests:** Positive xUnit tests add one spouse and multiple distinct dependants,
  retrieve each only by its case-local id, preserve insertion order, accept no
  SIN, mask a present SIN, and verify `ParticipantAdded`. Negative xUnit tests
  reject null/blank id, duplicate id, second primary, second spouse, undefined
  relationship, and firm-B participant on firm-A case; each proves the original
  case and change-record count are unchanged and messages contain no full SIN.

  **Mechanical acceptance:** AC-6, AC-7, AC-8, participant arms of AC-12,
  AC-32/33/37/42 pass; no CRA eligibility rule exists; task tests and
  `dotnet test TaxAI.sln` pass. Only then create `T03.done`; otherwise no marker.

- [ ] **T04 — Implement evidence identity, category, attribution, and year rules.**

  Implement `AttachEvidence`: validate mutable status, non-blank case-wide
  unique evidence id, supported category string, in-case participant, exact
  case tax year, and optional same-firm reference. Store receipt metadata only.

  **Allowed files:**
  - modified: `src/TaxCases/Models/TaxCase.Evidence.cs`
  - created: `tests/TaxCases.Tests/Models/TaxCaseEvidenceTests.cs`
  - created: `specs/006-tax-case-core/status/T04.done`

  **Depends on:** T01, T02.

  **Tests:** Positive xUnit tests attach and retrieve two distinct T4 records for
  one participant and two distinct medical records, proving identity is separate
  from category and attribution/year/source reference are retained. Negative
  xUnit tests reject null descriptor, blank/missing/duplicate id, missing/unknown
  category (error identifies only its `SafeDiagnosticText`-redacted form),
  non-member participant, absent/2024 evidence year,
  undefined/case-mismatched data, and firm-B evidence on firm-A case; failures
  add no evidence/change record and do not alter the case year.

  **Mechanical acceptance:** AC-9, AC-10, AC-11, evidence arms of AC-12,
  AC-18–AC-21, AC-32/33/39/42 pass; no parsing, amount, file bytes, CRA mapping,
  or mapping-file inspection exists; task tests and `dotnet test TaxAI.sln`
  pass. Only then create `T04.done`; otherwise no marker.

- [ ] **T05 — Implement participant-scoped requirements and intake answers.**

  Implement `ConfirmChecklist` and `AnswerQuestion`. Confirmation validates
  present lists; each evidence requirement needs a non-blank unique
  `RequirementId`, in-case `ParticipantId`, supported category, and positive
  `MinimumCount`; questions need non-blank unique ids/prompts and a valid explicit
  scope (`Case` with null participant, or `Participant` with one in-case target).
  Confirmed-empty remains valid. Reconfirmation snapshots inputs and clears
  answers for removed or retargeted questions. Answering requires a configured
  question and non-blank answer: case-level answers use null attribution;
  participant-targeted answers must use the exact target. Re-answering replaces
  the prior answer. Each success emits exactly one record.

  **Allowed files:**
  - modified: `src/TaxCases/Models/TaxCase.Intake.cs`
  - created: `tests/TaxCases.Tests/Models/TaxCaseIntakeTests.cs`
  - created: `specs/006-tax-case-core/status/T05.done`

  **Depends on:** T01, T02.

  **Tests:** Positive xUnit tests distinguish unconfigured from confirmed-empty;
  configure separate primary/spouse T4 requirements, a two-T4 minimum, and
  participant/case-level questions; answer with exact permitted attribution;
  replace an answer; reconfirm and clear removed/retargeted answers; prove
  defensive snapshots and exact change records. Negative tests reject null
  descriptor/list, blank/duplicate requirement id, non-member requirement
  participant, missing/unknown category, absent/zero/negative minimum, blank/
  duplicate question id, blank prompt, undefined scope, case scope with a
  participant, participant scope with null/non-member target, answer while
  unconfigured, unknown question, participant-A answer to participant-B target,
  non-null attribution on a case-level answer, and blank answer. Each proves no
  state/record change. Prompt/answer containing `123456789` remains available as
  domain data but is absent/redacted in JSON and every public diagnostic string.

  **Mechanical acceptance:** AC-16, checklist/intake portions of requirements
  18–21, AC-32/33/42 pass; requirements/questions inherit the case firm and
  evidence/answers cannot
  cross participant boundaries; task tests and `dotnet test TaxAI.sln` pass.
  Only then create `T05.done`; otherwise no marker.

---

## Wave 3 — derived readiness

- [ ] **T06 — Implement fail-closed readiness derivation.**

  Implement `ReadinessCalculator.Calculate` only. `TaxCase.Readiness` remains a
  get-only derivation: unconfigured checklist is always NotReady; otherwise
  require exactly one primary; allocate distinct evidence to each requirement in
  checklist/evidence insertion order, matching both participant and exact
  category until its positive minimum is met; and require an exactly attributed
  answer for every question. One evidence identity can satisfy at most one
  requirement. Produce deterministic SIN-safe reasons per unmet requirement/
  question; confirmed-empty may be Ready. `Other` satisfies only `Other`.

  **Allowed files:**
  - modified: `src/TaxCases/Services/ReadinessCalculator.cs`
  - created: `tests/TaxCases.Tests/Services/ReadinessCalculatorTests.cs`
  - created: `specs/006-tax-case-core/status/T06.done`

  **Depends on:** T01, T02, T03, T04, T05.

  **Tests:** Positive xUnit tests cover confirmed-empty Ready, the last required
  evidence/answer changing derived state to Ready, separate primary/spouse T4
  requirements, two distinct T4s satisfying `MinimumCount = 2`, exact `Other`
  satisfaction, stable allocation/reason ordering, and recomputation on each
  immutable replacement. Negative tests cover unconfigured checklist, one
  participant's T4/medical evidence failing another's requirement, one T4
  failing a two-T4 minimum, one evidence record not satisfying two same-key
  requirements, `Other` failing T4/medical, incorrectly attributed/unanswered
  questions, and a reflection-created no-primary state. Reasons identify only
  redacted requirement/question ids and remaining counts, never prompt/answer or
  SIN; missing-evidence reasons name requirement id, participant id, exact
  category, and remaining count through the safe diagnostic formatter.

  **Mechanical acceptance:** AC-17 and AC-22–AC-26 pass; reflection still proves
  no writable Ready flag; task tests and `dotnet test TaxAI.sln` pass. Only then
  create `T06.done`; otherwise no marker.

---

## Wave 4 — lifecycle, terminal states, and reopen

- [ ] **T07 — Implement lifecycle transitions, Completed restrictions, and Reopen.**

  Implement `TransitionTo` and `Reopen`. Enforce the exact transition table;
  require Ready to enter `InReview`; allow `Completed` only from `InReview`;
  route `Completed → InformationGathering` only through Reopen with a non-blank
  reason; make `Cancelled` terminal. The shared mutable guard must make every
  participant/evidence/checklist/answer mutation fail on Completed/Cancelled.

  **Allowed files:**
  - modified: `src/TaxCases/Models/TaxCase.Lifecycle.cs`
  - created: `tests/TaxCases.Tests/Models/TaxCaseLifecycleTests.cs`
  - created: `specs/006-tax-case-core/status/T07.done`

  **Depends on:** T01, T02, T03, T04, T05, T06.

  **Tests:** Positive table-driven xUnit tests cover every allowed normal edge,
  Ready entry to review, completion, cancellation from every non-terminal state,
  `InReview → InformationGathering`, and successful Reopen with exact status,
  recomputed readiness, reason, correlation, and change record. Negative tests
  cover every forbidden pair, NotReady entry to review, plain transition out of
  Completed, null/blank Reopen reason, Reopen outside Completed, every mutation
  family against Completed, and every mutation/Reopen against Cancelled; all
  prove unchanged original state and no record.

  **Mechanical acceptance:** AC-14, AC-15, AC-27–AC-31, lifecycle arms of
  AC-33/42 pass; `Archived` is absent; task tests and `dotnet test TaxAI.sln`
  pass. Only then create `T07.done`; otherwise no marker.

---

## Wave 5 — end-to-end assurance and module boundary documentation (parallel)

T08 and T09 may run in parallel after T07. They share no file and create no
types consumed by each other.

- [ ] **T08 — Add public-API acceptance, determinism, and PII suites.**

  Exercise the finished module only through its public API. Verify the full
  family case, readiness-to-review-to-completion path, accepted record shapes,
  rejected-operation atomicity, deterministic replay, participant separation,
  firm/year isolation, and exhaustive SIN-safe output.

  **Allowed files:**
  - created: `tests/TaxCases.Tests/EndToEnd/TaxCaseAcceptanceTests.cs`
  - created: `tests/TaxCases.Tests/EndToEnd/TaxCaseDeterminismTests.cs`
  - created: `tests/TaxCases.Tests/EndToEnd/TaxCasePiiTests.cs`
  - created: `specs/006-tax-case-core/status/T08.done`

  **Depends on:** T01, T02, T03, T04, T05, T06, T07.

  **Tests:** Positive xUnit tests cover AC-2, equal observable state/readiness and
  equal change-record sequences from identical replays, and every accepted
  change kind. Negative matrix tests submit one rejected input for every public
  operation and prove no new case/record/state, cross-firm/year data never
  appears, participant A data is not returned as B's, and no output/string/error/
  reason/record contains a compact, hyphenated, or space-separated full SIN.
  Run the complete mandatory PII matrix above for both `123456789` and
  `123-456-789` through actual operations. Also pass `123 456 789` through at
  least one accepted domain value, one rejected validation input, one readiness
  reason identifier, and one change-record diagnostic. Raw in-memory properties
  retain required values, while defensive direct JSON omits every caller string
  and all public `ToString()`/diagnostics omit or deterministically redact them.

  **Mechanical acceptance:** AC-2, AC-32, AC-33, AC-35, AC-37, AC-39, and AC-43
  pass; compact/hyphen/space raw patterns never appear and only
  `***-**-789` may appear; task tests and `dotnet test TaxAI.sln`
  pass. Only then create `T08.done`; otherwise no marker.

- [ ] **T09 — Verify module instructions and enforce dependency/source boundaries.**

  Add xUnit boundary checks and verify the Wave-0 module instructions remain
  complete and binding. Do not modify either instruction file, root `CLAUDE.md`,
  or `NEXT.md`, and do not claim spec 006 is complete.

  **Allowed files:**
  - created: `tests/TaxCases.Tests/Boundaries/TaxCasesBoundaryTests.cs`
  - created: `specs/006-tax-case-core/status/T09.done`

  **Depends on:** T01, T02, T03, T04, T05, T06, T07.

  **Tests:** Positive xUnit tests prove the solution/project paths, the sole
  production reference `TaxCases → TaxLogicEngine`, exact 2025 policy location,
  and required Wave-0 instruction pointers/inventory. Negative source/project scans
  reject reverse TaxLogicEngine dependency, ASP.NET Core, EF Core, SQL/database,
  filesystem, network, clock/environment/current-user, OCR, LLM, cloud, email,
  generated ids, money/tax calculation, CRA line/box logic, mapping-resource
  inspection, API/output DTOs or hosts, `Archived`, and duplicated rules in the
  local bridge.

  **Mechanical acceptance:** AC-34 and AC-40 pass; no forbidden package/project
  reference or source token exists; TaxLogicEngine production/tests are
  unchanged; task tests and `dotnet test TaxAI.sln` pass. Only then create
  `T09.done`; otherwise no marker.

---

## Wave 6 — final integration and repository-wide verification

- [ ] **T10 — Verify every PRD criterion and record the final result.**

  Add a repository-level xUnit verification matrix and create the human-readable
  verification record. Run every task-specific suite and the entire solution;
  map AC-1 through AC-43 to exact test names/evidence; review the final diff for
  declared-file ownership, project direction, deterministic boundaries, PII,
  hygiene, and unchanged TaxLogicEngine behavior. **Only after every AC passes,**
  update `NEXT.md` to mark spec 006 complete and make Document Extraction
  Contract the next feature; update root `CLAUDE.md` Current state to mark spec
  006 complete; then create `T10.done`. If any AC fails, `verification.md`
  records the failure, but `NEXT.md` and `CLAUDE.md` remain unchanged and no
  marker is created.

  **Allowed files:**
  - created: `tests/TaxCases.Tests/EndToEnd/RepositoryVerificationTests.cs`
  - created: `specs/006-tax-case-core/verification.md`
  - modified only after AC-1–AC-43 all pass: `NEXT.md`
  - modified only after AC-1–AC-43 all pass: `CLAUDE.md`
  - created: `specs/006-tax-case-core/status/T10.done`

  **Depends on:** T01, T02, T03, T04, T05, T06, T07, T08, T09.

  **Tests:** Positive xUnit tests prove all four expected solution projects,
  resolvable references, public-contract reflection, and representative complete
  case behavior. Negative xUnit tests prove no unexpected project/reference,
  no reverse dependency, no unsupported year/category/lifecycle value, no
  writable readiness/firm ownership, and no forbidden boundary token.

  **Mechanical acceptance:** `verification.md` has a pass/fail row for every
  AC-1 through AC-43 with exact command/test evidence; `dotnet build TaxAI.sln`
  and `dotnet test TaxAI.sln` pass; no tracked `bin/`, `obj/`, `.vs/`, `*.user`,
  secret, or `.claude/settings.local.json` exists; `git diff` shows no change
  under `src/TaxLogicEngine/` or `tests/TaxLogicEngine.Tests/`; every task changed
  only its declared files. On full pass, state files name spec 006 complete and
  Document Extraction Contract next, then `T10.done` is created. On any failure,
  record it only in `verification.md`, leave state files unchanged, create no
  marker, and stop.

---

## Acceptance-criterion ownership

| PRD criteria | Owning task(s) |
|---|---|
| AC-1, 3–5, 13, 36, 38, 41–42 (creation) | T01, T02 |
| AC-6–8, 12 (participant), 32–33/37/42 (participant) | T03 |
| AC-9–12 (evidence), 18–21, 32–33/39/42 (evidence) | T04 |
| AC-16, requirements 18–21 plus participant-scoped requirement/question validation, 32–33/42 (checklist/intake) | T05 |
| AC-17, 22–26 plus participant/count/exact-category/no-reuse readiness | T01, T06 |
| AC-14–15, 27–31, 33/42 (lifecycle) | T01, T07 |
| AC-2, 32–33, 35, 37, 39, 43 | T08 |
| AC-34, 40 and dependency/instruction boundaries | T09 |
| AC-1–43 final traceability and repository verification | T10 |

## Fail-closed negative-test ownership

| Rejection rule | Required negative xUnit owner |
|---|---|
| Null/empty create input; missing case/firm/year/primary; unsupported year; wrong or undefined primary relationship; blank participant id/SIN; cross-firm primary | T02 |
| Blank/duplicate participant; second primary/spouse; undefined relationship; cross-firm participant; SIN-safe errors | T03 |
| Blank/duplicate evidence identity; missing/unknown category; non-member attribution; absent/mismatched year; cross-firm evidence | T04 |
| Null/unconfigured checklist input; blank/duplicate requirement id; non-member requirement participant; missing/unknown category; absent/non-positive minimum; blank/duplicate question; blank prompt; invalid scope/target; unknown/cross-participant/case-level-misattributed answer; blank answer | T05 |
| Unconfigured checklist; participant/category mismatch; insufficient distinct evidence count; evidence reuse across requirements; `Other` mismatch; unanswered/misattributed question; no-primary defensive derivation | T06 |
| Every forbidden transition; NotReady review entry; Completed mutation/plain transition; invalid Reopen; every Cancelled mutation/Reopen | T07 |
| One rejected example for every public operation, with no replacement value/change record/state mutation; cross-firm/year/participant isolation; full-SIN output sweep | T08 |
| Every forbidden dependency/I/O/clock/generated-id/CRA-mapping/Archived source or project reference | T09 |
| Unexpected solution project/reference/value/public mutator and any failed AC-1–AC-43 evidence | T10 |

**10 tasks across 7 sequential waves.** Wave 2 has three parallel tasks
(T03–T05); Wave 5 has two parallel tasks (T08–T09). All siblings have disjoint
file ownership and depend only on artifacts merged in earlier waves.
