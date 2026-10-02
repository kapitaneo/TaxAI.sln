# Current Focus

## Completed

- Spec 001 — T5 parsing
- Spec 002 — T5 CRA line mapping
- Spec 003 — T4 parsing
- Spec 004 — T4 CRA line mapping
- Spec 005 — Modular monolith foundation (repository restructuring only):
  - solution normalized to `TaxAI.sln`;
  - projects moved to `src/TaxLogicEngine/` and `tests/TaxLogicEngine.Tests/`;
  - all existing TaxLogicEngine behavior preserved (same 261 tests green);
  - agent instructions split: root `CLAUDE.md` (repository-level) +
    `src/TaxLogicEngine/CLAUDE.md` (project-level);
  - Codex `AGENTS.md` bridge added (root + minimal local pointer);
  - `README.md` rewritten to describe only implemented software;
  - full AC-1…AC-30 verification recorded in
    `specs/005-modular-monolith-foundation/verification.md`.

- Spec 006 — Tax Case Core:
  - deterministic, immutable 2025 tax-case aggregate implemented in
    `src/TaxCases/`;
  - participants, evidence receipt metadata, participant-scoped checklist and
    intake, derived readiness, lifecycle, and deterministic change records;
  - firm/year/participant isolation and exhaustive SIN-safe diagnostics;
  - AC-1…AC-43 verification recorded in
    `specs/006-tax-case-core/verification.md`.

- Spec 007 — Document Extraction Contract:
  - deterministic, provider-independent ingestion contract implemented in
    `src/DocumentExtraction/`;
  - untrusted `ExtractionEnvelope` + `ExtractionProvenance`, the internal-only
    `ValidatedExtraction`, the closed `ExtractionFields` set, and the
    signature-only `IDocumentExtractionIngress` port (no implementation ships
    in spec 007);
  - `ExtractionEnvelopeValidator.Validate` — the single fail-closed gate:
    attribution presence only, 2025-only year isolation, payload/kind
    coherence, and a mechanical empty-extraction rule;
  - the raw `T4RawInput` payload passes through reference-identical and
    value-unchanged — no parsing, typing, normalization, mapping, or rounding,
    so extraction output never becomes tax truth;
  - PII by **omission, not masking**: no caller or payload value appears in any
    `ToString()`, `ValidationError`, diagnostic, or JSON output;
  - AC-1…AC-30 verification recorded in
    `specs/007-document-extraction-contract/verification.md`.

- Spec 008 — T4 Case Processor:
  - the deterministic coordinator implemented in `src/CaseProcessing/`, driving
    one already-validated T4 extraction through the existing engine;
  - `T4CaseProcessor.Process` — the module's single behaviour — runs eight fixed
    stages: null extraction, kind/payload, supported year and payload structure
    (these two accumulate), parse, slip/extraction year coherence, map, and
    construct;
  - the internal `PayloadStructureGuard` closes the seam where a payload that
    cleared spec 007's gate could still make `T4SlipParser.Parse` throw on a null
    `Boxes` collection — an escaping exception is not fail-closed behaviour;
  - **no tax truth is invented here**: the slip comes only from `T4SlipParser`,
    the line entries only from `T4LineMapper` plus the injected
    `IT4LineMapProvider`, and engine errors are surfaced verbatim and in order.
    The module contains no CRA line number, no box→line table, no arithmetic, and
    no `decimal` token;
  - the provider is asked for exactly the slip's year, once, with no
    adjacent-year retry; the 2025 policy lives only in
    `SupportedProcessingTaxYears`;
  - PII crosses an **omission → masking** seam: the raw `T4RawInput` never
    reaches the result, and the parsed Box 12 exists only as the engine's masked
    `RecipientIdentifier` on `T4Slip.Sin`;
  - AC-1…AC-43 verification recorded in
    `specs/008-t4-case-processor/verification.md`.

