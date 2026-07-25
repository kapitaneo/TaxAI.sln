# CLAUDE.md — DocumentExtraction

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the approved DocumentExtraction
module boundary and contract for spec 007.

## Boundary and dependency direction

DocumentExtraction is the deterministic, provider-independent ingestion
contract between future extraction provider adapters (OCR/LLM) and the tax
domain. Its sole production dependency is inward:
`DocumentExtraction → TaxLogicEngine`, only for `T4RawInput`/`T4RawBox`,
`ParseResult<T>`, and `ValidationError`. TaxLogicEngine never references
DocumentExtraction. There is **no `TaxCases` reference in either direction** —
no `SafeDiagnosticText`, and no masking helper of any kind exists here.

The module receives untrusted envelopes and validates well-formedness and
attribution **presence only**. It never authenticates a caller, never proves
case-belongs-to-firm or participant-belongs-to-case membership, and never
queries Tax Case Core — those belong to the future application workflow that
implements the ingress port. It performs no parsing, typing, normalization,
redaction, conversion, box→line mapping, rounding, or calculation: extraction
output is untrusted input, never a source of tax truth, and the payload passes
through reference-identical and value-unchanged.

## Public contract

The authoritative public signatures are in
`specs/007-document-extraction-contract/architecture.md`. Do not add, rename,
or change a public type, enum value, constructor, property, or method without a
new approved plan.

Public inventory (do not duplicate or rename):

- Models: `ExtractedDocumentKind` (`T4Slip = 1`, `GenericEvidence = 2`; **no
  T5 value** — T5 is deferred by approved scope), `ExtractionProvenance`,
  `ExtractionEnvelope`, `ValidatedExtraction` (internal constructor — only the
  validator creates one), `ExtractionFields` (the closed set of
  `ValidationError.Field` constants).
- Ports: `IDocumentExtractionIngress` — the inward application boundary,
  declaring exactly
  `Task<ParseResult<ValidatedExtraction>> SubmitAsync(ExtractionEnvelope envelope, CancellationToken cancellationToken = default)`.
  **No implementation ships in spec 007**; the async shape is signature-only
  and the interface performs no I/O.
- Services: `ExtractionEnvelopeValidator.Validate` (the single synchronous,
  static, pure, deterministic gate) and `SupportedExtractionTaxYears`.

## Tax-year policy

Spec 007 supports exactly tax year 2025, declared only in
`SupportedExtractionTaxYears`. The envelope year is explicit; missing, 2024,
2026, or any other year fails closed, and a T4 payload year must equal the
envelope year with neither value coerced. There is no "current" or
adjacent-year fallback, no system-clock read, and no inspection of
TaxLogicEngine mapping resources to infer support.

## PII: omission, not masking

- The raw `T4RawInput` payload may carry an unmasked SIN in Box 12 and may be
  retained unchanged in memory. It is **omitted entirely** — never echoed,
  never masked — from every `ToString()`, every `ValidationError` message,
  every diagnostic, and supported direct JSON serialization (`JsonIgnore`).
- Every caller-controlled string (`DocumentId`, `FirmId`, `CaseId`,
  `ParticipantId`, `SourceReference`, `ProviderLabel`) is likewise `JsonIgnore`
  and excluded from `ToString()` and error messages. Errors identify fields by
  `ExtractionFields` constants only; messages are fixed templates containing no
  caller or payload value.
- `ToString()` on every public type emits fixed structural facts only (kind,
  tax year, payload presence, box count, confidence). No output may contain a
  planted caller/payload string or any 9-consecutive-digit substring. Public
  types are sealed classes, never records, so no compiler-generated
  `ToString()` can print property values.
- No new SIN property or SIN type may be introduced, and the module contains no
  construction or population of `RecipientIdentifier` — Box 12 conversion
  remains `T4SlipParser`'s job downstream.

## Forbidden dependencies and behavior

Do not add references to `TaxCases`, ASP.NET Core, EF Core, SQL/database
access, filesystem access, networking, system-clock/environment/current-user
reads, OCR, LLM, cloud, email, frontend code, persistence, generated
identifiers, `T4SlipParser`/`T4LineMapper`/`MoneyRounding` usage, CRA
line-number literals, box→line mapping, or `TaxLogicEngine.Mappings` resource
reads. The module consumes no T5 type. The same envelope must always yield an
equal result: equal success/failure, equal error sequence, same payload
reference.

## Fail-closed workflow

Validation returns `ParseResult<ValidatedExtraction>` and fails closed: failure
carries at least one `ValidationError` and no value; success carries a value
and no errors; nothing is mutated, coerced, or partially accepted. Implement
only the explicit TaskID named by the human. Read the spec 007 PRD,
architecture, and read-only `tasks.md`; require earlier-wave `.done` markers;
change only the task-listed files; ship positive and negative xUnit tests; run
`dotnet test TaxAI.sln`; create the task marker only after every test and
acceptance criterion passes; never merge or approve your own work.
