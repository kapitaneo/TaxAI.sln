# PRD 007 — Document Extraction Contract

## Feature classification

**Class: `tax-domain`.**

The Document Extraction Contract is the declared boundary through which
document/OCR/LLM extraction output enters TaxAI. Per CLAUDE.md, "OCR and LLM
output are untrusted input — input to be validated, never a source of tax
truth." This feature defines exactly that: a provider-independent **ingestion
port** and an **untrusted extraction envelope** that carries already-extracted
document content toward the existing slip-parsing engine (`TaxLogicEngine`) and,
downstream, toward participant-scoped evidence in Tax Case Core.

Although the deliverable is "a contract" and much of it is structural, it
governs **taxpayer-domain untrusted input**, it carries **taxpayer PII (an
extracted recipient SIN can appear in extracted content)**, it is scoped to an
explicit **tax year**, and it enforces **tax-year isolation** and **firm
attribution** on data that will become a tax return. It therefore mixes
structural work with tax semantics. Per the classification rule ("if a feature
mixes both, it is `tax-domain`; when unsure, it is `tax-domain`") — and
consistently with how spec 006 (Tax Case Core) was classified for the same
PII / tax-year / taxpayer-domain reasons — this feature is `tax-domain` and every
tax-domain protection applies as written: explicit tax-year scope, fail-closed
behaviour, SIN masking, one negative acceptance criterion per fail-closed rule, a
tax-year-isolation criterion, and the "no hardcoded CRA line number / no box→line
mapping in this module" criterion.

This PRD deliberately introduces **no new CRA box, line, rate, threshold, limit,
deadline, gross-up, or eligibility rule** (see `## CRA sources`). It asserts no
new tax fact: it reuses the already-verified box/category semantics of specs
001–004 by carrying **raw, unvalidated** slip payloads (`T4RawInput`,
`T5RawInput`) and delegating *all* parsing, typing, box→line mapping, and
calculation to the existing engine and to the future **T4 Case Processor**. No
blocking Class A questions remain (see Open questions).

---

## Problem

TaxAI can already parse a structured T4/T5 slip payload and map its boxes to CRA
lines (`TaxLogicEngine`, specs 001–004), and it can record that evidence of a
category was received against a participant, firm, and tax year (Tax Case Core,
spec 006). But there is **no declared, provider-independent boundary** at which
extraction output — the structured result an OCR or LLM adapter produces from a
scanned or uploaded document — **legitimately enters the system**. Today the
engine's `T4RawInput` / `T5RawInput` payloads exist, but:

- there is **no port** (interface/contract) an OCR/LLM adapter implements to
  deliver extraction output inward, so infrastructure has nothing to depend on
  and the dependency direction CLAUDE.md mandates (infrastructure → contract) is
  not expressible;
- there is **no envelope** wrapping a raw payload with the provenance and
  attribution needed to route it safely: which firm, which participant, which
  tax year, which source document, from which provider, at what confidence;
- there is **no single "untrusted extraction result" abstraction** that unifies
  "an extracted T4 slip," "an extracted T5 slip," and "a non-slip evidence
  document" behind one document-kind discriminator;
- there is **no fail-closed, envelope-level validation** that rejects malformed,
  mis-attributed, wrong-year, or empty extraction **before** anything treats it
  as tax input.

The risk without this boundary is precisely the one CLAUDE.md forbids: OCR/LLM
output leaking into the system as if it were tax truth — an unvalidated,
low-confidence, or wrong-year extraction silently becoming a filing input. The
Document Extraction Contract closes that gap: a deterministic, provider-
independent contract that carries extraction output as **explicitly untrusted
input**, validates its envelope well-formedness and attribution, and hands a
well-formed-but-still-untrusted result to the downstream processor — while never
itself parsing a slip, typing a box value, mapping a box to a CRA line, or
producing any tax truth.

**Tax year in scope: 2025 only.** An extraction envelope declares exactly one
intended tax year; the contract validates it against this spec's supported set
(2025) and **fails closed** on any other or absent year. Per CLAUDE.md there is
no "latest"/"current" year and **no adjacent-year fallback**: a 2024 or 2026
envelope is rejected, never coerced to 2025. Supporting years other than 2025 is
out of scope (a new supported year is a scope change, not a silent widening).

---

## Current state and reuse analysis

Grounded in the actual tree at `D:\Projects\TaxAI`, not memory. A grep for
`extraction` / `DocumentExtraction` across `src/` matches **no production code**
today (only CLAUDE.md, NEXT.md, and prior spec prose). There is no
`src/DocumentExtraction*` project.

### Reused unchanged (do NOT re-specify, duplicate, or modify)

- **`T4RawInput` / `T4RawBox`** and **`T5RawInput` / `T5RawBox`**
  (`src/TaxLogicEngine/Models/`) — already documented as "the already-extracted,
  structured input for a single slip… unvalidated upstream data." These **are**
  the slip extraction payload. The Document Extraction Contract carries these
  verbatim as its slip payloads; it does not define a new slip payload type and
  does not alter these. Their `Boxes` remain a list (not a dictionary) so
  duplicate box codes survive to the engine that rejects them — the contract
  preserves that and never collapses them.
- **`ParseResult<T>`** (`Models/ParseResult.cs`) — the fail-closed result.
  Envelope validation returns `ParseResult<T>`: `Success` carries a well-formed
  (still untrusted) validated extraction and no errors; `Failure` carries ≥1
  error and no value; no partial state.
- **`ValidationError`** (`Models/ValidationError.cs`) — `{ Field; Message; }`,
  reused verbatim for every rejection reason; messages kept SIN-safe.
- **`RecipientIdentifier`** (`Models/RecipientIdentifier.cs`) — the only
  permitted representation of a SIN. Where extracted content contains a SIN and
  the contract must surface or hold it, it uses this masked value object; no new
  SIN type is created.
- **`SafeDiagnosticText`** (`src/TaxCases/Services/SafeDiagnosticText.cs`) — the
  existing deterministic SIN-masking helper for free text (masks compact,
  hyphenated, and space-separated SIN-like strings to `***-**-789`). Reused for
  any diagnostic that must echo caller/extracted text. (Whether reuse requires a
  reference to `TaxCases` or a relocation/duplication of this helper is a Class B
  `[ASSUMED]`; see Open questions.)

### Not duplicated, and explicitly out of this feature

- **`T4SlipParser` / `T5SlipParser`, `T4LineMapper` / `T5LineMapper`,
  `MoneyRounding`, the T4/T5 mapping JSON configs, `TaxLineEntry`,
  `T4MappingResult` / `T5MappingResult`** — the contract does **not** parse,
  type, round, map, or calculate. It carries the raw payload untouched. Parsing
  and mapping a received slip is the future **T4 Case Processor** spec, which
  invokes the engine. This contract produces no `T4Slip`, no `TaxLineEntry`, and
  no tax truth.
- **Tax Case Core (`TaxCase`, `ReceivedEvidence`, `EvidenceDescriptor`,
  `DocumentCategory`, readiness, lifecycle)** — the contract does **not** mutate
  a case, attach evidence to a case, or compute readiness. It carries the
  attribution metadata (firm, participant, tax year, source reference) a
  downstream step needs to construct an `EvidenceDescriptor`, but constructing it
  and calling Tax Case Core is downstream work, not this contract. This feature
  **does not add, rename, or change any TaxCases public type.**

### Genuinely new (introduced by this feature)

A new provider-independent contract module: an **ingestion port** interface an
OCR/LLM adapter implements to deliver extraction output inward; an **untrusted
extraction envelope** carrying a document identifier, a document-kind
discriminator, firm/participant/tax-year attribution, provenance metadata
(source reference, provider label, optional confidence), and a payload (a reused
`T4RawInput` / `T5RawInput` for slip kinds, or an opaque non-slip evidence
reference); and a **deterministic envelope validator** that fails closed on
malformed, mis-attributed, wrong-year, or empty extraction and otherwise returns
a well-formed-but-still-untrusted validated result. Concrete type names, the
module/project home, and file layout are Class B `[ASSUMED]`. The module obeys
CLAUDE.md's dependency rules: it may depend inward on `TaxLogicEngine` domain
types (and, if chosen, `TaxCases` for `SafeDiagnosticText`); it must **not**
depend on ASP.NET Core, EF Core, SQL, OCR/LLM/cloud/email SDKs, the filesystem,
the network, or the system clock. OCR/LLM code lives on the *other* side of the
port as an adapter that **implements** it and feeds it input; no OCR/LLM
invocation lives inside this contract.

---

## User stories

1. As an **integration engineer** wiring an OCR/LLM provider, I want one declared
   port to implement so my adapter delivers extraction output inward through a
   contract, never by reaching into the engine or a case module.
2. As an **engine maintainer**, I want every extraction to arrive as an
   explicitly **untrusted** envelope that the system validates for
   well-formedness and attribution before anything treats it as tax input, so
   OCR/LLM output can never be mistaken for tax truth.
3. As a **compliance reviewer**, I want an extraction whose intended tax year is
   not the case's supported year to be **rejected**, never silently coerced, so a
   2024 or 2026 extraction can never enter a 2025 engagement.
4. As a **firm administrator**, I want every extraction to carry an explicit firm
   attribution and be rejected when it is missing, so extracted content is never
   orphaned from the firm that owns it.
5. As a **compliance reviewer**, I want any SIN appearing in extracted content to
   be masked to the last 3 digits everywhere the contract surfaces it — errors,
   diagnostics, `ToString()`, serialized form — so extraction never leaks a SIN.
6. As the **downstream T4 Case Processor** author, I want a well-formed validated
   extraction to hand me the raw slip payload plus its firm/participant/tax-year
   attribution, without the contract having parsed, typed, or mapped anything, so
   I remain the single place slip parsing and box→line mapping happen.
7. As an **engine maintainer**, I want envelope validation to be deterministic
   and provider-independent (no clock, no network, no OCR/LLM call, no I/O) so the
   same extraction envelope always produces the same accept/reject result.

---

## Functional requirements

`[design]` marks a decision that depends on no CRA fact. `[reuse]` marks reuse of
an existing type. `[boundary]` marks a module-boundary invariant from CLAUDE.md.

### The ingestion port and untrusted envelope

1. **[design]** The feature SHALL define an **ingestion port** (interface) that an
   OCR/LLM/document adapter implements to deliver an extraction envelope inward.
   The port is the *only* declared way extraction output enters; adapters depend
   on the port, not the reverse (`Infrastructure → contract`).
2. **[design]** An **extraction envelope** SHALL carry: a non-empty **document
   identifier**; a **document-kind** discriminator; a **firm reference**; a
   **participant reference**; an **intended tax year**; **provenance** (a source
   reference, an optional provider label, and an optional confidence); and a
   **payload** appropriate to the document kind.
3. **[design]** Supported document kinds for the MVP SHALL be **T4 slip**, **T5
   slip**, and a **generic evidence document** `[ASSUMED]`. The T4-slip payload
   SHALL be a reused `T4RawInput`; the T5-slip payload SHALL be a reused
   `T5RawInput`; the generic-evidence payload SHALL be an **opaque source
   reference only** (no parsed content, no box values). An **unknown/unsupported
   document kind** SHALL fail closed with a structured error naming the kind.
4. **[design]** The envelope and its validated result SHALL represent
   **untrusted** input: the contract SHALL NOT parse a slip, type or coerce a box
   value, map a box to a CRA line, round currency, or compute any tax quantity. A
   validated extraction SHALL expose the **raw payload**, not a `T4Slip`,
   `TaxLineEntry`, or any parsed/mapped tax value. Extraction is never tax truth.

### Envelope validation (fail closed)

5. **[reuse]** Envelope validation SHALL return `ParseResult<T>`: on success a
   well-formed (still untrusted) validated extraction and **no** errors; on
   rejection ≥1 `ValidationError` and **no** value, mutating nothing. There is no
   partial/best-effort extraction.
6. **[design]** A **missing or blank document identifier** (null, empty, or
   whitespace-only) SHALL fail closed with a structured error and produce no
   validated extraction.
7. **[design]** A **missing or blank firm reference** SHALL fail closed with a
   structured error (attribution integrity).
8. **[design]** A **missing or blank participant reference** SHALL fail closed
   with a structured error `[ASSUMED: participant attribution is required for
   every MVP document kind]`.
9. **[design/verified-scope]** An **absent tax year** SHALL fail closed; an
   **unsupported tax year** (any value other than the supported year, 2025) SHALL
   fail closed. The year SHALL NEVER be coerced to 2025 and **no adjacent-year
   fallback** is permitted (CLAUDE.md). Supported-year policy is a product-scope
   policy of this spec, validated as an explicit year — the contract does **not**
   inspect `TaxLogicEngine` mapping files to decide year support.
10. **[design]** A **payload/kind mismatch** SHALL fail closed: a document kind of
    T4 slip whose payload is absent or is not a `T4RawInput` (and likewise T5
    slip / `T5RawInput`), or a generic-evidence kind carrying a slip payload,
    SHALL be rejected with a structured error.
11. **[design]** An **empty extraction** SHALL fail closed: a slip payload with
    **zero boxes and no identity content**, or a generic-evidence document with a
    missing/blank source reference, carries nothing to validate and SHALL be
    rejected.
12. **[design]** When a **confidence** value is present in provenance it SHALL lie
    within its declared closed range `[0.0, 1.0]` `[ASSUMED]`; a value outside
    that range SHALL fail closed as malformed provenance. **Confidence is
    carried as untrusted metadata only** — the contract SHALL NOT reject a
    well-formed extraction merely because confidence is low, and SHALL NOT invent
    a numeric acceptance threshold (see Open questions). Absent confidence is
    allowed.
13. **[design]** Validation SHALL NOT collapse, deduplicate, or reorder the raw
    payload's boxes. Duplicate box codes in a `T4RawInput`/`T5RawInput` SHALL
    survive validation untouched, so the engine's existing duplicate-box
    rejection still fires downstream. The contract itself makes no judgement about
    box validity.

### PII (extracted SIN)

14. **[reuse]** Where extracted content contains a SIN, the contract SHALL
    represent it only via `RecipientIdentifier` and SHALL NEVER emit it in full in
    any `ToString()`, `ValidationError.Message`, diagnostic, log line, or
    serialized form — only the last-3-digits masked form. Any diagnostic that must
    echo caller/extracted free text SHALL pass it through `SafeDiagnosticText`.
15. **[design]** The contract SHALL introduce **no** alternative, unmasked SIN
    representation and SHALL NOT widen the visibility of any SIN present in a
    reused raw payload beyond what those existing types already permit.

### Boundary, determinism, and dependency direction

16. **[boundary]** The contract module SHALL be deterministic and
    provider-independent: no system-clock read, no network, no filesystem, no
    database, no OCR/LLM invocation, no current-user/environment read. The same
    envelope SHALL always produce the same accept/reject `ParseResult`.
17. **[boundary]** The contract SHALL contain **no** CRA line number literal,
    **no** box→line mapping, **no** box-value typing/parsing, and **no** tax
    calculation. All such logic remains in `TaxLogicEngine` and its JSON config.
18. **[boundary]** Dependencies point inward only: the module may depend on
    `TaxLogicEngine` domain types (and, if chosen for `SafeDiagnosticText`,
    `TaxCases`); it SHALL NOT depend on ASP.NET Core, EF Core, SQL, OCR/LLM/cloud/
    email SDKs, or any I/O making its result non-deterministic. `TaxLogicEngine`
    SHALL NOT depend on this module.
19. **[design]** The contract SHALL NOT mutate a `TaxCase`, attach evidence,
    compute readiness, or add/rename/change any TaxCases public type. Bridging a
    validated extraction into Tax Case Core evidence is downstream work.

---

## Out of scope

Each item is explicitly excluded from this spec:

- **Actual OCR or LLM extraction / any provider adapter** — this feature defines
  the port and envelope; a concrete OCR/LLM adapter that *implements* the port is
  a separate integration and is out of scope (NEXT.md: "OCR or LLM integration"
  is explicitly not now).
- **Slip parsing, box typing, box→CRA-line mapping, rounding, tax calculation** —
  remain in `TaxLogicEngine` and the future **T4 Case Processor**; this contract
  produces no `T4Slip`, no `TaxLineEntry`, no tax truth.
- **Attaching evidence to a `TaxCase` / computing readiness / any TaxCases
  mutation** — downstream; no TaxCases public type is added or changed.
- **A numeric confidence acceptance threshold / auto-rejecting low-confidence
  extraction** — no such threshold is invented here; confidence is carried as
  untrusted metadata (requirement 12). If the product later wants hard gating,
  that is a follow-up decision.
- **Document storage / file bytes / upload / MIME handling / image processing** —
  the envelope carries an opaque source reference, not file bytes.
- **Batch/multi-document ingestion, retries, deduplication across documents** —
  the contract validates one envelope per call; duplicate-evidence detection
  across a case is Tax Case Core's existing responsibility.
- **Persistence, queuing, or delivery** of extraction results.
- **Database / API host / frontend** of any kind.
- **NETFILE / EFILE / CRA submission.**
- **Any CRA eligibility, definition, box, line, rate, threshold, or deadline** —
  none is asserted; slip semantics are reused unchanged from specs 001–004.
- **Tax years other than 2025.**
- **Any accidental change to existing `TaxLogicEngine` or `TaxCases` behaviour** —
  the full existing test suite must remain green (see AC-24).

---

## Acceptance criteria

Each criterion is a single mechanically-checkable test (Given/When/Then).

### Happy path

1. **Valid T4 extraction envelope.**
   Given an envelope with a non-empty document id, kind T4 slip, a non-empty firm
   reference, a non-empty participant reference, intended tax year 2025, and a
   `T4RawInput` payload carrying at least one box,
   When it is validated, Then it succeeds with zero errors and yields a validated
   extraction exposing that raw `T4RawInput` payload unchanged.

2. **Valid T5 and generic-evidence envelopes.**
   Given (a) an otherwise-valid envelope of kind T5 slip with a `T5RawInput`
   payload carrying at least one box, and (b) an otherwise-valid generic-evidence
   envelope with a non-empty source reference,
   When each is validated, Then each succeeds with zero errors and its payload/
   source reference is exposed unchanged.

### Fail-closed rejections (one rule per criterion)

3. **Missing/blank document identifier.**
   Given an otherwise-valid envelope whose document id is null, empty, or
   whitespace-only, When validated, Then it fails closed with a structured error
   naming the document id and yields no validated extraction.

4. **Unknown/unsupported document kind.**
   Given an envelope whose document kind is outside the supported set,
   When validated, Then it fails closed with a structured error naming the kind.

5. **Missing/blank firm reference.**
   Given an otherwise-valid envelope with a null/blank firm reference,
   When validated, Then it fails closed with a structured error naming the firm
   reference.

6. **Missing/blank participant reference.**
   Given an otherwise-valid envelope with a null/blank participant reference,
   When validated, Then it fails closed with a structured error.

7. **Absent tax year.**
   Given an otherwise-valid envelope with no intended tax year,
   When validated, Then it fails closed with a tax-year error and is never
   coerced to 2025.

8. **Unsupported tax year (year isolation).**
   Given an otherwise-valid envelope with intended tax year 2024 (and again
   2026), When validated, Then each fails closed with a tax-year error; the year
   is never coerced to 2025 and no adjacent-year fallback is applied.

9. **Payload/kind mismatch.**
   Given an envelope of kind T4 slip whose payload is absent or is a `T5RawInput`
   (and, separately, a generic-evidence kind carrying a slip payload),
   When validated, Then each fails closed with a structured error and yields no
   validated extraction.

10. **Empty extraction.**
    Given a T4-slip envelope whose `T4RawInput` has zero boxes and no identity
    content (and, separately, a generic-evidence envelope with a blank source
    reference), When validated, Then each fails closed with a structured error.

11. **Malformed confidence.**
    Given an otherwise-valid envelope whose provenance confidence is `-0.1` (and
    again `1.1`), When validated, Then each fails closed with a structured error
    naming the confidence.

### Boundaries

12. **Confidence at the range bounds is accepted.**
    Given otherwise-valid envelopes with provenance confidence exactly `0.0`,
    exactly `1.0`, and absent, When each is validated, Then each succeeds (a low
    or boundary confidence does not reject a well-formed extraction).

13. **Whitespace-only references are treated as blank.**
    Given an otherwise-valid envelope whose document id / firm reference /
    participant reference is a whitespace-only string, When validated, Then it
    fails closed exactly as for the empty case.

14. **Duplicate box codes survive validation.**
    Given a T4-slip envelope whose `T4RawInput.Boxes` contains the same box code
    twice, When validated, Then it succeeds and the validated extraction's payload
    still contains both entries in order (validation neither collapses nor
    reorders them; the engine's duplicate-box rejection remains a downstream
    concern).

15. **Empty envelope input.**
    Given an envelope with no document id, no firm reference, no participant
    reference, and no tax year, When validated, Then it fails closed with one
    structured error per missing required field and throws no exception.

### Untrusted-input / no-tax-truth

16. **A validated extraction produces no tax truth.**
    Given a valid T4-slip envelope, When it is validated, Then the accepted result
    exposes the raw `T4RawInput` and is **not** a `T4Slip`, `T4MappingResult`, or
    `TaxLineEntry`, and no box value has been typed, parsed, mapped, or rounded.

17. **No parsing/mapping/calculation is performed by validation.**
    Given a T4-slip envelope whose `T4RawInput` contains a box value that the
    engine would reject as malformed, When the envelope is validated, Then
    validation still succeeds at the envelope level (it makes no judgement about
    box validity) and performs no parse, map, or calculation.

### PII

18. **SIN masking in all output.**
    Given an envelope whose extracted content contains the SIN `123456789` (in a
    surfaced field and in any diagnostic), When the validated extraction, any
    `ValidationError.Message`, any `ToString()`, and any serialized form are
    produced, Then only `***-**-789` can appear and no 9-consecutive-digit SIN
    substring is present anywhere.

19. **A rejection message never echoes extracted PII in full.**
    Given an envelope that fails closed and whose extracted content contains a
    SIN, When the failure's `ValidationError`s are read, Then no full SIN (compact,
    hyphenated, or space-separated) appears in any message.

