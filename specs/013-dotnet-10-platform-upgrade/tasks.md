# Tasks: .NET 10 Platform Upgrade (spec 013)

**Feature class:** `platform`.

This plan upgrades the existing platform only. It adds no business capability,
tax rule, endpoint, project, module, schema change, migration, deployment, or
production resource.

## Rules that bind every task

- Implement only the TaskID explicitly named by the human.
- `tasks.md` is read-only after approval. Completion is recorded only in that
  task's `specs/013-dotnet-10-platform-upgrade/status/<TaskID>.done` marker,
  containing the PR number and commit SHA.
- A task is unblocked only when every task in every earlier wave has its marker.
  Sibling tasks in the same wave may run in parallel.
- Change only the exact files listed by the task. A listed conditional source
  file is permission to make a proved compatibility correction, not a
  requirement to manufacture a diff.
- No two sibling tasks in a wave share a path, and no sibling consumes a file
  created by another sibling.
- Before writing a marker, run all task-specific checks plus:

  ```powershell
  dotnet build TaxAI.sln
  dotnet test TaxAI.sln
  pwsh ./scripts/Invoke-SolutionTests.ps1
  git diff --check
  ```

- Every manifest project must run; failures and skips are zero; every test floor
  is met or raised in the same task that adds tests. A floor is never lowered.
- No warning is suppressed, disabled, or demoted. A clean build has zero errors
  and a warning multiset no larger than the PRD FR-21 budget.
- The three files in `src/Persistence/Migrations/` are immutable and are owned
  by no task in this plan.
- Historical Specs 001–011 and their status/baseline/verification artifacts are
  immutable.
- Agents never merge or approve their own work.

## Wave 0 — repository and behavior contract

### T01 — Freeze the approved .NET 8 baseline

- [ ] **T01** — From clean commit
  `13097dada6b1aae070fc0006d48a6862ce9904f8`, capture canonical repository,
  public/internal contract, EF model, relational schema, SQL Server DDL,
  migration-hash, route/auth, warning, and exact per-project test evidence.
  Add framework-neutral executable comparisons that pass on the baseline and
  will continue to run after each migration step. Contract normalization is
  exactly as defined in `architecture.md`: ordinal ordering for unordered
  contract/model/schema collections, compiler-generated/private members
  excluded, and only CRLF/LF plus one final-newline normalization for DDL.
  The route matrix covers `/`, `/health`, `/sign-in`, `/sign-out`, `/firms`,
  `/firms/select`, unmatched routes, allowed/disallowed methods, anonymous and
  authenticated states, status, `Location`, `WWW-Authenticate`, content type,
  body, authentication, and authorization. Raise only the affected manifest
  floors to the actual new counts after the full gate passes. Covers AC-5,
  AC-11, AC-14…AC-18, AC-22, AC-23 baseline, AC-25 baseline, AC-26 baseline,
  AC-27 baseline, AC-30 baseline, and AC-31 baseline. — files:
  `specs/013-dotnet-10-platform-upgrade/baseline.md`,
  `specs/013-dotnet-10-platform-upgrade/baselines/repository-contract.json`,
  `specs/013-dotnet-10-platform-upgrade/baselines/public-contracts.json`,
  `specs/013-dotnet-10-platform-upgrade/baselines/ef-model.json`,
  `specs/013-dotnet-10-platform-upgrade/baselines/relational-schema.json`,
  `specs/013-dotnet-10-platform-upgrade/baselines/initial-migration.sql`,
  `specs/013-dotnet-10-platform-upgrade/baselines/route-auth-contract.json`,
  `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`,
  `tests/TaxAI.Portal.Tests/Upgrade/RouteAuthenticationBaselineTests.cs`,
  `tests/Persistence.Tests/Upgrade/EfUpgradeBaselineTests.cs`,
  `scripts/test-projects.json`,
  `specs/013-dotnet-10-platform-upgrade/status/T01.done` — depends on: none

## Wave 1 — exact SDK and local tool contract

### T02 — Pin SDK 10.0.302 and dotnet-ef 10.0.10

