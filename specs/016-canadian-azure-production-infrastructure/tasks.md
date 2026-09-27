# Tasks: Canadian Azure Production Infrastructure

## Scope and approval

Feature class: **platform**. This review-size revision splits only the previous
source/key task and retention/purge task into two meaningful slices each.
There are **ten tasks in ten sequential waves**. The other six scopes are
preserved; IDs and references are consistently renumbered.
The [PRD](prd.md) remains unchanged and authoritative. The
[architecture](architecture.md) preserves all security boundaries.
No task is selected, implemented or deployed.

The explicit consolidation request overrides the generic contract-only Wave 0
pattern for this spec. T01 ships real configuration/identity validation, not a
shell. Contracts, SDKs and references appear only with their working consumer.
There is no setup-only, resource-only, gate-only or later-correction task.

- A human explicitly selects one TaskID per invocation. No automatic next task,
  batching, self-approval or merge.
- Every earlier wave's completion markers and human merge are prerequisites.
- Only exact listed files may change. No directory/glob ownership. A necessary
  unlisted file requires an explicit plan amendment, not a T10 repair.
- This plan is read-only after human approval. Completion uses the task's own
  status marker, with PR/commit and verification evidence; never checkbox edits.
- Every task runs its tests, `dotnet build TaxAI.sln`, and
  `pwsh ./scripts/Invoke-SolutionTests.ps1`. The raw solution command alone
  cannot prove all assemblies ran. No skipped tests or reduced floors.
- Every slice owns its consumed contracts, application integration, relevant
  IaC, tests, caused guard updates, observed floor increases and local DEP evidence.
  Existing listed guards may change only as caused by that slice; future
  exceptions are not pre-approved.
- Executable cloud probes and dry-run tests ship in their slice. Normal local
  mode is simulated/offline and cannot claim a live DEP pass. Live execution
  requires explicit target/subscription and change authority; missing access is
  recorded unexecuted, not skipped/passed.
- Code/IaC completion is not deployment-gate completion. T01–T09 finish all
  implementation and local proof, including complete executable probes, but may
  record pending externally observed evidence. T10 executes/assembles those
  probes; it may not supply missing implementation, tests, IaC, registrations,
  guard fixes, floors or gate logic from an earlier slice.
- Both provider assurance gaps independently keep production **REFUSED** even
  when all code/IaC tasks are complete. No affirmative example evidence.
  T10 has no done marker while either assurance or mandatory evidence is absent.
- No tax/CRA logic, SIN allowance, source-serving route, tenant boundary, browser
  field, old migration byte or Spec 015 task changes. Synthetic data only.
  Production activation remains a separately explicit human release action.

## Waves, dependencies and file counts

Keys precede source storage so envelopes consume a working wrapper.
Recovery precedes terminal purge so ledger acknowledgement is real.
Retention/state includes consumed active-operation fencing to protect unresolved
correction; execution consumes that completed state, not contract scaffolding.

| Previous task | Revised task(s) |
|---|---|
| T01 configuration | T01 unchanged scope |
| T02 hosting/network | T02 unchanged scope |
| T03 source/key protection | T03 key protection; T04 source/envelopes |
| T04 Document Intelligence | T05 unchanged scope |
| T06 recovery | T06 unchanged scope |
| T05 lifecycle/purge | T07 policy/state; T08 execution |
| T07 operations | T09 unchanged scope |
| T08 final verification | T10 unchanged scope |

Counts include all owned paths, including eventual evidence/marker files.
Application = production C#/project; IaC/tooling = infra, Azure scripts, CI;
tests/guards = tests tree; other = instructions/docs/manifest/marker.
Dependencies list the preceding integration wave; every earlier wave must merge.

| Wave | Task | Depends on | Application | IaC/tooling | Tests/guards | Other | Total |
|---|---|---|---:|---:|---:|---:|---:|
| 1 | T01 | none | 16 | 3 | 23 | 14 | 56 |
| 2 | T02 | T01 | 3 | 15 | 9 | 3 | 30 |
| 3 | T03 | T02 | 16 | 6 | 19 | 7 | 48 |
| 4 | T04 | T03 | 24 | 4 | 29 | 7 | 64 |
| 5 | T05 | T04 | 16 | 3 | 23 | 7 | 49 |
| 6 | T06 | T05 | 15 | 7 | 22 | 7 | 51 |
| 7 | T07 | T06 | 26 | 2 | 29 | 8 | 65 |
| 8 | T08 | T07 | 20 | 3 | 29 | 8 | 60 |
| 9 | T09 | T08 | 13 | 14 | 20 | 12 | 59 |
| 10 | T10 | T09 | 0 | 1 | 3 | 9 | 13 |

No pair is conflict-free: all tasks own their floor updates; runtime/resource
slices share real composition, IaC and guard files. Sequential shared paths and
individual reasons are listed below. Do not manufacture parallelism.

## Wave 1

- [ ] **T01** — Azure infrastructure boundary and production configuration — files: exact list below — depends on: none

**Working slice:** Deliver working production-profile validation and managed-identity binding verification in the new real Azure infrastructure library, called by Portal startup. Introduce only the Tenancy configuration/evidence shapes consumed here and a real Azure Identity-backed binding probe; no source/DI/recovery/telemetry ports or unused project references. Validate explicit Production, Canada Central, approved endpoint/resource evidence, GA API/model, dependency settings, managed identities, encryption, private-only posture, retention/deadline bounds, registered implementations and exact-version release evidence. Missing later adapters still cause refusal, not a placeholder implementation or a permissive switch. The configuration validator itself has valid/invalid tests; a deployable host cannot yet pass the complete gate. Create the actual runtime-setting IaC/schema and executable configuration check in this slice. Register project/tests, actual references/packages, preserve the single host/EF owner, and update every directly affected guard and documentation now. Threat model begins with the actual admission boundary and approved gateway/provider exceptions. All 24 evidence subjects are validated without affirmative defaults; both provider assurances remain independently required.

**Verification in this task:** Parameterize every startup/configuration refusal, both assurance gaps independently, missing concrete adapters, invalid/expired configuration evidence, wrong identity/environment/region/public endpoint/local source, and exact release binding. Test real validator/probe with controlled transports and capture safe errors. Verify Development and historical TestServer behavior without making it a production bypass.

**Single-owner ACs:** AC-003–008, AC-017, AC-130–152, AC-168–169.

**Single-owner DEP gates:** DEP-23.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T01.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (56 paths):**

- `CLAUDE.md`
- `NEXT.md`
- `README.md`
- `TaxAI.sln`
- `infra/azure/contracts/runtime-configuration.schema.json`
- `infra/azure/modules/runtime-configuration.bicep`
- `scripts/azure/Test-ProductionConfiguration.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/contracts/production-evidence.schema.json`
- `specs/016-canadian-azure-production-infrastructure/contracts/runtime-profile.schema.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T01.md`
- `specs/016-canadian-azure-production-infrastructure/status/T01.done` — only after this task's required gate
- `specs/016-canadian-azure-production-infrastructure/threat-model.md`
- `src/TaxAI.AzureInfrastructure/AGENTS.md`
- `src/TaxAI.AzureInfrastructure/AzureInfrastructureRegistration.cs`
- `src/TaxAI.AzureInfrastructure/CLAUDE.md`
- `src/TaxAI.AzureInfrastructure/Configuration/AzureInfrastructureOptions.cs`
- `src/TaxAI.AzureInfrastructure/Configuration/ManagedIdentityBindingProbe.cs`
- `src/TaxAI.AzureInfrastructure/Configuration/ProductionConfigurationValidator.cs`
- `src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/ExtractionConfiguration.cs`
- `src/TaxAI.Portal/IntakeConfiguration.cs`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `src/TaxAI.Portal/Program.cs`
- `src/TaxAI.Portal/TaxAI.Portal.csproj`
- `src/Tenancy/CLAUDE.md`
- `src/Tenancy/Contracts/ProductionControlContracts.cs`
- `src/Tenancy/Models/ProductionControlModels.cs`
- `src/Tenancy/Services/ProductionEvidencePolicy.cs`
- `tests/DocumentIntelligence.Tests/NoEgressTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Boundaries/AzureInfrastructureBoundaryTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Configuration/ManagedIdentityBindingTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Configuration/ProductionConfigurationTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/TaxAI.AzureInfrastructure.Tests.csproj`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`
- `tests/TaxAI.Portal.Tests/Extraction/ExtractionStartupGuardTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Startup/IntakeConfigurationTests.cs`
- `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/DotNet10RepositoryTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`
- `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`
- `tests/Tenancy.Tests/Contracts/TenancyContractTests.cs`
- `tests/Tenancy.Tests/Production/ProductionControlContractTests.cs`
- `tests/Tenancy.Tests/Production/ProductionEvidencePolicyTests.cs`

## Wave 2

- [ ] **T02** — Production hosting and private network topology — files: exact list below — depends on: T01

**Working slice:** Deliver the complete regional ingress/compute/egress topology in one slice: Canada Central P1v3, zone redundancy/minimum two instances, WAF v2 public 443, validated HTTPS re-encryption, private App Service/SCM, VNet application AND configuration traffic routing, private endpoints/DNS and default-deny approved egress. Include private endpoint bindings for all four approved dependency resource IDs; resource-owning later slices supply concrete IDs through this working module, not reimplement endpoints. A missing binding never makes deployment admissible. Include passive Canada East network foundations, not active traffic or DI failover. Implement trusted proxy/forwarded-header handling in Portal without widening browser or questionnaire behavior. Pin Bicep tooling and ship compile/lint, network policy, synthetic route/DNS/egress probes and positive controls now. No payload-bearing gateway/firewall diagnostic category is enabled.

**Verification in this task:** Compile actual templates; mutate every security parameter to prove denial. Exercise TLS/backend trust/forwarded headers, minimum capacity, origin/SCM denial and existing questionnaire. Ship executable per-worker private DNS and unapproved-byte egress tests with a positive receive detector; no simulation may be reported as deployed proof.

**Single-owner ACs:** AC-018–024, AC-049–058, AC-154.

**Single-owner DEP gates:** DEP-06, DEP-07, DEP-08, DEP-09.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T02.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (30 paths):**

- `infra/azure/bicepconfig.json`
- `infra/azure/contracts/dependency-allowlist.json`
- `infra/azure/contracts/toolchain.json`
- `infra/azure/main.bicep`
- `infra/azure/modules/app-service.bicep`
- `infra/azure/modules/application-gateway.bicep`
- `infra/azure/modules/egress.bicep`
- `infra/azure/modules/network.bicep`
- `infra/azure/modules/private-dns.bicep`
- `infra/azure/modules/private-endpoints.bicep`
- `infra/azure/parameters/production.example.bicepparam`
- `infra/azure/parameters/recovery.example.bicepparam`
- `infra/azure/parameters/synthetic.bicepparam`
- `scripts/azure/Test-PrivateNetwork.ps1`
- `scripts/azure/Test-ProductionPlan.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T02.md`
- `specs/016-canadian-azure-production-infrastructure/status/T02.done` — only after this task's required gate
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/Production/TrustedIngressConfiguration.cs`
- `src/TaxAI.Portal/Program.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/Production/HostTemplateTests.cs`
- `tests/TaxAI.Portal.Tests/Production/NetworkTemplateTests.cs`
- `tests/TaxAI.Portal.Tests/Production/TrustedIngressTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`

