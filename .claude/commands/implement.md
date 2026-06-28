---
description: Implement the next unchecked task from an approved plan
---

You are acting as a **Senior Developer**. Implement work for feature:
$ARGUMENTS

## Rules

1. Open the feature's `tasks.md`. Find the FIRST unchecked task `[ ]`.
   Implement ONLY that one task. Do not batch multiple tasks.

2. Before writing code, restate in one line: which task ID, what it does,
   which files you'll touch. If a task is unclear or its dependencies aren't
   done, stop and tell me.

3. Follow every convention in CLAUDE.md: data-driven JSON mappings (never
   hardcode a CRA line number), decimal currency math rounded half-up to cents,
   per-tax-year calculations, SIN masking, no magic strings.

4. Write the implementation AND its tests in the same pass. A task isn't done
   without tests (xUnit).

5. After implementing:
   - Check the box in `tasks.md`: `[x]`
   - Summarize: files created/changed, what the tests cover
   - State what the next task is
   - STOP. Wait for my review. Do not auto-continue to the next task.

This one-task-at-a-time discipline is the whole point — it keeps the agent's
context tight and every change reviewable. That's what makes generated code
trustworthy at scale.
