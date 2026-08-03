# Architecture: T4 and Medical Document Extraction

**Feature class: `tax-domain`** (from `prd.md` § Feature classification).
**Tax year: 2025 only.** Every requirement reference below is to `prd.md`.

---

## Approach

Spec 015 closes the one gap in an otherwise complete chain. Every tax judgement
already exists and is merged: `T4SlipParser` → `T4LineMapper` →
`T4CaseProcessor` → `ReviewPackageAssembler` → `ReviewPackageProjection` →
`IReviewRecordRepository.SaveAsync` → `GET /reviews/{recordId}`. What has never
existed is a production caller that produces the `ValidatedExtraction` starting
that chain. This feature is that caller, and it invents no tax truth of its own.

The design adds **one deterministic coordination module** and **one
infrastructure adapter**, and extends `Persistence` and `TaxAI.Portal`
additively:

```
                        ┌─────────────────────────────────────────┐
   accountant HTTP ────► │ TaxAI.Portal (only host, only ASP.NET)  │
                        │  ExtractionEndpoints, Razor components   │
                        │  DevelopmentEncryptedIntakeContentStore  │
                        │   ├─ writes (spec 014, unchanged)        │
                        │   └─ reads  (NEW: IExtractionContentReader)
                        └──────────────────┬──────────────────────┘
                                           │
                        ┌──────────────────▼──────────────────────┐
                        │ ExtractionWorkflow  (NEW, deterministic)│
                        │  IExtractionIngestWorkflow              │
                        │  IExtractionReviewWorkflow              │
                        │  DocumentExtractionIngress              │
                        │    implements IDocumentExtractionIngress │
                        │  declares: IExtractionContentReader,     │
                        │   IDocumentIntelligenceProvider,         │
                        │   IExtractionStore                       │
                        └───┬──────────┬──────────┬───────────┬────┘
                            │          │          │           │
        DocumentExtraction ─┘   CaseProcessing   CaseReview   Tenancy
        (envelope + gate)       (T4 chain)       (package)    (projection,
                            │                                  TrustedFirmId)
                    ClientIntake (case + document metadata)
                            │
                     TaxLogicEngine (the only tax authority)

   DocumentIntelligence (NEW infra)  ──implements──► IDocumentIntelligenceProvider
   Persistence          (existing)   ──implements──► IExtractionStore
   TaxAI.Portal         (existing)   ──implements──► IExtractionContentReader
```

The flow for one document, all inside a single authenticated, firm-scoped,
synchronous accountant request:

1. **Authorize and scope.** The portal resolves `TrustedFirmId` from the
   revalidated session and revalidates active user and membership (FR-10). No
   route, query, form, header, or cookie value is ever an authorization
   boundary.
2. **Verify the four-part scope.** The workflow loads the case with
   `IIntakeStore.FindAsync(firm, caseId)` and confirms the document and its
   opaque content key belong to that same stored firm and case (FR-33). A
   mismatch of any kind returns the shared empty `404` (FR-12).
3. **Open the source.** `IExtractionContentReader.OpenAsync` returns a
   disposable lease over a read stream, or `null`. The lease bounds the byte
   lifetime; nothing else in the system can obtain one (FR-31 … FR-36).
4. **Classify, then extract.** `IDocumentIntelligenceProvider` returns untrusted
   provider results. In spec 015 the only registered implementation is a
   deterministic in-process adapter — no network egress, no SDK (FR-37 … FR-40).
5. **Build and gate the envelope.** For a T4 the workflow assembles an
   `ExtractionEnvelope` whose `TaxYear` is the server-stored
   `StoredIntakeCase.TaxYear` and whose `T4RawInput.Year` is what the document
   showed, then calls the **existing** `ExtractionEnvelopeValidator.Validate`
   (FR-7, FR-8). Nothing is coerced.
6. **Run the existing chain.** `T4CaseProcessor.Process` produces
   `ProcessedT4Extraction` or the engine's own errors, verbatim and in order
   (FR-3, AC-6).
7. **Persist an immutable version.** The extraction version, its per-field
   outcomes, its non-SIN box values, its provenance and confidence, and one
   structural audit event are appended (FR-26, FR-27).
