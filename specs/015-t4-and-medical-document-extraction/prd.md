# PRD 015 — T4 and Medical Document Extraction

## Feature classification

**Class: `tax-domain`.**

This feature turns an opaque uploaded document into structured data that becomes
a filing input. It changes what the system treats as a Canadian tax fact
(classification, box values, field-level validation, duplicate identity,
accountant-corrected values), it touches the raw T4 Box 12 SIN, it is scoped to
an explicit tax year, and it authorizes three boundary changes that earlier specs
closed deliberately. Even the parts that look structural (a provider adapter, a
content read path, a persistence table) exist only to carry taxpayer-domain data.

Per the classification rule — a mixed feature is `tax-domain`, and when unsure it
is `tax-domain` — every tax-domain protection applies as written: explicit
tax-year scope, canada.ca as the only tax source, fail-closed behaviour with one
negative acceptance criterion per rule, PII/SIN protection, tax-year isolation,
and data-driven mapping with no CRA line number in C#.

---

## Status: ready for `/plan`

The thirteen Class A questions raised by the first draft of this PRD have been
answered by the human as approved decisions, and are now recorded as settled
functional requirements (FR-31 … FR-98) with acceptance criteria (AC-48 …
AC-119). The full record of what was asked and what was decided is preserved in
`## Resolved Class A decisions` so a reviewer can audit each answer against the
requirement it produced.

The three CRA facts the first draft could not verify have been verified directly
on canada.ca on **2026-08-03** and are cited in `## CRA sources`. This PRD now
asserts exactly those verified facts and no others.

No blocked requirement remains. `/plan` may run.

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
  open since spec 010. **This feature answers it: yes, as an authenticated,
  firm-scoped, accountant-initiated in-process ingress and nothing else.**
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
  specification. **This is that specification.**

The gap is therefore not "add OCR." It is: **the system has a verified tax
pipeline with no authorized way to put a real document into it.** This PRD
authorizes that path under explicit, narrow, human-approved terms.

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

Grounded in the actual tree at `D:\Projects\TaxAI`, not memory.

### Reused unchanged — do NOT re-specify, duplicate, or re-implement

**`TaxLogicEngine`** (`src/TaxLogicEngine/`)
- `T4RawInput`, `T4RawBox` — **this is the T4 extraction payload.** Box values
  are `string`; the engine, not extraction, decides whether a value is numeric.
- `T4SlipParser`, `T4LineMapper`, `IT4LineMapProvider`,
  `EmbeddedT4LineMapProvider`, `Mappings/T4LineMap.2025.json` — the sole CRA
  authority for parsing and box→line mapping. **`T4SlipParser` takes exactly one
  approved, CRA-verified change (FR-48 … FR-53); everything else about it is
  unchanged.**
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
`SupportedReviewTaxYears`, `ReviewFields`. **These stay T4-only and are not
extended for medical evidence (FR-54).**

**`Tenancy`** — `TrustedFirmId`, `ReviewPackageProjection.Project`,
`PersistedReviewRecord` and its closed child records, `IReviewRecordRepository`
(`SaveAsync` already supports versioned save), `RetentionPolicy`,
`MembershipVerificationPolicy`, `SessionLifetimePolicy`, `TaxpayerTextGuard`,
`MonetaryValueGuard`, `UtcInstantGuard`, `OpaqueTokenGuard`, `SlipTypeCodes`.
**`SlipTypeCodes` stays exactly `["T4"]`.**

**`TaxCases`** — `TaxCase`, `DocumentCategory` (`T4Slip`,
`MedicalExpenseEvidence`, `Other`), `ReceivedEvidence`, `EvidenceDescriptor`,
`ReadinessCalculator`, `SafeDiagnosticText`, `SupportedTaxYears`.

**`ClientIntake`** — `IIntakeStore` (including `FindAsync(firm, caseId, …)`
returning `StoredIntakeCase` with its `IntakeDocumentRecord` list and opaque
`ContentKey`), `IIntakeContentStore` (**unchanged — no read member is added to
it**), `IIntakeCredentialSource`, `IAccountantIntakeWorkflow`,
`IClientIntakeWorkflow`, the closed intake vocabularies, `IntakeAuditKind`, and
the append-only history model.

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
   `EmployeeAddress` — `ValidateRequiredIdentification`, same file, lines
   121-142. **An extracted T4 missing any of these cannot be parsed at all.**
   FR-48 … FR-53 change exactly two of these rules, on verified CRA grounds.
2. **`src/TaxLogicEngine/CLAUDE.md` records a `[CONFIRM]`-flagged spec 003
   decision:** "Box 54 is required (so the parser presumes the employer/filing-
   side copy, not the employee copy)." Every document a *client* uploads through
   spec 014 is an employee-side copy. **canada.ca confirms the employee copy does
   not carry Box 54** (see `## CRA sources`). That `[CONFIRM]` assumption is
   **superseded** by FR-48.
3. **`AccountantReviewPackage`, `ReviewedSlip`, `PersistedReviewRecord`,
   `PersistedSlip`, and `SlipTypeCodes` are T4-only.** `SlipTypeCodes.All` is
   exactly `["T4"]` and `IsRecognized` refuses anything else. Medical evidence
   therefore gets its **own separate neutral model** (FR-54 … FR-61) rather than
   being forced into these three merged contracts.
4. **`ProcessedT4Extraction` deliberately does not carry the raw `T4RawInput`**,
   precisely so a second unmasked SIN carrier cannot exist. A correction
   therefore re-reads the source and re-extracts (FR-45, FR-84).
5. **`IIntakeContentStore` has exactly two members**, `StageAsync` and
   `DeleteIfExistsAsync`. **It keeps exactly those two members.** The read path
   is a separate narrow port (FR-31).
6. **`ReviewPackageAssembler.Assemble` accepts only
   `IReadOnlyList<ProcessedT4Extraction?>`** and requires one shared firm, case,
   participant, and tax year plus unique document identifiers. FR-86 … FR-91
   respect this exactly.
7. **`tests/TaxLogicEngine.Tests/Services/T4SlipParserTests.cs` directly asserts
   the Box 54 and address requirements** that FR-48 and FR-52 supersede. Those
   specific assertions are the *only* existing tests this feature may change
   (AC-45).

### Genuinely new — introduced by this feature

- An **application-coordination module** implementing
  `IDocumentExtractionIngress` and orchestrating: read source bytes → classify →
  extract → build `ExtractionEnvelope` → `ExtractionEnvelopeValidator.Validate`
  → `T4CaseProcessor.Process` → accountant review of the extraction →
  correction → accountant acceptance → `ReviewPackageAssembler.Assemble` →
  `ReviewPackageProjection.Project` → `IReviewRecordRepository.SaveAsync`.
  `[ASSUMED]` home: `src/ExtractionWorkflow/`.
- A declared **extraction-only content-read port** (FR-31) and a declared
  **document-intelligence port** (classification + field extraction), both
  implemented by infrastructure, never by a domain module.
- A **separate neutral medical-evidence extraction and review model** (FR-54 …
  FR-61), owned by the coordination module, distinct from `CaseReview` and
  `Tenancy`'s T4-only review contracts.
- An **infrastructure adapter project** for the document-intelligence port.
  `[ASSUMED]` home: `src/DocumentIntelligence/`. **It ships deterministic
  Development/test adapters only and introduces no external SDK (FR-37).**
- **Additive persistence** for extraction versions, medical evidence versions,
  classification outcomes and overrides, confidence, per-field validation
  outcomes, duplicate determinations and their resolutions, and the immutable
  accountant-correction history, owned by `Persistence` in a new additive
  migration.
- **Accountant HTTP routes and components** in `TaxAI.Portal` for reviewing an
  extraction, correcting it, resolving a duplicate candidate, overriding a
  classification, and accepting — per document and then per case.
- **One CRA-verified change to `T4SlipParser`'s required-field rules**
  (FR-48 … FR-53).

### Explicitly NOT created

No second envelope type, validator, supported-year list, parser, mapper, mapping
config, rounding helper, review-package assembler, projection, masking helper,
SIN type, tax-case aggregate, readiness calculator, or byte-level
duplicate-fingerprint scheme. Every one of those already exists and is cited
above. No new CRA line mapping of any kind is created.

---

## The three boundary changes this feature is authorized to make

Named here so `/plan` and the reviewer cannot treat any of them as incidental.
Each was a Class A question; each now has an approved answer and a bounded
requirement.

### Change 1 — a narrow, extraction-only read path opens beside the write-only store

Spec 014 made `IIntakeContentStore` write-only on purpose and documented the
reason: unreadable content cannot be rendered, logged, served, or queried.
Extraction requires the bytes.

**Approved:** `IIntakeContentStore` keeps exactly its two existing members. A
**separate narrow extraction-only content-read port** is declared, requiring the
full server-derived firm + case + document + content-key scope, callable only by
the extraction workflow, reachable only from an authenticated live firm-scoped
accountant request. See FR-31 … FR-36.

**The invariant that survives verbatim:** no source byte may reach an HTTP
response, browser, log, metric, trace, diagnostic, SQL column, web-root file,
temporary queryable store, preview, download, print, email, or export surface.

### Change 2 — the ingress opens, and the process boundary stays closed

Spec 010 question F is answered. The ingress is implemented as an
accountant-initiated, firm-scoped, in-process operation.

**Approved:** **taxpayer source bytes do not leave the TaxAI process in this
spec.** No external network provider, cloud OCR provider, LLM provider,
provider-side retention, or provider-side human review ships. Only deterministic
Development/test adapters that run without taxpayer-content network egress are
delivered. A real Canadian Azure document-intelligence provider and everything
around it (Canadian region, encryption in transit, zero retention, secrets,
operations, production authorization) remains blocked until a separate Canadian
Azure infrastructure specification. See FR-37 … FR-40.

### Change 3 — an accountant edits a value that becomes a filing input

`src/TaxAI.Portal/CLAUDE.md` gates "editing or approving taxpayer values" behind
a further approved `tax-domain` specification. This is that specification.

**Approved:** a bounded, append-only correction vocabulary (FR-79 … FR-85) that
can never touch the SIN, never touch server-derived scope or the tax year, never
edit a CRA line directly, and never bypass a parser or validator rejection.
Every correction re-runs the complete existing chain.

---

## Preserved boundaries (non-negotiable)

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
   submissions, extraction versions, medical evidence versions, classification
   overrides, duplicate determinations, correction records, and audit events are
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
10. **Static SSR posture unchanged** — `UseStaticFiles()` middleware, never
    `MapStaticAssets()`; no global render mode; no new interactive component
    beyond an explicitly justified one.

