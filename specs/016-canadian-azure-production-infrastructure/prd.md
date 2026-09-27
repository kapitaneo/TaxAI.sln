## Feature classification

**Class: `platform`.** Spec 016 — Canadian Azure Production Infrastructure.

Tax/CRA semantic impact: none

This feature supplies production infrastructure and adapters for the completed
Spec 015 workflow. It changes no tax fact, parser rule, mapping, calculation,
tax-year policy, medical eligibility rule, or approved browser field.

**Status:** ready for /plan

The sixteen human decisions supplied on 2026-09-26 and the subsequent explicit
Q-A1/Q-A2 resolutions are authoritative. No blocking Class A question remains.
Human review precedes `/plan`; this invocation does not run it. An Azure
capability citation is not evidence that a particular subscription/resource has
been configured or approved. No infrastructure has been provisioned or tested
by writing this PRD. The production-deployment refusal remains in force.

## Problem

**Tax-year scope: Not applicable.** Existing year-scoped behavior is unchanged.

TaxAI can already coordinate intake, extraction, accountant correction and
acceptance, and immutable review creation. Its only shipped source reader and
document-intelligence provider are Development/test implementations. Production
startup intentionally refuses. Real taxpayer documents require a Canadian,
encrypted, tenant-isolated operational environment, a narrowly approved external
processor, demonstrable lifecycle destruction, and recovery that cannot restore
purged information or revoked access.

The desired outcome is one deployable TaxAI.Portal with verified Canadian Azure
dependencies and a real Azure AI Document Intelligence adapter, without moving
tax logic into infrastructure or creating new taxpayer-facing capabilities.

## Current state and reuse analysis

Repository inspection baseline: `master` at
`ef5899464ef9733edbf1c4a999b28b214753372b`; Spec 015 T01–T07 markers exist.
`NEXT.md` still describes older gates; the actual source and Spec 015 completion
record establish the current extraction capability. This PRD does not edit those
historical records or claim their original gates were already removed.

| Existing capability / evidence | Reuse in Spec 016 | Genuinely new work |
|---|---|---|
| `TaxAI.sln`, .NET 10 projects, `global.json`, `scripts/Invoke-SolutionTests.ps1`, `scripts/test-projects.json` | Same solution, runtime baseline, authoritative full-suite gate and nondecreasing test floors | Register tests for the real new infrastructure project; no speculative project shells |
| `src/TaxAI.Portal/ModuleRegistration.cs`, authentication/session services and approved routes | Single executable/composition root; server-derived firm scope, revocation, antiforgery and fixed safe refusals | Production registration, secure dependency validation and conditional release gate |
| Spec 014 components and upload transport | Static SSR plus the existing narrow Interactive Server questionnaire; existing streaming upload and configured limits | Prove HTTPS proxy, WebSocket/circuit behavior and shared Data Protection across multiple instances |
| `ClientIntake.Contracts.IIntakeContentStore` | Keep write-only staging and compensating cleanup; no read method or browser URL | Production encrypted Blob adapter and due lifecycle cleanup |
| `ExtractionWorkflow.Contracts.IExtractionContentReader` and `ExtractionSourceLease` | All four required scope values: trusted firm, case, document and content key; null on mismatch; disposable in-memory source lease | Production scope-verified decrypting reader; terminal-purge refusal |
| `ExtractionWorkflow.Contracts.IDocumentIntelligenceProvider` and `Models/ProviderResults.cs` | Existing nullable/untrusted classification, T4 and neutral-medical shapes; no new tax authority | Azure Document Intelligence implementation in its existing owning project |
| Existing ingest/review workflows, payload builder and engine/review chain | Corrections, classification overrides, duplicate decisions, exact-version acceptance and one-time review association | Connect real provider; prevent successful output when provider deletion fails |
| `SqlIntakeStore`, `SqlExtractionStore`, review/session repositories and existing migrations | Persistence remains the sole EF Core/SQL owner; scoped append-only metadata and durable revocation | Add only lifecycle/control metadata and necessary owner-managed purge operations, without changing historical tax data |
| `Tenancy.Services.RetentionPolicy` and `IReviewRecordPurgeService` | Existing exclusive seven-year engineering boundary; explicit trusted-firm purge | Coordinate extraction/intake history and source lifecycles; restore-safe structural purge ledger |
| Specs 011, 014 and 015 PRDs/architectures/verification records | Preserve approved Canadian residency, source-byte exception, browser allowlists, no-SIN boundaries and tests | Scoped production exceptions explicitly recorded below; no reimplementation of domain types |

The recorded T07 full gate passed 4,147 tests across 11 registered test projects.
That is historical evidence, not a test run performed for this documentation
change. The commands remain `dotnet build TaxAI.sln` and
`pwsh ./scripts/Invoke-SolutionTests.ps1`; raw `dotnet test TaxAI.sln` alone is
not proof that every assembly ran.

## User stories

- As an accountant, I can use the approved extraction workflow with real documents without changing its tax or review semantics.
- As a taxpayer, my document is processed only through the approved Canadian boundaries and is never previewed or downloaded through TaxAI.
- As a Security/Privacy approver, I can refuse deployment or extraction when residency, processor, encryption, access or deletion evidence is missing.
- As an operator, I can diagnose availability through structural telemetry without access to taxpayer values.
- As a recovery operator, I can restore service without resurrecting purged resources, expired credentials or revoked sessions.

## Preserved architecture and security boundaries

**B-1.** TaxAI remains a modular monolith. `TaxAI.Portal` is the only host and
composition root. There is no second production executable, microservice,
public extraction service, content API, or independent tax engine.

**B-2.** `DocumentIntelligence` alone may reference the Azure Document Intelligence
SDK. One real non-DI infrastructure project implements inward-facing ports for
Blob content, Key Vault wrapping, Azure Data Protection and Azure configuration
probes. Its proposed name/path is `src/TaxAI.AzureInfrastructure/` [ASSUMED].
The user explicitly authorizes these non-DI SDKs and the narrow Data Protection
integration outside Portal; this does not authorize endpoints, components, EF,
or business rules there. Persistence alone owns Azure SQL and EF Core.

**B-3.** No Azure/cloud SDK enters TaxLogicEngine, TaxCases, DocumentExtraction,
CaseProcessing, CaseReview, Tenancy, ClientIntake or ExtractionWorkflow. No tax
logic enters Portal or either infrastructure adapter. Existing envelope,
parser, JSON mapping, processing, review assembly and projection authorities
remain unchanged. Medical evidence remains neutral and outside T4 tax review.

**B-4.** No SIN in any form, derivative, presence signal or identifier enters a
TaxAI-owned structured persisted, browser, URL, cookie, header, audit, logging,
metric, trace, diagnostic or exception surface. The existing explicitly
authorized transient in-memory payload path remains; it is not a persisted
representation. No new SIN property, masking helper or correction field exists.

**B-5.** All taxpayer operations retain live-session and membership validation,
trusted firm/case/document isolation, and identical empty missing/foreign 404s.
Content keys never authorize reads by themselves. Accountant acceptance remains
an explicit act; existing histories remain append-only until authorized terminal
purge. No normal accountant receives arbitrary delete access.

**B-6.** No source preview, download, print, export, email attachment, public blob,
browser SAS, static-file source, or content-serving route is added. Existing
client/intake/review browser allowlists and `/health` disclosure posture remain.

**B-7.** Spec 016 explicitly supersedes only the previous production-adapter,
taxpayer-content egress, non-DI cloud-SDK and lifecycle deferrals after its gates
are met. Spec 015's in-process-only Development provider remains unchanged in
meaning. Its historical no-egress proof is not rewritten to assert that a
production adapter has no approved egress. Implementation must update relevant
boundary instructions/tests narrowly, never remove the preserved invariants.

## Functional requirements

### FR-01 — Canadian deployment and service inventory

Primary resources, processing, taxpayer data, keys and operational diagnostics
are in **Canada Central**. **Canada East** is Canadian DR/recovery/backup only;
there is no normally active second portal and no non-Canadian failover. Every
resource/replica/backup/monitoring destination is in the signed inventory.
Canada Central has availability zones and is paired with Canada East; Canada
East currently has no availability zones. Do not claim equivalent zonal topology
in the recovery region. [M01][m01]

Only GA services/features are permitted. Actual regional SKU/capacity and
combined networking/encryption capabilities must be validated for the selected
subscription; a region label or a successful deployment of one component is
not a proof of the complete topology. No preview or foreign-region fallback is
authorized when an approved combination cannot be provisioned.

### FR-02 — Existing Portal hosting and regional ingress

Use **Azure App Service Premium v3 P1v3 in Canada Central**, zone redundancy
enabled, at least **two running application instances**. Autoscale never lowers
production below two. Verify the plan's actual zone-capable scale unit and
application placement; per-site scaling must not silently leave one application
worker. Microsoft documents P1v3 zone-redundant creation with two workers and
requires a zone-capable scale unit. [M02][m02] [M03][m03]

