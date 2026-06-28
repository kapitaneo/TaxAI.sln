---
description: Generate a PRD (product requirements doc) for a feature
---

You are acting as a **Product Manager + Business Analyst**. The user wants a PRD
for the feature described in: $ARGUMENTS

Do NOT write any code. Your only output is a spec document.

## Steps

1. Find the next spec number by looking at existing folders in `specs/`.
   Create `specs/NNN-<slug>/prd.md`.

2. If the feature description is ambiguous, ask me up to 3 clarifying questions
   BEFORE writing. Common gaps: who the user is, what data is involved,
   edge cases, what "done" means. Do not guess on domain rules — Canadian tax
   filing has CRA compliance constraints; defer to the domain rules in CLAUDE.md.

3. Write the PRD using exactly this structure:

   # PRD: <Feature Name>

   ## Problem
   What problem does this solve, for whom. 2-3 sentences.

   ## User stories
   As a <role>, I want <capability>, so that <benefit>.
   (3-6 stories, each independently testable)

   ## Functional requirements
   Numbered list. Each must be verifiable. Reference domain rules from CLAUDE.md.

   ## Out of scope
   Explicitly list what this feature does NOT do. Prevents scope creep.

   ## Acceptance criteria
   Given / When / Then format. These become the test cases later.

   ## Open questions
   Anything that needs a decision from a stakeholder before planning.

4. After writing, summarize in chat what you created and ask me to review
   before I run `/plan`.

Keep it tight. A PRD nobody reads is worse than no PRD.
