# Tasks: Modular Monolith Foundation (spec 005)

**Feature class: `platform`** (repository/tooling/workflow migration; zero tax
semantic impact).

**This file is read-only once approved.** Never edit it; never check a box in
it. Completion is recorded only by a marker file
`specs/005-modular-monolith-foundation/status/<TaskID>.done` containing the PR
number and commit SHA.

## Wave model — why every wave is a single sequential task

This is a migration in which **each stage structurally depends on the previous
one** (you cannot fix the solution's project paths until the projects have
moved; you cannot move the projects sensibly until the solution is renamed).
The PRD mandates that **each ordered stage is independently green** before the
next begins. Therefore the waves run **strictly in sequence**, each merged to
`master` by a human before the next starts. There is no in-wave parallelism to
exploit here — parallelism would widen what a stage contains, which the PRD
forbids. The "no two sibling tasks share a file path" rule is trivially
satisfied because each wave has one task. Files legitimately revisited across
**different** waves (e.g. `CLAUDE.md` in T02, T05, then T08; `NEXT.md` in T02
then T08) are **not** sibling conflicts — sequential merges keep them
conflict-free.

**Branch & merge model (one stage at a time through `master`).** The current
`plan/005-modular-monolith-foundation` branch holds only this approved plan.
Then:

- One task = **one task branch created from the latest `master`** = one PR.
  (e.g. `git worktree add ../TaxAI-t02 -b feat/005-t02 master`.)
- A **human reviews and merges** each PR before the next wave begins; **agents
  never merge or approve their own work.**
- `master` advances **one independently‑green migration stage at a time**; the
  next task branches from the newly‑advanced `master`.
- **Rollback of one stage = revert that stage's merge commit** on `master`;
  earlier green stages are untouched.
- The PRD's feature‑branch safety requirement applies **separately to each task
  branch** — never as one long‑running migration branch.

**Global gate for every implementation task (T02–T07):** run
`dotnet build TaxAI.sln` (or `TaxAI.sln.sln` before T02) and
`dotnet test TaxAI.sln`; the result must be **261 passed, 0 failed, 0 skipped**
before the task's `.done` marker is written. No test may be removed, skipped,
weakened, or rewritten to make the migration pass.

---

## Wave 0 — Migration contract & baseline (no production change)

- [ ] **T01** — Establish and record the migration contract and pre-migration
  baseline, so later stages have a fixed reference to diff against.
  — **files (created):**
    - `specs/005-modular-monolith-foundation/baseline.md`
    - `specs/005-modular-monolith-foundation/status/T01.done`
  — **depends on:** none

  **The baseline record must state:**
  - **Expected old → new paths** (the table from `architecture.md` §Path and
    project-reference changes).
  - **Baseline test inventory:** `dotnet test TaxAI.sln.sln` →
    **261 passed, 0 failed, 0 skipped** (net8.0), and the per-file `[Fact]`/
    `[Theory]` breakdown (30 test files).
  - **Assembly name:** `TaxLogicEngine` (no `<AssemblyName>`; defaults to the
    `.csproj` filename).
  - **Namespaces:** `TaxLogicEngine.Models`, `TaxLogicEngine.Services`.
  - **Public-API preservation expectation (authoritative, reflection-based):**
    the **exact sorted full-name list** of every public type in the built
    `TaxLogicEngine` assembly, captured now by **reflection over the built DLL**
    — this is the authoritative AC-12 reference set. A source-code grep or an
    approximate count is **not** acceptable as the authoritative baseline (any
    "~34" figure is explanatory prose only, and must be labelled
    non-authoritative). Record the **exact reproducible command or procedure**
    used (e.g. build the assembly, then load it and enumerate
    `Assembly.GetExportedTypes()` by `FullName`, sorted ordinal), so **T08 runs
    the identical reflection procedure** and compares the two exact sorted sets.
  - **Embedded-resource logical names (never change):**
    `TaxLogicEngine.Mappings.T5LineMap.2025.json`,
    `TaxLogicEngine.Mappings.T4LineMap.2025.json`.
  - **The six literal references** to `TaxAI.sln.sln` (five files) that Stage 1
    must remove operationally.
  - **Allowed migration sequence:** the seven stages, in order.
  - **Verification commands:** `dotnet build`, `dotnet test`, tree listing,
    literal grep, manifest-resource enumeration, public-type diff.

  **Acceptance criteria (mechanically checkable):**
  - `baseline.md` exists and records the 261-test count, assembly name, both
    namespaces, both logical resource names, the **exact sorted reflection-based
    public-type full-name list** together with the exact reproducible command
    that yields it, the old→new path table, and the verification commands.
  - No file outside `specs/005-modular-monolith-foundation/` is created or
    modified by this task.
  - `git diff --name-only master...HEAD` lists only
    `specs/005-modular-monolith-foundation/baseline.md` and
    `specs/005-modular-monolith-foundation/status/T01.done`.
  - Create `specs/005-modular-monolith-foundation/status/T01.done` only after
    all required verification, tests, and acceptance criteria for T01 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Wave 1 — Solution filename normalization

