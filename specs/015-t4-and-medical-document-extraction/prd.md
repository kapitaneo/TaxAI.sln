# PRD 015 — T4 and Medical Document Extraction

## Feature classification

**Class: `tax-domain`.**

This feature turns an opaque uploaded document into structured data that becomes
a filing input. It changes what the system treats as a Canadian tax fact
(classification, box values, field-level validation, duplicate identity,
accountant-corrected values), it touches the raw T4 Box 12 SIN, it is scoped to
an explicit tax year, and it reverses three boundaries that earlier specs closed
deliberately. Even the parts that look structural (a provider adapter, a content
read path, a persistence table) exist only to carry taxpayer-domain data.

Per the classification rule — a mixed feature is `tax-domain`, and when unsure it
is `tax-domain` — every tax-domain protection applies as written: explicit
tax-year scope, canada.ca as the only tax source, fail-closed behaviour with one
negative acceptance criterion per rule, PII/SIN protection, tax-year isolation,
and data-driven mapping with no CRA line number in C#.

---

## Status: BLOCKED — this PRD is not ready for `/plan`

**Thirteen blocking Class A questions are open** (see *Open questions →
Blocking Class A questions*). They are not stylistic. Each one, answered wrongly
by an agent instead of a human, produces a filing error, a SIN disclosure, or a
reversal of a security boundary that a prior spec closed on purpose.

Additionally, **canada.ca was unreachable while this PRD was written** — every
request, including the site root, returned HTTP 403 (see `## CRA sources`).
Per the blocking-verification rule, nothing was verified, so **this PRD asserts
no new CRA fact at all.** Several of the blocking questions below cannot be
answered without canada.ca access.

What follows is therefore deliberately split:

- **Settled requirements** — invariants that no Class A answer may relax, plus
  Class B decisions that are mine to make and are tagged `[ASSUMED]`.
- **Blocked requirements** — named, scoped, and explicitly *not specified*,
  each pointing at the question that must be answered first.

`/plan` must not be run against the blocked sections. A wrong architecture built
on an unanswered Class A question is exactly the reconciliation this pipeline
exists to prevent.

---

## Problem

**Tax year in scope: 2025 only.**

Spec 014 shipped the complete client intake and document-upload flow. An
accountant creates a firm-scoped 2025 case with an expected-document checklist,
a client redeems a one-time link, answers closed availability questions, and
uploads PDF/JPEG/PNG source documents. Those documents are stored as **opaque
encrypted bytes** outside the web root, with only SIN-free metadata (opaque
content key, media kind, byte count, keyed scope-derived fingerprint, immutable
version, instants) in SQL.

And there the taxpayer data stops. Today:

- **Nothing reads the document.** `IIntakeContentStore` exposes no read member
  by design; `src/ClientIntake/CLAUDE.md` states "there is no way to get the
  content back out, no URL that serves it, and no mapping that could bring it
  into a query or a diagnostic."
- **Nothing classifies it.** A stored document carries the accountant-declared
  `DocumentCategory` of the checklist slot it was uploaded into. No independent
  check confirms the bytes are actually a T4, actually a medical receipt, or
  actually the right taxpayer's document.
- **`IDocumentExtractionIngress` has never been implemented.** Spec 007 declared
  the port and shipped no implementation; spec 010's reserved question F
  ("does this host expose the untrusted document-extraction ingress?") has been
  open since spec 010 and is still open. **This feature is the one that forces
  it.**
- **The engine chain is complete but unfed.** `T4SlipParser` → `T4LineMapper` →
  `T4CaseProcessor` → `ReviewPackageAssembler` → `ReviewPackageProjection` →
  `IReviewRecordRepository.SaveAsync` → `GET /reviews/{recordId}` all exist,
  merged and verified. No production caller ever produces the
  `ValidatedExtraction` that starts the chain. The accountant review screen can
  only render a record that some other, non-existent process created.
- **There is no correction path.** Extraction output is untrusted by rule. An
  accountant who can see a wrong value has no authorized way to fix it: spec
  012's route is read-only, and `src/TaxAI.Portal/CLAUDE.md` gates "editing or
  approving taxpayer values" behind a further approved `tax-domain`
  specification.

The gap is therefore not "add OCR." It is: **the system has a verified tax
pipeline with no authorized way to put a real document into it, and every
mechanism that would close that gap crosses a boundary a prior spec closed on
purpose.**

### Tax-year isolation

2025 is the only supported year, and it is the only year any existing authority
supports (`SupportedExtractionTaxYears`, `SupportedProcessingTaxYears`,
`SupportedReviewTaxYears`, `TaxCases.SupportedTaxYears`,
`T4SlipParser`'s expected year, and `Mappings/T4LineMap.2025.json`). This
feature introduces **no new supported-year authority**. The year an extraction
is attributed to comes only from the server-stored intake case
(`StoredIntakeCase.TaxYear`), never from the document, never from the browser,
never from a system clock. A document whose own printed year differs from the
case year fails closed through spec 007's existing envelope↔payload coherence
check — it is never coerced, and no adjacent-year fallback exists. Supporting
any year other than 2025 is a separate scope change.

---

## Current state and reuse analysis

Grounded in the actual tree at `D:\Projects\TaxAI` on 2026-08-02, not memory.

### Reused unchanged — do NOT re-specify, duplicate, or re-implement

**`TaxLogicEngine`** (`src/TaxLogicEngine/`)
- `T4RawInput`, `T4RawBox` — **this is the T4 extraction payload.** Box values
  are `string`; the engine, not extraction, decides whether a value is numeric.
- `T4SlipParser`, `T4LineMapper`, `IT4LineMapProvider`,
  `EmbeddedT4LineMapProvider`, `Mappings/T4LineMap.2025.json` — the sole CRA
  authority for parsing and box→line mapping.
- `T4BoxCodes`, `T4OtherInformationCodes`, `T4Slip`, `T4MappingResult`,
  `T4SlipBoxAmounts`, `TaxLineEntry`.
- `ParseResult<T>`, `ValidationError`, `MoneyRounding.ToCents`,
  `RecipientIdentifier`.
- All T5 types remain untouched and unconsumed (T5 extraction stays deferred per
  spec 007).

**`DocumentExtraction`** (`src/DocumentExtraction/`) — the seam this feature
finally implements.
- `ExtractionEnvelope`, `ValidatedExtraction` (internal constructor),
  `ExtractedDocumentKind` (`T4Slip = 1`, `GenericEvidence = 2`),
  `ExtractionProvenance` (including `decimal? Confidence`), `ExtractionFields`,
  `ExtractionEnvelopeValidator.Validate`, `SupportedExtractionTaxYears`.
- `IDocumentExtractionIngress` — declared, deliberately unimplemented. **This
  feature implements it; it does not redeclare it.**
- Note: `ExtractedDocumentKind.GenericEvidence` already covers medical-expense
  evidence. **No new enum value is required for medical documents.**

**`CaseProcessing`** — `T4CaseProcessor.Process(ValidatedExtraction?)`,
`ProcessedT4Extraction`, `PayloadStructureGuard`, `SupportedProcessingTaxYears`,
`T4ProcessingFields`. The eight-stage fail-closed chain is reused verbatim.

**`CaseReview`** — `ReviewPackageAssembler.Assemble`,
`AccountantReviewPackage`, `ReviewedSlip`, `ReviewedBoxAmount`,
`ReviewSignOff`, `ReviewDecisionDescriptor`, `SignOffDecision`,
`SupportedReviewTaxYears`, `ReviewFields`.

**`Tenancy`** — `TrustedFirmId`, `ReviewPackageProjection.Project`,
`PersistedReviewRecord` and its closed child records, `IReviewRecordRepository`
(`SaveAsync` already supports versioned save), `RetentionPolicy`,
`MembershipVerificationPolicy`, `SessionLifetimePolicy`, `TaxpayerTextGuard`,
`MonetaryValueGuard`, `UtcInstantGuard`, `OpaqueTokenGuard`, `SlipTypeCodes`.

**`TaxCases`** — `TaxCase`, `DocumentCategory` (`T4Slip`,
`MedicalExpenseEvidence`, `Other`), `ReceivedEvidence`, `EvidenceDescriptor`,
`ReadinessCalculator`, `SafeDiagnosticText`, `SupportedTaxYears`.

**`ClientIntake`** — `IIntakeStore` (including `FindAsync(firm, caseId, …)`
returning `StoredIntakeCase` with its `IntakeDocumentRecord` list and opaque
`ContentKey`), `IIntakeContentStore`, `IIntakeCredentialSource`,
`IAccountantIntakeWorkflow`, `IClientIntakeWorkflow`, the closed intake
vocabularies, `IntakeAuditKind`, and the append-only history model.

**`Persistence`** — the only EF Core project; `SqlIntakeStore`,
`SqlReviewRecordRepository`, migration `20260730000000_AddClientIntake`.

**`TaxAI.Portal`** — the only host and composition root; the read-only review
route, the twelve intake routes, the streaming neutral-name upload transport,
`PortalAuthorization`, and the trusted `taxai_firm_id` claim.

### Repository facts this feature must design around (verified by reading source, not memory)

These are facts about **this codebase**, not CRA claims. They are load-bearing.

1. **`T4SlipParser` requires Box 12 (SIN), Box 54 (employer account number), and
   Box 45 (dental benefits)** — `ValidateRequiredBoxes`,
   `src/TaxLogicEngine/Services/T4SlipParser.cs:146-162`. It also requires
   non-blank `EmployerName`, `EmployerAddress`, `EmployeeName`, and
   `EmployeeAddress`. **An extracted T4 missing any of these cannot be parsed at
   all**, so it can never become a `ProcessedT4Extraction`, a review package, or
   a persisted review record.
2. **`src/TaxLogicEngine/CLAUDE.md` records a `[CONFIRM]`-flagged spec 003
   decision:** "Box 54 is required (so the parser presumes the employer/filing-
   side copy, not the employee copy)." Every document a *client* uploads through
   spec 014 is an employee-side copy. This is a direct, unresolved collision
   between two merged specs (see Q-A4).
