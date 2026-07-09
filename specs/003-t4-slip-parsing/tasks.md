# Tasks: T4 Slip Parsing (spec 003)

Read `architecture.md` first. Rules that govern this breakdown (CLAUDE.md):
**one task = one branch = one PR = one review**; within a wave no two tasks
share a file path and none references a type created by a sibling; each wave
merges to `master` before the next begins; completion is a
`specs/003-t4-slip-parsing/status/<TaskID>.done` marker (PR # + commit SHA),
never a checkbox edit here. `tasks.md` is a read-only plan.

Reused as-is (do **not** re-create): `ParseResult<T>`, `ValidationError`,
`MoneyRounding.ToCents`, `RecipientIdentifier`.

Class A questions A1 (required-present set) and A2 (all money `>= 0`) are
**resolved** — see `architecture.md`. The two `[CONFIRM]` flags (Box 54 required
⇒ employer/filing-side input; Box 10 optional-when-present) should be ratified by
the human at plan review; the tasks below encode them as decided.

---

## Wave 0 — Contract (must merge before Wave 1)

- [ ] **T01** — Declare the full public T4 contract with `NotImplementedException`
  bodies and no logic: `T4RawBox`/`T4RawInput` (incl. the three Box-28 exempt
  bools), `T4Slip` (all identification/coded/monetary/passthrough members + an
  overridden `ToString()` stub), `T4BoxCodes` (all box-code constants, no line
  numbers), `T4OtherInformation`, and `T4SlipParser.Parse(T4RawInput) :
  ParseResult<T4Slip>`. Include a minimal contract test asserting the types
  exist, `Parse` has the right signature, and it throws `NotImplementedException`.
  — files: `TaxLogicEngine/Models/T4RawInput.cs`,
  `TaxLogicEngine/Models/T4Slip.cs`, `TaxLogicEngine/Models/T4BoxCodes.cs`,
  `TaxLogicEngine/Models/T4OtherInformation.cs`,
  `TaxLogicEngine/Services/T4SlipParser.cs`,
  `TaxLogicEngine.Tests/Services/T4ContractTests.cs`
  — depends on: none

---

## Wave 1 — Parser implementation (must merge before Wave 2)

- [ ] **T02** — Implement `T4SlipParser.Parse` end-to-end and its unit tests, in
  one pass. Validation passes exactly per `architecture.md` §"Services & public
  API": (1) duplicate box codes; (2) required-present set — `Year`,
  `EmployerName`, `EmployerAddress`, `EmployeeName`, `EmployeeAddress`, Box 12,
  Box 54, Box 45 (A1); (3) `Year == 2025`, no fallback; (4) monetary boxes as
  `decimal` via `MoneyRounding.ToCents`, non-numeric → error, `< 0` → error
  (A2); (5) constrained values when present — Box 10 province set, Box 29
  `{11..17}`/blank, Box 45 `{1..5}`; (6) conditionals 16⇒26, 18⇒24, 55⇒56,
  52⇒50 (companion may be `0`); (7) Box 12 → `RecipientIdentifier`, `000000000`
  accepted. Errors are collected (no short-circuit) and returned via
  `ParseResult.Failure`; `BuildSlip` runs only on zero errors and routes unknown
  codes to `T4OtherInformation`. Unit tests cover the happy path and every
  fail-closed branch — PRD ACs **1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15,
  19, 20**.
  — files: `TaxLogicEngine/Services/T4SlipParser.cs`,
  `TaxLogicEngine.Tests/Services/T4SlipParserTests.cs`
  — depends on: T01

---

## Wave 2 — Acceptance suites (parallel; distinct files; each passes on master + T01 + T02)

- [ ] **T03** — PII / SIN-masking acceptance suite: Box 12 masked to last 3 in
  `ToString()`, in any built-in string/serialization surface, and in every
  `ValidationError.Message`; no 9-consecutive-digit substring anywhere; the
  `000000000` sentinel accepted and reveals no real SIN. PRD ACs **21, 22, 23**.
  — files: `TaxLogicEngine.Tests/Services/T4SlipParserPiiTests.cs`
  — depends on: T02

- [ ] **T04** — Tax-year isolation + data-driven-mapping acceptance suite: a
  non-2025 slip (`Year = 2024` and `Year = 2026`) fails closed with a tax-year
  error and is not coerced; no 2025-only box rule (16A/17A, codes 90/91/92) is
  applied to another year; and no CRA line number appears in the compiled T4
  types (box codes are the only identifiers). PRD ACs **6, 24, 25**.
  — files: `TaxLogicEngine.Tests/Services/T4SlipParserYearIsolationTests.cs`
  — depends on: T02

- [ ] **T05** — Rounding / boundaries / passthrough / determinism acceptance
  suite: half-cent rounds up on a real box (`100.005 → 100.01`); zero companion
  accepted (Box 24 = `0` with Box 18 present); absent optional box takes its
  documented default (`0m` / `null` / `false`); known and unknown/future
  "Other information" codes pass through verbatim with no value dropped and no
  error; identical input parsed twice is equal. PRD ACs **16, 17, 18, 26, 27,
  28**.
  — files: `TaxLogicEngine.Tests/Services/T4SlipParserBoundaryTests.cs`
  — depends on: T02

---

## Coverage map (every PRD acceptance criterion → task)

| ACs | Task |
|---|---|
| 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 19, 20 | T02 |
| 21, 22, 23 | T03 |
| 6, 24, 25 | T04 |
| 16, 17, 18, 26, 27, 28 | T05 |

AC17 (negative-midpoint rounding) is asserted at the reused `MoneyRounding`
helper level per spec 001; T4 additionally rejects negative box inputs (A2), so
T05 asserts the helper contract rather than a negative T4 box value.

## Ordering rationale

Models/contract (T01) → parser + its unit tests (T02) → dedicated acceptance
suites (T03–T05). Tests ship **with** each task, never as a final lump. T03–T05
are independent (separate files, all depend only on the merged parser) and may
run in parallel worktrees.
