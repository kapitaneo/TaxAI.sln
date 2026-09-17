# CLAUDE.md

This file provides repository-level guidance to Claude Code (claude.ai/code) when working in this repository. Project-specific rules for the tax engine live in [src/TaxLogicEngine/CLAUDE.md](src/TaxLogicEngine/CLAUDE.md) — read that file whenever you touch TaxLogicEngine code.

## Product framing

TaxAI is an AI-powered Canadian tax filing assistant. Its deterministic core is
**TaxLogicEngine**, a .NET 10.0 class library that implements CRA slip parsing
(T4, T5) and box→CRA-line mapping via JSON config. Deterministic case,
extraction, processing, review, and tenancy modules surround that core;
infrastructure adapters live separately in `Persistence`, and the portal is the
single host.

Real people file real returns from this output. A wrong box→line mapping is a filing error with financial and legal consequences for the taxpayer.

## The modular monolith decision

TaxAI is, by decision, a **structured modular monolith**: one repository, one .NET solution, one initially-deployable application, in-process module communication, with explicit module ownership and dependency boundaries, and module extraction deferred until demonstrated need.

The future-module *pattern* is documented here; it is **not scaffolded**. Do not create empty projects for future modules (Clients, Firms, Documents, Intake, Review, TaxCases) — a real module's boundary is discovered from its contract, and empty shells acquire gravity.

## Build and Test Commands

```bash
# Build
dotnet build TaxAI.sln

# Run all tests — the gate. Use this to satisfy any "full suite must pass" rule.
pwsh ./scripts/Invoke-SolutionTests.ps1

# Run all tests (raw; see the warning below)
dotnet test TaxAI.sln

# Run a single test
dotnet test tests/TaxLogicEngine.Tests --filter "FullyQualifiedName~TestMethodName"
```

**A green `dotnet test TaxAI.sln` is not proof that the suite ran.** It returns
exit code 0 even when an entire test assembly is skipped — during spec 010 T02 a
Windows Application Control policy blocked `TaxCases.Tests.dll`, 337 tests
silently did not run, and the command still reported success.

`scripts/Invoke-SolutionTests.ps1` closes that hole: it runs the suite with a TRX
logger and then fails unless **every** project in `scripts/test-projects.json`
actually ran, reported no failures, skipped nothing, and met its recorded minimum
count. Every workflow rule in this file that says "the full suite must pass"
means this script, not the raw command.

`minimumTests` in that manifest is a **floor**, not an expected value. Raise it
when a spec adds tests; **never lower it to make a run pass** — a falling count
is the defect the gate exists to catch. A new test project must be registered in
the same PR that introduces it, or the gate fails on it by design.

## Repository layout

- **`src/TaxLogicEngine/`** — the production tax engine (net10.0, nullable enabled). Has its own `CLAUDE.md` with the tax-domain rules and type inventory.
- **`src/TaxCases/`** — the implemented deterministic Tax Case Core module
  (net10.0, nullable enabled). Its local `CLAUDE.md` owns the approved module
  boundary and public-contract inventory.
- **`src/DocumentExtraction/`** — the deterministic, provider-independent
  ingestion contract between future OCR/LLM adapters and the tax domain.
- **`src/CaseProcessing/`** — the deterministic T4 case processor.
- **`src/CaseReview/`** — the deterministic accountant review package.
- **`src/ClientIntake/`** — the deterministic application-coordination module
  for the spec 014 client intake and document-upload workflow: closed intake
  vocabularies, accountant and client commands and safe projections, implemented
  workflows, and declared persistence, content-store, and credential-source
  ports. Its production references remain exactly `TaxCases`, `Tenancy`, and
  `TaxLogicEngine`, and it declares no package. Spec 014 T02 implements the
  in-process backend without an HTTP surface. Its local `CLAUDE.md` owns the
  boundary, token/scope rules, and no-SIN-by-omission rule.
- **`src/Tenancy/`** — deterministic, provider-independent trusted-firm,
  persisted read-model, identity/session, projection, retention, purge-port, and
  validation contracts. Its production references are exactly `CaseReview` and
  `TaxLogicEngine`; its local `CLAUDE.md` owns the boundary and the no-SIN-at-
  persistence rule.
