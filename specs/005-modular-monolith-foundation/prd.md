# PRD 005 — Modular Monolith Foundation

## Feature classification

**Class: `platform`.**

This feature changes repository structure, the solution filename, build/test
invocation paths, module-boundary documentation, and agent-instruction layout.
It introduces and changes **no** tax fact, box, line, rate, threshold, mapping,
or calculation.

**Tax/CRA semantic impact: none**

If any part of this migration is found to change tax behaviour, that change is a
defect against this PRD, not a delivered outcome (see Out of scope).

---

## Problem

TaxAI is confirmed to start life as a **structured modular monolith**: one
repository, one .NET solution, one initially-deployable application, in-process
module communication, explicit module ownership and dependency boundaries,
with modules extracted later only on demonstrated need. Before any business
module is built, the repository itself must be put on a sound, honest, and
convention-following foundation. Today it is not:

1. **The solution file is mis-named `TaxAI.sln.sln`** (a literal double
   `.sln`). Five tracked files hardcode that name, including the build/test
   commands agents run and a `PostToolUse` hook. Every new contributor and
   every agent inherits the typo.
2. **Both projects sit at the repository root** (`TaxLogicEngine/` and
   `TaxLogicEngine.Tests/`) with no `src/` or `tests/` separation, so there is
   no structural place to hang future modules or a future host without
   re-litigating layout under pressure.
3. **There is no declared module boundary.** Nothing in the repository records
   the rules that keep TaxLogicEngine deterministic and provider-independent,
   or that forbid UI/OCR/LLM code from computing tax. Those rules must exist in
   writing before the first module that could violate them is added.
4. **Agent instructions are a single root `CLAUDE.md`** mixing repository-wide
   policy with TaxLogicEngine-specific tax rules, and there is **no
   `AGENTS.md`** for Codex-family agents. Instruction scope is undifferentiated.
5. **The README overclaims.** It advertises a "Draft tax return builder (to
   JSON or PDF)", a "Rule-based validation engine (CRA limits, deductions)",
   and "GPT assistant integration" — none of which exist in the source tree.
   A README that describes software that isn't there is a correctness hazard
   for anyone (human or agent) reasoning about what is safe to rely on.

**Tax-year scope: `Not applicable`.** This feature changes no tax semantics, so
no tax year is in scope. No slip, box, line, rate, or threshold is added,
removed, or altered.

---

## Current repository state and reuse analysis

All statements below are grounded in the actual tree at
`D:\Projects\TaxAI` as inspected for this PRD, not from memory.

### Solution and projects (exist today; reused as-is, only relocated/renamed)

- **`TaxAI.sln.sln`** at repo root. Format 12.00, VS 17. References exactly two
  projects by relative path:
  - `TaxLogicEngine\TaxLogicEngine.csproj`
  - `TaxLogicEngine.Tests\TaxLogicEngine.Tests.csproj`
- **`TaxLogicEngine/TaxLogicEngine.csproj`** — `net8.0`, `Nullable=enable`,
  `ImplicitUsings=enable`. Declares two `EmbeddedResource` items, each with an
  **explicit `<LogicalName>`**:
  - `Mappings\T5LineMap.2025.json` → `TaxLogicEngine.Mappings.T5LineMap.2025.json`
  - `Mappings\T4LineMap.2025.json` → `TaxLogicEngine.Mappings.T4LineMap.2025.json`
- **`TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj`** — `net8.0`, xunit
  2.5.3, `xunit.runner.visualstudio` 2.5.3, `coverlet.collector` 6.0.0,
  `Microsoft.NET.Test.Sdk` 17.8.0. `ProjectReference` to
  `..\TaxLogicEngine\TaxLogicEngine.csproj`.

### Production folders that exist (reused as-is)

- `TaxLogicEngine/Models/` — `ParseResult`, `ValidationError`,
  `RecipientIdentifier`, T5 and T4 box codes / other-information / slip /
  raw-input / line-map / line-mapping-config / mapping-result / tax-line-entry
  types.
- `TaxLogicEngine/Services/` — `MoneyRounding`, T5 and T4 slip parsers, line
  mappers, box-amount helpers, `IT5LineMapProvider`/`EmbeddedT5LineMapProvider`,
  `IT4LineMapProvider`/`EmbeddedT4LineMapProvider`.
- `TaxLogicEngine/Mappings/` — `README.md`, `T4LineMap.2025.json`,
  `T5LineMap.2025.json`.

