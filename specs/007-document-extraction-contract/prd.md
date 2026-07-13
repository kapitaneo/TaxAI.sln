# PRD 007 — Document Extraction Contract

> **Revision (2026-07-13):** revised after human review, before `/plan`. The
> approved MVP scope is **T4 slip extraction** and **generic evidence documents
> (initially medical-expense evidence)**. T5 extraction support is deferred to a
> future scope change; existing T5 engine types remain unchanged and are **not
> consumed** by spec 007. This revision also applies the human-directed fixes:
> case-scoped attribution, corrected ingress-port direction, raw-payload/PII
> omission semantics, a single required source reference, envelope↔payload
> tax-year coherence, and a `decimal?` confidence representation. Decisions
> resolved by this review are listed under *Resolved by human review*.

## Feature classification

**Class: `tax-domain`.**

The Document Extraction Contract is the declared boundary through which
document/OCR/LLM extraction output enters TaxAI. Per CLAUDE.md, "OCR and LLM
output are untrusted input — input to be validated, never a source of tax
truth." This feature defines exactly that: a provider-independent **ingress
port** (an inward application boundary that provider adapters *call*) and an
**untrusted extraction envelope** that carries already-extracted document
content toward the existing slip-parsing engine (`TaxLogicEngine`) and,
downstream, toward participant-scoped evidence in Tax Case Core.