Use **Application Gateway WAF v2 in Canada Central** with public HTTPS 443 only.
TLS terminates at this regional ingress, then encrypted HTTPS reaches the
private App Service backend with certificate/hostname validation. No general
public App Service origin, public SCM deployment bypass, plaintext backend,
global CDN/TLS terminator or public port 80 is authorized. WAF and proxy behavior
must preserve Spec 014 limits and the existing interactive island. Application
Gateway supports TLS re-encryption, private App Service backends, and WebSockets;
none of that proves payload-safe diagnostics by default. [M04][m04]
[M05][m05] [M06][m06] [M07][m07]

**Approved Q-A1 infrastructure-processing exception:** transient plaintext inside
the Canadian gateway process is authorized only as technically necessary for TLS
termination and WAF processing. The path is client -> HTTPS/TLS -> Canadian
Application Gateway WAF v2 -> unavoidable transient in-memory plaintext TLS/WAF
processing -> HTTPS/TLS re-encryption -> private App Service backend.

Gateway plaintext must not be intentionally persisted, exported, logged or
retained. This exception authorizes no plaintext request/response persistence,
body logging, taxpayer-payload diagnostics, taxpayer packet/body capture, WAF
custom document/form logging, payload support bundle or plaintext HTTP forwarding.
Encryption in transit protects both network legs; it does not mean plaintext
never exists at an authorized TLS termination point. Existing source-serving,
browser, persistence and telemetry prohibitions remain unchanged. [M04][m04]

### FR-03 — Encrypted source storage and extraction-only reader

Use **Azure Blob Storage standard GPv2 in Canada Central**, **GZRS when supported
for the selected configuration**, with a private endpoint/private DNS and public
network access disabled. GZRS synchronously distributes primary copies across
zones and asynchronously replicates to the paired region; it does not promise
zero data loss during regional failure. GPv2 supports GZRS. [M08][m08] [M09][m09]

Source blobs are upload-once: no overwrite, browser access, SAS or public
container. Preserve application-level authenticated encryption and bounded
streaming upload. Use a fresh per-content data encryption key wrapped by a
Key Vault key; plaintext keys are never persisted. Store only encrypted source
bytes plus the minimum protected envelope metadata, outside SQL and web root.
Encryption at the storage service is additional protection, not a replacement.
Microsoft documents Blob envelope encryption and Key Vault wrapping; obsolete
client-side encryption v1 is not an acceptable implementation. [M10][m10]
[M11][m11]

The production reader implements the existing separate extraction-only port,
verifies server-stored firm/case/document/key ownership before reading, and
returns a bounded disposable in-memory decrypting lease. An absent, mismatched,
tampered, undecryptable or terminally purged object fails closed before any
provider call. No filesystem staging, decrypted cache, persistent OCR cache or
source-read member on the anonymous intake store is permitted.

Use the user's safe baseline: source snapshots, source versioning, source soft
delete and source WORM/time-based immutability are **disabled**. Source account
settings must not be confused with separately protected key-ring/control data.
Enabling versioning or soft delete later requires the expressly permitted
purge-safe proof covering every retained form; until then it is not configured.
Microsoft's permanent-delete operation for soft-deleted versions/snapshots is
not a general immediate-delete guarantee for all soft-deleted blobs.
[M12][m12] [M13][m13]

### FR-04 — Keys, secrets and transport protection

Use **Azure Key Vault in Canada Central**, managed identities, Azure RBAC, soft
delete and purge protection. No stored runtime Azure credential, production
secret in source control or non-production identity is permitted. Use CMKs for
source Blob service encryption, application content-key wrapping, and Azure SQL
TDE where supported by the existing Azure SQL Database topology. The documented
Blob/SQL CMK integrations support Key Vault and managed identities; preserve
their prerequisites and verify the combined private-network configuration.
[M14][m14] [M15][m15] [M16][m16] [M17][m17]

Active encryption keys have a **90-day rotation target**. Preserve old versions
only while legitimately retained ciphertext or recoverable approved backups
need them; do not destroy a required key before successful rewrap/re-encryption
or expiry. Retirement is not proof of source purge. Key Vault purge protection
delays physical key deletion; disabling access and destroying ciphertext are
different events. Privileged incident revocation and emergency key work require
PIM/JIT, justification and review. No customer key is exported in plaintext.
[M16][m16] [M18][m18]

Configure a shared Canadian Azure Blob Data Protection key repository protected
by Key Vault and managed identity, with environment separation and an application
discriminator shared only by intended production instances. Keep historical
wrapping keys needed by valid key-ring material. Durable sessions, not successful
cookie decryption alone, continue to authorize access. [M19][m19]

Encrypt every transit leg; no trust-server-certificate bypass, HTTP fallback,
disabled hostname check or source-bearing diagnostic TLS interception. Microsoft
documents TLS 1.2-or-higher for Application Gateway and the DI privacy page calls
for a TLS 1.3-capable client OS; production verification must exercise the actual
runtime negotiation and satisfy the current service requirement. [M04][m04]
[M20][m20]

Service-managed encryption is acceptable only within the human-approved policy
for resources without taxpayer source/structured data. DI's documented CMK
scope includes analysis responses, but does not explicitly establish input-byte
CMK coverage. Do not infer that coverage or an encryption exception. This is a
deployment-only assurance blocker, not a design or `/plan` blocker; first
real-taxpayer processing remains refused until authoritative Microsoft
documentation and/or contractual evidence proves compatibility. [M21][m21]

### FR-05 — Private networking and denied egress

App Service outbound uses VNet integration. Blob, SQL, Key Vault and DI each use
private endpoints/private DNS and disabled public network access wherever
supported. App Service ingress and egress are distinct controls and require
distinct supported subnets. A private endpoint does not itself disable a public
endpoint. Verify resolution from every running application instance and after
recovery. [M05][m05] [M09][m09] [M22][m22] [M23][m23] [M24][m24]

Runtime outbound is **deny by default**. Explicit exceptions contain only proven
necessary Entra token/identity endpoints, Azure platform endpoints, approved
private dependencies and required certificate/CRL endpoints. They never carry
taxpayer payloads to an identity/platform/monitoring destination. No arbitrary
HTTP endpoint is configurable. Application and configuration traffic routing
must both be examined; a VNet integration toggle alone is insufficient.
Microsoft documents route-all and NSG/firewall enforcement, not an automatic
deny-all posture. [M25][m25] [M26][m26]

The architecture must enumerate the concrete egress enforcement and dependency
rules without widening this boundary; unsupported combinations block release.
Test a synthetic planted document-byte transmission to an unapproved destination
from the runtime and prove it is denied. Do not use real taxpayer bytes in tests.

### FR-06 — One approved external document processor

Use **Azure AI Document Intelligence v4.0 GA**, REST API **`2024-11-30`**, model
**`prebuilt-layout`**, with **`features=keyValuePairs`** where supported and useful.
The resource must be the specifically approved **Canada Central** resource.
Microsoft documents this GA version, model and optional feature. Entra token
authentication uses a resource-specific custom-subdomain endpoint rather than
a generic regional endpoint. [M27][m27] [M28][m28] [M29][m29]

This is an **explicit, narrowly bounded Canadian external-processor disclosure
exception**: after in-process decryption the complete document may be sent,
including its SIN regions and approved medical receipt content. Only the
configured Canada Central DI resource receives it. No second external processor,
LLM/generative extraction, preview API, US tax prebuilt model, provider-side
custom human review or training workflow is permitted.

The approved source-location list is: TaxAI process memory; the approved Canadian
source store in encrypted form; the approved Canada Central DI service's
documented temporary storage; and the Canadian gateway's narrowly authorized
transient in-memory TLS/WAF processing under FR-02. The gateway exception adds no
persistent location or secondary external document processor.
No full source or raw provider result enters TaxAI SQL, telemetry, support,
browser, files, durable queue, test fixture or incident attachment.

The adapter implements existing ports and transforms layout/text into the
existing untrusted in-memory shapes. It copies as-printed values, preserves
duplicates and omissions, invents no confidence, and assigns no tax meaning.
Only the existing validation/engine chain decides valid tax input. Ambiguous or
unsupported document interpretation fails closed; it does not select a new
model or manufacture fields. No prompt is needed or permitted.

### FR-07 — Provider lifecycle, deletion and failure

The approved processor exception **does retain data temporarily**. Microsoft
documents same-region encrypted input/results storage and automatic deletion
24 hours after analysis completion in its current privacy page. It also documents
earlier deletion of input/results using Delete Analyze Result. The REST operation
describes marking a result for deletion and returns HTTP 204. These descriptions
must not be represented as a measured instant physical-erasure guarantee.
[M20][m20] [M30][m30]

For each analysis: submit one document; retrieve the result; transform immediately
into the existing transient representation; discard raw JSON/OCR payload; call
Delete Analyze Result as soon as the provider result is no longer needed. Attempt
deletion on terminal failure whenever an operation/result identifier is known.
Identifiers live in process only, not in a persistence queue for later deletion.
Use bounded in-process deletion retries. No durable raw result is retained for
retry. No acceptance-eligible result is released until explicit deletion succeeds.

When deletion exhausts its bound, extraction fails closed and no accountant
acceptance from that operation is possible. Emit only a fixed structural
security/availability event; Microsoft's automatic deletion is the final
provider-side backstop. Process loss or ambiguous submission without a known
identifier cannot be portrayed as a successful explicit deletion; the operation
remains unsuccessful and relies on that documented provider backstop.

