# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TaxLogicEngine is a .NET 8.0 class library for an AI-powered Canadian tax filing assistant. It handles CRA form parsing (T4, T5), field-to-line-number mapping via JSON config, draft tax return generation, rule-based validation, and GPT-assisted explanations.

Real people file real returns from this output. A wrong box→line mapping is a filing error with financial and legal consequences for the taxpayer.

## Build and Test Commands

```bash
# Build
dotnet build TaxAI.sln

# Run all tests
dotnet test TaxAI.sln

# Run a single test
dotnet test TaxLogicEngine.Tests --filter "FullyQualifiedName~TestMethodName"
```

## Architecture

- **TaxLogicEngine/** — Core class library (net8.0, nullable enabled). Contains Models (T4Slip, TaxReturn, TaxLineEntry), Mappings (JSON config for box→CRA line), Services (Mapper, Calculator, Validator), and Prompts (GPT templates).
- **TaxLogicEngine.Tests/** — xUnit test project referencing the engine. Uses xunit 2.5.3, coverlet for coverage.

## Key Design Decisions

- Field mappings are data-driven via JSON files in Mappings/, not hardcoded.
- CRA line numbers are the canonical identifiers linking form boxes to tax return entries.

## Domain rules (Canadian tax)

- **Never log or echo a full SIN.** Mask to the last 3 digits in all logs, errors, and output. Note that C# records auto-generate a `ToString()` that prints every property — override it.
- **Tax calculations are per tax-year.** Never mix brackets, rates, or limits across years. There is no "latest" or "current" year. If a slip's year has no config, fail closed — never fall back to an adjacent year.
- **All currency math uses `decimal`,** rounded half-up to cents (`MidpointRounding.AwayFromZero`). Never `double` or `float`. Never a bare `Math.Round`.
- **T4/T5 box values map to CRA line numbers via the JSON config only.** Never hardcode a line number in C#.
- **CRA filing deadlines:** T1 — April 30; self-employed — June 15.
- **Tax facts come from canada.ca only.** Not from a blog, not from a competitor's site, not from memory, and not from a prior PRD that lacks a citation. An unverified tax fact never enters a spec or the code.

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

Spec 001 (T5 slip parsing), spec 002 (T5 box→CRA line mapping), spec 003 (T4 slip parsing), and spec 004 (T4 box→CRA line mapping) are implemented and merged. The following already exist — **check before creating anything; do not re-specify or re-implement them:**

- `ParseResult<T>`, `ValidationError`
- `MoneyRounding.ToCents` (half-up currency helper)
- `RecipientIdentifier` (SIN masking value object)
- `T5BoxCodes`, `T5OtherInformation`, `T5Slip`
- `T5RawInput`, `T5RawBox`, `T5SlipParser`
- `TaxLineEntry`, `T5MappingResult`
- `BoxLineMapping`, `T5LineMappingConfig`, `T5LineMap`
- `T5SlipBoxAmounts`, `T5LineMapper`
- `T4BoxCodes`, `T4OtherInformation`, `T4Slip`
- `T4RawInput`, `T4RawBox`, `T4SlipParser`
- `T4MappingResult`, `T4BoxLineMapping`, `T4LineMappingConfig`, `T4LineMap`
- `T4OtherInformationCodes`, `IT4LineMapProvider`, `EmbeddedT4LineMapProvider`
- `T4SlipBoxAmounts`, `T4LineMapper`
- `Mappings/T4LineMap.2025.json` (T4 box→line config, 2025 only)

Spec 003 covers T4 **parsing only**; spec 004 adds the T4 box→CRA line mapping (`TaxLineEntry` generation for T4) — both are merged. Two T4 required-field decisions are recorded in `specs/003-t4-slip-parsing/architecture.md` and flagged `[CONFIRM]`: Box 54 is required (so the parser presumes the employer/filing-side copy, not the employee copy), and Box 10 (province) is validated only when present. Spec 004 ships **2025 only**; several T4 boxes are deliberately deferred to `UnmappedBoxCodes` (calculation/schedule/residence-dependent) with per-box rationale in `TaxLogicEngine/Mappings/README.md`.

Before specifying or implementing anything, grep `specs/*/prd.md` and the source tree. Re-specifying an existing type is how a codebase ends up with two rounding helpers with different midpoint behaviour.

**Keep this section current.** A stale "current state" causes agents to rebuild what already exists.
