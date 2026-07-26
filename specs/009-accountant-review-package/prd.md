# PRD 009 — Accountant Review Package

> **STATUS: NOT READY FOR `/plan`.**
> One **blocking Class A question (A1)** is unresolved — see
> [Open questions](#open-questions). Functional requirement **FR-31 is
> deliberately left unspecified** because answering it would be a new tax
> judgement this agent is not entitled to make. `/plan` must not build
> architecture on this document until A1 is answered by a human.

## Feature classification

**Class: `tax-domain`.**

The Accountant Review Package is the **last human gate before a filing**. It
assembles what a named human accountant looks at, and records the decision that
human made about it. Everything downstream of that decision is a real return for
a real taxpayer.

It is `tax-domain`, not `platform`, because:

- it **presents CRA line entries and taxpayer amounts** — the exact material a
  filing error is made of;
- it handles **taxpayer PII**, including the Box 12 SIN reached through
  `T4Slip.Sin`, plus free-text reviewer notes that can contain anything a human
  types;
- it enforces **tax-year isolation** (2025 only) and **firm / case / participant
  isolation** at a new seam — a package that merged two participants' slips
  would merge two people's returns;
- it must decide what a preparer is shown, and (FR-31, blocked) what a preparer
  may sign off on, when a populated T4 box has no CRA line mapping — which spec
  008 explicitly recorded as **"a new tax judgement … deferred, in full, to the
  Accountant Review Package"** (`specs/008-t4-case-processor/prd.md`, Open
  questions).

Even the parts that look like plumbing (module home, type names, immutability
shape) mix with tax semantics, and the classification rule is explicit: a mixed
feature is `tax-domain`, and an uncertain feature is `tax-domain`. Every
tax-domain protection therefore applies as written — explicit tax-year scope,
canada.ca as the only tax source, one negative acceptance criterion per
fail-closed rule, PII criteria, tax-year-isolation criteria, and a
data-driven-mapping criterion.

Following spec 008's precedent, this PRD deliberately asserts **no new CRA
fact** — no box number-to-line assignment, line number, rate, threshold, limit,
deadline, gross-up, credit percentage, or eligibility rule. **No CRA line number
appears anywhere in this document**, by design. See `## CRA sources`.

---

## Problem

**Tax year in scope: 2025 only.**

Spec 008 completed the machine half of the pipeline. `T4CaseProcessor.Process`
now turns one already-validated T4 extraction into a `ProcessedT4Extraction`
carrying a parsed `T4Slip`, a `T4MappingResult` (CRA line entries plus
`UnmappedBoxCodes`), and the firm / case / participant / year attribution it came
in with.

Nothing consumes it. Concretely, today:

1. **There is no artifact a human reviews.** A `ProcessedT4Extraction` is a
   single-extraction engine output. An accountant preparing one participant's
   return holds several of them (one per T4 the client had), plus the physical
   slips to compare against. Nothing gathers those into one reviewable unit, so
   the first thing anyone building a screen, a report, or a checklist will do is
   hand-assemble that unit at their own call site — which is how two different
   "review views" get built, one of which quietly drops something.

2. **There is no isolation guard on assembly.** `ProcessedT4Extraction` echoes
   attribution and — by its own documentation — **verifies none of it**. Nothing
   stops a caller collecting the taxpayer's T4 and the spouse's T4 into one
   bundle. Spec 006 puts the taxpayer, spouse/common-law partner, and dependants
   on the *same* `TaxCase`, so two participants' processed slips are routinely in
   the same hand at the same time. A bundle that silently spans participants,
   cases, firms, or tax years is not a display bug; it is somebody else's income
   on somebody's return.

3. **There is no record of who decided what, on exactly what content.** "Real
   people file real returns from this output" (CLAUDE.md). Today there is no
   immutable artifact saying *this* named reviewer accepted *this* exact parsed
   slip and *these* exact line entries. Without it, an approval is a claim about
   nothing in particular, and a later change is undetectable.

4. **Populated boxes with no CRA line mapping have no owner.** Spec 004
   deliberately leaves a set of T4 box codes out of
   `Mappings/T4LineMap.2025.json`, and `T4LineMapper` surfaces them in
   `T4MappingResult.UnmappedBoxCodes` rather than dropping them. Spec 008 passed
   that list through unchanged, took no position, and named this feature as where
   the position gets taken. **That position is FR-31, and it is blocked on a
   human — see Class A question A1.** It is not a cosmetic question: the recorded
   deferral list in `src/TaxLogicEngine/Mappings/README.md` mixes box codes whose
   documented rationale is "nothing to report" with box codes whose documented
   rationale is "reported, but via a schedule or a calculation this engine does
   not yet do." Treating the whole list as informational and treating the whole
   list as blocking are both wrong, and picking either would be this agent
   inventing preparer duty.

**Tax-year scope.** Every collaborating component is 2025-only and says so in
exactly one module-local place: `SupportedTaxYears` (spec 006),
`SupportedExtractionTaxYears` (spec 007), `SupportedProcessingTaxYears`
(spec 008), `T4SlipParser`'s expected year, and the single embedded
`Mappings/T4LineMap.2025.json`. This feature adds one more module-local policy
(**2025 only**) and **fails closed** on anything else: no "current" year, no
adjacent-year fallback, no coercion, no system-clock read, and no inference of
supported years by inspecting mapping resources. A package may never span two
tax years, and a year with no policy entry is rejected, never approximated.

---

## Current state and reuse analysis

Grounded in the actual tree at `D:\Projects\TaxAI` on `master`, read directly
during this analysis — not memory, and not a prior PRD taken as a source.

A grep of `src/**/*.cs` for `ReviewPackage`, `SignOff`, `Attestation`,
`Approval`, `Approve`, and `Reviewer` returns **exactly one match**, an unrelated
comment in `src/DocumentExtraction/Models/ExtractedDocumentKind.cs`. No review,
sign-off, approval, or presentation type exists in any module. A grep of
`specs/*/prd.md` shows "Accountant Review Package" named as *future work* in
`NEXT.md`, root `CLAUDE.md`, and specs 006 and 008, and **implemented nowhere**.

`TaxAI.sln` today declares eight projects: `TaxLogicEngine`, `TaxCases`,
`DocumentExtraction`, `CaseProcessing`, and their four test projects. (Per root
`CLAUDE.md`, no test may assert a *total* project count for the solution.)

### Reused unchanged (do NOT re-specify, duplicate, modify, or wrap)

