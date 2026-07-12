# Current Focus

## Completed

- Spec 001 — T5 parsing
- Spec 002 — T5 CRA line mapping
- Spec 003 — T4 parsing
- Spec 004 — T4 CRA line mapping
- Spec 005 — Modular monolith foundation (repository restructuring only):
  - solution normalized to `TaxAI.sln`;
  - projects moved to `src/TaxLogicEngine/` and `tests/TaxLogicEngine.Tests/`;
  - all existing TaxLogicEngine behavior preserved (same 261 tests green);
  - agent instructions split: root `CLAUDE.md` (repository-level) +
    `src/TaxLogicEngine/CLAUDE.md` (project-level);
  - Codex `AGENTS.md` bridge added (root + minimal local pointer);
  - `README.md` rewritten to describe only implemented software;
  - full AC-1…AC-30 verification recorded in
    `specs/005-modular-monolith-foundation/verification.md`.

## Next

Create spec 006 — **Tax Case Core** (the next approved product feature).

## After spec 006

- then Document Extraction Contract;
- then T4 Case Processor;
- then Accountant Review Package;
- then Portal Foundation.

## Explicitly not now

- new CRA forms;
- T1 calculation;
- NETFILE/EFILE;
- frontend implementation;
- database implementation;
- OCR or LLM integration;
- microservices.
