# Tasks: T4 Case Processor (spec 008)

**Feature class: `tax-domain`.** Implements the approved [prd.md](prd.md)
through the exact public contract in [architecture.md](architecture.md).
Tax year **2025 only**.

## Binding execution rules

- This file is **read-only once approved**. Never edit it or check a box.
- One explicitly named TaskID is implemented per branch/PR/review. A human merges
  each wave to `master` before the next wave begins.
- Every task may create or modify **only** the exact files in its declared file
  list. A task's own `.done` marker is part of that list. Files outside the list
  must not change. `src/TaxLogicEngine/`, `tests/TaxLogicEngine.Tests/`,
  `src/TaxCases/`, `tests/TaxCases.Tests/`, `src/DocumentExtraction/`, and
  `tests/DocumentExtraction.Tests/` are **untouchable in every task**.
- Every task includes positive and negative xUnit coverage. No existing test may
  be removed, skipped, weakened, or rewritten to make a task pass.
- Before completion of **every** task, run `dotnet build TaxAI.sln
  --no-incremental` and `dotnet test TaxAI.sln`; the build must be warning-free
  for this module and the full solution suite must pass. Use
  `--no-incremental` — an incremental build hid a warning during spec 007 T02.
- A marker is `specs/008-t4-case-processor/status/<TaskID>.done` containing the
  PR number and commit SHA. Every task in every earlier wave must have its marker
  before a later wave starts.
- Processing returns `ParseResult<ProcessedT4Extraction>` and **fails closed**:
  failure carries ≥1 `ValidationError` and no value; success carries a value and
  no errors; nothing is mutated, coerced, or partially accepted; **nothing
  throws** on untrusted input.
- **No CRA line number, box→line table, mapping-resource read, or arithmetic may
  enter this module.** No `TaxCases` reference. No new SIN type or masking
  helper. No `RecipientIdentifier` constructed here.
- **Never assert a total project count for `TaxAI.sln`.** Assert that this
  module's projects are each declared exactly once at their approved path. A
  total count froze the solution against new modules twice already (root
  `CLAUDE.md` now forbids it).

### Mandatory PII matrix (T01, T03, T04)

Plant `123456789`, `123-456-789`, `123 456 789`, and the non-numeric markers
`ZZMARKERZZ` and `<<leak-me>>` **separately** in each of: the Box 12 value,
another box's value, `EmployerName`, `EmployerAddress`, `EmployeeName`,
`EmployeeAddress`, and each of the extraction's `DocumentId`, `FirmId`, `CaseId`,
`ParticipantId`, `SourceReference`. For an accepted and every rejected pipeline,
assert: values stay readable in memory where the contract exposes them
programmatically; **no** planted string and **no** 9-consecutive-digit run
appears in any `ToString()`, any `ValidationError.Field` or `Message`, or any
supported direct JSON serialization. Omission means not even a masked form like
`***-**-789` appears. The one permitted SIN representation is the engine's
`RecipientIdentifier` reached via `T4Slip.Sin`. Non-numeric markers are required
because a digit-run regex cannot catch an echoed document id.

Include a **reachability guard**: assert each plant slot is readable back off the
result, so a mis-wired slot fails loudly instead of passing vacuously.

---

## Wave 0 — compiling public contract

