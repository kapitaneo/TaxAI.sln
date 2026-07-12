---
description: Turn an approved PRD into architecture + an ordered task breakdown
---

You are acting as a **Software Architect**. Take the approved PRD for the
feature: $ARGUMENTS

Read its `prd.md` first. If no PRD exists, stop and tell me to run `/spec` first.

## Read the feature class first

Read the PRD's `## Feature classification` section. It is either **`tax-domain`**
or **`platform`**, and it selects which architecture template you use below. If
the PRD has no feature class, stop and tell me to send it back through `/spec` —
do not guess the class. A PRD classified `tax-domain` (including any mixed
feature) uses the tax-domain template; a `platform` PRD uses the platform
template.

Do NOT write production code. Output two documents.

## 1a. architecture.md — `tax-domain` template

# Architecture: <Feature Name>

## Approach
The chosen design in prose. Which parts of the class library are touched
(Models / Mappings / Services / Prompts), and how the pieces fit together.

## Data model
New/changed models and DTOs (T4Slip, TaxReturn, TaxLineEntry, etc.). Any new
or changed JSON mapping config in Mappings/ (box → CRA line). Note that mappings
are data-driven — never hardcode CRA line numbers in C#.

## Services & public API
For each service (Mapper / Calculator / Validator / …): the public methods,
their inputs and outputs, and the responsibility of each. Note validation rules
and which domain rules from CLAUDE.md apply (per-tax-year, decimal/half-up
currency math, SIN masking).

## Trade-offs
At least 2 alternatives you considered and why you rejected them. THIS IS
REQUIRED — a Lead must justify decisions, not just make them.

## Risks
Correctness (per-tax-year brackets, rounding), security/PII (SIN handling),
CRA compliance. How each is mitigated.

## 1b. architecture.md — `platform` template

A platform feature changes structure/tooling/workflow, not tax semantics. Its
architecture.md must contain, as sections:

# Architecture: <Feature Name>

- **Current structure** — the repository/projects/build as they exist today,
  grounded in the actual tree.
- **Target structure** — what it becomes.
- **Module boundaries** — the modules/projects and what each owns.
- **Dependency direction** — who may reference whom; the arrows that must not
  reverse.
- **Migration sequence** — the ordered steps from current to target.
- **Path and project-reference changes** — every project reference, solution
  entry, and file path that moves.
- **Backward-compatibility requirements** — namespaces, assembly names, and
  public surfaces that must stay compatible unless the PRD explicitly permits a
  change.
- **Build and test command changes** — the exact new solution/test commands, and
  every place they must be updated (instructions, docs, CI).
- **Trade-offs** — at least 2 alternatives considered and why rejected.
- **Risks and mitigations** — including cross-cutting breakage and CI drift.
- **Rollback / recovery approach** — how to back the migration out safely.
- **Tax behaviour unchanged** — an explicit confirmation that no tax mapping,
  calculation, parser behaviour, or public tax contract changes, and that the
  full existing test suite stays green.

## 2. tasks.md — both classes

A wave-organized, dependency-aware breakdown. Each task is small enough to
implement and test in one sitting. Governing rules from CLAUDE.md apply to every
plan, both classes:

- **Organize tasks into waves.** Use explicit task IDs (`T01`, `T02`, …).
- **List the exact files for every task.**
- **No two sibling tasks in one wave share a file path** — this is what keeps a
  wave parallel and merge-conflict-free.
- **No task references a type or file created by a sibling** in the same wave.
- **Tests ship WITH each task**, not as a final lump.
- **Include a final end-to-end verification wave.**
- Completion is a `.done` marker file, never a checkbox edit — **tasks.md is
  read-only once approved.**
- Format: `- [ ] **T01** — <what> — files: <paths> — depends on: <none / T0x>`

### Wave 0 is always a contract wave — its content depends on the class

- **`tax-domain`:** Wave 0 declares every public C# signature (types, interfaces,
  method names, JSON schema shape) with `NotImplementedException` bodies.
  Ordering thereafter: models → mapping config → services (mapper / calculator /
  validator) → validation rules → wiring → end-to-end.
- **`platform`:** Wave 0 is a **repository/architecture contract wave**. Instead
  of public tax-domain C# signatures it may declare **target paths, module
  boundaries, dependency rules, and verification contracts** (e.g. the solution
  file, project-reference graph, and the build/test command every later task
  must satisfy). Later waves execute the migration against that contract.

### Never combine a repository migration with new business functionality

A platform migration task moves/renames/rewires existing code. It does **not**
also add a new application feature. Keep them in separate specs.

After writing both, summarize the plan (name the feature class) and the task
count in chat, and ask me to review before I run `/implement`.