| Existing type | Where | Role in this feature |
|---|---|---|
| `ProcessedT4Extraction` | `src/CaseProcessing/Models/` | The **only** input. Its constructor is `internal` to `CaseProcessing`, so this module cannot fabricate one — possessing one *is* the proof the whole spec 007 + spec 008 pipeline succeeded. Its `DocumentId`, `FirmId`, `CaseId`, `ParticipantId`, `TaxYear`, `SourceReference`, `Slip`, and `Mapping` are read; none is altered. |
| `T4Slip` | `src/TaxLogicEngine/Models/` | The parsed slip, carried into the package by reference. **Never constructed, never copied field-by-field, never re-parsed.** |
| `T4MappingResult` / `TaxLineEntry` | `src/TaxLogicEngine/Models/` | The CRA line entries and `UnmappedBoxCodes`, carried through **verbatim, in the engine's own order**. `TaxLineEntry.SourceBoxCodes` is the existing, SIN-free provenance the reviewer needs; no new provenance type is invented. |
| `T4SlipBoxAmounts.Enumerate` | `src/TaxLogicEngine/Services/` | The **single existing place** that knows box code → typed `T4Slip` property, and the exact flattening `T4LineMapper` consumed. Reused to present "what the mapper saw", so the review view and the mapping input cannot drift. It emits no non-amount box (no SIN, no Box 54/50/10/29/45, no exempt indicators). |
| `RecipientIdentifier` | `src/TaxLogicEngine/Models/` | The **only** SIN representation in this feature, reached only via `T4Slip.Sin`. Masks to the last 3 digits. Never constructed here. |
| `ParseResult<T>` | `src/TaxLogicEngine/Models/` | The fail-closed result of assembly and of recording a decision. Success carries a value and no errors; failure ≥1 error and no value; no partial state. |
| `ValidationError` | `src/TaxLogicEngine/Models/` | Every rejection reason. |
| `MoneyRounding` | `src/TaxLogicEngine/Services/` | Reused **not at all, not even indirectly**. This module performs no arithmetic; every amount it shows was already rounded by the engine upstream. |

### Relied on but explicitly NOT consumed, created, mutated, or referenced

- **`TaxCases` — the whole module.** No project reference in either direction.
  This feature does not mutate a `TaxCase`, does not transition `CaseStatus` (in
  particular it does **not** drive `InReview → Completed`), does not construct an
  `EvidenceDescriptor`, and does not emit a `CaseChangeRecord`. Spec 006 owns
  case lifecycle and it keeps owning it; a package is a value handed to the
  application workflow, which is where a case transition belongs.
- **`ReadinessCalculator` / `ReviewReadiness` / `ReadinessReasonKind`**
  (`src/TaxCases/`) — **not reused, not duplicated, and not competed with.** Spec
  006 already derives "is this engagement complete enough for a human to look at
  it" from the case checklist. This feature answers a different question — "here
  is the slip-level content that human looks at, and here is what they decided" —
  and must not grow a second, contradictory readiness signal.
- **`SafeDiagnosticText`** (`src/TaxCases/Services/`) — not reused, not
  referenced, not reimplemented. PII here is handled by **masking** (the parsed
  SIN, via `RecipientIdentifier`) and by **omission** (every caller-controlled
  string, including the reviewer note). **No second masking helper is
  introduced.**
- **`T4SlipParser`, `T4LineMapper`, `IT4LineMapProvider`,
  `EmbeddedT4LineMapProvider`, `T4LineMap`, `T4LineMappingConfig`,
  `Mappings/T4LineMap.2025.json`** — **not invoked, not read, not referenced.**
  This module runs **no engine service that produces tax truth**. It cannot
  produce a different tax answer than spec 008 did, because it never asks the
  question again. (`T4SlipBoxAmounts` is the single deliberate exception: a pure
  flattener that contains no CRA line number and performs no mapping.)
- **`DocumentExtraction`** — no direct project reference. Everything this feature
  needs from an extraction already sits on `ProcessedT4Extraction`. The raw
  `T4RawInput` is unreachable from here by spec 008's design, and stays that way.
- **All T5 types** — unchanged and not consumed. T5 extraction does not exist
  (`ExtractedDocumentKind` has no T5 value), so no T5 slip can reach a package.

### Genuinely new (introduced by this feature)

A small module owning exactly two jobs — *assemble one reviewable unit from
already-processed extractions*, and *record one human decision about exactly that
unit* — and inventing no tax truth in either:

1. An **immutable review package**: one firm, one case, one participant, one tax
   year, one or more reviewed slips, assembled fail-closed.
2. A **per-slip reviewed view**: the processed extraction's attribution, its
   `T4Slip`, its flattened populated box amounts (from `T4SlipBoxAmounts`), its
   `TaxLineEntry` list, and its `UnmappedBoxCodes` — all carried through
   unchanged.
3. An **immutable sign-off record**: decision, reviewer reference, caller-supplied
   decided-at reference, optional note — attached to one specific package
   instance, once.
4. A **module-local supported-tax-year policy** (2025 only) and a **closed set of
   fixed field-name constants** for this module's own `ValidationError.Field`
   values.

Concrete type names, the module/project home, and the operation shapes are
Class B `[ASSUMED]` (see Open questions). Dependencies point inward and are
limited to `CaseProcessing` and `TaxLogicEngine`.

---

## User stories

1. As an **accountant**, I want every T4 I have for one client and one tax year
   gathered into one immutable package, so that I review one thing once instead
   of re-deriving what belongs together every time.
2. As an **accountant**, I want each slip in the package shown with its parsed box
   values *and* the CRA line entries those values produced, with the source box
   codes attached, so I can check a line against the paper slip in my hand without
   trusting a black box.
3. As an **accountant**, I want the package to show me, unchanged and
   unsummarised, every populated box the engine could not map to a CRA line, so
   nothing the slip reported disappears between the scan and the return.
4. As a **compliance reviewer**, I want assembly to **refuse** any bundle that
   spans two firms, two cases, two participants, or two tax years, so that one
   person's income can never be reviewed — or approved — as part of another
   person's return.
5. As a **compliance reviewer**, I want an approval to be permanently bound to the
   exact content that was approved, and impossible to apply twice or to alter
   afterwards, so that "the accountant signed off" is a statement about specific
   numbers rather than about a screen that has since changed.
6. As a **firm administrator**, I want it stated plainly that a signed-off package
   is **not** proof that the participant belongs to the case, that the reviewer
   was authorized, or that the return may be filed, so that nobody mistakes a
   recorded decision for an access decision or a filing authorization.
7. As a **compliance reviewer**, I want no full SIN to be reachable in any
   rendered, logged, serialized, or error output of this module — and reviewer
   free text never echoed anywhere — so the artifact designed to be read by humans
   is not the artifact that leaks.
8. As an **engine maintainer**, I want this module to invoke no parser, no mapper,
   and no mapping config, and to perform no arithmetic, so a review package can
   never disagree with the engine about what a slip says.
9. As an **engine maintainer**, I want assembly and sign-off to be deterministic —
   no clock, network, filesystem, database, OCR, or LLM — so the same inputs always
   yield the same package.
10. As the **Portal Foundation** author, I want a complete, SIN-safe,
    already-assembled read model to render, so the UI never computes tax and never
    implements a CRA mapping (CLAUDE.md).

---

## Functional requirements

`[reuse]` marks reuse of an existing type. `[design]` marks a decision that
depends on no CRA fact. `[boundary]` marks a module-boundary invariant from
CLAUDE.md. **No requirement below asserts a CRA fact.**

### Input and assembly

