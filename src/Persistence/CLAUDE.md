# CLAUDE.md — Persistence

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the approved Persistence module
boundary for spec 011.

## Boundary and exact reference set

Persistence is the modular monolith's infrastructure adapter for the contracts
owned by `Tenancy`. It owns the EF Core model, repository-owned migrations,
firm-scoped review-record storage, durable authentication sessions, identity
directory reads, privileged retention purge, connection-string validation,
opaque identifier generation, development-only synthetic seeding, and its one
registration extension.

Its only production project reference is exactly `Tenancy`. Its package
references are exactly the pinned `Microsoft.EntityFrameworkCore.SqlServer` and
`Microsoft.EntityFrameworkCore.Design` packages. Persistence is the only
production project permitted to reference EF Core.

Persistence does not directly reference `TaxAI.Portal`, `TaxLogicEngine`,
`TaxCases`, `DocumentExtraction`, `CaseProcessing`, or `CaseReview`. It receives
only the already-projected persistence model through Tenancy contracts; it
never receives or maps an executable review-package or tax-domain type.

## What this module owns

- `TaxAiDbContext`, explicit entity configurations, and repository-owned SQL
  Server migrations.
- `SqlReviewRecordRepository`, `SqlAuthenticationSessionStore`,
  `SqlIdentityDirectory`, and `SqlReviewRecordPurgeService`.
- `GuidOpaqueIdentifierSource`, `SqlConnectionStringGuard`, the
  development-only synthetic seeder, and `AddTaxAiPersistence`.

Schema changes come only from migrations. There is no `EnsureCreated`, no
automatic migration at startup, and no background session or retention sweep.

## Tenant isolation

Every taxpayer-data save, read, and purge requires a trusted firm with no
firm-omitting overload. Tenant-owned rows carry a non-nullable `FirmId`, and
tenant lookup indexes and uniqueness constraints lead with it. A foreign-firm
read is indistinguishable from not found. Existing review snapshots are
immutable; a later save creates a new version rather than updating or
overwriting one.

The identity bootstrap is the closed exception: user, membership, and session
lookups operate before a trusted firm is known. These operations are exactly
the exemptions declared by `Tenancy.Models.FirmScopeExemptions`; they carry no
taxpayer content and do not relax any review-record operation.

## PII posture: omission at rest and in diagnostics

No SIN or partial SIN may be stored, logged, serialized, placed in a migration,
or emitted by an error:

- The EF model contains no SIN column, partial-SIN value, SIN-presence flag,
  address, date of birth, email, or phone field.
- Identity and session tables hold only the approved structural identity and
  lifetime fields. They store no taxpayer content, OIDC token, or claims blob.
- Persistence maps only Tenancy's explicit allowlist model. It never maps or
  serializes a `CaseReview`, `CaseProcessing`, `DocumentExtraction`,
  `TaxCases`, or `TaxLogicEngine` type.
- Diagnostics contain fixed structural facts and approved counts only, never a
  taxpayer value, amount, code, session key, connection string, or caller text.

## Retention is a product decision

The seven-year retention default is an engineering product decision, not a CRA
fact or statutory statement. Persistence stores the exclusive
`RetentionUntilUtc` boundary supplied by Tenancy and the privileged purge
deletes only records in the trusted firm whose boundary has been reached.

## No tax-year policy of its own

Persistence declares no supported-year list, current-year concept, tax-year
literal as policy, adjacent-year fallback, or year coercion. It faithfully
stores and queries the explicit tax year on an already-validated Tenancy record.
The supported-year authority remains in `CaseReview`.

## Forbidden dependencies and behavior

Do not add ASP.NET Core, a direct reference to any tax or review module, another
database provider, PostgreSQL, a cloud SDK, OCR, LLM, email, frontend code, a
generic repository, an exposed `DbSet`/`IQueryable` contract, runtime schema
mutation, automatic migration, a background scheduler, tax parsing, a box→line
mapping, CRA line literals, tax calculation, amount rounding, or a second
tax-year authority.

Do not introduce a user-reachable delete or update of a review record. Purge is
an explicit privileged, trusted-firm-scoped operation governed by the exclusive
retention boundary.

## Implementation workflow

Implement only the explicit TaskID named by the human. Read spec 011's PRD,
architecture, and read-only `tasks.md`; require all earlier-wave `.done`
markers; change only task-listed files; ship applicable positive and negative
xUnit tests; run `dotnet build TaxAI.sln`, `dotnet test TaxAI.sln`, and
`pwsh ./scripts/Invoke-SolutionTests.ps1`; create only the task's marker after
the complete gate passes. Never merge or approve your own work.