## Wave 3

- [ ] **T03** — Key Vault, CMK and Data Protection — files: exact list below — depends on: T02

**Working slice:** Deliver the working Canadian key-protection boundary: Key Vault RBAC/managed identity, soft delete/purge protection, key wrapping/unwrapping, approved CMKs, 90-day target rotation, historical-version usage checks and privileged revocation/emergency-elevation refusal. Wire the shared Blob-backed, Key-Vault-protected ASP.NET Data Protection key ring and purpose-separated runtime credential material into Portal. Own a distinct key-ring storage module; source Blob resources belong to T04. Establish the secure SQL/managed-identity/TDE-CMK resource and transport configuration; T06 later extends recovery. The real wrapping adapter is consumed now by a non-sensitive wrap/unwrap capability probe and rotation checks, not an unused future interface. T04 later consumes it for source envelopes and binds source Blob SSE to the approved CMK. Add only the references/packages used here (ClientIntake for the existing credential port, not ExtractionWorkflow yet). Key usage checks accept actual inventoried retained ciphertext/key-ring/backup evidence and refuse retirement if that evidence is incomplete. Ship key/SQL/key-ring IaC, real adapters, Portal wiring, all caused guards, unit/integration tests and local DEP evidence together. No source store or placeholder source adapter is created.

**Verification in this task:** Controlled transports and actual crypto operations prove wrap/unwrap, wrong identity/key/version refusal, encrypted SQL/MI configuration and non-secret errors. Test shared protected sessions across workers, valid historical key material after rotation, refusal to destroy a still-needed version, missing usage evidence, emergency revocation authorization/reason/time bounds and absence of plaintext key persistence. Deliver the executable rotation drill and protected-key recovery checks now; later source integration adds source-specific fixtures without reopening key ownership.

**Single-owner ACs:** AC-041–048.

**Single-owner DEP gates:** DEP-14.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T03.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (48 paths):**

- `docs/operations/key-rotation.md`
- `infra/azure/contracts/data-configuration.schema.json`
- `infra/azure/main.bicep`
- `infra/azure/modules/key-ring-storage.bicep`
- `infra/azure/modules/key-vault.bicep`
- `infra/azure/modules/sql.bicep`
- `scripts/azure/Invoke-KeyRotationDrill.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T03.md`
- `specs/016-canadian-azure-production-infrastructure/status/T03.done` — only after this task's required gate
- `src/Persistence/CLAUDE.md`
- `src/Persistence/Services/SqlConnectionStringGuard.cs`
- `src/TaxAI.AzureInfrastructure/AzureInfrastructureRegistration.cs`
- `src/TaxAI.AzureInfrastructure/CLAUDE.md`
- `src/TaxAI.AzureInfrastructure/Configuration/ApprovedResourceProbe.cs`
- `src/TaxAI.AzureInfrastructure/Configuration/AzureInfrastructureOptions.cs`
- `src/TaxAI.AzureInfrastructure/Keys/KeyVaultContentKeyWrapper.cs`
- `src/TaxAI.AzureInfrastructure/Keys/KeyVaultRuntimeMaterialProvider.cs`
- `src/TaxAI.AzureInfrastructure/Keys/KeyVersionUsageGuard.cs`
- `src/TaxAI.AzureInfrastructure/Protection/AzureDataProtectionConfiguration.cs`
- `src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/CryptographicIntakeCredentialSource.cs`
- `src/TaxAI.Portal/IntakeConfiguration.cs`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalIdentity.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `tests/DocumentIntelligence.Tests/NoEgressTests.cs`
- `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`
- `tests/Persistence.Tests/Composition/ConnectionStringGuardTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Boundaries/AzureInfrastructureBoundaryTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Configuration/ApprovedResourceProbeTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Keys/KeyRotationTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Keys/KeyWrappingTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Protection/DataProtectionTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/Production/DataTemplateTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Startup/IntakeConfigurationTests.cs`
- `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`

## Wave 4

- [ ] **T04** — Source storage and envelope encryption — files: exact list below — depends on: T03

**Working slice:** Deliver working upload-once Canadian GPv2/GZRS source storage and the extraction-only four-part scoped reader. Consume the completed T03 wrapping adapter for per-content DEKs and authenticated envelopes; bind source Blob service encryption to its approved Key Vault CMK. Own source private endpoint/access wiring through T02's network module, no public access/SAS/routes, disabled source versioning/soft-delete/snapshots/WORM, bounded memory and no plaintext disk. Add the actually consumed ExtractionWorkflow reference, not another key implementation. Introduce and consume the minimal staged/bound/terminal source registry and additive SQL migration for ownership, orphan tracking and recovery replay. Implement compensating uncommitted cleanup and a scoped physical deletion adapter that refuses unverifiable primary/replica completion; due lifecycle policy is not invented here. Source-specific health/encryption probes, real adapters, IaC, registration, tests and local DEP evidence ship together. T03's key-ring/SQL configuration and credential algorithm remain unchanged.

**Verification in this task:** Known-answer authenticated-envelope, fresh per-content DEK, tamper/truncation/reordered-chunk, foreign firm/case/document/key, unavailable wrapping key, bound/disposed lease and no-plaintext-filesystem tests. Prove upload-once, atomic registry binding, compensating cleanup, source CMK binding, private access, Canadian redundancy and purge-compatible settings. Run the relational migration/old-schema tests and the executable source-protection probe; test source decryption against retained rotated keys using the completed key boundary.

**Single-owner ACs:** AC-025–040.

**Single-owner DEP gates:** DEP-10.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T04.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (64 paths):**

- `infra/azure/contracts/data-configuration.schema.json`
- `infra/azure/main.bicep`
- `infra/azure/modules/storage.bicep`
- `scripts/azure/Test-SourceProtection.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T04.md`
- `specs/016-canadian-azure-production-infrastructure/status/T04.done` — only after this task's required gate
- `src/ClientIntake/CLAUDE.md`
- `src/ClientIntake/Contracts/SourceRegistryContracts.cs`
- `src/ClientIntake/Models/SourceRegistryModels.cs`
- `src/Persistence/CLAUDE.md`
- `src/Persistence/Configurations/SourceRegistryConfiguration.cs`
- `src/Persistence/Entities/SourceRegistryEntities.cs`
- `src/Persistence/Migrations/20260927000000_AddSourceRegistry.Designer.cs`
- `src/Persistence/Migrations/20260927000000_AddSourceRegistry.cs`
- `src/Persistence/Migrations/TaxAiDbContextModelSnapshot.cs`
- `src/Persistence/PersistenceRegistration.cs`
- `src/Persistence/Repositories/SqlIntakeStore.cs`
- `src/Persistence/Repositories/SqlSourceRegistryStore.cs`
- `src/Persistence/TaxAiDbContext.cs`
- `src/TaxAI.AzureInfrastructure/AzureInfrastructureRegistration.cs`
- `src/TaxAI.AzureInfrastructure/CLAUDE.md`
- `src/TaxAI.AzureInfrastructure/Configuration/ApprovedResourceProbe.cs`
- `src/TaxAI.AzureInfrastructure/Configuration/AzureInfrastructureOptions.cs`
- `src/TaxAI.AzureInfrastructure/Content/AzureExtractionContentReader.cs`
- `src/TaxAI.AzureInfrastructure/Content/AzureIntakeContentStore.cs`
- `src/TaxAI.AzureInfrastructure/Content/AzureSourceObjectPurger.cs`
- `src/TaxAI.AzureInfrastructure/Content/SourceEnvelopeCodec.cs`
- `src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/ExtractionConfiguration.cs`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `tests/ClientIntake.Tests/Boundaries/ClientIntakeBoundaryTests.cs`
- `tests/ClientIntake.Tests/ContractTests.cs`
- `tests/ClientIntake.Tests/Lifecycle/SourceRegistryContractTests.cs`
- `tests/ClientIntake.Tests/Models/ContractOmissionTests.cs`
- `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`
- `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`
- `tests/Persistence.Tests/Lifecycle/SourceRegistryMigrationTests.cs`
- `tests/Persistence.Tests/Lifecycle/SourceRegistryTests.cs`
- `tests/Persistence.Tests/Schema/ExtractionMigrationTests.cs`
- `tests/Persistence.Tests/Schema/MigrationTests.cs`
- `tests/Persistence.Tests/Schema/ModelShapeTests.cs`
- `tests/Persistence.Tests/Upgrade/EfCore10CompatibilityTests.cs`
- `tests/Persistence.Tests/Upgrade/EfUpgradeBaselineTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Boundaries/AzureInfrastructureBoundaryTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Configuration/ApprovedResourceProbeTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Content/ContentReaderTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Content/ContentStoreTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Content/EnvelopeCodecTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeSecurityAcceptanceTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`
- `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs`
- `tests/TaxAI.Portal.Tests/Production/DataTemplateTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`