---

## User stories

1. As an **accountant**, I want an uploaded document to be classified as a T4, a
   medical receipt, or neither, so that a photo of a driver's licence in a T4
   slot never silently becomes tax input.
2. As an **accountant**, I want a client-uploaded 2025 employee-copy T4 to be
   turned into the same structured slip the engine already understands, so I
   review one screen of boxes and CRA lines instead of retyping a slip — and I
   want it to work on the copy my client actually has, not only on the
   employer's copy.
3. As an **accountant**, I want to see, per field, whether the value came from
   extraction, at what confidence, and whether the engine accepted it, so I know
   where to look rather than trusting a machine.
4. As an **accountant**, I want to correct a wrong extracted value and have the
   correction recorded immutably with who and when, so the resulting return is
   traceable to a human decision.
5. As an **accountant**, I want the same slip uploaded twice — as two different
   scans, not two identical files — to be surfaced as a probable duplicate for
   me to resolve, so income is neither doubled nor dropped by a machine.
6. As an **accountant**, I want a medical receipt's date, amount, provider, and
   patient captured as neutral evidence, so the receipt is usable later without
   this feature pretending to know whether the expense is claimable.
7. As an **accountant**, I want one explicit case-level acceptance to be what
   creates the persisted review record, so no tax record exists that no human
   put there.
8. As a **compliance reviewer**, I want extraction output, confidence, and
   classification to be treated as untrusted input that must still pass the
   existing fail-closed engine chain, so no machine output ever becomes tax truth
   directly.
9. As a **compliance reviewer**, I want a document whose printed year is not the
   case's 2025 year to be rejected rather than coerced, so a 2024 slip cannot
   enter a 2025 engagement.
10. As a **compliance reviewer**, I want this feature to make no claim about
    medical-expense eligibility, claimants, periods, thresholds, or lines, so no
    unverified tax rule enters the product.
11. As a **security reviewer**, I want the source bytes to remain unreachable from
    every route, log, query, and diagnostic even though extraction now reads them,
    so opening the read path does not open a disclosure path.
12. As a **security reviewer**, I want taxpayer bytes to stay inside the process,
    so no residency or third-party-disclosure question is answered by accident.
13. As a **firm administrator**, I want extraction to be impossible outside the
    firm that owns the case, so a document is never extracted, corrected, or
    reviewed under a foreign tenant.
14. As an **engine maintainer**, I want every tax judgement to remain in
    `TaxLogicEngine` and its JSON config, so no CRA line number, box→line rule,
    or rate is ever written into an extraction or UI module.

---

## Functional requirements

`[reuse]` = reuse of an existing type. `[boundary]` = an invariant restated from
an approved CLAUDE.md or prior spec, not a new decision. `[design]` = a Class B
decision made here. `[approved]` = a Class A decision made by the human and
recorded in `## Resolved Class A decisions`. `[CRA-verified]` = rests on a fact
cited in `## CRA sources`.

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

**FR-5 `[design]` (amended)** No new coordination, infrastructure, or UI project
SHALL contain a **hard-coded tax-authority box table or box-code-keyed tax
decision logic** — no box→line table, no box→meaning table, no per-box
eligibility, validity, plausibility, or required/optional rule, and no `switch`
or lookup that assigns tax significance to a box code. A box code produced by
extraction is an **untrusted value copied from the document**, transported as
data and judged only by the engine: unknown codes flow to `T4OtherInformation`
and surface as `T4MappingResult.UnmappedBoxCodes`, exactly as they do today.
Carrying, storing, displaying, and comparing a provider-produced box code as an
opaque string is permitted, and **tests MAY use synthetic box-code fixtures.**

**FR-6 `[boundary]`** No project introduced by this feature SHALL perform
currency arithmetic or rounding. Extracted amounts remain untrusted `string`
values on `T4RawBox` until the engine parses them. Medical amounts remain
untrusted `string` values as printed (FR-56). `MoneyRounding` is reached only
indirectly, through the parser and mapper.

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

**FR-10 `[boundary]`** Every extraction, correction, duplicate resolution,
classification override, acceptance, and content-read operation SHALL take a
`TrustedFirmId` as an explicit, non-optional scope, be additionally scoped to the
server-stored case and document, and revalidate the active user, active
membership, and live session on every protected request.

**FR-11 `[boundary]`** No new firm-omitting taxpayer-data operation SHALL be
added to any port or store contract. The two existing firm-omitting digest
lookups in `IIntakeStore` remain the only ones.

**FR-12 `[boundary]`** A document, case, extraction version, medical evidence
version, duplicate candidate, or correction that does not exist, belongs to
another firm, or is inconsistent SHALL produce the **same** empty `404`, with
nothing to distinguish the cases and no identifier echoed.

**FR-13 `[design]`** Extraction SHALL be initiated only by an explicit,
authenticated, firm-scoped **accountant** action within a single request. No
background worker, hosted service, queue, timer, client-triggered path, or
anonymous path may initiate extraction. `[ASSUMED]`.

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
described as accuracy, correctness, certainty, a guarantee, or a substitute for
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

**FR-19 `[boundary/design]` (amended)** The document-intelligence provider
adapter SHALL live in a **separate infrastructure project**, `[ASSUMED]` home
`src/DocumentIntelligence/`. That project exists to hold the **dependency
boundary**, not to hold an SDK: it is the only project that would ever be
permitted an OCR/LLM/document-intelligence/cloud SDK reference, **and in this
spec it declares none** (FR-37). No project reference to it is added to any
deterministic tax module.

**FR-20 `[boundary]`** Dependencies point inward only. The coordination module
declares the classification/extraction port and the extraction-only content-read
port; the infrastructure adapters implement them. `TaxLogicEngine`, `TaxCases`,
`DocumentExtraction`, `CaseProcessing`, `CaseReview`, `Tenancy`, and
`ClientIntake` SHALL gain no reference to any provider SDK, ASP.NET Core, EF
Core, SQL, filesystem, or network API. `Persistence` remains the only EF Core
project. `TaxAI.Portal` remains the only executable project, the only ASP.NET
Core project besides its test project, and the single composition root.

**FR-21 `[approved]` (amended)** No production document-intelligence provider
SHALL ship in this spec. Only deterministic Development/test adapters are
registered, mirroring spec 014's content store, and startup fails closed on
missing or invalid provider configuration and in any non-Development
environment. This preserves the existing deployment refusal rather than
partially lifting it.

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

**FR-24 `[boundary]`** The source-byte read introduced by Change 1 SHALL be
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

**FR-26 `[design]`** Extraction versions, medical evidence versions,
classification outcomes and overrides, per-field validation outcomes, duplicate
determinations and resolutions, and accountant corrections SHALL be
**append-only**. A re-extraction or a correction creates a new immutable version;
nothing is updated or deleted in place. Current state is a projection.

**FR-27 `[design]`** Every extraction attempt, classification outcome,
classification override, correction, duplicate determination, duplicate
resolution, document acceptance, case-level acceptance, and review-record
creation SHALL append one immutable audit event carrying only structural facts —
kind, actor kind, opaque actor reference, sequence, and instant. An audit event
SHALL carry no SIN, no amount, no date, no box code, no CRA line, no provider or
patient name, no file name, no content key, no fingerprint, no prompt, no
provider payload, and no free text. Whether this extends the existing
`IntakeAuditKind` vocabulary or declares a parallel one is `[ASSUMED]` Class B
for `/plan`.

### Preserved behaviour

**FR-28 `[boundary]`** This feature SHALL NOT change the spec 012 review route,
its authorization, its browser allowlist, its safe `404`, or its headers; the
spec 014 intake routes, upload transport, rate policies, cookie scope, or
components; `/health`; the existing EF review, identity, or intake tables; the
initial or `AddClientIntake` migrations; or any existing public contract of
`TaxLogicEngine`, `TaxCases`, `DocumentExtraction`, `CaseProcessing`,
`CaseReview`, `Tenancy`, or `ClientIntake` **except the single approved
`T4SlipParser` behaviour change in FR-48 … FR-53**, which changes no public type,
member, or signature. Any new schema SHALL arrive in a new additive migration.

**FR-29 `[boundary]`** Static assets SHALL continue to be served by
`UseStaticFiles()` middleware, never `MapStaticAssets()`, and no global render
mode, additional interactive component beyond an explicitly justified one,
WebAssembly project, npm tree, CDN, or third-party asset SHALL be introduced.

**FR-30 `[boundary]`** No repository-level test SHALL assert a total or closed
project set for `TaxAI.sln`; each new project is asserted exactly once at its
approved path. Any new test project SHALL be registered in
`scripts/test-projects.json` in the same PR that introduces it.

---

### Source-byte read — the extraction-only content-read port

*Approved decision Q-A1.*

**FR-31 `[approved]`** `IIntakeContentStore` SHALL keep exactly its two existing
members, `StageAsync` and `DeleteIfExistsAsync`. **No read member is added to
it.** The read path SHALL be a **separate narrow port declared for extraction
only**, implemented by the same infrastructure adapter that implements the write
side.

**FR-32 `[approved]`** The read operation SHALL require, as explicit
non-optional arguments, all four of: the `TrustedFirmId`, the case identifier,
the document identifier, and the opaque content key. **A content key by itself
SHALL NOT authorize a read**, and no overload, optional argument, or default may
omit any of the four.

**FR-33 `[approved]`** Before returning any byte, the implementation SHALL verify
that the firm, case, document, and content key all belong to the **same
server-stored firm and case**. Any mismatch SHALL fail closed with the shared
empty `404` behaviour and read nothing.

**FR-34 `[approved]`** The port SHALL be resolvable and callable **only by the
extraction workflow**. It SHALL NOT be reachable from client intake, any
anonymous route, any background worker, queue, timer, or hosted service, and
SHALL NOT be registered in a way that lets any other consumer resolve it.

**FR-35 `[approved]`** A read SHALL be initiated only within an **authenticated,
live, firm-scoped accountant request** whose session and membership have already
been revalidated (FR-10, FR-13).

**FR-36 `[approved]` `[boundary]`** The following invariant SHALL survive
verbatim and is restated here as the acceptance condition for the whole change:
**no source byte may reach an HTTP response, browser, log, metric, trace,
diagnostic, SQL column, web-root file, temporary queryable store, preview,
download, print, email, or export surface.**

### External provider — none ships

*Approved decision Q-A2.*

