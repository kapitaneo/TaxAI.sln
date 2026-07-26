# CLAUDE.md — CaseProcessing

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy, the
modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the approved CaseProcessing
module boundary and contract for spec 008.

## Boundary and dependency direction

CaseProcessing is a thin, deterministic **coordinator**, not a tax authority. It
owns exactly one job: drive a single already-validated T4 extraction through the
existing engine and return an attributed, fail-closed result. Every tax
judgement is delegated to code that already shipped and was already verified.

Its production dependencies are exactly two, and both point inward:

```
DocumentExtraction ──┐
                     ├──► CaseProcessing ──► TaxLogicEngine
TaxLogicEngine ──────┘
```

`CaseProcessing → DocumentExtraction` (for `ValidatedExtraction` and
`ExtractedDocumentKind`) and `CaseProcessing → TaxLogicEngine` (for the parser,
mapper, models, and `ParseResult<T>`). There is **no `TaxCases` reference in
either direction**, and nothing references `CaseProcessing` — not
`TaxLogicEngine`, not `TaxCases`, not `DocumentExtraction`, nor their test
trees.

The module never re-implements, duplicates, or bypasses spec 007's envelope
gate; its input is a `ValidatedExtraction` that already passed it. It never
mutates a `TaxCase`, attaches evidence, persists anything, or decides
authorization: a processed result carries attribution **forward** and verifies
none of it. It is not proof that the case belongs to the firm, the participant
to the case, or that the caller was authorized.

## Public contract

The authoritative public signatures are in
`specs/008-t4-case-processor/architecture.md`. Do not add, rename, or change a
public type, constructor, property, or method without a new approved plan.

Public inventory (do not duplicate or rename):

- Models: `ProcessedT4Extraction` (sealed class, **internal constructor** — only
  the processor creates one, so its existence is the proof the whole pipeline
  succeeded) and `T4ProcessingFields` (the closed set of `ValidationError.Field`
  constants: `Extraction`, `Kind`, `Payload`, `PayloadBoxes`, `PayloadBoxCode`,
  `TaxYear`, `SlipYear`).
- Services: `T4CaseProcessor` — the single entry point, declaring exactly
  `T4CaseProcessor(IT4LineMapProvider lineMapProvider)` and
  `ParseResult<ProcessedT4Extraction> Process(ValidatedExtraction? extraction)` —
  and `SupportedProcessingTaxYears`.
- Internal: `PayloadStructureGuard.Inspect` — the stage-4 seam guard, kept out of
  the public surface so the module exposes exactly one behaviour; the test
  project sees it through `InternalsVisibleTo`.

`ProcessedT4Extraction` deliberately **does not expose the raw `T4RawInput`**.
Once parsed its role is over, and retaining it would create a second, unmasked
carrier of the Box 12 SIN. Callers that still need it hold the
`ValidatedExtraction`.

## No tax truth is invented here

- The typed slip comes **only** from `T4SlipParser`. Never construct a `T4Slip`,
  never type or coerce a box value, never convert Box 12 into a
  `RecipientIdentifier`, and never add, relax, or reorder a parser rule.
- CRA line entries come **only** from `T4LineMapper` plus the injected
  `IT4LineMapProvider`. The module contains **no CRA line-number literal**, no
  box→line table, and no `TaxLogicEngine.Mappings` resource read.
- The module performs **no arithmetic and no rounding**: no `Math.Round`, no
  `MidpointRounding`, no `double`, no `float`. `MoneyRounding` is reused only
  indirectly, through the parser and mapper.
- Engine errors are surfaced **verbatim and in order** — never translated,
  summarized, re-worded, filtered, or interleaved with this module's own.
- `T4MappingResult.UnmappedBoxCodes` is surfaced unchanged and unfiltered and
  never causes rejection.
- The stage-4 guard is structural only: it reads **no `T4RawBox.Value`**, decides
  nothing about whether a code is known, required, or meaningful, and never
  trims, normalizes, deduplicates, reorders, or drops a box — duplicates
  preserved by spec 007 must still reach the parser so the parser's own duplicate
  rejection fires.

## Tax-year policy

Spec 008 processes exactly tax year 2025, declared only in
`SupportedProcessingTaxYears`. An unsupported extraction year fails closed, the
parsed slip's year must equal the extraction's year with neither value coerced,
and the mapping provider is queried for **exactly one year — the slip's — with
no retry** for any other year. There is no "current" or adjacent-year fallback,
no system-clock read, and no inspection of mapping resources to infer support.

## PII: the omission → masking seam

This module is the exact moment an opaque payload becomes typed taxpayer data,
and it is the highest-risk point in the feature.

- The raw `T4RawInput` (whose Box 12 may hold an unmasked SIN) stays handled by
  **omission** while it is here: never rendered, never serialized, never echoed
  in a `ValidationError`, and never placed on the result.
- The parsed Box 12 is handled by **masking**, through the engine's
  `RecipientIdentifier` reached via `T4Slip.Sin` — the one permitted SIN
  representation. Introduce **no new SIN property, no new SIN type, and no second
  masking helper**; never construct a `RecipientIdentifier` here; and use no
  `SafeDiagnosticText` or other masking helper from another module.
- Every caller-controlled string on the result (`DocumentId`, `FirmId`, `CaseId`,
  `ParticipantId`, `SourceReference`) **and** the `Slip` and `Mapping` properties
  are `JsonIgnore`d, so a careless direct serialization cannot become a
  taxpayer-data export. `TaxYear` is the one serializable property — a four-digit
  year is a structural fact.
- Public types are sealed classes, never records, so no compiler-generated
  `ToString()` can print property values. `ToString()` is hand-written and emits
  fixed structural facts only — tax year, line-entry count, unmapped-box count,
  whether a SIN is present — never a caller value, an identity string, an amount,
  or a SIN in any form, masked included.
- This module's own error messages are **non-interpolated constants**. A template
  that cannot take an argument cannot leak a caller or payload value. The
  supported year is never spelled out in a message either, so the policy stays in
  one place. Fields are named by `T4ProcessingFields` constants only.

## Forbidden dependencies and behavior

Do not add references to `TaxCases`, ASP.NET Core, EF Core, SQL/database access,
filesystem access, networking, system-clock (`DateTime.Now`/`UtcNow`),
environment or current-user reads, OCR, LLM, cloud, email, frontend code,
persistence, or generated identifiers. The module consumes no T5 type. The same
extraction and the same provider must always yield an equal result: equal
success/failure, equal error sequence and ordering, and neither the extraction
nor its payload mutated.

## Fail-closed workflow

`Process` returns `ParseResult<ProcessedT4Extraction>` and fails closed: failure
carries at least one `ValidationError` and no value; success carries a value and
no errors; nothing is mutated, coerced, or partially accepted; and **nothing
throws** on untrusted input. Implement only the explicit TaskID named by the
human. Read the spec 008 PRD, architecture, and read-only `tasks.md`; require
earlier-wave `.done` markers; change only the task-listed files; ship positive
and negative xUnit tests; run `dotnet build TaxAI.sln --no-incremental` and
`dotnet test TaxAI.sln`; create the task marker only after every test and
acceptance criterion passes; never merge or approve your own work.
