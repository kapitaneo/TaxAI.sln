# CLAUDE.md — TaxAI.Portal

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the narrowed portal boundary
after specs 011, 012, and spec 014 T03. The gate is narrower than spec 010's
gate; it is not deleted.

## What this project is

`TaxAI.Portal` is the repository's single ASP.NET Core host and composition
root. It serves a fixed public shell and unchanged health response, delegates
authentication to an external OIDC provider, uses a revocable server-side
authentication session, verifies active firm membership on protected requests,
and exposes authenticated firm selection, one read-only accountant review
route, and the approved accountant/client intake flow.

`GET /reviews/{recordId:guid}` reads one immutable persisted review-package
version and renders only the closed review browser allowlist below. The intake
routes render only the closed intake projections and never reveal a firm, case,
participant, expected-document, session, persistence, or review identifier to a
client. Neither surface is a JSON API.

## The Blazor Web App and intake screens (spec 014 T03)

`Components/` holds the whole component tree: `App`, `Routes`,
`Layout/PortalLayout`, the review and intake pages, and the shared questionnaire,
checklist, and status components, plus `_Imports`. The set is asserted exactly.

- **Interactivity has one narrow owner.** Review and accountant screens remain
  static SSR. `ClientQuestionnairePanel` is the only component that declares
  `@rendermode InteractiveServer`, solely for local closed-code reveal. It
  invokes no service and receives only the client allowlist. Detailed errors and
  WebSocket compression are disabled, `blazor.web.js` is local, and there is no
  global render mode. The route inventory includes exactly four `/_blazor`
  endpoints plus `/_framework/opaque-redirect`.
- **No component is routable.** None declares `@page`. `ReviewPage` is returned
  as `RazorComponentResult<ReviewPage>` by a minimal endpoint that has already
  authenticated the caller, revalidated the local user and membership, resolved
  the trusted firm, and performed the one firm-scoped read. Routing a taxpayer
  page through the component router would move the safe-`404` and R-1 decisions
  into component discovery, where a refused request could render a partial page.
- **Components are presentation-only.** A component may accept typed parameters
  and rely on Razor's contextual encoding. It may **not** `@inject` anything, and
  may not name a repository, content store, EF type, trusted-firm resolver,
  authorization policy implementation, readiness or status calculator, fingerprint
  service, audit writer, parser, mapper, processor, review assembler, or
  `decimal`. It loads no data from a lifecycle method.
- **Parameters are closed.** Every screen and shared component has the exact
  parameter set declared by spec 014 architecture. No component may declare a
  `CaptureUnmatchedValues` dictionary — an attribute splat is an unbounded way
  for values to reach markup.
- **No endpoint assembles screen markup or an inline stylesheet.**
  `ReviewEndpoints.cs` contains no `StringBuilder`, appended markup, `<style>`
  block, or `HtmlEncoder`. `FirmEndpoints.cs` still assembles its pre-spec-014
  firm-selection page; it is the one named exception, and a second markup
  assembler fails the boundary suite.
- **Every asset is local and tracked.** `wwwroot/portal.css`,
  `wwwroot/intake.css`, component-scoped `.razor.css`, and the single
  collocated neutral-name upload module. No CDN, remote font, remote image,
  remote script, npm tree, or WebAssembly project.

### Static assets are middleware, not endpoints

The host calls `UseStaticFiles()` once, parameterless, in `Program.cs`, and
**deliberately does not call `MapStaticAssets()`**. The endpoint-routing variant
registers a `{**path:file}` catch-all over the entire URL space, which:

- turns every POST-only route's `405 Method Not Allowed` into a `404`, because
  the catch-all matches the path before method selection can reject it; and
- adds an endpoint that is neither explicitly anonymous nor
  authorization-required — the third state spec 011 forbids.

The route inventory therefore stays exact and closed: nineteen application
routes plus exactly five framework component endpoints. Only
`ClientQuestionnairePanel` needs the four `/_blazor` circuit endpoints; no
component has a global render mode.

## The ClientIntake HTTP boundary