3. **`AccountantReviewPackage`, `ReviewedSlip`, `PersistedReviewRecord`,
   `PersistedSlip`, and `SlipTypeCodes` are T4-only.** `SlipTypeCodes.All` is
   exactly `["T4"]` and `IsRecognized` refuses anything else. **There is no
   representation anywhere in this repository for a medical-expense receipt's
   structured content** (see Q-A5).
4. **`ProcessedT4Extraction` deliberately does not carry the raw `T4RawInput`**,
   precisely so a second unmasked SIN carrier cannot exist. Any correction
   workflow that wants to re-parse must hold the raw payload somewhere else.
5. **`IIntakeContentStore` has exactly two members**, `StageAsync` and
   `DeleteIfExistsAsync`. There is no read member (see Q-A1).
6. **`ReviewPackageAssembler.Assemble` accepts only
   `IReadOnlyList<ProcessedT4Extraction?>`** and requires one shared firm, case,
   participant, and tax year plus unique document identifiers.

### Genuinely new — introduced by this feature

- An **application-coordination module** implementing
  `IDocumentExtractionIngress` and orchestrating: read source bytes → classify →
  extract → build `ExtractionEnvelope` → `ExtractionEnvelopeValidator.Validate`
  → `T4CaseProcessor.Process` → accountant review of the extraction →
  correction → `ReviewPackageAssembler.Assemble` →
  `ReviewPackageProjection.Project` → `IReviewRecordRepository.SaveAsync`.
  `[ASSUMED]` home: `src/ExtractionWorkflow/`.
- A declared **document-intelligence port** (classification + field extraction)
  and a declared **content-read port**, both implemented by infrastructure, never
  by a domain module.
- An **infrastructure adapter project** that is the only project permitted an
  OCR/LLM/document-intelligence SDK. `[ASSUMED]` home:
  `src/DocumentIntelligence/`.
- **Additive persistence** for extraction results, classification outcomes,
  confidence, per-field validation outcomes, duplicate determinations, and the
  immutable accountant-correction history, owned by `Persistence` in a new
  additive migration.
- **Accountant HTTP routes and components** in `TaxAI.Portal` for reviewing an
  extraction, correcting it, and accepting it.

### Explicitly NOT created

No second envelope type, validator, supported-year list, parser, mapper, mapping
config, rounding helper, review-package assembler, projection, masking helper,
SIN type, tax-case aggregate, readiness calculator, or duplicate-fingerprint
scheme. Every one of those already exists and is cited above.

---

## The three boundary reversals this feature forces

Named here so `/plan` and the reviewer cannot treat any of them as incidental.

### Reversal 1 — the write-only content store must become readable

Spec 014 made `IIntakeContentStore` write-only on purpose and documented the
reason: unreadable content cannot be rendered, logged, served, or queried.
Extraction requires the bytes. **The property that must survive, whatever
mechanism is chosen, is not "the store is write-only" but "no source byte ever
reaches an HTTP response, a log, a diagnostic, a metric, a SQL column, or a
query."** How the read happens, who may perform it, and whether it is a new port
or an in-flight read during upload is **Q-A1** — a security-boundary decision,
which is Class A in both feature classes and is not mine.

### Reversal 2 — the ingress opens, and taxpayer bytes may leave the process

Spec 010 question F has been reserved through five specs. Implementing the
ingress answers it. Separately, real OCR generally means an external
document-intelligence service — which means **taxpayer source bytes, including
the SIN region of a T4, leaving the TaxAI process**, against spec 011's Canadian
residency posture. Whether that is permitted, and under what constraints, is
**Q-A2**.

### Reversal 3 — an accountant edits a value that becomes a filing input

`src/TaxAI.Portal/CLAUDE.md` gates "editing or approving taxpayer values" behind
a further approved `tax-domain` specification. This is that specification's
subject matter, but the *semantics* — what may be corrected, whether a
correction may add or remove a box, whether a mis-read SIN can be corrected when
no SIN in any form may reach a browser, and what status a corrected value has —
are **Q-A3 and Q-A10**.

---

## Preserved boundaries (non-negotiable, and no Class A answer may relax them)

Any answer to any blocking question must be implementable without weakening any
of these. If an answer would require weakening one, that answer is refused and
must come back to the human.

1. **No SIN in any form** — not full, masked, hashed, tokenized, last-three,
   last-four, presence-flag, or SIN-derived identifier — in any browser model,
   rendered HTML, URL, route value, query, form, cookie, header, SQL column,
   metadata record, audit event, log, metric, trace, exception message,
   diagnostic, tracked test fixture, or persisted provider response. The
   engine's in-memory masked `RecipientIdentifier` remains the single permitted
   in-memory representation and its contract is unchanged.
2. **The raw source bytes remain the sole raw-byte exception**, are never
   described as SIN-free, and are never rendered, downloaded, previewed,
   emailed, exported, logged, or served.
3. **Tenant isolation** — firm scope comes only from the revalidated server-side
   session's trusted `taxai_firm_id` claim; case, participant, and document
   scope come only from server-stored state. No route, query, form, body,
   header, or cookie value is ever an authorization boundary. No new
   firm-omitting taxpayer-data operation is added.
4. **Safe `404`** — missing, foreign-firm, deleted, expired, revoked, and
   inconsistent resources share one empty `404` with no distinguishing signal.
5. **Immutable history** — uploaded document records, upload attempts,
   submissions, extraction results, correction records, and audit events are
   append-only. Nothing is updated or deleted in place; current state is
   projected.
6. **The UI computes no tax and implements no CRA mapping**, holds no tax-year
   policy, and contains no CRA line number, box→line table, arithmetic, or
   rounding.
7. **Deterministic tax modules stay deterministic** — `TaxLogicEngine`,
   `TaxCases`, `DocumentExtraction`, `CaseProcessing`, `CaseReview`, `Tenancy`,
   and `ClientIntake` gain no OCR/LLM/cloud SDK, no ASP.NET Core, no EF Core, no
   filesystem, no network, and no system-clock read.
8. **The deployment refusal is unchanged.** This spec authorizes no production
   deployment, no production content provider, no production
   document-intelligence provider, and no Azure provisioning.
