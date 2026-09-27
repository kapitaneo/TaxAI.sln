# Architecture: Canadian Azure Production Infrastructure

## Status and authority

Feature class: **platform**. Tax/CRA semantic impact: none.

Design for human review, based on the approved [PRD](prd.md), including resolved
Q-A1/Q-A2. This document does not amend the PRD, implement code, provision Azure,
authorize an implementation TaskID, or authorize real-taxpayer processing.
Provider input-CMK coverage and the exact provider retention/deletion clock
remain **deployment blockers, not planning blockers**. DEP-05, AC-168 and AC-169
must not become configuration defaults or assumed exceptions.

The companion [tasks](tasks.md) contains ten substantial tasks. Each task requires separate
human selection, an isolated branch/PR, its applicable tests and the authoritative
solution gate before its own completion marker. No Spec 015 task is reopened.

## Current structure

Inspected baseline: `ef5899464ef9733edbf1c4a999b28b214753372b`. The PRD is a
local, uncommitted document; preserve it unchanged during planning. Specs 015
T01–T07 are complete. Existing instruction passages describing earlier waves
are historical, not evidence that completed functionality is absent.

- .NET 10, SDK `10.0.302`, `TaxAI.sln`, eleven registered test projects.
- Portal is the sole `Microsoft.NET.Sdk.Web` production project and composition
  root. `ModuleRegistration`, `PortalStartupGuards`, `IntakeConfiguration` and
  `ExtractionConfiguration` currently admit the Development extraction path only.
- `ClientIntake` owns the write-only `IIntakeContentStore`, credential port,
  intake storage contract and server-derived client workflow.
- `ExtractionWorkflow` owns `IExtractionContentReader`, its disposable lease,
  three-method `IDocumentIntelligenceProvider`, ingest/review workflows and
  `IExtractionStore`. Classification and extraction are separate provider calls.
  Correction and finalization can re-read sources; there is no durable raw payload.
- `DocumentIntelligence` implements the deterministic in-process provider;
  it references only `ExtractionWorkflow` and DI abstractions for registration.
- `Persistence` owns every EF entity/configuration/migration and SQL adapter.
  Its actual references are `ClientIntake`, `ExtractionWorkflow`, `TaxCases`,
  `Tenancy`. The old local instruction reference list must be reconciled narrowly.
- Portal's actual references are `CaseProcessing`, `ClientIntake`,
  `DocumentIntelligence`, `ExtractionWorkflow`, `Persistence`, `TaxLogicEngine`,
  `Tenancy`. It has no direct Azure or EF package.
- `Tenancy.RetentionPolicy` is the sole seven-year retention authority. Existing
  review purge is firm-scoped; it is not a source or complete-case purge service.
- Authentication/session revocation is durable in the application database;
  restoring that database alone cannot prove later revocations remain effective.
- No existing Azure IaC or `.github/workflows` implementation was found.
  No cloud resource inventory or live-capability evidence was inspected.

## Target structure

Retain one application; add one **real** non-DI adapter library and its tests.
No separate worker host, function app, extraction microservice, or tax module.

```text
Client
  -- HTTPS/TLS --> Canada Central Application Gateway WAF v2
                   [only unavoidable transient TLS/WAF plaintext in memory]
  <-- HTTPS/TLS --> private TaxAI.Portal / App Service P1v3, >=2 zonal workers
                       |-- existing inward workflow/domain contracts
                       |-- Persistence --> private Azure SQL Database
                       |-- TaxAI.AzureInfrastructure --> private Blob / Key Vault
                       `-- DocumentIntelligence --> private Canada Central DI only

Canada East: passive Canadian replicas/recovery resources; manual activation
             of recoverable application service, NEVER alternate DI processing