### Tax-year isolation

20. **A 2025 contract never adopts another year's extraction.**
    Given the supported year is 2025, When a 2024-intended envelope and a
    2026-intended envelope are validated (AC-8), Then both fail closed and no
    validated extraction is ever produced under year 2025 for them; a validated
    2025 extraction only ever carries year 2025.

### Data-driven / no hardcoded CRA facts

21. **No CRA line number, box→line mapping, or mapping-file read in the module.**
    Given the compiled contract assembly, When its source/emitted metadata is
    inspected, Then it contains no CRA line number literal, no box→line mapping,
    no box-value typing/parsing, no tax calculation, and no read of a
    `TaxLogicEngine` mapping/config file — such logic lives only in
    `TaxLogicEngine` and its JSON config.

### Determinism / boundary

22. **No clock, network, filesystem, database, or OCR/LLM access.**
    Given the contract source/assembly, When inspected, Then it contains no read
    of the system clock (`DateTime.Now`/`UtcNow`), no network, no filesystem, no
    database, and no OCR/LLM invocation; any timestamp appears only as an
    externally supplied input.

23. **Determinism.**
    Given the same envelope validated twice, When the two `ParseResult`s are
    compared, Then they are equal (same success/failure, same errors, same
    exposed payload).

### Reuse / regression (existing behaviour stays green)

