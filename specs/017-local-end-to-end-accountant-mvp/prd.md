# PRD 017 — Local End-to-End Accountant MVP

## Feature classification

**Class: `tax-domain`.**

Tax/CRA semantic impact: none

This feature introduces no CRA fact, mapping, calculation, parser rule, rounding
rule, or tax-year policy. It is nevertheless `tax-domain`, for four independent
reasons, any one of which is sufficient:

1. **It adds a new taxpayer-data surface.** A firm-scoped case list renders
   taxpayer display names. `src/TaxAI.Portal/CLAUDE.md` states that "listing
   taxpayers" and "adding another taxpayer-data surface" each require a further
   approved `tax-domain` specification. This is that specification.
2. **It changes security boundaries.** A Development-only sign-in route, a seeded
   synthetic user and firm membership, and a Development-only relaxation of the
   SQL certificate-validation refusal are all identity/transport boundary
   changes.
3. **It touches SIN handling.** The locally generated T4 sample carries a
   SIN-shaped Box 12 value through the real upload, storage, and extraction path.
4. **It changes how an untrusted document reaches the tax chain** in Development
   (the deterministic upload bridge).

A mixed feature is `tax-domain`, and when unsure it is `tax-domain`. Every
tax-domain protection therefore applies unrelaxed: explicit tax-year scope,
canada.ca as the only tax source, one negative acceptance criterion per
fail-closed rule, the PII/SIN rules, tax-year isolation, and data-driven mapping.
No `platform` relaxation is claimed anywhere in this document.

