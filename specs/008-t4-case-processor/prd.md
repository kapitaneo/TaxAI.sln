# PRD 008 — T4 Case Processor

## Feature classification

**Class: `tax-domain`.**

The T4 Case Processor is the point at which **untrusted extraction output becomes
tax truth**. It takes a spec 007 `ValidatedExtraction` (which proves envelope
well-formedness and attribution *presence* only, and whose `T4RawInput` payload
is still explicitly untrusted, possibly containing an unmasked SIN in Box 12),
drives it through the existing `T4SlipParser` and `T4LineMapper`, and produces a
typed `T4Slip` plus CRA-line `TaxLineEntry` items attributed to a firm, case,
participant, and tax year.

It is `tax-domain`, not `platform`, because:

- it produces **CRA line entries** that a real return is drafted from;
- it enforces **tax-year isolation** (2025 only, no adjacent-year fallback) at a
  new seam;
- it handles **taxpayer PII**, and sits exactly on the seam where spec 007's
  PII-by-**omission** discipline hands over to TaxLogicEngine's PII-by-**masking**
  discipline (`RecipientIdentifier`);
- it defines **new fail-closed rejection rules** for structurally malformed
  untrusted payloads that spec 007 deliberately lets through (its AC-21).

Even the parts that look like plumbing (module home, injection shape) mix with
tax semantics, and the classification rule is explicit: a mixed feature is
`tax-domain`, and an uncertain feature is `tax-domain`. Every tax-domain
protection therefore applies as written — explicit tax-year scope, canada.ca as
the only tax source, one negative acceptance criterion per fail-closed rule, a
PII criterion, a tax-year-isolation criterion, and a data-driven-mapping
criterion.

This PRD deliberately asserts **no new CRA fact** — no box number, line number,
rate, threshold, limit, deadline, gross-up, credit percentage, or eligibility
rule. **No CRA line number appears anywhere in this document**, by design: every
mapping value stays in `TaxLogicEngine/Mappings/T4LineMap.2025.json`, verified in
spec 004 (see `## CRA sources`). No blocking Class A question remains (see Open
questions).

---

## Problem

**Tax year in scope: 2025 only.**

TaxAI now has three modules that each do their job and stop at their boundary:

- **`TaxLogicEngine`** (specs 001–004) can parse a `T4RawInput` into a validated
  `T4Slip` and map that slip's boxes to CRA lines via `Mappings/T4LineMap.2025.json`.
- **`DocumentExtraction`** (spec 007) can validate an untrusted
  `ExtractionEnvelope` and hand back a `ValidatedExtraction` carrying the raw
  `T4RawInput` **reference-identical and value-unchanged**, plus its firm / case /
  participant / tax-year attribution.
- **`TaxCases`** (spec 006) can record *that* evidence of a category was received
  against a participant, case, firm, and tax year — and explicitly "carries no CRA
  line number and no box→line logic" (spec 006 PRD, out-of-scope).

Nothing joins them. Concretely, today:

1. **There is no owner of the extraction→engine step.** Spec 007's architecture
   names the "T4 Case Processor (future)" as the component that "takes the raw
   payload from a success and performs the actual parse … and box→line mapping",
   and spec 006 names the same future spec as the place slip parsing happens. That
   component does not exist, so an integrator has to wire `T4SlipParser` and
   `T4LineMapper` by hand at every call site — which is how two subtly different
   pipelines get built.
2. **There is a real, unguarded fail-open gap between the two modules.** Spec 007
   validates envelope shape only and states (its AC-21) that a payload the engine
   would reject still passes the envelope gate. That is correct for spec 007 — but
   some of those payloads do not merely *fail* in the engine, they **throw**. A
   `T4RawInput` whose `Boxes` collection is null (trivially produced by
   deserializing `"Boxes": null` from an untrusted provider adapter) passes the
   spec 007 gate whenever any identity text is present, and then
   `T4SlipParser.Parse` dereferences `input.Boxes` and raises a
   `NullReferenceException`. The same is true of a null element inside `Boxes`, and
   of a `T4RawBox` whose `Code` is null. An exception escaping instead of a
   structured `ParseResult` failure is not fail-closed behaviour. Someone must own
   that seam; that owner is this feature.
3. **There is no attributed, review-ready result.** Even after a successful parse
   and mapping, nothing binds the `T4Slip` and `T4MappingResult` back to the firm,
   case, participant, and tax year the extraction was attributed to, so the
   accountant-facing steps that follow have nothing to consume.

The risk without this feature is the one CLAUDE.md exists to prevent: extraction
output reaching a return through an ad-hoc, per-call-site pipeline that skips a
validation step, mixes tax years, throws instead of failing closed, or leaks a
SIN at the moment the payload stops being opaque.

**Tax-year scope.** Every collaborating component is 2025-only and says so in
exactly one place: `SupportedTaxYears` (spec 006), `SupportedExtractionTaxYears`
(spec 007), `T4SlipParser`'s expected year, and the single embedded
`Mappings/T4LineMap.2025.json`. This feature adds one more module-local policy
(2025 only) and **fails closed** on anything else: no "current" year, no
adjacent-year fallback, no coercion, no system-clock read, and no inference of
supported years by inspecting mapping resources. A slip whose year has no
mapping config fails closed with the provider's errors and is never retried
against another year.

---

## Current state and reuse analysis

