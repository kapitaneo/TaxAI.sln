# CLAUDE.md — TaxCases

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the approved TaxCases module
boundary and contract for spec 006.

## Boundary and dependency direction

TaxCases is the deterministic, provider-independent in-memory aggregate for one
firm-owned tax engagement and one explicit tax year. Its sole production
dependency is inward to `TaxLogicEngine`, only for `ParseResult<T>`,
`ValidationError`, and `RecipientIdentifier`. The direction is
`TaxCases → TaxLogicEngine`; TaxLogicEngine never references TaxCases.

TaxCases records participants, evidence receipt metadata, participant-scoped
evidence requirements, participant-aware intake questions/answers, lifecycle
state, derived readiness, and deterministic change records. It performs no CRA
eligibility determination, slip parsing, box→line mapping, tax calculation, or
mapping-resource inspection.

## Public contract

The authoritative public signatures are in
`specs/006-tax-case-core/architecture.md` under “Exact Wave 0 public contract.”
Do not add, rename, or change a public type, enum value, constructor, property,
or operation without a new approved plan. The aggregate is immutable:
operations return `ParseResult<CaseOperationOutcome>` containing a replacement
case and exactly one change record on success. Rejection returns structured
errors, no replacement value, no record, and mutates nothing.

The contract includes `TaxCase`, participant/evidence/checklist/intake/readiness
records and descriptors, fixed lifecycle/change/readiness enums,
`SupportedTaxYears`, `CaseStatusTransitions`, and `ReadinessCalculator`.
Evidence requirements identify one in-case participant, one exact category, and
a positive minimum count. Questions are explicitly case-level or target one
in-case participant.

Public inventory (do not duplicate or rename):

- Enums: `RelationshipKind`, `DocumentCategory`, `CaseStatus`,
  `CaseChangeKind`, `ReadinessState`, `ReadinessReasonKind`,
  `IntakeQuestionScope`.
- State/outcome types: `CaseParticipant`, `ReceivedEvidence`,
  `EvidenceRequirement`, `IntakeQuestion`, `IntakeAnswer`,
  `RequirementChecklist`, `ReadinessReason`, `ReviewReadiness`,
  `CaseChangeRecord`, `CaseOperationOutcome`, `TaxCase`.
- Input types: `CreateTaxCaseRequest`, `ParticipantDescriptor`,
  `EvidenceDescriptor`, `EvidenceRequirementDescriptor`,
  `IntakeAnswerDescriptor`, `ChecklistDescriptor`.
- Policy services: `SupportedTaxYears`, `CaseStatusTransitions`,
  `ReadinessCalculator`.

## Tax-year policy

Spec 006 supports exactly tax year 2025. The case year is explicit; missing,
2024, 2026, or any other year fails closed. Evidence must match the case year.
There is no “current” or adjacent-year fallback, no system-clock read, and no
inspection of TaxLogicEngine mapping files to infer support.

## PII and safe diagnostics

- A participant SIN is optional and, once accepted, uses only the reused
  `RecipientIdentifier`.
- Every public instance type owns a safe `ToString()`; caller text is omitted or
  passed through `SafeDiagnosticText`.
- `SafeDiagnosticText` deterministically masks compact (`123456789`), hyphenated
  (`123-456-789`), and space-separated (`123 456 789`) SIN-like text as
  `***-**-789` using ordinal ASCII rules only.
- Every caller-controlled public string remains available in memory where the
  domain contract requires it but is marked `JsonIgnore`. Direct domain-object
  JSON is defensive, not an API contract; future APIs must map reviewed DTOs.
- Validation errors, readiness reasons, change-record diagnostics, and public
  `ToString()` output never echo a full SIN or raw rejected free text.

## Forbidden dependencies and behavior

Do not add ASP.NET Core, EF Core, SQL/database access, filesystem access,
networking, system-clock/environment/current-user reads, OCR, LLM, cloud, email,
frontend code, persistence, generated identifiers, API/output DTOs, money/tax
calculation, CRA line/box logic, or mapping-resource reads. The same inputs must
always yield equal state, readiness, and change records.

## Implementation workflow

Implement only the explicit TaskID named by the human. Read the spec 006 PRD,
architecture, and read-only `tasks.md`; require earlier-wave `.done` markers;
change only the task-listed files; ship positive and negative xUnit tests; run
`dotnet test TaxAI.sln`; create the task marker only after every test and
acceptance criterion passes; never merge or approve your own work.
