# Tasks: Accountant Review Package (spec 009)

**Feature class: `tax-domain`.** Implements the approved [prd.md](prd.md)
through the exact public contract in [architecture.md](architecture.md). Tax
year **2025 only**.

## Binding execution rules

- This file is **read-only once approved**. Never edit it or check a box.
- One explicitly named TaskID is implemented per branch, PR, and review. A human
  merges each completed wave to `master` before the next wave begins.
- Every task may create or modify **only** the exact files in its declared file
  list. Its own `.done` marker is part of that list.
- Every task in every earlier wave must have its `.done` marker before a later
  wave starts.
- Sibling tasks in one wave share no file path and use only types already
  declared by Wave 0. They may run in parallel in separate worktrees.
- Tests ship with the behavior they verify. No existing test may be removed,
  skipped, weakened, or rewritten to make a task pass.
- Before completing every task, run
  `dotnet build TaxAI.sln --no-incremental` and `dotnet test TaxAI.sln`.
  The new module must build warning-free and the full suite must pass.
- A marker is
  `specs/009-accountant-review-package/status/<TaskID>.done`, containing the PR
  number and commit SHA.
- `src/TaxLogicEngine/`, `tests/TaxLogicEngine.Tests/`, `src/TaxCases/`,
  `tests/TaxCases.Tests/`, `src/DocumentExtraction/`,
  `tests/DocumentExtraction.Tests/`, `src/CaseProcessing/`, and
  `tests/CaseProcessing.Tests/` are **untouchable in every task**.
- No task adds an `InternalsVisibleTo` grant to `CaseProcessing`. Only the new
  `CaseReview` project may expose internals to `CaseReview.Tests`.
- No CRA line-number literal, box-to-line table, box-code list,
  mapping-resource read, tax-producing engine call, arithmetic, or rounding may
  enter `CaseReview`. `T4SlipBoxAmounts.Enumerate` is the single permitted
  engine method.
- Production references are exactly `CaseProcessing` and `TaxLogicEngine`.
  There is no `TaxCases` reference and no direct `DocumentExtraction`
  reference.
- Never assert a total project count for `TaxAI.sln`. Assert only that this
  spec's production and test projects are each present exactly once at their
  approved paths.

### Mandatory PII and planted-value matrix

T01 and T06 own complementary versions of this matrix: constructed-contract
coverage in T01 and the real public pipeline in T06.

Plant each of these values separately:

- `123456789`;
- `123-456-789`;
- `123 456 789`;
- `ZZMARKERZZ`;
- `<<leak-me>>`.

Exercise each applicable value separately in:

- Box 12;
- another raw box value;
- employer name and address;
- employee name and address;
- `DocumentId`, `FirmId`, `CaseId`, `ParticipantId`, and `SourceReference`;
- reviewer reference, decided-at reference, and reviewer note.

For accepted and rejected paths, assert:

- every accepted value remains readable in memory where the contract exposes it
  programmatically;
- the raw Box 12 value is not reachable from any `CaseReview` output;
- the only SIN representation is the engine's masked `RecipientIdentifier`
  reached through `ReviewedSlip.Slip.Sin`;
- no plant and no nine-consecutive-digit run appears in any public
  `ToString()`, any module-originated `ValidationError.Field` or `Message`, or
  supported direct JSON serialization of a CaseReview public type;
- caller strings are `[JsonIgnore]`, including the note;
- no test passes vacuously: include reachability and matrix-size guards.

An invalid non-numeric amount belongs to a rejected processing path; it must
still be absent from all CaseReview failure output. Do not weaken the upstream
parser to make such a value succeed.

---

## Wave 0 — compiling public contract

