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
  - **every workflow constructor and method throws
    `NotImplementedException`** — T01 froze signatures and shipped no behaviour;
    nothing in the portal resolves or invokes them, and the module implements
    none of its own ports;
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

**No intake capability ships yet.** There is no intake route, workflow body,
relational intake table, encrypted content adapter, or interactive component —
and no Interactive Server registration, so the host carries no SignalR circuit
surface at all. **Spec 014 T03 owns Interactive Server**, introducing it with the
first interactive island (the adaptive questionnaire and upload panel) rather
than ahead of it.

## Next

Spec 014 T02, T03, and T04 remain. They implement the intake backend and
additive migration, then the client and accountant UI and routes, then the
cross-module security proof. Each is a separate human-named task; none may be
started without T01's `.done` marker on `master`.

The first taxpayer-data surface is complete under spec 012's exact browser
allowlist. Any expansion of that allowlist, document upload, stored-package
mutation or deletion, firm or membership provisioning, taxpayer listing,
review decisions, export, or second taxpayer-data surface requires another
human-approved `tax-domain` specification.

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
- any second taxpayer-data screen, taxpayer listing, or editing;
- document upload, OCR, or LLM integration;
- production deployment or provisioning;
- microservices.
