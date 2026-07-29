# Spec 013 EF Core 10 equivalence verification

## Result

PASS. The .NET 10/EF Core 10 implementation introduces no migration, model,
relational schema, SQL Server DDL, production mapping, or stored-data drift.
No database was contacted or modified.

## Canonical artifact comparison

`EfUpgradeBaselineTests` constructs the current SQL Server design-time model in
memory and compares it to the frozen .NET 8 evidence. The comparator applies
only these symmetric deterministic normalizations:

1. `System.Private.CoreLib` assembly `Version` metadata;
2. standalone `GO` batch-separator lines;
3. `__EFMigrationsHistory.ProductVersion`.

It does not broadly strip whitespace or SQL tokens. The normalized current and
baseline artifacts are byte-equal. The canonical equality artifacts therefore
represent both sides of the comparison:

| Artifact | Entries/meaning | SHA-256 |
|---|---|---|
| `evidence/ef-model.net10.json` | 109 model entries | `29e2ccebd4a2701767c78032576496f8af8c7c54c4f28814ed2646fe5b1908ee` |
| `evidence/relational-schema.net10.json` | 114 relational entries | `a071807f3384feac87b267b2abf60db42ebf066bd7b0f6db39dcab0186db2f1b` |
| `evidence/initial-migration.net10.sql` | normalized zero-to-initial SQL Server DDL | `bbbb9d241e37c89794e8c53342460c895d538d28f4c7d1ee5af474e16ea58505` |

No baseline was regenerated.

## Immutable migration set

The migration directory contains exactly the original three C# files. Their
SHA-256 values still match the approved rollback baseline:

| File | SHA-256 |
|---|---|
| `20260729021907_InitialPersistence.cs` | `2997abcf27357abe8e36818d07c4435319a1f4b758480463dd7b1ba3b5c500a1` |
| `20260729021907_InitialPersistence.Designer.cs` | `0c7a963af4de6b3224ef05e2d2b322c08e0c67727a4781a2dec3d098e087a81c` |
| `TaxAiDbContextModelSnapshot.cs` | `2452d274c7debec0955ba59d00e892069f5fdb223d06f874e68494df6e4df2b4` |

`git diff 13097dada6b1aae070fc0006d48a6862ce9904f8..HEAD` reports no
change under the migration directory, entity configurations, context, or
production mapping paths.

## Pending model

Local `dotnet-ef` resolves exactly `10.0.10`. With Persistence as the project,
Portal as startup, and a process-only synthetic encrypted SQL Server connection
string, the command exited zero:

> No changes have been made to the model since the last migration.

The complete non-secret command record is in `evidence/pending-model.txt`.
No `PendingModelChangesWarning` suppression exists.

## EF Core 10 behavior

`EfCore10CompatibilityTests` proves:

- all three inventoried parameterized-collection queries retain ordered,
  firm-scoped results and use the approved EF10 SQL disposition;
- `Application Name` insertion cannot bypass the encrypted SQL Server
  connection guard and error text remains fixed/secret-free;
- the provider remains `UseSqlServer`; no `UseAzureSql`, compatibility-level
  override, or `json` remap exists;
- SQLite offset, offsetless, REAL-affinity, UTC-instant, session, and exclusive
  retention boundaries retain approved behavior;
- the pre-10 SQLite time-handling switch is absent.

No empty or compensating migration was created, and no executable SQL
difference was hidden.