- Spec 009 — Accountant Review Package:
  - deterministic, immutable 2025 review packages implemented in
    `src/CaseReview/`, assembled only from successful
    `ProcessedT4Extraction` values;
  - exact slip identity and ordered snapshots of engine-produced line entries,
    source-box provenance, populated amounts, and unmapped codes, with no
    parsing, mapping, arithmetic, or rounding;
  - one immutable human decision per package, with fixed fail-closed validation
    and one package-level affirmative acknowledgement required before approval
    whenever any reviewed slip has an unmapped code;
  - PII by masking plus omission: the engine's masked `RecipientIdentifier` is
    the only SIN representation, while caller strings and reviewed nested
    content are omitted from defensive rendering and serialization;
  - AC-1…AC-54 verification recorded in
    `specs/009-accountant-review-package/verification.md`.

- Spec 010 — Portal Foundation:
  - the repository's first runnable application: `src/TaxAI.Portal/`, an
    ASP.NET Core host and the single deployable the modular-monolith decision
    committed to;
  - one composition root registering `IT4LineMapProvider`,
    `IT5LineMapProvider`, and `T4CaseProcessor` as singletons and nothing else,
    with validate-on-build and scope validation forced on unconditionally;
  - exactly two GET routes — `/health` returning a fixed literal and `/`
    returning a static shell document — with no input surface of any kind;
  - the inward dependency rule proved in both directions: the portal reaches
    modules only through contracts, and no module references the portal;
  - the web layer proved to contain no tax computation, CRA line number,
    mapping table, taxpayer type, PII, or tax-year policy; PII is handled by
    omission — the data never enters the process;
  - AC-1…AC-50 verification recorded in
    `specs/010-portal-foundation/verification.md`.

## Completed — specs 011, 012, and 013

- Spec 011 — Tenant Identity and Persistence Foundation:
  - `Tenancy` now owns the deterministic trusted-firm boundary, persisted
    review-record shapes and allowlist projection, identity/session contracts,
    membership and lifetime policies, retention calculation, and repository and
    purge ports;
  - `Persistence` is the only EF Core project and implements the SQL Server
    model, repository-owned migration, firm-scoped immutable review-record
    repository, durable shared session store, identity directory, explicit
    retention purge, connection guard, and development-only synthetic seeding;
  - external OIDC sign-in creates a server-side revocable session; the browser
    carries only a protected opaque key, and sign-out revokes the authoritative
    session so a copied cookie cannot reuse or recreate it;
  - protected requests reload the local user and active memberships, and the
    application-issued `taxai_firm_id` claim comes only from the live session;
    a provider-supplied firm claim is discarded and never trusted;
  - users with several memberships select one through the authenticated
    membership surface, which revokes the old session and issues a new one;
  - persistence and portal PII posture is **no SIN in any form** — not full,
    masked, partial, stored, rendered, logged, or placed in a fixture — while the
    engine's existing in-memory masking contract remains unchanged;
  - the exclusive seven-year retention boundary is an engineering product
    decision, not a CRA fact or statutory statement;
  - T21 final verification, Amendments audit, manifest-floor validation, and
    AC-by-AC evidence are merged.

- Spec 013 — .NET 10 Platform Upgrade:
  - all production and test projects target exactly `net10.0`;
  - SDK `10.0.302` and repository-local `dotnet-ef` `10.0.10` are pinned;
  - ASP.NET Core, EF Core, SQL Server, SQLite, OIDC, MVC Testing, and the
    approved xunit v2 test-package matrix are aligned;
  - the original migration set, model snapshot, relational schema, SQL Server
    DDL, stored-data compatibility, product behavior, public/internal contracts,
    route/authentication outcomes, security boundaries, and warning ceiling are
    preserved;
  - the repository preflight, all eight test projects, pending-model check, and
    Release publish are recorded in
    `specs/013-dotnet-10-platform-upgrade/verification.md`.

