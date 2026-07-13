# Tasks: Document Extraction Contract (spec 007)

**Feature class: `tax-domain`.** This plan implements the approved PRD (as
revised by human review 2026-07-13) through the exact public contract in
[architecture.md](architecture.md).

## Binding execution rules

- This file is **read-only once approved**. Never edit it or check a box.
- One explicitly named TaskID is implemented per branch/PR/review. A human
  merges each wave to `master` before the next wave begins.
- Every task may create or modify **only** the exact files in its declared file
  list. A task's own `.done` marker is part of that list. Files outside the
  list must not change. `src/TaxLogicEngine/`, `tests/TaxLogicEngine.Tests/`,
  `src/TaxCases/`, and `tests/TaxCases.Tests/` are untouchable in every task.
- Every task includes positive and negative xUnit coverage. No existing test
  may be removed, skipped, weakened, or rewritten to make a task pass.
- Before completion of **every** task, run `dotnet test TaxAI.sln`; the full
  solution suite must pass. Create the task marker only after its task-specific
  tests, the full suite, and all claimed acceptance criteria pass. If any check
  fails, do not create the marker.
- A marker is `specs/007-document-extraction-contract/status/<TaskID>.done`
  and contains the PR number and commit SHA. Every task in every earlier wave
  must have its marker before a later wave starts.
- Validation returns `ParseResult<ValidatedExtraction>` and fails closed:
  failure carries ≥1 `ValidationError` and no value; success carries a value
  and no errors; nothing is mutated, coerced, or partially accepted.
- The module depends on **`TaxLogicEngine` only**. No `TaxCases` reference, no
  `SafeDiagnosticText`, no new masking helper, no `RecipientIdentifier`
  construction, no CRA line/box logic, no mapping-resource read, no generated
  id, no system clock, no I/O, persistence, network, current-user, OCR, LLM,
  cloud, email, ASP.NET Core, EF Core, or SQL dependency may enter the module.
- **PII is handled by omission, not masking.** The raw `T4RawInput` payload
  (which may carry an unmasked SIN in Box 12) and every caller-controlled
  string are omitted from every `ToString()`, `ValidationError` message,
  diagnostic, and supported direct JSON serialization. Error `Field`s use
  `ExtractionFields` constants only; error messages are fixed templates
  containing no caller or payload value.

### Mandatory PII-omission matrix (T01, T02, T03)

Plant `123456789`, `123-456-789`, and `123 456 789` **separately** in each of:
the Box 12 value of `T4Payload.Boxes`, another box's value, `EmployerName`,
`EmployeeName`, `DocumentId`, `FirmId`, `CaseId`, `ParticipantId`,
`SourceReference`, and `ProviderLabel`. For both an accepted and a rejected
envelope, assert: the raw in-memory values remain readable exactly as supplied
where the contract exposes them programmatically; supported direct JSON
serialization of the envelope and of a `ValidatedExtraction` contains **none**
of the planted strings; every public `ToString()`, every
`ValidationError.Field`/`Message`, and every diagnostic contains none of the
planted strings and no 9-consecutive-digit substring. (Omission means not even
a masked form like `***-**-789` appears — the value simply is not there.)
T01 runs the matrix at the constructed-value/plumbing level; T02 runs it
through `Validate`; T03 repeats it end-to-end through the public API.

---

## Wave 0 — compiling public contract