- [ ] **T01** — Scaffold both projects and compile the complete public contract — files: `TaxAI.sln`, `src/CaseReview/CaseReview.csproj`, `src/CaseReview/Models/SignOffDecision.cs`, `src/CaseReview/Models/ReviewedBoxAmount.cs`, `src/CaseReview/Models/ReviewedSlip.cs`, `src/CaseReview/Models/ReviewDecisionDescriptor.cs`, `src/CaseReview/Models/ReviewSignOff.cs`, `src/CaseReview/Models/AccountantReviewPackage.cs`, `src/CaseReview/Models/AccountantReviewPackage.Decisions.cs`, `src/CaseReview/Models/ReviewFields.cs`, `src/CaseReview/Services/SupportedReviewTaxYears.cs`, `src/CaseReview/Services/ReviewPackageAssembler.cs`, `src/CaseReview/CLAUDE.md`, `src/CaseReview/AGENTS.md`, `tests/CaseReview.Tests/CaseReview.Tests.csproj`, `tests/CaseReview.Tests/ContractTests.cs`, `tests/CaseReview.Tests/Models/ContractOmissionTests.cs`, `specs/009-accountant-review-package/status/T01.done` — depends on: none

  Create the `CaseReview` net8.0 class library and xUnit project, both nullable
  enabled, and add them to `TaxAI.sln`. Add exactly the two production project
  references in architecture.md and
  `InternalsVisibleTo("CaseReview.Tests")` on the new module only. The test
  project references `CaseReview`.

  Declare every public signature exactly as architecture.md specifies:
  `SignOffDecision`, `ReviewedBoxAmount`, `ReviewedSlip`,
  `ReviewDecisionDescriptor`, `ReviewSignOff`,
  `AccountantReviewPackage`, `ReviewFields`, `SupportedReviewTaxYears`, and
  `ReviewPackageAssembler`. Numeric enum values, nullability, constructor
  visibility, `[JsonIgnore]`, `IEquatable<T>`, and partial-class placement are
  contract, not implementation choice.

  Implement constructor/property plumbing, fixed structural `ToString()`
  methods, the `ReviewFields` constants, and the 2025-only
  `SupportedReviewTaxYears` policy now. Behavioral methods and manual equality
  bodies not owned by Wave 0 compile with `NotImplementedException`; do not
  implement assembly or sign-off.

  Create `src/CaseReview/CLAUDE.md` before later implementation. It records the
  exact public inventory, two dependency edges, defensive snapshot rules,
  package-level acknowledgement gate, PII omission/masking boundary, 2025
  policy, forbidden tax behavior/dependencies, and TaskID workflow. Add a
  pointer-only local `AGENTS.md`.

  **Tests:** reflection proves the exact nine-type public surface, enum values,
  constructors, properties, methods, nullability, attributes, internal output
  constructors, no public package/content/sign-off constructor, no public
  setter, no record, and every stateful public class's own `ToString()`.
  `ReviewFields` is exactly the thirteen constants in architecture.md.
  `SupportedReviewTaxYears.All` is exactly `[2025]`; 2024 and 2026 fail.
  `ContractOmissionTests` run the constructed-value arm of the mandatory PII
  matrix over every public type and include reachability and serialization-shape
  guards. Instruction tests verify the local `CLAUDE.md` and pointer-only
  `AGENTS.md`.

  **Mechanical acceptance:** both projects compile warning-free; the two
  solution entries are present exactly once; `CaseReview.csproj` has exactly
  the approved references and no package/framework/content/resource reference;
  all contract tests and the full solution suite pass.

---

## Wave 1 — immutable model behavior (parallel)

T02 and T03 may run in parallel after T01. They share no file and every type
they reference already compiles from Wave 0.

- [ ] **T02** — Implement immutable reviewed-content and package-core semantics — files: `src/CaseReview/Models/ReviewedBoxAmount.cs`, `src/CaseReview/Models/ReviewedSlip.cs`, `src/CaseReview/Models/AccountantReviewPackage.cs`, `tests/CaseReview.Tests/Models/ReviewContentModelTests.cs`, `specs/009-accountant-review-package/status/T02.done` — depends on: T01

  Implement ordered defensive snapshots, manual ordinal/value equality,
  `Equals(object?)`, and stable `GetHashCode()` for reviewed amounts, reviewed
  slips, and the package core. `ReviewedSlip` preserves the exact `T4Slip`
  reference but snapshots line entries, each entry's source-box-code sequence,
  box amounts, and unmapped codes. `AccountantReviewPackage` snapshots reviewed
  slips and exposes no mutation path. Do not implement `RecordDecision`.

  **Tests:** accepted state preserves order and every value; slip identity is
  reference-equal; equivalent instances compare equal with equal hash codes;
  reordered/different content compares unequal; null/wrong-type equality is
  false; mutating caller-owned source lists after construction cannot mutate a
  reviewed slip or package; every collection property is read-only and every
  property get-only. Negative rendering and JSON tests prove no document,
  attribution, source, box code, amount, line number, identity string, or SIN
  escapes. Verify the package's unsigned state and fixed structural
  `ToString()`.

  **Mechanical acceptance:** model behavior needed by AC-3 through AC-10,
  AC-28, and AC-35 through AC-38 is present without parser, mapper, arithmetic,
  or mapping access; task tests and the full solution suite pass.