9. **The full existing test suite stays green** via
   `pwsh ./scripts/Invoke-SolutionTests.ps1`, with `minimumTests` raised, never
   lowered.

---

## User stories

1. As an **accountant**, I want an uploaded document to be classified as a T4, a
   medical receipt, or neither, so that a photo of a driver's licence in a T4
   slot never silently becomes tax input.
2. As an **accountant**, I want a client-uploaded 2025 T4 to be turned into the
   same structured slip the engine already understands, so I review one screen of
   boxes and CRA lines instead of retyping a slip.
3. As an **accountant**, I want to see, per field, whether the value came from
   extraction, at what confidence, and whether the engine accepted it, so I know
   where to look rather than trusting a machine.
4. As an **accountant**, I want to correct a wrong extracted value and have the
   correction recorded immutably with who and when, so the resulting return is
   traceable to a human decision.
5. As an **accountant**, I want the same slip uploaded twice — as two different
   scans, not two identical files — to be surfaced as a probable duplicate, so
   income is neither doubled nor dropped.
6. As a **compliance reviewer**, I want extraction output, confidence, and
   classification to be treated as untrusted input that must still pass the
   existing fail-closed engine chain, so no machine output ever becomes tax truth
   directly.
7. As a **compliance reviewer**, I want a document whose printed year is not the
   case's 2025 year to be rejected rather than coerced, so a 2024 slip cannot
   enter a 2025 engagement.
8. As a **security reviewer**, I want the source bytes to remain unreachable from
   every route, log, query, and diagnostic even though extraction now reads them,
   so opening the read path does not open a disclosure path.
9. As a **firm administrator**, I want extraction to be impossible outside the
   firm that owns the case, so a document is never extracted, corrected, or
   reviewed under a foreign tenant.
10. As an **engine maintainer**, I want every tax judgement to remain in
    `TaxLogicEngine` and its JSON config, so no CRA line number, box→line rule,
    or rate is ever written into an extraction or UI module.

---

## Functional requirements

`[reuse]` = reuse of an existing type. `[boundary]` = an invariant restated from
an approved CLAUDE.md or prior spec, not a new decision. `[design]` = a Class B
decision made here. `[BLOCKED]` = deliberately unspecified pending a Class A
answer.

### Reuse and the single-authority rule

**FR-1 `[reuse]`** The feature SHALL **implement** spec 007's
`IDocumentExtractionIngress` and SHALL reuse `ExtractionEnvelope`,
`ValidatedExtraction`, `ExtractedDocumentKind`, `ExtractionProvenance`,
`ExtractionFields`, `ExtractionEnvelopeValidator`, and
`SupportedExtractionTaxYears` unchanged. It SHALL NOT declare a second envelope,
validator, provenance record, document-kind discriminator, or supported-year
authority.

**FR-2 `[reuse]`** Medical-expense documents SHALL use the existing
`ExtractedDocumentKind.GenericEvidence` value and the existing
`TaxCases.DocumentCategory.MedicalExpenseEvidence`. No new document-kind enum
value or category enum is introduced.

**FR-3 `[reuse]`** Every typed slip SHALL come only from `T4SlipParser`; every
CRA line entry only from `T4LineMapper` plus `IT4LineMapProvider`; every
processed result only from `T4CaseProcessor.Process`; every review package only
from `ReviewPackageAssembler.Assemble`; every persisted record only from
`ReviewPackageProjection.Project` followed by
`IReviewRecordRepository.SaveAsync`. No new parser, mapper, assembler,
projection, or repository is introduced, and none of the existing ones is
bypassed, re-implemented, or re-ordered.

**FR-4 `[boundary]`** No project introduced or modified by this feature SHALL
contain a CRA line-number literal, a box→line table, a tax rate, a threshold, a
limit, a gross-up factor, a credit percentage, an eligibility rule, or a read of
a `TaxLogicEngine.Mappings` resource. Box→line mapping remains JSON-config-driven
inside `TaxLogicEngine`.

**FR-5 `[design]`** No new coordination or UI project SHALL declare a box-code
literal. A box code produced by extraction is a value copied from the document by
an untrusted provider and is judged only by the engine: unknown codes flow to
`T4OtherInformation` and surface as `T4MappingResult.UnmappedBoxCodes`, exactly
as they do today.

**FR-6 `[boundary]`** No project introduced by this feature SHALL perform
currency arithmetic or rounding. Extracted amounts remain untrusted `string`
values on `T4RawBox` until the engine parses them. `MoneyRounding` is reached
only indirectly, through the parser and mapper.

### Tax year and year isolation

**FR-7 `[boundary]`** The tax year of every extraction SHALL be the server-stored
`StoredIntakeCase.TaxYear` for the firm-scoped case. It SHALL NOT be read from
the document, from a request, from a browser, or from a system clock.

**FR-8 `[boundary]`** The envelope's `TaxYear` SHALL be that server-derived year
and the `T4RawInput.Year` SHALL be exactly what the document was extracted to
show. A mismatch SHALL fail closed through the existing
`ExtractionEnvelopeValidator` coherence check. Neither value is ever coerced, and
no adjacent-year fallback exists.

**FR-9 `[boundary]`** 2025 is the only supported year. This feature declares no
new supported-year list, no "current year" concept, and no tax-year literal as
policy in any new coordination, infrastructure, or UI project.

### Scope, tenancy, and authorization

**FR-10 `[boundary]`** Every extraction, correction, acceptance, and read
operation SHALL take a `TrustedFirmId` as an explicit, non-optional scope, be
additionally scoped to the server-stored case and document, and revalidate the
active user, active membership, and live session on every protected request.

**FR-11 `[boundary]`** No new firm-omitting taxpayer-data operation SHALL be
added to any port or store contract. The two existing firm-omitting digest
lookups in `IIntakeStore` remain the only ones.

**FR-12 `[boundary]`** A document, case, extraction, or correction that does not
exist, belongs to another firm, or is inconsistent SHALL produce the **same**
empty `404`, with nothing to distinguish the cases and no identifier echoed.

**FR-13 `[design]`** Extraction SHALL be initiated only by an explicit,
authenticated, firm-scoped **accountant** action within a single request. No
background worker, hosted service, queue, timer, client-triggered path, or
anonymous path may initiate extraction. `[ASSUMED]` — chosen because a
background executor would need a trusted-firm scope mechanism that does not
exist in this repository and inventing one is a security decision, not mine.

**FR-14 `[design]`** One document per ingress call. Batch extraction,
cross-document reconciliation, and retry orchestration are out of scope.
`[ASSUMED]`.

### Untrusted input

**FR-15 `[boundary]`** Classification results, extracted field values, and
confidence scores are **untrusted metadata and never tax truth**. No extracted
value becomes a filing input except by passing, unchanged and in order, the
existing fail-closed chain
`ExtractionEnvelopeValidator` → `T4CaseProcessor` → `ReviewPackageAssembler` →
`ReviewPackageProjection`.

**FR-16 `[boundary]`** No confidence value SHALL be presented, stored, or
described as an accuracy guarantee, a correctness measure, or a substitute for
accountant review.

**FR-17 `[design]`** A provider error, timeout, cancellation, empty result, or
malformed provider response SHALL fail closed: no partial extraction is stored,
no review package or persisted review record is created, no case is mutated, and
a single fixed structural error is returned. Nothing throws to the caller on
untrusted provider output.

**FR-18 `[boundary]`** The coordination module SHALL read no system clock, no
environment variable, no current user, and no HTTP context. The current instant
and the actor reference arrive as arguments, exactly as in `ClientIntake`.

### Provider adapter placement and dependency direction

**FR-19 `[boundary/design]`** The document-intelligence provider adapter SHALL
live in a **separate infrastructure project** and SHALL be the only project in
the repository permitted an OCR, LLM, document-intelligence, or cloud SDK
reference. `[ASSUMED]` home: `src/DocumentIntelligence/`.

**FR-20 `[boundary]`** Dependencies point inward only. The coordination module
declares the classification/extraction port and the content-read port; the
infrastructure adapters implement them. `TaxLogicEngine`, `TaxCases`,
`DocumentExtraction`, `CaseProcessing`, `CaseReview`, `Tenancy`, and
`ClientIntake` SHALL gain no reference to any provider SDK, ASP.NET Core, EF
Core, SQL, filesystem, or network API. `Persistence` remains the only EF Core
project. `TaxAI.Portal` remains the only executable project, the only ASP.NET
Core project besides its test project, and the single composition root.

