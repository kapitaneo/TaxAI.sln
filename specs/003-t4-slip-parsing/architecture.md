# Architecture: T4 Slip Parsing

> Scope: turn already-extracted T4 (Statement of Remuneration Paid) box values
> into one validated, strongly-typed `T4Slip` for **tax year 2025 only**. No
> mapping, calculation, persistence, or GPT (those are later specs). This
> document is design only — no production code.

## Status of the PRD's open Class A questions

Both blocking questions from `prd.md` are now **resolved by the requester** and
are treated as settled inputs to this design:

- **A2 — sign rule:** every T4 monetary box must be `>= 0`; a negative value
  **fails closed**. (CRA amends/cancels a slip rather than reporting a negative;
  confirmed against canada.ca.)
- **A1 — required-present set:** grounded in the **RC4120** *Employers' Guide*
  identification area (retrieved live from canada.ca via browser on 2026-07-09;
  `WebFetch` is 403-blocked by canada.ca, as the PRD already records). The
  parser **requires present** (fail closed if absent):
  1. `Year` (and it must equal 2025 — see below)
  2. `EmployerName`
  3. `EmployerAddress`
  4. `EmployeeName`
  5. `EmployeeAddress`
  6. **Box 12** — SIN present (value may be the `000000000` no-SIN sentinel; a
     *real* SIN is never required)
  7. **Box 54** — Employer's account number present
  8. **Box 45** — Employer-offered dental benefits present (value in `{1..5}`;
     canada.ca: mandatory for 2023 and after)

  Everything else (Box 10, Box 14, and all other numbered/coded boxes) is
  **optional-when-present**: validated only if a value is supplied.

### Two A1 points flagged for one-pass human ratification

Encoded as decided, but surfaced here because each touches a verified CRA fact:

- `[CONFIRM]` **Box 54 required ⇒ this parser presumes employer / filing-side
  input.** Source 1 (canada.ca T4 slip page) states Box 54 is *not on the
  employee copy*. Requiring it means an employee's paper-slip extraction would
  fail closed. This is consistent with RC4120 being the *employer's* guide and
  with the PRD's stated internal-consumer framing, but if the intended input is
  ever the employee copy, Box 54 must move to optional-when-present.
- `[CONFIRM]` **Box 10 (Province of employment) is optional-when-present**, per
  the enumerated A1 list. RC4120 lists province inside the mandatory
  identification area, so a stricter reading would require it. Kept optional as
  decided; the value is still validated against the abbreviation set when
  supplied. Province is not tax-load-bearing at the *parse* stage (mapping /
  calculation is spec 004+).

## Approach

One new service, `T4SlipParser`, in `Services/`, plus a small cluster of new
`Models/` types. The parser is a **pure, deterministic, fail-closed** function
`T4RawInput → ParseResult<T4Slip>`: no I/O, no OCR, no network, no ambient
state. It deliberately **parallels the existing `T5SlipParser`** rather than
sharing a generalized base (requester decision, PRD `[ASSUMED]`), so the two
slip families stay independently evolvable.

Pieces and how they fit:

- **`Models/` (data):** `T4RawBox` + `T4RawInput` (loose input DTOs),
  `T4Slip` (validated output), `T4BoxCodes` (canonical box-code constants),
  `T4OtherInformation` (passthrough code/amount pair).
- **`Services/` (behaviour):** `T4SlipParser.Parse` — validates first, and only
  on zero errors builds the slip.
- **Reused as-is (no change, not re-specified):** `ParseResult<T>`,
  `ValidationError`, `MoneyRounding.ToCents`, `RecipientIdentifier`. These are
  the T5-era chokepoints; T4 flows through the same ones so rounding and SIN
  masking cannot drift.

The parser follows the exact validate-then-build shape of `T5SlipParser`:
collect **all** errors across independent checks, return `Failure(errors)` if
any, else `Success(BuildSlip(input))`. Collecting all errors (not
short-circuiting) is what makes ACs like "one structured error per missing
required field" (AC20) checkable.