1. **[reuse]** Assembly SHALL accept a collection of `ProcessedT4Extraction` and
   nothing else — not a `ValidatedExtraction`, not an `ExtractionEnvelope`, not a
   `T4RawInput`, not a `T4Slip`. Because `ProcessedT4Extraction`'s constructor is
   internal to `CaseProcessing`, the only way to obtain one is through the spec
   008 processor; this module SHALL NOT re-implement, duplicate, or bypass it.
2. **[design]** A **null** collection SHALL fail closed with a structured error
   and SHALL NOT throw.
3. **[design]** An **empty** collection SHALL fail closed with a structured error.
   A package with nothing in it is not a review.
4. **[design]** A **null element** in the collection SHALL fail closed with a
   structured error and SHALL NOT throw.
5. **[design]** All elements SHALL share the same `FirmId`, compared **ordinally
   with no trimming, normalization, or case-folding**. Any difference SHALL fail
   closed.
6. **[design]** All elements SHALL share the same `CaseId`, compared ordinally.
   Any difference SHALL fail closed.
7. **[design]** All elements SHALL share the same `ParticipantId`, compared
   ordinally. Any difference SHALL fail closed. **This is the rule that prevents
   one participant's slip being reviewed as part of another's return.**
8. **[design]** All elements SHALL share the same `TaxYear`. Any difference SHALL
   fail closed, and neither value SHALL be coerced.
9. **[design]** `DocumentId` SHALL be unique across the package, compared
   ordinally. A repeated `DocumentId` SHALL fail closed — the same document
   appearing twice would present the same income twice to the reviewer.
10. **[design]** Assembly SHALL NOT reorder, deduplicate by content, merge, split,
    or filter the elements. The package's reviewed slips appear in **the order
    supplied**, one per element.
11. **[design]** Assembly SHALL NOT detect that two different `DocumentId`s
    describe the same underlying slip. Content-level duplicate detection is a tax
    judgement and is out of scope (see Open questions, item C).

### Presented content (carried through, never recomputed)

12. **[reuse]** Each reviewed slip SHALL carry the element's `DocumentId`,
    `SourceReference`, and `TaxYear` echoed **exactly as supplied**, and the
    element's `T4Slip` **by reference** — never rebuilt, re-parsed, or copied
    field-by-field.
13. **[reuse]** Each reviewed slip SHALL carry the element's `TaxLineEntry` list
    **verbatim and in the engine's own order**, including each entry's
    `LineNumber`, `Amount`, and `SourceBoxCodes`. This module SHALL NOT add,
    remove, reorder, re-aggregate, re-round, or re-label an entry.
14. **[reuse]** Each reviewed slip SHALL carry the element's `UnmappedBoxCodes`
    **verbatim, in order, unfiltered, and unclassified**. This module SHALL NOT
    rank, group, prioritise, annotate, or explain them. *(What, if anything,
    follows from their presence at sign-off is **FR-31**, which is blocked.)*
15. **[reuse]** Each reviewed slip SHALL carry the flattened populated box
    amounts obtained **only** by calling `T4SlipBoxAmounts.Enumerate` on the
    element's `T4Slip`, in that method's own order. This module SHALL NOT
    maintain its own box-code list, SHALL NOT read a `T4Slip` amount property
    directly, and SHALL NOT decide which boxes are "interesting".
16. **[design]** The package SHALL NOT compute any total, subtotal, count-based
    summary of money, or cross-slip figure of any kind. **No amount in a package
    is produced by this module**; every one of them was produced by the engine
    upstream.
17. **[design]** The package SHALL NOT present a participant-level or case-level
    CRA line total across several slips. Combining slips onto shared return lines
    is return assembly and is out of scope (see Open questions, item B).

### Sign-off

18. **[design]** A package SHALL support recording **exactly one** human decision.
    Recording SHALL return a **replacement** package carrying the decision; the
    original instance SHALL be unchanged, matching spec 006's immutable-aggregate
    pattern.
19. **[design]** A decision SHALL be one of a **closed set**: approved, changes
    requested, rejected. A value outside the declared set (including a cast
    integer) SHALL fail closed.
20. **[design]** A decision SHALL carry a **non-blank reviewer reference**, echoed
    exactly as supplied and never parsed, resolved, or verified. A null, empty, or
    whitespace-only reviewer reference SHALL fail closed.
21. **[design]** A decision SHALL carry a **non-blank caller-supplied decided-at
    reference**, echoed exactly as supplied. This module SHALL NOT read the system
    clock (determinism, FR-36) and SHALL NOT parse, validate, order, or compare the
    reference as a date. A null, empty, or whitespace-only value SHALL fail closed.
22. **[design]** A decision of *rejected* or *changes requested* SHALL carry a
    **non-blank note**; a null, empty, or whitespace-only note SHALL fail closed
    for those decisions. A note is optional for *approved*.
23. **[design]** A **null** decision input SHALL fail closed with a structured
    error and SHALL NOT throw.
24. **[design]** Recording a decision on a package that **already carries one**
    SHALL fail closed. There is no amend, no overwrite, and no second signature.
    Reviewing revised content means assembling a new package.
25. **[design]** A signed-off package SHALL NOT be presented as proof of
    firm/case/participant membership, of reviewer authorization, or of authority
    to file. It records a decision; it verifies none of those. Those checks belong
    to the future application workflow, exactly as specs 007 and 008 declared.
26. **[design]** Recording a decision SHALL NOT mutate, transition, or notify a
    `TaxCase`, SHALL NOT persist anything, and SHALL NOT emit a domain change
    record.
27. **[reuse]** Assembly and decision-recording SHALL both return `ParseResult<T>`:
    success carries a value and zero errors; failure carries ≥1 `ValidationError`
    and no value; nothing is partially accepted and nothing is mutated.

### Tax-year isolation

28. **[design]** The module SHALL declare its supported tax years in exactly one
    module-local place: **2025 only**. There is no "current" year, no
    adjacent-year fallback, no coercion, no system-clock read, and no inference of
    year support by inspecting mapping resources.
29. **[design]** A package whose tax year is not a supported year SHALL fail
    closed. (Spec 008 already guarantees 2025 for any constructible
    `ProcessedT4Extraction`; this is defence in depth so the module stays
    fail-closed if that policy ever diverges.)
30. **[design]** The package's tax year SHALL equal every member's `TaxYear`, and
    SHALL be carried, never derived from a clock, a config, or a default.

### Unmapped-box disposition

31. **OPEN — BLOCKED ON CLASS A QUESTION A1. No behaviour is specified here.**
    Whether the presence of a populated-but-unmapped box code in a reviewed slip
    (a) is purely informational, (b) must be individually acknowledged by the
    reviewer before *approved* may be recorded, (c) blocks *approved* outright, or
    (d) is classified per box code, is a **new tax judgement about preparer duty**.
    Spec 008 explicitly reserved it for this feature and this agent will not
    invent it. Until A1 is answered, **no gating, classification, acknowledgement,
    or advisory behaviour of any kind is in scope**; FR-14 (carry through verbatim
    and unclassified) is the entirety of the specified behaviour. `/plan` MUST NOT
    design around this gap.

### PII