8. **Detect probable duplicates** within the same firm and case only, never
   using the SIN, and flag — never auto-resolve (FR-73 … FR-78).
9. **Review, correct, accept.** The accountant reviews on a new accountant-only
   surface, may correct within a closed vocabulary, and each correction re-reads
   the source, rebuilds the payload, and re-runs the whole chain (FR-79 …
   FR-85).
10. **Finalize.** One explicit case-level acceptance assembles all accepted T4
    documents, projects, saves exactly one versioned `PersistedReviewRecord`,
    and creates the existing one-time association (FR-86 … FR-91).

Medical receipts follow steps 1–4 and 7–9 into a **separate neutral evidence
model** and never enter the T4 review record (FR-54 … FR-61, FR-91).

### Why the raw payload is never stored

`ProcessedT4Extraction` deliberately omits `T4RawInput` so no second unmasked SIN
carrier exists. This architecture keeps that property: the effective payload is
**rebuilt on demand** from the freshly re-read source plus the append-only
correction history (`ExtractionPayloadBuilder`), never loaded from a store
(FR-45, AC-61). Box 12 exists only in process memory, only for the duration of
one synchronous operation, and only so the parser can validate the slip (FR-41).

---

## Data model

### New models — `ExtractionWorkflow` (deterministic, no persistence attributes)

All public instance types are **sealed classes, never records**, with
hand-written structural `ToString()`, matching `ClientIntake`, `CaseProcessing`,
and `CaseReview`. Every identifier, taxpayer-text, content-key, and value
property is `[JsonIgnore]`.

| Type | Purpose | Notes |
|---|---|---|
| `DocumentClassification` | enum `T4Slip`, `MedicalExpenseEvidence`, `Unsupported` | The workflow's own vocabulary; maps onto the existing `ExtractedDocumentKind` at the envelope boundary (FR-2) |
| `ClassificationOutcome` | automated value, optional override, effective value, actor, instant | Retains the original (FR-97) |
| `ProviderClassificationResult` / `ProviderT4Result` / `ProviderMedicalResult` | untrusted provider output | Loose and nullable, exactly like `ExtractionEnvelope` |
| `ExtractionVersion` | one immutable extraction version | version, document id, classification, provenance, per-field outcomes, acceptance state, preceding version |
| `ExtractionBoxValue` | one **non-SIN** box code/value pair | Box 12 is rejected at construction, not filtered later |
| `FieldValidationOutcome` | per-field result | field key from a closed vocabulary + outcome kind; carries **no message text** |
| `MedicalEvidenceVersion` | the neutral medical model | the closed FR-55 field set and nothing else |
| `CurrencyDeclaration` | `IsKnown` + optional code | encodes "otherwise unknown" explicitly (FR-55.3) |
| `CorrectionField` | enum, the closed correction vocabulary | exactly the AC-98 members; **no Box 12 member exists** |
| `CorrectionRecord` | one immutable correction | actor reference, actor kind, instant, field, prior value, corrected value, preceding version (FR-81) |
| `DuplicateCandidate` / `DuplicateResolution` | probable-duplicate relationship and its human resolution | references **opaque identifiers only**, never a taxpayer-data fingerprint (FR-78) |
| `ExtractionAuditKind` / `ExtractionActorKind` | structural audit vocabulary | a **parallel** enum, not an extension of `IntakeAuditKind` — see Trade-off 3 |
| `ExtractionWorkflowFields` / `ExtractionWorkflowErrors` | closed field names and fixed, non-interpolated messages | FR-25, AC-32 |

**No new SIN property, SIN type, masking helper, or SIN-presence flag exists
anywhere in this model** (FR-22, FR-42, AC-62). `ExtractionBoxValue` refuses
`T4BoxCodes.SocialInsuranceNumber` at construction, so the "exclude the SIN"
rule is enforced by a type, not by discipline at each call site.

### New relational tables — `Persistence`, one additive migration

Migration `20260803000000_AddDocumentExtraction`. It **adds only** the tables
below and alters no review, identity, or intake table; the initial and
`AddClientIntake` migrations are untouched (FR-28, AC-42).