- **`src/ExtractionWorkflow/`** — the deterministic coordination module for
  spec 015 document extraction: the single `IDocumentExtractionIngress`
  implementation, the closed classification/correction/acceptance vocabularies,
  the neutral medical evidence model, the append-only `IExtractionStore`
  contract, the four-part-scoped `IExtractionContentReader` port, the
  `IDocumentIntelligenceProvider` port, and the ingest and review workflows. Its
  references are exactly `CaseProcessing`, `CaseReview`, `ClientIntake`,
  `DocumentExtraction`, `TaxLogicEngine`, and `Tenancy`, and it declares no
  package.
- **`src/DocumentIntelligence/`** — the provider boundary, and the only project
  that would ever be permitted an OCR, LLM, document-intelligence, or cloud SDK.
  **It declares none.** It holds `DeterministicDocumentIntelligenceProvider`, an
  in-process adapter registered for Development and test hosts only, and one
  registration extension. Its one package is the DI abstraction.
- **`src/Persistence/`** — the infrastructure adapter and the repository's only
  EF Core project. Its project references are exactly `ClientIntake`,
  `ExtractionWorkflow`, `TaxCases`, and `Tenancy`; it owns the SQL Server EF
  model, repository-owned migrations, repositories, the firm/case-scoped intake
  metadata store, the extraction store, the durable session store, identity
  directory, purge implementation, and one registration extension.
- **`src/TaxAI.Portal/`** — the ASP.NET Core host: the repository's only
  executable project, and its single dependency-injection composition root. Its
  production project references are exactly `TaxLogicEngine`, `CaseProcessing`,
  `ClientIntake`, `DocumentIntelligence`, `ExtractionWorkflow`, `Tenancy`, and
  `Persistence`. Its local `CLAUDE.md` owns the
  approved read-only review surface, the accountant/client intake surface, and
  the narrowed gate on every further taxpayer-data surface. **ASP.NET Core lives
  here and in its test project, and nowhere else.**
- **`tests/TaxLogicEngine.Tests/`** — xUnit test project referencing the engine. Uses xunit 2.5.3, coverlet for coverage.
- **`tests/TaxCases.Tests/`** — xUnit contract, model, service, boundary, and
  end-to-end verification for Tax Case Core.
- **`tests/DocumentExtraction.Tests/`**, **`tests/CaseProcessing.Tests/`**,
  **`tests/CaseReview.Tests/`** — the matching suites for those modules.
- **`tests/ClientIntake.Tests/`** — contract, JSON-omission, workflow, upload,
  projection, and boundary verification for the intake backend.
- **`tests/Tenancy.Tests/`**, **`tests/Persistence.Tests/`** — contract,
  boundary, model, service, relational, schema, and composition coverage for
  the two spec 011 modules.
- **`tests/ExtractionWorkflow.Tests/`**, **`tests/DocumentIntelligence.Tests/`**
  — ingest, review, duplicate, correction, medical, PII, boundary, fail-closed,
  and no-egress coverage for the two spec 015 modules.
- **`tests/TaxAI.Portal.Tests/`** — xUnit host, composition-root, boundary,
  security, and repository-verification suites for the portal. Adds
  `Microsoft.AspNetCore.Mvc.Testing` for in-process host testing; no network
  port is bound.
- **`specs/NNN-<slug>/`** — spec artifacts (`prd.md`, `architecture.md`, `tasks.md`, `status/`).
- **`.claude/`** — commands, agents, settings for the feature workflow.

Future modules get `src/<Module>/` homes when they are actually built.

## Module boundary and dependency rules (bind every current and future module)

- **TaxLogicEngine is deterministic, provider-independent tax logic.** No I/O that makes a tax result non-deterministic belongs in it.
- **Modules communicate only through declared contracts.** No module reaches into another module's internal implementation.
- **No module reads another module's database tables.**
- **UI must not compute tax or implement CRA mappings.**
- **OCR and LLM output are untrusted input** — input to be validated, never a source of tax truth.
- **Dependencies point inward.** Infrastructure (EF, OCR, LLM, email, cloud) may depend on application/domain contracts; never the reverse:

