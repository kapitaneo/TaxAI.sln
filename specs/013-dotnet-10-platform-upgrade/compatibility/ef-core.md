# Spec 013 EF Core 9 and EF Core 10 compatibility ledger

## Sources and evidence

- [EF Core 9 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- [EF Core 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes)

Named evidence:

- **EF baseline comparator:** `EfUpgradeBaselineTests` proves the canonical
  model, relational schema, initial SQL Server DDL, migration set, and three
  migration hashes.
- **EF10 compatibility:** `EfCore10CompatibilityTests` proves parameterized
  collections, firm/order semantics, SQL disposition, `Application Name`,
  unchanged `UseSqlServer`, no `UseAzureSql`/compatibility-level/JSON remap,
  SQLite affinity/UTC/exclusive-boundary behavior, absence of the pre-10
  switch, and no pending model.
- **CLI pending-model gate:** local `dotnet-ef` 10.0.10 reports no changes with
  Persistence plus the Portal startup project.
- **Source/package scan:** no Cosmos provider, compiled model, custom relational
  convention/logger, automatic migration, migration transaction, or runtime
  schema mutation exists.

## EF Core 9 relational/general changes

| Official heading | Applicability and disposition | Named passing proof |
|---|---|---|
| Exception when applying migrations with pending model changes | Applicable and verified. TaxAI does not apply migrations at runtime, does not suppress `PendingModelChangesWarning`, and the CLI plus focused test report no pending model. | CLI pending-model gate; `CurrentSqlServerModelHasNoPendingChanges` |
| Exception when applying migrations in an explicit transaction | Not present: no `Migrate`/`MigrateAsync`, automatic migration, or external migration transaction. | Source scan; startup tests |
| Microsoft.EntityFrameworkCore.Design not found with EF tools | Applicable and verified. Design remains 10.0.10 with `PrivateAssets=all`; local EF tooling builds and resolves the context. | Tool restore/version; pending-model gate; repository preflight |
| EF.Functions.Unhex returns nullable byte array | Not present. | Source scan |
| Compiled models reference converter methods directly | Not present: no compiled model. Existing converters produce the identical model/schema. | Source scan; EF baseline comparator |
| SqlFunctionExpression nullability arity validated | Not present: no custom SQL-function expression. | Source scan |
| ToString translation returns empty string for null | No affected query. All inventoried repository queries pass with unchanged results. | EF10 compatibility and repository suites |
| Shared framework dependencies updated | Applicable and verified: all EF/ASP.NET packages are pinned to 10.0.10 and restore/build succeed. | Repository preflight; build |
| EF tools no longer support .NET Framework | Not applicable: every project is single-target net10.0. | Repository preflight |
| EF.Constant/EF.Parameter unavailable in compiled queries | Not present: neither API nor compiled queries are used. | Source scan |
| NoTrackingWithIdentityResolution prohibited for JSON collections | Not present: no mapped JSON collection or affected query. | Model/schema scan |

## EF Core 9 Cosmos changes

TaxAI has no Cosmos provider/package/configuration. Each official Cosmos item
is therefore `not present`, proved by the direct package/model/source scan:

| Official heading | Disposition |
|---|---|
| Discriminator property renamed to `$type` | Not present |
| `id` no longer includes discriminator by default | Not present |
| JSON `id` property mapped to key | Not present |
| Synchronous Cosmos I/O unsupported | Not present |
| SQL queries must project JSON values directly | Not present |
| Undefined results filtered from query results | Not present |
| Incorrectly translated Cosmos queries rejected | Not present |
| `HasIndex` throws for Cosmos | Not present |
| `IncludeRootDiscriminatorInJsonId` renamed | Not present |
| Newtonsoft.Json dependency updated | Not present |

## EF Core 10 relational/general changes

| Official heading | Applicability and disposition | Named passing proof |
|---|---|---|
| EF tools require framework for multi-targeted projects | Not applicable: Persistence and Portal each declare one `TargetFramework` equal to `net10.0`. | Repository preflight; pending-model gate without `--framework` |
| Application Name injected into connection string | Applicable and verified. Explicit valid values are retained; injection cannot bypass the encrypted SQL Server guard; failures remain fixed and secret-free. | `Ef10ApplicationNameInjectionCannotBypassTheGuard`; `ApplicationNameFailuresKeepDiagnosticsFixedAndSecretFree` |
| SQL Server `json` used by default with Azure SQL/compatibility 170 | Not adopted. TaxAI retains `UseSqlServer`, adds no compatibility level, `UseAzureSql`, or `json` remap; model/schema/DDL remain exact. | `SqlServerConfigurationKeepsTheExistingProviderAndSchemaDisposition`; EF baseline comparator |
| Parameterized collections use multiple parameters | Applicable and verified for all three inventoried collection queries with unchanged ordered and firm-scoped results. | `InventoriedCollectionQueriesUseEf10MultipleScalarParameters` |
| ExecuteUpdateAsync accepts non-expression lambda | Not present: TaxAI does not use ExecuteUpdate. | Source scan |
| Complex type column names uniquified | Not present: no EF complex types; relational schema is identical. | Model/schema comparator |
| Nested complex properties use full column path | Not present: no EF complex types; relational schema is identical. | Model/schema comparator |
| IDiscriminatorPropertySetConvention signature changed | Not present: no custom convention. | Source scan |
| IRelationalCommandDiagnosticsLogger adds logCommandText | Not present: no custom relational logger. | Source scan |
| SQL parameter names simplified | Applicable and accepted. Query SQL shape and results are explicitly verified; no persisted schema contract depends on parameter names. | `InventoriedCollectionQueriesUseEf10MultipleScalarParameters` |

## Microsoft.Data.Sqlite 10 changes

| Official heading | Applicability and disposition | Named passing proof |
|---|---|---|
| GetDateTimeOffset without offset assumes UTC | Applicable and verified against explicit offsetless fixtures. | `Sqlite10PreservesOffsetsUtcInstantsAndDeclaredAffinities` |
| Writing DateTimeOffset to REAL writes UTC | Applicable and verified for non-zero offsets and stored UTC instants. | `Sqlite10PreservesOffsetsUtcInstantsAndDeclaredAffinities` |
| GetDateTime with offset returns UTC | Applicable and verified for explicit offsets and `DateTimeKind.Utc`. | `Sqlite10PreservesOffsetsUtcInstantsAndDeclaredAffinities` |

The `Microsoft.Data.Sqlite.Pre10TimeZoneHandling` escape hatch is absent and
forbidden by `Pre10SqliteTimeHandlingSwitchIsNotEnabledOrConfigured`.

## Result

No migration, model snapshot, schema, DDL, production mapping, or stored-data
contract changed. No suppression, empty migration, provider switch, JSON
remap, compatibility-level override, or pre-10 SQLite switch was introduced.