24. **Existing engine and case behaviour unchanged.**
    Given `dotnet test TaxAI.sln`, When the full suite runs after this feature is
    added, Then every existing `TaxLogicEngine.Tests` and `TaxCases.Tests` test
    remains green and no existing public tax type is added, renamed, or changed by
    this feature.

25. **Reused payload and result types.**
    Given the slip payloads and the validation result, When their types are
    inspected, Then slip payloads are the existing `T4RawInput`/`T5RawInput` (no
    new slip payload type), and validation returns the existing `ParseResult<T>`
    with `ValidationError`s and represents any SIN via `RecipientIdentifier` — no
    duplicate of these types is introduced.

---

## Open questions

### Blocking Class A questions

**None.** Every genuine tax semantic (what box codes/values are valid, what a
valid SIN is, box→line mapping, eligibility) is **delegated** to the already-
verified `TaxLogicEngine` and the future T4 Case Processor, not decided here, so
no unverifiable tax fact was assumed and no canada.ca claim is newly asserted.
The remaining decisions are structural/scope (Class B) and are listed below.

One design choice that borders on "what must be rejected" — **whether low
confidence should reject an extraction** — is resolved *conservatively without
inventing a fact*: the contract carries confidence as untrusted metadata and does
**not** invent a numeric threshold (requirement 12, and out-of-scope). This does
not weaken any protection: CLAUDE.md already forbids treating extraction as tax
truth, so low-confidence data still cannot become a filing input unvalidated. It
is surfaced as an `[ASSUMED]` below so a human can direct hard gating if desired.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **Feature name / slug.** The `/spec` command was invoked without a
  feature argument; "Document Extraction Contract" and slug
  `007-document-extraction-contract` were taken from CLAUDE.md's / NEXT.md's
  stated next feature.
