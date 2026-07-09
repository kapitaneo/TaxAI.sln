# PRD: T4 Slip Parsing

## Problem

Employment income and source deductions reported on a CRA **T4 (Statement of
Remuneration Paid)** slip arrive as loosely-typed, externally-extracted box
values that the engine cannot yet trust or work with. We need to turn those raw
box values into a single, validated, strongly-typed `T4Slip` model so that
downstream features (CRA-line mapping, calculation, validation, GPT
explanations) have a reliable, domain-safe foundation. This serves the engine's
internal consumers (the Mapper/Calculator services) and, indirectly, the
individual filer whose T4 data must be handled correctly and without leaking
their SIN.

**Tax year in scope: 2025 only.** This feature parses one T4 slip for the 2025
tax year. Brackets, rates, limits, and per-year box additions are never mixed
across years (CLAUDE.md per-tax-year rule). A T4 slip carries a required Year
box and represents exactly one tax year. The 2025 T4 box set includes boxes that
are year-sensitive (second CPP/QPP contributions in boxes 16A/17A apply to slips
filed for 2024 and after; certain "Other information" codes such as 90/91/92 are
valid only for 2024–2025) — these are validated against the **2025** definition
only. **Year selection at runtime:** this parser is the 2025 parser; a slip whose
Year box is not 2025 has no config here and **fails closed** — it is never
silently parsed under 2025 rules and never falls back to an adjacent year
(CLAUDE.md). Multi-year support is out of scope.

## Reuse — what is reused vs genuinely new

Grep of the source tree confirms these **already exist and are reused as-is;
this PRD does not re-specify or redesign them:**

- **`ParseResult<T>`** (`TaxLogicEngine/Models/ParseResult.cs`) — fail-closed
  result: `Success(value)` carries a value and no errors; `Failure(errors)`
  carries ≥1 `ValidationError` and no value. The T4 parser returns
  `ParseResult<T4Slip>`.
- **`ValidationError`** (`TaxLogicEngine/Models/ValidationError.cs`) — record
  `{ string Field; string Message; }`. Producers keep messages SIN-safe.
- **`MoneyRounding.ToCents`** (`TaxLogicEngine/Services/MoneyRounding.cs`) — the
  single currency chokepoint: `decimal`, half-up `AwayFromZero`, 2 dp, both
  signs. Every T4 monetary box flows through it. Its half-up/both-signs
  behaviour is already unit-tested under spec 001 and is **not** re-specified
  here.
- **`RecipientIdentifier`** (`TaxLogicEngine/Models/RecipientIdentifier.cs`) —
  the SIN/identifier value object. It masks unconditionally to the last 3 digits
  and never exposes the raw value. Reused for the T4 employee SIN (Box 12).

**Genuinely new (introduced by this feature):** `T4Slip` (validated domain
model), `T4RawInput` + `T4RawBox` (loose input DTOs), a `T4SlipParser` service,
a `T4BoxCodes` constant set, and a `T4OtherInformation` passthrough pair. **These
deliberately duplicate the shape of the existing `T5*` parsing types rather than
sharing a generalized base with them.** Not generalizing T5 into a shared
abstraction is a deliberate decision recorded by the requester; it is noted here
and tagged `[ASSUMED]` for one-pass override, and is not re-litigated.

## User stories

1. As a **calling service**, I want to pass already-extracted T4 box values (as
   a structured key/value input) and receive a typed `T4Slip`, so that I can
   work with named, correctly-typed fields instead of raw strings.
2. As a **calling service**, I want every monetary box parsed as `decimal`
   rounded half-up to cents, so that no float drift or inconsistent rounding
   enters the tax pipeline.
3. As a **calling service**, I want parsing scoped to one tax year (2025) per
   slip, so that I never accidentally mix values across tax years.
4. As a **compliance reviewer**, I want the employee's SIN (Box 12) to never
   appear in full in any log, error, or string output, so that we meet the
   CLAUDE.md SIN rule and CRA privacy expectations.