**Folders the README claims but that do NOT exist:** `Prompts/`, a `Calculator`
service, a `Validator` service, any PDF writer, any GPT/LLM integration. The
`TaxLogicEngine.Tests` project is a separate top-level project, not a `Tests/`
folder inside `TaxLogicEngine/` as the README diagram shows.

### Test project contents (reused as-is)

`TaxLogicEngine.Tests/` contains the T5 and T4 parser, mapper, provider,
boundary, PII, year-isolation, and embedded-resource test suites, plus
`UnitTest1.cs`. Two tests hardcode the embedded logical-name strings
(`TaxLogicEngine.Mappings.T4LineMap.2025.json`,
`TaxLogicEngine.Mappings.T5LineMap.2025.json`).

### Tracked files that reference `TaxAI.sln.sln` (must all be updated)

| File | Line | Context |
|---|---|---|
| `CLAUDE.md` | 15 | `dotnet build TaxAI.sln.sln` |
| `CLAUDE.md` | 18 | `dotnet test TaxAI.sln.sln` |
| `.claude/settings.json` | 9 | `PostToolUse` hook: `dotnet test TaxAI.sln.sln --nologo -v q` |
| `.claude/agents/reviewer.md` | 30 | "Do **not** assume `TaxAI.sln.sln`…" |
| `.claude/commands/implement.md` | 61 | `dotnet test TaxAI.sln.sln` |
| `NEXT.md` | 22 | "rename `TaxAI.sln.sln` to `TaxAI.sln`" |

### Instruction / doc files that exist

- Root `CLAUDE.md` (single combined file — repository policy + tax rules).
- `.claude/` — `settings.json`, `settings.local.json` (git-ignored),
  `commands/{spec,plan,implement}.md`, `agents/{business-analyst,reviewer}.md`.
- `NEXT.md`, `README.md` (32-line stub), `TaxLogicEngine/Mappings/README.md`.
- **No `AGENTS.md` anywhere.** **No `.github/` CI workflow.**
- `.gitignore` already excludes `[Bb]in/`, `[Oo]bj/`, `.vs/`, and
  `.claude/settings.local.json`.

### Reuse summary

- **Reused unchanged (moved only):** both `.csproj` files and every source and
  test file, all `Models/`, `Services/`, `Mappings/` contents, both embedded
  JSON configs, `.gitignore`, `.claude/` command and agent files.
- **Genuinely new artifacts this spec creates:**
  1. `TaxAI.sln` (renamed from `TaxAI.sln.sln`).
  2. `src/` and `tests/` directory structure `[ASSUMED]`.
  3. A project-level `CLAUDE.md` beside the TaxLogicEngine project.
  4. A root `AGENTS.md` Codex bridge (and an optional minimal local bridge
     beside TaxLogicEngine) `[ASSUMED]`.
  5. A rewritten, honest `README.md`.
  6. A repository-level rewrite of the root `CLAUDE.md` (scope-split, not new
     rules).

Nothing in the existing type inventory (`ParseResult<T>`, `MoneyRounding`,
`RecipientIdentifier`, the T4/T5 parsers and mappers, etc.) is re-specified or
re-implemented. This spec relocates them; it does not redefine them.

---

## Desired outcome

A repository that is structurally ready to grow business modules as an
in-process modular monolith, with:

- one correctly-named solution, `TaxAI.sln`, at the repository root;
- production and test projects under conventional `src/` and `tests/` roots
  `[ASSUMED]`, with all references intact and the full existing suite green;
- written, discoverable module-boundary and dependency rules;
- scope-separated agent instructions (repository-level vs TaxLogicEngine
  project-level) plus a Codex `AGENTS.md` bridge;
- a README that describes only software that actually exists.

All of this with **zero** change to tax behaviour, public tax contracts,
namespaces, assembly identity, embedded-resource logical names, monetary
rounding, SIN masking, or tax-year failure behaviour.

---

## Preserved behaviour (must remain byte-for-byte equivalent in effect)

The following are invariants. Any deviation is a defect, not a scope decision.

1. **T5 slip parsing** behaviour and outputs.
2. **T4 slip parsing** behaviour and outputs (including the recorded Box 54 /
   Box 10 decisions in spec 003).
3. **T5 box→CRA line mapping** behaviour.
4. **T4 box→CRA line mapping** behaviour (2025 config, deferred
   `UnmappedBoxCodes` set unchanged).