- [ ] **T01 — Scaffold both projects and compile the complete public contract.**

  Create the `DocumentExtraction` net8.0 class library (nullable enabled) and
  `DocumentExtraction.Tests` xUnit project, add both to `TaxAI.sln`, and add
  the one-way production reference `DocumentExtraction → TaxLogicEngine`.
  Declare every public type exactly as specified by architecture.md:
  `ExtractedDocumentKind` (T4Slip = 1, GenericEvidence = 2; **no T5 value**),
  `ExtractionProvenance`, `ExtractionEnvelope`, `ValidatedExtraction`
  (internal constructor), `ExtractionFields`, `IDocumentExtractionIngress`
  (interface only — **no implementation ships in spec 007** — declaring
  exactly `Task<ParseResult<ValidatedExtraction>> SubmitAsync(
  ExtractionEnvelope envelope, CancellationToken cancellationToken = default)`;
  the async shape is signature-only — the interface performs no I/O, and
  `Task`/`CancellationToken` do not violate the deterministic boundary), and
  `ExtractionEnvelopeValidator` whose `Validate` compiles with a
  `NotImplementedException` body. Implement the value plumbing now: init-only
  properties, `[JsonIgnore]` on the payload and every caller-controlled
  string, safe structural-facts-only `ToString()` overrides, and
  `SupportedExtractionTaxYears` (2025 only). Create the module-local
  instructions before any implementation: `src/DocumentExtraction/CLAUDE.md`
  (approved boundary, exact public-contract inventory, PII-omission rules,
  presence-only attribution scope, 2025 policy, forbidden dependencies,
  fail-closed workflow) and a pointer-only `src/DocumentExtraction/AGENTS.md`.
  No public signature is deferred to a later wave.

  **Allowed files:**
  - modified: `TaxAI.sln`
  - created: `src/DocumentExtraction/DocumentExtraction.csproj`
  - created: `src/DocumentExtraction/Models/ExtractedDocumentKind.cs`
  - created: `src/DocumentExtraction/Models/ExtractionProvenance.cs`
  - created: `src/DocumentExtraction/Models/ExtractionEnvelope.cs`
  - created: `src/DocumentExtraction/Models/ValidatedExtraction.cs`
  - created: `src/DocumentExtraction/Models/ExtractionFields.cs`
  - created: `src/DocumentExtraction/Ports/IDocumentExtractionIngress.cs`
  - created: `src/DocumentExtraction/Services/ExtractionEnvelopeValidator.cs`
  - created: `src/DocumentExtraction/Services/SupportedExtractionTaxYears.cs`
  - created: `src/DocumentExtraction/CLAUDE.md`
  - created: `src/DocumentExtraction/AGENTS.md`
  - created: `tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj`
  - created: `tests/DocumentExtraction.Tests/ContractTests.cs`
  - created: `tests/DocumentExtraction.Tests/Models/PayloadOmissionTests.cs`
  - created: `tests/DocumentExtraction.Tests/Services/SupportedExtractionTaxYearsTests.cs`
  - created: `specs/007-document-extraction-contract/status/T01.done`

  **Depends on:** none.

  **Tests:** Positive xUnit tests instantiate every public type, resolve every
  exact property/method signature by reflection, prove
  `SupportedExtractionTaxYears` supports exactly 2025, prove
  `ExtractedDocumentKind` defines exactly `T4Slip` and `GenericEvidence` (no
  `T5Slip` — by proving no such value exists), prove `ValidatedExtraction` has
  no public constructor, prove `IDocumentExtractionIngress` declares exactly
  one method `SubmitAsync` whose return type is
  `Task<ParseResult<ValidatedExtraction>>`, whose first parameter is an
  `ExtractionEnvelope`, and whose second parameter is a `CancellationToken`
  with a default value, prove `IDocumentExtractionIngress` has no
  implementation in the production assembly, and prove the payload and every
  caller-controlled string property carry `[JsonIgnore]` while remaining
  readable in memory. Run the mandatory PII-omission matrix at the
  constructed-value level (envelope + provenance + a reflection-constructed
  `ValidatedExtraction`). Instruction tests prove local `CLAUDE.md` contains
  the required rules and local `AGENTS.md` is pointer-only. Negative tests
  prove 2024/2026 are unsupported, `default(ExtractedDocumentKind)` is not a
  defined value, and no public type is a record with a generated `ToString()`
  (every public `ToString()` output contains no planted caller/payload
  string).

  **Mechanical acceptance:** `dotnet build TaxAI.sln` succeeds; the solution
  gains exactly the two new projects; `DocumentExtraction.csproj` references
  only `TaxLogicEngine.csproj`; `TaxLogicEngine.csproj` and `TaxCases.csproj`
  are unchanged; contract reflection matches architecture.md exactly; the
  PII-omission matrix passes at value level; task tests and
  `dotnet test TaxAI.sln` pass. Only then create `T01.done`; otherwise create
  no marker.

