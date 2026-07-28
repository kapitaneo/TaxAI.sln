# PRD 011 — Accountant Review Screen

> **STATUS: BLOCKED. Not ready for `/plan`.**
> Three Class A questions in [Open questions](#open-questions) must be answered
> by the human before this PRD is complete. They are recorded there, not guessed
> at. Everything else below is written and stable.

---

## Feature classification

**Class: `tax-domain`.**

This feature renders, in a browser, an `AccountantReviewPackage`: parsed T4 slip
fields, engine-produced box amounts, engine-produced CRA line entries, unmapped
box codes, and a masked SIN. It is the first surface in this repository capable
of putting taxpayer-domain data in front of a human, and the first that decides
**who may see whose data**.

It is `tax-domain` for four independent reasons, any one of which would be
sufficient:

1. **It renders CRA line numbers and tax amounts.** A screen that displays the
   wrong amount against the wrong CRA line is a filing error with exactly the
   same consequences as a wrong mapping in the engine — the accountant signs off
   on what the screen showed.
2. **It performs taxpayer-domain validation**: it must refuse to render a
   package whose tax year is unsupported, and refuse to render a package
   belonging to another firm.
3. **It handles PII** — the Box 12 SIN, and (subject to BQ-2 below) the
   employee's name and address.
4. **It crosses the firm/case/participant isolation boundary** for the first
   time at the HTTP layer.

It also contains a `platform` component (a project reference change, a new
package reference, authentication middleware, routing). Per the classification
rule, **a feature that mixes both classes is `tax-domain`**, and every
tax-domain protection in this file applies unrelaxed. No platform relaxation is
claimed anywhere in this PRD.

**This is the specification that FR-18 of spec 010 demanded.** It does not
bypass that gate; see [How the FR-18 gate is satisfied](#how-the-fr-18-gate-is-satisfied).

---

## Problem

### Tax-year scope

**Tax year 2025, and only 2025.**

Not "current", not "latest". Every shipped module declares 2025 explicitly and
fails closed on anything else: `SupportedProcessingTaxYears` (spec 008),
`SupportedReviewTaxYears` (spec 009), and the two embedded mapping configs
`TaxLogicEngine.Mappings.T4LineMap.2025.json` and
`TaxLogicEngine.Mappings.T5LineMap.2025.json`. This feature adds **no** new
tax-year policy: it reuses `SupportedReviewTaxYears` and refuses to render
anything outside it. A package carrying tax year 2024 or 2026 is refused
outright — never coerced, never rendered "best effort", never silently mapped to
an adjacent year.

### What is broken today

Spec 009 assembles a complete, SIN-safe, immutable review package and states in
its own user story 10 that its consumer is "the Portal Foundation author". Spec
010 then shipped that host and **deliberately did not consume the package**:
`src/TaxAI.Portal/CLAUDE.md` and root `CLAUDE.md` both record that `CaseReview`
is not referenced because an `AccountantReviewPackage` is taxpayer data.

The result, verified by reading the tree:

1. **`AccountantReviewPackage` has no consumer whatsoever.** Verified: a
   repository-wide grep for `CaseReview` outside `src/CaseReview/`,
   `tests/CaseReview.Tests/`, and spec documents returns nothing. Spec 009's own
   boundary tests assert this as an invariant
   (`CaseReviewBoundaryTests.NoExistingProductionOrTestProjectReferencesCaseReview`).
   The deterministic review boundary is, today, unreachable code from the
   application's point of view.

2. **The pipeline stops one step short of a human.** `ExtractionEnvelope` →
   `ValidatedExtraction` → `ProcessedT4Extraction` → `AccountantReviewPackage`
   is complete and merged, and then nothing. An accountant cannot look at a
   single value the engine produced without writing a test.

3. **The portal has no notion of who is calling.** `Program.cs` maps two
   constant-body GET routes and registers no authentication of any kind. Every
   isolation guarantee in the system — spec 006's firm/year/participant
   isolation, spec 009's single-firm/single-case/single-participant assembly
   rule — is enforced *in memory, on values the caller supplied*. Nothing binds
   an HTTP request to a firm. Spec 010 was safe only because it exposed nothing.

4. **The gate is doing its job and now needs opening properly.** Spec 010's
   FR-18 says no taxpayer-data surface may be added "until the Class A questions
   ... are answered in a separate specification". This is that separate
   specification. Opening the gate carelessly — by relaxing spec 010's
   assertions rather than replacing them with stronger ones — is the single
   biggest risk in this feature, and is addressed explicitly in
   [Amendments](#amendments--obligations-this-prd-invalidates).

### The honest constraint: there is no persistence in this repository

Verified: a repository-wide grep across `src/` for `Repository`, `IRepository`,
`Persist`, `Store`, `DbContext`, `EntityFrameworkCore`, and `SqlConnection`
returns **zero matches**. There is no database, no file store, no cache, no
queue. `AccountantReviewPackage` is an in-memory immutable value with an
`internal` constructor, produced only by `ReviewPackageAssembler.Assemble`.

Therefore **this feature cannot fetch a real package from anywhere**, and this
PRD does not pretend otherwise and does not invent a database. It ships:

- the screen, the authentication, the tenant scoping, the rendering, the
  fail-closed refusals, and the PII protections — all fully specified and fully
  tested;
- an **in-process source port** through which a package is supplied;
- a **default registered implementation that holds nothing and returns nothing**.

The consequence, stated plainly rather than buried: **in any deployment of spec
011 alone, the review screen has no package to show and always renders its
"nothing to display" state.** The rendering paths are exercised against
test-supplied packages in the test project. A production package source is a
separate, later specification, and it is a hard prerequisite for this screen
being useful to a real accountant.

That constraint is also what makes two of spec 010's reserved Class A questions
genuinely non-blocking here — see [Open questions](#open-questions).

---

## Reuse check

Performed by reading `specs/*/prd.md` and the source tree directly on branch
`spec/009-accountant-review-package` (whose spec 009/010 artifacts are merged to
`master`), not from memory and not from a prior PRD treated as a source.

### Reused unchanged — referenced, never re-specified, never duplicated

| Existing artifact | Location | Role in this feature |
|---|---|---|
| `AccountantReviewPackage` | `src/CaseReview/Models/` | The one and only thing the screen renders. `FirmId`, `CaseId`, `ParticipantId`, `TaxYear`, `ReviewedSlips`, `SignOff` are read; nothing is recomputed. |
| `ReviewedSlip` | `src/CaseReview/Models/` | Per-slip read model: `DocumentId`, `TaxYear`, `SourceReference`, `Slip`, `LineEntries`, `BoxAmounts`, `UnmappedBoxCodes`. |
| `ReviewedBoxAmount` | `src/CaseReview/Models/` | `BoxCode` + `Amount`, already produced by `T4SlipBoxAmounts.Enumerate`. |
| `ReviewSignOff`, `SignOffDecision`, `ReviewDecisionDescriptor` | `src/CaseReview/Models/` | `ReviewSignOff` is read for display of existing sign-off state. `ReviewDecisionDescriptor` and `RecordDecision` are **not invoked** — this feature is read-only. |
| `SupportedReviewTaxYears` | `src/CaseReview/Services/` | The single tax-year authority for this screen. No second year policy is declared. |
| `ReviewPackageAssembler` | `src/CaseReview/Services/` | **Not invoked.** Assembly happened upstream; the screen receives an assembled package. |
| `T4Slip`, `TaxLineEntry`, `RecipientIdentifier` | `src/TaxLogicEngine/Models/` | Reached only *through* the package. `RecipientIdentifier.Masked` is the only SIN representation; no `RecipientIdentifier` is ever constructed here. |
| `ModuleRegistration.AddTaxAiModules` | `src/TaxAI.Portal/` | The existing single composition root, extended in place. No second registration file is created. |
| `PortalContent.HealthBody`, `PortalContent.ShellHtml` | `src/TaxAI.Portal/` | Existing `/health` and `/` bodies, unchanged byte-for-byte. |
| `TaxAI.sln`, `src/TaxAI.Portal/`, `tests/TaxAI.Portal.Tests/` | repo root, `src/`, `tests/` | No new project is added by this feature. **No total project count is asserted anywhere.** |

### Deliberately NOT reused

- **`ReviewPackageAssembler.Assemble`** — the screen must not assemble. If it
  did, the portal would become a second place where a review package comes into
  existence, with its own idea of which extractions belong together.
- **`AccountantReviewPackage.RecordDecision`** — recording a sign-off is a
  mutation; scope is read-only (human decision 4).
- **`T4CaseProcessor`** — remains registered (spec 010) and remains uninvoked by
  any endpoint. The screen runs no tax logic.
- **`IDocumentExtractionIngress`** — still not implemented, not registered, not
  resolved. See Open question F.
- **`TaxCases`** — no reference in either direction; this feature computes no
  readiness and transitions no case status.
- **`MoneyRounding`, `T4SlipParser`, `T4LineMapper`, `T4SlipBoxAmounts`** — none
  is called. Every amount and line number rendered was produced upstream.

### Genuinely new (each justified individually)

| New artifact | Why it is not speculative |
|---|---|
| An authenticated review route (`GET /review/{packageId}` `[ASSUMED]`) | The feature itself. Nothing exists today that can display a package. |
| OIDC authentication + a firm-claim principal accessor | Human decision 1 and 2. Without them the screen has no caller identity and no tenant boundary. |
| An internal, firm-scoped review-package source port `[ASSUMED]` | Required because no persistence exists; makes the firm scoping structural (see FR-9) rather than a convention. |
| A default empty implementation of that port `[ASSUMED]` | The fail-closed default. Without a registration the container's validate-on-build would either fail at startup or the endpoint would 500 at request time — neither is a controlled refusal. |
| Server-rendered review view(s) `[ASSUMED]` | The rendering surface. Contains no data-derived logic beyond formatting. |
| Portal test suites for auth, tenant isolation, rendering fidelity, PII, and year isolation | Every AC below needs somewhere to live. |

**No new project, no new module, no placeholder for a future module, and no
empty speculative shell is created.**

---

## Amendments — obligations this PRD invalidates

Spec 010 is merged. A merged PRD is the record of what was agreed, so this
section states out loud what shipped code and shipped tests this feature makes
wrong. **`/plan` must treat every item here as required work, and a human must
approve the gate opening before any of it is written.**

### Spec 010 PRD requirements this feature supersedes

| Spec 010 obligation | Status under spec 011 | Replaced by |
|---|---|---|
| FR-5 / AC-3 — the portal declares **zero** `PackageReference` | **Superseded.** OIDC authentication is not in the shared framework. | FR-2: exactly one new, version-pinned package reference, named and justified. |
| FR-3 / AC-8 / AC-40 — production references are exactly `TaxLogicEngine` + `CaseProcessing` | **Superseded.** | FR-1: exactly `TaxLogicEngine`, `CaseProcessing`, `CaseReview` — still exactly three, still all inward, still asserted exactly. |
| FR-11 / AC-26 — exactly two routes, no route parameter | **Superseded.** | FR-6: exactly three routes; exactly one route parameter, on the review route only, strictly validated. |
| FR-12 / AC-27 — no endpoint accepts any input | **Superseded** for the review route only. | FR-7: the *only* input is the package selector. Every other input channel stays forbidden, including for the review route. |
| FR-15 / AC-31 — no `decimal` token in the portal | **Narrowed, not dropped.** | FR-16: `decimal` may be **carried and rendered**, never operated on. No arithmetic, no rounding, no comparison-derived value. |
| FR-16 / AC-32 / AC-36 — no taxpayer data type named; PII posture is omission | **Superseded.** | FR-14–FR-15: PII posture becomes **masking + minimum disclosure**, with a stricter set of assertions than omission provided. |
| FR-15 / AC-35 — no tax-year literal or policy | **Preserved in substance.** The portal still declares no year policy; it delegates to `SupportedReviewTaxYears`. | FR-12. |
| FR-19 / AC-39 — no authentication or authorization surface | **Superseded.** | FR-3–FR-5. |
| FR-21 / AC-42 — server-rendered, no client framework, no JSON API | **Preserved and reinforced.** | FR-18. |
| FR-18 / AC-44 — the gate | **Narrowed, not removed.** | FR-24: the portal's `CLAUDE.md` gate text is rewritten to a narrower gate that still stops upload, persistence, mutation, and any second taxpayer-data surface. |
| FR-20 — no persistence, HTTP client, background service, or clock | **Preserved** except that authentication requires an outbound metadata fetch to the OIDC provider. Called out in FR-4. |
| FR-13 / AC-13 / AC-28 — `/health` discloses nothing; FR-14 / AC-29 / AC-30 — no diagnostic leakage | **Preserved verbatim and extended** to the review route. | FR-21, FR-23. |

### Shipped test files that must be updated (not deleted)

Read file-by-file. Each of these currently **passes on `master` and will fail**
once this feature ships. Every one must be **replaced by a strictly stronger
assertion, never merely relaxed**; `/plan` must name the replacement for each.

- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
  — `PortalProject_DeclaresNoPackageReference`,
  `TestProject_AddsOnlyTheInProcessWebTestHost`,
  `PortalProject_ReferencesExactlyCaseProcessingAndTaxLogicEngine`,
  `ThePortalsOnlyPublicTypeIsProgram` (must still hold — new portal types stay
  internal), and the `PortalInstructions_*` gate-text assertions.
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
  — `ExpectedProductionFiles` (frozen 3-file list),
  `TheCompiledPortal_ReferencesExactlyCaseProcessingAndTaxLogicEngine`,
  `ThePortalProject_ReferencesNeitherTaxCasesNorCaseReview`,
  `EveryServiceRegistrationLivesInTheCompositionRoot` (asserts exactly 3
  registrations), and the `ForbiddenProductionPatterns` rows for
  `decimal`, taxpayer data types, `TaxYear`, and `Dictionary<`.
- `tests/TaxAI.Portal.Tests/ShellPageTests.cs`
  — `Assert.Equal(2, endpoints.Length)` and the `{ "/", "/health" }` route
  inventory.
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
  — `ExpectedProductionFiles`, `InputBindingPatterns`, `PiiPatterns` (the SIN and
  taxpayer-name rows), `AuthenticationPatterns` (all four rows),
  `NeitherResponseBodyContainsANineDigitRun` (must be **extended** to the review
  route, not weakened), and the `file system` row of `InfrastructurePatterns` if
  Razor views are used.
- `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
  — `TheReadmeMakesNoClaimThePortalCannotBack` (the README must now describe
  sign-in and a review screen), `TheReadmeSaysPlainlyThatNoTaxpayerDataIsRendered`,
  and `TheRootInstructionsRecordTheDeliveredScope`.

### Verified NOT affected (checked by reading, not assumed)

- `tests/CaseReview.Tests/Boundaries/CaseReviewBoundaryTests.cs`
  `NoExistingProductionOrTestProjectReferencesCaseReview` iterates a frozen
  **8-entry** `ExistingProjectFiles` list that predates the portal, guarded by
  `Assert.Equal(8, ExistingProjectFiles.Length)` — a check on the literal's own
  length, not on the repository. `TaxAI.Portal` is outside its scan.
- `tests/CaseReview.Tests/EndToEnd/RepositoryVerificationTests.cs`
  `PreExistingTrees` (8 entries) and `ExpectedProjects` (10 entries) likewise
  predate the portal. `EveryDeclaredSolutionProjectAndProjectReferenceResolves`
  only requires declarations to resolve to real files.
- `tests/CaseReview.Tests/ContractTests.cs` pins CaseReview's exported type list
  exactly. **This is why the source port is declared in the portal and not in
  `CaseReview`** — adding a public type to `CaseReview` would break spec 009's
  contract tests and change a merged module's approved contract.
- `SolutionAndRegressionTests.PublicSurfaceSnapshots` — no module's public
  surface changes, so the snapshots still hold.
- `RepositoryVerificationTests.NoExistingModuleInstructionFileWasRewrittenForTheHost`
  — therefore **no existing module's `CLAUDE.md` or `AGENTS.md` may be edited by
  this feature**, including `src/CaseReview/CLAUDE.md`.

### Shipped code that becomes wrong

None. **No box→line mapping, parser rule, rounding behaviour, or CRA fact is
corrected or changed by this PRD**, so no already-merged task (T0x) of specs
001–009 produces a now-incorrect tax result. The invalidation above is confined
to spec 010's *posture* assertions.

---

## How the FR-18 gate is satisfied

FR-18 requires that no taxpayer-data surface be added "until the Class A
questions in Open questions (A–F) are answered **in a separate specification**".

| Spec 010 question | Disposition here |
|---|---|
| **A — authentication and authorization** | **Answered by the human:** an external OIDC provider; the portal never stores or handles a password (decision 1). Authorization for v1 is "authenticated principal whose firm claim matches the package's firm" and nothing finer. |
| **B — tenant / firm isolation at the HTTP boundary** | **Answered by the human:** the firm id comes from the authenticated principal's claim and is **never** read from the URL, route, query string, header, cookie, or body (decision 2). Partially open: **BQ-1** (claim type and cardinality). |
| **C — what a UI may render, and at what masking level** | **Partially answered:** SIN is masked to the last 3 digits via `RecipientIdentifier`, and no full SIN reaches a browser, log, or error message, ever (decision 3). **Open: BQ-2** — the rest of the package's field inventory. |
| **D — logging, telemetry, error reporting** | **Not blocking, and not assumed.** This PRD does not choose a policy; it *forbids the entire problem class* for v1 (FR-21–FR-23): no taxpayer value, no identity value, no request URL, and no selector may enter any log, metric, trace, or error response, and no telemetry exporter may be registered. That is strictly more restrictive than any policy the human could later write, so deferring can only lead to a later relaxation, never to a leak. Recorded as a non-blocking reserved question. |
| **E — hosting, TLS, exposure, data residency** | **Not blocking for `/plan`; blocking for deployment.** Because there is no persistence and the default source is empty, **no real taxpayer data can enter this process in a spec-011-only deployment.** The PRD therefore neither answers nor approximates E, and adds a hard release constraint (FR-25). |
| **F — whether this host also exposes the untrusted extraction ingress** | **Irrelevant here, and stated as such.** This feature adds no ingress, implements `IDocumentExtractionIngress` nowhere, registers it nowhere, and accepts no uploaded file or request body. F is untouched and remains reserved. |

The gate is opened by a separate `tax-domain` specification with human answers
on record — which is precisely the mechanism FR-18 described. It is not bypassed
and not reinterpreted.

---

## User stories

1. As an **accountant**, I sign in through my firm's identity provider and open
   one review package, so I can check the engine's draft against the physical
   slip before I stake my name on it.
2. As an **accountant**, I see every CRA line entry and every populated box
   amount exactly as the engine produced them, so what I approve and what the
   engine computed cannot differ.
3. As an **accountant**, I see which box codes the engine could not map, because
   spec 009 makes acknowledging them a precondition of approval and I cannot
   acknowledge what I cannot see.
4. As a **taxpayer**, my SIN never appears in full on any screen, in any log, in
   any error message, or in any URL — only the last three digits.
5. As a **taxpayer at firm X**, no principal authenticated at firm Y can reach my
   data, and no URL they can construct changes that.
6. As a **firm principal**, an unauthenticated request gets nothing — not a
   partial page, not a "package not found for firm Z" message that confirms firm
   Z exists.
7. As an **engine maintainer**, the screen computes nothing: no sum, no
   rounding, no box→line decision, and no CRA line literal in any view.
8. As an **engine maintainer**, a package from a tax year other than 2025 is
   refused rather than rendered, so a screen can never be the place where year
   isolation is lost.
9. As an **operator**, `/health` still answers with a fixed literal and still
   requires no credential, so probing liveness neither leaks anything nor needs a
   login.
10. As a **reviewer of this repository**, every assertion spec 010 made about the
    portal is either still true or has been replaced by a strictly stronger one,
    with the replacement named.

---

## Functional requirements

`[reuse]` marks an existing type used as-is. `[new]` marks a genuinely new
artifact. `[gate]` marks a boundary invariant carried forward from root
`CLAUDE.md` or spec 010. `[BLOCKED-BQn]` marks a requirement whose detail
depends on an unanswered Class A question.

### Structure and references

**FR-1 `[gate]`** — The portal's production project references SHALL become
exactly `TaxLogicEngine`, `CaseProcessing`, and `CaseReview` — three, all
pointing inward. `CaseReview` is added **deliberately**: the read model lives
there, and the only alternative is to copy it into the portal, which would create
a second carrier of taxpayer data and a second definition of what a reviewed slip
is. `TaxCases` SHALL NOT be referenced. `DocumentExtraction` SHALL arrive only
transitively and SHALL NOT be named. **No module, and no module's test project,
SHALL reference `TaxAI.Portal` in either direction.**

**FR-2 `[new]`** — The portal MAY declare exactly one new `PackageReference`, for
OpenID Connect authentication, pinned to an explicit 8.0.x version `[ASSUMED]`.
No other package reference SHALL be added to the portal. No `PackageReference`,
`FrameworkReference`, `EmbeddedResource`, or `Content` item SHALL be added to any
pre-existing production project.

**FR-3 `[new]` `[BLOCKED-BQ3]`** — Authentication SHALL be delegated entirely to
an external OIDC provider. The portal SHALL NOT store, accept, transmit, hash,
compare, or handle a password, and SHALL declare no local user store, no
registration surface, and no password-reset surface.

### Identity and tenant isolation

**FR-4 `[new]` `[BLOCKED-BQ1]`** — The firm identifier SHALL be read **only**
from a claim on the authenticated `ClaimsPrincipal`. It SHALL NOT be read from a
route value, query-string value, request body, form value, header, or cookie, and
no code path SHALL exist that accepts a caller-supplied firm identifier.

**FR-5 `[new]`** — The review route SHALL require an authenticated principal. An
unauthenticated request SHALL receive an authentication challenge or a 401 and
SHALL NOT receive any package content, any package existence signal, or any
firm/case/participant/document identifier.

**FR-6 `[new]`** — The portal SHALL expose exactly three routes `[ASSUMED]`:
`GET /` (unchanged constant shell, anonymous), `GET /health` (unchanged constant
literal, anonymous), and `GET /review/{packageId}` (authenticated). `/health`
SHALL remain reachable without a credential so liveness probing never requires
one. No other route SHALL be mapped, including any sign-out, callback-debug, or
diagnostics route beyond what the OIDC handler itself requires.

**FR-7 `[new]`** — The **only** caller-supplied input in this feature is the
package selector `{packageId}`. The portal SHALL NOT read a request body, form
value, uploaded file, cookie value, or any header other than those the
authentication handler consumes. It SHALL declare no `IFormFile`, no
`[FromBody]`, no `[FromQuery]`, and no `[FromForm]`.

**FR-8 `[new]`** — `{packageId}` SHALL be treated as an opaque, untrusted token.
It SHALL be constrained to a fixed safe character set and a bounded maximum
length `[ASSUMED]`, and anything failing that constraint SHALL be refused before
any lookup occurs. It SHALL NOT be, contain, or be derived from a SIN, a name, an
address, a firm identifier, a case identifier, or a participant identifier.

**FR-9 `[new]`** — Package retrieval SHALL go through a single in-process port
whose lookup operation takes the firm identifier as a **required, non-optional
first parameter**, with **no overload, default, or alternate path that omits
it** `[ASSUMED name]`. Firm scoping is thereby structural, not a convention a
future caller can forget. The port SHALL be `internal` so the portal's only
public type remains `Program`.

**FR-10 `[new]`** — The portal SHALL register exactly one implementation of that
port by default: one that holds no data and returns "not found" for every
`(firmId, packageId)` pair `[ASSUMED]`. This is the fail-closed default, not a
placeholder module: without it the container would either fail validate-on-build
or produce an uncontrolled 500 at request time.

**FR-11 `[new]`** — A lookup that returns no package, and a lookup for a package
belonging to another firm, SHALL produce **byte-identical responses** — same
status, same body, same headers. A caller SHALL NOT be able to distinguish "does
not exist" from "exists but is not yours".

### Tax semantics the screen must respect

**FR-12 `[reuse]`** — Before rendering, the portal SHALL verify the package's
`TaxYear` against `SupportedReviewTaxYears.IsSupported`. An unsupported year
SHALL be refused with no package content rendered. The portal SHALL declare no
tax-year literal, no supported-year list, and no "current year" concept of its
own, and SHALL NOT fall back to an adjacent year.

**FR-13 `[gate]`** — The portal SHALL compute no tax and implement no CRA
mapping. It SHALL contain **no CRA line-number literal, no box-code literal, no
box→line table or dictionary, and no mapping-config read**. Every CRA line
number, every box code, and every amount rendered SHALL come from the
already-assembled package and from nowhere else. It SHALL invoke no
`T4SlipParser`, `T5SlipParser`, `T4LineMapper`, `T5LineMapper`,
`T4SlipBoxAmounts`, `T5SlipBoxAmounts`, `MoneyRounding`, or
`ReviewPackageAssembler`, and SHALL read no `TaxLogicEngine.Mappings` resource.

**FR-14 `[gate]`** — The portal SHALL render CRA line numbers, box codes, and
amounts **as opaque values**. It SHALL NOT emit any human-readable description,
label, tooltip, heading, or explanation of what a CRA line or a T4 box *means*.
Such text is a CRA claim, it would require canada.ca verification, and **no such
verification has been performed for this feature** (see [CRA sources](#cra-sources)).
Structural labels that assert no tax fact ("CRA line", "Box", "Amount",
"Unmapped box codes", "Tax year") are permitted.

**FR-15 `[gate]`** — The screen SHALL render the package's collections in the
package's own order and completely: slips in `ReviewedSlips` order, line entries
in `LineEntries` order, box amounts in `BoxAmounts` order, unmapped codes in
`UnmappedBoxCodes` order. It SHALL NOT sort, group, filter, deduplicate,
paginate, truncate, subtotal, or aggregate any of them.

**FR-16 `[gate]`** — `decimal` MAY be **carried and rendered**; it SHALL NOT be
**operated on**. The portal SHALL contain no `+`, `-`, `*`, `/`, or compound
assignment on a monetary value, no `Math.Round`, no `MidpointRounding`, no
`MoneyRounding`, no `double`, and no `float`. Rendering SHALL preserve the value
exactly: no rounding, no truncation, no abbreviation, no thousands-collapsing, no
scientific notation, and no locale-dependent decimal separator `[ASSUMED
invariant formatting]`. A negative amount SHALL render with its sign. A zero
amount SHALL render as an explicit zero, never as a blank cell.

### Rendering, transport, and disclosure

**FR-17 `[new]` `[BLOCKED-BQ2]`** — The set of package fields the screen renders
SHALL be a closed, explicitly enumerated allow-list. Any field not on the list
SHALL NOT reach the response, including in a comment, an HTML attribute, a
`data-` attribute, a hidden input, or a debug block. The contents of that list
are Class A and are **not** decided by this PRD (BQ-2).

**FR-18 `[gate]`** — The screen SHALL be server-rendered with automatic
contextual HTML encoding. It SHALL introduce no SPA framework, no `package.json`,
no npm artifact, no client-side data fetching, and no JSON, XML, or CSV
representation of any package type. **No package, slip, line entry, or box amount
SHALL be serialized to JSON anywhere in the portal.** Raw/unencoded output
helpers SHALL NOT be used.

**FR-19 `[gate]`** — The only SIN representation SHALL be the engine's existing
masked `RecipientIdentifier`, reached through `ReviewedSlip.Slip.Sin` and read via
`Masked`/`ToString()`. The portal SHALL construct no `RecipientIdentifier`,
declare no SIN property or type, and introduce **no second masking, redaction, or
sanitizing helper**. A slip with no SIN SHALL render an explicit "not provided"
state, never a blank that could be mistaken for a value.

**FR-20 `[new]`** — Every review response SHALL carry cache directives that
prevent storage by browsers and intermediaries `[ASSUMED: `no-store, no-cache,
must-revalidate` + `Pragma: no-cache`]`. Taxpayer data SHALL NOT be written to a
browser cache, a proxy cache, or a service worker.

**FR-21 `[new]`** — No log, metric, trace, span, event, or diagnostic sink SHALL
receive: any taxpayer value; any masked or unmasked SIN; any employer or employee
name or address; any box code, line number, or amount; any firm, case,
participant, or document identifier; the `{packageId}` selector; the request URL
or query string; or any claim value. **The portal SHALL register no telemetry
exporter and no HTTP-logging middleware.** Default framework request logging
SHALL be configured so that no request path containing `{packageId}` is emitted
`[ASSUMED]`.

**FR-22 `[new]`** — An unhandled exception SHALL produce a fixed, constant error
response containing no exception type, no message, no stack trace, no filesystem
path, no identifier, and no taxpayer value, in every environment `[ASSUMED: a
constant error page]`. A developer exception page, if present at all, SHALL be
confined to an `IsDevelopment()` branch.

**FR-23 `[gate]`** — `GET /health` and `GET /` SHALL keep their exact spec 010
bodies, byte-for-byte, SHALL remain anonymous, and SHALL continue to disclose no
version, environment name, machine name, user name, path, timestamp, or
configuration value. `/health` SHALL NOT report the review source's status.

**FR-24 `[gate]`** — `src/TaxAI.Portal/CLAUDE.md` SHALL be rewritten to state the
**new, narrower** boundary and gate: this project now renders taxpayer data under
named conditions, and it still may not upload a document, persist data, mutate a
package, compute tax, implement a CRA mapping, or add any second taxpayer-data
surface without a further `tax-domain` specification. The gate SHALL NOT be
deleted, and no existing module's `CLAUDE.md` or `AGENTS.md` SHALL be edited.

**FR-25 `[new]`** — This feature ships a **local development host only**. No
deployment artifact, container image, TLS certificate, reverse-proxy config, or
CI/CD pipeline SHALL be introduced, and no tracked configuration SHALL bind to a
non-local address or contain a secret, connection string, credential, key, token,
or certificate. **This build SHALL NOT be deployed to any environment that can
receive real taxpayer data until Open question E is answered.**

**FR-26** — Every behaviour listed under [Preserved behaviour](#preserved-behaviour)
SHALL remain identical, and the full existing test suite SHALL remain green apart
from the spec 010 posture assertions named in
[Amendments](#amendments--obligations-this-prd-invalidates), each of which SHALL
be replaced by a strictly stronger assertion.

---

## Preserved behaviour

Any deviation is a defect, not a delivered outcome.

1. **T4 and T5 slip parsing** — unchanged.
2. **T4 and T5 box→CRA line mapping**, including spec 004's deliberate
   `UnmappedBoxCodes` deferrals — unchanged.
3. **`Mappings/T4LineMap.2025.json` and `Mappings/T5LineMap.2025.json`** — file
   contents and embedded-resource logical names unchanged.
4. **`MoneyRounding.ToCents`** half-up (`MidpointRounding.AwayFromZero`) —
   unchanged, and not invoked by this feature.
5. **`RecipientIdentifier`** masking to the last 3 digits — unchanged, and no
   second masking path introduced.
6. **`TaxCases`, `DocumentExtraction`, `CaseProcessing`, `CaseReview`** —
   behaviour, public contracts, assembly identities, and namespaces unchanged;
   `IDocumentExtractionIngress` still has no implementation.
7. **Every module's 2025-only year policy and no-adjacent-year-fallback
   behaviour** — unchanged.
8. **`GET /` and `GET /health`** bodies, status codes, content types, and
   anonymous access — unchanged.
9. **Container validation** — validate-on-build and validate-scopes remain on
   unconditionally, outside any `IsDevelopment()` branch.
10. **One composition root.** All registrations stay in
    `ModuleRegistration.cs`; no second registration file appears.
11. **`Program` remains the portal's only public type**; every new portal type is
    `internal`.
12. **The three pre-existing `InternalsVisibleTo` grants** — unchanged, and no
    new grant added.
13. **No total project count** is asserted in any test, doc, or README.
14. **No existing module's source, project file, `CLAUDE.md`, or `AGENTS.md`** is
    edited.

---

## Out of scope

Each item is explicitly excluded. **Any accidental change to tax behaviour is out
of scope and is a defect, not a delivered outcome.**

- **Any change to tax semantics** — no new or changed box, CRA line, mapping,
  rate, threshold, limit, deadline, calculation, or taxpayer-domain validation.
- **Recording a sign-off.** `RecordDecision`, `ReviewDecisionDescriptor`, and the
  unmapped-box acknowledgement gate are not exercised. The screen is read-only
  (human decision 4). Sign-off is the obvious next feature and is `tax-domain`.
- **Editing any value** — no correction of a box amount, a line entry, or a slip
  field. Spec 009 already places this outside its boundary.
- **Persisting, storing, caching, exporting, printing, or emailing a package**,
  and any PDF/CSV/JSON export.
- **A production package source, a database, an ORM, a migration, or a file
  store.** A separate specification, and a hard prerequisite for real use.
- **A package index, client list, search, or firm roster.** A roster of a firm's
  clients is a second taxpayer-data surface with its own PII question; v1 has one
  route that shows one package.
- **Document upload, OCR, LLM, or any `IDocumentExtractionIngress`
  implementation.**
- **Multi-firm session switching, delegation, impersonation, role hierarchies,
  per-case ACLs, or any authorization finer than firm-claim equality.**
- **User management, invitations, provisioning, or firm onboarding.**
- **T5 review, multi-form packages, cross-slip or case-level totals, and any T1
  calculation.**
- **Bilingual / French rendering, localization, accessibility programme, design
  system, component library, theming, and navigation model.**
- **Deployment, hosting environment, TLS termination, reverse proxy, containers,
  CI/CD, monitoring, log shipping, and data residency** — Open question E.
- **Rate limiting, WAF, CSRF tokens** (no state-changing request exists), and any
  security control beyond those named in the FRs.
- **Any new project, and any placeholder project, page, controller, or interface
  for a future module.**
- **Any edit to a merged module's public contract**, including adding a retrieval
  port to `CaseReview`.

---

## Acceptance criteria

Each is a single mechanically-checkable condition. `[BLOCKED-BQn]` marks criteria
whose expected values cannot be fixed until the named Class A question is
answered; they are listed so the shape of the work is visible, not so `/plan` can
guess the values.

### Structure and references

1. **Portal references are exactly three.** Given `src/TaxAI.Portal/TaxAI.Portal.csproj`,
   When its `ProjectReference` items are read, Then they are exactly
   `../TaxLogicEngine/TaxLogicEngine.csproj`, `../CaseProcessing/CaseProcessing.csproj`,
   and `../CaseReview/CaseReview.csproj`.
2. **No forbidden module reference.** Given the compiled portal assembly, When its
   non-framework referenced assemblies are enumerated, Then they are exactly
   `CaseProcessing`, `CaseReview`, and `TaxLogicEngine`, and contain neither
   `TaxCases` nor `DocumentExtraction`.
3. **No reverse arrow.** Given every `.csproj` and hand-written `.cs` file under
   `src/TaxLogicEngine`, `src/TaxCases`, `src/DocumentExtraction`,
   `src/CaseProcessing`, `src/CaseReview` and their five test trees, When each is
   read, Then none contains the string `TaxAI.Portal`.
4. **Exactly one new package reference.** Given the portal project file, When its
   `PackageReference` items are read, Then there is exactly one, it is the OIDC
   authentication package, and it carries an explicit pinned version. `[ASSUMED]`
5. **No pre-existing production project gained an item.** Given the five
   pre-existing `src/*.csproj` files, When compared with `master`, Then none
   gained a `PackageReference`, `FrameworkReference`, `EmbeddedResource`, or
   `Content` item.
6. **No new project.** Given the repository, When all `.csproj` files are listed,
   Then the set is identical to `master`'s. *(No total project count is asserted.)*
7. **`Program` is still the only public portal type.** Given the portal assembly,
   When `GetExportedTypes()` is read, Then it is exactly `["Program"]`.
8. **One composition root.** Given the portal's production source, When scanned
   for `AddSingleton`, `AddScoped`, `AddTransient`, `TryAdd*`, `AddAuthentication`,
   `AddAuthorization`, and `AddRazorPages`, Then every match is in
   `ModuleRegistration.cs`.

### Regression — existing behaviour stays green

9. **Full suite green.** Given the repository after this feature, When
   `dotnet test TaxAI.sln` runs, Then it exits 0, and every test in
   `TaxLogicEngine.Tests`, `TaxCases.Tests`, `DocumentExtraction.Tests`,
   `CaseProcessing.Tests`, and `CaseReview.Tests` passes with none removed,
   skipped, renamed, or weakened.
10. **No tax mapping, calculation, or parser behaviour changed.** Given the five
    modules' parser, mapper, provider, rounding, review-assembly, PII, and
    year-isolation suites, When they run, Then every one passes unchanged, and
    each module's exported type full-name set is identical to its pre-feature set.
11. **Mapping resources untouched.** Given the built `TaxLogicEngine` assembly,
    When its manifest resource names are enumerated, Then
    `TaxLogicEngine.Mappings.T4LineMap.2025.json` and
    `TaxLogicEngine.Mappings.T5LineMap.2025.json` are present with exactly those
    names.
12. **No pre-existing module file changed.** Given `git diff` against the merge
    base, When the changed-file list is read, Then it contains no path under
    `src/TaxLogicEngine/`, `src/TaxCases/`, `src/DocumentExtraction/`,
    `src/CaseProcessing/`, `src/CaseReview/`, or their five test trees.
13. **No new internals grant.** Given every production project file and
    production source file, When scanned for `InternalsVisibleTo`, Then exactly
    the three pre-existing grants are found.
14. **Every superseded spec 010 assertion has a named replacement.** Given the
    Amendments table, When each superseded spec 010 AC is looked up in the portal
    test suite, Then a replacement test exists that asserts a strictly narrower
    condition, and no superseded assertion was deleted without one.

### Health and shell unchanged

15. **Health body unchanged and anonymous.** Given the started host with no
    credential, When `GET /health` is requested, Then the status is 200 and the
    body equals `Healthy` exactly.
16. **Shell body unchanged and anonymous.** Given the started host with no
    credential, When `GET /` is requested, Then the status is 200, the content
    type is HTML, and the body equals the spec 010 shell document byte-for-byte.
17. **Health discloses nothing.** Given the health response body, When inspected,
    Then it contains no digit, no environment name, no machine or user name, no
    path, no version, and no review-source status.
18. **Exactly three routes.** Given the started host's endpoint data sources, When
    route endpoints are enumerated, Then the application's own routes are exactly
    `/`, `/health`, and `/review/{packageId}`, all GET, and exactly one route
    parameter exists across all three. `[ASSUMED paths]`

### Authentication and tenant isolation — happy path

19. **Happy path.** Given an authenticated principal with firm claim `F`, and a
    source holding a 2025 package with `FirmId == F` at selector `P`, When
    `GET /review/P` is requested, Then the status is 200 and the response
    contains every allow-listed field of that package. `[BLOCKED-BQ2]`
20. **Firm claim is the only firm source.** Given the portal's production source,
    When scanned, Then no code path reads a firm identifier from `RouteData`,
    `Request.Query`, `Request.Form`, `Request.Body`, `Request.Headers`, or
    `Request.Cookies`.
21. **The lookup contract is firm-scoped by signature.** Given the source port
    type, When its methods are read by reflection, Then every lookup method
    declares a required firm-identifier parameter and no overload omits it.

### Authentication and tenant isolation — fail-closed (one AC per rule)

22. **Unauthenticated request.** Given no credential, When `GET /review/P` is
    requested, Then the status is 401 or a challenge redirect, and the response
    body contains no package content and no identifier.
23. **Authenticated principal with no firm claim.** Given an authenticated
    principal carrying no firm claim, When `GET /review/P` is requested, Then the
    request is refused with no package content. `[BLOCKED-BQ1]`
24. **Firm claim mismatch is indistinguishable from not-found.** Given a package
    with `FirmId == F1` at selector `P` and a principal with firm claim `F2`,
    When `GET /review/P` is requested, Then the status, body, and headers are
    byte-identical to the response for a selector that exists nowhere.
25. **Unknown selector.** Given a source holding no package at selector `Q`, When
    `GET /review/Q` is requested by an authenticated principal, Then the response
    is the fixed not-found response and contains no identifier.
26. **Foreign firm id in the query string is ignored.** Given a principal with
    firm claim `F1` and a package owned by `F2`, When
    `GET /review/P?firmId=F2` is requested, Then the response is byte-identical
    to `GET /review/P` without the query string.
27. **Foreign firm id in a header is ignored.** Given the same setup, When
    `GET /review/P` is requested with a header naming firm `F2`, Then the
    response is byte-identical to the request without that header.
28. **Foreign firm id in a cookie is ignored.** Given the same setup, When
    `GET /review/P` is requested with a cookie naming firm `F2`, Then the
    response is byte-identical to the request without that cookie.
29. **Malformed selector.** Given an authenticated principal, When `GET /review/`
    is requested with a selector violating the FR-8 character set or exceeding
    the maximum length, Then the request is refused before any lookup runs and no
    package content is returned. `[ASSUMED constraint]`
30. **Non-GET verb.** Given an authenticated principal, When `POST`, `PUT`,
    `PATCH`, or `DELETE` is sent to `/review/P`, Then the status is 405 and the
    body contains no package content.
31. **Source failure fails closed.** Given a source that throws, When
    `GET /review/P` is requested, Then the response is the fixed FR-22 error
    response, is not a partially-rendered page, and contains no exception
    detail, no identifier, and no taxpayer value.
32. **Source returning null fails closed.** Given a source that returns null,
    When `GET /review/P` is requested, Then the response is the fixed not-found
    response and no null-reference exception surfaces.
33. **Default registration is empty.** Given the started host with no test
    substitution, When the registered source is asked for any `(firmId,
    packageId)` pair, Then it returns not-found and holds no package.

### Tax-year isolation

34. **Year N is never rendered for N±1.** Given a package whose `TaxYear` is 2024,
    When `GET /review/P` is requested by its own firm's principal, Then the
    request is refused and no slip, amount, or line entry is rendered.
35. **Forward year is refused too.** Given a package whose `TaxYear` is 2026, When
    the same request is made, Then the request is refused identically — no
    adjacent-year fallback, no coercion, no "closest supported year".
36. **The supported year comes from `SupportedReviewTaxYears`.** Given the
    portal's production source, When scanned, Then it contains no four-digit
    year literal and no supported-year list of its own, and the only year check
    delegates to `SupportedReviewTaxYears`.

### Rendering fidelity — no tax computation

37. **Data-driven mapping: no hardcoded CRA line.** Given the portal's production
    source and views (executable lines only), When scanned for a five-digit
    integer literal, Then there are zero matches; and every CRA line number in a
    rendered response also appears in the supplied package's `LineEntries`.
38. **No box→line authority.** Given the portal's production source and views,
    When scanned, Then there is no box→line dictionary or lookup table, no box-code
    literal, no `TaxLogicEngine.Mappings` resource name, and no
    `GetManifestResourceStream`.
39. **No engine invocation.** Given the portal's production source, When scanned,
    Then it names none of `T4SlipParser`, `T5SlipParser`, `T4LineMapper`,
    `T5LineMapper`, `T4SlipBoxAmounts`, `T5SlipBoxAmounts`, `MoneyRounding`,
    `ReviewPackageAssembler`, or `RecordDecision`.
40. **No arithmetic.** Given the portal's production source and views, When
    scanned, Then they contain no `Math.Round`, no `MidpointRounding`, no
    `double`, no `float`, and no arithmetic or compound-assignment operator
    applied to a monetary value.
41. **Amounts are rendered exactly — positive half-cent.** Given a package
    carrying a box amount of `1.005m`, When the page is rendered, Then the
    response contains `1.005` and contains neither `1.01` nor `1.00`. *(The
    engine rounds half-up away from zero via `MoneyRounding.ToCents`; the portal
    must be provably incapable of rounding at all.)*
42. **Amounts are rendered exactly — negative half-cent.** Given a package
    carrying a box amount of `-1.005m`, When the page is rendered, Then the
    response contains `-1.005`, retains the minus sign, and contains neither
    `-1.01` nor `-1.00`.
43. **Zero renders as zero.** Given a line entry with amount `0m`, When rendered,
    Then the response shows an explicit zero for it, not an empty cell.
44. **No locale mangling.** Given a package with amount `1234.56m` and the host
    running under a comma-decimal culture, When rendered, Then the response
    contains `1234.56` and does not contain `1234,56`.
45. **Order preserved.** Given a package with three slips, each with several line
    entries, box amounts, and unmapped codes in a known order, When rendered,
    Then each collection appears in exactly the package's order, with no item
    added, removed, reordered, or merged.
46. **No aggregation.** Given a package with two slips whose amounts map to the
    same CRA line, When rendered, Then two separate entries appear and no
    combined total appears anywhere in the response.
47. **Unmapped codes are shown.** Given a slip with a non-empty
    `UnmappedBoxCodes`, When rendered, Then every code appears verbatim; and
    given an empty `UnmappedBoxCodes`, an explicit "none" state appears rather
    than a missing section.
48. **Empty package state.** Given a package with zero reviewed slips, When
    rendered, Then an explicit empty state appears and the page does not read as
    a successful, complete review.
49. **No CRA meaning is asserted.** Given the rendered response and all view
    files, When scanned, Then no descriptive text for a CRA line number or T4 box
    code appears beyond the structural labels permitted by FR-14.
50. **No serialization of package types.** Given the portal's production source
    and views, When scanned, Then no package, slip, line entry, or box amount is
    passed to a JSON, XML, or CSV serializer, and no JSON endpoint exists.

### PII and SIN

51. **Masked SIN only.** Given a slip whose Box 12 SIN is `123456789`, When the
    page is rendered, Then the response contains `***-**-789` and does **not**
    contain `123456789`.
52. **No nine-digit run anywhere in a response.** Given the rendered review page,
    the not-found response, the refusal responses, and the error response, When
    each body is scanned, Then none contains any nine-consecutive-digit
    substring.
53. **No SIN in an exception.** Given a package with a SIN and a deliberately
    failing render, When the exception and its full inner chain are flattened to
    text, Then no full SIN and no nine-digit run appears.
54. **No SIN in a log.** Given a captured in-memory logger across a full
    successful review request, When every log line is scanned, Then none contains
    a full SIN, a masked SIN, a name, an address, an amount, a box code, a line
    number, an identifier, or the selector.
55. **No second masking helper.** Given the portal's production source, When
    scanned, Then it constructs no `RecipientIdentifier` and declares no masking,
    redaction, sanitizing, or scrubbing helper of its own.
56. **Absent SIN.** Given a slip whose `Sin` is null, When rendered, Then an
    explicit "not provided" state appears and no blank field could be mistaken
    for a value.
57. **Nothing outside the allow-list reaches the wire.** Given a package whose
    every string field is a unique sentinel value, When the response is scanned,
    Then exactly the allow-listed sentinels appear and every other sentinel is
    absent — including from comments, attributes, and hidden elements.
    `[BLOCKED-BQ2]`
58. **Injection is encoded.** Given an employee name of
    `<script>alert(1)</script>` (or another allow-listed string field, per BQ-2),
    When rendered, Then the response contains the HTML-encoded form and no
    executable `<script>` element. `[BLOCKED-BQ2]`
59. **No taxpayer data in the URL.** Given a successful review request, When the
    request URI is inspected, Then it contains no name, address, amount, box
    code, line number, SIN, firm id, case id, or participant id.
60. **No taxpayer data in a cookie.** Given a successful review request, When
    every `Set-Cookie` header is inspected, Then none contains a package field
    value, an identifier, or a nine-digit run.

### Transport, caching, and diagnostics

61. **No-store on review responses.** Given a successful review response, When its
    headers are read, Then `Cache-Control` prevents storage by browsers and
    intermediaries. `[ASSUMED directives]`
62. **Health and shell are not gated.** Given no credential, When `GET /health`
    and `GET /` are requested, Then both return 200 without a challenge.
63. **Errors leak nothing, in every environment.** Given the host started as
    Production and again as Staging, When an unhandled exception occurs on the
    review route, Then the response contains no exception type, message, stack
    trace, filesystem path, identifier, or taxpayer value.
64. **Developer exception page confined.** Given the portal's production source,
    When scanned, Then any developer-exception-page call sits inside an
    `IsDevelopment()` branch, or is absent.
65. **No telemetry or HTTP logging.** Given the portal's source and configuration,
    When scanned, Then there is no `AddHttpLogging`, `UseHttpLogging`,
    `AddW3CLogging`, Application Insights, or OpenTelemetry registration.
66. **No secret, no non-local binding.** Given every tracked configuration and
    launch-profile file under the portal, When read, Then none contains a
    connection string, password, key, token, or certificate, and every declared
    URL binds to localhost.
67. **No persistence or infrastructure.** Given the portal's production source and
    its compiled assembly, When inspected, Then there is no `DbContext`,
    `SqlConnection`, `EntityFrameworkCore`, file-store write, message queue,
    background service, or system-clock read outside what the authentication
    handler requires.
68. **No client-side stack.** Given the repository, When listed, Then there is no
    `package.json`, no `node_modules`, no SPA framework asset, and no tracked
    minified bundle.

### Documentation

69. **Portal instructions carry the narrowed gate.** Given
    `src/TaxAI.Portal/CLAUDE.md`, When read, Then it states the three-reference
    set, that the UI computes no tax and implements no CRA mapping, that the
    portal holds no tax-year policy of its own, the masked-SIN-only rule, the
    firm-claim-only rule, and the remaining gate (no upload, no persistence, no
    mutation, no second taxpayer-data surface without a further `tax-domain`
    spec).
70. **No existing module instruction file changed.** Given `git diff` against the
    merge base, When the changed-file list is read, Then no `CLAUDE.md` or
    `AGENTS.md` under any of the five module trees appears in it.
71. **README describes what exists.** Given `README.md`, When read, Then it
    describes sign-in and the read-only review screen, states that no package
    source ships and the screen therefore shows nothing without one, asserts no
    project count, and claims no capability the build lacks.
72. **Root instructions record the change.** Given root `CLAUDE.md`, When read,
    Then its current-state section records spec 011's delivered scope, the
    portal's new three-reference set, and the narrowed gate, and it still
    contains "Never assert a total project count".

---

## Open questions

### Class A — BLOCKING. Nothing below is assumed; `/plan` must not proceed without answers.

**BQ-1 — The firm claim: which claim type, and how many?**
Human decision 2 fixes that the firm id comes from the authenticated principal's
claim. It does not say **which claim type carries it**, nor **whether a principal
may legitimately hold more than one**.

- (a) What is the claim type/URI that carries the firm identifier?
- (b) May one authenticated principal carry more than one firm claim — i.e. may
  an accountant act for multiple firms? If yes, how is the active firm chosen for
  a request, given decision 2 forbids reading it from the URL, query, body,
  header, or cookie?
- (c) Is the claim value the same string that appears as `AccountantReviewPackage.FirmId`,
  compared ordinally and case-sensitively — or is there a normalization or
  mapping step?

*Why blocking:* (b) determines whether a firm-selection surface exists at all,
which changes the route set and the session model. A wrong guess is cross-tenant
exposure of taxpayer data. This is tenant isolation — Class A in both classes.
*(Zero claims → deny is not asked: fail-closed is already mandated.)*

**BQ-2 — What of the package may be rendered?**
Human decision 3 covers the SIN. It does not cover anything else the package
carries. Please give an allow / deny for each — this is the FR-17 list:

| Field | Source | Renderable? |
|---|---|---|
| `T4Slip.EmployeeName` | slip | ? |
| `T4Slip.EmployeeAddress` | slip | ? |
| `T4Slip.EmployerName` | slip | ? |
| `T4Slip.EmployerAddress` | slip | ? |
| `T4Slip.EmployerAccountNumber` (Box 54) | slip | ? |
| `T4Slip.RppDpspRegistrationNumber` (Box 50) | slip | ? |
| `T4Slip.ProvinceOfEmployment` (Box 10) | slip | ? |
| `T4Slip.EmploymentCode` (Box 29) / `DentalBenefitCode` (Box 45) | slip | ? |
| `T4Slip.Sin` | slip | **masked only — answered (decision 3)** |
| `ReviewedBoxAmount.BoxCode` + `.Amount` | package | ? |
| `TaxLineEntry.LineNumber`, `.Amount`, `.SourceBoxCodes` | package | ? |
| `ReviewedSlip.UnmappedBoxCodes` | package | ? |
| `ReviewedSlip.DocumentId`, `.SourceReference` | package | ? |
| `AccountantReviewPackage.FirmId`, `.CaseId`, `.ParticipantId` | package | ? |
| `ReviewSignOff.Decision`, `.ReviewerReference`, `.DecidedAtReference`, `.Note` | package | ? |

*Why blocking:* it is a PII decision — Class A — and there is no safe default in
either direction. Rendering less risks the accountant approving amounts without
knowing **whose** slip they are, which is itself a filing-error vector.
Rendering more exposes a taxpayer's name and home address in a browser. Note
that spec 009 marked every one of these `[JsonIgnore]`, "readable only in
memory" — putting them on a wire crosses a posture the merged module chose
deliberately, and only the human may authorize that.

**BQ-3 — How is the authenticated session carried, and what may it hold?**
Decision 1 fixes that the portal never stores or handles a password. It says
nothing about what happens after the OIDC exchange.

- (a) Is the session carried by an encrypted cookie, or by something else?
- (b) May the OIDC id token or access token be retained in the session after
  sign-in, or must both be discarded once the principal is established?
- (c) Where may the data-protection key material used to encrypt that cookie
  live? (In-memory/ephemeral means sessions die on restart; anything else means
  key material at rest, which is a data-residency question.)
- (d) What is the maximum session lifetime and idle timeout for a session that
  can reach taxpayer data?

*Why blocking:* this is a security boundary, and key material at rest touches
Open question E. The most conservative posture (ephemeral in-memory keys, no
token retained, short lifetime) is **not** assumed here, because "security
boundaries stay Class A — never assume".

### Class A — reserved, judged NOT blocking (with the reasoning, so the judgement can be overruled)

- **D — logging, telemetry, and error-reporting policy.** Judged non-blocking
  **because this PRD does not defer the risk, it forbids the class**: FR-21 bans
  every taxpayer value, identity value, identifier, selector, and request URL
  from every log, metric, trace, and error response; FR-22 makes the error
  response a constant; FR-65 forbids telemetry exporters and HTTP logging. That
  is strictly tighter than any policy you would write, so the deferral can only
  be relaxed later, never breached now. **Overrule this if you want the policy
  settled before code exists.**
- **E — hosting, deployment, TLS, public exposure, data residency.** Not
  answered and not approximated here — it is a legal question. Judged
  non-blocking **for `/plan`** because with no persistence and an empty default
  source, no real taxpayer data can enter a spec-011-only deployment. It is
  **blocking for deployment**, and FR-25 states that as a hard release
  constraint.
- **F — whether this host also exposes the untrusted document-extraction
  ingress.** Irrelevant to this feature and confirmed so: v1 implements, registers,
  and resolves no `IDocumentExtractionIngress`, accepts no request body and no
  uploaded file, and adds no input surface beyond the package selector. F remains
  reserved and untouched.
- **Future — the production package source.** Where a package comes from, its
  audit guarantees, and its retention are Class A and belong to that separate
  spec. Note the known consequence of FR-9: the source port is `internal` to the
  portal, so when a real adapter ships, the port must move to a contract module
  that both the portal and the adapter can depend on (a module may never
  reference `TaxAI.Portal`). That relocation is expected work, recorded here so
  it is not discovered late.

### `[ASSUMED]` — Class B decisions, made and recorded for one-pass override

- `[ASSUMED]` **Spec folder and slug** are `specs/011-accountant-review-screen/`;
  011 confirmed free by listing `specs/` (001–010 exist).
- `[ASSUMED]` **v1 scope** is: one authenticated read-only route showing one
  package, the OIDC sign-in, the firm-scoped source port with an empty default
  implementation, and the documentation updates in FR-24 — and nothing else.
- `[ASSUMED]` **No package index, list, or search route.** A roster of a firm's
  clients is a second taxpayer-data surface with its own PII question, and with
  no persistence there is nothing to enumerate. Selection is by supplying the
  package selector, which satisfies "the only input is selecting which package to
  view".
- `[ASSUMED]` **Route shape** is `GET /review/{packageId}`; `/` and `/health`
  keep their exact spec 010 paths and bodies.
- `[ASSUMED]` **Selector constraint** is a bounded-length token restricted to a
  fixed safe character set (letters, digits, hyphen), refused before any lookup.
  The exact length and charset are `/plan`'s to fix, but they must be fixed.
- `[ASSUMED]` **Source port name and shape** — an `internal` interface, working
  name `IReviewPackageSource`, with a single lookup taking the firm identifier as
  its required first parameter and returning the package or null. Names are
  `/plan`'s to finalize; the required-firm-parameter property is not.
- `[ASSUMED]` **Default implementation** — an `internal` empty source, working
  name `EmptyReviewPackageSource`, returning not-found for every pair, registered
  as a singleton in `ModuleRegistration.cs`.
- `[ASSUMED]` **Rendering technology** — Razor Pages or Razor views under the
  existing web SDK, with automatic contextual HTML encoding and no raw-output
  helper. No SPA, no JSON API, no npm. `/plan` may pick the precise mechanism but
  not a client-side one.
- `[ASSUMED]` **Number formatting** — invariant culture, value-preserving,
  never rounding or truncating, sign retained, no thousands separator collapsing
  and no scientific notation.
- `[ASSUMED]` **Structural labels only** — "Tax year", "Slip", "Box", "Amount",
  "CRA line", "Source boxes", "Unmapped box codes", "SIN (masked)", "Sign-off".
  No CRA-derived descriptive text (FR-14).
- `[ASSUMED]` **Refusal responses** — a single fixed not-found response used for
  unknown selector, foreign firm, and unsupported tax year, so none of the three
  is distinguishable from the others; and a single fixed error response for
  every unhandled failure.
- `[ASSUMED]` **Cache directives** — `Cache-Control: no-store, no-cache,
  must-revalidate` plus `Pragma: no-cache` on the review route.
- `[ASSUMED]` **OIDC package** — `Microsoft.AspNetCore.Authentication.OpenIdConnect`
  pinned to 8.0.x, the portal's only `PackageReference`. Contingent on BQ-3(a).
- `[ASSUMED]` **Test approach** — the existing in-process `WebApplicationFactory`
  host with a test-only authentication handler that mints a principal with a
  chosen firm claim, and a test-only substitute for the source port. Neither may
  appear in production code.
- `[ASSUMED]` **Documentation set** — rewrite `src/TaxAI.Portal/CLAUDE.md`;
  update `README.md`, root `CLAUDE.md`, and `NEXT.md`; record
  `specs/011-accountant-review-screen/verification.md` AC-by-AC, following specs
  005–010. **No existing module's instruction file is touched.**
- `[ASSUMED]` **Wave ordering** — W0 contract (port, registration, route
  signatures, view model shape with `NotImplementedException` bodies); W1
  authentication + firm-claim accessor, and the source port default; W2
  rendering and refusals; W3 PII, year-isolation, boundary, and spec-010
  replacement suites; W4 documentation and verification record. Each wave green
  before the next.
- `[ASSUMED]` **No new project, no `Directory.Build.props`, no `global.json`, no
  central package management, no analyzer package, no CI workflow, no
  Dockerfile.**

**No Class A assumption is made anywhere in this PRD.** Every tax-semantic, PII,
tenant-isolation, and security-boundary question is either answered by the human
on record, forbidden outright in a strictly conservative direction, or listed
above as blocking.

---

## CRA sources

**This feature asserts no CRA fact, and therefore cites none.** No box number,
line number, filing deadline, rate, threshold, limit, gross-up factor, or credit
percentage enters this PRD.

That is a claim about scope, and it is enforced rather than promised:

- Every CRA line number rendered originates from
  `TaxLogicEngine.Mappings.T4LineMap.2025.json`, whose facts were verified
  against canada.ca in specs 003 and 004 and are unchanged here (AC-11, AC-37).
- Every box code and every amount rendered originates from `T4SlipParser` and
  `T4SlipBoxAmounts.Enumerate` via the assembled package, not from this feature
  (AC-37, AC-38, AC-39).
- **FR-14 forbids the UI from emitting any human-readable description of what a
  CRA line or T4 box means**, precisely because such text *would* be a CRA claim
  requiring canada.ca verification — and no such verification has been performed
  for this feature (AC-49).
- The only tax-year statement is "2025", which is not sourced from canada.ca here
  but delegated to the merged `SupportedReviewTaxYears` (AC-36).

If a future revision of this PRD adds a CRA line description, a form title, a
deadline, or any rate, it must first verify it against canada.ca and record it in
this table:

| Claim | Source URL | Tax year | Retrieved |
|---|---|---|---|
| *(none — this feature introduces no tax fact)* | — | — | — |

---

## Definition of Done — self-check

**Both classes:**

- [x] `specs/011-accountant-review-screen/prd.md` exists with the required
      structure, opening with `## Feature classification` (Problem / User stories
      / Functional requirements / Out of scope / Acceptance criteria / Open
      questions / CRA sources)
- [x] Feature class named explicitly (`tax-domain`) and justified; mixed features
      classified `tax-domain`
- [x] Reuse check performed and documented — `specs/*/prd.md` and the source tree
      read directly; `CaseReview`, `CaseProcessing`, and engine types referenced,
      none re-specified or duplicated; the portal and CaseReview test suites read
      file-by-file to determine exactly which shipped assertions this feature
      invalidates
- [x] Every AC is mechanically checkable by a single test
- [x] Zero Class A assumptions made — the three unresolved ones are raised, not
      guessed

**`tax-domain`, additionally:**

- [x] Every CRA fact has URL + tax year + retrieval date, all from canada.ca —
      **vacuously satisfied and enforced: this feature asserts no CRA fact**, and
      FR-14 + AC-49 prevent one from being introduced by view text
- [x] No source conflict was resolved by this agent's judgement (no source was
      consulted, because no CRA claim is made)
- [x] Tax year named explicitly in Problem — **2025 only**
- [x] Every fail-closed rule has its own negative AC — AC-22 (unauthenticated),
      AC-23 (no firm claim), AC-24 (firm mismatch), AC-25 (unknown selector),
      AC-26/27/28 (caller-supplied firm id in query/header/cookie), AC-29
      (malformed selector), AC-30 (non-GET verb), AC-31 (source throws), AC-32
      (source returns null), AC-34/35 (unsupported tax year, both directions)
- [x] PII acceptance criteria present — AC-51 to AC-60
- [x] Tax-year isolation AC present — AC-34, AC-35, AC-36

**BLOCKED items:**

- [ ] **Zero unanswered Class A questions.** Three remain: **BQ-1** (firm claim
      type and cardinality — tenant isolation), **BQ-2** (which package fields
      may be rendered — PII), **BQ-3** (session carriage, token retention, key
      material, lifetime — security boundary). AC-19, AC-23, AC-57, and AC-58
      cannot have their expected values fixed until they are answered.

**Result: NOT ready for `/plan`.** Answer BQ-1, BQ-2, and BQ-3, and confirm the
gate opening recorded in [Amendments](#amendments--obligations-this-prd-invalidates);
this PRD is then complete without further rework.
