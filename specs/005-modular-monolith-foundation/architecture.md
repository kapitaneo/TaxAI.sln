# Architecture: Modular Monolith Foundation (spec 005)

**Feature class: `platform`.** This is a structural/tooling/workflow migration.
It changes no tax fact, box, line, rate, threshold, mapping, or calculation.
See **Tax behaviour unchanged** at the end — that section is the contract the
whole plan is built to satisfy.

Throughout this document, the old solution filename is written as
`TaxAI.sln.sln` only where the historical name is unavoidable (the problem being
fixed). Acceptance criterion AC‑3 ("zero matches of the literal") is scoped to
**operational** files (build/test commands, hooks, agent/command instructions);
it does **not** forbid the historical name inside `specs/**` documents, which
necessarily record the name they are removing. See `[ASSUMED-AC3-SCOPE]` below.

---

## Current structure

Grounded in the tree at `D:\Projects\TaxAI` as inspected for this plan.

```
TaxAI.sln.sln                         <- mis-named solution (literal double .sln)
TaxLogicEngine/                       <- production project at repo root
  TaxLogicEngine.csproj               ( net8.0, Nullable, ImplicitUsings )
  Models/        (ParseResult, ValidationError, RecipientIdentifier, T4*/T5* types)
  Services/      (MoneyRounding, T4/T5 parsers, mappers, box-amount helpers, providers)
  Mappings/      (README.md, T4LineMap.2025.json, T5LineMap.2025.json)
TaxLogicEngine.Tests/                 <- test project at repo root
  TaxLogicEngine.Tests.csproj         ( ProjectReference: ..\TaxLogicEngine\TaxLogicEngine.csproj )
  Models/ Services/ Mappings/ UnitTest1.cs   (30 test files)
CLAUDE.md                             <- single combined file (repo policy + tax rules)
NEXT.md
README.md                             <- overclaims (PDF, GPT, validation engine)
.claude/  (settings.json, commands/, agents/)
specs/    (001..005)
.gitignore  .gitattributes
(no AGENTS.md anywhere; no .github/ CI)
```

**Baseline facts (captured, unchanging targets):**

- Solution format: `Format Version 12.00`, VS 17; two `Project(...)` entries.
- Assembly name: **`TaxLogicEngine`** (no `<AssemblyName>`; defaults to the
  `.csproj` filename, which does not change).
- Namespaces: exactly two — **`TaxLogicEngine.Models`**, **`TaxLogicEngine.Services`**.
- Public types: **~34 (non‑authoritative, explanatory only)** — a source‑grep
  estimate across 30 files. The **authoritative** AC‑12 reference is the exact
  sorted full‑name list of all public types obtained by **reflection over the
  built `TaxLogicEngine` assembly**, captured in Wave 0's baseline record; T08
  re‑runs the identical reflection procedure and compares the two sorted sets.
  A source‑code grep or approximate count is **not** the authoritative baseline.
- Embedded resources (two), each with an **explicit `<LogicalName>`**:
  - `Mappings\T5LineMap.2025.json` → `TaxLogicEngine.Mappings.T5LineMap.2025.json`
  - `Mappings\T4LineMap.2025.json` → `TaxLogicEngine.Mappings.T4LineMap.2025.json`
- **Baseline test inventory: 261 tests, 261 passed, 0 failed, 0 skipped**
  (`dotnet test TaxAI.sln.sln`, net8.0). This exact count is the pass/fail gate
  for every stage.
- Six tracked references to `TaxAI.sln.sln` across five files:
  `CLAUDE.md` (×2), `.claude/settings.json`, `.claude/agents/reviewer.md`,
  `.claude/commands/implement.md`, `NEXT.md`.

## Target structure

Minimal — exactly what the PRD's desired outcome asks for and nothing more.

```
TaxAI.sln                             <- normalized solution filename
src/
  TaxLogicEngine/                     <- moved as a unit (Models/, Services/, Mappings/, .csproj)
    CLAUDE.md                         <- NEW: TaxLogicEngine project-level instructions
    AGENTS.md                         <- NEW (required): minimal local bridge -> root AGENTS.md/CLAUDE.md + local CLAUDE.md
tests/
  TaxLogicEngine.Tests/               <- moved as a unit (30 test files, .csproj)
specs/                                <- unchanged location (incl. this spec)
.claude/                             <- unchanged location; settings/agents/commands edited only for the rename
CLAUDE.md                             <- rewritten to repository-level scope (T05); current-state marked complete after final verification (T08)
AGENTS.md                             <- NEW: root Codex bridge
NEXT.md                               <- updated only after final verification (T08): spec 005 complete, spec 006 next
README.md                             <- rewritten to describe only what exists (T07)
```

