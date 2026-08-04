# CLAUDE.md — ExtractionWorkflow

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy, the
modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file owns the ExtractionWorkflow module
boundary and its public-contract inventory.

## What this project is

`ExtractionWorkflow` is the deterministic, provider-independent **application
coordination module** for spec 015. It is the production caller the tax chain
never had: the thing that turns a stored source document into the
`ValidatedExtraction` that starts
`ExtractionEnvelopeValidator` → `T4CaseProcessor` → `ReviewPackageAssembler` →
`ReviewPackageProjection` → `IReviewRecordRepository.SaveAsync`.

**It invents no tax truth of its own.** Every typed slip comes from
`T4SlipParser`, every CRA line entry from `T4LineMapper` plus its JSON config,
every processed result from `T4CaseProcessor`, every review package from
`ReviewPackageAssembler`. Engine errors are surfaced verbatim and in order.

**As of spec 015 T02 this module carries the extraction foundation only.** It
declares the document-intelligence and extraction-only content-read ports,
the safe result shapes, the closed field/error/enum vocabularies, and the
single in-memory payload assembly path. The ingest workflow, the review
workflow, the store port, and the HTTP surface arrive in later waves and are
deliberately absent here.

## Dependencies point inward

Production project references are exactly what the working code consumes today,
all inward:

- `TaxLogicEngine` — the raw payload (`T4RawInput`/`T4RawBox`), `T4BoxCodes`,
  and the existing `ParseResult<T>` / `ValidationError` result vocabulary;
- `DocumentExtraction` — the existing `ExtractionProvenance`;
- `Tenancy` — `TrustedFirmId` and the existing UTC-instant guard.

**Nothing is pre-added for a later wave.** The architecture's wider dependency
set — the existing T4 chain in `CaseProcessing`, the review assembler in
`CaseReview`, the server-stored case in `ClientIntake` — is real, but a
reference no code names is an unused reference. The task that first consumes one
adds it in the same pass, and owns this file for that wave.

Forbidden: `Persistence`, `TaxAI.Portal`, `DocumentIntelligence`, and every
package. This module declares ports; infrastructure implements them. No EF Core,
ASP.NET Core, SQL, cloud SDK, OCR, LLM, HTTP, filesystem, network, clock,
environment, current-user, or `HttpContext` access may enter it.

## No second authority, of any kind

- **No CRA line number, box→line table, box→meaning table, tax rate, threshold,
  limit, gross-up, credit percentage, or eligibility rule.** A box code is an
  untrusted string copied from a document and transported as data; unknown codes
  flow to `T4OtherInformation` and surface as `UnmappedBoxCodes` exactly as they
  do today. There is no `switch` or lookup assigning tax significance to a box
  code anywhere in this module.
- **No arithmetic and no rounding.** Amounts stay untrusted strings until the
  engine parses them. The `decimal` token appears only where the existing
  `ExtractionProvenance.Confidence` contract is transported.
- **No tax-year policy.** No supported-year list, no current-year concept, no
  adjacent-year fallback, and no four-digit year literal as policy. The year of
  an extraction is the server-stored `StoredIntakeCase.TaxYear`; the payload's
  year is what the document showed; the existing envelope validator refuses the
  pair when they disagree, and neither value is ever coerced.
- **No second envelope, validator, provenance, document-kind, parser, mapper,
  assembler, projection, masking helper, or supported-year type.**

`DocumentClassification` is not a second `ExtractedDocumentKind`: it answers
what a provider says the bytes are, and it needs an `Unsupported` member the
envelope discriminator must never have.

## PII: omission, and type invariants where omission is not enough

- **No SIN in any form** — full, masked, hashed, tokenized, last-three,
  last-four, presence flag, or SIN-derived identifier — reaches any browser
  model, URL, cookie, header, SQL column, audit event, log, metric, trace,
  exception message, diagnostic, or tracked fixture. This module declares no SIN
  property, no SIN type, and no masking, redaction, or scrubbing helper, and it
  never constructs a `RecipientIdentifier`.
- **Box 12 exists only in process memory, for one call.**
  `ExtractionPayloadBuilder.Build` hands the assembled payload to a continuation
  and never returns it. No result shape carries it and no store has it, which is
  why a correction re-reads the source and rebuilds rather than loading a saved
  payload.
- **`ExtractionBoxValue` refuses the SIN box at construction** and
  **`CorrectionField` declares no Box 12 member.** Both are type invariants
  rather than filters: a box loop that wrote every extracted box is the
  highest-risk defect in this feature, and a correction naming the SIN is
  unrepresentable rather than rejected.
- Identifier, taxpayer-text, and content-key properties are `[JsonIgnore]`.
  Every public instance type is a **sealed class, never a record**, with a
  hand-written `ToString()` emitting fixed structural facts only — counts,
  closed enum values, and presence flags. A compiler-generated record
  `ToString()` prints every property, which is exactly the leak this forbids.
- `FieldValidationOutcome.Field` is `[JsonIgnore]` and absent from `ToString()`:
  engine field keys are built from the box code the document showed, and that
  code is untrusted text.
- Error messages are fixed, non-interpolated constants identified by
  `ExtractionWorkflowFields`. None can carry a caller value, document value,
  file name, content key, fingerprint, provider payload, or prompt.

## Untrusted input stays untrusted

Classification, extracted values, and confidence are **provider metadata, never
tax truth**. Confidence is an attention signal for a human reader: **no
confidence threshold exists anywhere in this feature**, and confidence never
rejects, accepts, gates, or is described as accuracy, correctness, certainty, or
a guarantee. The existing `[0,1]` structural check stays with the existing
envelope validator.

A provider error, timeout, cancellation, empty result, or malformed response
fails closed: nothing partial, nothing stored, nothing thrown to the caller.

## Medical evidence is neutral evidence

The medical model is **structured neutral evidence for a future medical-expense
tax-domain specification**, never a tax result. It carries only the approved
closed field set and cannot represent a diagnosis, treatment narrative,
medication name, detailed service description, payment-card information,
insurance identifier, or health-card number. It asserts nothing about
eligibility, deductibility, claimability, reimbursement, claimants, or CRA
lines, and performs no aggregation, period selection, dependant determination,
threshold, or calculation. The T4-only review contracts are unchanged and
`SlipTypeCodes.All` stays exactly `["T4"]`.

## Append facts; project current state

Extraction versions, medical evidence versions, classification outcomes and
overrides, per-field outcomes, duplicate determinations, and corrections are
**append-only**. Nothing is updated or deleted in place; a later version
supersedes an earlier one by existing. Acceptance is always an explicit human
act — no machine output arrives accepted.

## The gate

T02 ships the foundation. It authorizes **no** ingest workflow, review workflow,
store, migration, HTTP route, or UI component; no production
document-intelligence provider; no source-content preview, download, or export;
no retention or purge path; and no deployment. Each of those belongs to a later
task or a later approved specification. Implement only the explicit TaskID named
by the human, change only that task's listed files, ship its tests in the same
pass, and run `pwsh ./scripts/Invoke-SolutionTests.ps1` before creating the
task's `.done` marker. Never merge or approve your own work.