### Which parts of the library are touched

| Layer | Touched? | What |
|---|---|---|
| Models | **new files** | `T4RawInput`, `T4Slip`, `T4BoxCodes`, `T4OtherInformation` |
| Mappings | **no** | T4 box→line mapping is spec 004; no JSON, no line numbers here |
| Services | **new file** | `T4SlipParser` |
| Prompts | **no** | no GPT surface in this feature |

## Data model

### `T4RawBox` (new, `Models/T4RawInput.cs`)

`sealed record T4RawBox(string Code, string? Value)` — one extracted numbered or
"Other information" box: its code (e.g. `"14"`, `"16A"`, `"40"`) and raw string
value (`null` when present-but-empty). Mirrors `T5RawBox`.

### `T4RawInput` (new, `Models/T4RawInput.cs`)

Loose, all-nullable upstream data. `Boxes` is a **list, not a dictionary**, so a
duplicated box code survives to be detected (a dictionary would silently
collapse it — this is the FR20 / AC2 requirement).

| Member | Type | Notes |
|---|---|---|
| `Year` | `int?` | required; must be 2025 |
| `EmployerName` | `string?` | required |
| `EmployerAddress` | `string?` | required (A1) |
| `EmployeeName` | `string?` | required |
| `EmployeeAddress` | `string?` | required (A1) |
| `ExemptCppQpp` | `bool?` | Box 28 checkbox — null/false = not marked |
| `ExemptEi` | `bool?` | Box 28 checkbox |
| `ExemptPpip` | `bool?` | Box 28 checkbox |
| `Boxes` | `IReadOnlyList<T4RawBox>` | ordered; may contain duplicates; default empty |

**Box 28 as three raw booleans, not code/value entries** `[ASSUMED]`: Box 28 is
a checkbox trio (CPP/QPP, EI, PPIP), not a code/amount pair, so it does not
belong in `Boxes`. Modelling it as three nullable bools keeps `Boxes` strictly
for real numbered/coded boxes and avoids inventing synthetic sub-codes.

### `T4Slip` (new, `Models/T4Slip.cs`)

Validated, one tax year. Amounts are `decimal` already cents-rounded. Absent
optional amount boxes default to `0m`; absent optional scalars to `null`; exempt
indicators to `false`. **No CRA line number appears** — box codes stay canonical
(mapping is spec 004). `ToString()` is **overridden** to a SIN-safe summary that
defers to `RecipientIdentifier.Masked`.

Identification: `Year (int)`, `EmployerName`, `EmployerAddress?`,
`EmployeeName`, `EmployeeAddress?`, `Sin (RecipientIdentifier?)` (Box 12),
`EmployerAccountNumber? (string)` (Box 54), `RppDpspRegistrationNumber?
(string)` (Box 50).

Coded/constrained: `ProvinceOfEmployment? (string)` (Box 10),
`EmploymentCode? (string)` (Box 29), `DentalBenefitCode (string)` (Box 45),
`ExemptCppQpp/ExemptEi/ExemptPpip (bool)` (Box 28).

Monetary (`decimal`, cents-rounded): `EmploymentIncome` (14),
`CppContributions` (16), `SecondCppContributions` (16A), `QppContributions`
(17), `SecondQppContributions` (17A), `EiPremiums` (18), `RppContributions`
(20), `IncomeTaxDeducted` (22), `EiInsurableEarnings` (24),
`CppQppPensionableEarnings` (26), `UnionDues` (44), `CharitableDonations` (46),
`PensionAdjustment` (52), `PpipPremiums` (55), `PpipInsurableEarnings` (56).

Passthrough: `OtherInformation (IReadOnlyList<T4OtherInformation>)`.

### `T4OtherInformation` (new, `Models/T4OtherInformation.cs`)

`sealed record T4OtherInformation(string Code, decimal Amount)` — one
"Other information" code/amount pair preserved verbatim. Mirrors
`T5OtherInformation`. Unknown/future codes are **not** an error (FR21 / AC27).