Spec 014 T03 maps exactly six firm-scoped accountant routes and exactly six
anonymous client routes over the T02 workflows. Firm scope comes only from
`PortalAuthorization` request state. A raw one-time credential is accepted only
by the link confirmation GET and fixed-action redemption POST; successful
redemption issues a protected secure HTTP-only SameSite-Strict cookie scoped to
`/client-intake` and redirects to a clean URL.

Every mutation is a non-GET protected by antiforgery. Public, session, answer,
upload, and submission rate policies run before credential/session validity,
use safe keyed partitions, queue nothing, and return one fixed `429`. Invalid,
expired, revoked, spent, replayed, foreign, missing, or inconsistent resources
share one empty `404`. Intake responses use no-store, no-referrer, nosniff, and
local-only CSP headers.

Uploads use `MultipartReader`, configured count/byte limits, one discarded
received filename, and the literal client-side filename `upload.bin`; bytes
stream directly into the workflow. No `IFormFile`, `ReadFormAsync`, source
content response, preview, or download path exists.

## Dependencies point inward

At spec 014 the production project references were exactly five:
`TaxLogicEngine`, `CaseProcessing`, `ClientIntake`, `Tenancy`, and
`Persistence`. Spec 015 added `ExtractionWorkflow` and `DocumentIntelligence`.
Spec 016 T01 adds exactly one more, `TaxAI.AzureInfrastructure`, whose
production-profile validator and managed-identity probe the admission gate
consumes. The current set is exactly these eight:

- `TaxLogicEngine`;
- `CaseProcessing`;
- `ClientIntake`;
- `DocumentIntelligence`;
- `ExtractionWorkflow`;
- `Tenancy`;
- `Persistence`;
- `TaxAI.AzureInfrastructure`.

The portal declares no Azure, EF Core or telemetry package itself. It does not
directly reference `TaxCases`, `DocumentExtraction`, or
`CaseReview`. Those modules stay behind inward contracts, and no module may
reference `TaxAI.Portal`. All service registration remains rooted in
`ModuleRegistration.cs`.

## Identity, firm scope, and revocation

- External OIDC authenticates the caller. The portal stores no password and
  persists no provider token.
- The browser cookie carries only a Data-Protection-protected opaque ticket key.
  The durable server-side session is authoritative for local user identity,
  lifetime, revocation state, and active firm.
- A trusted firm is read only from the application-issued `taxai_firm_id` claim,
  which is rebuilt from the server-side session record. No firm identifier from
  a route, query, form, body, header, or cookie payload is an authorization
  boundary.
- A provider-supplied `taxai_firm_id` claim is always discarded and never
  trusted. Only the application may issue the firm claim after verifying a
  currently active local membership.
- Protected firm-scoped requests reload the local user and memberships and fail
  closed unless the user, membership, and session firm still agree.
- Sign-out revokes the session server-side as well as clearing the cookie. A
  copied pre-sign-out cookie cannot reuse or recreate a revoked session, and
  session-store failure fails closed.

## PII posture: no SIN in any form

No SIN or partial SIN may reach portal metadata, a response, URL, cookie, log,
metric, trace, error, browser model, or diagnostic. Not full, not masked, not
hashed, tokenized, last-three, last-four, or a SIN-derived display identifier.
The sole exception is the raw uploaded source byte stream and its authenticated
encrypted opaque object outside web root; source bytes may contain SIN but are
never read back, rendered, logged, queried, or served. The portal names no
`RecipientIdentifier` and creates no masking, redaction, or scrubbing path.

The engine's existing in-memory masking contract is unchanged. The structured
web, relational metadata, and browser boundary is stricter: no SIN in any form.

The review browser allowlist is exactly: tax year, immutable review-package
version, slip type, taxpayer display name, employer display name, boxes and
values, tax lines, source-box references, and unmapped codes. Taxpayer and
employer display names are the two approved display-PII fields.

The browser model (`ReviewPageModel` and its slip, box, and line records),
rendered HTML, page source, logs, diagnostics, and client-side
state must contain no date of birth, address, email address, phone number,
account number, provider identity value, `FirmId`, `UserId`, membership
identifier, session identifier, persistence identifier, or non-display
timestamp. The opaque `recordId` route key is permitted only to select the
stored version. It is never rendered, copied into the DTO, logged, or trusted as
tenant identity.

