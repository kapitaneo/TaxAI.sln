# Architecture: T4 Box → CRA Line Mapping

> Scope: map a validated `T4Slip` (2025) to aggregated `TaxLineEntry` outputs via
> a data-driven JSON config, mirroring the merged spec-002 T5 mapping with new
> `T4*` types. Mapping only — no parsing, no calculation. Design only; no
> production code here. Both former Class A questions are resolved in the PRD
> (A1 direct-sum set, A2 line values verified against canada.ca).

## Approach

One new service, `T4LineMapper`, plus the supporting types that feed it, all
paralleling the spec-002 T5 mapping cluster one-for-one (requester's standing
"duplicate, don't generalize" decision). Nothing about the T5 types changes; the
only genuinely shared types are the already-generic `TaxLineEntry`,
`ParseResult<T>`, `ValidationError`, and `MoneyRounding`.

Data flow:

```
IReadOnlyCollection<T4Slip>
   → T4LineMapper.Map
       ├─ validate (non-empty, single tax year)                → fail closed
       ├─ IT4LineMapProvider.TryGetForYear(year)               → fail closed (missing/malformed/mismatch)
       │     └─ EmbeddedT4LineMapProvider reads
       │        Mappings/T4LineMap.{year}.json → T4LineMappingConfig
       │        → T4LineMap.Create (validate codes/lines)      → fail closed (dup/empty/unknown/blank)
       ├─ for each slip: T4SlipBoxAmounts.Enumerate → (boxCode, amount) pairs
       │     (non-zero amount boxes + every OtherInformation entry)
       ├─ for each pair: map.LinesFor(boxCode)
       │     ├─ hit  → accumulate amount onto each CRA line, record source box
       │     └─ miss → add boxCode to UnmappedBoxCodes (once)
       └─ sum each line via MoneyRounding.ToCents → TaxLineEntry list
   → ParseResult<T4MappingResult> { Lines, UnmappedBoxCodes }
```

### Which parts of the library are touched

| Layer | Touched? | What |
|---|---|---|
| Models | **new files** | `T4MappingResult`, `T4BoxLineMapping` + `T4LineMappingConfig`, `T4LineMap`, `T4OtherInformationCodes` |
| Mappings | **new + edit** | new `T4LineMap.2025.json`; `README.md` gains a T4 section; `.csproj` gains one `<EmbeddedResource>` entry |
| Services | **new files** | `IT4LineMapProvider`, `EmbeddedT4LineMapProvider`, `T4SlipBoxAmounts`, `T4LineMapper` |
| Prompts | **no** | no GPT surface |

### The one deliberate deviation from the T5 mirror

Spec-002's `T5LineMap.Create` validates blank codes, empty line lists, blank
lines, and duplicate codes — but **not** whether a box code is real. The T4 PRD
adds **FR8 / AC9: an unknown box code in config fails closed**, so a config typo
cannot silently misroute real dollars. That requires a **recognized-code set**,
which introduces the one new type with no T5 analogue: `T4OtherInformationCodes`
(the CRA-verified 2025 "Other information" code set). `T4LineMap.Create`
validates each config box code against `T4BoxCodes` (standard boxes, referenced
by constant — never re-listed as literals) ∪ `T4OtherInformationCodes.All`.

## Data model

### `T4MappingResult` (new, `Models/T4MappingResult.cs`) — mirrors `T5MappingResult`
`sealed record T4MappingResult(IReadOnlyList<TaxLineEntry> Lines, IReadOnlyList<string> UnmappedBoxCodes)`
with a static `Empty`. `Lines` are the aggregated destinations; `UnmappedBoxCodes`
is every populated box the config did not route (nothing dropped — FR14, user
story 5).

### `T4BoxLineMapping` + `T4LineMappingConfig` (new, `Models/T4LineMappingConfig.cs`) — mirror `BoxLineMapping`/`T5LineMappingConfig`
- `sealed record T4BoxLineMapping(string BoxCode, IReadOnlyList<string> LineNumbers)` — one authored rule; a box may map to several lines, so `LineNumbers` is a list.
- `sealed record T4LineMappingConfig(int TaxYear, IReadOnlyList<T4BoxLineMapping> Mappings)` — the deserialized JSON file.

### `T4LineMap` (new, `Models/T4LineMap.cs`) — mirrors `T5LineMap`
Resolved, validated box→line lookup for one year. `static ParseResult<T4LineMap>
Create(T4LineMappingConfig config)` is **fail-closed**, collecting a
`ValidationError` per problem:
- blank box code;
- empty `LineNumbers` (FR6 / AC8);
- blank line number;
- duplicate box code (FR7 / AC7);
- **unknown box code** (FR8 / AC9) — not in `T4BoxCodes` ∪ `T4OtherInformationCodes.All`.

Instance: `IReadOnlyList<string>? LinesFor(string boxCode)` (null on miss),
`int TaxYear`, `IReadOnlyCollection<string> BoxCodes`. **No CRA line number is a
C# literal here** — every line string originates from the JSON (AC22).

