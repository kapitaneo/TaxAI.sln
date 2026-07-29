# PRD 013 — .NET 10 Platform Upgrade

## Feature classification

**Class:** `platform`

Upgrade TaxAI from .NET 8 to .NET 10 LTS without changing product, tax,
security, tenancy, persistence, or deployment behaviour.

Tax/CRA semantic impact: none

## Problem

TaxAI needs one reproducible upgrade from `net8.0` to .NET 10 LTS, including
review of the .NET 9 changes skipped by the direct target change.

This is a platform migration, not a product feature. It must not change tax
parsing, mappings, calculations, tax-year isolation, PII handling, trusted-firm
isolation, identity, authorization, persistence schema, session revocation,
residency, or the portal's deployment refusal.

**Tax-year scope:** Not applicable.

## Current repository state and reuse analysis

### Verified baseline

The baseline is `master` commit
`13097dada6b1aae070fc0006d48a6862ce9904f8`, inventoried 2026-07-29.

- `pwsh ./scripts/Invoke-SolutionTests.ps1` passed 3,172 tests with zero failed
  and zero skipped.
- `dotnet build TaxAI.sln` succeeds with 11 known warnings: 2 `CS0649`,
  2 `xUnit2008`, 6 `xUnit1031`, and 1 `xUnit2013`.
- Test floors are owned by `scripts/test-projects.json`:

  | Manifest entry | Floor |
  |---|---:|
  | `TaxLogicEngine.Tests` | 261 |
  | `TaxCases.Tests` | 337 |
  | `DocumentExtraction.Tests` | 673 |
  | `CaseProcessing.Tests` | 594 |
  | `CaseReview.Tests` | 482 |
  | `TaxAI.Portal.Tests` | 403 |
  | `Tenancy.Tests` | 289 |
  | `Persistence.Tests` | 133 |

Every currently present production and test project below targets `net8.0`.
This PRD deliberately does not assert a total solution project count.

Production project presence:

- `src/CaseProcessing/CaseProcessing.csproj`
- `src/CaseReview/CaseReview.csproj`
- `src/DocumentExtraction/DocumentExtraction.csproj`
- `src/Persistence/Persistence.csproj`
- `src/TaxAI.Portal/TaxAI.Portal.csproj`
- `src/TaxCases/TaxCases.csproj`
- `src/TaxLogicEngine/TaxLogicEngine.csproj`
- `src/Tenancy/Tenancy.csproj`

Test project presence:

- `tests/CaseProcessing.Tests/CaseProcessing.Tests.csproj`
- `tests/CaseReview.Tests/CaseReview.Tests.csproj`
- `tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj`
- `tests/Persistence.Tests/Persistence.Tests.csproj`
- `tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj`
- `tests/TaxCases.Tests/TaxCases.Tests.csproj`
- `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj`
- `tests/Tenancy.Tests/Tenancy.Tests.csproj`

Current framework-coupled packages are OIDC, EF Core SqlServer/Design/Sqlite,
and MVC Testing 8.0.0. Tests pin xunit/runner 2.5.3, Test.Sdk 17.8.0, and
coverlet 6.0.0.

The baseline has no tracked `global.json`, `.config/dotnet-tools.json`, CI/CD,
Docker/container definition, Azure/deployment/IaC file, or repository-wide
`.props`/`.targets` file.

### Reuse and new work

Reuse unchanged:

- `TaxAI.sln`, all projects, module boundaries, public contracts, and project
  references;
- `scripts/Invoke-SolutionTests.ps1` as the full-suite gate and every existing
  manifest floor;
- explicit per-project package ownership;
- all tax, identity, tenancy, persistence, portal, and security behaviour/tests;
- the repository-owned EF migration and model snapshot.

Amend existing version assertions rather than deleting or broadening them,
including those in:

- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`;
- `tests/TaxAI.Portal.Tests/HealthEndpointTests.cs`;
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`;
- `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`;
- `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`.