Never automatically retry an Analyze submission capable of creating an ambiguous
duplicate unless a proven idempotent mechanism exists. Poll/read retries use
bounded exponential backoff; deletion retries are bounded; the entire operation
has a cancellation/deadline budget. Cleanup is attempted within a finite budget
on cancellation, never by detaching an unbounded background job. Retry/deadline
configuration must be explicit, finite and validated; SDK defaults must not
silently add submission retries. Preserve existing Spec 014 upload limits and
Spec 015 call-limit authority. A stricter provider technical limit causes an
explicit safe failure, never page truncation or partial acceptance. [M31][m31]

### FR-08 — Processor assurance and change control

Before any real-taxpayer use, the named Security/Privacy approver must explicitly
approve evidence for Canada Central processing, same-region temporary storage,
the subscription's current Microsoft DPA/Product Terms and subprocessors, no
customer-content training/secondary use beyond the contracted service, no
provider-side human review, deletion, encryption and networking commitments.
The Product Owner may hold this role only if the record explicitly says so.

The public DI pages do not establish that this subscription's contractual review
has happened. Do not infer absolute no-human-access or no-secondary-use promises
from a resource's region. A contractual/service change invalidating approved
assumptions blocks new extraction until reviewed. Samples suggesting uploaded
support documents, labeling or Studio access do not authorize those disclosures.
The evidence record must identify the actual applicable terms, not terms for
Copilot, Azure OpenAI, Microsoft 365 or another product. [M20][m20] [M32][m32]

### FR-09 — Source and structured retention model

Deadlines are stored UTC instants derived from authoritative immutable workflow
events, not blob last-modified time, operator guesses or taxpayer values.

| Lifecycle class | Approved trigger and deadline | Required outcome |
|---|---|---|
| Successfully finalized case source | Case-level final acceptance plus 30 days | Purge source bytes; retain structured history |
| Rejected document source | Terminal accountant rejection plus 7 days | Purge that source |
| Accountant-confirmed duplicate source | Terminal accountant duplicate decision plus 7 days | Purge the duplicate's source, not the retained evidence it references |
| Abandoned intake/case source | 30 days after the later of intake/session expiry and last recorded case activity | Purge abandoned sources |
| Failed/partial/staged upload | Within 24 hours | Remove all staged/uncommitted representations; existing immediate compensating cleanup remains |
| Unresolved correction or pre-finalization review | No upload-age-only deadline | Preserve needed source until its authorized terminal lifecycle applies |
| Structured extraction/review history | Existing `RetentionPolicy` exclusive boundary | No change: seven calendar years after exclusive end of the record's tax year |

Source expiry does not erase extraction versions, medical versions, corrections,
overrides, duplicate decisions, acceptances or immutable review history early.
Append-only history allows an appended lifecycle fact; it does not authorize an
in-place rewrite of taxpayer evidence. The seven-year boundary is an engineering
product decision, **not a statutory or CRA retention claim**.

After terminal source purge, correction, re-extraction or finalization requiring
that source fails closed. It cannot reconstruct from SQL, OCR cache, provider
history or backup. A newly obtained upload is new evidence with its own immutable
history. Required unresolved review takes precedence over mere upload age;
uncertain lifecycle attribution cannot authorize deletion.

### FR-10 — Coordinated purge and destruction evidence

Routine due purge is system initiated. A privileged Security/Privacy operator
may initiate an already-due authorized purge, never erase before its boundary.
Source purge and seven-year structured purge are separate lifecycle classes.
Use explicit trusted firm/case/document scope and the existing retention
authority. Extend owner-provided purge contracts where required; no module reads
another's tables. Scheduling must remain within the single-host/composition-root
decision and be safe across multiple application instances.

Cover primary objects, replicas/failover representations, any permitted deleted
forms/versions, temporary application copies, and applicable provider results
whose identifiers are still in memory. Provider deletion never creates a durable
identifier-retry store. Ordinary append-only rules yield only to an authorized
terminal lifecycle, not an administrative convenience delete.

Purge is idempotent and retry-safe. A failed or unverified physical deletion is
not marked complete. Record only opaque resource reference, purge category,
initiated UTC instant, completed UTC instant and outcome. Replica propagation
must be accounted for; deleting a primary alone is not proof that an
asynchronously replicated copy is gone. Platform destruction commitments and
observable deletion/replication evidence must be distinguished; do not claim a
physical-erasure SLA Microsoft does not document. [M08][m08] [M13][m13]

Maintain a separate durable Canadian structural purge ledger/control record
containing opaque identifiers and purge timestamps only. It survives restoration
of the main SQL database and is retained **longer than the longest recoverable
backup window**, including actual service backup-chain dependencies. A restored
environment stays quarantined if this control record is unavailable or incomplete.
No legal/privacy deletion exception to the seven-year boundary is invented;
conflicting legal requests require a separate explicit compliance decision/spec.

### FR-11 — Recovery and backup boundaries

| Event | Approved objective | Constraints |
|---|---|---|
| Zone failure | RTO <=15 minutes; no planned loss of committed data on synchronously zone-redundant resources | Test the whole healthy dependency path, not only worker count |
| Canada Central region failure | Recoverable application service RTO <=4 hours; structured SQL target RPO <=15 minutes | Manual/controlled Canadian recovery only; unavailable Canada Central extraction remains unavailable |
| Missing recent source after regional failover | No asserted source RPO or zero-loss promise | Block source-dependent operations; obtain a new upload |

Keep the approved Azure SQL Database topology and Persistence ownership. Use
GA Canadian SQL DR mechanisms capable of the target; validate zone redundancy,
CMK/key access, private networking, migration compatibility and Canada East
recovery together. Microsoft documents zone-redundant SQL tiers and manual
failover groups. Geo-replication is asynchronous; geo-restore alone must not be
assumed to satisfy the stated RPO/RTO. No automatically initiated cross-region
application traffic failover is authorized. [M33][m33] [M34][m34]

SQL point-in-time operational retention uses the maximum practical short-term
window, capped at **35 days** unless the existing topology has a stricter approved
limit. No seven-year raw SQL backup archive or long-term-retention policy is
created merely because application records live seven years. Microsoft documents
the 35-day maximum for non-Basic databases and notes that underlying backup
chains may contain older backups; the ledger must account for the actual
recoverable window, not just a nominal setting. [M35][m35]

Recovery stays quarantined until due/terminal purge reconciliation, revoked-session
reconciliation, expired-credential checks, and security/configuration validation
all pass. Keys and Data Protection material required for authorized recovery are
available without reviving expired authority. A copied old cookie is still denied.
No real production copy enters non-production for a restore drill or debugging.

Canadian Blob geo-redundancy and key availability are separate recovery concerns.
Key Vault's Microsoft-managed regional failover can be delayed and is best effort;
it alone does not prove the four-hour objective. A tested Canadian key recovery
arrangement must preserve version access and RBAC/private networking. Documented
encrypted Key Vault backup/restore restrictions (same subscription/geography)
must be respected. No plaintext key export or foreign backup is authorized.
[M36][m36]

**Approved Q-A2 recovery scope:** the four-hour regional RTO applies to
recoverable application service, not every external/provider-backed capability.
It does not imply a four-hour extraction RTO. DI remains Canada Central only;
during a continuing regional outage or unavailable approved DI resource,
extraction remains unavailable even after application recovery in Canada East.

After quarantine checks pass, recovered service may authenticate, display
already-authorized case/review state where its dependencies are available, and
accept operations not requiring unavailable extraction. It must clearly report
temporary extraction unavailability without exposing taxpayer values. Every
operation requiring new DI processing fails closed.

No DI failover to Canada East, another Canadian region, a foreign region, another
OCR/provider, deterministic Development/test fallback or security bypass is
authorized to meet the application RTO. Extraction resumes only after the
approved Canada Central provider boundary is valid again, unless a separately
approved future specification changes that policy.

### FR-12 — Environment separation and operational identities

Production and non-production have separate subscriptions, resource groups,
VNets, private DNS links, Storage, SQL, Key Vaults, keys, managed identities,
DI resources and Application Insights/Log Analytics resources. No production
secret or identity is shared downward. Local, CI, test, demo and staging use only
synthetic documents/PII; production data is never copied downward for debugging.

Runtime application access uses least-privilege managed identities. CI/CD uses
a dedicated workload identity; migrations use a separate deployment/migration
identity, not broad runtime privileges. Workload identity federation is a
documented credential-free option, not permission to trust arbitrary repositories
or branches. Bootstrap identity and infrastructure creation are in scope; public
self-registration and application firm/user provisioning remain unchanged.
[M37][m37]

No standing human production taxpayer-data-plane access. Privileged human roles
use Entra PIM/JIT with phishing-resistant MFA where available, a reason,
time-limited activation, high-impact-role approval and complete activation audit.
Routine operators see only safe telemetry. Blob/SQL/Key Vault data access requires
incident/break-glass elevation; it is time limited, separately audited and
reviewed afterward by the named deployment-record approver. PIM supports duration,
justification, approval and Conditional Access authentication context; merely
assigning PIM does not prove phishing-resistant authentication. [M38][m38]

