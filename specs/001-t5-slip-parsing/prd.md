# PRD: T5 Slip Parsing

## Problem

Investment income reported on a CRA **T5 (Statement of Investment Income)** slip
arrives as loosely-typed, externally-extracted box values that the engine cannot
yet trust or work with. We need to turn those raw box values into a single,
validated, strongly-typed `T5Slip` model so that downstream features (CRA-line
mapping, calculation, validation, GPT explanations) have a reliable, domain-safe
foundation. This serves the engine's internal consumers (the Mapper/Calculator
services) and, indirectly, the individual filer whose T5 data must be handled
correctly and without leaking their SIN.

## User stories

1. As a **calling service**, I want to pass already-extracted T5 box values (as a
   structured key/value input) and receive a typed `T5Slip`, so that I can work
   with named, correctly-typed fields instead of raw strings.
2. As a **calling service**, I want every monetary box parsed as `decimal` rounded
   half-up to cents, so that no float drift or inconsistent rounding enters the
   tax pipeline.
3. As a **calling service**, I want parsing scoped to one tax year per slip, so
   that I never accidentally mix values across tax years.
4. As a **compliance reviewer**, I want the recipient's SIN to never appear in
   full in any log, error, or string output, so that we meet the CLAUDE.md SIN
   rule and CRA privacy expectations.
5. As a **calling service**, I want malformed or incomplete input to produce
   structured validation errors (not raw exceptions that echo PII), so that I can
   surface problems safely and decide how to handle them.
6. As an **engine maintainer**, I want unrecognized "other information" box
   code/amount pairs preserved rather than silently dropped, so that future boxes
   don't cause data loss before mapping support is added.

## Functional requirements

1. The parser SHALL accept a structured key/value input (e.g. a dictionary or
   input DTO) representing the extracted boxes of **one** T5 slip and return a
   single typed `T5Slip` model. It SHALL NOT read files, perform OCR, or do any
   I/O or network calls (input is already extracted).
2. The parser SHALL support **all standard numbered T5 boxes**, mapping each to a
   typed property on `T5Slip`:
   - Identification: payer name/address, recipient name/address, **Box 22**
     recipient identification number (SIN or business number), **Box 23**
     recipient type (`1` individual, `2` joint account, `3` corporation,
     `4` association/trust/club/partnership, `5` government/international
     organization — per CRA), **Box 21** report code (`O` original, `A` amended,
     `C` cancelled), **Box 27** foreign currency code (ISO 4217, one currency per
     slip), **Box 28** transit, **Box 29** account number.
   - Amounts: **10** other-than-eligible dividends (actual), **11** taxable amount,
     **12** dividend tax credit; **13** interest from Canadian sources;
     **14** other income from Canadian sources; **15** foreign income;
     **16** foreign tax paid; **17** royalties; **18** capital gains dividends;
     **19** accrued income/annuities; **24** eligible dividends (actual),
     **25** taxable amount, **26** dividend tax credit.
3. The parser SHALL collect any **"other information" code/amount pairs** that are
   not one of the standard boxes above into an extensible collection on the model,
   so no provided value is discarded.
4. Every monetary box value SHALL be parsed as `decimal` and rounded half-up to
   cents using `MidpointRounding.AwayFromZero` (per CLAUDE.md currency rule).
5. The `T5Slip` SHALL carry a required **tax year** and represent exactly one tax
   year. The parser SHALL NOT combine boxes from differing tax years into one slip
   (per CLAUDE.md per-tax-year rule).
6. The recipient identification number (Box 22), when it is a SIN, SHALL never be
   emitted in full by any logging, exception message, or `ToString()`; it SHALL be
   masked to the **last 3 digits** (per CLAUDE.md SIN rule).
7. The parser SHALL validate input and return **structured validation results**
   (field, problem) for: missing required fields (tax year, payer, recipient,
   recipient type), recipient type outside `{1,2,3,4,5}`, non-numeric/garbled amount
   strings, and amounts that violate sign expectations. Validation messages SHALL
   themselves obey the SIN masking rule.
8. The parser SHALL **retain box numbers as the canonical identifiers** on the
   model and SHALL NOT assign or hardcode any CRA line number in C# (mapping is a
   later, JSON-config-driven feature per CLAUDE.md).
9. When a foreign currency code (Box 27) is present and not `CAD`, the parser SHALL
   preserve amounts and the currency code as given and SHALL NOT perform any
   currency conversion.
10. Parsing SHALL be deterministic and side-effect free: the same input always
    yields the same `T5Slip` and validation result.

## Out of scope

- Extracting box values from a **PDF, scanned image, or CRA XML** — input is
  already structured key/value.
- **Box → CRA line number mapping** and `TaxLineEntry` generation (separate feature).
- Tax **calculation** of any kind (taxable amounts beyond what the slip states,
  dividend gross-up, credits).
- **Currency conversion** of foreign-currency amounts to CAD.
- **Persistence**, serialization to/from external stores, or PDF/JSON export.
- **GPT explanations** of the parsed slip.
- Handling **multiple slips** in one call or reconciling duplicates across slips.

## Acceptance criteria

1. **Happy path — full slip**
   Given a structured input containing valid values for all supported T5 boxes and
   a tax year,
   When the slip is parsed,
   Then a `T5Slip` is returned with each box value populated on its typed property
   and zero validation errors.

2. **Decimal rounding**
   Given a monetary box value of `100.005`,
   When the slip is parsed,
   Then the stored value is `100.01` (half-up, away from zero) as a `decimal`.

3. **SIN masking**
   Given a Box 22 SIN of `123456789`,
   When the parsed slip is logged, stringified, or named in a validation error,
   Then only `***-**-789` (last 3 digits) appears and the full SIN never does.

4. **Other-information passthrough**
   Given an input containing a non-standard box code/amount pair,
   When the slip is parsed,
   Then that pair is present in the model's "other information" collection and no
   value is dropped.

5. **Missing required field**
   Given an input with no tax year,
   When the slip is parsed,
   Then a structured validation error identifies the missing tax year and no
   partially-built slip is treated as valid.

6. **Invalid recipient type**
   Given a recipient type of `9`,
   When the slip is parsed,
   Then a structured validation error flags the recipient type as out of range
   `{1,2,3,4,5}`.

7. **Foreign currency preserved, not converted**
   Given Box 27 = `USD` with foreign-income amounts,
   When the slip is parsed,
   Then the amounts and `USD` code are stored unchanged and no CAD conversion occurs.

## Open questions

1. **Validation strictness:** should an invalid slip return a result object that
   carries *both* the best-effort parsed model *and* the errors, or fail closed
   (errors only, no model)? Affects the service's return shape.
2. **Sign expectations:** are any T5 box amounts allowed to be negative, or should
   all monetary boxes be validated as `>= 0`? (T5 generally reports income, not
   losses — confirm before encoding the rule.)
3. **Recipient identifier disambiguation:** should the parser use Box 23 recipient
   type to decide whether Box 22 is a SIN (mask) vs a business number (no mask), or
   always mask Box 22 to be safe?
4. **Required-field set:** which boxes are truly mandatory for a "valid" T5 vs
   optional? (Proposed minimum: tax year, payer name, recipient name, recipient
   type. Confirm.)
5. **Duplicate boxes:** if the input supplies the same box twice, is that a
   validation error, last-write-wins, or summed?