---

## Wave 1 — the fail-closed validator

- [ ] **T02 — Implement `ExtractionEnvelopeValidator.Validate` completely.**

  Implement the single validation gate per architecture.md's fixed evaluation
  order: null envelope → single `Envelope` error without throwing;
  presence/non-blankness of `DocumentId`, `FirmId`, `CaseId`, `ParticipantId`,
  and `Provenance.SourceReference` (null `Provenance` reports the
  `SourceReference` error; required for **every** kind), one error per missing
  field in one pass; defined-`Kind` check (absent or undefined value —
  including any casted unknown/T5 stand-in — fails closed); envelope tax year
  present and exactly 2025, never coerced, no adjacent-year fallback;
  payload/kind coherence (T4Slip requires a `T4RawInput` payload;
  GenericEvidence must carry none); T4-only checks — `T4Payload.Year` present,
  `T4Payload.Year == TaxYear` with neither value coerced, and the mechanical
  empty-extraction rule (`Boxes` null/empty **and** all four identity text
  fields blank); confidence absent or in `[0m, 1m]` inclusive, out-of-range
  fails closed, **no low-confidence threshold**. On success construct the
  `ValidatedExtraction` carrying the references exactly as supplied and the
  payload **reference-identical and value-unchanged** (the same `T4RawInput`
  object graph — never cloned, collapsed, or reordered). Error `Field`s use
  `ExtractionFields` constants; messages are fixed templates.

  **Allowed files:**
  - modified: `src/DocumentExtraction/Services/ExtractionEnvelopeValidator.cs`
  - created: `tests/DocumentExtraction.Tests/Services/ExtractionEnvelopeValidatorTests.cs`
  - created: `specs/007-document-extraction-contract/status/T02.done`

  **Depends on:** T01.

  **Tests:** Positive xUnit tests cover AC-1 (valid T4 envelope, payload
  exposed unchanged and reference-identical), AC-2 (valid generic-evidence
  envelope, source reference exposed, no payload), AC-16 (confidence `0m`,
  `1m`, and absent all accepted), AC-18 (duplicate box codes survive in
  order), AC-21 (an engine-rejectable box value still passes the envelope
  gate), and determinism (AC-28: validating the same envelope twice yields
  equal success/failure, equal error sequences, and the same payload
  reference). Negative xUnit tests cover, one criterion each: AC-3
  (null/empty/whitespace `DocumentId`), AC-4 (undefined kind incl. a casted
  unknown value), AC-5 (blank `FirmId`), AC-6 (blank `CaseId`), AC-7 (blank
  `ParticipantId`), AC-8 (blank `SourceReference` for **both** kinds), AC-9
  (absent envelope year), AC-10 (2024 and 2026, never coerced), AC-11 (null
  `T4Payload.Year`), AC-12 (envelope 2025 / payload 2024 mismatch), AC-13
  (T4Slip without payload; GenericEvidence with a `T4RawInput`), AC-14 (empty
  `Boxes` + all four identity fields blank), AC-15 (confidence `-0.1m` and
  `1.1m`), AC-17 (whitespace-only in each of the five references), AC-19
  (empty envelope → one error per missing required field, no exception). Every
  failure yields no value; every error `Field` is an `ExtractionFields`
  constant. Run the mandatory PII-omission matrix through `Validate` for an
  accepted and a rejected envelope (covers AC-23's fixed-field/no-echo rule).

  **Mechanical acceptance:** AC-1 through AC-19, AC-21, AC-23 (validator arm),
  and AC-28 pass; no coercion path, box inspection, or normalization exists in
  the validator; task tests and `dotnet test TaxAI.sln` pass. Only then create
  `T02.done`; otherwise no marker.

---

## Wave 2 — end-to-end assurance and boundary enforcement (parallel)

T03 and T04 may run in parallel after T02. They share no file and reference no
type created by a sibling.

- [ ] **T03 — Add public-API acceptance, PII-omission, and untrusted-input
  suites.**

  Exercise the finished contract only through its public surface. Verify the
  full accepted/rejected envelope families, tax-year isolation, no-tax-truth
  guarantees, deterministic replay, and exhaustive payload/identifier omission.

  **Allowed files:**
  - created: `tests/DocumentExtraction.Tests/EndToEnd/ExtractionAcceptanceTests.cs`
  - created: `tests/DocumentExtraction.Tests/EndToEnd/ExtractionPiiTests.cs`
  - created: `tests/DocumentExtraction.Tests/EndToEnd/ExtractionDeterminismTests.cs`
  - created: `specs/007-document-extraction-contract/status/T03.done`

  **Depends on:** T01, T02.

  **Tests:** `ExtractionAcceptanceTests` — a valid T4 and a valid
  generic-evidence envelope accepted with exact exposed attribution
  (firm/case/participant/year/source reference); AC-20 (the accepted result
  exposes a `T4RawInput` and is not, and contains no, `T4Slip`,
  `T4MappingResult`, or `TaxLineEntry`; no box value inspected, typed,
  normalized, redacted, converted, mapped, or rounded — asserted by
  reference-identity of the payload); AC-25 (2024 envelope, 2026 envelope, and
  2025-envelope/2024-payload all rejected; a validated extraction only ever
  carries envelope year 2025 with matching payload year).
  `ExtractionPiiTests` — the full mandatory PII-omission matrix end-to-end
  (AC-22, AC-23), including: Box 12 = `123456789` on an **accepted** envelope,
  every rejection family with planted SIN-like strings in payload and every
  caller reference, and marker strings planted in every caller-controlled
  field asserted absent from every `Field`, `Message`, `ToString()`, and
  supported direct JSON serialization. `ExtractionDeterminismTests` — AC-28
  replay equality over accepted and rejected envelopes, including equal error
  ordering. If exercising the `IDocumentExtractionIngress` shape end-to-end is
  needed, a **minimal test-only implementation inside the test files** (e.g. a
  trivial `SubmitAsync` wrapping `ExtractionEnvelopeValidator.Validate` in
  `Task.FromResult`) may be used — **no production implementation is added**,
  and the production assembly must still contain no implementor of the port.

  **Mechanical acceptance:** AC-20, AC-22, AC-23, AC-25, and AC-28 pass
  through the public API; task tests and `dotnet test TaxAI.sln` pass. Only
  then create `T03.done`; otherwise no marker.

- [ ] **T04 — Enforce dependency, no-tax-truth, and source boundaries.**

  Add xUnit boundary checks proving the module can never become a tax-truth
  producer or acquire a forbidden dependency. Do not modify any instruction
  file, root `CLAUDE.md`, or `NEXT.md`, and do not claim spec 007 complete.

  **Allowed files:**
  - created: `tests/DocumentExtraction.Tests/Boundaries/DocumentExtractionBoundaryTests.cs`
  - created: `specs/007-document-extraction-contract/status/T04.done`

  **Depends on:** T01, T02.

  **Tests:** Positive tests prove the solution/project paths, the sole
  production reference `DocumentExtraction → TaxLogicEngine`, the 2025 policy
  location, and the Wave-0 instruction files' required content/pointers.
  Negative source/project/assembly scans prove: **no `TaxCases` project
  reference and no reverse reference from `TaxLogicEngine` or `TaxCases` to
  this module** (AC-29's reference arm); no `SafeDiagnosticText` usage and no
  masking helper in the module; **no construction or population of
  `RecipientIdentifier` and no new SIN property/type** (AC-24); no
  `T4SlipParser`/`T4LineMapper`/`MoneyRounding` usage, no CRA line-number
  literal, no box→line mapping, and no `TaxLogicEngine.Mappings` resource read
  (AC-26); no `DateTime.Now`/`UtcNow`, filesystem, network, database,
  environment/current-user, OCR/LLM/cloud/email token (AC-27); no consumed T5
  type (AC-30's no-T5 arm); no ASP.NET Core/EF Core/SQL package reference.

  **Mechanical acceptance:** AC-24, AC-26, AC-27, and the reference arms of
  AC-29/AC-30 pass; no forbidden token or reference exists; TaxLogicEngine and
  TaxCases production/test trees are unchanged; task tests and
  `dotnet test TaxAI.sln` pass. Only then create `T04.done`; otherwise no
  marker.

---

## Wave 3 — final integration and repository-wide verification

- [ ] **T05 — Verify every PRD criterion and record the final result.**

  Add a repository-level xUnit verification matrix and create the
  human-readable verification record. Run every task-specific suite and the
  entire solution; map AC-1 through AC-30 to exact test names/evidence; review
  the final diff for declared-file ownership, project direction, deterministic
  boundaries, PII omission, hygiene, and unchanged TaxLogicEngine/TaxCases
  behaviour. **Only after every AC passes,** update `NEXT.md` and root
  `CLAUDE.md` Current state to mark spec 007 complete and name the next
  feature; then create `T05.done`. If any AC fails, `verification.md` records
  the failure, `NEXT.md` and `CLAUDE.md` remain unchanged, and no marker is
  created.

  **Allowed files:**
  - created: `tests/DocumentExtraction.Tests/EndToEnd/RepositoryVerificationTests.cs`
  - created: `specs/007-document-extraction-contract/verification.md`
  - modified only after AC-1–AC-30 all pass: `NEXT.md`
  - modified only after AC-1–AC-30 all pass: `CLAUDE.md`
  - created: `specs/007-document-extraction-contract/status/T05.done`

  **Depends on:** T01, T02, T03, T04.

  **Tests:** Positive xUnit tests prove all six expected solution projects,
  resolvable references, public-contract reflection against architecture.md,
  and one representative accepted T4 and generic-evidence flow. Negative tests
  prove no unexpected project/reference, no reverse dependency, no T5 kind, no
  unsupported year acceptance, no public `ValidatedExtraction` constructor,
  and no forbidden boundary token (AC-29 full, AC-30 full).

  **Mechanical acceptance:** `verification.md` has a pass/fail row for every
  AC-1 through AC-30 with exact command/test evidence; `dotnet build TaxAI.sln`
  and `dotnet test TaxAI.sln` pass with every pre-existing
  `TaxLogicEngine.Tests` and `TaxCases.Tests` test green; `git diff` shows no
  change under `src/TaxLogicEngine/`, `tests/TaxLogicEngine.Tests/`,
  `src/TaxCases/`, or `tests/TaxCases.Tests/`; no tracked `bin/`, `obj/`,
  `.vs/`, `*.user`, secret, or `.claude/settings.local.json` exists; every
  task changed only its declared files. On full pass, state files name spec
  007 complete, then `T05.done` is created. On any failure, record it only in
  `verification.md`, leave state files unchanged, create no marker, and stop.

---

## Acceptance-criterion ownership

| PRD criteria | Owning task(s) |
|---|---|
| Contract shape, `[JsonIgnore]`/`ToString` plumbing, 2025 policy, no-T5 enum, no public `ValidatedExtraction` ctor | T01 |
| AC-1–AC-19, AC-21, AC-23 (validator arm), AC-28 | T02 |
| AC-20, AC-22, AC-23, AC-25, AC-28 (public API) | T03 |
| AC-24, AC-26, AC-27, AC-29/AC-30 (reference and token arms) | T04 |
| AC-1–AC-30 final traceability and repository verification | T05 |

## Fail-closed negative-test ownership

| Rejection rule | Required negative xUnit owner |
|---|---|
| Null envelope; blank document id / firm / case / participant / source reference; undefined kind (incl. casted unknown); absent/2024/2026 envelope year; missing payload year; envelope/payload year mismatch; payload/kind mismatch (both directions); empty T4 extraction; out-of-range confidence; whitespace-as-blank; one-error-per-field empty envelope | T02 |
| Wrong-year and year-incoherent envelopes through the public API; planted SIN/marker strings absent from every output | T03 |
| Forbidden dependency/reference/token; `RecipientIdentifier` construction; masking helper; mapping-resource read; T5 consumption | T04 |
| Any failed AC-1–AC-30 evidence; unexpected project/reference/kind/year | T05 |

**5 tasks across 4 sequential waves.** Wave 2 has two parallel tasks
(T03–T04) with disjoint file ownership. Every sibling pair shares no file path
and references no sibling-created type.