32. **[design]** No full SIN SHALL appear in any output of this module — no
    `ToString()`, no diagnostic, no exception message, no `ValidationError`, and no
    supported direct JSON serialization. The Box 12 SIN SHALL be reachable only as
    the engine's existing masked `RecipientIdentifier` via `T4Slip.Sin`.
33. **[design]** This module SHALL introduce **no new SIN property, no new SIN
    type, and no second masking or redaction helper**, SHALL NOT construct a
    `RecipientIdentifier`, and SHALL NOT reference `SafeDiagnosticText` or any
    masking helper from another module.
34. **[design]** Every caller-controlled string on every public type of this module
    — `DocumentId`, `FirmId`, `CaseId`, `ParticipantId`, `SourceReference`, the
    reviewer reference, the decided-at reference, and **the reviewer note** — SHALL
    be handled by **omission**: readable programmatically, `JsonIgnore`d for
    supported direct JSON serialization, and absent from every `ToString()` and
    every `ValidationError` message. The note is free text a human typed and is
    treated as capable of containing anything, including a SIN.
35. **[design]** This module's own `ValidationError`s SHALL use **fixed field
    names** from a closed constant set and **fixed, non-interpolated message
    templates**, and SHALL NEVER echo a caller value, a box value, an amount, a
    box code, or a CRA line number. Every public type SHALL be a sealed class with
    a hand-written `ToString()` emitting **fixed structural facts only** (never a
    record, whose compiler-generated `ToString()` prints every property — the leak
    `src/TaxLogicEngine/CLAUDE.md` warns about).

### Boundary, determinism, dependency direction

36. **[design]** Assembly and decision-recording SHALL be deterministic: the same
    inputs SHALL always yield an equal result — equal success/failure, equal error
    sequence and ordering, equal reviewed-slip ordering, equal line entries — with
    no input mutated.
37. **[boundary]** Production project references SHALL be exactly `CaseProcessing`
    and `TaxLogicEngine`, pointing inward. The module SHALL NOT reference
    `TaxCases`, SHALL NOT directly reference `DocumentExtraction`, and SHALL NOT
    reference ASP.NET Core, EF Core, SQL, OCR/LLM/cloud/email SDKs, the filesystem,
    the network, the system clock, the environment, or the current user. No
    existing module SHALL reference this one.
38. **[boundary]** The module SHALL invoke **no engine service that produces tax
    truth**: no `T4SlipParser`, no `T4LineMapper`, no `IT4LineMapProvider`, no
    `MoneyRounding`, and no read of a `TaxLogicEngine.Mappings` resource.
    `T4SlipBoxAmounts.Enumerate` is the single permitted engine call, and it
    contains no CRA line number and performs no mapping.
39. **[boundary]** The module SHALL contain **no CRA line-number literal**, no
    box→line table, and no box-code list. Every line number a package displays
    arrives on a `TaxLineEntry` that the engine produced from
    `Mappings/T4LineMap.2025.json`.
40. **[design]** The module SHALL perform **no arithmetic and no rounding**: no
    `Math.Round`, no `MidpointRounding`, no `double`, no `float`, and no arithmetic
    operator applied to any monetary value.
41. **[boundary]** No existing public type in `TaxLogicEngine`, `TaxCases`,
    `DocumentExtraction`, or `CaseProcessing` SHALL be added, renamed, or changed
    by this feature — including no new `InternalsVisibleTo` grant on
    `CaseProcessing` — and the full existing test suite SHALL remain green.

---

## Out of scope

Each item is explicitly excluded:

- **Deciding what a preparer must do about an unmapped box** — blocked (A1/FR-31).
  No review flag, no severity, no classification, no advice, no gating ships until
  a human answers.
- **Cross-slip, participant-level, or case-level CRA line totals** — return
  assembly, not review. `T4LineMapper`'s existing multi-slip aggregation is
  unchanged and simply not invoked here.
- **Correcting a value inside the package** — the reviewer cannot edit a box
  amount, a line entry, or a slip field. A wrong value is rejected and re-extracted
  upstream. Editing tax data in a review UI is a separate, Class A feature with its
  own audit and re-mapping requirements.
- **Presenting the raw OCR/extracted payload for comparison** — the raw
  `T4RawInput` is deliberately absent from `ProcessedT4Extraction` (spec 008
  requirement 21) because it is a second, unmasked carrier of the Box 12 SIN, and
  it stays absent. The reviewer compares against the physical slip.
- **Packages for failed processing** — only successful `ProcessedT4Extraction`
  values can be assembled; a rejected slip never had a parse to review. Surfacing
  processing failures to a human is a separate workflow concern.
- **Any `TaxCases` interaction** — no project reference, no `CaseStatus`
  transition, no evidence attachment, no readiness computation, no
  `CaseChangeRecord`. Spec 006 keeps owning case lifecycle and readiness; this
  feature grows **no second readiness signal**.
- **Verifying firm/case/participant membership, authenticating a reviewer, or
  authorizing anything** — assigned to the future application workflow by specs 007
  and 008, and it stays there.
- **Persistence, retrieval, versioning, queuing, notification, audit storage** —
  the package is an in-memory immutable value. Where it is stored, and the audit
  guarantees of that store, are a later feature.
- **Rendering, export, PDF/HTML/JSON document generation, or any UI** — "package"
  here means a domain read model, not a file. Direct JSON serialization is
  defensive only (FR-34), never an API contract.
- **T5 review, generic-evidence review, multi-form packages** — no T5 type is
  consumed; `ExtractedDocumentKind` has no T5 value.
- **Any change to `T4SlipParser`, `T4LineMapper`, `T4SlipBoxAmounts`, or
  `Mappings/T4LineMap.2025.json`** — including the required-box set, the constrained
  code sets, the companion-box rules, and spec 004's deliberate `UnmappedBoxCodes`
  deferrals. Changing any of them is a tax-domain amendment to specs 003/004.
- **T1 calculation of any kind** — no taxable income, no credits, no CPP/EI
  reconciliation, no schedules.
- **NETFILE / EFILE / CRA submission, and any claim that a recorded sign-off has
  CRA, statutory, or attestation meaning** — none is asserted (see Open questions,
  item D).
- **Tax years other than 2025.**
- **Any accidental change to existing `TaxLogicEngine`, `TaxCases`,
  `DocumentExtraction`, or `CaseProcessing` behaviour** — the full suite must stay
  green (AC-45).

---

## Acceptance criteria

Each criterion is a single mechanically-checkable test (Given/When/Then).

Throughout, *"a processed 2025 T4 extraction"* means a `ProcessedT4Extraction`
obtained by running a well-formed 2025 T4 `ExtractionEnvelope` through
`ExtractionEnvelopeValidator.Validate` and then `T4CaseProcessor.Process` with the
embedded 2025 mapping provider — never a fabricated instance.

### Happy path and pass-through fidelity

1. **Happy path, single slip.**
   Given one processed 2025 T4 extraction, When a package is assembled, Then the
   result is a success with zero errors carrying exactly one reviewed slip and no
   sign-off.

2. **Happy path, several slips for one participant.**
   Given three processed 2025 T4 extractions sharing firm, case, participant, and
   year but with distinct document ids, When a package is assembled, Then the
   result is a success carrying three reviewed slips.

