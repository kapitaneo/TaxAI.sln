---
description: Generate a PRD (product requirements doc) for a feature
---

Use the **business-analyst** subagent to write a PRD for the feature described
in: $ARGUMENTS

Do not write the PRD yourself, and do not write any code. Your job here is only
to dispatch the subagent and relay what comes back.

## Feature classification (required)

Every PRD is one of exactly two feature classes, and the subagent MUST classify
it and state the class in a `## Feature classification` section:

- **`tax-domain`** — changes Canadian tax semantics, CRA forms, mappings,
  calculations, tax-year rules, or taxpayer-domain validation.
- **`platform`** — changes repository structure, build tooling, module
  boundaries, hosting, infrastructure, UI foundations, developer workflow, or
  other technical capability **without introducing or changing any tax fact.**

If a feature mixes both classes, it is **`tax-domain`**. When unsure, it is
`tax-domain`. The returned summary MUST name the selected feature class.

### For a `tax-domain` PRD

Preserve every existing requirement in full: CRA verification against canada.ca,
explicit tax-year scope, fail-closed behaviour, PII/SIN masking, and one negative
AC per fail-closed rule. Nothing below relaxes any of these.

### For a `platform` PRD

- Do **not** require a tax year when no tax behaviour changes.
- Do **not** require CRA research when the PRD introduces no CRA claim.
- The PRD MUST state, verbatim: `Tax/CRA semantic impact: none`.
- The PRD MUST carry an acceptance criterion that **all existing tax behaviour
  and the full test suite remain unchanged and green.**
- Any accidental change to tax behaviour is **out of scope** and must be listed
  as such.
- Acceptance criteria MUST be **mechanically testable** (a single check each).
- The PRD MUST include a **current-state and reuse analysis** before it proposes
  any new structure — what exists today, what is reused, what is genuinely new.

## After it returns, show me

1. The path to the created `specs/NNN-<slug>/prd.md`
2. Its **feature class** (`tax-domain` or `platform`) and one-line summary
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
