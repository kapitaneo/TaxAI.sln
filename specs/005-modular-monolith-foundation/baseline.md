# Baseline: Modular Monolith Foundation (spec 005, T01)

Pre-migration contract and baseline record, captured on branch `feat/005-t01`
(branched from `master` at commit `6a1f5a8`) on 2026-07-12. Later stages
(T02–T07) and the final verification (T08) diff against this record. This file
is the fixed reference; it is never edited after T01 merges.

This document necessarily contains the historical solution filename
`TaxAI.sln.sln` — per `[ASSUMED-AC3-SCOPE]`, occurrences inside `specs/**` are
documentation of the name being removed, not AC-3 violations.

---

## 1. Expected old → new paths

From `architecture.md` §Path and project-reference changes:

| Kind | Old | New |
|---|---|---|
| Solution file | `TaxAI.sln.sln` | `TaxAI.sln` |
| Production project dir | `TaxLogicEngine/` | `src/TaxLogicEngine/` |
| Production `.csproj` | `TaxLogicEngine/TaxLogicEngine.csproj` | `src/TaxLogicEngine/TaxLogicEngine.csproj` |
| Test project dir | `TaxLogicEngine.Tests/` | `tests/TaxLogicEngine.Tests/` |
| Test `.csproj` | `TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` | `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` |
| Solution → production entry | `TaxLogicEngine\TaxLogicEngine.csproj` | `src\TaxLogicEngine\TaxLogicEngine.csproj` |
| Solution → test entry | `TaxLogicEngine.Tests\TaxLogicEngine.Tests.csproj` | `tests\TaxLogicEngine.Tests\TaxLogicEngine.Tests.csproj` |
| Test `ProjectReference` | `..\TaxLogicEngine\TaxLogicEngine.csproj` | `..\..\src\TaxLogicEngine\TaxLogicEngine.csproj` |

**Interim reference (Stage 2 only):** while the production project is at
`src/` but the test project is still at the repo root, the test
`ProjectReference` is temporarily `..\src\TaxLogicEngine\TaxLogicEngine.csproj`;
Stage 3 rewrites it to the final `..\..\src\TaxLogicEngine\TaxLogicEngine.csproj`.
Solution GUIDs and `ProjectConfigurationPlatforms` blocks never change — only
the two relative project paths in the `Project(...)` lines.

## 2. Baseline test inventory

Command run (2026-07-12, net8.0):

```
dotnet build TaxAI.sln.sln --nologo          # Build succeeded, 0 errors
dotnet test  TaxAI.sln.sln --nologo --no-build
```

Result — the pass/fail gate for every later stage:

```
Passed!  - Failed: 0, Passed: 261, Skipped: 0, Total: 261 - TaxLogicEngine.Tests.dll (net8.0)
```

**261 passed, 0 failed, 0 skipped.** No test may be removed, skipped,
weakened, or rewritten during the migration.

Per-file `[Fact]`/`[Theory]` attribute breakdown (30 test files; attribute
counts — `[Theory]` rows expand at run time, which is why 202 attributes
execute as 261 test cases):

| Test file | `[Fact]`+`[Theory]` |
|---|---|
| `TaxLogicEngine.Tests/UnitTest1.cs` | 1 |
| `TaxLogicEngine.Tests/Mappings/T4LineMap2025Tests.cs` | 4 |
| `TaxLogicEngine.Tests/Mappings/T5LineMap2025Tests.cs` | 4 |
| `TaxLogicEngine.Tests/Models/ParseResultTests.cs` | 7 |
| `TaxLogicEngine.Tests/Models/RecipientIdentifierTests.cs` | 8 |
| `TaxLogicEngine.Tests/Models/T4LineMapTests.cs` | 9 |
| `TaxLogicEngine.Tests/Models/T5BoxCodesTests.cs` | 3 |
| `TaxLogicEngine.Tests/Models/T5LineMapTests.cs` | 7 |
| `TaxLogicEngine.Tests/Models/T5MappingResultTests.cs` | 2 |
| `TaxLogicEngine.Tests/Models/T5OtherInformationTests.cs` | 3 |
| `TaxLogicEngine.Tests/Models/T5RawInputTests.cs` | 4 |
| `TaxLogicEngine.Tests/Models/T5SlipTests.cs` | 4 |
| `TaxLogicEngine.Tests/Models/TaxLineEntryTests.cs` | 4 |
| `TaxLogicEngine.Tests/Services/EmbeddedT4LineMapProviderTests.cs` | 6 |
| `TaxLogicEngine.Tests/Services/EmbeddedT5LineMapProviderTests.cs` | 3 |
| `TaxLogicEngine.Tests/Services/MoneyRoundingTests.cs` | 3 |
| `TaxLogicEngine.Tests/Services/T4ContractTests.cs` | 3 |
| `TaxLogicEngine.Tests/Services/T4LineMapperTests.cs` | 14 |
| `TaxLogicEngine.Tests/Services/T4LineMappingAcceptanceTests.cs` | 5 |
| `TaxLogicEngine.Tests/Services/T4MappingContractTests.cs` | 3 |
| `TaxLogicEngine.Tests/Services/T4SlipBoxAmountsTests.cs` | 7 |
| `TaxLogicEngine.Tests/Services/T4SlipParserBoundaryTests.cs` | 8 |
| `TaxLogicEngine.Tests/Services/T4SlipParserPiiTests.cs` | 7 |
| `TaxLogicEngine.Tests/Services/T4SlipParserTests.cs` | 21 |
| `TaxLogicEngine.Tests/Services/T4SlipParserYearIsolationTests.cs` | 6 |
| `TaxLogicEngine.Tests/Services/T5LineMapperTests.cs` | 14 |
| `TaxLogicEngine.Tests/Services/T5LineMappingAcceptanceTests.cs` | 10 |
| `TaxLogicEngine.Tests/Services/T5SlipAcceptanceTests.cs` | 9 |
| `TaxLogicEngine.Tests/Services/T5SlipBoxAmountsTests.cs` | 5 |
| `TaxLogicEngine.Tests/Services/T5SlipParserTests.cs` | 18 |
| **Total (30 files)** | **202 attributes → 261 executed test cases** |