- [ ] **T02** — After SDK `10.0.302` is installed on the implementing machine,
  add the exact root `global.json` (`allowPrerelease: false`,
  `rollForward: "disable"`) and an `isRoot: true` local tool manifest containing
  only `dotnet-ef` `10.0.10`. Extend the Wave 0 repository contract tests to
  parse exact values, reject ranges/prereleases/extra tools, and prove
  `dotnet --version`, `dotnet tool restore`, and
  `dotnet tool run dotnet-ef --version`. The solution is still allowed to be
  `net8.0` in this transition task. Raise the Portal test floor if the added
  assertions increase its test count. Covers AC-1, AC-2, AC-8, and AC-9. — files:
  `global.json`, `.config/dotnet-tools.json`,
  `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`,
  `scripts/test-projects.json`,
  `specs/013-dotnet-10-platform-upgrade/status/T02.done` — depends on: T01

## Wave 2 — outer consumers (2 parallel)

The two tasks in this wave are independent. Nothing in the solution references
`TaxCases`; Portal can target `net10.0` while its inward dependencies still
target `net8.0`.

### T03 — Upgrade the ASP.NET Core host and its test project

- [ ] **T03** — Change the Portal and Portal test projects to exactly
  `net10.0`; pin OIDC and MVC Testing to `10.0.10` and the four standard test
  packages to FR-4's exact versions. Replace the existing exact/major-version
  assertions in `ProjectStructureTests`, and replace the obsolete current-TFM
  disclosure strings in the two response-safety suites without weakening their
  no-disclosure behavior. Run the unchanged Wave 0 route/auth comparator and
  every OIDC, cookie, session, revocation, firm-selection, R-1, antiforgery,
  authorization, PII, and startup-refusal suite. Assess the .NET 10 cookie
  API-endpoint behavior without adding/removing endpoint metadata. Modify the
  listed Portal source files only if a compiler or proved behavioral regression
  requires a minimal compatibility correction; public/internal contracts and
  endpoint outcomes must remain equal to baseline. Covers AC-6 (Portal/MVC),
  AC-7 (Portal test), AC-11, AC-22 (Portal), AC-26, and AC-27. — files:
  `src/TaxAI.Portal/TaxAI.Portal.csproj`,
  `tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj`,
  `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`,
  `tests/TaxAI.Portal.Tests/HealthEndpointTests.cs`,
  `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`,
  `src/TaxAI.Portal/Program.cs`,
  `src/TaxAI.Portal/ModuleRegistration.cs`,
  `src/TaxAI.Portal/PortalIdentity.cs`,
  `src/TaxAI.Portal/PortalAuthorization.cs`,
  `src/TaxAI.Portal/FirmEndpoints.cs`,
  `src/TaxAI.Portal/SessionTicketStore.cs`,
  `src/TaxAI.Portal/PortalStartupGuards.cs`,
  `specs/013-dotnet-10-platform-upgrade/status/T03.done` — depends on: T02

### T04 — Upgrade TaxCases and its test project

- [ ] **T04** — Change `TaxCases` and `TaxCases.Tests` to exactly `net10.0`
  and pin the test project to FR-4's exact package versions. Make no production
  C# change. Prove the existing immutable aggregate, tax-year, PII, public
  contract, boundary, and end-to-end suites pass unchanged and that the
  `TaxCases → TaxLogicEngine` reference remains exact. Covers AC-3 for these
  projects, AC-7 for this test project, AC-22 for TaxCases, and AC-24. — files:
  `src/TaxCases/TaxCases.csproj`,
  `tests/TaxCases.Tests/TaxCases.Tests.csproj`,
  `specs/013-dotnet-10-platform-upgrade/status/T04.done` — depends on: T02

## Wave 3 — EF Core and SQL Server adapter

### T05 — Upgrade Persistence, SQLite tests, and EF10 compatibility proof