5. As a **calling service**, I want malformed or incomplete input to produce
   structured validation errors (not raw exceptions that echo PII), so that I
   can surface problems safely and decide how to handle them.
6. As an **engine maintainer**, I want unrecognized "Other information"
   code/amount pairs preserved rather than silently dropped, so that future
   boxes don't cause data loss before mapping support is added.

## Functional requirements

Requirements marked **[verified]** encode a fact confirmed against canada.ca
(see `## CRA sources`). Requirements marked **[reuse]** or **[design]** depend
on no CRA fact. Two rules depend on **open Class A questions A1/A2** and are
marked **[pending A1]/[pending A2]** — they are written to the recommended
default so the PRD is coherent, but they are not final until a human confirms
them (see `## Open questions`).

### Input and output

1. **[design]** The parser SHALL accept a structured input (`T4RawInput`)
   representing the extracted values of **one** T4 slip and return a single
   `ParseResult<T4Slip>`. It SHALL NOT read files, perform OCR, or do any I/O or
   network calls (input is already extracted).
2. **[design]** `T4RawInput` SHALL carry the non-boxed identification fields as
   top-level properties (`Year`, `EmployerName`, `EmployeeName`,
   `EmployeeAddress`) and all numbered boxes (10, 12, 14, 16, 16A, 17, 17A, 18,
   20, 22, 24, 26, 28, 29, 44, 45, 46, 50, 52, 54, 55, 56) plus "Other
   information" code/amount pairs as an **ordered list** `T4RawBox { Code, Value }`.
   A list (not a dictionary) is required so the same box code can appear twice
   and be detected.
3. **[reuse]** The parser SHALL be **fail-closed**: on any validation failure it
   returns `ParseResult<T4Slip>.Failure(errors)` with structured errors and
   **no** model. There is no partial/best-effort slip.

### Tax year

4. **[verified/design]** `T4Slip` SHALL carry a required `Year` and represent
   exactly one tax year. The parser SHALL require `Year == 2025`; a slip whose
   Year is absent or is any value other than 2025 SHALL fail closed (no config
   for that year; never coerced to 2025; never an adjacent-year fallback —
   CLAUDE.md). The parser SHALL NOT combine boxes from differing tax years.

### Money and rounding

5. **[reuse]** Every monetary box (14, 16, 16A, 17, 17A, 18, 20, 22, 24, 26, 44,
   46, 52, 55, 56, and each "Other information" amount) SHALL be parsed as
   `decimal` (invariant culture) and rounded via `MoneyRounding.ToCents`
   (half-up, `MidpointRounding.AwayFromZero`). Never `double`/`float`, never a
   bare `Math.Round`.
6. **[design]** A monetary box whose value cannot be parsed as a `decimal` SHALL
   fail closed with a structured error naming the box.
7. **[pending A2]** Monetary boxes SHALL be validated as `>= 0`; a negative value
   SHALL fail closed with a structured error naming the box. *(Recommended
   default; pending A2 confirmation. See Open questions.)*

### PII (SIN — Box 12)

8. **[verified]** Box 12 is the employee's SIN. The parser SHALL accept the CRA
   "no SIN" sentinel value **`000000000`** (nine zeros) as valid input and SHALL
   NOT reject it. *(Source 1: enter nine zeros when the employer does not have
   the SIN.)*
9. **[reuse]** The Box 12 value SHALL be wrapped in `RecipientIdentifier` and
   SHALL never be emitted in full by any logging, exception message, or
   `ToString()`; it SHALL be masked to the last 3 digits. `T4Slip.ToString()`
   SHALL be overridden so the member-wise string never leaks the SIN.
10. **[reuse]** No `ValidationError.Message` SHALL contain a full SIN.

### Constrained-value boxes (validated when present)

11. **[verified]** Box 10 (province of employment), when present, SHALL be one of
    exactly: `AB, BC, MB, NB, NL, NT, NS, NU, ON, PE, QC, SK, YT, US, ZZ`. Any
    other value SHALL fail closed. *(US = employed in the United States; ZZ =
    employed in a country other than Canada/US, or in Canada beyond a province/
    territory.)*
