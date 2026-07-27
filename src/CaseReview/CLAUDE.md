# CLAUDE.md — CaseReview

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the approved CaseReview module
boundary and contract for spec 009.

## Boundary and dependency direction

CaseReview is the deterministic, provider-independent review boundary after
spec 008. It owns exactly two jobs: assemble one review package from successful
`ProcessedT4Extraction` values, and record exactly one immutable human decision
on that package.

Its production dependencies are exactly:

- `CaseReview → CaseProcessing`, for `ProcessedT4Extraction`;
- `CaseReview → TaxLogicEngine`, for engine result/model types,
  `ParseResult<T>`, `ValidationError`, and `T4SlipBoxAmounts.Enumerate`.

There is no `TaxCases` reference in either direction and no direct
`DocumentExtraction` reference. No existing module references CaseReview.
CaseReview never verifies firm/case/participant membership, reviewer
authorization, or filing authority.

## Public contract

The authoritative signatures are in
`specs/009-accountant-review-package/architecture.md`. Do not add, rename, or
change a public type, constructor, property, enum value, or method without a new
approved plan.

Public inventory:

- Models: `SignOffDecision`, `ReviewedBoxAmount`, `ReviewedSlip`,
  `ReviewDecisionDescriptor`, `ReviewSignOff`, `AccountantReviewPackage`, and
  `ReviewFields`.
- Services: `SupportedReviewTaxYears` and `ReviewPackageAssembler`.

Output constructors are internal. `AccountantReviewPackage` is partial only so
core state and decision behavior have disjoint task-file ownership. All public
instance types are sealed classes, never records, and own a hand-written
structural `ToString()`.

## Immutable snapshots and no tax truth

Assembly accepts only successful `ProcessedT4Extraction` values. It retains the
exact `T4Slip` reference and ordered value snapshots of line entries,
source-box-code provenance, populated box amounts, and unmapped codes. It never
reorders, filters, labels, sums, rounds, parses, or maps them.

`T4SlipBoxAmounts.Enumerate` is the single permitted engine call. Never call
`T4SlipParser`, `T4LineMapper`, `IT4LineMapProvider`,
`EmbeddedT4LineMapProvider`, or `MoneyRounding`; never read a mapping resource;
never add a CRA line-number literal, box-code list, or box-to-line table.

## Package-level acknowledgement gate

When any reviewed slip carries an unmapped code, `Approved` requires one
package-level affirmative acknowledgement. The exact
`bool? UnmappedBoxesAcknowledged` property distinguishes absent, negative, and
affirmative input. No per-slip or per-code acknowledgement,
classification, severity, explanation, or disposition exists. The mechanism
does not represent a dispute about a mapped amount.

`ChangesRequested` and `Rejected` remain available without acknowledgement,
subject to their note rules. Recording returns a replacement package; it never
mutates the original and never permits a second decision.

## Tax-year policy

Spec 009 supports exactly tax year 2025, declared only in
`SupportedReviewTaxYears`. Every element is checked. There is no current year,
adjacent-year fallback, coercion, clock read, or mapping-resource inference.

## PII: masking plus omission

- The only SIN representation is the engine's existing masked
  `RecipientIdentifier`, reached through `ReviewedSlip.Slip.Sin`. Declare no SIN
  property/type, construct no `RecipientIdentifier`, and add no masking helper.
- Every caller-controlled string, including reviewer note, is readable only in
  memory and marked `JsonIgnore`.
- Reviewed nested content is also omitted from defensive direct JSON.
- Every public instance type has a hand-written `ToString()` containing fixed
  structural facts only—never a caller string, name, note, amount, box code,
  line number, or SIN.
- Module-originated errors use `ReviewFields` and fixed, non-interpolated
  messages. They never echo input.

## Forbidden dependencies and behavior

Do not add `TaxCases`, direct `DocumentExtraction`, ASP.NET Core, EF Core, SQL,
database, filesystem, network, clock, environment/current-user, OCR, LLM,
cloud, email, frontend, persistence, notification, generated identifier, T5,
arithmetic, rounding, or mapping-config behavior.

## Implementation workflow

Implement only the explicit TaskID named by the human. Read spec 009's PRD,
architecture, and read-only `tasks.md`; require earlier-wave `.done` markers;
change only task-listed files; ship positive and negative xUnit tests; run
`dotnet build TaxAI.sln --no-incremental` and `dotnet test TaxAI.sln`; create
only the task's marker after the full suite passes. Never merge or approve your
own work.
