# CLAUDE.md — DocumentIntelligence

Read the root [CLAUDE.md](../../CLAUDE.md) first. It owns repository policy, the
modular-monolith dependency rules, global PII policy, and the
spec→plan→implement workflow. This file owns the DocumentIntelligence module
boundary.

## What this project is

`DocumentIntelligence` is the **infrastructure adapter** for
`ExtractionWorkflow`'s document-intelligence port. It is the only project in
this repository permitted a document-intelligence dependency (spec 016 B-2).

It ships two implementations of the same port:

- the **deterministic Development/test provider** (spec 015), which never
  leaves the process; and
- since spec 016 T05, the **one production adapter**: the approved Canada
  Central Azure AI Document Intelligence resource, REST API `2024-11-30`,
  model `prebuilt-layout`, feature `keyValuePairs`.

Production activation is not implied by either. Every deployable host stays
refused by the portal's admission gate until the deployment evidence and both
provider assurances exist.

## Packages

Exactly two: `Azure.Core` (`1.54.0`) and the dependency-injection abstraction
(`10.0.10`). There is no Document Intelligence SDK: the adapter calls the REST
API directly, so a single Analyze submission, redirect refusal and explicit
deletion are under its own control. `Azure.Core` carries the
`ManagedIdentityCredential` types that `Azure.Identity` forwards, so the
`Azure.Identity` package stays only in `TaxAI.AzureInfrastructure`.

## What ships

- `DeterministicDocumentIntelligenceProvider`, `DocumentIntelligenceOptions` —
  unchanged from spec 015; no network call, Development/test hosts only.
- `Production/AzureDocumentIntelligenceOptions` — the validated binding: an
  HTTPS custom-subdomain endpoint matching its resource ID, `canadacentral`,
  the exact GA API/model/feature triple, a user-assigned managed identity, and
  finite source, attempt, operation and cleanup bounds. Anything else refuses.
- `Production/AzureDocumentIntelligenceProvider` — the port implementation. A
  user-assigned `ManagedIdentityCredential` with no retries and no SDK
  logging; an `HttpClient` with no redirect following, cookies, proxy or
  activity propagation. Each call checks the host's per-operation
  `IExtractionAvailabilityGate` first.
- `Production/AnalyzeOperationLifecycle` — one lifecycle per call: bounded read
  of the source, **one** Analyze submission, polling until the operation
  deadline (only transient failures consume the exponential retry bound),
  immediate transformation, then **Delete Analyze Result** within a reserved
  cleanup budget that caller cancellation cannot shorten. The result ID is
  reconstructed from the approved origin and UUID, never followed. Only an
  explicit `204` releases a result; exhausted deletion emits one fixed
  structural event and releases nothing. No raw payload, operation ID or
  response is persisted, logged or cached; buffers are cleared.
- `Production/DocumentLayoutRecognition`, `Production/LayoutResultInterpreter`
  — syntactic interpretation of the printed CRA T4 (25) slip face (below).

## T4 slip-face vocabulary (spec 016 T05, Class A decision)

Every recognized string is the bilingual wording printed on the CRA T4 (25)
slip (`t4-25e`/`t4-25f` PDF and e-text on canada.ca); sources and the verified
English/French vocabulary are recorded in
`specs/016-canadian-azure-production-infrastructure/evidence/T05.md`.

- **Identity.** A T4 needs the printed `T4` designator, exactly one English
  (`Statement of Remuneration Paid`) and/or French (`État de la rémunération
  payée`) title, no T4 Summary title, and at least one printed slip-face box
  field. The title alone is never sufficient. A repeated title means several
  slips and refuses; it is never narrowed to one.
- **Fields.** A box is identified by its printed slip-face label (English,
  French, or both) and/or its printed box number; a number that contradicts
  its label, or an explicit `Box`/`Case` number not printed on the slip face,
  refuses. Unprinted synonyms are never aliases. Tolerance is presentation-only:
  case, Unicode composition, apostrophe/dash variants, punctuation and spacing.
- **Values** are copied exactly as read. Duplicates stay a list. A repeated
  scalar field refuses. Occupied other-information cells refuse, because their
  code/amount pairing is positional. Absent confidence stays absent.
- **No tax semantics.** Box 12, Box 45 and Box 54 are only copied when printed;
  the existing parser still decides every requirement (Box 12 and Box 45
  required, Box 54 not required on an employee copy).

**Medical receipts are not recognized in production.** CRA prescribes receipt
contents, not a printed title or layout, so no classifier is justified. No
layout is ever classified as medical evidence, and `ExtractMedicalAsync`
returns null **without submitting** the receipt. Real medical recognition is a
pending Class A item; do not add title aliases, provider formats, eligibility
rules or CRA-line mappings to approximate it.

## Rules that bind it

- **It decides nothing about tax.** No CRA line number, box→line table,
  box→meaning table, per-box rule, rate, threshold, or eligibility rule. The
  label table identifies which printed field a value sits in, nothing more.
  No tax-year policy and no year literal.
- **Its output is untrusted.** Classification, values, and confidence become
  tax input only by surviving the existing fail-closed chain. **No confidence
  threshold exists here.**
- **It never throws to the caller.** Every failure is a null result or an
  unsupported classification, never a partial one.
- **No other egress.** Only the two production transport files may name a
  network API, and only toward the approved endpoint; every other file stays
  network-free, and the deterministic flow still completes with no networking
  assembly loaded.

## Dependencies

Exactly one project reference, inward: `ExtractionWorkflow`. No deterministic
tax module references this project. It never references `Persistence`,
`TaxAI.Portal`, EF Core, ASP.NET Core, or SQL.

## The gate

Nothing here authorizes deployment, a second external processor, an
alternate region/model, a Development fallback in recovery, or source-content
preview or export. Implement only the explicit TaskID named by the human,
change only that task's listed files, ship its tests in the same pass, and run
`pwsh ./scripts/Invoke-SolutionTests.ps1` before creating the task's `.done`
marker. Never merge or approve your own work.
