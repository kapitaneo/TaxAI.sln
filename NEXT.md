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

- Spec 009 — Accountant Review Package:
  - deterministic, immutable 2025 review packages implemented in
    `src/CaseReview/`, assembled only from successful
    `ProcessedT4Extraction` values;
  - exact slip identity and ordered snapshots of engine-produced line entries,
    source-box provenance, populated amounts, and unmapped codes, with no
    parsing, mapping, arithmetic, or rounding;
  - one immutable human decision per package, with fixed fail-closed validation
    and one package-level affirmative acknowledgement required before approval
    whenever any reviewed slip has an unmapped code;
  - PII by masking plus omission: the engine's masked `RecipientIdentifier` is
    the only SIN representation, while caller strings and reviewed nested
    content are omitted from defensive rendering and serialization;
  - AC-1…AC-54 verification recorded in
    `specs/009-accountant-review-package/verification.md`.

- Spec 010 — Portal Foundation:
  - the repository's first runnable application: `src/TaxAI.Portal/`, an
    ASP.NET Core host and the single deployable the modular-monolith decision
    committed to;
  - one composition root registering `IT4LineMapProvider`,
    `IT5LineMapProvider`, and `T4CaseProcessor` as singletons and nothing else,
    with validate-on-build and scope validation forced on unconditionally;
  - exactly two GET routes — `/health` returning a fixed literal and `/`
    returning a static shell document — with no input surface of any kind;
  - the inward dependency rule proved in both directions: the portal reaches
    modules only through contracts, and no module references the portal;
  - the web layer proved to contain no tax computation, CRA line number,
    mapping table, taxpayer type, PII, or tax-year policy; PII is handled by
    omission — the data never enters the process;
  - AC-1…AC-50 verification recorded in
    `specs/010-portal-foundation/verification.md`.

## Next

Decide the **first taxpayer-data surface** for the portal. It is blocked, by
design, on the Class A questions recorded in
`specs/010-portal-foundation/prd.md` (Open questions A–F): authentication and
authorization; tenant/firm isolation at the HTTP boundary; what taxpayer data a
UI may render and at what masking level; logging, telemetry, and error-reporting
policy; hosting, TLS, public exposure, and data residency; and whether this host
also exposes the untrusted document-extraction ingress.

Those answers are a human's to give. Until they exist, no endpoint, page,
component, or middleware that renders taxpayer data, accepts user input, uploads
a document, persists data, reads a session or identity, or crosses a
firm/case/participant boundary may be added to the portal — adding one makes the
feature `tax-domain`.

## Explicitly not now

- new CRA forms;
- T1 calculation;
- NETFILE/EFILE;
- frontend implementation;
- database implementation;
- OCR or LLM integration;
- microservices.