```
UI / API host ──► application/module contracts ──► TaxLogicEngine (domain)
Infrastructure (EF, OCR, LLM, email, cloud) ──► application/domain contracts
```

- **Tax-domain code must not depend on** ASP.NET Core, EF Core, SQL, cloud SDKs, OCR providers, LLM providers, email providers, or frontend frameworks.

These rules are both documented invariants and compiled project boundaries.
Adding a new module never relaxes them.

## Global security / PII rules

- **Never log or echo a full SIN — anywhere in any module.** The portal and the
  persisted read model are stricter: **no SIN in any form**, including a masked
  or partial value, may reach a browser, URL, cookie, log, error, metric,
  fixture, or database. `TaxLogicEngine`'s existing in-memory
  `RecipientIdentifier` masking contract remains unchanged; its local
  `CLAUDE.md` owns that implementation boundary.
- **Tax facts come from canada.ca only.** Not from a blog, not from a competitor's site, not from memory, and not from a prior PRD that lacks a citation. An unverified tax fact never enters a spec or the code.
- No secrets, `*.user` files, or `.claude/settings.local.json` may become tracked.

## Feature development workflow

Features are built through a slash-command pipeline (`.claude/commands/`). Each stage stops for human review before the next. Spec artifacts live under `specs/NNN-<slug>/`. Find the next spec number from existing folders there.

1. **`/spec <feature>`** — delegates to the **business-analyst** subagent, which verifies CRA facts against canada.ca and writes `specs/NNN-<slug>/prd.md`. No code.

2. **`/plan <feature>`** — acting as architect, reads the PRD and writes `architecture.md` (design, data model, trade-offs, risks) plus `tasks.md`. No production code.

3. **`/implement <spec> <TaskID>`** — acting as senior dev, implements ONE explicitly named task and its tests in the same pass, then stops. The task ID is always given explicitly; never pick one yourself.

4. **`reviewer` subagent** — after a PR is open, mechanically verifies it against the domain rules above and the acceptance criteria the task claimed. Read-only. It prepares a human's review; it does not perform one, and it never merges.

### Ambiguity: two classes, handled oppositely

- **Class A — domain, CRA, tax, or PII rules.** Never assume. Stop and ask, however many questions it takes.
- **Class B — everything else** (scope, naming, ordering, v1 vs v2). Never ask. Decide, document the decision, tag it `[ASSUMED]` for one-pass human override.

If you are unsure which class something is, it is Class A. No request for speed or brevity moves an item from A to B.

### tasks.md is organized into waves

Wave 0 is always the **contract wave**: one task declaring every public signature (types, interfaces, method names, JSON schema shape) with `NotImplementedException` bodies. Everything downstream codes against the contract — that is what makes later waves parallel.

Within a wave, tasks are independently implementable **in parallel** by separate agents that cannot see each other. Each wave merges to `master` before the next begins. Within a wave:

- No task references a type or file *created* by a sibling task.
- **No two tasks list the same file path.** This is the rule that prevents merge conflicts.
- Each task's tests pass against `master` + W0 + that task alone.

Parallel tasks are run in separate git worktrees, one Claude Code session each:

```bash
git worktree add ../TaxAI-w1t01 -b feat/002-w1-t01 master
```

Parallelism widens how many tasks run at once. It must never widen what a single task contains. One task, one branch, one PR, one review.

### Task status: marker files, not checkboxes

**`tasks.md` is a read-only plan. Never edit it. Never check a box in it.**

Completion is recorded by creating `specs/NNN-<slug>/status/<TaskID>.done`, containing the PR number and commit SHA. Each agent writes only its own marker, so parallel branches never conflict on a shared status file.

A task is unblocked when every task in every earlier wave has a `.done` marker.

### Never automated

- **Merging.** `git merge` and `gh pr merge` always require a human. Merge is the moment tax logic enters `master`.
- **Opening a non-draft PR with unverified tests.** If tests cannot run, open the PR as a draft and say so in the title.
- **Approving your own work.** No agent merges its own PR.

## Current state