- `[ASSUMED]` **Module home / project:** a new `src/DocumentExtraction/` project
  (name and location assumed), per CLAUDE.md's "future modules get `src/<Module>/`
  homes when built," depending inward on `TaxLogicEngine` domain types only (plus
  `TaxCases` if that is how `SafeDiagnosticText` is reused).
- `[ASSUMED]` **Type names** (e.g. an `IDocumentExtractionSource` port, an
  `ExtractionEnvelope`, an `ExtractedDocumentKind` enum, an `ExtractionProvenance`
  record, an `ExtractionValidator`, and a `ValidatedExtraction` result) follow
  existing naming conventions; exact names are for `/plan`.
- `[ASSUMED]` **Supported document kinds** are exactly **T4 slip**, **T5 slip**,
  and a **generic evidence document** for the MVP; any other kind fails closed.
- `[ASSUMED]` **Slip payloads reuse `T4RawInput`/`T5RawInput`;** the generic
  evidence payload is an **opaque source reference only** (no parsed content).
- `[ASSUMED]` **Participant attribution is required** for every MVP document kind
  (blank ⇒ reject). If case-level (participant-less) documents are needed, that is
  a follow-up.
- `[ASSUMED]` **Confidence is an optional value in `[0.0, 1.0]`;** out-of-range
  fails closed, absent is allowed, and **no numeric acceptance threshold is
  invented** (low confidence does not reject a well-formed extraction).
