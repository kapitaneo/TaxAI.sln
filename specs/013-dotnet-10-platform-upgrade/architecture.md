# Architecture: .NET 10 Platform Upgrade

**Feature class:** `platform`.

This is a toolchain and dependency migration. It changes no tax fact, tax-year
policy, parser, mapper, calculation, persistence schema, identity rule,
authorization decision, tenant boundary, endpoint contract, or deployment
authorization.

The approved baseline is `master` commit
`13097dada6b1aae070fc0006d48a6862ce9904f8`. All comparisons in this design use
that commit, not an inferred earlier state.

## Current structure

### Repository and toolchain

- `TaxAI.sln` is the sole solution used by the repository commands.
- No tracked `global.json` exists. At planning time the workstation resolves
  SDK `10.0.301`; SDK `10.0.302` must be installed before the SDK-pinning task
  can pass.
- No tracked `.config/dotnet-tools.json` exists, so repository EF commands
  currently depend on an operator-selected tool.
- No tracked CI/CD workflow, Docker/container definition, Azure/IaC/deployment
  definition, or repository-wide `.props`/`.targets` file exists.
- `scripts/Invoke-SolutionTests.ps1` runs `dotnet test TaxAI.sln`, parses TRX
  output, refuses missing/failed/skipped projects, and enforces the floors in
  `scripts/test-projects.json`.
- The verified baseline is 3,172 passed, zero failed, zero skipped. The build
  warning multiset is `CS0649` ×2, `xUnit2008` ×2, `xUnit1031` ×6, and
  `xUnit2013` ×1.

The plan never asserts that the solution has a fixed total project count.
Project verification is data-driven from the real entries in `TaxAI.sln`, while
the paths approved by the PRD are checked for presence and uniqueness.

### Projects and packages

Every currently present production and test project targets `net8.0`.

| Production project | Direct production project references | Framework-coupled package |
|---|---|---|
| `src/TaxLogicEngine/TaxLogicEngine.csproj` | none | none |
| `src/TaxCases/TaxCases.csproj` | `TaxLogicEngine` | none |
| `src/DocumentExtraction/DocumentExtraction.csproj` | `TaxLogicEngine` | none |
| `src/CaseProcessing/CaseProcessing.csproj` | `DocumentExtraction`, `TaxLogicEngine` | none |
| `src/CaseReview/CaseReview.csproj` | `CaseProcessing`, `TaxLogicEngine` | none |
| `src/Tenancy/Tenancy.csproj` | `CaseReview`, `TaxLogicEngine` | none |
| `src/Persistence/Persistence.csproj` | `Tenancy` | EF Core SqlServer/Design `8.0.0` |
| `src/TaxAI.Portal/TaxAI.Portal.csproj` | `CaseProcessing`, `Persistence`, `TaxLogicEngine`, `Tenancy` | OIDC `8.0.0` |

Each matching test project targets `net8.0` and pins xunit `2.5.3`,
xunit.runner.visualstudio `2.5.3`, Microsoft.NET.Test.Sdk `17.8.0`, and
coverlet.collector `6.0.0`. `Persistence.Tests` additionally pins EF Core
Sqlite `8.0.0`; `TaxAI.Portal.Tests` additionally pins ASP.NET Core MVC Testing
`8.0.0`.

### Persistence and security baseline

`Persistence` is the only production EF Core project. It owns one initial
migration, its designer, and the model snapshot. Those three files are immutable
and have the SHA-256 values stated in the PRD.

The EF10 parameterized-collection inventory is presently limited to:

- `expiredIds.Contains(session.SessionId)` in
  `SqlAuthenticationSessionStore`;
- `requestedFirmIds.Contains(firm.FirmId)` in `SqlIdentityDirectory`;
- `eligibleIds.Contains(record.RecordId)` in
  `SqlReviewRecordPurgeService`.

The portal exposes the existing `/`, `/health`, `/sign-in`, `/sign-out`,
`/firms`, and `/firms/select` surface plus the OIDC callback paths owned by the
handler. Existing tests already cover external OIDC, protected opaque-key
cookies, durable server-side sessions, expiry, revocation, firm selection,
R-1 membership verification, antiforgery, authorization, safe failures, and
the non-Development Data Protection/startup refusal.

## Target structure

All existing project paths, project names, assembly names, namespaces, public
and internal contracts, solution entries, project-reference arrows, and module
ownership remain in place. The target adds only platform-control and evidence
artifacts:

```text
global.json
.config/
  dotnet-tools.json
scripts/
  Assert-DotNet10Toolchain.ps1
  Invoke-SolutionTests.ps1          (only compatibility/gate changes)
specs/013-dotnet-10-platform-upgrade/
  prd.md
  architecture.md
  tasks.md
  baseline.md
  baselines/
    repository-contract.json
    public-contracts.json
    ef-model.json
    relational-schema.json
    initial-migration.sql
    route-auth-contract.json
  compatibility/
    dotnet.md
    aspnet-core.md
    ef-core.md
  evidence/
    ef-model.net10.json
    relational-schema.net10.json
    initial-migration.net10.sql
    pending-model.txt
    route-auth.net10.json
  ef-verification.md
  security-verification.md
  verification.md
  status/
```

The exact SDK contract is:

- SDK `10.0.302`;
- `allowPrerelease: false`;
- `rollForward: "disable"`.

The root tool manifest has `isRoot: true` and contains only `dotnet-ef`
`10.0.10`. The target package matrix is exactly the one in PRD FR-3 and FR-4:
Microsoft/ASP.NET Core/EF Core/SQLite packages and `dotnet-ef` at `10.0.10`,
Microsoft.NET.Test.Sdk `18.8.1`, xunit `2.9.3`,
xunit.runner.visualstudio `3.1.5`, and coverlet.collector `10.0.1`.

No `Directory.Build.props`, `Directory.Build.targets`,
`Directory.Packages.props`, new project, new module, CI workflow, container
file, or deployment definition is introduced.

## Module boundaries

The migration changes target frameworks and approved package versions, not
ownership:

- **TaxLogicEngine** remains deterministic tax logic and owns parsing,
  data-driven T4/T5 mappings, decimal/half-up rounding, masked in-memory
  identifiers, and tax-year isolation.
- **TaxCases** remains the deterministic engagement aggregate.
- **DocumentExtraction** remains the untrusted extraction contract; it still
  ships no provider or ingress implementation.
- **CaseProcessing** remains the deterministic T4 coordinator and invents no
  tax truth.
- **CaseReview** remains the immutable review/sign-off boundary.
- **Tenancy** remains the provider-independent trusted-firm, persisted-shape,
  identity/session, retention, and port boundary.
- **Persistence** remains the only production EF Core/SQL Server adapter and the
  sole owner of migrations.
- **TaxAI.Portal** remains the sole ASP.NET Core host and composition root. It
  renders no taxpayer data and remains undeployable outside the approved
  Development posture.

No task may add a package to a deterministic production module. The only
production package changes are the already-owned OIDC and EF Core references.

## Dependency direction

The project-reference graph is unchanged:

```text
TaxAI.Portal
  ├──► CaseProcessing ──► DocumentExtraction ──► TaxLogicEngine
  │          └──────────────────────────────────► TaxLogicEngine
  ├──► Persistence ──► Tenancy ──► CaseReview ──► CaseProcessing
  │                         │            └──────► TaxLogicEngine
  │                         └───────────────────► TaxLogicEngine
  ├──► Tenancy
  └──► TaxLogicEngine

TaxCases ──► TaxLogicEngine
```

Infrastructure and the host may depend inward; domain/application modules never
gain references to ASP.NET Core, EF Core, SQL, cloud SDKs, the host, or
Persistence. The upgrade does not add or remove any `ProjectReference`.

## Migration sequence

The sequence expands the PRD's seven stages into independently green tasks.
Every task branches from the latest human-merged `master`, runs the complete
test gate, and writes only its own `.done` marker after success.

1. **Freeze the .NET 8 baseline and verification contracts.** Capture the
   project/reference graph, assembly and source-visible public/internal
   contracts, warnings, test totals/floors, route/auth outcomes, exact EF model,
   relational schema, SQL Server DDL, and migration hashes from the approved
   SHA. Add executable comparisons that pass against the baseline before any
   upgrade.
2. **Pin SDK and local EF tool.** Add `global.json` and the root tool manifest.
   SDK `10.0.302` must already be installed; failure to resolve that exact SDK
   blocks the task.
3. **Migrate project pairs from outer consumers inward.** A `net10.0` project
   may reference a `net8.0` dependency, but the reverse is invalid. Therefore
   the safe order is Portal (parallel with independent TaxCases), Persistence,
   Tenancy, CaseReview, CaseProcessing, DocumentExtraction, then
   TaxLogicEngine. Each production project moves with its matching test project
   and that test project's package pins.