Breakdown command:
`grep -rEc "\[(Fact|Theory)" TaxLogicEngine.Tests --include=*.cs`

## 3. Assembly name

**`TaxLogicEngine`.** `TaxLogicEngine/TaxLogicEngine.csproj` contains no
`<AssemblyName>` (and no `<RootNamespace>`); the assembly name defaults to the
`.csproj` filename, which never changes. Confirmed by reflection (§5 output:
`AssemblyName: TaxLogicEngine`). No `<AssemblyName>` or `<RootNamespace>` may
be added during the migration.

## 4. Namespaces

Exactly two public namespaces:

- `TaxLogicEngine.Models`
- `TaxLogicEngine.Services`

Confirmed by the reflection dump in §5 — every exported type's `FullName`
begins with one of these two prefixes.

## 5. Public-API preservation (authoritative, reflection-based — AC-12)

The authoritative AC-12 reference set is the **exact sorted full-name list of
every public (exported) type in the built `TaxLogicEngine` assembly**, captured
by reflection over the built DLL. A source-code grep or approximate count
(e.g. "~34") is explanatory prose only and **non-authoritative**.

### Exact reproducible procedure (T08 must run this identical procedure)

1. Build the solution:
   `dotnet build TaxAI.sln.sln --nologo` (after Stage 1: `dotnet build TaxAI.sln --nologo`).
2. Create a throwaway net8.0 console project **outside the repository**
   (`dotnet new console -o <temp>/PublicTypeDump`) with this exact `Program.cs`:

   ```csharp
   // Usage: dotnet run --project PublicTypeDump -- <path-to-TaxLogicEngine.dll>
   using System.Reflection;

   var asm = Assembly.LoadFrom(args[0]);

   Console.WriteLine($"AssemblyName: {asm.GetName().Name}");
   Console.WriteLine("--- ExportedTypes (FullName, sorted ordinal) ---");
   foreach (var name in asm.GetExportedTypes()
                .Select(t => t.FullName!)
                .OrderBy(n => n, StringComparer.Ordinal))
   {
       Console.WriteLine(name);
   }

   Console.WriteLine("--- ManifestResourceNames (sorted ordinal) ---");
   foreach (var res in asm.GetManifestResourceNames()
                .OrderBy(n => n, StringComparer.Ordinal))
   {
       Console.WriteLine(res);
   }
   ```

3. Run it against the built production DLL
   (baseline path: `TaxLogicEngine/bin/Debug/net8.0/TaxLogicEngine.dll`;
   post-migration path: `src/TaxLogicEngine/bin/Debug/net8.0/TaxLogicEngine.dll`):

   ```
   dotnet run --project <temp>/PublicTypeDump -- <path-to>/TaxLogicEngine.dll
   ```

4. Compare the emitted exported-type list (and manifest resource names)
   line-by-line against §5/§6 of this file. They must be **identical**.

### Exact sorted exported-type full-name list (34 types, ordinal sort)

