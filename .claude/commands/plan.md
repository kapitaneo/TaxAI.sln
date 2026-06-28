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
The chosen design in prose. Which layers are touched (Domain / Application /
Infrastructure / API), which CQRS commands and queries are introduced.

## Data model
New/changed entities, EF Core mappings, MSSQL schema changes (tables, columns,
indexes). Note any migration needed.

## Commands & queries
For each: name, input DTO, output, the MediatR handler's responsibility,
validation rules.

## API surface
Endpoints: method, route under /api/v1, request/response shape, status codes.

## Trade-offs
At least 2 alternatives you considered and why you rejected them. THIS IS
REQUIRED — a Lead must justify decisions, not just make them.

## Risks
Performance, security/PII, concurrency, compliance. How each is mitigated.

## 2. tasks.md

A numbered, ordered, dependency-aware checklist. Each task must be small enough
to implement and test in one sitting. Format:

- [ ] **T01** — <what> — files: <paths> — depends on: <none / T0x>
- [ ] **T02** — ...

Order matters: domain model → persistence → handlers → validation → endpoints
→ wiring. Tests come WITH each task, not as a final lump.

After writing both, summarize the plan and the task count in chat, and ask me
to review before I run `/implement`.
