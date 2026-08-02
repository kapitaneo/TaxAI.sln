# TaxAI

TaxAI is the foundation of a Canadian tax filing assistant, built as a
structured modular monolith. Its core is **TaxLogicEngine**, a .NET 10.0 class
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
  snapshots and append-only intake metadata behind trusted-firm-scoped
  contracts. No SIN or partial SIN is part of that model.
- **Retention and purge** — persisted review records carry an exclusive UTC
  retention boundary, and a privileged firm-scoped service purges eligible
  records. The seven-year default is an engineering product decision, not a CRA
  fact or statutory claim.
- **Portal host** — the ASP.NET Core host serves a fixed public shell and
  health response plus sign-in, sign-out, authenticated firm selection, and one
  firm-scoped read-only accountant review screen. The screen renders one exact
  immutable package version using a closed allowlist and carries no SIN in any
  form.
- **Blazor Web App foundation (static server rendering only)** — the review
  screen is a statically server-rendered Razor component returned by its own
  authorization-gated endpoint, styled from tracked local and component-scoped
  CSS. Interactive Server is not registered, so no SignalR circuit endpoint
  exists; there is no client script, WebAssembly project, npm tree, or
  third-party asset.
- **Client intake backend** — the `ClientIntake` workflows implement
  server-derived cases, one-time links, revocable sessions, adaptive
  questionnaires, immutable answers and submissions, validated opaque evidence
  uploads, progress/readiness projection, audit, and exact review association.
  Persistence supplies the firm/case-scoped SQL metadata store and one additive
  migration. The portal wires purpose-separated credentials and a
  Development-only authenticated encrypted streaming content store without
  exposing an intake route.

## Deliberately not shipped

- No second taxpayer-data screen or taxpayer listing ships.
- No client or accountant intake route, upload transport, intake screen, or
  raw-content read/download surface ships. The implemented backend is available
  only through in-process contracts; spec 014 T03 owns the HTTP and component
  surface.
- No interactive Blazor component ships, and Interactive Server is not registered
  at all — so the host exposes no SignalR circuit endpoint and loads no client
  script. Spec 014 T03 introduces both, with the first island that needs them.
- No OCR or LLM adapter ships.
- No production deployment configuration ships.
- No production provisioning ships for a firm, user, membership, or
  administrator.
- No taxpayer-value editing, review decision/approval screen, export, T1 calculation, or
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
  ClientIntake/                   Deterministic intake application workflows
  Tenancy/                        Trusted-firm, session, projection, retention ports
  Persistence/                    EF Core model, migrations, and SQL adapters
  TaxAI.Portal/                   ASP.NET Core host and composition root
    Components/                   Blazor Web App foundation and the review screen
    wwwroot/                      Tracked local stylesheet
tests/
  TaxLogicEngine.Tests/           xUnit suite
  TaxCases.Tests/                 xUnit suite
  DocumentExtraction.Tests/       xUnit suite
  CaseProcessing.Tests/           xUnit suite
  CaseReview.Tests/               xUnit suite
  ClientIntake.Tests/             xUnit contract, workflow, and boundary suite
  Tenancy.Tests/                  xUnit suite
  Persistence.Tests/              xUnit suite
  TaxAI.Portal.Tests/             xUnit in-process host and boundary suite
scripts/                           Full-suite execution gate and manifest
specs/                             PRDs, architecture, task plans, and status
```

Each production module carries a local `CLAUDE.md` defining its boundary.

## Build and test

Install the .NET 10 SDK and run commands from the repository root. `global.json`
requires SDK `10.0.302` exactly; the local tool manifest requires
`dotnet-ef` `10.0.10`.

```powershell
dotnet tool restore
dotnet --version
dotnet ef --version
dotnet restore TaxAI.sln
dotnet build TaxAI.sln
dotnet test TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1
dotnet publish src/TaxAI.Portal/TaxAI.Portal.csproj -c Release
git diff --check
```

A green direct `dotnet test` result is not by itself proof that every assembly
ran. The PowerShell gate verifies that each project listed in
`scripts/test-projects.json` ran, skipped nothing, failed nothing, and met its
recorded minimum test floor. It first runs the .NET 10 preflight, which verifies
the exact SDK, every solution TFM, the direct package matrix, and local EF tool
manifest.

To verify the EF model without connecting to a database, supply a process-only
synthetic SQL Server connection string that requires encryption and certificate
validation, then run:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__TaxAiDatabase = '<synthetic encrypted SQL Server connection string>'
dotnet ef migrations has-pending-model-changes `
  --project src/Persistence/Persistence.csproj `
  --startup-project src/TaxAI.Portal/TaxAI.Portal.csproj
Remove-Item Env:ConnectionStrings__TaxAiDatabase
Remove-Item Env:ASPNETCORE_ENVIRONMENT
```

The expected result is `No changes have been made to the model since the last
migration.` This command must not connect to, create, update, or migrate a
database.

## Future CI gate

No CI/CD definition is currently tracked. Any future pipeline must install SDK
`10.0.302` and run the commands above in the same order: tool restore, SDK/tool
version checks, solution restore, build, raw tests, the PowerShell execution
gate, pending-model verification with a non-production synthetic secret,
Release publish, and `git diff --check`. It must preserve zero failed/skipped
tests, all eight manifest floors, and the approved warning ceiling.

## Azure deployment remains blocked

The Release publish proves buildability; it is not deployment authorization.
Before any future code-based Azure App Service deployment, a separate approved
deployment specification must verify that `.NET 10 (LTS)` is available for the
chosen App Service OS/runtime and that every service and backup remains in
Canada Central or Canada East. It must also supply TLS/exposure controls,
managed identity and Key Vault secrets, a Canadian Azure Storage/Key Vault Data
Protection key ring, operations, and production provisioning.

Until then, the existing non-Development startup refusal remains authoritative,
and this application may not receive real taxpayer data in production.

## Roll back the platform upgrade

The last verified .NET 8 source baseline is
`13097dada6b1aae070fc0006d48a6862ce9904f8`. A rollback is a human-controlled
source/application operation:

1. create a recovery branch or release from that commit;
2. restore the .NET 8 SDK/runtime and the tool/package versions recorded under
   `specs/013-dotnet-10-platform-upgrade/baselines/`;
3. rebuild and run the baseline gates;
4. for a future deployment, swap back to the last verified .NET 8 App Service
   slot or revision only after its .NET 8 runtime is available.

Do not roll back, recreate, or migrate the database. Spec 013 changes no schema,
migration history, model snapshot, production mapping, or stored data.

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