5. **Public tax contracts** — every public type, member, and signature in
   `TaxLogicEngine` remains as-is.
6. **Assembly identity** — assembly name `TaxLogicEngine` and all namespaces
   unchanged.
7. **Embedded-resource logical names** — `TaxLogicEngine.Mappings.T5LineMap.2025.json`
   and `TaxLogicEngine.Mappings.T4LineMap.2025.json` remain **exactly** those
   strings. (Provider code and tests hardcode them; the migration keeps the
   explicit `<LogicalName>` values while only the `Include` path may change.)
8. **Monetary rounding** — `MoneyRounding.ToCents` half-up
   (`MidpointRounding.AwayFromZero`) behaviour unchanged.
9. **SIN masking** — `RecipientIdentifier` masking (last 3 digits only)
   unchanged in every output, log, exception, and serialized form.
10. **Tax-year isolation / fail-closed** — a slip whose year has no config
    still fails closed; no adjacent-year fallback is introduced.
11. **The full existing test suite passes**, with the same set of tests, before
    and after the migration.

---

## Migration constraints

- **Git-aware moves.** Relocate files with `git mv` (or an equivalent
  rename-preserving move) rather than copy-then-delete, wherever practical, so
  history is preserved and diffs read as renames.
- **Reference integrity.** After every move, the solution must list each
  project at its new path, and `TaxLogicEngine.Tests` must reference
  `TaxLogicEngine` at its new relative path. No dangling or absolute paths.
- **Embedded-resource `Include` paths** are updated to the project-relative
  location; **`<LogicalName>` values are not touched.**
- **Stage-by-stage verification.** After each migration stage, a full
  `dotnet build` and `dotnet test` against the (new) solution must succeed
  before the next stage begins.
- **No tracked build output or machine-local files.** No `bin/`, `obj/`,
  `.vs/`, `*.user`, secrets, or `.claude/settings.local.json` may become
  tracked as a side effect of the move.
- **No rename of assembly, namespaces, public types, or logical resource names**
  unless a specific need is documented and explicitly approved (none is
  anticipated).
- **Smallest useful migration.** Move and rename only what currently exists.
  Do not create empty projects for future modules
  (Clients, Firms, Documents, Intake, Review, TaxCases). The future module
  pattern may be *documented*; it must not be *scaffolded*.
- **Ordering `[ASSUMED]`** (single reasonable sequence; each stage independently
  green):
  1. Rename `TaxAI.sln.sln` → `TaxAI.sln`; update the six referencing tracked
     files; verify build/test.
  2. `git mv` `TaxLogicEngine/` → `src/TaxLogicEngine/` and
     `TaxLogicEngine.Tests/` → `tests/TaxLogicEngine.Tests/`; update solution
     project paths, the test `ProjectReference`, and embedded-resource `Include`
     paths; verify build/test.
  3. Split root `CLAUDE.md` into repository-level (root) + project-level
     (`src/TaxLogicEngine/CLAUDE.md`); verify no rule is lost or duplicated in
     full.
  4. Add root `AGENTS.md` (and optional local bridge); verify the documented
     read order and workflow rules are present.
  5. Rewrite `README.md` to match reality; verify claims against the source
     tree.
  6. Final end-to-end verification of the whole repository.

---

## User stories

- **As a developer or agent**, I run the documented `dotnet build TaxAI.sln`
  and `dotnet test TaxAI.sln` and they work first try, because the solution is
  named what the docs say and lives where the docs say.
- **As an architect adding the first business module**, I find written boundary
  and dependency rules in the repository, so I do not have to invent them under
  deadline.
- **As a Codex-family agent**, I read `AGENTS.md` and am told, in order, exactly
  which instruction files to load and which workflow rules bind me.
- **As a reviewer**, I trust the README to describe only software that exists,
  so I never green-light a change on the basis of a capability that was never
  built.
- **As the taxpayer whose return depends on this engine**, none of the above
  changes a single box, line, or amount — the migration is provably behaviour-
  preserving.

---

## Functional requirements

**FR-1 — Normalize the solution filename.** The solution is renamed to
`TaxAI.sln`; `TaxAI.sln.sln` no longer exists; every tracked reference to the
old name is updated to the new name (including build/test docs, the
`.claude/settings.json` hook, `.claude/commands/implement.md`,
`.claude/agents/reviewer.md`, `CLAUDE.md`, and `NEXT.md`).