### FR-13 — Taxpayer-safe observability

Use Application Insights and Log Analytics provisioned in **Canada Central**,
with regional ingestion configuration, RBAC/PIM and **30-day default retention**.
A particular security log may be retained longer only with separate recorded
justification. Set table-level retention explicitly where defaults differ;
changing retention later is not immediate erasure. Monitoring may use
service-managed encryption only because taxpayer content is prohibited there.
[M39][m39] [M40][m40] [M41][m41]

Telemetry is allowlist-based at collection, before any SDK/platform export.
Permitted fields are route template (not route value), status code, duration,
dependency category, fixed error code, deployment version, health state,
structural audit kind and anonymous aggregate counters without taxpayer labels.

Prohibited everywhere in telemetry: bodies/form content; document bytes; OCR,
provider JSON, prompts, operation payloads; SIN; taxpayer/employer/patient/provider
names; medical values; amounts/box values; content keys/fingerprints; filenames;
cookies/authorization headers; query values/raw route IDs; SQL parameters;
payload-bearing exception objects; memory dumps; memory-bearing support bundles.
This includes logs, metric labels/exemplars, trace tags/events/links/baggage,
SDK transport diagnostics, WAF access/matched-value logs, SQL diagnostics,
platform HTTP logs, alerts and deployment failure output. The approved gateway
TLS/WAF memory exception permits no taxpayer request/response-body logging,
packet/body capture, payload-bearing diagnostics, custom WAF document/form logs,
payload support bundles or intentional plaintext persistence/export/retention.

Crash dumps are disabled by default. Content-capturing diagnostic escalation
requires a separately approved incident-response exception. No such exception is
granted here. WAF log scrubbing exists, but does not itself establish the full
allowlist; unsafe categories remain disabled unless exclusion is proven before
collection. Sanitizing an already persisted payload is not compliance.
[M42][m42]

### FR-14 — Production startup, runtime failure and release gate

Startup in the production deployment path must explicitly identify Production,
Canada Central primary policy, and the approved resource inventory. Validate
actual registrations, not just option strings: production DI; approved GA API,
model and resource/region; production encrypted store/reader; managed identities;
Storage/Key Vault/SQL/monitoring; shared protected Data Protection; approved
retention/retry/deadline configuration; and private endpoint/public-access posture.
No Development provider, local filesystem source store, missing key ring,
unencrypted SQL connection, arbitrary endpoint or silent default is accepted.

An endpoint hostname alone does not prove its resource region. Bind endpoint,
Azure resource identity, environment and verified deployment configuration.
Validation must not grant broad runtime control-plane privileges. Missing,
inconsistent or unverifiable required evidence refuses taxpayer-data startup.
An invalid/unsupported regional recovery profile cannot silently bypass these
checks. The FR-11 approved degraded-recovery profile distinguishes an unavailable
Canada Central extraction dependency from invalid security configuration: only
operations with valid available dependencies may run; new DI processing remains
disabled. Recovery never changes the Canada Central-only provider policy.

After startup, unhealthy or invalid secure dependencies fail the affected
taxpayer-data operation closed. No public-network, local-disk, other-region,
Development-provider or secondary-external-service fallback. Unaffected portal
functions may remain available only when their own dependencies remain valid.

Remove the unconditional development-only refusal only by replacing it with
enforced production validation and the deployment acceptance gate. A feature flag,
environment rename, documentation note or arbitrary operator bypass is not enough.
The existing refusal remains until every mandatory evidence item below passes
and both human roles sign the exact configuration. Later invalidation withdraws
authorization for the affected operation; deployment evidence cannot be reused
blindly after material drift.

## Threat model requirements

The approved threat model must cover the following trust-boundary paths with
assets, attacker, entry point, mitigation, residual risk and a test/evidence owner.

| Threat / boundary | Required control and proof |
|---|---|
| Upload through Canadian ingress | Approved gateway transient in-memory TLS/WAF plaintext only; no intentional persistence, export, logging, retention, taxpayer capture or payload support bundle; certificate-validated HTTPS/TLS re-encryption to private App Service; bounded upload |
| Cross-firm/case/document source substitution | Server-owned attribution checked before decrypt/read/provider submission; empty 404 equivalence |
| Blob tampering or replay | Authenticated encryption, upload-once enforcement and scope binding; no partial plaintext returned |
| TaxAI memory to external processor | Pinned DI resource/API/model, private routing, no arbitrary URL or redirects to other resources |
| DNS/endpoint/configuration substitution | Inventory binding, private DNS verification and denied unapproved egress |
| Provider leakage/retention/deletion failure | Narrow processor exception, assurance approval, immediate deletion, bounded retries, failed-operation nonacceptance |
| Raw OCR/SIN escaping adapter | In-memory-only transformation, existing closed shapes and planted-value sweeps with positive controls |
| Privileged access/key theft | Identity separation, RBAC/PIM, no standing human data access, no plaintext keys/dumps |
| Purge race versus extraction/correction | Terminal lifecycle wins; no new successful source lease/acceptance after purge; retry-safe scope |
| Backup/failover resurrection | Separate structural ledger, quarantined restore, revocation/expiry reconciliation |
| Capacity/dependency loss | Bounded work and cancellation, safe fixed failures, no insecure fallback; four-hour application recovery excludes unavailable Canada Central extraction |
| Supply-chain/deployment or diagnostics drift | Pinned reproducible artifacts, workload identity, full gates and exact-version human approval |

No threat-model statement may imply that private networking alone prevents a
compromised authorized process from misusing an allowed dependency. Include
least privilege, response/payload discipline and tested denial as separate layers.

## Migration constraints and rollback

Spec 015 is complete; no existing Spec 015 task is reopened. This invocation
writes only this PRD. Future `/plan` must own any new contracts/migrations and
instruction-boundary amendments explicitly; it must not edit historical tasks.

Use inward contracts and owner-managed additive lifecycle metadata. Existing
review/intake/extraction data and seven-year boundary remain readable unchanged.
Do not reseed production, run `EnsureCreated`, migrate at application startup,
copy synthetic keys into production, or convert Development source objects into
real production evidence. Freeze the public route/field behavior except the
specified secure dependency refusal behavior.

[ASSUMED] Delivery ordering is evidence/contracts first, infrastructure/adapters
next, integration/lifecycle/recovery verification next, then guarded release.
This is not a task plan or permission to run `/plan`. No task IDs are invented.

Rollback withdraws production authorization and stops affected operations before
returning to the last compatible approved artifact/configuration. Never roll
back into the Development provider, a public origin, lost purge history or a
schema/key version unable to read retained ciphertext. Retain irreversible
purge decisions and revocations across rollback; quarantine restore-based rollback
exactly as recovery. Failed migration/rotation leaves production closed until a
verified compatible recovery, not a bypass.

## Out of scope

- Any accidental tax mapping, calculation, parser, tax-year or medical tax-treatment change.
- Another external processor/model, LLM, generative extraction, preview service or non-Canadian processing.
- Source preview/download/content serving, export, new browser allowlists or taxpayer-data API.
- Automatic multi-region traffic failover or normally active Canada East portal.
- New application firm/user provisioning, public signup, ordinary-accountant deletion or new legal-hold/deletion law.
- A seven-year raw backup archive, training dataset, provider human review or production-to-test data copy.
- Actual implementation, cloud provisioning, deployment, `/plan`, `architecture.md`, `tasks.md` or completion markers in this invocation.

## Acceptance criteria

Each numbered row is one check: the middle column is Given/When and the final
column is the single expected outcome. Negative rows are independent tests, not
one catch-all invalid-configuration test. Infrastructure checks inspect deployed
state as well as IaC. Integration/drill evidence uses synthetic taxpayer-shaped
data only. A passing configuration check is not substituted for a failure test.

### Preserved behavior and structural ownership

| ID / trace | Given / When | Then |
|---|---|---|
| AC-001 B-1–B-7 | Final implementation runs the authoritative full repository gate | All existing tax/production regression behavior and all registered test suites remain green with no failures, skips or decreased floors |
| AC-002 B-3 | Compare tax parser, mapping, calculation and tax-year behavior with the Spec 015 baseline | No semantic difference exists |
| AC-003 B-1 | Inspect executable projects and startup registrations | Portal is the only production host/composition root |
| AC-004 B-2 | Inspect DI package references | Only DocumentIntelligence owns the DI SDK |
| AC-005 B-2 | Inspect non-DI cloud adapters | They implement inward ports in the dedicated Azure infrastructure project |
| AC-006 B-2 | Inspect EF Core references | Persistence remains the sole production EF Core project |
| AC-007 B-3 | Inspect all eight protected deterministic modules | None references a cloud SDK |
| AC-008 B-3 | Scan Portal and infrastructure with positive scanner controls | No tax authority/logic is introduced |
| AC-009 B-4 | Execute a synthetic full source-to-review journey with a planted SIN | No TaxAI-owned structured persisted/browser/diagnostic surface contains any SIN representation |
| AC-010 B-5 | Request each existing resource as a foreign firm and as missing | Both responses have the same empty 404 representation |
| AC-011 B-5 | Revoke membership after page rendering, then mutate | Mutation is refused before source/provider access |
| AC-012 B-5 | Apply an accepted correction | Earlier taxpayer-data history remains unchanged |
| AC-013 B-6 | Inspect the route/response inventory after deployment | No raw-content serving, preview, download, print or export surface exists |
| AC-014 B-6 | Exercise the existing browser projections | Their approved field allowlists are unchanged |

