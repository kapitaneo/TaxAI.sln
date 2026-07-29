# Spec 013 final .NET 10 verification

## Final state

- Final implementation SHA:
  `86c82c6c440a15be8e5cc6d1648d534f911523be`
- Finalization base:
  `861fb1dc7835700a7cda29897f83eaf22f5f7621` (`origin/master`)
- Rollback baseline:
  `13097dada6b1aae070fc0006d48a6862ce9904f8`
- SDK: `10.0.302`
- Local `dotnet-ef`: `10.0.10`
- Result: PASS

This verification used no production secret or database. The pending-model
command received a process-only synthetic encrypted connection string and did
not connect, create, update, or migrate a database.

## Project and target-framework inventory

Every real project entry in `TaxAI.sln` occurs at its approved path and declares
exactly one `TargetFramework` equal to `net10.0`.

| Kind | Project | TFM |
|---|---|---|
| Production | `src/CaseProcessing/CaseProcessing.csproj` | `net10.0` |
| Production | `src/CaseReview/CaseReview.csproj` | `net10.0` |
| Production | `src/DocumentExtraction/DocumentExtraction.csproj` | `net10.0` |
| Production | `src/Persistence/Persistence.csproj` | `net10.0` |
| Production | `src/TaxAI.Portal/TaxAI.Portal.csproj` | `net10.0` |
| Production | `src/TaxCases/TaxCases.csproj` | `net10.0` |
| Production | `src/TaxLogicEngine/TaxLogicEngine.csproj` | `net10.0` |
| Production | `src/Tenancy/Tenancy.csproj` | `net10.0` |
| Test | `tests/CaseProcessing.Tests/CaseProcessing.Tests.csproj` | `net10.0` |
| Test | `tests/CaseReview.Tests/CaseReview.Tests.csproj` | `net10.0` |
| Test | `tests/DocumentExtraction.Tests/DocumentExtraction.Tests.csproj` | `net10.0` |
| Test | `tests/Persistence.Tests/Persistence.Tests.csproj` | `net10.0` |
| Test | `tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj` | `net10.0` |
| Test | `tests/TaxCases.Tests/TaxCases.Tests.csproj` | `net10.0` |
| Test | `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` | `net10.0` |
| Test | `tests/Tenancy.Tests/Tenancy.Tests.csproj` | `net10.0` |

No active tracked target declaration names `net8.0` or `net9.0`. Historical
baseline/rollback documentation retains those strings intentionally.

## Direct package inventory

The finalization performed targeted alignment only. It did not run an
update-all operation. Every test project uses the already-approved xunit v2
matrix; no project references `xunit.v3`.

| Owner(s) | Direct package | Version |
|---|---|---:|
| Every test project | `Microsoft.NET.Test.Sdk` | `18.8.1` |
| Every test project | `xunit` | `2.9.3` |
| Every test project | `xunit.runner.visualstudio` | `3.1.5` |
| Every test project | `coverlet.collector` | `10.0.1` |
| `TaxAI.Portal` | `Microsoft.AspNetCore.Authentication.OpenIdConnect` | `10.0.10` |
| `TaxAI.Portal.Tests` | `Microsoft.AspNetCore.Mvc.Testing` | `10.0.10` |
| `Persistence` | `Microsoft.EntityFrameworkCore.SqlServer` | `10.0.10` |
| `Persistence` | `Microsoft.EntityFrameworkCore.Design` (`PrivateAssets=all`) | `10.0.10` |
| `Persistence.Tests` | `Microsoft.EntityFrameworkCore.Sqlite` | `10.0.10` |
| `Persistence.Tests` | `SQLitePCLRaw.bundle_e_sqlite3` | `2.1.12` |

No other direct package reference exists. In particular, the accidental
TaxCases test telemetry references and xunit.v3 reference present on the
finalization base are removed.

## Exact build and test results

| Gate | Result |
|---|---|
| `dotnet tool restore` | PASS; `dotnet-ef` 10.0.10 restored |
| `dotnet --version` | PASS; `10.0.302` |
| `dotnet ef --version` | PASS; `10.0.10` |
| `dotnet restore TaxAI.sln` | PASS; all projects up to date |
| `dotnet build TaxAI.sln` | PASS; 0 errors, 0 warnings emitted by incremental build |
| `dotnet build TaxAI.sln --no-incremental` | PASS; 0 errors, exactly 11 approved warnings |
| `dotnet test TaxAI.sln` | PASS; 3,193 passed, 0 failed, 0 skipped |
| `pwsh ./scripts/Invoke-SolutionTests.ps1` | PASS; preflight and all eight projects/floors |
| pending-model command | PASS; no changes since last migration |
| `dotnet publish src/TaxAI.Portal/TaxAI.Portal.csproj -c Release` | PASS; `.NETCoreApp,Version=v10.0` |
| `git diff --check` | PASS |

The non-incremental warning multiset is unchanged from the approved ceiling:

| Diagnostic | Count |
|---|---:|
| `CS0649` | 2 |
| `xUnit2008` | 2 |
| `xUnit1031` | 6 |
| `xUnit2013` | 1 |
| **Total** | **11** |

There are no `NU1902`, `NU1903`, `xUnit2029`, `xUnit2031`, suppression,
`NoWarn`, severity demotion, audit disabling, or analyzer disabling changes.

Exact test totals:

| Test project | Passed | Floor | Failed | Skipped |
|---|---:|---:|---:|---:|
| TaxLogicEngine.Tests | 261 | 261 | 0 | 0 |
| TaxCases.Tests | 337 | 337 | 0 | 0 |
| DocumentExtraction.Tests | 673 | 673 | 0 | 0 |
| CaseProcessing.Tests | 594 | 594 | 0 | 0 |
| CaseReview.Tests | 482 | 482 | 0 | 0 |
| TaxAI.Portal.Tests | 411 | 411 | 0 | 0 |
| Tenancy.Tests | 289 | 289 | 0 | 0 |
| Persistence.Tests | 146 | 137 | 0 | 0 |
| **Total** | **3,193** |  | **0** | **0** |

## Migration, model, schema, and publish evidence

The migration set and SHA-256 values remain:

| File | SHA-256 |
|---|---|
| `20260729021907_InitialPersistence.cs` | `2997abcf27357abe8e36818d07c4435319a1f4b758480463dd7b1ba3b5c500a1` |
| `20260729021907_InitialPersistence.Designer.cs` | `0c7a963af4de6b3224ef05e2d2b322c08e0c67727a4781a2dec3d098e087a81c` |
| `TaxAiDbContextModelSnapshot.cs` | `2452d274c7debec0955ba59d00e892069f5fdb223d06f874e68494df6e4df2b4` |

The canonical model, schema, and DDL comparators pass with only the three
authorized symmetric metadata normalizations. No migration, model snapshot,
entity configuration, schema, production mapping, or mapping resource differs
from the rollback baseline. The local EF command reports:

> No changes have been made to the model since the last migration.

The Release Portal publish succeeds at
`src/TaxAI.Portal/bin/Release/net10.0/publish/`.

## AC-1 through AC-32

| AC | Result and evidence |
|---:|---|
| 1 | PASS — `global.json` exact SDK/prerelease/roll-forward contract; preflight/xUnit verifier. |
| 2 | PASS — root `dotnet --version` prints `10.0.302`. |
| 3 | PASS — every solution project has one `TargetFramework=net10.0`; data-driven preflight. |
| 4 | PASS — active project/build scan finds no `net8.0`/`net9.0` declaration. |
| 5 | PASS — every approved solution path occurs once and resolves. |
| 6 | PASS — exact Microsoft/ASP.NET Core/EF package owners/versions and Design private assets. |
| 7 | PASS — every test project has the exact approved xunit v2 matrix. |
| 8 | PASS — root manifest contains only `dotnet-ef` 10.0.10; restore/version pass. |
| 9 | PASS — no central build/package layer or speculative project exists. |
| 10 | PASS — complete .NET/ASP.NET Core/EF Core 9/10 ledgers name dispositions/proofs. |
| 11 | PASS — route/auth comparator is byte/field equivalent; no API metadata manipulation. |
| 12 | PASS — EF CLI and focused test report no pending model. |
| 13 | PASS — no pending-model warning suppression exists. |
| 14 | PASS — exact three-file migration set and hashes above. |
| 15 | PASS — canonical EF model equals baseline after only authorized metadata normalization. |
| 16 | PASS — canonical relational schema equals baseline exactly. |
| 17 | PASS — executable SQL/schema DDL equals baseline after only authorized normalization. |
| 18 | PASS — all inventoried collection-query results, order, firm scope, and SQL disposition. |
| 19 | PASS — Application Name accept/refuse and safe diagnostics remain approved. |
| 20 | PASS — unchanged `UseSqlServer`; no Azure SQL/compatibility/JSON remap. |
| 21 | PASS — SQLite offset/offsetless/affinity/UTC/exclusive-boundary tests; no pre-10 switch. |
| 22 | PASS — public/internal contracts, references, and embedded resources equal baseline. |
| 23 | PASS — 0 build errors; warning multiset exactly within the 11-warning ceiling. |
| 24 | PASS — 3,193 tests across all eight projects; zero failed/skipped; every floor met. |
| 25 | PASS — no manifest floor decreased; Portal floor increased to 411 for the new verifier. |
| 26 | PASS — all OIDC/cookie/session/revocation/R-1/firm/antiforgery/auth/PII suites. |
| 27 | PASS — Development fallback only; production startup/deployment remains refused. |
| 28 | PASS — Release Portal publish targets net10.0 and exits zero. |
| 29 | PASS — README/NEXT/CLAUDE active guidance is current; AGENTS/local guidance needed no version edit. |
| 30 | PASS — no spec 001–011 artifact changed; historical source/resource checks pass. |
| 31 | PASS — no CI/container/deployment/IaC file exists and none was created. |
| 32 | PASS — README covers local/CI/App Service preflight, refusal, rollback SHA, runtime/source reversal, and no database rollback. |

## Rollback and deployment disposition

Rollback is to source commit
`13097dada6b1aae070fc0006d48a6862ce9904f8`, with the recorded .NET 8
SDK/runtime/tool/package set and a future application slot/revision reversal.
There is no database rollback because spec 013 makes no database change.

Production deployment remains blocked. A successful Release publish does not
authorize deployment or real taxpayer data. A separate approved deployment
specification must provide and verify a .NET 10 LTS App Service runtime in
Canada Central/Canada East, Canadian Azure Storage/Key Vault Data Protection
keys, TLS/exposure, managed identity/secrets, operations, and production
provisioning before the existing non-Development refusal can be satisfied.