## Wave 5

- [ ] **T05** — Production Document Intelligence — files: exact list below — depends on: T04

**Working slice:** Deliver the Canada Central DI resource and its complete production adapter together. Preserve the existing three provider methods; each uses one Analyze submission, bounded read/poll/transform/delete, approved GA 2024-11-30, prebuilt-layout and useful supported keyValuePairs. Syntactically copy printed fields/duplicate lists into the existing untrusted representation, never tax logic, repairs, guessed classification or confidence. Disable automatic Analyze retry/redirect; enforce overall and cleanup budgets and cancellation. Attempt immediate Delete Analyze Result after transformation and every terminal known-ID failure; bounded deletion exhaustion yields no acceptance-eligible result. No durable raw payload or deletion-retry ID. Wire the real adapter and per-operation availability in Portal with scope-first empty 404 and a fixed safe unavailable response. No alternate processor/region/Development fallback. Deliver provider-specific IaC, unit/HTTP integration tests and synthetic live deletion probes in this task. Provider assurance remains unproven until authoritative evidence and approval; adapter implementation is not an assurance claim.

**Verification in this task:** Fake transports cover Analyze exactly once, ambiguous acknowledgement, redirects, malformed results, absent confidence, timeouts/cancellation, cleanup budget and exhausted deletion. Real workflow integration proves failed deletion cannot yield accepted history. Live probe checks approved destination/API/model and Delete Analyze Result; raw data never becomes an artifact.

**Single-owner ACs:** AC-059–083, AC-158–164, AC-166–167.

**Single-owner DEP gates:** DEP-04, DEP-12, DEP-13.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T05.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (49 paths):**

- `README.md`
- `infra/azure/main.bicep`
- `infra/azure/modules/document-intelligence.bicep`
- `scripts/azure/Test-DocumentIntelligence.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T05.md`
- `specs/016-canadian-azure-production-infrastructure/status/T05.done` — only after this task's required gate
- `src/DocumentIntelligence/CLAUDE.md`
- `src/DocumentIntelligence/DocumentIntelligence.csproj`
- `src/DocumentIntelligence/DocumentIntelligenceRegistration.cs`
- `src/DocumentIntelligence/Production/AnalyzeOperationLifecycle.cs`
- `src/DocumentIntelligence/Production/AzureDocumentIntelligenceOptions.cs`
- `src/DocumentIntelligence/Production/AzureDocumentIntelligenceProvider.cs`
- `src/DocumentIntelligence/Production/DocumentLayoutRecognition.cs`
- `src/DocumentIntelligence/Production/LayoutResultInterpreter.cs`
- `src/ExtractionWorkflow/CLAUDE.md`
- `src/ExtractionWorkflow/Contracts/IDocumentIntelligenceProvider.cs`
- `src/ExtractionWorkflow/Contracts/IExtractionAvailabilityGate.cs`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/ExtractionConfiguration.cs`
- `src/TaxAI.Portal/ExtractionEndpoints.cs`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/ExtractionAvailabilityGate.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `tests/DocumentIntelligence.Tests/NoEgressTests.cs`
- `tests/DocumentIntelligence.Tests/Production/AnalyzeLifecycleTests.cs`
- `tests/DocumentIntelligence.Tests/Production/LayoutRecognitionTests.cs`
- `tests/DocumentIntelligence.Tests/Production/LayoutResultInterpreterTests.cs`
- `tests/DocumentIntelligence.Tests/Production/ProductionPackageBoundaryTests.cs`
- `tests/DocumentIntelligence.Tests/Production/ProviderDeletionFailureTests.cs`
- `tests/DocumentIntelligence.Tests/Production/ProviderDestinationTests.cs`
- `tests/ExtractionWorkflow.Tests/BoundaryTests.cs`
- `tests/ExtractionWorkflow.Tests/Lifecycle/ExtractionAvailabilityTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`
- `tests/TaxAI.Portal.Tests/Extraction/ExtractionStartupGuardTests.cs`
- `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProviderAvailabilityTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProviderTemplateTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`

## Wave 6

- [ ] **T06** — Recovery, backup and Canadian DR — files: exact list below — depends on: T05

**Working slice:** Deliver recovery before T08 so terminal purge never ships against an unimplemented replay ledger. Create and consume the independent opaque-ID/timestamp control ledger, evidence reader and recovery ports with real Canadian adapters and quarantine orchestration. Extend the existing secure SQL/Blob IaC to zone and manual Canada East recovery, practical SQL PITR capped at 35d, no seven-year raw backup, and independently acknowledged control replication with retention beyond the recoverable chain. The reconciler works on T04 source registry tombstones and existing structured identifiers; synthetic terminal control records exercise replay without depending on T07's future retention calculator. It can re-delete a replayed terminal resource using the existing scoped physical purger and internal SQL cleanup, never authorize a new early purge. Quarantine blocks access until deletion, current authorization, key/security checks and invalidation of all pre-recovery sessions/client credentials/links finish. Preserve expired credentials. Measure zone RTO <=15m and regional application RTO <=4h/SQL RPO <=15m; asynchronous missing sources require new evidence. Healthy auth/review/non-DI operations recover while Canada Central extraction remains unavailable. Ship recovery IaC, unit/relational/degraded tests, ledger failure tests, runbook and executable DR/restore drill now.

**Verification in this task:** Real relational restore simulation plus independent ledger mocks: missing interval/ack, stale evidence, rollback, expired/revoked access, current membership uncertainty, key failure and lost source all quarantine/refuse. Test recovered auth/history availability with DI down and every forbidden failover. Ship executable zone/regional drills measuring full reconciliation-inclusive objectives.

**Single-owner ACs:** AC-102–103, AC-105–116, AC-155–157, AC-165.

**Single-owner DEP gates:** DEP-17, DEP-18, DEP-19, DEP-20.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T06.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (51 paths):**

- `docs/operations/canadian-recovery.md`
- `infra/azure/main.bicep`
- `infra/azure/modules/control-storage.bicep`
- `infra/azure/modules/recovery.bicep`
- `infra/azure/modules/sql.bicep`
- `infra/azure/modules/storage.bicep`
- `infra/azure/parameters/recovery.example.bicepparam`
- `scripts/azure/Invoke-RecoveryDrill.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T06.md`
- `specs/016-canadian-azure-production-infrastructure/status/T06.done` — only after this task's required gate
- `src/Persistence/CLAUDE.md`
- `src/Persistence/PersistenceRegistration.cs`
- `src/Persistence/Services/SqlRecoveryReconciler.cs`
- `src/TaxAI.AzureInfrastructure/AzureInfrastructureRegistration.cs`
- `src/TaxAI.AzureInfrastructure/Configuration/ApprovedResourceProbe.cs`
- `src/TaxAI.AzureInfrastructure/Content/AzureSourceObjectPurger.cs`
- `src/TaxAI.AzureInfrastructure/Control/AzureProductionEvidenceReader.cs`
- `src/TaxAI.AzureInfrastructure/Control/AzureRecoveryControlStore.cs`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `src/TaxAI.Portal/Production/RecoveryOrchestrator.cs`
- `src/Tenancy/CLAUDE.md`
- `src/Tenancy/Contracts/IRecoveryReconciler.cs`
- `src/Tenancy/Contracts/RecoveryControlContracts.cs`
- `src/Tenancy/Models/RecoveryControlModels.cs`
- `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`
- `tests/Persistence.Tests/Lifecycle/RecoveryQuarantineTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Boundaries/AzureInfrastructureBoundaryTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Control/ProductionEvidenceReaderTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Control/RecoveryControlStoreTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/ProductionRecoveryAcceptanceTests.cs`
- `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs`
- `tests/TaxAI.Portal.Tests/Production/DegradedRecoveryTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RecoveryOrchestratorTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RecoveryTemplateTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RecoveryToolingTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`
- `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`
- `tests/Tenancy.Tests/Contracts/TenancyContractTests.cs`
- `tests/Tenancy.Tests/Production/RecoveryControlContractTests.cs`

## Wave 7

- [ ] **T07** — Retention policy and lifecycle state — files: exact list below — depends on: T06

**Working slice:** Deliver the complete retention policy and live lifecycle-state projection, without initiating terminal purge. Implement final successfully associated case acceptance +30d, terminal accountant rejection/confirmed duplicate +7d, abandonment +30d after the later authorized expiry/activity and failed/partial/staged deadline <=24h; unresolved correction/review prevents age-only eligibility. Do not invent an accountant rejection route or treat failed upload as a decision. Capture authoritative facts in existing SQL intake/extraction commits, project stored deadlines and protect immutable history in the additive lifecycle migration. Introduce and consume active-operation leases/monotonic fences now in existing intake/ingest/review methods so concurrent projection and active correction preservation are real behavior, not future scaffolding. Helpers are internal where possible; do not subclass sealed workflows or change public workflow signatures. Scope/state checks also refuse terminal registry entries already used by T06 recovery. Tests exercise lease acquisition/renewal/takeover and commit fencing here. Wire policy/configuration and persistence now; T08 will consume the existing due state and exclusive fence to destroy data. A due deadline is not evidence that physical deletion occurred. Real-taxpayer deployment stays refused until execution and every other gate is complete.

**Verification in this task:** Exact deadline instants for all four categories, later-of abandonment, partial finalization versus successful association, duplicate candidate versus retained counterpart, unresolved correction preservation and current activity invalidation. Relational projection updates are atomic, immutable events remain append-only, foreign scope refuses, and exclusive seven-year structured policy stays authoritative. Test active-operation renewal/loss/takeover/stale commits with two actors and the consumed workflow helpers. No test reports AC-088 passed from a stored deadline alone: actual <=24h destruction is owned by T08.

**Single-owner ACs:** AC-084–087, AC-089, AC-093–095.

**Single-owner DEP gates:** none (meaningful policy/state proof belongs here; physical-deletion evidence belongs to T08).

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T07.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (65 paths):**

- `docs/operations/retention-and-purge.md`
- `infra/azure/main.bicep`
- `infra/azure/modules/source-lifecycle.bicep`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T07.md`
- `specs/016-canadian-azure-production-infrastructure/status/T07.done` — only after this task's required gate
- `src/ClientIntake/CLAUDE.md`
- `src/ClientIntake/Contracts/SourceLifecycleContracts.cs`
- `src/ClientIntake/Models/SourceLifecycleModels.cs`
- `src/ClientIntake/Services/AccountantIntakeWorkflow.cs`
- `src/ClientIntake/Services/ClientIntakeWorkflow.cs`
- `src/ClientIntake/Services/SourceFencedClientIntakeWorkflow.cs`
- `src/ClientIntake/Services/SourceRetentionPolicy.cs`
- `src/ExtractionWorkflow/CLAUDE.md`
- `src/ExtractionWorkflow/Contracts/IRetentionFactsReader.cs`
- `src/ExtractionWorkflow/Services/ExtractionIngestWorkflow.cs`
- `src/ExtractionWorkflow/Services/ExtractionReviewWorkflow.cs`
- `src/ExtractionWorkflow/Services/SourceFencedExtractionWorkflow.cs`
- `src/Persistence/CLAUDE.md`
- `src/Persistence/Configurations/SourceLifecycleConfiguration.cs`
- `src/Persistence/Entities/SourceLifecycleEntities.cs`
- `src/Persistence/Migrations/20260927010000_AddSourceLifecycle.Designer.cs`
- `src/Persistence/Migrations/20260927010000_AddSourceLifecycle.cs`
- `src/Persistence/Migrations/TaxAiDbContextModelSnapshot.cs`
- `src/Persistence/PersistenceRegistration.cs`
- `src/Persistence/Repositories/SqlExtractionStore.cs`
- `src/Persistence/Repositories/SqlIntakeStore.cs`
- `src/Persistence/Repositories/SqlRetentionFactsReader.cs`
- `src/Persistence/Repositories/SqlSourceLifecycleStore.cs`
- `src/Persistence/Repositories/SqlSourceRegistryStore.cs`
- `src/Persistence/TaxAiDbContext.cs`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `tests/ClientIntake.Tests/Boundaries/ClientIntakeBoundaryTests.cs`
- `tests/ClientIntake.Tests/ContractTests.cs`
- `tests/ClientIntake.Tests/Lifecycle/FencedIntakeWorkflowTests.cs`
- `tests/ClientIntake.Tests/Lifecycle/SourceLifecycleContractTests.cs`
- `tests/ClientIntake.Tests/Lifecycle/SourceRetentionPolicyTests.cs`
- `tests/ClientIntake.Tests/Models/ContractOmissionTests.cs`
- `tests/ExtractionWorkflow.Tests/BoundaryTests.cs`
- `tests/ExtractionWorkflow.Tests/Lifecycle/ExtractionLifecycleContractTests.cs`
- `tests/ExtractionWorkflow.Tests/Lifecycle/FencedExtractionWorkflowTests.cs`
- `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`
- `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`
- `tests/Persistence.Tests/EndToEnd/ProductionLifecyclePersistenceTests.cs`
- `tests/Persistence.Tests/Lifecycle/ProductionLifecycleMigrationTests.cs`
- `tests/Persistence.Tests/Lifecycle/SourceFenceConcurrencyTests.cs`
- `tests/Persistence.Tests/Lifecycle/SourceLifecycleStoreTests.cs`
- `tests/Persistence.Tests/Schema/ExtractionMigrationTests.cs`
- `tests/Persistence.Tests/Schema/MigrationTests.cs`
- `tests/Persistence.Tests/Schema/ModelShapeTests.cs`
- `tests/Persistence.Tests/Upgrade/EfCore10CompatibilityTests.cs`
- `tests/Persistence.Tests/Upgrade/EfUpgradeBaselineTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeSecurityAcceptanceTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`

