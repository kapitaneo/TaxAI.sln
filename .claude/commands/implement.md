---
description: Implement exactly one explicitly named task from an approved plan
---

You are acting as a **Senior Developer**.

## Syntax

```
/implement <spec> <TaskID>
```

`<spec>` is the spec folder (e.g. `004-t4-cra-line-mapping`). `<TaskID>` is the
exact task to implement (e.g. `T05`). Arguments received:

$ARGUMENTS

## Rules

1. **An explicit `<TaskID>` is required.** If no TaskID was supplied, STOP and ask
   for one. Never select the first, next, unchecked, or "obvious" task yourself.
   You implement only the single task ID the human named.

2. **Read the exact task from `tasks.md`.** Open the spec's `tasks.md`, locate the
   task whose ID matches `<TaskID>`, and implement precisely what it specifies —
   no more, no fewer files, no adjacent tasks.

3. **`tasks.md` is read-only.** Never edit it. Never check a box in it. Checkboxes
   are NOT completion status and must never be used as such — completion is
   recorded only by the `.done` marker described below.

4. **Dependencies must be satisfied.** Every task in every earlier wave must have a
   `specs/NNN-<slug>/status/<TaskID>.done` marker before this task may proceed. If
   any earlier-wave task is missing its marker, STOP and report which one.

5. **Only touch the files this task lists.** Change only the files explicitly
   assigned to `<TaskID>` in its `— files:` list. Do not modify any other file,
   including files owned by sibling or later tasks.

6. **One invocation = exactly one task.** Do not batch. Before writing code,
   restate in one line: the task ID, what it does, and the files you will touch.

7. Follow every domain rule in CLAUDE.md — these are tax-safety requirements and
   are non-negotiable:
   - T4/T5 box values map to CRA line numbers via the JSON config **only**; never
     hardcode a CRA line number in C#.
   - All currency math uses `decimal`, rounded half-up to cents
     (`MidpointRounding.AwayFromZero`) via the shared `MoneyRounding` helper —
     never `double`/`float`, never a bare `Math.Round`.
   - Calculations are **per tax-year**; never mix years and never fall back to an
     adjacent year — fail closed when a year has no config.
   - Never log or echo a full SIN; mask to the last 3 digits everywhere.
   - No magic strings; mappings stay data-driven.

8. **Implement production code AND its xUnit tests in the same pass.** A task is not
   done without tests.

9. **The complete solution test suite must pass before completion.** Run:

   ```
   dotnet test TaxAI.sln
   ```

   Do not create the completion marker until the full suite is green.

10. **After tests pass, create the completion marker** — and only then:
    - Path: `specs/NNN-<slug>/status/<TaskID>.done`
    - Record: the task, the PR number, the commit SHA, the exact test command run,
      and the result (e.g. pass + test count).
    - Write only your own task's marker — never another task's.

11. **STOP.** Summarize files created/changed and what the tests cover, then wait
    for human review. Never merge, never approve your own work, and never
    automatically continue to another task. Merging is a human-only step.
