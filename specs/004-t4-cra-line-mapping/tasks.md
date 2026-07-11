# Tasks: T4 Box → CRA Line Mapping (spec 004)

Read `architecture.md` first. Governing rules (CLAUDE.md): **one task = one branch
= one PR = one review**; within a wave no two tasks share a file path and none
references a type created by a sibling; each wave merges to `master` before the
next; completion is a `specs/004-t4-cra-line-mapping/status/<TaskID>.done` marker
(PR # + SHA), never a checkbox edit here. `tasks.md` is read-only.

Reused as-is (do **not** re-create): `TaxLineEntry`, `ParseResult<T>`,
`ValidationError`, `MoneyRounding`, and the spec-003 inputs `T4Slip`,
`T4OtherInformation`, `T4BoxCodes`. Both former Class A questions are resolved in
the PRD (A1 direct-sum set = FR13/FR14; A2 line values = canada.ca-verified).

Design note carried from `architecture.md`: the one deviation from the T5 mirror
is **FR8/AC9 unknown-box-code validation**, which needs the new
`T4OtherInformationCodes` recognized-code set.

---

## Wave 0 — Contract (must merge before Wave 1)

- [ ] **T01** — Declare the full public T4-mapping contract with
  `NotImplementedException` bodies (data types as declared shells): `T4MappingResult`
  (+ `Empty`); `T4BoxLineMapping` + `T4LineMappingConfig`; `T4LineMap` (`static Create`
  and `LinesFor` throwing `NotImplementedException`, `TaxYear`/`BoxCodes` members);
  `T4OtherInformationCodes` (with `All` as an empty placeholder set);
  `IT4LineMapProvider`; `EmbeddedT4LineMapProvider` (`TryGetForYear` throws);
  `T4SlipBoxAmounts` (`Enumerate` throws); `T4LineMapper` (ctor + `Map` throws).
  Include a contract test asserting the signatures exist, `Map`/`Create`/`Enumerate`/
  `TryGetForYear` throw `NotImplementedException`, and `T4MappingResult.Empty` is empty.
  — files: `TaxLogicEngine/Models/T4MappingResult.cs`,
  `TaxLogicEngine/Models/T4LineMappingConfig.cs`,
  `TaxLogicEngine/Models/T4LineMap.cs`,
  `TaxLogicEngine/Models/T4OtherInformationCodes.cs`,
  `TaxLogicEngine/Services/IT4LineMapProvider.cs`,
  `TaxLogicEngine/Services/EmbeddedT4LineMapProvider.cs`,
  `TaxLogicEngine/Services/T4SlipBoxAmounts.cs`,
  `TaxLogicEngine/Services/T4LineMapper.cs`,
  `TaxLogicEngine.Tests/Services/T4MappingContractTests.cs`
  — depends on: none

---

## Wave 1 — Flatten + resolve (parallel; distinct files; each passes on master + T01 + itself)

- [ ] **T02** — Implement `T4SlipBoxAmounts.Enumerate`: yield each of the 15 amount
  boxes (`14,16,16A,17,17A,18,20,22,24,26,44,46,52,55,56`) whose value is non-zero,
  then every `OtherInformation` entry verbatim; never enumerate non-amount boxes
  (SIN/54/50/10/29/45/28). Unit tests: non-zero amounts yielded with correct codes;
  zero/absent boxes skipped (AC17/18 support); deferred amount boxes (e.g. 16, 55)
  ARE yielded when populated (so the mapper can surface them unmapped); Other-info
  passthrough; SIN/coded boxes never appear.
  — files: `TaxLogicEngine/Services/T4SlipBoxAmounts.cs`,
  `TaxLogicEngine.Tests/Services/T4SlipBoxAmountsTests.cs`
  — depends on: T01

- [ ] **T03** — Implement `T4OtherInformationCodes.All` (the CRA-verified 2025
  "Other information" code set from the PRD) and `T4LineMap.Create`/`LinesFor`.
  `Create` is fail-closed, one error per problem: blank code, empty line list
  (FR6/AC8), blank line, duplicate box code (FR7/AC7), and **unknown box code**
  (FR8/AC9 — not in `T4BoxCodes` ∪ `T4OtherInformationCodes.All`). Tests: a valid
  config resolves and `LinesFor` returns the mapped lines / null on miss; one
  negative test per fail-closed rule; a recognized "Other information" code (e.g.
  40) is accepted, a bogus code (e.g. "999") rejected.
  — files: `TaxLogicEngine/Models/T4OtherInformationCodes.cs`,
  `TaxLogicEngine/Models/T4LineMap.cs`,
  `TaxLogicEngine.Tests/Models/T4LineMapTests.cs`
  — depends on: T01

---

## Wave 2 — Config data + mapper (parallel; distinct files; each passes on master + W0 + W1 + itself)

- [ ] **T04** — Author `Mappings/T4LineMap.2025.json` with **only** the canada.ca-
  verified direct mappings (14→10100, 18→31200, 20→20700, 22→43700, 44→21200,
  52→20600; codes 39→24900, 41→24900, 42→10120, 43→24400, 77→22900). Add the
  matching `<EmbeddedResource>` (LogicalName `TaxLogicEngine.Mappings.T4LineMap.2025.json`)
  to `TaxLogicEngine.csproj`. Add a **T4 section** to `Mappings/README.md`
  documenting each mapped box + source and the per-box reason every deferred/
  unmapped box is absent. Test: load the embedded 2025 resource, deserialize to
  `T4LineMappingConfig`, `T4LineMap.Create` succeeds, and `LinesFor` returns each
  verified line (guards AC2/13/14's config side); the deferred boxes (16/46/55…)
  are absent.
  — files: `TaxLogicEngine/Mappings/T4LineMap.2025.json`,
  `TaxLogicEngine/Mappings/README.md`, `TaxLogicEngine/TaxLogicEngine.csproj`,
  `TaxLogicEngine.Tests/Mappings/T4LineMap2025Tests.cs`
  — depends on: T03

- [ ] **T05** — Implement `T4LineMapper` (ctor-injected `IT4LineMapProvider`; `Map`):
  validate non-empty + single tax year (fail closed on empty/null AC3, mixed years
  AC4; **no** currency check); resolve the year's map via the provider (propagate
  its failure AC5/AC6); enumerate via `T4SlipBoxAmounts`; accumulate per CRA line
  (sum + distinct source boxes), unmapped-but-populated → `UnmappedBoxCodes` once;
  order by line, total via `MoneyRounding.ToCents`. Tests use a **fake**
  `IT4LineMapProvider` returning an in-memory `T4LineMap`: AC1, AC10, AC11, AC12,
  AC13, AC14, AC15, AC16, AC19, AC23, plus empty/mixed-year fail-closed.
  — files: `TaxLogicEngine/Services/T4LineMapper.cs`,
  `TaxLogicEngine.Tests/Services/T4LineMapperTests.cs`
  — depends on: T02, T03

---

## Wave 3 — Embedded provider (must merge before Wave 4)

- [ ] **T06** — Implement `EmbeddedT4LineMapProvider.TryGetForYear`: load
  `TaxLogicEngine.Mappings.T4LineMap.{year}.json` from assembly resources; fail
  closed on missing resource (AC5), malformed JSON / missing `taxYear` (AC6), and
  declared-year mismatch; delegate to `T4LineMap.Create`. Tests: 2025 loads and
  resolves; year 2099 fails closed with a missing-config error; a mismatched/
  malformed resource fails closed.
  — files: `TaxLogicEngine/Services/EmbeddedT4LineMapProvider.cs`,
  `TaxLogicEngine.Tests/Services/EmbeddedT4LineMapProviderTests.cs`
  — depends on: T04

---

## Wave 4 — End-to-end acceptance (must merge last)

- [ ] **T07** — End-to-end acceptance suite wiring the **real**
  `EmbeddedT4LineMapProvider` into `T4LineMapper` against the shipped 2025 config,
  covering the PRD's acceptance criteria not already unit-covered: AC2 (full slip →
  all verified lines), AC20 (no full SIN in any output/serialized form), AC21
  (2025 config not applied to another year → fails closed), AC22 (reflection: no
  CRA line number literal in the compiled T4 mapping types), and a determinism
  end-to-end check (AC23).
  — files: `TaxLogicEngine.Tests/Services/T4LineMappingAcceptanceTests.cs`
  — depends on: T05, T06

---

## Coverage map (every PRD acceptance criterion → task)

| ACs | Task |
|---|---|
| 7, 8, 9 | T03 (config resolution) |
| 17, 18 | T02 (enumeration skips zero/absent) |
| 1, 3, 4, 10, 11, 12, 13, 14, 15, 16, 19, 23 | T05 (mapper, fake provider) |
| 5, 6 | T06 (embedded provider) |
| 2, 20, 21, 22 | T07 (end-to-end) |

*(AC13/AC14/AC2 have a config-side guard in T04 as well as the behavioural check in
T05/T07; AC16 asserts the shared `MoneyRounding` helper's both-signs behaviour.)*

## Ordering rationale

Contract (T01) → flatten + resolve (T02, T03 parallel) → config data + mapper (T04,
T05 parallel) → real provider (T06) → end-to-end acceptance (T07). Tests ship
**with** each task. The two parallel pairs (T02/T03, T04/T05) touch disjoint files
and never reference each other's new types; T05 uses a fake provider so it does not
wait on the real one (T06). Line numbers exist only in T04's JSON — no C# literal,
verified by AC22 in T07.
