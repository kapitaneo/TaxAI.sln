# Architecture: T4 Case Processor

Implements [prd.md](prd.md) (spec 008), **feature class `tax-domain`**, tax year
**2025 only**. This document asserts **no CRA fact** and contains **no CRA line
number**: every mapping value stays in
`src/TaxLogicEngine/Mappings/T4LineMap.2025.json`, verified in spec 004.

## Approach

One new module, `src/CaseProcessing/`, owning exactly one job: *drive a single
validated T4 extraction through the existing engine and return an attributed,
fail-closed result.* It is a thin, deterministic coordinator — every tax
judgement is delegated to code that already shipped and was already verified.

A new module is **forced**, not chosen. `src/DocumentExtraction/CLAUDE.md`
forbids `T4SlipParser`/`T4LineMapper` usage inside that module, and
`src/TaxCases/CLAUDE.md` fixes TaxCases' sole production dependency as
`TaxLogicEngine`. Neither existing module may host this code without breaking
its own approved boundary, and TaxLogicEngine itself must not learn about
`DocumentExtraction` (that arrow would reverse the dependency direction). The
new module is the only placement that keeps every existing boundary intact.

Dependencies point inward and are exactly two:

```
DocumentExtraction ──┐
                     ├──► CaseProcessing ──► TaxLogicEngine
TaxLogicEngine ──────┘        (new)
```

`CaseProcessing → DocumentExtraction` (for `ValidatedExtraction`,
`ExtractedDocumentKind`) and `CaseProcessing → TaxLogicEngine` (for the parser,
mapper, models, and `ParseResult<T>`). **No `TaxCases` reference in either
direction**, and nothing references `CaseProcessing`.

### The pipeline, in fixed order

Error ordering is fixed so determinism (AC-7) is mechanically testable.

| # | Stage | On failure |
|---|---|---|
| 1 | Null extraction | single `Extraction` error, return |
| 2 | `Kind` is `T4Slip` **and** `T4Payload` non-null | error(s), return — nothing below can be evaluated without a payload |
| 3 | `TaxYear` is a supported year | accumulate |
| 4 | Payload structurally traversable | accumulate |
| — | *if stage 3 or 4 produced errors* | return them together |
| 5 | `T4SlipParser.Parse(payload)` | parser errors **verbatim, in order**; **mapper never invoked**, provider never queried |
| 6 | `slip.Year == extraction.TaxYear` | single `SlipYear` error, return |
| 7 | `T4LineMapper.Map([slip])` | mapper/provider errors verbatim; **no retry with another year** |
| 8 | Success | construct `ProcessedT4Extraction` |

Stages 3 and 4 accumulate together because they are independent — an
unsupported year and a malformed box collection are separate defects and a
caller fixing one should learn about the other in the same pass. Stages 1, 2, 5,
6, and 7 short-circuit because each strictly depends on the previous stage
having succeeded; continuing would mean guessing what an absent value "meant",
which is the coercion the contract forbids.

Stage 4 is the **new** rule and the reason this module is more than plumbing.
Spec 007's AC-21 deliberately passes payloads the engine will reject, but some
of those do not fail — they **throw**. `T4SlipParser.Parse` reaches
`input.Boxes.GroupBy(...)` at [T4SlipParser.cs:101](../../src/TaxLogicEngine/Services/T4SlipParser.cs:101)
with no null guard, and spec 007's `IsEmptyExtraction` only rejects a null
`Boxes` when *all four* identity fields are also blank. So
`{ Boxes: null, EmployerName: "Acme" }` clears the envelope gate and then raises
a `NullReferenceException`. An escaping exception is not fail-closed behaviour.
Stage 4 closes that gap **at the seam**, leaving spec 003's shipped parser
untouched (hardening the parser itself is an open Class A question the PRD
raises for the human, not a side effect of this spec).

## Data model

**No new JSON mapping config, no change to `Mappings/T4LineMap.2025.json`, no
new mapped box.** The only data-shaped additions are one result type and one
closed constant set.

### `ProcessedT4Extraction` (sealed class, `Models/ProcessedT4Extraction.cs`)

