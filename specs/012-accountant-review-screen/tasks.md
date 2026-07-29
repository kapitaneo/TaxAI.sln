# Tasks 012 — Accountant Review Screen

Read `prd.md` and `architecture.md` first.

This is the approved plan once the human accepts the proposed browser allowlist
and task ownership. After approval, this file is read-only. Never check a box in
it. Completion is recorded only in `status/T01.done`.

## Plan shape

- **Implementation task count: 1**
- **Parallel implementation: none**
- **Contract-only task: none**
- **`NotImplementedException` scaffold: forbidden**
- **Corrective follow-on tasks for ordinary findings: forbidden**

Spec 012 is an explicit exception to the repository's usual contract-first wave:
the required public contracts already shipped in spec 011, and every new portal
type is internal. The only independently green unit is the complete vertical
slice.

## Prerequisites

Before T01 begins:

1. every spec 011 `.done` marker must exist;
2. every spec 013 `.done` marker must exist because the current branch targets
   .NET 10;
3. the human must explicitly name `T01`;
4. the human must approve this replan, including the closed browser allowlist.

Do not auto-select T01.

## Atomic delivery wave

### T01 — Firm-scoped read-only accountant review screen

**Outcome:** one authenticated, server-rendered route reads one exact immutable
persisted review-package version through the existing firm-scoped repository and
renders only the approved SIN-free DTO.

**Implementation requirements:**

1. Map only `GET /reviews/{recordId:guid}` and require
   `PortalAuthorization.FirmScopedPolicy`.
2. Obtain the active firm only from the existing authorization request state
   after current active-user and active-membership verification.
3. Call `IReviewRecordRepository.FindAsync` once with that
   `TrustedFirmId`. Accept no caller-supplied firm value and add no repository
   method.
4. Return one empty, indistinguishable `404` for missing and foreign records.
5. Project `PersistedReviewRecord` into an internal closed DTO containing only:
   tax year, package version, slip type, taxpayer and employer display names,
   box codes and invariant amounts, line codes and invariant amounts, source box
   codes, and unmapped box codes.
6. Exclude every firm/case/participant/record/child/document identifier,
   timestamp, retention value, address, account number, source reference,
   sign-off value, and every SIN representation.
7. Render encoded HTML in the persisted collection order with explicit
   populated and empty states. Add no JSON, JavaScript, static asset tree, Razor
   package, controller, or client data fetch.
8. Perform no parsing, mapping, tax calculation, arithmetic, rounding,
   aggregation, supported-year decision, review assembly, or review decision.
9. Call no mutation API. Keep immutable versions separately addressable by
   their own record IDs and add no "latest" behavior.
10. Send non-cacheable review responses.
11. Replace every directly invalidated exact portal assertion with a stricter
    exact allowlist; do not simply remove or weaken it.
12. Update the portal/root documentation to describe exactly the one shipped
    read-only surface and retain the gates on upload, mutation, a second
    taxpayer-data surface, provisioning, extraction ingress, and deployment.
13. Keep the spec 013 baseline artifacts immutable. Amend their tests so the
    pre-existing route/authentication and internal-contract baseline remains a
    required subset while spec 012's exact new route, internal types, and
    truthful root-shell revision are asserted separately.
14. Record AC-by-AC evidence in `verification.md`, raise the portal test floor,
    run the full gate, and create `status/T01.done` only after it is green.

**Acceptance coverage in the same task:**

- successful authorized render;
- server-derived trusted firm passed to the repository;
- unauthenticated, inactive-user, inactive-membership, missing-claim,
  mismatched-membership, and store-failure refusals before lookup;
- missing and foreign byte-identical `404`;
- malformed selector and non-GET methods never look up or mutate;
- exact DTO allowlist and excluded-field sentinels;
- HTML encoding;
- ordered slips/boxes/lines/source codes/unmapped codes;
- positive, negative, zero, and four-decimal invariant amounts;
- populated and every empty state;
- two immutable versions remain separately addressable;
- `SaveAsync` is never called;
- no SIN token, representation, nine-digit run, log, cookie, or response leak;
- no parser, mapper, mapping resource, arithmetic, rounding, aggregation,
  CaseReview reference, direct EF use, serializer, client stack, new project,
  package, migration, or schema change;
- unchanged health, identity/session, membership revalidation, revocation, and
  startup-refusal suites.

**Owned files — production:**

- `src/TaxAI.Portal/Program.cs`
- `src/TaxAI.Portal/PortalContent.cs`
- `src/TaxAI.Portal/ReviewEndpoints.cs` *(new)*
- `src/TaxAI.Portal/CLAUDE.md`

**Owned files — tests and gate:**

- `tests/TaxAI.Portal.Tests/ReviewPageTests.cs` *(new)*
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

**Owned files — documentation and evidence:**

- `README.md`
- `CLAUDE.md`
- `NEXT.md`
- `specs/012-accountant-review-screen/verification.md` *(new)*
- `specs/012-accountant-review-screen/status/T01.done` *(new, only after the
  complete gate passes)*

**Forbidden changes:**

- any file not listed above;
- any file under `src/Tenancy`, `src/Persistence`, `src/CaseReview`,
  `src/TaxLogicEngine`, `src/TaxCases`, `src/DocumentExtraction`, or
  `src/CaseProcessing`;
- any test under those module test trees;
- any `.csproj`, solution entry, migration, EF configuration, database model,
  package reference, public contract, or deployment/startup-guard behavior.

**Required verification:**

```powershell
dotnet build TaxAI.sln
dotnet test TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1
```

The authoritative suite command must show every registered project executed,
zero failures, and zero skips. Do not create the marker or open a ready PR if
that proof is unavailable.

After T01 is complete, stop. Do not select or invent another task.