Genuinely new work is limited to exact SDK/tool manifests, exact .NET 10
package pins, compatibility dispositions for versions 9 and 10, regression and
schema-equivalence proof, version-test amendments, and environment/rollback
guidance. No new application module, endpoint, data field, database object, tax
rule, or user-facing capability is required.

## Desired outcome

The same TaxAI software builds, tests, and publishes with the exact .NET 10 LTS
toolchain below. The repository proves all applicable breaking changes in both
major versions were reviewed, the persistence model/schema/DDL is unchanged,
and every security-bearing behaviour still fails closed.

## User stories

1. As a developer, I can restore repository-local tools and use the exact SDK.
2. As a maintainer, I can audit .NET, ASP.NET Core, and EF Core 9/10
   compatibility dispositions and their proof.
3. As a tax reviewer, I can verify no parser, mapper, calculation, configuration,
   tax-year rule, or tax result changed.
4. As a security reviewer, I can verify OIDC, cookies, durable sessions, R-1,
   firm selection, Data Protection, residency, and deployment refusal.
5. As a persistence reviewer, I can verify exact migration bytes, model, schema,
   and DDL equivalence.
6. As an operator, I can identify local, CI, and future App Service requirements
   and roll source back to the named baseline.

## Functional requirements

### SDK, projects, packages, and tools

**FR-1 `[ASSUMED]` — SDK.** Add root `global.json` selecting SDK `10.0.302`,
`allowPrerelease: false`, and `rollForward: "disable"`. Repository commands
must fail clearly if that exact SDK is unavailable.

**FR-2 `[ASSUMED]` — TFM.** Every production and test project present in
`TaxAI.sln` at implementation time must target exactly `net10.0`. Migrate
directly from `net8.0`; do not commit `net9.0`, multi-target, or retain an old
TFM.

**FR-3 `[ASSUMED]` — Microsoft packages.** Preserve current ownership and pin:

| Project | Package | Version |
|---|---|---:|
| `src/TaxAI.Portal/TaxAI.Portal.csproj` | `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 10.0.10 |
| `src/Persistence/Persistence.csproj` | `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.10 |
| `src/Persistence/Persistence.csproj` | `Microsoft.EntityFrameworkCore.Design` | 10.0.10 |
| `tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj` | `Microsoft.AspNetCore.Mvc.Testing` | 10.0.10 |
| `tests/Persistence.Tests/Persistence.Tests.csproj` | `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.10 |

Design retains `PrivateAssets="all"`. Do not add a direct EF base package merely
because it is transitive from the provider.

**FR-4 `[ASSUMED]` — Test packages.** Every test project pins
`Microsoft.NET.Test.Sdk` 18.8.1, `xunit` 2.9.3,
`xunit.runner.visualstudio` 3.1.5, and `coverlet.collector` 10.0.1. No range,
wildcard, unversioned reference, or prerelease is allowed.

**FR-5 `[ASSUMED]` — EF tool.** Add root manifest
`.config/dotnet-tools.json` (`isRoot: true`) containing exactly `dotnet-ef`
10.0.10. Local and automated use starts with `dotnet tool restore`; no global
tool is required.

**FR-6 `[ASSUMED]` — No central build layer.** Do not introduce
`Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`,
or another central-version mechanism.

### Both-version compatibility review

**FR-7 — Complete review.** Record a disposition for every potentially
applicable item in the official .NET, ASP.NET Core, and EF Core breaking-change
indexes for both 9 and 10, frozen to the URLs retrieved 2026-07-29 below.
Allowed dispositions are: not present; exercised and unchanged; remediation
required; intentionally accepted without product-semantic change. Every
applicable row names passing proof.

**FR-8 — Cookie API endpoints.** Explicitly assess .NET 10's change from
cookie redirects to 401/403 for known API endpoints. Preserve each existing
route's baseline status, redirect/challenge headers, body, authentication, and
authorization outcome. Do not add/remove API metadata merely to manufacture a
result.

**FR-9 — EF9 pending model.** Run the upgraded local tool's pending-model check
using Persistence and the portal startup project. It reports no pending changes.
Do not suppress `PendingModelChangesWarning` or add an empty migration.

**FR-10 — EF10 collection translation.** Inventory every parameterized-
collection query and prove identical results, contractually required ordering,
firm scoping, and authorization. Record SQL disposition. A translation override
is allowed only for a proved regression and may not weaken behaviour.

**FR-11 — EF10 Application Name.** Prove injected `Application Name` does not
bypass `SqlServerConnectionGuard`, expose secrets, change trusted validation or
transactions, or create another data path. Any explicit value is fixed,
non-secret, and remains guarded.

**FR-12 — EF10 Azure SQL JSON.** Prove no `UseAzureSql`, new compatibility
level, `json` store type, or remap of an existing `nvarchar(max)` column. No JSON
column migration is permitted.

**FR-13 — EF10 SQLite DateTimeOffset.** Exercise explicit-offset, offsetless,
and relevant SQLite-affinity cases. Preserve existing UTC instants and exclusive
boundary semantics without `Microsoft.Data.Sqlite.Pre10TimeZoneHandling`.

### Migration, model, schema, and DDL

**FR-14 — Immutable migrations.** No migration may be added, removed, renamed,
regenerated, or squashed. These files remain byte-identical:

| File | SHA-256 |
|---|---|
| `src/Persistence/Migrations/20260729021907_InitialPersistence.cs` | `2997ABCF27357ABE8E36818D07C4435319A1F4B758480463DD7B1BA3B5C500A1` |
| `src/Persistence/Migrations/20260729021907_InitialPersistence.Designer.cs` | `0C7A963AF4DE6B3224EF05E2D2B322C08E0C67727A4781A2DEC3D098E087A81C` |
| `src/Persistence/Migrations/TaxAiDbContextModelSnapshot.cs` | `2452D274C7DEBEC0955BA59D00E892069F5FDB223D06F874E68494DF6E4DF2B4` |

**FR-15 `[ASSUMED]` — Exact proof.** Capture deterministic reference artifacts
from the baseline SHA and compare upgraded output exactly:

- EF model: entities/properties, converters, keys/FKs/delete behaviour, indexes,
  concurrency/value generation, table/schema names, and store facets;
- relational schema: tables, columns, types, nullability, length,
  precision/scale, defaults/computed SQL, keys/FKs/actions, and indexes;
- SQL Server DDL from zero through `20260729021907_InitialPersistence`.

The comparison must preserve exact application model, relational schema, and
database behaviour. In addition to the newline/final-newline and deterministic
ordering normalization already used by the Wave 0 comparator, only the following
deterministic tool/runtime metadata normalization is allowed:

- normalization of the System.Private.CoreLib assembly Version component inside
  CLR type metadata, while preserving the exact CLR type, generic structure,
  nullability, assembly name, culture, and public-key token;
- removal of standalone GO batch-separator lines from generated SQL;
- normalization of only the __EFMigrationsHistory ProductVersion value.

These normalizations must be applied symmetrically to baseline and upgraded
artifacts.

No normalization may conceal a difference in entities, properties, CLR types,
converters, tables, schemas, columns, SQL types, nullability, lengths,
precision/scale, defaults, computed SQL, keys, foreign keys, delete actions,
indexes, migration operations, or executable SQL statements.

Any such semantic difference blocks completion.

### Preserved behaviour and boundaries

**FR-16 — Tax and public contracts.** Preserve all parser, validation, mapping,
rounding, calculation, configuration, tax-year-isolation, error-order, and
masked-engine-identifier behaviour; mapping resources; public API signatures;
project references; module ownership; and forbidden-dependency rules.

**FR-17 — Identity.** Preserve external OIDC, issuer/subject local resolution,
provider-token omission, provider-firm-claim discard, and existing challenge,
sign-in, and sign-out flows.

**FR-18 — Cookies/sessions.** Preserve protected opaque-key-only cookies and
their flags, durable server-authoritative sessions, lifetime checks, server-side
revocation, and refusal of copied/expired/revoked/unknown/unavailable state.

**FR-19 — Firm selection and R-1.** Preserve explicit selection, session
revocation/reissuance, application-issued `taxai_firm_id`, caller-firm refusal,
and active-user/active-membership verification on every protected request.

**FR-20 — PII, residency, and refusal.** Preserve no-full-SIN globally and
no-SIN-in-any-form at web/persistence; Canada Central primary and Canada East
Canadian DR/backup only; encrypted transport; managed identity/Key Vault;
Canadian Data Protection keys; non-Development durable key repository,
encryptor, session, and transport guards; and refusal to deploy until a separate
deployment specification is approved.

### Build, tests, environments, and recovery

**FR-21 — Warning budget.** Build with zero errors and no new diagnostic. The
warning multiset is a subset of: `CS0649` ≤ 2, `xUnit2008` ≤ 2,
`xUnit1031` ≤ 6, `xUnit2013` ≤ 1. Do not suppress, disable, or demote warnings.

**FR-22 — Test/publish gates.** The full completion gate is
`pwsh ./scripts/Invoke-SolutionTests.ps1`: every manifest project runs, nothing
fails/skips, and each floor is met or raised, never lowered. The portal also
publishes successfully for `net10.0`; publishing does not authorize deployment.

**FR-23 — Exact version tests.** Amend all implementation-time version
assertions to the exact values above and add repository-wide coverage where a
required version is not frozen. Do not broaden an exact assertion to `10.0.x`.

**FR-24 `[ASSUMED]` — Active guidance.** Update active `README.md`, `NEXT.md`,
`CLAUDE.md`, and `AGENTS.md` only where an implementation-time scan finds
version guidance. Baseline finds such guidance only in root `README.md` and root
`CLAUDE.md`. Historical specs/status/baselines/verification for 001–011 are
immutable.

Use existing `README.md` as the active runbook for:

- local SDK 10.0.302 verification, tool restore, build, full tests, and publish;
- CI SDK/tool restore, build, pending-model, full-test, and publish gates;
- future code-based Azure App Service .NET 10 LTS configuration, preceded by
  region/OS runtime availability checks;
- unchanged deployment refusal and absence of deployment creation here;
- rollback to `13097dada6b1aae070fc0006d48a6862ce9904f8`.

**FR-25 `[ASSUMED]` — Conditional environment inventory.** Before editing,
re-inventory tracked CI/CD, Docker/container, Azure/deployment/IaC,
props/targets, and active guidance. Update a newly present artifact to the exact
SDK/runtime and gates; if absent, create none. Any discovered deployment file
retains deployment refusal.

**FR-26 — Rollback.** Any failed compatibility, schema, security, build, test,
or publish gate triggers source rollback to the baseline SHA. Because no
migration/schema difference is permitted, database rollback is neither required
nor authorized. A future separately approved deployment must additionally
reverse application revision and App Service runtime via slots or equivalent.

## Migration constraints

Implementation order is `[ASSUMED]`:

1. Re-inventory and capture baseline warnings, tests, API/routes/auth, EF model,
   schema, and DDL from the named SHA.
2. Add exact SDK/tool manifests; verify resolution.
3. Update TFMs, packages, and exact version tests coherently.
4. Make only compatibility-review-backed source/test amendments.
5. Prove pending model, migration hashes, exact model/schema/DDL.
6. Run targeted tax/persistence/identity/security tests, then build, full gate,
   and publish.
7. Update active guidance/conditional artifacts and record rollback proof.

No migration/schema change, new package role beyond local `dotnet-ef`, central
build layer, project/source move, new module, weakened test, or suppression is
permitted.

## Preserved behaviour

All existing production behaviour remains identical and the full suite remains
green, including deterministic T4/T5 results; extraction, processing, review,
tenancy, retention, purge, and persistence contracts; schema/DDL; portal routes;
OIDC, cookies, sessions, revocation, firm selection, R-1, antiforgery and
authorization; PII boundaries; residency; startup guards; and deployment
refusal.

## Out of scope

- Any accidental or intentional tax/CRA behaviour change.
- New capability, endpoint, UI, taxpayer-data surface, upload, OCR/LLM, T1,
  NETFILE/EFILE, provisioning, module, project, or restructuring.
- Database/model/schema change, new/regenerated/squashed migration, migration
  application, data transformation, or SQLite pre-10 compatibility switch.
- Creating CI/CD, containers, Azure/IaC, secrets, resources, or deployment.
- Resolving or weakening the deployment block.
- Unrelated warning cleanup or adoption of unrelated .NET 10 features.
- Editing historical specs 001–011 or their status/baseline/verification.

## Acceptance criteria

Each criterion is one mechanically executable check.

1. **SDK manifest.** Given `global.json`, When parsed, Then its version,
   prerelease, and roll-forward values equal FR-1 exactly.
2. **SDK resolution.** Given SDK 10.0.302 is installed, When `dotnet --version`
   runs at repository root, Then it prints `10.0.302`.
3. **All TFMs.** Given every real project entry in `TaxAI.sln`, When a
   data-driven verifier reads each project, Then it declares exactly one
   `TargetFramework` of `net10.0` without asserting a total project count.
4. **No old TFM.** Given active tracked project/build files outside historical
   specs/output, When scanned, Then no target declaration names `net8.0` or
   `net9.0`.
5. **Project path preservation.** Given `TaxAI.sln`, When read, Then every path
   inventoried in this PRD remains present exactly once.
6. **Microsoft package matrix.** Given the four owning project files, When their
   framework-coupled references are read, Then names, owners, exact versions,
   and Design private-assets match FR-3.
7. **Test package matrix.** Given every test project, When its standard
   references are read, Then all four exact FR-4 versions are present.
8. **Local EF manifest/tool.** Given a clean tool cache, When the manifest is
   parsed, restored, and queried, Then it contains only `dotnet-ef` 10.0.10 and
   resolves that version.
9. **No central/speculative layer.** Given tracked files after upgrade, When
   scanned, Then no new central props/targets/package file or speculative
   module/project exists.
10. **Compatibility ledger.** Given the frozen official indexes, When the
    ledger is validated, Then every .NET/ASP.NET Core/EF Core 9/10 item has one
    allowed disposition and every applicable row names passing proof.
11. **Cookie endpoint behaviour.** Given every existing route, When baseline
    and .NET 10 auth-response snapshots are compared, Then status, redirect,
    challenge, body, authentication, and authorization match.
12. **Pending model.** Given local EF 10.0.10 with Persistence and portal
    startup projects, When `has-pending-model-changes` runs, Then it exits zero
    and reports none.
13. **No pending suppression.** Given tracked source/configuration, When
    scanned, Then no `PendingModelChangesWarning` suppression exists.
14. **Migration set/bytes.** Given `src/Persistence/Migrations`, When its tracked
    files and SHA-256 values are verified, Then only FR-14's three paths exist
    and all three hashes match.
15. **Exact EF model.** Given deterministic baseline/upgraded model manifests,
    When compared, Then every model-bearing value is exactly equal.
    System.Private.CoreLib assembly-version metadata is normalized according to
    FR-15 before comparison.
16. **Exact schema.** Given deterministic baseline/upgraded relational schema
    manifests, When compared, Then every schema-bearing value is exactly equal.
17. **Exact DDL.** Given baseline and upgraded SQL Server scripts, When
    both are normalized only by the targeted FR-15 runtime/tooling metadata rules,
    Then the remaining executable SQL and schema-bearing content are exactly equal.
18. **Collection queries.** Given every inventoried parameterized-collection
    query, When run on baseline fixtures under EF10, Then ordered results and
    trusted-firm scope equal baseline.
19. **Application Name.** Given guarded strings with/without application name,
    When registration/guard execute, Then accept/refuse and safe diagnostics
    match baseline.
20. **No JSON remap.** Given the upgraded relational model/configuration, When
    inspected, Then no `UseAzureSql`, new compatibility level, `json` store
    type, or remap exists.
21. **SQLite time semantics.** Given offset/offsetless/affinity fixtures, When
    round-tripped under Sqlite 10.0.10, Then UTC instants and exclusive
    boundaries match approved values and the pre-10 switch is absent.
22. **Public/module contracts.** Given baseline/upgraded assemblies and project
    manifests, When normalized signatures/references are compared, Then they
    are exactly equal.
23. **Build budget.** Given a clean `dotnet build TaxAI.sln`, When diagnostics
    are parsed, Then errors are zero and warnings are a subset of FR-21.
24. **Full behaviour gate.** Given the upgraded repository, When
    `pwsh ./scripts/Invoke-SolutionTests.ps1` runs unfiltered, Then all existing
    tax/production regressions pass, every manifest project runs, failures and
    skips are zero, and every floor is met.
25. **Floors never fall.** Given baseline/upgraded test manifests, When matching
    entries are compared, Then no `minimumTests` decreased.
26. **Identity/session security.** Given existing OIDC, cookie, lifetime,
    revocation, R-1, firm-selection, antiforgery, authorization, and PII suites,
    When run on .NET 10, Then every assertion passes unchanged.
27. **Startup/deployment refusal.** Given Development and non-Development
    startup fixtures and tracked deployment inventory, When verified, Then only
    approved Development fallback starts and production remains refused.
28. **Publish.** Given exact SDK/packages, When the portal publishes for
    `net10.0`, Then publish exits zero and targets `.NETCoreApp,Version=v10.0`.
29. **Active guidance.** Given active README/NEXT/CLAUDE/AGENTS files, When
    version guidance is scanned, Then present values match this PRD and files
    without version guidance were not changed merely for the upgrade.
30. **Historical immutability.** Given the diff to baseline, When specs 001–011
    and their status/baseline/verification paths are checked, Then none changed.
31. **Conditional environment update.** Given implementation-time inventory,
    When CI/container/deployment/IaC paths are evaluated, Then each discovered
    path uses approved SDK/runtime/gates, or none existed and none was created.
32. **Runbook/rollback.** Given `README.md`, When its environment and rollback
    sections are validated, Then they cover local, CI, App Service .NET 10 LTS
    preflight, unchanged deployment refusal, baseline SHA, source/runtime
    reversal, and no database rollback/deployment authorization.

## Rollback and recovery

Stop and revert source to
`13097dada6b1aae070fc0006d48a6862ce9904f8` if any compatibility, migration,
model/schema/DDL, API, security, PII, build, test, or publish check fails. Do not
partially ship. Local/test databases are disposable or backed up before proof.
No database rollback is permitted because no schema change is permitted.
Production deployment remains blocked.

## Open questions

### Class B decisions made for review

- `[ASSUMED]` Exact SDK 10.0.302; prereleases false; roll-forward disabled.
- `[ASSUMED]` Direct `net8.0` → `net10.0`, reviewing both 9 and 10.
- `[ASSUMED]` Exact Microsoft/EF/SQLite/`dotnet-ef` 10.0.10 pins.
- `[ASSUMED]` Exact Test.Sdk 18.8.1, xunit 2.9.3, runner 3.1.5, coverlet 10.0.1.
- `[ASSUMED]` Root `.config/dotnet-tools.json`, containing only local EF.
- `[ASSUMED]` Explicit per-project versions; no central props/targets.
- `[ASSUMED]` Exact baseline model/schema/DDL with only stated normalization.
- `[ASSUMED]` Seven-step ordering and warning inventory as a ceiling.
- `[ASSUMED]` Existing README is the runbook; other active guidance changes
  only where version text exists.
- `[ASSUMED]` Re-inventory conditional environment artifacts; create none when
  absent.
- `[ASSUMED]` Failed gate rolls source back to the baseline; no DB rollback.

### Blocking Class A questions

None. This PRD makes zero Class A assumptions and authorizes zero tax, CRA, PII,
tenant-isolation, identity, security, residency, or deployment-posture changes.

## CRA sources

No external tax sources required; this feature introduces no tax facts.

## Official platform sources

Official Microsoft/NuGet sources (retrieved 2026-07-29):

| Subject | URL |
|---|---|
| .NET 10 SDK 10.0.302/runtime 10.0.10 | https://dotnet.microsoft.com/en-us/download/dotnet/10.0 |
| .NET 10 LTS support | https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core |
| `global.json` | https://learn.microsoft.com/en-us/dotnet/core/tools/global-json |
| Local tools | https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools |
| .NET 9 / .NET 10 breaking changes | https://learn.microsoft.com/en-us/dotnet/core/compatibility/9.0 ; https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0 |
| ASP.NET 8→9 / 9 breaking changes | https://learn.microsoft.com/en-us/aspnet/core/migration/80-to-90?view=aspnetcore-10.0 ; https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/9/overview?view=aspnetcore-10.0 |
| ASP.NET 9→10 / 10 breaking changes | https://learn.microsoft.com/en-us/aspnet/core/migration/90-to-100?view=aspnetcore-10.0 ; https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/overview?view=aspnetcore-10.0 |
| Cookie API endpoint status change | https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/cookie-authentication-api-endpoints?view=aspnetcore-10.0 |
| EF Core 9 / 10 breaking changes | https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes ; https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes |
| EF pending-model check | https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing |
| Azure App Service ASP.NET Core runtime | https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnetcore |
| OIDC / MVC Testing 10.0.10 | https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.OpenIdConnect/10.0.10 ; https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/10.0.10 |
| EF SqlServer / Design / Sqlite 10.0.10 | https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer/10.0.10 ; https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.10 ; https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/10.0.10 |
| `dotnet-ef` 10.0.10 | https://www.nuget.org/packages/dotnet-ef/10.0.10 |
| Test.Sdk 18.8.1 / xunit 2.9.3 / runner 3.1.5 / coverlet 10.0.1 | https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.8.1 ; https://www.nuget.org/packages/xunit/2.9.3 ; https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5 ; https://www.nuget.org/packages/coverlet.collector/10.0.1 |

## Definition of Done

- [x] Required structure opens with explicit `platform` classification.
- [x] `Tax/CRA semantic impact: none` and `Not applicable` tax-year scope.
- [x] Current state/reuse are grounded in the tree without a total project count.
- [x] Desired outcome, stories, requirements, constraints, preserved behaviour,
  out of scope, rollback, ACs, questions, and CRA sources are complete.
- [x] Every AC maps to one executable check.
- [x] Existing tax behaviour and the full suite must remain unchanged/green.
- [x] .NET/ASP.NET/EF 9 and 10 reviews and named breaking changes are required.
- [x] Migration bytes, pending model, exact model/schema/DDL proof are required.
- [x] OIDC, cookies, durable sessions, firm selection, R-1, Data Protection,
  PII, residency, and deployment refusal are preserved.
- [x] Historical records are immutable; no application feature/module invented.
- [x] Class B choices are `[ASSUMED]`; zero Class A assumptions/questions.

Ready for: /plan