- [ ] **T02** — Rename the solution `TaxAI.sln.sln` → `TaxAI.sln` and update
  every operational reference to the old name.
  — **files:**
    - **renamed (git):** `TaxAI.sln.sln` → `TaxAI.sln`
    - **modified:** `CLAUDE.md` (two commands, lines ~15 and ~18),
      `.claude/settings.json` (PostToolUse hook command),
      `.claude/commands/implement.md` (test command, line ~61),
      `.claude/agents/reviewer.md` (reword the "do not assume `TaxAI.sln.sln`"
      sentence to drop the stale literal while keeping the "don't hardcode the
      solution filename" intent),
      `NEXT.md` (remove the stale `TaxAI.sln.sln` literal from the pending-work
      line; the final NEXT.md refresh is T08 after all acceptance criteria pass)
    - **created:** `specs/005-modular-monolith-foundation/status/T02.done`
  — **depends on:** T01

  **Notes / constraints:**
  - Use `git mv` for the solution rename so history is preserved.
  - Only the two relative project paths and the filename change; solution GUIDs
    and configuration blocks are untouched.
  - Do **not** move any project directory in this task.

  **Acceptance criteria:**
  - `TaxAI.sln` exists at the repo root; `TaxAI.sln.sln` does not (AC-1, AC-2).
  - Grep of operational files for the literal `TaxAI.sln.sln` returns **zero**
    matches (AC-3, scoped per `[ASSUMED-AC3-SCOPE]` — `specs/**` history
    excluded).
  - `dotnet build TaxAI.sln` exits 0 (AC-4); `dotnet test TaxAI.sln` runs the
    full suite — **261 passed** (AC-5, AC-15).
  - The `.claude/settings.json` hook now invokes `dotnet test TaxAI.sln ...`.
  - Create `specs/005-modular-monolith-foundation/status/T02.done` only after
    all required verification, tests, and acceptance criteria for T02 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Wave 2 — Production project directory migration

- [ ] **T03** — Move the production project to `src/` and repair the solution
  and the test reference so the stage stays green.
  — **files:**
    - **moved (git):** `TaxLogicEngine/` → `src/TaxLogicEngine/` (the entire
      directory as a unit — `TaxLogicEngine.csproj`, `Models/`, `Services/`,
      `Mappings/` incl. `README.md`, `T4LineMap.2025.json`,
      `T5LineMap.2025.json`). Key file renames:
      `TaxLogicEngine/TaxLogicEngine.csproj` →
      `src/TaxLogicEngine/TaxLogicEngine.csproj`.
    - **modified:** `TaxAI.sln` (production `Project(...)` path →
      `src\TaxLogicEngine\TaxLogicEngine.csproj`),
      `TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` (interim
      `ProjectReference` → `..\src\TaxLogicEngine\TaxLogicEngine.csproj`)
    - **conditionally modified (only if the build proves it necessary):**
      `src/TaxLogicEngine/TaxLogicEngine.csproj` embedded-resource `Include`
      paths — **`<LogicalName>` values never change**
    - **created:** `specs/005-modular-monolith-foundation/status/T03.done`
  — **depends on:** T02

  **Notes / constraints:**
  - `Mappings/` moves with the project, so the project-relative `Include`
    (`Mappings\T*.json`) is expected to still resolve. **Verify first:** build,
    then enumerate manifest resource names; edit an `Include` **only if** the
    resource is actually missing after the move.
  - The test project has not moved yet, so its `ProjectReference` is the interim
    `..\src\...` path; T04 rewrites it to the final `..\..\src\...`.

  **Acceptance criteria:**
  - `src/TaxLogicEngine/TaxLogicEngine.csproj` exists (AC-6); the old
    `TaxLogicEngine/` directory no longer exists at the repo root (part of AC-8).
  - `dotnet build TaxAI.sln` exits 0; `dotnet test TaxAI.sln` = **261 passed**
    (AC-15).
  - Both manifest resource names are present and unchanged:
    `TaxLogicEngine.Mappings.T5LineMap.2025.json`,
    `TaxLogicEngine.Mappings.T4LineMap.2025.json` (AC-13); the
    `EmbeddedT4/T5LineMapProvider` and mapping test suites pass (AC-14).
  - The built assembly name is still `TaxLogicEngine` (AC-11).
  - `git diff` reads as renames, not add/delete pairs; no `bin/`/`obj/`/`.vs/`
    added (AC-30).
  - Create `specs/005-modular-monolith-foundation/status/T03.done` only after
    all required verification, tests, and acceptance criteria for T03 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Wave 3 — Test project directory migration

- [ ] **T04** — Move the test project to `tests/` and finalize its reference and
  the solution.
  — **files:**
    - **moved (git):** `TaxLogicEngine.Tests/` → `tests/TaxLogicEngine.Tests/`
      (entire directory as a unit — `TaxLogicEngine.Tests.csproj`, `Models/`,
      `Services/`, `Mappings/`, `UnitTest1.cs`; 30 test files). Key file rename:
      `TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` →
      `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj`.
    - **modified:** `TaxAI.sln` (test `Project(...)` path →
      `tests\TaxLogicEngine.Tests\TaxLogicEngine.Tests.csproj`),
      `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj`
      (`ProjectReference` → `..\..\src\TaxLogicEngine\TaxLogicEngine.csproj`)
    - **created:** `specs/005-modular-monolith-foundation/status/T04.done`
  — **depends on:** T03

  **Acceptance criteria:**
  - `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` exists (AC-7);
    neither `TaxLogicEngine/` nor `TaxLogicEngine.Tests/` exists at the repo
    root (AC-8).
  - The test `ProjectReference` resolves to the production `.csproj` at its
    `src/` path (AC-9).
  - Exactly two `.csproj` files exist in the repo — no new project was created
    (AC-10).
  - `dotnet build TaxAI.sln` exits 0; `dotnet test TaxAI.sln` = **261 passed**
    (AC-5, AC-15, AC-16).
  - `git diff` reads as renames; no build output or `settings.local.json`
    tracked (AC-30).
  - Create `specs/005-modular-monolith-foundation/status/T04.done` only after
    all required verification, tests, and acceptance criteria for T04 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Wave 4 — Instruction split (CLAUDE.md by scope)

- [ ] **T05** — Split the single combined `CLAUDE.md` into repository-level
  (root) and project-level (`src/TaxLogicEngine/CLAUDE.md`) files; no rule is
  duplicated verbatim in full.
  — **files:**
    - **modified:** `CLAUDE.md` (rewritten to repository-level scope: product
      framing; the modular-monolith decision; global dependency/boundary rules
      from FR-4; global security/PII rules; the spec→plan→implement→reviewer
      workflow incl. the two-class ambiguity rule and the wave/`.done` model;
      current state; `dotnet build TaxAI.sln` / `dotnet test TaxAI.sln`)
    - **created:** `src/TaxLogicEngine/CLAUDE.md` (TaxLogicEngine specifics:
      existing T4/T5 types; parsing and CRA-mapping responsibilities; tax-year
      isolation; decimal/half-up rounding; SIN masking; JSON-driven mapping;
      forbidden dependencies; completed tax specs 001–004)
    - **created:** `specs/005-modular-monolith-foundation/status/T05.done`
  — **depends on:** T04

  **Notes / constraints:**
  - Shared rules live in **one** file (root) and are **referenced** — not
    cloned — from the project file (AC-23).
  - No tax rule is lost, weakened, or newly invented; this is a scope-split of
    existing text, not a rewrite of policy.

  **Acceptance criteria:**
  - `src/TaxLogicEngine/CLAUDE.md` exists (AC-20).
  - Root `CLAUDE.md` contains the repository-level guidance list from FR-5
    (AC-21).
  - Project `CLAUDE.md` contains the TaxLogicEngine specifics list from FR-5
    (AC-22).
  - No rule paragraph appears verbatim-duplicated in full in both files (AC-23);
    the module-boundary rules of FR-4 are present in repository-level docs
    (AC-18).
  - `dotnet build TaxAI.sln` / `dotnet test TaxAI.sln` still **261 passed**
    (instruction-only change must not affect the build).
  - Create `specs/005-modular-monolith-foundation/status/T05.done` only after
    all required verification, tests, and acceptance criteria for T05 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Wave 5 — Codex bridge (AGENTS.md)

- [ ] **T06** — Add the root Codex bridge and the required minimal local bridge.
  — **files:**
    - **created:** `AGENTS.md` (root Codex bridge)
    - **created (required, minimal — pointer only, no duplicated rules):**
      `src/TaxLogicEngine/AGENTS.md`
    - **created:** `specs/005-modular-monolith-foundation/status/T06.done`
  — **depends on:** T05

  **Notes / constraints:**
  - Root `AGENTS.md` names the four instruction sources **in order**: (1) root
    `CLAUDE.md`; (2) `NEXT.md`; (3) the nearest project-level `CLAUDE.md`;
    (4) the requested spec's `prd.md`, `architecture.md`, `tasks.md`.
  - It restates the binding workflow rules: explicit TaskID only; never
    auto-select the next task; `tasks.md` is read-only; prerequisite `.done`
    markers required; only task-listed files may change; one task per
    invocation; complete applicable tests before completion; never merge or
    approve own work.
  - The **required** local `src/TaxLogicEngine/AGENTS.md` is a **minimal pointer
    only** (`[ASSUMED-LOCAL-BRIDGE]`): it points to the root `AGENTS.md`, the
    root `CLAUDE.md`, and the local `src/TaxLogicEngine/CLAUDE.md`. It carries
    **no** duplicated architecture, workflow, or tax rules.

  **Acceptance criteria:**
  - Root `AGENTS.md` exists (AC-24).
  - It names the four sources in the required order (AC-25).
  - It states each preserved workflow rule from FR-6 (AC-26).
  - `src/TaxLogicEngine/AGENTS.md` exists and is a minimal pointer to root
    `AGENTS.md`, root `CLAUDE.md`, and local `CLAUDE.md`, with no duplicated
    architecture, workflow, or tax rules.
  - `dotnet build`/`dotnet test` unaffected — **261 passed**.
  - Create `specs/005-modular-monolith-foundation/status/T06.done` only after
    all required verification, tests, and acceptance criteria for T06 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Wave 6 — README

- [ ] **T07** — Rewrite the README to describe only software that exists.
  **T07 does not update project state:** it does **not** mark spec 005 complete
  and does **not** touch `NEXT.md` — those updates belong to T08, after final
  verification (`[ASSUMED-STATE-AT-T08]`).
  — **files:**
    - **modified:** `README.md` (remove claims of PDF generation, a complete
      tax-return builder, GPT integration, and a full rule-based validation
      engine; document the `src/`/`tests/` structure; document
      `dotnet build TaxAI.sln` and `dotnet test TaxAI.sln`; the single-test
      example becomes `dotnet test tests/TaxLogicEngine.Tests`; list only
      implemented capabilities — T4/T5 parsing and box→CRA-line mapping via JSON
      config)
    - **created:** `specs/005-modular-monolith-foundation/status/T07.done`
  — **depends on:** T06

  **Acceptance criteria:**
  - `README.md` makes no claim of PDF generation, a complete tax-return builder,
    GPT integration, or a full rule-based validation engine (AC-27).
  - `README.md` documents the `src/`/`tests/` structure and both `TaxAI.sln`
    commands (AC-28).
  - Every capability README lists as *implemented* is present in the source
    tree (AC-29).
  - `git diff --name-only master...HEAD` lists only `README.md` and
    `specs/005-modular-monolith-foundation/status/T07.done` (no `NEXT.md`, no
    completion claim).
  - `dotnet build`/`dotnet test` unaffected — **261 passed**.
  - Create `specs/005-modular-monolith-foundation/status/T07.done` only after
    all required verification, tests, and acceptance criteria for T07 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Wave 7 — Final end-to-end verification

- [ ] **T08** — Verify the whole migrated repository against the Wave 0
  baseline, record the result, and — **only if every AC passes** — update
  project state.
  — **files:**
    - **created:** `specs/005-modular-monolith-foundation/verification.md`
    - **created:** `specs/005-modular-monolith-foundation/status/T08.done`
    - **modified (only after all AC-1…AC-30 pass):**
      `NEXT.md` (mark spec 005 complete; make **spec 006 Tax Case Core** the
      next approved product feature; reflect the `src/`/`tests/` layout; keep
      the forward roadmap),
      `CLAUDE.md` (update the "Current state" section to mark spec 005 —
      modular-monolith foundation — complete)
  — **depends on:** T07

  **The verification record must confirm, each with the command used:**
  - `dotnet build TaxAI.sln` exits 0 and `dotnet test TaxAI.sln` =
    **261 passed, 0 failed, 0 skipped** — the same set as the Wave 0 baseline
    (AC-15, AC-16).
  - Tree listing: `TaxAI.sln` present, `TaxAI.sln.sln` absent (AC-1/2);
    `src/TaxLogicEngine/TaxLogicEngine.csproj` and
    `tests/TaxLogicEngine.Tests/TaxLogicEngine.Tests.csproj` present (AC-6/7);
    no root `TaxLogicEngine/` or `TaxLogicEngine.Tests/` (AC-8); exactly two
    `.csproj` (AC-10); no future-module project (AC-19).
  - Operational grep for `TaxAI.sln.sln` → zero matches (AC-3, scoped).
  - Assembly name `TaxLogicEngine` (AC-11); the public-type set is compared by
    **re-running the identical Wave 0 reflection procedure** over the migrated
    `TaxLogicEngine` assembly and diffing the two **exact sorted full-name
    lists** — they must be identical (AC-12).
  - Manifest resource names present and unchanged (AC-13); mapping/provider
    suites pass (AC-14).
  - Diff review: every pre-existing production/test/`.csproj`/mapping/resource
    change is path/reference-only — no parser, mapper, model, JSON mapping,
    rounding, PII/SIN, or tax-year behaviour changed (AC-17).
  - Instruction split present and non-duplicated (AC-18/20/21/22/23); root
    `AGENTS.md` present with correct read order and workflow rules
    (AC-24/25/26); required local `src/TaxLogicEngine/AGENTS.md` pointer present;
    README honest and accurate (AC-27/28/29).
  - Hygiene: no `bin/`, `obj/`, `.vs/`, `*.user`, secret, or
    `.claude/settings.local.json` newly tracked (AC-30).

  **State-update guard (`[ASSUMED-STATE-AT-T08]`):**
  - The `NEXT.md` and `CLAUDE.md` state updates are applied **only after** every
    AC-1…AC-30 passes.
  - **If any acceptance criterion fails:** `verification.md` records the failure,
    `NEXT.md` and `CLAUDE.md` **must not** claim spec 005 is complete, and **no
    `.done` marker is created** — the task stops and reports.

  **Acceptance criteria:**
  - `verification.md` records a pass/fail line for every AC (AC-1 … AC-30) with
    the exact command or file:line evidence used.
  - When all 30 ACs pass: `NEXT.md` marks spec 005 complete with spec 006 next,
    and `CLAUDE.md` "Current state" marks spec 005 complete.
  - The only files this task changes are
    `specs/005-modular-monolith-foundation/verification.md` and
    `specs/005-modular-monolith-foundation/status/T08.done`, and — only after
    all AC-1 through AC-30 pass — `NEXT.md` and `CLAUDE.md`.
  - Create `specs/005-modular-monolith-foundation/status/T08.done` only after
    all required verification, tests, and acceptance criteria for T08 pass. If
    any verification, test, or acceptance criterion fails, do not create the
    marker.

---

## Summary

| Wave | Task | Purpose |
|---|---|---|
| 0 | T01 | Migration contract & baseline record (261 tests, assembly, API, logical names) |
| 1 | T02 | Rename `TaxAI.sln.sln` → `TaxAI.sln`; update six operational references |
| 2 | T03 | `git mv` production project → `src/TaxLogicEngine/`; fix solution + interim test ref |
| 3 | T04 | `git mv` test project → `tests/TaxLogicEngine.Tests/`; finalize solution + test ref |
| 4 | T05 | Split `CLAUDE.md` into root + `src/TaxLogicEngine/CLAUDE.md` |
| 5 | T06 | Root `AGENTS.md` Codex bridge + required minimal local bridge |
| 6 | T07 | Honest `README.md` only (no state change) |
| 7 | T08 | Final verification against baseline; on full pass, mark spec 005 complete in `NEXT.md` + `CLAUDE.md` |

**8 tasks, 8 sequential waves.** Each task branches from the latest `master` and
is merged by a human before the next wave begins; agents never merge. Every
implementation stage (T02–T07) must show **261 passed, 0 failed, 0 skipped**
before its `.done` marker is written, and T08 requires the same green suite
plus all 30 ACs before it may mark spec 005 complete. No test is deleted,
skipped, weakened, or rewritten to make the migration pass.