**FR-2 — `src/`/`tests/` layout.** The production project moves to
`src/TaxLogicEngine/` and the test project to `tests/TaxLogicEngine.Tests/`
`[ASSUMED]`, with the solution and all project references updated. No empty
future-module projects are created.

**FR-3 — Preserve assembly, namespaces, public API, and logical resource
names.** No rename of the `TaxLogicEngine` assembly, its namespaces, any public
type/member, or the two embedded-resource logical names.

**FR-4 — Documented modular-monolith boundaries.** Repository-level
documentation states, at minimum:
TaxLogicEngine is deterministic and provider-independent tax logic; future
business modules communicate only through declared contracts; no module reaches
into another module's internal implementation; no module reads another module's
database tables; UI must not compute tax or implement CRA mappings; OCR and LLM
output are untrusted input; infrastructure may depend inward on
application/domain contracts; and tax-domain code must not depend on ASP.NET
Core, EF Core, SQL, cloud SDKs, OCR providers, LLM providers, email providers,
or frontend frameworks. This spec documents these rules; it does not implement
any future module.

**FR-5 — Split agent instructions by scope.** Root `CLAUDE.md` becomes
repository-level guidance (the product framing; the modular-monolith decision;
global dependency rules; global security/PII rules; the
spec/plan/implement/reviewer workflow; current project state; build/test
commands). A project-level `CLAUDE.md` beside the TaxLogicEngine project
`[ASSUMED: src/TaxLogicEngine/CLAUDE.md]` holds TaxLogicEngine specifics
(existing T4/T5 types; parsing and CRA-mapping responsibilities; tax-year
isolation; decimal/rounding rules; SIN masking; JSON-driven mapping; forbidden
dependencies; completed tax specs). The two files must not each carry a full
duplicated copy of the same rule set; shared rules live in one place and are
referenced, not cloned.

**FR-6 — Codex bridge (`AGENTS.md`).** A root `AGENTS.md` instructs Codex and
compatible agents to read, in order: (1) root `CLAUDE.md`; (2) `NEXT.md`;
(3) the nearest project-level `CLAUDE.md`; (4) the requested spec's `prd.md`,
`architecture.md`, `tasks.md`. It restates and preserves the binding workflow
rules: TaskID is always explicit; never auto-select the next task; `tasks.md`
is read-only; prerequisite `.done` markers must exist; only files listed by the
task may change; one task per invocation; complete applicable tests before
declaring completion; never merge or approve its own work. A minimal local
`AGENTS.md` bridge beside TaxLogicEngine may be added if it aids instruction
discovery without duplicating rules `[ASSUMED]`.

**FR-7 — Honest README.** The README distinguishes implemented from planned
functionality. It must not claim PDF generation, a complete tax-return builder,
GPT integration, or a full rule-based validation engine, because the source
tree does not contain them. It documents the resulting `src/`/`tests/`
structure and the corrected `dotnet build TaxAI.sln` / `dotnet test TaxAI.sln`
commands.

**FR-8 — Behaviour preservation.** The migration changes none of the items in
"Preserved behaviour"; the full existing test suite remains green.

---

## Out of scope

Each item below is explicitly excluded from this spec:

- **Any change to tax semantics** — new/changed box, line, rate, threshold,
  mapping, or calculation. **Any accidental change to tax behaviour is out of
  scope and is a defect.**
- Tax Case Core (spec 006 and beyond).
- New CRA forms; T1 calculation; NETFILE/EFILE.
- ASP.NET Core host; frontend/Blazor; authentication.
- Database / Entity Framework; migrations; any persistence.
- OCR or LLM integration.
- Email / invitations; accountant workflows.
- **Empty placeholder modules** for future domains (Clients, Firms, Documents,
  Intake, Review, TaxCases) — not created here.
- Microservices or any out-of-process module split.
- Renaming the `TaxLogicEngine` assembly, its namespaces, public types, or
  embedded-resource logical names.
- Introducing CI (`.github/`), `Directory.Build.props`, or other build
  scaffolding beyond what the smallest useful migration requires `[ASSUMED
  excluded]`.

---

## Acceptance criteria

Each criterion is a single mechanically-checkable condition.

### Solution filename (FR-1)

- **AC-1** — Given the repo after migration, When the tree is listed, Then
  `TaxAI.sln` exists at the repository root.