## Wave 8

- [ ] **T08** — Coordinated purge execution — files: exact list below — depends on: T07

**Working slice:** Deliver system-initiated due-only terminal destruction using T07's working deadlines/state/fences and T06's independently recoverable ledger. Re-evaluate eligibility under the exclusive fence, append terminal intent, obtain recoverable control acknowledgement before deletion, remove every enabled primary/replica/version/soft-deleted representation and append structural-only outcome. Unverifiable replica destruction stays pending; retries are bounded, cancellation propagates, and retries never select a different resource/tenant. Implement Portal-only bounded maintenance with an explicit authorized firm inventory, privileged due-now initiation through operational tooling and no arbitrary accountant/early-delete route. Extend the source reader, physical purger and SQL stores at their terminal boundary so concurrent or later source-dependent work fails closed; re-upload creates new evidence. Complete due-only seven-year structured-history deletion separately from short-lived sources, preserving unrelated history. Integrate purge results with the already-working recovery reconciliation. Reuse T07's schema/state/fences rather than adding a second lifecycle model. Ship actual execution, IaC scheduling/policy integration, tests, purge drill and local DEP evidence; never defer missing implementation to T10.

**Verification in this task:** Prove staged/partial bytes are gone within 24h, final/duplicate/abandoned due execution, early/foreign/accountant refusal, repeated purge safety, bounded failures and every enabled physical form. Race purge against an active source operation and stale commit using T07's tested fences; no new successful operation may use terminal data. Test pending replica/ledger failure, structural evidence, no resurrection, source-versus-structured retention, exact seven-year execution and end-to-end restore reconciliation. Deliver the executable purge drill now.

**Single-owner ACs:** AC-088, AC-090–092, AC-096–101, AC-104.

**Single-owner DEP gates:** DEP-15, DEP-16.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T08.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (60 paths):**

- `docs/operations/retention-and-purge.md`
- `infra/azure/main.bicep`
- `infra/azure/modules/source-lifecycle.bicep`
- `scripts/azure/Invoke-PurgeDrill.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T08.md`
- `specs/016-canadian-azure-production-infrastructure/status/T08.done` — only after this task's required gate
- `src/ClientIntake/CLAUDE.md`
- `src/ClientIntake/Contracts/SourceLifecycleContracts.cs`
- `src/ClientIntake/Models/SourceLifecycleModels.cs`
- `src/ExtractionWorkflow/CLAUDE.md`
- `src/ExtractionWorkflow/Services/SourceLifecycleCoordinator.cs`
- `src/Persistence/CLAUDE.md`
- `src/Persistence/PersistenceRegistration.cs`
- `src/Persistence/Repositories/SqlExtractionStore.cs`
- `src/Persistence/Repositories/SqlIntakeStore.cs`
- `src/Persistence/Repositories/SqlSourceLifecycleStore.cs`
- `src/Persistence/Repositories/SqlSourceRegistryStore.cs`
- `src/Persistence/Services/SqlReviewRecordPurgeService.cs`
- `src/Persistence/Services/SqlSourcePurgeStore.cs`
- `src/Persistence/Services/SqlStructuredHistoryPurger.cs`
- `src/TaxAI.AzureInfrastructure/AzureInfrastructureRegistration.cs`
- `src/TaxAI.AzureInfrastructure/Content/AzureExtractionContentReader.cs`
- `src/TaxAI.AzureInfrastructure/Content/AzureSourceObjectPurger.cs`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/AuthorizedMaintenanceScope.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `src/TaxAI.Portal/Production/ProductionMaintenanceService.cs`
- `tests/ClientIntake.Tests/Boundaries/ClientIntakeBoundaryTests.cs`
- `tests/ClientIntake.Tests/ContractTests.cs`
- `tests/ClientIntake.Tests/Models/ContractOmissionTests.cs`
- `tests/ExtractionWorkflow.Tests/BoundaryTests.cs`
- `tests/ExtractionWorkflow.Tests/Lifecycle/SourceLifecycleCoordinatorTests.cs`
- `tests/ExtractionWorkflow.Tests/Models/ResultShapeTests.cs`
- `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`
- `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`
- `tests/Persistence.Tests/EndToEnd/ProductionLifecyclePersistenceTests.cs`
- `tests/Persistence.Tests/Lifecycle/SourceLifecycleStoreTests.cs`
- `tests/Persistence.Tests/Lifecycle/StructuredHistoryPurgeTests.cs`
- `tests/Persistence.Tests/Services/PurgeDiagnosticsTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Boundaries/AzureInfrastructureBoundaryTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Content/SourcePurgeTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeSecurityAcceptanceTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/ProductionInfrastructureAcceptanceTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`
- `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs`
- `tests/TaxAI.Portal.Tests/Production/MaintenanceServiceTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/PurgeToolingTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`
- `tests/Tenancy.Tests/EndToEnd/DiagnosticsLeakTests.cs`