Spec 001 (T5 slip parsing), spec 002 (T5 box→CRA line mapping), spec 003 (T4 slip parsing), and spec 004 (T4 box→CRA line mapping) are implemented and merged. Spec 005 (modular monolith foundation) is complete: the solution is `TaxAI.sln`, projects live under `src/` and `tests/`, instructions are scope-split (this file + `src/TaxLogicEngine/CLAUDE.md`), the `AGENTS.md` bridges exist, and the migration's AC-by-AC verification is recorded in `specs/005-modular-monolith-foundation/verification.md`.

Spec 006 (Tax Case Core) is complete. `src/TaxCases/` implements the immutable,
provider-independent 2025 engagement aggregate with firm/year/participant
isolation, participant-scoped evidence and intake, derived readiness, lifecycle
guards, deterministic change records, and SIN-safe diagnostics. AC-1 through
AC-43 and the module/repository boundaries are recorded in
`specs/006-tax-case-core/verification.md`.

Spec 007 (Document Extraction Contract) is complete. `src/DocumentExtraction/`
implements the deterministic, provider-independent ingestion contract between
future OCR/LLM adapters and the tax domain: an untrusted `ExtractionEnvelope`,
the internal-only `ValidatedExtraction`, the closed `ExtractionFields` set, the
signature-only `IDocumentExtractionIngress` port (**no implementation ships in
spec 007**), and `ExtractionEnvelopeValidator.Validate` — the single fail-closed
gate doing attribution **presence** checks only, with 2025-only year isolation.
The raw `T4RawInput` payload passes through reference-identical and
value-unchanged, so extraction output never becomes tax truth, and PII is
handled by **omission, not masking**. AC-1 through AC-30 are recorded in
`specs/007-document-extraction-contract/verification.md`.

Spec 008 (T4 Case Processor) is complete. `src/CaseProcessing/` implements the
deterministic coordinator that drives one already-validated T4 extraction
through the existing engine: `T4CaseProcessor.Process` runs eight fixed stages
(null extraction; kind/payload; supported year and payload structure, which
accumulate together; parse; slip/extraction year coherence; map; construct) and
returns `ParseResult<ProcessedT4Extraction>`. The internal
`PayloadStructureGuard` closes the seam where a payload that cleared spec 007's
gate could still make `T4SlipParser.Parse` throw. **No tax truth is invented
here** — the slip comes only from `T4SlipParser`, the CRA line entries only from
`T4LineMapper` plus the injected `IT4LineMapProvider`, engine errors are
surfaced verbatim and in order, and the module contains no CRA line number, no
box→line table, no arithmetic, and no `decimal` token. The provider is asked for
exactly the slip's year, once, with no adjacent-year retry. PII crosses the
**omission → masking** seam: the raw `T4RawInput` never reaches the result, and
the parsed Box 12 exists only as the engine's masked `RecipientIdentifier` on
`T4Slip.Sin`. AC-1 through AC-43 are recorded in
`specs/008-t4-case-processor/verification.md`.

Spec 009 (Accountant Review Package) is complete. `src/CaseReview/` implements
the deterministic 2025 review boundary after CaseProcessing: it assembles one
immutable package from one or more successful `ProcessedT4Extraction` values,
preserving the exact parsed-slip reference and ordered snapshots of
engine-produced line entries, provenance, populated amounts, and unmapped
codes. `AccountantReviewPackage.RecordDecision` records exactly one immutable
human decision on a replacement package. Approval fails closed when any
reviewed slip has an unmapped code unless the reviewer supplies one affirmative
package-level acknowledgement; there is no per-slip/per-code disposition or
mapped-amount dispute mechanism. The module invokes no parser, mapper, mapping
provider, mapping resource, arithmetic, or rounding. PII is protected by the
engine's existing masked `RecipientIdentifier` plus omission of caller strings
and reviewed content from defensive output. AC-1 through AC-54 are recorded in
`specs/009-accountant-review-package/verification.md`.

