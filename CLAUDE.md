# CLAUDE.md

This file provides repository-level guidance to Claude Code (claude.ai/code) when working in this repository. Project-specific rules for the tax engine live in [src/TaxLogicEngine/CLAUDE.md](src/TaxLogicEngine/CLAUDE.md) — read that file whenever you touch TaxLogicEngine code.

## Product framing

TaxAI is an AI-powered Canadian tax filing assistant. Its deterministic core is **TaxLogicEngine**, a .NET 8.0 class library that today implements CRA slip parsing (T4, T5) and box→CRA-line mapping via JSON config; future business modules (clients, firms, documents, intake, review, tax cases) will grow around it.

Real people file real returns from this output. A wrong box→line mapping is a filing error with financial and legal consequences for the taxpayer.

## The modular monolith decision

TaxAI is, by decision, a **structured modular monolith**: one repository, one .NET solution, one initially-deployable application, in-process module communication, with explicit module ownership and dependency boundaries, and module extraction deferred until demonstrated need.

The future-module *pattern* is documented here; it is **not scaffolded**. Do not create empty projects for future modules (Clients, Firms, Documents, Intake, Review, TaxCases) — a real module's boundary is discovered from its contract, and empty shells acquire gravity.

## Build and Test Commands

```bash
# Build
dotnet build TaxAI.sln

# Run all tests
dotnet test TaxAI.sln

# Run a single test
dotnet test tests/TaxLogicEngine.Tests --filter "FullyQualifiedName~TestMethodName"
```

## Repository layout

- **`src/TaxLogicEngine/`** — the production tax engine (net8.0, nullable enabled). Has its own `CLAUDE.md` with the tax-domain rules and type inventory.
- **`src/TaxCases/`** — the implemented deterministic Tax Case Core module
  (net8.0, nullable enabled). Its local `CLAUDE.md` owns the approved module
  boundary and public-contract inventory.
- **`tests/TaxLogicEngine.Tests/`** — xUnit test project referencing the engine. Uses xunit 2.5.3, coverlet for coverage.
- **`tests/TaxCases.Tests/`** — xUnit contract, model, service, boundary, and
  end-to-end verification for Tax Case Core.
- **`specs/NNN-<slug>/`** — spec artifacts (`prd.md`, `architecture.md`, `tasks.md`, `status/`).
- **`.claude/`** — commands, agents, settings for the feature workflow.

Future modules get `src/<Module>/` homes when they are actually built.

## Module boundary and dependency rules (bind every current and future module)

- **TaxLogicEngine is deterministic, provider-independent tax logic.** No I/O that makes a tax result non-deterministic belongs in it.
- **Modules communicate only through declared contracts.** No module reaches into another module's internal implementation.
- **No module reads another module's database tables.**
- **UI must not compute tax or implement CRA mappings.**
- **OCR and LLM output are untrusted input** — input to be validated, never a source of tax truth.
- **Dependencies point inward.** Infrastructure (EF, OCR, LLM, email, cloud) may depend on application/domain contracts; never the reverse:

```
UI / API host ──► application/module contracts ──► TaxLogicEngine (domain)
Infrastructure (EF, OCR, LLM, email, cloud) ──► application/domain contracts
```

- **Tax-domain code must not depend on** ASP.NET Core, EF Core, SQL, cloud SDKs, OCR providers, LLM providers, email providers, or frontend frameworks.

Today there is one project, so these are documented invariants rather than compiled constraints — but they bind the first business module before it exists.

## Global security / PII rules