- **AC-2** — Given the repo after migration, When the tree is listed, Then
  `TaxAI.sln.sln` does not exist.
- **AC-3** — Given the tracked files after migration, When grepped for the
  literal `TaxAI.sln.sln`, Then zero matches are returned.
- **AC-4** — Given the repo after migration, When `dotnet build TaxAI.sln` is
  run, Then it succeeds (exit 0).
- **AC-5** — Given the repo after migration, When `dotnet test TaxAI.sln` is
  run, Then it discovers and runs the full existing test suite (exit 0).

### Layout (FR-2, FR-3)

- **AC-6** — Given the repo after migration, When the tree is listed, Then
  `src/TaxLogicEngine/TaxLogicEngine.csproj` exists. `[ASSUMED path]`
- **AC-7** — Given the repo after migration, When the tree is listed, Then
  `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` exists.
  `[ASSUMED path]`
- **AC-8** — Given the repo after migration, When the tree is listed, Then
  neither `TaxLogicEngine/` nor `TaxLogicEngine.Tests/` exists at the repository
  root.
- **AC-9** — Given the test project after migration, When its
  `ProjectReference` is resolved, Then it points to the production `.csproj` at
  its new `src/` path and resolves without error.
- **AC-10** — Given the repo after migration, When the tree is listed, Then no
  new `.csproj` exists other than the two pre-existing projects (no empty
  future-module projects were created).
- **AC-11** — Given the built assembly after migration, When its assembly name
  is read, Then it is `TaxLogicEngine` (unchanged).
- **AC-12** — Given the source after migration, When the set of public type
  full-names in `TaxLogicEngine` is compared to the pre-migration set, Then the
  two sets are identical (no public type added, removed, or renamed).

### Embedded resources / mapping behaviour (FR-3, FR-8)

- **AC-13** — Given the migrated production assembly, When its manifest resource
  names are enumerated, Then both `TaxLogicEngine.Mappings.T5LineMap.2025.json`
  and `TaxLogicEngine.Mappings.T4LineMap.2025.json` are present with exactly
  those names.