**FR-37 `[approved]`** Spec 015 SHALL NOT authorize taxpayer source bytes to
leave the TaxAI process. **No external network provider, cloud OCR provider, LLM
provider, provider-side retention, or provider-side human review ships in this
specification.** The document-intelligence adapters delivered by this spec SHALL
be deterministic Development/test adapters that run **without taxpayer-content
network egress**.

**FR-38 `[approved]`** A real Canadian Azure document-intelligence provider,
Canadian-region residency requirements, encryption-in-transit requirements,
zero-retention configuration, provider secrets, provider operations, and
production authorization SHALL remain **blocked until a separate Canadian Azure
infrastructure specification**.

**FR-39 `[approved]`** No prompt, provider request, provider response, or source
document SHALL be logged or persisted as an opaque provider payload, in any
store, in any environment.

**FR-40 `[approved]`** No requirement of this PRD SHALL be read as obliging the
implementation to introduce an external production SDK merely to satisfy a
project-layout assumption. The separate adapter project exists as an
**infrastructure boundary**; a boundary with no SDK behind it is the correct
outcome for this spec.

### Box 12 (SIN)

*Approved decision Q-A3.*

**FR-41 `[approved]`** Box 12 MAY be extracted, and its value MAY exist **only in
process memory, for the duration of one synchronous accountant-initiated
operation**, for the sole purpose of letting the existing `T4SlipParser` validate
the T4.

**FR-42 `[approved]` `[boundary]`** The raw SIN SHALL NEVER be persisted,
serialized, logged, audited, rendered, masked for display, hashed, tokenized,
included in duplicate identity, included in a correction record, or included in
any provider response stored by TaxAI. The prohibition on SIN in every structured
surface (FR-22) is unchanged and unqualified.

**FR-43 `[approved]`** Box 12 SHALL NOT be correctable through the UI. No
route, form field, component, or command SHALL accept a SIN value from a browser
in any form, write-only or otherwise.

**FR-44 `[approved]`** A missing, unreadable, malformed, or engine-rejected SIN
SHALL prevent accountant acceptance of that document. The remedy offered is to
re-extract from the stored source, or to obtain and upload a clearer document.
No alternative path may produce an accepted T4 without a parser-accepted Box 12.

**FR-45 `[approved]`** Because the raw payload is never persisted, a correction of
another field MAY require re-reading the stored source and re-extracting, so that
the complete in-memory payload — including Box 12 — can pass through the engine
again. Re-extraction on correction is authorized and expected.

**FR-46 `[approved]`** The masked `RecipientIdentifier` produced by the engine
remains the single permitted in-memory representation and SHALL NOT be rendered,
persisted, logged, or projected by this feature.

**FR-47 `[approved]`** No feature of this spec SHALL infer, display, or store a
"SIN present / SIN absent" signal as a distinguishable field. Failure to accept a
document because of Box 12 SHALL be reported through the engine's own existing
`ValidationError` surface only (FR-69), which already never echoes a SIN value.

### The employee-copy T4 — approved parser change

*Approved decision Q-A4. Rests on the two CRA facts cited in `## CRA sources`.*

**FR-48 `[approved]` `[CRA-verified]`** **Box 54 SHALL no longer be required** by
`T4SlipParser` for a client-uploaded employee-copy T4. canada.ca states that the
employer enters the payroll account number in Box 54 on the employer's own copy
and the CRA copy only, and does not enter it on the copies given to employees.
Requiring it would reject every legitimate client-supplied slip.

**FR-49 `[approved]` `[CRA-verified]`** **Box 45 SHALL remain required** for tax
year 2025. canada.ca states that indicating employer-offered dental benefits is
mandatory for calendar year 2023 and after, and that the administrative relief
permitting omission when code 1 applies covers **calendar years 2023 and 2024
only**. It therefore does not apply to 2025.

**FR-50 `[approved]`** **Box 12 SHALL remain required.** The change in FR-48 does
not relax any other required box.

**FR-51 `[approved]`** **Employer and employee display names SHALL remain
required** and non-blank.

**FR-52 `[approved]`** **Employer and employee addresses SHALL become optional**
extraction and review metadata. A missing or unextractable address SHALL NOT
prevent an otherwise valid T4 from reaching review. `T4RawInput.EmployerAddress`
and `T4RawInput.EmployeeAddress` remain declared members; only the required-field
validation changes.

**FR-53 `[approved]`** This is an **approved, recorded exception** to the
otherwise unchanged `T4SlipParser` behaviour. It **supersedes** the `[CONFIRM]`
spec 003 decision recorded in `src/TaxLogicEngine/CLAUDE.md` that "Box 54 is
required (so the parser presumes the employer/filing-side copy, not the employee
copy)." `/plan` SHALL record the supersession in `src/TaxLogicEngine/CLAUDE.md`
in the same PR that changes the parser. No other spec 003 or spec 004 decision is
affected, no public type/member/signature changes, and no box→line mapping
changes. **No value is ever invented to satisfy a removed requirement** — a
missing Box 54 stays missing, and a missing address stays missing.

### Medical evidence — a separate neutral model

*Approved decision Q-A5. Rests on the lines 33099/33199 facts cited in
`## CRA sources`.*

**FR-54 `[approved]`** Medical extraction SHALL NOT be classification-only, and
SHALL NOT be forced into the existing T4-only `AccountantReviewPackage`,
`ReviewedSlip`, `PersistedReviewRecord`, `PersistedSlip`, or `SlipTypeCodes`.
Those five contracts are **unchanged**, and `SlipTypeCodes.All` stays exactly
`["T4"]`. A **separate neutral medical-evidence extraction and review model**
SHALL be introduced, owned by the coordination module and persisted by
`Persistence`.

**FR-55 `[approved]`** The medical evidence model MAY contain **only** these
members and nothing else:
1. service or payment date **as printed**;
2. gross amount **as printed**;
3. currency **when explicitly present**, otherwise an explicit unknown;
4. provider display name;
5. patient display name;
6. receipt or invoice reference **when explicitly present**;
7. extraction provenance and per-field confidence;
8. structural validation outcomes.

**FR-56 `[approved]`** The date and amount SHALL be carried **as printed**, as
untrusted values, with no normalization to a canonical tax representation, no
currency conversion, no arithmetic, and no rounding.

**FR-57 `[approved]` `[boundary]`** The model SHALL NOT extract, transport,
display, or persist a diagnosis, treatment narrative, medication name, detailed
service description, payment-card information, insurance identifier, health-card
number, or any other medical narrative. This is a closed list of permitted
fields, not a minimum.

**FR-58 `[approved]`** The model SHALL NOT assert — in any field, name, comment,
label, or rendered string — that an expense is eligible, deductible, claimable,
reimbursed, unreimbursed, assigned to a claimant, or mapped to a CRA line.

**FR-59 `[approved]` `[CRA-verified]`** Because canada.ca establishes that
line 33099 and line 33199 depend on **who the expense concerns** (self, spouse,
and children under 18 versus other dependants) and on later claim logic — a
12-month period ending in the year, netting of reimbursements, and a lesser-of
3%-of-net-income or fixed-amount threshold — **Spec 015 SHALL perform no CRA-line
mapping, aggregation, eligibility decision, dependant decision, eligible-period
selection, threshold calculation, or tax calculation for medical evidence.**

**FR-60 `[approved]`** Medical output is **structured neutral evidence for a
future medical-expense tax-domain specification**. It SHALL be described that way
in code, contracts, UI text, and documentation, and never as a tax result.

**FR-61 `[approved]`** No new JSON mapping config, no new `SlipTypeCodes` member,
and no new CRA line identifier of any kind SHALL be created for medical evidence.

### Medical information in the browser and in SQL

*Approved decision Q-A6.*

**FR-62 `[approved]`** The approved medical fields (FR-55) MAY be shown **only on
the new accountant-only extraction-review surface**. They SHALL NOT be added to
the spec 012 read-only T4 review route or to its browser allowlist, which remain
byte-identical (FR-28, AC-40).

**FR-63 `[approved]`** The new surface SHALL declare its **own explicit closed
browser allowlist** containing only: the approved medical fields, structural
validation status, provenance, and confidence. Anything not on that list SHALL
NOT reach a browser model or rendered HTML.

**FR-64 `[approved]`** Provider display name and patient display name are
**sensitive display PII**. They MAY appear only on that accountant-only surface,
never on a client-facing surface, never on the spec 012 review route, and never
in a URL, route value, query string, cookie, or header.

**FR-65 `[approved]`** The approved medical fields MAY be stored as **immutable
structured extraction versions in SQL through `Persistence`**. They SHALL NOT
appear in logs, metrics, traces, exception messages, audit free text, URLs, route
values, cookies, headers, or diagnostic output. Audit records remain structural
only (FR-27).

### Confidence

*Approved decision Q-A7. This resolves the first draft's conflict between Q-A7
and AC-22.*

**FR-66 `[approved]`** **No confidence threshold is introduced.** This is a
settled requirement, not an open question. No numeric threshold, per-field or
per-document, exists anywhere in this feature.

**FR-67 `[approved]`** Per-field and document-level confidence MAY be **displayed
to the accountant as untrusted provider metadata and an attention signal only**.
Confidence SHALL NEVER reject an otherwise valid extraction, create a tax fact,
automatically accept a field, automatically create a review record, bypass
accountant review, or be described as accuracy, correctness, certainty, or a
guarantee.

**FR-68 `[approved]`** The existing `[0,1]` structural validation on
`ExtractionProvenance.Confidence` remains unchanged. An out-of-range confidence
value fails closed through the **existing** `ExtractionEnvelopeValidator`, not
through any new rule.

### Field-level validation

*Approved decision Q-A8.*

**FR-69 `[approved]`** For T4 values, "field-level tax validation" means **only
the existing engine validation rules**, surfaced per field, with the single
exception of the approved FR-48 and FR-52 changes to Box 54 and addresses.

**FR-70 `[approved]`** This feature SHALL introduce **no** plausibility limit,
cross-box consistency rule, maximum or minimum value, new province rule, new
employment-code rule, or any other unverified tax rule. None was invented and
none is authorized.

**FR-71 `[approved]`** For medical evidence, validation is **structural rather
than tax-semantic**:
1. malformed field shapes fail closed;
2. no value is ever invented, defaulted, or inferred;
3. an unextractable date or amount is surfaced as **missing** and prevents
   acceptance of that medical extraction version;
4. missing optional provider, patient, currency, or receipt-reference fields are
   surfaced as **missing** and are explicitly **not** tax eligibility decisions.

**FR-72 `[approved]`** No validation rule introduced by this feature SHALL force
acceptance of anything the T4 engine rejected, or convert an engine rejection
into a warning.

### Duplicate detection

*Approved decision Q-A9.*

