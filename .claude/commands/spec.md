---
description: Generate a PRD (product requirements doc) for a feature
---

Use the **business-analyst** subagent to write a PRD for the feature described
in: $ARGUMENTS

Do not write the PRD yourself, and do not write any code. Your job here is only
to dispatch the subagent and relay what comes back.

## After it returns, show me

1. The path to the created `specs/NNN-<slug>/prd.md`
2. Its one-line summary
3. Its `[ASSUMED]` list (Class B decisions) — so I can override them all in one
   pass rather than a back-and-forth
4. Its **blocking Class A questions**, if any

## Then stop

If there are blocking Class A questions, that is the end of this command. Those
are CRA/domain/PII decisions — they need a human answer before anything else
happens. Do not attempt to research them yourself and do not propose an answer
"to save a round trip."

If the subagent reported that its Definition of Done was not met, relay exactly
which items failed and stop. Do not present a partial PRD as ready.

Only if the subagent emitted `Ready for: /plan` and no Class A questions remain,
tell me the PRD is ready for review, and wait. I run `/plan`, not you.