- **AC-14** — Given the migrated repo, When the existing
  `EmbeddedT4LineMapProvider` / `EmbeddedT5LineMapProvider` and the mapping test
  suites run, Then they load the configs and pass unchanged (no CRA line number
  is hardcoded in C#; mapping still comes from the JSON config only).

### Behaviour preservation / suite green (FR-8, Preserved behaviour)

- **AC-15** — Given the migrated repo, When `dotnet test TaxAI.sln` is run,
  Then the same set of tests that passed before the migration passes after it,
  with no test removed, skipped, or newly failing.
- **AC-16** — Given the migrated repo, When the T4 and T5 parser, mapper,
  boundary, PII, and year-isolation test suites run, Then all pass — confirming
  no parsing, mapping, rounding, SIN-masking, or tax-year failure behaviour
  changed.
- **AC-17** — Given the migrated repo, When the diff of every file under the
  production and test projects is reviewed, Then the only content changes are
  path/reference updates (project paths, `ProjectReference`, embedded-resource
  `Include`) — no change to any tax mapping, calculation, or parser logic.

### Module boundaries (FR-4)

- **AC-18** — Given the repository documentation after migration, When it is
  read, Then it contains each of the boundary rules enumerated in FR-4 (a
  reviewer can point to each rule in the text).
- **AC-19** — Given the repo after migration, When the tree is listed, Then no
  future business-module project (Clients, Firms, Documents, Intake, Review,
  TaxCases) has been created.

### Instruction split (FR-5)

- **AC-20** — Given the repo after migration, When the tree is listed, Then a
  project-level `CLAUDE.md` exists beside the TaxLogicEngine project.
  `[ASSUMED: src/TaxLogicEngine/CLAUDE.md]`
- **AC-21** — Given the root `CLAUDE.md` after migration, When it is read, Then
  it contains the repository-level guidance listed in FR-5 (product framing,
  modular-monolith decision, global dependency rules, global security/PII
  rules, spec/plan/implement/reviewer workflow, current state, build/test
  commands).
- **AC-22** — Given the project-level `CLAUDE.md` after migration, When it is
  read, Then it contains the TaxLogicEngine specifics listed in FR-5 (T4/T5
  types, parsing/mapping responsibilities, tax-year isolation, decimal/rounding
  rules, SIN masking, JSON-driven mapping, forbidden dependencies, completed tax
  specs).
- **AC-23** — Given both `CLAUDE.md` files after migration, When compared, Then
  no rule paragraph appears verbatim-duplicated in full in both files (shared
  rules live in one file and are referenced from the other).

### Codex bridge (FR-6)

- **AC-24** — Given the repo after migration, When the tree is listed, Then a
  root `AGENTS.md` exists.
- **AC-25** — Given root `AGENTS.md`, When it is read, Then it names the four
  instruction sources to read in this order: root `CLAUDE.md`; `NEXT.md`; the
  nearest project-level `CLAUDE.md`; the requested spec's `prd.md`,
  `architecture.md`, `tasks.md`.
- **AC-26** — Given root `AGENTS.md`, When it is read, Then it states each
  preserved workflow rule from FR-6 (explicit TaskID only; never auto-select the
  next task; `tasks.md` is read-only; prerequisite `.done` markers required;
  only task-listed files may change; one task per invocation; complete
  applicable tests before completion; never merge or approve its own work).

### README honesty (FR-7)

- **AC-27** — Given `README.md` after migration, When grepped, Then it makes no
  claim of PDF generation, a complete tax-return builder, GPT integration, or a
  full rule-based validation engine.
- **AC-28** — Given `README.md` after migration, When it is read, Then it
  documents the `src/`/`tests/` structure and the commands
  `dotnet build TaxAI.sln` and `dotnet test TaxAI.sln`.
- **AC-29** — Given `README.md` after migration, When each capability it lists
  as *implemented* is checked against the source tree, Then every such
  capability is present in the tree (T4/T5 parsing and box→CRA line mapping via
  JSON config).

### Hygiene (Migration constraints)

- **AC-30** — Given the migration commit set, When tracked files are listed,
  Then no `bin/`, `obj/`, `.vs/`, `*.user`, secret, or
  `.claude/settings.local.json` file is newly tracked.

---

## Rollback and recovery

- The entire migration is performed on the feature branch
  `spec/005-modular-monolith-foundation`; `master` is untouched until a human
  merges.
- Because moves are git-aware renames, the migration is fully reversible by
  reverting the branch's commits; no file content is lost in a copy/delete gap.
- Each ordered stage (see Migration constraints) is independently green, so a
  failing stage can be reverted to the last green stage without discarding
  earlier stages.
- Recovery check: after any revert, `dotnet build TaxAI.sln` (or, before the
  rename stage, the prior solution name) and `dotnet test` must return to green
  with the same test set — the same AC-15 condition guards both directions.

---

## Open questions

Class B decisions made and recorded here for one-pass human override. None
block `/plan`.

- **[ASSUMED]** Production project moves to `src/TaxLogicEngine/`; test project
  moves to `tests/TaxLogicEngine.Tests/` (conventional `src`/`tests` roots).
- **[ASSUMED]** The single solution stays `TaxAI.sln` at the repository root.
- **[ASSUMED]** The project-level instruction file is
  `src/TaxLogicEngine/CLAUDE.md` (beside the `.csproj`).
- **[ASSUMED]** A root `AGENTS.md` is created; an optional minimal
  `src/TaxLogicEngine/AGENTS.md` bridge that only points to the local
  `CLAUDE.md` is included for discovery, carrying no duplicated rules.
- **[ASSUMED]** Shared rules are kept in the repository-level `CLAUDE.md` and
  referenced (not cloned) from the project-level file.
- **[ASSUMED]** Migration ordering is the six-stage sequence in "Migration
  constraints".
- **[ASSUMED]** No `Directory.Build.props`, no `.github/` CI, and no other build
  scaffolding is introduced — smallest useful migration only.
- **[ASSUMED]** `specs/` and `.claude/` remain at the repository root (not moved
  under `src/`).
- **[ASSUMED]** The single-test doc example is updated from
  `dotnet test TaxLogicEngine.Tests` to the new project path under `tests/`.
- **[ASSUMED]** `NEXT.md` is updated to mark the `TaxAI.sln` rename done and to
  reflect the new layout, since it currently lists the rename as pending work.

No Class A (tax semantics, PII exposure, tenant isolation, security boundary)
question is open. This is a pure structural migration; the one tax-adjacent
invariant — preserving the embedded-resource logical names and all tax behaviour
— is stated as a hard constraint (see Preserved behaviour, FR-3, AC-13/14), not
a decision to be made.

---

## CRA sources

No external tax sources required; this feature introduces no tax facts.
