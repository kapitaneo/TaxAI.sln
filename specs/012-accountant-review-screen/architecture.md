# Architecture 012 — Accountant Review Screen

## Outcome

Implement spec 012 as **one atomic vertical-slice task**.

The current architecture already supplies every cross-module contract the
screen needs:

- `PortalAuthorization.FirmScopedPolicy` authenticates the durable session and
  revalidates the current local user and memberships;
- its request state carries the server-derived active `FirmId`;
- `IReviewRecordRepository.FindAsync` requires `TrustedFirmId`, scopes the query
  by `(FirmId, RecordId)`, loads the immutable graph in stored order, and returns
  `null` for both missing and foreign records;
- `SqlReviewRecordRepository` is already registered behind the Tenancy
  contract.

Splitting this into a contract task and a UI task would create a temporary
endpoint or stub with no independent product value. It would also force shared
ownership of `Program.cs`, route tests, and the browser projection. The smallest
green merge unit is the complete read-only slice.

## No new module or public contract

There is no change to:

- `Tenancy`, `Persistence`, `CaseReview`, or `TaxLogicEngine`;
- `IReviewRecordRepository`;
- the persisted entity model, EF configuration, migration, or database schema;
- project references or package references;
- the portal's public surface (`Program` remains its only public type).

All new review-screen types are internal to `TaxAI.Portal`.

## Request flow

1. Routing accepts only `GET /reviews/{recordId:guid}`.
2. The existing firm-scoped authorization policy authenticates the server-side
   session and reloads the active local user and memberships.
3. The endpoint reads the trusted firm from
   `PortalAuthorization.BootstrapStateFor(HttpContext)`. It never reads a firm
   from request data.
4. The endpoint wraps that server-derived GUID with
   `TrustedFirmId.FromGuid`.
5. It calls `IReviewRecordRepository.FindAsync` once.
6. `null` becomes one empty `404`; because the repository scopes by firm,
   missing and foreign records reach the identical branch.
7. A record is projected into an internal DTO through a closed property
   allowlist.
8. A server-side renderer HTML-encodes every data value and returns non-cacheable
   HTML.

The global fixed exception handler remains responsible for unexpected failures.
No catch block logs, echoes, or reclassifies record data.

## Endpoint and response contract

| Condition | Status | Body |
|---|---:|---|
| authorized, record found | 200 | encoded server-rendered review HTML |
| missing record | 404 | empty |
| foreign record | 404 | byte-identical to missing |
| malformed GUID | 404 | routing refusal; repository not called |
| unauthenticated | 401 | existing fixed authentication refusal |
| inactive/missing/mismatched user or membership | 403 | existing fixed authorization refusal |
| unhandled failure | 500 | existing fixed error body |

The review endpoint sets `Cache-Control: no-store, no-cache, must-revalidate`
and `Pragma: no-cache`. It emits no `ETag` or application cache key.

## Browser DTO

The DTO is not a persistence type and is never serialized. It exists only
between endpoint projection and HTML rendering.

| DTO field | Source | Browser-visible |
|---|---|---|
| tax year | `PersistedReviewRecord.TaxYear` | yes |
| package version | `PersistedReviewRecord.PackageVersion` | yes |
| slip type | `PersistedSlip.SlipTypeCode` | yes |
| taxpayer display name | `PersistedSlip.TaxpayerDisplayName` | yes |
| employer display name | `PersistedSlip.EmployerDisplayName` | yes |
| box code / invariant amount | `PersistedBoxAmount` | yes |
| line code / invariant amount | `PersistedLineEntry` | yes |
| source box codes | `PersistedSourceBoxCode` | yes |
| unmapped box codes | `PersistedUnmappedBoxCode` | yes |
| any firm/case/participant/record/child/document identifier | persisted graph | no |
| timestamps or retention boundary | persisted graph | no |
| SIN, masked SIN, partial SIN, or SIN-presence flag | does not exist in persisted graph | no |

Amounts become invariant strings in the projection. This is formatting only:
no arithmetic, comparison, rounding, scaling, subtotal, or aggregation occurs.

## Rendering

Use a single new `ReviewEndpoints.cs` file containing:

- endpoint mapping and handler;
- internal DTO records and explicit projection;
- server-rendered HTML and small inline styles.

This follows the existing `FirmEndpoints.cs` pattern and avoids controllers,
Razor infrastructure, static files, client JavaScript, a second registration
extension, or a new package. `HtmlEncoder.Default` encodes every stored string.
Structural labels do not explain CRA meanings.

The renderer has explicit states for:

- a populated record;
- no slips;
- no populated boxes;
- no CRA line entries;
- no source boxes for a line;
- no unmapped box codes.

Authorization refusals, `404`, and fixed `500` remain plain text or empty and
never instantiate the review DTO.

The anonymous root remains fixed and taxpayer-data-free. Its obsolete
application-wide "serves no taxpayer data" sentence is replaced with a truthful
description of the authenticated review capability. The health response remains
byte-identical.

## Immutability and version semantics

The URL selects one `RecordId`, which is one stored immutable version.
The screen:

- calls only `FindAsync`;
- never calls `SaveAsync`;
- adds no update, upsert, delete, decision, sign-off, or purge endpoint;
- adds no lookup by subject and no "latest" behavior;
- does not rewrite, merge, or hide earlier versions.

Tests use two records for the same subject with different record IDs and prove
that each route renders its own stored `PackageVersion`.

## Security and PII

- The firm boundary remains structural: the repository cannot be called without
  a `TrustedFirmId`.
- The browser allowlist is narrower than the persistence allowlist.
- No portal production source names or reads a SIN-bearing domain type.
- No masking or redaction helper is added; omission remains the only portal and
  persistence posture for SIN.
- Display names and code strings are HTML-encoded.
- No review value is logged, measured, traced, serialized, placed in a cookie,
  or reflected into an error.
- The opaque record ID is the only new caller input. No firm, case,
  participant, document, or taxpayer identifier is accepted.

## Existing assertions that must be amended, not deleted

Spec 011 intentionally froze the pre-screen portal. The atomic task replaces
these assertions with narrower exact allowlists:

- `ShellPageTests`: exact route inventory and the now-obsolete "no route
  parameter" rule;
- `PortalBoundaryTests`: exact production-file list, approved registration-token
  list, and the blanket bans on carrying `decimal`/`TaxYear`;
- `PortalSecurityTests`: exact production-file list, route-input allowlist,
  blanket taxpayer-name omission, response inventory, and the pre-screen
  comments around browser data;
- `ProjectStructureTests`: portal instruction text that currently says taxpayer
  data is never rendered;
- `AmendmentAuditTests`: spec 011 assertions superseded by this approved first
  taxpayer-data surface;
- `RepositoryVerificationTests`: README/current-state/roadmap assertions that
  currently disclaim every taxpayer-data screen.
- `SessionRevocationTests` and `StartupRefusalTests`: assertions that use the
  obsolete root-shell sentence as their proof that the anonymous constant route
  stays available;
- `RouteAuthenticationBaselineTests`: compare the unchanged spec 013 legacy
  route/authentication subset to its immutable baseline while recognizing the
  one additive spec 012 route and intentional root-body revision;
- `UpgradeBaselineContractTests`: retain spec 013's immutable pre-upgrade
  evidence as a required subset while permitting the exact internal portal
  additions owned by spec 012.

Every replacement remains exact. In particular, allowing stored amounts to be
carried does not allow arithmetic, allowing one GUID route parameter does not
allow a firm parameter, and allowing display names does not relax the no-SIN
rule.

## Test strategy

One new integration suite supplies:

- a test authentication/session/identity setup that exercises the real
  firm-scoped authorization policy;
- an `IReviewRecordRepository` test double that records the trusted firm and
  requested record ID, returns firm-scoped records, and fails the test if
  `SaveAsync` is called;
- synthetic `PersistedReviewRecord` graphs containing only approved,
  non-SIN-shaped values.

The suite verifies authorization, active-user and active-membership revocation,
server-derived firm scope, missing/foreign equivalence, malformed selectors,
method restrictions, DTO exclusions, encoding, order, amount fidelity, empty
states, version selection, no mutation, and cache headers.

Boundary suites verify the absence of direct CaseReview/EF use, serializers,
client assets, tax logic, arithmetic, hard-coded CRA values, SIN tokens, and
logging sinks.

The final gate is:

```powershell
dotnet build TaxAI.sln
dotnet test TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1
```

The last command is authoritative because it proves that every registered test
assembly actually ran without failures or skips. Its portal minimum increases
with the added tests and is never lowered.

## Deployment

No startup guard, environment check, connection-string rule, session
durability rule, or Data Protection registration changes. The build still
starts in Development only as shipped and remains undeployable for real
taxpayer data until the separate deployment specification supplies the approved
Canadian durable key ring and hosting posture.

## Task topology

**Proposed implementation task count: 1.**

T01 owns the entire vertical slice, all directly invalidated portal tests,
documentation, full verification, and its done marker. There is no contract-only
wave and no follow-on corrective task.

## Exact T01 file ownership

### Production

- `src/TaxAI.Portal/Program.cs`
- `src/TaxAI.Portal/PortalContent.cs`
- `src/TaxAI.Portal/ReviewEndpoints.cs` (new)
- `src/TaxAI.Portal/CLAUDE.md`

### Tests and gate

- `tests/TaxAI.Portal.Tests/ReviewPageTests.cs` (new)
- `tests/TaxAI.Portal.Tests/ShellPageTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
- `tests/TaxAI.Portal.Tests/Identity/SessionRevocationTests.cs`
- `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/RouteAuthenticationBaselineTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`
- `scripts/test-projects.json`

### Documentation and completion evidence

- `README.md`
- `CLAUDE.md`
- `NEXT.md`
- `specs/012-accountant-review-screen/verification.md` (new)
- `specs/012-accountant-review-screen/status/T01.done` (new, only after the
  authoritative full-suite gate passes)

No other file is owned by T01. In particular, no file under `src/Tenancy`,
`src/Persistence`, `src/CaseReview`, or their test trees may change.
