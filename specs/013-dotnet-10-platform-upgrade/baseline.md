# Spec 013 approved .NET 8 baseline

## Source and purpose

- Approved rollback commit:
  `13097dada6b1aae070fc0006d48a6862ce9904f8`.
- Capture date: 2026-07-29.
- Solution: `TaxAI.sln`.
- Runtime baseline: every production and test project targets exactly
  `net8.0`.
- This evidence freezes behavior and contracts before the direct .NET 10
  migration. It does not authorize a database connection, update, migration,
  or deployment.

The JSON and SQL files in `baselines/` are generated evidence. The tests in
`TaxAI.Portal.Tests/Upgrade` and `Persistence.Tests/Upgrade` regenerate the
same canonical representations in memory and compare them exactly. They have
no update mode and do not write the approved artifacts.

## Repository and contract inventory

`baselines/repository-contract.json` records every approved solution project
at its path, assembly name, production/test kind, target framework, package
references, exact project references, test floors and totals, warning
multiset, immutable migration hashes, and environment paths absent from the
approved repository. The executable comparison requires every approved
project path exactly once without asserting a total solution project count.
It permits the planned framework and package changes while retaining their
historical .NET 8 values as rollback evidence.

`baselines/public-contracts.json` records user-declared public, protected, and
internal types and members for all eight production assemblies. Signatures
include accessibility, type/member shape, base types, interfaces, parameters,
generic constraints, constants, accessors, and contract-relevant attributes.
Private and compiler-generated members are excluded. Friend assemblies,
embedded-resource logical names, and every unordered collection are recorded
in ordinal order.

| Production assembly | Canonical signatures | Embedded resources |
|---|---:|---:|
| CaseProcessing | 28 | 0 |
| CaseReview | 77 | 0 |
| DocumentExtraction | 54 | 0 |
| Persistence | 91 | 0 |
| TaxAI.Portal | 81 | 0 |
| TaxCases | 181 | 0 |
| TaxLogicEngine | 232 | 2 |
| Tenancy | 242 | 0 |

The repository baseline also proves these paths were absent:
`.github`, `Directory.Build.props`, `Directory.Build.targets`,
`Directory.Packages.props`, `Dockerfile`, `docker-compose.yml`, and
`azure-pipelines.yml`.

## EF Core, schema, DDL, and migration inventory

The provider-specific comparison constructs the SQL Server design-time model
without connecting to a database:

- `baselines/ef-model.json`: 109 ordinally sorted entries covering entities,
  properties and store facets, value converters, keys, indexes, foreign keys,
  and navigations.
- `baselines/relational-schema.json`: 114 ordinally sorted entries covering
  tables, columns and store types, primary/unique keys, indexes, and foreign
  keys.
- `baselines/initial-migration.sql`: SQL Server script from database zero
  through `20260729021907_InitialPersistence`.

JSON comparison normalizes line endings and one final newline. DDL comparison
normalizes only CRLF/LF differences and exactly one final newline; SQL text,
batch boundaries, identifiers, types, constraints, indexes, and ordering are
otherwise exact.

The approved migration set and SHA-256 values are:

| File | SHA-256 |
|---|---|
| `src/Persistence/Migrations/20260729021907_InitialPersistence.cs` | `2997ABCF27357ABE8E36818D07C4435319A1F4B758480463DD7B1BA3B5C500A1` |
| `src/Persistence/Migrations/20260729021907_InitialPersistence.Designer.cs` | `0C7A963AF4DE6B3224EF05E2D2B322C08E0C67727A4781A2DEC3D098E087A81C` |
| `src/Persistence/Migrations/TaxAiDbContextModelSnapshot.cs` | `2452D274C7DEBEC0955BA59D00E892069F5FDB223D06F874E68494DF6E4DF2B4` |

## Route, authentication, and authorization inventory

`baselines/route-auth-contract.json` records endpoint metadata plus 26 actual
in-process responses: 13 route/method cases for both anonymous and
authenticated clients. Dynamic GUID and antiforgery values are replaced by
fixed tokens; OIDC `Location` retains the origin and path plus ordinally sorted
query-key names. Status, normalized `Location`, `WWW-Authenticate`, content
type, normalized body length, and normalized body SHA-256 remain exact.

| Method and path | Anonymous | Authenticated |
|---|---:|---:|
| `GET /` | 200 | 200 |
| `POST /` | 405 | 405 |
| `GET /health` | 200 | 200 |
| `POST /health` | 405 | 405 |
| `GET /sign-in` | 302 | 302 |
| `POST /sign-in` | 405 | 405 |
| `GET /firms` | 401 | 200 |
| `POST /firms` | 405 | 405 |
| `POST /firms/select` without antiforgery token | 401 | 400 |
| `GET /firms/select` | 405 | 405 |
| `POST /sign-out` | 401 | 200 |
| `GET /sign-out` | 405 | 405 |
| `GET /does-not-exist` | 404 | 404 |

The six endpoint metadata rows independently freeze allowed methods,
anonymous/required authorization, and antiforgery metadata. The test host uses
the production OIDC, cookie, durable server-side session, firm-membership, and
authorization pipeline with a static, non-network OIDC configuration.

## Build warnings

A non-incremental baseline build succeeded with zero errors and this approved
11-warning multiset:

| Warning | Count | Baseline locations |
|---|---:|---|
| `CS0649` | 2 | `T4SlipBoxAmountsTests.cs:24` |
| `xUnit2008` | 2 | `T4SlipParserPiiTests.cs:91,127` |
| `xUnit1031` | 6 | `SignInTests.cs:300`; `SessionLifetimeTests.cs:124`; `StartupRefusalTests.cs:359,362,456,459` |
| `xUnit2013` | 1 | `PortalSecurityTests.cs:567` |

This is a ceiling by code and multiplicity, not permission to add new warning
categories or instances. Later tasks must remove or explicitly dispose of
warnings introduced or exposed by the upgrade.

## Exact pre-upgrade test evidence

The approved commit ran all eight registered test projects with zero failed
and zero skipped tests:

| Test project | Passed |
|---|---:|
| TaxLogicEngine.Tests | 261 |
| TaxCases.Tests | 337 |
| DocumentExtraction.Tests | 673 |
| CaseProcessing.Tests | 594 |
| CaseReview.Tests | 482 |
| TaxAI.Portal.Tests | 403 |
| Tenancy.Tests | 289 |
| Persistence.Tests | 133 |
| **Total** | **3,172** |

T01 adds five Portal baseline tests and four Persistence baseline tests.
Their manifest floors are raised only after the complete solution gate proves
the resulting actual counts.

## Reproduction gates

The baseline artifacts and executable comparators are verified by:

```powershell
dotnet build TaxAI.sln
dotnet test TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1
git diff --check
```

The PowerShell gate remains authoritative for complete test execution because
it requires every registered project, no failures, no skips, and no floor
regression.