**No future business-module projects** (Clients, Firms, Documents, Intake,
Review, TaxCases) are created. The `src/` root exists to hold the one project
that exists today and to give future modules a conventional home; it is not
pre-populated.

## Structured modular monolith decision

TaxAI is, by decision, a **structured modular monolith**: one repository, one
.NET solution, one initially-deployable application, in‑process module
communication, with explicit module ownership and dependency boundaries, and
module extraction deferred until demonstrated need. This spec lays only the
*foundation* for that model:

- a conventional `src/`/`tests/` split so modules and a future host have an
  obvious, uncontested home;
- **written** boundary and dependency rules (in repository‑level `CLAUDE.md`)
  that bind the first business module before it can violate them;
- scope‑separated agent instructions and a Codex bridge (a **required** root
  `AGENTS.md` plus a **required** minimal `src/TaxLogicEngine/AGENTS.md` pointer)
  so both human and agent contributors inherit those rules.

### Why no empty future modules are created

Creating `Clients/`, `Firms/`, `Documents/`, `Intake/`, `Review/`, or
`TaxCases/` projects now would:

1. **Add unbuilt surface area** — empty projects with no contracts invite
   premature coupling and speculative references that later have to be undone.
2. **Contradict "smallest useful migration"** — the PRD explicitly scopes this
   spec to moving/renaming what exists, not scaffolding what doesn't.
3. **Freeze boundaries prematurely** — a real module's boundary is discovered
   from its contract; an empty shell hard‑codes a guess.

The future‑module *pattern* is **documented** in repository‑level `CLAUDE.md`
(how a module declares contracts, what it may/may not depend on); it is not
*scaffolded*. Documentation is reversible prose; an empty project is tracked
structure that acquires gravity.

## Module boundaries (TaxLogicEngine)

TaxLogicEngine's boundary, as it will be written into repository‑level and
project‑level instructions:

- **What it owns:** deterministic, provider‑independent Canadian tax logic —
  slip parsing (T4, T5), box→CRA‑line mapping via JSON config, currency
  rounding, SIN masking, per‑tax‑year isolation.
- **What it must never contain:** UI, OCR, LLM/GPT, persistence/EF/SQL,
  HTTP/ASP.NET Core, cloud/email SDKs, or any I/O that makes a tax result
  non‑deterministic.
- **Inputs are untrusted:** OCR and LLM output are input to be validated, never
  a source of tax truth. Tax facts come from canada.ca only.

## Dependency direction

The arrows that must not reverse (documented now, enforced as modules arrive):

```
UI / API host  ─────────►  Application/module contracts  ─────────►  TaxLogicEngine (domain)
Infrastructure (EF, OCR, LLM, email, cloud) ─────────►  Application/domain contracts
```

- Dependencies point **inward** toward domain/application contracts.
- **TaxLogicEngine depends on nothing outward** — not ASP.NET Core, EF Core,
  SQL, cloud SDKs, OCR providers, LLM providers, email providers, or frontend
  frameworks.
- No module reaches into another module's internal implementation; no module
  reads another module's database tables; modules communicate only through
  declared contracts.
- UI must not compute tax or implement CRA mappings.

For this spec these are **documented invariants**, not compiled constraints —
there is only one project, so there is no cross‑module reference to enforce yet.

## Migration sequence

Seven ordered stages; **each stage is independently green** (`dotnet build` +
`dotnet test` = 261 passing) before the next begins. One stage = one task =
one wave = one branch = one PR (see `tasks.md`).

0. **(Wave 0) Contract & baseline** — record old/new paths, the 261‑test
   inventory, assembly name, the exact reflection‑based public‑type set, logical
   resource names, allowed sequence, and verification commands. No production
   change.
1. Normalize `TaxAI.sln.sln` → `TaxAI.sln`; update all six references in the
   five operational files.
2. `git mv TaxLogicEngine/` → `src/TaxLogicEngine/`; update the solution's
   production path and the test project's `ProjectReference` (interim); verify
   embedded‑resource `Include` still resolves.
3. `git mv TaxLogicEngine.Tests/` → `tests/TaxLogicEngine.Tests/`; update the
   solution's test path and the moved test project's `ProjectReference` to the
   final relative path.