4. **Apply only proved compatibility remediation.** Portal and Persistence
   tasks own the narrowly identified ASP.NET Core/EF seams and their targeted
   tests. If the upgraded build and behavior need no source change, those source
   files remain untouched. No speculative .NET 10 feature adoption is allowed.
5. **Lock the repository-wide target contract.** Once every project is
   `net10.0`, make the PowerShell gate and data-driven repository tests enforce
   exact SDK, TFMs, packages, tool manifest, solution paths, and absence of a
   central build/version layer.
6. **Complete compatibility and equivalence evidence.** Record one disposition
   per official .NET, ASP.NET Core, and EF Core 9/10 breaking-change index item;
   prove pending-model, migration bytes, model, schema, DDL, route/auth, OIDC,
   cookie, session, firm, R-1, Data Protection, residency, and refusal outcomes.
7. **Update active guidance and verify end to end.** Update only active files
   that actually contain framework guidance, add the local/CI/App Service
   preflight and rollback runbook, execute every acceptance command, and record
   exact totals. Mark the spec complete only after every acceptance criterion
   passes.

Portal and TaxCases are the only parallel project-migration tasks. All later
project tasks are ordered by the reference graph. This preserves a buildable
mixed-TFM solution at every merge boundary without multi-targeting.

## Path and project-reference changes

### Existing paths

No source, test, solution, project, migration, or mapping path moves. Every
project path inventoried in the PRD remains exactly once in `TaxAI.sln`.
`TaxAI.sln` itself is not edited because neither project paths nor GUIDs change.

Every existing `ProjectReference` remains byte-equivalent after path separator
normalization. No assembly name or root namespace is added to a project.

### New paths

The only new operational paths are `global.json`,
`.config/dotnet-tools.json`, and `scripts/Assert-DotNet10Toolchain.ps1`.
The remaining new paths are verification artifacts under the Spec 013
directory and upgrade tests inside existing test projects.

### Conditionally present environment paths

The baseline has no tracked CI, container, Azure/IaC, or deployment file.
Implementation re-runs that inventory before documentation work:

- if none exists, none is created;
- if one has appeared, implementation stops and this task plan is amended with
  its exact path and ownership before it is edited.

This stop-and-amend rule is required because `tasks.md` must name exact files;
an implementation task may not mutate a path hidden behind a wildcard.

## Backward-compatibility requirements

### Assemblies and contracts

The Wave 0 contract captures, per production assembly:

- assembly name and referenced project assembly names;
- every user-declared public, protected, and internal type/member signature;
- generic arity and constraints, parameter modifiers/defaults, return types,
  accessibility, static/abstract/sealed state, and public attributes that form a
  contract;
- declared `InternalsVisibleTo` relationships;
- embedded-resource logical names.

Compiler-generated/private implementation details and metadata ordering are
excluded; all included records are sorted ordinally. The same normalizer runs
before and after the upgrade. Any difference requires a separately documented,
approved compatibility correction; this PRD authorizes none by default.

Tax mapping JSON and logical resource names remain byte-identical.

### Web behavior

The baseline route/auth contract records method, authentication state, status,
`Location`, `WWW-Authenticate`, content type, fixed body, and authorization
result for every existing route class. The .NET 10 cookie-handler API-endpoint
change is assessed against this matrix. No endpoint metadata is added or
removed merely to obtain a preferred redirect/401/403 result.

Existing OIDC issuer/subject resolution, provider-token omission,
provider-firm-claim discard, cookie flags, protected opaque ticket key,
server-authoritative session lifetime/revocation, firm re-selection,
application-issued `taxai_firm_id`, per-request R-1 verification, antiforgery,
and safe-error behavior remain unchanged.

### EF model, schema, and migration history

The baseline EF model canonicalizer records entities, properties, converters,
keys, foreign keys, delete behavior, indexes, concurrency/value generation,
table/schema names, and store facets. Unordered collections are sorted
ordinally; values are not normalized.

The relational schema canonicalizer records tables, columns, store types,
nullability, length, precision/scale, defaults/computed SQL, keys, foreign-key
actions, and indexes using the same deterministic ordering.

SQL Server DDL is generated from zero through
`20260729021907_InitialPersistence`. Comparison normalizes CRLF/LF and one final
newline only; it does not reorder statements or normalize SQL tokens.

The migration, designer, and snapshot are never task-owned and remain
byte-identical. `dotnet-ef migrations add`, `remove`, `update`, and database
migration application are prohibited. The only model-state command is
`migrations has-pending-model-changes`; the only script command is read-only DDL
generation.