### Hosting, storage, keys and network

| ID / trace | Given / When | Then |
|---|---|---|
| AC-015 FR-01 | Inspect the complete normal-operation resource inventory | All required primary data/processing/key/monitoring locations are Canada Central |
| AC-016 FR-01 | Inspect every DR/backup destination | No destination is outside the approved Canadian pair |
| AC-017 FR-01 | Attempt deployment with a preview resource feature | Deployment is refused |
| AC-018 FR-02 | Inspect the production App Service plan | Its SKU is P1v3 |
| AC-019 FR-02 | Inspect actual plan/app placement | Zone-redundant production has at least two running application workers |
| AC-020 FR-02 | Exercise every autoscale scale-in rule | Running production capacity never drops below two |
| AC-021 FR-02 | Probe public listeners | HTTPS 443 through the approved WAF v2 ingress is the only application ingress |
| AC-022 FR-02 | Attempt public-origin/SCM bypass | The bypass is denied |
| AC-023 FR-02 | Inspect the gateway-to-private-App-Service handshake after gateway TLS termination | The outgoing backend leg uses certificate-validated HTTPS/TLS re-encryption |
| AC-024 FR-02 | Exercise the existing questionnaire through ingress | Its approved Interactive Server behavior succeeds without widening interactivity |
| AC-025 FR-03 | Inspect source account kind/redundancy | GPv2 GZRS is configured when supported by the approved account configuration |
| AC-026 FR-03 | Upload a synthetic document, then inspect stored bytes | The source object is application ciphertext, not plaintext |
| AC-027 FR-03 | Attempt to overwrite an existing source object | The overwrite fails |
| AC-028 FR-03 | Inspect client/browser responses | No source SAS or blob access URL is exposed |
| AC-029 FR-03 | Inspect the default source protection configuration | No snapshot, versioning, soft-delete or WORM shadow-retention mechanism is enabled |
| AC-030 FR-03 | Open content using a foreign firm | No source lease is returned |
| AC-031 FR-03 | Open content using the wrong case | No source lease is returned |
| AC-032 FR-03 | Open content using the wrong document | No source lease is returned |
| AC-033 FR-03 | Open content using an unassociated content key | No source lease is returned |
| AC-034 FR-03 | Open a tampered ciphertext | No source lease is returned |
| AC-035 FR-03 | Open content whose wrapping key cannot be used | No source lease is returned |
| AC-036 FR-03 | Finish a source read, including error/cancellation paths | Its in-memory lease is disposed |
| AC-037 FR-03 | Capture runtime filesystem writes during extraction | No plaintext source or provider payload is written |
| AC-038 FR-04 | Inspect source encryption envelopes from two uploads | Each uses a distinct per-content data key |
| AC-039 FR-04 | Inspect persisted key/envelope material | No plaintext encryption key is present |
| AC-040 FR-04 | Inspect source service encryption | It uses the approved Key Vault CMK |
| AC-041 FR-04 | Inspect Azure SQL TDE | It uses the approved supported CMK configuration |
| AC-042 FR-04 | Inspect Key Vault authorization | Azure RBAC is enabled |
| AC-043 FR-04 | Inspect Key Vault deletion configuration | Soft delete and purge protection are enabled |
| AC-044 FR-04 | Inspect active key rotation policy | Its target interval is 90 days |
| AC-045 FR-04 | Run the rotation drill against retained old ciphertext | Authorized old ciphertext remains decryptable |
| AC-046 FR-04 | Attempt to destroy a key still required by retained ciphertext | The operation is refused |
| AC-047 FR-04 | Issue a valid protected session on one worker and use it on another | The existing session contract succeeds using the shared protected key ring |
| AC-048 FR-04 | Present an invalid backend/service TLS certificate | The connection fails closed |
| AC-049 FR-05 | Resolve Blob from every running application worker | Resolution targets the approved private endpoint |
| AC-050 FR-05 | Resolve SQL from every running application worker | Resolution targets the approved private endpoint |
| AC-051 FR-05 | Resolve Key Vault from every running application worker | Resolution targets the approved private endpoint |
| AC-052 FR-05 | Resolve DI from every running application worker | Resolution targets the approved private endpoint |
| AC-053 FR-05 | Access Blob via its public endpoint | Public access is denied |
| AC-054 FR-05 | Access SQL via its public endpoint | Public access is denied |
| AC-055 FR-05 | Access Key Vault via its public endpoint | Public access is denied |
| AC-056 FR-05 | Access DI via its public endpoint | Public access is denied |
| AC-057 FR-05 | Attempt synthetic document-byte transmission to an unapproved external destination | No payload reaches that destination |
| AC-058 FR-05 | Audit application and configuration outbound routes | No unrestricted internet bypass of the reviewed deny-default policy exists |

### Provider and deletion

| ID / trace | Given / When | Then |
|---|---|---|
| AC-059 FR-06 | Capture a successful synthetic production analysis request | Its destination is the approved Canada Central resource |
| AC-060 FR-06 | Inspect the analysis request version | It is exactly `2024-11-30` |
| AC-061 FR-06 | Inspect the analysis request model | It is exactly `prebuilt-layout` |
| AC-062 FR-06 | Exercise approved key-value extraction | The request uses the documented `keyValuePairs` option |
| AC-063 FR-06 | Transform a synthetic response with duplicates and as-printed amounts | Existing untrusted representation retains those values without repair |
| AC-064 FR-06 | Transform a response lacking confidence | No confidence is invented |
| AC-065 FR-06 | Supply unsupported/ambiguous document classification | No supported classification is guessed |
| AC-066 FR-06 | Configure an LLM, US-tax, custom-review or alternate model | The provider configuration is refused |
| AC-067 FR-06 | Receive a provider redirect to an unapproved resource | No taxpayer payload follows it |
| AC-068 FR-07 | Complete a successful analysis transformation | Delete Analyze Result is called immediately after use |
| AC-069 FR-07 | Fail terminally after obtaining a result identifier | Explicit deletion is attempted |
| AC-070 FR-07 | Exhaust the configured bounded deletion retries | No acceptance-eligible extraction is produced |
| AC-071 FR-07 | Attempt accountant acceptance from that failed operation | Acceptance is refused |
| AC-072 FR-07 | Capture deletion-failure diagnostics | Only the fixed structural event is emitted |
| AC-073 FR-07 | Inspect durable stores after success/failure/process-loss simulation | No provider-result identifier is persisted solely for deletion retry |
| AC-074 FR-07 | Inspect durable stores after provider interaction | No raw input/response/OCR/prompt/operation payload is persisted |
| AC-075 FR-07 | Lose the Analyze acknowledgement ambiguously | No automatic Analyze resubmission occurs |
| AC-076 FR-07 | Inject transient poll/read failures | Attempts remain within the configured exponential retry bound |
| AC-077 FR-07 | Inject repeated deletion failures | Attempts stop at the configured deletion retry bound |
| AC-078 FR-07 | Cancel analysis during polling | Work stops within the enforced operation/cleanup deadline |
| AC-079 FR-07 | Return malformed or partial provider output | No partial extraction is accepted |
| AC-080 FR-07 | Submit a document exceeding a stricter provider technical limit | The operation explicitly fails without truncation |
| AC-081 FR-07 | Exercise Spec 014 upload-limit boundary cases | Application upload behavior remains unchanged |
| AC-082 FR-08 | Withdraw the recorded processor assurance approval | New extraction is blocked |
| AC-083 FR-08 | Mark a contractual/service assumption invalid | New extraction remains blocked until explicit review |

### Retention, purge, recovery and access

