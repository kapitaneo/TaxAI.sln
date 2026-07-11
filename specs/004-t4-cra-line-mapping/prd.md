# PRD: T4 Box → CRA Line Mapping

## Problem

Spec 003 produces validated `T4Slip` models (employment income and source
deductions) but stops before those values mean anything on a tax return: a box
value is useless until it lands on the correct CRA line. This feature maps a
taxpayer's parsed `T4Slip` box values to CRA line numbers and aggregates them
into the `TaxLineEntry` items a draft return is built from — the T4 analogue of
what spec 002 did for T5. It serves the engine's return-builder and, ultimately,
the individual filer whose employment income and deductions must reach the right
lines.

**Tax year in scope: 2025 only.** Brackets, rates, limits, box definitions, and
line numbers are never mixed across years (CLAUDE.md per-tax-year rule). The
mapper loads exactly the config for the slips' tax year. **Year selection at
runtime:** the mapper reads each slip's `Year`; all slips in one call must share
one year, and a year with no config **fails closed** — never coerced, never an
adjacent-year fallback (CLAUDE.md). This feature ships the **2025** config only;
multi-year support is out of scope. Line numbers are year-scoped even when
unchanged year-over-year, so the 2025 config is authoritative for 2025 slips and
no other year.

**Parsing is done (spec 003, merged).** Input here is already a validated
`T4Slip`; this feature is mapping only.

## Reuse — what is reused vs genuinely new

Grep of `specs/*/prd.md` and the source tree confirms these **already exist and
are reused as-is; this PRD does not re-specify or redesign them:**

- **`TaxLineEntry`** (`Models/TaxLineEntry.cs`) — the aggregated destination
  `{ string LineNumber; decimal Amount; IReadOnlyList<string> SourceBoxCodes; }`.
  It is generically named (not T5-specific) and is **reused unchanged** as the T4
  mapper's output element. `LineNumber` is a CRA line string sourced from config,
  never a C# constant; `SourceBoxCodes` is SIN-free provenance.
- **`ParseResult<T>`** (`Models/ParseResult.cs`) — fail-closed result. The T4
  mapper returns `ParseResult<T4MappingResult>`.
- **`ValidationError`** (`Models/ValidationError.cs`) — `{ Field; Message; }`.
  Producers keep messages SIN-safe.
- **`MoneyRounding.ToCents`** (`Services/MoneyRounding.cs`) — the single currency
  chokepoint (`decimal`, half-up `AwayFromZero`, both signs). Line aggregation
  sums through it. Its rounding behaviour is already unit-tested (spec 001) and is
  **not** re-specified here.
- **`T4Slip`, `T4OtherInformation`, `T4BoxCodes`** (spec 003, merged) — the
  parsed input and the canonical box-code constants. **Reused unchanged as
  input.** The mapper consumes them; it does not re-parse or re-validate slip
  contents.

**Genuinely new (introduced by this feature):**

- `T4MappingResult` (success payload — mirrors `T5MappingResult`).
- `T4BoxLineMapping` (one config rule: box code → list of line numbers — mirrors
  `BoxLineMapping`).
- `T4LineMappingConfig` (deserialized JSON file — mirrors `T5LineMappingConfig`).
- `T4LineMap` (resolved in-memory lookup — mirrors `T5LineMap`).
- `IT4LineMapProvider` / `EmbeddedT4LineMapProvider` (year → `T4LineMap`; embedded
  JSON resource loader — mirrors the T5 provider pair).
- `T4SlipBoxAmounts` (flattens a `T4Slip` to `(boxCode, amount)` pairs — mirrors
  `T5SlipBoxAmounts`).
- `T4LineMapper` (the entry point — mirrors `T5LineMapper`).
- `Mappings/T4LineMap.2025.json` (the per-year data) + a `Mappings/README.md`
  update.

