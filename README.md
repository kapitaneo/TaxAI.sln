# TaxAI

TaxAI is the foundation of a Canadian tax filing assistant, built as a
structured modular monolith. Its core is **TaxLogicEngine**, a .NET 8.0 class
library of deterministic, provider-independent Canadian tax logic.

This README describes only what is implemented today. The forward roadmap lives
in [NEXT.md](NEXT.md); repository and agent policy live in
[CLAUDE.md](CLAUDE.md) and [AGENTS.md](AGENTS.md).

## Implemented capabilities

- **T5 and T4 slip parsing** — validation and parsing of raw input into typed
  slips.
- **Data-driven box → CRA line mapping** — T4 and T5 line entries come from the
  embedded, tax-year-scoped JSON mapping configurations rather than hardcoded C#
  line numbers.
- **Currency rounding** — decimal-only tax math, rounded half-up to cents through
  the engine's `MoneyRounding`.
- **Engine SIN masking** — the existing in-memory `RecipientIdentifier` masks a
  SIN to its last three digits. The newer web and persistence boundary is
  stricter: no SIN in any form is stored or rendered.
- **Tax case core** — an immutable engagement aggregate with
  firm/year/participant isolation, participant-scoped evidence and intake,
  derived readiness, and deterministic change records.
- **Document extraction contract** — the deterministic, provider-independent
  boundary for future extraction adapters. Extraction output is untrusted input,
  never tax truth. No adapter implementation ships.
- **T4 case processing** — a deterministic coordinator that drives one validated
  extraction through the parser and mapper without inventing tax truth.
- **Accountant review package** — an immutable review boundary with ordered
  engine-produced values, one human decision, and fail-closed approval when
  unmapped codes have not been acknowledged.
- **OIDC sign-in** — the portal delegates authentication to an external provider
  and stores neither passwords nor provider tokens.
- **Revocable sessions** — the browser cookie carries only a protected opaque
  key; the durable database session is authoritative for identity, lifetime,
  active firm, and server-side revocation.
- **Tenancy and firm scoping** — protected requests revalidate the local user and
  active membership. Firm selection is explicit, revokes the old session, and
  issues a new one. A provider-supplied firm claim is never trusted.
- **Database persistence** — EF Core SQL Server configurations and
  repository-owned migrations persist immutable, versioned review-record
  snapshots behind trusted-firm-scoped contracts. No SIN or partial SIN is part
  of that model.
- **Retention and purge** — persisted review records carry an exclusive UTC
  retention boundary, and a privileged firm-scoped service purges eligible
  records. The seven-year default is an engineering product decision, not a CRA
  fact or statutory claim.
- **Portal host** — the ASP.NET Core host serves the unchanged public shell and
  health response plus sign-in, sign-out, and authenticated firm selection. It
  renders no taxpayer data.

## Deliberately not shipped

- No taxpayer-data screen ships.
- No document upload ships.
- No OCR or LLM adapter ships.
- No production deployment configuration ships.
- No production provisioning ships for a firm, user, membership, or
  administrator.
- No taxpayer-value editing, approval screen, export, T1 calculation, or
  NETFILE/EFILE capability ships.

The persistence layer and migrations are implemented, but no database instance
or automatic migration is bundled. Spec 011 deliberately ships a fail-closed
startup guard instead of the production Azure Data Protection provider. As
shipped, the portal starts in Development only and may not be deployed anywhere
that can receive real taxpayer data until a separate deployment specification
supplies and verifies the Canadian Azure key ring, hosting, TLS, residency,
secrets, operations, and provisioning posture.

## Project structure

```plaintext
TaxAI.sln                         Solution
src/
  TaxLogicEngine/                 Deterministic tax engine
    Models/                       Slips, raw inputs, results, and line maps
    Services/                     Parsers, mappers, providers, and rounding
    Mappings/                     JSON box-to-line configurations
  TaxCases/                       Tax case engagement aggregate
  DocumentExtraction/             Untrusted-extraction contract
  CaseProcessing/                 Deterministic T4 processing coordinator
  CaseReview/                     Accountant review package and sign-off
  Tenancy/                        Trusted-firm, session, projection, retention ports
  Persistence/                    EF Core model, migrations, and SQL adapters
  TaxAI.Portal/                   ASP.NET Core host and composition root
tests/
  TaxLogicEngine.Tests/           xUnit suite
  TaxCases.Tests/                 xUnit suite
  DocumentExtraction.Tests/       xUnit suite
  CaseProcessing.Tests/           xUnit suite
  CaseReview.Tests/               xUnit suite
  Tenancy.Tests/                  xUnit suite
  Persistence.Tests/              xUnit suite
  TaxAI.Portal.Tests/             xUnit in-process host and boundary suite
scripts/                           Full-suite execution gate and manifest
specs/                             PRDs, architecture, task plans, and status
```

Each production module carries a local `CLAUDE.md` defining its boundary.

## Build and test

```bash
# Build
dotnet build TaxAI.sln

# Run the full suite through the execution gate (required for task completion)
pwsh ./scripts/Invoke-SolutionTests.ps1

# Run all tests directly
dotnet test TaxAI.sln

# Run a single test
dotnet test tests/TaxLogicEngine.Tests --filter "FullyQualifiedName~TestMethodName"
```

A green direct `dotnet test` result is not by itself proof that every assembly
ran. The PowerShell gate verifies that each project listed in
`scripts/test-projects.json` ran, skipped nothing, failed nothing, and met its
recorded minimum test floor.

## Run the application in Development

Configure an external Development SQL Server connection named
`ConnectionStrings:TaxAiDatabase`, apply the repository-owned migration, and
configure the external OIDC settings through environment variables or user
secrets. No connection string, client secret, key, or credential belongs in a
tracked file.

```bash
dotnet run --project src/TaxAI.Portal
```

The launch profile binds to localhost. Development uses a developer-local Data
Protection key ring excluded from source control and writes only the approved
synthetic fixture. The host does not migrate the schema automatically.
