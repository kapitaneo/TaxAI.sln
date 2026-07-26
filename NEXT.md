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

- Spec 006 — Tax Case Core:
  - deterministic, immutable 2025 tax-case aggregate implemented in
    `src/TaxCases/`;
  - participants, evidence receipt metadata, participant-scoped checklist and
    intake, derived readiness, lifecycle, and deterministic change records;
  - firm/year/participant isolation and exhaustive SIN-safe diagnostics;
  - AC-1…AC-43 verification recorded in
    `specs/006-tax-case-core/verification.md`.

- Spec 007 — Document Extraction Contract:
  - deterministic, provider-independent ingestion contract implemented in
    `src/DocumentExtraction/`;
  - untrusted `ExtractionEnvelope` + `ExtractionProvenance`, the internal-only
    `ValidatedExtraction`, the closed `ExtractionFields` set, and the
    signature-only `IDocumentExtractionIngress` port (no implementation ships
    in spec 007);
  - `ExtractionEnvelopeValidator.Validate` — the single fail-closed gate:
    attribution presence only, 2025-only year isolation, payload/kind
    coherence, and a mechanical empty-extraction rule;
  - the raw `T4RawInput` payload passes through reference-identical and
    value-unchanged — no parsing, typing, normalization, mapping, or rounding,
    so extraction output never becomes tax truth;
  - PII by **omission, not masking**: no caller or payload value appears in any
    `ToString()`, `ValidationError`, diagnostic, or JSON output;
  - AC-1…AC-30 verification recorded in
    `specs/007-document-extraction-contract/verification.md`.

- Spec 008 — T4 Case Processor:
  - the deterministic coordinator implemented in `src/CaseProcessing/`, driving
    one already-validated T4 extraction through the existing engine;
  - `T4CaseProcessor.Process` — the module's single behaviour — runs eight fixed
    stages: null extraction, kind/payload, supported year and payload structure
    (these two accumulate), parse, slip/extraction year coherence, map, and
    construct;
  - the internal `PayloadStructureGuard` closes the seam where a payload that
    cleared spec 007's gate could still make `T4SlipParser.Parse` throw on a null
    `Boxes` collection — an escaping exception is not fail-closed behaviour;
  - **no tax truth is invented here**: the slip comes only from `T4SlipParser`,
    the line entries only from `T4LineMapper` plus the injected
    `IT4LineMapProvider`, and engine errors are surfaced verbatim and in order.
    The module contains no CRA line number, no box→line table, no arithmetic, and
    no `decimal` token;
  - the provider is asked for exactly the slip's year, once, with no
    adjacent-year retry; the 2025 policy lives only in
    `SupportedProcessingTaxYears`;
  - PII crosses an **omission → masking** seam: the raw `T4RawInput` never
    reaches the result, and the parsed Box 12 exists only as the engine's masked
    `RecipientIdentifier` on `T4Slip.Sin`;
  - AC-1…AC-43 verification recorded in
    `specs/008-t4-case-processor/verification.md`.

## Next

Create the **Accountant Review Package** spec.

## After Accountant Review Package

- then Portal Foundation.

## Explicitly not now

- new CRA forms;
- T1 calculation;
- NETFILE/EFILE;
- frontend implementation;
- database implementation;
- OCR or LLM integration;
- microservices.