## Wave 9

- [ ] **T09** — Diagnostics, environment separation and operational access — files: exact list below — depends on: T08

**Working slice:** Deliver operational security as a working integrated slice: Canada Central Application Insights/Log Analytics, 30-day default retention, allowlist-before-export instruments, disabled unsafe automatic collectors/crash dumps/support memory capture, and gateway/platform diagnostic controls. Introduce and consume the closed safe-telemetry port here, not earlier. Separate all listed production/non-production subscriptions/resources/keys/identities; synthetic-only non-production and no downward production copy. Complete least-privilege workload/deployment/migration/runtime roles, RBAC/PIM/JIT, phishing-resistant MFA where available, time-limited approved elevation and named break-glass review. Own federated CI/CD and bootstrap scripts, exact-resource evidence validation and actual managed-identity secret/key integration. Extend earlier service-local role assignments without broadening runtime access. Finish the deployment reconstruction pipeline and inventory verification. Ship operational IaC, all tests/guard updates, telemetry positive-control capture and access/environment probes here. A normal push cannot activate real-taxpayer ingress.

**Verification in this task:** Positive planted-value controls capture app, SDK, SQL, gateway and every enabled sink; fixed counters/events only. Offline/live probe contracts prove environment isolation, denied runtime migration, no standing content access, required PIM/MFA/approval and break-glass review. Pipeline rejects wrong targets, secrets, unsafe logs, stale/forged evidence and missing approvals.

**Single-owner ACs:** AC-015–016, AC-117–129, AC-153.

**Single-owner DEP gates:** DEP-02, DEP-03, DEP-11.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T09.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (59 paths):**

- `.github/workflows/azure-candidate.yml`
- `.github/workflows/verify.yml`
- `CLAUDE.md`
- `NEXT.md`
- `README.md`
- `docs/operations/break-glass.md`
- `docs/operations/production-bootstrap.md`
- `docs/operations/production-release.md`
- `infra/azure/main.bicep`
- `infra/azure/modules/app-service.bicep`
- `infra/azure/modules/application-gateway.bicep`
- `infra/azure/modules/identities.bicep`
- `infra/azure/modules/monitoring.bicep`
- `infra/azure/parameters/production.example.bicepparam`
- `infra/azure/parameters/recovery.example.bicepparam`
- `infra/azure/parameters/synthetic.bicepparam`
- `scripts/azure/Invoke-CandidateDeployment.ps1`
- `scripts/azure/New-DeploymentEvidence.ps1`
- `scripts/azure/Test-DeploymentEvidence.ps1`
- `scripts/azure/Test-OperationalSecurity.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/evidence/T09.md`
- `specs/016-canadian-azure-production-infrastructure/status/T09.done` — only after this task's required gate
- `src/TaxAI.AzureInfrastructure/AzureInfrastructureRegistration.cs`
- `src/TaxAI.AzureInfrastructure/CLAUDE.md`
- `src/TaxAI.AzureInfrastructure/Control/AzureProductionEvidenceReader.cs`
- `src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj`
- `src/TaxAI.AzureInfrastructure/Telemetry/AzureSafeTelemetryExporter.cs`
- `src/TaxAI.Portal/CLAUDE.md`
- `src/TaxAI.Portal/ModuleRegistration.cs`
- `src/TaxAI.Portal/PortalIdentity.cs`
- `src/TaxAI.Portal/PortalStartupGuards.cs`
- `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs`
- `src/TaxAI.Portal/Production/ProductionConfiguration.cs`
- `src/TaxAI.Portal/Production/TaxpayerSafeTelemetry.cs`
- `src/TaxAI.Portal/Program.cs`
- `src/Tenancy/CLAUDE.md`
- `src/Tenancy/Contracts/SafeTelemetryContracts.cs`
- `src/Tenancy/Models/SafeTelemetryModels.cs`
- `tests/DocumentIntelligence.Tests/NoEgressTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Boundaries/AzureInfrastructureBoundaryTests.cs`
- `tests/TaxAI.AzureInfrastructure.Tests/Telemetry/SafeTelemetryExporterTests.cs`
- `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`
- `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`
- `tests/TaxAI.Portal.Tests/Production/DeploymentEvidenceTests.cs`
- `tests/TaxAI.Portal.Tests/Production/EnvironmentAndAccessTests.cs`
- `tests/TaxAI.Portal.Tests/Production/OperationalToolingTests.cs`
- `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs`
- `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs`
- `tests/TaxAI.Portal.Tests/Production/TaxpayerSafeTelemetryTests.cs`
- `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`
- `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`
- `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs`
- `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`
- `tests/Tenancy.Tests/Contracts/TenancyContractTests.cs`
- `tests/Tenancy.Tests/EndToEnd/DiagnosticsLeakTests.cs`
- `tests/Tenancy.Tests/Production/SafeTelemetryContractTests.cs`

## Wave 10

- [ ] **T10** — Production security and deployment verification — files: exact list below — depends on: T09

**Working slice:** Perform only cross-cutting verification and evidence assembly using the already complete slice-owned adapters, IaC, tests and probes. Add the final synthetic journey/PII/isolation/evidence-assembly tests and orchestration; do not repair T01–T09, add missing package/reference/registration, implement a resource, author missing slice tests, or backfill their test floors. Run network/DNS/egress, telemetry, DI deletion/failure, key rotation, purge, SQL restore/reconciliation/session, and Canada East DR probes against explicitly authorized exact targets; record measured results. Distinguish simulated/offline proof from actual DEP proof. Review the threat model and current official/contractual provider assurance evidence, assemble every gate and obtain both named role approvals. Missing authority, failed probe, either unresolved assurance or absent mandatory evidence means REFUSED and no T10 done marker. Fixes require reopening the owning task under explicit human direction, never expanding T10. No real taxpayer data, commit, merge, self-approval or production activation is inferred.

**Verification in this task:** Re-run full repository gate and final cross-tenant/PII/no-tax-change journey; validate a 169-row AC ledger and 24 distinct DEP results with no missing/double owner. Execute previously supplied probes under separate cloud authority; retain exact artifacts/configuration/resource identities and named approvals, not payloads. Missing cloud access is unexecuted evidence, never a skipped/passed test.

**Single-owner ACs:** AC-001–002, AC-009–014.

**Single-owner DEP gates:** DEP-01, DEP-05, DEP-21, DEP-22, DEP-24.

**Evidence and completion:** Record commands, actual counts and probe modes in `evidence/T10.md`. Mandatory external results remain pending until observed against the exact resources. A slice marker is never real-taxpayer authorization.

**Exact file ownership (13 paths):**

- `docs/operations/production-release.md`
- `scripts/azure/Invoke-SyntheticVerification.ps1`
- `scripts/test-projects.json`
- `specs/016-canadian-azure-production-infrastructure/deployment-evidence-template.json`
- `specs/016-canadian-azure-production-infrastructure/deployment-verification.md`
- `specs/016-canadian-azure-production-infrastructure/evidence/T10.md`
- `specs/016-canadian-azure-production-infrastructure/release-evidence.json`
- `specs/016-canadian-azure-production-infrastructure/status/T10.done` — only after this task's required gate
- `specs/016-canadian-azure-production-infrastructure/threat-model.md`
- `specs/016-canadian-azure-production-infrastructure/verification.md`
- `tests/TaxAI.Portal.Tests/EndToEnd/ProductionInfrastructurePiiTests.cs`
- `tests/TaxAI.Portal.Tests/EndToEnd/ProductionSecurityVerificationTests.cs`
- `tests/TaxAI.Portal.Tests/Production/DeploymentEvidenceAssemblyTests.cs`

## Single-owner acceptance traceability

Each AC has exactly one accountable implementation/verification owner. Inputs
from another slice and final reruns do not change ownership. In particular,
AC-088 belongs to physical execution, not merely the <=24h deadline projection.
All 169 IDs are partitioned once.

| AC range/group | Sole owner |
|---|---|
| AC-001–002 | T10 |
| AC-003–008 | T01 |
| AC-009–014 | T10 |
| AC-015–016 | T09 |
| AC-017 | T01 |
| AC-018–024 | T02 |
| AC-025–040 | T04 |
| AC-041–048 | T03 |
| AC-049–058 | T02 |
| AC-059–083 | T05 |
| AC-084–087 | T07 |
| AC-088 | T08 |
| AC-089 | T07 |
| AC-090–092 | T08 |
| AC-093–095 | T07 |
| AC-096–101 | T08 |
| AC-102–103 | T06 |
| AC-104 | T08 |
| AC-105–116 | T06 |
| AC-117–129 | T09 |
| AC-130–152 | T01 |
| AC-153 | T09 |
| AC-154 | T02 |
| AC-155–157 | T06 |
| AC-158–164 | T05 |
| AC-165 | T06 |
| AC-166–167 | T05 |
| AC-168–169 | T01 |