```

No plaintext gateway-to-backend leg. Gateway body capture, payload logging,
packet capture, payload-bearing WAF diagnostics and support bundles are forbidden.
The runtime returns only existing approved browser projections. Private networking
does not make raw provider objects, exception messages or default SDK logs safe.

### Azure resource design

Use reproducible Bicep modules, environment parameter contracts and a private
deployment runner. Bicep and directory naming are [ASSUMED] tooling choices, not
new service/privacy approvals. All service combinations remain deployment-proven.

| Concern | Design within approved policy |
|---|---|
| Ingress/compute | Canada Central WAF v2, public 443 only, validated backend HTTPS, private App Service and SCM, P1v3 zone redundancy with minimum two application workers; no public origin or global TLS terminator |
| Egress | Dedicated App Service integration subnet, separate private-endpoint subnet and gateway subnet; route application AND configuration traffic through reviewed route tables/NSGs and a regional Azure Firewall deny-default policy; no TLS interception or payload logging at firewall |
| Private dependencies | Resource-specific private DNS/endpoint bindings for Blob, SQL, Key Vault and DI; public access disabled; DNS and routing proven from every worker; no arbitrary configurable destination |
| Source bytes | GPv2 GZRS Canadian source account, CMK plus application envelope encryption, upload-once; source snapshots/versioning/soft delete/WORM disabled |
| Control and key-ring storage | Separate private accounts/containers from source content, Canadian encryption and identities; controls cannot inherit the source's short retention/deletion policy |
| SQL | Existing Azure SQL Database family, CMK, validated encrypted transport, Canada Central zone-capable tier and Canada East asynchronous secondary with customer-controlled/manual failover group; not Managed Instance or another provider |
| Key Vault | Canadian RBAC, managed identity, soft delete/purge protection, active-key 90-day target, retained required historical versions, Canadian encrypted backup/recovery artifacts |
| Provider | Specifically inventoried Canada Central DI resource; GA `2024-11-30`, `prebuilt-layout`, `keyValuePairs`; no alternate processor/region/model |
| Monitoring | Canada Central Application Insights/Log Analytics, explicit table retention, allowlisted structural events only; unsafe automatic collectors disabled |
| Recovery | Passive Canada East resources, no normal serving; manually validated recovery profile, no regional DI resource; application RTO <=4h does not include unavailable extraction |

The SQL tier/capacity, firewall capacity and finite retry/deadline values are
explicit release parameters, not silent platform defaults. The owning resource slices must verify
that the selected GA Canadian combination meets the approved bounds; unsupported
capacity cannot cause a region, encryption, network or service substitution.
Use a zone-capable Azure SQL Database tier, not geo-restore alone, to support the
stated zone and regional targets. No numeric service SLA is invented here.

The firewall realizes the PRD's already required deny-default boundary. Its
rules admit only reviewed identity/platform/certificate dependencies and private
services. No wildcard arbitrary HTTPS access, public dependency fallback or
payload to a token/monitoring endpoint is permitted. Private service traffic may
use explicit private routes; every competing route must be audited for bypass.

## Module boundaries

| Owner | New responsibility | Must not acquire |
|---|---|---|
| ClientIntake | Source lifecycle value contracts, policy and scoped persistence/purge ports; source-operation fences; intake terminal/activity facts | Azure, EF, HTTP, system clock, a second tax-case aggregate, anonymous content read |
| ExtractionWorkflow | Availability port; lifecycle fact interpretation using existing extraction records; scoped operation fencing around existing workflows | SDKs, tax parsing/mapping rules, persisted SIN, invented accountant decisions |
| Tenancy | Structural recovery/control-evidence contracts and exact-configuration admission vocabulary | Cloud, filesystem, SQL, environment reads, tax policy |
| Persistence | Additive lifecycle/control metadata; atomic leases/fences and owner-managed terminal SQL purge/recovery transactions | Source bytes/provider JSON, cloud SDKs, user-reachable arbitrary deletion |
| DocumentIntelligence | GA DI adapter, layout interpretation and per-call analyze/read/delete state machine | Another provider, tax authority, content store, durable provider identifier, EF, Portal reference |
| TaxAI.AzureInfrastructure | Blob content/reader, envelope-key access, protected Data Protection, structural ledger replication and secure dependency probes | DI SDK, EF, endpoint/component/host, tax logic |
| Portal | Composition, secure configuration, request-scoped admission, safe availability indication, telemetry export, bounded hosted maintenance and recovery orchestration | Cloud/EF SDK ownership, source routes, tax computations |

No new project is created merely as a placeholder. T01 creates the infrastructure
library with working configuration validation and a managed-identity binding
probe, its consumed inward contracts, tests, IaC setting contract and Portal
registration. It is not a project shell.
The narrowly approved Data Protection integration may use its abstractions and
Azure integration packages in that library; ASP.NET hosting/routing stays in Portal.

## Dependency direction

```text
Portal --> current seven references + TaxAI.AzureInfrastructure
TaxAI.AzureInfrastructure --> ClientIntake + ExtractionWorkflow + Tenancy
DocumentIntelligence --> ExtractionWorkflow
Persistence --> ClientIntake + ExtractionWorkflow + TaxCases + Tenancy
ExtractionWorkflow --> existing six inward projects (unchanged)
ClientIntake --> TaxCases + Tenancy + TaxLogicEngine (unchanged)
Tenancy --> CaseReview + TaxLogicEngine (unchanged)
```

All eight protected deterministic modules keep zero cloud SDK references.
Persistence alone has EF Core. No new consumer references Portal. Infrastructure
does not query another adapter's tables: Azure content checks ownership through
the declared scoped source/intake ports, not `TaxAiDbContext`.

SDK families: DI's GA client only in DocumentIntelligence; Blob, Identity,
Key Vault key access and Azure Data Protection packages only in the Azure
infrastructure library. Azure Monitor exporter belongs in that library behind
a safe telemetry adapter port, not in deterministic code. Exact supported GA
package versions are pinned by the owning implementation task after restore and
compatibility verification; no preview/floating dependency is allowed. Existing
.NET/EF/OIDC versions are not upgraded by this spec.

Project references and packages are introduced only with their first working
consumer: T01 consumes Tenancy and Azure Identity for production validation;
T03 adds ClientIntake and consumed key/Data Protection packages;
T04 adds ExtractionWorkflow and the working source adapters;
T05 adds the DI SDK only in DocumentIntelligence; T06/T09 add only dependencies
actually needed by recovery/export. Each owning slice lists its project manifests,
registration, affected guards and test-floor file. No future-only reference,
package installation task or unused adapter interface is predeclared.

## Contract and data design

The human-authorized consolidation supersedes the generic contract-only Wave 0
pattern for this specification. Ten functional slices run in ten sequential
waves numbered 1–10. Contracts are introduced and consumed with their first
working implementation; no NotImplementedException scaffold ships. Existing
public workflow methods and the three provider methods remain source-compatible. New sensitive instance types are sealed, defensively
copied, non-record types with fixed structural `ToString()` and serialization
omission for opaque identifiers, keys and caller-controlled text.

### Source lifecycle contract (ClientIntake)

T04 introduces SourceRegistryContracts/SourceRegistryModels and a real SQL
registry for staging, binding, ownership and terminal replay. T07 extends that
working integration with SourceLifecycleContracts, immutable deadline facts and
consumed active-operation fences. T08 adds due-only destruction against those
working states; it does not introduce another lifecycle schema. No future-only
contract is predeclared.
The complete consumed lifecycle contract family is:
Every data operation takes `TrustedFirmId`, explicit case/resource scope, UTC
instant and cancellation; no firm-omitting overload. Result codes are closed.

| Contract | Required operations / result |
|---|---|
| `ISourceLifecycleStore` | Register staged opaque resource; bind a committed intake document; read scope/status; append immutable lifecycle facts; acquire/renew/release case-scoped operation fence; atomically prepare a due purge; append completion/failure |
| `ISourceOperationScope` | Request-scoped lease/fencing-token carrier used by workflow and store collaborators; never supplies firm authorization; absent production fence refuses relevant writes |
| `ISourcePurgeStore` | Enumerate due source/structured work for an explicit trusted firm, bounded page; read authoritative lifecycle evidence; perform due-only terminal owner-managed SQL cleanup |
| `ISourceObjectPurger` | Delete an authorized opaque object and attest observed primary/replica state; return Pending/Complete/Unavailable, never false success |
| `SourceRetentionPolicy` | Pure UTC calculation from final case acceptance, terminal duplicate/rejection, expiry/activity and unresolved-review facts; existing RetentionPolicy remains authoritative for structured data |

New metadata is structural: source resource ID distinct from its reader content
key, firm/case/document association, staging instant, wrapped-envelope location,
event reference, deadline, fence generation, lease expiry, purge state and outcome.
Opaque identifiers are not telemetry labels. The source registry uses existing
metadata allowances; it adds no raw text, filename, tax value or provider ID.
The SQL schema separates immutable lifecycle events from mutable coordination
lease/projection rows. Only coordination state may be updated in place.

Staging records exist before a Blob allocation becomes reachable. The existing
intake metadata commit binds the source under the same firm/case and records
commit state atomically. Failure leaves a staged record for compensating cleanup
and the <=24h sweep. Blob lifecycle rules alone are insufficient: uncommitted
upload representations, replica lag and uncertain metadata commits are tracked.

### Recovery/control contract (Tenancy)

`ProductionControlContracts.cs` owns the immediately consumed deployment-evidence
and configuration types: inventory identity/configuration digest, exact artifact
version, assurance subjects, approval roles and readiness result.
`RecoveryControlContracts.cs`/`RecoveryControlModels.cs` add recovery epoch and
independent ledger types when T06 consumes them. A deployment
manifest contains infrastructure identifiers, not taxpayer facts or credentials.
Validation distinguishes `Absent`, `Invalid`, `Unavailable` and approved state
without logging supplied values. Actual authorization is evidence-backed, not a
boolean `EnableProduction` setting.

T01 introduces only configuration/evidence shapes and ports used by its validator
and identity probe. T06 introduces the evidence-reader and recovery/control ports
with the working ledger and reconciler. T09 introduces SafeTelemetryContracts
and SafeTelemetryModels with the real Portal instruments and Azure exporter;
neither adapter references the other's implementation. No later-only port is
included in T01.

`IProductionEvidenceReader` reads approved exact-configuration evidence;
`IProductionDependencyProbe` checks the named dependency without returning its
payload; `IRecoveryControlStore` appends/replays terminal control facts outside
the restorable application database. Purge ledger records contain only opaque
resource identifiers and timestamps; category is a closed storage namespace.
The richer structural initiated/completed/outcome audit stays in its approved
audit store, not in general telemetry.

The control ledger is append-only, separately permissioned and independently
recoverable in Canada East. A terminal fact is acknowledged recoverable before
destructive progress; a mere asynchronously replicated write is not proof of
that acknowledgement. No terminal purge completion or recovery release is claimed
if the independent ledger cannot be reconciled. Retain it beyond the actual
longest recoverable backup chain; compute the bound from inventory, not a guessed
35-day constant. Operator approval does not waive a missing interval.

## Source content and keys

The new Blob store implements `IIntakeContentStore`; a separate adapter implements
`IExtractionContentReader`. Neither exposes a new source API. Both depend on
existing intake scope plus the lifecycle registry. A content key alone is never
sufficient. Resource names and envelope headers contain no taxpayer values.

Use a fresh per-content DEK, authenticated versioned ciphertext and a versioned
Key Vault wrapping-key reference. Enforce Spec 014 size/concurrency limits before
allocation grows beyond its approved bound. Stream ciphertext to upload-once
storage, never plaintext to disk. Bind authenticated envelope metadata to the
server-owned resource/firm/case; document ownership comes from the committed
registry because its identifier is assigned after staging. Verify the complete
bounded ciphertext and scope before releasing an in-memory source lease; no
partial plaintext is handed to DI after a later integrity failure.

T04 must use a reviewed supported envelope implementation and known-answer,
tamper, truncation, reordered-chunk, key-isolation and scope-substitution tests.
No novel cryptographic primitive. Source-content format versioning is an internal
encrypted-container format, not a change to tax/medical representation. Disposal
releases buffers; no promise of perfect managed-runtime forensic zeroization.

Source encryption, Blob CMK, SQL TDE CMK, Data Protection wrapping and intake
credential/fingerprint key material are distinct purposes/identities. Production
credential material is retrieved from Key Vault by managed identity, not stored
as plaintext application settings. Preserve the existing credential algorithms
and purpose separation. Version rotation must keep unexpired digests/credentials
verifiable without trying arbitrary keys; it cannot silently log out users or
resurrect expired credentials. Emergency revocation remains privileged.

Wrapped-DEK metadata is separate from the upload-once source object so wrapping
key rotation need not overwrite taxpayer source bytes. Retirement checks retained
ciphertext, key-ring and recoverable backups. Rotation and Canadian key recovery
must be drilled before old versions are disabled or destroyed.

## Provider execution and interpretation

Keep the port's `ClassifyAsync`, `ExtractT4Async`, `ExtractMedicalAsync` methods.
Each call performs one independent Analyze/read/transform/delete lifecycle.
An ingest can therefore perform classification and field extraction as two
separate service operations, consistent with the existing call structure. There
is no cross-call OCR cache or hidden long-lived lease to reduce provider calls.

1. Validate resource, version, model, availability and source bounds; never accept
   a caller-supplied URL. Use managed identity with the pinned custom subdomain.
2. Submit document bytes once. Disable automatic SDK submission retries and
   redirects; an ambiguous acknowledgement yields no successful extraction.
3. Poll/read under a validated finite deadline and bounded exponential retry.
   Copy as-printed fields directly to existing untrusted in-memory shapes.
4. Dispose the raw result and explicitly delete as soon as no longer needed.
   Known operation ID on failure/cancellation also triggers finite cleanup.
5. Only successful deletion permits result release. Exhausted cleanup returns
   null/unsupported under existing contracts, emits a fixed structural event,
   and creates no acceptance-eligible history. Nothing durable stores the ID.

Reserve a finite cleanup budget within the existing workflow call deadline;
cancellation stops ordinary processing but permits only that bounded cleanup.
The host, provider and lease owner must agree on total deadline accounting, not
start an unlimited detached cleanup task. Crash/unknown-ID paths are unsuccessful
and rely on the approved, still-to-be-assured provider backstop.

`LayoutResultInterpreter` is provider-specific syntactic interpretation, not
another tax parser: explicit document labels/geometry, unique printed field
associations, literal printed box codes and untouched amount/date strings.
Keep duplicate entries as a list. Do not infer missing values, normalize amounts,
pick a likely box or infer classification from filename/intake category. Ambiguous
layouts refuse. Medical projection has only the existing closed fields.
Confidence is copied only if the provider supplies the matching signal; absent
confidence stays absent. No derived confidence or acceptance threshold.

Fixtures are synthetic, generated in memory for payload-bearing cases. Supported
layout recognition has positive/negative fixtures and a conservative unsupported
outcome; it does not assert universal OCR coverage or change tax validation.

## Retention, fencing and purge sequence

Deadlines follow PRD FR-09 exactly: final acceptance +30 days; terminal rejection
or confirmed duplicate +7 days; abandonment +30 days after the later expiry and
activity; failed/staged upload <=24h; unresolved review is not upload-age purged.
Store authoritative event-derived UTC deadlines, never Blob modification time.
Case final acceptance requires the successfully associated immutable review,
not merely a partially committed acceptance row from a failed finalization.

Existing confirmed-duplicate decisions identify the candidate source, never the
retained counterpart. An upload rejection is not an accountant's terminal
rejection. There is currently no general accountant-reject route: do not invent
one. The policy supports a validated terminal rejection fact when one exists;
unavailable/ambiguous evidence must not synthesize that event or delete content.
Abandonment requires expired relevant access, no active protected review and
authoritative last activity; new legitimate activity clears an uncommitted due
projection. A terminal purge cannot be cleared by activity or a later correction.

Existing production workflow methods invoke scoped operation helpers to acquire
a case-scoped fence before source-dependent work and hold it through the final
allowed metadata commit. Preserve the existing public signatures: the workflows
are concrete/sealed, so do not plan inheritance-based decorators. ClientIntake
interface decoration is permissible only where the existing interface suffices.
Production SQL adapters check the same fence at commit. Long network work does
not hold an open SQL transaction: renewable leases use a monotonically increasing
fencing token, and stale holders cannot commit after takeover. All source
consumers, acceptance/finalization paths and relevant intake writes participate;
helpers do not implement a second tax workflow.

Purge claims an exclusive fence, re-evaluates deadline and unresolved work in a
serializable metadata transaction, and appends terminal intent. From that point
new reads/mutations refuse; the coordinator records intent in the independent
recovery ledger before deleting any physical object or structured record. A
ledger-write failure leaves safe pending work, not deletion without replay proof.
It retries only the same due resources, never selects a new tenant by key alone.
After observable deletion and required replica/destruction evidence, append
completion. Unverifiable replica deletion remains pending; no fabricated SLA.

Source purge preserves not-yet-due structured history. At the exclusive seven-year
boundary, Persistence deletes owner-managed extraction/intake/review children in
referentially safe order, retaining structural replay controls independently.
Use existing RetentionPolicy results; do not derive a fresh seven-year policy.
No early deletion API, legal exception or cascade into another firm's data.

The Portal hosts the bounded maintenance scheduler (no second executable).
It receives an explicitly authorized firm work inventory through the existing
operational provisioning/release process, then invokes only scoped ports; no
firm-omitting taxpayer enumeration is introduced. Multi-instance SQL claims make
repeated scheduling safe. A missing work inventory blocks lifecycle acceptance,
not silently excludes a firm. Privileged due-now initiation uses the same
operational channel, not an accountant route.

## Recovery and degraded mode

Recovery begins fenced/quarantined with public taxpayer ingress disabled. Steps:

1. Establish the approved Canadian inventory, authoritative recovery epoch and
   surviving independent control ledger; refuse incomplete or stale control state.
2. Execute controlled SQL/Blob Canadian failover or restore; measure SQL RPO,
   identify missing source objects and make no asynchronous zero-loss claim.
3. Restore required protected key versions/key-ring access with correct identities.
   Microsoft-managed Key Vault failover alone is not an RTO proof.
4. Replay terminal source/structured purge decisions and current due purges before
   any taxpayer read. Reconcile replica state and do not revive a source from backup.
5. Invalidate all pre-recovery authentication sessions, client sessions and intake
   links in the recovered database before release; require fresh authorized
   authentication/link issuance through existing flows. This conservative recovery
   fence prevents any post-backup revocation being undone, without reconstructing
   raw credentials. Revalidate current users/memberships through the existing
   trusted provisioning process; missing current authorization evidence blocks release.
6. Re-run secure configuration/identity/network/telemetry checks; manual activation
   of Canada East serves only dependency-healthy existing capabilities.

The four-hour application RTO includes quarantine/reconciliation, not only resource
creation. Authentication and approved existing case/review reads may recover while
DI stays unavailable. New DI processing fails closed until the **Canada Central**
boundary is valid. No Canada East DI, second provider, Development fallback or
security bypass. Source-dependent operations with lost/purged bytes require new
evidence through existing intake, not restoration of an old evidence identity.
If a closed case's existing rules disallow upload, use the existing permitted
new-case/intake flow; do not reopen a finalized case in this infrastructure spec.

## Admission, evidence and telemetry

Separate structural startup validation, current dependency health, and real-data
release authorization. All three are required for live taxpayer operations.
Primary deployment profile identifies Canada Central; signed recovery profile
identifies Canada East application recovery while retaining Canada Central DI.
This is not setting the primary region to Canada East to evade AC-131.

Typed options have no permissive defaults. The manifest binds subscription,
resource IDs/endpoints/regions, package/artifact/configuration digest and DEP-01
through DEP-24 outcomes/approvals. It carries no secrets. Startup inspects actual
DI registrations and protected key-ring components. A stale/missing/altered
manifest, unresolved assurance or configuration drift refuses real-data startup.
Runtime checks fence affected operations; healthy unrelated functions remain
available under their own authorization and dependency checks. Missing/foreign
resource lookups still return identical empty 404 before an availability response.

Expose temporary extraction unavailability as a fixed safe indication through
the existing extraction surface, never a diagnostic resource ID or new route.
Use a bounded 503 response only after scope checks for unavailable infrastructure;
retain existing not-found/validation/conflict behavior otherwise. No tax fields,
client-side state or content are added to the browser model.

Evidence collection must not create a release-gate cycle: synthetic integration
tests and private inspection/drill tooling exercise candidates before real-data
admission. Local/in-process test hosts are not production authorization. Deployed
checks identify the actual resources, and a synthetic equivalent cannot claim
another resource's evidence. Tests which need a serving candidate use an isolated
synthetic-only non-production subscription and exact build; production checks
verify the actual matching configuration/dependencies and record the distinction.
If a mandatory actual-resource check cannot be run while admission is closed,
DEP evidence remains incomplete; do not invent a production bypass or mark it
passed. T10 requires explicit target authority and both real-data sign-offs.

Disable automatic body/request/dependency/exception collectors that can export
raw URLs or payloads. Emit only approved fields through `TaxpayerSafeTelemetry`;
safe exporter construction, not after-the-fact redaction, is the boundary.
Platform/WAF/firewall/SQL logs are separate proof surfaces. Unsafe categories
remain off. Existing purge logging currently includes a firm ID and must be
narrowed for the production policy; its structural audit is distinct from an
operational log. Capture every enabled sink with positive planted-value controls.

## Migration sequence

The two review-size splits retain the other six functional boundaries.
Key protection precedes source storage so envelope encryption consumes the
working wrapper rather than a temporary production substitute. Recovery precedes
terminal purge so destructive progress has a working independently recoverable
ledger. Retention/state includes real active-operation fencing needed to protect
unresolved work; purge execution later consumes it, not a stub contract.

| Wave | Task | Complete functional boundary |
|---|---|---|
| 1 | T01 | Infrastructure module, identity/configuration validation and refusal |
| 2 | T02 | Hosting, HTTPS/WAF, private network and deny-default egress |
| 3 | T03 | Key Vault, CMK, rotation/revocation and shared protected Data Protection |
| 4 | T04 | Source Blob store/reader, envelopes and consumed source registry |
| 5 | T05 | DI Analyze/read/delete, interpretation and unavailable-mode integration |
| 6 | T06 | Independent ledger, quarantined restore and Canadian recovery |
| 7 | T07 | Working retention deadlines/state and active-operation fencing |
| 8 | T08 | Coordinated physical and structured due-purge execution |
| 9 | T09 | Diagnostics, separated environments and operational access |
| 10 | T10 | Cross-cutting verification and human release evidence only |

Every slice owns its consumed contracts, integration, IaC, tests, caused guard
updates, floor ratchet and local evidence. T10 cannot repair missing earlier
work. Shared manifest/composition/IaC/guard paths make these sequential; no
artificial parallelism or ninth/eleventh setup task is introduced.

T04 owns the minimal source-registry migration. T07 owns the additive lifecycle
projection/lease migration and consumes every field in live projection and
source-operation coordination. T08 reuses that completed model for terminal
intent/cleanup; no speculative schema or second model is required. T06 uses
T04 terminal registry records/existing structured IDs and synthetic replay facts,
not a future retention calculator.

No rollout admits real taxpayer data between steps. Only the dedicated migration
identity applies reviewed migrations; no startup Migrate/EnsureCreated, fixture
seeding or in-place taxpayer-history rewrite.

## Path and project-reference changes

No existing file, namespace, assembly or solution path moves. New library:
`src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj`; matching test
project: `tests/TaxAI.AzureInfrastructure.Tests/TaxAI.AzureInfrastructure.Tests.csproj`.
T01 registers both once in TaxAI.sln and the test-floor manifest, and adds the
Portal reference with its working validator. Initially the new library references
only Tenancy. T03 adds ClientIntake with the consumed runtime credential port;
T04 adds ExtractionWorkflow with the actual source reader. Other existing project-reference sets stay unchanged. The exact new and modified files are owned by tasks.md.

New infrastructure paths are `infra/azure/`, `scripts/azure/`, `docs/operations/`
and `.github/workflows/`. [ASSUMED] These are repository/tooling paths, not new
services. Evidence schemas/templates are tracked; live secrets, connection
strings, encrypted backups and taxpayer-shaped payload artifacts are not.

## Backward-compatibility requirements

Preserve existing tax/parser/mapping/review signatures and behavior, year authority,
source-reader four-part scope, write-only intake port, provider shapes, browser
allowlists, empty 404s, antiforgery, session/membership checks and immutable history.
Existing in-memory fixtures remain available only in Development/test. Refactor
blanket no-egress/package assertions into exact module/adapter boundaries plus a
real deterministic no-egress test; do not delete their positive controls.

Old database migrations are unchanged. New lifecycle tables backfill only
authoritatively attributable metadata; unknown timing or scope quarantines that
resource from automatic deletion, not invents a deadline. No Development source
or key migration into real production is authorized. Tests with historical
TestServer startup substitutions do not become an admission bypass.

## Build and test command changes

Unchanged authoritative commands:

```powershell
dotnet build TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1
```

Add `pwsh ./scripts/azure/Test-ProductionPlan.ps1` for offline IaC/schema/policy
checks and `pwsh ./scripts/azure/Invoke-SyntheticVerification.ps1` for explicitly
authorized synthetic Azure checks. Missing cloud access is a recorded unexecuted
deployment check, never a skipped/passing solution test. Do not put live network
requirements into the ordinary solution gate. Build IaC through the version-pinned
toolchain in the first script; do not assume a locally installed version.

Each task's explicit file list assigns affected instructions, exact-set tests,
registration guards and the test manifest to the slice that changes them. T01
registers the new project; each slice raises its own observed floors without a
decrease. T09 adds CI and the complete reproducible deployment pipeline. T10
assembles proof, not deferred registrations, tests, floors or IaC. The companion
task document records the exhaustive guard scan, every sequentially shared path,
and single-owner mappings for all 169 ACs and all 24 DEP IDs.

## Trade-offs

- **Keep the existing three provider calls** instead of changing every workflow
  to a combined analysis port. This costs extra analyses but avoids a retained
  raw/SIN-bearing cross-call cache and preserves the approved contract. Measure
  availability/cost; never optimize by weakening deletion or acceptance.
- **Private regional App Service/WAF monolith** instead of AKS/functions or a
  second extraction service. This implements the approved topology and keeps one
  composition root; maintenance still needs distributed leases and admission.
- **Separate replay ledger** instead of relying on SQL PITR and Blob lifecycle.
  It adds coordinated failure states but makes terminal deletion replayable after
  rollback. No success is claimed when durable control acknowledgement is missing.
- **Explicit policy-aware purge** instead of source WORM/version retention.
  Short-lived source data and seven-year structured history have different clocks;
  optional storage protection cannot quietly create another retained copy.

## Risks and mitigations

| Risk | Design/verification response |
|---|---|
| Provider input encryption or deletion clock cannot meet policy | DEP-05 remains false; no first taxpayer; no alternate service/assumption |
| Generic layout model cannot reliably identify a field/document | Conservative syntactic recognition, synthetic ambiguous-layout tests, unsupported/null outcome; no guessed tax facts |
| Purge versus long analysis or lost worker | Durable terminal intent, case fence generation, store-side stale-token refusal, finite leases; concurrent tests across two workers |
| Azure region/CMK/private-network combination unavailable | Fail deployment checks; no fallback; exact configuration must be human reviewed |
| Key Vault recovery cannot meet four-hour objective | Preposition only protected Canadian recovery artifacts, drill version access and realistic timing; missing proof keeps release closed |
| Platform logs expose route IDs or WAF matched payload | Disable unsafe categories, allowlist before export, test platform collectors as well as application logs |
| Backup resurrection or incomplete replica purge | Independent replay controls, quarantine, conservative credential invalidation, explicit pending deletion |
| Package/reference assertions freeze the old boundary | Each boundary-changing slice updates its exact guards; preserves domain constraints and positive controls; no test-floor decrease |
| Many infrastructure files obscure source ownership | Exact task file lists, one SQL migration owner per wave, no sibling overlap, reference/coverage validation |
| Evidence generation becomes an operator bypass | Exact-resource/version approvals, no arbitrary production flag, no live secrets in tracked evidence |

## Rollback / recovery approach

First withdraw real-data admission and fence affected operations. Roll back only
to an artifact/configuration compatible with retained schema, key versions and
control history. Never roll back a purge decision, rotate back an expired
credential, restore plaintext files or substitute Development/provider/public
fallbacks. Database restore follows the quarantine sequence above, including
current authorization verification. Additive schema can remain unused by an old
compatible binary; destructive down-migration is not an automatic rollback.

## Tax behaviour unchanged

No tax mapping, calculation, parser behavior, public tax contract, tax-year rule,
medical eligibility rule or review assembler changes. Box labels and values
remain untrusted source data until the existing chain processes them. Every
implementation task runs the complete authoritative suite; the T10 final verification
compares preserved behavior against the Spec 015 baseline. Infrastructure metadata
and lifecycle coordination do not become tax authority.

## Official capability references and limitations

Use the PRD's M01–M42 register for unchanged approved Azure capabilities. The
following official sources were additionally checked during planning on 2026-09-26:

- [App Service outbound control with Azure Firewall](https://learn.microsoft.com/en-us/azure/app-service/network-secure-outbound-traffic-azure-firewall): route-all/route table and deny-policy enforcement; this plan does not adopt sample logging or broad sample destinations.
- [Azure SQL Database failover groups](https://learn.microsoft.com/en-us/azure/azure-sql/database/failover-group-sql-db?view=azuresql-db): asynchronous Canadian secondary and controlled failover; not a guarantee of this application's RPO/RTO.
- [Azure SQL Database zone redundancy](https://learn.microsoft.com/en-us/azure/azure-sql/database/high-availability-sla-local-zone-redundancy?view=azuresql-db): tier/region prerequisites must be verified for the release.
- [Key Vault protected backup](https://learn.microsoft.com/en-us/azure/key-vault/general/backup): protected object backup/recovery constraints; no plaintext key export or automatic four-hour claim.
- [Data Protection key storage](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0): Azure Blob integration; adopt managed identity, not example SAS access.

These are design inputs, not completed DEP evidence. Current source documents,
actual Canadian inventory and the two unresolved provider assurances must be
checked again for the release. No planning step removes a deployment blocker.


## Review-size split

The prior source/key slice is now T03 (working key/CMK/Data Protection boundary)
and T04 (working source storage/envelope boundary). The prior lifecycle/purge
slice is now T07 (policy, live state projection and active-operation fencing)
and T08 (due-only destruction). The other six task scopes are unchanged except
for consistent numbering and dependency references.

Ten tasks remain substantial and independently testable. T07 is the largest at
65 paths, T04 has 64; many paths are caused existing-guard updates, not new
subsystems. No task exceeds 65 paths and none became resource-level setup.
T10 remains smaller because it owns only genuinely cross-cutting verification,
not deferred implementation. Complete counts and every shared path are in tasks.md.

Implementation completion never implies deployment approval. All slice probes
and local tests must be complete in their owner; externally observed evidence
may remain explicitly pending, never labelled a DEP pass. T10 can run and
assemble already supplied probes, not fill implementation gaps. Both provider
assurances independently keep deployment REFUSED until authoritative evidence
and approvals resolve them. No cloud operation is performed by this plan.