4. Split `CLAUDE.md` into repository‑level (root) + project‑level
   (`src/TaxLogicEngine/CLAUDE.md`).
5. Add root `AGENTS.md` **and** the required minimal `src/TaxLogicEngine/AGENTS.md`.
6. Rewrite `README.md` only (no state change).
7. **(Final wave) End‑to‑end verification** of the whole repository; **only on a
   full AC pass**, mark spec 005 complete in `NEXT.md` (spec 006 next) and in the
   root `CLAUDE.md` current state.

## Path and project-reference changes

Every path/reference that moves, old → new:

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

**Interim reference (Stage 2 only).** When the production project has moved to
`src/` but the test project is still at the repo root, the test
`ProjectReference` must temporarily become `..\src\TaxLogicEngine\TaxLogicEngine.csproj`
so Stage 2 stays green. Stage 3 moves the test project and rewrites the same
reference to its final `..\..\src\TaxLogicEngine\TaxLogicEngine.csproj`. The
solution GUIDs and `ProjectConfigurationPlatforms` blocks are **unchanged** —
only the two relative project paths in the `Project(...)` lines change.

## Embedded-resource preservation

- The two `<EmbeddedResource Include="Mappings\...json">` items are
  **project‑relative**. Because the entire `TaxLogicEngine/` directory moves as
  a unit, `Mappings/` stays beside the `.csproj`, so the `Include` paths are
  expected to resolve unchanged.
- **Verify first, modify only if necessary.** The move task must confirm the
  build succeeds and both manifest resource names are still present *before*
  concluding no `Include` edit is needed. An `Include` path is changed **only
  if** the build actually fails to find the resource after the move.
- **`<LogicalName>` values are never touched** under any circumstance. The two
  logical names — `TaxLogicEngine.Mappings.T5LineMap.2025.json` and
  `TaxLogicEngine.Mappings.T4LineMap.2025.json` — are hard‑coded in provider
  code and in two tests; they are load‑bearing strings and remain byte‑for‑byte
  identical.

## Assembly and namespace preservation

- **Assembly name** stays `TaxLogicEngine`. There is no `<AssemblyName>` in the
  `.csproj`; the default is the project filename, which does not change when the
  directory moves. No `<AssemblyName>` or `<RootNamespace>` is added.
- **Namespaces** (`TaxLogicEngine.Models`, `TaxLogicEngine.Services`) live in
  `.cs` files whose contents are not edited by any move. Directory location does
  not affect namespace.
- **Public API** (~34 public types) is unchanged: no type is added, removed, or
  renamed. AC‑12 compares the post‑migration public‑type full‑name set against
  the Wave 0 baseline record and requires them identical.

## Instruction-file lookup hierarchy

After the split, the intended read order (documented in root `AGENTS.md`) is:

1. **Root `CLAUDE.md`** — repository‑level: product framing; the modular
   monolith decision; global dependency/boundary rules; global security/PII
   rules; the spec → plan → implement → reviewer workflow; current state;
   build/test commands (`dotnet build TaxAI.sln`, `dotnet test TaxAI.sln`).
2. **`NEXT.md`** — current focus / what is next.
3. **Nearest project‑level `CLAUDE.md`** — for TaxLogicEngine work,
   `src/TaxLogicEngine/CLAUDE.md`: T4/T5 types; parsing and CRA‑mapping
   responsibilities; tax‑year isolation; decimal/half‑up rounding; SIN masking;
   JSON‑driven mapping; forbidden dependencies; completed tax specs.
4. **The requested spec's** `prd.md`, `architecture.md`, `tasks.md`.

**No rule is duplicated verbatim in full** across the two `CLAUDE.md` files:
repository‑wide rules live in the root file; the project file holds
TaxLogicEngine specifics and *references* (does not clone) the shared rules
(AC‑23). Root `AGENTS.md` restates the binding workflow rules for Codex‑family
agents (explicit TaskID only; never auto‑select the next task; `tasks.md` is
read‑only; prerequisite `.done` markers required; only task‑listed files may
change; one task per invocation; complete applicable tests before completion;
never merge or approve own work).

**Two Codex bridges, both required (no duplicated rules).** Root `AGENTS.md`
carries the read order and workflow rules above. The **required** local bridge
`src/TaxLogicEngine/AGENTS.md` is a **minimal pointer only** — it directs the
reader to the root `AGENTS.md`, the root `CLAUDE.md`, and the local
`src/TaxLogicEngine/CLAUDE.md`, and contains **no** duplicated architecture,
workflow, or tax rules. Its sole job is instruction discovery for an agent that
enters at the project directory.