**FR-73 `[approved]`** Spec 014's existing **byte-identical** duplicate
prevention (the keyed scope-derived fingerprint) is unchanged and continues to
operate exactly as it does today.

**FR-74 `[approved]`** Spec 015 MAY additionally identify **probable semantic
duplicates**, and only within the **exact same server-derived firm and case**. No
cross-firm, cross-case, or cross-participant comparison of any kind is performed,
and no comparison creates a cross-tenant oracle.

**FR-75 `[approved]` `[boundary]`** Duplicate comparison SHALL NEVER use the SIN,
a SIN hash, a SIN token, Box 12, or any SIN-derived value.

**FR-76 `[approved]`** For T4 documents, probable-duplicate comparison MAY use
normalized equality of: tax year; employer display name; employee display name;
and the complete **non-SIN** extracted box-code/value multiset. **Box 54 SHALL
NOT be required for this comparison** (it is absent from employee copies —
FR-48).

**FR-77 `[approved]`** For medical evidence, probable-duplicate comparison MAY
use normalized equality of: date; amount; currency when present; provider display
name; patient display name; and receipt or invoice reference when present.

**FR-78 `[approved]`** A probable duplicate SHALL be **flagged for accountant
review only**. It SHALL NEVER be automatically rejected, discarded, merged,
accepted, or excluded from a case. The determination and the accountant's later
resolution are immutable and firm/case scoped. The store SHALL persist
**relationships between opaque extraction/document identifiers**, never a
taxpayer-data-derived duplicate fingerprint. Two genuinely distinct T4s from the
same employer for the same taxpayer SHALL NOT be treated as duplicates merely
because the employer and taxpayer names match.

### Accountant corrections

*Approved decision Q-A10.*

**FR-79 `[approved]`** On the new extraction-review surface, an accountant MAY
correct exactly: a non-SIN T4 box **value**; a non-SIN T4 box **code**; **add** a
non-SIN box that extraction missed; **remove** a non-SIN box that extraction
invented; the employer or employee **display name**; the employer or employee
**address metadata**; and the approved **medical fields** of FR-55.
Classification is changed only through the separate override operation of FR-95.

**FR-80 `[approved]` `[boundary]`** An accountant SHALL NEVER be able to correct:
Box 12 or any SIN representation (FR-43); the server-stored tax year; the firm,
case, participant, document, or content-key scope; an engine-generated CRA line
directly; or a validation result directly.

**FR-81 `[approved]`** Each correction SHALL create a **new immutable version**
and record exactly: opaque actor reference; actor kind; supplied instant; field
identifier drawn from a **closed vocabulary**; prior value; corrected value; and
preceding version identifier.

**FR-82 `[approved]`** A sensitive value MAY exist in the correction store **only
where it is already approved for structured persistence** (a non-SIN T4 box value
or an approved medical field). **SIN is always excluded**, as both prior and
corrected value, without exception.

**FR-83 `[approved]`** A correction SHALL require an **explicit accountant
action**. **Final acceptance acts as the accountant's attestation** that the
corrected values were reviewed against the source evidence; no separate
per-correction attestation checkbox is required.

**FR-84 `[approved]`** After every correction, the source SHALL be re-read as
necessary (FR-45), the effective payload rebuilt, and the **complete existing
validation, parser, mapping, processing, and review chain SHALL run again**, in
order, unchanged.

**FR-85 `[approved]`** A correction SHALL NEVER bypass a parser or validator
rejection and SHALL NEVER directly edit a CRA line. Correction history is
append-only; no prior version is ever rewritten or deleted.

### Review-record creation

*Approved decision Q-A11.*

**FR-86 `[approved]`** For T4 documents, `ReviewPackageProjection.Project`
followed by `IReviewRecordRepository.SaveAsync` is authorized to run from this
feature **only after an explicit accountant case-level final acceptance action**.

**FR-87 `[approved]` `[boundary]`** A `PersistedReviewRecord` SHALL NOT be
created automatically after OCR, classification, extraction, validation, document
acceptance, or an individual field correction.

**FR-88 `[approved]`** The final T4 review record SHALL contain **all
accountant-accepted T4 documents for the same firm, case, participant, and tax
year** at the time the case-level acceptance occurs. Before final acceptance,
documents may be extracted, corrections may create new extraction versions,
duplicate candidates may be resolved, and documents may remain unaccepted —
none of which creates a record.

**FR-89 `[approved]`** At final acceptance: only the **latest explicitly accepted
version** of each included T4 document is assembled; the existing
`ReviewPackageAssembler` invariants (one shared firm, case, participant, and tax
year; unique document identifiers) remain mandatory and unrelaxed; exactly **one
versioned review record** is saved; and the existing **one-time** case/review
association is created.

**FR-90 `[approved]`** After that association, Spec 015 SHALL provide **no
reopen, replace, "latest", silent reassociation, or post-finalization correction
workflow**. Those require a future specification. The existing review route
continues to read only the exact `recordId` requested and gains **no** latest-
record selection.

**FR-91 `[approved]`** Medical evidence SHALL NOT enter the T4 review record. It
remains accepted neutral evidence in its separate extraction-review model for a
future medical-expense calculation specification.

### Retention

*Approved decision Q-A12.*

**FR-92 `[approved]`** Structured extraction versions, medical evidence versions,
duplicate decisions, classification overrides, correction history, and the T4
persisted review record SHALL inherit the **existing exclusive seven-year
engineering retention boundary** (`RetentionPolicy`).

**FR-93 `[approved]` `[boundary]`** That seven-year default SHALL NOT be
described, in code, comment, contract, UI text, or documentation, as a CRA
statutory retention requirement. It is an engineering product decision.

**FR-94 `[approved]`** **Source-document purge and production storage lifecycle
remain deferred** to the separate Canadian Azure infrastructure/retention
specification. Because production deployment remains refused, **Spec 015 SHALL
NOT authorize production taxpayer-data use** until that future specification
defines and tests source-document lifecycle and purge. Non-Development startup
and production use remain blocked (AC-114).

### Classification override

*Approved decision Q-A13.*

**FR-95 `[approved]`** An authenticated, firm-scoped accountant MAY override an
automated classification, and **only to one of the two supported paths**: T4, or
medical-expense evidence. No other target value is accepted.

**FR-96 `[approved]`** When the accountant-declared checklist category and the
automated classification **disagree**, both SHALL be displayed on the new
extraction-review surface. **Neither silently wins.** Without an override, the
**automated classification** controls whether the corresponding extraction path
runs.

**FR-97 `[approved]`** An override SHALL: be explicit; be immutable; carry an
opaque actor reference and the supplied instant; be firm/case/document scoped;
**retain the original classification** alongside it; and append one structural
audit event.

**FR-98 `[approved]` `[boundary]`** An override SHALL NEVER change the tax year,
never bypass the T4 parser, the envelope validator, or medical structural
validation, and **never convert `DocumentCategory.Other` into accepted tax input
without an explicit supported override**.

---

## Out of scope

- Tax calculation of any kind, T1 assembly or generation, NETFILE/EFILE, CRA
  submission, and any Azure or production provisioning — excluded by the human's
  instruction and unchanged from spec 014.
- **Any CRA eligibility, rate, threshold, limit, deadline, gross-up, or credit
  rule.** The only CRA facts asserted are the four cited in `## CRA sources`.
- Medical-expense eligibility, claimant rules, dependant rules, eligible-period
  rules, reimbursement netting, receipt sufficiency, certification, totals,
  thresholds, credits, CRA-line mapping, and advice (FR-59).
- An external, cloud, or production document-intelligence provider; provider
  residency, encryption-in-transit, zero-retention, secrets, and operations
  posture (FR-37, FR-38).
- T5 extraction and every document category other than T4 and medical-expense
  evidence; `DocumentCategory.Other` remains refused absent an explicit supported
  override (FR-98).
- Raw-document preview, download, inline rendering, print, email, export, or any
  client- or accountant-facing delete path for accepted content.
- SIN correction, SIN display, SIN storage in any form, and any SIN-presence
  signal (FR-42 … FR-47).
- Confidence thresholds and any gating driven by confidence (FR-66).
- Reopening, replacing, or post-finalization correction of an associated review
  record (FR-90).
- Source-document purge and production storage lifecycle (FR-94).
- Handwriting interpretation, translation, image enhancement as a tax
  representation, and any claim that a scan is legible, authentic, or complete.
- Client-facing extraction results. Extraction output, confidence, classification,
  corrections, duplicate candidates, and review packages are accountant-facing
  only; the client intake surface is unchanged.
- Notification or email delivery of any extraction outcome.
- Firm, user, membership, or identity-provider provisioning.
- Multi-year support, cross-year reconciliation, and prior-year comparison.
- Cross-slip aggregation, income totalling, and any arithmetic over multiple
  documents.
- Batch extraction, cross-document reconciliation, retry orchestration, and any
  background or queued initiator (FR-13, FR-14).
- Any accidental change to existing `TaxLogicEngine`, `TaxCases`,
  `DocumentExtraction`, `CaseProcessing`, `CaseReview`, `Tenancy`,
  `ClientIntake`, `Persistence`, or portal behaviour **other than the single
  approved `T4SlipParser` change of FR-48 … FR-53**. The full suite must remain
  green.

---

## Acceptance criteria

Each criterion is one mechanically checkable Given/When/Then test.

### Reuse and single authority

**AC-1 — The declared ingress is implemented, not replaced.** Given the compiled
solution, When types implementing `DocumentExtraction.Ports.IDocumentExtractionIngress`
are enumerated, Then exactly one implementation exists, it lives in the new
coordination module, and no second envelope, validator, provenance, document-kind,
or supported-year type is declared anywhere.

**AC-2 — Medical documents reuse the existing kind and category.** Given the
compiled solution, When `ExtractedDocumentKind` and
`TaxCases.Models.DocumentCategory` are inspected, Then their member sets are
identical to before this feature, and the medical path uses `GenericEvidence` and
`MedicalExpenseEvidence`.

**AC-3 — No new tax authority (amended).** Given the source of every project added
or modified by this feature **other than `TaxLogicEngine`**, When it is scanned,
Then it contains no CRA line-number literal, no box→line table, no box-code-keyed
tax decision logic (no `switch`, dictionary, or lookup assigning tax significance
to a box code), no tax rate, no threshold, no gross-up factor, no credit
percentage, and no read of a `TaxLogicEngine.Mappings` resource. Carrying,
storing, displaying, or comparing a provider-produced box code as an opaque string
is permitted, and synthetic box-code fixtures in test projects are permitted.