- [ ] **T01** — Scaffold both projects and compile the complete public contract —
  files: `TaxAI.sln`, `src/CaseProcessing/CaseProcessing.csproj`,
  `src/CaseProcessing/Models/ProcessedT4Extraction.cs`,
  `src/CaseProcessing/Models/T4ProcessingFields.cs`,
  `src/CaseProcessing/Services/SupportedProcessingTaxYears.cs`,
  `src/CaseProcessing/Services/PayloadStructureGuard.cs`,
  `src/CaseProcessing/Services/T4CaseProcessor.cs`,
  `src/CaseProcessing/CLAUDE.md`, `src/CaseProcessing/AGENTS.md`,
  `tests/CaseProcessing.Tests/CaseProcessing.Tests.csproj`,
  `tests/CaseProcessing.Tests/ContractTests.cs`,
  `tests/CaseProcessing.Tests/Models/ResultOmissionTests.cs`,
  `tests/CaseProcessing.Tests/Services/SupportedProcessingTaxYearsTests.cs`,
  `specs/008-t4-case-processor/status/T01.done` — depends on: none

  Create the `CaseProcessing` net8.0 class library (nullable enabled) and its
  xUnit test project, add both to `TaxAI.sln`, and add the two one-way production
  references `CaseProcessing → DocumentExtraction` and
  `CaseProcessing → TaxLogicEngine`. Add `InternalsVisibleTo("CaseProcessing.Tests")`
  so T02 can unit-test the internal guard.

  Declare every signature exactly as architecture.md specifies:
  `ProcessedT4Extraction` (sealed class, **internal** constructor),
  `T4ProcessingFields` (the seven constants), `SupportedProcessingTaxYears`
  (2025 only — **implement now**), `PayloadStructureGuard.Inspect` and
  `T4CaseProcessor.Process` compiling with `NotImplementedException` bodies.
  Implement the value plumbing now: `[JsonIgnore]` on every caller string **and**
  on `Slip` and `Mapping`, and a hand-written structural `ToString()`.
  Create `src/CaseProcessing/CLAUDE.md` (approved boundary, public-contract
  inventory, the omission/masking seam, 2025 policy, forbidden dependencies,
  fail-closed workflow) and a pointer-only `AGENTS.md` **before** any
  implementation, so every later task is bound by them.

  **Tests:** reflection proves the exact public surface, that
  `ProcessedT4Extraction` has no public constructor, that `Slip`/`Mapping`/every
  caller string carry `[JsonIgnore]` while `TaxYear` does not, and that no public
  type is a record. `SupportedProcessingTaxYears` supports exactly 2025 (2024 and
  2026 fail). Run the mandatory PII matrix at the constructed-value level against
  a reflection-built result. Instruction tests prove local `CLAUDE.md` content
  and pointer-only `AGENTS.md`.

  **Mechanical acceptance:** `dotnet build TaxAI.sln --no-incremental` succeeds
  warning-free for the new projects; the solution gains exactly the two new
  projects (**presence-and-uniqueness, not a total count**);
  `CaseProcessing.csproj` references only `DocumentExtraction` and
  `TaxLogicEngine`; no existing project file changes; task tests and
  `dotnet test TaxAI.sln` pass.

---

## Wave 1 — the structural seam guard

- [ ] **T02** — Implement `PayloadStructureGuard.Inspect` completely — files:
  `src/CaseProcessing/Services/PayloadStructureGuard.cs`,
  `tests/CaseProcessing.Tests/Services/PayloadStructureGuardTests.cs`,
  `specs/008-t4-case-processor/status/T02.done` — depends on: T01

  Implement stage 4: return one `ValidationError` per structural defect in fixed
  order — `Boxes` null (`PayloadBoxes`), any null element (`PayloadBoxes`), any
  null/blank `Code` (`PayloadBoxCode`). Never throws, whatever the input.

  It must read **no `T4RawBox.Value`**, make no judgement about which codes are
  known/required/meaningful, and must not trim, normalize, deduplicate, reorder,
  or drop a box.

  **Tests:** positive — a well-formed payload yields zero errors; a payload with
  duplicate codes, unknown codes, blank values, and garbage values yields zero
  errors (the guard is structural only). Negative, one per rule — null `Boxes`
  (AC-11), a null element (AC-12), a null `Code`, an empty `Code`, a
  whitespace-only `Code` (AC-13), and several defects at once producing one error
  each in fixed order. Prove no input throws, that every `Field` is a
  `T4ProcessingFields` constant, that no `Value` is read (a payload whose values
  are all planted SIN strings still yields errors naming no value), and that the
  input payload is unmutated and reference-identical afterwards.

  **Mechanical acceptance:** AC-11, AC-12, AC-13 pass at unit level; the guard
  provably reads no box value; task tests and `dotnet test TaxAI.sln` pass.