## Single-owner deployment traceability

Owners supply complete executable probes and local evidence in their slice.
T10 assembles/reruns exact-resource proof with authority, not missing implementation.
DEP-14 key-rotation ownership remains T03; T04 contributes source integration
fixtures. No pending assurance or simulated probe is a live gate pass.

| DEP gate | Sole owner | Required evidence |
|---|---|---|
| DEP-01 | T10 | Approved final threat model, including gateway transient plaintext and provider disclosure |
| DEP-02 | T09 | IaC-only reconstruction through the completed deployment pipeline |
| DEP-03 | T09 | Complete Canadian inventory and separated environments |
| DEP-04 | T05 | Actual Canada Central DI resource/API/model |
| DEP-05 | T10 | Authoritative processor/privacy/input-CMK/deletion-clock evidence and named approval |
| DEP-06 | T02 | Private endpoints and per-worker private DNS |
| DEP-07 | T02 | Disabled public networking for all four dependencies |
| DEP-08 | T02 | Default-deny runtime/configuration outbound |
| DEP-09 | T02 | Unapproved synthetic source-byte egress non-delivery with positive controls |
| DEP-10 | T04 | No source bytes/SAS through HTTP or browser |
| DEP-11 | T09 | Complete app/platform/gateway allowlisted telemetry capture |
| DEP-12 | T05 | Actual Delete Analyze Result integration |
| DEP-13 | T05 | Deletion failure prevents extraction/acceptance |
| DEP-14 | T03 | Key rotation including legitimately retained old ciphertext |
| DEP-15 | T08 | Source-retention and due-purge drill |
| DEP-16 | T08 | Every enabled version/deleted form purged, or proof both disabled |
| DEP-17 | T06 | SQL restore drill |
| DEP-18 | T06 | Canada East application DR within scope; no DI failover |
| DEP-19 | T06 | Post-restore terminal purge reconciliation |
| DEP-20 | T06 | Revoked-session/expired-credential non-resurrection |
| DEP-21 | T10 | Full existing/extended cross-tenant isolation |
| DEP-22 | T10 | Final authoritative whole-repository gate |
| DEP-23 | T01 | Every individual startup/configuration refusal |
| DEP-24 | T10 | Both named roles approve exact production artifacts/resources/configuration |
## Exhaustive repository guard scan and pre-assignment

