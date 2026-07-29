# PRD 012 — Accountant Review Screen

> **Status: proposed for human approval. No implementation is authorized by
> this document.**

## Feature classification

**Class: `tax-domain`.**

This is the first portal route that renders persisted taxpayer review data.
Although it changes only the host and its tests, firm isolation, PII disclosure,
and faithful display of engine-produced values make it a tax-domain feature.

## Problem

Specs 009 and 011 already provide the immutable accountant review package, the
closed SIN-free persistence projection, the firm-scoped repository, durable
sessions, server-derived firm selection, and per-request active-user and
active-membership checks. The portal does not yet expose that read path.

An authenticated accountant therefore cannot open one persisted review-package
version and inspect the taxpayer and employer display names, populated box
amounts, CRA line entries, source box codes, and unmapped box codes that were
stored for review.

The feature is intentionally one small, read-only screen. It does not create a
new review model, storage adapter, query language, list/search surface, or
mutation path.

## Human decisions carried into this revision

The 2026-07-29 replanning request fixes these requirements:

1. Use the completed spec 011 tenancy, persistence, session, and R-1
   foundations.
2. Derive `FirmId` only from server-authenticated request state.
3. Revalidate the active local user and active firm membership.
4. Return a safe, indistinguishable `404` for missing and foreign records.
5. Preserve immutable review-package versions.
6. Permit no SIN in the browser DTO, HTML, logs, or persistence projection.
7. Preserve the existing deployment refusal.
8. Use no more than two implementation tasks, preferring one atomic task when
   the existing architecture supports it.
9. Do not create a contract-only task, `NotImplementedException` scaffold,
   microtask split, or ordinary corrective task.

## Scope

The screen reads one exact persisted version by its TaxAI-generated opaque
`RecordId`.

The proposed closed browser allowlist is:

- package tax year and immutable package version;
- slip type;
- taxpayer display name and employer display name;
- populated box codes and their stored amounts;
- CRA line codes, their stored amounts, and ordered source box codes;
- ordered unmapped box codes.

The browser projection excludes:

- `FirmId`, `CaseId`, and `ParticipantId`;
- record, slip, line, box, source-code, and document identifiers;
- creation, update, and retention instants;
- addresses, account numbers, contact details, source references, sign-off
  notes, and every field not present in spec 011's persistence allowlist;
- SIN in every form, including full, masked, partial, derived, or presence
  indicators.

The route is proposed as `GET /reviews/{recordId:guid}`. The selector is a
TaxAI-generated opaque GUID; no tenant identifier or taxpayer value is accepted
from the route, query, headers, body, form, or cookies.

## User stories

1. As an accountant, I can open one persisted review-package version and inspect
   its stored review facts without changing it.
2. As an accountant, I can distinguish populated, empty-package, empty-section,
   unmapped-code, not-found, authorization-refused, and unexpected-failure
   states.
3. As a taxpayer, a user from another firm receives the same `404` as a user
   requesting a record that does not exist.
4. As a taxpayer, my SIN never enters the portal-facing model or response.
5. As an operator, the existing non-Development startup refusal remains
   unchanged.

## Functional requirements

### Retrieval and authorization

**FR-1** — The endpoint SHALL require the existing
`PortalAuthorization.FirmScopedPolicy`.

**FR-2** — The endpoint SHALL obtain the trusted firm only from the
`PortalAuthorization` request state produced after durable-session
authentication and current active-user/current active-membership verification.
It SHALL NOT parse or accept a caller-supplied firm value.

**FR-3** — Retrieval SHALL use the existing
`IReviewRecordRepository.FindAsync(TrustedFirmId, Guid, CancellationToken)`
contract. No new repository contract, overload, unscoped query, or direct EF
access SHALL be introduced.

**FR-4** — A missing record and a record owned by another firm SHALL both return
the same empty `404` response. The response SHALL reveal no identifier,
existence signal, or taxpayer content.

**FR-5** — Malformed selectors SHALL fail routing without invoking the
repository. Non-GET methods SHALL not invoke the repository.

### DTO projection and rendering

**FR-6** — An internal portal DTO SHALL be projected explicitly from
`PersistedReviewRecord` through the closed allowlist above. Persistence entities
SHALL NOT be serialized or passed directly to a response writer.

**FR-7** — The projection SHALL preserve the persisted order of slips, box
amounts, line entries, source box codes, and unmapped box codes. It SHALL not
sort, group, filter, deduplicate, aggregate, subtotal, or calculate.

**FR-8** — Stored monetary values SHALL be rendered with invariant culture
without arithmetic or rounding. Zero and negative values SHALL remain explicit.

**FR-9** — The page SHALL be server-rendered and contextually HTML-encoded. It
SHALL introduce no JSON endpoint, client-side data fetch, SPA framework,
`package.json`, static-file tree, or raw HTML output helper.

**FR-10** — The page SHALL provide explicit populated, empty-package,
empty-boxes, empty-lines, no-unmapped-codes, authorization-refused, not-found,
and fixed-error states.