- [ ] **T05** — Change `Persistence` and `Persistence.Tests` to exactly
  `net10.0`; pin EF Core SqlServer/Design/Sqlite to `10.0.10` and the four test
  packages to FR-4. Update `PersistenceBoundaryTests` to exact values while
  retaining the Design `PrivateAssets="all"` and one-production-provider
  contract. Update only the `Persistence.Tests` rows in the cross-project
  package matrix held by `TenancyBoundaryTests`; its still-`net8.0`
  `Tenancy.Tests` rows remain unchanged until T06. Add focused EF10 tests for
  the three inventoried
  parameterized-collection queries, ordered/firm-scoped results, SQL
  disposition, `Application Name` guard/registration behavior, the absence of
  `UseAzureSql`/compatibility-level/`json` remapping, and SQLite
  DateTimeOffset/affinity/UTC/exclusive-boundary behavior without the pre-10
  switch. Re-run the Wave 0 EF equivalence comparator unchanged; migration
  source files must not change. Modify only the listed production seams if a
  proved EF10 regression requires the smallest behavior-preserving correction.
  Run the local pending-model command using Persistence plus the Portal startup
  project and require no pending changes. Raise only the Persistence test floor
  to its new actual count. Covers AC-6 (EF matrix), AC-7 (Persistence test),
  AC-12…AC-21, AC-22 (Persistence), AC-24, AC-25, and AC-26's persistence
  portion. — files: `src/Persistence/Persistence.csproj`,
  `tests/Persistence.Tests/Persistence.Tests.csproj`,
  `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`,
  `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`,
  `tests/Persistence.Tests/Upgrade/EfUpgradeBaselineTests.cs`,
  `tests/Persistence.Tests/Upgrade/EfCore10CompatibilityTests.cs`,
  `tests/Persistence.Tests/Support/RelationalTestContext.cs`,
  `tests/Persistence.Tests/Composition/ConnectionStringGuardTests.cs`,
  `tests/Persistence.Tests/Composition/PersistenceRegistrationTests.cs`,
  `tests/Persistence.Tests/Repositories/AuthenticationSessionStoreTests.cs`,
  `tests/Persistence.Tests/Repositories/IdentityDirectoryTests.cs`,
  `tests/Persistence.Tests/Repositories/ReviewRecordRepositoryTests.cs`,
  `tests/Persistence.Tests/Repositories/ReviewRecordTenantIsolationTests.cs`,
  `tests/Persistence.Tests/Repositories/ReviewRecordVersioningTests.cs`,
  `tests/Persistence.Tests/Services/ReviewRecordPurgeServiceTests.cs`,
  `src/Persistence/PersistenceRegistration.cs`,
  `src/Persistence/Services/SqlConnectionStringGuard.cs`,
  `src/Persistence/Repositories/SqlAuthenticationSessionStore.cs`,
  `src/Persistence/Repositories/SqlIdentityDirectory.cs`,
  `src/Persistence/Repositories/SqlReviewRecordRepository.cs`,
  `src/Persistence/Services/SqlReviewRecordPurgeService.cs`,
  `scripts/test-projects.json`,
  `specs/013-dotnet-10-platform-upgrade/status/T05.done` — depends on: T03, T04

## Wave 4 — trusted-firm application boundary

### T06 — Upgrade Tenancy and its test project

- [ ] **T06** — Change `Tenancy` and `Tenancy.Tests` to exactly `net10.0`,
  pin the test project to FR-4, and update only the existing exact framework and
  test-package assertions in `TenancyBoundaryTests`. Make no production C#
  change. Prove project references remain exactly CaseReview and
  TaxLogicEngine, no package enters the production module, and all trusted-firm,
  R-1, session-lifetime, retention, projection, PII, and contract tests pass
  unchanged. Covers AC-3/AC-7 for these projects, AC-22 (Tenancy), AC-24, and
  AC-26's Tenancy portion. — files: `src/Tenancy/Tenancy.csproj`,
  `tests/Tenancy.Tests/Tenancy.Tests.csproj`,
  `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`,
  `specs/013-dotnet-10-platform-upgrade/status/T06.done` — depends on: T05

## Wave 5 — review boundary

### T07 — Upgrade CaseReview and its test project

- [ ] **T07** — Change `CaseReview` and `CaseReview.Tests` to exactly
  `net10.0` and pin the test project to FR-4. Make no production C# change.
  Prove the exact inward references, immutable review/sign-off behavior,
  unmapped-code acknowledgement, PII defenses, public/internal contracts, and
  ordered snapshots pass unchanged. Covers AC-3/AC-7 for these projects, AC-22
  (CaseReview), and AC-24. — files:
  `src/CaseReview/CaseReview.csproj`,
  `tests/CaseReview.Tests/CaseReview.Tests.csproj`,
  `specs/013-dotnet-10-platform-upgrade/status/T07.done` — depends on: T06

## Wave 6 — processing coordinator

### T08 — Upgrade CaseProcessing and its test project