---

## Wave 2 — the processor

- [ ] **T03** — Implement `T4CaseProcessor.Process` completely — files:
  `src/CaseProcessing/Services/T4CaseProcessor.cs`,
  `tests/CaseProcessing.Tests/Services/T4CaseProcessorTests.cs`,
  `specs/008-t4-case-processor/status/T03.done` — depends on: T01, T02

  Implement the eight stages in architecture.md's fixed order, with stages 3–4
  accumulating and all others short-circuiting. Construct `T4SlipParser` and
  `T4LineMapper` internally; take `IT4LineMapProvider` by constructor. Engine
  errors are surfaced **verbatim and in order** — never translated, summarized,
  re-worded, filtered, or interleaved with this module's own. On parser failure
  the mapper must not be invoked and the provider must not be queried. On success
  construct `ProcessedT4Extraction` echoing attribution exactly as supplied.
  Messages are non-interpolated constants.

  **Tests:** positive — AC-1 (happy path), AC-3 and AC-4 (the slip and mapping
  are `Equal` to what `T4SlipParser`/`T4LineMapper` produce when invoked directly
  on the same input — the anti-drift assertion), AC-5 (attribution echoed
  exactly, untrimmed), AC-6 (`UnmappedBoxCodes` surfaced unchanged and never
  rejecting), AC-8 (result year coherence), AC-24 through AC-26 (empty slip, zero
  amounts, absent optional boxes), AC-27 (half-cent behaviour equals the
  engine's), AC-28 (no arithmetic performed).

  Negative, one criterion each — AC-9 (null extraction, no throw), AC-10
  (`GenericEvidence` kind), AC-14 through AC-21 (each parser rejection family:
  duplicate box, missing Box 12, missing Box 45, missing Box 54, malformed
  amount, negative amount, constrained code out of range, missing companion box —
  each asserting the parser's own errors verbatim), AC-22 (no mapping config for
  the year, via a provider returning failure, with **no retry for another year**),
  AC-23 (a parser rejection stops the pipeline — assert with a recording provider
  that `TryGetForYear` was **never called**), plus an unsupported extraction year
  and a slip/extraction year mismatch.

  Tax-year isolation — AC-34 (policy in one place), AC-35 (a recording
  `IT4LineMapProvider` proves exactly one year requested, and it is the slip's),
  AC-36 (a provider holding only year N's config never maps a slip from N±1),
  AC-37. Every failure yields no value and every self-originated `Field` is a
  `T4ProcessingFields` constant.

  **Mechanical acceptance:** AC-1, AC-3–AC-6, AC-8–AC-28, AC-34–AC-37 pass; no
  coercion, no box-value inspection, no arithmetic, no CRA literal exists in the
  module; task tests and `dotnet test TaxAI.sln` pass.

---

## Wave 3 — end-to-end assurance and boundary enforcement (parallel)

T04 and T05 may run in parallel after T03. They share no file and reference no
type created by a sibling.

- [ ] **T04** — Add end-to-end, PII, and determinism suites — files:
  `tests/CaseProcessing.Tests/EndToEnd/T4ProcessingAcceptanceTests.cs`,
  `tests/CaseProcessing.Tests/EndToEnd/T4ProcessingPiiTests.cs`,
  `tests/CaseProcessing.Tests/EndToEnd/T4ProcessingDeterminismTests.cs`,
  `specs/008-t4-case-processor/status/T04.done` — depends on: T01, T02, T03

  Exercise the finished contract only through its public surface, starting from a
  real `ExtractionEnvelope`.

  **Tests:** `T4ProcessingAcceptanceTests` — AC-2 (full chain: envelope →
  `ExtractionEnvelopeValidator.Validate` → `T4CaseProcessor.Process` → a result
  whose line entries equal the engine's), AC-38 (no CRA line number is hardcoded:
  the produced line numbers all come from the injected map, proved by supplying a
  provider with a known in-memory map and asserting the output follows it), AC-39
  (no box value is interpreted and no `T4Slip` is constructed here).

  `T4ProcessingPiiTests` — the full mandatory PII matrix end-to-end (AC-29
  through AC-33), including Box 12 = `123456789` on an **accepted** pipeline, and
  every rejection family with planted values in the payload and every caller
  reference. AC-31: exactly one SIN representation exists — the masked
  `RecipientIdentifier` on `T4Slip.Sin` — and the last 3 digits are the only
  digits visible. AC-32: the raw `T4RawInput` is unreachable from the result
  (assert by reflection over the public surface). Include the reachability guard.

  `T4ProcessingDeterminismTests` — AC-7 replay equality over accepted and every
  rejected family, five replays each, asserting **equal error ordering**, not
  merely set equality; that two distinct-but-equivalent extractions agree; and
  that processing mutates neither the extraction nor its payload.

  **Mechanical acceptance:** AC-2, AC-7, AC-29–AC-33, AC-38, AC-39 pass through
  the public API; task tests and `dotnet test TaxAI.sln` pass.

- [ ] **T05** — Enforce dependency, no-tax-truth, and source boundaries — files:
  `tests/CaseProcessing.Tests/Boundaries/CaseProcessingBoundaryTests.cs`,
  `specs/008-t4-case-processor/status/T05.done` — depends on: T01, T02, T03

  Prove the module cannot become a second mapping authority or acquire a
  forbidden dependency. Do not modify any instruction file, root `CLAUDE.md`, or
  `NEXT.md`, and do not claim spec 008 complete.

  **Tests:** positive — both projects declared exactly once at their approved
  solution paths (**presence-and-uniqueness, never a total project count**); the
  production reference set is exactly `DocumentExtraction` + `TaxLogicEngine`; no
  `PackageReference`/`FrameworkReference`/`EmbeddedResource`/`Content`; the 2025
  policy lives only in `SupportedProcessingTaxYears`; Wave-0 instruction files
  have their required content and pointers.

  Negative source/project/assembly scans — AC-40 (exactly two production project
  references), AC-41 (**no `TaxCases` reference**, and no reverse reference to
  `CaseProcessing` from `TaxLogicEngine`, `TaxCases`, `DocumentExtraction`, or
  their test trees), AC-42 (no `DateTime.Now`/`UtcNow`, filesystem, network,
  database, environment/current-user, OCR/LLM/cloud/email token, ASP.NET Core,
  EF Core, SQL, or generated identifier), no CRA line-number literal, no
  box→line table, no `TaxLogicEngine.Mappings` resource read, no `Math.Round`/
  `MidpointRounding`/`double`/`float`, no `SafeDiagnosticText` or other masking
  helper, no `RecipientIdentifier` construction, no new SIN property or type, and
  no T5 token.

  Scan **executable lines only** — doc comments legitimately name forbidden
  concepts while explaining why they are forbidden. Include a **scanner guard**
  asserting the file walk sees every production file and that a known-present
  pattern still matches, so a broken walk cannot make every assertion pass for
  free.

  **Mechanical acceptance:** AC-40, AC-41, AC-42 pass; no forbidden token or
  reference exists; the other five projects' trees are unchanged; task tests and
  `dotnet test TaxAI.sln` pass.

---

## Wave 4 — final integration and repository-wide verification

- [ ] **T06** — Verify every PRD criterion and record the final result — files:
  `tests/CaseProcessing.Tests/EndToEnd/RepositoryVerificationTests.cs`,
  `specs/008-t4-case-processor/verification.md`,
  `NEXT.md` *(modified only after AC-1–AC-43 all pass)*,
  `CLAUDE.md` *(modified only after AC-1–AC-43 all pass)*,
  `specs/008-t4-case-processor/status/T06.done` — depends on: T01, T02, T03, T04, T05

  Add a repository-level verification matrix and the human-readable record. Run
  every task suite and the entire solution; map AC-1 through AC-43 to exact test
  names; review the final diff for declared-file ownership, project direction,
  deterministic boundaries, the PII omission/masking seam, hygiene, and unchanged
  TaxLogicEngine / TaxCases / DocumentExtraction behaviour. **Only after every AC
  passes**, update `NEXT.md` and root `CLAUDE.md` to record spec 008 complete and
  name the next feature (Accountant Review Package); then create `T06.done`. If
  any AC fails, `verification.md` records the failure, the state files stay
  unchanged, and no marker is created.

  **Tests:** positive — all eight expected solution projects present at their
  approved paths (**presence-and-uniqueness; assert no total count**), every
  declared project and reference resolvable, exact reference sets and inward
  direction, public-contract reflection against architecture.md, and one
  representative accepted end-to-end flow. Negative — no unexpected project
  *reference*, no reverse dependency, no `TaxCases` coupling, no T5 consumption,
  no unsupported year accepted, no public `ProcessedT4Extraction` constructor, no
  forbidden boundary token, and AC-43 (every pre-existing `TaxLogicEngine.Tests`,
  `TaxCases.Tests`, and `DocumentExtraction.Tests` test still green).

  **Mechanical acceptance:** `verification.md` carries a pass/fail row for every
  AC-1 through AC-43 with exact command/test evidence; `dotnet build TaxAI.sln
  --no-incremental` and `dotnet test TaxAI.sln` pass with every pre-existing test
  green; `git diff` shows no change under `src/TaxLogicEngine/`,
  `tests/TaxLogicEngine.Tests/`, `src/TaxCases/`, `tests/TaxCases.Tests/`,
  `src/DocumentExtraction/`, or `tests/DocumentExtraction.Tests/`; no tracked
  `bin/`, `obj/`, `.vs/`, `*.user`, secret, or `.claude/settings.local.json`;
  every task changed only its declared files. On full pass the state files name
  spec 008 complete, then `T06.done` is created. On any failure, record it only
  in `verification.md`, leave the state files unchanged, create no marker, stop.

---

## Acceptance-criterion ownership

| PRD criteria | Owning task(s) |
|---|---|
| Contract shape, `[JsonIgnore]`/`ToString` plumbing, 2025 policy, no public result constructor | T01 |
| AC-11, AC-12, AC-13 (structural guard, unit level) | T02 |
| AC-1, AC-3–AC-6, AC-8–AC-28, AC-34–AC-37 | T03 |
| AC-2, AC-7, AC-29–AC-33, AC-38, AC-39 | T04 |
| AC-40, AC-41, AC-42 | T05 |
| AC-1–AC-43 final traceability, AC-43 regression | T06 |

## Fail-closed negative-test ownership

| Rejection rule | Required negative xUnit owner |
|---|---|
| Null `Boxes`; null element in `Boxes`; null/empty/whitespace box `Code` | T02 |
| Null extraction; non-T4 kind; each of the eight parser rejection families; no mapping config for the year; parser rejection stops the pipeline; unsupported extraction year; slip/extraction year mismatch | T03 |
| Planted SIN and marker strings absent from every output on success and on every rejection family; raw payload unreachable from the result | T04 |
| Forbidden dependency/reference/token; `TaxCases` coupling; reverse reference; CRA line literal; mapping-resource read; arithmetic; masking helper; `RecipientIdentifier` construction; T5 consumption | T05 |
| Any failed AC-1–AC-43 evidence; unexpected project reference; unsupported year accepted | T06 |

**6 tasks across 5 waves.** Wave 3 has two parallel tasks (T04–T05) with disjoint
file ownership. Every sibling pair shares no file path and references no
sibling-created type.