- **Never log or echo a full SIN — anywhere in any module.** Mask to the last 3 digits in all logs, errors, and output. (TaxLogicEngine's implementation specifics are in its `CLAUDE.md`.)
- **Tax facts come from canada.ca only.** Not from a blog, not from a competitor's site, not from memory, and not from a prior PRD that lacks a citation. An unverified tax fact never enters a spec or the code.
- No secrets, `*.user` files, or `.claude/settings.local.json` may become tracked.

## Feature development workflow

Features are built through a slash-command pipeline (`.claude/commands/`). Each stage stops for human review before the next. Spec artifacts live under `specs/NNN-<slug>/`. Find the next spec number from existing folders there.

1. **`/spec <feature>`** — delegates to the **business-analyst** subagent, which verifies CRA facts against canada.ca and writes `specs/NNN-<slug>/prd.md`. No code.

2. **`/plan <feature>`** — acting as architect, reads the PRD and writes `architecture.md` (design, data model, trade-offs, risks) plus `tasks.md`. No production code.

3. **`/implement <spec> <TaskID>`** — acting as senior dev, implements ONE explicitly named task and its tests in the same pass, then stops. The task ID is always given explicitly; never pick one yourself.

4. **`reviewer` subagent** — after a PR is open, mechanically verifies it against the domain rules above and the acceptance criteria the task claimed. Read-only. It prepares a human's review; it does not perform one, and it never merges.

### Ambiguity: two classes, handled oppositely

- **Class A — domain, CRA, tax, or PII rules.** Never assume. Stop and ask, however many questions it takes.
- **Class B — everything else** (scope, naming, ordering, v1 vs v2). Never ask. Decide, document the decision, tag it `[ASSUMED]` for one-pass human override.

If you are unsure which class something is, it is Class A. No request for speed or brevity moves an item from A to B.

### tasks.md is organized into waves

Wave 0 is always the **contract wave**: one task declaring every public signature (types, interfaces, method names, JSON schema shape) with `NotImplementedException` bodies. Everything downstream codes against the contract — that is what makes later waves parallel.

Within a wave, tasks are independently implementable **in parallel** by separate agents that cannot see each other. Each wave merges to `master` before the next begins. Within a wave:

- No task references a type or file *created* by a sibling task.
- **No two tasks list the same file path.** This is the rule that prevents merge conflicts.
- Each task's tests pass against `master` + W0 + that task alone.

Parallel tasks are run in separate git worktrees, one Claude Code session each:

```bash
git worktree add ../TaxAI-w1t01 -b feat/002-w1-t01 master
```

Parallelism widens how many tasks run at once. It must never widen what a single task contains. One task, one branch, one PR, one review.

### Task status: marker files, not checkboxes

**`tasks.md` is a read-only plan. Never edit it. Never check a box in it.**

Completion is recorded by creating `specs/NNN-<slug>/status/<TaskID>.done`, containing the PR number and commit SHA. Each agent writes only its own marker, so parallel branches never conflict on a shared status file.

A task is unblocked when every task in every earlier wave has a `.done` marker.

### Never automated

- **Merging.** `git merge` and `gh pr merge` always require a human. Merge is the moment tax logic enters `master`.
- **Opening a non-draft PR with unverified tests.** If tests cannot run, open the PR as a draft and say so in the title.
- **Approving your own work.** No agent merges its own PR.

## Current state

Spec 001 (T5 slip parsing), spec 002 (T5 box→CRA line mapping), spec 003 (T4 slip parsing), and spec 004 (T4 box→CRA line mapping) are implemented and merged. Spec 005 (modular monolith foundation) is complete: the solution is `TaxAI.sln`, projects live under `src/` and `tests/`, instructions are scope-split (this file + `src/TaxLogicEngine/CLAUDE.md`), the `AGENTS.md` bridges exist, and the migration's AC-by-AC verification is recorded in `specs/005-modular-monolith-foundation/verification.md`.

Spec 006 (Tax Case Core) is complete. `src/TaxCases/` implements the immutable,
provider-independent 2025 engagement aggregate with firm/year/participant
isolation, participant-scoped evidence and intake, derived readiness, lifecycle
guards, deterministic change records, and SIN-safe diagnostics. AC-1 through
AC-43 and the module/repository boundaries are recorded in
`specs/006-tax-case-core/verification.md`. The next feature is the Document
Extraction Contract.

The full inventory of existing public types, embedded mapping configs, and the recorded per-spec decisions live in [src/TaxLogicEngine/CLAUDE.md](src/TaxLogicEngine/CLAUDE.md) — **check there before creating anything; do not re-specify or re-implement an existing type.**

Before specifying or implementing anything, grep `specs/*/prd.md` and the source tree. Re-specifying an existing type is how a codebase ends up with two rounding helpers with different midpoint behaviour.

**Keep this section current.** A stale "current state" causes agents to rebuild what already exists.