- [ ] **T03** — Implement decision-input and immutable sign-off model semantics — files: `src/CaseReview/Models/ReviewDecisionDescriptor.cs`, `src/CaseReview/Models/ReviewSignOff.cs`, `tests/CaseReview.Tests/Models/ReviewDecisionModelTests.cs`, `specs/009-accountant-review-package/status/T03.done` — depends on: T01

  Complete descriptor and sign-off value plumbing. The descriptor performs no
  validation and preserves null, blank, undefined-enum, opaque decided-at,
  note, and nullable acknowledgement inputs exactly so `RecordDecision` can
  reject them fail-closed. Implement ordinal/value equality and stable hash code
  for `ReviewSignOff`.

  **Tests:** descriptor construction never trims, parses, validates, or throws
  for the invalid shapes `RecordDecision` owns; `null`, `false`, and `true`
  acknowledgements remain distinct; sign-off equality covers every member;
  differing decision, strings, note, or acknowledgement compares unequal.
  Caller strings remain programmatically readable but absent from `ToString()`
  and JSON; direct JSON may expose only the safe enum and nullable boolean.
  Plant all reviewer-reference, decided-at, and note PII values and assert no
  leak.

  **Mechanical acceptance:** the models can represent every positive and
  negative sign-off case in AC-19 through AC-30 and AC-50 through AC-54 without
  adding validation outside the aggregate; task tests and the full suite pass.

---

## Wave 2 — feature behavior (parallel)

T04 and T05 may run in parallel after T02 and T03. Their production and test
files are disjoint. Both code only against the Wave-0 contract and completed
Wave-1 model behavior.

- [ ] **T04** — Implement package assembly completely — files: `src/CaseReview/Services/ReviewPackageAssembler.cs`, `tests/CaseReview.Tests/Services/ReviewPackageAssemblerTests.cs`, `specs/009-accountant-review-package/status/T04.done` — depends on: T01, T02, T03

  Implement the six assembly stages and exact error order from architecture.md.
  Materialize the input once. Structural null/empty/null-element failures
  short-circuit; independent coherence, year-policy, and duplicate-document
  failures accumulate once each in fixed order. Compare every identifier
  ordinally. Preserve supplied slip order.

  Build each reviewed slip from the processed output only. Preserve the exact
  `T4Slip` reference; value-snapshot lines and source codes; call only
  `T4SlipBoxAmounts.Enumerate` for box amounts; copy unmapped codes verbatim.
  Use fixed, non-interpolated error constants. Perform no arithmetic.

  **Tests — positive:** AC-1 through AC-10; single and several slips; supplied
  order; exact attribution; slip reference identity; line/provenance,
  box-amount, and unmapped-code sequence fidelity; deterministic equality;
  input immutability. AC-31 through AC-33: empty line mapping succeeds, zero
  remains the engine's value, and both half-cent cases equal upstream output.
  AC-42: package year equals every member.

  **Tests — negative, one rule each:** AC-11 null collection, AC-12 empty
  collection, AC-13 null element, AC-14 firm mismatch, AC-15 case mismatch,
  AC-16 participant mismatch, AC-17 ordinal/case/whitespace differences, and
  AC-18 duplicate document. Assert field constants, no caller values, no
  partial package, no exception, and fixed multi-error order. Use only real
  `ProcessedT4Extraction` values produced through spec 008.

  **Mechanical acceptance:** AC-1 through AC-18, AC-31 through AC-33, and AC-42
  pass at unit/service level; the implementation contains no tax-producing
  call, mapping resource, normalization, arithmetic, or unsupported-year
  fabrication; task tests and the full suite pass.

- [ ] **T05** — Implement decision recording and the acknowledgement gate completely — files: `src/CaseReview/Models/AccountantReviewPackage.Decisions.cs`, `tests/CaseReview.Tests/Models/AccountantReviewPackageDecisionTests.cs`, `specs/009-accountant-review-package/status/T05.done` — depends on: T01, T02, T03

  Implement `RecordDecision` in the exact fixed order from architecture.md.
  Null descriptor and already-signed package short-circuit. Remaining
  independent errors accumulate: undefined decision, blank reviewer reference,
  blank decided-at reference, required note, then unmapped-box
  acknowledgement. Messages are fixed constants and contain no input value.

  Scan all reviewed slips when deciding whether acknowledgement is required.
  For `Approved` with any unmapped code, `null` and `false` fail separately and
  `true` succeeds. `ChangesRequested` and `Rejected` do not require it. With no
  unmapped code, acknowledgement is optional. Record every accepted value
  exactly and return a replacement package reusing the immutable reviewed
  snapshot; never mutate the original.

  **Tests — negative:** AC-19 through AC-25 individually; null decision, invalid
  enum cast, each blank reviewer form, each blank decided-at form, missing note
  for each required decision, and a second decision. AC-50 absent required
  acknowledgement and AC-51 explicitly negative acknowledgement are separate
  tests with no replacement and an unchanged unsigned original.

  **Tests — positive:** AC-26 approval with no unmapped code and no
  acknowledgement; AC-27 both non-approval decisions with exact notes; AC-28
  unchanged reviewed content; AC-29 opaque non-date decided-at reference; AC-30
  deterministic replacement equality; AC-52 one affirmative acknowledgement
  over several slips/codes; AC-53 both non-approval decisions without
  acknowledgement; AC-54 exactly one package field and no per-code or mapped
  dispute semantics.

  **Mechanical acceptance:** AC-19 through AC-30 and AC-50 through AC-54 pass;
  no mutable setter, clock, authorization, persistence, case transition,
  per-code classification, or mapped-dispute field exists; task tests and the
  full suite pass.