Spec 010 (Portal Foundation) is complete. `src/TaxAI.Portal/` is the
repository's first runnable application. At the spec 010 boundary it was an
ASP.NET Core host whose single
composition root (`ModuleRegistration.AddTaxAiModules`) registers
`IT4LineMapProvider`, `IT5LineMapProvider`, and `T4CaseProcessor` as singletons
and nothing else, with the service provider validated on build and with scope
validation forced on unconditionally — `WebApplication.CreateBuilder` enables
those only in Development, so a config-free host would ship to Production
unvalidated. It exposes exactly two GET routes, `/health` returning the literal
`Healthy` and `/` returning a fixed shell document, both compile-time constants;
neither accepts a body, route value, query value, form value, header, or cookie.
**No endpoint resolves or invokes `T4CaseProcessor`: the portal runs no tax
logic.** It contains no CRA line number, box code, box→line table, tax-year
literal, taxpayer type, SIN, masking helper, or arithmetic token, and its PII
posture is **omission, not masking — the data never enters the process.**
Production references are exactly `TaxLogicEngine` and `CaseProcessing`;
`CaseReview` is deliberately not referenced, because an
`AccountantReviewPackage` is taxpayer data. AC-1 through AC-50 are recorded in
`specs/010-portal-foundation/verification.md`.

Spec 011 (Tenant Identity and Persistence Foundation) has delivered the
repository's trusted identity, tenancy, persistence, retention, and revocation
foundation:

- `Tenancy` owns the deterministic trusted-firm boundary, the explicit
  persisted review model and allowlist projection, identity/session models,
  repository and purge ports, membership verification, session lifetimes, and
  the exclusive retention calculation. Its references are exactly
  `CaseReview` and `TaxLogicEngine`.
- `Persistence` is the only EF Core project. It references exactly `Tenancy`
  and implements the SQL Server model, repository-owned migration,
  firm-scoped review repository, durable shared session store, identity reads,
  retention purge, connection guard, and development-only synthetic seeding.
- The portal now delegates sign-in to OIDC, carries only a protected opaque
  ticket key in the browser, rebuilds identity and the trusted
  `taxai_firm_id` claim from the durable session, revalidates active membership
  on protected requests, supports explicit firm selection, and revokes sessions
  server-side on sign-out.
- The portal's production project references are exactly `TaxLogicEngine`,
  `CaseProcessing`, `Tenancy`, and `Persistence`; it still directly references
  neither `TaxCases`, `DocumentExtraction`, nor `CaseReview`.
- The persistence and web boundary reverses the earlier portal masking
  possibility: **no SIN in any form** is stored or rendered. The engine's
  existing in-memory `RecipientIdentifier` masking remains untouched.
- The seven-year retention default is an engineering product decision, not a
  CRA fact or statutory minimum. The stored boundary is exclusive and purge is
  explicit and trusted-firm-scoped.

Spec 011 answers spec 010 questions A (authentication and authorization), B
(tenant/firm isolation), C for SIN only (nothing may be rendered in any form),
D (fail-closed diagnostics and logging prohibition), and E (required Canadian
residency and encryption posture). Question F — whether this host exposes the
untrusted document-extraction ingress — remains reserved and the ingress remains
unimplemented.

**The portal is still a foundation, not a taxpayer-data product surface.** Its
gate is narrowed, never deleted. Rendering taxpayer data, uploading a document,
mutating or deleting a stored package, provisioning a firm or membership, or
adding a second taxpayer-data surface requires a further approved
`tax-domain` specification. The UI computes no tax, implements no CRA mapping,
and holds no tax-year policy.

**Deployment remains fail-closed.** Spec 011 ships no production Azure Data
Protection provider. As shipped, this build starts in Development only and may
not be deployed anywhere that can receive real taxpayer data until a deployment
specification supplies and verifies the Canadian Azure Storage and Key Vault key
ring plus the hosting, TLS, exposure, residency, secrets, operations, and
provisioning posture.

Spec 012 (Accountant Review Screen) is complete. The portal now exposes exactly
one read-only, firm-scoped route for one immutable persisted review-package
version:

- the active firm comes only from the validated server-side session after
  active-user and active-membership checks;
- missing and foreign-firm record selectors return the same empty `404`;
- the server-rendered browser projection contains only tax year, package
  version, slip type, taxpayer and employer display names, boxes and values, tax
  lines, source-box references, and unmapped codes;
- taxpayer and employer display names are approved display PII, while SIN in
  every form, broader contact/account/provider identity data, tenant/session
  identifiers, persistence identifiers, and non-display timestamps remain
  excluded;
