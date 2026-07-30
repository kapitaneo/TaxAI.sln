# PRD 014 — Client Intake and Document Upload Flow

## Feature classification

**Class: `tax-domain`.**

This feature opens a second taxpayer-data surface and accepts source documents
that can contain tax and medical information. It also extends the portal's PII,
tenant-isolation, persistence, and authorization boundaries. The portal gate
therefore requires a `tax-domain` specification even though this feature adds no
CRA mapping, calculation, eligibility rule, or other tax truth. It additionally
establishes the Blazor Web App UI foundation and migrates one existing review
renderer, but that platform work does not relax the tax-domain classification
or any tax-domain protection.

**Tax/CRA semantic impact:** none. T4 and medical-expense documents are opaque
evidence categories in this workflow. The application does not decide whether a
person should have a T4, whether a medical expense is eligible, which person may
claim it, or how any uploaded content affects a return.

No blocking Class A question remains. The request itself fixes the material
Class A boundaries: the active accountant firm is server-derived; a client
receives no firm or tenant identifier; invalid, expired, revoked, replayed,
foreign, and missing resources are indistinguishable; tokens are stored
one-way; SIN is forbidden from every browser and metadata surface named below;
and the only narrowly authorized exception is the opaque source-document byte
stream necessarily supplied by a T4 or medical-document upload. That exception
does not authorize parsing, indexing, rendering, logging, or copying a SIN.

---

## Problem

TaxAI can authenticate an accountant, derive and revalidate one trusted active
firm, persist immutable review-package versions, and render one exact version
on the existing Accountant Review Screen. It cannot yet start an engagement
with a client, issue a safe intake link, ask the client what is available,
accept source documents, or tell the accountant what remains outstanding.

The missing workflow creates four risks:

1. a caller-supplied firm or case identifier could be trusted at an anonymous
   boundary and cross a tenant;
2. a bearer link could be stored or replayed as a reusable credential;
3. uploaded documents and questionnaire changes could be overwritten without a
   durable record of what the client submitted;
4. “ready” could be set independently of the expected-document checklist and
   questionnaire, or be mistaken for a generated review record.

**Tax year in scope: 2025 only.** This is the exact year supported by the
completed `TaxCases`, extraction, processing, and review foundations on which
the requested workflow is built. The year is explicit on the case and every
accepted document association. A missing year or any year other than 2025 fails
closed. There is no “current” or “latest” year, no clock-derived year, no
coercion, and no adjacent-year fallback.

The MVP supports exactly two expected-document categories:

- T4 slip;
- medical-expense document.

The category names do not establish CRA eligibility. An accountant explicitly
configures what this engagement expects; the workflow never derives the
checklist from a tax rule.

---

## Human-authoritative UI and planning decision

The human's 2026-07-29 direction fixes the following requirements. None is an
`[ASSUMED]` Class B choice:

1. `TaxAI.Portal` uses a **Blazor Web App** foundation for every new interactive
   portal screen in this spec.
2. Server-side rendering is the default. **Interactive Server** components may
   be used only for the narrow client interaction that requires them; it is not
   a global render mode and does not move authorization or business state into
   the browser.
3. The client intake, adaptive questionnaire, expected-document checklist,
   upload, and accountant-progress UI use strongly typed Razor components. Their
   successful screen markup is not assembled through manually concatenated HTML
   strings, `StringBuilder`, or an equivalent raw-HTML renderer.
4. Spec 014 migrates the existing Accountant Review Screen from
   `ReviewEndpoints.cs`'s manual `StringBuilder` rendering into a strongly typed
   Blazor component.
5. That migration preserves the exact `/reviews/{recordId:guid}` selection,
   `IReviewRecordRepository.FindAsync` query, closed browser DTO allowlist,
   collection order and amount fidelity, server-derived `FirmId`, R-1
   authorization, indistinguishable safe `404`, session-revocation, non-cache,
   contextual-encoding, immutability, and no-SIN boundaries established by
   specs 011 and 012.
6. Razor components contain presentation and component-local interaction only.
   Authorization, trusted-scope derivation, repository access, validation,
   status/readiness projection, upload decisions, audit decisions, and other
   business logic remain in server-side endpoint/application boundaries outside
   the components.
7. Reusable markup and layout live in components, and reusable styling lives in
   CSS assets rather than inline style strings assembled by endpoint code.
8. The UI migration by itself changes no `Persistence` or `Tenancy` production
   file, immutable review-package contract, EF migration, database model, or
   database schema. Intake's genuinely new persistence needs remain governed by
   the intake requirements; they must not be disguised as review-UI changes.