- [ ] **T08** — Change `CaseProcessing` and `CaseProcessing.Tests` to exactly
  `net10.0` and pin the test project to FR-4. Make no production C# change.
  Prove exact references, parser/mapper delegation, error ordering, tax-year
  isolation, raw-payload omission, masked-engine-identifier behavior, and the
  public/internal contract pass unchanged. Covers AC-3/AC-7 for these projects,
  AC-22 (CaseProcessing), and AC-24. — files:
  `src/CaseProcessing/CaseProcessing.csproj`,
  `tests/CaseProcessing.Tests/CaseProcessing.Tests.csproj`,
  `specs/013-dotnet-10-platform-upgrade/status/T08.done` — depends on: T07

## Wave 7 — extraction contract

### T09 — Upgrade DocumentExtraction and its test project

- [ ] **T09** — Change `DocumentExtraction` and
  `DocumentExtraction.Tests` to exactly `net10.0` and pin the test project to
  FR-4. Make no production C# change. Prove the exact inward reference,
  untrusted-input boundary, payload identity, validation/error ordering,
  2025-only isolation, PII omission, and public/internal contract pass
  unchanged. Covers AC-3/AC-7 for these projects, AC-22
  (DocumentExtraction), and AC-24. — files:
  `src/DocumentExtraction/DocumentExtraction.csproj`,
  `tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj`,
  `specs/013-dotnet-10-platform-upgrade/status/T09.done` — depends on: T08

## Wave 8 — deterministic tax engine

### T10 — Upgrade TaxLogicEngine and its test project

- [ ] **T10** — Change `TaxLogicEngine` and `TaxLogicEngine.Tests` to exactly
  `net10.0` and pin the test project to FR-4. Make no production C# or mapping
  JSON change. Prove both embedded-resource logical names, all T4/T5 parser and
  mapping results, year isolation, validation/error ordering, decimal half-up
  rounding, masked identifiers, and public/internal contracts pass unchanged.
  This completes the outer-to-inner TFM migration. Covers AC-3/AC-4/AC-7 for
  these projects, AC-22 (TaxLogicEngine), and AC-24. — files:
  `src/TaxLogicEngine/TaxLogicEngine.csproj`,
  `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj`,
  `specs/013-dotnet-10-platform-upgrade/status/T10.done` — depends on: T09

## Wave 9 — repository-wide .NET 10 enforcement

### T11 — Make the toolchain and PowerShell gate data-driven and exact

- [ ] **T11** — Add a PowerShell preflight that requires SDK `10.0.302`,
  parses every real project entry in `TaxAI.sln` without asserting a total
  project count, requires exactly one `TargetFramework` equal to `net10.0`,
  verifies the exact Microsoft/test package matrices and local EF manifest,
  proves every approved solution path remains exactly once, rejects active
  `net8.0`/`net9.0` target declarations and central props/targets/package files,
  and rejects extra/speculative projects. Invoke it from
  `Invoke-SolutionTests.ps1` before the existing TRX gate; preserve all
  missing/failed/skipped/unregistered/floor refusal behavior under Test SDK
  18.8.1. Add an independent xUnit repository test for the same target contract
  and raise the Portal floor to its actual count. Covers AC-1…AC-9, AC-13,
  AC-20, AC-23, AC-24, AC-25, AC-28, and AC-31's repository inventory. — files:
  `scripts/Assert-DotNet10Toolchain.ps1`,
  `scripts/Invoke-SolutionTests.ps1`,
  `tests/TaxAI.Portal.Tests/Upgrade/DotNet10RepositoryTests.cs`,
  `scripts/test-projects.json`,
  `specs/013-dotnet-10-platform-upgrade/status/T11.done` — depends on: T10

## Wave 10 — breaking-change dispositions (3 parallel)

Each ledger enumerates every item in the frozen official 9 and 10 index for its
subsystem. Every row records version, source URL/heading, repository
applicability, one allowed PRD disposition, and named passing proof. These
tasks write no product or test code.

### T12 — .NET 9 and .NET 10 compatibility ledger

- [ ] **T12** — Complete the runtime, libraries, SDK/MSBuild, deployment, and
  language/tooling dispositions from the official .NET 9 and .NET 10
  compatibility indexes. A not-present row names the scan/evidence that proves
  absence; an applicable row names a passing test or build/publish proof. No
  item is omitted because the direct migration skips .NET 9. Covers AC-10's
  .NET portion. — files:
  `specs/013-dotnet-10-platform-upgrade/compatibility/dotnet.md`,
  `specs/013-dotnet-10-platform-upgrade/status/T12.done` — depends on: T11