**AC-4 — No arithmetic or rounding (amended).** Given the new coordination and UI
projects, When their source is scanned, Then they contain no `Math.Round`, no
`MidpointRounding`, no `double`, no `float`, and no arithmetic operator applied to
a monetary or tax value. The `decimal` token is permitted **only** where the
existing `ExtractionProvenance.Confidence` (`decimal?`) contract is transported or
displayed, and every such occurrence is a confidence value, never a monetary or
tax amount.

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
inventory, When it is enumerated, Then every extraction, correction, duplicate
resolution, classification override, and acceptance route requires an
authenticated firm-scoped accountant, no anonymous client-intake route reaches
extraction, and every mutation is a non-GET protected by antiforgery.

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
classifier does not classify as either supported kind and for which no supported
override exists, When extraction runs, Then no `T4RawInput`, no
`ValidatedExtraction`, no `ProcessedT4Extraction`, no medical evidence version,
and no review record is produced, and the outcome is recorded as an immutable
audit event only.

**AC-22 — Confidence never gates (amended).** Given two otherwise identical
extractions with confidence `0m` and `1m`, When each runs through the complete
feature — envelope validation, processing, document acceptance, and case-level
acceptance — Then both are accepted identically at every stage, confirming FR-66
that **no confidence threshold exists anywhere in this feature.**

**AC-23 — Out-of-range confidence fails closed.** Given provenance confidence
`-0.1m` and separately `1.1m`, When the envelope is validated, Then each fails
closed through the **existing** `ExtractionEnvelopeValidator` with the confidence
field named, and no new-module rule participated.

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
whose T4 Box 12 contains `123456789`, When the extraction, correction,
duplicate-resolution, and acceptance flows are run end to end and every rendered
HTML response, browser model, URL, cookie, header, log entry, metric, trace,
exception message, audit record, and every column of every SQL row written by this
feature is captured, Then no capture contains `123456789`, any separator-variant
of it, any masked or partial rendering of it, any hash or token derived from it, or
any SIN-presence flag.

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
value, file name, content key, fingerprint, provider payload, prompt, provider
name, patient name, date, or amount.

### Immutability and audit

**AC-33 — Append-only history.** Given every new store contract, When its members
are enumerated, Then none updates or deletes an extraction version, medical
evidence version, classification override, duplicate determination, correction, or
audit event in place, and a re-extraction produces a new version alongside the
prior one.

**AC-34 — Audit is structural only.** Given a completed extraction, override,
correction, duplicate resolution, document acceptance, and case-level acceptance,
When every appended audit row is read, Then each carries only kind, actor kind,
opaque actor reference, sequence, and instant, and no SIN, amount, date, box code,
CRA line, provider name, patient name, file name, content key, fingerprint,
prompt, or free text.

### Boundary and dependency direction

**AC-35 — No provider SDK ships (amended).** Given every project file in the
solution, When package references are enumerated, Then **no** project declares an
OCR, LLM, document-intelligence, or cloud SDK package reference, and in particular
the new infrastructure adapter project declares none. No deterministic tax module
references the adapter project.

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

**AC-38 — No production provider ships (amended).** Given a non-Development host
start, When startup runs, Then it refuses to start, no document-intelligence
provider other than the deterministic Development/test adapter is registered in
any environment, and the existing spec 011 deployment refusal is unchanged.

**AC-39 — Startup fails closed on invalid provider configuration (amended).**
Given missing, empty, or invalid document-intelligence or content-read
configuration in separate host starts, When startup runs, Then each refuses to
start with no taxpayer-data surface exposed.

### Preserved behaviour

**AC-40 — The review route is untouched.** Given `GET /reviews/{recordId:guid}`,
When its route, authorization policy, headers, trusted-firm extraction, browser
projection allowlist, collection order, single `FindAsync` call, and empty `404`
are compared before and after this feature, Then all are identical, and in
particular no medical field joined its allowlist.

**AC-41 — Spec 014 surfaces are untouched.** Given the twelve intake routes, the
upload transport, the rate/concurrency policies, the client cookie scope, and the
component inventory, When compared before and after, Then all are identical, and
`/health` remains byte-identical to spec 010.

**AC-42 — Schema change is additive only.** Given the EF model and migration
history, When compared before and after, Then the initial and `AddClientIntake`
migrations are unmodified, no existing review, identity, or intake table is
altered, and any new table arrives only in a new additive migration.

**AC-43 — Existing contracts unchanged except the enumerated approved changes
(amended).** Given the public surfaces of `TaxLogicEngine`, `TaxCases`,
`DocumentExtraction`, `CaseProcessing`, `CaseReview`, `Tenancy`, and
`ClientIntake`, When compared before and after, Then **no public type, member,
enum value, constructor, or signature was added, renamed, removed, or changed**,
and the **only** approved semantic changes are exactly these three, all internal
to `T4SlipParser`'s validation behaviour:
1. Box 54 is no longer a required box (FR-48);
2. `EmployerAddress` is no longer a required non-blank field (FR-52);
3. `EmployeeAddress` is no longer a required non-blank field (FR-52).

Everything else is unchanged, and in particular: Box 12 and Box 45 remain
required; `T4Slip.EmployerAccountNumber` remains a declared member populated when
present; `T4RawInput.EmployerAddress` and `T4RawInput.EmployeeAddress` remain
declared members; `SlipTypeCodes.All` remains exactly `["T4"]`;
`IIntakeContentStore` keeps exactly its two existing members; and the new medical
evidence model, the extraction-only content-read port, and the
document-intelligence port are **new contracts in new projects**, not changes to
these seven. **No other change is authorized by this criterion** — it grants no
general exception.

**AC-44 — Full suite green.** Given `pwsh ./scripts/Invoke-SolutionTests.ps1`,
When it runs after this feature, Then every registered test project runs, nothing
is skipped, no test fails, every project meets its recorded minimum, and each new
test project is registered in `scripts/test-projects.json`.

**AC-45 — No unauthorized tax behaviour changed (amended).** Given the existing
`TaxLogicEngine.Tests`, `TaxCases.Tests`, `DocumentExtraction.Tests`,
`CaseProcessing.Tests`, and `CaseReview.Tests` suites, When they run after this
feature, Then every one passes, and the **only** existing tests modified are those
in `tests/TaxLogicEngine.Tests/Services/T4SlipParserTests.cs` that directly assert
the three superseded rules of AC-43. Every other existing test runs unmodified,
and no test anywhere is removed, skipped, weakened, or rewritten to make a change
pass.

Note for `/plan`: superseding the Box 54 rule deletes one `[InlineData]` case
from the required-box `[Theory]`, which **lowers** `TaxLogicEngine.Tests`'s
executed count by one. `scripts/test-projects.json` records a floor of 261 for
that project and **a floor is never lowered** (FR-30). The same PR SHALL
therefore add the FR-48 … FR-53 engine tests (AC-63 … AC-67) so the project's
net count rises, and SHALL raise the floor to the new count.

**AC-46 — Solution assertions stay open.** Given every repository-level test, When
inspected, Then none asserts a total or closed project set for `TaxAI.sln`; each
asserts only that its own projects are declared exactly once at their approved
paths.

**AC-47 — Static asset middleware unchanged.** Given `Program.cs`, When inspected,
Then `UseStaticFiles()` is still called parameterless and `MapStaticAssets()` is
still absent, and no global render mode, WebAssembly project, npm tree, CDN, or
third-party asset was introduced.

### Source-byte read (FR-31 … FR-36)

**AC-48 — The write-only store is unchanged.** Given `IIntakeContentStore`, When
its members are enumerated after this feature, Then it declares exactly
`StageAsync` and `DeleteIfExistsAsync`, with no read member, no overload, and no
extension method that returns content.

**AC-49 — The read port requires the full four-part scope.** Given the
extraction-only content-read port, When its members are enumerated, Then every
member takes `TrustedFirmId`, case identifier, document identifier, and content
key as explicit non-optional arguments, with no overload, optional parameter, or
default omitting any of the four.

**AC-50 — A content key alone reads nothing (negative).** Given a valid content
key from firm F's case C paired with a different firm, a different case, or a
different document identifier, When a read is attempted for each combination,
Then each fails closed, returns no byte, and produces the same empty `404`
behaviour as a non-existent document.

**AC-51 — Only the extraction workflow can resolve the read port (negative).**
Given the composition root, When the service registrations and consumers of the
read port are enumerated, Then the extraction workflow is its only consumer, and
no client-intake type, anonymous endpoint handler, hosted service, queue consumer,
or timer can resolve or invoke it.

**AC-52 — A read requires a live firm-scoped accountant request (negative).**
Given an anonymous request, a client-cookie request, a revoked accountant session,
and an inactive membership, When each attempts to reach the read path, Then no
byte is read in any of the four cases and each fails before the adapter is
invoked.

**AC-53 — The source-byte invariant holds verbatim.** Given a document whose bytes
contain a distinctive marker, When the full extraction, correction, and acceptance
flows run and every HTTP response body, browser model, log, metric, trace,
diagnostic, SQL column, and web-root file is captured, Then none contains the
marker, and no preview, download, print, email, or export path exists to reach it.

### No external provider (FR-37 … FR-40)

**AC-54 — No taxpayer-content network egress (negative).** Given the full
extraction flow running against the shipped Development/test adapter with all
outbound network access blocked, When extraction, correction, and acceptance run
end to end, Then every operation completes normally and no outbound network call
is attempted.

**AC-55 — No provider payload is stored or logged (negative).** Given a completed
extraction, When every SQL column, log entry, metric, trace, and diagnostic
written by this feature is captured, Then none contains a prompt, a provider
request, a provider response, an opaque provider payload, or any source-document
content.

**AC-56 — The adapter project carries no SDK.** Given the new infrastructure
adapter project file, When its `PackageReference` set is enumerated, Then it
declares no OCR, LLM, document-intelligence, HTTP-client, or cloud SDK package.

### Box 12 / SIN (FR-41 … FR-47)

**AC-57 — Box 12 reaches the parser in memory.** Given a source document whose
Box 12 is legible, When extraction runs, Then `T4SlipParser` receives the value
and produces a `T4Slip` whose `Sin` is the engine's masked `RecipientIdentifier`,
and the operation is a single synchronous accountant-initiated call.

**AC-58 — The raw SIN outlives nothing (negative).** Given a completed extraction
whose Box 12 was `123456789`, When the operation returns and every store written
by this feature, every serialized object, every log, every audit row, and every
in-flight cache is inspected, Then the raw value exists nowhere, in no encoding,
hash, token, or partial form.