12. **[verified]** Box 29 (employment code), when present, SHALL be one of
    exactly: `11, 12, 13, 14, 15, 16, 17` (blank is allowed). Any other value
    SHALL fail closed.
13. **[verified]** Box 45 (employer-offered dental benefits), when present, SHALL
    be one of exactly: `1, 2, 3, 4, 5`. Any other value SHALL fail closed.
14. **[verified/design]** Box 28 (Exempt) SHALL be modelled as **three
    independent boolean indicators** — CPP/QPP, EI, and PPIP — each true when
    marked and false when blank. Box 28 is **not** a monetary amount and SHALL
    NOT be parsed as one.

### Conditional companion boxes (all verified)

15. **[verified]** If Box 16 (CPP contributions) is present, Box 26 (CPP/QPP
    pensionable earnings) SHALL be present (its value may be `0`); otherwise fail
    closed.
16. **[verified]** If Box 18 (EI premiums) is present, Box 24 (EI insurable
    earnings) SHALL be present (its value may be `0`); otherwise fail closed.
17. **[verified]** If Box 55 (PPIP premiums) is present, Box 56 (PPIP insurable
    earnings) SHALL be present (its value may be `0`); otherwise fail closed.
18. **[verified]** If Box 52 (pension adjustment) is present, Box 50 (RPP/DPSP
    registration number) SHALL be present; otherwise fail closed.

### Required fields

19. **[pending A1]** The parser SHALL require, at minimum, `Year` (see FR4),
    `EmployerName`, and `EmployeeName`; a missing required field SHALL fail
    closed with one structured error per missing field. Which additional boxes
    (Box 45 dental — CRA-mandatory for 2023+; Box 10; Box 12; Box 14) are
    required is **pending A1**. *(Recommended default: require Year + Employer
    name + Employee name + Box 45; treat Box 10/12/14 as optional. See Open
    questions.)*

### Structure and passthrough

20. **[design]** The parser SHALL **reject duplicate box codes** in the input
    with one structured error per duplicated code.
21. **[verified/design]** Any code/amount pair whose code is not one of the
    standard boxes above (the CRA "Other information" area) SHALL be preserved
    verbatim in a `T4OtherInformation` collection; an unrecognized code is **not**
    an error and SHALL NOT be dropped. *(Unlike the mapping config in spec 002,
    an unknown T4 box is passthrough, not a rejection.)*
22. **[design]** The parser SHALL retain box numbers/codes as the canonical
    identifiers and SHALL NOT assign or hardcode any CRA line number in C# (box →
    CRA line mapping is spec 004).
23. **[design]** Parsing SHALL be deterministic and side-effect free: the same
    input always yields the same `T4Slip` and validation result.

## Out of scope

- **Box → CRA line number mapping** and `TaxLineEntry` generation for T4 — that
  is the separate future **spec 004** and is explicitly excluded here.
- Extracting box values from a PDF, scanned image, or CRA XML — input is already
  structured key/value.
- Tax **calculation** of any kind (CPP/EI recomputation, pension-adjustment
  logic, credits, taxable-income math).
- **Persistence**, serialization to external stores, or PDF/JSON export.
- **GPT explanations** of the parsed slip.
- Handling **multiple slips** in one call, or reconciling duplicates across
  slips (including CRA "Other information" **overflow slips**).
- Generalizing/refactoring the existing `T5*` parsing types into a shared
  abstraction with T4 (duplication is intentional — see `[ASSUMED]`).
- **Provincial companion slips** (e.g. Relevé 1) — T4 only.
- **Multi-year** T4 support — 2025 only.
- Enforcing **format** of Box 54 (employer account number, 15-char `9+RP+4`) and
  Box 50 (7-digit registration number) beyond presence — see `[ASSUMED]`.
- Enforcing the CRA **"dollars only" (no cents)** presentation of Box 52 as a
  rejection — see `[ASSUMED]`.
- Enforcing **province/box consistency** (e.g. Box 17 QPP only when Box 10 = QC)
  and **CPP2/QPP2 cross-box** consistency beyond the four verified conditionals —
  see `[ASSUMED]`.