Grounded in the actual tree at
`D:\Projects\TaxAI\.claude\worktrees\jolly-dhawan-458716`, not memory. A grep of
`specs/*/prd.md` and `src/` shows the T4 Case Processor is named as *future work*
in five places (spec 006 PRD, spec 007 PRD, spec 007 architecture,
`ValidatedExtraction`'s XML docs, `NEXT.md`) and **implemented nowhere**. There
is no processor, orchestrator, or pipeline type in any module. The solution today
holds exactly `src/TaxLogicEngine`, `src/TaxCases`, `src/DocumentExtraction` and
their three test projects.

### Reused unchanged (do NOT re-specify, duplicate, modify, or wrap)

| Existing type | Where | Role in this feature |
|---|---|---|
| `ValidatedExtraction` | `src/DocumentExtraction/Models/` | The **only** input. Its constructor is `internal` to DocumentExtraction, so this module cannot fabricate one — possessing one *is* the proof the spec 007 gate was passed. |
| `ExtractedDocumentKind` | `src/DocumentExtraction/Models/` | The kind discriminator this feature gates on (`T4Slip` only). It has **no T5 value**; T5 is deferred. |
| `T4RawInput` / `T4RawBox` | `src/TaxLogicEngine/Models/` | The untrusted payload, taken from the `ValidatedExtraction` and handed to the parser. Never re-modelled, never copied, never normalized. |
| `T4SlipParser` | `src/TaxLogicEngine/Services/` | The **sole** authority on T4 box validity, required boxes, amount typing, constrained codes, companion boxes, and Box 12 → `RecipientIdentifier`. Invoked, never re-implemented, never bypassed, never adjusted. |
| `T4Slip` | `src/TaxLogicEngine/Models/` | The parser's output, surfaced on the result. Never constructed by this module. |
| `T4LineMapper` | `src/TaxLogicEngine/Services/` | The **sole** authority on box→CRA-line mapping and line aggregation. Invoked with the single parsed slip. |
| `IT4LineMapProvider` / `EmbeddedT4LineMapProvider` | `src/TaxLogicEngine/Services/` | The year-scoped mapping-config source. Injected, so year isolation is mechanically testable. |
| `T4MappingResult`, `TaxLineEntry` | `src/TaxLogicEngine/Models/` | The mapper's output, surfaced on the result verbatim — including `UnmappedBoxCodes`. |
| `RecipientIdentifier` | `src/TaxLogicEngine/Models/` | The **only** SIN representation in this feature, reached only via `T4Slip.Sin` as produced by the parser. Masks to the last 3 digits. |
| `MoneyRounding` | `src/TaxLogicEngine/Services/` | Reused **indirectly only**, through the parser and mapper. This module performs no arithmetic and never calls it. |
| `ParseResult<T>` | `src/TaxLogicEngine/Models/` | The fail-closed result of processing. Success carries a value and no errors; failure ≥1 error and no value; no partial state. |
| `ValidationError` | `src/TaxLogicEngine/Models/` | Every rejection reason, including engine errors surfaced verbatim. |

### Relied on but explicitly NOT consumed, created, mutated, or referenced

- **`TaxCases` — the whole module.** This feature does **not** reference the
  `TaxCases` project, does not construct an `EvidenceDescriptor`, does not call
  `TaxCase.AttachEvidence`, does not compute readiness, and does not add, rename,
  or change any TaxCases public type. It produces an *attachment-ready* result;
  building the descriptor and attaching it (with the membership and authorization
  checks spec 007 assigned to the future application workflow) stays downstream.
  `TaxCase` is immutable and owns its own contract; reaching into it from here
  would duplicate its invariants.
- **`SafeDiagnosticText`** (`src/TaxCases/Services/`) — not reused, not
  referenced, not reimplemented. SIN safety here comes from `RecipientIdentifier`
  (masking, for the parsed slip) and from omission (for the raw payload). No
  second masking helper is introduced.
- **`ExtractionEnvelopeValidator` / `IDocumentExtractionIngress`** — not
  re-specified. This feature runs *after* the gate; it does not re-validate the
  envelope, re-check attribution presence, or implement the ingress port.
- **All T5 types** (`T5RawInput`, `T5SlipParser`, `T5LineMapper`, T5 configs) —
  unchanged and **not consumed**. T5 remains deferred (spec 007's approved MVP
  scope); `ExtractedDocumentKind` has no T5 value, so no T5 extraction can exist
  to process.

### Genuinely new (introduced by this feature)

A small module owning exactly one job — *drive one validated T4 extraction
through the existing engine and return an attributed, fail-closed result*:

1. A **payload structural pre-check** — the guard that closes the fail-open gap
   in Problem §2. It verifies only that the untrusted payload is structurally
   well-formed enough for the parser to be invoked without throwing (`Boxes`
   present, no null entry, every `Code` non-blank). It makes **no judgement about
   which box codes exist, what they mean, or whether a value is valid** — all of
   that stays with `T4SlipParser`.
2. A **processor service** that gates on kind, applies the year policy, invokes
   the parser, then (only on parser success) invokes the mapper, and fails closed
   at every step.
3. An **attributed processed-result type** carrying the extraction's firm, case,
   participant, tax year, document id, and source reference alongside the engine's
   `T4Slip` and `T4MappingResult`.
4. A **module-local supported-tax-year policy** (2025 only) and a **closed set of
   fixed field-name constants** for this module's own `ValidationError.Field`
   values.

Concrete type names, the module/project home, and the injection shape are Class B
`[ASSUMED]` (see Open questions). Dependencies point inward and are limited to
`DocumentExtraction` and `TaxLogicEngine`.

---

## User stories

1. As an **integration engineer**, I want one declared component that takes a
   `ValidatedExtraction` and returns either a parsed-and-mapped result or
   structured errors, so that no call site ever hand-wires `T4SlipParser` and
   `T4LineMapper` into a second, subtly different pipeline.
2. As an **engine maintainer**, I want a structurally malformed untrusted payload
   that spec 007 deliberately let through to produce a **structured failure, never
   an exception**, so the seam between the extraction gate and the parser is
   fail-closed rather than fail-loud.
3. As an **accountant**, I want a T4 the OCR adapter got wrong — a duplicated box,
   a garbled amount, a missing required box — to be **rejected with the engine's
   own reasons**, so a defective slip never silently becomes half a return.
4. As a **compliance reviewer**, I want the mapping for a 2025 slip to be produced
   only from the 2025 configuration, with a missing-config year failing closed and
   never falling back to an adjacent year, so a wrong year's rules can never reach
   a taxpayer's return.
5. As a **compliance reviewer**, I want the full Box 12 SIN to be unreachable in
   every rendered, logged, serialized, or error output of this module — visible
   only as the engine's masked `RecipientIdentifier` — so the moment the payload
   stops being opaque is not the moment a SIN leaks.
6. As the **Accountant Review Package** author, I want the result to carry the
   firm, case, participant, and tax year alongside the slip and its CRA line
   entries, so I can present and attach it without re-deriving attribution.
7. As an **engine maintainer**, I want this module to contain no CRA line number,
   no box→line table, and no arithmetic, so the single source of mapping truth
   stays `Mappings/T4LineMap.2025.json`.
8. As a **firm administrator**, I want it stated plainly that a processed result
   is **not** proof that the participant belongs to the case or that the caller was
   authorized, so nobody mistakes a successful parse for an access decision.
9. As an **engine maintainer**, I want processing to be deterministic — no clock,
   network, filesystem, database, OCR, or LLM — so the same extraction always
   yields the same result.

---

## Functional requirements

`[reuse]` marks reuse of an existing type. `[design]` marks a decision that
depends on no CRA fact. `[boundary]` marks a module-boundary invariant from
CLAUDE.md. **No requirement below asserts a CRA fact.**

### Input and gating

1. **[reuse]** The processor SHALL accept exactly one `ValidatedExtraction` per
   call and SHALL NOT accept a raw `ExtractionEnvelope`, a raw `T4RawInput`, or
   any other unvalidated input. Because `ValidatedExtraction`'s constructor is
   internal to `DocumentExtraction`, the only way to obtain one is through the
   spec 007 gate; this module SHALL NOT re-implement, duplicate, or bypass that
   gate.
2. **[design]** A **null** extraction SHALL fail closed with a structured error
   and SHALL NOT throw.
3. **[design]** The processor SHALL process only an extraction whose `Kind` is
   `ExtractedDocumentKind.T4Slip` **and** whose `T4Payload` is non-null. Any other
   kind — today only `GenericEvidence`, which by spec 007's rules never carries a
   payload — SHALL fail closed with a structured error naming the kind field. The
   payload-non-null half of this rule is defence in depth: spec 007's validator
   makes a `T4Slip` extraction without a payload unconstructible.
4. **[design]** T5 extractions SHALL remain impossible to submit: this module
   consumes no T5 type and `ExtractedDocumentKind` has no T5 value. Adding T5 is a
   future scope change, not a widening of this spec.

### Payload structural pre-check (fail closed, no tax judgement)

5. **[design]** Before invoking the parser, the processor SHALL verify that the
   payload is **structurally traversable**:
   - `T4RawInput.Boxes` is not null;
   - no element of `Boxes` is null;
   - every `T4RawBox.Code` is non-null and non-blank.

   Each violation SHALL fail closed with a structured error and SHALL NOT throw.
   This check exists solely because spec 007 (its AC-21) deliberately passes such
   payloads through and `T4SlipParser` dereferences these members.
6. **[design]** The pre-check SHALL make **no judgement about tax content**: it
   SHALL NOT decide whether a box code is known, whether a box is required,
   whether a value is well-formed, or what any box means. It SHALL NOT read any
   `T4RawBox.Value`. All of that remains `T4SlipParser`'s responsibility.
7. **[design]** The pre-check SHALL NOT modify the payload: it SHALL NOT trim,
   normalize, deduplicate, reorder, or drop a box. Duplicate box codes preserved
   by spec 007 SHALL still reach the parser so the parser's existing duplicate-box
   rejection fires.

### Parsing (delegated in full)

8. **[reuse]** The processor SHALL obtain the typed slip **only** by invoking the
   existing `T4SlipParser` on the payload. It SHALL NOT construct a `T4Slip`, SHALL
   NOT type or coerce a box value, SHALL NOT convert Box 12 into a
   `RecipientIdentifier` itself, and SHALL NOT add, relax, or reorder any parser
   rule.
9. **[design]** On parser failure the processor SHALL fail closed, returning the
   parser's `ValidationError`s **verbatim and in order**, with no value, no partial
   result, and no mapping attempted (the mapping provider SHALL NOT be queried).
10. **[design]** The processor SHALL NOT translate, summarize, re-word, or filter
    engine errors. What the engine rejected, and why, reaches the caller unchanged.

### Mapping (delegated in full)

11. **[reuse]** The processor SHALL obtain CRA line entries **only** by invoking
    the existing `T4LineMapper` with the single parsed slip. It SHALL NOT
    aggregate, re-aggregate, split, filter, re-order, or recompute any
    `TaxLineEntry`, and SHALL NOT perform any arithmetic or rounding of its own.
12. **[boundary]** The module SHALL contain **no CRA line number literal**, no
    box→line table, and no read of a `TaxLogicEngine.Mappings` resource. Mapping
    values come exclusively from `Mappings/T4LineMap.2025.json` through
    `IT4LineMapProvider`.
13. **[design]** The mapping provider SHALL be supplied by construction
    (`IT4LineMapProvider`), so the year requested is observable and testable and
    no year is ever silently substituted.
14. **[design]** On mapper or provider failure — including "no configuration for
    this tax year" — the processor SHALL fail closed with those errors verbatim,
    produce no value, and SHALL NOT retry with any other year.
15. **[design]** `T4MappingResult.UnmappedBoxCodes` SHALL be surfaced on the
    result **unchanged and unfiltered**, and SHALL NOT cause rejection. Spec 004
    deliberately routes several boxes there with recorded canada.ca rationale;
    inventing a rejection rule for them here would be a new tax judgement this
    feature is not entitled to make. Deciding what a preparer must *do* about an
    unmapped box is the Accountant Review Package's concern.

### Tax-year isolation

16. **[design]** The module SHALL declare its supported tax years in exactly one
    module-local place: **2025 only**. There is no "current" year, no
    adjacent-year fallback, no coercion, no system-clock read, and no inference of
    year support by inspecting mapping resources.
17. **[design]** An extraction whose `TaxYear` is not a supported year SHALL fail
    closed. (Spec 007 already guarantees 2025 for any constructible
    `ValidatedExtraction`; this is defence in depth so the module remains
    fail-closed if that policy ever diverges.)
18. **[design]** The processor SHALL verify that the parsed `T4Slip.Year` equals
    the extraction's `TaxYear` and SHALL fail closed on any mismatch. Neither value
    is ever coerced.
19. **[design]** The mapping provider SHALL be queried for **exactly one year —
    the parsed slip's year — and no other**, on any code path.

### Output

20. **[design]** On success the processor SHALL return a single processed-result
    value carrying: the extraction's `DocumentId`, `FirmId`, `CaseId`,
    `ParticipantId`, `TaxYear`, and `SourceReference`, echoed **exactly as
    supplied**; the parsed `T4Slip`; and the `T4MappingResult`. Nothing is
    generated, defaulted, or invented.
21. **[design]** The processed result SHALL NOT expose the raw `T4RawInput`. Once
    the payload has been parsed its role is over, and retaining it on the result
    would create a second, unmasked carrier of the Box 12 SIN alongside the
    parser's masked `RecipientIdentifier`. Callers that need the raw payload
    already hold the `ValidatedExtraction`.
22. **[design]** A processed result SHALL NOT be presented as proof of firm/case/
    participant membership or of caller authorization. It carries attribution
    forward; it verifies none of it. Those checks belong to the future application
    workflow, exactly as spec 007 declared.
23. **[reuse]** Processing SHALL return `ParseResult<T>`: success carries a value
    and zero errors; failure carries ≥1 `ValidationError` and no value; nothing is
    partially accepted and nothing is mutated.

### PII — the omission/masking seam

24. **[design]** The raw `T4RawInput` (which may contain an unmasked SIN in
    Box 12) SHALL continue to be handled by **omission** while it is in this
    module: it is never rendered, never serialized, never written to a
    `ValidationError` message, and never placed on the result (requirement 21).
25. **[design]** The parsed slip's Box 12 SHALL be handled by **masking**, using
    only the engine's existing `RecipientIdentifier` (last 3 digits visible). This
    module SHALL introduce **no new SIN property, no new SIN type, and no second
    masking helper**, and SHALL NOT construct a `RecipientIdentifier` itself.
26. **[design]** No full SIN SHALL appear in any output of this module — no
    `ToString()`, no diagnostic, no exception message, no `ValidationError`, and no
    supported direct JSON serialization.
27. **[design]** This module's own `ValidationError`s SHALL use **fixed field
    names** from a closed constant set and **fixed message templates**, and SHALL
    NEVER echo a payload value or a caller-controlled identifier (document id,
    firm/case/participant reference, source reference).
28. **[design]** Every public type in this module SHALL be a sealed class with an
    explicit `ToString()` emitting **fixed structural facts only** (never a record,
    whose compiler-generated `ToString()` prints every property — the leak
    `src/TaxLogicEngine/CLAUDE.md` warns about). Every caller-controlled string on
    the result SHALL be `JsonIgnore`d, and the `T4Slip` and `T4MappingResult`
    properties SHALL be `JsonIgnore`d as well, so a careless direct serialization
    of the result cannot emit taxpayer identity text or amounts.

### Boundary, determinism, dependency direction

29. **[boundary]** Production dependencies SHALL be exactly `DocumentExtraction`
    and `TaxLogicEngine`, pointing inward. The module SHALL NOT reference
    `TaxCases`, ASP.NET Core, EF Core, SQL, OCR/LLM/cloud/email SDKs, the
    filesystem, the network, the system clock, the environment, or the current
    user. No existing module SHALL reference this one.
30. **[boundary]** The module SHALL NOT mutate a `TaxCase`, attach evidence,
    compute readiness, persist anything, generate an identifier, or emit a domain
    change record.
31. **[design]** Processing SHALL be deterministic: the same `ValidatedExtraction`
    and the same provider SHALL always yield an equal result (same success/failure,
    same errors in the same order, same slip, same line entries).
32. **[boundary]** No existing public type in `TaxLogicEngine`, `TaxCases`, or
    `DocumentExtraction` SHALL be added, renamed, or changed by this feature, and
    the full existing test suite SHALL remain green.

---

## Out of scope

Each item is explicitly excluded:

- **Attaching the result to a `TaxCase`** — no `TaxCases` project reference, no
  `EvidenceDescriptor` construction, no `AttachEvidence` call, no readiness
  computation, no change record. The result is attachment-*ready*; attaching is
  the future application workflow's job.
- **Verifying firm/case/participant membership or authenticating a caller** —
  spec 007 assigned this to the future application workflow and it stays there.
- **T5 processing** — deferred with spec 007's T5 extraction deferral. No T5 type
  is consumed; `ExtractedDocumentKind` has no T5 value.
- **Generic-evidence processing** — a `GenericEvidence` extraction carries no
  payload and nothing to parse; it fails closed here. Interpreting evidence
  documents (medical or otherwise) is a separate, CRA-verified future spec.
- **Multi-slip aggregation** — one extraction, one slip, one call. Combining
  several slips (or several participants' slips) onto shared return lines is
  return assembly, not this feature. `T4LineMapper`'s existing multi-slip
  behaviour is unchanged and simply not exercised with more than one slip here.
- **Any change to `T4SlipParser`'s or `T4LineMapper`'s rules** — including the
  required-box set, the province/employment/dental code sets, the companion-box
  rules, and the deliberate `UnmappedBoxCodes` deferrals of spec 004. Changing any
  of them is a tax-domain amendment to specs 003/004, not a side effect of this
  one.
- **Any new box→line mapping, new mapped box, or change to
  `Mappings/T4LineMap.2025.json`** — none.
- **Deciding what a preparer must do about an unmapped box, a low-confidence
  extraction, or a rejected slip** — no review flag, no queue, no
  needs-human-review state. That is the Accountant Review Package.
- **T1 calculation of any kind** — no taxable income, no credits, no CPP/EI
  reconciliation, no Schedule 8 / RC381 handling.
- **Persistence, queuing, retries, batching, deduplication, logging
  infrastructure.**
- **Any provider adapter, OCR/LLM call, or implementation of
  `IDocumentExtractionIngress`.**
- **Database, API host, or frontend of any kind.**
- **NETFILE / EFILE / CRA submission.**
- **Any CRA box, line, rate, threshold, limit, deadline, gross-up, credit
  percentage, or eligibility rule** — none is asserted; all are reused unchanged
  from specs 003–004.
- **Tax years other than 2025.**
- **Any accidental change to existing `TaxLogicEngine`, `TaxCases`, or
  `DocumentExtraction` behaviour** — the full suite must stay green (AC-43).

---

## Acceptance criteria

Each criterion is a single mechanically-checkable test (Given/When/Then).

Throughout, *"a valid 2025 T4 extraction"* means a `ValidatedExtraction` obtained
by running a well-formed 2025 T4 `ExtractionEnvelope` through
`ExtractionEnvelopeValidator.Validate`, whose payload the existing `T4SlipParser`
accepts.

### Happy path and pass-through

1. **Happy path.**
   Given a valid 2025 T4 extraction and the embedded 2025 mapping provider,
   When it is processed, Then the result is a success with zero errors carrying a
   parsed `T4Slip` and a `T4MappingResult`.

2. **End-to-end from an envelope.**
   Given a well-formed 2025 T4 `ExtractionEnvelope`, When it is validated by
   `ExtractionEnvelopeValidator.Validate` and the resulting `ValidatedExtraction`
   is processed, Then the processing succeeds and no step between the two was
   hand-written.

3. **The parsed slip is exactly the engine's.**
   Given a valid 2025 T4 extraction, When it is processed and, separately,
   `new T4SlipParser().Parse(payload)` is called on the same payload, Then the
   processor's slip is value-equal to the parser's slip property for property.

4. **The mapping is exactly the engine's.**
   Given a valid 2025 T4 extraction, When it is processed and, separately,
   `new T4LineMapper(provider).Map(new[]{ parsedSlip })` is called with the same
   provider, Then the processor's `T4MappingResult` equals the mapper's — same
   line entries in the same order with the same amounts and the same source box
   codes. (The test names no CRA line number; it compares against the engine.)

5. **Attribution is echoed exactly.**
   Given a valid 2025 T4 extraction whose `DocumentId`, `FirmId`, `CaseId`,
   `ParticipantId`, and `SourceReference` are distinctive marker strings,
   When it is processed, Then the result carries each of those five values and the
   tax year ordinally identical to the extraction's, with nothing trimmed,
   normalized, or generated.

6. **Unmapped box codes pass through and never reject.**
   Given a valid 2025 T4 extraction whose payload populates a box that the 2025
   config does not map, When it is processed, Then processing **succeeds** and the
   result's `UnmappedBoxCodes` is sequence-equal to the mapper's own
   `UnmappedBoxCodes` for the same slip.

7. **Determinism.**
   Given the same `ValidatedExtraction` and the same provider, When processing runs
   twice, Then the two results are equal — same success/failure, same errors in the
   same order, same slip values, same line entries.

8. **Result year coherence.**
   Given any successful processing, When the result is inspected, Then its
   `TaxYear` equals the extraction's `TaxYear` and equals the parsed
   `T4Slip.Year`.

### Fail-closed rejections (one rule per criterion)

9. **Null extraction.**
   Given a null `ValidatedExtraction`, When processed, Then processing fails closed
   with exactly one structured error, returns no value, and throws no exception.

10. **Non-T4 extraction kind.**
    Given a `ValidatedExtraction` of kind `GenericEvidence` (obtained from a valid
    generic-evidence envelope), When processed, Then processing fails closed with a
    structured error naming the kind field, returns no value, and the mapping
    provider is never queried.

11. **Null `Boxes` collection.**
    Given a 2025 T4 extraction whose payload has `Boxes` set to null and non-blank
    identity text (so it passes the spec 007 gate), When processed, Then processing
    fails closed with a structured error naming the payload-boxes field and
    **throws no exception**.

12. **Null element inside `Boxes`.**
    Given a 2025 T4 extraction whose payload's `Boxes` contains a null element,
    When processed, Then processing fails closed with a structured error and throws
    no exception.

13. **Blank box code.**
    Given a 2025 T4 extraction whose payload's `Boxes` contains a `T4RawBox` whose
    `Code` is null (and, separately, empty, and whitespace-only), When each is
    processed, Then each fails closed with a structured error and throws no
    exception.

14. **Parser rejection — duplicate box code.**
    Given a 2025 T4 extraction whose payload repeats the same box code twice (spec
    007 preserves duplicates by design), When processed, Then processing fails
    closed carrying the parser's duplicate-box error verbatim and no value.

15. **Parser rejection — missing Box 12.**
    Given an otherwise-parseable 2025 T4 payload with no Box 12 value, When
    processed, Then processing fails closed carrying the parser's Box 12 error
    verbatim.

16. **Parser rejection — missing Box 45.**
    Given an otherwise-parseable 2025 T4 payload with no Box 45 value, When
    processed, Then processing fails closed carrying the parser's Box 45 error
    verbatim.

17. **Parser rejection — missing Box 54.**
    Given an otherwise-parseable 2025 T4 payload with no Box 54 value, When
    processed, Then processing fails closed carrying the parser's Box 54 error
    verbatim. (Box 54's required status is spec 003's recorded, `[CONFIRM]`-flagged
    decision; this feature surfaces it, it does not change it — see Open
    questions.)

18. **Parser rejection — malformed amount.**
    Given an otherwise-parseable 2025 T4 payload whose Box 14 value is
    `"not-a-number"`, When processed, Then processing fails closed carrying the
    parser's non-numeric-amount error verbatim.

19. **Parser rejection — negative amount.**
    Given an otherwise-parseable 2025 T4 payload whose Box 14 value is `"-1.00"`,
    When processed, Then processing fails closed carrying the parser's
    negative-amount error verbatim.

20. **Parser rejection — constrained code out of range.**
    Given an otherwise-parseable 2025 T4 payload whose Box 10 value is not one of
    the parser's accepted province codes, When processed, Then processing fails
    closed carrying the parser's Box 10 error verbatim.

21. **Parser rejection — missing companion box.**
    Given an otherwise-parseable 2025 T4 payload that populates a trigger box
    without its parser-required companion box, When processed, Then processing
    fails closed carrying the parser's companion-box error verbatim.

22. **No mapping config for the slip's year.**
    Given a valid 2025 T4 extraction and an `IT4LineMapProvider` stub that returns
    a failure for 2025, When processed, Then processing fails closed carrying the
    provider's errors verbatim, returns no value, and the provider is not asked for
    any other year.

23. **A parser rejection stops the pipeline.**
    Given any payload the parser rejects and a recording `IT4LineMapProvider` stub,
    When processed, Then the stub is never invoked and the result carries only the
    parser's errors.

### Boundaries

24. **Empty slip (no boxes at all).**
    Given a 2025 T4 extraction whose payload has an empty `Boxes` list but
    non-blank identity text (so it passes the spec 007 gate), When processed, Then
    processing fails closed carrying the parser's required-box errors and no value.

25. **Zero amounts.**
    Given a valid 2025 T4 extraction whose Box 14 value is `"0"`, When processed,
    Then processing succeeds and the resulting amounts are value-equal to the
    engine's own output for the same slip.

26. **Absent optional boxes.**
    Given a valid 2025 T4 extraction carrying only the boxes the parser requires,
    When processed, Then processing succeeds and the result equals the engine's
    output for the same payload.

27. **Half-cent boundary is the engine's, unchanged.**
    Given a valid 2025 T4 extraction whose Box 14 value is `"100.005"` (and,
    separately, `"100.015"`), When processed, Then each resulting amount is exactly
    equal to `MoneyRounding.ToCents` half-up / `MidpointRounding.AwayFromZero`
    applied by the engine — i.e. `100.01` and `100.02` — proving this module
    neither re-rounds nor alters midpoint behaviour. (The negative-sign direction
    is unreachable here because the parser rejects negative amounts outright —
    AC-19 — so no negative amount can reach any rounding path in this pipeline.)

28. **The module performs no arithmetic.**
    Given the module's source and compiled assembly, When inspected, Then it
    contains no call to `MoneyRounding`, no `Math.Round`, and no arithmetic
    operation on any monetary value.

### PII

29. **No full SIN in any successful output.**
    Given a valid 2025 T4 extraction whose Box 12 value is `123456789` and whose
    identity text fields are populated, When it is processed and the result's
    `ToString()`, the slip's `ToString()`, every `TaxLineEntry`, and every supported
    direct JSON serialization of the result are produced, Then none contains the
    string `123456789` or any 9-consecutive-digit substring, and the SIN is
    reachable only as the masked `RecipientIdentifier` (`***-**-789`).

30. **No full SIN or caller value in any failure output.**
    Given a payload with `123456789` in Box 12 that the pipeline rejects, and
    caller references containing distinctive marker strings, When every resulting
    `ValidationError` is read, Then no message contains `123456789`, any
    9-consecutive-digit substring, any payload value, or any caller-supplied
    identifier.

31. **One SIN representation only.**
    Given the compiled module, When its public surface and emitted metadata are
    inspected, Then it declares no SIN property or SIN type, contains no
    construction of `RecipientIdentifier`, and contains no masking or redaction
    helper of its own.

32. **The raw payload never reaches the result.**
    Given a successful processing, When the result's public surface is inspected,
    Then no member exposes a `T4RawInput` or `T4RawBox`, and no output of the
    module renders or serializes the raw payload.

33. **Fixed field names and caller-string omission.**
    Given every `ValidationError` this module itself produces (as opposed to those
    surfaced from the engine), When inspected, Then each `Field` is one of the
    module's closed constant set; and given a successful result, its `ToString()`
    contains none of the caller-supplied `DocumentId`, `FirmId`, `CaseId`,
    `ParticipantId`, or `SourceReference` values, each of which is `JsonIgnore`d.

### Tax-year isolation

34. **The year policy is 2025 and lives in one place.**
    Given the module's supported-tax-year policy, When inspected, Then its
    supported set is exactly `[2025]`, and it reports 2024 and 2026 as
    unsupported.

35. **The provider is asked for exactly the slip's year.**
    Given a valid 2025 T4 extraction and a recording `IT4LineMapProvider` stub,
    When processed, Then the stub was asked for year 2025 exactly once and for no
    other year.

36. **A config for year N is never applied to a slip from year N±1.**
    Given a 2025 T4 extraction and an `IT4LineMapProvider` stub that succeeds only
    for 2024 and fails for every other year, When processed, Then processing fails
    closed, no `T4MappingResult` is produced, and the 2024 map is never used.

37. **Off-year extractions never reach the processor.**
    Given a 2024 T4 `ExtractionEnvelope` and, separately, a 2026 one, When each is
    validated by `ExtractionEnvelopeValidator.Validate` and the pipeline is run,
    Then validation fails closed, no `ValidatedExtraction` exists, no processed
    result is produced, and neither year is coerced to 2025.

### Data-driven mapping / no hardcoded CRA facts

38. **No CRA line number is hardcoded.**
    Given the module's source and compiled assembly, When inspected, Then they
    contain no CRA line-number literal, no box→line table or dictionary, and no
    reference to a `TaxLogicEngine.Mappings.*` resource name — every line number in
    a result originates from `Mappings/T4LineMap.2025.json` via
    `IT4LineMapProvider`.

39. **No box value is interpreted and no slip is constructed here.**
    Given the module's source, When inspected, Then it contains no `new T4Slip`,
    no read of `T4RawBox.Value`, no box-code allow-list or required-box list, and
    no `decimal.Parse`/`TryParse` of a box value — the pre-check reads box
    *structure* only.

### Boundary, dependencies, regression

40. **Exactly two production project references.**
    Given the module's project file, When inspected, Then its production project
    references are exactly `DocumentExtraction` and `TaxLogicEngine`.

41. **No `TaxCases` coupling and no reverse references.**
    Given the solution's project files and the compiled assemblies, When inspected,
    Then this module references no `TaxCases` type in either direction, and
    `TaxLogicEngine`, `TaxCases`, and `DocumentExtraction` each reference this
    module not at all.

42. **No clock, network, filesystem, database, OCR, or LLM access.**
    Given the module's source and assembly, When inspected, Then they contain no
    `DateTime.Now`/`UtcNow`, no network, filesystem, database, environment, or
    current-user access, and no OCR/LLM invocation.

43. **Existing behaviour stays green.**
    Given `dotnet test TaxAI.sln`, When the full suite runs after this feature is
    added, Then every existing `TaxLogicEngine.Tests`, `TaxCases.Tests`, and
    `DocumentExtraction.Tests` test remains green, and no existing public type in
    any of the three modules has been added, renamed, or changed — in particular no
    parser rule, no mapper behaviour, and no mapping-config entry.

---

## Open questions

### Blocking Class A questions

**None.** This PRD asserts no CRA fact and makes no tax-semantic decision. Every
tax judgement in the pipeline — which boxes exist, which are required, what a
value may be, which CRA line a box feeds, how amounts round, how a SIN is
represented — is **delegated unchanged** to `T4SlipParser`, `T4LineMapper`,
`Mappings/T4LineMap.2025.json`, and `RecipientIdentifier`, all shipped and
canada.ca-verified under specs 003–004. Nothing was assumed in their place, and
nothing was carried over from a prior PRD as if it were a source.

The one genuinely new rejection family this feature introduces — the payload
structural pre-check (requirement 5) — is deliberately confined to *structural
traversability* (null collection, null element, blank code). It decides nothing
about tax content, which is why it is not a Class A judgement: it strictly
converts a would-be `NullReferenceException` into a structured, fail-closed
rejection, and leaves every payload the parser can traverse to the parser.

### Class A items raised for the human, non-blocking (delegated, nothing assumed)

These do not block this PRD because this feature changes none of them — but this
is the first spec where each becomes operationally visible, so they are surfaced
rather than buried.

- **Box 54 and the employee-copy problem.** `T4SlipParser` requires Box 54
  (employer's account number), and `T4Slip`'s own documentation notes Box 54 is
  "not on the employee copy". Spec 003 recorded this as a `[CONFIRM]`-flagged
  decision meaning the parser presumes the employer/filing-side copy. The T4 Case
  Processor is where an OCR'd **employee-copy** T4 first meets that rule, and every
  such extraction will fail closed at AC-17. This PRD keeps the rule exactly as
  shipped and takes no position on changing it. **If the intended product flow is
  "accountant scans the client's copy of the T4", spec 003 needs an amendment
  before this pipeline is usable in production** — that amendment is a Class A tax
  decision and is not this agent's to make.
- **Engine robustness at the raw-payload boundary.** `T4SlipParser` throws rather
  than failing closed on a null `Boxes` collection, a null box element, or a null
  box `Code`. This feature guards the seam (requirement 5, AC-11 to AC-13) instead
  of changing the engine, because hardening a parser is a change to spec 003's
  shipped tax-domain code. Whether the engine should *also* be hardened is a
  separate decision for the human.
- **Unmapped boxes and preparer duty.** Spec 004 deliberately routes several
  populated boxes to `UnmappedBoxCodes` (calculation-, schedule-, or
  residence-dependent) with per-box canada.ca rationale in
  `src/TaxLogicEngine/Mappings/README.md`. This feature surfaces that list
  unchanged and rejects nothing (requirement 15, AC-6). Deciding that an unmapped
  box must raise a review flag, block a draft, or trigger a schedule would be a new
  tax judgement; it is deferred, in full, to the Accountant Review Package.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **Spec folder and slug** are `specs/008-t4-case-processor/`
  (001–007 taken); the feature name is taken from `NEXT.md` and root `CLAUDE.md`.
- `[ASSUMED]` **Module home:** a new `src/CaseProcessing/` project (net8.0,
  nullable enabled) with tests in `tests/CaseProcessing.Tests/`, both added to
  `TaxAI.sln`. A new module is not optional here: the approved boundaries forbid
  the alternatives — `src/DocumentExtraction/CLAUDE.md` explicitly forbids
  `T4SlipParser`/`T4LineMapper` usage inside DocumentExtraction, and
  `src/TaxCases/CLAUDE.md` fixes TaxCases' sole production dependency as
  `TaxLogicEngine`. The module is real and contract-bearing, not a speculative
  empty shell.
- `[ASSUMED]` **Module name is form-neutral (`CaseProcessing`), the service inside
  it is form-specific (`T4CaseProcessor`)**, so a future T5 processor does not
  require a second module. Nothing for T5 is scaffolded.
- `[ASSUMED]` **Type names** — `T4CaseProcessor` (the service),
  `ProcessedT4Extraction` (the success value), `T4ProcessingFields` (the closed
  field-name constant set), `SupportedProcessingTaxYears` (the module-local year
  policy). Exact names are `/plan`'s to finalize.
- `[ASSUMED]` **Method shape** — a single synchronous
  `ParseResult<ProcessedT4Extraction> Process(ValidatedExtraction extraction)`.
  Synchronous because the module does no I/O; spec 007's async ingress port is
  signature-only and is not implemented here.
- `[ASSUMED]` **Injection shape** — `T4CaseProcessor` is constructed with an
  `IT4LineMapProvider` (so year isolation is observable in tests); the stateless
  `T4SlipParser` and the `T4LineMapper` may be constructed internally. No DI
  container, no service registration, no host wiring.
- `[ASSUMED]` **One extraction per call**; no batch API, no multi-slip overload.
- `[ASSUMED]` **The result carries attribution and engine output only** — no
  provenance echo (`ProviderLabel`, `Confidence`), because the caller still holds
  the `ValidatedExtraction` that carries them and duplicating them adds a second
  copy to keep in sync.
- `[ASSUMED]` **Null input fails closed rather than throwing**
  (`ArgumentNullException`), matching `ExtractionEnvelopeValidator.Validate` and
  `TaxCase.AttachEvidence` rather than `T4SlipParser.Parse`. A pipeline entry point
  that never throws is the safer default at an untrusted seam.
- `[ASSUMED]` **Error accumulation order** — this module's own pre-check errors
  are produced in a fixed declared order (so AC-7 determinism holds), and engine
  errors are appended verbatim without interleaving.
- `[ASSUMED]` **A parser failure short-circuits before mapping** rather than
  collecting both stages' errors, so no mapping error is ever reported for a slip
  that was never validly parsed.

---

## CRA sources

**No new external tax fact is asserted by this PRD**, and — deliberately — **no
CRA line number, box number-to-line assignment, rate, threshold, limit, deadline,
gross-up, credit percentage, or eligibility rule appears anywhere in this
document**. The acceptance criteria are written to compare this feature's output
against the *engine's own output* (AC-3, AC-4, AC-25 to AC-27) precisely so that
no line number needs to be, or is, restated here; AC-38 enforces the same rule in
code.

Where an acceptance criterion names a box number (AC-15 Box 12, AC-16 Box 45,
AC-17 Box 54, AC-18/19 Box 14, AC-20 Box 10), it is naming an **existing,
shipped `T4SlipParser` rule** — a verifiable fact about
`src/TaxLogicEngine/Services/T4SlipParser.cs`, read directly during this analysis
— not a CRA claim. Those parser rules were verified against canada.ca in spec 003
and are reused unchanged; if any of them is wrong, the correction belongs in spec
003, not here.

The tax facts this feature *relies on* already exist, were verified against
canada.ca in prior specs with citations recorded there, and are reused unchanged
rather than re-asserted:

| Reused fact (not re-asserted here) | Verified in | Source | Tax year | Retrieved |
|---|---|---|---|---|
| T4 box identities/definitions, required-box set, constrained code sets, companion-box rules (invoked via `T4SlipParser`; unchanged) | `specs/003-t4-slip-parsing/prd.md` | canada.ca — "T4 slip – Information for employers" (URL recorded there), plus RC4120 | 2025 | 2026-07-09 |
| T4 box→CRA-line mapping values (applied only from `Mappings/T4LineMap.2025.json` via `T4LineMapper`; unchanged) | `specs/004-t4-cra-line-mapping/prd.md` | canada.ca — "T4 slip: Statement of Remuneration Paid", `https://www.canada.ca/en/revenue-agency/services/tax/individuals/topics/about-your-tax-return/tax-return/completing-a-tax-return/tax-slips/understand-your-tax-slips/t4-slips/t4-statement-remuneration-paid.html` (date modified 2026-02-02) | 2025 | 2026-07-11 |
| Deliberate `UnmappedBoxCodes` deferrals and their per-box rationale (surfaced unchanged) | `specs/004-t4-cra-line-mapping/prd.md`, `src/TaxLogicEngine/Mappings/README.md` | canada.ca — same individuals page as above | 2025 | 2026-07-11 |

T5 facts (specs 001–002) are **not relied on**: no T5 type is consumed.

**Verification note (2026-07-25).** Because this PRD asserts no new CRA fact,
canada.ca was not required. A retrieval was nevertheless attempted for diligence
against the spec 004 individuals page above and returned **HTTP 403** to this
agent's fetch tool — the same bot filter spec 004 recorded on 2026-07-11, where
verification was completed by the coordinator in a browser session. **No fact was
taken from memory, from a secondary source, or from a prior PRD in place of a
source**, and no source conflict was resolved by this agent's judgement. Should
`/plan` or any implementation task find itself needing a CRA line number, a box
definition, or any other tax fact that is not already in
`Mappings/T4LineMap.2025.json` or `T4SlipParser`, that is a signal this PRD's
scope has been exceeded: stop and verify against canada.ca (coordinator browser
session) before proceeding.

---

## Definition of Done — self-check

Both classes:

- [x] `specs/008-t4-case-processor/prd.md` exists with the required structure,
      opening with `## Feature classification` (Problem / User stories /
      Functional requirements / Out of scope / Acceptance criteria / Open
      questions / CRA sources)
- [x] Feature class named explicitly (`tax-domain`) and justified; mixed content
      classified `tax-domain`
- [x] Reuse check performed and documented (`## Current state and reuse
      analysis`, grounded in the actual tree; twelve existing types reused, none
      re-specified)
- [x] Every AC is mechanically checkable by a single test
- [x] Zero Class A assumptions made (tax semantics, PII, tenant isolation, and
      security boundaries all delegated or escalated, never assumed)

`tax-domain` additionally:

- [x] Every CRA fact has URL + tax year + retrieval date, all from canada.ca —
      vacuously and deliberately: this PRD asserts **no** CRA fact and names no
      CRA line number; the reused facts are tabulated with their canada.ca
      citations and retrieval dates
- [x] No source conflict was resolved by this agent's judgement (none arose)
- [x] Tax year named explicitly in Problem (**2025 only**), with runtime
      selection and no-config behaviour stated (fail closed, no adjacent-year
      fallback)
- [x] Every fail-closed rule has its own negative AC (AC-9 to AC-23, plus AC-24)
- [x] PII acceptance criteria present (AC-29 to AC-33), covering the
      omission→masking seam
- [x] Tax-year isolation ACs present (AC-34 to AC-37), including "a config for
      year N is never applied to a slip from year N±1" (AC-36)
- [x] Data-driven mapping AC present (AC-38): no CRA line number hardcoded in C#