### T13 — ASP.NET Core 9 and 10 compatibility ledger

- [ ] **T13** — Complete every official ASP.NET Core 9 and 10 disposition,
  explicitly including cookie authentication's API-endpoint redirect change.
  Link applicable rows to the unchanged route/auth comparator and the OIDC,
  cookie, session, revocation, firm-selection, R-1, antiforgery,
  authorization, PII, and startup-refusal suites. Record that no endpoint
  metadata was manipulated to manufacture compatibility. Covers AC-10's
  ASP.NET Core portion, AC-11, AC-26, and AC-27. — files:
  `specs/013-dotnet-10-platform-upgrade/compatibility/aspnet-core.md`,
  `specs/013-dotnet-10-platform-upgrade/status/T13.done` — depends on: T11

### T14 — EF Core 9 and EF Core 10 compatibility ledger

- [ ] **T14** — Complete every official EF Core 9 and 10 disposition,
  explicitly including pending-model enforcement, parameterized collections,
  SQL Server `Application Name`, Azure SQL JSON mapping, and SQLite
  DateTimeOffset/affinity behavior. Link each applicable row to exact tests,
  query/SQL evidence, or the pending/model/schema/DDL proof; record the
  unchanged `UseSqlServer` choice and forbidden pre-10 switch. Covers AC-10's
  EF Core portion and AC-12…AC-21. — files:
  `specs/013-dotnet-10-platform-upgrade/compatibility/ef-core.md`,
  `specs/013-dotnet-10-platform-upgrade/status/T14.done` — depends on: T11

## Wave 11 — equivalence and security evidence (2 parallel)

### T15 — Prove zero EF model, schema, DDL, or migration drift

- [ ] **T15** — With local `dotnet-ef` 10.0.10 and a synthetic,
  environment-supplied encrypted SQL Server connection string, capture the
  upgraded canonical EF model, relational schema, and zero-to-initial-migration
  DDL. Run `migrations has-pending-model-changes` with Persistence and the
  Portal startup project. Compare the upgraded artifacts exactly to Wave 0,
  verify the three immutable migration hashes and migration file set, verify no
  suppression/empty migration/`UseAzureSql`/JSON remap/pre-10 switch, and record
  command outputs and pass/fail evidence. Do not connect to, update, or migrate
  a database. Covers AC-12…AC-21 and AC-30's migration-history portion. — files:
  `specs/013-dotnet-10-platform-upgrade/evidence/ef-model.net10.json`,
  `specs/013-dotnet-10-platform-upgrade/evidence/relational-schema.net10.json`,
  `specs/013-dotnet-10-platform-upgrade/evidence/initial-migration.net10.sql`,
  `specs/013-dotnet-10-platform-upgrade/evidence/pending-model.txt`,
  `specs/013-dotnet-10-platform-upgrade/ef-verification.md`,
  `specs/013-dotnet-10-platform-upgrade/status/T15.done` — depends on: T12,
  T13, T14

### T16 — Prove unchanged identity, tenant, session, and startup behavior

- [ ] **T16** — Capture the upgraded normalized route/auth result matrix and
  compare it exactly to Wave 0. Record targeted passing evidence for OIDC
  issuer/subject resolution, provider-token omission, provider-firm-claim
  discard, cookie flags and opaque ticket, durable session authority/lifetime/
  revocation/fail-closed storage, firm selection and old-session revocation,
  application-issued firm claim, R-1 on every protected request, antiforgery,
  authorization, fixed errors, no-SIN boundaries, Data Protection guards,
  Canadian residency requirements, and non-Development deployment refusal.
  No behavior assertion may be changed in this evidence task. Covers AC-11,
  AC-26, and AC-27. — files:
  `specs/013-dotnet-10-platform-upgrade/evidence/route-auth.net10.json`,
  `specs/013-dotnet-10-platform-upgrade/security-verification.md`,
  `specs/013-dotnet-10-platform-upgrade/status/T16.done` — depends on: T12,
  T13, T14

## Wave 12 — active guidance and rollback runbook

### T17 — Document local, CI, App Service, and rollback requirements

