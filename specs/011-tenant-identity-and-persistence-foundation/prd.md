# PRD 011 — Tenant Identity and Persistence Foundation

> **STATUS: COMPLETE. Ready for `/plan`.**
> Every Class A question is answered by the human and recorded in
> [Class A decisions on record](#class-a-decisions-on-record-given-by-the-human-not-re-asked)
> and [Open questions](#open-questions). **No Class A decision is assumed
> anywhere in this document.**

---

## Feature classification

**Class: `tax-domain`.**

This feature writes taxpayer data to a database and decides how long it stays
there. It is `tax-domain` for five independent reasons, any one of which would be
sufficient:

1. **It persists taxpayer-domain data at rest** — T4 box codes, engine-produced
   box amounts, CRA line codes, mapped CRA amounts, unmapped box codes, and a
   taxpayer display name — in a durable store that outlives the process.
2. **It carries a tax-year-scoped retention rule.** `RetentionUntilUtc` is
   derived from the record's `TaxYear`. A record deleted early is a destroyed
   taxpayer record; a record kept past its policy is an over-retention. Either is
   a domain error, not a technical one.
3. **It performs taxpayer-domain validation**: it refuses to persist a package
   whose tax year is unsupported, refuses to persist a package whose declared
   firm differs from the trusted firm, and refuses to persist any value it cannot
   store without altering it.
4. **It decides tenant isolation for taxpayer data** for the first time — at the
   HTTP boundary, in the session model, in the claim model, and in every table,
   index, and query.
5. **It changes the repository's PII posture.** It implements the human's
   reversal of the earlier "masked last three" answer: **no SIN in any form
   reaches the browser or the persisted read model.**

It also contains a large `platform` component (two new class libraries, EF Core,
migrations, new project references, authentication middleware, routing). Per the
classification rule, **a feature that mixes both classes is `tax-domain`**, and
every tax-domain protection in this file applies unrelaxed. **No platform
relaxation is claimed anywhere in this PRD**: the CRA-source rule, the explicit
tax-year requirement, the one-negative-AC-per-fail-closed-rule requirement, the
PII rules, and the tax-year-isolation requirement all bind in full.

**This is (part of) the specification that spec 010's FR-18 demanded.** It does
not bypass that gate; see
[How the FR-18 gate is satisfied](#how-the-fr-18-gate-is-satisfied).

---

## Problem

### Tax-year scope

**Tax year 2025, and only 2025.**

Not "current", not "latest", not "whatever the package says". This feature
declares **no tax-year policy of its own**. It reuses the merged, human-approved
`CaseReview.Services.SupportedReviewTaxYears` (`All = [2025]`) as the single
authority for which tax years may be persisted, and it fails closed on anything
else. A package carrying tax year 2024 or 2026 is **refused outright** — never
coerced, never stored "for later", never silently mapped to an adjacent year, and
never persisted with a corrected year.

`RetentionUntilUtc` is computed from the record's own explicit `TaxYear`, never
from a system clock, never from "the current year", and never from an adjacent
year's rule.

The seven-year retention period is an **engineering product default set by the
human**. It is **explicitly not a statement of any CRA statutory minimum**, it is
not sourced from canada.ca, and nothing in this PRD presents it as a CRA fact.

### What is broken today

Verified by reading the tree at `D:\Projects\TaxAI` on `master`, not from memory
and not from a prior PRD treated as a source:

1. **Nothing in this repository can remember anything.** A repository-wide grep
   across `src/` for `DbContext`, `DbSet`, `EntityFrameworkCore`,
   `SqlConnection`, `System.Data`, `IRepository`, and `Persist` returns **zero
   matches in production code**; the only occurrences are in boundary tests that
   scan for those tokens as *forbidden* patterns. Every artefact the pipeline
   produces — `ValidatedExtraction`, `ProcessedT4Extraction`,
   `AccountantReviewPackage` — exists only for the lifetime of one in-memory
   call and is then garbage.

2. **The portal has no notion of who is calling.** `src/TaxAI.Portal/Program.cs`
   maps two constant-body GET routes and registers no authentication of any
   kind. `PortalSecurityTests.AuthenticationPatterns` asserts, as a shipped
   invariant, that the portal names no `AddAuthentication`, no
   `ClaimsPrincipal`, and no session or cookie state.

3. **Every isolation guarantee in the system is enforced on values the caller
   supplied.** Spec 006 isolates firm/year/participant *in memory*; spec 009
   assembles one package for one firm/case/participant *from strings the caller
   passed in*; specs 007, 008, and 009 each state explicitly that they verify no
   membership and no authorization and that attribution is carried forward, not
   checked. There is no trusted firm identity anywhere in the repository, so
   there is nothing for a repository query to be scoped by.

4. **Spec 012 (Accountant Review Screen) is blocked on exactly this.** Its own
   Problem section records that "there is no persistence in this repository", so
   it ships a port with an empty default implementation and a screen that always
   renders "nothing to display". Its BQ-1 (firm claim type and cardinality) and
   BQ-3 (session carriage, token retention, key material, lifetime) are precisely
   the questions this feature answers. **Its BQ-2 field inventory is now partly
   superseded: SIN is not renderable in any form, masked included.**

5. **A retention rule cannot exist without a store.** Real returns generate real
   records with real retention obligations. Today there is no record, so there is
   no retention, no purge, and no way to prove either.

### What this feature deliberately is not

It is **not** the review screen. It renders **no taxpayer data**. It adds no
endpoint that displays a slip, a box amount, a CRA line, a taxpayer name, or a
package of any kind. It is the tenant identity, the session, the storage, and the
retention that a review screen will later stand on — specified now, so the screen
spec does not have to invent a tenancy model under deadline.

---

## Class A decisions on record (given by the human; not re-asked)

Recorded so `/plan` and the reviewer can see exactly what was decided by a human
and what was derived from it. **Every question this PRD raised has been
answered**; the answers are folded into the requirements below and the reasoning
is preserved in [Open questions](#open-questions).

### UTC `DateTimeOffset` provenance (Class A clarification)

**This clarification supersedes the ambiguous earlier wording in FR-34 and
AC-154.** `UtcInstantGuard` accepts a `DateTimeOffset` only when its `Offset` is
exactly `TimeSpan.Zero`; it rejects every non-zero offset and never silently
normalizes one. The guard does not attempt to infer the originating
`DateTime.Kind`. A `DateTimeOffset` does not retain whether an equal zero-offset
value originated from `DateTimeKind.Utc` or `DateTimeKind.Unspecified`, so
AC-154 does not require rejecting a zero-offset value based on unavailable
`DateTime.Kind` provenance.

A future API that accepts `DateTime` directly may reject
`DateTimeKind.Unspecified` before constructing a `DateTimeOffset`. Such an API
is outside T03. T03's current public signature remains unchanged.

### Wave 4 construction seams (human-directed corrective clarification)

After T14 merged, implementation of T12, T13, and T15 exposed three
contradictions between the approved module boundary and the Wave 0 contract
shapes. The following decisions are normative and T11A must land before any of
those three tasks resumes:

1. **The review repository accepts only the approved Tenancy persistence
   model.** `ReviewPackageProjection.Project` converts
   `AccountantReviewPackage` to `PersistedReviewRecord` before the value crosses
   into `Persistence`. `IReviewRecordRepository.SaveAsync` accepts the trusted
   firm plus that projected record; it does not accept, return, serialize, or
   reference `AccountantReviewPackage` or another executable `CaseReview` type.
   The merged Persistence boundary that forbids executable `CaseReview`
   references remains authoritative.
2. **Authentication-session creation is an invariant-preserving Tenancy
   operation.** `AuthenticationSession.Create` accepts non-empty opaque session
   and user identifiers, an optional non-empty active-firm identifier, and a
   zero-offset creation instant. It creates an initially unrevoked session,
   initializes last-seen time to creation time, and derives the 30-minute idle
   and 8-hour absolute boundaries through `SessionLifetimePolicy`. EF Core keeps
   the private parameterless materialization constructor. Persistence receives
   no internals grant and may not invoke a non-public constructor by reflection.
3. **Purge outcomes have named valid construction paths.**
   `PurgeOutcome.NoEligibleRecords` represents the zero-record outcome;
   `PurgeOutcome.RecordsPurged` requires a positive record count and a
   non-negative deleted-slip count. Setters stay non-public, invalid counts are
   refused, and `ToString()` emits fixed structural identity only.

### SIN — an intentional reversal of the earlier "masked last three" answer

**No SIN or partial SIN may reach the browser or the persisted read model.** Not
full, not masked, not last three or four digits, not any SIN-derived display
identifier. SIN must not appear in portal DTOs, rendered HTML, URLs or query
strings, logs, exception messages, metrics, telemetry, the persisted
review-package read model, or development/test fixtures. The taxpayer is
identified by display name, participant role, tax year, and opaque internal
identifiers only.

**`TaxLogicEngine`'s existing `RecipientIdentifier` masking behaviour is
unchanged and must not be touched.** This decision governs what the **new**
persistence layer stores and what a portal surface may emit — not the engine's
in-memory contract, and not `T4Slip.Sin`.

### Database and persistence technology

Azure SQL Database with EF Core and `Microsoft.EntityFrameworkCore.SqlServer`.
Repository-owned EF Core migrations. One Azure SQL database for the modular
monolith. Explicit persistence entities **separate from domain models**.
Application-layer repositories that **require a trusted `FirmId`**. Every
tenant-owned table contains `FirmId`. Tenant resource lookups use `FirmId`
**plus** the resource identifier. Composite indexes and uniqueness constraints
include `FirmId` where appropriate. **`FirmId` is never accepted from a URL,
query string, request body, or caller-provided DTO as an authorization
boundary.** No PostgreSQL. **No generic repository abstraction.**

### Identity and tenancy

Local user identity by **OIDC issuer + subject**. `Firm`. `FirmMembership` with
active/disabled status. Exactly **one trusted active `FirmId` per session**. A
user may belong to multiple firms in the membership model; changing the active
firm requires an explicit **server-side** firm switch that verifies active
membership and issues a new authentication session. The application-issued claim
is named `taxai_firm_id`; **a firm claim supplied directly by the external OIDC
provider is never trusted.** The claim value equals the canonical `FirmId`
exactly. **A missing resource and a resource belonging to another firm must
produce the same safe not-found response.**

### T18A tenancy-bootstrap identity recovery (Class A correction)

The durable session deliberately stores only the opaque local `UserId`, not the
OIDC issuer or subject. Per-request user revalidation and firm selection
therefore reload the local `PortalUser` through
`IIdentityDirectory.FindUserByIdAsync(UserId, CancellationToken)`. This is the
fourth and final approved firm-scope exemption: it reads only the user table in
the existing identity carve-out, returns the same closed `PortalUser` shape as
issuer-plus-subject lookup, and returns no provider token, claims payload,
profile data, or taxpayer information.

Issuer, subject, `UserId`, and trusted `FirmId` are never accepted from a URL,
query string, request body, or caller-supplied claim. On firm selection, issuer
and subject are recovered only from that local user record and supplied to the
existing T18 sign-in/session-issuance flow. They are never added to the opaque
browser cookie.

### Per-request membership verification (R-1 — CONFIRMED)

**Every protected taxpayer-data request must re-verify, server-side, that:** the
local user is active; the selected `FirmMembership` still exists; the membership
is active; and the membership matches the active `FirmId` in the session.

A disabled membership is refused on the **next protected request**, not when the
cookie expires. **Membership or persistence failure fails closed.**

Tenancy bootstrap is a narrower authenticated stage, not a relaxation of R-1.
`GET /firms` and `POST /firms/select` may be reached while the live session's
`ActiveFirmId` is null, because those endpoints exist to discover and select it.
They remain subject to the durable-session check, local-user lookup by the
session's `UserId`, active-user check, and a fresh load of active memberships.
They are exempt only from the active-firm/matching-membership portion of R-1.
Every normal firm-scoped protected endpoint continues to require the complete
R-1 check.

The public root page (`GET /`), the health endpoint (`GET /health`), the OIDC
callback mechanics, and the sign-in page do **not** require taxpayer-data
membership verification.

### Server-side session revocation (R-2 — IN SCOPE for 011)

Clearing the browser cookie alone is insufficient: it permits replay of a copied
pre-logout cookie. **Spec 011 includes server-side session revocation.**

An Azure-SQL-backed server-side authentication session, or an ASP.NET Core ticket
store with equivalent behaviour. **The browser cookie carries only a protected
opaque session identifier or ticket key.** OIDC access and refresh tokens are
stored neither in the cookie nor in the session record.

The persisted session minimally contains: opaque `SessionId`; local `UserId`;
active `FirmId`; `CreatedAtUtc`; `LastSeenAtUtc`; `IdleExpiresAtUtc`;
`AbsoluteExpiresAtUtc`; `RevokedAtUtc` or equivalent active/revoked state.

Required behaviour: 30-minute idle timeout; 8-hour absolute lifetime; activity
may extend the idle boundary but **never** the absolute boundary; logout revokes
or deletes the server-side session **and** clears the cookie; a revoked session is
refused on the next request; **a copied pre-logout cookie can neither reuse nor
recreate a revoked session**; disabled membership is still independently refused
via the R-1 check; **session-store unavailability fails closed for protected
taxpayer-data routes.** The session store must work across multiple application
instances and **must not be an in-memory-only production implementation.**

### Encryption, residency, sessions

Production Azure SQL uses Transparent Data Encryption; connections require
encrypted transport; credentials come from managed identity or Azure Key Vault
and are never committed. Application/column-level encryption is **out of scope**
for 011, because the model already excludes SIN, DOB, address, email, and phone.
Production taxpayer data stays in Canada: **Canada Central** primary, **Canada
East** for Canadian DR/backup only; no database replication, backups, logs, key
storage, or telemetry outside Canadian Azure regions. ASP.NET Core cookie
authentication after OIDC sign-in; OIDC access and refresh tokens discarded after
sign-in and never stored in the cookie, browser storage, database, or logs;
HttpOnly, Secure, SameSite=Lax; server-side authorization on every request;
antiforgery for state-changing endpoints; **30-minute idle timeout, 8-hour
absolute maximum, sliding expiration never exceeding the absolute maximum,
reauthentication after the absolute lifetime, logout invalidates the local
session.** Data Protection keys in Canadian Azure Storage protected by Key Vault;
local development may use a developer-local key ring excluded from source
control.

### Retention (BQ-1 — ANSWERED)

Fixed v1 product retention: **seven years after the end of the tax year**. **An
engineering product default, explicitly NOT a statement of the statutory
minimum.** Every persisted review package carries `TaxYear`, `RetentionUntilUtc`,
`CreatedAtUtc`, `PackageVersion`.

**`RetentionUntilUtc` is an exclusive UTC boundary.** For tax year 2025:

```
tax-year end, exclusive:  2026-01-01T00:00:00Z
add seven calendar years
RetentionUntilUtc      =  2033-01-01T00:00:00Z
```

A record is purge-eligible when `evaluationInstantUtc >= RetentionUntilUtc`.
Therefore `2032-12-31T23:59:59.9999999Z` is **not** purge-eligible;
`2033-01-01T00:00:00Z` **is**; every later instant **is**.

**No inclusive end-of-day value (`23:59:59.9999999` or similar) is ever
modelled**, and the exclusive meaning of the name is documented wherever the
field is declared. All instants are UTC `DateTimeOffset`; **a supplied value
whose offset is not exactly zero is rejected at the application boundary,
never silently converted.**

Normal portal users cannot delete retained packages in 011. A deterministic
**privileged purge service/command** deletes only records whose
`RetentionUntilUtc` has been reached or passed, requires explicit trusted firm
scope, records only structural purge counts and identifiers (never taxpayer
values), and has full tenant-isolation and boundary tests. Scheduling that purge
in Azure is a later deployment spec; the retention calculation and purge
behaviour belong to 011.

### Provisioning (R-3 — accepted, with hard prohibitions)

A production deployment containing only spec 011 may have no `Firm`, `User`, or
`FirmMembership` records, so **no user can obtain a taxpayer-data session.** That
is the intended, conservative outcome.

**011 must not create:** a hidden default firm; a default administrator; a
production seed credential; a bypass or super-user claim; or a hard-coded
development membership that exists in production. Secure production provisioning
and administrative onboarding are a later specification. Deterministic synthetic
seeding stays development/test-only and environment-gated.

### Fixtures

Synthetic data only. No real taxpayer information, no copied document values, no
real names/addresses/emails/phones/SINs/account numbers/employer identifiers, no
production snapshots, **no realistic nine-digit SIN-like strings**. Obviously
synthetic values only ("Test Taxpayer 001", "Example Employer A", deterministic
GUIDs, toy amounts). **No SIN field exists in 011 fixtures at all.** Development
fixture seeding is explicitly environment-gated and disabled in production.

### Scope exclusions given by the human

**Not in 011:** the review UI, uploads, OCR, source documents, AI, editing,
approval actions, TaxCycle export, Azure OpenAI, Document Intelligence, document
ingestion, source-document storage.

---

## Reuse check

Performed by reading `specs/*/prd.md`, every `src/*/CLAUDE.md`, and the source
tree directly. Specs 001–010 are implemented and merged.

### Reused unchanged — referenced, never re-specified, never duplicated, never modified

| Existing artefact | Location | Role here |
|---|---|---|
| `AccountantReviewPackage` | `src/CaseReview/Models/` | The **input** to the allowlist projection. `FirmId`, `CaseId`, `ParticipantId`, `TaxYear`, `ReviewedSlips` are read. Nothing is recomputed, reordered, or re-derived. |
| `ReviewedSlip` | `src/CaseReview/Models/` | Per-slip source: `TaxYear`, `Slip`, `LineEntries`, `BoxAmounts`, `UnmappedBoxCodes` are read. `DocumentId` and `SourceReference` are **deliberately not persisted** (see FR-22). |
| `ReviewedBoxAmount` | `src/CaseReview/Models/` | `BoxCode` + `Amount`, already produced by `T4SlipBoxAmounts.Enumerate` upstream. |
| `TaxLineEntry` | `src/TaxLogicEngine/Models/` | `LineNumber`, `Amount`, `SourceBoxCodes` — the CRA line facts, copied verbatim. |
| `T4Slip` | `src/TaxLogicEngine/Models/` | Read only for `EmployeeName` and `EmployerName`. **`Sin` is never read.** |
| `RecipientIdentifier` | `src/TaxLogicEngine/Models/` | **Referenced by name in this PRD only to say it is untouched.** No new module names it, constructs one, or reads `Masked`. |
| `SupportedReviewTaxYears` | `src/CaseReview/Services/` | The single tax-year authority for what may be persisted. No second year policy is declared anywhere. |
| `ParseResult<T>`, `ValidationError` | `src/TaxLogicEngine/Models/` | The repository's existing fail-closed result and error shapes, reused for every new operation. No new result type is invented. |
| `ModuleRegistration.AddTaxAiModules` | `src/TaxAI.Portal/` | The existing single composition root, extended in place. **No second registration file is created in the portal.** |
| `PortalContent.HealthBody`, `PortalContent.ShellHtml` | `src/TaxAI.Portal/` | `/health` and `/` bodies, unchanged byte-for-byte. |
| `TaxAI.sln` | repo root | Appended to. No existing declaration, GUID, or configuration row is altered. **No total project count is asserted anywhere.** |

### Deliberately NOT reused

- **`ReviewPackageAssembler.Assemble`** — not invoked. The persistence layer
  never assembles a package; it receives one already assembled.
- **`AccountantReviewPackage.RecordDecision`**, `ReviewDecisionDescriptor`,
  `ReviewSignOff`, `SignOffDecision` — not invoked, not persisted. Sign-off is
  out of scope (approval actions are excluded), and the allowlist does not
  include a reviewer note, reviewer reference, or decision.
- **`T4SlipParser`, `T4LineMapper`, `T4SlipBoxAmounts`, `MoneyRounding`,
  `IT4LineMapProvider`, the embedded mapping configs** — none is called and none
  is read. Every box code, CRA line code, and amount stored was produced upstream.
- **`T4CaseProcessor`** — remains registered in the composition root (spec 010)
  and remains uninvoked by any endpoint.
- **`IDocumentExtractionIngress`** — still not implemented, not registered, not
  resolved.
- **`TaxCases`** — no reference in either direction. In particular
  `SafeDiagnosticText` is **not** used: it is a masking helper, and this feature's
  posture is omission, not masking.
- **A generic `IRepository<T>`** — explicitly forbidden by the human's decision.
  Each repository interface is concrete, named for its aggregate, and declares a
  required trusted-firm parameter except for exactly four enumerated
  identity-bootstrap lookups (FR-21).
- **ASP.NET Core's default in-memory cookie ticket handling as the production
  session of record** — explicitly overridden by R-2.

### Genuinely new (each justified individually)

| New artefact | Why it is not speculative |
|---|---|
| `src/Tenancy/` — deterministic identity, tenancy, session, read-model, and retention **contracts**, no EF `[ASSUMED]` | Root `CLAUDE.md` forbids EF Core in tax-domain code and requires dependencies to point inward. The contracts must live somewhere that both the EF implementation and the host can depend on, and that somewhere cannot be `CaseReview` (spec 009's `ContractTests` pin its exported type list exactly) and cannot be the portal (a module may never reference `TaxAI.Portal`). It ships real behaviour: the allowlist projection, the fail-closed guards, the session lifetime rules, and the retention calculation. |
| `src/Persistence/` — the **only** project in the repository containing EF Core `[ASSUMED]` | Confines `Microsoft.EntityFrameworkCore.SqlServer`, `DbContext`, entity configurations, and migrations to one project, so "tax-domain code must not depend on EF Core" is enforced by the reference graph rather than by memory. |
| `tests/Tenancy.Tests/`, `tests/Persistence.Tests/` `[ASSUMED]` | Every AC below needs somewhere to live. |
| OIDC + cookie authentication in the portal | The human's identity decision. Without it there is no trusted `FirmId`, and without that every repository is unusable by design. |
| A **durable server-side authentication session store** | The human's R-2 override. Cookie deletion alone permits replay of a copied pre-logout cookie; revocation must be a server-side fact, visible to every application instance. |
| A firm-selection / firm-switch surface in the portal | The human's decision requires an explicit **server-side** switch that verifies active membership and issues a new session. Without it, a user with more than one active membership can never obtain a firm claim at all. |
| EF Core migrations under `src/Persistence/Migrations/` | "Repository-owned EF Core migrations" — the human's decision. |
| Environment-gated development fixture seeding | The human's fixture decision, and the only way any of this is exercisable locally without an admin surface (which is out of scope). |

**No project, folder, page, controller, entity, or interface is created for a
future module or a future screen.** In particular no project named `Clients`,
`Firms`, `Documents`, `Intake`, or `Review` is created — spec 010's
`SolutionAndRegressionTests.NoPlaceholderProjectExistsForAFutureModule` forbids
exactly those five names, and `Tenancy`/`Persistence` are deliberately outside
that set.

---

## Current repository state

Grounded in the actual tree, read during this analysis.

### Projects declared in `TaxAI.sln`

`TaxLogicEngine`, `TaxCases`, `DocumentExtraction`, `CaseProcessing`,
`CaseReview`, `TaxAI.Portal` under `src/`, and the six matching test projects
under `tests/`. Test projects uniformly use `xunit` 2.5.3,
`xunit.runner.visualstudio` 2.5.3, `Microsoft.NET.Test.Sdk` 17.8.0,
`coverlet.collector` 6.0.0; `TaxAI.Portal.Tests` adds
`Microsoft.AspNetCore.Mvc.Testing`.

*(A description of today's tree, not a licence to assert a total project count
anywhere — see Migration constraints.)*

### Build and test commands

```
dotnet build TaxAI.sln
dotnet test TaxAI.sln
dotnet test tests/<Project> --filter "FullyQualifiedName~TestMethodName"
```

`master` has **2571 passing tests across six test projects**.

### What does not exist today

- **No persistence of any kind.** No `DbContext`, no `DbSet`, no
  `EntityFrameworkCore`, no `SqlConnection`, no `System.Data`, no file store, no
  cache, no queue, in any production file.
- **No identity of any kind.** No `AddAuthentication`, no `ClaimsPrincipal`, no
  cookie, no session, no user, no firm, no membership, no claim.
- No `global.json`, `Directory.Build.props`, `Directory.Packages.props`,
  `.editorconfig`, `nuget.config`, CI workflow, Dockerfile, `package.json`, or
  `node_modules`.
- `src/TaxAI.Portal/` production source is exactly three files:
  `Program.cs`, `ModuleRegistration.cs`, `PortalContent.cs`.

### The test-execution gate

`scripts/Invoke-SolutionTests.ps1` and `scripts/test-projects.json` (PR #97)
**will be merged to `master` before spec 011 implementation begins** and may
therefore be assumed present. 011 registers its new test projects in that merged
manifest, with their counts recorded as **floors**, in the same PR that
introduces them (FR-9).

### Existing tests read file-by-file to determine impact

- **`tests/TaxLogicEngine.Tests`, `TaxCases.Tests`, `DocumentExtraction.Tests`,
  `CaseProcessing.Tests`, `CaseReview.Tests`** — every forbidden-pattern and
  production-file scan is rooted at that module's own directory; every solution
  assertion is presence-and-uniqueness; the frozen `PreExistingTrees`,
  `ExpectedProjects`, and `ExistingProjectFiles` lists all predate the portal.
  **No new sibling project enters any of their scans.** `CaseReview.Tests`'
  `ContractTests` pins CaseReview's exported type list exactly — which is
  precisely why no new public type may be added to `CaseReview` (FR-4).
- **`tests/TaxAI.Portal.Tests`** — *is* affected, extensively and deliberately.
  Every affected assertion is enumerated in [Amendments](#amendments) with its
  strictly stronger replacement.

---

## How the FR-18 gate is satisfied

Spec 010's FR-18 requires that no endpoint, page, component, or middleware that
"renders taxpayer data, accepts user input, uploads a document, persists data,
reads a session or identity, or crosses a firm/case/participant boundary" be
added to the portal "until the Class A questions in Open questions (A–F) are
answered **in a separate specification**". This is that separate specification,
for four of the six surfaces and five of the six questions.

| Spec 010 question | Disposition here |
|---|---|
| **A — authentication and authorization** | **Answered by the human.** External OIDC for authentication; local identity keyed by issuer + subject; a **durable server-side session** with a cookie carrying only an opaque protected ticket key; server-side authorization and membership re-verification on every protected request; authorization in 011 is exactly "authenticated principal holding a trusted `taxai_firm_id` claim backed by a live, unrevoked session and a currently-active `FirmMembership`". |
| **B — tenant / firm isolation at the HTTP boundary** | **Answered by the human.** One trusted active `FirmId` per session, carried in an application-issued `taxai_firm_id` claim materialized from the server-side session record; never read from URL, query, body, header, or cookie as an authorization boundary; a provider-supplied firm claim is never trusted; every repository requires a trusted `FirmId`; missing and foreign resources are indistinguishable. |
| **C — what a UI may render, and at what masking level** | **Partly answered, and tightened.** SIN: **nothing, in any form** — the earlier "masked last three" answer is reversed. The wider render inventory is not needed here, because **011 renders no taxpayer data at all**; it remains spec 012's BQ-2. |
| **D — logging, telemetry, error reporting** | **Not deferred — the problem class is forbidden.** FR-54 bans every taxpayer value, SIN in any form, display name, amount, box code, CRA line code, claim value, session identifier, OIDC token, connection string, and request URL from every log, metric, trace, span, and error response; only structural counts and opaque identifiers may be emitted. That is strictly tighter than any policy the human would later write, so deferring can only lead to a relaxation, never to a leak. |
| **E — hosting, deployment, TLS, exposure, data residency** | **Answered by the human** for residency and encryption: Canada Central primary, Canada East for Canadian DR/backup only, TDE, encrypted transport, managed identity / Key Vault credentials, Canadian Data Protection key storage. 011 ships **no deployment artefact**; it enforces the posture by **refusing to start** outside Development without encrypted-transport configuration, a configured key ring, and a durable session store (FR-53), and by the hard release constraint in Out of scope. **B-6:** "a configured key ring" means one **registered in the built service provider**, with a non-framework repository and a non-framework encryptor — Azure Blob Storage plus Azure Key Vault in production. Configuration values alone do not satisfy it, and documentation saying "do not deploy" is not a runtime control. |
| **F — whether this host also exposes the untrusted extraction ingress** | **Untouched and still reserved.** 011 implements, registers, and resolves no `IDocumentExtractionIngress`, and accepts no uploaded file. |

The gate is opened by a separate `tax-domain` specification with human answers on
record — the mechanism FR-18 described. It is not bypassed and not reinterpreted.
**The portal's remaining gate is narrowed, never deleted** (FR-61).

---

## Amendments

Spec 010 is merged. A merged PRD is the record of what was agreed, so this
section says out loud which shipped assertions this feature makes wrong.
**`/plan` must treat every row as required work, and a human must approve the
gate opening before any of it is written.** Every superseded assertion must be
**replaced by a strictly stronger one — never merely relaxed, never deleted.**

### Spec 010 PRD requirements superseded

| Spec 010 obligation | Status under 011 | Replaced by |
|---|---|---|
| FR-5 / AC-3 — the portal declares **zero** `PackageReference` | **Superseded.** OIDC is not in the shared framework. | FR-13: exactly one new, version-pinned portal package reference, named and justified; AC asserts the exact set. |
| FR-3 / AC-8 / AC-40 — portal production references are exactly `TaxLogicEngine` + `CaseProcessing` | **Superseded.** | FR-12: exactly `TaxLogicEngine`, `CaseProcessing`, `Tenancy`, `Persistence` — still all inward, still asserted exactly. |
| FR-11 / AC-26 — exactly two routes, no route parameter | **Superseded.** | FR-48: an exact, enumerated route inventory; still no route parameter carries a firm identifier; still no taxpayer-data route. |
| FR-12 / AC-27 — no endpoint accepts any input | **Superseded** for the firm-selection endpoint only. | FR-49: the only caller-supplied inputs are the antiforgery token and the membership selector; every other input channel stays forbidden on every route. |
| FR-19 / AC-39 — no authentication or authorization surface | **Superseded.** | FR-38–FR-49 and FR-63–FR-72. |
| FR-20 — no persistence, HTTP client, background service, or clock | **Superseded** for persistence (the feature's point) and for the OIDC metadata fetch. **Preserved** for background services (session expiry is evaluated, never swept by a timer) and for clock reads inside domain code. | FR-16, FR-21, FR-33, FR-66. |
| FR-15 / AC-31 — no `decimal` token in the portal | **Preserved verbatim.** The portal still holds no monetary value. | FR-51. |
| FR-16 / AC-32 / AC-36 — the portal names no taxpayer data type; PII posture is omission | **Preserved and extended.** The portal still names none, still renders none, and now additionally may not name any persistence entity carrying taxpayer data. | FR-50, FR-51. |
| FR-15 / AC-35 — no tax-year literal or policy in the portal | **Preserved.** | FR-51. |
| FR-18 / AC-44 — the gate | **Narrowed, not removed.** | FR-61: the portal's `CLAUDE.md` gate is rewritten to a narrower gate that still stops rendering taxpayer data, uploads, mutation of stored packages, provisioning, and any second taxpayer-data surface. |
| FR-13 / AC-28, FR-14 / AC-29, AC-30 — `/health` discloses nothing; no diagnostic leakage | **Preserved verbatim and extended** to every new route. | FR-57, AC-29, AC-132. |
| FR-21 / AC-42 — server-rendered, no client framework, no JSON API | **Preserved and reinforced.** | FR-52. |
| FR-22 / AC-41 — no secret, no non-local binding in tracked configuration | **Preserved and strengthened.** No connection string appears in any tracked file at all. | FR-58. |

### Shipped test assertions superseded, with their required replacements

Read file-by-file. Each **passes on `master` today and will fail** once this
feature ships. `/plan` must name the replacement for each; none may be deleted or
weakened.

| File (spec 010 task) | Shipped assertion | Required strictly stronger replacement |
|---|---|---|
| `ProjectStructureTests.cs` (T01) | `PortalProject_DeclaresNoPackageReference` | Assert the portal's `PackageReference` set is **exactly** the one approved OIDC package at an exactly pinned version — an exact-set assertion is stronger than "empty" once one entry is legitimate. |
| `ProjectStructureTests.cs` (T01) | `PortalProject_ReferencesExactlyCaseProcessingAndTaxLogicEngine` | Assert the exact four-entry inward reference set, **plus** that the portal references neither `TaxCases`, `CaseReview`, nor `DocumentExtraction` directly. |
| `ProjectStructureTests.cs` (T01) | `TestProject_AddsOnlyTheInProcessWebTestHost` | Assert the portal test project's exact extra-package set, still excluding any network-binding or live-database package. |
| `ProjectStructureTests.cs` (T01) | `PortalInstructions_StateTheBoundaryAndTheGate`, `PortalInstructions_ForbidTaxpayerDataSurfacesUntilTheClassAQuestionsAreAnswered` | Assert the **narrowed** gate text, which must additionally state the no-SIN-in-any-form rule, the firm-claim-only rule, the never-trust-a-provider-firm-claim rule, the server-side-revocation rule, and that rendering taxpayer data still requires a further `tax-domain` spec. |
| `ProjectStructureTests.cs` (T01) | `ThePortalsOnlyPublicTypeIsProgram` | **Preserved unchanged.** Every new portal type is `internal`. |
| `CompositionRootTests.cs` (T02) | `BothValidationFlagsAreSetOutsideAnyDevelopmentBranch` (asserts `Program.cs` executable lines contain **no** `IsDevelopment`) | Assert `ValidateOnBuild`/`ValidateScopes` are set unconditionally and are not inside any conditional block, **and** that every `IsDevelopment` occurrence in the portal guards only an approved, enumerated concern (developer key ring, fixture seeding, developer exception page) and never a security control, a session store, or a membership check. |
| `CompositionRootTests.cs` (T02) | `TheUntrustedIngressPortIsNotRegistered`, `ThePortalImplementsNoIngress`, `NoLineMapperIsRegistered`, `TheProvidersAreSingletons` | **Preserved unchanged.** |
| `ShellPageTests.cs` (T03) | `TheHostMapsExactlyTwoGetEndpoints`, the `{ "/", "/health" }` inventory, `NoMappedEndpointTakesARouteParameter` | Assert the exact new route inventory with exact HTTP methods, **plus** that no route pattern parameter is named or used as a firm, case, participant, document, session, or package identifier. |
| `ShellPageTests.cs` (T03) | `TheRootReturnsTheFixedShellDocument`, `TheShellCarriesNoDynamicValue`, `TheShellReferencesNoExternalAsset`, `TheRootIsGetOnly` | **Preserved unchanged, byte-for-byte.** |
| `PortalBoundaryTests.cs` (T04) | `ExpectedProductionFiles` (frozen 3-file list, used by the scanner guard) | Replace with the new exact production-file list; the guard must still prove the walk saw every file and that the comment filter still works. |
| `PortalBoundaryTests.cs` (T04) | `TheCompiledPortal_ReferencesExactlyCaseProcessingAndTaxLogicEngine` | Assert the exact new non-framework referenced-assembly set, **and** that it contains neither `TaxCases` nor `CaseReview`. |
| `PortalBoundaryTests.cs` (T04) | `EveryServiceRegistrationLivesInTheCompositionRoot` (asserts exactly 3 registration matches) | Assert every `Add*`/`TryAdd*` registration in portal source is in `ModuleRegistration.cs`, that `ModuleRegistration.cs` calls exactly the approved set of per-module registration extensions, and that each new module declares exactly one such extension. |
| `PortalBoundaryTests.cs` (T04) | `ForbiddenProductionPatterns` rows for money math, taxpayer data types, mapping tables, tax-year policy; `NoCraLineNumberLiteralAppearsInThePortal`; `NoTaxYearLiteralAppearsInThePortal`; `ThePortalDeclaresNoBoxCodeOrCraLineConcept` | **Preserved, and extended** to the two new modules with module-appropriate rows. The `\bDictionary\s*<` row stays; the `\b\d{5}\b` row stays, so durations must be expressed as `TimeSpan.FromHours`/`FromMinutes`, never as a five-digit second count. |
| `PortalSecurityTests.cs` (T05) | `AuthenticationPatterns` (all four rows: authentication wiring, authorization wiring, identity, session/cookie state) | Replace with an **exact allow-list**: authentication, authorization, and session wiring may appear **only** in the approved files, must be the approved schemes and the durable ticket store, and every endpoint must be provably either explicitly anonymous (an enumerated list) or authorization-required. That is stronger than "none", because it forbids an unlisted endpoint from being anonymous. |
| `PortalSecurityTests.cs` (T05) | `InputBindingPatterns` rows | Replace with an exact allow-list: request input may be read only in the approved file(s), only for the antiforgery token and the membership selector, and never for a firm, case, participant, document, session, or package identifier used as an authorization boundary. |
| `PortalSecurityTests.cs` (T05) | `InfrastructurePatterns` "file system" row (`\bSystem\.IO\b`…) | Narrowed: file-system use is permitted only for the developer-local Data Protection key ring, only inside an `IsDevelopment()` branch, and `UseStaticFiles`/`wwwroot` remain forbidden outright. |
| `PortalSecurityTests.cs` (T05) | `InfrastructurePatterns` "database / ORM" row | **Preserved.** The portal names no `DbContext`, `DbSet`, or `SqlConnection`; EF Core lives only in `Persistence`, and the session store is reached through a `Tenancy` contract. |
| `PortalSecurityTests.cs` (T05) | `PiiPatterns` rows (SIN identifier, masking helper, taxpayer name/address field) | **Preserved and strengthened** — extended with rows forbidding any SIN-derived display identifier and any persisted-taxpayer-content type name in portal source. |
| `PortalSecurityTests.cs` (T05) | `NeitherResponseBodyContainsANineDigitRun`, `ProductionSource_ContainsNoNineDigitLiteral`, `LoggingPatterns`, `TrackedConfiguration_*`, `TheRepositoryHasNoClientSideStack` | **Preserved and extended** to every new route, every new module, and every new tracked configuration file. |
| `PortalSecurityTests.cs` (T05) | `ExpectedProductionFiles` scanner guard | Replace with the new exact list, guard behaviour unchanged. |
| `SolutionAndRegressionTests.cs` (T06) | `PublicSurfaceSnapshots`, `ModuleReferenceSets`, `TheModuleStillDeclaresNoPackageOrFrameworkReference`, `PreExistingDeclarations`, `NoPlaceholderProjectExistsForAFutureModule`, `TheEmbeddedMappingResourcesKeepTheirExactLogicalNames`, `TheRepositoryHasExactlyItsThreePreExistingInternalsGrants` | **All preserved unchanged.** No public type is added to any of the five modules; no module gains a package or framework reference; the three internals grants stay three. |
| `SolutionAndRegressionTests.cs` (T06) | `ThePortalNamesNoSpec009Type` | **Preserved.** The portal still names none of them; the projection lives in `Tenancy`. |
| `SolutionAndRegressionTests.cs` (T06) | `TheCompiledPortalDoesNotReferenceCaseReview` | **Preserved.** Portal code names no `CaseReview` type, so `CaseReview` must not appear in the portal assembly's referenced-assembly set. |
| `RepositoryVerificationTests.cs` (T07) | `TheReadmeMakesNoClaimThePortalCannotBack` (forbids "authentication", "sign in", "database" outside the disclaimer section) | Replace with an exact capability inventory: the README must state what 011 does ship (sign-in, revocable sessions, tenancy, a database, retention) and must still be provably unable to claim what it does not (taxpayer-data screens, upload, OCR/LLM, deployment, provisioning). |
| `RepositoryVerificationTests.cs` (T07) | `TheReadmeDocumentsEveryProjectTreeThatExists`, `TheRootInstructionsListTheNewTree`, `TheRootInstructionsRecordTheDeliveredScope`, `TheForwardRoadmapRecordsWhatBlocksTheNextPortalFeature` | Extended with the new trees and the new delivered scope. |
| `RepositoryVerificationTests.cs` (T07) | `TheReadmeSaysPlainlyThatNoTaxpayerDataIsRendered`, `TheReadmeAssertsNoProjectCount`, `TheRootInstructionsStillForbidATotalProjectCount`, `NoExistingModuleInstructionFileWasRewrittenForTheHost` | **All preserved unchanged.** |
| `HealthEndpointTests.cs` | Every assertion | **Preserved unchanged.** `/health` stays anonymous, fixed, GET-only, disclosure-free, and — per FR-70 — still answers when the session store is unavailable. **B-6 adds a non-assertion change:** the fixture must register the approved durable Data Protection test provider, because this suite starts a **Production** host and FR-53 now refuses one without it. No assertion is added, changed, weakened, or removed. |
| `ShellPageTests.cs` | Every assertion | **Preserved** (the route inventory is separately superseded above). **B-6 adds the same non-assertion change:** this suite also starts a Production host and must register the approved durable Data Protection test provider. No assertion is added, changed, weakened, or removed by B-6. |

### Spec 012's draft PRD

`specs/012-accountant-review-screen/prd.md` is a **DRAFT and is not edited by
this feature.** Two of its blocking questions are answered by the decisions
recorded above (BQ-1 firm claim; BQ-3 session carriage, token retention, key
material, lifetime), and its BQ-2 row
`T4Slip.Sin | masked only — answered (decision 3)` is **superseded**: no SIN in
any form may be rendered. That must be reconciled in spec 012 before it proceeds.

### Shipped tax code that becomes wrong

**None.** No box→line mapping, parser rule, rounding behaviour, tax-year policy,
or CRA fact is corrected or changed by this PRD, so **no already-merged task
(T0x) of specs 001–009 produces a now-incorrect tax result.** The invalidation
above is confined to spec 010's *posture* assertions (T01–T07 of spec 010), each
of which is replaced by a strictly stronger one.

---

## User stories

1. As an **accountant**, I sign in through my firm's identity provider, and my
   session is bound to exactly one firm, so nothing I do can reach another firm's
   client.
2. As an **accountant who works for two firms**, I switch firms by an explicit
   server-side action that re-checks my membership and issues a fresh session, so
   my "active firm" is never something a URL or a stale cookie decides.
3. As a **firm principal**, when I disable a departed employee's membership, the
   next protected request they make is refused — not the next request after their
   eight-hour session expires.
4. As a **firm principal**, when someone signs out, a copy of their cookie taken
   five minutes earlier is worthless: the session is revoked server-side, and
   every application instance sees that immediately.
5. As a **taxpayer**, my SIN is nowhere in this system's database, its logs, its
   fixtures, its URLs, or its error messages — not in full and not in part.
6. As a **taxpayer**, the amounts and CRA line codes stored about me are exactly
   the ones the engine produced: the storage layer cannot round, truncate,
   reorder, or re-derive a single one of them.
7. As a **taxpayer**, my record is kept until a documented, exclusive UTC
   boundary and is then deleted by an explicit, privileged,
   firm-scoped operation that logs counts, not contents.
8. As a **firm principal at firm X**, a request authenticated at firm Y that
   guesses one of my record identifiers gets exactly the same answer as a request
   for a record that never existed.
9. As an **engine maintainer**, the persistence layer computes no tax: no box→line
   table, no CRA line literal, no arithmetic, no rounding, no `MoneyRounding`.
10. As an **engine maintainer**, a 2024 or 2026 package is refused rather than
    stored, so the database can never become the place where year isolation is
    lost.
11. As a **compliance owner**, the production posture — Canadian regions,
    encrypted transport, TDE, Key Vault credentials, Canadian key storage, a
    durable session store — is written down, and a host missing any of it refuses
    to start rather than running unprotected.
12. As a **security reviewer**, there is no default firm, no default
    administrator, no seeded production credential, and no bypass claim anywhere
    in this build, and a test says so.
13. As a **reviewer of this repository**, every assertion spec 010 made about the
    portal is either still true or has been replaced by a strictly stronger one,
    with the replacement named.
14. As the **author of spec 012**, I inherit a trusted `FirmId`, a revocable
    session, a firm-scoped repository, and an answered SIN policy, so the review
    screen spec is about a screen.

---

## Functional requirements

`[reuse]` marks an existing type used as-is. `[new]` marks a genuinely new
artefact. `[gate]` marks a boundary invariant carried forward from root
`CLAUDE.md` or spec 010. **No requirement below asserts a CRA fact.**

### Module structure and dependency direction

**FR-1 `[new]`** — Two production class libraries SHALL be added: `src/Tenancy/`
and `src/Persistence/` `[ASSUMED]`, both net8.0 with nullable reference types
enabled, both using `Microsoft.NET.Sdk`.

**FR-2 `[gate]`** — `Tenancy` SHALL be deterministic and provider-independent. Its
production references SHALL be exactly `CaseReview` and `TaxLogicEngine`, both
pointing inward. It SHALL contain **no** EF Core, ASP.NET Core, SQL, cloud SDK,
OCR, LLM, email, filesystem, network, system-clock, environment, or current-user
dependency, and no `TaxCases` reference.

**FR-3 `[gate]`** — `Persistence` SHALL be the **only** project in the repository
that references EF Core. Its production references SHALL be exactly `Tenancy` and
the EF Core packages it needs. It SHALL contain no ASP.NET Core dependency, and no
direct reference to `CaseReview`, `CaseProcessing`, `DocumentExtraction`,
`TaxCases`, or `TaxAI.Portal`.

**FR-4 `[gate]`** — **No public type, member, or signature of `TaxLogicEngine`,
`TaxCases`, `DocumentExtraction`, `CaseProcessing`, or `CaseReview` SHALL be
added, renamed, removed, or re-shaped**, and no file under those five trees or
their five test trees SHALL be edited. Spec 009's `ContractTests` and spec 010's
`PublicSurfaceSnapshots` pin those surfaces exactly; this is why the read-model
and session contracts live in `Tenancy`.

**FR-5 `[gate]`** — **No module SHALL reference `TaxAI.Portal`**, in a project
file, in source, or transitively. The dependency arrows SHALL be:

```
TaxAI.Portal ──► Tenancy ──► CaseReview ──► CaseProcessing ──► DocumentExtraction
             ──► Persistence ──► Tenancy                    ──► TaxLogicEngine
             ──► CaseProcessing
             ──► TaxLogicEngine
```

**FR-6 `[gate]`** — **No module SHALL read another module's database tables.**
All access to the persisted store SHALL go through `Tenancy`'s repository and
session-store interfaces, implemented only in `Persistence`.

**FR-7 `[new]`** — Two test projects SHALL be added, `tests/Tenancy.Tests/` and
`tests/Persistence.Tests/` `[ASSUMED]`, using the repository's standard xUnit
tooling versions.

**FR-8 `[new]`** — Every new project SHALL be declared in `TaxAI.sln` exactly
once at its approved path, by appending. **No artefact of this feature SHALL
assert a total project count** for the solution, in a test, a document, or the
README.

**FR-9 `[new]`** — The test-execution gate (`scripts/Invoke-SolutionTests.ps1` +
`scripts/test-projects.json`) is present on `master` before implementation
begins. Each new test project SHALL be registered in `scripts/test-projects.json`
**in the same PR that introduces it**, with its recorded test count treated as a
**floor** — a minimum that may rise, never an exact equality that later work must
not exceed.

**FR-10 `[new]`** — No `PackageReference`, `FrameworkReference`,
`EmbeddedResource`, or `Content` item SHALL be added to `TaxLogicEngine`,
`TaxCases`, `DocumentExtraction`, `CaseProcessing`, or `CaseReview`.

**FR-11 `[new]`** — No new `InternalsVisibleTo` grant SHALL be added to any
pre-existing project; the repository's three existing grants SHALL remain exactly
three. New modules MAY declare their own grants to their own test projects.

**FR-12 `[gate]`** — The portal's production project references SHALL become
exactly `TaxLogicEngine`, `CaseProcessing`, `Tenancy`, and `Persistence`
`[ASSUMED]`. It SHALL NOT directly reference `TaxCases`, `CaseReview`, or
`DocumentExtraction`.

**FR-13 `[new]`** — The portal MAY declare exactly one new `PackageReference`, for
OpenID Connect authentication, pinned to an explicit 8.0.x version `[ASSUMED]`. No
other package reference SHALL be added to the portal. **EF Core SHALL NOT appear
in the portal's project file**: `Persistence` exposes its own registration
extension, which the composition root calls.

**FR-14 `[gate]`** — All portal service registration SHALL remain rooted in
`ModuleRegistration.cs`. That file SHALL call exactly one registration extension
per contributing module, and each contributing module SHALL declare exactly one
such extension. No `Add*`/`TryAdd*` registration SHALL appear elsewhere in portal
source.

### The persisted store

**FR-15 `[new]`** — Persistence SHALL target **Azure SQL Database** via
`Microsoft.EntityFrameworkCore.SqlServer`. **No PostgreSQL, and no second
provider, SHALL be introduced in production code.** One database serves the
modular monolith.

**FR-16 `[new]`** — Schema SHALL be defined by **repository-owned EF Core
migrations** under `src/Persistence/Migrations/` `[ASSUMED]`. No runtime
`EnsureCreated`, no automatic migration on startup, and no schema mutation
outside a migration.

**FR-17 `[new]`** — Persistence entities SHALL be **explicit and separate from
domain models**. No `CaseReview`, `CaseProcessing`, `DocumentExtraction`,
`TaxCases`, or `TaxLogicEngine` type SHALL be mapped as an EF entity, owned type,
or complex type, and **no `AccountantReviewPackage`, `ReviewedSlip`,
`ReviewedBoxAmount`, `TaxLineEntry`, or `T4Slip` SHALL be serialized to any
column, in any format.**

**FR-18 `[new]`** — **Every tenant-owned table SHALL contain a non-nullable
`FirmId` column.** Every uniqueness constraint and every index used for a tenant
lookup SHALL include `FirmId`, and `FirmId` SHALL be the leading column of every
such composite index `[ASSUMED ordering]`.

The **identity carve-out** is closed and consists of **exactly three tables**:
the user table (keyed by OIDC issuer + subject; owns no `FirmId`), the firm
membership table (carries a non-nullable `FirmId` and is looked up by user), and
the authentication session table (carries a **nullable** `ActiveFirmId`, because
a signed-in principal with zero or multiple active memberships legitimately has
none — FR-45). **No other table is exempt from FR-18, and none of the three may
carry taxpayer content.** These three are the bootstrap of tenancy; they cannot
themselves be tenant-scoped without circularity.

**FR-19 `[new]`** — `FirmId` SHALL be a GUID stored as `uniqueidentifier`
`[ASSUMED]`, so tenant comparison can never depend on database collation. Every
string identifier column SHALL declare an explicit binary (case- and
accent-sensitive) collation `[ASSUMED]`, and every identifier comparison in code
SHALL be ordinal.

**FR-20 `[new]`** — Every monetary column SHALL declare an **explicit** precision
and scale — never the provider default `[ASSUMED: decimal(19,4)]` — and the save
path SHALL **refuse** any amount whose value would not survive a store-and-reload
round trip exactly. The persistence layer SHALL never round, truncate, or
re-scale an amount.

**FR-21 `[new]`** — Repositories SHALL be **concrete and named for their
aggregate**. **No generic repository abstraction** (`IRepository<T>`, a generic
`Save<T>`, or an exposed `IQueryable`/`DbSet`) SHALL exist in any public or
internal contract. Every lookup, save, and delete operation SHALL take a **trusted
`FirmId` as a required, non-optional parameter, with no overload, default,
optional argument, or alternate code path that omits it.**

`IReviewRecordRepository.SaveAsync` SHALL accept only the already-projected
`PersistedReviewRecord` plus the trusted firm and cancellation token. It SHALL
accept, return, serialize, and reference no `AccountantReviewPackage` or other
executable `CaseReview` type. The caller invokes
`ReviewPackageProjection.Project` before the value crosses into `Persistence`.

There are **exactly four** enumerated firm-scope exemptions, all confined to the
approved identity bootstrap:

1. session lookup by `SessionId`, which establishes the local user and active
   firm scope and therefore returns `ActiveFirmId` rather than accepting one;
2. local-user lookup by OIDC issuer + subject at initial sign-in;
3. local-user lookup by the opaque `UserId` recovered from a live server-side
   session, for per-request active-state revalidation and firm-selection
   identity recovery; and
4. active-membership lookup by that local `UserId`, which is the input to the
   decision about which firm — if any — the session may hold.

`FirmScopeExemptions.All` SHALL contain those four entries and no others and
SHALL be asserted by name and count. None returns a provider token, claims
payload, profile field, or taxpayer value. Every taxpayer-data save, read, and
purge remains firm-required with no omitting overload.

**FR-22 `[new]` — The allowlist projection.** An `AccountantReviewPackage` SHALL
be converted to persistence entities by a single explicit projection whose output
properties are a **closed allowlist**. The persisted fields SHALL be exactly:

- trusted `FirmId`; TaxAI-generated opaque internal identifiers for the record and
  each persisted slip;
- the package's `CaseId` and `ParticipantId`, as opaque internal identifiers
  subject to FR-25;
- `TaxYear`;
- taxpayer display name (from `T4Slip.EmployeeName`);
- safe employer display name (from `T4Slip.EmployerName`);
- slip type discriminator (structural; see FR-24);
- a **TaxAI-generated** opaque document reference per persisted slip;
- box codes and box amounts (from `ReviewedSlip.BoxAmounts`, in package order);
- CRA line codes, mapped CRA amounts, and source box codes (from
  `ReviewedSlip.LineEntries`, in package order);
- populated unmapped box codes (from `ReviewedSlip.UnmappedBoxCodes`, in package
  order);
- `PackageVersion`, `CreatedAtUtc`, `UpdatedAtUtc`, `RetentionUntilUtc`.

The successful projection result is the only review-record value supplied to
`IReviewRecordRepository.SaveAsync`. The repository does not perform the
conversion and never receives the package.

Nothing else. In particular the persistence model SHALL declare **no** property
or column for: a SIN in any form; a SIN-presence flag; a date of birth; a mailing
address; an email; a phone number; an employer payroll or account number
(`T4Slip.EmployerAccountNumber`); an RPP/DPSP registration number; bank account
information; `ReviewedSlip.SourceReference`; a raw source path, blob name, or
storage key; raw OCR text; document bytes; an OIDC access, refresh, or id token;
or any provider diagnostic string.

**FR-23 `[new]`** — The projection SHALL **never read `T4Slip.Sin`**, SHALL
name no `RecipientIdentifier`, SHALL construct no masking, redaction, sanitizing,
or scrubbing helper, and SHALL contain no SIN-derived value of any kind. **PII
here is handled by omission, not masking.** `TaxLogicEngine`'s existing
`RecipientIdentifier` behaviour is untouched.

**FR-24 `[new]`** — The slip type discriminator SHALL be a structural code owned
by `Tenancy`, whose only permitted value in 011 is the single slip type the
merged upstream contract produces `[ASSUMED: "T4"]`. It asserts **no** CRA
semantics: no box meaning, no line meaning, no rate, no threshold. A value the
persistence layer does not recognize SHALL be refused.

**FR-25 `[new]`** — `CaseId` and `ParticipantId` arrive as caller-supplied
strings. Before persisting, each SHALL be validated as an **opaque token**:
non-empty, bounded maximum length, and restricted to a fixed safe character set
`[ASSUMED]`. Anything failing that guard SHALL be refused before anything is
written. No caller string SHALL be trimmed, normalized, case-folded, or truncated
to make it pass.

**FR-26 `[new]`** — Any taxpayer-content string value (display name, employer
display name) that contains **nine consecutive digits** SHALL be refused, and
nothing SHALL be written. Any string value exceeding its column's declared length
SHALL be refused and **never truncated**.

**FR-27 `[new]` — Immutable versioned snapshots.** A persisted review record SHALL
be immutable once written. Saving a package for a subject key that already has a
record SHALL create a **new record at `PackageVersion` = existing maximum + 1**,
leaving every earlier version byte-unchanged. **No update path, no upsert, and no
in-place overwrite SHALL exist.** The subject key SHALL be
`(FirmId, CaseId, ParticipantId, TaxYear)` `[ASSUMED]`, and
`(FirmId, CaseId, ParticipantId, TaxYear, PackageVersion)` SHALL be unique.

**FR-28 `[new]`** — Collections SHALL be persisted **completely and in the
package's own order**, with an explicit stored ordinal. The persistence layer
SHALL NOT sort, group, filter, deduplicate, merge, subtotal, aggregate, or drop
any slip, line entry, box amount, or unmapped code.

**FR-29 `[reuse]`** — Before persisting, the record's `TaxYear` SHALL be checked
against `CaseReview.Services.SupportedReviewTaxYears.IsSupported`. An unsupported
year SHALL be refused with nothing written. `Tenancy` and `Persistence` SHALL
declare **no tax-year literal, no supported-year list, and no "current year"
concept**, and SHALL NOT fall back to an adjacent year.

**FR-30 `[new]`** — Every slip in a package SHALL carry the same `TaxYear` as the
package. Any divergence SHALL be refused with nothing written.

**FR-31 `[new]`** — A package whose `AccountantReviewPackage.FirmId` differs
ordinally from the trusted `FirmId` supplied to
`ReviewPackageProjection.Project` SHALL be refused before any persistence call.
**The trusted `FirmId` — never the package's — is what the projected record
stores and what the repository independently requires as its scope.**

**FR-32 `[new]`** — Every read operation SHALL be scoped by `(FirmId, resource
identifier)`. A record that does not exist and a record owned by another firm
SHALL produce **the same** result — the same value, the same error, the same
absence — with no signal distinguishing them and no identifier echoed.

### Retention and purge

**FR-33 `[new]`** — Every persisted review record SHALL carry `TaxYear`,
`CreatedAtUtc`, `UpdatedAtUtc`, `PackageVersion`, and `RetentionUntilUtc`.
`RetentionUntilUtc` SHALL be computed **deterministically from `TaxYear` alone**,
as **seven calendar years after the exclusive end of that tax year** — the fixed
v1 product default, **not a CRA statutory statement**:

```
RetentionUntilUtc = midnight UTC on 1 January of (TaxYear + 1), plus seven years
                  = 1 January (TaxYear + 8), 00:00:00 UTC
For TaxYear 2025  = 2033-01-01T00:00:00Z
```

**`RetentionUntilUtc` is an EXCLUSIVE boundary**, and that meaning SHALL be
documented in the declaration of the field, in its EF configuration, and in the
migration. **No inclusive end-of-day value (`23:59:59`, `23:59:59.9999999`, or
similar) SHALL be computed, stored, defaulted, or compared against anywhere.**

The evaluation instant for `CreatedAtUtc`, `UpdatedAtUtc`, session lifetimes, and
purge SHALL be supplied to the contract as an explicit parameter; **no clock is
read inside `Tenancy` or inside the projection**, so the same inputs always
produce the same record.

**FR-34 `[new]`** — All instants SHALL be UTC `DateTimeOffset`.
`UtcInstantGuard` SHALL accept a value only when its `Offset` is exactly
`TimeSpan.Zero`; it SHALL reject every non-zero offset and SHALL **never coerce,
convert, normalize, or assume a non-zero offset is UTC**. The guard SHALL NOT
attempt to infer the originating `DateTime.Kind`: `DateTimeOffset` does not
retain whether an equal zero-offset value originated from `DateTimeKind.Utc` or
`DateTimeKind.Unspecified`. Every persisted instant SHALL be stored with a zero
offset. A future API that accepts `DateTime` directly may reject
`DateTimeKind.Unspecified` before constructing a `DateTimeOffset`; that separate
API is outside T03, whose public signature remains unchanged.

**FR-35 `[new]`** — A **privileged purge operation** SHALL delete a record when
`evaluationInstantUtc >= RetentionUntilUtc`, and SHALL delete no other record. It
SHALL require an explicit trusted firm scope as a required parameter with no
firm-omitting overload, SHALL never delete a record of another firm, and SHALL be
deterministic.

**FR-36 `[new]`** — Purge output and purge logging SHALL contain **only structural
counts and opaque identifiers** — never a taxpayer value, display name, box code,
CRA line code, amount, session identifier, or any SIN-shaped string.
`PurgeOutcome.NoEligibleRecords()` SHALL create the zero-record outcome;
`PurgeOutcome.RecordsPurged(recordsDeleted, slipsDeleted)` SHALL require a
positive record count and a non-negative slip count. No unrestricted public
constructor or mutable public setter SHALL exist, and `ToString()` SHALL emit
fixed structural identity only.

**FR-37 `[new]`** — **Normal portal users SHALL have no way to delete a retained
record.** No endpoint, no repository method reachable from an HTTP request, and no
cascade SHALL delete a persisted review record in 011.

### Identity, session, and tenancy at the HTTP boundary

**FR-38 `[new]`** — Authentication SHALL be delegated entirely to an **external
OIDC provider**. The portal SHALL NOT store, accept, transmit, hash, compare, or
handle a password, and SHALL declare no local user store credential, no
registration surface, and no password-reset surface.

**FR-39 `[new]`** — A local user identity SHALL be keyed by **OIDC issuer +
subject**, unique together, and SHALL carry an active/inactive state. The user
record SHALL store **issuer, subject, active state, and timestamps only**
`[ASSUMED]` — **no email, no phone, no display name, no provider profile claim**
is persisted, because email and phone are forbidden at rest and a display name is
available from the live principal when needed.

**FR-40 `[new]`** — After sign-in the session SHALL be carried by **ASP.NET Core
cookie authentication backed by a durable server-side session store** (FR-63).
**The cookie SHALL carry only a protected opaque session identifier or ticket
key** — no claims payload, no `FirmId`, no `UserId`, no token, no taxpayer value.
The **OIDC access, refresh, and id tokens SHALL be discarded once the principal is
established** and SHALL never be stored in the cookie, in browser storage, in the
session record, in the database, or in any log. The cookie SHALL be `HttpOnly`,
`Secure`, and `SameSite=Lax`.

**FR-41 `[new]`** — Session lifetime SHALL be enforced **server-side** from the
session record: **30-minute idle timeout; 8-hour absolute maximum; activity may
extend the idle boundary but SHALL NEVER extend the absolute boundary;
reauthentication required after the absolute lifetime; logout revokes the
server-side session and clears the cookie.**

**FR-42 `[new]`** — Data Protection keys SHALL be configured explicitly. Outside
Development the key ring SHALL be a configured Canadian Azure Storage location
protected by Key Vault; in Development a **developer-local key ring excluded from
source control** MAY be used. **A host running outside Development without a
configured key ring SHALL refuse to start.**

> **Corrected by decision B-6.** "Configured" means **registered in the built
> service provider**, not merely named in configuration. **Configuration values
> alone SHALL NOT satisfy this requirement.** Outside Development the host SHALL
> refuse to start unless the built provider contains `IDataProtectionProvider`, a
> non-null `KeyManagementOptions.XmlRepository`, and a non-null
> `KeyManagementOptions.XmlEncryptor`, **and neither the repository nor the
> encryptor is an implementation supplied by the ASP.NET Core Data Protection
> framework assembly.** The framework's default behaviour leaves `XmlRepository`
> null and resolves a machine-local key directory lazily, so a null repository is
> precisely the fallback this rule exists to refuse. The intended production
> provider is **Azure Blob Storage for the repository and Azure Key Vault for key
> encryption.** See B-6 in `tasks.md` for the enumerated rejection list.

**FR-43 `[new]`** — Exactly **one trusted active `FirmId` per session**. It SHALL
be carried by the application-issued claim `taxai_firm_id`, materialized from the
server-side session record's `ActiveFirmId` on each request, whose value SHALL
equal the canonical `FirmId` exactly (ordinal, no normalization, no trimming, no
case folding). **A claim of that name arriving from the external OIDC provider
SHALL be discarded before the local principal is created and SHALL never be
trusted.** A divergence between the claim and the session record SHALL fail
closed.

**FR-44 `[new]`** — The trusted `FirmId` SHALL be read **only** from that claim,
which is itself derived only from the session record. It SHALL NOT be read from a
route value, query-string value, request body, form value, header, or cookie
payload, and **no code path SHALL exist that accepts a caller-supplied firm
identifier as an authorization boundary.**

**FR-45 `[new]`** — On sign-in the portal SHALL resolve the user's **active**
`FirmMembership` records:
- **exactly one active membership** → that firm becomes the session's
  `ActiveFirmId` and `taxai_firm_id` is issued;
- **zero active memberships**, or **more than one** → the principal is
  authenticated, the session record's `ActiveFirmId` is null, **no
  `taxai_firm_id` claim is issued**, and every firm-scoped operation fails closed
  until the user performs the explicit firm selection of FR-46. **The application
  never chooses a firm on the user's behalf.**

**FR-46 `[new]`** — Changing or setting the active firm SHALL require an explicit
**server-side** operation that: is state-changing and therefore antiforgery-
protected; verifies that the authenticated user holds a **currently active**
membership in the requested firm; **revokes the current server-side session**; and
issues a **new** server-side session with a new `SessionId` and a new
`AbsoluteExpiresAtUtc`, carrying the new `ActiveFirmId`. The requested firm
arrives as a caller-supplied selector and is treated as an **unverified request**,
never as an authorization boundary — it is only ever used to look up a membership
belonging to the authenticated user.

The operation SHALL take its `UserId` only from the authenticated server-side
session, reload the current `PortalUser` through `FindUserByIdAsync`, refuse a
missing or inactive user, and reload current active memberships. After the
selected membership is proved active and owned by that user, the operation SHALL
recover issuer and subject only from the local `PortalUser` and issue the
replacement session through the existing T18 sign-in/session-issuance flow.
Issuer, subject, `UserId`, and trusted `FirmId` SHALL NOT be accepted from the
request or from caller-supplied claims, and issuer/subject SHALL NOT enter the
cookie.

**FR-47 `[new]` — Per-request verification (R-1).** On **every protected
taxpayer-data request**, the server SHALL re-verify, in this order, that: the
session exists, is unrevoked, and is within both its idle and absolute bounds;
the local user exists and is active; the selected `FirmMembership` still exists;
that membership is active; and that membership's `FirmId` matches the session's
`ActiveFirmId`. **Any failure — including a membership-store or persistence
failure — SHALL fail closed**, refusing the request and returning no record
content and no identifier.

Authenticated tenancy-bootstrap requests (`GET /firms` and
`POST /firms/select`) SHALL run the same live-session, valid-session-`UserId`,
fresh local-user, active-user, and fresh active-membership reads. They MAY have a
null `ActiveFirmId` and are exempt only from the final active-firm/matching-
membership portion of R-1. No normal firm-scoped protected endpoint receives
that exemption.

`GET /`, `GET /health`, the sign-in page, and the OIDC callback mechanics are
**exempt** from taxpayer-data membership verification and SHALL remain reachable
without it.

**FR-48 `[new]`** — The portal SHALL expose exactly these routes `[ASSUMED]`:
`GET /` (constant shell, anonymous, unchanged); `GET /health` (constant literal,
anonymous, unchanged); `GET /sign-in` (challenge); the OIDC handler's own callback
paths; `GET /firms` (authenticated; lists the signed-in user's **active** firm
memberships by firm display name and opaque membership identifier);
`POST /firms/select` (authenticated, antiforgery-protected, FR-46); and
`POST /sign-out` (authenticated, antiforgery-protected). **No other route SHALL be
mapped.** Every route SHALL be provably either on an enumerated anonymous list
(`/`, `/health`, `/sign-in`, the OIDC callback paths) or authorization-required.

**FR-49 `[new]`** — The only caller-supplied inputs in this feature SHALL be the
antiforgery token and the membership selector on `POST /firms/select`. The portal
SHALL read no request body, uploaded file, or query value for any other purpose,
and SHALL declare no `IFormFile`, no `[FromBody]`, and no `[FromQuery]`.

**FR-50 `[gate]`** — **The portal SHALL render no taxpayer data.** It SHALL name
no `T4Slip`, `T5Slip`, `TaxLineEntry`, `RecipientIdentifier`, `T4RawInput`,
`ProcessedT4Extraction`, `AccountantReviewPackage`, `ReviewedSlip`,
`ReviewedBoxAmount`, `TaxCase`, or any `Tenancy` persistence entity carrying
taxpayer content. A firm's display name and the signed-in user's own display name
are **not** taxpayer data and MAY be rendered.

**FR-51 `[gate]`** — The portal SHALL compute no tax and implement no CRA mapping:
no CRA line-number literal, no box code, no box→line table, no mapping-config
read, no `decimal`, `double`, `float`, `Math.Round`, `MidpointRounding`, or
`MoneyRounding`, and no tax-year literal or supported-year policy.

**FR-52 `[gate]`** — The portal SHALL remain **server-rendered**: no SPA
framework, no `package.json`, no npm artefact, no client-side data fetching, no
`wwwroot`, no static-file middleware, and no JSON/XML/CSV representation of any
persistence entity or package type.

### Configuration, secrets, logging, and diagnostics

**FR-53 `[new]`** — Outside Development, the host SHALL **refuse to start** when:
the database connection string is absent; the connection string does not require
encrypted transport with certificate validation; the Data Protection key ring is
unconfigured; or the registered session store is not the durable implementation. A
misconfigured host that fails to start cannot leak; one that starts unprotected
can.

**FR-54 `[new]`** — **No log, metric, trace, span, event, exception message, or
diagnostic sink SHALL receive**: any SIN in any form; any taxpayer display name or
employer display name; any box code, CRA line code, or amount; any OIDC token;
any session identifier or ticket key; any claim value other than an opaque
identifier; any connection string; any request URL or query string; or any
caller-supplied free text. **Only structural counts, enumerated field names, and
opaque internal identifiers (including `FirmId`) may be emitted.** No telemetry
exporter and no HTTP-logging middleware SHALL be registered.

**FR-55 `[new]`** — Every public and internal type in `Tenancy` and `Persistence`
SHALL be a sealed class or record with a **hand-written `ToString()` emitting
fixed structural facts only** — never a name, an amount, a box code, a CRA line
code, a session identifier, a caller string, or any SIN-shaped value. Every
caller-controlled and taxpayer-content property SHALL be `[JsonIgnore]`d, so a
careless direct serialization cannot become a taxpayer-data export.

**FR-56 `[new]`** — Module-originated errors SHALL use a closed set of field-name
constants and **fixed, non-interpolated messages**. A template that cannot take
an argument cannot leak a caller or payload value.

**FR-57 `[new]`** — An unhandled exception SHALL produce a fixed constant error
response containing no exception type, message, stack trace, filesystem path,
identifier, or taxpayer value, in every environment. A developer exception page,
if present, SHALL be confined to an `IsDevelopment()` branch.

**FR-58 `[new]`** — No tracked file SHALL contain a connection string, password,
key, token, certificate, or credential, and no tracked configuration file SHALL
declare a `ConnectionStrings` section at all. The connection string SHALL come
from configuration providers outside source control (environment, user-secrets,
or Key Vault). Any tracked launch profile SHALL bind to localhost only.

### Fixtures and documentation

**FR-59 `[new]`** — Every fixture, seed, and test datum SHALL be **synthetic**:
no real taxpayer information, no copied document values, no real name, address,
email, phone, SIN, account number, or employer identifier, no production
snapshot, and **no realistic nine-digit SIN-like string anywhere**. Obviously
synthetic values only ("Test Taxpayer 001", "Example Employer A", deterministic
GUIDs, toy amounts). **No fixture SHALL declare a SIN field at all.**

**FR-60 `[new]`** — Development fixture seeding SHALL be **explicitly
environment-gated and provably disabled outside Development**.

**FR-61 `[gate]` — The narrowed gate.** `src/TaxAI.Portal/CLAUDE.md` SHALL be
rewritten to state that the portal now authenticates users, holds a revocable
server-side session and a trusted firm scope, and composes a persistence layer —
**and that it still may not render taxpayer data, upload a document, mutate or
delete a stored package, compute tax, implement a CRA mapping, provision a firm
or membership, or add any second taxpayer-data surface without a further
`tax-domain` specification.** It SHALL state the no-SIN-in-any-form rule, the
firm-claim-only rule, the never-trust-a-provider-firm-claim rule, and the
server-side-revocation rule. **The gate SHALL NOT be deleted.**

**FR-62 `[new]`** — Documentation SHALL be updated as follows and no further: new
`src/Tenancy/CLAUDE.md` and `src/Persistence/CLAUDE.md` plus pointer `AGENTS.md`
files matching the established pattern; the rewritten `src/TaxAI.Portal/CLAUDE.md`
of FR-61; `README.md`, root `CLAUDE.md`, and `NEXT.md`; and a
`specs/011-.../verification.md` AC-by-AC record. **No existing module's
`CLAUDE.md` or `AGENTS.md` SHALL be edited**, and no existing module instruction
file SHALL name `TaxAI.Portal`.

### Server-side session store and revocation (R-2)

**FR-63 `[new]`** — A **durable server-side authentication session store** SHALL
be introduced: an Azure-SQL-backed session table, or an ASP.NET Core ticket store
with equivalent durability and revocation behaviour. Its contract SHALL live in
`Tenancy`; its only implementation SHALL live in `Persistence`.

`AuthenticationSession.Create(sessionId, userId, activeFirmId, createdAtUtc)`
SHALL be the public construction seam used by that implementation. It SHALL
refuse empty required identifiers, an empty present active-firm identifier, and
a non-zero-offset instant; derive the idle and absolute boundaries through
`SessionLifetimePolicy`; initialize the session as unrevoked; and expose no
caller-supplied boundary or revoked-state parameter. Persistence SHALL use
neither reflection nor internal access to construct the session.

**FR-64 `[new]`** — The session record SHALL carry at minimum: an opaque
`SessionId`; the local `UserId`; a nullable `ActiveFirmId`; `CreatedAtUtc`;
`LastSeenAtUtc`; `IdleExpiresAtUtc`; `AbsoluteExpiresAtUtc`; and `RevokedAtUtc`
or an equivalent active/revoked state. It SHALL carry **no taxpayer value, no
OIDC token, no email, no display name, and no free text.**

**FR-65 `[new]`** — Sign-in SHALL create exactly one session record. Every
subsequent request SHALL resolve the principal from that record via the opaque
ticket key in the cookie, and SHALL update `LastSeenAtUtc` and
`IdleExpiresAtUtc` — **never `AbsoluteExpiresAtUtc`, which is fixed at creation.**

**FR-66 `[new]`** — A session SHALL be refused when it is revoked, when
`evaluationInstantUtc >= IdleExpiresAtUtc`, or when
`evaluationInstantUtc >= AbsoluteExpiresAtUtc`. **Expiry SHALL be evaluated on
each request, not swept by a timer or background service**; removal of expired
rows, if implemented at all, SHALL be an explicit privileged command.

**FR-67 `[new]`** — Sign-out SHALL **revoke or delete the server-side session
record AND clear the cookie**. The revocation is the security-bearing act; the
cookie clearing is a convenience and is never relied on alone.

**FR-68 `[new]`** — A revoked session SHALL be refused on the next request. **A
copied pre-logout cookie SHALL be able neither to reuse a revoked session nor to
recreate one**: presenting a ticket key whose session is revoked, deleted, or
unknown SHALL produce an unauthenticated request and SHALL NOT create, resurrect,
or re-issue any session record.

**FR-69 `[new]`** — The session store SHALL be **durable and shared across
application instances**. It SHALL hold no process-local authoritative state, so a
session created by one instance is immediately visible to another and a
revocation performed by one instance is immediately effective on another.

**FR-70 `[new]`** — **Session-store unavailability SHALL fail closed** on every
protected taxpayer-data route: the request is refused, no principal is
established, and no cached or fallback session is used. `GET /` and `GET /health`
SHALL continue to answer, because they are constants and expose nothing.

**FR-71 `[new]`** — **No in-memory-only session store SHALL be registered outside
Development.** A host started outside Development whose session store is not the
durable implementation SHALL refuse to start.

**FR-72 `[new]`** — The R-1 membership check (FR-47) SHALL be **independent of**
the session check: a live, unrevoked, in-bounds session whose membership has been
disabled SHALL still be refused. Neither check SHALL be short-circuited by the
other.

### Provisioning prohibitions (R-3)

**FR-73 `[new]`** — No migration, seed, startup path, or configuration default
SHALL create: a default or implicit `Firm`; a default administrator or other
privileged principal; a production credential; or a `FirmMembership` outside
Development.

**FR-74 `[new]`** — **No bypass, super-user, impersonation, or
"skip-authorization" claim, header, configuration flag, or code path SHALL
exist**, in any environment.

**FR-75 `[new]`** — No firm identifier, membership, user subject, or issuer SHALL
be hard-coded in production source. Synthetic identities exist only in
development/test fixtures, behind the FR-60 environment gate.

**FR-76** — Every behaviour listed under [Preserved behaviour](#preserved-behaviour)
SHALL remain identical, and the full existing test suite SHALL remain green apart
from the spec 010 posture assertions named in [Amendments](#amendments), each of
which SHALL be replaced by a strictly stronger assertion.

---

## Preserved behaviour

Any deviation is a defect, not a delivered outcome.

1. **T4 and T5 slip parsing** — behaviour and outputs unchanged.
2. **T4 and T5 box→CRA line mapping**, including spec 004's deliberate
   `UnmappedBoxCodes` deferrals — unchanged.
3. **`Mappings/T4LineMap.2025.json` and `Mappings/T5LineMap.2025.json`** — file
   contents and embedded-resource logical names
   (`TaxLogicEngine.Mappings.T4LineMap.2025.json`,
   `TaxLogicEngine.Mappings.T5LineMap.2025.json`) unchanged.
4. **`MoneyRounding.ToCents`** half-up (`MidpointRounding.AwayFromZero`) —
   unchanged, and not invoked by this feature.
5. **`RecipientIdentifier`** masking to the last three digits — **unchanged and
   untouched**. No second masking path is introduced, and the new SIN decision
   does not alter this in-memory contract.
6. **`TaxCases`, `DocumentExtraction`, `CaseProcessing`, `CaseReview`** —
   behaviour, public contracts, assembly identities, and namespaces unchanged;
   `IDocumentExtractionIngress` still has no implementation.
7. **Every module's 2025-only tax-year policy and no-adjacent-year-fallback
   behaviour** — unchanged.
8. **`GET /` and `GET /health`** — bodies byte-for-byte, status codes, content
   types, GET-only, and anonymous access unchanged.
9. **Container validation** — validate-on-build and validate-scopes remain on
   unconditionally, outside any `IsDevelopment()` branch.
10. **One composition root.** All portal registrations stay rooted in
    `ModuleRegistration.cs`; no second registration file appears in the portal.
11. **`Program` remains the portal's only public type**; every new portal type is
    `internal`.
12. **The three pre-existing `InternalsVisibleTo` grants** — unchanged.
13. **No total project count** is asserted in any test, document, or README.
14. **No existing module's source, project file, `CLAUDE.md`, or `AGENTS.md`** is
    edited.
15. **The full existing test suite passes** — 2571 tests across six projects,
    none removed, skipped, renamed, or weakened, except the spec 010 posture
    assertions in [Amendments](#amendments), each replaced by a strictly stronger
    one.

---

## Migration constraints

- **Additive to the five tax modules.** `src/TaxLogicEngine/`, `src/TaxCases/`,
  `src/DocumentExtraction/`, `src/CaseProcessing/`, `src/CaseReview/` and their
  five test trees are **untouchable**.
- **The portal and its test project are edited deliberately**, and only for the
  changes enumerated in [Amendments](#amendments). If a design choice would
  require *relaxing* an existing portal assertion rather than *strengthening* it,
  the design is wrong, not the assertion.
- **No test may be deleted.** A superseded assertion is replaced in place by a
  stricter one, with the replacement named in the PR body.
- **Never assert a total project count** for `TaxAI.sln`, anywhere.
- **Test-manifest counts are floors, not equalities.** Registering a test project
  in `scripts/test-projects.json` with an exact-equality count would reproduce
  the "exactly N projects" trap one level down.
- **The solution is edited by appending only**: new `Project(...)`/`EndProject`
  blocks with fresh GUIDs and their configuration rows. No existing GUID,
  ordering, or configuration row changes.
- **No project may be named `Clients`, `Firms`, `Documents`, `Intake`, or
  `Review`** — spec 010's `NoPlaceholderProjectExistsForAFutureModule` forbids
  exactly those five file names.
- **No duration may be written as a five-digit second count** in portal source;
  the shipped `\b\d{5}\b` CRA-line scan would match it. Use
  `TimeSpan.FromMinutes(30)` / `TimeSpan.FromHours(8)`.
- **Stage-by-stage verification.** After each stage, `dotnet build TaxAI.sln` and
  `dotnet test TaxAI.sln` must both succeed before the next begins.
- **No tracked build output or machine-local file** — no `bin/`, `obj/`, `.vs/`,
  `*.user`, `node_modules/`, developer key ring, secret, certificate, or
  `.claude/settings.local.json` may become tracked.
- **Ordering `[ASSUMED]`** (each stage independently green):
  1. `Tenancy` contracts (Wave 0): identity/membership/session records, the
     review read-model types, repository and session-store interfaces, the
     projection signature, the retention policy signature, the UTC-instant guard,
     the field-name constants — initially with temporary
     `NotImplementedException` scaffold bodies. Before Wave 1 can complete,
     T01A replaces implementation-state sentinels with durable contract and
     boundary coverage that remains valid as concrete behaviours land.
  2. `Tenancy` behaviour: the allowlist projection, every fail-closed guard, the
     UTC-instant rejection, the session lifetime rules, the exclusive retention
     boundary calculation, and their tests.
  3. `Persistence`: `DbContext`, entity configurations, and the initial
     migration; after T14, corrective T11A fixes the three Wave 4 contract seams;
     only then do the remaining repository, durable-session-store, and purge
     implementations resume with their tests.
  4. Portal authentication, the durable ticket store, session revocation, the
     firm claim, per-request R-1 verification, and the firm-selection surface.
  5. Portal composition-root wiring, startup refusal checks, and environment-gated
     development seeding.
  6. Replacement of every superseded spec 010 assertion with its strictly
     stronger successor.
  7. Documentation: two new module `CLAUDE.md`/`AGENTS.md` pairs, the rewritten
     portal `CLAUDE.md`, `README.md`, root `CLAUDE.md`, `NEXT.md`, and
     `verification.md`.

---

## Out of scope

Each item is explicitly excluded. **Any accidental change to tax behaviour is out
of scope and is a defect, not a delivered outcome.**

- **Any change to tax semantics** — no new or changed box, CRA line, mapping,
  rate, threshold, limit, deadline, calculation, or taxpayer-domain validation.
- **The review UI and any rendering of taxpayer data** — spec 012's job.
- **Document upload, OCR, LLM, AI, Azure OpenAI, Document Intelligence,
  document ingestion, and source-document storage.** No
  `IDocumentExtractionIngress` implementation.
- **Editing any taxpayer value, recording a sign-off, or any approval action.**
  `RecordDecision`, `ReviewDecisionDescriptor`, and `ReviewSignOff` are neither
  invoked nor persisted.
- **TaxCycle export, or any export/print/email of a record.**
- **Application- or column-level encryption** — excluded by the human's decision,
  because the model already excludes SIN, DOB, address, email, and phone.
- **Production provisioning and administrative onboarding** — creating a `Firm`,
  a `User`, or a `FirmMembership` in production. Explicitly excluded, and
  explicitly **not** worked around: **no default firm, no default administrator,
  no production seed credential, no bypass or super-user claim, and no hard-coded
  development membership that exists in production** (FR-73–FR-75,
  AC-187–AC-191).
- **Role hierarchies, per-case ACLs, delegation, and impersonation.**
- **Any authorization finer than "authenticated principal with a live, unrevoked,
  in-bounds session and a trusted `taxai_firm_id` backed by an active
  membership".**
- **Deployment, IaC, container images, TLS termination, reverse proxy, CI/CD,
  monitoring, log shipping, backup configuration, and the scheduling of the purge
  in Azure.** The residency and encryption *requirements* are recorded and
  enforced by startup refusal; the *provisioning* is a later spec. **This build
  SHALL NOT be deployed to any environment that can receive real taxpayer data
  until that deployment spec exists.**
- **A second database, a read replica, sharding, distributed caching, a message
  queue, or a background service.**
- **A package index, client list, search, roster, or any listing of taxpayers.**
- **T5 persistence, multi-form packages, cross-slip or case-level totals, and any
  T1 calculation.**
- **Bilingual/French rendering, localization, accessibility programme, design
  system, component library, theming, and navigation model.**
- **Any new project beyond the two production and two test projects named**, and
  any placeholder project, page, controller, entity, or interface for a future
  module.
- **Any edit to a merged module's public contract**, including adding a
  persistence port to `CaseReview` or a mapping version to `TaxLogicEngine`.
- **Any edit to `specs/012-accountant-review-screen/prd.md`.**

---

## Acceptance criteria

Each criterion is a single mechanically-checkable condition (Given/When/Then).

### Structure, references, and boundaries

1. **`Tenancy` exists with the approved settings.** Given
   `src/Tenancy/Tenancy.csproj`, When loaded, Then its `Sdk` is
   `Microsoft.NET.Sdk`, `TargetFramework` is `net8.0`, and `Nullable` is
   `enable`. `[ASSUMED path]`
2. **`Persistence` exists with the approved settings.** Given
   `src/Persistence/Persistence.csproj`, When loaded, Then its `Sdk` is
   `Microsoft.NET.Sdk`, `TargetFramework` is `net8.0`, and `Nullable` is
   `enable`. `[ASSUMED path]`
3. **`Tenancy` references exactly two modules.** Given its project file, When its
   `ProjectReference` items are read, Then they are exactly `CaseReview` and
   `TaxLogicEngine`.
4. **`Tenancy` declares no infrastructure package.** Given its project file, When
   its `PackageReference` items are read, Then there is no EF Core, ASP.NET Core,
   SQL, Azure, OCR, LLM, or email package.
5. **`Tenancy` source names no infrastructure token.** Given `Tenancy`'s
   production source (executable lines), When scanned, Then it contains none of
   `DbContext`, `DbSet<`, `EntityFrameworkCore`, `SqlConnection`, `System.Data`,
   `Microsoft.AspNetCore`, `HttpClient`, `DateTime.Now`, `DateTime.UtcNow`,
   `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Environment.`, `File.`,
   `Directory.`.
6. **`Persistence` references exactly one module.** Given its project file, When
   its `ProjectReference` items are read, Then the only entry is `Tenancy`.
7. **`Persistence` is the only EF Core project.** Given every `.csproj` under
   `src/`, When scanned for `Microsoft.EntityFrameworkCore`, Then
   `Persistence.csproj` matches and no other production project does.
8. **`Persistence` uses the SQL Server provider and no other.** Given its project
   file, When its `PackageReference` items are read, Then
   `Microsoft.EntityFrameworkCore.SqlServer` is present with a pinned version and
   no Npgsql, MySQL, Oracle, or other database provider package is present.
9. **`Persistence` takes no ASP.NET Core dependency.** Given its project file and
   production source, When scanned, Then neither contains `Microsoft.AspNetCore`.
10. **Portal references are exactly four.** Given
    `src/TaxAI.Portal/TaxAI.Portal.csproj`, When its `ProjectReference` items are
    read, Then they are exactly `TaxLogicEngine`, `CaseProcessing`, `Tenancy`, and
    `Persistence`. *(Supersedes spec 010 AC-8.)*
11. **Portal package references are exactly one.** Given the portal project file,
    When its `PackageReference` items are read, Then there is exactly one, it is
    the OIDC authentication package, and it carries an explicit pinned 8.0.x
    version. *(Supersedes spec 010 AC-3.)* `[ASSUMED]`
12. **Portal declares no EF Core.** Given the portal project file and production
    source, When scanned, Then neither contains `EntityFrameworkCore`,
    `DbContext`, `DbSet`, `SqlConnection`, or `System.Data`.
13. **Portal names no `CaseReview` type.** Given the portal's project file and
    production source, When scanned, Then they contain none of `CaseReview`,
    `AccountantReviewPackage`, `ReviewedSlip`, `ReviewedBoxAmount`,
    `ReviewSignOff`, `ReviewPackageAssembler`, and the compiled portal assembly's
    referenced assemblies do not include `CaseReview`.
14. **No reverse arrow.** Given every `.csproj` and hand-written `.cs` file under
    `src/TaxLogicEngine`, `src/TaxCases`, `src/DocumentExtraction`,
    `src/CaseProcessing`, `src/CaseReview`, `src/Tenancy`, `src/Persistence` and
    their test trees, When each is read, Then none contains the string
    `TaxAI.Portal`.
15. **`Persistence` is not referenced by `Tenancy`.** Given `Tenancy`'s project
    file and source, When scanned, Then neither names `Persistence`.
16. **No new project has a forbidden name.** Given the repository, When all
    `.csproj` files are listed, Then no file is named `Clients.csproj`,
    `Firms.csproj`, `Documents.csproj`, `Intake.csproj`, or `Review.csproj`.
17. **Solution declares each new project exactly once.** Given `TaxAI.sln`, When
    its `Project(` lines are counted, Then each of the four new projects appears
    exactly once at its approved path. *(No total project count is asserted.)*
18. **Existing solution entries are untouched.** Given `TaxAI.sln`, When compared
    with `master`, Then every pre-existing `Project(` line, GUID, and
    configuration row is unchanged.
19. **Test projects use the standard tooling versions.** Given
    `tests/Tenancy.Tests/` and `tests/Persistence.Tests/` project files, When
    their package references are read, Then `xunit` is 2.5.3,
    `xunit.runner.visualstudio` is 2.5.3, `Microsoft.NET.Test.Sdk` is 17.8.0, and
    `coverlet.collector` is 6.0.0.
20. **New test projects are registered in the test-execution manifest.** Given
    `scripts/test-projects.json`, When it is read, Then every test project under
    `tests/` — including `Tenancy.Tests` and `Persistence.Tests` — appears in it
    exactly once.
21. **No pre-existing module file changed.** Given `git diff` against the merge
    base, When the changed-file list is read, Then it contains no path under
    `src/TaxLogicEngine/`, `src/TaxCases/`, `src/DocumentExtraction/`,
    `src/CaseProcessing/`, `src/CaseReview/`, or their five test trees.
22. **No new internals grant on a pre-existing project.** Given every
    pre-existing production project file and production source file, When scanned
    for the `[assembly: InternalsVisibleTo(` attribute, Then exactly the three
    pre-existing grants are found.
23. **No generic repository abstraction.** Given `Tenancy`'s and `Persistence`'s
    public and internal types, When inspected by reflection, Then no generic
    interface or class named `*Repository*` with a type parameter exists, and no
    member returns `IQueryable`, `DbSet<>`, or `IEnumerable<object>`.

### Regression — existing behaviour stays green

24. **Full suite green.** Given the repository after this feature, When
    `dotnet test TaxAI.sln` runs, Then it exits 0 and every test in
    `TaxLogicEngine.Tests`, `TaxCases.Tests`, `DocumentExtraction.Tests`,
    `CaseProcessing.Tests`, and `CaseReview.Tests` passes with none removed,
    skipped, renamed, or weakened.
25. **No tax mapping, calculation, or parser behaviour changed.** Given the five
    modules' parser, mapper, provider, rounding, review-assembly, PII, and
    year-isolation suites, When they run, Then every one passes unchanged, and
    each module's exported type full-name set is identical to its pre-feature
    snapshot.
26. **Embedded mapping resources untouched.** Given the built `TaxLogicEngine`
    assembly, When its manifest resource names are enumerated, Then
    `TaxLogicEngine.Mappings.T4LineMap.2025.json` and
    `TaxLogicEngine.Mappings.T5LineMap.2025.json` are present with exactly those
    names.
27. **No module gained a package or framework reference.** Given the five
    pre-existing `src/*.csproj` files, When their `PackageReference`,
    `FrameworkReference`, `EmbeddedResource`, and `Content` items are read, Then
    each set is identical to `master`'s.
28. **Every superseded spec 010 assertion has a named strictly stronger
    replacement.** Given the [Amendments](#amendments) table, When each superseded
    assertion is looked up in the portal test suite, Then a replacement test
    exists that asserts a strictly narrower condition, and no superseded
    assertion was deleted without one.
29. **`/health` unchanged.** Given the started host with no credential, When
    `GET /health` is requested, Then the status is 200, the body equals `Healthy`
    exactly, the content type is `text/plain; charset=utf-8`, and the body
    contains no digit.
30. **`/` unchanged.** Given the started host with no credential, When `GET /` is
    requested, Then the status is 200, the content type is HTML, and the body
    equals the spec 010 shell document byte-for-byte.
31. **Container validation still unconditional.** Given the portal's `Program.cs`
    executable lines, When scanned, Then `ValidateOnBuild = true` and
    `ValidateScopes = true` appear and neither is inside a conditional block.
32. **Every `IsDevelopment` branch guards an approved concern.** Given the
    portal's production source, When every `IsDevelopment` occurrence is located,
    Then each guards only a developer key ring, development fixture seeding, or a
    developer exception page — and none guards an authentication, authorization,
    antiforgery, cookie, session-store, membership-verification, tenancy, or
    container-validation control.
33. **`Program` is still the portal's only public type.** Given the portal
    assembly, When `GetExportedTypes()` is read, Then it is exactly `["Program"]`.
34. **Ingress still unimplemented.** Given the started host, When
    `IDocumentExtractionIngress` is requested with a null-returning lookup, Then
    the result is null, and no type in the portal, `Tenancy`, or `Persistence`
    implements it.

### Happy path

35. **A supported projected record is persisted and read back.** Given a trusted
    `FirmId` `F` and a successful `ReviewPackageProjection.Project` result for a
    2025 `AccountantReviewPackage` with `FirmId == F`, one reviewed slip with two
    line entries, three box amounts, and one unmapped code, When the projected
    `PersistedReviewRecord` is saved and then read back by
    `(F, record identifier)`, Then a record is returned whose `TaxYear`, ordered
    line codes and amounts, ordered box codes and amounts, and ordered unmapped
    codes are exactly equal to the projection output. The repository API names
    no executable `CaseReview` type.
36. **Sign-in with exactly one active membership yields a firm claim.** Given an
    authenticated principal whose user has exactly one active `FirmMembership` in
    firm `F`, When sign-in completes, Then the local principal carries exactly one
    `taxai_firm_id` claim whose value is the canonical `FirmId` of `F`, and the
    session record's `ActiveFirmId` equals it.
37. **Firm switch issues a new session.** Given an authenticated user with active
    memberships in `F1` and `F2` and a session whose `ActiveFirmId` is `F1`, When
    a valid antiforgery-protected `POST /firms/select` for `F2` is made, Then a
    **new** session record exists with a new `SessionId` and `ActiveFirmId == F2`,
    and the previous session record is revoked.
38. **Retention is stamped on save.** Given a 2025 package saved at any supplied
    evaluation instant, When the stored record is read, Then `TaxYear` is 2025,
    `PackageVersion` is 1, `CreatedAtUtc` equals the supplied instant, and
    `RetentionUntilUtc` equals `2033-01-01T00:00:00Z`.
39. **A regenerated package creates a new version.** Given a stored record at
    `PackageVersion` 1 for subject key `K`, When a second package for `K` is
    saved, Then a record at `PackageVersion` 2 exists **and** the version 1 record
    is byte-unchanged in every column.

### Fail-closed rejections — one AC per rule

40. **Null package.** Given a trusted `FirmId`, When
    `ReviewPackageProjection.Project` is called with a null package, Then it is
    refused with at least one validation error and no repository call is made.
41. **No trusted firm parameter exists to omit.** Given every repository, purge,
    and projection type in `Tenancy` and `Persistence`, When their methods are
    read by reflection, Then every lookup, save, and delete declares a required
    firm-identifier parameter and **no overload, optional parameter, or default
    omits it**, except exactly the four enumerated identity-bootstrap operations
    of FR-21.
42. **Package firm mismatch.** Given trusted `FirmId` `F1` and a package whose
    `FirmId` is `F2`, When projection is called, Then it is refused, no
    persistence record is produced, and nothing is written.
43. **Unsupported tax year — backward.** Given a package whose `TaxYear` is 2024,
    When projection is called, Then it is refused and nothing is written.
44. **Unsupported tax year — forward.** Given a package whose `TaxYear` is 2026,
    When projection is called, Then it is refused identically — no adjacent-year
    fallback, no coercion, no "closest supported year".
45. **Slip/package year divergence.** Given a 2025 package containing a slip whose
    `TaxYear` is 2024, When projection is called, Then it is refused and nothing is
    written.
46. **Amount that cannot round-trip.** Given a package containing a monetary
    amount whose exact value cannot be stored and reloaded equal under the
    configured precision and scale, When projection is called, Then it is refused and
    nothing is written — never rounded, never truncated.
47. **Over-length string.** Given a package whose taxpayer display name exceeds
    the declared column length by one character, When projection is called, Then it is
    refused and nothing is written — never truncated.
48. **Nine-digit run in taxpayer content.** Given a package whose taxpayer display
    name contains nine consecutive digits, When projection is called, Then it is refused
    and nothing is written.
49. **Non-opaque `CaseId`.** Given a package whose `CaseId` violates the FR-25
    opacity guard (empty, over-length, or containing a character outside the safe
    set), When projection is called, Then it is refused before anything is written.
50. **Non-opaque `ParticipantId`.** Given the same violation on `ParticipantId`,
    When projection is called, Then it is refused before anything is written.
51. **Unknown slip type discriminator.** Given a package carrying a slip type
    `ReviewPackageProjection` does not recognize, When projection is called,
    Then it is refused, no persistence record is produced, and nothing is
    written.
52. **Cross-firm read is indistinguishable from not-found.** Given a record owned
    by `F1` at identifier `R` and a read by trusted firm `F2`, When the read
    executes, Then the result is byte-identical to a read of an identifier that
    exists nowhere, and no identifier is echoed in any error.
53. **No firm claim.** Given an authenticated principal carrying no
    `taxai_firm_id` claim, When any firm-scoped operation is attempted, Then it is
    refused and no record content is returned.
54. **Unparseable firm claim.** Given an authenticated principal whose
    `taxai_firm_id` claim value is not a canonical `FirmId`, When any request is
    made, Then it is refused and the local session is not treated as firm-scoped.
55. **Provider-supplied firm claim is never trusted.** Given an OIDC token
    containing a `taxai_firm_id` claim naming firm `F2` and a user whose only
    active membership is in `F1`, When sign-in completes, Then the local principal
    carries no claim naming `F2`, and no firm-scoped operation resolves to `F2`.
56. **Firm identifier in the query string is ignored.** Given a session whose
    `ActiveFirmId` is `F1`, When a request adds `?firmId=F2`, Then the response is
    byte-identical to the request without it.
57. **Firm identifier in a header is ignored.** Given the same session, When a
    request adds a header naming `F2`, Then the response is byte-identical to the
    request without it.
58. **Firm identifier in a cookie is ignored.** Given the same session, When a
    request adds a cookie naming `F2`, Then the response is byte-identical to the
    request without it.
59. **No code path reads a firm identifier from a request.** Given the portal's
    production source, When scanned, Then no expression reads a firm identifier
    from `RouteData`, `Request.Query`, `Request.Form`, `Request.Body`,
    `Request.Headers`, or `Request.Cookies` for use as an authorization boundary.
60. **Firm switch to a firm without membership.** Given an authenticated user with
    no membership in `F3`, When a valid antiforgery-protected
    `POST /firms/select` for `F3` is made, Then it is refused, no new session is
    created, and the existing session is unchanged.
61. **Firm switch to a disabled membership.** Given a user whose membership in
    `F2` is disabled, When the same request is made for `F2`, Then it is refused
    and no new session is created.
62. **Firm switch without antiforgery.** Given an authenticated user with an
    active membership in `F2`, When `POST /firms/select` is made without a valid
    antiforgery token, Then it is refused and no new session is created.
63. **Zero active memberships.** Given a user with no active membership, When
    sign-in completes, Then the session record's `ActiveFirmId` is null, the
    principal carries no `taxai_firm_id` claim, and every firm-scoped operation is
    refused.
64. **More than one active membership.** Given a user with two active memberships,
    When sign-in completes, Then the session record's `ActiveFirmId` is null and
    the principal carries **no** `taxai_firm_id` claim — the application chooses
    neither firm — and every firm-scoped operation is refused until an explicit
    selection succeeds.
65. **Membership disabled mid-session.** Given a live session whose
    `ActiveFirmId` is `F1` and whose membership is then disabled, When the next
    protected request is made, Then it is refused — without waiting for the idle
    or absolute timeout.
66. **Unauthenticated request to an authenticated route.** Given no credential,
    When `GET /firms` is requested, Then the response is an authentication
    challenge or 401 and contains no firm name, membership identifier, or record
    content.
67. **Idle timeout.** Given a session with no activity for longer than the
    30-minute idle timeout, When a request is made, Then it is refused and
    reauthentication is required.
68. **Absolute lifetime.** Given a session kept continuously active for longer
    than the 8-hour absolute maximum, When a request is made, Then it is refused
    and reauthentication is required — sliding expiration never extends past the
    absolute maximum.
69. **Logout.** Given an established session, When `POST /sign-out` succeeds and a
    subsequent request is made with the returned cookie state, Then the request is
    unauthenticated.
70. **Purge has no firm-omitting path.** Given the purge contract, When its
    methods are read by reflection, Then every one declares a required trusted
    firm parameter and no overload omits it.
71. **Purge spares unexpired records.** Given a 2025 record and an evaluation
    instant of `2032-12-31T23:59:59.9999999Z`, When purge runs for its firm, Then
    the record still exists.
72. **Purge spares other firms.** Given an expired record owned by `F1` and a
    purge run scoped to `F2`, When purge runs, Then the `F1` record still exists.
73. **No user-reachable delete.** Given the portal's mapped endpoints and the
    repository contracts reachable from them, When enumerated, Then no operation
    deletes or updates a persisted review record.
74. **Startup refuses without a connection string.** Given the host started as
    Production with no configured connection string, When it starts, Then startup
    fails and no request is served.
75. **Startup refuses without encrypted transport.** Given the host started as
    Production with a connection string that does not require encryption with
    certificate validation, When it starts, Then startup fails.
76. **Startup refuses without a key ring.** Given the host started as Production
    with no configured Data Protection key ring, When it starts, Then startup
    fails. **Strengthened by B-6:** it is not enough that the key-ring
    configuration values are absent — startup SHALL also fail when the values are
    present but the built service provider has no `IDataProtectionProvider`, when
    `KeyManagementOptions.XmlRepository` or `XmlEncryptor` is null, or when
    either is an implementation supplied by the ASP.NET Core Data Protection
    framework assembly. Startup SHALL succeed, for this criterion, only when both
    are explicitly registered non-framework implementations.
77. **Seeding is disabled outside Development.** Given the host started as
    Production with a seedable store, When startup completes (with FR-53's
    prerequisites satisfied), Then no fixture row was written.

### Boundaries

78. **Half-cent amount, positive, is stored exactly.** Given a package carrying a
    box amount of `1.005m`, When it is saved and reloaded, Then the reloaded value
    equals `1.005m` exactly — the persistence layer neither rounds to `1.01` nor
    to `1.00`. *(The engine rounds half-up away from zero via `MoneyRounding`; the
    store must be provably incapable of rounding at all.)*
79. **Half-cent amount, negative, is stored exactly.** Given a box amount of
    `-1.005m`, When it is saved and reloaded, Then the reloaded value equals
    `-1.005m` exactly and retains its sign.
80. **Zero amount is stored as zero.** Given a line entry with amount `0m`, When
    saved and reloaded, Then the stored value is `0m` and the row exists — never
    omitted, never null.
81. **Empty package.** Given a package with zero reviewed slips, When saved and
    reloaded, Then a record exists with zero persisted slips and is not reported
    as an error.
82. **Empty unmapped-code list.** Given a slip with an empty `UnmappedBoxCodes`,
    When saved and reloaded, Then the stored unmapped-code collection is empty and
    is distinguishable from an unstored one.
83. **Absent optional slip fields.** Given a slip whose `EmployerAddress`,
    `EmployerAccountNumber`, `RppDpspRegistrationNumber`, `ProvinceOfEmployment`,
    and `EmploymentCode` are null, When saved, Then the save succeeds and no
    column exists for any of them.
84. **Order preserved across a round trip.** Given a package with three slips,
    each with several line entries, box amounts, and unmapped codes in a known
    order, When saved and reloaded, Then each collection is returned in exactly
    the package's original order, with no item added, removed, reordered, or
    merged.
85. **Duplicate CRA line codes are not aggregated.** Given two slips whose line
    entries carry the same CRA line code, When saved and reloaded, Then two
    separate rows exist and no combined total is stored anywhere.

### PII

86. **No SIN column exists.** Given the EF model and the generated migration DDL,
    When every column name and type is enumerated, Then no column is named for or
    typed as a SIN, a partial SIN, a social insurance number, a date of birth, an
    address, an email, or a phone number.
87. **No SIN-presence flag exists.** Given the EF model, When enumerated, Then no
    column records whether a slip had a SIN.
88. **The projection never reads a SIN.** Given `Tenancy`'s production source
    (executable lines), When scanned, Then it contains no `.Sin`,
    `RecipientIdentifier`, `SocialInsurance`, `Masked`, `SafeDiagnosticText`, or
    any masking, redaction, sanitizing, or scrubbing helper.
89. **No nine-digit run reaches the database.** Given a package whose
    `Slip.Sin` is `123456789` and whose other values are synthetic, When it is
    saved and every persisted string and identifier column of every affected row is
    read, Then no value contains any nine-consecutive-digit substring.
90. **No SIN in any exception.** Given a package with a SIN and a deliberately
    failing save, When the exception and its full inner chain are flattened to
    text, Then no nine-digit run and no `Sin` value appears.
91. **No SIN or taxpayer value in any log.** Given an in-memory logger captured
    across a full successful save, a full read, a full sign-in, and a full purge
    run, When every log line is scanned, Then none contains a nine-digit run, a
    display name, an employer name, an amount, a box code, a CRA line code, a
    session identifier, a claim value, an OIDC token, or a connection string.
92. **No taxpayer value in any error message.** Given every module-originated
    `ValidationError` produced by every fail-closed rule above, When its message
    is read, Then it is a fixed non-interpolated constant containing no caller or
    package value.
93. **`ToString()` leaks nothing.** Given every public and internal instance type
    in `Tenancy` and `Persistence` populated with sentinel values, When
    `ToString()` is called, Then the output contains no sentinel, no amount, no
    box code, no CRA line code, no session identifier, and no nine-digit run.
94. **Direct serialization leaks nothing.** Given a fully populated persistence
    entity with sentinel string values, When it is serialized with the default
    JSON serializer, Then no sentinel appears in the output.
95. **No SIN or taxpayer PII in the portal.** Given the portal's production
    source, When scanned, Then it contains no SIN-related identifier, no
    `RecipientIdentifier`, no masking or redaction helper, no nine-digit literal,
    and no taxpayer name or address field name.
96. **No taxpayer data in any response body.** Given the started host, When the
    bodies of every mapped route are read for an authenticated principal with a
    firm claim, Then none contains a nine-digit run, a taxpayer display name, an
    employer name, an amount, a box code, or a CRA line code.
97. **No taxpayer data in a URL.** Given every request the portal can generate or
    accept, When each URI is inspected, Then it contains no name, address, amount,
    box code, CRA line code, SIN, firm id, case id, participant id, session id, or
    record identifier.
98. **No token or taxpayer value in a cookie.** Given a completed sign-in and a
    completed firm switch, When every `Set-Cookie` header is inspected, Then none
    contains an OIDC access, refresh, or id token, a taxpayer value, a `FirmId`, a
    `UserId`, or a nine-digit run.
99. **OIDC tokens are not persisted.** Given a completed sign-in, When the
    database, the session record, the authentication cookie, and every log are
    inspected, Then no OIDC access, refresh, or id token appears in any of them.
100. **No email or phone at rest.** Given the user, membership, and session
     entities in the EF model and the generated DDL, When enumerated, Then no
     column stores an email address, phone number, display name, or provider
     profile claim.
101. **Fixtures contain no SIN-shaped value.** Given every fixture, seed, and test
     data file added by this feature, When scanned, Then none contains a
     nine-consecutive-digit substring, a SIN field name, a real-looking postal
     address, an email address, or a phone number.
102. **Fixtures are obviously synthetic.** Given every seeded and fixture taxpayer
     display name and employer display name, When read, Then each matches the
     approved synthetic pattern and none is a plausible real person or company
     name. `[ASSUMED pattern]`

### Tax-year isolation

103. **Year N is never stored under year N±1.** Given a 2025 package saved for
     firm `F`, When the stored record is read, Then its `TaxYear` is 2025 and no
     record with `TaxYear` 2024 or 2026 was created.
104. **A record for year N is never returned for a year-N±1 query.** Given stored
     records and a read scoped to a tax year, When a year other than the record's
     is requested, Then no record is returned and none is coerced.
105. **No tax-year literal in the new modules.** Given `Tenancy`'s and
     `Persistence`'s production source (executable lines), When scanned for a
     four-digit year literal in `(19|20)\d{2}`, Then there are zero matches, and
     the only tax-year decision delegates to `SupportedReviewTaxYears`.
106. **No supported-year policy is redeclared.** Given `Tenancy` and
     `Persistence`, When their types are enumerated, Then neither declares a
     supported-tax-year list, a "current year" concept, or a year-fallback rule.
107. **Retention is derived from the record's year alone.** Given two otherwise
     identical packages differing only in `TaxYear`, When both are saved at the
     same supplied instant, Then their `RetentionUntilUtc` values differ by
     exactly one year and neither depends on the instant.
108. **No clock is read in `Tenancy`.** Given `Tenancy`'s production source
     (executable lines), When scanned, Then it contains no `DateTime.Now`,
     `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, or
     `TimeProvider`.

### Data-driven mapping and no tax computation

109. **No CRA line number is hardcoded.** Given the production source of
     `Tenancy`, `Persistence`, and `TaxAI.Portal` (executable lines only), the
     generated migration files, and every seed and fixture file, When scanned for
     a five-digit integer literal, Then there are zero matches.
110. **Every stored CRA line code came from the package.** Given a saved package,
     When every stored CRA line code is compared with the package's
     `LineEntries`, Then each is present in the package and no code appears that
     the package did not carry.
111. **No box→line authority.** Given the production source of `Tenancy`,
     `Persistence`, and `TaxAI.Portal`, When scanned, Then there is no box→line
     dictionary or lookup table, no box-code literal, no
     `TaxLogicEngine.Mappings` resource name, and no `GetManifestResourceStream`.
112. **No engine invocation.** Given the same source, When scanned, Then it names
     none of `T4SlipParser`, `T5SlipParser`, `T4LineMapper`, `T5LineMapper`,
     `T4SlipBoxAmounts`, `T5SlipBoxAmounts`, `MoneyRounding`,
     `ReviewPackageAssembler`, or `RecordDecision`.
113. **No arithmetic on a monetary value.** Given the same source, When scanned,
     Then there is no `Math.Round`, no `MidpointRounding`, no `double`, no
     `float`, and no `+`, `-`, `*`, `/`, or compound assignment applied to a
     monetary value.
114. **The portal holds no monetary type.** Given the portal's production source,
     When scanned, Then it contains no `decimal`, `double`, or `float` token.
115. **No package type is mapped as an entity.** Given the EF model, When every
     entity, owned type, and complex type is enumerated, Then none is
     `AccountantReviewPackage`, `ReviewedSlip`, `ReviewedBoxAmount`,
     `TaxLineEntry`, `T4Slip`, `T5Slip`, `ProcessedT4Extraction`,
     `ValidatedExtraction`, or `TaxCase`.
116. **No package type is serialized to a column.** Given the EF model and every
     value converter, When inspected, Then no converter serializes a
     `CaseReview`, `CaseProcessing`, `DocumentExtraction`, or `TaxLogicEngine`
     type to JSON, XML, or binary.

### Schema and tenancy at the storage layer

117. **Every tenant-owned table has `FirmId`.** Given the EF model, When every
     entity that carries taxpayer content or tenant-owned state is enumerated,
     Then each declares a non-nullable `FirmId` property mapped to a
     non-nullable column.
118. **`FirmId` leads every tenant index and constraint.** Given the EF model,
     When every index and unique constraint on a tenant-owned entity is read,
     Then `FirmId` is present and is the first column. `[ASSUMED ordering]`
119. **`FirmId` is collation-independent.** Given the generated DDL, When the
     `FirmId` column type is read, Then it is `uniqueidentifier`. `[ASSUMED]`
120. **Identifier columns declare an explicit binary collation.** Given the
     generated DDL, When every string identifier column is read, Then each
     declares an explicit case- and accent-sensitive binary collation.
     `[ASSUMED]`
121. **Monetary columns declare explicit precision and scale.** Given the EF
     model, When every `decimal` property is read, Then each declares an explicit
     precision and scale and none relies on the provider default.
122. **Snapshot uniqueness includes version.** Given the EF model, When the review
     record's unique constraints are read, Then
     `(FirmId, CaseId, ParticipantId, TaxYear, PackageVersion)` is unique.
     `[ASSUMED key]`
123. **The persistence model declares exactly the allowlisted properties.** Given
     the EF model's review-record entities, When every mapped property name is
     enumerated, Then the set equals the FR-22 allowlist exactly — nothing more,
     nothing less.
124. **Schema comes only from migrations.** Given `Persistence` and the portal's
     production source, When scanned, Then neither calls `EnsureCreated`,
     `Migrate`, or any schema-mutating API at startup.
125. **No migration or seed contains a taxpayer value.** Given every file under
     `src/Persistence/Migrations/` and every seed file, When scanned, Then none
     contains a person name, address, email, phone, nine-digit run, CRA line
     code, or box code.
126. **A cross-firm write cannot be constructed.** Given a projection executed
     with trusted firm `F1`, When its output is passed to the repository under
     the same required trusted scope and the stored row's `FirmId` is read, Then
     it equals `F1`; a package carrying any other firm produced no projected
     record to save.

### Routing, transport, and diagnostics

127. **Exact route inventory.** Given the started host's endpoint data sources,
     When the application's own route endpoints are enumerated, Then they are
     exactly the FR-48 set, each with exactly its declared HTTP methods.
     *(Supersedes spec 010 AC-26.)* `[ASSUMED paths]`
128. **No route parameter is an authorization boundary.** Given every mapped route
     pattern, When its parameters are enumerated, Then none is named for or bound
     to a firm, case, participant, document, session, or record identifier.
129. **Anonymous routes are exactly the enumerated set.** Given every mapped
     endpoint, When its authorization metadata is read, Then exactly `/`,
     `/health`, `/sign-in`, and the OIDC callback paths allow anonymous access and
     every other endpoint requires authorization.
130. **State-changing routes require antiforgery.** Given `POST /firms/select` and
     `POST /sign-out`, When each is requested without a valid antiforgery token,
     Then each is refused.
131. **Cookie flags.** Given a completed sign-in, When the authentication cookie
     is inspected, Then it is `HttpOnly`, `Secure`, and `SameSite=Lax`.
132. **Errors leak nothing, in every environment.** Given the host started as
     Production and again as Staging, When an unhandled exception occurs on any
     route, Then the response contains no exception type, message, stack trace,
     filesystem path, identifier, or taxpayer value.
133. **Developer exception page confined.** Given the portal's production source,
     When scanned, Then any developer-exception-page call sits inside an
     `IsDevelopment()` branch, or is absent.
134. **No telemetry or HTTP logging.** Given the portal's source and
     configuration, When scanned, Then there is no `AddHttpLogging`,
     `UseHttpLogging`, `AddW3CLogging`, Application Insights, or OpenTelemetry
     registration.
135. **No secret in a tracked file.** Given every tracked configuration, launch
     profile, migration, seed, and fixture file, When read, Then none contains a
     connection string, `ConnectionStrings` section, password, key, token, or
     certificate.
136. **Tracked URLs bind to localhost.** Given every tracked configuration and
     launch-profile file under the portal, When every URL is read, Then each
     host is `localhost`, `127.0.0.1`, or `[::1]`.
137. **No developer key ring is tracked.** Given the repository's tracked files,
     When listed, Then no Data Protection key file, `bin/`, `obj/`, `.vs/`,
     `*.user`, certificate, or `.claude/settings.local.json` is tracked.
138. **No client-side stack.** Given the repository, When listed, Then there is no
     `package.json`, no `node_modules`, no SPA framework asset, no
     `src/TaxAI.Portal/wwwroot`, and no tracked minified bundle.
139. **No background service.** Given the portal's, `Tenancy`'s, and
     `Persistence`'s production source, When scanned, Then there is no
     `AddHostedService`, `BackgroundService`, `IHostedService`, or scheduler
     registration — the purge and any session cleanup are invoked explicitly,
     never on a timer.
140. **Purge output is structural only.** Given a purge run that deletes records,
     When its returned result and every emitted log line are read, Then they
     contain only counts and opaque identifiers — no display name, employer name,
     amount, box code, CRA line code, or nine-digit run.

### Documentation

141. **New module instructions exist.** Given `src/Tenancy/CLAUDE.md` and
     `src/Persistence/CLAUDE.md`, When read, Then each states its module's
     boundary, its exact reference set, its forbidden dependencies, its PII
     posture, and that it declares no tax-year policy of its own.
142. **Pointer bridges.** Given `src/Tenancy/AGENTS.md` and
     `src/Persistence/AGENTS.md`, When read, Then each is fewer than 15 lines and
     links `../../AGENTS.md`, `../../CLAUDE.md`, and its local `CLAUDE.md`.
143. **The portal gate is narrowed, not deleted.** Given
     `src/TaxAI.Portal/CLAUDE.md`, When read, Then it states the four-reference
     set, the no-SIN-in-any-form rule, the firm-claim-only rule, the
     never-trust-a-provider-firm-claim rule, the server-side-revocation rule, that
     the UI computes no tax and implements no CRA mapping, that the portal holds
     no tax-year policy, and that rendering taxpayer data, uploading a document,
     mutating a stored package, provisioning a firm or membership, or adding a
     second taxpayer-data surface still requires a further `tax-domain`
     specification.
144. **No existing module instruction file changed.** Given `git diff` against the
     merge base, When the changed-file list is read, Then no `CLAUDE.md` or
     `AGENTS.md` under `src/TaxLogicEngine/`, `src/TaxCases/`,
     `src/DocumentExtraction/`, `src/CaseProcessing/`, or `src/CaseReview/`
     appears in it.
145. **README describes what exists.** Given `README.md`, When read, Then it
     documents every project tree including the two new ones, describes sign-in,
     revocable sessions, firm scoping, the database, and retention, states that no
     taxpayer-data screen ships and that no production provisioning ships, asserts
     no project count, and claims no capability the build lacks.
146. **Root instructions record the change.** Given root `CLAUDE.md`, When read,
     Then its repository-layout section lists `src/Tenancy/`, `src/Persistence/`,
     `tests/Tenancy.Tests/`, and `tests/Persistence.Tests/`; its current-state
     section records spec 011's delivered scope, the portal's new reference set,
     the narrowed gate, and the reversed SIN decision; and it still contains
     "Never assert a total project count".
147. **The roadmap records what is now unblocked and what is not.** Given
     `NEXT.md`, When read, Then it records spec 011's delivered scope, that spec
     010's questions A, B, C (SIN only), D, and E are answered and F remains
     reserved, and that rendering taxpayer data remains a separate `tax-domain`
     specification.

### Retention boundary semantics (BQ-1)

148. **Exclusive boundary value.** Given a 2025 record, When `RetentionUntilUtc`
     is read, Then it equals `2033-01-01T00:00:00Z` exactly, with a zero UTC
     offset.
149. **One tick before the boundary is not purge-eligible.** Given a 2025 record
     and an evaluation instant of `2032-12-31T23:59:59.9999999Z`, When the purge
     eligibility predicate is evaluated, Then it is false.
150. **The boundary instant itself is purge-eligible.** Given a 2025 record and an
     evaluation instant of exactly `2033-01-01T00:00:00Z`, When the predicate is
     evaluated, Then it is true and purge deletes the record.
151. **Later instants are purge-eligible.** Given a 2025 record and an evaluation
     instant of `2033-01-01T00:00:00.0000001Z`, When the predicate is evaluated,
     Then it is true.
152. **No inclusive end-of-day value is modelled.** Given `Tenancy`'s and
     `Persistence`'s production source, the generated migrations, and every seed
     file, When scanned, Then none contains `23:59:59`, `.9999999`, or an
     `AddTicks(-1)`/`AddMilliseconds(-1)` construction applied to a retention or
     session boundary.
153. **Non-UTC instant is rejected.** Given an evaluation or creation instant
     whose offset is not zero, When it is supplied to any `Tenancy` operation,
     Then the operation is refused and nothing is written or deleted — never
     converted.
154. **Unavailable `DateTime.Kind` provenance is not inferred.** Given two equal
     zero-offset `DateTimeOffset` values, one constructed from
     `DateTimeKind.Utc` and one constructed from `DateTimeKind.Unspecified`, When
     each is supplied to `UtcInstantGuard`, Then both are accepted because
     `DateTimeOffset` retains no originating-kind provenance. A non-zero offset
     remains refused under AC-153 and is never normalized. A future API accepting
     `DateTime` directly may reject `DateTimeKind.Unspecified` before construction;
     that separate API is outside T03.
155. **Persisted instants are UTC.** Given every stored `CreatedAtUtc`,
     `UpdatedAtUtc`, `RetentionUntilUtc`, `LastSeenAtUtc`, `IdleExpiresAtUtc`,
     `AbsoluteExpiresAtUtc`, and `RevokedAtUtc`, When each is read back, Then its
     offset is zero.
156. **The exclusive meaning is documented.** Given the declaration of
     `RetentionUntilUtc` in `Tenancy`, its EF configuration, and the migration,
     When read, Then each states that the boundary is **exclusive**.

### Per-request verification (R-1)

157. **Inactive local user is refused.** Given a live session whose local user has
     been marked inactive, When either a tenancy-bootstrap request or a normal
     firm-scoped protected request is made, Then it is refused and no record
     content is returned.
158. **Missing membership is refused.** Given a live session whose
     `FirmMembership` row no longer exists, When a protected request is made, Then
     it is refused.
159. **Membership/session firm mismatch is refused.** Given a live session whose
     `ActiveFirmId` no longer matches the user's membership `FirmId`, When a
     protected request is made, Then it is refused.
160. **Identity- or membership-store failure fails closed.** Given the
     user-by-id or membership lookup throws or times out, When a bootstrap or
     firm-scoped protected request is made, Then it is refused with the fixed
     error response and no record content, and no cached authorization decision
     is used.
161. **Exempt routes skip membership verification.** Given a membership store that
     throws on every call, When `GET /`, `GET /health`, `GET /sign-in`, and the
     OIDC callback path are requested, Then each still behaves exactly as
     specified.

### Server-side session store and revocation (R-2)

162. **Session record declares the required fields.** Given the session entity in
     the EF model, When its properties are enumerated, Then `SessionId`, `UserId`,
     `ActiveFirmId` (nullable), `CreatedAtUtc`, `LastSeenAtUtc`,
     `IdleExpiresAtUtc`, `AbsoluteExpiresAtUtc`, and a revoked-state property are
     all present.
163. **The cookie carries only an opaque ticket key.** Given a completed sign-in,
     When the authentication cookie's decrypted payload is inspected, Then it
     contains a ticket key or session identifier and no claims payload, no
     `FirmId`, no `UserId`, no OIDC token, and no taxpayer value.
164. **Sign-in creates exactly one session record.** Given a user with one active
     membership, When sign-in completes, Then exactly one unrevoked session row
     exists for that user.
165. **The session record holds no OIDC token.** Given a completed sign-in, When
     every column of the session row is read, Then none contains an access,
     refresh, or id token.
166. **Claim equals the session record.** Given a live session, When the
     materialized `taxai_firm_id` claim is compared with the session record's
     `ActiveFirmId`, Then they are ordinally equal.
167. **Claim/session divergence fails closed.** Given a principal whose
     `taxai_firm_id` claim does not match the session record's `ActiveFirmId`,
     When a protected request is made, Then it is refused.
168. **Idle boundary is enforced from the record.** Given a session whose
     `IdleExpiresAtUtc` has been reached, When a request is made, Then it is
     refused regardless of the cookie's own expiry.
169. **Absolute boundary is enforced from the record.** Given a session whose
     `AbsoluteExpiresAtUtc` has been reached, When a request is made, Then it is
     refused regardless of the cookie's own expiry.
170. **Activity never extends the absolute boundary.** Given a session and a
     sequence of requests spread across several hours, When
     `AbsoluteExpiresAtUtc` is read after each, Then its value is identical to the
     value set at creation.
171. **Activity does extend the idle boundary.** Given a session and a request
     made before its idle boundary, When `IdleExpiresAtUtc` is read afterwards,
     Then it has advanced and does not exceed `AbsoluteExpiresAtUtc`.
172. **Logout revokes the server-side session.** Given an established session,
     When `POST /sign-out` succeeds, Then the session row is revoked or deleted
     and the response clears the cookie.
173. **A revoked session is refused on the next request.** Given a session revoked
     out of band, When the next request is made with the same cookie, Then it is
     unauthenticated.
174. **A copied pre-logout cookie cannot reuse a revoked session.** Given a cookie
     captured before sign-out and a completed sign-out, When a request is made
     with the captured cookie, Then it is unauthenticated and no record content is
     returned.
175. **A copied pre-logout cookie cannot recreate a session.** Given the same
     captured cookie after sign-out, When the request completes, Then no new
     session row exists and the revoked row is not resurrected.
176. **Session-store unavailability fails closed on protected routes.** Given a
     session store that throws on every call, When `GET /firms` is requested with
     a valid cookie, Then the request is refused and no principal is established.
177. **Session-store unavailability does not break the constants.** Given the same
     failing store, When `GET /` and `GET /health` are requested, Then both return
     200 with their exact bodies.
178. **No in-memory session store outside Development.** Given the started host in
     Production, When the registered session-store implementation is resolved,
     Then it is the durable implementation and not an in-memory one.
179. **Startup refuses a non-durable session store.** Given the host started as
     Production configured with an in-memory session store, When it starts, Then
     startup fails.
180. **Cross-instance visibility.** Given two independently constructed session
     stores over the same database, When a session is created through the first,
     Then the second observes it without any process-local priming.
181. **Cross-instance revocation.** Given the same two stores and a session
     created through the first, When it is revoked through the second, Then the
     first refuses it on the next lookup.
182. **The firm-scope exemption inventory is exactly four entries.** Given
     `FirmScopeExemptions.All`, When it is read, Then it contains exactly:
     session lookup by `SessionId`, user lookup by issuer + subject, user lookup
     by the session's opaque `UserId`, and active-membership lookup by `UserId`.
     Every entry omits a trusted firm only because it operates on the approved
     identity bootstrap; the session lookup returns `ActiveFirmId` rather than
     accepting one.
183. **The identity carve-out is exactly three tables.** Given the EF model, When
     every entity exempt from FR-18's non-nullable-`FirmId` rule is enumerated,
     Then the set is exactly the user, firm-membership, and session entities.
184. **No identity table carries taxpayer content.** Given the user,
     firm-membership, and session entities, When their columns are enumerated,
     Then none stores a taxpayer display name, employer name, amount, box code,
     CRA line code, or any value derived from a slip.
185. **Session expiry is evaluated, not swept.** Given the production source of
     the portal, `Tenancy`, and `Persistence`, When scanned, Then no timer,
     hosted service, or scheduler removes or expires sessions, and expired
     sessions are refused by evaluation on each request.
186. **The R-1 and R-2 checks are independent.** Given a live, unrevoked,
     in-bounds session whose membership has been disabled, When a protected
     request is made, Then it is refused — proving the session check does not
     short-circuit the membership check.

### Provisioning prohibitions (R-3)

187. **No default firm is created.** Given a freshly migrated database with no
     seeding, When the firm table is read, Then it is empty.
188. **No default administrator or privileged principal is created.** Given the
     same database, When the user and membership tables are read, Then both are
     empty, and no migration or startup path inserts into either.
189. **No credential or membership is seeded outside Development.** Given the host
     started as Production against a freshly migrated database, When startup
     completes, Then the user, firm, membership, and session tables are all empty.
190. **No bypass claim exists.** Given the production source of the portal,
     `Tenancy`, and `Persistence`, When scanned, Then there is no bypass,
     super-user, impersonation, or skip-authorization claim, header, configuration
     key, or code path, and no `AllowAnonymous` appears on any route outside the
     enumerated anonymous set.
191. **No hard-coded identity in production source.** Given the same source, When
     scanned, Then it contains no GUID literal used as a firm, user, membership,
     or session identifier, and no hard-coded OIDC issuer or subject.

---

## Rollback and recovery

- All work happens on feature branches; `master` is untouched until a human
  merges. Merging is never automated.
- **Reverting the branch restores the exact prior tree** for source: the five tax
  modules are byte-unchanged, and the two new projects and the portal's changes
  are the entire diff.
- **Database rollback is not symmetric with code rollback.** Each EF migration
  SHALL have a working `Down`, and the initial migration's `Down` SHALL drop
  exactly what its `Up` created — **including the session table**. **A rollback
  that leaves a table containing taxpayer data or a live session behind is a
  defect**, and a check on the down-migration script's completeness is required
  before any environment holding real data exists.
- **Rolling back the session store is a security event, not just a schema
  change:** reverting to cookie-only sessions would silently restore the replay
  window R-2 exists to close. A revert of stage 4 or later must be accompanied by
  revoking every outstanding session.
- Each ordered stage (see Migration constraints) is independently green, so a
  failing stage reverts to the last green stage.
- **Recovery check:** after any revert, `dotnet build TaxAI.sln` and
  `dotnet test TaxAI.sln` must return to green with the same test set.
- If `Tenancy` and `Persistence` were deleted outright, only the portal would
  need reverting: no tax module depends on either (AC-14, AC-21).

---

## Open questions

### Class A — all answered; recorded here with their resolutions

- **BQ-1 — retention boundary: ANSWERED.** `RetentionUntilUtc` is an **exclusive
  UTC boundary**; for tax year 2025 it is `2033-01-01T00:00:00Z`, computed as the
  exclusive tax-year end (`2026-01-01T00:00:00Z`) plus seven calendar years. A
  record is purge-eligible when `evaluationInstantUtc >= RetentionUntilUtc`, so
  `2032-12-31T23:59:59.9999999Z` is not eligible and `2033-01-01T00:00:00Z` is.
  No inclusive end-of-day value is modelled, and the exclusive meaning is
  documented at the declaration. All instants are UTC `DateTimeOffset`; values
  with a non-zero offset are **rejected at the application boundary rather than
  converted**, while zero-offset values are accepted without attempting to infer
  unavailable originating `DateTime.Kind` provenance (FR-33, FR-34, FR-35;
  AC-148–AC-156). Still a v1
  product-retention rule, **not** a statutory CRA assertion.
- **R-1 — per-request membership verification: CONFIRMED and clarified by
  T18A.** Every firm-scoped taxpayer-data request re-verifies the local user's
  active state, the membership's existence, active state, and match to the
  session's `ActiveFirmId`; membership or persistence failure fails closed.
  Authenticated tenancy-bootstrap endpoints may be reached with a null
  `ActiveFirmId`, but still require a live session, a fresh local-user-by-id
  lookup, an active user, and fresh active memberships. `/`, `/health`,
  `/sign-in`, and the OIDC callback mechanics remain fully exempt (FR-47;
  AC-157–AC-161).
- **R-2 — server-side session revocation: OVERRIDDEN INTO SCOPE.** Cookie
  clearing alone was rejected as insufficient. 011 ships a durable, shared,
  Azure-SQL-backed session store; the cookie carries only a protected opaque
  ticket key; logout revokes server-side; a copied pre-logout cookie can neither
  reuse nor recreate a revoked session; store unavailability fails closed; no
  in-memory-only production store (FR-63–FR-72; AC-162–AC-186).
- **R-3 — provisioning: ACCEPTED as informational, with hard prohibitions.** A
  spec-011-only production deployment has no `Firm`, `User`, or `FirmMembership`
  records, so no taxpayer-data session can be obtained. No default firm, no
  default administrator, no production seed credential, no bypass claim, and no
  hard-coded development membership in production (FR-73–FR-75; Out of scope;
  AC-187–AC-191).
- **Spec 010 question F — the untrusted extraction ingress.** Untouched: 011
  implements, registers, and resolves no `IDocumentExtractionIngress` and accepts
  no uploaded file. **Still reserved for a later specification.**
- **Mapping/ruleset version at rest.** The human's allowlist permits it, but
  `TaxLogicEngine` exposes **no** version on `T4LineMappingConfig` or
  `T4LineMap`, and adding one would change a merged module's public contract
  (forbidden by FR-4 and by spec 010's `PublicSurfaceSnapshots`). 011 therefore
  persists none. Adding a mapping-config version to the engine is a good
  candidate for a later spec; recorded so its absence is a decision rather than
  an oversight.

### `[ASSUMED]` — Class B decisions, made and recorded for one-pass override

- `[ASSUMED]` **Spec folder and slug** are
  `specs/011-tenant-identity-and-persistence-foundation/`; 011 confirmed free by
  listing `specs/` (001–010 and 012 exist; the previously-numbered 011 draft has
  been renumbered to 012 by the human).
- `[ASSUMED]` **v1 scope** is: identity, tenancy, revocable server-side sessions,
  the persisted review read model, retention, purge, and the portal
  sign-in/firm-scoping surface — and nothing else. No taxpayer-data screen, no
  upload, no ingestion, no export, no provisioning.
- `[ASSUMED]` **Two production projects**, `src/Tenancy/` (deterministic
  contracts, projection, guards, session rules, retention) and `src/Persistence/`
  (the only EF Core project). A reviewer who prefers three (splitting the review
  read model or the session store out of `Tenancy`) may say so in one pass; the
  required property — that EF Core lives in exactly one project and never in a
  tax module — is not negotiable.
- `[ASSUMED]` **Two test projects**, `tests/Tenancy.Tests/` and
  `tests/Persistence.Tests/`, with the repository's standard tooling versions,
  both registered in `scripts/test-projects.json` with floor counts.
- `[ASSUMED]` **Project names avoid `Clients`, `Firms`, `Documents`, `Intake`,
  `Review`**, because spec 010's shipped test forbids exactly those five
  `.csproj` names.
- `[ASSUMED]` **`FirmId` is a GUID** stored as `uniqueidentifier`; the
  `taxai_firm_id` claim carries its canonical string form and is parsed strictly,
  so tenant comparison can never depend on collation.
- `[ASSUMED]` **String identifier columns use an explicit binary collation** and
  every identifier comparison in code is ordinal.
- `[ASSUMED]` **Monetary columns are `decimal(19,4)`**, with the mandatory
  exact-round-trip refusal of FR-20 making the specific number non-load-bearing:
  any value the store could not hold exactly is refused rather than altered.
- `[ASSUMED]` **Opacity guard for `CaseId`/`ParticipantId`** — non-empty, bounded
  maximum length, restricted to letters, digits, hyphen, and underscore. Exact
  length and character set are `/plan`'s to fix, but they must be fixed, and
  nothing may be trimmed or normalized to pass.
- `[ASSUMED]` **Subject key** for versioning is
  `(FirmId, CaseId, ParticipantId, TaxYear)`, unique together with
  `PackageVersion`.
- `[ASSUMED]` **`ReviewedSlip.DocumentId` and `.SourceReference` are not
  persisted.** Both arrive from the untrusted extraction envelope; the human's
  allowlist admits a **TaxAI-generated** opaque document reference and forbids
  caller-controlled source references, so 011 generates its own per persisted slip.
- `[ASSUMED]` **Participant role is not persisted in 011.** The allowlist permits
  it, but `AccountantReviewPackage` carries no role and inventing one would
  duplicate `TaxCases.RelationshipKind` and create a second definition. The column
  is added when a source exists.
- `[ASSUMED]` **Structural warnings are not persisted in 011.** The allowlist
  permits them, but nothing in the current contract produces one, and a column
  with no writer is a speculative shell.
- `[ASSUMED]` **The user record stores issuer, subject, active state, and
  timestamps only** — no email, phone, display name, or provider profile claim,
  because email and phone are forbidden at rest and a display name is available
  from the live principal.
- `[ASSUMED]` **`SessionId` is an opaque GUID**, and the cookie carries the
  protected ticket key rather than the raw identifier.
- `[ASSUMED]` **The session store is implemented as an ASP.NET Core
  `ITicketStore` over the `Tenancy` session contract**, so the cookie handler
  itself never holds a claims payload. `/plan` may choose an equivalent mechanism
  but not a process-local or in-memory production one.
- `[ASSUMED]` **Slip type discriminator** is a structural code owned by `Tenancy`
  whose only permitted 011 value is `"T4"`, asserting no CRA semantics.
- `[ASSUMED]` **Route shape** — `/` and `/health` keep their exact spec 010 paths
  and bodies; new routes are `GET /sign-in`, `GET /firms`, `POST /firms/select`,
  `POST /sign-out`, plus the OIDC handler's own callback paths.
- `[ASSUMED]` **OIDC package** — `Microsoft.AspNetCore.Authentication.OpenIdConnect`
  pinned to 8.0.x, the portal's only `PackageReference`.
- `[ASSUMED]` **`Persistence` exposes one registration extension** that the
  portal's `ModuleRegistration.cs` calls, so the SQL Server provider package never
  enters the portal's project file while the composition root stays single.
- `[ASSUMED]` **Migrations live under `src/Persistence/Migrations/`** and are the
  only source of schema; no `EnsureCreated` and no migrate-on-startup.
- `[ASSUMED]` **Relational behaviour tests** (tenant isolation, uniqueness,
  versioning, purge, session revocation, cross-instance visibility) run against a
  relational provider available without Azure, while schema/DDL assertions target
  the generated SQL Server migration. `/plan` may pick the mechanism; it may not
  substitute a non-relational in-memory provider for the isolation, constraint, or
  revocation tests.
- `[ASSUMED]` **Synthetic fixture pattern** — `Test Taxpayer NNN`,
  `Example Employer X`, deterministic GUIDs, toy amounts; no SIN field of any kind.
- `[ASSUMED]` **Durations are expressed as `TimeSpan.FromMinutes(30)` and
  `TimeSpan.FromHours(8)`**, never as five-digit second counts, so spec 010's
  CRA-line-literal scan stays intact.
- `[ASSUMED]` **Documentation set** — new `CLAUDE.md` + pointer `AGENTS.md` for
  both new modules; rewritten `src/TaxAI.Portal/CLAUDE.md`; updates to
  `README.md`, root `CLAUDE.md`, `NEXT.md`; and an AC-by-AC
  `specs/011-.../verification.md`. **No existing module's instruction file is
  touched.**
- `[ASSUMED]` **Wave ordering** is the seven-stage sequence in Migration
  constraints, each stage green before the next.
- `[ASSUMED]` **No `Directory.Build.props`, no `global.json`, no central package
  management, no analyzer package, no CI workflow, no Dockerfile.**

**No Class A assumption is made anywhere in this PRD.** Every tax-semantic, PII,
tenant-isolation, and security-boundary question is answered by the human on
record, or is explicitly excluded and listed above.

---

## CRA sources

**This feature asserts no CRA fact, and therefore cites none.** No box number,
CRA line number, filing deadline, rate, threshold, limit, gross-up factor, or
credit percentage enters this PRD.

That is a claim about scope, and it is enforced rather than promised:

- Every CRA line code stored originates from
  `TaxLogicEngine.Mappings.T4LineMap.2025.json`, whose facts were verified against
  canada.ca in specs 003 and 004 and are unchanged here (AC-26, AC-109, AC-110).
- Every box code and every amount stored originates from `T4SlipParser` and
  `T4SlipBoxAmounts.Enumerate` via the assembled package, not from this feature
  (AC-110, AC-111, AC-112).
- The only tax-year statement is "2025", which is not sourced from canada.ca here
  but delegated to the merged `SupportedReviewTaxYears` (AC-105, AC-106).
- **The seven-year retention period, and its exclusive `2033-01-01T00:00:00Z`
  boundary for tax year 2025, are an engineering product default set by the human,
  explicitly NOT a statement of any CRA statutory minimum.** They are deliberately
  **not** cited to canada.ca, must not be presented as a CRA fact, and must not be
  silently converted into one by `/plan`, by an architecture document, or by a
  code comment. If a CRA retention rule is later found to be relevant, it is a new
  Class A question and a new PRD amendment — not a re-labelling of this default.

| Claim | Source URL | Tax year | Retrieved |
|---|---|---|---|
| *(none — this feature introduces no tax fact)* | — | — | — |

---

## Definition of Done — self-check

**Both classes:**

- [x] `specs/011-tenant-identity-and-persistence-foundation/prd.md` exists with
      the required structure, opening with `## Feature classification` (Problem /
      User stories / Functional requirements / Out of scope / Acceptance criteria
      / Open questions / CRA sources)
- [x] Feature class named explicitly (`tax-domain`) and justified; the feature
      mixes classes and is therefore `tax-domain`, with no platform relaxation
      claimed
- [x] Reuse check performed and documented — `specs/*/prd.md`, every
      `src/*/CLAUDE.md`, and the source tree were read directly; the reused engine,
      processor, and review types are referenced and none re-specified; the portal
      test suite was read file-by-file to determine exactly which shipped
      assertions this feature invalidates
- [x] Every AC is mechanically checkable by a single test
- [x] **Zero Class A assumptions made, and zero unanswered Class A questions.**
      BQ-1 is answered (exclusive UTC boundary, `>=` comparison, non-zero
      offset rejected without inferring unavailable `DateTime.Kind` provenance);
      R-1 is confirmed; R-2 is overridden into scope; R-3 is accepted with hard
      prohibitions. Every one is folded into requirements and acceptance criteria.

**`tax-domain`, additionally:**

- [x] Every CRA fact has URL + tax year + retrieval date, all from canada.ca —
      **vacuously satisfied and enforced: this feature asserts no CRA fact**, and
      the retention default is explicitly recorded as a product decision, not a
      CRA one
- [x] No source conflict was resolved by this agent's judgement (no source was
      consulted, because no CRA claim is made)
- [x] Tax year named explicitly in Problem — **2025 only**, delegated to
      `SupportedReviewTaxYears`
- [x] Every fail-closed rule has its own negative AC — AC-40…AC-77,
      AC-149/AC-153/AC-154, AC-157…AC-161, AC-167…AC-179, AC-186…AC-191
- [x] PII acceptance criteria present — AC-86…AC-102, extended by AC-163…AC-165
      and AC-184
- [x] Tax-year isolation AC present — AC-103…AC-108
- [x] Data-driven mapping AC present — AC-109…AC-116
- [x] Boundary ACs present (half-cent both signs, zero, absent optionals, empty
      package, retention boundary ±1 tick) — AC-78…AC-85, AC-148…AC-151
- [x] Amendments section present, naming every superseded merged assertion and
      its strictly stronger replacement, and stating that no shipped tax code
      becomes wrong

**Result: COMPLETE.**

Ready for: /plan
