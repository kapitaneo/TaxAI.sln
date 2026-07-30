# CLAUDE.md — TaxAI.Portal

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy,
the modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file defines the narrowed portal boundary
after specs 011 and 012. The gate is narrower than spec 010's gate; it is not
deleted.

## What this project is

`TaxAI.Portal` is the repository's single ASP.NET Core host and composition
root. It serves a fixed public shell and unchanged health response, delegates
authentication to an external OIDC provider, uses a revocable server-side
authentication session, verifies active firm membership on protected requests,
and exposes authenticated firm selection plus one read-only accountant review
route.

`GET /reviews/{recordId:guid}` is the single approved taxpayer-data surface. It
reads one immutable persisted review-package version and renders only the closed
browser allowlist below. It is not an upload, edit, approval, export, listing, or
JSON surface.

## The Blazor Web App foundation (spec 014 T01)

`Components/` holds the whole component tree: `App`, `Routes`,
`Layout/PortalLayout`, and `Pages/ReviewPage`, plus `_Imports`. There are
exactly five components, and the set is asserted exactly.

- **Static server rendering is the default, because nothing opts out.** No
  component declares `@rendermode` and no global render mode is applied.
  Interactive Server is registered so a later approved task's one adaptive
  questionnaire island can opt in per component; **no component is interactive
  today**, so the circuit has no component to run and no taxpayer model to carry.
  WebSocket compression is disabled.
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
- **Parameters are closed.** `ReviewPage` has exactly one required
  `[Parameter, EditorRequired] ReviewPageModel Model`. No component may declare a
  `CaptureUnmatchedValues` dictionary — an attribute splat is an unbounded way for
  values to reach markup.
- **No endpoint assembles screen markup or an inline stylesheet.**
  `ReviewEndpoints.cs` contains no `StringBuilder`, appended markup, `<style>`
  block, or `HtmlEncoder`. `FirmEndpoints.cs` still assembles its pre-spec-014
  firm-selection page; it is the one named exception, and a second markup
  assembler fails the boundary suite.
- **Every asset is local and tracked.** `wwwroot/portal.css` plus
  component-scoped `.razor.css`. No CDN, remote font, remote image, remote
  script, npm tree, WebAssembly project, or handwritten JavaScript.

### Static assets are middleware, not endpoints

The host calls `UseStaticFiles()` once, parameterless, in `Program.cs`, and
**deliberately does not call `MapStaticAssets()`**. The endpoint-routing variant
registers a `{**path:file}` catch-all over the entire URL space, which:

- turns every POST-only route's `405 Method Not Allowed` into a `404`, because
  the catch-all matches the path before method selection can reject it; and
- adds an endpoint that is neither explicitly anonymous nor
  authorization-required — the third state spec 011 forbids.

The route inventory therefore stays exact and closed: this application's seven
routes plus exactly five framework component endpoints (`/_blazor`,
`/_blazor/disconnect/`, `/_blazor/initializers/`, `/_blazor/negotiate`, and
`/_framework/opaque-redirect`). None of those five takes a route parameter.

## The frozen ClientIntake contract is not wired here

`ClientIntake` exists as an approved contract with
`NotImplementedException` bodies. **The portal does not reference it, register
it, resolve it, or expose any intake route.** Doing any of those belongs to the
later approved tasks of spec 014.

## Dependencies point inward, and are exactly four

The production project references are exactly:

- `TaxLogicEngine`;
- `CaseProcessing`;
- `Tenancy`;
- `Persistence`.

The portal does not directly reference `TaxCases`, `DocumentExtraction`, or
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

No SIN or partial SIN may reach this project, a response, URL, cookie, log,
metric, trace, error, or persisted portal-facing model. Not full, not masked,
not hashed, tokenized, last-three, last-four, or a SIN-derived display
identifier. The portal names no `RecipientIdentifier` and creates no masking,
redaction, or scrubbing path.

The engine's existing in-memory masking contract is unchanged. The web and
persistence boundary is stricter: no SIN in any form.

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
  unresolved. The portal accepts no document upload.

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

## The narrowed gate — read before adding a surface

The identity, trusted-firm, session, logging, and residency decisions made by
spec 011 authorize no surface by themselves. Spec 012 authorizes exactly the
single read-only review route and closed browser allowlist above.

**Uploading a document, mutating or deleting a stored package, provisioning a
firm or membership, expanding the browser allowlist, or adding a second
taxpayer-data surface still requires a further approved `tax-domain`
specification.**

Spec 014 T01 added a rendering foundation, not a capability. **A Blazor
foundation is not authorization to add a screen.** Mapping an intake route,
registering an intake workflow, making a component interactive, applying a global
render mode, adding a WebAssembly or npm dependency, or serving a file other than
the tracked stylesheets each requires the later approved tasks of spec 014 or a
further specification.

The same is true for editing or approving taxpayer values, listing taxpayers,
exporting a record, implementing an extraction ingress, or exposing a JSON API.
Do not reinterpret an available repository contract as authorization to map an
endpoint. Normal portal users have no delete path, production provisioning does
not ship, and `/health` remains byte-identical to spec 010.

Any further `tax-domain` specification must retain the no-SIN-in-any-form rule,
trusted-firm isolation, per-request membership verification, server-side
revocation, fixed safe errors, and the rule that the UI computes no tax and
implements no CRA mapping.