Planning scanned the full tests tree (193 C# files containing assertions), source
project graph, solution/test manifests and gate script. Searches covered exact
exports/internal signatures, reflected types, project/package/assembly references,
filesystem inventories, registration tokens, environment branches, Development-only
claims, network/SDK bans, retention/purge bans, model/migration inventories and PII
scanner positive controls. Matching guards were inspected by assertion and scope;
not every assertion in every business test requires an edit.

Predictable blockers omitted by the former draft include Tenancy/ClientIntake
exact public inventories, the second intake-security purge ban, Portal's exact
source/security/registration allowlists, hosted-service reader ban, exact EF
entity/configuration/migration inventories, Tenancy's hardcoded firm-ID purge
log audit, and upgrade baseline signature filters.

The table lists exact existing guard paths, pre-assigned in the ownership lists.
Preserve methods referenced by AmendmentAuditTests, or update the audited narrower
replacement in the same task; never delete its audit or edit historical baselines.

| Guard path | Owning slices | Required narrow treatment |
|---|---|---|
| `tests/ClientIntake.Tests/Boundaries/ClientIntakeBoundaryTests.cs` | T04, T07, T08 | Own-port implementation/source scanners and package/reference guards; explicitly scope helpers without infrastructure in the module. |
| `tests/ClientIntake.Tests/ContractTests.cs` | T04, T07, T08 | ClientIntake exact exports and port signatures; add only consumed registry/lifecycle types with scope preserved. |
| `tests/ClientIntake.Tests/Models/ContractOmissionTests.cs` | T04, T07, T08 | Exhaustive sensitive-property table for new registry/lifecycle identifiers; serialization remains omission-based. |
| `tests/DocumentIntelligence.Tests/NoEgressTests.cs` | T01, T03, T05, T09 | Whole-repository cloud-package prohibition, DI-only package inventory, compiled networking and source-token bans; keep deterministic no-egress/positive controls. |
| `tests/ExtractionWorkflow.Tests/BoundaryTests.cs` | T05, T07, T08 | T05 narrows DI's exact file/package/network bans only for the approved adapter; T07/T08 cover consumed lifecycle and execution helpers. Deterministic modules remain cloud/network-free. |
| `tests/ExtractionWorkflow.Tests/Models/ResultShapeTests.cs` | T08 | T08 adds lifecycle-helper coverage without mutable public data, provider payloads or changed existing results. |
| `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs` | T03, T04, T07, T08 | EF-only ownership, SQL provider/reference set and infrastructure/tax token guards over each new adapter. |
| `tests/Persistence.Tests/Composition/ConnectionStringGuardTests.cs` | T03 | T03 proves actual SQL managed-identity and encrypted transport with safe refusal; the source slice does not change this boundary. |
| `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs` | T04, T06, T07, T08 | Single public registration entry point, internal adapters/context and no queryable escape; unchanged signature is mandatory. |
| `tests/Persistence.Tests/Schema/ExtractionMigrationTests.cs` | T04, T07 | The closed last-migration assertion must admit the additive migration while preserving extraction migration identity/schema/hash. |
| `tests/Persistence.Tests/Schema/MigrationTests.cs` | T04, T07 | Exact migration inventory advances only for the current additive migration; original migrations remain byte-preserved. |
| `tests/Persistence.Tests/Schema/ModelShapeTests.cs` | T04, T07 | Exact EF entity/configuration inventory; retain all existing column/allowlist assertions. |
| `tests/Persistence.Tests/Services/PurgeDiagnosticsTests.cs` | T08 | T08 replaces firm-ID log payload with fixed safe operational evidence and runtime positive controls. |
| `tests/Persistence.Tests/Upgrade/EfCore10CompatibilityTests.cs` | T04, T07 | Whole-model pending-change and exact provider setup checks for each schema change. |
| `tests/Persistence.Tests/Upgrade/EfUpgradeBaselineTests.cs` | T04, T07 | Protect original model/schema/DDL while proving each new table is outside—not excluded from—the baseline. |
| `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs` | T01, T02, T03, T04, T05, T06, T07, T08, T09 | Exact Portal file inventory, per-file registration tokens, contributing extensions, compiled references and forbidden-token scanners. |
| `tests/TaxAI.Portal.Tests/CompositionRootTests.cs` | T01, T02, T03, T04, T05, T06, T07, T08, T09 | Approved environment branches, unconditional authentication/validation and real module composition. |
| `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs` | T01, T02, T03, T04, T05, T06, T07, T08, T09 | Ensure each amended historical assertion still names a live narrower proof; preserve every old audit row and method reference. |
| `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeSecurityAcceptanceTests.cs` | T04, T07, T08 | Second independent no-intake/source-retention prohibition; replace with approved scoped due-only policy, never weaken PII/tax scanners. |
| `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs` | T01, T05, T09 | Portal reference graph and documented capability/deployment claims; old module/migration preservation remains enforced. |
| `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs` | T01, T04, T05, T07, T08 | Solution/test registration plus Development-only source-store and no-intake-purge assertions; narrow only at the implementing slice. |
| `tests/TaxAI.Portal.Tests/Extraction/ExtractionStartupGuardTests.cs` | T01, T05 | Production/Development provider-reader registrations and secure startup refusal. |
| `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs` | T04, T05, T06, T08 | Existing source-reader/hosted-service prohibition: scoped source adapters and lifecycle/recovery scheduler get exact exceptions; no general reader/content route. |
| `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs` | T01, T02, T03, T05, T06, T08, T09 | Exact Portal references/public surface, project documentation and Development-only release assertions. |
| `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs` | T01, T02, T03, T04, T05, T06, T07, T08, T09 | Exact source/auth/clock/file allowlists, environment-sensitive protections and critical-only logging; later telemetry gets only explicit safe exceptions. |
| `tests/TaxAI.Portal.Tests/Startup/IntakeConfigurationTests.cs` | T01, T03 | Production key/storage configuration while all Spec 014 bounds and Development behavior remain preserved. |
| `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs` | T01, T03 | Actual durable key ring, session and SQL/configuration negative gates; TestServer exceptions never become deployment authorization. |
| `tests/TaxAI.Portal.Tests/Upgrade/DotNet10RepositoryTests.cs` | T01 | T01 registers actual net10 projects in the version/framework verification matrix; no frozen global project count. |
| `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs` | T01, T02, T03, T04, T05, T06, T07, T08, T09 | Closed baseline graph and public/internal signatures, including Tenancy exact signatures and approved Persistence additions; preserve the historical baseline files. |
| `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs` | T01, T06, T09 | Exact Tenancy source inventory and inward/zero-cloud boundary as consumed control types are added. |
| `tests/Tenancy.Tests/Contracts/TenancyContractTests.cs` | T01, T06, T09 | Closed public export/type/signature/firm-omitting port sets; keep all existing contracts and omission rules. |
| `tests/Tenancy.Tests/EndToEnd/DiagnosticsLeakTests.cs` | T08, T09 | T08 removes firm-ID purge logging; T09 adds the narrowly allowlisted exporter while preserving diagnostics positive controls. |

### Scanned preservation-only guards

These remain unchanged and must pass, rather than acquiring broad edit permission:
TaxLogicEngine/TaxCases/DocumentExtraction/CaseProcessing/CaseReview contract,
boundary and repository-verification suites. Their closed exports, mapping
resources and no-cloud signatures still apply. Their solution assertions are
presence/uniqueness checks, not permission to freeze new projects.

Other matching preservation-only families inspected:
ExtractionWorkflow SinInvariantTests/IngestPiiTests/ScopeAndTenancyTests;
CaseReview ContractOmissionTests; Portal ShellPageTests/BlazorBoundaryTests/
ReviewPageTests/TenantIsolationAtTheHttpBoundaryTests/DevelopmentEncryptedIntakeContentStoreTests;
DocumentExtraction ExtractionAcceptanceTests; CaseProcessing T4ProcessingPiiTests
and engine year-isolation tests; Persistence ExtractionSchemaTests/
ReviewRecordRepositoryTests/DocumentExtractionPersistenceTests; Tenancy
RetentionPolicyTests. They protect existing payloads/routes/columns or the
unchanged seven-year authority, not new metadata tables. The gate script already
discovers manifest entries and keeps its all-project/no-skip behavior.

New source/lease helper types still satisfy every generic sealed, immutable,
structural-ToString and JsonIgnore sweep. No excluded-directory escape.
New infrastructure guards created by T01 are extended by T03/T04/T06/T08/T09 within
their listed ownership, not left to final verification.


### Guard placement after the two splits

T03 completes key-ring, credential/SQL settings and their startup guards.
T04 does not modify those settings or repeat those guard edits; it owns source
configuration, reader integration and the source registry/schema guards. The
Portal project reference/public surface and already-approved Blob SDK ownership
do not change again merely because the source adapter is added.

T07 introduces live state and internal operation helpers; the existing generic
ExtractionWorkflow result-shape and Portal public-type checks stay unchanged.
T08 owns its execution coordinator and any resulting narrow shape assertion.
All unchanged guards still run. No guard was removed from the repository or
deferred to final verification to reduce a file count.

## Every sequentially shared path and rationale

All 82 multi-wave shared paths are listed individually. Owner order
is execution order. Repeated ownership extends a completed integration point;
it is not permission to redo earlier work or weaken its boundary.

| Exact path | Sequential owners | Why shared |
|---|---|---|
| `CLAUDE.md` | T01 → T09 | T01 records the real module/configuration boundary; T09 records completed slice integration and still-pending release evidence, preserving historical policy. |
| `NEXT.md` | T01 → T09 | T01 records the approved implementation scope without selecting a task; T09 records implemented boundaries and pending T10 evidence. |
| `README.md` | T01 → T05 → T09 | T01 describes the new but refused production path; T05 records the implemented provider without claiming assurance; T09 records operational readiness with deployment still conditional. |
| `docs/operations/production-release.md` | T09 → T10 | T09 authors a complete working release procedure; T10 appends observed exact-version approval/evidence, not missing implementation. |
| `docs/operations/retention-and-purge.md` | T07 → T08 | T07 documents authoritative lifecycle facts, deadlines and active-operation protection; T08 adds the completed destructive execution and structural evidence procedure. |
| `infra/azure/contracts/data-configuration.schema.json` | T03 → T04 | T03 defines consumed key/CMK/SQL settings; T04 extends the schema only for its actual source store and envelope settings. |
| `infra/azure/main.bicep` | T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | T02 establishes the topology; T03 integrates working key/SQL/key-ring protection; T04 source storage; T05 DI; T06 recovery; T07 retention state; T08 purge execution; T09 operational security. |
| `infra/azure/modules/app-service.bicep` | T02 → T09 | T02 owns compute/private origin/TLS; T09 extends only operational identities, safe diagnostics and dump policy. |
| `infra/azure/modules/application-gateway.bicep` | T02 → T09 | T02 owns WAF/TLS/private backend with unsafe logging disabled; T09 adds only proved-safe monitoring/operational controls. |
| `infra/azure/modules/source-lifecycle.bicep` | T07 → T08 | T07 supplies enforced deadline/state policy configuration; T08 adds bounded due-execution scheduling/configuration without changing approved clocks. |
| `infra/azure/modules/sql.bicep` | T03 → T06 | T03 owns the secure SQL/MI/private/TDE-CMK resource; T06 extends backup, replica and manual Canadian recovery policy. |
| `infra/azure/modules/storage.bicep` | T04 → T06 | T04 owns encrypted GZRS/source protection; T06 adds validated recovery behavior without changing retention or enabling shadow copies. |
| `infra/azure/parameters/production.example.bicepparam` | T02 → T09 | T02 establishes non-deployable example topology parameters; T09 supplies operational placeholders; no example grants real-data admission. |
| `infra/azure/parameters/recovery.example.bicepparam` | T02 → T06 → T09 | T02 establishes passive network inputs; T06 adds recovery configuration and T09 separated operational identities. |
| `infra/azure/parameters/synthetic.bicepparam` | T02 → T09 | T02 establishes safe concrete topology parameters; T09 extends environment/identity/monitoring separation without secrets. |
| `scripts/test-projects.json` | T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 → T10 | Every task raises only its own observed test floors; T01 also registers the new test project. T10 changes only floors for its new cross-cutting tests. |
| `specs/016-canadian-azure-production-infrastructure/threat-model.md` | T01 → T10 | T01 records admission and approved disclosure boundaries; T10 verifies the final implemented threat model and records approval. |
| `src/ClientIntake/CLAUDE.md` | T04 → T07 → T08 | Update only this slice's actual responsibilities, consumed references and secure integration; preserve historical and invariant policy. |
| `src/ClientIntake/Contracts/SourceLifecycleContracts.cs` | T07 → T08 | T07 introduces only consumed projection/active-operation ports; T08 adds consumed due-purge execution members. No unused purge interface is declared early. |
| `src/ClientIntake/Models/SourceLifecycleModels.cs` | T07 → T08 | T07 defines live lifecycle/lease state used by workflows; T08 adds the closed execution outcomes needed by real coordinated purge. |
| `src/ExtractionWorkflow/CLAUDE.md` | T05 → T07 → T08 | Update only this slice's actual responsibilities, consumed references and secure integration; preserve historical and invariant policy. |
| `src/Persistence/CLAUDE.md` | T03 → T04 → T06 → T07 → T08 | Update only this slice's actual responsibilities, consumed references and secure integration; preserve historical and invariant policy. |
| `src/Persistence/Migrations/TaxAiDbContextModelSnapshot.cs` | T04 → T07 | T04 adds only the consumed source registry; T07 adds the lifecycle/fence model consumed by state projection and active operations. T08 reuses it. Original migration bytes stay unchanged. |
| `src/Persistence/PersistenceRegistration.cs` | T04 → T06 → T07 → T08 | T04 wires registry ownership; T06 recovery reconciliation; T07 live projection/fencing; T08 due-only destruction. Preserve the public registration signature. |
| `src/Persistence/Repositories/SqlExtractionStore.cs` | T07 → T08 | T07 captures review/activity facts and enforces source-operation fences; T08 extends commit refusal to coordinated terminal destruction. |
| `src/Persistence/Repositories/SqlIntakeStore.cs` | T04 → T07 → T08 | T04 binds registry metadata atomically; T07 records authoritative lifecycle facts and checks active-operation fences; T08 integrates exclusive terminal intent with those commits. |
| `src/Persistence/Repositories/SqlSourceLifecycleStore.cs` | T07 → T08 | T07 implements live deadlines/events and consumed operation leases; T08 extends it with due-purge claim/terminal outcome behavior, not a second schema. |
| `src/Persistence/Repositories/SqlSourceRegistryStore.cs` | T04 → T07 → T08 | T04 implements ownership/staging/terminal replay; T07 coordinates live projection/fences; T08 records coordinated due-terminal intent/completion. |
| `src/Persistence/TaxAiDbContext.cs` | T04 → T07 | T04 maps the source registry; T07 maps consumed lifecycle/fence entities. No original taxpayer schema changes. |
| `src/TaxAI.AzureInfrastructure/AzureInfrastructureRegistration.cs` | T01 → T03 → T04 → T06 → T08 → T09 | T01 validation, T03 working keys/DP, T04 source adapters, T06 recovery, T08 terminal-purge integration and T09 safe exporter are registered in their owning slices. |
| `src/TaxAI.AzureInfrastructure/CLAUDE.md` | T01 → T03 → T04 → T09 | Update only this slice's actual responsibilities, consumed references and secure integration; preserve historical and invariant policy. |
| `src/TaxAI.AzureInfrastructure/Configuration/ApprovedResourceProbe.cs` | T03 → T04 → T06 | T03 validates actual key/CMK/SQL protection including wrap capability; T04 adds actual source checks; T06 adds recovery inventory. |
| `src/TaxAI.AzureInfrastructure/Configuration/AzureInfrastructureOptions.cs` | T01 → T03 → T04 | T01 consumed admission/identity options; T03 key/DP settings; T04 source/envelope settings. No future-only option registration. |
| `src/TaxAI.AzureInfrastructure/Content/AzureExtractionContentReader.cs` | T04 → T08 | T04 implements four-part scoped authenticated reads; T08 adds terminal/fence refusal using the completed lifecycle boundary. |
| `src/TaxAI.AzureInfrastructure/Content/AzureSourceObjectPurger.cs` | T04 → T06 → T08 | T04 implements scoped compensating physical cleanup; T06 uses it for replayed terminal destruction; T08 adds due-lifecycle orchestration integration and full-form verification. |
| `src/TaxAI.AzureInfrastructure/Control/AzureProductionEvidenceReader.cs` | T06 → T09 | T06 implements actual Canadian evidence storage/reading; T09 adds operational publication/identity validation, not a permissive replacement. |
| `src/TaxAI.AzureInfrastructure/TaxAI.AzureInfrastructure.csproj` | T01 → T03 → T04 → T09 | T01 consumes Tenancy/identity; T03 adds ClientIntake and key/DP packages with working consumers; T04 adds ExtractionWorkflow for the source reader; T09 adds its exporter SDK. |
| `src/TaxAI.Portal/CLAUDE.md` | T01 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Update only this slice's actual responsibilities, consumed references and secure integration; preserve historical and invariant policy. |
| `src/TaxAI.Portal/ExtractionConfiguration.cs` | T01 → T04 → T05 | T01 enforces approved production structure with missing adapters refused; T04 wires the actual reader and T05 the actual provider. |
| `src/TaxAI.Portal/IntakeConfiguration.cs` | T01 → T03 | T01 validates the production shape; T03 integrates actual key/credential protection. T04 leaves this behavior unchanged. |
| `src/TaxAI.Portal/ModuleRegistration.cs` | T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Each runtime slice registers its own completed behavior at the sole composition root; missing later capabilities remain refused. |
| `src/TaxAI.Portal/PortalIdentity.cs` | T03 → T09 | T03 integrates protected Data Protection and runtime material; T09 completes approved operational secret/identity policy without new provisioning. |
| `src/TaxAI.Portal/PortalStartupGuards.cs` | T01 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Each runtime slice adds checks of its actual completed registrations/dependencies; no deferred T10 wiring or early deployment permission. |
| `src/TaxAI.Portal/Production/ProductionAdmissionGate.cs` | T01 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | T01 implements fail-closed evidence admission; later runtime slices integrate their real secure dependency/health checks. T10 cannot edit it. |
| `src/TaxAI.Portal/Production/ProductionConfiguration.cs` | T01 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | T01 supplies the working structural validator; later runtime slices extend their consumed configuration only, with no permissive default. |
| `src/TaxAI.Portal/Program.cs` | T01 → T02 → T09 | T01 invokes secure validation; T02 integrates trusted HTTPS proxy handling; T09 integrates safe telemetry. Authentication/antiforgery stay unconditional. |
| `src/Tenancy/CLAUDE.md` | T01 → T06 → T09 | Update only this slice's actual responsibilities, consumed references and secure integration; preserve historical and invariant policy. |
| `tests/ClientIntake.Tests/Boundaries/ClientIntakeBoundaryTests.cs` | T04 → T07 → T08 | Own-port implementation/source scanners and package/reference guards; explicitly scope helpers without infrastructure in the module. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/ClientIntake.Tests/ContractTests.cs` | T04 → T07 → T08 | ClientIntake exact exports and port signatures; add only consumed registry/lifecycle types with scope preserved. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/ClientIntake.Tests/Models/ContractOmissionTests.cs` | T04 → T07 → T08 | Exhaustive sensitive-property table for new registry/lifecycle identifiers; serialization remains omission-based. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/DocumentIntelligence.Tests/NoEgressTests.cs` | T01 → T03 → T05 → T09 | Whole-repository cloud-package prohibition, DI-only package inventory, compiled networking and source-token bans; keep deterministic no-egress/positive controls. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/ExtractionWorkflow.Tests/BoundaryTests.cs` | T05 → T07 → T08 | T05 narrows the DI exact-file/package/network bans only for the approved adapter; T08 covers consumed workflow helpers. The coordination module remains cloud/network-free. |
| `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs` | T03 → T04 → T07 → T08 | EF-only ownership, SQL provider/reference set and infrastructure/tax token guards over each new adapter. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs` | T04 → T06 → T07 → T08 | Single public registration entry point, internal adapters/context and no queryable escape; unchanged signature is mandatory. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Persistence.Tests/EndToEnd/ProductionLifecyclePersistenceTests.cs` | T07 → T08 | T07 proves persisted policy/state through real workflows; T08 extends the journey through physical/structured destruction and replay. |
| `tests/Persistence.Tests/Lifecycle/SourceLifecycleStoreTests.cs` | T07 → T08 | T07 proves atomic deadlines/state/active-operation fencing; T08 proves exclusive due claims and terminal outcomes on that same store. |
| `tests/Persistence.Tests/Schema/ExtractionMigrationTests.cs` | T04 → T07 | The closed last-migration assertion must admit the additive migration while preserving extraction migration identity/schema/hash. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Persistence.Tests/Schema/MigrationTests.cs` | T04 → T07 | Exact migration inventory advances only for the current additive migration; original migrations remain byte-preserved. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Persistence.Tests/Schema/ModelShapeTests.cs` | T04 → T07 | Exact EF entity/configuration inventory; retain all existing column/allowlist assertions. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Persistence.Tests/Upgrade/EfCore10CompatibilityTests.cs` | T04 → T07 | Whole-model pending-change and exact provider setup checks for each schema change. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Persistence.Tests/Upgrade/EfUpgradeBaselineTests.cs` | T04 → T07 | Protect original model/schema/DDL while proving each new table is outside—not excluded from—the baseline. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.AzureInfrastructure.Tests/Boundaries/AzureInfrastructureBoundaryTests.cs` | T01 → T03 → T04 → T06 → T08 → T09 | Exact consumed references/packages/adapter ownership advance only with working consumers; no EF/DI SDK/host/tax logic. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.AzureInfrastructure.Tests/Configuration/ApprovedResourceProbeTests.cs` | T03 → T04 | T03 proves working key/CMK probes; T04 extends proof to actual source-store configuration and protection. |
| `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs` | T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Exact Portal file inventory, per-file registration tokens, contributing extensions, compiled references and forbidden-token scanners. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/CompositionRootTests.cs` | T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Approved environment branches, unconditional authentication/validation and real module composition. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs` | T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Ensure each amended historical assertion still names a live narrower proof; preserve every old audit row and method reference. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeSecurityAcceptanceTests.cs` | T04 → T07 → T08 | Second independent no-intake/source-retention prohibition; replace with approved scoped due-only policy, never weaken PII/tax scanners. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs` | T01 → T05 → T09 | Portal reference graph and documented capability/deployment claims; old module/migration preservation remains enforced. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs` | T01 → T04 → T05 → T07 → T08 | Solution/test registration plus Development-only source-store and no-intake-purge assertions; narrow only at the implementing slice. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Extraction/ExtractionStartupGuardTests.cs` | T01 → T05 | Production/Development provider-reader registrations and secure startup refusal. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Extraction/PreservedSurfaceTests.cs` | T04 → T05 → T06 → T08 | Existing source-reader/hosted-service prohibition: scoped source adapters and lifecycle/recovery scheduler get exact exceptions; no general reader/content route. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Production/DataTemplateTests.cs` | T03 → T04 | T03 verifies actual key/SQL/key-ring templates; T04 adds source Blob/private/CMK/envelope configuration checks. |
| `tests/TaxAI.Portal.Tests/Production/ProductionStartupTests.cs` | T01 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Extend the T01 gate with each real registration/health integration; test every absence and unsafe substitution before the slice completes. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Production/RepositoryContractTests.cs` | T01 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Extend the runtime repository contract with each consumed adapter and scanner positive control, not future references. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs` | T01 → T02 → T03 → T05 → T06 → T08 → T09 | Exact Portal references/public surface, project documentation and Development-only release assertions. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs` | T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Exact source/auth/clock/file allowlists, environment-sensitive protections and critical-only logging; later telemetry gets only explicit safe exceptions. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Startup/IntakeConfigurationTests.cs` | T01 → T03 | Production key/storage configuration while all Spec 014 bounds and Development behavior remain preserved. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs` | T01 → T03 | Actual durable key ring, session and SQL/configuration negative gates; TestServer exceptions never become deployment authorization. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/TaxAI.Portal.Tests/Upgrade/UpgradeBaselineContractTests.cs` | T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 | Closed baseline graph and public/internal signatures, including Tenancy exact signatures and approved Persistence additions; preserve the historical baseline files. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs` | T01 → T06 → T09 | Exact Tenancy source inventory and inward/zero-cloud boundary as consumed control types are added. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Tenancy.Tests/Contracts/TenancyContractTests.cs` | T01 → T06 → T09 | Closed public export/type/signature/firm-omitting port sets; keep all existing contracts and omission rules. Each slice owns its caused delta and positive controls; no blanket exemption. |
| `tests/Tenancy.Tests/EndToEnd/DiagnosticsLeakTests.cs` | T08 → T09 | T08 removes firm-ID purge logging; T09 adds the narrowly allowlisted exporter while preserving diagnostics positive controls. Each slice owns its caused delta and positive controls; no blanket exemption. |

## Review-size assessment

Ten tasks; no further split. T07 has 65 paths and T04 has 64, at the requested
review-size ceiling; every other task is smaller. Counts include necessary
existing guard edits, evidence and markers. No task exceeds 65 paths.
The new key/source and state/execution tasks each deliver independent working
behavior with their own tests and local evidence. None became too small or
resource-level setup. T10 remains deliberately verification-only and cannot
absorb unfinished earlier work.

All 169 ACs and DEP-01–DEP-24 retain one owner each. Both provider assurance gaps
remain deployment blockers, independently enforced even after every code/IaC
task completes. Human review precedes an explicitly named implementation task.