3. **Order is the order supplied.**
   Given three processed extractions supplied in a distinctive order, When a
   package is assembled, Then the reviewed slips appear in exactly that order,
   identified by their document ids.

4. **Attribution is echoed exactly.**
   Given processed extractions whose `FirmId`, `CaseId`, `ParticipantId`,
   `DocumentId`, and `SourceReference` are distinctive marker strings, When a
   package is assembled, Then the package and its reviewed slips carry those values
   ordinally identical, with nothing trimmed, normalized, case-folded, or
   generated.

5. **The slip is the engine's, by reference.**
   Given a processed extraction, When a package is assembled, Then the reviewed
   slip's `T4Slip` is **reference-equal** to `ProcessedT4Extraction.Slip`.

6. **The line entries are the engine's, verbatim and in order.**
   Given a processed extraction, When a package is assembled, Then the reviewed
   slip's line entries are sequence-equal to `ProcessedT4Extraction.Mapping.Lines`
   — same count, same order, same `LineNumber`, same `Amount`, same
   `SourceBoxCodes` — with no entry added, removed, reordered, or altered.

7. **Unmapped box codes are the engine's, verbatim and in order.**
   Given a processed extraction whose payload populates a box the 2025 config does
   not map, When a package is assembled, Then assembly **succeeds** and the reviewed
   slip's unmapped codes are sequence-equal to
   `ProcessedT4Extraction.Mapping.UnmappedBoxCodes`, unfiltered and unclassified.

8. **Populated box amounts come only from the engine's flattener.**
   Given a processed extraction, When a package is assembled and, separately,
   `T4SlipBoxAmounts.Enumerate(slip)` is called on the same slip, Then the reviewed
   slip's box amounts are sequence-equal to the flattener's output — same codes, same
   order, same amounts.

9. **Determinism.**
   Given the same processed extractions, When a package is assembled twice, Then
   the two packages are equal — same success/failure, same errors in the same
   order, same reviewed-slip order, same line entries, same amounts.

10. **Assembly mutates nothing.**
    Given processed extractions, When a package is assembled, Then every input's
    `DocumentId`, `FirmId`, `CaseId`, `ParticipantId`, `TaxYear`,
    `SourceReference`, `Slip`, and `Mapping` are unchanged and reference-identical
    to before the call.

### Fail-closed rejections — assembly (one rule per criterion)

11. **Null collection.**
    Given a null collection, When assembly runs, Then it fails closed with exactly
    one structured error, returns no value, and throws no exception.

12. **Empty collection.**
    Given an empty collection, When assembly runs, Then it fails closed with a
    structured error and returns no value.

13. **Null element.**
    Given a collection containing one valid processed extraction and one null
    element, When assembly runs, Then it fails closed with a structured error,
    returns no value, and throws no exception.

14. **Firm mismatch.**
    Given two processed extractions identical in every attribution except `FirmId`,
    When assembly runs, Then it fails closed with a structured error naming the firm
    field and returns no value.

15. **Case mismatch.**
    Given two processed extractions differing only in `CaseId`, When assembly runs,
    Then it fails closed with a structured error naming the case field.

16. **Participant mismatch.**
    Given two processed extractions differing only in `ParticipantId`, When assembly
    runs, Then it fails closed with a structured error naming the participant field
    and returns no value.

17. **Attribution comparison is ordinal.**
    Given two processed extractions whose `ParticipantId`s differ only by letter
    case (and, separately, only by leading whitespace), When assembly runs, Then
    each fails closed — the values are not folded, trimmed, or treated as equal.

18. **Duplicate document id.**
    Given two processed extractions sharing all attribution **including
    `DocumentId`**, When assembly runs, Then it fails closed with a structured error
    naming the document field and returns no value.

### Fail-closed rejections — sign-off (one rule per criterion)

19. **Null decision.**
    Given an assembled package and a null decision input, When recording runs, Then
    it fails closed with exactly one structured error, returns no value, and throws
    no exception.

20. **Undefined decision value.**
    Given an assembled package and a decision whose value is an integer cast outside
    the declared set, When recording runs, Then it fails closed with a structured
    error naming the decision field.

21. **Blank reviewer reference.**
    Given an assembled package and a decision whose reviewer reference is null (and,
    separately, empty, and whitespace-only), When recording runs, Then each fails
    closed with a structured error naming the reviewer field.

22. **Blank decided-at reference.**
    Given an assembled package and a decision whose decided-at reference is null
    (and, separately, empty, and whitespace-only), When recording runs, Then each
    fails closed with a structured error naming the decided-at field.

23. **Rejected without a note.**
    Given an assembled package and a *rejected* decision whose note is null (and,
    separately, whitespace-only), When recording runs, Then each fails closed with a
    structured error naming the note field.

24. **Changes-requested without a note.**
    Given an assembled package and a *changes requested* decision whose note is null
    (and, separately, whitespace-only), When recording runs, Then each fails closed
    with a structured error naming the note field.

25. **Second decision on an already-decided package.**
    Given a package that already carries a recorded decision, When recording runs
    again with a valid decision, Then it fails closed with a structured error,
    returns no value, and the existing decision is unchanged.

### Sign-off — positive behaviour

26. **Approved is recorded on a replacement package.**
    Given an assembled package and a valid *approved* decision with no note, When
    recording runs, Then it succeeds, the returned package carries the decision, the
    reviewer reference, and the decided-at reference ordinally identical to those
    supplied, and the **original package instance still carries no decision**.

27. **Rejected and changes-requested are recorded with their notes.**
    Given an assembled package and, separately, a valid *rejected* and a valid
    *changes requested* decision each with a non-blank note, When recording runs,
    Then each succeeds and the returned package carries that decision and that note
    ordinally identical to the one supplied.

28. **The reviewed content is unchanged by the decision.**
    Given an assembled package, When a decision is recorded, Then the returned
    package's reviewed slips are sequence-equal to the original's — same order, same
    `T4Slip` references, same line entries, same amounts, same unmapped codes — so
    what was approved is exactly what was reviewed.

29. **The decided-at reference is never interpreted.**
    Given a decision whose decided-at reference is the non-date string
    `"not-a-timestamp"`, When recording runs, Then it succeeds and the value is
    echoed ordinally unchanged — proving the module neither parses nor validates it
    as a date.

30. **Recording is deterministic and clock-free.**
    Given the same package and the same decision input, When recording runs twice,
    Then the two resulting packages are equal in every observable property.

### Boundaries

31. **Empty line mapping.**
    Given a processed extraction whose mapping produced no line entries, When a
    package is assembled, Then assembly **succeeds** and the reviewed slip's line
    entry list is empty — an empty mapping is data, not a defect for this module to
    judge.

32. **Zero amounts survive unchanged.**
    Given a processed extraction whose Box 14 value was `"0"`, When a package is
    assembled, Then every presented amount is value-equal to the engine's own output
    for the same slip.