- the route performs no mutation, parsing, mapping, tax calculation, arithmetic,
  rounding, “latest” selection, or client-side data fetch.

The gate remains in force for upload, editing, decisions, export, listing,
provisioning, browser-allowlist expansion, and every second taxpayer-data
surface. The existing deployment refusal is unchanged.

Spec 013 (.NET 10 Platform Upgrade) is complete. Every production and test
project in `TaxAI.sln` targets exactly `net10.0`; `global.json` pins SDK
`10.0.302`; the repository-local `dotnet-ef`, ASP.NET Core, and EF Core packages
are aligned to `10.0.10`; and every test project retains the approved xunit v2
package matrix. The executable repository preflight, all eight test projects,
EF model/schema/DDL and migration-hash equivalence, route/authentication and
security equivalence, pending-model check, and Release publish are recorded in
`specs/013-dotnet-10-platform-upgrade/verification.md`.

This platform upgrade does not authorize production deployment. The same
Canadian residency, Data Protection, secrets, TLS/exposure, operations,
provisioning, and non-Development startup-refusal requirements remain in force.

Spec 014 (Client Intake and Document Upload Flow) is **complete**. T01
delivered the frozen `ClientIntake` contract plus the Blazor foundation and
review-screen migration; T02 implemented the backend; T03 exposed the approved
HTTP and component experience; T04 recorded the cross-module acceptance and
security proof in
`specs/014-client-intake-and-document-upload-flow/verification.md`:

- **The complete in-process workflow.** `ClientIntake` now creates the
  server-scoped 2025 case/checklist, issues and atomically redeems one-time
  links, revalidates revocable sessions, appends immutable answers, document
  metadata, upload attempts, submissions, review associations, and audits, and
  derives progress/readiness through `TaxCases`. It preserves exactly two
  firm-omitting digest lookups and never accepts client-supplied firm, case,
  participant, or document scope.
- **Additive intake infrastructure.** `Persistence` references exactly
  `ClientIntake`, `TaxCases`, and `Tenancy`, implements `SqlIntakeStore`, and owns
  migration `20260730000000_AddClientIntake`. The migration adds only the twelve
  intake metadata tables and does not alter the initial migration or any
  review/identity table. `TaxAI.Portal` now references `ClientIntake`, wires both
  workflows and the SQL store, supplies cryptographic purpose-separated
  credentials, and registers an authenticated encrypted streaming content store
  only in Development/test. Startup refuses invalid origin, lifetime, size,
  rate/concurrency, digest, fingerprint, encryption-key, or outside-web-root
  content configuration.
- **The content boundary remains write-only.** `IIntakeContentStore` exposes no
  read member, relational rows store no source bytes or filenames, and the
  encrypted source object is the sole raw-byte exception. It may contain SIN and
  is never queryable, rendered, logged, or served.
- **The HTTP boundary is closed and server-derived.** Six firm-scoped
  accountant routes and six anonymous client routes map over the workflow
  contracts. Accountant firm scope comes only from the revalidated portal
  session; a one-time raw credential is exchanged for a protected,
  path-scoped, revocable client cookie. Every mutation is a non-GET with
  antiforgery, and finite public/session/answer/upload/submission rate or
  concurrency policies run before resource validity. Invalid resources share
  one empty `404`, while overload shares one fixed `429`.
- **The UI remains presentation-only.** Create, one-time-link, confirmation,
  client intake, questionnaire, checklist, progress, and review-handoff states
  render through strongly typed components. Review and accountant screens stay
  static SSR. Only `ClientQuestionnairePanel` declares
  `@rendermode InteractiveServer`, solely for local closed-code reveal;
  detailed circuit errors and WebSocket compression are disabled, and no
  global render mode is applied. The host therefore has the exact four
  `/_blazor` framework endpoints plus `/_framework/opaque-redirect`.
- **The upload transport is streaming and neutral-name.** The tracked local
  module sends the literal `upload.bin` and antiforgery header. The endpoint
  parses with `MultipartReader`, discards received filenames, enforces
  configured limits, and streams into the workflow. There is no
  `IFormFile`, form buffering, content read, preview, or download route.