**FR-21 `[design]`** The production document-intelligence provider SHALL NOT ship
in this spec. As with spec 014's content store, only a Development/test adapter
is registered, and startup fails closed on missing or invalid provider
configuration. `[ASSUMED]` — this preserves the existing deployment refusal
rather than partially lifting it.

### PII

**FR-22 `[boundary]`** No SIN in any form — full, masked, hashed, tokenized,
last-three, last-four, presence flag, or SIN-derived identifier — SHALL reach any
browser model, rendered HTML, URL, route value, query string, form field,
cookie, header, SQL column, metadata record, audit event, log, metric, trace,
exception message, diagnostic, or tracked test fixture. This feature introduces
no new SIN property, no new SIN type, and no masking, redaction, or scrubbing
helper.

**FR-23 `[boundary]`** The raw `T4RawInput` and the raw provider response SHALL
be handled by **omission**: never rendered, never serialized, never logged, never
placed in an error message, and never persisted in any form that a query, route,
log, or diagnostic can reach.

**FR-24 `[boundary]`** The source-byte read introduced by Reversal 1 SHALL be
in-process only. Source bytes SHALL never be written to an HTTP response body,
a log, a metric, a trace, a diagnostic, a SQL column, a temporary file inside the
web root, or any queryable store. No route SHALL return, redirect to, or
reference source content. The existing prohibition on preview, download, inline
rendering, print, email, and export is unchanged.

**FR-25 `[boundary]`** Error messages originating in new modules SHALL be fixed,
non-interpolated constants identified by closed field-name constants. They SHALL
never echo a caller value, a document value, a file name, a content key, a
fingerprint, a provider payload, or a prompt.

### Immutability and audit

**FR-26 `[design]`** Extraction results, classification outcomes, per-field
validation outcomes, duplicate determinations, and accountant corrections SHALL
be **append-only**. A re-extraction or a correction creates a new immutable
version; nothing is updated or deleted in place. Current state is a projection.

**FR-27 `[design]`** Every extraction attempt, classification outcome,
correction, duplicate determination, and review-record creation SHALL append one
immutable audit event carrying only structural facts — kind, actor kind, opaque
actor reference, sequence, and instant. An audit event SHALL carry no SIN, no
amount, no box code, no CRA line, no file name, no content key, no fingerprint,
no prompt, no provider payload, and no free text. Whether this extends the
existing `IntakeAuditKind` vocabulary or declares a parallel one is `[ASSUMED]`
Class B for `/plan`.

### Preserved behaviour

**FR-28 `[boundary]`** This feature SHALL NOT change the spec 012 review route,
its authorization, its browser allowlist, its safe `404`, or its headers; the
spec 014 intake routes, upload transport, rate policies, cookie scope, or
components; `/health`; the existing EF review, identity, or intake tables; the
initial or `AddClientIntake` migrations; or any existing public contract of
`TaxLogicEngine`, `TaxCases`, `DocumentExtraction`, `CaseProcessing`,
`CaseReview`, `Tenancy`, or `ClientIntake`. Any new schema SHALL arrive in a new
additive migration.

**FR-29 `[boundary]`** Static assets SHALL continue to be served by
`UseStaticFiles()` middleware, never `MapStaticAssets()`, and no global render
mode, additional interactive component beyond an explicitly justified one,
WebAssembly project, npm tree, CDN, or third-party asset SHALL be introduced.

**FR-30 `[boundary]`** No repository-level test SHALL assert a total or closed
project set for `TaxAI.sln`; each new project is asserted exactly once at its
approved path. Any new test project SHALL be registered in
`scripts/test-projects.json` in the same PR that introduces it.

### BLOCKED — named, deliberately unspecified

Each item below is real scope that this PRD refuses to specify until its Class A
question is answered. `/plan` must not design any of them yet.

- **FR-B1 — Source-byte read mechanism.** Blocked on **Q-A1**.
- **FR-B2 — Whether source bytes may leave the process, and to which provider
  under which residency/retention/human-review constraints.** Blocked on
  **Q-A2**.
- **FR-B3 — Whether the Box 12 SIN may be extracted, where the raw value may
  exist, and for how long.** Blocked on **Q-A3**.
- **FR-B4 — What happens when an employee-copy T4 lacks a parser-required box
  (12, 45, or 54).** Blocked on **Q-A4**.
- **FR-B5 — The structured output, field set, and destination for medical-expense
  receipts.** Blocked on **Q-A5**.
- **FR-B6 — Whether medical/health content may reach a browser and how the review
  allowlist changes.** Blocked on **Q-A6**.
- **FR-B7 — Confidence thresholds and any gating behaviour they drive.** Blocked
  on **Q-A7**.
- **FR-B8 — Any new field-level validation rule beyond the engine's existing
  rules.** Blocked on **Q-A8**.
- **FR-B9 — Document-level duplicate identity and the response to a detected
  duplicate.** Blocked on **Q-A9**.
- **FR-B10 — What an accountant may correct and the status of a corrected
  value.** Blocked on **Q-A10**.
- **FR-B11 — Whether and how a `PersistedReviewRecord` is created from an
  extraction.** Blocked on **Q-A11**.
- **FR-B12 — Retention and purge of extraction results, correction history, and
  source documents.** Blocked on **Q-A12**.
- **FR-B13 — Whether an accountant may override a classification.** Blocked on
  **Q-A13**.

---

## Out of scope

- Tax calculation of any kind, T1 assembly or generation, NETFILE/EFILE, CRA
  submission, and any Azure or production provisioning — excluded by the human's
  instruction and unchanged from spec 014.
- **Any CRA eligibility, box, line, rate, threshold, limit, deadline, gross-up,
  or credit rule.** None is asserted anywhere in this PRD (see `## CRA sources`).
- Medical-expense eligibility, claimant rules, dependant rules, eligible-period
  rules, receipt sufficiency, certification, totals, thresholds, credits, or
  advice.
- T5 extraction and every document category other than T4 and medical-expense
  evidence; `DocumentCategory.Other` remains refused.
- Raw-document preview, download, inline rendering, print, email, export, or any
  client- or accountant-facing delete path for accepted content.
- Handwriting interpretation, translation, image enhancement as a tax
  representation, and any claim that a scan is legible, authentic, or complete.
- Client-facing extraction results. Extraction output, confidence, classification,
  corrections, and review packages are accountant-facing only; the client intake
  surface is unchanged.
- Notification or email delivery of any extraction outcome.
- Firm, user, membership, or identity-provider provisioning.
- Multi-year support, cross-year reconciliation, and prior-year comparison.
- Cross-slip aggregation, income totalling, and any arithmetic over multiple
  documents.
- A production document-intelligence provider, a production content-store
  provider, hosting, TLS, exposure, secrets, operations, migration execution, and
  production deployment authorization.
- Any accidental change to existing `TaxLogicEngine`, `TaxCases`,
  `DocumentExtraction`, `CaseProcessing`, `CaseReview`, `Tenancy`,
  `ClientIntake`, `Persistence`, or portal behaviour. The full suite must remain
  green.

---

## Acceptance criteria

Each criterion is one mechanically checkable Given/When/Then test.

**These cover the settled requirements only.** Acceptance criteria for FR-B1
through FR-B13 cannot be written until the corresponding Class A questions are
answered; writing them now would encode an assumption. That gap is recorded in
the Definition of Done below.

### Reuse and single authority

**AC-1 — The declared ingress is implemented, not replaced.** Given the compiled
solution, When types implementing `DocumentExtraction.Ports.IDocumentExtractionIngress`
are enumerated, Then exactly one implementation exists, it lives in the new
coordination module, and no second envelope, validator, provenance, document-kind,
or supported-year type is declared anywhere.

**AC-2 — Medical documents reuse the existing kind and category.** Given the
compiled solution, When `ExtractedDocumentKind` and
`TaxCases.Models.DocumentCategory` are inspected, Then their member sets are
byte-for-byte the same as before this feature, and the medical path uses
`GenericEvidence` and `MedicalExpenseEvidence`.

