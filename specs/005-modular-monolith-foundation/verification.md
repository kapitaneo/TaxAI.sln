# Verification: Modular Monolith Foundation (spec 005, T08)

Final end-to-end verification of the migrated repository against the Wave 0
baseline (`baseline.md`), run on branch `feat/005-t08` (from `master` at
`8b404c3`, i.e. after T01–T07 merged) on 2026-07-12.

**Result: all 30 acceptance criteria PASS.** The state updates to `NEXT.md`
and `CLAUDE.md` (marking spec 005 complete, spec 006 next) are therefore
applied in this same task, per `[ASSUMED-STATE-AT-T08]`.

Historical occurrences of the old solution filename in this document are
documentation per `[ASSUMED-AC3-SCOPE]`.

## Build and test (AC-4, AC-5, AC-15, AC-16)

| AC | Check | Command | Result |
|---|---|---|---|
| AC-4 | Build exits 0 | `dotnet build TaxAI.sln --nologo` | **PASS** — exit 0, 0 errors |
| AC-5 | Full suite runs | `dotnet test TaxAI.sln --nologo --no-build` | **PASS** — 261 total discovered and run |
| AC-15 | Same set as baseline, none removed/skipped/failing | same command vs `baseline.md` §2 | **PASS** — **261 passed, 0 failed, 0 skipped** — identical to the Wave 0 count |
| AC-16 | Parser/mapper/boundary/PII/year-isolation suites pass | included in the 261 (all 30 test files present under `tests/TaxLogicEngine.Tests/`) | **PASS** |

## Tree listing (AC-1, AC-2, AC-6, AC-7, AC-8, AC-10, AC-19)

Commands: `Test-Path` per file, `git ls-files "*.csproj"`, `git ls-files`
grep for future-module names.

| AC | Check | Result |
|---|---|---|
| AC-1 | `TaxAI.sln` exists at repo root | **PASS** |
| AC-2 | `TaxAI.sln.sln` does not exist | **PASS** |
| AC-6 | `src/TaxLogicEngine/TaxLogicEngine.csproj` exists | **PASS** |
| AC-7 | `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` exists | **PASS** |
| AC-8 | no root `TaxLogicEngine/` or `TaxLogicEngine.Tests/` | **PASS** |
| AC-10 | exactly two tracked `.csproj` | **PASS** — the two above, nothing else |
| AC-19 | no future-module project (Clients/Firms/Documents/Intake/Review/TaxCases) | **PASS** — grep over `git ls-files` returns nothing |

## Literal grep (AC-3)

Command: `git grep -n --fixed-strings "TaxAI.sln.sln" -- ":!specs"` → exit 1
(zero matches). **PASS** (scoped per `[ASSUMED-AC3-SCOPE]` — `specs/**`
history excluded).

## References (AC-9)

`tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` contains
`<ProjectReference Include="..\..\src\TaxLogicEngine\TaxLogicEngine.csproj" />`
and the solution builds (AC-4), so the reference resolves. **PASS**

## Assembly identity and public API (AC-11, AC-12)

Procedure: the **identical Wave 0 reflection procedure** from `baseline.md` §5
(throwaway net8.0 console app, `Assembly.LoadFrom`, `GetExportedTypes()`
`FullName` sorted ordinal, `GetManifestResourceNames()` sorted ordinal), run
against `src/TaxLogicEngine/bin/Debug/net8.0/TaxLogicEngine.dll`.

- AC-11 — emitted `AssemblyName: TaxLogicEngine`. **PASS**
- AC-12 — the emitted exported-type list (34 types) was diffed with
  `Compare-Object` against the exact sorted list in `baseline.md` §5:
  **zero differences — the sets are identical.** **PASS**

## Embedded resources (AC-13, AC-14)

- AC-13 — the same reflection run emitted exactly
  `TaxLogicEngine.Mappings.T4LineMap.2025.json` and
  `TaxLogicEngine.Mappings.T5LineMap.2025.json`, byte-identical to
  `baseline.md` §6. **PASS**
- AC-14 — `dotnet test TaxAI.sln --filter "FullyQualifiedName~LineMap"`
  (embedded providers + line-map + mapping suites): **104 passed, 0 failed,
  0 skipped**; also included in the full 261. **PASS**

## Diff review — pre-existing files path/reference-only (AC-17)

Command: `git diff -M --name-status 6a1f5a8 HEAD -- src tests TaxLogicEngine
TaxLogicEngine.Tests "TaxAI.sln*"` (6a1f5a8 = pre-migration `master` at the
T01 branch point), plus content diff of every non-R100 entry.

Every pre-existing production, test, `.csproj`, mapping, and resource file is
an **R100 rename** except exactly two files, whose entire content change is:

- `TaxAI.sln` (R087): the two `Project(...)` relative paths →
  `src\TaxLogicEngine\...` and `tests\TaxLogicEngine.Tests\...` (GUIDs and
  configuration blocks untouched);
- `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` (R089): the single
  `ProjectReference` line → `..\..\src\TaxLogicEngine\TaxLogicEngine.csproj`.

The production `.csproj` is R100 (content untouched — `<LogicalName>` values
never edited). No parser, mapper, model, JSON mapping, rounding, PII/SIN, or
tax-year content changed. New instruction files (`src/TaxLogicEngine/CLAUDE.md`,
`AGENTS.md`, `src/TaxLogicEngine/AGENTS.md`) are expected additions per AC-17.
**PASS**

## Instruction split (AC-18, AC-20, AC-21, AC-22, AC-23)

- AC-18 — every FR-4 boundary rule is present in root `CLAUDE.md` (checked by
  substring per rule): deterministic/provider-independent; contracts-only
  communication; no reaching into module internals; no reading another
  module's database tables; UI must not compute tax; OCR/LLM output untrusted;
  dependencies point inward; tax-domain code must not depend on ASP.NET Core,
  EF Core, SQL, cloud SDKs, OCR providers, LLM providers, email providers, or
  frontend frameworks. **PASS**
- AC-20 — `src/TaxLogicEngine/CLAUDE.md` exists. **PASS**
- AC-21 — root `CLAUDE.md` contains product framing, the modular-monolith
  decision, global dependency rules, global security/PII rules, the
  spec/plan/implement/reviewer workflow, current state, and the
  `dotnet build TaxAI.sln` / `dotnet test TaxAI.sln` commands. **PASS**
- AC-22 — project `CLAUDE.md` contains the T4/T5 type inventory,
  parsing/mapping responsibilities, tax-year isolation (fail closed),
  decimal/half-up rounding (`MidpointRounding.AwayFromZero`), SIN masking
  (last 3 digits), JSON-driven mapping ("JSON config only"), forbidden
  dependencies, and completed tax specs 001–004. **PASS**
- AC-23 — `Compare-Object` over all substantive lines (>40 chars) of the two
  files: **zero verbatim-duplicated lines**; shared rules live in the root
  file and are referenced from the project file. **PASS**

## Codex bridge (AC-24, AC-25, AC-26)

- AC-24 — root `AGENTS.md` exists. **PASS**
- AC-25 — it names the four sources in order: root `CLAUDE.md` → `NEXT.md` →
  nearest project-level `CLAUDE.md` → the requested spec's `prd.md`,
  `architecture.md`, `tasks.md` (index-order check). **PASS**
- AC-26 — all eight FR-6 rules stated: explicit TaskID only; never
  auto-select; `tasks.md` read-only; prerequisite `.done` markers; only
  task-listed files; one task per invocation; tests before completion; never
  merge/approve own work. **PASS**
- The required minimal local bridge `src/TaxLogicEngine/AGENTS.md` exists and
  is a pointer only (root `AGENTS.md`, root `CLAUDE.md`, local `CLAUDE.md`;
  no duplicated rules). **PASS**

## README honesty (AC-27, AC-28, AC-29)

- AC-27 — `Select-String -Path README.md -Pattern 'PDF|GPT|LLM|validation
  engine|return builder|Prompts'` → zero matches. **PASS**
- AC-28 — README documents the `src/`/`tests/` structure and both
  `dotnet build TaxAI.sln` / `dotnet test TaxAI.sln` commands (plus the
  `tests/TaxLogicEngine.Tests` single-test example). **PASS**
- AC-29 — every capability listed as implemented exists in the tree
  (`git ls-files`): `T4SlipParser.cs`, `T5SlipParser.cs`, `T4LineMapper.cs`,
  `T5LineMapper.cs`, `MoneyRounding.cs`, `RecipientIdentifier.cs`, and both
  `Mappings/T?LineMap.2025.json` configs. **PASS**

## Hygiene (AC-30)

`git ls-files` grep for `bin/`, `obj/`, `.vs/`, `*.user`,
`settings.local.json` → zero matches; no secrets tracked. **PASS**

## Summary

| ACs | Verdict |
|---|---|
| AC-1 … AC-30 | **30 / 30 PASS** |

The migration is behaviour-preserving: the same 261 tests pass, the public
API and assembly identity are byte-identical to the Wave 0 baseline, and both
embedded-resource logical names are unchanged. Accordingly, this task also
updates `NEXT.md` (spec 005 complete; spec 006 Tax Case Core next) and the
root `CLAUDE.md` current state (spec 005 complete), per
`[ASSUMED-STATE-AT-T08]`.