| ID / trace | Given / When | Then |
|---|---|---|
| AC-084 FR-09 | Final case acceptance is recorded | Source deadline is exactly acceptance plus 30 days |
| AC-085 FR-09 | A terminal accountant rejection is recorded | Its source deadline is exactly decision plus 7 days |
| AC-086 FR-09 | A terminal accountant duplicate decision is recorded | Its source deadline is exactly decision plus 7 days |
| AC-087 FR-09 | Abandonment is evaluated | Source deadline is 30 days after the later authorized expiry/activity instant |
| AC-088 FR-09 | A failed/partial/staged upload reaches 24 hours | No staged representation remains |
| AC-089 FR-09 | An old upload is required by unresolved correction/review | Age alone does not authorize purge |
| AC-090 FR-09 | Open a terminally purged source | The source-dependent operation fails closed |
| AC-091 FR-09 | Re-upload after terminal purge | The upload has a new immutable evidence identity |
| AC-092 FR-09 | Purge a short-lived source | Its not-yet-due structured history is preserved |
| AC-093 FR-09 | Compute any structured-history retention instant | It equals the existing exclusive RetentionPolicy result |
| AC-094 FR-10 | Evaluate structured purge one tick before its boundary | The record survives |
| AC-095 FR-10 | Evaluate structured purge exactly at its boundary | The record is eligible |
| AC-096 FR-10 | Invoke due purge for another firm | Foreign resources remain unchanged |
| AC-097 FR-10 | Repeat a successfully completed due purge | No additional non-due data is removed |
| AC-098 FR-10 | A physical/replica deletion cannot be confirmed | Purge is not recorded as complete |
| AC-099 FR-10 | Inspect a completed purge audit | Its fields equal the structural purge allowlist |
| AC-100 FR-10 | An ordinary accountant invokes purge | Access is denied |
| AC-101 FR-10 | A privileged operator requests a not-yet-due purge | The early deletion is denied |
| AC-102 FR-10 | Restore SQL from before a terminal purge | Separate-ledger reconciliation removes the resurrected resource before release |
| AC-103 FR-10 | Inspect ledger retention against the longest recoverable backup window | Ledger retention is strictly longer |
| AC-104 FR-10 | Race terminal purge against a source-dependent operation | No new successful operation uses the terminally purged source |
| AC-105 FR-11 | Run the approved zone-failure drill | Recovery completes within 15 minutes |
| AC-106 FR-11 | Compare committed synchronously redundant data across the zone drill | No committed data is lost |
| AC-107 FR-11 | Run the Canada East regional application-recovery drill while approved Canada Central DI remains unavailable | Recoverable application service is restored within four hours |
| AC-108 FR-11 | Measure structured SQL loss in the regional drill | RPO is at most 15 minutes |
| AC-109 FR-11 | Inspect regional failover policy | Cross-region application traffic activation is controlled/manual |
| AC-110 FR-11 | A recent source is absent after failover | Source-dependent operations require a new upload |
| AC-111 FR-11 | Inspect SQL short-term retention | It is the maximum practical approved value, no more than 35 days |
| AC-112 FR-11 | Inspect backup policies | No seven-year raw SQL long-term archive exists |
| AC-113 FR-11 | Restore before a session's revocation and present its old cookie | Access remains denied |
| AC-114 FR-11 | Restore an expired credential | It remains unusable |
| AC-115 FR-11 | Restore with missing purge control history | The environment remains quarantined |
| AC-116 FR-11 | Restore with invalid security configuration | The environment remains quarantined |
| AC-117 FR-12 | Compare production/non-production inventories | All enumerated environment-separation boundaries are distinct |
| AC-118 FR-12 | Attempt non-production identity access to production | Access is denied |
| AC-119 FR-12 | Inspect non-production data inventories | They contain only synthetic data |
| AC-120 FR-12 | Inspect human taxpayer-data-plane assignments | No standing human access exists |
| AC-121 FR-12 | Attempt migration using runtime privileges | Migration is denied |
| AC-122 FR-12 | Activate a high-impact role without required approval | Activation is denied |
| AC-123 FR-12 | Attempt privileged activation without required authentication strength | Activation is denied |
| AC-124 FR-12 | Inspect a break-glass event | It has the required reason, bounded activation and named review record |

### Diagnostics and fail-closed configuration

For AC-125 the telemetry harness must capture every enabled collector/exporter,
including gateway TLS/WAF diagnostics, all numeric metrics and activity
events/links/baggage, and use positive controls proving planted prohibited values
would be detected. This is a single
allowlist-conformance assertion, not permission to skip inaccessible surfaces.

| ID / trace | Given / When | Then |
|---|---|---|
| AC-125 FR-13 | Run planted-data ingress/TLS/WAF, success, failure, retry, purge and restore flows with complete telemetry capture | Every captured field conforms to the telemetry allowlist |
| AC-126 FR-13 | Inspect monitoring destinations/ingestion configuration | They are approved Canadian regional destinations |
| AC-127 FR-13 | Inspect each enabled telemetry table's retention | It is 30 days unless separately justified as a security-log exception |
| AC-128 FR-13 | Inspect production dump/support settings | No memory-content capture is enabled |
| AC-129 FR-13 | Enable an unsafe SDK/platform log category without exclusion proof | Production validation refuses it |
| AC-130 FR-14 | Omit explicit Production environment from the production path | Startup refuses |
| AC-131 FR-14 | Configure a primary outside Canada Central | Startup refuses |
| AC-132 FR-14 | Register the Development provider in Production | Startup refuses |
| AC-133 FR-14 | Omit the production DI adapter | Startup refuses |
| AC-134 FR-14 | Configure a nonapproved/preview DI API version | Startup refuses |
| AC-135 FR-14 | Configure a DI endpoint without matching resource/region evidence | Startup refuses |
| AC-136 FR-14 | Omit production Storage configuration | Startup refuses |
| AC-137 FR-14 | Omit Key Vault configuration | Startup refuses |
| AC-138 FR-14 | Omit SQL configuration | Startup refuses |
| AC-139 FR-14 | Omit monitoring configuration | Startup refuses |
| AC-140 FR-14 | Omit the required managed identity binding | Startup refuses |
| AC-141 FR-14 | Omit source envelope-encryption configuration | Startup refuses |
| AC-142 FR-14 | Register a local filesystem source store in Production | Startup refuses |
| AC-143 FR-14 | Omit the durable protected Data Protection registration | Startup refuses |
| AC-144 FR-14 | Configure unencrypted/unvalidated SQL transport | Startup refuses |
| AC-145 FR-14 | Configure retention outside approved policy | Startup refuses |
| AC-146 FR-14 | Configure unbounded retries or no deadline | Startup refuses |
| AC-147 FR-14 | Substitute a public dependency endpoint | Startup refuses |
| AC-148 FR-14 | Configure an arbitrary outbound destination | Startup refuses |
| AC-149 FR-14 | Invalidate a dependency's verified security posture after startup | The affected operation fails closed |
| AC-150 FR-14 | Make an approved dependency unavailable | No insecure fallback is used |
| AC-151 FR-14 | Remove one mandatory deployment evidence item | Real-taxpayer deployment remains refused |
| AC-152 FR-14 | Provide all required passing evidence and both role approvals | Only the exact approved production configuration can pass the replacement deployment gate |

### Approved ingress exception, degraded recovery and assurance gates

These checks apply the explicit human Q-A1/Q-A2 resolutions. Recovery checks run
only after all applicable quarantine/security checks pass; permitting a healthy
operation never authorizes bypassing its own dependencies or access rules.

| ID / trace | Given / When | Then |
|---|---|---|
| AC-153 FR-02, FR-13 | Inspect the gateway's enabled persistence/export/capture paths with a synthetic planted-body test and positive detection controls | No path intentionally retains, persists, exports or logs gateway taxpayer plaintext |
| AC-154 FR-02 | Configure plaintext HTTP forwarding from gateway to App Service | Production validation refuses the configuration |
| AC-155 FR-11 | Authenticate in recovered application service with valid healthy identity/session dependencies while approved DI is unavailable | The existing authentication contract remains usable |
| AC-156 FR-11 | Request already-authorized case/review state in recovered service with its own dependencies healthy while DI is unavailable | The existing authorized projection remains available |
| AC-157 FR-11 | Invoke an otherwise authorized operation not requiring DI with all its own dependencies healthy in recovered service | The operation remains available |
| AC-158 FR-11 | View extraction availability during the continuing Canada Central provider outage | The portal clearly reports temporary extraction unavailability using a taxpayer-safe indication |
| AC-159 FR-11 | Invoke an operation requiring new DI processing while the approved provider is unavailable | The operation fails closed |
| AC-160 FR-11 | Attempt DI failover to Canada East to meet the application RTO | The provider substitution is refused |
| AC-161 FR-11 | Attempt DI failover to another Canadian region to meet the application RTO | The provider substitution is refused |
| AC-162 FR-11 | Attempt DI failover to a non-Canadian region to meet the application RTO | The provider substitution is refused |
| AC-163 FR-11 | Attempt a secondary OCR/extraction provider to meet the application RTO | The provider substitution is refused |
| AC-164 FR-11 | Attempt the deterministic Development/test provider during recovery | The provider substitution is refused |
| AC-165 FR-11, FR-14 | Attempt to bypass a required security check to meet the application RTO | The bypass is refused |
| AC-166 FR-11 | Restore the approved Canada Central DI boundary and pass every affected dependency/security validation | New extraction becomes eligible to resume under the existing authorization rules |
| AC-167 FR-11 | Restore provider connectivity while its approved Canada Central security boundary remains invalid | New extraction remains unavailable |
| AC-168 FR-04, FR-08, FR-14 | Leave provider input-CMK compatibility unsupported by authoritative Microsoft documentation and/or contractual evidence | First real-taxpayer processing remains refused |
| AC-169 FR-07, FR-08, FR-14 | Leave the applicable provider retention/deletion clock unresolved in authoritative Microsoft documentation and/or contractual evidence | First real-taxpayer processing remains refused |

## Production verification and deployment acceptance

These are **required future evidence**, not completed results. Every item must
identify its synthetic test/drill or approval record, timestamp, resource identity,
configuration/artifact version, outcome and accountable approver. One evidence
record may reference several individual AC tests, but none may be waived merely
because an adjacent check passed. Evidence contents obey the telemetry/data rules.