## Tax and tax-year boundary

- **The UI computes no tax and implements no CRA mapping.** It contains no CRA
  line authority, box→line table, mapping-config read, arithmetic, or rounding.
- **No tax-year policy lives here.** The portal declares no supported-year list,
  current-year concept, tax-year fallback, or tax-year literal as policy.
- `IDocumentExtractionIngress` remains unimplemented, unregistered, and
  unresolved. The intake upload transport stores opaque source bytes only; it
  does not extract, parse, map, calculate, or create a review record.

## Deployment refusal

Spec 011 deliberately ships the startup refusal, not the production Azure Data
Protection provider. As shipped, this build starts in Development only. A
non-Development host refuses to start without a deployment-owned, explicitly
registered durable key repository and encryptor in addition to the other
startup requirements.

This build may not be deployed anywhere that can receive real taxpayer data
until a deployment specification supplies and verifies the Canadian Azure
Storage and Key Vault key ring, hosting, exposure, TLS, residency, secrets,
operations, and provisioning posture.

## Spec 016 T01: the production admission gate

The unconditional non-Development refusal is replaced by an **enforced**
production admission gate, not by a switch. `Production/ProductionConfiguration.cs`
reads the closed `TaxAi:Production:*` profile (any other key under that
section is an unapproved entry) plus the secret-free evidence manifest, and
computes the configuration digest. `Production/ProductionAdmissionGate.cs`,
invoked unconditionally by `Program.cs` after `PortalStartupGuards`, evaluates
every rule and throws one fixed, value-free message unless all pass:

- the host environment is exactly `Production` and the profile validates;
- intake and extraction settings have the production shape — no Development
  provider, no local encrypted filesystem store, root, key or reader;
- the container holds a durable key ring, the durable session store, the
  registered validator and the approved production Document Intelligence
  adapter, source store and source reader;
- the configured artifact version is the running build's informational version;
- the platform-issued managed identity matches the configured binding, within
  the validated probe deadline;
- the manifest proves DEP-01 to DEP-24, both provider assurances and both role
  approvals for this exact artifact and configuration digest.

**As of T01 no deployable host can pass:** no production Document Intelligence
adapter, source store or source reader exists, and their absence is itself a
refusal. Development hosts and the in-process TestServer are not evaluated, and
neither is ever production authorization. `PortalStartupGuards` now applies the
durable key-ring and session-store refusals to every non-Development host,
deployable ones included. The intake content-encryption key is required only in
Development; outside it the key and the Development content root are refused by
the production shape.

## Spec 016 T03: key protection in Production

T03 wires the real key protection into the Production host. The portal still
declares no Azure package and names no Azure SDK type: every credential, key,
secret and key-ring component is built by `TaxAI.AzureInfrastructure`.

- **Shared protected key ring.** In Production only, `PortalIdentity` registers
  `AzureDataProtectionConfiguration.ProtectWithAzureKeyRing` from the closed
  profile: one private Canadian Blob key ring, protected by the Key Vault Data
  Protection key, reached by the configured managed identity, with the
  production application discriminator. A profile that does not describe it
  registers none, and startup refuses. Development keeps its local key ring;
  every other environment registers none. `PortalStartupGuards` accepts the
  profile key ring in place of the spec 011 location/protection pair in
  Production only. Durable sessions, not cookie decryption, still authorize.
- **Intake credential material from Key Vault.** In Production the composition
  root loads the intake digest and fingerprint keys once, by managed identity,
  with `AzureInfrastructureRegistration.LoadProductionRuntimeMaterial`, and
  supplies them to the unchanged intake validation. `IntakeConfiguration.cs`
  and the credential algorithms are unchanged. Plaintext
  `TaxAi:Intake:DigestKey`/`FingerprintKey` settings are never loaded over; the
  gate refuses them. A refused load leaves no material and intake validation
  refuses startup with its fixed message. `ModuleRegistration.RegisterTaxAiModules`
  takes the loader explicitly so tests never contact a vault; `AddTaxAiModules`
  keeps its signature.
