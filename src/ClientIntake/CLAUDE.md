# CLAUDE.md — ClientIntake

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy, the
modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file owns the ClientIntake module boundary
and its public-contract inventory.

## What this project is

`ClientIntake` is the deterministic, provider-independent **application
coordination module** for the spec 014 client intake and document-upload
workflow. It owns the closed intake vocabularies, the accountant and client
commands and safe projections, the workflow services, and the declared
persistence / content-store / credential-source ports.

**As of spec 014 T01 this module is a frozen contract, not an implementation.**
Every workflow constructor and method throws `NotImplementedException`. T02
replaces those bodies without changing a signature. Nothing in the portal
resolves or invokes a workflow stub.

## What it is not

- **Not a second tax-case model.** `TaxCases` remains the sole case, checklist,
  answer, evidence, and readiness authority. This module calls `TaxCase.Create`,
  `AnswerQuestion`, and `AttachEvidence` and reads `TaxCase.Readiness`. It
  declares no competing aggregate and **no second readiness calculator**.
- **Not a tax engine.** It contains no CRA line number, no box→line table, no
  parser, no mapper, no arithmetic, no rounding, and no `decimal` token.
  Uploaded T4 and medical files are **opaque evidence**, never a source of tax
  truth.
- **Not infrastructure.** It declares ports; it implements none of them. No EF
  Core, ASP.NET Core, SQL, cloud SDK, OCR, LLM, email, or frontend dependency
  may enter this project, and it declares **no `PackageReference` at all**.
- **Not a JSON API.** The public records are in-process contracts. There is no
  controller, endpoint, route, or serialization contract here.

## Dependencies point inward, and are exactly three

The production project references are exactly:

- `TaxCases` — the sole case/readiness authority;
- `Tenancy` — the trusted-firm scope and the existing immutable-review contract;
- `TaxLogicEngine` — named only for the existing `ParseResult<T>` /
  `ValidationError` result vocabulary used while replaying a `TaxCase`.

Forbidden dependencies: `Persistence`, `TaxAI.Portal`, `CaseReview`,
`CaseProcessing`, `DocumentExtraction`, and any package. No module may reference
`ClientIntake` except `Persistence` (the relational metadata adapter) and
`TaxAI.Portal` (the host) — and both of those arrive in later tasks, not T01.

## Scope always moves from a trusted boundary inward

- **No accountant command record has a firm member.** Every
  `IAccountantIntakeWorkflow` member takes a `TrustedFirmId` as its first
  argument, with no overload, optional argument, or default that omits it. The
  firm is resolved from the request's authorization state by the host.
- **`FindLinkScopeAsync` and `FindClientScopeAsync` are the only firm-omitting
  members in `IIntakeStore`.** Each takes a keyed digest and *returns* a trusted
  scope rather than accepting one, so neither can enumerate.
- **Client scope is derived, never supplied.** No `IClientIntakeWorkflow` member
  takes a firm, case, participant, or document identifier.
- Every scoped client read and write revalidates the same live session **in the
  same database operation**. A lookup followed by an unconditional read is a
  time-of-check/time-of-use hole.
- The workflows read no clock, environment, current user, or HTTP context. The
  actor reference and the current instant arrive as arguments.

## Append facts; derive current progress

Answer versions, document metadata, upload attempts, submissions, associations,
and audit events are **append-only**. There is no update or delete of immutable
history anywhere in `IIntakeStore`, and **no writable readiness flag** — current
answers, per-item status, link state, latest submission, unsubmitted changes,
readiness, and handoff state are all projections.

Per-item status precedence is `Ready` → `Uploaded` → `Rejected` → `Missing` →
`Requested`. Link state precedence is revoked, then spent, then expired, then
active. Readiness uses only the latest immutable submission.

## Tokens and secrets

- `IntakeSecret.TryParse` accepts **only** the canonical unpadded base64url
  encoding of exactly 32 bytes. Nothing is trimmed, padded, case-folded, or
  normalized, and a non-canonical encoding of the right bytes is still refused.
  Whitespace is rejected before decoding, because the framework's base64 decoder
  would otherwise skip it.
- `IntakeTokenDigest` requires exactly 32 bytes, copies the input, exposes no
  mutable array, and compares with `CryptographicOperations.FixedTimeEquals`.
- Only keyed digests are ever stored or looked up. There is no raw-credential
  store member and no member that returns or verifies a raw credential.
- `IntakeTokenPurpose` enforces domain separation, so a bootstrap-link digest can
  never be replayed as a session digest.
- The port is `IIntakeCredentialSource`, **not** `IIntakeSecretSource`. The
  repository forbids any tracked file name containing "secret" — a hygiene guard
  against committed secret material, enforced independently by two suites — and a
  contract file has no business tripping it. `IntakeSecret` remains the
  secret-bearing value type; this port mints and digests one.

## PII posture: no SIN in any form, by omission

The **only** raw-byte exception is the encrypted source object behind
`IIntakeContentStore`. It may contain a SIN and **must never be described as
SIN-free**. That is precisely why the contract exposes **no read member**: there
is no way to get content back out, no URL that serves it, and no mapping that
could bring it into a query or a diagnostic.

Every structured surface this module declares — commands, views, storage
records, scopes, audit records, and diagnostics — has **no SIN in any form**, not
full, masked, hashed, tokenized, last-three, or last-four. There is no
SIN-named field. This module names no `RecipientIdentifier` and creates no
masking, redaction, or scrubbing path: the data never enters these types.

Also absent from every record here: file name, raw credential, cookie, document
content, amount, prompt, content snippet, and free text.

## Safe output is part of the contract

- Identifier, secret, digest, taxpayer-text, content-key, and fingerprint
  properties are `[JsonIgnore]`-marked.
- **Where a property is declared by hand, the attribute goes on the property.**
  A `[property: JsonIgnore]` target on a positional parameter whose property is
  hand-declared is silently discarded (CS0657) — it looks exactly like an
  omission that is not there.
- Every public instance declares `public override string ToString()` emitting
  only type identity plus closed counts and enum values — never a caller string,
  identifier, secret, digest, prompt, or URL.
- Every collection constructor copies into a read-only snapshot, so a caller
  holding the original list cannot change what was validated after validation.

## No tax-year policy of its own

This module declares no supported-year list, current-year concept, or tax-year
fallback. Year isolation belongs to the modules that own it; a year literal here
would be a second policy, and the two would drift.

## The gate

T01 froze this contract and shipped **no behavior**. Implementing a workflow
body, adding a store or content adapter, exposing a route, or registering any of
these types in the composition root belongs to the later approved tasks of spec
014 — not to a passing improvement. Nothing here authorizes a production content
provider, a retention or purge path, or a deployment.