- **The review-screen migration remains unchanged.** `ReviewEndpoints.cs`
  keeps its route, policy, headers, trusted-firm extraction, browser-safety
  guard, closed projection, invariant amounts, collection order, single
  `FindAsync` call, and identical empty `404`.

**Static assets are served by `UseStaticFiles()` middleware, deliberately not
`MapStaticAssets()`.** The endpoint-routing variant registers a
`{**path:file}` catch-all over the whole URL space, which turns every POST-only
route's `405` into a `404` and adds an endpoint in the "neither explicitly
anonymous nor authorization-required" third state spec 011 forbids.

**T04 closed the acceptance and security proof, and changed no product code.**
It added only cross-module tests and the evidence record. Two suites carry it:
`Persistence.Tests/EndToEnd/ClientIntakeWorkflowPersistenceTests.cs` drives the
real workflows through the real `SqlIntakeStore` and `SqlReviewRecordRepository`
against a real relational database with the real migration applied, and
`TaxAI.Portal.Tests/EndToEnd/ClientIntakeAcceptanceTests.cs` plus
`ClientIntakeSecurityAcceptanceTests.cs` drive the real portal composition,
workflows, cryptographic credential source, Development encrypted content
adapter, and every component over HTTP. Only the two SQL-Server-backed
collaborators are substituted in the portal suite, and their relational
counterparts are the ones exercised for real in the persistence suite.

**AC-1 through AC-99 are recorded, per criterion, in
`specs/014-client-intake-and-document-upload-flow/verification.md`.**

**The gates are unchanged and remain in force.** No raw-content
preview/download, extraction, parsing, tax processing, review manufacture,
production content provider, retention/purge path, or deployment authorization
ships. The review and identity schema, public review/persistence contracts, and
deployment refusal are unchanged. The one raw-byte exception is still the
authenticated encrypted source object outside web root: it may contain a SIN,
it is **never** described as SIN-free, and no query, route, log, or diagnostic
can reach it. Every structured surface — browser model, HTML, URL, cookie,
header, SQL metadata, audit, log, and error — remains SIN-free in every form.

Spec 015 (T4 and Medical Document Extraction) is **complete**, with one
recorded defect. It answers spec 010's reserved question F: **yes, the document
extraction ingress opens — as an authenticated, firm-scoped,
accountant-initiated, in-process ingress and nothing else.** The system can now
turn a stored uploaded document into a persisted review record.

- **The chain is fed, not reimplemented.** `ExtractionWorkflow` implements the
  single `IDocumentExtractionIngress` spec 007 declared and left unimplemented,
  and drives the existing `ExtractionEnvelopeValidator` → `T4SlipParser` →
  `T4LineMapper` → `T4CaseProcessor` → `ReviewPackageAssembler` →
  `ReviewPackageProjection` → `IReviewRecordRepository.SaveAsync` chain. It
  contains no CRA line number, box→line table, tax rate, threshold, arithmetic,
  or rounding, and it declares no second envelope, validator, provenance,
  document-kind, supported-year, or retention authority.
- **The employee-copy T4 parses.** T01 removed exactly three `T4SlipParser`
  validation requirements — Box 54, `EmployerAddress`, and `EmployeeAddress` —
  against a canada.ca citation recorded in `src/TaxLogicEngine/CLAUDE.md`. Box 12
  and Box 45 remain required, nothing is invented in place of a removed
  requirement, and **no public type, member, or signature changed.**
- **History is append-only.** Extraction versions, medical evidence versions,
  corrections, classification overrides, duplicate determinations and their
  resolutions, acceptances, and audit events are each appended and never
  rewritten. `Persistence` owns migration `20260803000000_AddDocumentExtraction`,
  which adds only ten extraction tables and alters no existing one.
- **Medical evidence is neutral evidence, not a tax result.** It is a closed
  field set — date and amount **as printed**, currency-or-unknown, provider and
  patient display names, receipt reference, provenance, and structural outcomes.
  There is no aggregation, threshold, dependant rule, eligibility claim, or CRA
  line for it anywhere, and it never enters a T4 review record.