| Evidence ID | Mandatory acceptance evidence (one verification check per row) |
|---|---|
| DEP-01 | Approved threat-model record exists, including source bytes leaving the TaxAI process and the bounded Canadian gateway plaintext exception |
| DEP-02 | IaC-only reconstruction of the approved infrastructure succeeds |
| DEP-03 | Resource inventory proves Canada Central primary and Canada East-only DR use |
| DEP-04 | Actual DI resource/API/model combination is verified |
| DEP-05 | Named Security/Privacy approver accepts current Microsoft processor/privacy/retention evidence proving compatibility, including input-CMK protection and the applicable retention/deletion clock |
| DEP-06 | Private-endpoint/private-DNS test report passes |
| DEP-07 | Public-network-disabled checks pass for Storage, SQL, Key Vault and DI where supported |
| DEP-08 | Default-deny outbound test passes |
| DEP-09 | Attempted unapproved synthetic source-byte egress test proves non-delivery |
| DEP-10 | Source-byte HTTP/browser non-disclosure test passes |
| DEP-11 | Complete telemetry capture, including gateway TLS/WAF paths, passes the prohibited-data check |
| DEP-12 | Delete Analyze Result integration test passes against the approved service |
| DEP-13 | Failed explicit deletion integration test proves extraction refusal |
| DEP-14 | Key-rotation drill passes |
| DEP-15 | Source-retention/purge drill passes |
| DEP-16 | Every enabled soft-delete/version representation passes purge testing; when both are disabled, configuration evidence proves that fact |
| DEP-17 | SQL restore drill passes |
| DEP-18 | Canada East regional DR drill passes the approved application-recovery scope with unavailable Canada Central extraction remaining safely unavailable |
| DEP-19 | Post-restore purge-reconciliation test passes |
| DEP-20 | Revoked-session/expired-credential non-resurrection test passes |
| DEP-21 | Existing and extended cross-tenant isolation suite passes |
| DEP-22 | Full authoritative repository test gate passes |
| DEP-23 | Each individual startup/configuration-failure test passes |
| DEP-24 | Manual Product Owner and Security/Privacy sign-off names the exact production resource versions/configuration |

If one person holds both approval roles, DEP-24 explicitly records both roles.
The record also names the break-glass reviewer. No unresolved Class A question,
unverified required capability combination, missing dependency or failed mandatory
evidence item permits removal of the deployment refusal.

## Official Microsoft sources and capability verification

All sources below were retrieved on **2026-09-26**. These are official Microsoft
Learn documentation, not Q&A answers, third-party advice or unverified recollection.
The claim column records the limited capability verified. Normative TaxAI policy
comes from the human decisions, not Microsoft's samples or defaults.

| ID | Official source | Verified capability / limitation |
|---|---|---|
| M01 | [Azure regions][m01] | Canada Central has zones and pairs with Canada East; Canada East has no zones |
| M02 | [Configure App Service zone redundancy][m02] | P1v3 example with zone redundancy and at least two workers; actual plan capability must be checked |
| M03 | [App Service reliability][m03] | Premium v3 zone support requires a suitable region/scale unit; worker count alone is not full application availability proof |
| M04 | [Application Gateway TLS][m04] | Gateway decrypts requests; HTTPS re-encryption to validated backends is supported |
| M05 | [App Service private endpoints][m05] | Premium v3 inbound private endpoint; public access must separately be disabled; outbound integration is distinct |
| M06 | [App Service/Application Gateway integration][m06] | Private App Service backend and DNS/SCM considerations |
| M07 | [Application Gateway WebSockets][m07] | WebSocket forwarding capability; integration still needs actual Blazor verification |
| M08 | [Storage redundancy][m08] | GPv2 Blob supports GZRS; primary zonal copies are synchronous, paired-region replication asynchronous |
| M09 | [Storage private endpoints][m09] | Private endpoints/DNS and failover connectivity; private endpoints do not automatically disable public access |
| M10 | [Blob client-side encryption][m10] | Envelope encryption and Key Vault wrapping are supported |
| M11 | [Storage encryption at rest][m11] | Service-side CMK support and warning against client-side encryption v1 |
| M12 | [Blob soft delete][m12] | Soft-delete retention is an additional recoverable-data lifetime |
| M13 | [Delete Blob REST API][m13] | Permanent-delete support is limited to eligible soft-deleted snapshots/versions, not every retained blob form |
| M14 | [Storage customer-managed keys][m14] | Managed identity, wrap/unwrap/get permissions, Key Vault soft delete/purge protection prerequisites |
| M15 | [SQL customer-managed TDE][m15] | Azure SQL Database CMK, key-access/recovery requirements; preview symmetric-key features are not approved |
| M16 | [Key Vault soft delete][m16] | Purge protection prevents immediate purge during its configured recovery period |
| M17 | [Key Vault RBAC][m17] | Separate data-plane role authorization is supported |
| M18 | [Key Vault rotation][m18] | Key rotation policies exist; application/service consumers must preserve required versions |
| M19 | [ASP.NET Core Data Protection configuration][m19] | Blob key repository, Key Vault encryption, managed identity and retained wrapping versions |
| M20 | [DI data/privacy/security][m20] | Same-region encrypted temporary input/results; automatic deletion after completion and explicit deletion described |
| M21 | [DI encryption at rest][m21] | CMK scope explicitly includes analysis responses; same-region/tenant Key Vault and identity prerequisites |
| M22 | [SQL Private Link][m22] | SQL private connectivity and public-network control |
| M23 | [Key Vault network security][m23] | Private/network controls and service-access considerations |
| M24 | [DI secure managed/private access][m24] | Private endpoints and disabled public access; Studio/training allowances in examples are not adopted |
| M25 | [App Service routing][m25] | Application and configuration traffic routing are separately configurable |
| M26 | [App Service outbound controls][m26] | NSG/firewall/route controls can restrict outbound traffic; the platform default is not deny-all |
| M27 | [DI overview/version support][m27] | v4.0 `2024-11-30` is GA; regional availability is linked separately |
| M28 | [DI add-on capabilities][m28] | `prebuilt-layout` supports the approved `keyValuePairs` feature |
| M29 | [DI v4 SDK/authentication][m29] | GA client/API support and Entra token authentication with a custom-subdomain resource endpoint |
| M30 | [Delete Analyze Result REST API][m30] | Approved API version provides DELETE and HTTP 204; API wording is a deletion marker, not a measured erasure-time attestation |
| M31 | [DI service limits][m31] | Technical limits/tier behavior must be checked without widening or truncating application input policy |
| M32 | [Microsoft data-protection commitments][m32] | Points to contractual customer/processor responsibilities; not evidence that this subscription's required assurance review passed |
| M33 | [SQL local/zone redundancy][m33] | GA SQL tiers support zonal resiliency with tier/regional constraints |
| M34 | [SQL failover groups][m34] | Customer-managed/manual geo-failover and secondary configuration requirements |
| M35 | [SQL automated backups][m35] | Non-Basic short-term PITR up to 35 days; backup-chain dependencies and geo-redundancy constraints |
| M36 | [Key Vault reliability][m36] | Paired-region failover is best effort; backup/restore has geography/subscription restrictions |
| M37 | [Workload identity federation][m37] | Federation avoids stored deployment credentials while retaining explicit trust configuration |
| M38 | [Azure-role PIM settings][m38] | Time-limited activation, justification, approval and authentication-context controls |
| M39 | [Application Insights FAQ][m39] | Regional ingestion and configurable raw-data retention; global endpoints do not ensure regional compliance |
| M40 | [Log Analytics retention][m40] | Table-specific defaults/retention and delayed removal after shortening retention |
| M41 | [Azure Monitor security][m41] | Encryption, RBAC and collection controls; default collection is not TaxAI's taxpayer-data allowlist |
| M42 | [WAF sensitive-data protection][m42] | Log scrubbing exists; not blanket proof that all platform diagnostics are safe |

**Evidence limitations, not invented guarantees:**

- M27 directs regional availability to Microsoft's [product availability table](https://azure.microsoft.com/en-us/explore/global-infrastructure/products-by-region/table). The exact Canada Central resource/API/model/private-network/CMK combination must be proven by DEP-04 and associated checks. Public documentation of the component capabilities is not a claim that a subscription quota or deployed combination was verified here. No alternate region/model is selected if that proof fails.
- The current DI privacy page (M20) describes a 24-hour period after completion, while the [DI FAQ](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/faq?view=doc-intel-4.0.0), also retrieved on 2026-09-26, describes 24 hours from submission. This PRD does not promise the shorter interval or resolve Microsoft's differing wording as contractual advice. Authoritative Microsoft documentation and/or contractual evidence must establish the applicable completion/failure retention behavior and deletion clock as compatible before real-taxpayer processing. The human explicitly classifies this as a deployment blocker, not a design or `/plan` blocker.
- M21 does not explicitly establish CMK protection of every temporarily stored input byte. The strict human key policy remains binding. DEP-05 must establish compatible input protection for the actual service through authoritative Microsoft documentation and/or contractual evidence. Until then production remains blocked. The human explicitly classifies this as a deployment blocker, not a design or `/plan` blocker; fail-closed architecture can proceed. No assumption or exception replaces either assurance proof, and the approved temporary-storage exception is not expanded into a service-managed-key exception.
- No cited public document alone establishes an absolute no-human-access or no-secondary-use contract for the actual subscription. Those are mandatory approval subjects in FR-08, not assumed service capabilities. Missing assurance cannot be treated as passed evidence.
- End-to-end RTO, replica purge completion, runtime egress denial and taxpayer-safe diagnostics are properties to prove in the actual topology, not automatic Azure guarantees. No physical-erasure latency or regional failover SLA is invented.

