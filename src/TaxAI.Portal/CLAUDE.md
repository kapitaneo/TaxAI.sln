# CLAUDE.md — TaxAI.Portal

Project-level guidance for `src/TaxAI.Portal/`. Read this together with the root
[CLAUDE.md](../../CLAUDE.md). Delivered by spec 010 (Portal Foundation), a
`platform` feature: **Tax/CRA semantic impact: none.**

## What this project is

The repository's single deployable application: an ASP.NET Core web host that
starts the process, composes the already-shipped modules into one
dependency-injection container, and serves fixed content. It is the **UI / API
host** layer in the root instructions' dependency diagram.

It is **not** a portal yet. There is no screen, no data, no input, no login, and
no storage. Every one of those is a later, separately-specified feature.

## Dependencies point inward, and are exactly two

```
TaxAI.Portal ──► CaseProcessing ──► DocumentExtraction
TaxAI.Portal ──► TaxLogicEngine  ◄──┘
```

The production project references are exactly `TaxLogicEngine` and
`CaseProcessing`. Nothing else. **No module may reference this project** — not in
a project file, not in source, not transitively. If a module ever needs something
from here, the design is inverted.

`TaxCases` is not referenced. `DocumentExtraction` arrives transitively and is
never named. `CaseReview` is **not** referenced: spec 009 is merged, but an
`AccountantReviewPackage` and its reviewed slips are taxpayer data, and rendering
them is exactly what the gate below stops.

## The line this project may not cross

- **The UI computes no tax and implements no CRA mapping.** No CRA line number,
  no box code, no box→line table, no mapping-config read. Every line number in
  the system originates from `TaxLogicEngine`'s embedded JSON config, reached
  only through a provider.
- **No tax-year policy lives here.** No tax-year literal, no supported-year type.
  Year isolation belongs to the modules that own it.
- **No arithmetic and no rounding.** No `decimal`, `double`, `float`,
  `Math.Round`, `MidpointRounding`, or `MoneyRounding`.
- **No taxpayer data and no PII.** No `T4Slip`, `T5Slip`, raw input, line entry,
  mapping result, `RecipientIdentifier`, extraction, or `TaxCase` type is named,
  rendered, logged, serialized, persisted, or transmitted. No SIN handling and no
  masking or redaction helper — in v1 the safest posture is available and is
  therefore mandatory: **the data never enters the process.**
- **No endpoint accepts input.** No request body, route value, query value, form
  value, header, cookie, or uploaded file. There is no input surface to validate,
  because there is no input surface.
- **No authentication, session, identity, persistence, HTTP client, background
  service, or system clock.**

## Wiring lives in exactly one file

All service registrations belong in `ModuleRegistration.cs`. Wiring lives in one
place or it lives in several — and the second consumer of the engine must not be
able to invent a second provider instance. The service provider is validated on
build, with scope validation, so a mis-wired container fails at startup rather
than on first request.

`IDocumentExtractionIngress` is **not** implemented, registered, or resolved.
Spec 007 shipped that signature deliberately without an implementation; creating
one here would invent the untrusted-input intake path inside a hosting spec.

## The gate — read this before adding anything

**No endpoint, page, component, or middleware that renders taxpayer data,
accepts user input, uploads a document, persists data, reads a session or
identity, or crosses a firm/case/participant boundary may be added to this
project until the Class A questions in
[specs/010-portal-foundation/prd.md](../../specs/010-portal-foundation/prd.md)
(Open questions A–F) are answered in a separate specification.**

Those questions are authentication and authorization; tenant/firm isolation at
the HTTP boundary; what taxpayer data a UI may render and at what masking level;
logging, telemetry, and error-reporting policy; hosting, TLS, public exposure,
and data residency; and whether this host also exposes the untrusted extraction
ingress.

Adding any of those surfaces makes the feature **`tax-domain`**, and every
tax-domain protection reattaches: CRA verification against canada.ca, explicit
tax-year scope, fail-closed behaviour, PII masking, and a negative test per
fail-closed rule. That reclassification is not a design decision anyone
downstream gets to make silently.