```
TaxLogicEngine.Models.BoxLineMapping
TaxLogicEngine.Models.ParseResult`1
TaxLogicEngine.Models.RecipientIdentifier
TaxLogicEngine.Models.T4BoxCodes
TaxLogicEngine.Models.T4BoxLineMapping
TaxLogicEngine.Models.T4LineMap
TaxLogicEngine.Models.T4LineMappingConfig
TaxLogicEngine.Models.T4MappingResult
TaxLogicEngine.Models.T4OtherInformation
TaxLogicEngine.Models.T4OtherInformationCodes
TaxLogicEngine.Models.T4RawBox
TaxLogicEngine.Models.T4RawInput
TaxLogicEngine.Models.T4Slip
TaxLogicEngine.Models.T5BoxCodes
TaxLogicEngine.Models.T5LineMap
TaxLogicEngine.Models.T5LineMappingConfig
TaxLogicEngine.Models.T5MappingResult
TaxLogicEngine.Models.T5OtherInformation
TaxLogicEngine.Models.T5RawBox
TaxLogicEngine.Models.T5RawInput
TaxLogicEngine.Models.T5Slip
TaxLogicEngine.Models.TaxLineEntry
TaxLogicEngine.Models.ValidationError
TaxLogicEngine.Services.EmbeddedT4LineMapProvider
TaxLogicEngine.Services.EmbeddedT5LineMapProvider
TaxLogicEngine.Services.IT4LineMapProvider
TaxLogicEngine.Services.IT5LineMapProvider
TaxLogicEngine.Services.MoneyRounding
TaxLogicEngine.Services.T4LineMapper
TaxLogicEngine.Services.T4SlipBoxAmounts
TaxLogicEngine.Services.T4SlipParser
TaxLogicEngine.Services.T5LineMapper
TaxLogicEngine.Services.T5SlipBoxAmounts
TaxLogicEngine.Services.T5SlipParser
```

## 6. Embedded-resource logical names (never change)

Manifest resource names emitted by the reflection procedure, byte-for-byte:

```
TaxLogicEngine.Mappings.T4LineMap.2025.json
TaxLogicEngine.Mappings.T5LineMap.2025.json
```

Both are explicit `<LogicalName>` values in `TaxLogicEngine.csproj`
(`Include="Mappings\T4LineMap.2025.json"` / `Include="Mappings\T5LineMap.2025.json"`).
The `<LogicalName>` elements are **never touched**; the `Include` paths are
project-relative and edited only if a build proves a resource missing after
the Stage 2 move.

## 7. The six literal `TaxAI.sln.sln` references Stage 1 must remove (five files)

Captured by `git grep -n --fixed-strings "TaxAI.sln.sln" -- ":!specs"`:

| # | File | Line | Context |
|---|---|---|---|
| 1 | `CLAUDE.md` | 15 | `dotnet build TaxAI.sln.sln` |
| 2 | `CLAUDE.md` | 18 | `dotnet test TaxAI.sln.sln` |
| 3 | `.claude/settings.json` | 9 | PostToolUse hook: `dotnet test TaxAI.sln.sln --nologo -v q` |
| 4 | `.claude/commands/implement.md` | 61 | `dotnet test TaxAI.sln.sln` |
| 5 | `.claude/agents/reviewer.md` | 30 | "Do **not** assume `TaxAI.sln.sln`…" (reword, keep intent) |
| 6 | `NEXT.md` | 22 | "rename `TaxAI.sln.sln` to `TaxAI.sln`" (pending-work line) |

Six references, five files. After Stage 1 the same grep must return zero
matches (AC-3, scoped per `[ASSUMED-AC3-SCOPE]` — `specs/**` excluded).

## 8. Allowed migration sequence (the only permitted order)

0. **Wave 0 (T01)** — this contract & baseline record; no production change.
1. **T02** — rename `TaxAI.sln.sln` → `TaxAI.sln`; update the six references
   in the five operational files.
2. **T03** — `git mv TaxLogicEngine/` → `src/TaxLogicEngine/`; update the
   solution's production path and the test project's interim
   `ProjectReference`; verify embedded resources still resolve.
3. **T04** — `git mv TaxLogicEngine.Tests/` → `tests/TaxLogicEngine.Tests/`;
   update the solution's test path and the final test `ProjectReference`.
4. **T05** — split `CLAUDE.md` into repository-level (root) +
   project-level (`src/TaxLogicEngine/CLAUDE.md`).
5. **T06** — add root `AGENTS.md` and the required minimal
   `src/TaxLogicEngine/AGENTS.md` pointer.
6. **T07** — rewrite `README.md` only (no state change).
7. **T08** — final end-to-end verification against this baseline; only on a
   full AC-1…AC-30 pass, mark spec 005 complete in `NEXT.md` and `CLAUDE.md`.

Each stage must be independently green (build + 261 tests passing) and merged
to `master` by a human before the next begins.

## 9. Verification commands

| Check | Command |
|---|---|
| Build | `dotnet build TaxAI.sln.sln --nologo` (after Stage 1: `dotnet build TaxAI.sln --nologo`) — exit 0 |
| Tests | `dotnet test TaxAI.sln.sln --nologo` (after Stage 1: `dotnet test TaxAI.sln --nologo`) — 261 passed, 0 failed, 0 skipped |
| Tree listing | `git ls-files` — verify solution filename, `src/`/`tests/` layout, exactly two `.csproj`, no `bin/`/`obj/`/`.vs/`/`settings.local.json` tracked |
| Literal grep | `git grep -n --fixed-strings "TaxAI.sln.sln" -- ":!specs"` — zero matches after Stage 1 |
| Manifest resources | reflection procedure in §5 (emits `ManifestResourceNames`) — both §6 names present, unchanged |
| Public-type diff | reflection procedure in §5 — emitted sorted list identical to §5's 34-type list |