**AC-3 — No new tax authority.** Given the source of every project added or
modified by this feature, When it is scanned, Then it contains no CRA line-number
literal, no box→line table, no box-code literal, no tax rate, no threshold, no
gross-up factor, no credit percentage, and no read of a `TaxLogicEngine.Mappings`
resource.

**AC-4 — No arithmetic or rounding.** Given the new coordination and UI projects,
When their source is scanned, Then they contain no `decimal` token, no
`Math.Round`, no `MidpointRounding`, no `double`, and no `float`.

**AC-5 — The engine chain is invoked, not reimplemented.** Given a successful T4
extraction, When the call graph is inspected, Then the typed slip came only from
`T4SlipParser` via `T4CaseProcessor`, the line entries only from `T4LineMapper`,
and no new parsing, mapping, or projection code executed.

**AC-6 — Engine errors surface verbatim.** Given a T4 payload the engine rejects,
When extraction runs, Then the returned errors are the engine's own
`ValidationError`s in the engine's own order, neither translated, filtered,
re-worded, nor interleaved with new-module errors.

### Tax year and year isolation

**AC-7 — The year comes from the case.** Given a firm-scoped 2025 intake case and
a request that supplies a tax year in a route value, query string, form field,
header, and cookie, When extraction runs, Then the envelope's year is 2025 from
`StoredIntakeCase.TaxYear` and every supplied value is ignored.

**AC-8 — Document year mismatch fails closed.** Given a 2025 case and a document
extracted to show `T4RawInput.Year` = 2024, When the envelope is validated, Then
it fails closed with the existing year-coherence error, no `ProcessedT4Extraction`
is produced, and neither year is coerced.

**AC-9 — Adjacent-year fallback does not exist.** Given a case whose stored year
is 2024 and separately 2026, When extraction is attempted, Then each fails closed,
no extraction result is stored, and neither is treated as 2025.

**AC-10 — No new year authority.** Given the new projects, When their source is
scanned, Then none declares a supported-year list, a current-year concept, an
adjacent-year fallback, or a four-digit tax-year literal as policy.

### Scope, tenancy, and safe failure

**AC-11 — Firm scope is server-derived.** Given a live firm-F accountant session
and a request naming firm G in every available input channel, When extraction is
requested, Then G is never used, no firm-G read or write occurs, and a bound firm
field is refused.

**AC-12 — Foreign-firm document is a plain 404.** Given a document belonging to
firm G, When a firm-F accountant requests extraction for it, Then the response is
the same empty `404` returned for a document that does not exist, and the two
responses are byte-identical.

**AC-13 — Revoked session fails before any work.** Given an accountant session
revoked server-side, When extraction is requested, Then it fails before any
content read, provider call, store read, or store write occurs.

**AC-14 — Inactive membership fails before any work.** Given an accountant whose
firm membership is no longer active, When extraction is requested, Then it fails
before any content read, provider call, or store operation.

**AC-15 — No firm-omitting taxpayer operation.** Given every port and store
contract added or modified by this feature, When their members are enumerated,
Then every taxpayer-data member takes a `TrustedFirmId` with no overload,
optional argument, or default that omits it.

**AC-16 — No anonymous or client-initiated extraction.** Given the portal route
inventory, When it is enumerated, Then every extraction, correction, and
acceptance route requires an authenticated firm-scoped accountant, no anonymous
client-intake route reaches extraction, and every mutation is a non-GET protected
by antiforgery.

**AC-17 — No background initiator.** Given the composition root and the new
projects, When they are inspected, Then no `IHostedService`, background service,
timer, queue consumer, or fire-and-forget task initiates extraction.

### Untrusted input and fail-closed behaviour

**AC-18 — Provider failure fails closed.** Given a provider adapter that throws,
When extraction runs, Then a single fixed structural error is returned, nothing is
thrown to the caller, no extraction result or review record is created, and no
case is mutated.

**AC-19 — Provider timeout fails closed.** Given a provider adapter that
cancels/times out, When extraction runs, Then the same fail-closed outcome holds
and no partial extraction is persisted.

**AC-20 — Malformed provider response fails closed.** Given a provider response
that is empty, or carries a null box list, or carries a box with a null code, When
extraction runs, Then it fails closed with a structured error, nothing is stored,
and nothing throws.

**AC-21 — Unclassified document produces no tax data.** Given a document the
classifier does not classify as either supported kind, When extraction runs, Then
no `T4RawInput`, no `ValidatedExtraction`, no `ProcessedT4Extraction`, and no
review record is produced, and the outcome is recorded as an immutable audit event
only.

**AC-22 — Confidence never gates by accident.** Given two otherwise identical
extractions with confidence `0m` and `1m`, When each is validated by the existing
`ExtractionEnvelopeValidator`, Then both are accepted identically, confirming that
no undeclared confidence threshold was introduced by this feature.

**AC-23 — Out-of-range confidence fails closed.** Given provenance confidence
`-0.1m` and separately `1.1m`, When the envelope is validated, Then each fails
closed through the existing validator with the confidence field named.

**AC-24 — Duplicate box codes still reach the parser.** Given an extraction whose
payload carries the same box code twice, When it flows through the chain, Then no
new module collapses, deduplicates, reorders, or drops a box, and the engine's own
duplicate-box rejection is what fires.

**AC-25 — Determinism.** Given the same stored document, the same case, the same
supplied instant, and a fixed deterministic provider stub, When extraction runs
twice, Then the two results are equal — same success/failure, same error sequence,
same produced payload.

**AC-26 — No clock, environment, or current-user read.** Given the coordination
module's source and emitted metadata, When inspected, Then it contains no
`DateTime.Now`/`UtcNow`, no `Environment` read, no current-user read, and no
`HttpContext` reference; instants and actor references are parameters.

### PII

**AC-27 — No SIN in any structured surface.** Given a test-only source document
whose T4 Box 12 contains `123456789`, When the extraction is run end to end and
every rendered HTML response, browser model, URL, cookie, header, log entry,
metric, trace, exception message, audit record, and every column of every SQL row
written by this feature is captured, Then no capture contains `123456789`, any
separator-variant of it, any masked or partial rendering of it, any hash or token
derived from it, or any SIN-presence flag.

**AC-28 — No new SIN representation.** Given every assembly added or modified by
this feature, When its public surface and emitted metadata are inspected, Then it
declares no SIN property or SIN type, constructs no `RecipientIdentifier`, and
contains no masking, redaction, or scrubbing helper.

**AC-29 — Raw payload is omitted from output.** Given a valid T4 extraction whose
payload contains a SIN and populated identity text, When every `ToString()`,
every `ValidationError`, every log entry, and every supported direct JSON
serialization produced by the new modules is captured, Then none contains any box
value, identity text, or nine-consecutive-digit substring.

**AC-30 — No source byte escapes.** Given a completed extraction of a document
whose bytes contain a distinctive marker sequence, When every HTTP response body,
log entry, metric, trace, diagnostic, temporary file inside the web root, and SQL
column written by this feature is captured, Then none contains the marker
sequence.

**AC-31 — No content-serving route.** Given the portal route inventory, When it is
enumerated, Then no route returns, streams, redirects to, or references source
content, and no preview, download, print, email, or export path exists.

**AC-32 — Fixed error messages.** Given every error the new modules can return,
When their message templates are inspected, Then each is a non-interpolated
constant identified by a closed field-name constant, and none can carry a caller
value, file name, content key, fingerprint, provider payload, or prompt.

### Immutability and audit

**AC-33 — Append-only history.** Given every new store contract, When its members
are enumerated, Then none updates or deletes an extraction result, correction, or
audit event in place, and a re-extraction produces a new version alongside the
prior one.

**AC-34 — Audit is structural only.** Given a completed extraction, correction,
and acceptance, When every appended audit row is read, Then each carries only kind,
actor kind, opaque actor reference, sequence, and instant, and no SIN, amount, box
code, CRA line, file name, content key, fingerprint, prompt, or free text.

### Boundary and dependency direction