**FR-11** — Review responses SHALL be non-cacheable by browsers and
intermediaries.

### PII, tax, and immutability

**FR-12** — No SIN token or representation SHALL exist in the portal DTO,
renderer, HTML, response headers, cookies, logs, metrics, traces, test fixtures,
or persistence projection. The portal SHALL not name `RecipientIdentifier`,
read a slip SIN, or add a masking/redaction helper.

**FR-13** — The portal SHALL not invoke a parser, mapper, mapping provider,
rounding helper, processor, review assembler, or decision recorder. CRA line
codes, box codes, and amounts SHALL come only from the persisted record.

**FR-14** — The page SHALL use structural labels only. It SHALL not add
hard-coded CRA line numbers, box codes, descriptions, tax meanings, or a second
supported-year policy.

**FR-15** — The endpoint SHALL expose no save, update, upsert, delete, sign-off,
or "latest version" operation. The requested `RecordId` identifies one immutable
stored version, and older versions remain separately addressable.

### Existing posture

**FR-16** — `TaxAI.Portal` SHALL continue to reference exactly
`TaxLogicEngine`, `CaseProcessing`, `Tenancy`, and `Persistence`. It SHALL not
directly reference `CaseReview`, `TaxCases`, or `DocumentExtraction`.

**FR-17** — The current OIDC, opaque-cookie, durable-session, revocation,
antiforgery, connection-string, Data Protection, and active-membership behavior
SHALL remain unchanged.

**FR-18** — The existing non-Development startup refusal SHALL remain. This spec
adds no Azure key-ring provider, hosting configuration, deployment artifact,
provisioning flow, or claim that the application is deployable with real
taxpayer data.

**FR-19** — The anonymous root SHALL remain fixed, server-rendered, and
taxpayer-data-free, but its obsolete claim that the application renders no
taxpayer data SHALL be replaced by a truthful description of the one
authenticated read-only review surface. `/health` SHALL remain byte-identical.

## Out of scope

- review-package list, search, paging, filtering, or "latest" lookup;
- editing, approval, rejection, notes, sign-off, upload, deletion, or purge UI;
- new persistence entities, columns, indexes, migrations, or repository methods;
- direct use of `CaseReview` from the portal;
- document extraction, upload, OCR, LLM, or case workflow;
- JSON, XML, CSV, PDF, print, email, or export;
- tax calculation, mapping, rounding, CRA descriptions, or new tax-year policy;
- production deployment, cloud key-ring implementation, firm/user provisioning,
  or a second taxpayer-data screen.

## Acceptance criteria

1. `GET /reviews/{recordId:guid}` is the only new route and requires the exact
   existing firm-scoped policy.
2. A successful request calls `FindAsync` once with the server-derived
   `TrustedFirmId`, requested `RecordId`, and request cancellation token.
3. Missing and foreign records produce byte-identical empty `404` responses.
4. A malformed selector, unauthenticated caller, inactive user, inactive
   membership, or session/identity-store failure never calls the review
   repository and renders no taxpayer content.
5. A successful response contains every field in the browser allowlist,
   contains no excluded identifier or instant, and preserves every collection's
   stored order.
6. The HTML encoder neutralizes taxpayer-controlled display names and stored
   code values.
7. Positive, negative, zero, and four-decimal stored amounts render invariantly
   with no rounding or arithmetic.
8. Empty package and each empty nested collection have an explicit state.
9. No response, DTO property, executable portal source, captured log, or test
   fixture contains a SIN representation or a nine-consecutive-digit run.
10. A successful GET never calls `SaveAsync`; no production endpoint maps a
    review mutation verb.
11. Existing record versions remain separately retrievable by their own record
    IDs; no "latest" selection is added.
12. The portal still has exactly the existing four project references and one
    package reference; no project or schema artifact is added.
13. `/health`, identity/session behavior, tenant verification, sign-out
    revocation, and startup refusal remain green.
14. Portal boundary tests still prove no parser, mapper, assembler, decision,
    arithmetic, rounding, hard-coded CRA line, hard-coded box code, second
    tax-year policy, client-side stack, direct EF access, or direct CaseReview
    dependency.
15. `pwsh ./scripts/Invoke-SolutionTests.ps1` passes with every registered test
    project executed, no failure, and no skip.
16. The spec 013 upgrade baselines remain immutable evidence. Their tests prove
    the pre-existing route/authentication and internal-contract subset remains
    compatible while recognizing spec 012's exact additive route, internal
    types, and intentionally revised root body.

## Open questions

There is no implementation-blocking architecture question.

The browser allowlist above is the proposed minimum useful review surface and is
presented for human approval with this replan. Any expansion is a new PII
decision and is not implicitly authorized.

## CRA sources

This feature introduces no CRA fact and therefore cites none. It renders stored
codes and values as opaque data and adds no explanation of their tax meaning.
