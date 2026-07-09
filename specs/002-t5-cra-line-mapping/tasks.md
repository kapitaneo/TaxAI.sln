# Tasks: T5 Box → CRA Line Mapping

Ordered, dependency-aware. Each task includes its xUnit tests in the same pass —
not done without them. Implement ONE unchecked task at a time, then stop for review.
Follow CLAUDE.md: no hardcoded CRA line numbers (config only), decimal half-up
(`MoneyRounding`), per-tax-year, SIN masking. Reuses spec-001 types (`T5Slip`,
`T5BoxCodes`, `ParseResult`, `ValidationError`, `MoneyRounding`).

## Models

- [x] **T01** — Output types: `TaxLineEntry` (LineNumber, Amount, SourceBoxCodes) and
  `T5MappingResult` (Lines, UnmappedBoxCodes) —
  files: `TaxLogicEngine/Models/TaxLineEntry.cs`, `TaxLogicEngine/Models/T5MappingResult.cs`;
  tests: `TaxLogicEngine.Tests/Models/TaxLineEntryTests.cs` (construction, value
  equality/holding, empty defaults) — depends on: none

- [x] **T02** — Config types + resolved map: `BoxLineMapping` (BoxCode, LineNumbers),
  `T5LineMappingConfig` (TaxYear, Mappings), and `T5LineMap` (year + box→lines lookup
  built from config; rejects duplicate box codes / empty line lists) —
  files: `TaxLogicEngine/Models/T5LineMappingConfig.cs`, `TaxLogicEngine/Models/T5LineMap.cs`;
  tests: `TaxLogicEngine.Tests/Models/T5LineMapTests.cs` (build lookup, one box → many
  lines, duplicate box code rejected) — depends on: none

## Mapping config (data)

- [x] **T03** — Author the **CRA-verified** 2025 config `T5LineMap.2025.json`, embedded
  as a resource. Verify **every** box→line entry against canada.ca (browser) and cite
  the source per line in a comment/README; map taxable dividend boxes (11→[12000,12010],
  25→[12000]), credits (12,26→[40425]), interest/other/foreign (13,14,15→[12100]),
  foreign tax (16→[40500]), royalties (17), capital gains dividends (18, Schedule 3
  destination — no inclusion rate applied), accrued income (19); actual boxes 10/24
  intentionally absent —
  files: `TaxLogicEngine/Mappings/T5LineMap.2025.json`, `TaxLogicEngine/Mappings/README.md`,
  `TaxLogicEngine/TaxLogicEngine.csproj` (EmbeddedResource);
  tests: `TaxLogicEngine.Tests/Mappings/T5LineMap2025Tests.cs` (JSON deserializes to
  `T5LineMappingConfig`; asserts the CRA-anchored mappings above; no actual-dividend
  boxes present) — depends on: T02

## Services

- [ ] **T04** — `IT5LineMapProvider` + `EmbeddedT5LineMapProvider`: resolve tax year →
  `ParseResult<T5LineMap>` from the embedded JSON (missing/malformed year → Failure) —
  files: `TaxLogicEngine/Services/IT5LineMapProvider.cs`, `TaxLogicEngine/Services/EmbeddedT5LineMapProvider.cs`;
  tests: `TaxLogicEngine.Tests/Services/EmbeddedT5LineMapProviderTests.cs` (loads 2025,
  unknown year fails, resolved lookup matches config) — depends on: T02, T03

- [ ] **T05** — `T5SlipBoxAmounts.Enumerate(T5Slip)` → `(BoxCode, Amount)` pairs: the 13
  typed amount boxes with non-zero value + all `OtherInformation` entries —
  files: `TaxLogicEngine/Services/T5SlipBoxAmounts.cs`;
  tests: `TaxLogicEngine.Tests/Services/T5SlipBoxAmountsTests.cs` (yields populated typed
  boxes, includes other-info incl. box 30, skips zero typed boxes) — depends on: none

- [ ] **T06** — `T5LineMapper.Map` core aggregation (happy path): enumerate amounts,
  config lookup, aggregate by line (sum via `MoneyRounding.ToCents`), collect distinct
  source box codes, build `UnmappedBoxCodes`, return `ParseResult.Success` —
  files: `TaxLogicEngine/Services/T5LineMapper.cs`;
  tests: `TaxLogicEngine.Tests/Services/T5LineMapperTests.cs` (AC1 single box→line,
  AC2 multi-box→one line, AC3 multi-slip aggregation, AC4 credit line, AC5 unmapped
  preserved, AC8 rounding) — depends on: T01, T04, T05

- [ ] **T07** — `T5LineMapper` validation (fail-closed): non-null/non-empty slips, all
  one tax year, every slip `CurrencyCode == CAD`, config exists for the year; errors
  reference line/box/currency only (SIN-safe) —
  files: `TaxLogicEngine/Services/T5LineMapper.cs` (extend);
  tests: extend `T5LineMapperTests.cs` (AC6 foreign rejected, AC7 per-year config,
  mixed-year rejected, empty input rejected, missing-year config rejected, no partial
  result on failure) — depends on: T06

## Wiring / acceptance

- [ ] **T08** — Acceptance suite AC1–AC8 end-to-end through `T5LineMapper` +
  `EmbeddedT5LineMapProvider`, plus provenance (SourceBoxCodes) and SIN-safety checks
  (no PII in entries/errors) —
  files: `TaxLogicEngine.Tests/Services/T5LineMappingAcceptanceTests.cs`;
  tests: AC1–AC8 — depends on: T06, T07