### `T4BoxCodes` (new, `Models/T4BoxCodes.cs`)

`static class` of `const string` box codes (`"10"`, `"12"`, `"14"`, `"16"`,
`"16A"`, `"17"`, `"17A"`, `"18"`, `"20"`, `"22"`, `"24"`, `"26"`, `"29"`,
`"44"`, `"45"`, `"46"`, `"50"`, `"52"`, `"54"`, `"55"`, `"56"`). **Box codes,
not line numbers.** Box 28 is not here (it is the three raw booleans).

### Mappings/

**None.** No JSON, no `Mappings/` change. T4 box→CRA line mapping is spec 004.
No CRA line number is written in C# (AC25).

## Services & public API

### `T4SlipParser` (new, `Services/T4SlipParser.cs`)

```
public sealed class T4SlipParser
{
    public ParseResult<T4Slip> Parse(T4RawInput input);
}
```

Responsibility: validate one slip's extracted data and, only if fully valid,
produce the typed slip. Deterministic and side-effect free (AC28). Validation
passes (each appends to a shared error list; none short-circuits the others):

1. **Duplicate box codes** (FR20/AC2) — one error per code appearing >1×.
2. **Required-present** (A1/FR19, ACs 5,7,8,20) — `Year`, `EmployerName`,
   `EmployerAddress`, `EmployeeName`, `EmployeeAddress`, and boxes **12, 54, 45**
   present; one error per missing item.
3. **Tax year == 2025** (FR4, ACs 5,6,24) — missing Year and Year ≠ 2025 are
   distinct errors. No coercion, **no adjacent-year fallback** (CLAUDE.md).
4. **Monetary boxes** (FR5–7, A2; ACs 3,4,16,18) — every money box parsed as
   `decimal` (invariant culture); non-numeric ⇒ error naming the box; `< 0` ⇒
   error naming the box; value rounded via `MoneyRounding.ToCents` (half-up
   `AwayFromZero`). Never `double`/`float`, never a bare `Math.Round`.
5. **Constrained-value boxes when present** (FR11–13; ACs 9,10,11) —
   Box 10 ∈ `{AB,BC,MB,NB,NL,NT,NS,NU,ON,PE,QC,SK,YT,US,ZZ}`;
   Box 29 blank or ∈ `{11,12,13,14,15,16,17}`;
   Box 45 ∈ `{1,2,3,4,5}` (also required-present per A1).
6. **Conditional companion boxes** (FR15–18; ACs 12–15) —
   16⇒26, 18⇒24, 55⇒56, 52⇒50. Companion value may be `0`; absence fails closed.
7. **Box 12 SIN** (FR8–10; ACs 21–23) — wrapped in `RecipientIdentifier`;
   `000000000` accepted; never emitted in full anywhere.

`BuildSlip` runs only on zero errors: routes each box to its typed property;
any non-standard code becomes a `T4OtherInformation` entry (FR21); the three
exempt booleans map straight through.

### Domain rules from CLAUDE.md and where they land

| Rule | Where enforced |
|---|---|
| Never log/echo a full SIN; mask to last 3; override `ToString` | Box 12 → `RecipientIdentifier`; `T4Slip.ToString()` override; no `ValidationError.Message` includes a SIN |
| Per-tax-year; no "latest"; fail closed if year has no config | Pass 3: require `Year == 2025`, no fallback |
| `decimal`, half-up `AwayFromZero`, via the shared helper | Pass 4: `MoneyRounding.ToCents`; no `double`/`float`/bare `Math.Round` |
| Box values → CRA line via JSON only; never hardcode a line in C# | No line numbers anywhere; box codes canonical (AC25) |
| Tax facts from canada.ca only | Box sets/codes verified in `prd.md` CRA table + RC4120 for A1 |

## Trade-offs

