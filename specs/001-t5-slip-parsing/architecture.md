# Architecture: T5 Slip Parsing

## Approach

This feature adds a single, pure transformation: raw key/value T5 input →
validated, typed `T5Slip`. It touches two parts of the class library —
**Models/** (the typed slip, its value objects, the raw-input DTOs, and the
result/error primitives) and **Services/** (the parser plus small helpers).
**Mappings/** and **Prompts/** are deliberately *not* touched: box → CRA-line
mapping is out of scope (a later, JSON-driven feature), and there is no GPT
surface here.

Control flow is one call:

```
T5RawInput  ──►  T5SlipParser.Parse(input)  ──►  ParseResult<T5Slip>
                     │                               ├─ IsSuccess → Value: T5Slip
                     │                               └─ !IsSuccess → Errors: ValidationError[]
                     ├─ validate required fields / recipient type / duplicates
                     ├─ parse amounts as decimal, round half-up to cents
                     ├─ classify known boxes vs "other information" passthrough
                     └─ never emit Box 22 in full (masked value object)
```

The parser is **fail-closed** (chosen in review): on any validation failure it
returns errors and *no* model, so half-valid data cannot leak into the tax
pipeline. It is deterministic and side-effect free — no I/O, no network, no
static/ambient state — satisfying FR1 and FR10.

## Data model

All new. No changes to existing types (the source tree is still a skeleton).

**Models/**

- **`T5Slip`** — the aggregate result. Carries:
  - `int TaxYear` (required; the slip represents exactly one tax year — FR5).
  - Identification: `PayerName`, `PayerAddress`, `RecipientName`, `RecipientAddress`,
    `RecipientIdentifier RecipientId` (Box 22), `int RecipientType` (Box 23),
    `string? ReportCode` (Box 21), `string? TransitNumber` (Box 28),
    `string? AccountNumber` (Box 29).
  - `string CurrencyCode` (Box 27, default `"CAD"`) — amounts are stored as given;
    no conversion (FR9).
  - Typed amount properties for every standard numbered box: 10, 11, 12, 13, 14,
    15, 16, 17, 18, 19, 24, 25, 26 — each `decimal` (FR2).
  - `IReadOnlyList<T5OtherInformation> OtherInformation` — non-standard code/amount
    pairs preserved verbatim (FR3).
  - `ToString()` overridden to mask the recipient id (never leaks Box 22 — FR6).

- **`RecipientIdentifier`** — value object wrapping Box 22. Stores the raw digits
  privately; exposes `Masked` (`***-**-789`, last 3 digits) and a masking
  `ToString()`. **Always masks**, regardless of recipient type (chosen in review) —
  there is no code path that emits the full value, so a SIN cannot leak (FR6, AC3).
  Value equality on the underlying digits.

- **`T5OtherInformation`** — `{ string Code; decimal Amount; }` for passthrough boxes
  (e.g. Box 30 "Equity linked notes interest" and other CRA "Other information" codes).

- **`T5RawInput`** — the input DTO: `int? TaxYear`, `PayerName`, `PayerAddress`,
  `RecipientName`, `RecipientAddress`, and `IReadOnlyList<T5RawBox> Boxes`. Using a
  **list** (not a dictionary) is deliberate: it can represent the same box code
  twice, which the parser must detect and reject (duplicate → error).

- **`T5RawBox`** — `{ string Code; string? Value; }` — one extracted box as received.

- **`ParseResult<T>`** — `IsSuccess`, `Value` (default when failed), and
  `IReadOnlyList<ValidationError> Errors`, with `Success(value)` / `Failure(errors)`
  factories. Generic so it can be reused by later services.

- **`ValidationError`** — `{ string Field; string Message; }`. Messages never
  include a raw Box 22 value (SIN masking applies to errors too — FR7).

- **`T5BoxCodes`** — named `const string` box-code identifiers (`"10"`, `"11"`, …),
  including the documented named "Other information" code **`"30"` (Equity linked
  notes interest)**. Avoids magic strings. **These are box codes, not CRA line
  numbers** — retaining box codes as canonical identifiers is exactly what FR8
  requires; no CRA line number is introduced anywhere in C#. Codes not in this set
  are not errors — they flow to `OtherInformation` (FR3). CRA does not define a
  fixed catalogue of named boxes 31–39; that area is the generic code/amount region
  handled by the passthrough.

**Mappings/ JSON:** none. Box → CRA-line config is a later feature; this one stops
at the parsed model.

## Services & public API

**Services/**

- **`T5SlipParser`** — the one public entry point.
  - `ParseResult<T5Slip> Parse(T5RawInput input)`
  - **Responsibility:** validate, convert, and assemble a `T5Slip`, or return the
    collected validation errors. Pure and deterministic.
  - **Validation rules applied** (all produce `ValidationError`, fail-closed):
    - Required fields present: `TaxYear`, `PayerName`, `RecipientName`,
      `RecipientType` (the confirmed minimum set) — FR7, AC5.
    - `RecipientType ∈ {1,2,3,4,5}` — else out-of-range error (AC6). Per CRA the
      codes are 1 individual, 2 joint account, 3 corporation, 4 association/trust/
      club/partnership, 5 government/international organization.
    - `ReportCode` (Box 21), when present, ∈ `{O, A, C}` (original/amended/
      cancelled) — per CRA.
    - Each box `Value` parses as a `decimal` (invariant culture); non-numeric →
      error (FR7).
    - Parsed amounts are `>= 0`; negatives rejected (chosen in review).
    - No duplicate box `Code` in `input.Boxes`; duplicates → error (chosen in review).
  - **Domain rules (CLAUDE.md):** per-tax-year (single `TaxYear`, never merged);
    decimal + half-up `AwayFromZero` rounding via the helper below; SIN masking via
    `RecipientIdentifier`; no CRA line numbers.

- **`MoneyRounding`** (internal static helper) — `decimal ToCents(decimal value)`
  = `Math.Round(value, 2, MidpointRounding.AwayFromZero)`. Single chokepoint for the
  currency rule (FR4, AC2). Every amount flows through it.

No other services change. The `Mapper`, `Calculator`, and `Validator` described in
the README remain future work and consume `T5Slip` later.

## Trade-offs

1. **`ParseResult<T>` vs throwing exceptions for invalid input.**
   Rejected exceptions. Invalid slips are an expected, routine outcome (bad OCR
   upstream, missing boxes), not exceptional control flow. A result object lets the
   caller collect *all* errors at once and keeps PII out of stack traces/exception
   messages — throwing risks a raw Box 22 landing in a log via an unhandled
   exception. Result is also cheaper and deterministic.

2. **Fail-closed (errors, no model) vs returning a partial best-effort slip.**
   Rejected the partial-model option (explicitly, in review). A partially-valid
   `T5Slip` is a foot-gun: a downstream Calculator could consume it without noticing
   the errors and produce a wrong return. Fail-closed makes "invalid" unusable by
   construction. Cost: the caller can't see the good boxes when one is bad — accepted,
   since correctness of a tax figure outranks partial convenience.

3. **List-of-pairs input vs `IReadOnlyDictionary<string,string>`.**
   Rejected a dictionary. A dictionary silently collapses duplicate box codes, making
   the "duplicate box → validation error" rule impossible to enforce. A list preserves
   exactly what was extracted, so the parser — not the collection type — decides how
   duplicates are handled.

4. **`RecipientIdentifier` value object vs a plain masked `string`.**
   Rejected the plain string. A value object guarantees there is no accessor that
   returns the full SIN and centralizes masking in one place, so FR6/AC3 can't be
   violated by a stray `string.Format`. It also gives correct value equality.

## Risks

- **SIN leakage (PII).** Highest-severity risk. Mitigated structurally:
  `RecipientIdentifier` never exposes the full value; `T5Slip.ToString()` and all
  `ValidationError.Message`s use only the masked form; "always mask" removes the
  type-based branch where a SIN could slip through. A dedicated final task
  regression-tests that no full Box 22 appears in any output (AC3).
- **Rounding / currency correctness.** Mitigated by routing every amount through the
  single `MoneyRounding.ToCents` chokepoint (half-up `AwayFromZero`) and parsing with
  invariant culture so `"100.005"` is never mis-read as thousands separators. Directly
  tested (AC2).
- **Per-tax-year mixing.** Mitigated: `T5Slip` holds exactly one required `TaxYear`;
  the parser takes one slip's boxes and never merges years (FR5).
- **CRA compliance / scope creep into mapping.** Mitigated by retaining box codes as
  the only identifiers and introducing no CRA line number in C#; mapping stays a
  separate JSON-driven feature (FR8).
- **Silent data loss on unknown boxes.** Mitigated by the `OtherInformation`
  passthrough (FR3, AC4), so future/unknown boxes survive until mapping exists.

## Notes for planning

The five PRD open questions were resolved in review: fail-closed `ParseResult<T>`;
always-mask Box 22; reject negative amounts; duplicate box = validation error;
required-field minimum = tax year, payer name, recipient name, recipient type. These
are encoded above and drive `tasks.md`.

**Reconciled against the CRA "Completing the T5 slip" guidance:** box names/numbers
confirmed; recipient type (Box 23) corrected to the full CRA set `{1,2,3,4,5}`
(was `{1,2,3}`); report code (Box 21) values `{O,A,C}` added; foreign currency
(Box 27) is ISO 4217 with one currency per slip. One consequence of the
"always-mask Box 22" decision, now that recipient types 3–5 are known to be
non-individuals: business/account numbers are masked too. That is harmless for
safety (over-masking never leaks) but means Box 22 is never shown in full even for a
corporation — accepted under the chosen decision; revisit only if a downstream
feature needs the full business number.