- **Nothing is automatic.** No confidence threshold exists anywhere in the
  feature; a duplicate is flagged and never auto-resolved; and every document
  acceptance and case finalization is an explicit accountant action.
- **No provider ships and nothing leaves the process.** `DocumentIntelligence`
  holds the boundary and declares no SDK. The only adapter is deterministic,
  in-process, and registered for Development and test hosts only; a deployable
  non-Development host still refuses to start.
- **The content read is the narrowest thing that works.** `IIntakeContentStore`
  stays write-only. A separate `IExtractionContentReader` requires the full
  firm/case/document/content-key scope with no omitting overload, has exactly one
  consumer, and hands back a disposable lease. No route serves, streams,
  previews, downloads, or exports source content.

**AC-1 through AC-119 are recorded, per criterion, in
`specs/015-t4-and-medical-document-extraction/verification.md`. 117 are met.**
Two are not:

- **AC-16 is NOT MET — an open CSRF defect.** `POST …/extract`,
  `POST …/accept`, and `POST …/extractions/finalize` carry antiforgery metadata
  that is **never validated**, because the minimal-API pipeline validates the
  token when the form is read during parameter binding and those three handlers
  bind no `[FromForm]` value. The three that do bind one are correctly refused.
  The exposure is narrowed — not closed — by the session cookie's
  `SameSite=Lax` policy. **Fix this in `ExtractionEndpoints` before the portal
  is exposed to anything real.**
- **One T06-owned guard blocks the merge.**
  `PreservedSurfaceTests.NoT07VerificationSurfaceWasIntroduced` asserts that
  `specs/015-.../verification.md` does not exist. It proved T06 stayed in its
  lane, but it is bounded to future `HEAD` rather than to T06's own range, so it
  fails the moment T07 writes the file T07 exists to write. T07 may not fix it —
  the file is not in its list. Whoever owns T06 must either drop that one
  assertion or bound it to T06's completion commit, the way AC-45's third
  amendment bounds the spec 006 and spec 007 historical guards.
- **AC-44 is NOT VERIFIED.** T07 ran in a Linux container without PowerShell or
  the pinned SDK, so `pwsh ./scripts/Invoke-SolutionTests.ps1` could not run.
  `dotnet test TaxAI.sln` added 30 passing tests with an unchanged failure set of
  14 pre-existing environment-dependent failures (Windows line endings, Windows
  path separators, and the SDK pin). **Run the gate on Windows before merge.**

**The gates are unchanged and remain in force.** No raw-content
preview/download, production content or document-intelligence provider,
retention/purge path, second taxpayer-data surface beyond the approved
accountant extraction-review screen, or deployment authorization ships. The
spec 012 review route and every spec 014 intake surface are byte-identical. The
one raw-byte exception is still the authenticated encrypted source object
outside the web root: it may contain a SIN, it is **never** described as
SIN-free, and no query, route, log, or diagnostic can reach it. Every structured
surface — browser model, HTML, URL, cookie, header, SQL column, audit, log, and
error — remains SIN-free in every form.

**The shipped Development provider cannot read a client-uploaded document.** The
upload transport requires a PDF/JPEG/PNG signature prefix and the deterministic
adapter requires its own text header as the first line, so no byte sequence
satisfies both. That is a property of the Development-only adapter — a real
provider would read a PDF — but do not read a green suite as proof that the
uploaded-document path works end to end.

**Never assert a total project count for `TaxAI.sln`.** Repository-level tests
must assert that a spec's own projects are each declared exactly once at their
approved path, never that they are the only projects in the solution. A total
count freezes the solution against every future module: spec 006 shipped one
and it blocked spec 007's Wave 0 outright, requiring PR #66 to unpick.

The full inventory of existing public types, embedded mapping configs, and the recorded per-spec decisions live in [src/TaxLogicEngine/CLAUDE.md](src/TaxLogicEngine/CLAUDE.md) — **check there before creating anything; do not re-specify or re-implement an existing type.**

Before specifying or implementing anything, grep `specs/*/prd.md` and the source tree. Re-specifying an existing type is how a codebase ends up with two rounding helpers with different midpoint behaviour.

**Keep this section current.** A stale "current state" causes agents to rebuild what already exists.