### EF9/EF10 compatibility seams

- The EF9 pending-model exception is not suppressed. The local EF10 tool must
  report no pending change with `Persistence` as the target project and the
  portal as startup project.
- The three parameterized-collection queries named above must return the same
  ordered, firm-scoped result on approved fixtures. Generated SQL and query
  disposition are recorded.
- EF10 `Application Name` injection is exercised with absent, valid, malformed,
  encrypted, and unsafe connection strings. Guard decisions, safe diagnostics,
  transactions, and the one SQL Server data path stay unchanged.
- The provider remains `UseSqlServer`; no `UseAzureSql`, compatibility-level
  change, `json` store type, or `nvarchar(max)` remap is allowed.
- SQLite tests cover explicit non-zero offsets, zero/offsetless construction,
  type affinity, UTC instant preservation, and exclusive retention/session
  boundaries. The `Microsoft.Data.Sqlite.Pre10TimeZoneHandling` switch is
  forbidden.

## Build and test command changes

The solution commands remain stable; prerequisites and verification become
exact:

```powershell
dotnet --version
dotnet tool restore
dotnet tool run dotnet-ef --version
dotnet restore TaxAI.sln
dotnet build TaxAI.sln
dotnet test TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__TaxAiDatabase = '<synthetic encrypted SQL Server connection string>'
dotnet tool run dotnet-ef migrations has-pending-model-changes `
  --project src/Persistence/Persistence.csproj `
  --startup-project src/TaxAI.Portal/TaxAI.Portal.csproj `
  --context TaxAiDbContext

dotnet publish src/TaxAI.Portal/TaxAI.Portal.csproj `
  --configuration Release `
  --framework net10.0