## Open questions

### Blocking Class A questions

None. The human explicitly resolved Q-A1 and Q-A2 below. No unresolved policy
decision prevents design or `/plan`; this is not production authorization.

### Approved decision record

**Q-A1 — Resolved: gateway plaintext location.** The human explicitly authorizes
only technically necessary transient in-memory plaintext TLS termination/WAF
processing inside the Canadian Application Gateway WAF v2. The gateway
re-encrypts via HTTPS/TLS to the private App Service backend. No intentional
plaintext persistence, export, logging, retention, taxpayer-payload diagnostics,
packet/body capture, custom document/form logs, payload support bundles or HTTP
forwarding is authorized. FR-02, FR-06, FR-13 and the threat model preserve this
narrow infrastructure exception without relaxing any browser/source or
structured-data prohibition.

**Q-A2 — Resolved: four-hour regional recovery scope.** The human explicitly
applies the regional RTO to recoverable application service, not every external
capability. Recovery may provide healthy authenticated/authorized non-extraction
functions while clearly reporting extraction unavailable. New DI processing
fails closed until the specifically approved Canada Central provider boundary
is valid again. There is no four-hour extraction RTO, alternate region/provider,
Development/test fallback or security bypass authorized by recovery. A future
policy change requires a separately approved specification.

### Deployment-only assurance gates

Provider input-CMK coverage and the inconsistent exact Microsoft provider
retention/deletion clock are unresolved **deployment evidence**, explicitly not
design/plan blockers. Architecture may be designed to fail closed. Both block
first real-taxpayer processing until authoritative Microsoft documentation and/or
contractual evidence proves compatibility with the approved policy. Neither an
assumption nor an exception can replace that evidence. DEP-05, AC-168 and AC-169
retain these gates; the existing production-deployment refusal stays active
while either is unresolved. All other mandatory deployment evidence is also
still required, not presumed complete by PRD readiness.

### Class B decisions for one-pass review

- [ASSUMED] Name the single new non-DI adapter project `TaxAI.AzureInfrastructure`, at `src/TaxAI.AzureInfrastructure/`, with its matching tests at `tests/TaxAI.AzureInfrastructure.Tests/`.
- [ASSUMED] Organize this PRD by preserved boundary, functional requirement, individual acceptance check and deployment-evidence ID; these are traceability labels, not implementation TaskIDs.
- [ASSUMED] Deliver evidence/contracts, then infrastructure/adapters, then integration/lifecycle/recovery verification, then guarded release. `/plan` will detail ownership and waves only after human review and resolution of blocking questions.

No Class A assumption has been made about residency, disclosure, keys, network,
purge, access, retention or recovery. Unnamed SKUs/capacity/finite retry values
must be justified within these approved constraints during design and included
in exact-configuration approval; a value that changes a security boundary must
return to the human rather than be tagged Class B.

## CRA sources

No external tax sources required; this feature introduces no tax facts.

## Definition of Done

### PRD readiness checklist

- [x] Only `specs/016-canadian-azure-production-infrastructure/prd.md` is produced by this `/spec` invocation.
- [x] The document opens with feature classification and states `Tax/CRA semantic impact: none` and tax-year scope `Not applicable`.
- [x] Problem, user stories, current-state/reuse analysis, preserved behavior, requirements, out of scope, ACs, open questions and CRA-source section are present.
- [x] All sixteen supplied human decisions are represented without silently replacing them.
- [x] Each AC/DEP row defines one mechanically checkable outcome; individual fail-closed cases are named.
- [x] Source/structured retention, recovery quarantine, seven-year compatibility and all 24 deployment-evidence requirements are explicit.
- [x] Azure capability statements are linked to current official Microsoft documentation; capability/contract/deployed-state limitations are disclosed rather than asserted as proven.
- [x] Class B naming/structure/ordering assumptions are enumerated; no Class A exception is assumed.
- [x] Q-A1 and Q-A2 are explicitly resolved by the human and incorporated consistently throughout requirements, threat model and ACs.
- [x] The PRD is unblocked and ready for human review before `/plan`; unresolved input-CMK and retention/deletion-clock assurance remains deployment-only evidence.

**Current result: ready for /plan.** No blocking Class A question remains. Human
review precedes the separately requested `/plan` workflow; it has not been run.
PRD readiness grants no implementation, provisioning or real-taxpayer deployment
permission and does not satisfy any future deployment evidence item.

### Future feature/deployment completion (not satisfied by this document)

Completion requires all applicable ACs, the full authoritative repository gate,
and DEP-01 through DEP-24 to pass against the exact release configuration; every
required dependency and processor assurance, including input-CMK compatibility
and the exact retention/deletion clock, must be verified; both named human
roles must approve first real-taxpayer use. Only then may the conditional
production validation replace the previous unconditional deployment refusal.

[m01]: https://learn.microsoft.com/en-us/azure/reliability/regions-list
[m02]: https://learn.microsoft.com/en-us/azure/app-service/configure-zone-redundancy
[m03]: https://learn.microsoft.com/en-gb/azure/reliability/reliability-app-service
[m04]: https://learn.microsoft.com/en-us/azure/application-gateway/ssl-overview
[m05]: https://learn.microsoft.com/en-us/azure/app-service/networking/private-endpoint
[m06]: https://learn.microsoft.com/en-us/azure/app-service/networking/app-gateway-with-service-endpoints
[m07]: https://learn.microsoft.com/en-us/azure/application-gateway/application-gateway-websocket
[m08]: https://learn.microsoft.com/en-us/azure/storage/common/storage-redundancy
[m09]: https://learn.microsoft.com/en-us/azure/storage/common/storage-private-endpoints
[m10]: https://learn.microsoft.com/en-us/azure/storage/blobs/client-side-encryption
[m11]: https://learn.microsoft.com/en-us/azure/storage/common/storage-service-encryption
[m12]: https://learn.microsoft.com/en-us/azure/storage/blobs/soft-delete-blob-overview
[m13]: https://learn.microsoft.com/en-us/rest/api/storageservices/delete-blob
[m14]: https://learn.microsoft.com/en-us/azure/storage/common/customer-managed-keys-overview
[m15]: https://learn.microsoft.com/en-us/azure/azure-sql/database/transparent-data-encryption-byok-overview?view=azuresql
[m16]: https://learn.microsoft.com/en-us/azure/key-vault/general/soft-delete-overview
[m17]: https://learn.microsoft.com/en-us/azure/key-vault/general/rbac-guide
[m18]: https://learn.microsoft.com/en-us/azure/key-vault/keys/how-to-configure-key-rotation
[m19]: https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0
[m20]: https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/document-intelligence/data-privacy-security
[m21]: https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/authentication/encrypt-data-at-rest?view=doc-intel-4.0.0
[m22]: https://learn.microsoft.com/en-us/azure/azure-sql/database/private-endpoint-overview?view=azuresql-db
[m23]: https://learn.microsoft.com/en-us/azure/key-vault/general/network-security
[m24]: https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/authentication/managed-identities-secured-access?view=doc-intel-4.0.0
[m25]: https://learn.microsoft.com/en-us/azure/app-service/configure-vnet-integration-routing
[m26]: https://learn.microsoft.com/en-us/azure/app-service/network-secure-outbound-traffic-azure-firewall
[m27]: https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/overview?view=doc-intel-4.0.0
[m28]: https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/concept/add-on-capabilities?view=doc-intel-4.0.0
[m29]: https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/versioning/sdk-overview-v4-0?view=doc-intel-4.0.0
[m30]: https://learn.microsoft.com/en-us/rest/api/aiservices/document-models/delete-analyze-result?view=rest-aiservices-v4.0+%282024-11-30%29
[m31]: https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/service-limits?view=doc-intel-4.0.0
[m32]: https://learn.microsoft.com/en-us/compliance/regulatory/gdpr
[m33]: https://learn.microsoft.com/en-us/azure/azure-sql/database/high-availability-sla-local-zone-redundancy?view=azuresql-db
[m34]: https://learn.microsoft.com/en-us/azure/azure-sql/database/auto-failover-group-sql-db?view=azuresql-db
[m35]: https://learn.microsoft.com/en-us/azure/azure-sql/database/automated-backups-overview?view=azuresql-db
[m36]: https://learn.microsoft.com/en-us/azure/reliability/reliability-key-vault
[m37]: https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation
[m38]: https://learn.microsoft.com/en-us/entra/id-governance/privileged-identity-management/pim-resource-roles-configure-role-settings
[m39]: https://learn.microsoft.com/en-us/azure/azure-monitor/app/application-insights-faq
[m40]: https://learn.microsoft.com/en-us/azure/azure-monitor/logs/data-retention-configure
[m41]: https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/best-practices-security
[m42]: https://learn.microsoft.com/en-us/azure/web-application-firewall/ag/waf-sensitive-data-protection-configure