- Spec 012 — Accountant Review Screen:
  - one authenticated, server-rendered route reads one exact immutable review
    package through the existing trusted-firm repository;
  - the active firm is always derived from the validated server-side session,
    with active-user and active-membership revalidation before repository
    access;
  - missing and foreign-firm resources produce the same empty `404`;
  - the exact browser allowlist is tax year, package version, slip type,
    taxpayer and employer display names, boxes and values, tax lines, source-box
    references, and unmapped codes;
  - no SIN in any form, contact/account/provider identity, tenant/session or
    persistence identifier, or non-display timestamp reaches the browser;
  - the screen is read-only and introduces no tax logic, mutation, “latest”
    selection, JSON API, or client-side state.

## In progress — spec 014, Client Intake and Document Upload Flow

- Spec 014 T01 — Blazor foundation, review migration, and frozen intake contract:
  - `src/ClientIntake/` and `tests/ClientIntake.Tests/` added, referencing exactly
    `TaxCases`, `Tenancy`, and `TaxLogicEngine`, with no package reference;
  - the closed intake vocabularies, accountant/client commands and safe
    projections, storage records, and the
    `IIntakeStore`/`IIntakeContentStore`/`IIntakeCredentialSource` ports are declared
    exactly as approved;
  - T01 initially left every workflow constructor and method throwing
    `NotImplementedException`; T02 subsequently implemented those frozen
    signatures without widening them;
  - `IIntakeStore` has exactly two firm-omitting members, both keyed-digest
    lookups that *return* trusted scope; `IIntakeContentStore` exposes no read
    member at all;
  - identifier, secret, digest, taxpayer-text, content-key, and fingerprint
    properties are `[JsonIgnore]`-marked, every public model declares a
    structural `ToString()`, and every collection constructor copies into a
    read-only snapshot;
  - one Blazor Web App foundation added to `TaxAI.Portal` — `App`, `Routes`,
    `PortalLayout`, `ReviewPage` — **static server rendering only**: no component
    declares a render mode, no global mode is applied, **Interactive Server is not
    registered**, so no `/_blazor` circuit endpoint exists and no client script
    loads; and there is no WebAssembly, npm tree, remote font, or third-party
    asset;
  - `ReviewEndpoints.cs` lost only its `Render` method, `StringBuilder`,
    appended markup, and inline style assembly, and now returns
    `RazorComponentResult<ReviewPage>` with one closed `ReviewPageModel`; its
    route, policy, headers, trusted-firm extraction, browser-safety guard, closed
    projection, invariant amounts, collection order, single `FindAsync` call, and
    identical empty `404` are unchanged;
  - reusable styling moved to tracked `wwwroot/portal.css` plus component-scoped
    CSS, served by `UseStaticFiles()` middleware — deliberately **not**
    `MapStaticAssets()`, whose `{**path:file}` catch-all would turn every
    POST-only route's `405` into a `404`;
  - no production file under `Persistence` or `Tenancy`, no review or persistence
    public contract, no EF migration or model snapshot, and no database schema
    changed.

- Spec 014 T02 — complete backend and additive infrastructure:
  - the frozen signatures now have deterministic implementations for
    server-scoped case/checklist creation, one-time links, revocable client
    sessions, adaptive questions, immutable answers/submissions, upload
    validation and duplicate handling, progress/readiness, audit, and exact
    immutable review association;
  - `TaxCases` remains the only case/readiness authority, client scope is always
    session-derived, and only the two keyed-digest bootstrap lookups omit a firm;
  - `Persistence` now references exactly `ClientIntake`, `TaxCases`, and
    `Tenancy`, implements the scoped SQL store, and adds only migration
    `20260730000000_AddClientIntake` with twelve intake metadata tables; the
    initial migration and existing review/identity schema are unchanged;
  - `TaxAI.Portal` now references `ClientIntake`, wires the workflows and store,
    supplies purpose-separated cryptographic credentials, and registers the
    authenticated encrypted streaming content store only in Development/test;
  - startup validates the canonical HTTPS public origin plus every lifetime,
    size/count, rate/concurrency, digest, fingerprint, encryption-key, and
    outside-web-root content setting and retains non-Development refusal;
  - the content contract has no read member, SQL stores no source bytes or
    filename, and the sole potentially SIN-bearing surface is the encrypted
    opaque source object outside web root.