- [ ] **T17** — Re-inventory tracked active guidance, CI/CD,
  Docker/container, Azure/deployment/IaC, and props/targets paths before editing.
  The approved baseline contains none of the environment artifacts; if one has
  appeared, stop and amend this plan with its exact path rather than editing it
  under wildcard authority. Update `README.md` with SDK 10.0.302 verification,
  local tool restore/version, restore/build/raw test/full PowerShell gate,
  pending-model, Release publish, and no-deployment meaning; document the same
  gate order for future CI; document future code-based Azure App Service
  `.NET 10 (LTS)` runtime selection only after Canada Central/Canada East and
  OS/runtime availability preflight; retain encrypted transport, managed
  identity/Key Vault, Canadian Data Protection keys, and the separate
  deployment-spec refusal. Add rollback to
  `13097dada6b1aae070fc0006d48a6862ce9904f8`, SDK/runtime reversal, application
  slot/revision reversal for a future deployment, and explicitly no database
  rollback. Update root `CLAUDE.md` framework guidance from .NET 8 to .NET 10
  without changing product or module rules. Verify `AGENTS.md`, local
  instructions, and `NEXT.md` contain no stale framework guidance; do not edit
  them merely for coverage. Covers AC-29, AC-31, and AC-32. — files:
  `README.md`, `CLAUDE.md`,
  `specs/013-dotnet-10-platform-upgrade/status/T17.done` — depends on: T15,
  T16

## Wave 13 — final end-to-end verification

### T18 — Execute every acceptance gate and mark Spec 013 complete

- [ ] **T18** — From a clean worktree on the merged upgrade, record a pass/fail
  line with command/file evidence for AC-1…AC-32. Verify exact SDK/tool
  resolution and restore; every real solution project at exactly `net10.0`;
  exact package matrices; unchanged paths/references/contracts/resources;
  all three compatibility ledgers complete; migration set/hashes, no pending
  model, exact model/schema/DDL; route/auth/security equivalence; zero build
  errors and warnings within the approved multiset; `dotnet build TaxAI.sln`;
  `dotnet test TaxAI.sln`; `pwsh ./scripts/Invoke-SolutionTests.ps1` with exact
  per-project and aggregate totals, zero failed/skipped, and no floor decrease;
  Release Portal publish targeting `.NETCoreApp,Version=v10.0`; active guidance,
  absent/updated environment inventory, historical Specs 001–011 immutability,
  no unintended files, and `git diff --check`. Only after every check passes,
  update `NEXT.md` and root `CLAUDE.md` current state to record Spec 013
  complete and create the marker. If any check fails, record the failure in
  `verification.md`, do not claim completion, and do not create the marker.
  Covers AC-1…AC-32 and the Definition of Done. — files:
  `specs/013-dotnet-10-platform-upgrade/verification.md`, `NEXT.md`,
  `CLAUDE.md`,
  `specs/013-dotnet-10-platform-upgrade/status/T18.done` — depends on: T17

## Summary

| Wave | Tasks | Purpose |
|---|---|---|
| 0 | T01 | Freeze executable .NET 8 repository, contract, route/auth, and EF baselines |
| 1 | T02 | Pin SDK 10.0.302 and local dotnet-ef 10.0.10 |
| 2 | T03, T04 | Upgrade Portal and independent TaxCases in parallel |
| 3 | T05 | Upgrade EF Core/SQL Server/SQLite Persistence boundary |
| 4 | T06 | Upgrade Tenancy |
| 5 | T07 | Upgrade CaseReview |
| 6 | T08 | Upgrade CaseProcessing |
| 7 | T09 | Upgrade DocumentExtraction |
| 8 | T10 | Upgrade TaxLogicEngine and complete all-project net10.0 migration |
| 9 | T11 | Enforce the exact repository-wide .NET 10 toolchain in tests and PowerShell |
| 10 | T12, T13, T14 | Complete .NET, ASP.NET Core, and EF Core 9/10 ledgers in parallel |
| 11 | T15, T16 | Produce EF equivalence and security-equivalence evidence in parallel |
| 12 | T17 | Update active guidance, App Service preflight, and rollback runbook |
| 13 | T18 | Execute AC-1…AC-32 and record final completion |

**18 tasks across 14 waves.** The only parallel implementation wave is Portal
plus independent TaxCases; the compatibility and evidence waves parallelize
read-only proof with disjoint files. Every merge boundary remains buildable and
fully tested, no migration file is task-owned, and the final wave records exact
test totals rather than assuming them.
