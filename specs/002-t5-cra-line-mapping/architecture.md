# Architecture: T5 Box → CRA Line Mapping

## Approach

A pure transformation layered on spec 001: a collection of validated `T5Slip`s →
aggregated `TaxLineEntry` items. It touches **Models/** (the output entry, the
result, and the config DTOs), **Mappings/** (the per-tax-year JSON box→line config —
the actual data), and **Services/** (the mapper plus two small helpers). **Prompts/**
is untouched.

Reuses spec-001 building blocks directly: `T5Slip`, `T5BoxCodes`, `ParseResult<T>`,
`ValidationError`, and `MoneyRounding`.

Flow (one call):

```
IReadOnlyCollection<T5Slip>
        │
        ▼
T5LineMapper.Map(slips)
   ├─ validate: non-empty, all one tax year, every slip CurrencyCode == CAD
   ├─ load that year's map via IT5LineMapProvider  (embedded JSON; missing → error)
   ├─ for each slip → enumerate (boxCode, amount) pairs   [T5SlipBoxAmounts]
   ├─ for each pair → config lookup:
   │     mapped   → emit a contribution per destination line (line, amount, boxCode)
   │     unmapped → record boxCode in the unmapped set
   ├─ aggregate contributions by CRA line: sum (MoneyRounding.ToCents),
   │     collect distinct source box codes
   ▼
ParseResult<T5MappingResult>   // Success(lines + unmapped)  |  Failure(errors)
```

The mapper is **fail-closed** (FR9): on any problem it returns errors and no partial
map. It is deterministic and side-effect free (FR10) — the only "input" beyond the
slips is the config, which is supplied through an injected provider so the mapper
itself does no file I/O and is trivially unit-testable.

The CRA structural facts that drove the design (verified against canada.ca during
spec/plan): amounts for the same CRA line are **summed** across boxes and slips
(Federal Worksheet), a **single box can feed multiple lines** (box 11 → line 12000
*and* 12010), and **taxable** dividend boxes (11, 25) map while **actual** boxes
(10, 24) are informational/unmapped.

## Data model

All new except where noted. No changes to spec-001 types.

**Models/**

- **`TaxLineEntry`** — one aggregated destination:
  `{ string LineNumber; decimal Amount; IReadOnlyList<string> SourceBoxCodes; }`.
  `LineNumber` is a CRA line string (e.g. `"12100"`) sourced from config, never a C#
  constant. `SourceBoxCodes` is provenance (SIN-free box codes) for auditing and the
  later "explain what's filed" feature. Uniform for every destination — income lines
  and credit lines (40425/40500) alike.

- **`T5MappingResult`** — the success payload:
  `{ IReadOnlyList<TaxLineEntry> Lines; IReadOnlyList<string> UnmappedBoxCodes; }`.
  `UnmappedBoxCodes` carries every populated box the config did not map (incl. actual
  dividend boxes 10/24 and `OtherInformation` codes) so nothing is silently dropped
  (FR7).

- **`BoxLineMapping`** — one config rule:
  `{ string BoxCode; IReadOnlyList<string> LineNumbers; }`. A box maps to a **list** of
  lines (box 11 → `["12000","12010"]`).

- **`T5LineMappingConfig`** — the deserialized JSON file:
  `{ int TaxYear; IReadOnlyList<BoxLineMapping> Mappings; }`.

- **`T5LineMap`** — the resolved, in-memory form: tax year + a
  `IReadOnlyDictionary<string, IReadOnlyList<string>>` box→lines lookup built from the
  config (validated: no duplicate box codes, non-empty line lists).

**Mappings/**

- **`T5LineMap.2025.json`** — the per-tax-year config, embedded as a resource. Every
  entry is **CRA-verified** during its implementation task (see tasks) and cites its
  source. Illustrative (values confirmed so far):
  - `"11" → ["12000","12010"]`, `"25" → ["12000"]` (taxable dividends)
  - `"12" → ["40425"]`, `"26" → ["40425"]` (dividend tax credit)
  - `"13" → ["12100"]`, `"14" → ["12100"]`, `"15" → ["12100"]` (interest/other/foreign)
  - `"16" → ["40500"]` (foreign tax credit), `"17"`, `"18"` (capital gains dividends),
    `"19"` — destinations to be confirmed in the config task.
  - Actual boxes `"10"`, `"24"` intentionally **absent** (informational).

  New per-year files (`T5LineMap.2026.json`, …) are added without code changes (FR2/FR3).

## Services & public API

**Services/**

- **`T5LineMapper`** — the entry point.
  - ctor: `T5LineMapper(IT5LineMapProvider provider)`.
  - `ParseResult<T5MappingResult> Map(IReadOnlyCollection<T5Slip> slips)`.
  - **Responsibility:** validate, resolve amounts, look up config, aggregate, or return
    errors. Pure/deterministic.
  - **Validation (fail-closed):** slips non-null and non-empty; **all slips share one
    `TaxYear`** (mixed years → error, per CLAUDE.md per-year rule); **every slip's
    `CurrencyCode == "CAD"`** (non-CAD → error, FR8); a config exists for the year.
  - **Domain rules:** per-tax-year (single year → single config); decimal half-up via
    `MoneyRounding.ToCents` at aggregation (FR5); no hardcoded CRA line numbers (all
    from config, FR2); no SIN handling — `TaxLineEntry` carries only line/amount/box
    codes, and error messages reference lines/boxes/currency, never Box 22.

- **`IT5LineMapProvider`** / **`EmbeddedT5LineMapProvider`** — resolves a tax year to a
  `T5LineMap`. `ParseResult<T5LineMap> TryGetForYear(int taxYear)`. The embedded
  implementation loads `Mappings/T5LineMap.{year}.json` from assembly resources and
  deserializes with `System.Text.Json`; a missing or malformed file → `Failure`.
  Injecting the interface keeps `T5LineMapper` file-I/O-free and lets tests supply an
  in-memory map.

- **`T5SlipBoxAmounts`** (static helper) — `IEnumerable<(string BoxCode, decimal
  Amount)> Enumerate(T5Slip slip)`: yields the 13 typed amount boxes **whose value is
  non-zero** plus every `OtherInformation` entry. Central place that knows box-code →
  `T5Slip` property, so the mapper stays box-code-agnostic. (Absent/zero typed boxes
  are skipped to avoid reporting 13 zero boxes as "unmapped" each run.)

No spec-001 service changes. `Calculator`/`Validator` remain future work.

## Trade-offs

1. **JSON config + provider vs a hardcoded C# box→line dictionary.**
   Rejected hardcoding: it violates the CLAUDE.md "no hardcoded CRA line numbers"
   rule and FR2, and would make an annual CRA change a code change. Config-as-data with
   a provider keeps line numbers out of C# and per-year files additive.

2. **One box → a *list* of lines vs one box → one line.**
   Rejected the single-line model: CRA maps box 11 to **both** 12000 and 12010, so a
   one-line mapping literally cannot represent the real rules. The list costs almost
   nothing and is correct.

3. **Injected `IT5LineMapProvider` vs the mapper reading the JSON file itself.**
   Rejected direct file access in the mapper: it would make the mapper impure and hard
   to unit-test, and couple aggregation logic to resource loading. The provider isolates
   I/O and makes the mapper a pure function of (slips, map).

4. **Uniform `TaxLineEntry` for all destinations vs typed destination kinds.**
   Chose uniform (a line number is a line number, income or credit). Rejected typed
   kinds for v1: added complexity for value that only materializes once calculation/
   schedule features exist; the config can still carry a "kind" later without breaking
   the entry shape.

5. **Reporting actual boxes (10/24) as `UnmappedBoxCodes` vs a config "ignored" list.**
   Chose report-as-unmapped: simplest, and honest (the caller sees the value exists).
   Rejected an explicit ignored-list in config for v1 — extra config surface for little
   gain; can be added if the unmapped list proves noisy.

## Risks

- **Wrong mapping table (correctness / CRA compliance).** Highest risk. Mitigated by
  making the config the *only* source of line numbers and **CRA-verifying every entry**
  in a dedicated implementation task (canada.ca via browser, citing the line page for
  each), plus tests asserting the known anchors (box 11 → 12000+12010, box 13 → 12100,
  box 12/26 → 40425). Scope is route-and-total only — **no** tax calculation.
- **Capital gains dividends (box 18) inclusion-rate subtlety.** Only the taxable
  portion reaches line 12700, via Schedule 3, after the inclusion rate — that is
  *calculation*, out of scope here. Mitigation: the config routes box 18 to its
  Schedule 3 destination line as the CRA line page dictates (full amount), and this
  feature applies **no** inclusion rate; the exact destination is confirmed in the
  config task and documented.
- **Per-tax-year mixing.** Mitigated: the mapper requires all slips to share one tax
  year and loads exactly that year's config; mixed years fail closed.
- **Foreign currency mis-report.** Mitigated: non-CAD slips are rejected (FR8), so a
  USD amount can never be summed onto a CAD line without conversion (a separate
  feature).
- **Rounding drift.** Mitigated by summing through `MoneyRounding.ToCents` once per
  line at aggregation (inputs are already cents-rounded from spec 001).
- **PII / SIN.** Low: `TaxLineEntry` holds no PII (line, amount, box codes); no SIN is
  read or emitted. Error messages reference lines/boxes/currency only.

## Notes for planning

Resolved in review: `TaxLineEntry` = line + amount + source box codes; uniform entry
keyed by line number; **full T5 box set** in v1; first config year **2025**; foreign
slips rejected (FR8); taxable dividend boxes mapped, actual boxes unmapped (CRA-
confirmed). These drive `tasks.md`. The definitive per-line mapping values are authored
and CRA-verified in task T03, not invented here.