- Spec 014 T03 — secure HTTP and component experience:
  - the exact six accountant and six anonymous client routes map over the T02
    workflows, with firm scope derived only from the revalidated accountant
    session and client scope derived only from the protected intake session;
  - one-time link confirmation/redeem, clean redirect, secure path-scoped
    cookie, purpose-separated protected proof, fixed safe `404`/`429`, finite
    rate and upload-concurrency limits, antiforgery, no-store browser headers,
    and local-only CSP are enforced at the HTTP boundary;
  - successful create, issued-link, confirmation, client intake, questionnaire,
    checklist, upload, submission, progress, and handoff states render through
    strongly typed presentation-only Razor components;
  - review and accountant screens remain static SSR. Interactive Server is
    registered only for the questionnaire's local closed-code reveal panel,
    with detailed errors and WebSocket compression disabled and no global
    render mode;
  - the tracked upload module sends only `upload.bin` plus the antiforgery
    header; the endpoint uses `MultipartReader`, discards received filenames,
    enforces configured limits, and streams into the workflow;
  - progress cannot recover a raw client URL, and the existing review URL
    appears only for a verified ready association. No review record is created;
  - no production file under `ClientIntake`, `Persistence`, `Tenancy`, or a
    domain module, and no migration, snapshot, or schema shape changes.

The encrypted opaque source object outside web root remains the sole potentially
SIN-bearing exception. No raw-content read/download route, production content
provider, retention/purge path, extraction, tax processing, review construction,
or deployment authorization ships.

- Spec 014 T04 — cross-module acceptance and security proof:
  - the real workflows drive the real `SqlIntakeStore` and
    `SqlReviewRecordRepository` against a real relational database with the real
    migration applied, and the real portal composition, credential source,
    Development encrypted content adapter, and components are driven over HTTP;
  - the synthetic happy path runs end to end: an R-1 accountant creates one 2025
    case, issues one link, the client confirms, redeems, answers, uploads
    signature-valid T4 and medical files, and submits; the accountant sees exact
    progress and associates one exact existing review record, after which the
    unchanged review route opens;
  - two firms over one shared store prove identical safe refusals for missing,
    foreign, malformed, replayed, spent, and revoked resources, with no unscoped
    read and no cross-case duplicate oracle;
  - accountant membership revocation and client session revocation between a
    rendered page and the next operation both refuse before any mutation;
  - the raw credential is stored one-way, the transport filename is neutral, the
    source bytes are encrypted outside web root, fingerprints are scope-derived,
    and answers, submissions, audit, and the review association are immutable;
  - AC-1…AC-99 evidence and the authoritative gate totals are recorded in
    `specs/014-client-intake-and-document-upload-flow/verification.md`;
  - T04 changed no production C# file, component, asset, reference, package,
    migration, snapshot, or schema.

## Completed — spec 017, Local End-to-End Accountant MVP