### `T4OtherInformationCodes` (new, `Models/T4OtherInformationCodes.cs`) — no T5 analogue
`static class` exposing `IReadOnlySet<string> All` — the CRA-verified **2025**
"Other information" code set (`30,31,32,33,34,36,38,39,40,41,42,43,66,67,69,71,
74,75,77,78,79,80,81,82,83,85,86,87,88,90,91,92,94,95`, per spec-003's cited CRA
table). These are **box codes, not line numbers**, so they are legitimately C#
constants (the no-hardcoding rule governs *line numbers*). Used only to validate
config completeness (FR8). The 2025-scoped set is tagged `[ASSUMED]` — a new year
that changes the code set is a new constant set, consistent with the per-year
rule.

### `Mappings/T4LineMap.2025.json` (new) — the only place line numbers live
Shape mirrors T5 exactly:
```json
{ "taxYear": 2025,
  "mappings": [
    { "boxCode": "14", "lineNumbers": ["10100"] },
    { "boxCode": "18", "lineNumbers": ["31200"] },
    { "boxCode": "20", "lineNumbers": ["20700"] },
    { "boxCode": "22", "lineNumbers": ["43700"] },
    { "boxCode": "44", "lineNumbers": ["21200"] },
    { "boxCode": "52", "lineNumbers": ["20600"] },
    { "boxCode": "39", "lineNumbers": ["24900"] },
    { "boxCode": "41", "lineNumbers": ["24900"] },
    { "boxCode": "42", "lineNumbers": ["10120"] },
    { "boxCode": "43", "lineNumbers": ["24400"] },
    { "boxCode": "77", "lineNumbers": ["22900"] } ] }
```
Every value is the canada.ca-verified 2025 line (PRD FR13 / CRA-sources table).
Boxes 16/16A/17/17A/46/55, 24/26/56, and the "see line" codes are **deliberately
absent** so they surface in `UnmappedBoxCodes` (FR14). Embedded via a new
`.csproj` `<EmbeddedResource>` with `LogicalName`
`TaxLogicEngine.Mappings.T4LineMap.2025.json`.

### `Mappings/README.md` — gains a T4 section
The existing T5 content is untouched; a new "T4 box → CRA line mapping" section
records each mapped box with its source, and — critically — the **per-box reason**
each deferred/unmapped box is absent (calc/schedule/residence vs no-line vs
do-not-report). Per FR14 that distinction is *documentary here*, not encoded as
behaviour.

## Services & public API

### `T4SlipBoxAmounts` (new, `Services/T4SlipBoxAmounts.cs`) — mirrors `T5SlipBoxAmounts`
`static IEnumerable<(string BoxCode, decimal Amount)> Enumerate(T4Slip slip)`.
The single place that knows box code → typed property. Yields each of the 15
amount boxes (`14,16,16A,17,17A,18,20,22,24,26,44,46,52,55,56`) whose value is
**non-zero** (zero/absent skipped so they aren't falsely reported unmapped —
AC17/18), then every `OtherInformation` entry verbatim. **Deferred amount boxes
(16/16A/17/17A/24/26/46/55/56) are intentionally included** so that, when
populated, the mapper surfaces them as unmapped (AC13/AC14) rather than dropping
them. Non-amount boxes (12 SIN, 54, 50, 10, 29, 45, 28) are **never enumerated**
— the SIN never enters the pipeline (FR15 / AC20).

### `IT4LineMapProvider` / `EmbeddedT4LineMapProvider` (new, `Services/`) — mirror the T5 provider pair
- `interface IT4LineMapProvider { ParseResult<T4LineMap> TryGetForYear(int taxYear); }` — abstracts I/O so the mapper is pure and tests inject in-memory maps.
- `EmbeddedT4LineMapProvider` loads `TaxLogicEngine.Mappings.T4LineMap.{year}.json` from assembly resources; fail-closed on missing resource (FR5/AC5), malformed JSON / missing `taxYear` (FR9/AC6), and declared-year mismatch; then delegates to `T4LineMap.Create`.

### `T4LineMapper` (new, `Services/T4LineMapper.cs`) — the entry point, mirrors `T5LineMapper`
```
public sealed class T4LineMapper
{
    public T4LineMapper(IT4LineMapProvider provider);   // ctor-injected
    public ParseResult<T4MappingResult> Map(IReadOnlyCollection<T4Slip> slips);
}
```
Responsibility: validate the slip set, resolve the year's map, aggregate. Pure
and deterministic (AC23). Validation (`Failure`, no partial map — FR3):
- empty/null slip set → error (AC3);
- **mixed tax years** → error (AC4).

Note the deliberate **absence of the T5 currency check**: `T4Slip` has no
`CurrencyCode` (T4 is CAD by definition — PRD "out of scope"), so `Validate` only
checks non-emptiness and single-year.