Although the deliverable is "a contract" and much of it is structural, it
governs **taxpayer-domain untrusted input**, it carries a raw T4 payload that
**may contain an unmasked SIN (Box 12)**, it is scoped to an explicit **tax
year**, and it enforces **tax-year isolation** and **firm/case/participant
attribution** on data that will become a tax return. It therefore mixes
structural work with tax semantics. Per the classification rule ("if a feature
mixes both, it is `tax-domain`; when unsure, it is `tax-domain`") — and
consistently with how spec 006 (Tax Case Core) was classified for the same
PII / tax-year / taxpayer-domain reasons — this feature is `tax-domain` and every
tax-domain protection applies as written: explicit tax-year scope, fail-closed
behaviour, PII protection (here: **omission** of the untrusted raw payload from
all diagnostics rather than echo-and-redact), one negative acceptance criterion
per fail-closed rule, a tax-year-isolation criterion, and the "no hardcoded CRA
line number / no box→line mapping in this module" criterion.

This PRD deliberately introduces **no new CRA box, line, rate, threshold, limit,
deadline, gross-up, or eligibility rule** (see `## CRA sources`). It asserts no
new tax fact: it reuses the already-verified T4 box/category semantics of specs
003–004 by carrying a **raw, unvalidated** slip payload (`T4RawInput`) and
delegating *all* parsing, typing, box→line mapping, and calculation to the
existing engine and to the future **T4 Case Processor**. No blocking Class A
questions remain (see Open questions).

---

## Problem

TaxAI can already parse a structured T4 slip payload and map its boxes to CRA
lines (`TaxLogicEngine`, specs 003–004), and it can record that evidence of a
category was received against a participant, case, firm, and tax year (Tax Case
Core, spec 006). But there is **no declared, provider-independent boundary** at
which extraction output — the structured result an OCR or LLM adapter produces
from a scanned or uploaded document — **legitimately enters the system**. Today
the engine's `T4RawInput` payload exists, but:

- there is **no ingress port** (interface/contract) through which an OCR/LLM
  adapter delivers extraction output inward. The dependency direction CLAUDE.md
  mandates (infrastructure → contract) is not expressible: a provider adapter
  should depend on a declared contract and **call** an ingress port whose
  implementation lives inward in a future application layer — today there is
  nothing for it to depend on or call;
- there is **no envelope** wrapping a raw payload with the provenance and
  attribution needed to route it safely: which firm, which **case**, which
  participant (participant identifiers are case-local, so a participant
  reference is never meaningful without its case), which tax year, from which
  source document, from which provider, at what confidence;
- there is **no single "untrusted extraction result" abstraction** that unifies
  "an extracted T4 slip" and "a non-slip evidence document" (initially
  medical-expense evidence) behind one document-kind discriminator;
- there is **no fail-closed, envelope-level validation** that rejects malformed,
  mis-attributed, wrong-year, year-incoherent, or empty extraction **before**
  anything treats it as tax input.

The risk without this boundary is precisely the one CLAUDE.md forbids: OCR/LLM
output leaking into the system as if it were tax truth — an unvalidated,
low-confidence, or wrong-year extraction silently becoming a filing input. The
Document Extraction Contract closes that gap: a deterministic, provider-
independent contract that carries extraction output as **explicitly untrusted
input**, validates its envelope well-formedness and the *presence* of its
attribution, and hands a well-formed-but-still-untrusted result to the
downstream processor — while never itself parsing a slip, inspecting or typing a
box value, mapping a box to a CRA line, or producing any tax truth.

**Tax year in scope: 2025 only.** An extraction envelope declares exactly one
intended tax year; the contract validates it against this spec's supported set
(2025) and **fails closed** on any other or absent year. For a T4 payload the
contract additionally requires the payload's own `T4RawInput.Year` to be present
and to **equal** the envelope year — a missing raw year or a mismatch fails
closed. Per CLAUDE.md there is no "latest"/"current" year and **no
adjacent-year fallback**: a 2024 or 2026 envelope is rejected, never coerced to
2025, and neither the envelope year nor the payload year is ever coerced.
Supporting years other than 2025 is out of scope (a new supported year is a
scope change, not a silent widening).

---

## Current state and reuse analysis

Grounded in the actual tree at `D:\Projects\TaxAI`, not memory. A grep for
`extraction` / `DocumentExtraction` across `src/` matches **no production code**
today (only CLAUDE.md, NEXT.md, and prior spec prose). There is no
`src/DocumentExtraction*` project.

### Reused unchanged (do NOT re-specify, duplicate, or modify)

- **`T4RawInput` / `T4RawBox`** (`src/TaxLogicEngine/Models/`) — already
  documented as "the already-extracted, structured input for a single slip…
  unvalidated upstream data." This **is** the T4 extraction payload. The
  Document Extraction Contract carries it verbatim as its T4-slip payload; it
  does not define a new slip payload type and does not alter these. It is
  **explicitly untrusted raw input and may contain an unmasked SIN inside
  Box 12**; spec 007 never inspects, parses, normalizes, redacts, or converts
  individual box values. `Boxes` remains a list (not a dictionary) so duplicate
  box codes survive to the engine that rejects them — the contract preserves
  that and never collapses them.
- **`ParseResult<T>`** (`Models/ParseResult.cs`) — the fail-closed result.
  Envelope validation returns `ParseResult<T>`: `Success` carries a well-formed
  (still untrusted) validated extraction and no errors; `Failure` carries ≥1
  error and no value; no partial state.
- **`ValidationError`** (`Models/ValidationError.cs`) — `{ Field; Message; }`,
  reused verbatim for every rejection reason. `Field` is always a **fixed field
  name** defined by this contract; `Message` never echoes raw payload values or
  caller-controlled identifiers.

### Downstream types this spec relies on but does NOT consume, create, or populate

- **`RecipientIdentifier`** (`Models/RecipientIdentifier.cs`) — remains a
  **downstream `T4SlipParser` result**. Converting Box 12 into a
  `RecipientIdentifier` is the existing parser's responsibility, exercised by
  the future T4 Case Processor. Spec 007 **does not create, populate, or expose
  a `RecipientIdentifier`** and introduces **no SIN property or SIN type** of
  its own: the raw payload is carried opaquely and omitted from all diagnostics.
- **T5 engine types (`T5RawInput`, `T5RawBox`, `T5SlipParser`, `T5LineMapper`,
  T5 mapping configs)** — remain unchanged in `TaxLogicEngine` but are **not
  consumed by spec 007**. T5 extraction support is **deferred to a future scope
  change** (human-approved MVP scope decision).
- **`SafeDiagnosticText`** (`src/TaxCases/Services/SafeDiagnosticText.cs`) —
  **not reused and not referenced.** This contract's diagnostics **omit**
  untrusted values entirely rather than echoing and redacting them, so no
  masking helper is needed here and no second masking helper is introduced. The
  `TaxCases` project is **not** a dependency of this feature.

### Not duplicated, and explicitly out of this feature

- **`T4SlipParser`, `T4LineMapper`, `MoneyRounding`, the T4 mapping JSON
  config, `TaxLineEntry`, `T4MappingResult`** — the contract does **not**
  parse, type, round, map, or calculate. It carries the raw payload untouched.
  Parsing and mapping a received slip is the future **T4 Case Processor** spec,
  which invokes the engine. This contract produces no `T4Slip`, no
  `TaxLineEntry`, and no tax truth.
- **Tax Case Core (`TaxCase`, `ReceivedEvidence`, `EvidenceDescriptor`,
  `DocumentCategory`, readiness, lifecycle)** — the contract does **not** mutate
  a case, attach evidence to a case, compute readiness, or reference the
  `TaxCases` project at all. It carries the attribution references (firm, case,
  participant, tax year, source reference) a downstream step needs, but
  constructing an `EvidenceDescriptor` and calling Tax Case Core is downstream
  work, not this contract. This feature **does not add, rename, or change any
  TaxCases public type.**

### Genuinely new (introduced by this feature)

A new provider-independent contract module containing:

- an **ingress port** interface (e.g. `IDocumentExtractionIngress`) defining the
  **inward application boundary** for extraction: a provider adapter depends on
  this contract and **calls** the ingress port; the port is **implemented by a
  future application layer**, not by the provider adapter. The
  DocumentExtraction module does not depend on provider infrastructure, and no
  OCR/LLM invocation lives inside it;
- an **untrusted extraction envelope** carrying a document identifier, a
  document-kind discriminator, the attribution tuple (**firm reference, case
  reference, participant reference, intended tax year**), provenance metadata
  (a required non-empty **source reference**, an optional provider label, an
  optional `decimal?` confidence), and — for the T4-slip kind only — a payload
  that is exactly a reused `T4RawInput` (generic evidence has **no separate
  payload**; its required source reference *is* the opaque reference to the
  stored document);
- a **deterministic envelope validator** that fails closed on malformed,
  mis-attributed, wrong-year, year-incoherent, or empty extraction and otherwise
  returns a well-formed-but-still-untrusted validated result.

Concrete type names, the module/project home, and file layout are Class B
`[ASSUMED]`. The module obeys CLAUDE.md's dependency rules: it depends inward
**only on `TaxLogicEngine`** (for `T4RawInput`, `ParseResult<T>`, and
`ValidationError`); it must **not** depend on `TaxCases`, ASP.NET Core, EF Core,
SQL, OCR/LLM/cloud/email SDKs, the filesystem, the network, or the system
clock. The concrete application-layer implementation of the ingress port and
all provider adapters remain **out of scope**.

---

## User stories

1. As an **integration engineer** wiring an OCR/LLM provider, I want a declared
   contract my adapter depends on and one **ingress port to call** so my adapter
   delivers extraction output inward through the application boundary, never by
   reaching into the engine or a case module — and never by having the domain
   depend on my provider code.
2. As an **engine maintainer**, I want every extraction to arrive as an
   explicitly **untrusted** envelope that the system validates for
   well-formedness and attribution-presence before anything treats it as tax
   input, so OCR/LLM output can never be mistaken for tax truth.
3. As a **compliance reviewer**, I want an extraction whose intended tax year is
   not the supported year — or whose T4 payload's own year is missing or differs
   from the envelope's — to be **rejected**, never silently coerced, so a 2024
   or 2026 extraction (or a year-incoherent one) can never enter a 2025
   engagement.
4. As a **firm administrator**, I want every extraction to carry an explicit
   firm reference, **case reference**, and participant reference and be rejected
   when any is missing — because a participant identifier is case-local and
   meaningless without its case — so extracted content is never orphaned or
   mis-routed. (Verifying that the case belongs to the firm, the participant
   belongs to the case, and the caller is authorized is a **future application
   workflow**, not this contract.)
5. As a **compliance reviewer**, I want the untrusted raw payload — which may
   contain an unmasked SIN in Box 12 — to be **omitted entirely** from every
   `ToString()`, diagnostic, validation error, and supported direct JSON
   serialization the contract produces, so extraction diagnostics can never leak
   payload content, masked or otherwise.
6. As the **downstream T4 Case Processor** author, I want a well-formed
   validated extraction to hand me the raw T4 payload programmatically, plus its
   firm/case/participant/tax-year attribution, without the contract having
   inspected, parsed, typed, or mapped anything, so I remain the single place
   slip parsing (including Box 12 → `RecipientIdentifier`) and box→line mapping
   happen.
7. As a **preparer**, I want a medical-expense receipt or similar evidence
   document to enter as a generic-evidence extraction carrying only an opaque
   source reference — no parsed medical fields, amounts, or receipt contents —
   so unstructured evidence is registered without anything pretending to have
   extracted tax facts from it.
8. As an **engine maintainer**, I want envelope validation to be deterministic
   and provider-independent (no clock, no network, no OCR/LLM call, no I/O) so
   the same extraction envelope always produces the same accept/reject result.

---

## Functional requirements

`[design]` marks a decision that depends on no CRA fact. `[reuse]` marks reuse of
an existing type. `[boundary]` marks a module-boundary invariant from CLAUDE.md.

### The ingress port and untrusted envelope

1. **[design/boundary]** The feature SHALL define an **ingress port**
   (interface; a name such as `IDocumentExtractionIngress`) as the **inward
   application boundary** for extraction: a provider adapter **depends on the
   contract** and **calls** the ingress port; the port is **implemented by a
   future application layer**, not by the provider adapter. The
   DocumentExtraction module SHALL NOT depend on provider infrastructure. The
   concrete application implementation and all provider adapters are out of
   scope. The port is the *only* declared way extraction output enters
   (`Infrastructure → contract`, never the reverse).
2. **[design]** An **extraction envelope** SHALL carry: a non-empty **document
   identifier**; a **document-kind** discriminator; the **attribution tuple** —
   a **firm reference (FirmId)**, a **case reference (CaseId)**, a
   **participant reference (ParticipantId)**, and an **intended tax year** —
   noting that ParticipantId is **case-local and never meaningful without
   CaseId**; **provenance** (a required non-empty **source reference**, an
   optional provider label, and an optional confidence); and — for the T4-slip
   kind only — a **payload**.
3. **[design]** Supported document kinds for the MVP SHALL be exactly **T4
   slip** and a **generic evidence document** (initially medical-expense
   evidence). The T4-slip payload SHALL be exactly a reused `T4RawInput`. A
   generic-evidence extraction has **no separate payload**: its required source
   reference *is* the opaque reference to the stored document, and it carries
   **no parsed medical fields, amounts, or receipt contents**. An
   **unknown/unsupported document kind** (including T5, which is deferred)
   SHALL fail closed with a structured error naming the kind.
4. **[design]** The envelope and its validated result SHALL represent
   **untrusted** input: the contract SHALL NOT parse a slip; SHALL NOT inspect,
   parse, normalize, redact, or convert **individual T4 box values** (in
   particular it SHALL NOT convert Box 12 into a `RecipientIdentifier` — that
   remains the existing `T4SlipParser`'s responsibility); SHALL NOT type or
   coerce a box value, map a box to a CRA line, round currency, or compute any
   tax quantity. A validated extraction SHALL expose the **raw payload**, not a
   `T4Slip`, `TaxLineEntry`, or any parsed/mapped tax value. Extraction is
   never tax truth.

### Attribution (presence-only validation)

5. **[design]** The contract SHALL validate that FirmId, CaseId,
   ParticipantId, the document identifier, and the source reference are
   **present and non-blank only** (null, empty, or whitespace-only fails
   closed). It SHALL NOT authenticate a caller, SHALL NOT query Tax Case Core,
   and SHALL NOT be claimed to prove firm/case/participant membership. A
   **future application workflow** must verify that the case belongs to the
   firm, the participant belongs to the case, and the caller is authorized —
   none of that is this contract.

### Envelope validation (fail closed)

6. **[reuse]** Envelope validation SHALL return `ParseResult<T>`: on success a
   well-formed (still untrusted) validated extraction and **no** errors; on
   rejection ≥1 `ValidationError` and **no** value, mutating nothing. There is
   no partial/best-effort extraction.
7. **[design]** A **missing or blank document identifier** SHALL fail closed
   with a structured error and produce no validated extraction.
8. **[design]** A **missing or blank firm reference (FirmId)** SHALL fail
   closed with a structured error (attribution integrity).
9. **[design]** A **missing or blank case reference (CaseId)** SHALL fail
   closed with a structured error. ParticipantId is case-local, so an envelope
   without a case can never be routed.
10. **[design]** A **missing or blank participant reference (ParticipantId)**
    SHALL fail closed with a structured error (participant attribution is
    required for every MVP document kind).
11. **[design]** A **missing or blank source reference** SHALL fail closed with
    a structured error **for every document kind**: for generic evidence it is
    the only reference to the stored document; for T4 slip it is still
    required alongside the payload.
12. **[design/verified-scope]** An **absent envelope tax year** SHALL fail
    closed; an **unsupported envelope tax year** (any value other than the
    supported year, 2025) SHALL fail closed. The year SHALL NEVER be coerced to
    2025 and **no adjacent-year fallback** is permitted (CLAUDE.md).
    Supported-year policy is a product-scope policy of this spec, validated as
    an explicit year — the contract does **not** inspect `TaxLogicEngine`
    mapping files to decide year support.
13. **[design]** **T4 tax-year coherence.** For a T4-slip payload,
    `T4RawInput.Year` SHALL be present and SHALL equal the envelope's intended
    tax year. A **missing raw year** SHALL fail closed; an **envelope/payload
    year mismatch** SHALL fail closed. Neither value is coerced and no
    adjacent-year fallback is allowed. This comparison is **tax-year isolation
    only** — it does not inspect or validate box codes or box values.
14. **[design]** A **payload/kind mismatch** SHALL fail closed: a T4-slip
    envelope whose payload is absent or is not a `T4RawInput`, or a
    generic-evidence envelope carrying any payload, SHALL be rejected with a
    structured error.
15. **[design]** An **empty T4 extraction** SHALL fail closed, defined
    mechanically as: `Boxes` is empty **and** all supported top-level identity
    text fields of `T4RawInput` (`EmployerName`, `EmployerAddress`,
    `EmployeeName`, `EmployeeAddress`) are null, empty, or whitespace. This
    definition makes **no judgement about whether individual box codes or
    values are valid** — that remains the engine's downstream concern.
16. **[design]** **Confidence** SHALL be represented as `decimal? Confidence`:
    **absent is allowed**; values from `0m` through `1m` **inclusive** are
    accepted; values outside that range SHALL fail closed as malformed
    provenance. There is **no low-confidence rejection threshold** — confidence
    is carried as **untrusted metadata only**, and the contract SHALL NOT
    reject a well-formed extraction merely because confidence is low. (`decimal`
    avoids NaN, infinity, and floating-point equality ambiguity.)
17. **[design]** Validation SHALL NOT collapse, deduplicate, or reorder the raw
    payload's boxes. Duplicate box codes in a `T4RawInput` SHALL survive
    validation untouched, so the engine's existing duplicate-box rejection
    still fires downstream. The contract itself makes no judgement about box
    validity.

### PII and the raw payload (omission, not redaction)

18. **[design]** The reused `T4RawInput` is **explicitly untrusted raw input
    and may contain an unmasked SIN inside Box 12**. The contract SHALL
    introduce **no new SIN property and no new SIN type**, and SHALL NOT
    create or populate a `RecipientIdentifier` (that remains a downstream
    `TaxLogicEngine` parser result). The raw T4 payload MAY be retained
    unchanged in memory.
19. **[design]** The raw payload SHALL be **omitted entirely** from every
    DocumentExtraction `ToString()`, diagnostic, error, and supported direct
    JSON serialization. No box value, identity text, or other payload content —
    masked or unmasked — appears in any of them. Because diagnostics **omit**
    untrusted values rather than echo-and-redact them, **no masking helper is
    introduced or referenced** (no `SafeDiagnosticText` dependency, no second
    masking implementation).
20. **[design]** Validation errors SHALL identify fields using **fixed field
    names** defined by this contract and SHALL NEVER echo raw payload values or
    caller-controlled identifiers (document id, firm/case/participant
    references, source reference, provider label) in any message.
21. **[design]** A validated extraction MAY expose the raw payload
    **programmatically** to the downstream processor, but SHALL never present
    it as tax truth.

### Boundary, determinism, and dependency direction

22. **[boundary]** The contract module SHALL be deterministic and
    provider-independent: no system-clock read, no network, no filesystem, no
    database, no OCR/LLM invocation, no current-user/environment read. The same
    envelope SHALL always produce the same accept/reject `ParseResult`.
23. **[boundary]** The contract SHALL contain **no** CRA line number literal,
    **no** box→line mapping, **no** box-value typing/parsing, and **no** tax
    calculation. All such logic remains in `TaxLogicEngine` and its JSON config.
24. **[boundary]** Dependencies point inward only: the module SHALL depend on
    **`TaxLogicEngine` only** (for `T4RawInput`, `ParseResult<T>`, and
    `ValidationError`). It SHALL NOT depend on `TaxCases`, ASP.NET Core, EF
    Core, SQL, OCR/LLM/cloud/email SDKs, or any I/O making its result
    non-deterministic. `TaxLogicEngine` SHALL NOT depend on this module.
25. **[design]** The contract SHALL NOT mutate a `TaxCase`, attach evidence,
    compute readiness, or add/rename/change any TaxCases public type. Bridging
    a validated extraction into Tax Case Core evidence — including verifying
    firm/case/participant membership and caller authorization — is downstream
    application work.

---

## Out of scope

Each item is explicitly excluded from this spec:

- **T5 extraction support** — deferred to a future scope change. Existing T5
  engine types (`T5RawInput`, `T5RawBox`, parser, mapper, configs) remain
  unchanged and are **not consumed** by spec 007; a T5 document kind fails
  closed as unsupported.
- **Actual OCR or LLM extraction / any provider adapter** — this feature
  defines the contract and ingress port; a concrete OCR/LLM adapter that
  *calls* the port is a separate integration and is out of scope (NEXT.md: "OCR
  or LLM integration" is explicitly not now).
- **The concrete application-layer implementation of the ingress port** — the
  contract declares the port; the application workflow that implements it
  (including verifying that the case belongs to the firm, the participant
  belongs to the case, and the caller is authorized) is future work.
- **Authenticating callers or proving firm/case/participant membership** — the
  contract validates presence/non-blankness of references only; it never
  queries Tax Case Core.
- **Slip parsing, box typing, Box 12 → `RecipientIdentifier` conversion,
  box→CRA-line mapping, rounding, tax calculation** — remain in
  `TaxLogicEngine` and the future **T4 Case Processor**; this contract produces
  no `T4Slip`, no `TaxLineEntry`, no tax truth.
- **Parsed content for generic evidence** — no medical fields, amounts, dates,
  or receipt contents are extracted, carried, or validated; generic evidence is
  an opaque source reference only.
- **Attaching evidence to a `TaxCase` / computing readiness / any TaxCases
  mutation or reference** — downstream; no TaxCases public type is added,
  changed, or depended on.
- **A masking helper** — diagnostics omit untrusted values entirely rather than
  echoing and redacting them, so no `SafeDiagnosticText` reuse, relocation, or
  reimplementation occurs here.
- **A numeric confidence acceptance threshold / auto-rejecting low-confidence
  extraction** — no such threshold exists; confidence is untrusted metadata
  (requirement 16). If the product later wants hard gating, that is a follow-up
  decision.
- **Document storage / file bytes / upload / MIME handling / image processing**
  — the envelope carries an opaque source reference, not file bytes.
- **Batch/multi-document ingestion, retries, deduplication across documents** —
  the contract validates one envelope per call; duplicate-evidence detection
  across a case is Tax Case Core's existing responsibility.
- **Persistence, queuing, or delivery** of extraction results.
- **Database / API host / frontend** of any kind.
- **NETFILE / EFILE / CRA submission.**
- **Any CRA eligibility, definition, box, line, rate, threshold, or deadline** —
  none is asserted; T4 slip semantics are reused unchanged from specs 003–004.
- **Tax years other than 2025.**
- **Any accidental change to existing `TaxLogicEngine` or `TaxCases` behaviour**
  — the full existing test suite must remain green (see AC-29).

---

## Acceptance criteria

Each criterion is a single mechanically-checkable test (Given/When/Then).

### Happy path

1. **Valid T4 extraction envelope.**
   Given an envelope with a non-empty document id, kind T4 slip, a non-empty
   firm reference, a non-empty case reference, a non-empty participant
   reference, intended tax year 2025, a non-empty source reference, and a
   `T4RawInput` payload whose `Year` is 2025 and which carries at least one box,
   When it is validated, Then it succeeds with zero errors and yields a
   validated extraction exposing that raw `T4RawInput` payload unchanged.

2. **Valid generic-evidence envelope.**
   Given an otherwise-valid generic-evidence envelope (e.g. medical-expense
   evidence) with a non-empty source reference and no payload,
   When it is validated, Then it succeeds with zero errors and the source
   reference is exposed unchanged, with no parsed content of any kind.

### Fail-closed rejections (one rule per criterion)

3. **Missing/blank document identifier.**
   Given an otherwise-valid envelope whose document id is null, empty, or
   whitespace-only, When validated, Then it fails closed with a structured error
   naming the document-id field and yields no validated extraction.

4. **Unknown/unsupported document kind (including T5).**
   Given an envelope whose document kind is outside the supported set
   (including a T5-slip kind, which is deferred),
   When validated, Then it fails closed with a structured error naming the
   kind field.

5. **Missing/blank firm reference.**
   Given an otherwise-valid envelope with a null/blank firm reference,
   When validated, Then it fails closed with a structured error naming the
   firm-reference field.

6. **Missing/blank case reference.**
   Given an otherwise-valid envelope with a null/blank case reference,
   When validated, Then it fails closed with a structured error naming the
   case-reference field and yields no validated extraction.

7. **Missing/blank participant reference.**
   Given an otherwise-valid envelope with a null/blank participant reference,
   When validated, Then it fails closed with a structured error naming the
   participant-reference field.

8. **Missing/blank source reference (every document kind).**
   Given an otherwise-valid T4-slip envelope with a null/blank source
   reference, and separately an otherwise-valid generic-evidence envelope with
   a null/blank source reference, When each is validated, Then each fails
   closed with a structured error naming the source-reference field.

9. **Absent envelope tax year.**
   Given an otherwise-valid envelope with no intended tax year,
   When validated, Then it fails closed with a tax-year error and is never
   coerced to 2025.

10. **Unsupported envelope tax year (year isolation).**
    Given an otherwise-valid envelope with intended tax year 2024 (and again
    2026), When validated, Then each fails closed with a tax-year error; the
    year is never coerced to 2025 and no adjacent-year fallback is applied.

11. **Missing `T4RawInput.Year`.**
    Given an otherwise-valid T4-slip envelope whose `T4RawInput.Year` is null,
    When validated, Then it fails closed with a structured error naming the
    payload-year field; the year is never defaulted or coerced.

12. **Envelope/payload year mismatch.**
    Given an otherwise-valid T4-slip envelope with intended tax year 2025 whose
    `T4RawInput.Year` is 2024, When validated, Then it fails closed with a
    structured error naming the year-coherence rule; neither value is coerced
    and no adjacent-year fallback is applied.

13. **Payload/kind mismatch.**
    Given a T4-slip envelope whose payload is absent (and, separately, a
    generic-evidence envelope carrying a `T4RawInput` payload),
    When validated, Then each fails closed with a structured error and yields
    no validated extraction.

14. **Empty T4 extraction.**
    Given a T4-slip envelope whose `T4RawInput` has an empty `Boxes` list and
    whose `EmployerName`, `EmployerAddress`, `EmployeeName`, and `EmployeeAddress`
    are each null, empty, or whitespace, When validated, Then it fails closed
    with a structured error (no judgement is made about box-code or box-value
    validity).

15. **Malformed confidence.**
    Given an otherwise-valid envelope whose provenance confidence is `-0.1m`
    (and again `1.1m`), When validated, Then each fails closed with a
    structured error naming the confidence field.

### Boundaries

16. **Confidence at the range bounds is accepted.**
    Given otherwise-valid envelopes with provenance confidence exactly `0m`,
    exactly `1m`, and absent (`null`), When each is validated, Then each
    succeeds (a low or boundary confidence does not reject a well-formed
    extraction; no low-confidence threshold exists).

17. **Whitespace-only references are treated as blank.**
    Given an otherwise-valid envelope whose document id / firm reference / case
    reference / participant reference / source reference is a whitespace-only
    string, When validated, Then it fails closed exactly as for the empty case.

18. **Duplicate box codes survive validation.**
    Given a T4-slip envelope whose `T4RawInput.Boxes` contains the same box code
    twice, When validated, Then it succeeds and the validated extraction's
    payload still contains both entries in order (validation neither collapses
    nor reorders them; the engine's duplicate-box rejection remains a
    downstream concern).

19. **Empty envelope input.**
    Given an envelope with no document id, no firm reference, no case
    reference, no participant reference, no source reference, and no tax year,
    When validated, Then it fails closed with one structured error per missing
    required field and throws no exception.

### Untrusted-input / no-tax-truth

20. **A validated extraction produces no tax truth.**
    Given a valid T4-slip envelope, When it is validated, Then the accepted
    result exposes the raw `T4RawInput` programmatically and is **not** a
    `T4Slip`, `T4MappingResult`, or `TaxLineEntry`, and no box value has been
    inspected, typed, parsed, normalized, redacted, converted, mapped, or
    rounded.

21. **No parsing/mapping/calculation is performed by validation.**
    Given a T4-slip envelope whose `T4RawInput` contains a box value that the
    engine would reject as malformed, When the envelope is validated, Then
    validation still succeeds at the envelope level (it makes no judgement
    about box validity) and performs no parse, map, or calculation.

### PII / raw-payload omission

22. **The raw payload is omitted from every rendered or serialized output.**
    Given a valid T4-slip envelope whose `T4RawInput` contains the SIN
    `123456789` as the value of Box 12 and populated identity text fields,
    When the envelope's and the validated extraction's `ToString()`, every
    diagnostic, every `ValidationError`, and every supported direct JSON
    serialization are produced, Then none of them contains any box value, any
    identity text, or any 9-consecutive-digit substring — the payload is
    omitted entirely, not masked.

23. **A rejection never echoes payload values or caller-controlled
    identifiers.**
    Given an envelope that fails closed and whose `T4RawInput` contains a SIN
    in Box 12 and whose caller-supplied references contain distinctive marker
    strings, When the failure's `ValidationError`s are read, Then every
    `Field` is one of the contract's fixed field names and no message contains
    any raw payload value or any caller-supplied identifier (document id,
    firm/case/participant reference, source reference, provider label).

24. **No new SIN representation and no `RecipientIdentifier` construction.**
    Given the compiled contract assembly, When its public surface and emitted
    metadata are inspected, Then it declares no SIN property or SIN type and
    contains no construction or population of `RecipientIdentifier` (which
    remains a downstream `T4SlipParser` result).

### Tax-year isolation

25. **A 2025 contract never adopts another year's extraction.**
    Given the supported year is 2025, When a 2024-intended envelope, a
    2026-intended envelope, and a 2025 envelope carrying a 2024 `T4RawInput`
    are validated, Then all fail closed and no validated extraction is ever
    produced under year 2025 for them; a validated 2025 extraction only ever
    carries envelope year 2025 with a matching payload year.

### Data-driven / no hardcoded CRA facts

26. **No CRA line number, box→line mapping, or mapping-file read in the
    module.**
    Given the compiled contract assembly, When its source/emitted metadata is
    inspected, Then it contains no CRA line number literal, no box→line
    mapping, no box-value typing/parsing, no tax calculation, and no read of a
    `TaxLogicEngine` mapping/config file — such logic lives only in
    `TaxLogicEngine` and its JSON config.

### Determinism / boundary

27. **No clock, network, filesystem, database, or OCR/LLM access.**
    Given the contract source/assembly, When inspected, Then it contains no
    read of the system clock (`DateTime.Now`/`UtcNow`), no network, no
    filesystem, no database, and no OCR/LLM invocation; any timestamp appears
    only as an externally supplied input.

28. **Determinism.**
    Given the same envelope validated twice, When the two `ParseResult`s are
    compared, Then they are equal (same success/failure, same errors, same
    exposed payload).

### Reuse / regression (existing behaviour stays green)

29. **Existing engine and case behaviour unchanged; no TaxCases dependency.**
    Given `dotnet test TaxAI.sln`, When the full suite runs after this feature
    is added, Then every existing `TaxLogicEngine.Tests` and `TaxCases.Tests`
    test remains green, no existing public tax type is added, renamed, or
    changed by this feature, and the DocumentExtraction project references
    **only** `TaxLogicEngine` (no `TaxCases` project reference).

30. **Reused payload and result types; nothing duplicated.**
    Given the T4 payload and the validation result, When their types are
    inspected, Then the T4 payload is the existing `T4RawInput` (no new slip
    payload type), validation returns the existing `ParseResult<T>` with
    `ValidationError`s, no T5 type is consumed, no masking helper exists in the
    module, and no duplicate of any reused type is introduced.

---

## Open questions

### Blocking Class A questions

**None.** Every genuine tax semantic (what box codes/values are valid, what a
valid SIN is, box→line mapping, eligibility) is **delegated** to the already-
verified `TaxLogicEngine` and the future T4 Case Processor, not decided here, so
no unverifiable tax fact was assumed and no canada.ca claim is newly asserted.
The remaining decisions are structural/scope (Class B) and are listed below.

One design choice that borders on "what must be rejected" — **whether low
confidence should reject an extraction** — was resolved *conservatively without
inventing a fact* and has since been **confirmed by human review**: the contract
carries confidence as untrusted `decimal?` metadata and does **not** define a
numeric threshold (requirement 16, and out-of-scope). This does not weaken any
protection: CLAUDE.md already forbids treating extraction as tax truth, so
low-confidence data still cannot become a filing input unvalidated.

### Resolved by human review (2026-07-13 revision — no longer assumptions)

- **MVP document kinds are T4 slip and generic evidence (initially
  medical-expense evidence).** T5 extraction is deferred to a future scope
  change; T5 engine types remain unchanged and unconsumed.
- **Attribution is case-scoped:** FirmId, CaseId, ParticipantId, and intended
  TaxYear are all required and non-blank; ParticipantId is case-local. The
  contract validates presence only; membership and authorization checks belong
  to a future application workflow.
- **Ingress-port direction:** the port (e.g. `IDocumentExtractionIngress`) is
  an inward application boundary that provider adapters *call*; it is
  implemented by a future application layer, never by the provider adapter.
- **Raw-payload/PII semantics:** no new SIN property or type; the raw
  `T4RawInput` (which may contain an unmasked SIN in Box 12) is retained
  unchanged in memory, omitted entirely from all rendered/serialized output,
  and exposed programmatically only; errors use fixed field names and never
  echo payload values or caller-controlled identifiers; `RecipientIdentifier`
  stays a downstream parser result; no `TaxCases`/`SafeDiagnosticText`
  dependency and no new masking helper.
- **One required SourceReference per envelope** for every kind; for generic
  evidence it *is* the opaque stored-document reference and there is no
  separate payload.
- **Tax-year coherence:** envelope TaxYear required and equal to 2025;
  `T4RawInput.Year` required and equal to the envelope year; missing or
  mismatched years fail closed with no coercion and no adjacent-year fallback.
- **Confidence is `decimal? Confidence`** in `[0m, 1m]` inclusive; absent
  allowed; out-of-range fails closed; no low-confidence threshold; untrusted
  metadata (decimal avoids NaN/infinity/float-equality ambiguity).
- **Empty T4 extraction** is defined mechanically (empty `Boxes` and all four
  identity text fields blank) and fails closed, with no judgement on box
  codes/values.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **Feature name / slug.** The `/spec` command was invoked without a
  feature argument; "Document Extraction Contract" and slug
  `007-document-extraction-contract` were taken from CLAUDE.md's / NEXT.md's
  stated next feature.
- `[ASSUMED]` **Module home / project:** a new `src/DocumentExtraction/` project
  (name and location assumed), per CLAUDE.md's "future modules get
  `src/<Module>/` homes when built," depending inward on `TaxLogicEngine` only.
- `[ASSUMED]` **Type names** (e.g. an `IDocumentExtractionIngress` port, an
  `ExtractionEnvelope`, an `ExtractedDocumentKind` discriminator, an
  `ExtractionProvenance` record, an `ExtractionValidator`, and a
  `ValidatedExtraction` result) follow existing naming conventions; exact names
  are for `/plan`.
- `[ASSUMED]` **Firm/case/participant/document references and the source
  reference are opaque, caller-supplied, non-empty values;** the contract
  validates presence and non-blankness only and never generates an identifier
  itself (generation would be a non-deterministic side effect).
- `[ASSUMED]` **One envelope per validation call;** batch ingestion and
  cross-document deduplication are out of scope (Tax Case Core already rejects
  duplicate evidence ids).
- `[ASSUMED]` **Spec folder** is `specs/007-document-extraction-contract/`
  (001–006 taken).

---

## CRA sources

**No new external tax facts are asserted by this PRD.** The Document Extraction
Contract introduces no CRA box number, line number, rate, threshold, limit,
deadline, gross-up, credit percentage, or eligibility rule. It carries a **raw,
unvalidated** T4 payload and defers all parsing, typing, box→line mapping, and
calculation to `TaxLogicEngine` and the future T4 Case Processor. Generic
evidence (initially medical-expense evidence) is carried as an opaque source
reference only — no medical-expense eligibility, amount, or receipt rule is
asserted or implemented. The only tax facts this PRD *relies on* already exist
and were verified against canada.ca in prior, cited specs, and are reused
unchanged rather than re-asserted here:

| Reused fact (not re-asserted here) | Verified in | Source | Tax year | Retrieved |
|---|---|---|---|---|
| T4 slip structure / box codes (carried only as an untrusted `T4RawInput` payload; not parsed or mapped here) | spec 003 `prd.md`, spec 004 `prd.md` | canada.ca T4 employer & individual pages (URLs recorded in those PRDs) | 2025 | 2026-07-09 / 2026-07-11 |

T5 slip facts (specs 001–002) are **not relied on** by this revision: T5
extraction is deferred and no T5 type is consumed.

Because this PRD asserts no new tax fact, canada.ca was not re-queried for a new
claim; no source conflict was resolved by the author's own judgement, and no
unverified tax fact entered the PRD. The moment a future spec (e.g. the T4 Case
Processor) asserts a box→line or eligibility fact, that spec must verify it
against canada.ca at that time.