| Table | Holds | Never holds |
|---|---|---|
| `ExtractionVersions` | firm, case, participant, document, version, classification, acceptance state, provenance, confidence, instants | SIN, source bytes, file name, provider payload |
| `ExtractionBoxValues` | non-SIN box code + printed value per version | Box 12 in any form |
| `ExtractionFieldOutcomes` | field key + outcome kind per version | error message text, taxpayer value |
| `MedicalEvidenceVersions` | the closed FR-55 field set | diagnosis, medication, service description, card/insurance/health numbers |
| `ClassificationOverrides` | automated value, override value, actor, instant | free text |
| `DuplicateCandidates` / `DuplicateResolutions` | opaque identifier pairs + resolution + actor + instant | any taxpayer-data-derived fingerprint |
| `ExtractionCorrections` | field, prior value, corrected value, actor, instant, preceding version | Box 12 as prior or corrected value |
| `ExtractionAcceptances` | per-document and case-level acceptance facts | free text |
| `ExtractionAuditEvents` | kind, actor kind, opaque actor reference, sequence, instant | amount, date, box code, CRA line, provider/patient name, file name, content key, fingerprint, prompt, free text |

Every table carries the exclusive seven-year `RetentionUntilUtc` derived from
the **existing** `RetentionPolicy` — no second retention authority is declared
(FR-92, AC-112). There is no update or delete path on any of them.

### Mapping configuration

**None is added or changed.** `Mappings/T4LineMap.2025.json` is the sole
box→line authority and is untouched. **No CRA line number, box→line table, tax
rate, threshold, or eligibility rule enters any new project** (FR-4, AC-3). No
medical mapping config, `SlipTypeCodes` member, or CRA line identifier is
created (FR-61, AC-75), because canada.ca establishes that lines 33099/33199
depend on whom the expense concerns and on claim logic spanning multiple
receipts and the taxpayer's income — a single receipt cannot determine a line
(FR-59).

### The one approved change to a merged tax contract

`T4SlipParser` — behaviour only, **no public type, member, or signature
changes** (FR-48 … FR-53, AC-43):

| Rule | Before | After | Ground |
|---|---|---|---|
| Box 54 required | yes | **no** | canada.ca: the payroll account number goes on the employer copy and the CRA copy only, and is not entered on the copies given to employees |
| Box 45 required | yes | **yes, unchanged** | canada.ca: mandatory for 2023 and after; the code-1 relief covers 2023 and 2024 only |
| Box 12 required | yes | **yes, unchanged** | — |
| `EmployerName` / `EmployeeName` required | yes | **yes, unchanged** | — |
| `EmployerAddress` / `EmployeeAddress` required | yes | **no** — optional metadata | absent or garbled on real scans; must not block review |

`T4Slip.EmployerAccountNumber`, `T4RawInput.EmployerAddress`, and
`T4RawInput.EmployeeAddress` remain declared and populated when present. **No
value is ever invented to satisfy a removed requirement** (FR-53, AC-64,
AC-66). The spec 003 `[CONFIRM]` decision is recorded as superseded in
`src/TaxLogicEngine/CLAUDE.md` in the same PR (AC-68).

---

## Services & public API

### `ExtractionWorkflow` — declared ports

```csharp
// The extraction-only source read. IIntakeContentStore is NOT touched (FR-31).
public interface IExtractionContentReader
{
    // All four scope arguments are required; no overload or default omits any.
    // Returns null for every mismatch, so the caller cannot distinguish causes.
    Task<ExtractionSourceLease?> OpenAsync(
        TrustedFirmId firm, Guid caseId, Guid documentId, string contentKey,
        CancellationToken cancellationToken);
}

// IAsyncDisposable; exposes Stream Content. The lease bounds byte lifetime so no
// caller can retain a stream past the operation (FR-36).
public sealed class ExtractionSourceLease : IAsyncDisposable { ... }

public interface IDocumentIntelligenceProvider
{
    Task<ProviderClassificationResult?> ClassifyAsync(Stream source, CancellationToken ct);
    Task<ProviderT4Result?>             ExtractT4Async(Stream source, CancellationToken ct);
    Task<ProviderMedicalResult?>        ExtractMedicalAsync(Stream source, CancellationToken ct);
}

// Append-only. No update, no delete, no exposed queryable, no generic save,
// and every taxpayer-data member takes a TrustedFirmId (FR-11, AC-15, AC-33).
public interface IExtractionStore { /* Append*/Find* members, all firm-scoped */ }

public interface IExtractionIngestWorkflow  { /* extract, classify-override, duplicate-resolve */ }
public interface IExtractionReviewWorkflow  { /* correct, accept document, accept case */ }
```