## Build and test command changes

| Where | Old | New |
|---|---|---|
| Root `CLAUDE.md` build | `dotnet build TaxAI.sln.sln` | `dotnet build TaxAI.sln` |
| Root `CLAUDE.md` test | `dotnet test TaxAI.sln.sln` | `dotnet test TaxAI.sln` |
| `.claude/settings.json` PostToolUse hook | `dotnet test TaxAI.sln.sln --nologo -v q` | `dotnet test TaxAI.sln --nologo -v q` |
| `.claude/commands/implement.md` | `dotnet test TaxAI.sln.sln` | `dotnet test TaxAI.sln` |
| `.claude/agents/reviewer.md` | text "Do **not** assume `TaxAI.sln.sln`…" | reworded to drop the stale literal while keeping the "don't hardcode the solution name" intent |
| `README.md` | (absent / stub) | documents `dotnet build TaxAI.sln`, `dotnet test TaxAI.sln`, and the `src/`/`tests/` layout |
| Single‑test doc example | `dotnet test TaxLogicEngine.Tests` | `dotnet test tests/TaxLogicEngine.Tests` |
| `NEXT.md` | lists the rename as pending | at **T08 only** (after final verification): spec 005 marked complete, spec 006 Tax Case Core made the next approved feature, new layout reflected |

No CI exists, so there is no CI command to update; introducing CI is explicitly
out of scope.

## Trade-offs

**1. Two separate move stages vs. one combined move.**
*Chosen:* move the production project (Stage 2) and the test project (Stage 3)
in separate tasks/waves, accepting one throwaway interim `ProjectReference`
(`..\src\...`) that Stage 3 immediately rewrites.
*Rejected:* a single task that `git mv`s both directories at once.
*Why:* the PRD's approved migration order enumerates the two moves as distinct
steps, and finer stage granularity gives cleaner per‑stage rollback (the PRD's
rollback section relies on "each ordered stage is independently green"). The
cost is one short‑lived interim path, fully documented and green.

**2. `src/`/`tests/` split vs. leaving projects at the repo root.**
*Chosen:* conventional `src/`/`tests/` roots.
*Rejected:* keep both projects at the root and only rename the solution.
*Why:* the modular monolith needs an uncontested home for future modules and a
future host; deciding layout now, with one project, is cheap and reversible.
Deciding it later under deadline, with several modules, is neither. This is the
PRD's `[ASSUMED]` layout and the desired outcome.

**3. Split `CLAUDE.md` by scope vs. one combined file.**
*Chosen:* repository‑level root `CLAUDE.md` + project‑level
`src/TaxLogicEngine/CLAUDE.md`, shared rules referenced not cloned.
*Rejected:* keep a single combined file and add module docs into it.
*Why:* as business modules arrive, a single file becomes an undifferentiated
dumping ground where a module author cannot tell which rules bind them.
Scope‑separation now, with the read order codified in `AGENTS.md`, keeps
instruction scope legible. (An alternative — duplicating shared rules into both
files — is rejected because it drifts; AC‑23 forbids full verbatim duplication.)

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| A move silently breaks the embedded‑resource `Include`, so a provider can't load its JSON at runtime | Stage 2 verifies the build **and** enumerates manifest resource names before concluding no edit is needed; the mapping/provider test suites (AC‑13/14) must stay green; `<LogicalName>` is never touched. |
| A `ProjectReference` or solution path is left dangling after a move | Each move stage rebuilds+tests on `TaxAI.sln`; a dangling reference fails the build, blocking the stage. Old/new paths are enumerated in `tasks.md`. |
| Cross‑cutting breakage: the `PostToolUse` hook still names the old solution and starts failing on every edit mid‑migration | Stage 1 updates the hook in the same task as the rename, so the hook and the solution filename change together. |
| Copy/delete instead of `git mv` loses history and defeats reversibility | Constraint: use `git mv` (rename‑preserving); reviewer verifies the diff reads as renames, not add/delete pairs. |
| Build output or machine‑local files get tracked by the move (`bin/`, `obj/`, `.vs/`, `settings.local.json`) | `.gitignore` already excludes them; AC‑30 + reviewer C6 check the commit set; move only tracked files. |
| A tax test is deleted, skipped, or weakened to "make the move pass" | Hard constraint: the same 261 tests pass before and after; AC‑15/16 require no test removed, skipped, or newly failing; the count is pinned in the Wave 0 baseline. |
| AC‑3 read too literally flags historical mentions in `specs/**` | `[ASSUMED-AC3-SCOPE]`: AC‑3 applies to operational files, not spec history; recorded below and in `tasks.md`. |
| A public type is accidentally renamed/removed while editing project files | AC‑12 diffs the post‑migration public‑type set against the Wave 0 baseline; moves don't edit `.cs` bodies. |