9. Planning uses the compact substantial-task topology stated in
   [Planning constraint](#planning-constraint). Components, CSS, DTOs,
   individual endpoints, documentation, and mechanical wiring are not separate
   tasks.

---

## Current state and reuse analysis

The reuse check covered every existing `specs/*/prd.md` and the current source
tree. Searches for intake-link, upload, content-fingerprint, questionnaire
submission-history, and document-status implementations found no existing
capability to reuse. The following foundations do exist and remain authoritative.

`TaxAI.Portal` is currently an ASP.NET Core host without a Blazor Web App
component foundation. Its completed review route is correctly authorized and
firm-scoped, but `ReviewEndpoints.cs` projects internal allowlisted DTOs and
manually builds the successful HTML and inline styles with `StringBuilder`.
That query/projection/security path is reused; only the successful presentation
mechanism is migrated as required above.

### Reused, with only the explicitly stated boundary tightening

| Existing capability | Reuse in spec 014 |
|---|---|
| `TaxCases.Models.TaxCase` and `TaxCase.Create` | Create the immutable 2025 tax engagement. The accountant flow supplies server-generated case and participant identifiers, the server-derived firm, a primary-taxpayer display name, and no SIN. No second case aggregate is introduced. |
| `TaxCase.ConfirmChecklist`, `EvidenceRequirement`, `IntakeQuestion`, and `RequirementChecklist` | Represent the accountant-confirmed T4/medical evidence requirements and closed questionnaire. Requirements remain explicit engagement inputs, not CRA-derived rules. |
| `TaxCase.AttachEvidence`, `AnswerQuestion`, and immutable `CaseChangeRecord` results | Apply accepted document receipts and structured answers when replaying the durable intake history. They remain deterministic and side-effect free. |
| `TaxCase.Readiness` / `ReadinessCalculator` | Remain the only domain readiness authority. Spec 014 does not add a writable “ready” flag or a competing readiness calculator. |
| `DocumentCategory.T4Slip` and `DocumentCategory.MedicalExpenseEvidence` | Are the exact two categories exposed by this MVP. The existing `Other` value is not accepted by the new workflow. |
| `ParseResult<T>` and `ValidationError` | Remain the fail-closed domain result shapes. |
| `TrustedFirmId`, durable accountant session, R-1 active-user/active-membership verification, and `PortalAuthorization.FirmScopedPolicy` | Authorize every accountant operation and supply the firm. No form, route, query, header, or client cookie supplies a trusted firm. |
| `TaxpayerTextGuard` and `PersistedSchema.DisplayNameMaxLength` | Remain the approved taxpayer-display-text boundary and maximum. The intake path applies them before case construction/persistence and strengthens rejection to the compact, hyphenated, space-separated, and masked SIN-shaped forms required by this spec; it never masks or echoes the input. |
| `IReviewRecordRepository.FindAsync(TrustedFirmId, recordId, ...)`, the closed internal review DTO allowlist, and `GET /reviews/{recordId:guid}` | Remain the only query, browser projection, and route for one exact immutable review record. The manual `ReviewEndpoints.cs` `StringBuilder` renderer is replaced by a strongly typed Blazor component without changing those boundaries. Handoff never constructs a review package and never selects “latest.” |
| Existing Data Protection, antiforgery, non-cacheable-response, fixed-error, logging, and non-Development startup-refusal foundations | Remain in force and are extended to the new endpoints. This spec does not make the product deployable with real taxpayer data. |

### Relied on but not invoked by this feature

- `IDocumentExtractionIngress`, `ExtractionEnvelopeValidator`,
  `T4CaseProcessor`, `ReviewPackageAssembler`, and
  `ReviewPackageProjection` are not upload handlers. This spec does not
  implement, register, or invoke extraction, processing, assembly, or tax
  mapping.
- `RecipientIdentifier` remains the engine's in-memory SIN representation, but
  the intake workflow never constructs, names, serializes, stores, or renders
  one. The case is created with no SIN.
- The existing persisted review model remains SIN-free and immutable. Uploaded
  source bytes do not enter that SQL read model.

### Genuinely new

The new capability is a durable, firm-and-case-scoped intake workflow:

- a Blazor Web App portal foundation and behavior-preserving migration of the
  existing Accountant Review Screen;
- accountant-driven case creation and explicit expected-document setup;
- a single-use client-link bootstrap and a revocable server-side client intake
  session;
- an adaptive, closed-choice questionnaire with immutable answer and
  submission history;
- secure receipt of opaque T4 and medical-document bytes in a dedicated content
  boundary, with SIN-free relational metadata;
- case-scoped duplicate detection, expected-item status projections, progress,
  append-only audit events, and a conditional handoff to one exact independently
  existing review record.

This is not a second tax engine, document-extraction implementation, or review
model.

---

## Desired outcome

An authenticated accountant can create a 2025 case, explicitly request one or
more T4 and/or medical documents, receive a one-time client URL, and monitor the
case without exposing firm identity to the client. The client can redeem that
URL once, resume through a server-side scoped session, answer only the
applicable questions, upload expected documents, and create immutable
submissions.

Every accepted state transition is associated with the firm and case determined
by trusted server state. The accountant sees a derived current projection and an
append-only history. A ready intake may expose a link to the existing
Accountant Review Screen only when an exact, independently created persisted
review record is already associated with the same firm and case.

All successful interactive portal screens added by this feature render through
strongly typed Blazor components on the shared Blazor Web App foundation.
Read-only review and progress output remains server-rendered; Interactive Server
is limited to any adaptive questionnaire/upload interaction that genuinely
needs it. The review screen's implementation moves from manual HTML-string
assembly to a component without changing what it queries, authorizes, or
discloses.

---

## User stories

1. As an **accountant**, I can create one client tax case under my currently
   active firm and explicitly state how many T4 and medical documents I expect.
2. As an **accountant**, I receive a high-entropy client link that contains no
   firm, tenant, case, participant, taxpayer, or document identifier.
3. As a **client**, I can open the link without an accountant login, answer only
   the questions relevant to the documents requested from me, upload those
   documents, save progress, and submit what I have.
4. As an **accountant**, I can see requested, uploaded, rejected, missing, and
   ready statuses, questionnaire completion, link state, and the latest
   immutable submission number without viewing hidden tenant identifiers.
5. As a **taxpayer**, an invalid, expired, revoked, already-redeemed, foreign, or
   missing selector reveals neither whether a case exists nor which firm owns
   it.
6. As a **compliance reviewer**, I can reconstruct case creation, link issuance
   and revocation, accepted/rejected uploads, answers, submissions, readiness,
   and handoff from immutable history and audit events that contain no SIN or
   taxpayer-controlled text.
7. As an **accountant**, when intake is ready and an exact review record has
   independently been persisted for this firm and case, I can follow the
   existing review route. If no such record exists, I see an “awaiting review
   record” state rather than fabricated review data.
8. As an **operator**, link/session-store or intake-store failure fails closed,
   public endpoints are rate-limited, and the unchanged production-deployment
   refusal prevents real taxpayer uploads to an unprovisioned environment.
9. As a **portal maintainer**, the intake, upload, progress, and existing review
   experiences share a strongly typed Blazor Web App foundation whose
   presentation components cannot become an alternate authorization, business,
   persistence, or tax-logic layer.

---

## Functional requirements

### Case creation and expected-document configuration

**FR-1 — Accountant authorization.** Every accountant create, view, revoke,
reissue, and handoff operation SHALL require the existing firm-scoped policy,
live durable session, active local user, and freshly verified active membership.

**FR-2 — Server-derived ownership.** The accountant workflow SHALL obtain the
trusted firm only from `PortalAuthorization` request state. A firm or tenant
identifier from a route, query, form, body, header, cookie, or identity-provider
claim SHALL never be accepted as authorization input.

**FR-3 — Case creation.** Creating an intake SHALL create one existing
`TaxCase` for tax year 2025 with one primary taxpayer, a server-generated opaque
case identifier, a server-generated opaque participant identifier, the
server-derived firm, and no SIN. The only taxpayer field accepted from the
accountant is the primary taxpayer display name. Before case construction,
persistence, audit, or rendering, that untrusted value SHALL pass the reused
`TaxpayerTextGuard`/approved display-name maximum and SHALL be rejected if it is
blank, over length, or contains a compact, hyphenated, space-separated, or
masked SIN-shaped form. Rejection uses a fixed non-echoing error and introduces
no masking path.

**FR-4 — Explicit checklist.** At creation, the accountant SHALL configure a
positive expected count for T4, a positive expected count for medical documents,
or both. Zero for one category means that category is not requested. Both zero,
negative counts, missing/non-integral counts, or counts above **25 per category**
`[ASSUMED]` SHALL fail closed, as SHALL an undefined category or the existing
`Other` category. No checklist item is inferred from a CRA rule.

**FR-5 — Atomic initialization.** The case, primary participant, confirmed
checklist, active link digest, finite expiry, and creation/link-issued audit
events SHALL be committed atomically. A partial failure SHALL expose neither a
usable link nor an orphan case.

**FR-6 — One tax year.** The case, expected items, questionnaire, answer
versions, document metadata, submissions, audit events, client sessions, and
handoff association SHALL all resolve to the same explicit 2025 case. A missing
or different year fails closed and is never adopted into the case.

### Link issuance, redemption, replay protection, and client session

**FR-7 — Opaque URL.** The server SHALL generate the client credential with a
cryptographically secure random source. The public URL SHALL contain only the
application origin, a fixed route prefix, and that opaque credential. It SHALL
contain no firm, tenant, case, participant, taxpayer, tax-year, document,
membership, user, or session identifier.

**FR-8 — One-way storage and one-time display.** Only a cryptographic one-way
digest of the client credential SHALL be persisted. The raw credential SHALL be
returned to the accountant exactly once at issuance and SHALL not be
redisplayable, recoverable, logged, audited, placed in diagnostics, or written
to metadata. If the response is lost, the remedy is reissue, not recovery.

**FR-9 — Finite policy, no insecure default.** Every link SHALL have an
explicit server-computed expiry from a required positive finite lifetime
policy. No infinite lifetime and no compiled fallback are permitted. Missing or
invalid lifetime configuration SHALL fail startup. This PRD does not invent a
production duration; production configuration remains part of the excluded
deployment work.

**FR-10 — Single active link.** At most one active bootstrap link may exist for
a case. Reissue SHALL atomically revoke every prior active link and every client
session derived from it before issuing the replacement.

**FR-11 — Safe two-step, atomic single-use redemption.** `GET` of a link SHALL
perform a read-only validity check and return either the safe empty `404` or a
fixed, non-cacheable confirmation form containing an antiforgery token. It SHALL
not spend the link or create an intake session, so browser/email prefetch cannot
consume the credential. The form SHALL post to a fixed clean route using a
Data-Protection-protected opaque redemption handle derived from the digest; the
raw credential and firm/case identifiers SHALL not appear in the form action,
hidden fields, HTML, or POST URL/body. The form's `POST` SHALL validate
antiforgery and redeem the protected handle exactly once in one atomic
operation. Successful `POST` marks the bootstrap credential spent and creates
one revocable server-side intake session. A second concurrent or later
redemption `POST` is a replay and SHALL produce the same safe not-found response
as an invalid credential.

**FR-12 — Clean continuation URL.** Successful redemption `POST` SHALL
immediately redirect to a fixed clean intake URL with no credential or
identifier. Client pages SHALL send `Cache-Control: no-store`, a no-referrer
policy, a restrictive content-security policy, and no third-party resource
request.

**FR-13 — Client cookie.** The client browser SHALL carry only a
Data-Protection-protected opaque intake-session key in a `Secure`, `HttpOnly`,
appropriately `SameSite` cookie. The durable server-side session is
authoritative for firm, case, link, creation, expiry, and revocation. The cookie
contains none of those values and no questionnaire or taxpayer content.
The framework antiforgery cookie may coexist; it likewise contains no firm,
case, taxpayer, answer, document, or raw-link value and is not an authorization
boundary.

**FR-14 — Session bounds.** A client session SHALL expire at a finite idle
boundary and at an absolute boundary no later than its source link's expiry.
Activity may extend idle lifetime but never absolute lifetime. Link revocation,
reissue, case cancellation, or session-store failure invalidates the client
session immediately and fails closed.

**FR-15 — Closed bootstrap exemptions.** Firm is unknown only during (a)
atomic link lookup/redemption by digest and (b) intake-session lookup by opaque
session key. These SHALL be the only new firm-omitting persistence operations.
Each returns the stored firm and case scope rather than accepting them from the
caller, carries no taxpayer content, and cannot enumerate links, sessions,
firms, or cases. Every subsequent operation requires both values derived from
that result.

**FR-16 — Revocation.** An authorized accountant SHALL be able to revoke the
active link. Revocation SHALL atomically revoke every derived client session,
append an audit event, and make copied link URLs and cookies unusable. It SHALL
not delete immutable submissions or history.

### Adaptive questionnaire and immutable submissions

**FR-17 — Closed questionnaire.** The MVP questionnaire SHALL contain only
server-owned prompts and closed answer codes. It SHALL accept no free text, SIN,
date of birth, address, email, phone, account number, medical description,
amount, employer identifier, or tax value.

**FR-18 — Adaptation.** The questionnaire SHALL render an availability question
only for each category the accountant requested. An “available now” answer
reveals that category's required upload slots; “will provide later” leaves them
requested; “unavailable” marks them missing in the next submission. An
unrequested category and its upload controls SHALL not be rendered or accepted.
The answers are workflow declarations only and never eligibility decisions.

**FR-19 — Answer validation.** Every answer SHALL identify a server-defined
question belonging to the session-derived case and use one value from that
question's closed option set. Unknown, duplicate-within-one-request,
unrequested, foreign-case, missing, or malformed question/answer values fail
closed.

**FR-20 — Append-only answer versions.** Saving a changed answer SHALL append a
new immutable answer version with firm, case, question, version, safe answer
code, actor class, and server UTC instant. It SHALL not update or delete an
earlier answer. The current questionnaire is a projection of the last accepted
version for each question.

**FR-21 — Immutable submission.** Each successful Submit operation SHALL append
one immutable, monotonically versioned submission snapshot referencing the exact
current answer versions and accepted document records that the server resolves
inside the session-derived firm and case at that instant. The client SHALL not
supply a firm, case, answer-version, document, or storage identifier as
submission authority. A later save, upload, rejection, or submission SHALL not
modify an earlier snapshot.

**FR-22 — Partial submission.** A client MAY submit while an expected item is
unavailable or absent. An explicit `unavailable` answer projects `missing`; a
“will provide later” answer or an unanswered/absent item remains `requested`.
Every such incomplete submission remains NotReady. A null submission, an
internally inconsistent current answer/document projection, or concurrent
creation of the same next version SHALL fail closed with no partial snapshot.

### Secure document upload

**FR-23 — Exact categories.** The upload endpoint SHALL accept a document only
for one server-owned expected item in the session-derived case and only when its
category is T4 or medical expense. A caller-supplied firm, case, participant,
tax year, category override, storage key, status, or filename SHALL be ignored
as authority and rejected if bound.

**FR-24 — Closed format and size policy.** The closed MVP format allowlist is
PDF, JPEG, and PNG `[ASSUMED]`. Acceptance SHALL require a non-empty stream, one
of those normalized media kinds, a matching file signature, and request/file
byte and count limits supplied by required positive finite configuration.
Missing, empty, or invalid limit configuration fails startup. Every other
format, signature/type mismatch, empty stream, malformed multipart body, and
limit violation fails closed.

**FR-25 — Original filename disposal.** The browser-supplied filename is
untrusted transport input. The client upload code SHALL replace it with a fixed
neutral transport name rather than copy it into the multipart request; the
server SHALL nevertheless discard any received filename before persistence and
before any response is created. An original/received filename SHALL never be
logged, audited, diagnosed, rendered, copied into metadata, used in a storage
path, or used as duplicate identity. All content keys and any server-side
filenames are opaque and server-generated.

**FR-26 — Narrow raw-byte PII exception.** An uploaded T4 or medical document
may contain a taxpayer's or another person's SIN. The opaque source bytes are
the sole authorized content exception to the otherwise SIN-free portal and
persistence boundary: they may transit the upload handler and be written only
to the dedicated encrypted content store. The system SHALL not OCR, parse,
extract, index, tokenize, classify, display, log, diagnose, or copy a SIN or any
other document content into SQL or metadata.

**FR-27 — Separate encrypted content boundary.** Raw source bytes SHALL remain
outside the existing SIN-free SQL read model and outside the web root. The
content store SHALL use server-generated opaque keys, encryption at rest, and
authenticated server-only access; it SHALL expose no public object URL. SQL may
store only the firm/case/document association, opaque content key, normalized
media kind, byte count, whole-file duplicate fingerprint, immutable version and
status references, and safe structural instants.

**FR-28 — No raw-document viewing surface.** This MVP SHALL not add a browser
preview, inline rendering, download, email, or export of source bytes. Upload
receipt and progress responses contain structural status only. Authorizing a
raw-document return path is a later PII/security decision.

**FR-29 — Duplicate fingerprint.** For an otherwise valid upload, the server
SHALL compute a cryptographically keyed, scope-derived whole-file fingerprint
while streaming. Firm and case context come only from trusted server state and
are part of the fingerprint derivation, so identical bytes in different cases
or firms yield non-correlatable stored values. Duplicate comparison and the
uniqueness constraint SHALL additionally be scoped to the exact server-derived
firm and case. The key and fingerprint are never exposed or logged. A document
that exists only in another case or firm SHALL neither reject the upload nor
alter the response, so duplicate detection creates no cross-case or
cross-tenant oracle. Missing or invalid fingerprint-key configuration fails
startup.

**FR-30 — Duplicate result.** A byte-identical second upload within the same
firm and case SHALL be rejected with a fixed duplicate result, create no second
document record, and append a safe audit event. Same filename with different
bytes is not a duplicate; different filenames with identical bytes are.

**FR-31 — Concurrent and failure-safe upload.** Concurrent identical uploads
within one case SHALL yield exactly one immutable document record. A failed
validation or failed metadata commit SHALL leave no reachable content object.
A content-store failure SHALL create no accepted document metadata or evidence
record. Cleanup diagnostics contain only structural outcome and safe counts.

**FR-32 — Opaque evidence only.** A successfully stored document SHALL produce
an immutable document record and may be replayed as one existing
`ReceivedEvidence` association for the primary participant and 2025 case. The
workflow SHALL not read the document's amounts, names, dates, boxes, lines, or
SIN and SHALL not invoke extraction or tax processing.

### Expected-document status and readiness

**FR-33 — Status belongs to the expected item.** The closed values
`requested`, `uploaded`, `rejected`, `missing`, and `ready` are a derived
projection for each expected-document item. They are not mutable fields on the
immutable uploaded document record. Uploaded document records and upload-attempt
events remain append-only.

**FR-34 — Status definitions.**

- `requested`: the item is expected and has no current accepted upload or
  submitted absence declaration;
- `uploaded`: a distinct valid upload is stored for the item but is not yet
  referenced by the latest immutable submission;
- `rejected`: the latest attempt for an otherwise unsatisfied item failed the
  upload rules or duplicate rule; no rejected source bytes are persisted;
- `missing`: the latest submission explicitly declares the item unavailable;
- `ready`: the latest submission references a valid immutable document for the
  item.

When facts overlap, the current projection applies the fixed precedence
`ready` → `uploaded` → `rejected` → `missing` → `requested`. This preserves a
submitted ready item while separately reporting later unsubmitted changes and
allows a valid replacement upload to move an earlier missing/rejected item to
uploaded.

For an item that was not ready, a later valid upload creates a new immutable
document version and projects `uploaded`; a later submission may project
`ready`. A new answer/upload after a ready submission does not erase that
submission's ready status; progress instead reports `unsubmitted changes` until
the client submits again. Earlier records and submission statuses remain
reconstructible.

**FR-35 — Category counts.** A category requirement is satisfied only when the
latest submission contains at least the accountant-confirmed number of distinct
valid document records of that exact category. T4 never satisfies medical,
medical never satisfies T4, and unrequested or foreign records never satisfy
either.

**FR-36 — One readiness authority.** “Intake package ready” SHALL mean the
replayed existing `TaxCase.Readiness` is `Ready` after the confirmed checklist,
the exact answer versions, and the exact documents referenced by the latest
immutable submission—and only those versions—have been applied through the
existing TaxCase operations. Working-latest versions that have not been
submitted affect only the structural `unsubmitted changes` indicator. Readiness
SHALL not be stored as a caller-writable Boolean and SHALL not be calculated by
a second independent rule set. Ready means workflow completeness only; it is
not a claim about document safety, authenticity, legibility, tax correctness,
or medical eligibility.

**FR-37 — Unsubmitted changes are not a second readiness.** Readiness SHALL be
derived only from the latest immutable submission replayed through the existing
TaxCase operations. A newer saved answer or upload does not rewrite that
submission and does not independently change TaxCase readiness; it sets a
separate structural `unsubmitted changes` progress indicator. A later
submission replaces the current projection and recalculates readiness through
the same existing authority. Link revocation does not rewrite a past
submission or its historical readiness.

### Accountant-visible progress and safe rendering

**FR-38 — Accountant progress.** An authenticated accountant SHALL be able to
open one opaque case selector under the server-derived firm and see the
taxpayer display name, 2025 tax year, case lifecycle state, derived readiness,
expected T4/medical counts, each expected item's status, questionnaire
completion, immutable submission version, the structural unsubmitted-changes
indicator, link active/expired/revoked/spent state, safe structural instants,
and handoff state.

**FR-39 — Client allowlist.** The client page SHALL render only the TaxAI brand,
2025 tax year, server-owned question prompts and choices, generic T4/medical
category labels and ordinals, expected-item statuses, upload controls, and
fixed success/error text, plus framework antiforgery fields and the
Data-Protection-protected opaque redemption handle on the confirmation page.
It SHALL not render the raw link credential, taxpayer display name, accountant
identity, firm name, or any firm, tenant, case, participant, document, user,
membership, session, content-key, review-record, or audit identifier.

**FR-40 — SIN-free browser and diagnostics.** No SIN in any form—full, masked,
partial, hashed, tokenized, last digits, presence flag, or extracted
representation—SHALL appear in a browser DTO, rendered HTML, page source, URL,
cookie, log, metric, trace, filename, SQL metadata, content metadata, audit
payload, exception, error response, or diagnostic. Request/body logging SHALL
be disabled for intake and upload routes. The raw opaque document bytes remain
only the FR-26 exception and SHALL never be asserted to be SIN-free.
Browser/request DTOs SHALL expose no SIN-named field. A malicious caller can
still place SIN-shaped text in raw HTTP transport or the display-name field;
the application SHALL treat it as untrusted transient input, reject it before
copying it into any case/model/event/response, never echo or log it, and SHALL
not claim the incoming transport was SIN-free.

**FR-41 — Encoding and caching.** Every taxpayer-controlled display name SHALL
be contextually encoded on the accountant page. Every accountant and client
intake response SHALL be non-cacheable. Persistence entities SHALL not be
serialized directly and source-document bytes SHALL not be rendered.

### Handoff to the existing Accountant Review Screen

**FR-42 — Readiness does not manufacture review data.** Reaching intake Ready
SHALL NOT invoke OCR, an LLM, manual transcription, a parser, mapper, processor,
review assembler, projection, calculation, or `SaveAsync`. It SHALL NOT create a
`PersistedReviewRecord`.

**FR-43 — Exact independent record association.** An authenticated accountant
MAY submit one exact opaque `RecordId` to the case-detail handoff operation. The
operation SHALL derive firm and case from trusted server state, load that exact
already-existing `PersistedReviewRecord` through
`IReviewRecordRepository.FindAsync(trustedFirm, recordId, ...)`, verify that its
stored case matches the intake case, and append one immutable association plus
audit event. Missing/blank/malformed, missing, foreign-firm, or wrong-case
records fail closed and create no association. A case with an association
rejects a second association rather than replacing it. There is no review-record
creation, “latest” lookup, guessing, or cross-case fallback.

**FR-44 — Conditional link.** The accountant page SHALL link to the unchanged
`GET /reviews/{recordId:guid}` route only when (a) current intake readiness is
Ready and (b) the exact associated record passes FR-43. If intake is not ready,
no link is shown. If ready but no exact record independently exists, the page
shows a fixed “awaiting review record” state and creates nothing.

**FR-45 — Safe handoff failure.** A missing, foreign-firm, wrong-case, deleted,
or otherwise unavailable associated review record SHALL produce no handoff link
and no existence detail. Opening the existing route retains spec 012's
indistinguishable empty `404`.

### Safe not-found, rate limiting, audit, and failure posture

**FR-46 — Uniform client not-found.** Malformed, unknown, expired, revoked,
spent/replayed, foreign, or missing link/session/item/document resources, and a
link whose case is missing or inconsistent, SHALL produce the same empty `404`
status, body, content type, and cache/security headers after rate-limit
evaluation. The same response writer SHALL handle every such result and SHALL
not add an existence-dependent delay. No path reveals whether a firm, case,
client, item, document, or link exists.

**FR-47 — Uniform accountant not-found.** Under a valid accountant session, a
missing case and a case owned by another firm SHALL produce byte-identical empty
`404` responses. A foreign selector SHALL never cause an unscoped lookup.
Unauthenticated and failed R-1 requests retain the existing fixed authorization
behavior and never reach intake storage.

**FR-48 — Rate limiting.** Link redemption, client-session reads, answer saves,
uploads, and submissions SHALL have required positive finite rate/concurrency
policies. Public redemption is partitioned without logging raw credentials;
session operations use the opaque session fingerprint plus network partition.
Limits are evaluated without disclosing resource validity. An exceeded limit
returns one fixed `429` response and performs no mutation. Missing/invalid limit
configuration fails startup; exact production values belong to deployment
configuration and are not invented here.

**FR-49 — Antiforgery and methods.** Every state-changing accountant or client
operation—including link redemption—SHALL use an explicit non-GET method and
pass antiforgery validation after the applicable authenticated/session or FR-11
confirmation context is established. GET and HEAD never mutate application
state; the link GET only renders the fixed confirmation form. Malformed methods
or antiforgery failure create no case, link redemption, session, answer,
document, submission, status, association, or audit event.

**FR-50 — Append-only audit.** Every accepted case creation, checklist
confirmation, link issue/redemption/revocation/reissue, answer version, upload
acceptance/rejection/duplicate, submission, readiness transition, and exact
review-record association SHALL append an immutable audit event with
server-derived firm and case, event kind, actor class, safe opaque actor
reference where applicable, and UTC instant. Events contain no raw credential,
client-session key, display name, answer prompt, original filename, content,
fingerprint, amount, code, or taxpayer text.

**FR-51 — Rejected operations.** A fail-closed domain or authorization
rejection SHALL mutate nothing. Security-relevant upload/link failures MAY add
only a fixed, non-attributive structural event after a valid firm/case scope is
already established; invalid public tokens never create a case-scoped event or
oracle.

**FR-52 — Store failure.** Intake metadata, audit, link/session, review-record,
or content-store unavailability SHALL fail closed. There is no cached
authorization, stale case projection, in-memory fallback outside Development,
or partial success response.

**FR-53 — Deployment and retention limitation.** This spec SHALL not extend
spec 011's seven-year review-record retention decision to cases, questionnaire
history, audit events, link/session records, metadata, or source-document blobs.
Source-document retention and privileged destruction require a separate
human-approved PII/security and deployment decision. Until then, the existing
non-Development startup refusal remains, no production content-store provider
or deployment artifact ships, and no environment may receive real taxpayer
uploads. Development/test content uses synthetic non-SIN fixtures only.

**FR-54 — Preserved tax behavior.** The portal and workflow SHALL contain no tax
calculation, monetary arithmetic, rounding, CRA line/box mapping, medical
eligibility list, or new tax-year authority. All existing engine, case,
processing, review, identity, persistence, review-screen, and full-suite
behavior remains green.

**FR-55 — Narrow the portal gate, do not erase it.** Repository instructions and
capability documentation SHALL be updated to authorize only this spec's
enumerated case-detail, link/session, questionnaire, upload, progress, and exact
handoff surfaces plus the FR-26 opaque-byte exception. They SHALL continue to
forbid SIN in structured surfaces, raw-document viewing, extraction, tax
calculation, additional document categories, production deployment, and any
unapproved taxpayer-data surface. Any production project-reference change
selected by `/plan` SHALL preserve inward dependency direction and the single
portal composition root.

### Blazor Web App and review-screen migration

**FR-56 — Blazor Web App foundation.** `TaxAI.Portal` SHALL establish one
Blazor Web App foundation for every new interactive portal screen in this spec.
Server-side rendering SHALL be the default. Interactive Server MAY be enabled
only on the narrow adaptive-questionnaire or upload interaction that requires
client interactivity; it SHALL NOT be applied globally or to the read-only
Accountant Review Screen, accountant progress output, fixed error responses,
anonymous shell, or health response. This foundation SHALL add no WebAssembly,
SPA, npm, or client-side authorization/business-logic path.

**FR-57 — Strongly typed component rendering.** Successful accountant case
creation/link-management/handoff, client intake, questionnaire,
expected-document checklist, upload, accountant-progress, and review-screen
markup SHALL be produced by strongly typed Razor components. Endpoint code
SHALL NOT manually concatenate those screens' HTML, use `StringBuilder` as an
HTML renderer, or construct an equivalent raw-markup string. This prohibition
does not alter the existing fixed structural writers for empty safe `404`,
fixed `429`, or fixed plain-text error responses.

**FR-58 — Exact review query and route preservation.** The migrated Accountant
Review Screen SHALL retain `GET /reviews/{recordId:guid}`, the existing
`PortalAuthorization.FirmScopedPolicy`, and exactly one
`IReviewRecordRepository.FindAsync(TrustedFirmId, Guid, CancellationToken)`
lookup using the trusted firm from `PortalAuthorization` request state, the
requested opaque record selector, and the request cancellation token. A firm
value from the route, query, form, body, header, cookie, component parameter, or
Interactive Server circuit SHALL never become authority. A missing, malformed,
foreign-firm, or browser-unsafe record SHALL preserve spec 012's
indistinguishable empty `404`, and non-GET methods SHALL not query the
repository.

**FR-59 — Exact review projection preservation.** The strongly typed review
component SHALL receive only the closed spec 012 browser DTO allowlist: tax
year, immutable package version, slip type, taxpayer display name, employer
display name, boxes and invariant values, tax lines and invariant values,
ordered source-box references, and unmapped codes. It SHALL preserve persisted
order, explicit empty states, invariant formatting without arithmetic or
rounding, contextual encoding, and non-cacheable responses. It SHALL receive no
firm, case, participant, record, document, user, membership, session,
persistence, retention, or non-display timestamp value and no SIN
representation. Persistence entities SHALL not be passed to the component.

**FR-60 — Presentation-only components.** Razor components SHALL NOT derive a
firm or case, perform R-1 or client-session authorization, access a repository
or content store, validate or fingerprint an upload, project checklist status
or readiness, decide an audit event, invoke a tax-domain service, or mutate
intake/review state. Those operations SHALL remain in server-side
endpoint/application services. Component parameters and event payloads are
untrusted presentation input and never an authorization boundary.

**FR-61 — Revocation across component interactions.** Component use SHALL not
cache an authorization decision in component or circuit state. Every
accountant taxpayer-data operation SHALL re-enter the existing durable-session
and R-1 server boundary, and every client operation SHALL re-enter the
server-side client-session boundary. Revoked accountant sessions, inactive
users or memberships, revoked/reissued links, revoked/expired client sessions,
and either session-store failure SHALL therefore fail on the next protected
operation without repository/content-store mutation or taxpayer response,
including when an Interactive Server circuit is still connected.

**FR-62 — Components, layout, and CSS assets.** Reusable portal markup and
layout introduced by this spec SHALL live in reusable Razor components, and
reusable styling SHALL live in tracked CSS assets or component-scoped CSS.
Endpoint code SHALL contain no assembled style block, and the migrated review
screen SHALL not retain its current endpoint-built inline stylesheet. CSS and
layout reuse SHALL not expand either browser allowlist or introduce a
third-party resource request.

**FR-63 — UI migration has no persistence side effect.** The Blazor foundation
and review-screen migration SHALL require no change to `Persistence` or
`Tenancy` production code, `IReviewRecordRepository`,
`PersistedReviewRecord` or its immutable nested contracts, any existing EF
migration/model snapshot, or the existing database schema. Any new intake
schema or migration selected by `/plan` SHALL be attributable only to
FR-1–FR-55's new intake persistence and SHALL not alter the existing immutable
review tables or contracts.

---

## Out of scope

- OCR, LLM extraction, document intelligence, text extraction, image
  interpretation, manual transcription, or implementation/registration of
  `IDocumentExtractionIngress`;
- T4 parsing, box validation, box-to-line mapping, cross-slip aggregation, or
  any use of uploaded bytes as tax truth;
- medical-expense eligibility, patient/dependant rules, eligible-period rules,
  receipt sufficiency, certification rules, totals, thresholds, credits, or
  advice;
- tax calculations, T1 assembly or generation, review-package construction from
  uploads, and persisted review-record creation from intake readiness;
- T5 or any document category other than T4 and medical-expense documents;
- raw-document preview, download, inline rendering, print, email, export, or
  client/accountant deletion;
- review decisions, any functional or browser-allowlist expansion of the
  existing Accountant Review Screen, “latest review” selection, or creating a
  review record; the renderer migration required by FR-56–FR-63 changes
  implementation and reusable presentation only;
- SIN collection as a questionnaire/case field, SIN extraction, masking, search,
  indexing, tokenization, metadata, or display;
- notification or email delivery of the generated link—the accountant copies it
  once and uses an out-of-band channel;
- firm, user, membership, or identity-provider provisioning;
- source-document/blob, intake-history, or audit retention and purge policy;
- Azure Storage/Key Vault provisioning, production content-store implementation,
  hosting, TLS/exposure, secrets, operations, migration execution, or production
  deployment;
- UI-migration-driven changes to `Persistence` or `Tenancy` production code,
  immutable review-package/persisted-review contracts, existing EF migrations
  or model snapshot, or existing database schema; genuinely new intake storage
  remains in scope only under FR-1–FR-55;
- Blazor WebAssembly, a standalone SPA, npm/client-framework infrastructure,
  client-side authorization or business logic, global Interactive Server render
  mode, and migration of pre-existing portal surfaces other than the Accountant
  Review Screen;
- tax years other than 2025.

---

## Acceptance criteria

Each acceptance criterion is one mechanically checkable Given/When/Then test.

### Happy path and reuse

**AC-1 — Create and issue.** Given an active accountant session in firm F, a
primary display name, tax year 2025, two expected T4 items, and one expected
medical item, When intake creation succeeds, Then exactly one existing
`TaxCase` is created under F with no SIN, its checklist has those category
counts, one active link digest exists, and the raw identifier-free URL is
returned once.

**AC-2 — Redeem, answer, upload, submit.** Given AC-1's link, When the client
redeems it once, answers each rendered availability question, uploads three
distinct valid files to the matching slots, and submits, Then one immutable
submission references those exact answer/document versions and the reused
`TaxCase.Readiness` is Ready.

**AC-3 — Accountant progress.** Given AC-2, When the authenticated accountant
opens the case under firm F, Then the response shows the display name, 2025,
expected counts, Ready item statuses, completed-question count, submission
version, unsubmitted-changes state, link state, and no excluded identifier or
content.

### Case, checklist, and year fail-closed rules

**AC-4 — Missing active firm.** Given an authenticated principal with no trusted
active firm, When case creation is requested, Then authorization fails before
intake storage is called and no record or audit event is created.

**AC-5 — Caller firm ignored/refused.** Given a live firm-F session and a create
request containing firm G in any input channel, When requested, Then G is never
used, no firm-G operation occurs, and a bound firm field is rejected.

**AC-6 — Missing display name.** Given a blank primary display name, When create
is requested, Then it fails closed and no case or link is created.

**AC-7 — Missing or foreign year.** Given year absent, 2024, and 2026 in separate
create requests, When each is executed, Then each fails closed, no case exists,
and none is coerced to 2025.

**AC-8 — Empty checklist.** Given T4 count zero and medical count zero, When
create is requested, Then it fails closed and no case/link exists.

**AC-9 — Invalid count.** Given a negative, missing, non-integral, or 26-item
category count in separate requests, When create is requested, Then each fails
closed and creates nothing; counts 0 through 25 are evaluated according to FR-4.

**AC-10 — Unsupported category.** Given `Other` or an undefined document
category, When submitted through case or upload input, Then it fails closed and
no expected item or document exists.

**AC-11 — Initialization rollback.** Given a failure while storing the link
digest after case/checklist preparation, When create runs, Then no reachable case,
link, partial metadata, or creation audit event remains.

### Link and replay fail-closed rules

**AC-12 — URL content.** Given a newly issued link, When its path/query/fragment
are inspected, Then only the fixed prefix and opaque credential appear and no
firm, tenant, case, participant, taxpayer, year, document, user, membership, or
session identifier appears.

**AC-13 — One-way store.** Given a newly issued raw credential, When every SQL
value, log, audit event, diagnostic, and content-store key is inspected, Then the
raw credential is absent and only its non-reversible digest is persisted.

**AC-14 — No redisplay.** Given a completed issuance response, When the case is
reloaded, Then no operation can recover or return the raw URL; reissue returns a
different credential and digest.

**AC-15 — Invalid lifetime configuration.** Given absent, zero, negative, or
non-finite link/session lifetime configuration in separate host starts, When
startup runs, Then each host refuses to start.

**AC-16 — Expiry boundary.** Given a link with expiry E, When redemption occurs
at the last instant before E and exactly at E in separate tests, Then the first
may succeed and the second returns the safe empty 404 with no session.

**AC-17 — Atomic replay.** Given two concurrent antiforgery-valid redemption
POSTs for one valid link, When both complete, Then exactly one client session is
created and the other response is byte-identical to an invalid-link 404.

**AC-18 — Revoked link.** Given an unspent link that the accountant revokes,
When it is presented, Then it returns the same empty 404 as an unknown link and
creates no session.

**AC-19 — Reissue revokes old scope.** Given an active link and derived client
session, When the accountant reissues, Then the old link and cookie both fail,
one new active link exists, and immutable prior history remains.

**AC-20 — Read-only confirmation and clean redirect.** Given a valid unspent
link, When its GET-then-confirmation-POST sequence is executed, Then the GET
renders a fixed antiforgery-protected form without spending the link or creating
a session, with only a protected opaque redemption handle posted to a fixed
clean route; and the POST redeems it and redirects once to the fixed clean
intake URL with the raw credential absent from confirmation HTML, action,
hidden fields, POST URL/body, destination, and response body and all client
responses non-cacheable/no-referrer.

**AC-21 — Cookie content.** Given a redeemed session, When application-session
and antiforgery cookies plus the durable row are inspected, Then the session
cookie contains only a protected opaque key, the antiforgery cookie contains no
scope/content value, neither cookie contains a firm/case/link/answer/taxpayer
value, and firm/case exist only in the server record.

**AC-22 — Revoked/expired session.** Given separately a revoked, idle-expired,
absolute-expired, and link-expired client session, When `/intake` is requested,
Then each receives the same empty 404 and no scoped repository operation follows.

**AC-23 — Client-session store failure.** Given a client cookie and a session
store that throws, When any client endpoint is requested, Then it fails closed
without a cached scope, mutation, or taxpayer response.

### Questionnaire and immutable history

**AC-24 — Adaptive T4-only page.** Given a case requesting T4 but not medical,
When the client page renders, Then only the T4 availability question and T4
upload slots appear.

**AC-25 — Adaptive medical-only page.** Given a case requesting medical but not
T4, When the page renders, Then only the medical availability question and
medical upload slots appear.

**AC-26 — Closed answers.** Given a valid question, When each supported answer
code is submitted, Then it is accepted; a free-text, unknown, blank, or
case-foreign value in separate tests fails closed without a version.

**AC-27 — Unrequested question.** Given a T4-only case, When a medical answer or
upload is posted, Then the response is the safe empty 404 and no answer/document
is stored.

**AC-28 — Answer versions.** Given answer A at version 1, When the client changes
it to B, Then version 2 is appended, version 1 remains byte-for-byte unchanged,
and the current projection is B.

**AC-29 — Duplicate questions in one request.** Given one question appears twice
in one answer-save request, When saved, Then the request fails closed and no
version is appended.

**AC-30 — Immutable submission.** Given submission version 1, When answers and
documents later change and submission version 2 is created, Then version 1
still references its original exact versions and version 2 references the new
ones.

**AC-31 — Concurrent submission version.** Given two concurrent attempts to
create the same next submission version, When committed, Then exactly one
snapshot exists and the other attempt fails with no partial references.

**AC-32 — Partial submission.** Given one unavailable expected item, When the
client submits, Then an immutable snapshot is created, that item projects
`missing`, and reused readiness is NotReady.

### Upload validation and duplicate detection

**AC-33 — Allowed formats.** Given one runtime-generated synthetic PDF, JPEG,
and PNG with matching signatures in separate tests, When each is uploaded
within configured limits, Then each creates one immutable document record with
only normalized safe metadata.

**AC-34 — Empty upload.** Given a zero-byte upload, When posted, Then it is
rejected, no document/content record exists, and the expected item projects
`rejected`.

**AC-35 — Unsupported format.** Given an archive or executable in separate
tests, When posted, Then each is rejected and no source bytes remain reachable.

**AC-36 — Signature mismatch.** Given a file declared as a configured allowed
media kind whose signature does not match that kind, When posted, Then it is
rejected and creates no document record.

**AC-37 — Malformed multipart.** Given a malformed upload body, When posted,
Then a fixed error is returned and no state or attributable audit event is
created.

**AC-38 — Byte limit.** Given a file exactly at the configured maximum and one
byte over in separate tests, When uploaded, Then the first is accepted and the
second is rejected with no retained content.

**AC-39 — File/count limit.** Given a request at the configured file-count limit
and one above it, When uploaded, Then the first is evaluated normally and the
second is rejected before persistence.

**AC-40 — Filename disposal.** Given an original filename containing a planted
SIN-like value and HTML/script text, When the upload succeeds or fails, Then the
multipart request uses only the fixed neutral transport name and the original
filename appears in no SQL row, content key/path, response, HTML, log, audit,
metric, trace, exception, or diagnostic.

**AC-41 — Same-case duplicate bytes.** Given one accepted document in firm F/case
C, When byte-identical content is uploaded again under a different filename,
Then it is rejected as duplicate and no second document record exists.

**AC-42 — Same name, different bytes.** Given one accepted document, When
different bytes are uploaded with the same original filename, Then duplicate
detection does not use the filename and the distinct content may be accepted.

**AC-43 — Cross-case non-oracle.** Given identical bytes already stored in
firm F/case C1, When uploaded to C2 in F and to case C3 in another firm in
separate tests, Then both are evaluated as new within their server-derived case
and neither response reveals the other record; the three stored scope-derived
fingerprints are not equal.

**AC-44 — Concurrent duplicate.** Given two concurrent identical uploads to one
case, When both complete, Then exactly one immutable document/content object is
reachable and one request receives the fixed duplicate result.

**AC-45 — Content-store failure.** Given the content store fails after streaming
but before commit, When upload completes, Then no accepted metadata,
`ReceivedEvidence`, or ready status exists.

**AC-46 — Metadata failure cleanup.** Given encrypted content is written but the
metadata transaction fails, When recovery runs, Then the unreferenced object is
removed and diagnostics contain only fixed structural facts.

**AC-47 — No content extraction.** Given a test-only source document assembled
in memory at runtime (never checked in) whose synthetic bytes include names, a
SIN-shaped probe, amounts, T4 box-like text, medical text, and
`-1.005`/`1.005`, When uploaded and submitted, Then
SQL/DTO/log/audit/diagnostic inspection finds none of those values, no parsed
field or monetary result exists, and no rounding or arithmetic service was
called.

### Status and readiness

**AC-48 — Requested.** Given an expected item with no answer/upload/submission,
When progress is read, Then its status is `requested`.

**AC-49 — Uploaded.** Given a valid distinct upload not referenced by the latest
submission, When progress is read, Then its status is `uploaded`.

**AC-50 — Rejected.** Given an unsatisfied item whose latest upload attempt fails
format or duplicate validation, When progress is read, Then its status is
`rejected` and no rejected source bytes exist.

**AC-51 — Missing.** Given the latest submission explicitly declares an item
unavailable, When progress is read, Then its status is `missing` and readiness
is NotReady.

**AC-52 — Ready.** Given the latest submission references one valid current
document for an item, When progress is read, Then its status is `ready`.

**AC-53 — Category count.** Given a checklist requiring two T4s and one medical
document, When the latest submission contains only one T4 and one medical,
Then medical is ready, the unsatisfied T4 item/count is requested unless
explicitly declared unavailable, and case readiness is NotReady.

**AC-54 — Category isolation.** Given two medical documents for a case requiring
one T4 and one medical, When submitted, Then medical is satisfied, T4 remains
requested unless explicitly declared unavailable, and readiness is NotReady.

**AC-55 — Final requirement.** Given all questions answered and all but one
expected item ready, When the last valid document is included in a new
submission, Then the existing `TaxCase.Readiness` changes from NotReady to Ready
without a writable ready flag.

**AC-56 — Post-ready unsubmitted change.** Given a ready submission, When a new
answer version or replacement upload is saved, Then historical and current
TaxCase readiness remain derived from that latest submission, the submission is
unchanged, and progress reports `unsubmitted changes` until another submission.

### Tenant isolation and safe not-found

**AC-57 — Every owned row is scoped.** Given the relational model, When case,
item, answer, document, submission, audit, link, client-session, and handoff
entities are enumerated, Then each tenant-owned row has non-null server-derived
firm and case keys, except the two closed lookup operations in FR-15 still
return—not accept—those keys.

**AC-58 — No unscoped operation.** Given every intake repository contract, When
its methods are inspected, Then only digest redemption and opaque session lookup
omit trusted firm/case inputs; all other operations require both.

**AC-59 — Foreign accountant case.** Given firm-F accountant and a case in firm
G, When the opaque G selector is requested, Then the response is byte-identical
to a missing-case 404 and no unscoped query executes.

**AC-60 — Foreign client item/document.** Given a client session for case C1,
When an item or document selector from C2 is posted, Then each returns the same
empty 404 as missing and no C2 state is read or mutated.

**AC-61 — Invalid-link equivalence.** Given malformed, random, expired, revoked,
replayed, missing-case, and internally inconsistent link records in separate
tests, When redeemed below the rate limit, Then every response has identical
status, empty body, security/cache headers, and no client session.

**AC-62 — Rate-limit equivalence.** Given valid and invalid credentials that
exceed the same configured public partition limit, When presented, Then both
receive the same fixed 429 and neither is redeemed or audited to a case.

**AC-63 — Antiforgery.** Given each client/accountant state-changing route
without a valid antiforgery token, When called, Then it creates no case, link,
answer, document, submission, status change, or audit event.

### PII, browser, and audit

**AC-64 — SIN-free structured surfaces.** Given raw source bytes and an original
filename assembled in memory at test runtime (never checked in) containing
synthetic full, formatted, masked, partial, and tokenized SIN-shaped probes,
When the complete flow runs, Then none appears in any DTO, HTML, URL, cookie,
SQL/content metadata, log, metric, trace, audit, exception, error, or diagnostic.

**AC-65 — Opaque bytes acknowledged.** Given an accepted encrypted source object
whose test bytes were assembled in memory at runtime (never checked in) with a
synthetic SIN-shaped probe, When the content store and structured surfaces are
inspected, Then the probe is retained only inside that opaque encrypted object
and no test or product claim asserts that the source document itself is
SIN-free.

**AC-66 — No SIN field.** Given all new browser DTOs, persisted metadata models,
schema columns, audit payloads, and public contracts, When names/properties are
enumerated, Then none declares a SIN, partial-SIN, SIN-presence, SIN-derived
identifier, masking, or redaction field.

**AC-67 — Client allowlist.** Given a successful client page, When DTO, HTML,
headers, and client-side state are inspected, Then every FR-39 field is present
as applicable and every excluded identity/taxpayer/storage field is absent.

**AC-68 — Accountant allowlist and encoding.** Given a display name containing
HTML/script metacharacters, When progress renders, Then the FR-38 fields are
present, the name is encoded, and no raw document content or excluded identifier
is present.

**AC-69 — Append-only audit.** Given the happy path plus reissue, one rejected
upload, and a second submission, When audit storage is read in order, Then one
immutable event exists for every accepted FR-50 action, earlier events are
unchanged, and payloads contain only the safe allowlist.

**AC-70 — Rejected public token creates no oracle event.** Given a random invalid
token, When redemption fails, Then no case-scoped audit event is appended.

### Review handoff

**AC-71 — Ready without record.** Given a Ready intake with no associated
persisted review record, When progress renders, Then it shows the fixed
“awaiting review record” state, no review link, and no review record is created.

**AC-72 — Record without readiness.** Given a NotReady intake with an exact
same-firm/same-case persisted review record, When progress renders, Then no
review link is shown and neither artifact is mutated.

**AC-73 — Exact handoff.** Given a Ready intake and an exact independently
existing review record whose firm and case match, When the authenticated
accountant submits that `RecordId` and then requests progress, Then one
immutable association/audit event is appended, one link targets the existing
exact `/reviews/{recordId:guid}` route, and opening it preserves spec 012
behavior.

**AC-74 — Invalid handoff association.** Given a Ready intake and a blank,
malformed, missing, foreign-firm, or wrong-case `RecordId` in separate tests,
When the accountant submits it, Then the response is the fixed safe failure, no
association/link/audit event exists, and no fallback/latest record is selected.

**AC-75 — No manufacture.** Given a flow that reaches Ready, When collaborators
are inspected, Then no extraction ingress, parser, mapper, processor, assembler,
projection, review save, calculation, rounding, or manual-transcription service
was invoked.

### Tax-domain boundaries and regression

**AC-76 — Tax-year isolation.** Given a 2025 case and any answer, document,
submission, or handoff association stated for 2024 or 2026, When applied, Then
it fails closed and no adjacent-year policy or record is used.

**AC-77 — No tax truth or hard-coded mappings.** Given new production source and
schema, When scanned and inspected, Then there is no CRA line-number authority,
box-to-line table, mapping-resource read, tax calculation, medical eligibility
rule, monetary arithmetic, `Math.Round`, `MoneyRounding`, `double`, or `float`.

**AC-78 — Raw amount/rounding boundary.** Given positive and negative half-cent
text, zero, absent optional box text, and an otherwise empty tax payload inside
valid opaque document bytes, When uploaded, Then the bytes are never parsed as
amounts or boxes and no rounding, required-box, or empty-slip rule is evaluated.

**AC-79 — Existing behavior.** Given the completed specs 001–013 baseline, When
`pwsh ./scripts/Invoke-SolutionTests.ps1` runs after implementation, Then every
registered test project executes with no failure and no skip, and all existing
tax parsing, mapping, case, processing, review, tenancy, persistence,
authentication, firm-selection, review-screen, health, and startup-refusal
behavior remains green, including every spec 012 route, query, allowlist,
ordering, encoding, amount-fidelity, safe-`404`, session-revocation, and no-SIN
assertion after the review renderer becomes a Blazor component.

**AC-80 — Deployment refusal.** Given a non-Development host without a
separately approved production content store and deployment configuration, When
it starts, Then startup fails before any request can be served.

**AC-81 — Retention non-extension.** Given the new source, schema, configuration,
and documentation, When inspected, Then no seven-year or other
source-document/intake-history retention or purge rule has been introduced and
spec 011's review-record policy remains unchanged.

**AC-82 — Status precedence.** Given separately (a) a missing/rejected item that
later receives a valid unsubmitted replacement and (b) a ready item that later
receives an additional valid upload, When progress is read, Then (a) projects
`uploaded` and (b) remains `ready` with `unsubmitted changes`, matching the one
fixed precedence rule.

**AC-83 — Fingerprint key configuration.** Given absent, empty, or invalid
fingerprint-key configuration in separate host starts, When startup runs, Then
each host refuses to start before accepting an upload.

**AC-84 — Credential generation.** Given link issuance and client-session
creation, When production collaborators and generated samples are inspected,
Then credentials come from the approved cryptographically secure random source,
are opaque and non-empty, and are never derived from firm, case, taxpayer,
clock, counter, or another identifier.

**AC-85 — Portal gate remains narrow.** Given the post-implementation root and
portal instructions plus capability documentation, When inspected, Then they
authorize exactly spec 014's new surfaces and opaque-byte exception while
retaining every prohibition listed in FR-55 and the unchanged production
refusal.

**AC-86 — Handoff association is immutable.** Given a case already associated
with one exact review `RecordId`, When the accountant submits the same or a
different `RecordId`, Then the operation fails closed, the first association is
unchanged, and no replacement association or success audit event is appended.

**AC-87 — Display-name SIN-shaped input.** Given compact, hyphenated,
space-separated, and masked synthetic SIN-shaped display-name probes assembled
only at test runtime in separate requests, When case creation is attempted,
Then each is rejected before `TaxCase.Create`, persistence, or audit; the fixed
response and captured logs never echo or mask the rejected value.

**AC-88 — Display-name length boundary.** Given otherwise safe display names
exactly at `PersistedSchema.DisplayNameMaxLength` and one character over in
separate valid create requests, When each is submitted, Then the exact-boundary
name is accepted and the over-length name is rejected without truncation,
persistence, audit, or echo.

### Blazor foundation and review-screen migration

**AC-89 — One Blazor Web App foundation.** Given the implemented portal, When
its service registration, endpoint mapping, project/package references, and
tracked client assets are inspected, Then one Blazor Web App foundation renders
every new successful interactive screen in spec 014 and there is no
WebAssembly, standalone SPA, npm, or second composition-root path.

**AC-90 — Server rendering is the default.** Given every Razor component and
render-mode declaration introduced by spec 014, When enumerated, Then the
Accountant Review Screen, accountant progress output, and server-form surfaces
are statically server-rendered, no global Interactive Server mode exists, and
any Interactive Server declaration is confined to the adaptive questionnaire or
upload interaction that requires it.

**AC-91 — No manual screen-markup assembly.** Given production source for
accountant case creation/link management/handoff, client intake,
questionnaire, expected-document checklist, upload, accountant-progress, and
review rendering, When scanned and its success paths are exercised, Then each
success response comes from a strongly typed Razor component,
`ReviewEndpoints.cs` contains no `StringBuilder`/append-based review renderer
or assembled style block, and no endpoint/application service concatenates
equivalent screen HTML; fixed empty `404`, fixed `429`, and fixed plain-text
error writers remain unchanged.

**AC-92 — Review route and exact query are preserved.** Given an authorized
firm-F accountant and a firm-F persisted review record, When
`GET /reviews/{recordId:guid}` succeeds through the migrated component, Then the
endpoint calls `IReviewRecordRepository.FindAsync` exactly once with the
server-derived `TrustedFirmId` F, that exact route `RecordId`, and the request
cancellation token, and performs no save or alternate/unscoped/latest query.

**AC-93 — Review refusal equivalence is preserved.** Given malformed, missing,
foreign-firm, browser-unsafe, revoked-accountant-session, inactive-user, and
inactive-membership review requests in separate tests, When each is made after
the migration, Then the applicable spec 012 authorization or byte-identical
empty-`404` result is unchanged, no taxpayer component is rendered, and no
unscoped or authorization-refused repository read occurs.

**AC-94 — Review component allowlist is exact.** Given a successful migrated
review request, When the component parameter/view-model property set, rendered
HTML, page source, headers, and component state are inspected, Then they contain
exactly the applicable FR-59 fields and no persistence entity, excluded
identifier/instant, or SIN representation.

**AC-95 — Review presentation fidelity is preserved.** Given a spec 012 record
containing ordered populated and empty collections, HTML metacharacters, and
positive, negative, zero, and four-decimal stored amounts, When the migrated
component renders it, Then collection order, explicit empty states, contextual
encoding, invariant values without rounding/arithmetic, and non-cacheable
headers match the pre-migration contract.

**AC-96 — Components are presentation-only.** Given every Razor component
introduced by spec 014, When its constructor/property injections, namespaces,
parameters, event handlers, and executable source are inspected, Then it
depends on no repository, content store, EF type, trusted-firm resolver,
authorization policy implementation, readiness/status calculator, fingerprint
service, audit writer, parser, mapper, processor, or review assembler, and
component inputs are passed only to the owning server-side operation.

**AC-97 — Revocation survives Interactive Server.** Given separately an
accountant or client with an already-connected Interactive Server circuit, When
the accountant session/membership or client link/session is revoked or its
store fails before the next protected interaction, Then that interaction
re-enters the applicable server authorization boundary, returns the fixed
refusal, performs no repository/content-store mutation, and renders no new
taxpayer response.

**AC-98 — Reusable component and CSS assets.** Given the migrated review screen
and new intake/progress screens, When their source and responses are inspected,
Then shared layout/markup is implemented by reusable Razor components, reusable
styles are served only from tracked local CSS or component-scoped CSS assets,
no endpoint emits an inline assembled stylesheet, and no third-party resource
request is present.

**AC-99 — Review UI migration is storage-neutral.** Given the isolated task
that establishes the Blazor foundation and migrates the review screen, When its
diff and the post-task contract/schema baselines are inspected, Then it changes
no `Persistence` or `Tenancy` production file, immutable review-package or
persisted-review contract, EF migration/model snapshot, or database schema, and
all spec 011/012 persistence tests remain green.

---

## Planning constraint

`/plan` SHALL use a small number of substantial implementation tasks and SHALL
start from this human-authoritative split:

1. Blazor Web App portal foundation plus the behavior-preserving migration of
   the existing Accountant Review Screen.
2. Secure intake case, invitation/token, questionnaire, expected-checklist,
   persistence/content-storage, and projection backend.
3. Client intake/questionnaire/checklist/document-upload UI plus
   accountant-visible progress and exact review handoff.
4. Final integration and security verification **only if genuinely necessary**
   as an independently valuable merge boundary; it is not a mandatory
   catch-all.

The planner MAY adjust ownership at a genuinely independent security,
persistence/content-storage, upload, or UI boundary, but SHALL preserve the
compact topology. It SHALL NOT create separate tasks for components, CSS, DTOs,
individual endpoints, documentation, migrations, tests, or mechanical wiring,
and SHALL NOT create a contract-only scaffold or speculative OCR, extraction,
calculation, or deployment task.

---

## Open questions

### Blocking Class A questions

None.

### `[ASSUMED]` Class B decisions for one-pass human override

- `[ASSUMED]` The folder/slug is
  `specs/014-client-intake-and-document-upload-flow/`.
- `[ASSUMED]` The accountant configures expected document counts at case
  creation, with a maximum of 25 items per category; the client does not add an
  unrequested category.
- `[ASSUMED]` The adaptive questionnaire uses the closed availability codes
  “available now,” “will provide later,” and “unavailable,” with no free text.
- `[ASSUMED]` The closed source-format allowlist is PDF, JPEG, and PNG, each
  validated by content signature as well as normalized media kind.
- `[ASSUMED]` Status is projected per expected item, while uploaded document
  records and upload/submission events are immutable.
- `[ASSUMED]` The raw link is shown once to the accountant for out-of-band
  delivery; email/SMS delivery is not part of the MVP.
- `[ASSUMED]` The accountant progress surface is one case-detail route, not a
  case list/search/paging feature.
- `[ASSUMED]` Raw-document preview/download is excluded; progress is structural.

The Blazor foundation, review-screen migration, preserved security/data
boundaries, and compact planning topology are human-authoritative decisions
recorded above, not assumptions.

---

## CRA sources

This PRD introduces no CRA box number, line number, filing deadline, rate,
threshold, limit, gross-up factor, credit percentage, eligibility rule, or
calculation. The sources below establish only that the two 2025 document
categories are genuine tax/source-document categories and that source
documents can contain sensitive identifiers. They do not authorize TaxAI to
interpret eligibility or content.

| Claim | Source URL | Tax year | Retrieved |
|---|---|---|---|
| A T4 is a remuneration slip and the CRA publishes a 2025 T4 form; in this feature it is only an opaque upload category. | https://www.canada.ca/en/revenue-agency/services/forms-publications/forms/t4.html | 2025 | 2026-07-29 |
| The CRA's 2025 T4 electronic specification includes an employee SIN field; this is cited only to justify treating opaque source bytes as highly sensitive, not to extract or validate it. | https://www.canada.ca/en/revenue-agency/services/e-services/filing-information-returns-electronically-t4-t5-other-types-returns-overview/t619-next-year/t4-2025.html | 2025 | 2026-07-29 |
| CRA's 2025 medical-expenses guide treats receipts/supporting documents as evidence to keep and notes that some receipt types may contain a SIN; this feature therefore treats all source bytes as sensitive and makes no eligibility decision. | https://www.canada.ca/en/revenue-agency/services/forms-publications/publications/rc4065/medical-expenses.html | 2025 | 2026-07-29 |

No source conflict was resolved by judgement. Medical eligibility, claimant,
period, certification, amount, and calculation semantics are deliberately
absent.

---

## Definition of Done — self-check

- [x] `prd.md` exists and opens with an explicit `tax-domain` classification.
- [x] Problem, reuse analysis, user stories, functional requirements, out of
      scope, mechanically checkable acceptance criteria, open questions, and
      CRA sources are present.
- [x] The exact 2025 tax-year scope and fail-closed year isolation are stated.
- [x] Reuse was checked against existing PRDs and source; existing TaxCase,
      checklist/readiness, tenancy/session, review repository, and review route
      are reused rather than duplicated.
- [x] Every named fail-closed rule has a distinct acceptance criterion.
- [x] Happy path, empty/zero/limit boundaries, raw half-cent/no-rounding
      boundary, PII, tax-year isolation, no hard-coded mapping, and full-suite
      regression are covered.
- [x] The raw-file content exception and the no-SIN structured boundary are
      explicit without falsely claiming a T4 or medical source file is SIN-free.
- [x] Expected-item status is distinct from immutable uploaded document records.
- [x] Duplicate detection is scoped to the server-derived firm and case and
      creates no cross-tenant oracle.
- [x] The review handoff cannot manufacture a `PersistedReviewRecord` and uses
      only one exact independently existing same-firm/same-case record.
- [x] The human-authoritative Blazor Web App, SSR-first, narrowly interactive,
      strongly typed component, separation-of-concerns, reusable CSS/layout,
      and review-migration decisions are stated without an `[ASSUMED]` tag.
- [x] The review migration preserves the exact spec 012 route, query, DTO
      allowlist, server-derived firm, R-1 authorization, safe `404`,
      session-revocation, non-cache, immutability, and no-SIN boundaries.
- [x] The UI-only task is mechanically required to leave `Persistence`,
      `Tenancy`, immutable review contracts, migrations, model snapshot, and
      database schema unchanged.
- [x] Planning is constrained to the human-authoritative three substantial
      implementation tasks plus a fourth integration/security task only when
      genuinely necessary, with no component/CSS/DTO/endpoint/documentation/
      wiring microtasks.
- [x] Spec 011's seven-year review-record retention rule was not extended to
      source documents or intake history.
- [x] No Class A tax, PII, tenant-isolation, or security assumption was made.
- [x] No architecture, task list, code, status marker, branch, commit, or PR was
      created by this PRD work.
