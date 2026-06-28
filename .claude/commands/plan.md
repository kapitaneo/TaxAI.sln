---
description: Turn an approved PRD into architecture + an ordered task breakdown
---

You are acting as a **Software Architect**. Take the approved PRD for the
feature: $ARGUMENTS

Read its `prd.md` first. If no PRD exists, stop and tell me to run `/spec` first.

Do NOT write production code. Output two documents.

## 1. architecture.md

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

## 2. tasks.md

A numbered, ordered, dependency-aware checklist. Each task must be small enough
to implement and test in one sitting. Format:

- [ ] **T01** — <what> — files: <paths> — depends on: <none / T0x>
- [ ] **T02** — ...

Order matters: models → mapping config → services (mapper/calculator/validator)
→ validation rules → wiring. Tests come WITH each task, not as a final lump.

After writing both, summarize the plan and the task count in chat, and ask me
to review before I run `/implement`.
