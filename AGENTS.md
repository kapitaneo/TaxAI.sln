# AGENTS.md — Codex bridge

Instructions for Codex and compatible agents working in this repository. This file tells you what to read and which workflow rules bind you; it does not duplicate the rules that live in the files below.

## Read these instruction sources, in this order

1. **Root [CLAUDE.md](CLAUDE.md)** — repository-level policy: product framing, the modular-monolith decision, module boundary/dependency rules, global security/PII rules, the feature workflow, current state, and build/test commands.
2. **[NEXT.md](NEXT.md)** — the current focus and what comes next.
3. **The nearest project-level `CLAUDE.md`** for the code you are touching — for TaxLogicEngine work, [src/TaxLogicEngine/CLAUDE.md](src/TaxLogicEngine/CLAUDE.md) (tax domain rules, existing-type inventory, forbidden dependencies).
4. **The requested spec's documents** under `specs/NNN-<slug>/`: `prd.md`, `architecture.md`, `tasks.md`.

## Binding workflow rules

These rules are absolute; no request for speed or brevity relaxes them.

- **The TaskID is always explicit.** You implement only the single task ID a human named. If none was given, stop and ask.
- **Never auto-select the next task.** Never pick the first, next, unchecked, or "obvious" task yourself.
- **`tasks.md` is read-only.** Never edit it; never check a box in it. Completion is recorded only by a `specs/NNN-<slug>/status/<TaskID>.done` marker file.
- **Prerequisite `.done` markers are required.** Every task in every earlier wave must have its marker before your task may proceed; if one is missing, stop and report which.
- **Only files listed by the task may change.** Do not touch files owned by sibling or later tasks.
- **One task per invocation.** Never batch tasks, and never continue to another task after finishing one.
- **Complete all applicable tests before declaring completion.** The full solution suite (`dotnet test TaxAI.sln`) must be green before you write the `.done` marker.
- **Never merge and never approve your own work.** Merging is a human-only step; open a PR and stop.