### `ExtractionWorkflow` — services

| Service | Responsibility | Must not |
|---|---|---|
| `DocumentExtractionIngress` | The **single** implementation of spec 007's `IDocumentExtractionIngress` (FR-1, AC-1). Delegates to `ExtractionEnvelopeValidator.Validate`. | Re-implement the gate, declare a second envelope or validator |
| `ExtractionIngestWorkflow` | Scope verification → lease → classify → extract → envelope → validate → `T4CaseProcessor.Process` → append version → duplicate detection → audit | Read a clock, environment, current user, or `HttpContext`; accept a client-supplied firm/case/participant/document scope |
| `ExtractionReviewWorkflow` | Correction, per-document acceptance, case-level finalization, `ReviewPackageAssembler.Assemble` → `ReviewPackageProjection.Project` → `IReviewRecordRepository.SaveAsync` → `AssociateReviewAsync` | Create a record on anything but explicit case-level acceptance (FR-87); relax an assembler invariant; edit a CRA line |
| `ExtractionPayloadBuilder` | Rebuild the effective `T4RawInput` from a freshly re-read source plus the correction history | Persist the payload; carry Box 12 beyond the operation |
| `MedicalEvidenceValidator` | Structural validation only: malformed shapes fail closed; missing date or amount blocks acceptance of that version; missing optional fields are surfaced, not judged | Assert eligibility, deductibility, reimbursement, a claimant, or a CRA line (FR-58, AC-72) |
| `DuplicateCandidateDetector` | Normalized equality within one firm and case, on the FR-76 / FR-77 inputs | Use the SIN or any SIN-derived value; require Box 54; auto-resolve |
| `ExtractionProjection` | Current state from append-only history | Expose a writable status |
| `ExtractionWorkflowOptionsGuard` | Fail-closed configuration validation at startup | Default a missing value |

**Domain rules from CLAUDE.md that bind every one of these:** per-tax-year
isolation with no adjacent-year fallback; all currency math stays in
`TaxLogicEngine` via the parser and mapper; SIN masking remains the engine's
`RecipientIdentifier` and nothing else; OCR/LLM output is untrusted input, never
tax truth; no module reads another module's tables; the UI computes no tax.

### `DocumentIntelligence` — infrastructure adapter

`DeterministicDocumentIntelligenceProvider` implements
`IDocumentIntelligenceProvider` from the source bytes alone, in-process, with
**no network call and no SDK package reference** (FR-37, AC-54, AC-56). The
project exists to hold the dependency boundary; a boundary with nothing behind
it is the correct outcome for this spec (FR-40). Registration is
Development/test only; startup refuses any other environment (FR-21, AC-38).

### `Persistence` — `SqlExtractionStore`

Implements `IExtractionStore` against the existing `TaxAiDbContext`. Appends
only. `Persistence` gains exactly one project reference: `ExtractionWorkflow`.

### `TaxAI.Portal` — routes and components

