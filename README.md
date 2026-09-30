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
  SIN to its last three digits. The newer structured web and relational
  persistence boundary is stricter: no SIN in any form is stored or rendered;
  the encrypted opaque upload exception is described below.
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
  health response plus identity, firm selection, accountant review, and the
  firm-scoped accountant/client intake flow. Accountant requests revalidate
  trusted firm membership; client requests use one-time credentials followed by
  a protected, revocable, path-scoped session cookie. The existing one
  firm-scoped read-only accountant review screen still renders one exact
  immutable package version through its closed allowlist.
- **Blazor Web App** — review and accountant screens remain static SSR. Only the
  client questionnaire's closed-code reveal panel uses Interactive Server;
  detailed circuit errors and WebSocket compression are disabled. Components
  are endpoint-returned, presentation-only, and styled with tracked local CSS.
  There is no global render mode, WebAssembly project, npm tree, or third-party
  asset.
- **Client intake experience** — the `ClientIntake` workflows implement
  server-derived cases, one-time links, revocable sessions, adaptive
  questionnaires, immutable answers and submissions, validated opaque evidence
  uploads, progress/readiness projection, audit, and exact review association.
  Persistence supplies the firm/case-scoped SQL metadata store and one additive
  migration. The portal wires purpose-separated credentials and a
  Development-only authenticated encrypted streaming content store, maps the
  exact accountant and client routes, streams neutral-name multipart uploads,
  and exposes the existing review URL only after verified ready association.

## Deliberately not shipped

- No taxpayer listing or unapproved taxpayer-data surface ships.
- No raw-content preview, read, or download surface ships. The sole
  potentially SIN-bearing exception is the authenticated encrypted opaque
  source object outside web root; it is never rendered, logged, queried, or
  served. All metadata, browser state, URLs, cookies, errors, and diagnostics
  remain SIN-free.
- No client-side workflow, authorization, status, readiness, upload-validation,
  fingerprint, tax, or review authority ships. Interactive Server is confined
  to presentation-only closed-code reveal.
- No LLM adapter ships. Spec 016 T05 adds exactly one production OCR
  adapter: the approved Canada Central Azure AI Document Intelligence
  resource (REST `2024-11-30`, `prebuilt-layout`, `keyValuePairs`), reached
  by managed identity over a private endpoint. It reads only the printed CRA
  T4 (25) slip face in English and French; production medical-receipt
  interpretation is refused pending an approved recognition rule. Its output
  stays untrusted input to the existing parser and engine.
- No production deployment configuration ships. Spec 016 T01 adds only the
  refused production path: a closed runtime-configuration contract, a
  settings-only Bicep module, an offline configuration check, and a startup
  admission gate that no deployable host can pass yet.
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
  TaxAI.AzureInfrastructure/      Production profile validation and identity probe
  TaxAI.Portal/                   ASP.NET Core host and composition root
    Components/                   Static screens and one narrow interactive island
    wwwroot/                      Tracked local styles and upload module
tests/
  TaxLogicEngine.Tests/           xUnit suite
  TaxCases.Tests/                 xUnit suite
  DocumentExtraction.Tests/       xUnit suite
  CaseProcessing.Tests/           xUnit suite
  CaseReview.Tests/               xUnit suite
  ClientIntake.Tests/             xUnit contract, workflow, and boundary suite
  Tenancy.Tests/                  xUnit suite
  Persistence.Tests/              xUnit suite
  TaxAI.AzureInfrastructure.Tests/ xUnit validator, probe, and boundary suite
  TaxAI.Portal.Tests/             xUnit in-process host and boundary suite
infra/azure/                       Runtime-configuration contract and Bicep module
scripts/                           Full-suite execution gate and manifest
  azure/                           Offline production-configuration check
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

Each completed specification records its acceptance evidence under
`specs/NNN-<slug>/verification.md`, naming the tests and commands that prove
each criterion. The client intake and document upload flow is recorded in
`specs/014-client-intake-and-document-upload-flow/verification.md`.

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
tests, every manifest floor, and the approved warning ceiling.

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

Spec 016 T01 replaces that unconditional refusal with an enforced production
admission gate rather than a switch. A deployable host is refused unless it is
explicitly Production, its closed `TaxAi:Production:*` profile describes the
approved Canada Central topology, its real registrations are the approved
production adapters, its managed identity verifies, its artifact version is the
released one, and the evidence manifest approves DEP-01 to DEP-24, both provider
assurances and both roles for that exact configuration. Spec 016 T03–T05 add
the real key protection, source store and reader, and the production Document
Intelligence adapter, but no deployment evidence has been observed and both
provider assurance gaps are unresolved, so every deployable host is still
refused. The offline check validates a settings file without Azure access and
is not deployment evidence:

```powershell
pwsh ./scripts/azure/Test-ProductionConfiguration.ps1 -SettingsPath <app-settings.json>
```

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