**AC-59 — No route accepts a SIN (negative).** Given the portal route and
component inventory, When every accepted request field, form field, route value,
query parameter, and component parameter is enumerated, Then none accepts a SIN,
a Box 12 value, or a write-only SIN field, and the correction field vocabulary
excludes Box 12.

**AC-60 — A rejected SIN blocks acceptance (negative).** Given documents whose
Box 12 is missing, blank, and malformed respectively, When each is extracted and
acceptance is attempted, Then each fails closed through the engine's existing
Box 12 error, no document acceptance succeeds, no review record is created, and
no error message contains any digits from the attempted value.

**AC-61 — Correction re-extracts rather than reusing a stored payload.** Given an
accepted-into-review extraction and a subsequent correction of a non-SIN box,
When the correction is applied, Then the source is re-read, a complete payload
including Box 12 is rebuilt in memory, the full engine chain re-runs, and no
persisted raw payload was read because none exists.

**AC-62 — No SIN-presence signal (negative).** Given every browser model, SQL
column, projection, and audit row produced by this feature, When inspected, Then
none carries a boolean, enum, count, or flag whose meaning is "a SIN was present"
or "a SIN was absent".

### The employee-copy T4 (FR-48 … FR-53)

**AC-63 — A valid employee copy without Box 54 is accepted.** Given a 2025
`T4RawInput` carrying Box 12, Box 45, employer and employee display names, and
income boxes, but **no Box 54**, When `T4SlipParser.Parse` runs, Then it succeeds,
the resulting `T4Slip` is produced, and no Box 54 error is returned.

**AC-64 — No fake Box 54 is required or invented (negative).** Given the same
input, When the extraction workflow builds the payload and when the resulting slip
is inspected, Then no module added, defaulted, or synthesized a Box 54 value, and
`T4Slip.EmployerAccountNumber` is absent rather than populated with a placeholder.

**AC-65 — A 2025 T4 without Box 45 fails through the engine (negative).** Given a
2025 `T4RawInput` carrying Box 12 and identity names but **no Box 45**, When it
flows through the full extraction chain, Then it fails closed with the engine's
own existing Box 45 required error, no `ProcessedT4Extraction` is produced, and
the error came from `T4SlipParser`, not from a new module.

**AC-66 — Missing addresses do not block review and are not invented (negative).**
Given a 2025 `T4RawInput` whose `EmployerAddress` and `EmployeeAddress` are null
and separately blank, When it flows through the full chain, Then parsing succeeds,
the document can reach accountant review, no address error is returned, and no
module supplied a substitute, placeholder, or inferred address value.

**AC-67 — Box 12 and the tax-year rule are unchanged (negative).** Given a 2025
`T4RawInput` missing Box 12, and separately a `T4RawInput` whose `Year` is 2024,
When each is parsed, Then each still fails closed with the engine's existing
error, unchanged in field name, message, and ordering from before this feature.

**AC-68 — The supersession is recorded.** Given `src/TaxLogicEngine/CLAUDE.md`
after this feature, When it is read, Then the spec 003 `[CONFIRM]` Box 54 decision
is recorded as superseded by spec 015 with the canada.ca citation, and no other
spec 003 or spec 004 decision is altered.

### Medical evidence (FR-54 … FR-61)

**AC-69 — The T4-only review contracts are unchanged.** Given
`AccountantReviewPackage`, `ReviewedSlip`, `PersistedReviewRecord`,
`PersistedSlip`, and `SlipTypeCodes`, When their public surfaces are compared
before and after, Then all five are identical and `SlipTypeCodes.All` is exactly
`["T4"]`.

**AC-70 — The medical model is a closed field set.** Given the medical evidence
model, When its members are enumerated, Then they are exactly: date as printed;
gross amount as printed; currency-or-unknown; provider display name; patient
display name; receipt/invoice reference; extraction provenance with per-field
confidence; and structural validation outcomes — and nothing else.

**AC-71 — Prohibited medical content is unrepresentable (negative).** Given the
medical evidence model, its persistence schema, and its browser projection, When
inspected, Then there is no member, column, or field able to carry a diagnosis,
treatment narrative, medication name, detailed service description, payment-card
information, insurance identifier, or health-card number, and a provider payload
containing such content produces no corresponding stored or rendered value.

**AC-72 — No eligibility or claim assertion (negative).** Given the medical model,
its schema, its browser projection, and all rendered strings, When scanned, Then
none contains a member, column, label, or string asserting eligible, deductible,
claimable, reimbursed, unreimbursed, claimant assignment, or a CRA line.

**AC-73 — No medical tax computation exists (negative).** Given every project
touched by this feature, When scanned, Then there is no medical aggregation, no
summation across receipts, no 12-month-period selection, no dependant
determination, no threshold calculation, no percentage-of-income calculation, and
no CRA line assignment for medical evidence.

**AC-74 — Amounts and dates stay as printed.** Given a receipt printed with a
locale-specific date and an amount with a currency symbol, When extraction runs,
Then the stored values are the printed forms, unnormalized and unconverted, and
no arithmetic or rounding was applied.

**AC-75 — No new mapping config or slip type.** Given `TaxLogicEngine/Mappings/`
and `SlipTypeCodes`, When compared before and after, Then no file was added or
changed and no member was added.

### Medical in the browser and SQL (FR-62 … FR-65)

**AC-76 — Medical fields render only on the new surface.** Given the accountant
extraction-review surface, When a medical evidence version is rendered, Then the
approved fields, structural validation status, provenance, and confidence appear,
and this is the only route in the application that renders any of them.

**AC-77 — The spec 012 allowlist is unchanged (negative).** Given the review
route's browser projection, When compared before and after, Then no medical field,
provider name, or patient name was added, and rendering a case containing medical
evidence changes that route's output not at all.

**AC-78 — The new surface has its own closed allowlist (negative).** Given the new
surface's browser model, When its members are enumerated and a fully populated
domain object is projected, Then only allowlisted members appear, and content-key,
fingerprint, persistence identifier, tenant identifier, session identifier, raw
provider payload, and non-display timestamp are all absent.

**AC-79 — Provider and patient names never leave the accountant surface
(negative).** Given a stored medical evidence version whose provider name and
patient name are distinctive markers, When every client-facing response, URL,
route value, query string, cookie, header, log, metric, trace, and exception
message is captured, Then neither marker appears in any of them.

**AC-80 — Medical fields persist as immutable versions.** Given two successive
medical extractions of the same document, When the store is read, Then two
immutable versions exist, the earlier is unmodified, and no row was updated in
place.

### Confidence (FR-66 … FR-68)

**AC-81 — No threshold exists anywhere (negative).** Given every project added or
modified by this feature, When scanned, Then no numeric confidence threshold,
comparison of a confidence value against a constant, or configuration key naming a
confidence threshold exists.

**AC-82 — Confidence does not gate acceptance (negative).** Given two documents
identical except that one carries confidence `0m` on every field, When both are
extracted, reviewed, accepted per document, and accepted at case level, Then both
follow byte-identical paths and both produce a review record.

**AC-83 — Confidence is never described as accuracy (negative).** Given every
identifier, comment, label, and rendered string in the new projects, When scanned,
Then none describes confidence as accuracy, correctness, certainty, a guarantee,
or a substitute for review.

**AC-84 — Confidence never auto-accepts (negative).** Given a document whose every
field carries confidence `1m`, When extraction completes, Then no field, document,
or case is accepted without an explicit accountant action, and no review record
exists.

### Field-level validation (FR-69 … FR-72)

**AC-85 — T4 validation is the engine's, surfaced per field.** Given a T4 payload
with several invalid fields, When extraction runs, Then every reported field-level
outcome maps one-to-one onto an engine `ValidationError`, with no added, dropped,
merged, or re-worded outcome.

**AC-86 — No new T4 rule exists (negative).** Given the new projects, When scanned,
Then they contain no plausibility bound, no maximum or minimum value, no
cross-box consistency check, no province-code list, and no employment-code list.

**AC-87 — A missing medical date or amount blocks acceptance (negative).** Given
medical extractions whose date is unextractable and separately whose amount is
unextractable, When acceptance is attempted for each, Then each is refused, the
field is surfaced as missing, and no value was invented or defaulted.

**AC-88 — A missing optional medical field does not block and is not an
eligibility decision.** Given a medical extraction missing provider name, patient
name, currency, and receipt reference, When it is reviewed, Then each is surfaced
as missing, acceptance is not blocked by their absence alone, and no eligibility,
deductibility, or claim status is asserted anywhere.

**AC-89 — No rule forces acceptance of an engine rejection (negative).** Given a
T4 payload the engine rejects, When every new-module validation path is exercised,
Then none produces an accepted document, downgrades the rejection to a warning, or
allows acceptance to proceed.

### Duplicate detection (FR-73 … FR-78)

**AC-90 — Byte-identical duplicate prevention is unchanged.** Given the spec 014
byte-identical duplicate path, When compared before and after this feature, Then
its behaviour, fingerprint derivation, and responses are identical.

**AC-91 — Semantic duplicates are firm and case scoped (negative).** Given
matching documents in a different firm, a different case, and a different
participant, When duplicate detection runs, Then no candidate is produced for any
of the three, and no cross-scope read occurred.

**AC-92 — Duplicate identity never uses the SIN (negative).** Given two documents
identical in every respect except Box 12, and separately two documents differing
only in Box 12, When duplicate detection runs, Then the outcome is the same for
both pairs, and no comparison input, stored relationship, or diagnostic derives
from Box 12 or any SIN-derived value.

**AC-93 — T4 duplicate comparison ignores Box 54.** Given two scans of the same
employee-copy T4 that carry no Box 54, When duplicate detection runs, Then they
are surfaced as a probable duplicate, and the absence of Box 54 neither prevents
nor is required for the comparison.

**AC-94 — Two distinct same-employer T4s are not duplicates (negative).** Given
two T4s for the same taxpayer and the same employer with different box value
multisets, When duplicate detection runs, Then neither is flagged as a duplicate
of the other.

**AC-95 — Medical duplicate comparison uses only the approved fields.** Given two
receipts identical in date, amount, currency, provider name, patient name, and
receipt reference, When duplicate detection runs, Then a probable duplicate is
surfaced; and given two receipts differing in amount, Then none is.

**AC-96 — A duplicate is never auto-resolved (negative).** Given a flagged
probable duplicate, When no accountant action is taken, Then neither document is
rejected, discarded, merged, accepted, or excluded from the case, and both remain
present and unaccepted.