Aggregation exactly as `T5LineMapper`: a `LineAccumulator` per CRA line sums
amounts and collects distinct source boxes; a box with no mapping is added once
to `UnmappedBoxCodes`; entries are ordered by line number and each total passes
through `MoneyRounding.ToCents` (AC10/11/15).

### Domain rules from CLAUDE.md and where they land

| Rule | Where |
|---|---|
| Never read/emit a full SIN; mask to last 3 | `T4SlipBoxAmounts` enumerates amounts + Other-info only; SIN is not an amount box, never enters the pipeline; no error message references it (FR15 / AC20) |
| Per-tax-year; no "latest"; fail closed if year absent | `Map` requires one year; `EmbeddedT4LineMapProvider` fails closed on a missing year, no adjacent-year fallback (FR5 / AC5, AC21) |
| `decimal`, half-up `AwayFromZero`, via the shared helper | Every line total through `MoneyRounding.ToCents` (FR11 / AC15/16) |
| Box→line via JSON only; never hardcode a line in C# | All line numbers in `T4LineMap.2025.json`; none in C# (FR4 / AC22) |
| Tax facts from canada.ca only | Every mapped line canada.ca-verified in the PRD CRA-sources table |

## Trade-offs

**1. Parallel `T4*` mapping types vs a shared generic `LineMapper<TSlip>` base.**
Rejected sharing. A generic base spanning T4 and T5 would couple two families
that map real dollars onto real returns; a change to one risks the other. The
requester's standing decision (spec 003) is to duplicate, and the mapping shapes
already diverge (T5 has a currency check and different box enumeration; T4 adds
unknown-code validation). Cost: ~7 near-duplicate types. Benefit: each family is
independently reviewable and regression-isolated. Accepted.

**2. Unknown-code validation (FR8) via a C# code set vs config-only or none.**
Spec-002 did no unknown-code check. Rejected "no check" — a config typo (`"144"`)
would silently drop real income into `UnmappedBoxCodes` with no signal. Rejected
putting the recognized-code *list* in JSON — the recognized set is a domain fact
about what a valid T4 box code *is*, not a mapping value, and lives naturally
next to `T4BoxCodes`. `T4OtherInformationCodes` (box codes, not line numbers) is
the minimal addition; it does not violate the no-hardcoded-**line** rule.
Accepted, tagged `[ASSUMED]`.

**3. One uniform `TaxLineEntry` per destination vs a typed "kind" (income /
deduction / credit).** Kept uniform (mirrors spec-002 trade-off #4). A typed kind
would help a later calculation feature but is speculative now and would diverge
from the merged T5 result shape. `SourceBoxCodes` already carries enough
provenance. Deferred; `[ASSUMED]`.

**4. Include deferred amount boxes in `T4SlipBoxAmounts` vs exclude them.**
Chose to **include** boxes 16/16A/17/17A/24/26/46/55/56 in enumeration even
though the config doesn't map them. Excluding would make AC13/AC14 impossible —
a populated CPP box would vanish rather than surface as unmapped, violating
"nothing a slip reported is lost." The mapper's miss-path is exactly what turns
an unmapped-but-populated box into an honest `UnmappedBoxCodes` entry. Accepted.

## Risks

**Correctness — wrong line number.** A mis-keyed line routes real income to the
wrong place on a return. *Mitigation:* every mapped line is canada.ca-verified
(PRD CRA-sources); values live only in `T4LineMap.2025.json`; the T05 config task
ships a test asserting the resolved map returns the verified lines; AC2 exercises
the full mapped set end-to-end.

**Correctness — silent misroute from a config typo.** *Mitigation:* FR8/AC9
unknown-code validation fails the config closed rather than dropping the amount.

**Correctness — cross-year contamination.** A 2025 line applied to a 2024 slip is
a filing error. *Mitigation:* single-year enforcement (AC4), fail-closed missing
config with no fallback (AC5/AC21), provider rejects a year-mismatched file.

**Correctness — rounding drift.** *Mitigation:* single chokepoint
`MoneyRounding.ToCents` on every line total (AC15/16); no `double`/`float`, no
bare `Math.Round`.

**Security / PII — SIN leak.** *Mitigation:* the SIN is a non-amount box that
`T4SlipBoxAmounts` never enumerates, so it cannot reach a `TaxLineEntry`,
`UnmappedBoxCodes`, or an error message; AC20 asserts no 9-digit run in any
mapping output/serialization.

**CRA compliance — over-mapping a calculation box.** Mapping Box 16 or Box 46
directly would produce a wrong (un-split, un-scheduled) amount. *Mitigation:*
those boxes are deliberately absent from the config and verified to surface as
`UnmappedBoxCodes` (AC13/14); README records the canada.ca reason for each.

## Contract / parallelism note

Wave 0 (T01) declares every public signature above with `NotImplementedException`
bodies (and the data types as declared shells); later waves code against it.
Within each later wave, no two tasks share a file path and none references a type
created by a sibling — see `tasks.md`.
