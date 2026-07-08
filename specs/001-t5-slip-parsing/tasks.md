# Tasks: T5 Slip Parsing

Ordered, dependency-aware. Each task includes its tests (xUnit) in the same pass —
a task is not done without them. Implement ONE unchecked task at a time, then stop
for review. Follow CLAUDE.md domain rules throughout (SIN masking, decimal half-up
`AwayFromZero`, per-tax-year, no hardcoded CRA line numbers).

## Primitives (no dependencies)

- [x] **T01** — Result + error primitives: `ParseResult<T>` (IsSuccess, Value, Errors,
  `Success`/`Failure` factories) and `ValidationError` (Field, Message) —
  files: `TaxLogicEngine/Models/ParseResult.cs`, `TaxLogicEngine/Models/ValidationError.cs`;
  tests: `TaxLogicEngine.Tests/Models/ParseResultTests.cs` — depends on: none

- [x] **T02** — `MoneyRounding.ToCents(decimal)` helper (`Math.Round(v, 2,
  MidpointRounding.AwayFromZero)`) — files: `TaxLogicEngine/Services/MoneyRounding.cs`;
  tests: `TaxLogicEngine.Tests/Services/MoneyRoundingTests.cs` (incl. `100.005 → 100.01`,
  AC2) — depends on: none

- [x] **T03** — `RecipientIdentifier` value object: stores raw digits privately,
  exposes `Masked` (`***-**-789`, last 3) and masking `ToString()`, value equality;
  always masks — files: `TaxLogicEngine/Models/RecipientIdentifier.cs`;
  tests: `TaxLogicEngine.Tests/Models/RecipientIdentifierTests.cs` (masking, equality,
  full value never returned) — depends on: none

- [x] **T04** — `T5BoxCodes` constants (standard boxes 10–29 + named other-info
  code 30 "Equity linked notes interest") + `T5OtherInformation` (Code, Amount) —
  files: `TaxLogicEngine/Models/T5BoxCodes.cs`, `TaxLogicEngine/Models/T5OtherInformation.cs`;
  tests: `TaxLogicEngine.Tests/Models/T5BoxCodesTests.cs` — depends on: none

## Models

- [x] **T05** — `T5Slip` aggregate: `TaxYear`, identification fields,
  `RecipientIdentifier RecipientId`, `RecipientType`, `CurrencyCode` (default `"CAD"`),
  typed `decimal` properties for boxes 10–19/24–26, `OtherInformation` collection,
  and `ToString()` that masks the recipient id —
  files: `TaxLogicEngine/Models/T5Slip.cs`;
  tests: `TaxLogicEngine.Tests/Models/T5SlipTests.cs` (construction, `ToString` never
  leaks Box 22) — depends on: T03, T04

- [ ] **T06** — Raw input DTOs: `T5RawInput` (`TaxYear?`, payer/recipient
  name+address, `IReadOnlyList<T5RawBox> Boxes`) and `T5RawBox` (Code, Value) —
  files: `TaxLogicEngine/Models/T5RawInput.cs`;
  tests: `TaxLogicEngine.Tests/Models/T5RawInputTests.cs` (holds duplicate codes) —
  depends on: none

## Parser service

- [ ] **T07** — `T5SlipParser.Parse` happy path: map known boxes to typed
  properties, parse amounts as `decimal` via `MoneyRounding.ToCents`, passthrough
  unknown boxes to `OtherInformation`, preserve `CurrencyCode` without conversion,
  return `ParseResult.Success` — files: `TaxLogicEngine/Services/T5SlipParser.cs`;
  tests: `TaxLogicEngine.Tests/Services/T5SlipParserTests.cs` (AC1 full slip, AC2
  rounding, AC4 other-info passthrough, AC7 foreign currency preserved) —
  depends on: T01, T02, T04, T05, T06

- [ ] **T08** — `T5SlipParser` validation rules (fail-closed, errors only): required
  fields (tax year, payer name, recipient name, recipient type), `RecipientType ∈
  {1,2,3,4,5}` (CRA codes: individual/joint/corporation/association-trust/government),
  non-numeric amount, negative amount rejected, duplicate box code rejected;
  all messages SIN-safe — files: `TaxLogicEngine/Services/T5SlipParser.cs` (extend);
  tests: extend `T5SlipParserTests.cs` (AC5 missing field, AC6 bad recipient type,
  negative, duplicate, garbled amount, no model on failure) — depends on: T07

## Wiring / cross-cutting verification

- [ ] **T09** — SIN-safety sweep + acceptance regression: assert no full Box 22
  value appears in `T5Slip.ToString()` or any `ValidationError.Message` (AC3), and a
  single test class exercises all seven acceptance criteria end-to-end —
  files: `TaxLogicEngine.Tests/Services/T5SlipAcceptanceTests.cs`;
  tests: AC1–AC7 — depends on: T05, T08