Six firm-scoped accountant routes, all requiring an authenticated firm-scoped
accountant, every mutation a non-GET with antiforgery (AC-16):

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/intake-cases/{caseId:guid}/extractions` | case-scoped extraction list |
| `GET` | `/intake-cases/{caseId:guid}/extractions/{documentId:guid}` | the extraction-review surface |
| `POST` | `/intake-cases/{caseId:guid}/extractions/{documentId:guid}/extract` | run or re-run extraction |
| `POST` | `/intake-cases/{caseId:guid}/extractions/{documentId:guid}/classification` | classification override |
| `POST` | `/intake-cases/{caseId:guid}/extractions/{documentId:guid}/corrections` | one correction |
| `POST` | `/intake-cases/{caseId:guid}/extractions/{documentId:guid}/accept` | accept one document |
| `POST` | `/intake-cases/{caseId:guid}/extractions/duplicates/{candidateId:guid}` | resolve a duplicate candidate |
| `POST` | `/intake-cases/{caseId:guid}/extractions/finalize` | case-level final acceptance |

The read implementation of `IExtractionContentReader` lives beside the existing
write-side adapter in `DevelopmentEncryptedIntakeContentStore.cs`, deriving its
key from firm + case exactly as `StageAsync` does — so a mismatched scope cannot
decrypt even if the metadata check were bypassed (defence in depth for AC-50).

Components are **static SSR**; the only interactive component in the repository
remains `ClientQuestionnairePanel`. `UseStaticFiles()` stays; `MapStaticAssets()`
stays absent (FR-29, AC-47). The new surface declares its **own** closed browser
allowlist; the spec 012 review route and its allowlist are byte-identical
(FR-62, FR-63, AC-40, AC-77).

---

## Trade-offs

### 1. A separate read port, not a read member on `IIntakeContentStore`

**Rejected: adding `ReadAsync` to `IIntakeContentStore`.** It is the smallest
diff and reuses the adapter directly. Rejected because `ClientIntake`'s
`CLAUDE.md` makes "there is no read member" a stated, load-bearing property, and
`IClientIntakeWorkflow` — reachable from **anonymous** client routes — already
holds that interface. A read member there is one injection away from every
anonymous handler in the system. A separate port keeps exactly one consumer
(AC-51).

**Rejected: extracting from the in-flight upload stream** during the spec 014
upload request, storing only the result. It never stores readable content at
all — genuinely the strongest PII posture. Rejected because it makes extraction
a **client-triggered anonymous** operation, contradicting FR-13 and FR-35, and
because a correction could then never re-extract (FR-45), forcing the raw
payload to be persisted — reintroducing the second unmasked SIN carrier that
`ProcessedT4Extraction` was designed to prevent.

### 2. A separate neutral medical model, not extended review contracts

**Rejected: extending `SlipTypeCodes`, `PersistedReviewRecord`, and
`AccountantReviewPackage` to carry medical content.** It reuses the persisted
review path and gives medical evidence a home the review screen already renders.
Rejected because it changes **three merged public contracts** (AC-43 forbids it),
because `PersistedSlip` is shaped around boxes and CRA lines that medical
evidence does not have, and because anything inside a review package reads as a
filing input — which medical evidence explicitly is not (FR-60). The separate
model costs new tables and a new surface but keeps the T4 review record exactly
what it is today.

### 3. A parallel `ExtractionAuditKind`, not an extension of `IntakeAuditKind`

**Rejected: adding members to `IntakeAuditKind`.** One audit vocabulary and one
audit table would be simpler to query. Rejected because `IntakeAuditKind` is a
merged **public enum of `ClientIntake`**, and AC-43 permits no added enum value
on any of the seven existing modules. This resolves the PRD's open `[ASSUMED]`
on audit vocabulary.

### 4. Rebuild the payload on demand, not persist it encrypted

**Rejected: persisting the raw `T4RawInput` in an encrypted column** so
corrections need not re-read the source. It is faster and makes correction
independent of content availability. Rejected because the payload's Box 12 is a
SIN, and an encrypted column is still a column — FR-42 forbids the SIN in any
persisted form, encrypted included. Re-extraction is the cost of that rule and
the PRD authorizes it explicitly (FR-45).

### 5. One coordination module, not an extension of `ClientIntake`

**Rejected: adding extraction to `ClientIntake`.** The case, document metadata,
and audit history already live there. Rejected because `ClientIntake` is
forbidden from referencing `CaseProcessing`, `CaseReview`, and
`DocumentExtraction` by its own `CLAUDE.md`, and lifting that would give the
module holding the **anonymous client workflow** a direct path into the tax
chain. A separate module keeps the anonymous surface and the tax chain in
different assemblies.

---

## Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | **Box 12 leaks into a stored surface** — the highest-risk defect in the feature. A box loop that writes every extracted box would persist the SIN. | `ExtractionBoxValue` **refuses** `T4BoxCodes.SocialInsuranceNumber` at construction, so the rule is a type invariant, not per-call-site discipline. `CorrectionField` has no Box 12 member, so a SIN correction is unrepresentable. AC-27, AC-58, AC-59, AC-101 plant `123456789` and sweep every HTML response, browser model, URL, cookie, header, log, metric, trace, exception, audit row, and SQL column. |
| R2 | **The parser change alters merged tax semantics.** Getting Box 45 wrong would let a non-compliant 2025 slip through; getting Box 54 wrong rejects every real client slip. | Both facts verified on canada.ca 2026-08-03 and cited in the PRD. AC-63 … AC-67 prove Box 54 optional, Box 45 still required, Box 12 still required, the year rule unchanged, and that **no placeholder value is invented**. The change is confined to two private validation methods. |
| R3 | **A correction becomes a way to bypass the engine.** An accountant asserting a value the parser rejects would put an unvalidated number into a filing input. | Corrections mutate only the rebuilt payload, never the parsed slip or the mapping. Every correction re-runs the full chain (FR-84). AC-103 proves a correction leaving the payload invalid refuses acceptance and leaves no partially accepted state. |
| R4 | **Duplicate detection drops or doubles income.** An auto-reject drops a genuine second slip; a missed duplicate doubles income. | Flag only; never auto-reject, discard, merge, or accept (FR-78). AC-94 proves two distinct same-employer T4s are not duplicates; AC-96 proves an unresolved candidate leaves both documents present and unaccepted. |
| R5 | **A review record is created without a human.** | `ReviewPackageProjection` → `SaveAsync` is reachable from exactly one code path, gated on explicit case-level acceptance (FR-86). AC-105 proves a fully extracted, corrected, per-document-accepted case has **no** record until finalization. |
| R6 | **Medical PHI reaches a log, a URL, or the spec 012 route.** | A closed field set that cannot represent a diagnosis or narrative (AC-71); its own closed browser allowlist (AC-78); AC-77 diffs the spec 012 allowlist; AC-79 plants markers in provider and patient names and sweeps every non-surface channel. |
| R7 | **Source bytes escape the process.** | The lease bounds lifetime; the reader has one consumer; no route returns, streams, or references content. AC-53 plants a byte marker and sweeps every response, log, metric, trace, SQL column, and web-root file. AC-54 runs the whole flow with outbound network blocked. |
| R8 | **The test floor regresses.** Removing the Box 54 `[InlineData]` lowers `TaxLogicEngine.Tests` by one against a floor of 261. | T01 ships the FR-48 … FR-53 tests in the **same** task, so the project's net count rises, and raises the floor itself. Every task owns its own floor: a task that introduces a test project registers it, and a task that adds tests raises that project's minimum, in the same pass. No task depends on a later one to reach its minimum, and no floor is ever lowered. |
| R9 | **A repository test freezes the solution** by asserting a total or closed project set — this exact trap blocked spec 007 W0 and recurred in spec 013. | FR-30 and AC-46: each new project is asserted exactly once at its approved path, never as part of a closed set. |
| R10 | **Deployment refusal weakens** because a provider now exists. | No production provider ships and no SDK is referenced. AC-38 and AC-114 prove Production and Staging starts both refuse and that no purge path or production provider is registered in any environment. |

---

## What does not change

The spec 012 review route, its authorization, allowlist, headers, collection
order, single `FindAsync`, and empty `404`; the twelve spec 014 intake routes,
upload transport, rate and concurrency policies, and cookie scope; `/health`;
the initial and `AddClientIntake` migrations and every existing table; the
static-SSR posture; the deployment refusal. `IIntakeContentStore` keeps exactly
its two members. `SlipTypeCodes.All` stays exactly `["T4"]`. The public surfaces
of all seven existing modules are unchanged — the only approved semantic change
is the three internal `T4SlipParser` validation rules enumerated in AC-43.