- Validating "Other information" codes against the closed 2025 code set, and
  enforcing the CRA **6-codes-per-slip** maximum — see `[ASSUMED]`.

## Acceptance criteria

Each AC is a single mechanically-checkable test (Given/When/Then). ACs marked
**[pending A1/A2]** are written to the recommended default and finalize once the
matching Class A question is answered.

### Happy path

1. **Happy path — full valid 2025 slip.**
   Given a `T4RawInput` with `Year = 2025`, employer and employee names, a valid
   Box 12 SIN, and valid values for the standard boxes (including a Box 16/Box 26
   pair, a Box 18/Box 24 pair, Box 45 in `{1..5}`, Box 10 in the province set,
   Box 29 blank or in `{11..17}`),
   When parsed,
   Then a `T4Slip` is returned with each value on its typed property and zero
   errors.

### Fail-closed rejections (one rule per AC)

2. **Duplicate box code.**
   Given an input where the same box code appears twice,
   When parsed, Then a structured error naming that code is returned and no slip
   is produced.

3. **Malformed amount.**
   Given a monetary box whose value is non-numeric (e.g. `"abc"`),
   When parsed, Then a structured error naming that box is returned and no slip.

4. **Negative amount** *(recommended; [pending A2]).*
   Given a monetary box with a negative value,
   When parsed, Then a structured error naming that box is returned and no slip.

5. **Missing Year.**
   Given an input with no Year,
   When parsed, Then a structured error identifies the missing Year and no slip.

6. **Wrong tax year (year isolation).**
   Given an input with `Year = 2024` (and again `Year = 2026`),
   When parsed by this 2025 parser, Then it fails closed with a tax-year error;
   no 2025 box rule is applied to it and it is not coerced to 2025.

7. **Missing employer name** *(recommended; [pending A1]).*
   Given an input with no employer name,
   When parsed, Then a structured error identifies the missing employer name.

8. **Missing employee name** *(recommended; [pending A1]).*
   Given an input with no employee name,
   When parsed, Then a structured error identifies the missing employee name.

9. **Invalid province code (Box 10).**
   Given Box 10 = `"XX"` (not in the CRA set),
   When parsed, Then a structured error flags Box 10 as out of the allowed set
   `{AB,BC,MB,NB,NL,NT,NS,NU,ON,PE,QC,SK,YT,US,ZZ}`.

10. **Invalid employment code (Box 29).**
    Given Box 29 = `"18"`,
    When parsed, Then a structured error flags Box 29 as not one of
    `{11,12,13,14,15,16,17}`.

11. **Invalid dental-benefit code (Box 45).**
    Given Box 45 = `"6"`,
    When parsed, Then a structured error flags Box 45 as not one of `{1,2,3,4,5}`.

12. **Conditional — Box 16 without Box 26.**
    Given Box 16 present and Box 26 absent,
    When parsed, Then a structured error requires Box 26 and no slip.

13. **Conditional — Box 18 without Box 24.**
    Given Box 18 present and Box 24 absent,
    When parsed, Then a structured error requires Box 24 and no slip.

14. **Conditional — Box 55 without Box 56.**
    Given Box 55 present and Box 56 absent,
    When parsed, Then a structured error requires Box 56 and no slip.

15. **Conditional — Box 52 without Box 50.**
    Given Box 52 present and Box 50 absent,
    When parsed, Then a structured error requires Box 50 and no slip.

### Boundaries

16. **Rounding — positive half-cent.**
    Given a monetary box value of `100.005`,
    When parsed, Then the stored value is `100.01` (half-up, away from zero) as a
    `decimal`.

17. **Rounding — both signs at the helper.**
    Given the reused `MoneyRounding.ToCents`, `-100.005` maps to `-100.01`
    (`AwayFromZero`, asserted at the helper per spec 001). T4 additionally
    rejects negative box inputs (AC4, [pending A2]); if A2 permits negatives for
    a box, the negative-midpoint rounding is asserted on that box directly.