- **The gate adds three T03 rules:** the registered key ring must be exactly the
  Azure Blob repository with the Azure Key Vault encryptor and the approved
  discriminator; the intake material must be exactly the vault-loaded material;
  and, only after the managed identity is verified, `IApprovedResourceProbe`
  must find both wrapping keys enabled, non-exportable, purge-protected and
  actually wrapping, and the key ring round-tripping, within the probe
  deadline. No new `TaxAi:Production:*` setting exists.

Production admission is still refused: no production Document Intelligence
adapter, source store or source reader exists, and both provider assurance
gaps remain unresolved.

## Spec 016 T04: source storage in Production

In Production the composition root passes the vault-loaded fingerprint key and
the validated `TaxAi:Extraction:MaximumSourceBytes` bound to
`AddTaxAiAzureInfrastructure` as `SourceContentSettings`; the library
registers the Azure Blob `IIntakeContentStore`, the scoped purger and the
`AzureExtractionContentReader` **as its concrete type only**. The portal
names no Azure adapter type in its composition root and declares no new
setting; the source container is fixed in code and template.

- **The gate now requires the real adapters.** The intake port must resolve to
  exactly `AzureIntakeContentStore`; exactly `AzureExtractionContentReader`
  must be registered, and a general `IExtractionContentReader` registration is
  a local-store refusal. An unusable source endpoint, wrapping key, identity or
  bound registers nothing and is refused. The approved-resource probe also
  requires a GPv2 `Standard_GZRS` source account and a non-public,
  non-immutable source container.
- **Still refused.** As of T04 the extraction workflows refused outside
  Development because no production Document Intelligence adapter existed
  (T05 below), and both provider assurance gaps remain unresolved. No source
  preview, download, export, SAS, blob address or content route exists.

## Spec 016 T05: production Document Intelligence

In Production the composition root builds the adapter options from the
existing closed profile (`ExtractionConfiguration.ProductionOptions`: the
resource binding, managed identity, GA API/model/feature triple, the
production source bound and the finite resilience deadlines, the operation
deadline never above the provider call limit) and calls the one
`AddDocumentIntelligence` extension. Invalid options register nothing. The
production ingest and review workflows are then built over the concrete
`AzureExtractionContentReader` the infrastructure library registered; the
reader is still never a general service. No setting is added.

- **Per-operation availability.** `Production/ExtractionAvailabilityGate.cs`
  implements `IExtractionAvailabilityGate`. Outside Development and
  Production it is always unavailable. In Production it rechecks, on every
  call, the closed profile, artifact version, structural validator, evidence
  manifest (both provider assurances included), managed-identity binding,
  approved-resource probe and the adapter's non-content reachability probe,
  inside the validated probe deadline. Any failure is unavailable.
- **Scope first, then availability.** The extract, correction and finalize
  routes carry one endpoint filter. It resolves the trusted firm, case and
  document first, so a foreign or missing selector is the same empty `404`
  as before; only an in-scope request consults availability. Unavailable is
  a fixed `503` with the text `Document extraction is temporarily
  unavailable.` and the intake no-store headers. Read routes are unchanged.
- **No substitution.** There is no alternate region, processor, Development
  provider or retry-with-another-model path. The gate refuses admission unless
  the registered provider is exactly `AzureDocumentIntelligenceProvider` whose
  options equal the configured ones and the availability gate is exactly this
  class; the Development provider is still its own refusal.
- **Still refused.** Every deployable host remains refused until real DEP
  evidence (including DEP-04, DEP-12 and DEP-13, pending) is observed and both
  provider assurance gaps are resolved.

## Spec 016 T06: recovery, backup and Canadian DR

- **Recovery settings.** The closed profile gains fourteen digest-covered
  `TaxAi:Production:Recovery:*` keys: the profile (`Primary` or the separately
  signed `RegionalRecovery`), the Canada Central and Canada East control-ledger
  bindings, and the 35-day point-in-time, 7-day backup-chain and 90-day ledger
  windows. In `RegionalRecovery`, SQL and source storage are the Canada East
  resources; Document Intelligence, Key Vault and monitoring stay Canada
  Central. Document Intelligence never fails over: another region is refused.
- **Admission adds three rules.** Exactly the Azure control ledger and evidence
  reader, the durable reconciler and the orchestrator must be registered; the
  release evidence stored in the Canadian evidence store for this exact binding
  must be present, not withdrawn, and admitted; and the recovery inventory
  probe must find the six immutable control containers.