---

## Wave 3 — public-pipeline assurance and boundary enforcement (parallel)

T06 and T07 may run in parallel after T04 and T05. They share no file and add
no production behavior.

- [ ] **T06** — Add end-to-end, PII, and determinism suites — files: `tests/CaseReview.Tests/EndToEnd/ReviewPackageAcceptanceTests.cs`, `tests/CaseReview.Tests/EndToEnd/ReviewPackagePiiTests.cs`, `tests/CaseReview.Tests/EndToEnd/ReviewPackageDeterminismTests.cs`, `specs/009-accountant-review-package/status/T06.done` — depends on: T01, T02, T03, T04, T05

  Exercise only the public pipeline:
  `ExtractionEnvelope → ExtractionEnvelopeValidator.Validate →
  T4CaseProcessor.Process → ReviewPackageAssembler.Assemble →
  AccountantReviewPackage.RecordDecision`. Use the embedded provider where the
  criterion requires production behavior and controlled in-memory providers to
  prove pass-through behavior without asserting a CRA line literal.

  **Acceptance suite:** multi-slip happy path; pass-through fidelity; empty
  mapping; zero and half-cent upstream values; all three decisions; package
  acknowledgement across several slips and several unmapped codes; approval
  without acknowledgement when no unmapped code exists; both absent and false
  gate failures; the original package and all upstream inputs remain unchanged.

  **PII suite:** run the full mandatory matrix against successful assembly,
  successful sign-off, every assembly rejection family, every decision
  rejection family, and acknowledgement failures. Serialize and render the
  package, each reviewed slip, each reviewed amount, descriptor, and sign-off.
  Assert the compact, hyphenated, and spaced SIN plants, non-numeric markers,
  and nine-digit runs never escape. Prove all accepted caller values are
  reachable in memory, the raw payload is unreachable from CaseReview output,
  and the one permitted SIN is `RecipientIdentifier` on `Slip.Sin`.

  **Determinism suite:** replay accepted and every rejected family five times;
  assert package/sign-off value equality, exact error sequence and ordering,
  equal behavior for distinct-but-equivalent input collections, and no input
  mutation.

  **Mechanical acceptance:** AC-1 through AC-10, AC-26 through AC-30, AC-31
  through AC-38, AC-42, and AC-50 through AC-53 pass end-to-end; task tests and
  the full suite pass.

- [ ] **T07** — Enforce public contract, dependency, no-tax-truth, PII, and year boundaries — files: `tests/CaseReview.Tests/Boundaries/CaseReviewBoundaryTests.cs`, `specs/009-accountant-review-package/status/T07.done` — depends on: T01, T02, T03, T04, T05

  Prove the module cannot become a second mapping authority, acquire a forbidden
  dependency, widen its year policy, or grow per-code acknowledgement.

  **Project and contract checks:** both spec projects are declared exactly once
  at approved paths, never by total solution count; production references are
  exactly `CaseProcessing` and `TaxLogicEngine`; no package/framework/content/
  embedded-resource item exists; no direct `DocumentExtraction` or any
  `TaxCases` reference; no reverse reference from any existing production or
  test project. Reflection pins the exact public contract and proves no
  per-slip/per-code acknowledgement or mapped-dispute member (AC-54).

  **Source and assembly checks:** AC-34 no arithmetic/rounding; AC-39 no SIN
  type/property, `RecipientIdentifier` construction, `SafeDiagnosticText`, or
  masking helper; AC-40 every module-originated field is a `ReviewFields`
  constant and every message is fixed/non-interpolated; AC-43 source and IL
  unconditionally compare element years and consult
  `SupportedReviewTaxYears` for every assembly path; AC-44 no CRA line literal,
  box-code list/table, mapping-resource name, parser, mapper, provider, or engine
  service except `T4SlipBoxAmounts.Enumerate`; AC-46 through AC-49 exact
  references, no reverse coupling, no clock/network/filesystem/database/
  environment/current-user/OCR/LLM/cloud/email/generated-id token, and exact
  solution presence.

  Scan executable lines and compiled references, not explanatory comments that
  name forbidden concepts to prohibit them. Include scanner guards proving
  every production file was visited and a known-present allowed token was found.
  Assert no `double`, `float`, `Math.Round`, `MidpointRounding`, arithmetic
  operation over money, T5 type, ASP.NET Core, EF Core, or SQL dependency.

  **Mechanical acceptance:** AC-34, AC-39 through AC-49, and AC-54 pass; the
  forbidden existing module trees are unchanged; task tests and the full suite
  pass.