33. **Half-cent boundary is the engine's, unchanged.**
    Given processed extractions whose Box 14 values were `"100.005"` and, separately,
    `"100.015"`, When packages are assembled, Then each presented amount is exactly
    the value `MoneyRounding.ToCents` half-up / `MidpointRounding.AwayFromZero`
    already produced upstream — `100.01` and `100.02` — proving this module neither
    re-rounds nor alters midpoint behaviour. (The negative-sign direction is
    unreachable: `T4SlipParser` rejects negative amounts outright, so no negative
    amount can reach a package.)

34. **The module performs no arithmetic.**
    Given the module's source and compiled assembly, When inspected, Then they
    contain no `MoneyRounding` call, no `Math.Round`, no `MidpointRounding`, no
    `double`, no `float`, and no arithmetic operator applied to a monetary value —
    in particular no summation across reviewed slips.

### PII

35. **No full SIN in any successful output.**
    Given a processed extraction whose Box 12 value was `123456789` and whose
    identity fields are populated, When a package is assembled, a decision recorded,
    and the `ToString()` of the package, of every reviewed slip, of the sign-off,
    and every supported direct JSON serialization are produced, Then none contains
    `123456789` or any 9-consecutive-digit substring, and the SIN is reachable only
    as the masked `RecipientIdentifier` (`***-**-789`).

36. **No full SIN or caller value in any failure output.**
    Given inputs that assembly rejects and, separately, that recording rejects,
    where the payload carried `123456789` in Box 12 and every caller reference is a
    distinctive marker string, When every resulting `ValidationError` is read, Then
    no message contains `123456789`, any 9-consecutive-digit substring, any caller
    reference, any box code, any amount, or any CRA line number.

37. **The reviewer note never escapes.**
    Given a recorded decision whose note is the distinctive string
    `"NOTE-MARKER-123456789"`, When the package's `ToString()`, the sign-off's
    `ToString()`, every supported direct JSON serialization, and every
    `ValidationError` are produced, Then none contains the note, any part of it, or
    any 9-consecutive-digit substring.

38. **Caller strings are omitted from rendering and serialization.**
    Given a signed-off package whose `DocumentId`, `FirmId`, `CaseId`,
    `ParticipantId`, `SourceReference`, reviewer reference, decided-at reference,
    and note are distinctive markers, When every public `ToString()` and every
    supported direct JSON serialization are produced, Then none contains any of those
    values, and each such property is `JsonIgnore`d while remaining readable
    programmatically.

39. **One SIN representation only.**
    Given the compiled module, When its public surface and emitted metadata are
    inspected, Then it declares no SIN property or SIN type, contains no construction
    of `RecipientIdentifier`, contains no masking or redaction helper of its own, and
    references no `SafeDiagnosticText`.

40. **Fixed field names and non-interpolated messages.**
    Given every `ValidationError` this module produces, When inspected, Then each
    `Field` is a member of the module's closed constant set, and every message is a
    fixed template with no interpolated argument.

### Tax-year isolation

41. **The year policy is 2025 and lives in one place.**
    Given the module's supported-tax-year policy, When inspected, Then its supported
    set is exactly `[2025]`, and it reports 2024 and 2026 as unsupported.

42. **The package year equals every member's year.**
    Given any successfully assembled package, When inspected, Then its tax year
    equals every reviewed slip's tax year and equals every input
    `ProcessedT4Extraction.TaxYear`.

43. **Year checks exist and are unconditional.**
    Given the assembler's source and compiled IL, When inspected, Then it compares
    `TaxYear` across all elements and consults the module's supported-tax-year policy
    on every assembly path, with no branch that skips either check. *(A cross-year
    package cannot be constructed in a test today: `ProcessedT4Extraction`'s
    constructor is internal to `CaseProcessing` and spec 008 emits 2025 only. This
    PRD refuses to weaken that boundary with an `InternalsVisibleTo` grant for test
    convenience — FR-41 — so the guard is verified structurally. See `[ASSUMED]`
    item on year-mismatch testability.)*

### No tax truth invented / data-driven mapping

44. **No CRA line number is hardcoded, and no engine tax service is invoked.**
    Given the module's source and compiled assembly, When inspected, Then they
    contain no CRA line-number literal, no box→line table or dictionary, no box-code
    list, no reference to a `TaxLogicEngine.Mappings.*` resource name, and no
    reference to `T4SlipParser`, `T4LineMapper`, `IT4LineMapProvider`, or
    `EmbeddedT4LineMapProvider` — `T4SlipBoxAmounts.Enumerate` being the single
    permitted engine call. Every line number a package displays originates from
    `Mappings/T4LineMap.2025.json` via the engine, upstream.

### Boundary, dependencies, regression

45. **Existing behaviour stays green.**
    Given `dotnet test TaxAI.sln`, When the full suite runs after this feature is
    added, Then every existing `TaxLogicEngine.Tests`, `TaxCases.Tests`,
    `DocumentExtraction.Tests`, and `CaseProcessing.Tests` test remains green, and no
    existing public type in any of the four modules has been added, renamed, or
    changed — in particular no parser rule, no mapper behaviour, no mapping-config
    entry, and no new `InternalsVisibleTo` grant.

46. **Exactly two production project references.**
    Given the module's project file, When inspected, Then its production project
    references are exactly `CaseProcessing` and `TaxLogicEngine` — no `TaxCases`, and
    no direct `DocumentExtraction` reference.

47. **No reverse references and no `TaxCases` coupling.**
    Given the solution's project files and the compiled assemblies, When inspected,
    Then this module references no `TaxCases` type in either direction, and
    `TaxLogicEngine`, `TaxCases`, `DocumentExtraction`, and `CaseProcessing` each
    reference this module not at all.

48. **No clock, network, filesystem, database, OCR, or LLM access.**
    Given the module's source and assembly, When inspected, Then they contain no
    `DateTime.Now`/`UtcNow`, no network, filesystem, database, environment, or
    current-user access, no generated identifier, and no OCR/LLM invocation.

49. **The solution declares this spec's projects exactly once each.**
    Given `TaxAI.sln`, When inspected, Then this feature's production and test
    projects are each declared exactly once at their approved paths. *(Per root
    `CLAUDE.md`, no test may assert a total project count for the solution.)*

---

## Open questions

### Blocking Class A questions

**A1 — When a populated T4 box has no CRA line mapping, what may an accountant
sign off on?**

This is the question spec 008 explicitly deferred here, in its own words: *"Deciding
that an unmapped box must raise a review flag, block a draft, or trigger a schedule
would be a new tax judgement; it is deferred, in full, to the Accountant Review
Package."* It is Class A — a statement about preparer duty with direct filing
consequences — and this agent will not answer it.

Why it is material rather than cosmetic: `src/TaxLogicEngine/Mappings/README.md`
records, per box code, why spec 004 left it out of `T4LineMap.2025.json`, and those
recorded rationales are **not uniform**. The T4 codes that file lists as
intentionally not mapped are 16, 16A, 17, 17A, 24, 26, 30, 31, 32, 33, 34, 36, 38,
40, 45, 46, 55, 56, 66, 67, 69, 71, 74, 75, 78–83, 85, 86, 87, and 88 — a list whose
recorded reasons range from "not reported on a return at all", through "already
included in another box", to "reported, but only via a schedule or a calculation
this engine does not yet perform". A single blanket policy is therefore wrong in one
direction or the other:

- treating them all as **informational** risks a reviewer approving a package while
  an amount the taxpayer is entitled to claim goes nowhere;
- treating them all as **blocking** makes ordinary slips unapprovable and trains
  reviewers to click past the warning.

Please answer all four parts:

- **A1a.** Is the presence of a populated-but-unmapped box code (i) purely
  informational, (ii) something the reviewer must individually acknowledge before
  *approved* may be recorded, (iii) an outright block on *approved*, or (iv) handled
  per box code?
- **A1b.** If per box code (A1a-iv): each box code's disposition is a distinct tax
  fact requiring its **own canada.ca verification** for tax year 2025 — which would
  make this a substantially larger tax-domain spec. Do you want that scope, or a
  uniform rule for v1?
- **A1c.** If a reviewer acknowledgement is required (A1a-ii), must the
  acknowledgement be recorded per box code (auditable) or once per package?
- **A1d.** Does the same answer apply to a box code the engine mapped but whose
  amount the reviewer disputes? *(This PRD currently excludes reviewer correction
  entirely — see `[ASSUMED]` below — so a "no, that is a different feature" answer
  keeps the exclusion intact.)*

Until A1 is answered, **FR-31 is unspecified**, this PRD is **not ready for
`/plan`**, and no gating, classification, or advisory behaviour may be designed or
implemented.

### Class A items raised for the human, non-blocking (excluded, nothing assumed)

Each of these is a tax, PII, or security judgement that this PRD **excludes** rather
than answers. Excluding is the conservative direction: the feature cannot produce a
wrong result about something it does not do. Each is surfaced because this is the
first spec where it becomes operationally visible.

- **B — Participant-level CRA line totals.** Should a package show one total per CRA
  line across the participant's several T4 slips? `T4LineMapper.Map` already
  aggregates a slip *set* onto shared lines with canada.ca-verified 2025 config, so
  delegating would invent no arithmetic — but *which slips form the set* is the tax
  judgement, and spec 008 classified cross-slip combination as "return assembly, not
  this feature". **Excluded from v1** (FR-16, FR-17). If you want totals, say so and
  say whether the set is strictly one participant's slips.
- **C — Content-level duplicate slips.** The same physical T4 scanned twice yields
  two document ids and would appear twice. FR-9 rejects a repeated *document id*;
  it cannot detect that two different ids describe the same slip. Deciding two slips
  are "the same" is a tax judgement (and, if totals are ever added under B, a
  double-counting risk). **Excluded from v1.**
- **D — What a sign-off means.** This PRD asserts **nothing** about whether a
  recorded decision has CRA, statutory, attestation, retention, or filing
  significance, and makes no claim about any CRA authorization or transmission form.
  v1 sign-off is an internal workflow record with **no filing authority** (FR-25). If
  it must carry legal meaning, that is a Class A decision with its own canada.ca
  verification and its own record-keeping requirements.
- **E — Nothing in this pipeline authorizes anybody.** Specs 007 and 008 both
  assigned membership verification and caller authorization to the future application
  workflow, and this feature inherits that unchanged (FR-25). It is worth a human's
  explicit eye that **the artifact gating a filing performs no authorization check
  whatsoever** — including that the reviewer reference is echoed, never resolved or
  verified. That is consistent with the two merged specs; confirm it is still what
  you want now that a human decision is being recorded.
- **F — Reviewer sight of raw extracted values.** "Review the OCR output" arguably
  implies seeing what OCR actually said. This PRD **excludes** it, because the raw
  `T4RawInput` is a second unmasked carrier of the Box 12 SIN that spec 008
  deliberately kept off `ProcessedT4Extraction`. Re-admitting it is a PII decision,
  not a UX one.
- **G — Inherited: Box 54 and the employee copy.** `T4SlipParser` requires Box 54,
  which spec 003 recorded as a `[CONFIRM]`-flagged decision meaning the parser
  presumes the employer/filing-side copy. Every OCR'd *employee-copy* T4 therefore
  fails closed in spec 008 and never reaches a review package at all. This PRD takes
  no position; if the intended flow is "accountant scans the client's copy",
  **spec 003 needs an amendment before this pipeline is usable in production**.

### `[ASSUMED]` — Class B (one-pass human override)

- `[ASSUMED]` **Spec folder and slug** are `specs/009-accountant-review-package/`
  (001–008 taken, confirmed by listing `specs/`); the feature name is taken from
  `NEXT.md` and root `CLAUDE.md`.
- `[ASSUMED]` **Core purpose** — v1 is *assemble one reviewable unit + record one
  human decision about exactly that unit*. Assembly alone would be a read model, not
  a review; sign-off alone would have nothing to attach to.
- `[ASSUMED]` **Module home** — a new `src/CaseReview/` project (net8.0, nullable
  enabled) with tests in `tests/CaseReview.Tests/`, both added to `TaxAI.sln`. A new
  module is not optional: `src/CaseProcessing/CLAUDE.md` fixes that module's job as
  "drive a single already-validated T4 extraction through the engine" with a
  no-additions rule on its public contract, and `src/TaxCases/CLAUDE.md` fixes
  TaxCases' sole production dependency as `TaxLogicEngine`. The module is real and
  contract-bearing, not a speculative empty shell.
- `[ASSUMED]` **Module name is form-neutral (`CaseReview`)** while the content it
  carries is T4-specific, mirroring spec 008's `CaseProcessing`/`T4CaseProcessor`
  split. Nothing for T5 is scaffolded.
- `[ASSUMED]` **Type names** — `AccountantReviewPackage` (the aggregate, internal
  constructor so only the assembler creates one), `ReviewedSlip`,
  `ReviewedBoxAmount`, `ReviewSignOff`, `ReviewDecisionDescriptor`,
  `SignOffDecision` (enum: `Approved`, `ChangesRequested`, `Rejected`),
  `ReviewPackageAssembler`, `ReviewFields` (closed field-name constants),
  `SupportedReviewTaxYears`. Exact names are `/plan`'s to finalize.
- `[ASSUMED]` **`ReviewedBoxAmount` is a new two-member carrier**, not a reuse of
  `T4OtherInformation` — that existing type means specifically an "Other information"
  code/amount pair, and widening its meaning to every box would be worse than one
  small new type. It is populated **only** from `T4SlipBoxAmounts.Enumerate`.