- **`Production/RecoveryOrchestrator.cs`.** Only after every admission rule has
  passed, the gate runs the orchestrator within the operation deadline. The
  recovery epoch is the **database incarnation**: a released incarnation starts
  at once; a pristine environment writes the ledger's genesis; any other
  incarnation stays in **quarantine** — the host does not start, so nothing is
  served — until the ledger is complete, fresh and covers the recovered data, a
  current attestation exists, every acknowledged terminal fact is replayed,
  tombstones are cleaned, every pre-recovery session and link is revoked,
  unlisted memberships are deactivated, and the release is acknowledged in
  Canada East. Any failure refuses with one fixed message. There is no hosted
  service, route, switch or partial release, and no step is skipped to meet an
  objective.
- **Degraded scope.** Recovered service authenticates, reads existing
  authorized state and runs non-extraction operations; new extraction keeps the
  fixed `503` until the Canada Central provider is valid again.
- **The manually activated Canada East recovery host** (T06 amendment 2) runs
  this same composition with the `RegionalRecovery` profile, deployed from the
  committed IaC only by an explicit activation input, in its own recovery
  resource group. It starts quarantined like any other host and is admitted only
  after reconciliation releases its database incarnation. See
  `docs/operations/canadian-recovery.md`.

Production admission remains refused: DEP evidence is unobserved and both
provider assurance gaps are unresolved.

## Spec 017 T01: local run and the Development-only sign-in

Spec 017 makes the host runnable on a developer machine. T01 adds exactly one
surface, and it exists in Development alone.

- **`DevelopmentSignInEndpoints.cs`** maps `GET` and `POST /development/sign-in`,
  both explicitly anonymous, the `POST` requiring antiforgery. `Program.cs`
  calls its `Map` once, inside the one branch that is both `IsDevelopment()`
  and the ordinal `PortalStartupGuards.RunsInDevelopment` comparison. In every
  other environment neither route exists and the path answers exactly like any
  unmapped path. The application route inventory outside Development is
  unchanged.
- **It is a shortcut around the identity provider, not a second authorization
  model.** The handler signs in to the existing cookie scheme with a principal
  carrying only the fixed synthetic issuer and subject
  (`Persistence.DevelopmentFixtureIdentity`), exactly as the OpenID Connect
  handler does after validating a token. The existing sign-in event resolves
  the local user, the existing rule issues the firm claim only for exactly one
  active membership, and `SessionTicketStore` writes the durable session. Every
  later request passes the same R-1 revalidation; sign-out and revocation apply
  unchanged. The handler reads no request value, issues no claim of its own,
  names no session store, and redirects to a compile-time constant.
- **The handler enforces its own antiforgery result.** It binds no form value,
  so it reads `IAntiforgeryValidationFeature` and refuses with a fixed `400`
  unless the token is valid.
- **`DevelopmentSignInPage`** is a parameterless, statically rendered component
  holding one antiforgery form. It injects nothing.
- **Local database allowance.** The composition root's single
  `AddTaxAiPersistence` call passes the existing ordinal `runsInDevelopment`
  flag. `PortalStartupGuards` is unchanged: outside Development a connection
  string that disables certificate validation is still refused before a
  container exists.
- **`Properties/launchSettings.json`** gains one HTTPS loopback profile for the
  local tool. Nothing binds a non-loopback address.
- **No `UserSecretsId`.** `scripts/local/Start-LocalDemo.ps1` keeps generated
  settings in the user-secrets store under its own identifier and hands them to
  the launched host as that process's environment. This project file is
  unchanged, so no test host reads a developer's secrets.

None of this is production authorization. The gate below is unchanged: the
Development sign-in authorizes no taxpayer-data surface, and the OIDC
configuration requirement, the admission gate, and every non-Development
refusal are exactly as before.

## Spec 017 T03: the case list, navigation, and refusal notices

T03 adds exactly one route in every environment and changes the response of
exactly two existing ones.