**AC-35 — Provider SDK is confined.** Given every project file in the solution,
When package and project references are enumerated, Then exactly one project
declares an OCR/LLM/document-intelligence/cloud SDK, that project is the new
infrastructure adapter, and no domain or coordination module references it.

**AC-36 — Deterministic modules stay clean.** Given `TaxLogicEngine`, `TaxCases`,
`DocumentExtraction`, `CaseProcessing`, `CaseReview`, `Tenancy`, and
`ClientIntake`, When their references are enumerated after this feature, Then none
gained ASP.NET Core, EF Core, SQL, a provider SDK, a filesystem API, a network
API, or a clock read, and `ClientIntake` still declares no `PackageReference`.

**AC-37 — Persistence remains the only EF project; portal the only host.** Given
the solution, When project references are enumerated, Then `Persistence` is the
only project referencing EF Core, `TaxAI.Portal` is the only executable and the
only ASP.NET Core project besides its own test project, and all registration
remains rooted in `ModuleRegistration`.

**AC-38 — No production provider ships.** Given a non-Development host start with
no explicitly registered document-intelligence provider, When startup runs, Then
it refuses to start, and the existing spec 011 deployment refusal is unchanged.

**AC-39 — Startup fails closed on invalid provider configuration.** Given missing,
empty, or invalid provider configuration in separate host starts, When startup
runs, Then each refuses to start with no taxpayer-data surface exposed.

### Preserved behaviour

**AC-40 — The review route is untouched.** Given `GET /reviews/{recordId:guid}`,
When its route, authorization policy, headers, trusted-firm extraction, browser
projection allowlist, collection order, single `FindAsync` call, and empty `404`
are compared before and after this feature, Then all are identical.

**AC-41 — Spec 014 surfaces are untouched.** Given the twelve intake routes, the
upload transport, the rate/concurrency policies, the client cookie scope, and the
component inventory, When compared before and after, Then all are identical, and
`/health` remains byte-identical to spec 010.

**AC-42 — Schema change is additive only.** Given the EF model and migration
history, When compared before and after, Then the initial and `AddClientIntake`
migrations are unmodified, no existing review, identity, or intake table is
altered, and any new table arrives only in a new additive migration.

**AC-43 — Existing contracts unchanged.** Given the public surfaces of
`TaxLogicEngine`, `TaxCases`, `DocumentExtraction`, `CaseProcessing`,
`CaseReview`, `Tenancy`, and `ClientIntake`, When compared before and after, Then
no public type, member, enum value, constructor, or signature was added, renamed,
removed, or changed, except any change explicitly authorized by an answered Class
A question and recorded in an amendment to this PRD.

**AC-44 — Full suite green.** Given `pwsh ./scripts/Invoke-SolutionTests.ps1`,
When it runs after this feature, Then every registered test project runs, nothing
is skipped, no test fails, every project meets its recorded minimum, and each new
test project is registered in `scripts/test-projects.json`.

**AC-45 — No tax behaviour changed.** Given the existing `TaxLogicEngine.Tests`,
`TaxCases.Tests`, `DocumentExtraction.Tests`, `CaseProcessing.Tests`, and
`CaseReview.Tests` suites, When they run unmodified after this feature, Then every
one passes without a test being removed, skipped, weakened, or rewritten.

**AC-46 — Solution assertions stay open.** Given every repository-level test, When
inspected, Then none asserts a total or closed project set for `TaxAI.sln`; each
asserts only that its own projects are declared exactly once at their approved
paths.

**AC-47 — Static asset middleware unchanged.** Given `Program.cs`, When inspected,
Then `UseStaticFiles()` is still called parameterless and `MapStaticAssets()` is
still absent, and no global render mode, WebAssembly project, npm tree, CDN, or
third-party asset was introduced.

---

## Planning constraint (from the human, recorded for `/plan`)

`/plan` SHALL use a small number of substantial tasks, splitting only where
extraction, validation, persistence, or review UI are genuinely independent. It
SHALL NOT create microtasks for DTOs, markup, individual fields, individual
endpoints, migrations, or documentation, and SHALL NOT create speculative
contract-only scaffolds or empty modules.

**`/plan` SHALL NOT be run until the blocking Class A questions below are
answered and this PRD is amended**, because Q-A1, Q-A5, Q-A10, and Q-A11 each
determine how many independent tasks there are.

---

## Open questions

### Blocking Class A questions

Every question below is domain, CRA, tax, PII, tenancy, or security. None is
mine to answer. Options are listed to make each answerable in one pass; listing
an option is not a recommendation.

---

**Q-A1 — How may extraction read the encrypted source bytes, and who may do it?**

Spec 014 made `IIntakeContentStore` write-only on purpose:
`src/ClientIntake/CLAUDE.md` says "there is no way to get the content back out,
no URL that serves it, and no mapping that could bring it into a query or a
diagnostic." Extraction needs the bytes. Please decide:

1. Which mechanism?
   (a) add a read member to `IIntakeContentStore`;
   (b) declare a separate narrow port (e.g. an extraction-only content reader)
       implemented by the same adapter, resolvable by exactly one consumer;
   (c) never read stored content — extract from the in-flight upload stream
       during the spec 014 upload request, before the bytes are encrypted and
       stored, and store only the extraction result.
2. Who may invoke it — accountant-initiated requests only, or also some
   non-interactive path?
3. Must the read require the same server-derived `TrustedFirmId` + `CaseId` +
   opaque `ContentKey` triple that keyed the write, so a key alone is not
   sufficient?
4. Confirm the invariant that must survive verbatim: no source byte may reach an
   HTTP response, log, metric, trace, diagnostic, SQL column, in-web-root file,
   or any queryable store.

*Why blocked:* this is a security boundary and a PII exposure decision. Both are
Class A in every feature class.

---

**Q-A2 — May taxpayer source bytes leave the TaxAI process, and to whom?**

Real classification and OCR usually mean an external document-intelligence
service. A T4 image contains the taxpayer's SIN. Spec 011 established a Canadian
residency posture and this build refuses non-Development startup. Please decide:

1. Is an external provider permitted at all, or must the MVP be strictly
   in-process/on-host with no network egress of taxpayer content?
2. If external: which residency constraint (Canadian region only?), which
   encryption-in-transit requirement, what provider-side retention (zero
   retention?), and is provider-side human review permitted or forbidden?
3. May the SIN region of the slip be included in what is sent, or must it be
   excluded — and if excluded, see Q-A3, because the parser requires Box 12.
4. May any prompt, provider request, or provider response be logged or persisted
   anywhere, in any form?

*Why blocked:* data residency and third-party disclosure of taxpayer PII.

---

**Q-A3 — May the Box 12 SIN be extracted, where may the raw value live, and how
is a mis-read SIN corrected?**

`T4SlipParser.ValidateRequiredBoxes` (`src/TaxLogicEngine/Services/T4SlipParser.cs:146-162`)
**requires Box 12**. Without a SIN there is no `T4Slip`, no
`ProcessedT4Extraction`, no review package, and no persisted review record. But
no SIN in any form may reach a browser, SQL column, log, or audit record. Please
decide:

1. Is extraction authorized to read the SIN off the document at all?
2. If yes: may the raw value exist only in memory for the duration of one
   request, or may it be persisted in any form (encrypted column, keyed digest,
   anything)? The current repository answer for every structured surface is
   "no SIN in any form" — confirm that stands.
3. If the raw payload is not persisted, then a correction or re-review cannot
   re-parse without re-extracting. Is re-extraction on every correction
   acceptable, or is some other mechanism authorized?
4. How does an accountant correct a mis-read SIN, given that neither the
   extracted value nor the corrected value may be displayed? Is the answer
   "SIN is never correctable in the UI", "the accountant re-enters a SIN blind
   into a write-only field", or "documents with an unreadable SIN are refused"?
5. If extraction may **not** read the SIN, confirm that the T4 path cannot
   produce a review record at all in this spec, and state what it should produce
   instead.

*Why blocked:* this is the single highest-risk PII decision in the feature.

---

**Q-A4 — Does the employee copy of a 2025 T4 carry Box 54 and Box 45, and if not,
what happens?**