The success value. Constructor **internal**, so only the processor can create
one — its existence *is* the proof the whole pipeline succeeded, mirroring
`ValidatedExtraction`'s proof-by-construction from spec 007.

```csharp
public sealed class ProcessedT4Extraction
{
    internal ProcessedT4Extraction(
        string documentId, string firmId, string caseId, string participantId,
        int taxYear, string sourceReference, T4Slip slip, T4MappingResult mapping);

    [JsonIgnore] public string DocumentId { get; }
    [JsonIgnore] public string FirmId { get; }
    [JsonIgnore] public string CaseId { get; }
    [JsonIgnore] public string ParticipantId { get; }
    public int TaxYear { get; }
    [JsonIgnore] public string SourceReference { get; }
    [JsonIgnore] public T4Slip Slip { get; }
    [JsonIgnore] public T4MappingResult Mapping { get; }
    public override string ToString();   // fixed structural facts only
}
```

- **A sealed class, never a record.** `src/TaxLogicEngine/CLAUDE.md` warns that a
  record's compiler-generated `ToString()` prints every property; here that would
  print the taxpayer's name, address, and every amount. `ToString()` emits
  structural facts only — tax year, line-entry count, unmapped-box count,
  whether a SIN is present — and never a caller value, an identity string, an
  amount, or a SIN.
- **`Slip` and `Mapping` are `[JsonIgnore]`d too.** `T4Slip` and
  `T4MappingResult` are the engine's own types and carry identity text and
  amounts; a careless `JsonSerializer.Serialize(result)` must not become a
  taxpayer-data export. Callers reach them programmatically.
- **`TaxYear` is the one serializable property** — a four-digit year is a
  structural fact, not caller-controlled data.
- **The raw `T4RawInput` is deliberately absent** (PRD requirement 21). Once
  parsed, its role is over; keeping it would create a second, *unmasked* carrier
  of the Box 12 SIN beside the parser's masked `RecipientIdentifier`. Callers
  needing it still hold the `ValidatedExtraction`.

### `T4ProcessingFields` (static class, `Models/T4ProcessingFields.cs`)

The closed set of `ValidationError.Field` names this module may emit, declared
once so tests can assert the set mechanically:

`Extraction`, `Kind`, `Payload`, `PayloadBoxes`, `PayloadBoxCode`, `TaxYear`,
`SlipYear`.

Engine errors keep the engine's own field names — they are surfaced verbatim
(PRD requirement 10), so the closed-set assertion applies only to errors this
module originates.

## Services & public API

### `SupportedProcessingTaxYears` (static class, `Services/SupportedProcessingTaxYears.cs`)

```csharp
public static IReadOnlyList<int> All { get; }   // [2025]
public static bool IsSupported(int taxYear);
```

The module-local year policy, mirroring the `SupportedTaxYears` /
`SupportedExtractionTaxYears` pattern without referencing either. Widening a year
is a code change in exactly one place — a scope change, never a silent
widening. It deliberately does **not** read the system clock and does **not**
infer support by inspecting mapping resources.

### `PayloadStructureGuard` (internal static class, `Services/PayloadStructureGuard.cs`)

```csharp
internal static IReadOnlyList<ValidationError> Inspect(T4RawInput payload);
```

Stage 4. Returns one error per structural defect, in fixed order: `Boxes` null →
`PayloadBoxes`; any null element → `PayloadBoxes`; any null/blank `Code` →
`PayloadBoxCode`.

It **never reads a `T4RawBox.Value`**, never decides whether a code is known,
required, or meaningful, and never trims, normalizes, deduplicates, reorders, or
drops a box — duplicates preserved by spec 007 must still reach the parser so the
parser's own duplicate rejection fires (PRD requirements 6–7).

`internal` because it is a seam guard, not public API; the test project sees it
via `InternalsVisibleTo`. Keeping it out of the public surface means the module
exposes exactly one behaviour — `Process`.

### `T4CaseProcessor` (sealed class, `Services/T4CaseProcessor.cs`)

```csharp
public T4CaseProcessor(IT4LineMapProvider lineMapProvider);
public ParseResult<ProcessedT4Extraction> Process(ValidatedExtraction? extraction);
```

