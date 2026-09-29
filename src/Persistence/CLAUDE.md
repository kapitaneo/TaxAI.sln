# CLAUDE.md — Persistence

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the approved Persistence module
boundary for spec 011.

## Boundary and exact reference set

Persistence is the modular monolith's infrastructure adapter for the contracts
owned by `Tenancy` and `ClientIntake`. It owns the EF Core model,
repository-owned migrations, firm-scoped review-record and intake-metadata
storage, durable authentication sessions, identity directory reads, privileged
retention purge, connection-string validation, opaque identifier generation,
development-only synthetic seeding, and its one registration extension.

Its production project references are exactly `ClientIntake`, `TaxCases`, and
`Tenancy`. The explicit `TaxCases` reference supplies only the existing
category/lifecycle enum types used by intake storage records; Persistence never
constructs, invokes, or maps `TaxCase`. Its package references are exactly the
pinned `Microsoft.EntityFrameworkCore.SqlServer` and
`Microsoft.EntityFrameworkCore.Design` packages. Persistence is the only
production project permitted to reference EF Core.

Persistence does not directly reference `TaxAI.Portal`, `TaxLogicEngine`,
`DocumentExtraction`, `CaseProcessing`, or `CaseReview`. It receives only
explicit storage records through inward contracts; it never receives or maps an
executable review package or `TaxCase`.

## What this module owns

- `TaxAiDbContext`, explicit entity configurations, and repository-owned SQL
  Server migrations.
- `SqlReviewRecordRepository`, `SqlAuthenticationSessionStore`,
  `SqlIdentityDirectory`, `SqlReviewRecordPurgeService`, and `SqlIntakeStore`.
- `GuidOpaqueIdentifierSource`, `SqlConnectionStringGuard`, the
  development-only synthetic seeder, and `AddTaxAiPersistence`.

Spec 016 T03 tightens `SqlConnectionStringGuard` without changing its
signature: transport must be explicitly encrypted with the server certificate
validated against the server name — no `TrustServerCertificate` bypass and no
`HostNameInCertificate` override — and `Persist Security Info` is refused.
Among Microsoft Entra modes only the platform managed identity is accepted, and
never with a password or integrated security; a mode that stores a secret,
prompts a person or walks a credential chain is refused. SQL and Windows
authentication remain available to Development and the in-process verification
hosts; the production profile validator independently requires the managed
identity with strict encryption. Customer-managed TDE, Entra-only
authentication and the private endpoint are infrastructure
(`infra/azure/modules/sql.bicep`), not code here: this module still takes no
cloud SDK, credential or key dependency.

Spec 016 T04 adds the source registry: `SourceRegistrationEntity`, its one
configuration, `SqlSourceRegistryStore` (the `ISourceRegistryStore`
implementation, registered by `AddTaxAiPersistence` with an unchanged
signature) and the additive migration `20260927000000_AddSourceRegistry`,
which creates only the firm-scoped `SourceRegistrations` table. Every key and
index leads with the firm; the row references its intake case and, once bound,
its committed intake document. `SqlIntakeStore` binds a staged registration
to the accepted document inside the same serializable upload transaction, and
refuses the whole commit when compensating cleanup already took the object.
State changes are single conditional updates, so cleanup and a racing commit
cannot both win. The table holds no source byte, key material, file name,
provider identifier or taxpayer value, and no row is deleted here.

Migration `20260730000000_AddClientIntake` is additive: it creates only the
twelve intake metadata tables. It does not alter the initial migration or any
existing firm, identity, session, or review table. Source content remains
outside SQL behind the write-only content-store contract.

Schema changes come only from migrations. There is no `EnsureCreated`, no
automatic migration at startup, and no background session or retention sweep.

## Tenant isolation

Every taxpayer-data save, read, and purge requires a trusted firm with no
firm-omitting overload. The only additional bootstrap exemptions are
`FindLinkScopeAsync` and `FindClientScopeAsync`: each accepts only a keyed digest
and returns trusted scope. All later client reads/writes revalidate the same live
session in the operation. Tenant-owned rows carry a non-nullable `FirmId`, and
tenant lookup indexes, uniqueness constraints, and required relationships are
firm/case scoped. A foreign-firm read is indistinguishable from not found.
Existing review snapshots and intake facts are immutable; later state is
represented by a new version or append.

Identity is the other closed bootstrap family: user, membership, and
authentication-session lookups operate before a trusted firm is known. These
operations are exactly the exemptions declared by
`Tenancy.Models.FirmScopeExemptions`; they carry no taxpayer content and do not
relax any review-record or intake operation.

## PII posture: omission at rest and in diagnostics

No SIN or partial SIN may be stored, logged, serialized, placed in a migration,
or emitted by an error:

- The EF model contains no SIN column, partial-SIN value, SIN-presence flag,
  address, date of birth, email, or phone field.
- Identity and session tables hold only the approved structural identity and
  lifetime fields. They store no taxpayer content, OIDC token, or claims blob.
- Persistence maps only explicit Tenancy and ClientIntake storage records. It
  never maps or serializes a `CaseReview`, `CaseProcessing`,
  `DocumentExtraction`, `TaxCase`, or `TaxLogicEngine` type.
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
stores and queries the explicit tax year on already-validated Tenancy and intake
records. The supported-year authorities remain in `CaseReview` and the
`ClientIntake` workflow that constructs `TaxCase`.

## Forbidden dependencies and behavior

Do not add ASP.NET Core, another tax or review module reference, another
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