- `[ASSUMED]` **Firm/participant/document references and the source reference are
  opaque, caller-supplied, non-empty values;** the contract validates presence
  and non-blankness only and never generates an identifier itself (generation
  would be a non-deterministic side effect).
- `[ASSUMED]` **One envelope per validation call;** batch ingestion and
  cross-document deduplication are out of scope (Tax Case Core already rejects
  duplicate evidence ids).
- `[ASSUMED]` **`SafeDiagnosticText` is reused** for masking free text; whether
  that requires a `TaxCases` reference or a relocation of the helper to a shared
  location is a `/plan` decision — no second masking helper is introduced.
- `[ASSUMED]` **Tax year 2025 only** for the MVP; any other or absent year fails
  closed (mirrors the engine, Tax Case Core, and CLAUDE.md). This is a
  product-scope policy of this spec, not derived from mapping files.
- `[ASSUMED]` **Spec folder** is `specs/007-document-extraction-contract/`
  (001–006 taken).

---

## CRA sources

**No new external tax facts are asserted by this PRD.** The Document Extraction
Contract introduces no CRA box number, line number, rate, threshold, limit,
deadline, gross-up, credit percentage, or eligibility rule. It carries **raw,
unvalidated** slip payloads and defers all parsing, typing, box→line mapping, and
calculation to `TaxLogicEngine` and the future T4 Case Processor. The only tax
facts it *relies on* already exist and were verified against canada.ca in prior,
cited specs, and are reused unchanged rather than re-asserted here:

| Reused fact (not re-asserted here) | Verified in | Source | Tax year | Retrieved |
|---|---|---|---|---|
| T4 slip structure / box codes (carried only as an untrusted `T4RawInput` payload; not parsed or mapped here) | spec 003 `prd.md`, spec 004 `prd.md` | canada.ca T4 employer & individual pages (URLs recorded in those PRDs) | 2025 | 2026-07-09 / 2026-07-11 |
| T5 slip structure / box codes (carried only as an untrusted `T5RawInput` payload; not parsed or mapped here) | spec 001 `prd.md`, spec 002 `prd.md` | canada.ca T5 pages (URLs recorded in those PRDs) | 2025 | (as recorded in specs 001–002) |

Because this PRD asserts no new tax fact, canada.ca was not re-queried for a new
claim; no source conflict was resolved by the author's own judgement, and no
unverified tax fact entered the PRD. The moment a future spec (e.g. the T4 Case
Processor) asserts a box→line or eligibility fact, that spec must verify it
against canada.ca at that time.