The single entry point, executing the eight stages above. Synchronous, pure, and
deterministic: the same extraction and the same provider always yield an equal
result.

- **`IT4LineMapProvider` is constructor-injected**, so the year requested is
  observable. A recording test double makes AC-35 ("asked for exactly the slip's
  year") and AC-36 ("a config for year N is never applied to a slip from N±1")
  mechanically testable rather than argued. `T4SlipParser` and `T4LineMapper` are
  constructed internally — they have no policy to vary, so injecting them would
  add surface without adding a testable guarantee.
- **`Process` takes a nullable parameter** and fails closed on null rather than
  throwing, matching `ExtractionEnvelopeValidator.Validate` and
  `TaxCase.AttachEvidence`. (`T4SlipParser.Parse` throws on null; that difference
  is deliberate and documented — this module is the untrusted-input boundary.)
- **Error messages are non-interpolated constants.** A template that cannot take
  an argument cannot leak a caller or payload value. The supported year is never
  spelled out in a message either, so the policy stays in one place.

### Domain rules from CLAUDE.md and where they land

| Rule | Where it lands |
|---|---|
| Per-tax-year, no adjacent-year fallback, fail closed | Stages 3, 6, 7; provider queried for exactly one year; no retry |
| Currency is `decimal`, half-up via `MoneyRounding` | **Not applicable** — this module performs no arithmetic. It is reused *indirectly* through parser and mapper; a boundary test forbids `Math.Round`, `MidpointRounding`, `double`, and `float` here |
| Box→line only via JSON config | Stage 7 delegates entirely to `T4LineMapper` + `IT4LineMapProvider`; a boundary test forbids any CRA line-number literal in the module |
| Never log or echo a full SIN | The raw payload is handled by **omission** (never rendered, serialized, or placed on the result); the parsed Box 12 by **masking**, via the engine's `RecipientIdentifier` only. No new SIN type, no second masking helper, no `RecipientIdentifier` constructed here |
| No magic strings; data-driven | Field names are `T4ProcessingFields` constants; no box code or line number appears in module code |

## Trade-offs

**1. A new `CaseProcessing` module vs. hosting this in `DocumentExtraction`.**
Hosting it in spec 007's module would avoid a project and read as "one ingestion
pipeline". Rejected: `src/DocumentExtraction/CLAUDE.md` explicitly forbids
`T4SlipParser`/`T4LineMapper` usage, CRA line logic, and mapping-resource reads
inside that module, and its whole guarantee is that extraction output is
*untrusted input, never tax truth*. Putting the thing that turns a payload into
CRA line entries inside the module whose contract is "this is not tax truth"
would destroy the boundary that makes spec 007 worth having. The extra project is
the cheaper price.

**2. A new module vs. extending `TaxLogicEngine`.**
The engine already owns the parser and mapper, so adding a coordinator there
needs no new project at all. Rejected: the coordinator's input is
`ValidatedExtraction`, a `DocumentExtraction` type, so TaxLogicEngine would have
to reference `DocumentExtraction` — reversing the dependency arrow, breaking
"dependencies point inward", and dragging provider-shaped concerns into the
deterministic tax core. `src/TaxLogicEngine/CLAUDE.md` forbids exactly this.

**3. Returning an attachment-ready result vs. attaching to the `TaxCase` here.**
Attaching directly would deliver a visibly complete story: extraction in, case
updated. Rejected: it requires a `TaxCases` reference and would duplicate
invariants that immutable aggregate already owns, and — more importantly —
attaching implies the participant belongs to the case and the caller is
authorized, neither of which this module can know. Spec 007 assigned membership
and authorization to the future application workflow; quietly taking them on here
would turn "the slip parsed" into an implied access decision. The result carries
attribution forward and verifies none of it.

**4. Guarding the payload at the seam vs. hardening `T4SlipParser`.**
Fixing the null-`Boxes` dereference in the parser would protect every caller, not
just this one, and is arguably where it belongs. Rejected *for this spec*:
`T4SlipParser` is shipped, spec-003-verified tax-domain code with 261 green
tests, and changing its behaviour is a tax-domain amendment to spec 003 requiring
its own review — not a side effect of spec 008. The PRD raises parser hardening
as a non-blocking Class A question for the human. Guarding at the seam is the
change that is fully within this spec's authority.

**5. Accumulating stage 3+4 errors vs. short-circuiting every stage.**
Short-circuiting throughout is simpler to implement and reason about. Rejected
for these two stages only: an unsupported year and a malformed box collection are
independent defects, and reporting one at a time forces an adapter author through
avoidable round trips. Every other stage genuinely depends on the previous one,
so those still short-circuit.

## Risks

**Correctness — a second, divergent pipeline.** The failure this feature exists
to prevent is call sites hand-wiring parser and mapper differently. *Mitigation:*
one entry point; AC-3 and AC-4 assert the processor's slip and mapping are
**equal to what the engine produces when invoked directly** on the same input,
so the processor cannot drift from the engine without a test failing.

**Correctness — tax-year leakage.** A 2025 slip mapped with another year's config
is a filing error. *Mitigation:* stage 3 (extraction year), stage 6 (slip/year
coherence), stage 7 querying exactly one year with no retry; AC-35 and AC-36 use a
recording provider to assert the exact set of years requested — the strongest
available evidence, since it observes the call rather than the outcome.

**Correctness — rounding drift.** Any arithmetic here would silently diverge from
`MoneyRounding`'s half-up behaviour. *Mitigation:* the module performs no
arithmetic; AC-27 asserts half-cent behaviour is the engine's unchanged, AC-28
asserts no arithmetic is performed, and a boundary test forbids `Math.Round`,
`MidpointRounding`, `double`, and `float` in module code.

**Security/PII — the omission→masking handover.** This is the exact moment an
opaque payload becomes typed taxpayer data, and it is the highest-risk point in
the feature. *Mitigation:* the raw payload never reaches the result (requirement
21) and is never rendered or serialized; the only SIN representation is the
engine's masked `RecipientIdentifier`; every caller string and both engine
objects are `[JsonIgnore]`d; `ToString()` is hand-written and structural;
messages are non-interpolated constants. The PII suite plants SIN-like and
non-numeric marker strings and asserts absence from every `Field`, `Message`,
`ToString()`, and JSON output on both success and every failure path — with a
reachability guard so a mis-wired plant slot fails loudly rather than passing
vacuously.

**CRA compliance — inventing a tax judgement.** The tempting mistakes are
rejecting an unmapped box or "helpfully" normalizing a box value. *Mitigation:*
requirement 15 surfaces `UnmappedBoxCodes` unchanged and never rejects on it;
the stage-4 guard is forbidden from reading any `Value`; boundary tests forbid
CRA line literals and mapping-resource reads. No CRA fact is asserted by this
spec.

**Boundary erosion — the module growing into an orchestrator.** A coordinator is
where persistence, retries, and authorization accumulate. *Mitigation:* the
out-of-scope list is explicit, and boundary tests forbid `TaxCases` references,
reverse references, clock, filesystem, network, database, environment, OCR/LLM,
cloud, email, and generated identifiers.

**Known upstream blocker (not caused by this feature).** `T4SlipParser` requires
Box 54, which `T4Slip`'s own docs note is absent from the employee copy — spec
003's `[CONFIRM]`-flagged decision. Every OCR'd employee-copy T4 therefore fails
closed here (AC-17). *Mitigation:* AC-17 pins the behaviour so it is visible
rather than surprising, and the PRD raises it as a Class A item for the human. If
the intended flow is "accountant scans the client's copy", **spec 003 needs
amending before this pipeline is production-usable** — that decision is out of
this spec's authority.

## Note on PRD requirement 28

Requirement 28 says "every public type in this module SHALL be a sealed class
with an explicit `ToString()`". Read as intended, that governs **data-carrying**
public types — here, `ProcessedT4Extraction`. `T4ProcessingFields` and
`SupportedProcessingTaxYears` are public *static* classes holding constants and a
policy predicate; they carry no instance data and cannot leak a value through
`ToString()`. Spec 007 shipped exactly this shape (`ExtractionFields`,
`SupportedExtractionTaxYears`) under identically worded rules. **`[ASSUMED]`**,
flagged for one-pass override.