**These deliberately duplicate the shape of the existing `T5*` mapping types
rather than sharing a generalized abstraction with them.** Not generalizing T5
and T4 into a shared base is the standing decision recorded in spec 003
(`[ASSUMED]`); it is honoured here and tagged `[ASSUMED]`, not re-litigated. The
only genuinely shared types are the already-generic `TaxLineEntry`,
`ParseResult<T>`, `ValidationError`, and `MoneyRounding`.

## User stories

1. As the **return builder**, I want a set of parsed `T4Slip`s mapped to CRA line
   numbers, so that I can assemble a draft return without knowing box-to-line
   rules myself.
2. As the **return builder**, I want amounts that land on the same CRA line summed
   into one `TaxLineEntry`, so that each line carries the total across all slips
   (e.g. Box 14 across every T4 totals onto line 10100; codes 39 and 41 total onto
   line 24900).
3. As an **engine maintainer**, I want the box→line rules to live in the JSON
   mapping config, not C#, so that a CRA change is a data edit and no line number
   is ever hardcoded (CLAUDE.md).
4. As a **compliance reviewer**, I want the mapping scoped to a single tax year,
   so that a box never lands on a line that changed in another year.
5. As the **return builder**, I want boxes with no direct line mapping
   (identification, coded, calculation-/schedule-/residence-dependent boxes, "do
   not report" boxes, and unmapped "Other information" codes) reported explicitly
   rather than silently dropped, so that nothing a slip reported is lost.
6. As a **developer**, I want a fail-closed result (errors, no partial map) when a
   slip set can't be mapped or the config is malformed, so that a bad mapping
   never yields a wrong return.
7. As a **compliance reviewer**, I want the employee's SIN (Box 12) never read or
   emitted by the mapper, so that mapping cannot leak PII (CLAUDE.md).

## Functional requirements

All requirements are **[verified]** (encode a canada.ca fact from `## CRA
sources`), **[design]**, or **[reuse]**. Both former Class A questions are
**resolved** — the direct-sum-vs-deferred set (A1) and every line-number value
(A2) are settled and cited.

### Input and output

1. **[design]** The mapper SHALL accept one or more already-validated `T4Slip`s
   (from spec 003) for a single tax year and return a
   `ParseResult<T4MappingResult>` carrying the resulting `TaxLineEntry` items plus
   the unmapped box codes. It SHALL NOT parse slips, re-validate slip contents, or
   read files other than the mapping config.
2. **[design]** `T4MappingResult` SHALL carry
   `{ IReadOnlyList<TaxLineEntry> Lines; IReadOnlyList<string> UnmappedBoxCodes; }`,
   mirroring `T5MappingResult`.
3. **[reuse]** The mapper SHALL be **fail-closed**: on any mapping error (empty
   input, mixed tax years, missing/malformed year config) it SHALL return
   `Failure(errors)` with structured errors and **no** partial mapping.

### Config (data-driven, per year)

4. **[design]** Box→CRA-line rules SHALL come **only** from a JSON mapping config
   in `Mappings/` (`T4LineMap.{year}.json`), loaded via `IT4LineMapProvider`. **No
   CRA line number SHALL be hardcoded in C#** (CLAUDE.md). The config is the single
   source of truth for which box maps to which line.
5. **[design]** The mapping config SHALL be **per tax-year**. The mapper SHALL use
   the config for the slips' tax year and SHALL NOT mix mappings across years. A
   year with no embedded config SHALL fail closed (no adjacent-year fallback).
6. **[design]** A box in the config SHALL map to a **list** of one or more line
   numbers. An **empty line list** for a box is a config error and SHALL fail
   closed.
7. **[design]** A **duplicate box code** within a config SHALL fail closed with a
   structured error naming the code.
8. **[design]** A config entry whose box code is **not a recognized T4 box code**
   (a standard box in `T4BoxCodes`, or a valid 2025 "Other information" code) SHALL
   fail closed with a structured error naming the code, so a typo in config cannot
   silently misroute.
9. **[design]** A **missing or malformed** year config (absent resource, invalid
   JSON, missing `taxYear`) SHALL fail closed with a structured error.

### Aggregation and money

10. **[verified/design]** When multiple box amounts (across boxes and across slips)
    map to the **same CRA line**, the mapper SHALL **sum them** into a single
    `TaxLineEntry` for that line and collect the distinct source box codes into
    `SourceBoxCodes`. Two verified canonical cases: Box 14 across all T4 slips totals
    onto line 10100; "Other information" codes 39 and 41 both enter line 24900 and
    therefore sum onto one `TaxLineEntry`. *(Multiple-source-boxes-into-one-line is
    already supported by the mirrored `T5LineMapper`/`T5SlipBoxAmounts` pattern.)*
11. **[reuse]** All amount aggregation SHALL use `decimal`, rounded half-up to
    cents (`MidpointRounding.AwayFromZero`) via `MoneyRounding.ToCents`. Never
    `double`/`float`, never a bare `Math.Round`.
12. **[design]** Only the box values the config maps SHALL be routed.
    `T4SlipBoxAmounts` SHALL flatten a `T4Slip` to `(boxCode, amount)` pairs over the
    **amount boxes** (14, 16, 16A, 17, 17A, 18, 20, 22, 24, 26, 44, 46, 52, 55, 56)
    whose value is non-zero, plus every `OtherInformation` entry. Non-amount boxes
    (Box 12 SIN, Box 54, Box 50, Box 10, Box 29, Box 45, Box 28 exempt indicators)
    SHALL NOT be routed to any line.

### What is mapped in v1 (verified line numbers — individuals page)

13. **[verified]** The 2025 config SHALL map exactly the boxes canada.ca instructs
    to **enter directly on a line**:
    - Box 14 → **10100**; Box 18 → **31200**; Box 20 → **20700**; Box 22 →
      **43700**; Box 44 → **21200**; Box 52 → **20600**.
    - "Other information" code 39 → **24900**; code 41 → **24900**; code 42 →
      **10120**; code 43 → **24400**; code 77 → **22900**.
    Every line value is from the individuals T4 page (Source I; see `## CRA
    sources`).

### What is deferred to `UnmappedBoxCodes` in v1 (verified reasons)

14. **[verified/design]** A box whose canada.ca destination is a **schedule, form,
    calculation, or residence condition**, or that canada.ca says **"do not
    report,"** SHALL NOT be mapped in v1 and, when populated, SHALL surface in
    `UnmappedBoxCodes` (nothing silently dropped) — mirroring spec 002's treatment
    of T5 boxes 16/18. This resolved set (per canada.ca) is:
    - **Box 16 / 17 (CPP/QPP)** → lines 30800 **and** 22215 via **Schedule 8 / Form
      RC381** (base-vs-enhanced split is a calculation — human-confirmed defer).
    - **Box 16A / 17A (second CPP/QPP)** → line 22215 via **Schedule 8 / RC381**
      (calculation — human-confirmed defer).
    - **Box 46 (charitable donations)** → line 34900 but entered on **Schedule 9**
      (schedule-dependent — human-confirmed defer).
    - **Box 55 (PPIP premiums)** → line **31205 if Quebec resident, else 31200**;
      residence is not carried on the T4, so this cannot be routed without external
      data — defer.
    - **Boxes 24 / 26 / 56 (EI/CPP-QPP/PPIP insurable-pensionable earnings)** —
      calculation inputs with **no line**; when non-zero they surface as unmapped.
    - Softer **"see line"** cases, also deferred in v1: Box 66/67 retiring
      allowances → 13000; Box 85 private-health premiums → 33099/33199; Box 87
      emergency-services volunteer → 10100 + 31220/31240.
    `UnmappedBoxCodes` is the single honest bucket for any populated-but-unrouted
    box; the per-box **reason** (deferred-to-calc/schedule/residence vs no-line-ever
    vs do-not-report) is documented in `Mappings/README.md`, not encoded as
    behaviour.

### PII (SIN — Box 12)

15. **[reuse]** The mapper SHALL never read the Box 12 SIN and SHALL never emit it.
    `T4SlipBoxAmounts` enumerates amount boxes and "Other information" only — the SIN
    is not an amount box and never enters the pipeline. No `TaxLineEntry`,
    `T4MappingResult`, log line, exception message, or serialized form SHALL contain
    a full SIN; the SIN, if referenced at all, is masked to the last 3 digits.

### Determinism

16. **[design]** Mapping SHALL be deterministic and side-effect free: same slips +
    same config → same `T4MappingResult`. The mapper does no file I/O itself (config
    arrives via the injected provider).

## Out of scope

- **Parsing** T4 slips (done in spec 003) — input is already `T4Slip` models. No
  re-validation of slip contents.
- **Tax calculation** of any kind: the base/enhanced CPP/QPP split (Schedule 8 /
  RC381), EI/PPIP credit computation, RPP/PA interactions, the donations credit
  (Schedule 9), retiring-allowance and private-health-premium handling, or any
  taxable-income / tax-owing math. This feature only **routes and totals** amounts
  the slip already states.
- **Residence determination.** Box 55's Quebec-vs-rest-of-Canada split (31205 vs
  31200) depends on residence, which is not on the T4; it is deferred, not guessed.