**1. Parallel T4* types vs a shared generic `SlipParser<TBox,TSlip>` base.**
Rejected sharing. A generic base would couple T4 and T5 evolution (a new T5
rule risks perturbing T4, which files real returns) and force a premature
abstraction over two slips that differ in identification, conditionals, and
constrained-value boxes. The requester explicitly asked for duplication (PRD
`[ASSUMED]`). Cost: some copy-pasted validation shape. Benefit: each slip
family is independently reviewable and can't regress the other. Accepted.

**2. Box 28 as three raw booleans vs three synthetic box codes in `Boxes`.**
Rejected synthetic codes (e.g. `"28-EI"`). Box 28 is physically a checkbox
trio, not a code/amount pair; forcing it into `Boxes` would invent non-CRA
codes and complicate the duplicate-detection and amount-parsing passes (which
assume `Boxes` holds real numbered/coded boxes). Three nullable bools model the
slip faithfully. Cost: three extra top-level DTO fields. Accepted.

**3. Requiring Box 54 (per A1) vs keeping all boxes optional-when-present like
T5.** T5 requires only names + year + recipient type. Adopting the RC4120
identification-area set makes T4 stricter and ties input provenance to the
employer/filing copy (flagged `[CONFIRM]`). Alternative: mirror T5's lighter
required set (the PRD's original default). Chose the requester's RC4120 set
because a T4 that a filer relies on should carry its mandatory identification
fields; the provenance implication is documented for override rather than
hidden. Accepted with a ratification flag.

**4. One cohesive parser class vs split validator classes for parallelism.**
Kept a single `T4SlipParser` (matches the `T5SlipParser` idiom). Splitting
validation across files would enable more parallel implementation tasks but
would diverge from the established codebase shape and fragment a ~200-line
cohesive unit. Parallelism is instead recovered in the **test** wave (distinct
suites in distinct files). Accepted.

## Risks

**Correctness — tax-year isolation.** A 2024/2026 slip silently parsed under
2025 rules would be a filing error. *Mitigation:* Pass 3 hard-requires
`Year == 2025` with no fallback; ACs 6 and 24 assert a non-2025 slip fails
closed and that no 2025-only box rule (16A/17A, codes 90/91/92) is applied.

**Correctness — rounding.** Float drift or a bare `Math.Round` would mis-round
cents. *Mitigation:* single chokepoint `MoneyRounding.ToCents` (half-up
`AwayFromZero`), already unit-tested under spec 001; AC16 asserts `100.005 →
100.01` on a real box; `double`/`float` never used.

**Correctness — conditional pairs.** Missing a companion (e.g. Box 16 without
Box 26) would drop pensionable/insurable context. *Mitigation:* Pass 6 enforces
all four verified pairs; companion may be `0` but not absent (ACs 12–15, 18).

**Security / PII — SIN (Box 12).** A leaked SIN is a privacy breach.
*Mitigation:* Box 12 only ever exists as `RecipientIdentifier` (raw value held
privately, never emitted); `T4Slip.ToString()` overridden; no error message
contains a SIN; `000000000` sentinel accepted (ACs 21–23). AC22 asserts no
9-consecutive-digit substring on any built-in string/serialization surface.

**CRA compliance — box/code set drift.** Using a wrong box code or an outdated
constrained set would map real dollars wrongly downstream. *Mitigation:* every
box number, the province set, the Box 29/45 sets, and the conditional pairs are
canada.ca-verified in the PRD's CRA-sources table; A1's required set is grounded
in RC4120. No box→line number is assigned here (deferred to spec 004's JSON).

**Compliance — required-set provenance (Box 54 / Box 10).** Getting "what must
be present" wrong rejects valid slips or accepts invalid ones. *Mitigation:*
the two debatable items are flagged `[CONFIRM]` above for one-pass human
ratification before implementation, not silently baked in.

## Contract / parallelism note

Wave 0 (T01) declares every public signature above with
`NotImplementedException` bodies; later waves code against that contract. Within
the test wave, no two tasks share a file path and none references a type created
by a sibling — the rule that keeps parallel branches conflict-free.