`src/TaxLogicEngine/CLAUDE.md` records a `[CONFIRM]`-flagged spec 003 decision:
"Box 54 is required (so the parser presumes the employer/filing-side copy, not
the employee copy)." Every spec 014 upload is a **client-supplied, employee-side**
document. If the employee copy does not print the employer's payroll account
number, then **every extracted client T4 fails the merged parser**, and this
feature produces nothing.

1. Does the CRA's 2025 T4 employee copy carry Box 54? Does it carry Box 45?
   **This must be verified on canada.ca with a citation. canada.ca was
   unreachable while this PRD was written (HTTP 403 on every request), so I could
   not verify it and did not guess.**
2. If it does not: does spec 015 change `T4SlipParser`'s required-box rule — a
   tax-semantics change to merged, shipped code that would need its own CRA
   verification and would invalidate the spec 003 `[CONFIRM]` decision — or does
   it refuse such documents and tell the accountant to obtain the employer copy?
3. Same question for the parser's required non-blank `EmployerAddress` and
   `EmployeeAddress` when a scan omits or garbles them.

*Why blocked:* a CRA form fact plus a change to merged tax semantics.

---

**Q-A5 — What is a medical-expense receipt's structured output, and where does it
go?**

There is **no representation anywhere in this repository** for a medical
receipt's content. `AccountantReviewPackage`, `ReviewedSlip`,
`PersistedReviewRecord`, `PersistedSlip`, and `SlipTypeCodes` (whose `All` is
exactly `["T4"]`) are T4-only. The human's instruction says the output must be
"structured data ready for the existing review-package flow" — for medical
receipts, that flow currently cannot represent the data at all. Please decide:

1. Which fields, if any, may be extracted from a medical receipt (amount, date,
   provider name, patient name, service description, payment method, none)?
2. Does any extracted amount claim to be an **eligible** medical expense? Spec
   014 explicitly excluded eligibility, and I will not invent an eligibility
   rule.
3. Does medical output map to a CRA line? If yes: **which line, for tax year
   2025, verified on canada.ca**, and delivered as a new JSON mapping config in
   `TaxLogicEngine/Mappings/` (never a C# literal, per CLAUDE.md). If no, medical
   output is non-tax metadata and must be described as such.
4. Do `AccountantReviewPackage` / `PersistedReviewRecord` / `SlipTypeCodes` get
   extended to carry non-T4 content — a change to three merged public contracts —
   or does medical output live in a separate, non-review store?
5. Or is medical extraction limited in this spec to **classification only** —
   "this is a medical receipt" — with no field extraction at all?

*Why blocked:* tax semantics, a possible CRA line mapping, and a change to merged
public contracts.

---

**Q-A6 — May medical/health content reach a browser?**

A medical receipt's provider name, patient name, service description, and amount
are health information — a sensitive category this repository has never
authorized in a browser projection. Spec 012's review allowlist is closed and
spec 014 kept it closed. Please decide:

1. May any extracted medical field be rendered to an accountant?
2. If yes, exactly which fields join the browser allowlist, and does that
   allowlist expansion apply to the existing review route or only to a new
   extraction-review surface?
3. Are provider name and patient name treated like the two already-approved
   display-PII fields (taxpayer and employer display names), or more strictly?
4. May any medical field be logged, audited, or stored in SQL?

*Why blocked:* PII exposure and an explicit browser-allowlist expansion, both
gated by `src/TaxAI.Portal/CLAUDE.md`.

---

**Q-A7 — Is there a confidence threshold, and what does it gate?**

Spec 007 deliberately defined **no** threshold: `decimal? Confidence` in `[0,1]`
inclusive, untrusted metadata, and "the contract SHALL NOT reject a well-formed
extraction merely because confidence is low." The request asks for confidence
scoring. Please decide:

1. Is a numeric threshold now authorized? At what value, and is it per-field,
   per-document, or both?
2. What does crossing it do — refuse the extraction, require an explicit
   accountant confirmation before a review record may be created, or only flag
   for attention?
3. Does a below-threshold field block review-record creation, or is that always
   an explicit human act regardless (see Q-A11)?
4. Confirm that no confidence value may be presented as an accuracy or
   correctness guarantee.

*Why blocked:* "what must be rejected" is Class A, and this reverses an explicit
spec 007 decision.

---

**Q-A8 — Does "field-level validation" mean anything beyond the engine's existing
rules?**

`T4SlipParser` already rejects: missing year, wrong year, blank employer/employee
identity fields, missing Box 12/45/54, non-numeric amounts, negative amounts,
unknown province codes, unknown employment codes, and duplicate boxes. Please
decide:

1. Is field-level validation exactly "surface the engine's existing per-field
   result per field", with no new rule? (This is the only option that asserts no
   new tax fact.)
2. Or is any **new** rule wanted — for example a cross-box consistency check, a
   plausibility bound, or a maximum? Every such rule is a CRA-verifiable tax rule
   and each one must be named and verified on canada.ca before it enters this
   PRD. I did not invent any.
3. May a new validation rule ever *reject* a document the engine accepts, or only
   *flag* it for the accountant?

*Why blocked:* any new validation rule is a tax rule.

---

**Q-A9 — What makes two documents the same slip, and what happens then?**

Spec 014 already rejects **byte-identical** duplicates within a firm and case
(keyed scope-derived fingerprint). Spec 015's duplicate detection presumably
means the same slip arriving as two different scans or photos. Getting this wrong
either drops income or doubles it. Please decide:

1. What is duplicate identity for a T4 — employer account number + SIN + year?
   employer name + Box 14? something else? (Any identity rule using the SIN has
   its own PII consequence; see Q-A3.)
2. What is duplicate identity for a medical receipt, if medical field extraction
   exists at all (see Q-A5)?
3. Is a detected duplicate **rejected**, or **flagged for the accountant to
   decide**? Fail-closed argues for "never silently discard a document"; a wrong
   auto-reject drops income.
4. Must duplicate comparison stay scoped to the exact server-derived firm and
   case, so no cross-tenant or cross-case oracle is created (spec 014 FR-29)?
   I assume yes, but confirm.
5. Two genuinely different T4s from the **same employer** for the same taxpayer
   are legitimate. Confirm the rule must not treat them as duplicates.

*Why blocked:* an incorrect answer is a filing error in either direction.

---

**Q-A10 — What may an accountant correct, and what is the status of a corrected
value?**

`src/TaxAI.Portal/CLAUDE.md` gates "editing or approving taxpayer values" behind
a further approved `tax-domain` specification. Please decide:

1. Which are correctable: a box **value**; a box **code**; adding a box
   extraction missed; deleting a box extraction invented; an identity text field;
   the classification (see Q-A13); the tax year (I assume never — confirm)?
2. Is a corrected value the accountant's asserted fact? Does recording it require
   an explicit attestation, and must the correction record carry actor, instant,
   and the prior extracted value?
3. May a correction be applied to Box 12 (SIN), which cannot be displayed (see
   Q-A3)?
4. After a correction, is the document re-parsed and re-mapped through the engine
   (I assume yes — a corrected value must not bypass validation) and does that
   produce a new review-package version?
5. May a correction ever bypass a parser rejection — i.e. force acceptance of a
   value the engine refuses? I assume never; confirm.
6. Is the correction history immutable and firm-scoped, and is a prior version
   ever rewritten? I assume append-only; confirm.

*Why blocked:* human-asserted tax facts entering a filing input.

---

**Q-A11 — Does this feature create `PersistedReviewRecord`s, and on whose act?**

Spec 014 explicitly put "review-package construction from uploads, and persisted
review-record creation from intake readiness" out of scope, and
`AssociateReviewAsync` may associate only an independently existing record. The
human's instruction ("output must be structured data ready for the existing
review-package flow") implies this spec authorizes creation. Please confirm and
scope:

1. Is `ReviewPackageProjection.Project` → `IReviewRecordRepository.SaveAsync`
   authorized to run from this feature?
2. Is a record created **automatically** on successful extraction, or **only on
   an explicit accountant acceptance action**? (Automatic creation means a
   machine-produced tax record exists with no human in the loop.)
3. `ReviewPackageAssembler.Assemble` requires one shared firm, case, participant,
   and tax year across all extractions and unique document identifiers. Does a
   record cover **one document** or **all accepted documents in a case**?