## Implementation branch & merge model

The migration advances through `master` **one independently‑green stage at a
time** — not on a single long‑running migration branch.

- The current `plan/005-modular-monolith-foundation` branch contains **only**
  this approved architecture and task plan (no production change).
- Each implementation task **T01–T08 runs on its own task branch created from
  the latest `master`** (e.g. `git worktree add ../TaxAI-t02 -b feat/005-t02 master`).
- Each task produces **one PR**. A **human reviews and merges** that PR before
  the next wave begins; **agents never merge or approve their own work.**
- `master` therefore advances one independently‑green migration stage at a time;
  the next task branches from the newly‑advanced `master`.
- The PRD's feature‑branch safety requirement applies **separately to each task
  branch**, not to one long‑running migration branch — `master` only ever
  receives a stage that already built and tested green.

## Rollback / recovery approach

- Because every stage is merged as its own commit, **rollback of one stage is a
  revert of that stage's merge commit** on `master`; earlier green stages are
  untouched.
- Because every move is a git‑aware rename, each stage is fully reversible — no
  content is lost in a copy/delete gap.
- Each stage is independently green, so reverting the most recent stage's merge
  returns `master` to the prior green stage.
- Recovery check after any revert: `dotnet build` + `dotnet test` return to
  **261 passing** with the same test set — the AC‑15 condition guards both
  directions.

## Tax behaviour unchanged

This migration changes **no** tax mapping, calculation, parser behaviour, or
public tax contract. Specifically, all of the following are byte‑for‑byte
equivalent in effect before and after:

- T4 and T5 slip parsing (including the recorded Box 54 / Box 10 decisions);
- T4 and T5 box→CRA‑line mapping (2025 config; deferred `UnmappedBoxCodes`
  unchanged);
- every public type, member, and signature in `TaxLogicEngine`;
- assembly name `TaxLogicEngine` and both namespaces;
- the two embedded‑resource logical names;
- `MoneyRounding.ToCents` half‑up (`MidpointRounding.AwayFromZero`) behaviour;
- `RecipientIdentifier` SIN masking (last 3 digits) in every output;
- tax‑year isolation / fail‑closed behaviour (no adjacent‑year fallback).

The **full existing test suite (261 tests) stays green** at every stage. Any
change to tax behaviour discovered during this migration is a **defect against
this spec**, not a delivered outcome.

---

### Recorded Class B decisions

- `[ASSUMED-AC3-SCOPE]` — AC‑3's "zero matches of the literal `TaxAI.sln.sln`"
  is scoped to operational files (build/test commands, the hook, agent/command
  instructions, README, NEXT.md). Historical occurrences inside `specs/**`
  (this PRD, architecture, tasks) are documentation of the name being removed
  and are **not** violations.
- `[ASSUMED-LOCAL-BRIDGE]` — the local Codex bridge is **not** an
  implementation‑time option. Both `AGENTS.md` files are **required**: root
  `AGENTS.md` (read order + workflow rules) and `src/TaxLogicEngine/AGENTS.md`
  (a minimal pointer to root `AGENTS.md`, root `CLAUDE.md`, and the local
  `CLAUDE.md`, with **no** duplicated architecture, workflow, or tax rules).
- `[ASSUMED-STATE-AT-T08]` — project‑state updates (`NEXT.md` marking spec 005
  complete and spec 006 next; root `CLAUDE.md` current‑state marking spec 005
  complete) happen in **T08 only, after all AC‑1…AC‑30 pass**. T07 rewrites
  `README.md` only and makes no completion claim. If any AC fails, `NEXT.md` and
  `CLAUDE.md` must not claim spec 005 is complete and no `.done` marker is
  created.
- `[ASSUMED]` layout, instruction paths, shared‑rule placement, ordering, and
  "no build scaffolding" follow the PRD's Open Questions verbatim.