- `[ASSUMED]` **Operation shapes** — `ReviewPackageAssembler.Assemble(...)` returning
  `ParseResult<AccountantReviewPackage>`, and an instance operation on the package
  returning a **replacement** `ParseResult<AccountantReviewPackage>` carrying the
  sign-off (spec 006's immutable-replacement pattern). Both synchronous: the module
  does no I/O.
- `[ASSUMED]` **Null inputs fail closed rather than throwing**, matching
  `ExtractionEnvelopeValidator.Validate`, `TaxCase`'s operations, and
  `T4CaseProcessor.Process`.
- `[ASSUMED]` **No content fingerprint or hash in v1.** Immutability plus an internal
  constructor already binds a decision to exact content in memory; a tamper-evident
  digest only becomes meaningful once packages are persisted, which is out of scope.
- `[ASSUMED]` **No `CaseChangeRecord`-style change log**, because no aggregate is
  being mutated — assembly and sign-off each produce a new immutable value.
- `[ASSUMED]` **Error accumulation order** — assembly reports its own errors in a
  fixed declared order (so AC-9 determinism holds); structural rules (null/empty
  collection, null element) short-circuit before attribution-coherence rules, because
  coherence cannot be evaluated across an untraversable collection.
- `[ASSUMED]` **Year-mismatch testability** — FR-8's cross-element year check is
  verified structurally (AC-43) rather than behaviourally, because
  `ProcessedT4Extraction`'s internal constructor makes a non-2025 instance
  unconstructible from a foreign test assembly. **No `InternalsVisibleTo` grant is
  added to `CaseProcessing`** (FR-41): weakening a shipped module's boundary for test
  convenience is a worse trade than a structural assertion. Override this if you would
  rather have the behavioural test.
- `[ASSUMED]` **A note is optional for *approved* and required for *rejected* and
  *changes requested*** (FR-22) — a rejection with no stated reason is not actionable.
  This is a workflow rule, not a tax rule.
- `[ASSUMED]` **The decided-at reference is a caller-supplied opaque string**, never
  parsed or compared, following spec 006's precedent of carrying no timestamp and
  reading no clock. `/plan` must not substitute a `DateTimeOffset` populated from the
  system clock.

---

## CRA sources

**No new external tax fact is asserted by this PRD**, and — deliberately — **no CRA
line number, box-to-line assignment, rate, threshold, limit, deadline, gross-up,
credit percentage, or eligibility rule appears anywhere in this document.** The
acceptance criteria are written to compare this feature's output against the
*engine's own already-produced output* (AC-5 to AC-8, AC-32, AC-33) precisely so that
no line number needs to be, or is, restated here; AC-44 enforces the same rule in
code.

Where this document names box codes — FR-15's flattener scope, AC-32/AC-33's Box 14,
and A1's deferred-code list — it is naming **facts about files in this repository**
(`src/TaxLogicEngine/Services/T4SlipBoxAmounts.cs`,
`src/TaxLogicEngine/Services/T4SlipParser.cs`,
`src/TaxLogicEngine/Mappings/README.md`), each read directly during this analysis —
not CRA claims. Those underlying tax facts were verified against canada.ca in specs
003 and 004 and are reused unchanged; if any is wrong, the correction belongs in
spec 003 or 004, not here.

The tax facts this feature *relies on* already exist, were verified against canada.ca
in prior specs with citations recorded there, and are reused unchanged rather than
re-asserted:

| Reused fact (not re-asserted here) | Verified in | Source | Tax year | Retrieved |
|---|---|---|---|---|
| T4 box identities/definitions, required-box set, constrained code sets, companion-box rules (applied upstream by `T4SlipParser`; unchanged) | `specs/003-t4-slip-parsing/prd.md` | canada.ca — "T4 slip – Information for employers" (URL recorded there), plus RC4120 | 2025 | 2026-07-09 |
| T4 box→CRA-line mapping values (applied upstream from `Mappings/T4LineMap.2025.json` via `T4LineMapper`; displayed unchanged) | `specs/004-t4-cra-line-mapping/prd.md` | canada.ca — "T4 slip: Statement of Remuneration Paid", `https://www.canada.ca/en/revenue-agency/services/tax/individuals/topics/about-your-tax-return/tax-return/completing-a-tax-return/tax-slips/understand-your-tax-slips/t4-slips/t4-statement-remuneration-paid.html` (date modified 2026-02-02) | 2025 | 2026-07-11 |
| Deliberate `UnmappedBoxCodes` deferrals and their per-box rationale (displayed unchanged and unclassified; the subject of blocking question A1) | `specs/004-t4-cra-line-mapping/prd.md`, `src/TaxLogicEngine/Mappings/README.md` | canada.ca — same individuals page as above | 2025 | 2026-07-11 |

T5 facts (specs 001–002) are **not relied on**: no T5 type is consumed.

**Verification note (2026-07-26).** Because this PRD asserts no new CRA fact,
canada.ca was not required. A retrieval was nevertheless attempted for diligence
against the spec 004 individuals page above and returned **HTTP 403 Forbidden** to
this agent's fetch tool — the same bot filter recorded by spec 004 on 2026-07-11 and
spec 008 on 2026-07-25, where verification was completed by the coordinator in a
browser session. **No fact was taken from memory, from a secondary source, or from a
prior PRD in place of a source**, and no source conflict was resolved by this agent's
judgement.

**This is precisely why A1 blocks.** Answering A1 in the per-box direction (A1a-iv)
would require a *new* canada.ca verification for every deferred box code, and
canada.ca is not reachable from this agent's tooling. Should `/plan` or any
implementation task find itself needing a CRA line number, a box definition, a
preparer obligation, or any other tax fact that is not already in
`Mappings/T4LineMap.2025.json` or `T4SlipParser`, that is a signal this PRD's scope
has been exceeded: stop and verify against canada.ca (coordinator browser session)
before proceeding.

---

## Definition of Done — self-check

Both classes:

- [x] `specs/009-accountant-review-package/prd.md` exists with the required
      structure, opening with `## Feature classification` (Problem / User stories /
      Functional requirements / Out of scope / Acceptance criteria / Open questions /
      CRA sources)
- [x] Feature class named explicitly (`tax-domain`) and justified; mixed content
      classified `tax-domain`
- [x] Reuse check performed and documented (`## Current state and reuse analysis`,
      grounded in the actual tree; eight existing types reused, none re-specified;
      `ReadinessCalculator` and `SafeDiagnosticText` explicitly not duplicated)
- [x] Every AC is mechanically checkable by a single test
- [x] Zero Class A assumptions made — the one Class A decision this feature cannot
      avoid (A1/FR-31) is **escalated, not assumed**; the rest are excluded and
      surfaced

`tax-domain` additionally:

- [x] Every CRA fact has URL + tax year + retrieval date, all from canada.ca —
      vacuously and deliberately: this PRD asserts **no** CRA fact and names no CRA
      line number; the reused facts are tabulated with their canada.ca citations and
      retrieval dates
- [x] No source conflict was resolved by this agent's judgement (none arose)
- [x] Tax year named explicitly in Problem (**2025 only**), with fail-closed
      behaviour stated and no adjacent-year fallback
- [x] Every fail-closed rule has its own negative AC (AC-11 to AC-25)
- [x] PII acceptance criteria present (AC-35 to AC-40), covering the masked SIN, the
      omitted caller strings, and the free-text reviewer note
- [x] Tax-year isolation ACs present (AC-41 to AC-43)
- [x] Data-driven mapping AC present (AC-44): no CRA line number hardcoded in C#,
      and no engine tax service invoked
- [ ] **FAILED — every functional requirement is specified.** FR-31 is deliberately
      left open pending blocking Class A question A1.

**Result: NOT ready for `/plan`.** One box is unchecked and it is unchecked on
purpose. Answering A1 closes it.
