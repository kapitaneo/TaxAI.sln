# CLAUDE.md — Tenancy

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the approved Tenancy module
boundary for spec 011.

## Boundary and exact reference set

Tenancy is the deterministic, provider-independent application boundary for
trusted firm identity, persisted review-record shapes, identity and membership
records, durable-session contracts, package projection, retention, purge
contracts, and fail-closed validation policies.

Its production project references are exactly `CaseReview` and
`TaxLogicEngine`:

- `Tenancy → CaseReview` for the already-assembled review package and the
  existing supported-review-year authority;
- `Tenancy → TaxLogicEngine` for the existing `ParseResult<T>` and
  `ValidationError` result shapes.

Tenancy does not reference `Persistence`, `TaxAI.Portal`, `TaxCases`,
`DocumentExtraction`, or `CaseProcessing`. Infrastructure implements Tenancy's
ports; the dependency arrow never reverses.

## What this module owns

- `TrustedFirmId` and the closed firm-scope exemption inventory.
- The explicit persisted review-record model and its closed allowlist.
- Local user, firm, membership, and authentication-session models.
- Repository, identity-directory, session-store, purge, identifier-source, and
  development-seeder contracts.
- Deterministic guards for UTC instants, opaque tokens, taxpayer text, and
  exact monetary storage.
- Review-package projection, session-lifetime evaluation, membership
  verification, and retention-boundary calculation.

Every instant enters as an explicit parameter. Tenancy reads no system clock,
environment, current user, filesystem, network, or database.

## Spec 016 T01: production-control contracts

Tenancy owns the structural, provider-independent vocabulary used to decide
whether a production host may be admitted. It adds no reference and no package:

- `Models/ProductionControlModels.cs` — the twenty-four DEP evidence subjects
  (numbered as their DEP ids), the evidence states (`Absent` is the zero value
  and only `Approved` counts), the two provider assurance subjects, the two
  approval roles, the closed `ProductionAdmissionRefusal` vocabulary (no
  admitting member), and `ProductionReadiness`.
- `Contracts/ProductionControlContracts.cs` — the exact release binding
  (artifact version, configuration digest, subscription), the evidence
  manifest, role approvals, the managed-identity binding, and the
  `IManagedIdentityBindingProbe` port. Opaque infrastructure identifiers are
  serialization-omitted and every type renders structurally.
- `Services/ProductionEvidencePolicy.cs` — the pure admission policy. Every one
  of DEP-01 to DEP-24 must be Approved and unexpired for the exact release;
  each provider assurance (input-CMK coverage, retention/deletion clock) is
  evaluated independently and neither is satisfied by the DEP-05 row; both
  roles must approve the exact binding. It also computes the deterministic
  configuration digest. It reads no clock: the instant is a parameter.

These types carry infrastructure evidence only — never a secret, taxpayer fact
or SIN — and add no tax policy. The recovery/control ledger and telemetry
contracts arrive with the slices that consume them (T06, T09).

## PII posture: omission at the persistence boundary

No SIN or partial SIN may enter a `Tenancy` persisted model, diagnostic, JSON
representation, or error:

- `ReviewPackageProjection` never reads `T4Slip.Sin`, never names
  `RecipientIdentifier`, and creates no masking, redaction, or scrubbing path.
- The persisted model has no SIN column, partial-SIN value, or SIN-presence
  flag. Identity and session records carry no taxpayer content.
- Taxpayer-controlled and taxpayer-content properties are `[JsonIgnore]`;
  hand-written `ToString()` methods expose fixed structural facts only.
- Validation errors use closed field names and fixed, non-interpolated
  messages. They never echo a caller or taxpayer value.

The engine's existing in-memory `RecipientIdentifier` behavior remains
unchanged. At this boundary, however, the rule is stricter: omission, with no
SIN in any form.

## Retention is a product decision

The seven-year retention default is an engineering product decision made for
this application. It is not a CRA fact and must never be described as a CRA
statutory minimum. `RetentionUntilUtc` is an exclusive UTC boundary derived
from the record's explicit tax year; purge eligibility begins at that boundary.

## No tax-year policy of its own

Tenancy declares no supported-year list, current-year concept, adjacent-year
fallback, or tax-year literal as policy. Review-package support delegates to
`CaseReview.Services.SupportedReviewTaxYears`. Retention consumes the already
validated record year; it does not decide which years are supported.

## Forbidden dependencies and behavior

Do not add EF Core, ASP.NET Core, SQL or another database API, a cloud SDK, OCR,
LLM, email, frontend code, HTTP, filesystem or network I/O, environment or
current-user access, a system-clock read, a second tax-year authority, tax
parsing, a box→line mapping, CRA line literals, arithmetic on tax amounts,
rounding, or a generic repository abstraction.

Do not add a firm-omitting taxpayer-data operation. Review-record save, read,
and purge always require a trusted firm. The only firm-scope exemptions are the
closed identity-bootstrap operations named by `FirmScopeExemptions.All`.

## Implementation workflow

Implement only the explicit TaskID named by the human. Read spec 011's PRD,
architecture, and read-only `tasks.md`; require all earlier-wave `.done`
markers; change only task-listed files; ship applicable positive and negative
xUnit tests; run `dotnet build TaxAI.sln`, `dotnet test TaxAI.sln`, and
`pwsh ./scripts/Invoke-SolutionTests.ps1`; create only the task's marker after
the complete gate passes. Never merge or approve your own work.
