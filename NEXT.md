# Current Focus

## Completed

- Spec 001 — T5 parsing
- Spec 002 — T5 CRA line mapping
- Spec 003 — T4 parsing
- Spec 004 — T4 CRA line mapping

## Next

Create spec 005:

`005-modular-monolith-foundation`

Its purpose will be repository restructuring only:

- migrate to a structured modular monolith;
- preserve all existing TaxLogicEngine behavior;
- introduce no new tax functionality;
- keep all existing tests passing;
- normalize the solution filename to `TaxAI.sln`;
- establish repository-level and project-level instruction boundaries.

## After spec 005

- split repository and TaxLogicEngine agent instructions;
- create the Codex `AGENTS.md` bridge;
- create spec 006 — Tax Case Core;
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