---

## Wave 4 — final integration and repository-wide verification

- [ ] **T08** — Verify all 54 PRD criteria and record completion — files: `tests/CaseReview.Tests/EndToEnd/RepositoryVerificationTests.cs`, `specs/009-accountant-review-package/verification.md`, `NEXT.md`, `CLAUDE.md`, `specs/009-accountant-review-package/status/T08.done` — depends on: T01, T02, T03, T04, T05, T06, T07

  Add a repository-level verification matrix and human-readable evidence record.
  Map AC-1 through AC-54 to exact test names and commands. Re-run every task suite
  and the full solution. Review the final diff for declared-file ownership,
  public-contract exactness, project direction, deterministic error ordering,
  defensive snapshots, the acknowledgement gate, PII omission/masking, year
  isolation, no tax recomputation, and repository hygiene.

  Only after all 54 criteria pass, update `NEXT.md` and root `CLAUDE.md` to record
  spec 009 complete and name Portal Foundation as next. If any criterion fails,
  record the failure in `verification.md`, leave both state files unchanged,
  create no marker, and stop.

  **Tests:** repository verification asserts the spec's production and test
  projects are present exactly once at approved paths (never a total project
  count), every declared project/reference resolves, exact reference direction,
  exact public surface, one representative accepted full pipeline, one rejected
  acknowledgement gate, no unsupported year accepted, no public output
  constructor, no per-code acknowledgement, no forbidden dependency/token, and
  every pre-existing test remains green.

  **Mechanical acceptance:** `verification.md` has a pass/fail row for AC-1
  through AC-54 with exact evidence; `dotnet build TaxAI.sln --no-incremental`
  and `dotnet test TaxAI.sln` pass; no diff exists under any forbidden existing
  module/test tree; no tracked `bin/`, `obj/`, `.vs/`, `*.user`, secret, or
  `.claude/settings.local.json`; every task changed only declared files. On full
  pass, state files are updated and `T08.done` is created.

---

## Acceptance-criterion ownership

| PRD criteria | Primary owner(s) |
|---|---|
| Exact public surface, constructor visibility, JSON/ToString contract, fixed fields, 2025 policy, project scaffold | T01 |
| Immutable reviewed-content snapshots and package equality plumbing | T02 |
| Descriptor/sign-off value and PII plumbing | T03 |
| AC-1–AC-18, AC-31–AC-33, AC-42 | T04 |
| AC-19–AC-30, AC-50–AC-54 | T05 |
| AC-1–AC-10, AC-26–AC-38, AC-42, AC-50–AC-53 end-to-end | T06 |
| AC-34, AC-39–AC-49, AC-54 boundary evidence | T07 |
| AC-1–AC-54 final traceability and regression | T08 |

## Fail-closed negative-test ownership

| Rejection or forbidden rule | Required owner |
|---|---|
| Null collection, empty collection, null element | T04 |
| Firm, case, participant, or tax-year mismatch; ordinal near-match; duplicate document; unsupported year guard present | T04 behavior where constructible; T07 structural year evidence |
| Null decision; undefined decision; blank reviewer; blank decided-at; missing note; second decision | T05 |
| Required acknowledgement absent; required acknowledgement explicitly negative | T05, repeated end-to-end by T06 |
| Any caller/payload/note/SIN plant in rendering, JSON, or error output | T01 constructed surface; T06 public pipeline |
| Arithmetic, rounding, tax service/config, CRA literal, box-code list, forbidden dependency, reverse reference, per-code acknowledgement, mapped-dispute member | T07 |
| Any missing or failed AC evidence, unsupported year accepted, regression, or repository hygiene failure | T08 |

**8 tasks across 5 waves.** Waves 1, 2, and 3 each contain two parallel tasks
with disjoint file ownership. Every public signature exists after Wave 0, no
sibling references a sibling-created type, and every task includes its tests.