**AC-97 — Duplicate relationships are opaque and immutable.** Given a persisted
duplicate determination and its later accountant resolution, When the stored rows
are read, Then they reference only opaque extraction/document identifiers, carry
no taxpayer-data-derived fingerprint, and the determination row is unmodified by
the resolution.

### Corrections (FR-79 … FR-85)

**AC-98 — The correction vocabulary is closed.** Given the correction field
vocabulary, When its members are enumerated, Then they are exactly: non-SIN T4 box
value; non-SIN T4 box code; add non-SIN box; remove non-SIN box; employer display
name; employee display name; employer address; employee address; and the approved
medical fields — and nothing else.

**AC-99 — Prohibited corrections are refused (negative).** Given correction
attempts targeting Box 12, the tax year, the firm, the case, the participant, the
document, the content key, a CRA line entry, and a validation result, When each is
submitted through every available channel, Then each is refused, nothing is
mutated, and no version is created.

**AC-100 — Each correction creates a complete immutable version.** Given a
correction, When the stored record is read, Then it carries opaque actor
reference, actor kind, supplied instant, closed-vocabulary field identifier, prior
value, corrected value, and preceding version identifier; the prior version is
byte-unchanged; and no row was updated in place.

**AC-101 — No SIN in a correction record (negative).** Given a document whose
Box 12 is `123456789` and a correction of an unrelated box, When the correction
row and its prior/corrected values are read, Then neither contains the SIN in any
form.

**AC-102 — Correction re-runs the whole chain.** Given a corrected non-SIN box
value, When the correction is applied, Then the envelope validator, parser,
mapper, processor, and review assembly all run again in the existing order, and
the resulting outcome derives only from their output.

**AC-103 — A correction cannot bypass a rejection (negative).** Given a correction
that leaves the payload in a state the engine rejects, When it is applied, Then
acceptance is refused, the engine's own errors are returned, and no partially
accepted state exists.

**AC-104 — Correction requires an explicit accountant action (negative).** Given
an extraction with obviously wrong values and no accountant action, When the case
is inspected, Then no correction record exists and no value changed.

### Review-record creation (FR-86 … FR-91)

**AC-105 — No record before case-level acceptance (negative).** Given a case whose
documents have been extracted, corrected, duplicate-resolved, and individually
accepted, but with no case-level final acceptance, When the review record store is
read, Then it contains no record for that case.

**AC-106 — Case-level acceptance creates exactly one versioned record.** Given a
case with three accepted T4 documents for one participant and tax year, When
case-level final acceptance runs, Then `ReviewPackageAssembler.Assemble`,
`ReviewPackageProjection.Project`, and `IReviewRecordRepository.SaveAsync` each
run once, exactly one versioned record is saved containing all three documents,
and the one-time case/review association is created.

**AC-107 — Only the latest accepted version of each document is assembled.**
Given a document with three extraction versions of which the second was the last
explicitly accepted, When case-level acceptance runs, Then the assembled package
contains that accepted version and neither of the others.

**AC-108 — Assembler invariants are unrelaxed (negative).** Given acceptance
attempts spanning two participants, two tax years, and separately duplicate
document identifiers, When each runs, Then each fails closed through the existing
assembler invariants and no record is saved.

**AC-109 — No post-finalization workflow exists (negative).** Given a finalized,
associated review record, When every route and workflow member is enumerated, Then
none reopens, replaces, reassociates, deletes, or post-corrects it, and a second
association attempt fails closed.

**AC-110 — The review route gains no latest selection (negative).** Given two
record versions for one case, When `GET /reviews/{recordId}` is called for each,
Then each returns exactly the requested record, and no route, query, or workflow
resolves a "latest" record.

**AC-111 — Medical evidence never enters the T4 record (negative).** Given a case
containing accepted T4 documents and accepted medical evidence, When the persisted
review record is read, Then it contains no medical field, provider name, patient
name, or medical document identifier.

### Retention and deployment refusal (FR-92 … FR-94)

**AC-112 — New taxpayer data inherits the seven-year exclusive boundary.** Given a
persisted extraction version, medical evidence version, duplicate decision,
classification override, correction record, and review record, When each is read,
Then each carries the existing exclusive seven-year `RetentionUntilUtc` derived
from the existing `RetentionPolicy`, with no second retention authority declared.

**AC-113 — Retention is never described as a CRA requirement (negative).** Given
every identifier, comment, contract, UI string, and document introduced by this
feature, When scanned, Then none describes the seven-year boundary as a CRA
statutory, legal, or required retention period.

**AC-114 — Production use remains blocked (negative).** Given a host start in
Production and separately in Staging, When startup runs, Then each refuses to
start, no taxpayer-data surface is exposed, and no source-document purge path,
production content provider, or production document-intelligence provider is
registered in any environment.

### Classification override (FR-95 … FR-98)

**AC-115 — Override targets are exactly the two supported paths (negative).**
Given override attempts targeting T4, medical-expense evidence, `Other`, and an
unrecognized value, When each is submitted, Then the first two are accepted and
the last two are refused with nothing mutated.

**AC-116 — Disagreement is surfaced, and neither silently wins.** Given a document
whose checklist category is T4 and whose automated classification is
medical-expense evidence, When the extraction-review surface renders it, Then both
values appear, and absent an override the automated classification is the one that
selects the extraction path.

**AC-117 — An override is immutable, scoped, and retains the original.** Given an
applied override, When the stored record is read, Then it carries an opaque actor
reference, the supplied instant, and the firm/case/document scope; the original
automated classification is still readable; the row is unmodified by later
activity; and exactly one structural audit event was appended.

**AC-118 — An override bypasses no validation (negative).** Given a non-T4
document overridden to T4, When extraction runs, Then it still passes through the
envelope validator and `T4SlipParser` and fails closed on the engine's own errors,
producing no `ProcessedT4Extraction` and no review record.

**AC-119 — `Other` never becomes tax input by default (negative).** Given a
document classified `DocumentCategory.Other` with no override applied, When
extraction is attempted, Then no extraction path runs, no tax data is produced,
and the outcome is an immutable audit event only.

---

## Planning constraint (from the human, recorded for `/plan`)

`/plan` SHALL use a small number of substantial tasks, splitting only where
extraction, validation, persistence, or review UI are genuinely independent. It
SHALL NOT create microtasks for DTOs, markup, individual fields, individual
endpoints, migrations, or documentation, and SHALL NOT create speculative
contract-only scaffolds or empty modules.

The natural seams this PRD exposes, offered as input rather than as a task list:
the coordination contract and the approved parser change; extraction, validation,
and classification; persistence and the additive migration; and the accountant
extraction-review, correction, and acceptance surface.

---

## Resolved Class A decisions

Every question below was raised as blocking in the first draft of this PRD and
answered by the human as an approved decision. Each is recorded here with the
requirements it produced, so a reviewer can audit the answer against the
implementation.

| # | Question | Approved decision | Requirements | Criteria |
|---|---|---|---|---|
| Q-A1 | How may extraction read the encrypted source bytes, and who may do it? | A separate narrow extraction-only content-read port; `IIntakeContentStore` unchanged; four-part server-verified scope; extraction workflow only; live firm-scoped accountant request only; source-byte invariant preserved verbatim. | FR-31 … FR-36 | AC-48 … AC-53 |
| Q-A2 | May taxpayer source bytes leave the TaxAI process, and to whom? | No. No external provider, cloud OCR, LLM, provider-side retention, or human review ships. Deterministic Development/test adapters only, with no taxpayer-content network egress. A real Canadian Azure provider is deferred to a separate infrastructure spec. No prompt or provider payload is logged or persisted. | FR-37 … FR-40, FR-21 | AC-54 … AC-56, AC-35, AC-38 |
| Q-A3 | May the Box 12 SIN be extracted, where may it live, and how is a mis-read SIN corrected? | Yes, in process memory only, for one synchronous accountant-initiated operation. Never persisted, serialized, logged, audited, rendered, masked for display, hashed, tokenized, used in duplicate identity, or placed in a correction record. Not correctable in the UI. A missing or rejected SIN blocks acceptance; the remedy is re-extraction or a clearer document. Correction may require re-reading and re-extracting the source. | FR-41 … FR-47 | AC-57 … AC-62 |
| Q-A4 | Does the employee copy of a 2025 T4 carry Box 54 and Box 45, and if not, what happens? | Verified on canada.ca: Box 54 is not entered on employee copies; Box 45 is required for 2025 because the relief covered 2023 and 2024 only. Approved parser change: Box 54 no longer required; Box 45 and Box 12 still required; display names still required; addresses become optional metadata. Supersedes the spec 003 `[CONFIRM]` assumption. | FR-48 … FR-53 | AC-63 … AC-68, AC-43, AC-45 |
| Q-A5 | What is a medical receipt's structured output, and where does it go? | A separate neutral medical-evidence model, not the T4-only review contracts. Closed field set: printed date, printed gross amount, currency-or-unknown, provider display name, patient display name, receipt reference, provenance with per-field confidence, structural validation. No medical narrative. No eligibility, claimant, reimbursement, or CRA-line assertion. Verified on canada.ca that lines 33099/33199 depend on who the expense concerns and on later claim logic, so no mapping, aggregation, period selection, threshold, or calculation is performed. | FR-54 … FR-61 | AC-69 … AC-75 |
| Q-A6 | May medical/health content reach a browser and SQL? | Only on the new accountant-only extraction-review surface, under its own explicit closed allowlist. Not added to the spec 012 route or its allowlist. Provider and patient names are sensitive display PII confined to that surface. Storable as immutable structured versions in SQL; never in logs, metrics, traces, exceptions, audit free text, URLs, cookies, headers, or diagnostics. | FR-62 … FR-65 | AC-76 … AC-80 |
| Q-A7 | Is there a confidence threshold, and what does it gate? | No threshold is introduced. Confidence is displayable untrusted provider metadata and an attention signal only. It never rejects, creates a tax fact, auto-accepts, auto-creates a record, bypasses review, or is described as accuracy. Existing `[0,1]` structural validation is unchanged. | FR-66 … FR-68 | AC-81 … AC-84, AC-22, AC-23 |
| Q-A8 | Does field-level validation mean anything beyond the engine's existing rules? | For T4, only the existing engine rules surfaced per field, plus the approved Q-A4 changes. No plausibility, cross-box, maximum, province, or employment-code rule is added. For medical evidence, validation is structural: malformed shapes fail closed, nothing is invented, a missing date or amount blocks acceptance of that version, and missing optional fields are surfaced without being eligibility decisions. Nothing may force acceptance of an engine rejection. | FR-69 … FR-72 | AC-85 … AC-89 |
| Q-A9 | What makes two documents the same slip, and what happens then? | Byte-identical prevention unchanged. Probable semantic duplicates within the exact same server-derived firm and case only. Never the SIN or any SIN-derived value. T4: tax year, employer name, employee name, and the complete non-SIN box multiset, with Box 54 not required. Medical: date, amount, currency when present, provider name, patient name, receipt reference when present. Flag only — never auto-reject, discard, merge, accept, or exclude. Opaque identifier relationships, immutable, firm/case scoped. Two distinct same-employer T4s are not duplicates. | FR-73 … FR-78 | AC-90 … AC-97 |
| Q-A10 | What may an accountant correct, and what is the status of a corrected value? | A closed vocabulary: non-SIN box value/code, add or remove a non-SIN box, display names, address metadata, approved medical fields. Never Box 12, the tax year, server-derived scope, a CRA line, or a validation result. Each correction is a new immutable version recording actor, actor kind, instant, closed-vocabulary field, prior and corrected value, and preceding version. SIN always excluded. Explicit action required; final acceptance is the attestation. The full chain re-runs afterwards. No bypass of a rejection; append-only. | FR-79 … FR-85 | AC-98 … AC-104 |
| Q-A11 | Does this feature create `PersistedReviewRecord`s, and on whose act? | Yes, but only on an explicit accountant case-level final acceptance — never automatically. The record covers all accepted T4 documents for the same firm, case, participant, and tax year, using only the latest explicitly accepted version of each. Existing assembler invariants unrelaxed; one versioned record; the existing one-time association. No reopen, replace, latest, reassociation, or post-finalization correction. Medical evidence stays out. | FR-86 … FR-91 | AC-105 … AC-111 |
| Q-A12 | What is the retention and purge policy? | Extraction versions, medical evidence versions, duplicate decisions, classification overrides, correction history, and the review record inherit the existing exclusive seven-year engineering boundary, never described as a CRA statutory claim. Source-document purge and production lifecycle stay deferred to the Canadian Azure infrastructure spec, and production use remains blocked until then. | FR-92 … FR-94 | AC-112 … AC-114 |
| Q-A13 | May an accountant override a classification? | Yes, to one of the two supported paths only. Checklist category and automated classification are both displayed when they disagree; neither silently wins; absent an override the automated classification controls. The override is explicit, immutable, actor- and instant-carrying, firm/case/document scoped, retains the original, appends a structural audit event, never changes the tax year, never bypasses validation, and never converts `Other` into accepted tax input without an explicit supported override. | FR-95 … FR-98 | AC-115 … AC-119 |