4. Does a correction create a **new version** (`SaveAsync` already versions), and
   does the existing read-only review route continue to show only the record
   selected by `recordId` with no "latest" selection (spec 012 forbids "latest"
   selection)?
5. Does the existing seven-year exclusive `RetentionUntilUtc` apply unchanged to
   records created this way? (It is an engineering default, never a CRA
   statutory claim.)

*Why blocked:* this decides who may bring a taxpayer tax record into existence.

---

**Q-A12 — What is the retention and purge policy for extraction results,
correction history, and source documents?**

Spec 011's exclusive seven-year boundary and purge port cover
`PersistedReviewRecord` only. Spec 014 explicitly left source-document, intake-
history, and audit retention out of scope. This feature creates a new class of
taxpayer data at rest. Please decide:

1. Do extraction results and correction history inherit the same seven-year
   exclusive boundary, get their own, or is retention deferred again to a later
   spec?
2. Is source-document retention still deferred?
3. If deferred: confirm that shipping new taxpayer data at rest with no purge
   path is acceptable for this milestone.

*Why blocked:* retention of taxpayer PII is a compliance decision.

---

**Q-A13 — May an accountant override a classification?**

If the classifier says "not a T4" (or says medical when the slot said T4), may the
accountant force T4 treatment? Forcing a classification is a human asserting a tax
fact about a document, and it determines whether the engine chain runs at all.

1. Is override permitted?
2. If yes, is it recorded as an immutable human decision with actor and instant,
   and does it still have to pass every engine rule afterwards?
3. If the accountant-declared checklist category and the classifier disagree,
   which wins, and is the disagreement surfaced?

*Why blocked:* a human-asserted tax fact that gates the entire pipeline.

---

### `[ASSUMED]` — Class B decisions for one-pass human override

- `[ASSUMED]` **Spec folder/slug** is
  `specs/015-t4-and-medical-document-extraction/` (014 was the highest existing
  number).
- `[ASSUMED]` **Coordination module home and name**: a new deterministic,
  provider-independent application module `src/ExtractionWorkflow/`, referencing
  inward only `DocumentExtraction`, `CaseProcessing`, `CaseReview`, `TaxCases`,
  `Tenancy`, `ClientIntake`, and `TaxLogicEngine`, and declaring no
  `PackageReference`.
- `[ASSUMED]` **Infrastructure adapter home and name**:
  `src/DocumentIntelligence/`, the only project permitted an OCR/LLM/cloud SDK,
  implementing the ports declared by the coordination module.
- `[ASSUMED]` **Test project homes**: `tests/ExtractionWorkflow.Tests/` and
  `tests/DocumentIntelligence.Tests/`, registered in
  `scripts/test-projects.json` in the same PR that introduces them.
- `[ASSUMED]` **Persistence ownership**: all new tables live in `Persistence` in
  one new additive migration; the coordination module declares its own storage
  record shapes and a store port, exactly as `ClientIntake` does.
- `[ASSUMED]` **Trigger model**: extraction is initiated only by an explicit,
  authenticated, firm-scoped accountant request; no background worker, hosted
  service, queue, or timer ships (FR-13).
- `[ASSUMED]` **Granularity**: one document per ingress call; no batch,
  cross-document reconciliation, or retry orchestration (FR-14).
- `[ASSUMED]` **Provider registration posture**: only a Development/test provider
  adapter is registered, mirroring spec 014's content store, and startup fails
  closed on missing or invalid provider configuration (FR-21).
- `[ASSUMED]` **Audit vocabulary**: whether new audit kinds extend
  `IntakeAuditKind` or form a parallel closed enum is left to `/plan`; either way
  the events carry structural facts only (FR-27).
- `[ASSUMED]` **Naming** of all new types, ports, records, routes, and components
  follows existing conventions and is finalized by `/plan`.
- `[ASSUMED]` **Surface shape**: the accountant extraction/correction experience
  is a case-scoped detail surface, not a cross-firm queue, list, search, or
  paging feature.

---

## CRA sources

**canada.ca was unreachable while this PRD was written.** Every attempt returned
HTTP 403 Forbidden, including:

- `https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/completing-filing-information-returns/t4-information-employers/t4-slip.html`
- `https://www.canada.ca/en/revenue-agency/services/forms-publications/publications/rc4065/medical-expenses.html`
- `https://www.canada.ca/en/revenue-agency/services/forms-publications/forms/t4.html`
- `https://www.canada.ca/en/revenue-agency.html`
- `https://www.canada.ca/en/services/taxes.html`

Attempted 2026-08-02.

Per the blocking-verification rule, **nothing was verified, so nothing was
asserted.** No fallback to memory, to a prior PRD's claim, or to a secondary
source was made.

**This PRD therefore asserts no new CRA box number, line number, filing deadline,
rate, threshold, limit, gross-up factor, credit percentage, or eligibility rule.**

| Claim | Source URL | Tax year | Retrieved |
|---|---|---|---|
| *(none — this PRD asserts no new CRA fact)* | — | — | — |

The tax facts this feature *relies on* already exist in merged, cited specs and
are reused unchanged rather than re-asserted here:

| Reused fact (not re-asserted here) | Verified in | Tax year |
|---|---|---|
| T4 box codes and slip structure | `specs/003-t4-slip-parsing/prd.md` | 2025 |
| T4 box→CRA-line mapping (`Mappings/T4LineMap.2025.json`) | `specs/004-t4-cra-line-mapping/prd.md` | 2025 |
| T4 and medical-expense receipts are genuine source-document categories | `specs/014-client-intake-and-document-upload-flow/prd.md` | 2025 |

**Facts that MUST be verified on canada.ca before this feature can be planned**,
and which are blocked by the outage:

- whether the CRA's 2025 T4 **employee copy** carries Box 54 and Box 45 (Q-A4);
- if medical-expense output is to map to a CRA line, **which 2025 line** (Q-A5);
- any **new** field-level validation rule the human wants (Q-A8).

No source conflict was resolved by my own judgement, because no source was read.

---

## Definition of Done — self-check

**Both classes**

- [x] `specs/015-t4-and-medical-document-extraction/prd.md` exists with the
      required structure, opening with `## Feature classification`.
- [x] Feature class named explicitly; the mixed feature is classified
      `tax-domain`.
- [x] Reuse check performed and documented against `specs/*/prd.md`, every module
      `CLAUDE.md`, and the actual source tree.
- [x] Every stated acceptance criterion (AC-1 … AC-47) is mechanically checkable
      by a single test.
- [x] Zero Class A assumptions made. Every tax-semantic, PII, tenant-isolation,
      and security-boundary decision is either restated from an already-approved
      rule or raised as a blocking question.

**`tax-domain`, additionally**

- [x] Tax year named explicitly in Problem: **2025 only**.
- [x] Every CRA fact has a source — vacuously, because **no new CRA fact is
      asserted**; canada.ca was unreachable and nothing was guessed.
- [x] No source conflict was resolved by my judgement.
- [x] PII acceptance criteria present (AC-27 … AC-32).
- [x] Tax-year isolation acceptance criteria present (AC-7 … AC-10).
- [x] Data-driven mapping criterion present (AC-3).
- [x] Every fail-closed rule **that this PRD states** has its own negative AC
      (AC-8, AC-9, AC-11 … AC-14, AC-18 … AC-21, AC-23, AC-38, AC-39).

**FAILING — this PRD is NOT ready for `/plan`**

- [ ] **Thirteen blocking Class A questions are open (Q-A1 … Q-A13).** Until they
      are answered, FR-B1 … FR-B13 are unspecified and the feature cannot be
      designed.
- [ ] **canada.ca was unreachable**, so three facts that this feature needs
      (Q-A4, Q-A5, Q-A8) could not be verified. Nothing was guessed in their
      place, but they remain unverified.
- [ ] **Acceptance criteria for the blocked requirements do not exist.** Writing
      them now would encode a Class A assumption. Coverage for classification
      semantics, medical output, confidence gating, duplicate identity,
      correction semantics, and review-record creation is therefore incomplete by
      design.

No architecture, task list, code, status marker, branch, commit, or PR was
created by this PRD work.