18. **Zero amount.**
    Given Box 24 = `"0"` (with Box 18 present),
    When parsed, Then Box 24 stores `0m` and no error is raised.

19. **Absent optional box.**
    Given an input that omits a recognized optional box,
    When parsed, Then its property is the documented default (`0m` for amounts,
    `null`/`false` for optional scalars/indicators) and no error is raised.

20. **Empty slip.**
    Given an input with no boxes and no identification,
    When parsed, Then the result fails closed with one structured error per
    missing required field and no exception is thrown.

### PII

21. **SIN masking in output.**
    Given Box 12 = `123456789`,
    When the parsed slip is logged, `ToString()`-ed, or named in a validation
    error, Then only `***-**-789` appears and the full SIN never does.

22. **No full SIN in any member-wise/serialized form.**
    Given a successfully parsed `T4Slip` with a SIN,
    When any built-in string/serialization surface is exercised,
    Then no 9-consecutive-digit SIN substring is present.

23. **No-SIN sentinel accepted.**
    Given Box 12 = `000000000`,
    When parsed, Then the slip is accepted with no error and its masked form
    reveals no real SIN.

### Tax-year isolation

24. **2025-only box rules not applied to another year.**
    Given a slip whose Year is not 2025,
    When parsed, Then no 2025-specific box rule (e.g. boxes 16A/17A, or
    "Other information" codes 90/91/92) is applied; the slip fails closed per
    AC6 rather than being validated under 2025 definitions.

### Data-driven mapping

25. **No hardcoded CRA line number.**
    Given the compiled T4 parsing types,
    When their source/emitted metadata is inspected,
    Then no CRA line number appears in C# — box codes are the only identifiers
    (mapping is spec 004).

### Passthrough and determinism