---

## `[ASSUMED]` — Class B decisions for one-pass human override

- `[ASSUMED]` **Spec folder/slug** is
  `specs/015-t4-and-medical-document-extraction/`.
- `[ASSUMED]` **Coordination module home and name**: a new deterministic,
  provider-independent application module `src/ExtractionWorkflow/`, referencing
  inward only `DocumentExtraction`, `CaseProcessing`, `CaseReview`, `TaxCases`,
  `Tenancy`, `ClientIntake`, and `TaxLogicEngine`, and declaring no
  `PackageReference`.
- `[ASSUMED]` **Infrastructure adapter home and name**:
  `src/DocumentIntelligence/`, the dependency boundary for document
  intelligence, implementing the ports declared by the coordination module. Per
  FR-37 and FR-40 it ships **no SDK** in this spec.
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
- `[ASSUMED]` **Provider registration posture**: only a deterministic
  Development/test adapter is registered, mirroring spec 014's content store,
  and startup fails closed on missing or invalid provider configuration (FR-21).
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

canada.ca was reachable on **2026-08-03** and each fact below was read directly
from the cited page. Nothing was taken from memory, from a secondary source, or
from a prior PRD lacking a citation. No source conflict arose, so none was
resolved by my own judgement.

| Claim asserted by this PRD | Source page title | URL | Tax year | Retrieved |
|---|---|---|---|---|
| Box 54 (employer's payroll account number) is entered on the employer's own copy and the copy sent to the CRA only, and is **not** entered on the copies given to employees. The page states the account number is entered on the employer copy and the CRA copy only, and instructs employers not to enter it on the copies given to employees. | T4 slip – Information for employers | https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/completing-filing-information-returns/t4-information-employers/t4-slip.html | 2025 | 2026-08-03 |
| Box 45 (employer-offered dental benefits) is mandatory for calendar year 2023 and after. The Health Canada administrative policy that makes Box 45 non-mandatory when — and only when — code 1 applies is stated for **calendar years 2023 and 2024 only**, and therefore does not apply to 2025. | T4 slip – Information for employers | https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/payroll/completing-filing-information-returns/t4-information-employers/t4-slip.html | 2025 | 2026-08-03 |
| Line 33099 is used for eligible medical expenses paid for self, spouse or common-law partner, and children under 18; line 33199 is used for the allowable amount for other dependants. Which line applies therefore depends on **who the expense concerns**. | Lines 33099 and 33199 – Eligible medical expenses you can claim on your tax return | https://www.canada.ca/en/revenue-agency/services/tax/individuals/topics/about-your-tax-return/tax-return/completing-a-tax-return/deductions-credits-expenses/lines-33099-33199-eligible-medical-expenses-you-claim-on-your-tax-return.html | 2025 | 2026-08-03 |
| A medical-expense claim depends on later claim logic beyond any single receipt: a chosen 12-month period ending in the year, exclusion of amounts reimbursed, and a threshold that is the lesser of 3% of net income or a fixed amount. The same page states that a supporting receipt must show the name of the company or individual to whom the expense was paid. | Lines 33099 and 33199 – Eligible medical expenses you can claim on your tax return | https://www.canada.ca/en/revenue-agency/services/tax/individuals/topics/about-your-tax-return/tax-return/completing-a-tax-return/deductions-credits-expenses/lines-33099-33199-eligible-medical-expenses-you-claim-on-your-tax-return.html | 2025 | 2026-08-03 |

The first two facts support FR-48 and FR-49 — the approved `T4SlipParser`
change. The last two support FR-59: because the line depends on whom the expense
concerns and on claim logic that spans multiple receipts and the taxpayer's
income, **a single receipt cannot determine a line, an eligibility, or an
amount**, so this feature determines none of them. The receipt-content fact
supports the provider-display-name member of FR-55.

The tax facts this feature *relies on but does not re-assert* already exist in
merged, cited specs and are reused unchanged:

| Reused fact (not re-asserted here) | Verified in | Tax year |
|---|---|---|
| T4 box codes and slip structure | `specs/003-t4-slip-parsing/prd.md` | 2025 |
| T4 box→CRA-line mapping (`Mappings/T4LineMap.2025.json`) | `specs/004-t4-cra-line-mapping/prd.md` | 2025 |
| T4 and medical-expense receipts are genuine source-document categories | `specs/014-client-intake-and-document-upload-flow/prd.md` | 2025 |

**No other CRA box number, line number, filing deadline, rate, threshold, limit,
gross-up factor, credit percentage, or eligibility rule is asserted anywhere in
this PRD.**

---

## Definition of Done — self-check

**Both classes**

- [x] `specs/015-t4-and-medical-document-extraction/prd.md` exists with the
      required structure, opening with `## Feature classification`.
- [x] Feature class named explicitly; the mixed feature is classified
      `tax-domain`.
- [x] Reuse check performed and documented against `specs/*/prd.md`, every module
      `CLAUDE.md`, and the actual source tree.
- [x] Every stated acceptance criterion (AC-1 … AC-119) is mechanically checkable
      by a single test.
- [x] Zero Class A assumptions made. Every tax-semantic, PII, tenant-isolation,
      and security-boundary decision is either restated from an already-approved
      rule or recorded in `## Resolved Class A decisions` as a human decision.
- [x] No blocked requirement remains. FR-B1 … FR-B13 have been replaced by the
      settled requirements FR-31 … FR-98.

**`tax-domain`, additionally**

- [x] Tax year named explicitly in Problem: **2025 only**.
- [x] Every CRA fact has a source, page title, URL, tax year, and retrieval date
      (`## CRA sources`, retrieved 2026-08-03). Exactly four facts are asserted
      and no other CRA claim appears.
- [x] No source conflict was resolved by my judgement.
- [x] PII acceptance criteria present (AC-27 … AC-32, AC-53, AC-57 … AC-62,
      AC-71, AC-79, AC-92, AC-101).
- [x] Tax-year isolation acceptance criteria present (AC-7 … AC-10, AC-67,
      AC-99, AC-118).
- [x] Data-driven mapping criterion present (AC-3), with the FR-5/AC-3 box-code
      conflict resolved: hard-coded tax-authority box tables and box-code-keyed
      decision logic are prohibited; carrying an untrusted provider box code and
      using synthetic test fixtures are permitted.
- [x] Every fail-closed rule has its own negative AC: AC-8, AC-9, AC-11 … AC-14,
      AC-18 … AC-21, AC-23, AC-38, AC-39, AC-50 … AC-52, AC-54, AC-55, AC-58 …
      AC-60, AC-62, AC-64 … AC-67, AC-71 … AC-73, AC-77 … AC-79, AC-81 … AC-84,
      AC-86, AC-87, AC-89, AC-91, AC-92, AC-94, AC-96, AC-99, AC-101, AC-103,
      AC-104, AC-105, AC-108 … AC-111, AC-113, AC-114, AC-115, AC-118, AC-119.
- [x] Every former Class A decision has at least one positive and, where
      applicable, one fail-closed negative criterion — see the criteria column of
      `## Resolved Class A decisions`.

**Internal consistency fixes applied**

- [x] FR-5 versus AC-3 — box-code prohibition narrowed to tax-authority tables
      and decision logic; provider-produced codes and synthetic fixtures allowed.
- [x] Q-A7 versus AC-22 — "no confidence threshold" is now settled FR-66, and
      AC-22 tests the whole feature rather than the validator alone.
- [x] AC-4 versus confidence — `decimal` permitted solely for the existing
      `decimal? Confidence` contract; monetary/tax arithmetic, rounding,
      `double`, and `float` still prohibited.
- [x] AC-43 — the approved contract and semantic changes are enumerated exactly
      and the generic loophole is removed; AC-45 names the only existing tests
      that may change.
- [x] FR-19, FR-21, AC-35, AC-38, AC-39 — reconciled with FR-37/FR-40: no
      external production provider or SDK ships, and no criterion requires one.
- [x] Status and Definition of Done — the blocked status is removed and this PRD
      is ready for `/plan`.

**Not done here, by instruction**

No architecture, task list, code, status marker, branch, commit, or PR was
created by this PRD work. `/plan` has not been run.