- Spec 017 — a developer can run TaxAI locally and complete the accountant
  workflow in a browser:
  - `scripts/local/Start-LocalDemo.ps1` prepares LocalDB, applies the
    repository-owned migrations as an explicit developer action, generates the
    Development settings outside the repository, writes two synthetic samples
    into the ignored `artifacts/local-demo/` directory, and starts the host on
    HTTPS loopback. The application still never migrates;
  - a Development-only sign-in signs in one seeded synthetic accountant through
    the existing cookie sign-in event and durable session store, so every later
    request passes the normal revalidation. It is not mapped outside
    Development, and no migration inserts a row;
  - `TrustServerCertificate=True` is accepted only through an explicit opt-in
    and only for a loopback data source, with encryption still explicit. The
    default guard, the non-Development refusal, and the production validator
    are unchanged;
  - the deterministic Development provider tolerates one leading upload
    signature line. This is a local sample convention, not PDF support, and the
    upload gate and the production adapter are byte-identical to before;
  - one firm-scoped case list (`GET /intake-cases`) shows only the taxpayer
    display name and stored tax year of each case, with the firm taken only from
    the revalidated session. There is no search, filter, sort, paging, or
    cross-firm view;
  - accountant navigation appears on accountant pages only, and an in-scope
    refused correction or case finalization shows one of four fixed, value-free
    notices. Every missing, foreign, session, and membership failure keeps its
    existing response;
  - the whole flow is proved over HTTP through rendered links and forms with the
    real multipart upload, and through the real stores against the real
    migrations; the review output equals what the existing chain produces, and
    the recipient identifier reaches no response, cookie, log, or column;
  - the demo is described in `docs/local-demo.md`, and AC-1…AC-93 evidence and
    one real local run are recorded in
    `specs/017-local-end-to-end-accountant-mvp/verification.md`;
  - no tax file, mapping, rounding rule, project file, migration, or spec 016
    file changed. There is no accountant-side document upload.

## Next

**Spec 014 is complete.** No spec 014 task remains.

**Spec 017 is complete.** No spec 017 task remains. A local run is not
production authorization, and no real taxpayer document may be used locally.

**Spec 016 is frozen after T06.** T07 to T10 are not resumed, no Azure resource
is deployed, the pending drills are not executed, and production admission stays
refused. The paragraphs below describe that specification as planned; they do
not schedule it.

Spec 016 (Canadian Azure Production Infrastructure) is the approved platform
specification: `specs/016-canadian-azure-production-infrastructure/` holds the
PRD, architecture and a ten-wave task plan (T01–T10), each wave human-selected
and merged before the next. It starts from the gates below; none of them moved.

- **T01 — Azure infrastructure boundary and production configuration** is
  implemented on its branch: the `TaxAI.AzureInfrastructure` library with the
  structural production-profile validator and the data-plane managed-identity
  binding probe, Tenancy's production-control contracts and evidence policy,
  the Portal's closed production profile and enforced admission gate, the
  runtime-configuration schema and Bicep module, the offline configuration
  check, and the initial threat model. It admits no deployable host: the
  production Document Intelligence adapter, source store and reader do not
  exist yet, and both provider assurance gaps stay independently required.
  Its completion marker waits for the authoritative gate on the supported
  Windows environment.
- No later task is selected. T02 onward each require explicit human selection
  after T01 merges.

The first taxpayer-data surface is complete under spec 012's review allowlist,
and spec 014 adds the separately approved intake surface. Spec 017 adds the
approved firm-scoped case list. Each is limited to its approved browser
allowlist.
Any expansion, raw-document view/download, stored-package deletion, firm or
membership provisioning, taxpayer search or cross-firm listing, accountant-side
document upload, review decisions, export, or additional taxpayer-data surface
requires another human-approved `tax-domain` specification.

Question F — whether the portal exposes the untrusted document-extraction
ingress — remains reserved, and no ingress implementation ships.

Deployment is separately blocked. As shipped, this build starts in Development
only and may not be deployed anywhere that can receive real taxpayer data until
a deployment specification provides and verifies the Canadian Azure Storage and
Key Vault Data Protection key ring, hosting, TLS, exposure, residency, secrets,
operations, and production provisioning.

## Explicitly not now

- new CRA forms;
- T1 calculation;
- NETFILE/EFILE;
- any unapproved taxpayer-data screen, taxpayer search, cross-firm listing, or
  editing;
- accountant-side document upload;
- raw-document view/download, extraction, OCR, or LLM integration;
- production deployment or provisioning;
- microservices.