git diff --check
```

The connection string used for design-time proof is synthetic, requires
encryption, validates certificates, is supplied through the environment, and is
never written to a tracked file or echoed. The pending-model and DDL commands do
not connect to, migrate, or update a database.

`scripts/Assert-DotNet10Toolchain.ps1` performs the data-driven SDK/TFM/package
preflight. `Invoke-SolutionTests.ps1` calls that preflight and retains its TRX
missing-project/failure/skip/floor protections. If Test SDK 18.8.1 produces the
same TRX contract, no other parsing behavior changes; any necessary parser
amendment must retain every existing refusal case.

Active documentation is updated as follows:

- `README.md`: exact local commands, CI gate order, future code-based Azure App
  Service .NET 10 runtime preflight, unchanged Canadian-region and deployment
  refusal, and rollback;
- root `CLAUDE.md`: `.NET 10.0` product/project guidance and final current
  state;
- `NEXT.md`: completion state only, after final verification.

`AGENTS.md` and local module instructions presently contain no framework-version
guidance and are not changed merely to create diff coverage.

## Trade-offs

### 1. Outer-to-inner staged TFMs vs one atomic all-project change

**Chosen:** upgrade production/test pairs from outer consumers inward, with
Portal and TaxCases parallel and the remaining graph sequential.

**Rejected:** update every project and every package in one task.

**Why:** one atomic task would be mechanically broad and would combine ASP.NET,
EF, deterministic modules, test tooling, and every compatibility seam in a
single review. Outer-to-inner staging keeps every intermediate `master`
buildable because `net10.0` can consume `net8.0`, while avoiding the invalid
reverse reference. It also gives each module owner a narrow diff and rollback
point.

### 2. Exact per-project versions vs central package management

**Chosen:** preserve explicit versions in each existing project.

**Rejected:** introduce `Directory.Packages.props` or a shared build props file.

**Why:** centralization is a separate repository architecture decision, not
required for the runtime upgrade. It would expand the blast radius, change
package ownership evidence, and violate the PRD's no-central-layer decision.

### 3. Repository-local `dotnet-ef` vs a global tool

**Chosen:** exact root tool manifest and `dotnet tool restore`.

**Rejected:** require a globally installed tool or let the SDK select an
unversioned command.

**Why:** schema proof must be reproducible in local and automated environments.
A global tool is machine state and can silently disagree with the project's EF
packages.

### 4. Exact committed equivalence artifacts vs migration regeneration

**Chosen:** capture canonical .NET 8 model/schema/DDL evidence and compare the
upgraded output.

**Rejected:** regenerate the migration/snapshot under EF10 or add an empty
migration to satisfy the pending-model check.

**Why:** regenerated artifacts would rewrite migration history and could conceal
schema drift. The product requirement is zero database change, so any
difference is a blocker, not an artifact to accept.

### 5. Conditional environment inventory vs speculative CI/deployment files

**Chosen:** verify absent environment artifacts and create none.

**Rejected:** add a workflow, Dockerfile, App Service definition, or IaC merely
to demonstrate .NET 10 syntax.

**Why:** this spec upgrades existing software; it does not authorize
deployment. Speculative files could weaken the current production refusal and
would invent an operational system that does not exist.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| SDK `10.0.302` is not installed, and `rollForward: disable` makes every `dotnet` command fail | T02 has an explicit external prerequisite and verifies `dotnet --version` before any repository change is marked complete. The planning workstation currently has only `10.0.301`; implementation must install `10.0.302`. |
| A lower-TFM consumer references an already-upgraded dependency | Use the outer-to-inner task order. No inner project upgrades until every production consumer is already `net10.0`. |
| Test package upgrades alter discovery or TRX shape and silently drop a suite | Retain and re-run the PowerShell gate after every task; validate all manifest projects, zero skipped, and floors never lower. Update TRX parsing only if proved necessary. |
| .NET 10 cookie behavior changes redirect vs 401/403 | Compare the unchanged route/auth matrix and existing OIDC/cookie/session tests; do not manipulate endpoint metadata to force a result. |
| EF10 collection translation changes ordering or firm scoping | Inventory the three production collection queries, assert results and ordering on relational fixtures, inspect SQL, and allow an override only for a proved regression. |
| EF10 changes effective SQL connection strings | Test guarded strings with and without `Application Name`; assert encryption/certificate requirements, safe diagnostics, registration, and transaction behavior. |
| EF/SQLite time handling shifts a UTC instant or exclusive boundary | Add explicit offset, zero/offsetless, affinity, session, and retention round trips; forbid the legacy compatibility switch. |
| A package/tool upgrade changes the EF model, DDL, migration history, or stored schema | Immutable migration hashes, pending-model command, canonical model/schema comparison, and line-ending-only DDL comparison all block completion on a difference. |
| Reflection comparison reports compiler-generated noise | Capture only user-declared public/protected/internal contracts and sort canonically; exclude compiler-generated/private implementation details in both baseline and target runs. |
| Build warnings are hidden by a new SDK or runner | Parse a clean build and require zero errors plus a warning multiset that is a subset of the approved baseline; no suppression or demotion is allowed. |
| Active docs imply production deployment is now allowed | README and CLAUDE retain the Development-only refusal, Canadian residency requirements, and the need for a separate deployment specification. Publish is build proof only. |
| CI/container/deployment files appear after planning | Stop and amend the plan with exact paths before editing; do not use wildcard authority. |
| Historical evidence is rewritten to say .NET 10 | Final diff verification excludes Specs 001–011 and their status/baseline/verification artifacts from all edits. |

## Rollback / recovery approach

Every task is independently green and human-merged, so a failed stage is
reverted as source rather than pushed forward with suppressions or compatibility
switches.

1. Stop on any SDK, restore, build, warning, test, publish, compatibility,
   security, migration-hash, pending-model, model, schema, or DDL failure.
2. Revert the failed task/PR. If the migration as a whole must be abandoned,
   restore source to
   `13097dada6b1aae070fc0006d48a6862ce9904f8`.
3. Restore the .NET 8 SDK/runtime used for that commit and remove only the local
   restored tool cache/build outputs; no tracked migration is run in either
   direction.
4. Do not apply a database rollback. This specification permits no database
   migration or schema/data change, so a database rollback would be both
   unnecessary and unauthorized.
5. A future separately approved deployment would reverse the application
   revision and App Service runtime through a slot or equivalent. This
   repository change creates no deployment and therefore executes no production
   rollback.

Local/test databases used for proof are disposable or backed up first. Real
Azure SQL data is never used for migration experimentation.

## Tax behaviour unchanged

Tax/CRA semantic impact: none.

No task edits TaxLogicEngine C# behavior, T4/T5 mapping JSON, tax-year
authorities, parser/mapper contracts, decimal/rounding rules, validation/error
ordering, or the engine's existing masked identifier behavior. The deterministic
modules receive only TFM and test-tooling manifest changes.

The baseline/target contract comparison and the complete existing suites must
prove identical tax, extraction, processing, review, tenancy, retention,
persistence, identity, authorization, and portal outcomes. A test may be
amended only where it freezes an obsolete framework/package version; no
behavior assertion, test case, manifest floor, or security refusal may be
removed, skipped, broadened, or weakened to make the upgrade pass.