**Status:** approved by the human on 2026-10-01 with the clarifications
recorded; ready for `/plan`. The approval authorizes exactly the guard
amendments listed in [Authorized amendments](#authorized-amendments-approved),
under the conditions recorded there, and no others. No deployment or production
use is authorized by this document.

---

## Problem

**Tax year in scope: 2025 only.**

2025 is the only year any existing authority supports
(`TaxCases.SupportedTaxYears`, `SupportedExtractionTaxYears`,
`SupportedProcessingTaxYears`, `SupportedReviewTaxYears`, `T4SlipParser`'s
expected year, and `Mappings/T4LineMap.2025.json`). This feature declares **no
new supported-year authority**, no "current" or "latest" year, and no
adjacent-year fallback. The tax year shown on the new case list is the
server-stored `StoredIntakeCase.TaxYear`. The locally generated T4 sample prints
2025; a document whose printed year differs from the case year still fails
closed through spec 007's existing envelope/payload coherence check and is never
coerced (AC-38).

### What is broken today

Specs 011 through 016 delivered every functional stage of the accountant
workflow: identity and durable sessions, case creation and one-time client
links, client upload, extraction, correction, acceptance, finalization, and the
read-only review output. Each stage is proven by tests. **No developer can run
the whole thing in a browser.** Verified against the tree on 2026-10-01:

1. **The host cannot be started without substantial undocumented setup.**
   Startup requires a SQL Server connection string
   (`ConnectionStrings:TaxAiDatabase`), a manually applied migration, two OIDC
   settings in every environment (`PortalIdentity.cs`), twenty `TaxAi:Intake:*`
   settings including an HTTPS `PublicOrigin`, three distinct 32-byte keys, and
   an absolute `DevelopmentContentRoot` outside the web root
   (`IntakeConfiguration.cs`), and `TaxAi:Extraction:*` settings
   (`ExtractionConfiguration.cs`). There is no `UserSecretsId`, no setup script,
   and no configuration template. `Properties/launchSettings.json` binds only
   `http://localhost:5199`, while the intake origin must be HTTPS.
2. **A local SQL Server cannot be used.**
   `Persistence.Services.SqlConnectionStringGuard` refuses
   `TrustServerCertificate=True` in every environment, and a local instance
   presents a self-signed certificate.
3. **Nobody can sign in.** The only way in is an external OIDC challenge.
   `DevelopmentFixtureSeeder` seeds one firm and one synthetic review record but
   no user and no membership. Tests sign in through a test-only harness route
   that does not exist in the product.
4. **No browser-uploadable file extracts.** `IntakeUploadValidator` accepts only
   PDF, JPEG, and PNG signatures. `DeterministicDocumentIntelligenceProvider`
   requires `TAXAI-DETERMINISTIC-DOCUMENT/1` as the first non-empty line of
   strict UTF-8 text. No single file satisfies both, so the acceptance tests
   stage bytes directly into the content store
   (`tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionAcceptanceTests.cs`).
   **The real HTTP upload path has never been exercised together with
   extraction.**
5. **There is no case list.** An accountant can open a case only by already
   knowing its opaque identifier.
6. **There is no navigation.** `/` is a fixed shell with only a sign-in link.
   Nothing links to `/intake-cases/new`. `IntakeCaseProgressPage` has no link to
   `/intake-cases/{caseId}/extractions`.
7. **A refused action is silent.** Every refused extraction mutation, including
   an in-scope rejected correction or finalization, returns an empty `404`
   (`ExtractionEndpoints.MutationResult`), so the accountant cannot tell a
   refused correction from a broken link.

### Desired outcome

**Primary acceptance criterion: a developer can run TaxAI locally, open a
browser, and complete the accountant workflow end to end.**

The target flow, in order:

1. start the application locally with one command;
2. sign in through the Development sign-in;
3. see the firm's case list and create a case;
4. obtain the existing one-time client intake link;
5. as the client, in a second browser or private window, redeem the link and
   upload the T4 and the medical document;
6. as the accountant, run extraction with the existing deterministic provider;
7. review the extraction on the existing accountant extraction-review surface;
8. correct an allowed non-SIN field;
9. have the existing validation and tax-processing chain run again;
10. accept the document;
11. finalize the case;
12. see the existing read-only review output at `/reviews/{recordId}`.

Medical evidence follows the same local path through steps 5 to 10 exactly where
the approved spec 015 contracts already support it: neutral evidence, no tax
decision, not part of finalization, and not part of the review record.

Priorities, in order: working local UX; the actual accountant workflow;
correctness of tax and extraction behaviour; an easy, repeatable local demo;
tests for the vertical slice.

---

## Definition of Done for the spec (human, 2026-10-01)

Spec 017 is done when a human can run the local demo: **start the application,
sign in, create or select a case, open the client link in a second browser
session, upload a generated T4, extract, review, correct, accept, finalize, and
reach the review output.**

Delivery is bounded to a **maximum of four substantial tasks**:

- **T01** — local startup, plus Development identity and session;
- **T02** — real client upload to deterministic extraction bridge, plus
  generated samples;
- **T03** — accountant case navigation and case list, plus workflow feedback;
- **T04** — complete browser/HTTP vertical-slice verification, plus the demo
  guide.

There is **no** infrastructure-hardening, Azure, recovery, deployment, or
operations task. Spec 016 T07 to T10 are not resumed.

---

## Approved Class A decisions (given by the human; not re-asked)

These six decisions were answered by the human before this PRD was written. They
are recorded here as approved decisions. Each is the authority for the
requirements that cite it. Where a decision needed a mechanical reading to become
testable, the **narrowest** reading consistent with every clause of the decision
is used; those readings were confirmed by the human on 2026-10-01 and are
recorded in
[Readings of approved decisions](#readings-of-approved-decisions-confirmed-by-the-human-on-2026-10-01).

**D-1 — Development sign-in.** Add a Development-only sign-in route. Seed one
synthetic development user and active firm membership. The route must create and
use the normal durable session path; there is no parallel authorization model.
The route must not be mapped outside `Development`. Production and Staging
startup and route inventories must prove it is absent. No local OIDC container is
required.

**D-2 — Deterministic upload bridge.** The spec 014 upload signature gate is not
weakened or altered for production. Demo/sample T4 and medical files may use a
valid accepted upload signature such as `%PDF-`; the deterministic Development
provider may read its structured deterministic payload after that header. This
behaviour is Development-only and must not become a production PDF parser or OCR
rule. The real HTTP upload path must be exercised end to end.

**D-3 — Synthetic Box 12 value.** No SIN-shaped sample fixture is committed. The
local setup/demo tooling generates the synthetic T4 sample locally and leaves it
untracked. It may contain an obviously synthetic SIN-shaped value required by the
parser. No tracked fixture, README example, log, evidence file, or repository
artifact may contain that value. Existing SIN/PII repository guards remain
unchanged.

**D-4 — Local SQL.** `TrustServerCertificate=True` is allowed only for the
Development local database path. Local startup is made easy with LocalDB or the
approved local SQL option. Production and Staging continue to refuse
`TrustServerCertificate=True`. The production SQL connection guard is not
weakened. No local PKI or certificate provisioning is required.

**D-5 — Case list.** This specification approves a new accountant-only,
firm-scoped case-list surface: the firm comes only from the trusted, revalidated
server-side session; there is no caller-supplied firm scope; no cross-firm
existence signal; no global taxpayer search; only the minimum fields needed to
select and open a case; existing authorization and safe-failure rules are
preserved.

**D-6 — Upload actor.** There is no accountant-side document upload in this
specification. The demo exercises the real client workflow: the accountant
creates or opens a case, obtains the existing one-time client intake link, the
human opens it in a second browser or private window, the client uploads the T4
and medical document, and the accountant returns to the authenticated portal to
continue extraction and review.

**D-7 — Fixed notice for a refused correction or case finalization (approved
narrowly by the human on 2026-10-01).** After authentication, live-session
validation, membership validation, firm scope, and case/document ownership have
all succeeded, an allowed accountant **correction** or **case-finalization**
operation that is refused by the existing workflow/domain validation may return
a fixed, value-free user notice instead of the historical empty `404`. Nothing
else changes; the full terms are FR-42 and FR-43.

### Spec 016 freeze (human direction)

Spec 016 is frozen after T06. T07 to T10 are not resumed by this specification,
no Azure resource is deployed, the pending production and recovery drills are
not executed, DEP-17 to DEP-20 stay unexecuted, and production admission stays
refused. This PRD edits no file under
`specs/016-canadian-azure-production-infrastructure/`, `infra/azure/`, or
`scripts/azure/`, and creates no spec 016 status marker (AC-89, AC-90).

---

## Current repository state and reuse analysis

Grounded in the tree at `D:\Projects\TaxAI` on `master`. **The planning baseline
is commit `7ee2574`** (merge of PR #152, spec 016 T06), with local `master`
synced to `origin/master`; spec 016 is frozen after T06 and
`status/T06.done` exists. The reuse check covered every `specs/*/prd.md` and the
source tree.

**Baseline note.** This PRD was first drafted against the T05 merge and was
re-based on `7ee2574` on 2026-10-01. T06 changed **no** file under
`src/ClientIntake`, `src/DocumentIntelligence`, `src/ExtractionWorkflow`, the
portal `Components`, any `*Endpoints.cs`, `Program.cs`, `PortalIdentity.cs`,
`SqlConnectionStringGuard.cs`, `DevelopmentFixtureSeeder.cs`,
`RouteAuthenticationBaselineTests.cs`, the Persistence composition tests, or
`ClientIntake.Tests`, so every repository fact below still holds. T06 **did
add**:

- `src/TaxAI.Portal/Production/RecoveryOrchestrator.cs`;
- `src/Persistence/Services/SqlRecoveryReconciler.cs`, registered in
  `AddTaxAiPersistence`;
- `PortalStartupGuards.HasDurableRecoveryReconciler`;
- a Production-only registration in `ModuleRegistration.cs`;
- one test in `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs`;
- new rows in `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`.

Spec 017 depends on none of these and changes none of them.

### Build and test commands (unchanged)

```
dotnet build TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1     # the gate
```

`scripts/test-projects.json` registers twelve test projects with `minimumTests`
floors. At the baseline: `TaxAI.Portal.Tests` 1189, `Persistence.Tests` 305,
`Tenancy.Tests` 395, `TaxAI.AzureInfrastructure.Tests` 528,
`ClientIntake.Tests` 188, `DocumentIntelligence.Tests` 230. Floors may rise and
never fall.

### Verified repository facts this feature designs around

These are facts about this codebase, not CRA claims.

1. **`SqlConnectionStringGuard.RequireValid`** requires an explicit
   `Encrypt=True`, refuses `TrustServerCertificate` and any
   `HostNameInCertificate`, and restricts authentication modes. It runs in every
   environment and reads no environment. `PortalStartupGuards` adds a host-level
   refusal of unencrypted transport and of `TrustServerCertificate=True` outside
   Development. `ProductionConfigurationValidator` independently requires
   managed identity with strict encryption.
2. **No automatic migration.** `src/Persistence/CLAUDE.md`: "There is no
   `EnsureCreated`, no automatic migration at startup". Schema changes come only
   from repository-owned migrations.
3. **`PortalIdentity.AddPortalIdentity`** requires `TaxAi:Oidc:Authority` and
   `TaxAi:Oidc:ClientId` in every environment. A local principal is built in
   exactly one place, `ResolveLocalPrincipalAsync` (the cookie scheme's
   `OnSigningIn` event), from issuer and subject through `IIdentityDirectory`;
   the firm claim is issued only when exactly one active membership exists; the
   durable session is written by `SessionTicketStore`.
   `FirmEndpoints.IssueReplacementAsync` already signs in to the cookie scheme
   with only issuer and subject claims, so a non-OIDC caller of the same path has
   a precedent.
4. **`DevelopmentFixtureSeeder`** seeds firm "Example Firm A" and one synthetic
   review record, idempotently, and is invoked only from the
   `app.Environment.IsDevelopment()` branch of `Program.cs`. It creates no user,
   membership, session, or credential, and existing tests assert that.
5. **`IntakeUploadValidator.Matches`** accepts exactly three signatures: `%PDF-`
   (five bytes), JPEG (`FF D8 FF`), and PNG (eight bytes).
6. **`DeterministicDocumentIntelligenceProvider.Parse`** decodes the whole
   source as strict UTF-8 and classifies as `Unsupported` unless the first
   non-empty line equals `TAXAI-DETERMINISTIC-DOCUMENT/1`. It is registered only
   in Development (`ModuleRegistration`, `isDevelopmentOrTestHost: true`).
   JPEG and PNG signatures are not valid UTF-8, so only the PDF signature can
   ever precede a deterministic payload.
7. **`T4SlipParser.ValidateRequiredBoxes`** requires Box 12 and Box 45 to be
   present, and `RecipientIdentifier` accepts any non-blank alphanumeric value.
   The parser checks presence only; it applies no nine-digit or checksum rule to
   Box 12. (Repository fact, read from source.)
8. **`ExtractionEndpoints`** maps eight routes. `MutationResult`,
   `ResolveDuplicateAsync`, and `FinalizeAsync` return
   `IntakeHttpSecurity.NotFound` for every unsuccessful workflow result,
   including in-scope `Invalid` and `Conflict` results. `ExtractionResultCode`
   is the closed set `Success`, `Invalid`, `NotFound`, `Conflict`. The spec 016
   T05 availability filter already implements "scope first, then a fixed non-404
   response" for in-scope requests. `IntakeEndpoints.MapFailure` already
   distinguishes `NotFound`, `Conflict`, and `Invalid` for accountant intake
   routes.
9. **`IIntakeStore`** has no list member. Its only firm-omitting members are
   `FindLinkScopeAsync` and `FindClientScopeAsync`.
10. **`tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs`** pins
    `IntakeEndpoints.cs`, `IntakeCaseCreatePage.razor`,
    `IntakeCaseIssuedPage.razor`, `IntakeCaseProgressPage.razor`,
    `ClientIntakeEndpoints.cs`, `IntakeHttpSecurity.cs`, the client pages, and
    the spec 012 review files byte-identical to a fixed merge commit, and pins
    `IntakeConfiguration.cs` to one approved line-exact diff.
    `tests/TaxAI.Portal.Tests/Upgrade/RouteAuthenticationBaselineTests.cs`
    enumerates the exact route, method, and authentication matrix.
11. **`PortalLayout.razor`** is shared by accountant pages and client pages
    (`ClientIntakePage`, `ClientLinkConfirmationPage`). Anything added to the
    layout is rendered to clients.
12. **`.gitignore`** already ignores `artifacts/`, `*.mdf`, `*.ldf`, `*.user`,
    and `DataProtection-Keys/`. Two suites forbid any tracked file name
    containing "secret".
13. **`tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs` is a ledger
    that names live guard test methods by name** (for example
    `PreservedSurfaceTests.ExistingIntakeTransportPoliciesCookieAndComponentsAreByteIdentical`).
    **An amended guard must therefore keep its existing test method names**; a
    renamed guard method breaks the ledger.

### Reused unchanged — do NOT re-specify, duplicate, or re-implement

| Existing capability | Location | Role in spec 017 |
|---|---|---|
| The whole tax chain: `T4SlipParser`, `T4LineMapper`, `IT4LineMapProvider`, `Mappings/T4LineMap.2025.json`, `MoneyRounding.ToCents`, `RecipientIdentifier`, `ParseResult<T>`, `ValidationError` | `src/TaxLogicEngine/` | Untouched. No file under this tree changes. |
| `TaxCase`, readiness, `DocumentCategory` | `src/TaxCases/` | Untouched. |
| `ExtractionEnvelopeValidator`, `IDocumentExtractionIngress` | `src/DocumentExtraction/` | Untouched. |
| `T4CaseProcessor` | `src/CaseProcessing/` | Untouched. |
| `ReviewPackageAssembler` | `src/CaseReview/` | Untouched. |
| `TrustedFirmId`, `IIdentityDirectory`, `IAuthenticationSessionStore`, `SessionLifetimePolicy`, `MembershipVerificationPolicy`, `ReviewPackageProjection`, `IReviewRecordRepository`, `RetentionPolicy`, `TaxpayerTextGuard` | `src/Tenancy/` | Reused. The Development sign-in uses the existing directory and session contracts; no new identity contract. |
| `IAccountantIntakeWorkflow`, `IClientIntakeWorkflow`, `IIntakeStore`, `IIntakeContentStore` (two members, write-only), `IntakeUploadValidator`, link/session/credential model | `src/ClientIntake/` | Reused. Exactly one additive firm-scoped list read is added (FR-30). The upload validator is unchanged. |
| `IExtractionIngestWorkflow`, `IExtractionReviewWorkflow`, `CorrectionField` (no Box 12 member), `ExtractionResultCode`, medical evidence model | `src/ExtractionWorkflow/` | Reused unchanged. |
| `DeterministicDocumentIntelligenceProvider`, `DocumentIntelligenceOptions` | `src/DocumentIntelligence/` | Reused; gains exactly the D-2 leading-signature tolerance (FR-21). `Production/*` is untouched. |
| `SqlIntakeStore`, `SqlExtractionStore`, `SqlReviewRecordRepository`, `SqlAuthenticationSessionStore`, `SqlIdentityDirectory`, `SqlRecoveryReconciler`, migrations, `DevelopmentFixtureSeeder`, `SqlConnectionStringGuard` | `src/Persistence/` | Reused. The guard gains the D-4 Development-local allowance; the seeder gains the D-1 synthetic user and membership; the store gains the FR-30 list read. The recovery reconciler is untouched. |
| `PortalAuthorization` (R-1), `SessionTicketStore`, `PortalIdentity`, `IntakeEndpoints` (six routes), `ClientIntakeEndpoints` (six routes), `ExtractionEndpoints` (eight routes), `ReviewEndpoints`, `DevelopmentEncryptedIntakeContentStore`, all existing components | `src/TaxAI.Portal/` | Reused. The twelve-step flow is assembled from these. |
| `scripts/Invoke-SolutionTests.ps1`, `scripts/test-projects.json`, `scripts/Assert-DotNet10Toolchain.ps1`, `.config/dotnet-tools.json` (repository-local `dotnet-ef`) | repo root | Reused. |

### Genuinely new

- A local setup-and-run tool (FR-1 to FR-8).
- A Development-only sign-in path and one seeded synthetic user plus membership
  (FR-9 to FR-16).
- A Development-local `TrustServerCertificate` allowance (FR-17 to FR-20).
- A leading-`%PDF-` tolerance in the deterministic Development provider, and a
  local sample generator (FR-21 to FR-29).
- One firm-scoped case-list read, route, and component (FR-30 to FR-38).
- Accountant navigation and fixed feedback notices (FR-39 to FR-45).
- A vertical-slice test through the real HTTP upload, and a demo guide
  (FR-46 to FR-49).

### Explicitly NOT created

No new project, test project, module, package reference, migration table, tax
type, parser, mapper, validator, envelope, review model, readiness calculator,
masking helper, SIN type, second session model, second authorization policy,
second upload validator, PDF parser, OCR component, JSON API, background
service, or accountant-side upload path. `TaxAI.sln` is not modified.

---

## Preserved boundaries (non-negotiable)

**P-1 — No SIN in any form** (full, masked, hashed, tokenized, last-three,
last-four, presence flag, or derived identifier) in any browser model, HTML,
URL, cookie, header, SQL column, audit event, log, metric, trace, exception,
diagnostic, tracked fixture, README, guide, or evidence file. The sole raw-byte
exception remains the encrypted source object behind `IIntakeContentStore`,
which is never described as SIN-free and is never rendered, logged, queried, or
served. Box 12 exists only in process memory for one extraction call.

**P-2 — Tenant isolation.** Firm scope comes only from the revalidated
server-side session's `taxai_firm_id` claim. No route, query, form, body, header,
or cookie value is an authorization boundary. No new firm-omitting taxpayer-data
operation is added.

**P-3 — Safe `404`.** A missing, foreign-firm, or inconsistent selector returns
the same empty `404` on every existing route, byte-identical between missing and
foreign.

**P-4 — One authorization model.** The Development sign-in creates a session
only through the existing cookie sign-in event, directory lookup, and durable
ticket store. Every protected request still passes the complete R-1 check.

**P-5 — The upload gate is unchanged.** `IntakeUploadValidator` is not edited.

**P-6 — The production boundary is unchanged.** No file under
`src/DocumentIntelligence/Production/`, `src/TaxAI.AzureInfrastructure/`, or
`src/TaxAI.Portal/Production/` changes. Production admission stays refused.
Development is never production authorization.

**P-7 — Tax behaviour is unchanged.** No file under `src/TaxLogicEngine/`,
`src/TaxCases/`, `src/DocumentExtraction/`, `src/CaseProcessing/`, or
`src/CaseReview/` changes. The UI computes no tax, implements no CRA mapping, and
holds no tax-year policy.

**P-8 — Append-only history, explicit human acceptance, T4-only review record,
neutral medical evidence** — all exactly as spec 015 left them.

**P-9 — Static SSR posture.** `UseStaticFiles()` middleware, never
`MapStaticAssets()`; no global render mode; `ClientQuestionnairePanel` remains
the only interactive component; no component declares `@page` or `@inject`.

**P-10 — The full gate stays green**, with no test removed, skipped, or
weakened, and no `minimumTests` floor lowered.

---

## User stories

1. As a **developer**, I run one command and get a running local TaxAI with a
   database, configuration, and sample documents, so I can demonstrate the
   product without reading six module instruction files.
2. As a **developer**, I sign in locally without an identity provider, and what I
   get is a normal session subject to the normal checks, so the demo exercises
   the real authorization path.
3. As an **accountant**, I see the cases of my firm and open one, so I do not
   need to remember an opaque identifier.
4. As an **accountant**, I can reach every step of the workflow by following
   links, so the product is usable rather than merely routable.
5. As a **client**, I upload a document through the real intake link, and the
   accountant can extract it, so the upload path and the extraction path are
   proven together.
6. As an **accountant**, when a correction or finalization is refused I am told
   that it was refused, so I do not mistake a refusal for a broken page.
7. As an **accountant**, I review and accept a client-uploaded medical receipt
   as neutral evidence, and it stays out of the T4 review record.
8. As a **taxpayer**, the Development conveniences do not exist in Production:
   no sign-in shortcut, no certificate bypass, no deterministic document format.
9. As a **taxpayer at firm X**, an accountant at firm Y cannot learn from the
   case list that my case exists.
10. As a **security reviewer**, no SIN-shaped value is committed to the
    repository, printed by tooling, or written to any structured surface.
11. As an **engine maintainer**, this feature changes no tax file, and a test
    says so.

---

## Functional requirements

`[D-n]` = rests on the numbered approved decision. `[approved]` = rests on an
explicit human approval recorded in this PRD. `[boundary]` = an existing
invariant restated. `[design]` = a Class B decision made here and tagged
`[ASSUMED]` in Open questions. `[reuse]` = an existing type used as-is.

### A. Local startup

**FR-1 `[design]`** One tracked PowerShell entry point SHALL prepare and start
the local application. `[ASSUMED]` path: `scripts/local/Start-LocalDemo.ps1`. A
single invocation SHALL perform, idempotently: toolchain check, database
preparation, configuration generation, sample generation, and host start in the
`Development` environment.

**FR-2 `[D-4]` `[boundary]`** The tool SHALL apply the repository-owned
migrations as an explicit developer action using the repository-local `dotnet-ef`
tool. **The application SHALL NOT migrate at startup**; no `Migrate()` or
`EnsureCreated()` call is added to any production project.

**FR-3 `[design]`** The tool SHALL generate, when absent, every setting the
Development host requires: the database connection string, three distinct
32-byte keys in the encoding `IntakeConfiguration` accepts, an absolute
`DevelopmentContentRoot` outside the web root, the HTTPS loopback
`PublicOrigin`, every intake lifetime, limit, and rate setting, the extraction
settings, and inert OIDC placeholder values. Generated values SHALL be stored
**outside the tracked tree**. `[ASSUMED]` store: the .NET user-secrets store,
enabled by a `UserSecretsId` property in `TaxAI.Portal.csproj` (no
`PackageReference` is added).

**FR-4 `[boundary]`** No tracked file SHALL gain a key, a connection string
value in a configuration file, or any secret. Existing tracked-configuration
guards remain in force unmodified. No tracked file name SHALL contain "secret".

**FR-5 `[design]`** A second run of the tool SHALL NOT regenerate existing keys
or the content root, because regenerated keys would make already-stored encrypted
content unreadable. Regeneration requires an explicit reset option.

**FR-6 `[boundary]`** The existing every-environment OIDC configuration
requirement in `PortalIdentity` is **not relaxed**. The local configuration
supplies inert placeholder values; `GET /sign-in` remains mapped and unchanged.

**FR-7 `[design]`** The Development launch profile SHALL bind an HTTPS loopback
address in addition to or instead of the current HTTP one, using the standard
.NET SDK HTTPS development certificate. The HTTPS `PublicOrigin` rule and the
`Secure` cookie policy are **not relaxed**. No non-loopback binding is added.

**FR-8 `[boundary]`** The tool SHALL print no key, no connection string, and no
Box 12 value. It SHALL print the local URL of the Development sign-in page and
the directory of the generated samples.

### B. Development identity

**FR-9 `[D-1]`** The host SHALL map a Development sign-in at exactly one path,
`[ASSUMED]` `/development/sign-in`, with two methods: `GET` renders a fixed page
containing only an antiforgery-protected form; `POST` performs the sign-in. Both
SHALL be mapped **only** when the host environment name equals `Development` by
ordinal comparison.

**FR-10 `[D-1]` `[boundary]`** The `POST` SHALL sign in to the existing cookie
scheme with a principal carrying only the seeded user's issuer and subject, so
that the existing `OnSigningIn` resolution, `IIdentityDirectory` lookup, firm
claim rule, and `SessionTicketStore` create the session. It SHALL NOT construct
a local principal itself, add a claim, issue a firm claim, write a session row
directly, register an authentication scheme, add an authorization policy, or
mark any endpoint as exempt.

**FR-11 `[D-1]`** The `POST` SHALL accept no input other than the antiforgery
token. It SHALL read no user, firm, membership, or return-URL value from any
request channel. Its success redirect target is a compile-time constant, the case
list.

**FR-12 `[D-1]` `[boundary]`** The Development sign-in SHALL fail closed exactly
as an OIDC sign-in does when the seeded user is absent or inactive: no session,
no cookie, a fixed non-interpolated failure.

**FR-13 `[D-1]`** In Development only, seeding SHALL additionally create,
idempotently, exactly one synthetic user and exactly one active membership of
that user in the existing seeded firm. The user carries a fixed synthetic issuer
and subject and **no credential of any kind**. Exactly one membership exists so
that the existing rule issues the firm claim without a firm-selection step.

**FR-14 `[D-1]` `[boundary]`** Seeding SHALL remain reachable only from the
existing Development-gated invocation. No migration SHALL insert a user, firm,
membership, or session. No seeded identity exists in a database that only had
migrations applied.

**FR-15 `[D-1]`** Outside Development the path SHALL be absent from the endpoint
inventory, and a request to it SHALL be indistinguishable from a request to any
unmapped path.

**FR-16 `[boundary]`** Sign-out, session lifetime, per-request user and
membership revalidation, and server-side revocation SHALL apply to a
Development-sign-in session exactly as to an OIDC session.

### C. Local SQL

**FR-17 `[D-4]`** A connection string carrying `TrustServerCertificate=True`
SHALL be accepted only when **both** hold: (a) the host environment name equals
`Development` by ordinal comparison, and (b) the data source is a loopback
target — LocalDB, or a SQL Server instance addressed by a loopback name or
address.

**FR-18 `[D-4]` `[boundary]`** Every other rule of `SqlConnectionStringGuard`
SHALL remain in force on that path: explicit `Encrypt=True` is still required,
`HostNameInCertificate` is still refused, `Persist Security Info` is still
refused, and the authentication-mode rules are unchanged.

**FR-19 `[D-4]` `[boundary]`** The strict behaviour SHALL remain the default.
The allowance SHALL be an explicit opt-in supplied by the host, and only the
Development host may supply it. `Persistence` SHALL NOT read the environment
itself. Outside Development, `PortalStartupGuards`, `SqlConnectionStringGuard`,
and `ProductionConfigurationValidator` SHALL each refuse
`TrustServerCertificate=True` exactly as today.

**FR-20 `[design]`** The default local database is LocalDB `[ASSUMED]`. A
developer MAY instead supply the connection string of any SQL Server instance
that satisfies FR-17. No local certificate authority or certificate provisioning
is performed for SQL.

### D. Deterministic upload bridge and local samples

**FR-21 `[D-2]`** `DeterministicDocumentIntelligenceProvider` SHALL additionally
recognize a source whose first non-empty line begins with the PDF signature
`%PDF-` and whose **next** non-empty line is exactly the existing header
`TAXAI-DETERMINISTIC-DOCUMENT/1`. The remainder is read exactly as today. The
existing header-first form remains recognized.

**FR-22 `[D-2]` `[boundary]`** Nothing else about the provider changes. A source
that begins with `%PDF-` and is not followed by the header classifies as
`Unsupported`. The provider SHALL NOT parse PDF structure, read a PDF text
layer, perform OCR, or infer anything from a real PDF. Strict UTF-8 decoding of
the whole source is unchanged, so a JPEG or PNG source remains `Unsupported`.
**The `%PDF-` deterministic payload convention is Development-only and SHALL
NOT become a production PDF interpretation rule.**

**FR-23 `[D-2]` `[boundary]`** `IntakeUploadValidator` SHALL NOT be edited. It
gains no signature, no text media kind, and no Development branch.

**FR-24 `[D-2]` `[boundary]`** No file under
`src/DocumentIntelligence/Production/` SHALL change, and no production code
SHALL name the deterministic header. The deterministic provider remains
registered in Development and test hosts only; a production profile naming it is
still refused.

**FR-25 `[D-3]`** The local tool SHALL generate one synthetic T4 sample and one
synthetic medical-receipt sample into an untracked, git-ignored directory
`[ASSUMED]` `artifacts/local-demo/`, each beginning with a `%PDF-` line followed
by the deterministic header, each with a `.pdf` file name. The samples are
deterministic-format text, not viewable PDFs; the guide says so.

**FR-26 `[D-3]`** The T4 sample SHALL print tax year 2025, carry display names
that contain the word "Synthetic", and contain exactly the fields the existing
2025 `T4SlipParser` requires plus at least one box for which the existing
`T4LineMap.2025.json` yields a line entry. Which box that is SHALL be determined
by reading the existing configuration; **this PRD asserts no box-to-line fact.**

**FR-27 `[D-3]`** The Box 12 value in the T4 sample SHALL be produced at
generation time by the tool, **only at runtime and only into ignored local
artifacts**. It SHALL NOT be a literal or a constant in any tracked file, SHALL
NOT be printed, logged, or echoed, and SHALL exist only in the generated
untracked file. The parser requires presence only; this PRD requires and asserts
no checksum or number-range property.

**FR-28 `[D-3]` `[boundary]`** **No SIN-shaped literal SHALL be committed in
source, fixture, docs, evidence, or logs.** No tracked fixture, test source,
README, guide, log, evidence file, or other repository artifact SHALL contain a
generated Box 12 value. Tests that need a planted value SHALL construct it at
run time. Existing SIN/PII guards are not edited, narrowed, or removed.

**FR-29 `[boundary]`** The medical sample SHALL contain only the closed spec 015
medical field set (service date, gross amount, currency, provider display name,
patient display name, receipt reference), synthetic values, and no nine-digit
run.

### E. Case list

**FR-30 `[D-5]`** `ClientIntake` SHALL declare exactly one additive firm-scoped
list read whose first parameter is a `TrustedFirmId`, with no overload, optional
argument, or default that omits it. `FindLinkScopeAsync` and
`FindClientScopeAsync` remain the only firm-omitting members of `IIntakeStore`.
`SqlIntakeStore` implements it with a firm-leading query. No existing member
changes. No table is added; an index, if needed, arrives only in a new additive
migration.

**FR-31 `[D-5]`** The host SHALL map `GET /intake-cases` `[ASSUMED]` under the
existing `PortalAuthorization.FirmScopedPolicy`, returning a statically
server-rendered component.

**FR-32 `[D-5]` `[boundary]`** The firm SHALL come only from
`PortalAuthorization` request state. The route SHALL accept no route value and
SHALL read no query, form, body, header, or cookie value as scope, search,
filter, sort, or paging input.

**FR-33 `[D-5]`** The browser allowlist of the case list is **exactly**: the
primary taxpayer display name and the tax year of each case. The opaque case
identifier appears only inside link targets to existing case routes, exactly as
it already does on existing accountant pages. Nothing else is rendered: no firm,
participant, document, user, membership, or session identifier, no instant, no
count, no status, and no SIN in any form.

**FR-34 `[D-5]`** The list SHALL contain only cases of the trusted firm. Its
content SHALL NOT vary with the existence, number, or content of any other firm's
cases.

**FR-35 `[design]`** The list SHALL be ordered newest first by stored creation
instant (not rendered) and bounded to a fixed maximum, `[ASSUMED]` 100 cases.
When more exist, one fixed notice says so. There is no search, filter, or caller
paging.

**FR-36 `[boundary]`** An unauthenticated request, a session with no active
firm, an inactive user, an inactive membership, and a revoked session SHALL each
receive the existing refusal and SHALL NOT reach the store. A store failure SHALL
produce the existing fixed error and no partial list.

**FR-37 `[boundary]`** The response SHALL carry the existing intake no-store,
no-referrer, nosniff, and local-only CSP headers. The surface is not a JSON API.

**FR-38 `[design]`** A firm with no case SHALL see an explicit empty state with a
link to create a case.

### F. Navigation and feedback

**FR-39 `[design]`** Every step of the target flow SHALL be reachable by
following a rendered link or submitting a rendered form. The only URL a human
copies is the one-time client link shown on the existing issued page. At
minimum:

- Development sign-in leads to the case list;
- the case list links to the create page, and for each case to its intake
  progress page and to its documents (extraction list) page;
- the intake progress page links to the same case's documents page;
- the documents page and the extraction-review page link back to the case list;
- an accountant page offers the existing sign-out as a form.

**FR-40 `[boundary]`** No accountant link, case identifier, or accountant control
SHALL be rendered on any client page. Because `PortalLayout` is shared with
client pages, accountant navigation SHALL NOT be added unconditionally to it.

**FR-41 `[boundary]`** The spec 012 review surface (`ReviewEndpoints.cs`,
`ReviewPage.razor`, `ReviewPage.razor.css`, `ReviewPageModel.cs`), `/health`, and
the root shell `/` SHALL remain byte-identical.

**FR-42 `[approved]` (D-7; approved narrowly by the human on 2026-10-01)**
After authentication, live-session validation, membership validation, firm
scope, and case/document ownership have **all** succeeded, an allowed accountant
**correction** or **case-finalization** operation that is refused by the
existing workflow/domain validation MAY return a fixed, value-free user notice
instead of the historical empty `404`.

- **Only those two operations change.** No other extraction mutation — extract,
  classification override, accept, duplicate resolution — changes its response.
- **Safe-`404` behaviour is unchanged for:** missing resources; foreign-firm
  resources; invalid or foreign selectors; deleted, revoked, or inconsistent
  resources where revealing the state would create an existence or authorization
  signal; and failed session or membership validation.
- A workflow result of `NotFound` on either operation therefore keeps the
  existing empty `404`.

**FR-43 `[approved]` `[boundary]`** A notice SHALL be drawn only from the closed
operation/result vocabulary approved by this specification, which is **exactly
four entries**: {correction, case finalization} × {`ExtractionResultCode.Invalid`,
`ExtractionResultCode.Conflict`}. Each entry is one fixed, non-interpolated
text. A notice SHALL NOT include taxpayer values, SIN information, box values,
provider or OCR text, exception text, SQL details, identifiers not already
visible to the authorized user, or interpolated internal error messages. It
SHALL NOT be selected by a field name, box code, or engine message, and no
notice SHALL identify Box 12 or state or imply whether a SIN was present. An
unknown notice selector renders nothing; arbitrary text is never reflected.

**FR-44 `[boundary]`** A refused operation SHALL still mutate nothing. Feedback
changes only what the accountant is told.

**FR-45 `[boundary]`** The existing spec 016 T05 availability response (`503`
with its fixed text) and its "scope first" ordering are unchanged.

### G. Medical evidence through the same path

**FR-46 `[boundary]`** The medical sample SHALL travel the same client upload
route and the same extraction, correction, and acceptance routes as today.
Medical evidence remains neutral: no eligibility, claimant, period, threshold,
line, or tax decision; it does not participate in finalization and never enters
the review record. `SlipTypeCodes.All` stays exactly `["T4"]`.

### H. Vertical-slice proof and demo guide

**FR-47 `[D-2]` `[D-6]`** An automated test SHALL drive the complete target flow
against the real portal composition in a Development host: Development sign-in,
case list, case creation, link issuance, client redemption with a separate
cookie container, answers, **a real multipart `POST` to the existing client
upload route**, submission, extraction, correction, acceptance, finalization,
and `GET /reviews/{recordId}`. It SHALL NOT stage bytes directly into the
content store.

**FR-48 `[design]`** A tracked demo guide `[ASSUMED]` `docs/local-demo.md` SHALL
describe prerequisites, the one command, and the twelve steps, and SHALL state
that the samples are synthetic deterministic-format files, that real taxpayer
documents must not be used locally, and that a local run is not production
authorization. It SHALL contain no key, credential, or nine-digit run.

**FR-49 `[design]`** The root `CLAUDE.md` "Current state" section and the
`CLAUDE.md` files of `TaxAI.Portal`, `Persistence`, `DocumentIntelligence`, and
`ClientIntake` SHALL be updated in the task that changes each boundary, so the
narrowed portal gate records the case list and the Development sign-in and still
forbids every other new taxpayer-data surface.

---

## Authorized amendments (approved)

Existing guards pin today's surface. Each row below passes on `master` today and
will fail once this feature ships. **The human approved this table on
2026-10-01 exactly as listed, and nothing else**, under these conditions:

- `/plan` SHALL assign those exact files narrowly to the spec 017 task that
  causes the downstream change.
- The authorization covers **only** the reported categories: route baseline
  tests and portal route-count documentation; `PreservedSurfaceTests.cs` for
  `IntakeCaseProgressPage.razor`, and for `IntakeEndpoints.cs` only if the
  case-list route is actually implemented there; component inventory and
  production-file guards; the environment-token allowlist; development seeder
  assertions; connection-string guard tests; the `ClientIntake` contract
  inventory; extraction-route tests whose historical in-scope refusal assertion
  must change under the approved FR-42 rule; and the README capability
  inventory.
- **Unrelated guarantees are not weakened.**
- **Any required file or guard outside the approved table stops implementation
  and comes back for explicit authorization.**

Every amendment SHALL follow the spec 016 T01 precedent: the guard is kept and
re-pinned to the exact approved change (line-exact or exact-set), never deleted,
never loosened to a pattern. Because `AmendmentAuditTests.cs` names live guard
test methods, **an amended guard keeps its existing test method names**. The
extraction-route row is bounded by FR-42: only assertions about an in-scope
refused **correction** or **case finalization** may change.

| Guard | Why it must change | Required replacement |
|---|---|---|
| `Upgrade/RouteAuthenticationBaselineTests.cs` and the exact route counts in `src/TaxAI.Portal/CLAUDE.md` | `GET /intake-cases` is new in every environment; the Development sign-in path is new in Development only | The exact matrix plus exactly the new entries, with the Development sign-in asserted present in Development and absent in Production and Staging |
| `Extraction/PreservedSurfaceTests.cs` — `IntakeCaseProgressPage.razor` byte identity | FR-39 adds navigation links | Byte identity replaced by a line-exact approved diff containing only the added links |
| `Extraction/PreservedSurfaceTests.cs` — `IntakeEndpoints.cs` byte identity | Only if `/plan` places the case-list route in this file | Preferably avoided by a separate file; otherwise a line-exact approved diff |
| Exact component-inventory assertion (`BlazorBoundaryTests.cs`) | New case-list, sign-in, navigation, and notice components | Exact set plus exactly the named new components; still no `@page`, `@inject`, or render mode |
| Exact production-file lists (`PortalBoundaryTests.cs`, `PortalSecurityTests.cs`) | New portal source files | Exact list plus exactly the new files |
| Environment-token allow-list (`CompositionRootTests.cs`, `PortalSecurityTests.cs`) | The Development sign-in mapping is a new approved Development-gated concern | The enumerated approved concerns plus exactly this one; still never a negated environment check around a control |
| Seeder assertions (`Persistence.Tests/Composition/DevelopmentSeederTests.cs`, `Tenancy` contract text) | The seeder now creates one synthetic user and membership | Assert exactly one user and one membership, synthetic, credential-free, idempotent, Development-only |
| `Persistence.Tests/Composition/ConnectionStringGuardTests.cs`, `PersistenceBoundaryTests.cs` | The Development-local allowance exists | Every existing refusal kept on the default path; the allowance asserted only with the explicit opt-in and a loopback data source |
| `ClientIntake.Tests` public-contract inventory | One additive list member and its result shape | Exact inventory plus exactly the new member and type |
| Extraction route tests asserting an empty `404` for an **in-scope refused** mutation | FR-42 | The fixed notice for in-scope refusals; every missing/foreign `404` assertion kept unmodified |
| `DocumentIntelligence.Tests` assertions that a `%PDF-` first line followed by the header is `Unsupported` (if any exist) | FR-21 | Recognized; every other `Unsupported` case kept |
| `README.md` capability-inventory test (`RepositoryVerificationTests.cs`) | New delivered scope | Exact inventory extended; still unable to claim deployment, export, or accountant upload |

No amendment to any `TaxLogicEngine.Tests`, `TaxCases.Tests`,
`DocumentExtraction.Tests`, `CaseProcessing.Tests`, or `CaseReview.Tests` file
is requested or permitted.

---

## Out of scope

- Azure deployment; recovery regions; failover; production WAF or gateway work;
  private-endpoint expansion; production RTO/RPO; operational drills; scaling;
  production-cost optimization; further infrastructure hardening; spec 016 T07
  to T10; any DEP evidence item.
- Accountant-side document upload (D-6).
- Review decision or approval UI; sign-off; export; print; email; T1
  calculation; any tax calculation.
- Any change to tax semantics, a parser rule, a mapping, rounding, or a tax-year
  rule. **Any accidental change to tax behaviour is out of scope and is a
  defect.**
- A production PDF parser, OCR, or any real-document recognition; any change to
  the production Document Intelligence adapter.
- A local OIDC provider or container; any relaxation of the OIDC configuration
  requirement.
- Case search, filter, sort, caller paging, cross-firm or global listing, and any
  case-list field beyond FR-33.
- Firm, user, or membership provisioning outside the one Development seed; a
  second seeded user or firm; changing firm selection's `204` response.
- Changing the root shell `/`, `/health`, or the spec 012 review surface.
- A feedback notice for any operation other than correction and case
  finalization, or for any refusal outside FR-42.
- Source preview, download, or content serving; expansion of the review or
  extraction-review browser allowlists.
- Post-finalization reopening or correction.
- Tax years other than 2025.
- Automatic migration at application startup.
- Redesign of any working spec 011 to 016 infrastructure or security module.

---

## Acceptance criteria

Each criterion is one mechanically checkable Given/When/Then test. "Baseline"
means the planning baseline `7ee2574`.

### Primary vertical slice

**AC-1 — The T4 flow completes over real HTTP.** Given a Development host built
from the real composition and a T4 source constructed at run time in the bridge
format, When a test performs Development sign-in, opens the case list, creates a
case, redeems the issued link with a separate cookie container, answers, uploads
the source by multipart `POST` to the existing client upload route, submits,
then as the accountant extracts, corrects one non-SIN box value, accepts,
finalizes, and follows the redirect, Then `GET /reviews/{recordId}` returns `200`
and renders the corrected box value and at least one line entry.

**AC-2 — The medical flow completes over real HTTP.** Given the same host and a
medical source in the bridge format, When it is uploaded through the client
route, extracted, and accepted, Then the extraction-review page shows the
medical panel, and after T4 finalization the review record and
`/reviews/{recordId}` contain no medical field.

**AC-3 — Medical evidence does not gate finalization.** Given a case with one
accepted T4 and one unaccepted medical document, When the accountant finalizes,
Then finalization succeeds exactly as it does without the medical document.

**AC-4 — No direct content staging.** Given the vertical-slice test source, When
it is scanned, Then it names no `IIntakeContentStore`, `StageAsync`, or content
store type.

**AC-5 — Navigation is complete.** Given the host of AC-1, When a test walks the
flow using only `href` and form `action` values taken from rendered responses
(plus the one-time client link taken from the issued page), Then every step from
sign-in to `/reviews/{recordId}` is reached without constructing a URL.

### Local startup

**AC-6 — Generated configuration starts a Development host.** Given the
configuration the local tool produces, captured into a temporary store, When a
Development host is built with it, Then `PortalStartupGuards.RequireSafeStartup`
and intake and extraction validation all pass.

**AC-7 — Idempotent setup.** Given a completed setup, When the tool's setup step
runs again without the reset option, Then the three keys and the content root
are unchanged.

**AC-8 — The three generated keys are distinct.** Given a fresh setup, When the
generated digest, fingerprint, and content-encryption keys are compared, Then no
two are equal.

**AC-9 — Nothing sensitive is tracked.** Given a completed setup and sample
generation in a clean working tree, When `git status --porcelain` is read, Then
it is empty.

**AC-10 — The tool prints no sensitive value.** Given a captured run of the
tool, When its standard output and error are scanned, Then they contain no
generated key, no connection string, and no nine-digit run.

**AC-11 — No startup migration.** Given every production project's source, When
it is scanned, Then no `Migrate(`, `MigrateAsync(`, or `EnsureCreated` call
exists.

**AC-12 — Loopback binding only.** Given the tracked launch settings, When their
application URLs are read, Then every one is a loopback address.

**AC-13 — OIDC requirement unchanged.** Given a Development host with no
`TaxAi:Oidc:Authority`, When startup runs, Then it refuses with the existing
fixed message.

### Development sign-in — positive

**AC-14 — It uses the durable session path.** Given a seeded Development host,
When `POST` to the Development sign-in succeeds, Then exactly one new durable
session row exists for the seeded user with the seeded firm as active firm, and
the browser cookie is the existing opaque-key session cookie.

**AC-15 — The principal is the normal one.** Given AC-14's session, When the
next protected request's principal is inspected, Then its claim set is exactly
the existing local user claim and firm claim and nothing else.

**AC-16 — R-1 still applies.** Given AC-14's session, When the seeded membership
is disabled and a firm-scoped route is requested, Then the request is refused by
the existing R-1 check and the store behind the route is not called.

**AC-17 — Sign-out revokes.** Given AC-14's session, When sign-out is posted and
the earlier cookie is replayed, Then the replay is refused.

**AC-18 — Seeding is exact and idempotent.** Given a Development database seeded
twice, When the identity tables are read, Then exactly one seeded user and
exactly one seeded membership exist, the membership is active, and no row holds
a credential or token.

### Development sign-in — fail-closed

**AC-19 — Absent in Production.** Given a host built with environment
`Production`, When its endpoint inventory is enumerated, Then no endpoint has
the Development sign-in path.

**AC-20 — Absent in Staging.** Given a host built with environment `Staging`,
When its endpoint inventory is enumerated, Then no endpoint has the Development
sign-in path.

**AC-21 — Indistinguishable from unmapped outside Development.** Given a
non-Development host, When `GET` and `POST` are sent to the Development sign-in
path and to an unmapped path, Then the paired responses have identical status,
headers, and body, and no session row is created.

**AC-22 — No seeding outside Development.** Given a non-Development host start
against a migrated database, When the identity tables are read, Then no user,
membership, or session row exists.

**AC-23 — No seed in any migration.** Given every migration file, When scanned,
Then none inserts a firm, user, membership, or session row.

**AC-24 — Antiforgery required.** Given a Development host, When the sign-in
`POST` is sent without a valid antiforgery token, Then it is refused and no
session row is created.

**AC-25 — No caller input is honoured.** Given a Development host, When the
sign-in `POST` carries a user identifier, a firm identifier, and a return URL in
the query, form, and headers, Then the session is for the seeded user and firm
and the redirect target is the constant case-list path.

**AC-26 — Inactive seeded user fails closed.** Given a Development database whose
seeded user is disabled, When the sign-in `POST` is sent, Then no session row
and no session cookie are created.

**AC-27 — No parallel model.** Given the portal source, When it is scanned, Then
the Development sign-in file constructs no cookie-scheme principal, adds no
authentication scheme or authorization policy, names no session store, and
writes no claim other than issuer and subject.

### Local SQL — fail-closed

**AC-28 — Accepted on the Development loopback path.** Given environment
`Development`, the explicit allowance, and a loopback data source with
`Encrypt=True;TrustServerCertificate=True`, When the guard runs, Then the
connection string is accepted.

**AC-29 — Refused in Production.** Given environment `Production` and
`TrustServerCertificate=True`, When startup runs, Then it refuses with the
existing fixed message.

**AC-30 — Refused in Staging.** Given environment `Staging` and
`TrustServerCertificate=True`, When startup runs, Then it refuses with the
existing fixed message.

**AC-31 — Refused by default.** Given `SqlConnectionStringGuard` invoked without
the explicit allowance, When a string with `TrustServerCertificate=True` is
supplied, Then it is refused exactly as at baseline.

**AC-32 — Refused for a non-loopback data source.** Given environment
`Development`, the allowance, and a non-loopback data source with
`TrustServerCertificate=True`, When the guard runs, Then it refuses.

**AC-33 — Explicit encryption still required.** Given the Development loopback
path with `TrustServerCertificate=True` and no explicit `Encrypt=True`, When the
guard runs, Then it refuses.

**AC-34 — Host-name override still refused.** Given the Development loopback path
with a `HostNameInCertificate` value, When the guard runs, Then it refuses.

**AC-35 — Production validator unchanged.** Given
`src/TaxAI.AzureInfrastructure/`, When compared with baseline, Then no file
differs.

### Deterministic bridge — positive and fail-closed

**AC-36 — The bridge format passes the unchanged gate and extracts.** Given a
source beginning with a `%PDF-` line followed by the deterministic header and a
T4 body, When it is uploaded as PDF through the client route and extracted, Then
the upload is accepted and the automated classification is T4.

**AC-37 — The upload gate is unchanged.** Given
`src/ClientIntake/Services/IntakeUploadValidator.cs`, When compared with
baseline, Then it is byte-identical.

**AC-38 — Tax-year isolation through the real path.** Given a 2025 case and a
bridge-format T4 printing year 2024, When it is uploaded and extracted, Then the
existing year-coherence error results, no processed extraction or review record
is produced, and neither year is coerced.

**AC-39 — Header without a signature is still rejected at upload.** Given a
source beginning directly with the deterministic header and declared as PDF,
When it is uploaded through the client route, Then the unchanged gate rejects it
and no document record exists.

**AC-40 — A PDF signature without the header is unsupported.** Given a source
beginning with `%PDF-` and not followed by the header, When it is classified,
Then the result is `Unsupported` and no T4 payload, medical version, or review
record is produced.

**AC-41 — The header must immediately follow the signature line.** Given a
source with `%PDF-`, then another non-empty line, then the header, When
classified, Then the result is `Unsupported`.

**AC-42 — Image signatures gain no bridge.** Given sources beginning with the
JPEG and PNG signatures followed by the header bytes, in separate tests, When
classified, Then each is `Unsupported`.

**AC-43 — The legacy form still works.** Given a header-first source, When
classified, Then the result is unchanged from baseline.

**AC-44 — The production adapter is untouched.** Given
`src/DocumentIntelligence/Production/`, When compared with baseline, Then no file
differs, and no file in it contains the deterministic header text.

**AC-45 — No parsing dependency.** Given `DocumentIntelligence.csproj`, When its
package references are read, Then the set is exactly the baseline set.

**AC-46 — The Development provider is still refused in Production.** Given a
production profile naming the deterministic provider, When admission runs, Then
`DevelopmentProviderConfigured` is among the refusals.

**AC-47 — Empty slip fails closed through the bridge.** Given a bridge-format T4
with no box line, When uploaded, extracted, and accepted, Then acceptance is
refused and no review record is created.

**AC-48 — Missing required box fails closed without a SIN signal.** Given a
bridge-format T4 with no Box 12 line, When uploaded, extracted, and accepted,
Then acceptance is refused, and the response contains no text naming Box 12 or
SIN.

**AC-49 — Zero amount and absent optional boxes pass unchanged.** Given a
bridge-format T4 with a zero amount in one box and no optional box, When
uploaded, extracted, accepted, and finalized, Then the rendered review values
equal the values produced by invoking the existing engine chain directly on the
same payload.

### Samples and SIN hygiene

**AC-50 — Samples are ignored by git.** Given the generated sample directory,
When `git check-ignore` is run on each generated file, Then each is ignored.

**AC-51 — The generated Box 12 value is in no tracked file.** Given a generated
T4 sample, When every tracked file is searched for its Box 12 value, Then no
match exists.

**AC-52 — The generator holds no nine-digit literal.** Given the tracked tooling
source, When scanned, Then it contains no nine-digit run.

**AC-53 — The generated sample is usable.** Given the generator run into a
temporary directory, When its T4 output is classified by the deterministic
provider and its payload parsed by the existing parser, Then classification is
T4 and parsing succeeds; and the medical output classifies as medical evidence.

**AC-54 — The medical sample carries no SIN shape.** Given the generated medical
sample, When scanned, Then it contains no nine-digit run.

**AC-55 — Documentation carries no SIN shape.** Given the demo guide, `README.md`,
and every file under `specs/017-local-end-to-end-accountant-mvp/`, When scanned,
Then none contains a nine-digit run.

**AC-56 — No SIN in any structured surface, end to end.** Given AC-1 run with a
Box 12 value constructed at run time, When every response body, URL, cookie,
header, captured log entry, and every column of every SQL row written during the
flow is captured, Then none contains the value, a separator variant, a masked or
partial rendering, or a nine-digit run.

**AC-57 — Existing SIN/PII guards are intact.** Given the baseline's nine-digit
and SIN guard tests, When the suite runs, Then each still exists under its
baseline name and passes, and no file containing one differs from baseline
except by an amendment listed in this PRD.

### Case list — positive and fail-closed

**AC-58 — The list shows the firm's cases.** Given firm F with two cases, When
its accountant requests the list, Then both appear, each with its display name
and tax year and links to its progress and documents pages.

**AC-59 — The allowlist is exact.** Given the case-list browser model type, When
its properties are enumerated, Then they are exactly the display name, the tax
year, and the case selector used for links.

**AC-60 — Firm isolation.** Given cases in firms F and G, When F's accountant
requests the list, Then no G case appears.

**AC-61 — No cross-firm existence signal.** Given F's list response, When a case
is then created in G and F's list is requested again, Then the rendered case
rows of the two F responses are identical.

**AC-62 — Caller-supplied firm is ignored.** Given a firm-F session and a request
naming firm G in the query, a form field, a header, and a cookie, When the list
is requested, Then only F cases appear and no firm-G store call occurs.

**AC-63 — Unauthenticated is refused before the store.** Given no session, When
the list is requested, Then the existing `401` is returned and the store is not
called.

**AC-64 — No active firm is refused before the store.** Given a session with no
active firm, When the list is requested, Then the existing refusal is returned
and the store is not called.

**AC-65 — Revoked session and inactive membership are refused before the
store.** Given each condition in a separate test, When the list is requested,
Then the existing refusal is returned and the store is not called.

**AC-66 — Store failure fails closed.** Given a store that throws, When the list
is requested, Then the existing fixed error is returned and no case row is
rendered.

**AC-67 — No search or paging input.** Given a firm with three cases, When the
list is requested with arbitrary query parameters, Then the response is identical
to the request without them.

**AC-68 — The contract is firm-required.** Given the `ClientIntake` contracts,
When members are enumerated, Then the new list member takes a `TrustedFirmId`
first with no omitting overload, and the firm-omitting members of `IIntakeStore`
are still exactly `FindLinkScopeAsync` and `FindClientScopeAsync`.

**AC-69 — The bound holds.** Given a firm with one more case than the fixed
maximum, When the list is requested, Then exactly the maximum number of newest
cases and the fixed truncation notice are rendered.

**AC-70 — Empty state.** Given a firm with no case, When the list is requested,
Then `200` with the explicit empty state is returned.

**AC-71 — Safe headers and no JSON.** Given a list response, When inspected, Then
it carries the existing intake no-store, no-referrer, nosniff, and CSP headers
and an HTML content type.

**AC-72 — The stored year is rendered.** Given a stored case, When the list is
rendered, Then the tax year shown equals `StoredIntakeCase.TaxYear`, and the
portal source contains no tax-year literal.

### Navigation and feedback

**AC-73 — Clients see no accountant navigation.** Given every client response of
AC-1, When scanned, Then none contains a link to an `/intake-cases` or `/reviews`
path or to the case list.

**AC-74 — In-scope refused correction gives a notice.** Given an authenticated
accountant with a live session and active membership, a case and document owned
by the trusted firm, and a correction the existing workflow refuses with
`Invalid` or `Conflict`, When posted, Then the response carries the fixed
correction notice for that result code rather than an empty `404`, and no
version or correction row is appended.

**AC-75 — In-scope refused finalization gives a notice.** Given the same
preconditions and a case the existing workflow refuses to finalize with
`Invalid` or `Conflict`, When posted, Then the response carries the fixed
finalization notice for that result code, and no review record or association is
created.

**AC-76 — Safe `404` is unchanged.** Given each of the eight extraction routes,
When requested with a missing selector, a foreign-firm selector, and an invalid
selector, and separately with a revoked session and an inactive membership, Then
every response equals its baseline response — the missing and foreign responses
byte-identical empty `404`s — proven by the baseline tests unmodified.

**AC-76a — The other four mutations are unchanged.** Given an in-scope extract,
classification override, accept, and duplicate resolution that the existing
workflow refuses, in separate tests, When each is posted, Then each response is
identical to its baseline response and carries no notice.

**AC-76b — A `NotFound` workflow result keeps the `404`.** Given an in-scope
correction and an in-scope finalization for which the existing workflow returns
`NotFound`, in separate tests, When each is posted, Then each response is the
existing empty `404` and carries no notice.

**AC-77 — Notices are the closed approved vocabulary.** Given the notice
vocabulary type and a request carrying an unknown or arbitrary notice selector,
When the vocabulary is enumerated and the page renders, Then the vocabulary has
exactly the four FR-43 entries, no notice is rendered for the unknown selector,
and the supplied text appears nowhere in the response.

**AC-78 — Notices carry no data.** Given every notice text and every notice
response of AC-74 and AC-75 run with planted taxpayer, box, and Box 12 values,
When inspected, Then each text is a non-interpolated constant, and no response
contains a planted value, a box code or box value, provider or OCR text,
exception or SQL text, an identifier not already rendered on the page before the
refusal, or the words "SIN" or "Box 12".

**AC-79 — Preserved surfaces are byte-identical.** Given the spec 012 review
files, `PortalContent.cs`, and the `/health` and `/` responses, When compared
with baseline, Then each is byte-identical.

### Route, component, and dependency inventory

**AC-80 — The route inventory is exact.** Given a Development host and a
Production host, When their endpoint inventories are enumerated, Then each
equals the baseline inventory plus exactly `GET /intake-cases`, and the
Development host additionally has exactly the Development sign-in path with
`GET` and `POST`.

**AC-81 — No accountant upload exists.** Given the route inventory, When
enumerated, Then no firm-scoped route accepts a document body, and the only
multipart route is the existing client upload route.

**AC-82 — Components stay presentation-only.** Given every component added or
changed, When scanned, Then none declares `@page`, `@inject`, or a render mode,
and `ClientQuestionnairePanel` is still the only interactive component.

**AC-83 — References and packages are unchanged.** Given every project file and
`TaxAI.sln`, When compared with baseline, Then no project reference, package
reference, or solution entry was added or removed (a `UserSecretsId` property is
not a reference).

**AC-84 — No background initiator.** Given the composition root, When inspected,
Then no hosted service, timer, or queue consumer is registered by this feature.

### Tax behaviour, data-driven mapping, and the gate

**AC-85 — No tax file changed.** Given `src/TaxLogicEngine/`, `src/TaxCases/`,
`src/DocumentExtraction/`, `src/CaseProcessing/`, and `src/CaseReview/`, and the
five matching test trees, When compared with baseline, Then no file differs.

**AC-86 — Rounding is untouched.** Given `MoneyRounding` and its existing
midpoint tests for both signs, When the gate runs, Then the source file is
byte-identical to baseline and those tests pass unmodified.

**AC-87 — No hard-coded CRA line or mapping.** Given every production file and
tooling file added or changed by this feature, When scanned, Then none contains a
CRA line-number literal, a box-to-line table, a read of a
`TaxLogicEngine.Mappings` resource, arithmetic on an amount, or a rounding call.

**AC-88 — The review output equals the engine's output.** Given the payload of
AC-1 after correction, When the values rendered at `/reviews/{recordId}` are
compared with the result of invoking the existing chain directly on the same
payload in the test, Then every box amount, line code, and line amount is equal.

**AC-89 — Spec 016 is untouched.** Given
`specs/016-canadian-azure-production-infrastructure/`, `infra/azure/`, and
`scripts/azure/`, When compared with baseline, Then no file differs and no new
status marker exists.

**AC-90 — Production admission is still refused.** Given the baseline production
admission tests, When the gate runs, Then they pass unmodified.

**AC-91 — The full gate is green.** Given `pwsh ./scripts/Invoke-SolutionTests.ps1`,
When it runs after this feature, Then every registered project runs, nothing is
skipped, nothing fails, and every project meets its floor.

**AC-92 — No floor is lowered.** Given `scripts/test-projects.json`, When
compared with baseline, Then every project's `minimumTests` is greater than or
equal to its baseline value and no project was removed.

**AC-93 — The demo guide exists and is clean.** Given the demo guide, When read,
Then it names the one command and the twelve steps and contains no key,
credential, or connection string.

---

## Planning hint (for `/plan`)

Keep the task plan small. **Four substantial tasks is the maximum**:

- **T01** — local startup, plus Development identity and session — FR-1 to
  FR-20;
- **T02** — real client upload to deterministic extraction bridge, plus
  generated samples — FR-21 to FR-29;
- **T03** — accountant case navigation and case list, plus workflow feedback —
  FR-30 to FR-45;
- **T04** — complete browser/HTTP vertical-slice verification, plus the demo
  guide — FR-46 to FR-49.

There is no infrastructure-hardening, Azure, recovery, deployment, or operations
task, and spec 016 T07 to T10 are not resumed.

Each task owns its tests, the approved guard amendments it causes, its floor
increases, and its instruction-file updates. Components, CSS, models, individual
endpoints, and documentation are not separate tasks. T01, T02, and T03 share
composition and guard files, so sequential ordering is expected; do not
manufacture parallelism.

---

## Open questions

### Blocking Class A questions

None. The Class A decisions were answered by the human and are recorded above.
No tax fact, PII exposure, tenant-isolation rule, or security boundary is assumed
in this document.

### Readings of approved decisions (confirmed by the human on 2026-10-01)

Each is the narrowest mechanical reading that satisfies every clause of an
approved decision. The human confirmed D-1 to D-5 as written. A wider reading
still needs the human.

- **D-1** "a route" is realized as one path with `GET` (fixed form) and `POST`
  (action), because every existing state-changing operation is a non-GET with
  antiforgery. Both are Development-only.
- **D-2** Only the `%PDF-` signature gains a bridge, because JPEG and PNG
  signatures are not valid UTF-8 and the provider decodes strictly.
- **D-3** The Box 12 value is generated at run time, because a value that may
  appear in no tracked artifact cannot be a tracked constant. The parser requires
  presence only, so no checksum or number-range property is required or asserted.
- **D-4** "Local database path" is read as: environment exactly `Development`
  **and** a loopback data source (LocalDB, `localhost`, `127.0.0.1`, `::1`, `.`,
  `(local)`).
- **D-5** "Minimum fields" is read as exactly display name and tax year, plus the
  case selector inside links.

Additional wording recorded from the human's review:

- Any `%PDF-` deterministic payload convention is **Development-only** and must
  not become a production PDF interpretation rule.
- **No SIN-shaped literal may be committed** in source, fixture, docs, evidence,
  or logs. The demo sample may generate the required synthetic value **only at
  runtime, into ignored local artifacts**.
- `TrustServerCertificate=True` is permitted **only in Development and only for
  the approved local database path**. Production and Staging remain fail-closed.

### Class B decisions accepted by the human for planning (2026-10-01)

- `[ASSUMED — accepted]` Entry script `scripts/local/Start-LocalDemo.ps1`.
- `[ASSUMED — accepted]` Local configuration in the .NET user-secrets store via
  a `UserSecretsId` in `TaxAI.Portal.csproj`.
- `[ASSUMED — accepted]` Default local database is LocalDB.
- `[ASSUMED — accepted]` Development sign-in path `/development/sign-in`.
- `[ASSUMED — accepted]` The seeded synthetic user joins the existing seeded
  firm "Example Firm A".
- `[ASSUMED — accepted]` Samples are generated, untracked, under
  `artifacts/local-demo/`.
- `[ASSUMED — accepted]` Case-list route `GET /intake-cases`, newest first,
  bounded to 100.
- `[ASSUMED — accepted]` The case-list read is an additive firm-scoped member on
  the existing `ClientIntake` contracts.
- `[ASSUMED — accepted]` Demo guide at `docs/local-demo.md`.
- `[ASSUMED — accepted]` No accountant-side upload; the real client
  one-time-link upload stays part of the demo.

### Remaining Class B decisions — `[ASSUMED]`, open to one-pass override

These were not named in the human's acceptance list and stay ordinary Class B
assumptions.

- `[ASSUMED]` Spec folder and slug: `specs/017-local-end-to-end-accountant-mvp/`.
- `[ASSUMED]` The entry script performs setup and start in one command and has
  an explicit reset option.
- `[ASSUMED]` OIDC settings are satisfied locally by inert placeholder values;
  the requirement itself is unchanged.
- `[ASSUMED]` HTTPS loopback binding uses the standard .NET SDK development
  certificate; the port is chosen by `/plan`.
- `[ASSUMED]` Development sign-in success redirects to the case list; no second
  firm or user is seeded.
- `[ASSUMED]` The medical sample is generated locally alongside the T4 sample.
- `[ASSUMED]` The case list shows a fixed truncation notice past the bound and
  does not render the creation instant.
- `[ASSUMED]` The wording of the four FR-43 notice texts and the carrying
  mechanism (re-render or closed-code redirect) are for `/plan`, within FR-42
  and FR-43. The scope and vocabulary themselves are human-approved, not
  Class B.
- `[ASSUMED]` The root shell `/` and firm selection's `204` are left unchanged;
  the tool prints the Development sign-in URL instead.
- `[ASSUMED]` Task ordering T01 → T02 → T03 → T04.
- `[ASSUMED]` The planning baseline is `7ee2574`, with spec 016 frozen after
  T06.

---

## CRA sources

**This PRD introduces no CRA or tax fact.** canada.ca was not consulted because
no box number, line number, deadline, rate, threshold, limit, gross-up factor, or
credit percentage is asserted here. Statements about Box 12 and Box 45 describe
what the existing `T4SlipParser` source does (repository facts); the CRA facts
behind those rules were verified and cited by spec 015 and are recorded in
`src/TaxLogicEngine/CLAUDE.md`. They are neither restated as claims nor relied
upon as a source by this document.

| Claim | Source URL | Tax year | Retrieved |
|---|---|---|---|
| (none — no tax fact is asserted) | — | — | — |

If implementation discovers that a tax fact is needed, it is a blocking Class A
question and must be verified against canada.ca before it enters any artifact.

---

## Definition of Done (PRD)

- [x] `specs/017-local-end-to-end-accountant-mvp/prd.md` exists and opens with
      `## Feature classification`.
- [x] Feature class named explicitly (`tax-domain`); the mixed feature is not
      classified `platform`.
- [x] Reuse check performed and documented.
- [x] Every acceptance criterion is mechanically checkable by a single test.
- [x] Zero Class A assumptions; the approved decisions are recorded and their
      readings confirmed by the human.
- [x] Every CRA fact has a canada.ca citation — vacuously, because none is
      asserted.
- [x] No source conflict was resolved by judgement — none arose.
- [x] Tax year named explicitly in Problem (2025 only).
- [x] Every fail-closed rule has its own negative acceptance criterion.
- [x] PII acceptance criteria present (AC-48, AC-51 to AC-57, AC-78).
- [x] Tax-year isolation acceptance criterion present (AC-38, AC-72).
- [x] Data-driven mapping acceptance criterion present (AC-87).
- [x] Existing tax behaviour and the full gate stay green with floors never
      lowered (AC-85, AC-86, AC-91, AC-92).
