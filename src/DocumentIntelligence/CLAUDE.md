# CLAUDE.md — DocumentIntelligence

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy, the
modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file owns the DocumentIntelligence module
boundary.

## What this project is

`DocumentIntelligence` is the **infrastructure adapter** for
`ExtractionWorkflow`'s document-intelligence port. It exists to hold a
dependency boundary: it is the only project in this repository that would ever
be permitted an OCR, LLM, document-intelligence, or cloud SDK reference.

**In spec 015 it declares none, and that is the correct outcome.** Taxpayer
source bytes do not leave the TaxAI process in this specification. A real
Canadian Azure document-intelligence provider — and with it Canadian-region
residency, encryption in transit, zero-retention configuration, provider
secrets, provider operations, and production authorization — remains blocked
until a separate Canadian Azure infrastructure specification. No requirement
obliges this project to acquire an SDK merely to look like an adapter.

The one package it declares is the dependency-injection abstraction its single
registration extension needs, pinned to the repository's `10.0.10` line.

## What ships

- `DeterministicDocumentIntelligenceProvider` — computes classification and
  T4/medical field extraction **from the source bytes alone**, in process, with
  no network call. It reads one fixed self-describing text format and
  classifies everything else as `Unsupported`. It never guesses a document into
  a supported kind.
- `DocumentIntelligenceOptions` and its guard — no value has a default; a
  missing, empty, or invalid one refuses at composition time.
- `DocumentIntelligenceRegistration` — the single registration extension.
  **Development and test hosts only**, and the host states that explicitly as
  an argument rather than this module reading an environment variable. A
  refusal a variable can lift is not a refusal.

## Rules that bind it

- **It decides nothing about tax.** No CRA line number, box→line table,
  box→meaning table, per-box rule, rate, threshold, or eligibility rule. A box
  code is copied verbatim as an opaque string; amounts and dates are copied as
  printed, with no parsing, arithmetic, rounding, normalization, or currency
  conversion. No tax-year policy and no year literal: the year it reports is
  the one the document printed.
- **Its output is untrusted.** Classification, values, and confidence are
  provider metadata that become tax input only by surviving the existing
  fail-closed chain. Confidence is transported, never invented, and copied
  verbatim including an out-of-range value, so the existing envelope validator
  stays the single place a range is checked. **No confidence threshold exists
  here.**
- **It never throws.** An absent or unreadable stream, content beyond the
  configured limit, a cancelled operation, and undecodable content all fail
  closed — a null result or an unsupported classification, never a partial one
  and never an exception handed to the caller.
- **It retains nothing.** No prompt, request, response, provider payload, or
  source byte is logged, cached, buffered to disk, or persisted, in any
  environment. Bytes live for the duration of one call, inside the caller's
  content lease.
- **No network, no SDK, no egress.** No HTTP client, socket, DNS lookup, cloud
  SDK, or outbound call of any kind. The whole extraction flow completes with
  outbound network access blocked.

## Dependencies

Exactly one project reference, inward: `ExtractionWorkflow`, for the port it
implements and the untrusted result shapes it fills. No deterministic tax module
references this project. It never references `Persistence`, `TaxAI.Portal`, EF
Core, ASP.NET Core, or SQL.

## The gate

Nothing here authorizes a production provider, an external provider, network
egress, source-content preview or export, or deployment. Implement only the
explicit TaskID named by the human, change only that task's listed files, ship
its tests in the same pass, and run
`pwsh ./scripts/Invoke-SolutionTests.ps1` before creating the task's `.done`
marker. Never merge or approve your own work.