- **Currency conversion.** T4 employment amounts are CAD by definition; there is no
  `CurrencyCode` on `T4Slip` and no foreign-currency handling here (unlike the T5
  mapper's non-CAD rejection).
- **Other slip types** (T5 — spec 002; T3, T5013). T4 only.
- Building the full **draft T1 return**, any PDF/JSON export, or GPT explanations.
- **Multi-year** support — 2025 config only.
- Generalizing/refactoring the `T5*` mapping types into a shared abstraction with
  T4 (duplication is intentional — see `[ASSUMED]`).

## Acceptance criteria

Each AC is a single mechanically-checkable test (Given/When/Then). Concrete line
numbers are the CRA-verified values from `## CRA sources`; a test asserts the
mapper produced them **from the config** (the "no hardcoded line in C#" rule,
AC22, governs production code, not the test's expected value).

### Happy path

1. **Single box to a line.**
   Given one 2025 `T4Slip` with Box 14 = `50000.00`,
   When mapped, Then one `TaxLineEntry` for line `10100` with amount `50000.00` is
   produced and its `SourceBoxCodes` contains `"14"`.

2. **Full valid slip maps each direct box to its verified line.**
   Given a 2025 slip carrying Box 14, 18, 20, 22, 44, 52 and "Other information"
   codes 39, 41, 42, 43, 77,
   When mapped, Then `TaxLineEntry`s appear for lines 10100, 31200, 20700, 43700,
   21200, 20600, 24900, 10120, 24400, 22900 with the corresponding amounts (code 39
   + code 41 summed on 24900).

### Fail-closed rejections (one rule per AC)

3. **Empty / null slip set.**
   Given a null or empty slip collection,
   When mapped, Then a structured error is returned and no `TaxLineEntry`s.

4. **Mixed tax years.**
   Given two slips, one `Year = 2025` and one `Year = 2024`,
   When mapped together, Then a structured tax-year error is returned and no mapping
   is produced.

5. **Missing year config.**
   Given slips for a year with no embedded config (e.g. 2099),
   When mapped, Then a structured error identifies the missing config and no
   adjacent-year config is used.

6. **Malformed config JSON.**
   Given a provider returning invalid JSON / missing `taxYear`,
   When mapped, Then a structured error is returned and no mapping.

7. **Duplicate box code in config.**
   Given a config where the same box code appears twice,
   When the config is resolved, Then a structured error names the duplicated code
   and no map is produced (FR7).

8. **Empty line list in config.**
   Given a config entry whose `lineNumbers` list is empty,
   When resolved, Then a structured error names that box and no map is produced
   (FR6).

9. **Unknown box code in config.**
   Given a config entry whose box code is neither a standard T4 box nor a valid 2025
   "Other information" code,
   When resolved, Then a structured error names that code and no map is produced
   (FR8).

### Aggregation

10. **Aggregation across slips onto one line.**
    Given two 2025 slips each with Box 14 = `30000.00`,
    When mapped together, Then one `TaxLineEntry` for line 10100 totals `60000.00`.

11. **Two boxes onto one line (codes 39 + 41 → 24900).**
    Given a slip with "Other information" code 39 = `100.00` and code 41 = `50.00`,
    When mapped, Then a single `TaxLineEntry` for line 24900 carries `150.00` and its
    `SourceBoxCodes` lists both `"39"` and `"41"`.

### Unmapped preservation

12. **Unmapped "Other information" code preserved, not dropped.**
    Given a slip carrying an "Other information" code with no mapping in the config,
    When mapped, Then that code appears in `UnmappedBoxCodes` and does not silently
    vanish.

13. **Deferred calculation box surfaces as unmapped, not misrouted.**
    Given a 2025 slip with Box 16 (CPP) = `3500.00`,
    When mapped, Then Box 16 appears in `UnmappedBoxCodes` and is not summed onto any
    line (deferred to Schedule 8 / RC381 calculation — FR14).

14. **Residence-dependent box (55) deferred.**
    Given a slip with Box 55 (PPIP) = `400.00`,
    When mapped, Then Box 55 appears in `UnmappedBoxCodes` and no 31205/31200 line is
    produced (residence not on the T4 — FR14).

### Boundaries

15. **Rounding — positive half-cent.**
    Given box amounts that sum to `100.005` on a line,
    When mapped, Then that line's `TaxLineEntry` amount is `100.01` (half-up, away
    from zero).

16. **Rounding — negative half-cent at the helper.**
    Given the reused `MoneyRounding.ToCents`, `-100.005` maps to `-100.01`
    (`AwayFromZero`), asserted at the helper as under spec 001. (T4 amount inputs are
    already `>= 0` cents-rounded from spec 003; this asserts the aggregation
    chokepoint's both-signs behaviour.)

17. **Zero amounts contribute nothing.**
    Given a slip whose mapped amount boxes are all `0m`,
    When mapped, Then no `TaxLineEntry` is produced for a zero-only line and no zero
    box is reported as unmapped (zero boxes are skipped by `T4SlipBoxAmounts`).

18. **Absent optional boxes.**
    Given a slip that omits optional amount boxes (they default to `0m`),
    When mapped, Then those boxes produce neither a line nor an unmapped entry.

19. **Empty-of-amounts slip.**
    Given a valid 2025 slip carrying identification but no non-zero amount boxes and
    no "Other information",
    When mapped, Then the result succeeds with zero `TaxLineEntry`s and zero unmapped
    codes (no exception).

### PII

20. **No full SIN anywhere in mapping output.**
    Given a slip whose Box 12 SIN is `123456789`,
    When mapped and the result, its `TaxLineEntry`s, any `UnmappedBoxCodes`, and any
    `ValidationError.Message` are inspected/serialized,
    Then no 9-consecutive-digit SIN substring appears; the SIN is never read into the
    amount pipeline.

### Tax-year isolation

21. **2025 config not applied to another year.**
    Given slips for tax year 2024,
    When mapped, Then the 2024 config is required (and, absent here, fails closed per
    AC5); the 2025 config is never applied to a non-2025 slip.

### Data-driven mapping

22. **No hardcoded CRA line number in C#.**
    Given the compiled T4 mapping types,
    When their source/emitted metadata is inspected, Then no CRA line number appears
    in C# — every line number comes from `T4LineMap.2025.json` only.

### Determinism

23. **Determinism.**
    Given identical slips + config mapped twice,
    When the two results are compared, Then they are equal (same lines, same totals,
    same unmapped set).

## Open questions

### Blocking Class A

**None.** Both prior Class A questions are resolved by the human and folded in:
the direct-sum-vs-deferred set (former A1) is FR13/FR14, and every line-number
value (former A2) is canada.ca-verified in `## CRA sources`.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **New T4-specific mapping types, not a shared abstraction with T5:**
  `T4MappingResult`, `T4BoxLineMapping`, `T4LineMappingConfig`, `T4LineMap`,
  `IT4LineMapProvider`/`EmbeddedT4LineMapProvider`, `T4SlipBoxAmounts`,
  `T4LineMapper`, paralleling the `T5*` mapping types. The existing `T5*` types are
  **not** generalized/renamed. Duplication is accepted (standing spec-003 decision).
- `[ASSUMED]` **`TaxLineEntry`, `ParseResult<T>`, `ValidationError`, `MoneyRounding`
  are reused unchanged** — already generically named and shared across slip families.
- `[ASSUMED]` **Config shape mirrors T5:** `{ "taxYear": 2025, "mappings": [ { "boxCode",
  "lineNumbers": [ ... ] } ] }`, embedded as an assembly resource, loaded by
  `EmbeddedT4LineMapProvider`; a new year is a new file, no code change.
- `[ASSUMED]` **Mapper entry point** is `T4LineMapper.Map(IReadOnlyCollection<T4Slip>
  slips) : ParseResult<T4MappingResult>`, ctor-injected `IT4LineMapProvider`
  (matching `T5LineMapper`).
- `[ASSUMED]` **`T4SlipBoxAmounts` enumerates non-zero amount boxes + "Other
  information"** (skipping zero/absent boxes so they are not reported as unmapped),
  mirroring `T5SlipBoxAmounts`.
- `[ASSUMED]` **Uniform `TaxLineEntry` for every destination** (income, deduction,
  and credit lines alike), keyed by line number — no typed destination "kind" in v1
  (mirrors spec 002 trade-off #4).
- `[ASSUMED]` **Box 55, the "see line" codes (66/67, 85, 87), and boxes 24/26/56 are
  deferred to `UnmappedBoxCodes` in v1** with their canada.ca rationale recorded in
  `Mappings/README.md`. Human-confirmed as Class B residual scope (the values are all
  canada.ca-verified; only the v1 include/defer line is a scope call, mirroring
  spec 002 deferring T5 boxes 16/18).
- `[ASSUMED]` **`UnmappedBoxCodes` is the single bucket** for every
  populated-but-unrouted box (deferred-to-calc, no-line, and do-not-report alike);
  the distinction is documentary (README), not a third result field.
- `[ASSUMED]` **Spec folder** is `specs/004-t4-cra-line-mapping/` (001, 002, 003
  taken).
- `[ASSUMED]` **Config validation surfaces via `ParseResult` failure** (duplicate box
  code, empty line list, unknown box, missing/malformed file), not exceptions —
  mirroring T5.
- `[ASSUMED]` **`SourceBoxCodes` records SIN-free provenance** on each entry for later
  "explain what's filed" auditing.
- `[ASSUMED]` **v1 ships tax year 2025 only;** a slip whose Year has no config fails
  closed (no adjacent-year fallback).

## CRA sources

Two canada.ca pages in **distinct, non-overlapping roles**. The mapping VALUES
(box → T1 line number) come **only** from the individuals page (Source I). The
box **identities/definitions** (what each box/code means, the identification area)
come from the employers page (Source E), which contains **no** T1 line numbers —
no box→line value is drawn from it.

**Source I (box → CRA line numbers)** — "T4 slip: Statement of Remuneration Paid",
canada.ca, date modified **2026-02-02**, retrieved **2026-07-11** (verified by the
coordinator via a browser session; canada.ca returns HTTP 403 to this agent's
`WebFetch` bot filter — the transport differed, the source did not).
`https://www.canada.ca/en/revenue-agency/services/tax/individuals/topics/about-your-tax-return/tax-return/completing-a-tax-return/tax-slips/understand-your-tax-slips/t4-slips/t4-statement-remuneration-paid.html`

**Source E (box identities / definitions only — NOT line numbers)** — "T4 slip –
Information for employers", canada.ca, date modified **2026-02-25**, retrieved
**2026-07-11**. Same "Source 1" cited in `specs/003-t4-slip-parsing/prd.md`.
`https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/completing-filing-information-returns/t4-information-employers/t4-slip.html`

### Mapped in v1 — Source I, "Enter this amount on line X"

| Claim (box → CRA line) | Line source | Definition source | Tax year | Retrieved |
|---|---|---|---|---|
| Box 14 Employment income → line **10100** | Source I | Source E | 2025 | 2026-07-11 |
| Box 18 EI premiums → line **31200** | Source I | Source E | 2025 | 2026-07-11 |
| Box 20 RPP contributions → line **20700** | Source I | Source E | 2025 | 2026-07-11 |
| Box 22 Income tax deducted → line **43700** | Source I | Source E | 2025 | 2026-07-11 |
| Box 44 Union dues → line **21200** | Source I | Source E | 2025 | 2026-07-11 |
| Box 52 Pension adjustment → line **20600** | Source I | Source E | 2025 | 2026-07-11 |
| Code 39 Security options deduction 110(1)(d) → line **24900** | Source I | Source E | 2025 | 2026-07-11 |
| Code 41 Security options deduction 110(1)(d.1) → line **24900** | Source I | Source E | 2025 | 2026-07-11 |
| Code 42 Employment commissions → line **10120** | Source I | Source E | 2025 | 2026-07-11 |
| Code 43 CAF personnel and police deduction → line **24400** | Source I | Source E | 2025 | 2026-07-11 |
| Code 77 Workers' compensation benefits repaid to employer → line **22900** | Source I | Source E | 2025 | 2026-07-11 |

### Deferred to `UnmappedBoxCodes` in v1 — Source I routes via schedule/form/calc/residence, or "do not report"

| Box/code | Source I destination & reason to defer | Tax year | Retrieved |
|---|---|---|---|
| Box 16 / 17 CPP/QPP | lines 30800 **and** 22215 via **Schedule 8 / Form RC381** (base-vs-enhanced calculation) | 2025 | 2026-07-11 |
| Box 16A / 17A second CPP/QPP | line 22215 via **Schedule 8 / RC381** (calculation) | 2025 | 2026-07-11 |
| Box 46 Charitable donations | line 34900 entered via **Schedule 9** (schedule-dependent) | 2025 | 2026-07-11 |
| Box 55 PPIP premiums | line **31205 if Quebec resident, else 31200** (residence not on T4) | 2025 | 2026-07-11 |
| Boxes 24 / 26 / 56 insurable/pensionable earnings | calculation inputs — **no line** | 2025 | 2026-07-11 |
| Code 66/67 Retiring allowances | line 13000 ("see line") — deferred | 2025 | 2026-07-11 |
| Code 85 Private health-services-plan premiums | lines 33099/33199 ("see line") — deferred | 2025 | 2026-07-11 |
| Code 87 Emergency-services volunteer | line 10100 + 31220/31240 ("see line") — deferred | 2025 | 2026-07-11 |

### Not reported at all — Source I "do not report" / already-in-Box-14 / form-only

| Box/code | Reason | Tax year | Retrieved |
|---|---|---|---|
| Box 45 Employer-offered dental benefits | "Do not report on your income tax return" | 2025 | 2026-07-11 |
| Codes 30,31,32,33,34,36,38,40,86 | Already included in Box 14; do not report separately | 2025 | 2026-07-11 |
| Codes 69,71,88 (Indian Act exempt) | Exempt employment; not a taxable line here | 2025 | 2026-07-11 |
| Codes 78–83 (self-employment / fisher) | Reported via other forms | 2025 | 2026-07-11 |
| Codes 74,75 (past-service) | "see line 20700" — informational | 2025 | 2026-07-11 |

*(Box 45 and the "not reported" codes are not amount boxes routed by `T4SlipBoxAmounts`
— Box 45 is a coded box; the do-not-report codes are documented so the config author
does not add them. Any of these that arrive as populated "Other information" amounts
surface in `UnmappedBoxCodes`, per FR13/FR14 — never dropped.)*