- **`CaseListEndpoints.cs`** maps `GET /intake-cases` under
  `PortalAuthorization.FirmScopedPolicy`. It shares its path with the existing
  create `POST`, so a route is identified by path **and** method. The handler
  binds nothing: no route value, query, form, body, header or cookie is read as
  scope, search, filter, sort or paging input. The firm comes only from
  `PortalAuthorization.BootstrapStateFor`; the bound is the constant `100`. It
  reads `IIntakeStore.ListCasesAsync` directly and returns the statically
  rendered `CaseListPage`. A store failure is the existing fixed error with no
  partial list.
- **The case-list browser allowlist is exactly** the primary taxpayer display
  name and the stored tax year of each case. The opaque case selector appears
  only inside link targets to the existing progress and documents routes. No
  firm, participant, document, user, membership or session identifier, no
  instant, no count, no status and no SIN in any form is rendered. When the
  bound is reached one fixed sentence says so; it carries no number.
- **The route inventory is now exact at twenty-eight application routes plus
  the same five framework component endpoints** outside Development (the
  twenty-seven that existed, plus the case list). A Development host
  additionally maps the two Development sign-in routes, and nothing else.
- **`AccountantNavigation`** is a parameterless, statically rendered shared
  component: the case list, the create page, and the existing sign-out as an
  antiforgery form. It is placed in `CaseListPage`, `IntakeCaseProgressPage`,
  `ExtractionListPage` and `ExtractionReviewPage`, and **never in
  `PortalLayout`**, which client pages share: no client response carries an
  accountant link, case identifier or control. `IntakeCaseProgressPage` gains
  exactly two lines (the navigation and the link to the case's documents page),
  pinned line-exactly by `PreservedSurfaceTests`. New styles are component
  scoped (`CaseListPage.razor.css`); `wwwroot/intake.css` is unchanged.
- **The refusal notice (FR-42, FR-43).** `ExtractionEndpoints.CorrectAsync` and
  `FinalizeAsync` render the page again — the review page for a correction,
  the documents page for a finalization — when the existing workflow answers
  `Invalid` (`422`) or `Conflict` (`409`). `WorkflowNoticeKind` is the closed
  vocabulary (`None` plus exactly four entries) and `WorkflowNotice` holds the
  four constant texts. The kind is chosen from the operation and the result
  code and from nothing else: **no query, form or header value selects a
  notice**, an unknown kind renders nothing, and no taxpayer value, field,
  box code, amount, identifier or engine message can reach the text. Both
  routes still run the scope-first availability filter, so a missing, foreign
  or inconsistent selector is the same empty `404` before the handler runs.
  A workflow `NotFound`, and every refusal of the extract, classification,
  accept and duplicate routes, keep the existing empty `404`. The `503`
  availability response is unchanged. A refused operation mutates nothing.
- **The Development sign-in now lands on the case list.** Its redirect is still
  a compile-time constant.

The gate below is narrowed by exactly this: one firm-scoped list of the
firm's own cases with the allowlist above. It authorizes no search, filter,
caller paging, cross-firm view, export, accountant upload, content preview or
download, or any further taxpayer-data surface.

## The narrowed gate — read before adding a surface

The identity, trusted-firm, session, logging, and residency decisions made by
spec 011 authorize no surface by themselves. Spec 012 authorizes exactly the
single read-only review route and closed browser allowlist above.

**Reading/downloading raw content, mutating or deleting a stored review package,
provisioning a firm or membership, expanding either browser allowlist, or adding
another taxpayer-data surface still requires a further approved `tax-domain`
specification.** T03 authorizes only its exact routes, components, upload
transport, and single interactive island. A global render mode, another
interactive component, WebAssembly/npm dependency, or third-party asset remains
forbidden without further approval.

The same is true for editing or approving taxpayer values, listing taxpayers
beyond the spec 017 T03 firm-scoped case list, exporting a record, implementing an extraction ingress, or exposing a JSON API.
Do not reinterpret an available repository contract as authorization to map an
endpoint. Normal portal users have no delete path, production provisioning does
not ship, and `/health` remains byte-identical to spec 010.

Any further `tax-domain` specification must retain the no-SIN-in-any-form rule,
trusted-firm isolation, per-request membership verification, server-side
revocation, fixed safe errors, and the rule that the UI computes no tax and
implements no CRA mapping.