26. **Other-information passthrough (known code).**
    Given an "Other information" pair (e.g. code `40` "Other taxable allowances
    and benefits") with an amount,
    When parsed, Then the pair is present in `T4OtherInformation`, no value is
    dropped, and no error is raised.

27. **Other-information passthrough (unknown/future code).**
    Given a code that is not in the recognized set (e.g. `99`),
    When parsed, Then it is preserved verbatim in `T4OtherInformation` and is not
    rejected.

28. **Determinism.**
    Given identical input parsed twice,
    When the two results are compared, Then they are equal (same slip, same
    errors).

## Open questions

### Blocking Class A — human decision required (not assumed)

Both items below determine **what constitutes a valid T4 and what must be
rejected** — the category CLAUDE.md marks Class A. canada.ca does not settle
either as an explicit parser rule, so I am **not** assuming them. Strong,
verified-source-grounded recommendations are given so each can be confirmed in
one pass.

**A1 — Required-field / box-presence set.** Which boxes must be *present* for a
slip to be "valid" (as opposed to merely value-validated when present)? canada.ca
defines *when an employer must issue a T4* (remuneration over $500, or any
CPP/QPP/EI/PPIP/income tax withheld) and defines conditional companion pairs
(FR15–18), but it does **not** define a flat per-field set the parser must
require. Recommended default: require `Year` + `EmployerName` + `EmployeeName` +
**Box 45** (dental — canada.ca states this is *mandatory for 2023 and after*);
treat Box 10, Box 12, and Box 14 as optional at parse time.
- Note (load-bearing): **Box 14 (employment income) cannot be made
  unconditionally required** — exempt-employment slips legitimately omit it and
  report elsewhere (e.g. "Other information" code 71, Indian Act exempt
  employment). Requiring Box 14 would wrongly reject valid slips.
- Note: Box 12 is always populated per CRA (real SIN or `000000000`), so
  "require Box 12 present" is defensible, but "require a *real* SIN" is not.

**A2 — Sign rule for monetary boxes.** May any T4 monetary box legitimately be
negative, or are all monetary boxes `>= 0` (negatives fail closed)? Recommended
default: **all monetary boxes `>= 0`, reject negatives.** Grounding: (a) the
merged T5 parser already rejects negative amounts; (b) canada.ca uniformly floors
the relevant boxes ("report 0" for boxes 24/26/56) and explicitly does **not**
report a negative pension adjustment (Box 52), and authorizes no negative value
anywhere; (c) every verified 2025 T4 money box is income, a deduction, a
contribution, a premium, insurable/pensionable earnings, or a PA — none is a
net-of-loss figure. canada.ca states no *global* "no negatives" sentence, which
is why this is surfaced rather than assumed.

*Why these are Class A and A2 was not folded into `[ASSUMED]`:* both are
"what constitutes valid input / what must be rejected," which CLAUDE.md
enumerates as Class A; where I was unsure of the A/B boundary I resolved to
Class A per the "if unsure, it is Class A" rule, and I may not reclassify a
Class A item to Class B for brevity.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **New T4-specific types, not a shared abstraction with T5.** This
  feature introduces `T4Slip`, `T4RawInput`, `T4RawBox`, `T4SlipParser`,
  `T4BoxCodes`, `T4OtherInformation` paralleling the `T5*` types; the existing
  `T5*` types are **not** generalized/renamed into a shared base. Duplication is
  accepted for now (explicitly requested).
- `[ASSUMED]` **Input DTO shape mirrors T5:** `Year`, `EmployerName`,
  `EmployeeName`, `EmployeeAddress` as top-level fields; all numbered boxes and
  "Other information" pairs in an ordered `T4RawBox` list.
- `[ASSUMED]` **Duplicate box code = validation error** (reject), mirroring the
  merged T5 convention, rather than last-write-wins or summing.
- `[ASSUMED]` **Fail-closed via the existing `ParseResult<T>`** (errors and no
  model), consistent with T5.
- `[ASSUMED]` **Naming** follows the T5 precedent (`T4SlipParser.Parse`,
  `T4Slip`, named constants in `T4BoxCodes`).
- `[ASSUMED]` **Spec folder** is `specs/003-t4-slip-parsing/` (001, 002 taken).
- `[ASSUMED]` **Defaults for absent boxes:** amount `0m`, optional scalar `null`,
  exempt indicators `false`.
- `[ASSUMED]` **Box 28 (Exempt) is modelled as three independent booleans**
  (CPP/QPP, EI, PPIP); the raw-input representation of the three sub-fields is an
  architecture detail for `/plan`.
- `[ASSUMED]` **Box 50 (RPP/DPSP registration number) and Box 54 (employer
  account number) are parsed as optional scalar strings;** their fixed formats
  (7-digit; `9 digits + RP + 4 digits`) are **not** enforced in v1 (Box 54 is not
  even on the employee copy). Only Box 50's conditional presence (FR18) is
  enforced.
- `[ASSUMED]` **Box 52 (pension adjustment) is parsed as a `decimal` amount via
  `MoneyRounding`;** the CRA "dollars only (no cents)" presentation is **not**
  separately enforced as a rejection in v1.
- `[ASSUMED]` **"Other information" codes pass through verbatim** and are **not**
  validated against the closed 2025 code set; unknown/future codes are preserved
  (mirrors T5). The CRA **6-codes-per-slip** maximum is **not** enforced in v1.
- `[ASSUMED]` **Value-domain validation (Box 10/29/45) applies only when the box
  is present;** absence is governed by A1, not by these value checks.
- `[ASSUMED]` **No province/box consistency and no CPP2/QPP2 cross-box
  consistency** beyond the four verified conditionals (FR15–18) are enforced in
  v1.
- `[ASSUMED]` **v1 handles tax year 2025 only;** a slip whose Year ≠ 2025 fails
  closed (CLAUDE.md: no config for that year ⇒ fail closed, no adjacent-year
  fallback).
- `[ASSUMED]` **Scope stops at the parsed `T4Slip`;** no mapping, calculation,
  persistence, or GPT surface (spec 004 owns mapping).

## CRA sources

Both pages are the current CRA (canada.ca) T4 pages; older
`.../completing-slips-summaries/` URLs now 404 and are deliberately not cited.
Content was captured live from canada.ca on the retrieval date below (the
analyst's own `WebFetch` was blocked by canada.ca's bot filter with HTTP 403;
the pages were retrieved through a real browser session and treated as the
primary source — the transport differed, the source did not).

**Source 1** — "T4 slip – Information for employers", canada.ca date modified
2026-02-25:
`https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/completing-filing-information-returns/t4-information-employers/t4-slip.html`

**Source 2** — "T4 slip: Statement of Remuneration Paid", canada.ca date
modified 2026-02-02:
`https://www.canada.ca/en/revenue-agency/services/tax/individuals/topics/about-your-tax-return/tax-return/completing-a-tax-return/tax-slips/understand-your-tax-slips/t4-slips/t4-statement-remuneration-paid.html`

| Claim | Source | Tax year | Retrieved |
|---|---|---|---|
| Box 10 Province of employment; valid values `AB, BC, MB, NB, NL, NT, NS, NU, ON, PE, QC, SK, YT, US, ZZ` (ZZ = outside Canada/US or beyond a province) | Source 1 | 2025 | 2026-07-09 |
| Box 12 = employee SIN; enter `000000000` when the employer does not have it | Source 1 | 2025 | 2026-07-09 |
| Box 14 Employment income | Source 1, Source 2 | 2025 | 2026-07-09 |
| Box 16 Employee's CPP contributions; Box 16A Employee's second CPP contributions (CPP2), for slips filed 2024 and after | Source 1 | 2025 | 2026-07-09 |
| Box 17 Employee's QPP contributions; Box 17A Employee's second QPP contributions (QPP2), for slips filed 2024 and after | Source 1 | 2025 | 2026-07-09 |
| Box 18 Employee's EI premiums | Source 1, Source 2 | 2025 | 2026-07-09 |
| Box 20 RPP contributions | Source 1 | 2025 | 2026-07-09 |
| Box 22 Income tax deducted | Source 1, Source 2 | 2025 | 2026-07-09 |
| Box 24 EI insurable earnings; report `0` if none | Source 1 | 2025 | 2026-07-09 |
| Box 26 CPP/QPP pensionable earnings; report `0` if none | Source 1 | 2025 | 2026-07-09 |
| Box 28 Exempt — three independent checkbox indicators: CPP/QPP, EI, PPIP | Source 1 | 2025 | 2026-07-09 |
| Box 29 Employment code — blank or one of `11,12,13,14,15,16,17` | Source 1 | 2025 | 2026-07-09 |
| Box 44 Union dues | Source 1 | 2025 | 2026-07-09 |
| Box 45 Employer-offered dental benefits — one of `1,2,3,4,5`; mandatory for 2023 and after | Source 1 | 2025 | 2026-07-09 |
| Box 46 Charitable donations | Source 1 | 2025 | 2026-07-09 |
| Box 50 RPP or DPSP registration number (7 digits); required when Box 52 present | Source 1 | 2025 | 2026-07-09 |
| Box 52 Pension adjustment (dollars only); not reported when calculated PA is negative or 0 | Source 1 | 2025 | 2026-07-09 |
| Box 54 Employer's account number (15 chars: 9 digits + `RP` + 4 digits); not on the employee copy | Source 1 | 2025 | 2026-07-09 |
| Box 55 Employee's PPIP premiums | Source 1 | 2025 | 2026-07-09 |
| Box 56 PPIP insurable earnings; report `0` if none | Source 1 | 2025 | 2026-07-09 |
| Conditional pairs: Box 16⇒Box 26; Box 18⇒Box 24; Box 55⇒Box 56; Box 52⇒Box 50 | Source 1 | 2025 | 2026-07-09 |
| "Other information" is an open-ended coded code/amount area (max 6 codes per slip); 2025 valid code set incl. 30,31,32,33,34,36,38,39,40,41,42,43,66,67,69,71,74,75,77,78,79,80,81,82,83,85,86,87,88,90,91,92,94,95 | Source 1 | 2025 | 2026-07-09 |
| When to issue a T4: remuneration over $500, or any CPP/QPP/EI/PPIP/income tax deducted | Source 1 | 2025 | 2026-07-09 |
