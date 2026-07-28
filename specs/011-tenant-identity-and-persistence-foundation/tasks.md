# Tasks: Tenant Identity and Persistence Foundation (spec 011)

**Feature class: `tax-domain`** (mixed with `platform`, therefore `tax-domain` in
full — no platform relaxation anywhere).
Read `prd.md` and `architecture.md` first. **This file is read-only once
approved. Never edit it. Never check a box in it.** Completion is recorded by
creating `specs/011-tenant-identity-and-persistence-foundation/status/<TaskID>.done`
containing the PR number and commit SHA.

11 waves, 22 tasks. Each wave merges to `master` before the next begins. Every
task ships its tests in the same pass.

---

## BLOCKING — one precondition remains

**B-1 — precondition, currently unmet.** FR-9 and AC-20 assume
`scripts/Invoke-SolutionTests.ps1` and `scripts/test-projects.json` are on
`master`. They are not: they exist only on the unmerged branch
`chore/test-execution-gate` (commit `d2d9696`, PR #97). **Merge that PR before
T01.** T01 must *register* projects in the existing manifest, never recreate it.

## Class A decisions taken during planning (given by the human; not re-asked)

**B-2 — the firm-scope exemption list: ANSWERED — exactly three entries.**
FR-21/AC-41's single exemption cannot coexist with FR-39, FR-45, and FR-47, which
require identity lookups that run before any firm is known.
`FirmScopeExemptions.All` therefore contains exactly three entries, mirroring
FR-18's three-table identity carve-out one-for-one:
`IAuthenticationSessionStore.FindAsync(Guid sessionId, …)`,
`IIdentityDirectory.FindUserAsync(string issuer, string subject, …)`, and
`IIdentityDirectory.FindActiveMembershipsAsync(Guid userId, …)`. **Nothing about
the taxpayer-data path is relaxed:** every save, read, and purge of a review
record still takes a trusted `FirmId` as a required parameter with no overload,
default, optional argument, or alternate code path that omits it. T01 ships the
list as an asserted constant; AC-41's reflection test is written against it.

**B-3 — the Data Protection key ring: ANSWERED — ship the refusal, not the
provider.** FR-42's Azure Storage + Key Vault key ring needs two Azure packages
and FR-13/AC-11 permit the portal exactly one, spent on OIDC. 011 registers no
Azure Data Protection provider; `PortalStartupGuards` requires both key-ring
settings **and** a registered durable provider outside Development, so **a
non-Development host refuses to start**. The package budget is unchanged and the
real Azure key ring becomes an obligation of the later deployment specification.
**Consequence T20 must document:** a spec-011-only build starts in Development
only, and may not be deployed anywhere that can receive real taxpayer data until
that deployment spec exists.

**B-4 — the T03 UTC contract: CLARIFIED — zero offset is authoritative.**
`UtcInstantGuard` accepts a `DateTimeOffset` only when
`Offset == TimeSpan.Zero`, rejects every non-zero offset, never normalizes one,
and does not infer originating `DateTime.Kind`. `DateTimeOffset` does not retain
whether an equal zero-offset value originated from `DateTimeKind.Utc` or
`DateTimeKind.Unspecified`; AC-154 therefore does not require rejecting such a
value based on unavailable provenance. A future API accepting `DateTime`
directly may reject `DateTimeKind.Unspecified` before constructing a
`DateTimeOffset`, but that is outside T03. This Class A clarification supersedes
the ambiguous earlier wording and does not change T03's public signature.

---

## Rules that bind every task below

- One task, one branch, one PR, one review. `git worktree add ../TaxAI-w<N>t<NN> -b feat/011-w<N>-t<NN> master`
- **No file under `src/TaxLogicEngine/`, `src/TaxCases/`, `src/DocumentExtraction/`,
  `src/CaseProcessing/`, `src/CaseReview/`, or their five test trees may be
  edited by any task** (FR-4, AC-21).
- **No test may be deleted.** A superseded spec 010 assertion is replaced *in
  place* by a strictly stronger one, and the replacement is named in the PR body.
- **Never assert a total project count** for `TaxAI.sln`, in any test, document,
  or README.
- Every task runs `dotnet build TaxAI.sln`, `dotnet test TaxAI.sln`, and
  `pwsh ./scripts/Invoke-SolutionTests.ps1` green before its `.done` marker.
- Durations are `TimeSpan.FromMinutes(30)` / `TimeSpan.FromHours(8)` — **never a
  five-digit second count**; spec 010's `\b\d{5}\b` CRA-line scan would match one.
- No `bin/`, `obj/`, `.vs/`, `*.user`, developer key ring, secret, certificate,
  or `.claude/settings.local.json` may become tracked.

---

## Wave 0 — contract wave

Declares every public and internal signature with `NotImplementedException`
bodies, plus the constants (which are declarations, not behaviour). These
sentinels are temporary scaffolding: as concrete behaviours land, durable tests
must assert public contract and boundary invariants rather than require
implemented members to keep throwing. Everything downstream codes against the
declared surface.

- [ ] **T01** — Create `Tenancy` and `Persistence` (production + test projects), append them to the solution, register both test projects in the test-execution manifest with initial **floor** counts, and declare every type, port, guard, policy, and constant with `NotImplementedException` bodies. Ship contract tests that enumerate the declared surface, boundary tests asserting the two modules' reference sets and forbidden-token scans, and the solution/manifest assertions. Covers AC-1…AC-9, AC-14…AC-23, AC-27, AC-105, AC-106, AC-182. — files: `src/Tenancy/Tenancy.csproj`, `src/Tenancy/AssemblyInfo.cs`, `src/Tenancy/Models/TrustedFirmId.cs`, `src/Tenancy/Models/PersistedReviewRecord.cs`, `src/Tenancy/Models/PersistedSlip.cs`, `src/Tenancy/Models/PersistedBoxAmount.cs`, `src/Tenancy/Models/PersistedLineEntry.cs`, `src/Tenancy/Models/PersistedSourceBoxCode.cs`, `src/Tenancy/Models/PersistedUnmappedBoxCode.cs`, `src/Tenancy/Models/PortalUser.cs`, `src/Tenancy/Models/Firm.cs`, `src/Tenancy/Models/FirmMembership.cs`, `src/Tenancy/Models/AuthenticationSession.cs`, `src/Tenancy/Models/SessionBoundaries.cs`, `src/Tenancy/Models/SessionEvaluation.cs`, `src/Tenancy/Models/MembershipVerification.cs`, `src/Tenancy/Models/PurgeOutcome.cs`, `src/Tenancy/Models/PersistedSchema.cs`, `src/Tenancy/Models/SlipTypeCodes.cs`, `src/Tenancy/Models/TenancyFields.cs`, `src/Tenancy/Models/TenancyErrors.cs`, `src/Tenancy/Models/FirmScopeExemptions.cs`, `src/Tenancy/Contracts/IReviewRecordRepository.cs`, `src/Tenancy/Contracts/IAuthenticationSessionStore.cs`, `src/Tenancy/Contracts/IIdentityDirectory.cs`, `src/Tenancy/Contracts/IReviewRecordPurgeService.cs`, `src/Tenancy/Contracts/IOpaqueIdentifierSource.cs`, `src/Tenancy/Contracts/IDevelopmentFixtureSeeder.cs`, `src/Tenancy/Services/UtcInstantGuard.cs`, `src/Tenancy/Services/OpaqueTokenGuard.cs`, `src/Tenancy/Services/TaxpayerTextGuard.cs`, `src/Tenancy/Services/MonetaryValueGuard.cs`, `src/Tenancy/Services/RetentionPolicy.cs`, `src/Tenancy/Services/SessionLifetimePolicy.cs`, `src/Tenancy/Services/MembershipVerificationPolicy.cs`, `src/Tenancy/Services/ReviewPackageProjection.cs`, `src/Persistence/Persistence.csproj`, `src/Persistence/AssemblyInfo.cs`, `src/Persistence/TaxAiDbContext.cs`, `src/Persistence/PersistenceRegistration.cs`, `tests/Tenancy.Tests/Tenancy.Tests.csproj`, `tests/Tenancy.Tests/Contracts/TenancyContractTests.cs`, `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`, `tests/Persistence.Tests/Persistence.Tests.csproj`, `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`, `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs`, `TaxAI.sln`, `scripts/test-projects.json` — depends on: none (gated by B-1 only)

- [ ] **T01A** — Correct the Wave 0 scaffold transition before any Wave 1 PR merges: record the Class A T03 UTC clarification without changing its signature; replace Tenancy's all-behaviours-throw and global `NotImplementedException` scanner sentinels with durable public-contract and boundary coverage; and replace Persistence's all-behaviours-throw and frozen-production-file sentinels with durable coverage now, so T11–T16 neither require nor own an unplanned scaffold-test transition. No T02–T07 or T11–T16 production behaviour is implemented here. — files: `specs/011-tenant-identity-and-persistence-foundation/prd.md`, `specs/011-tenant-identity-and-persistence-foundation/architecture.md`, `specs/011-tenant-identity-and-persistence-foundation/tasks.md`, `tests/Tenancy.Tests/Contracts/TenancyContractTests.cs`, `tests/Tenancy.Tests/Boundaries/TenancyBoundaryTests.cs`, `tests/Persistence.Tests/Contracts/PersistenceContractTests.cs`, `tests/Persistence.Tests/Boundaries/PersistenceBoundaryTests.cs` — depends on: T01

---

## Wave 1 — `Tenancy` leaf guards and policies (6 parallel)

No task in this wave references a type another one implements. Each edits only
the W0-declared file it owns, plus its own new test file.

- [ ] **T02** — Implement `TrustedFirmId` (strict canonical GUID parse, ordinal equality, no normalization or trimming) and `SlipTypeCodes` (structural `"T4"` only, asserting no CRA semantics; unrecognized values refused). Covers AC-51 (guard level), AC-54. — files: `src/Tenancy/Models/TrustedFirmId.cs`, `src/Tenancy/Models/SlipTypeCodes.cs`, `tests/Tenancy.Tests/Models/TrustedFirmIdTests.cs`, `tests/Tenancy.Tests/Models/SlipTypeCodeTests.cs` — depends on: T01, T01A

- [ ] **T03** — Implement `UtcInstantGuard` without changing its public signature: accept a `DateTimeOffset` only when `Offset == TimeSpan.Zero`; reject every non-zero offset; **never convert, coerce, normalize, or assume UTC**. Do not infer originating `DateTime.Kind`: `DateTimeOffset` does not retain whether an equal zero-offset value came from `DateTimeKind.Utc` or `DateTimeKind.Unspecified`, so both are accepted. A future API accepting `DateTime` directly may reject `DateTimeKind.Unspecified` before construction, but that is outside T03. Covers AC-153, AC-154 as clarified by B-4. — files: `src/Tenancy/Services/UtcInstantGuard.cs`, `tests/Tenancy.Tests/Services/UtcInstantGuardTests.cs` — depends on: T01, T01A

- [ ] **T04** — Implement `OpaqueTokenGuard`: non-empty, bounded length, restricted character set (`A–Z a–z 0–9 - _`); **nothing trimmed, normalized, case-folded, or truncated to pass**. Covers AC-49, AC-50 (guard level). — files: `src/Tenancy/Services/OpaqueTokenGuard.cs`, `tests/Tenancy.Tests/Services/OpaqueTokenGuardTests.cs` — depends on: T01, T01A

- [ ] **T05** — Implement `TaxpayerTextGuard`: refuse any value exceeding its declared column length (**never truncate**) and any value containing nine consecutive digits. Covers AC-47, AC-48 (guard level). — files: `src/Tenancy/Services/TaxpayerTextGuard.cs`, `tests/Tenancy.Tests/Services/TaxpayerTextGuardTests.cs` — depends on: T01, T01A

- [ ] **T06** — Implement `MonetaryValueGuard`: refuse any `decimal` that would not survive a `decimal(19,4)` round trip exactly, by inspecting the invariant-culture string form — **no arithmetic on a monetary value, no rounding, no truncation, no re-scaling**. Must accept `1.005m`, `-1.005m`, and `0m`. Covers AC-46, AC-78–AC-80 (guard level), AC-113 (module level). — files: `src/Tenancy/Services/MonetaryValueGuard.cs`, `tests/Tenancy.Tests/Services/MonetaryValueGuardTests.cs` — depends on: T01, T01A

- [ ] **T07** — Implement `RetentionPolicy`: `ComputeRetentionUntilUtc(taxYear)` = 1 January of `taxYear + TaxYearEndOffsetYears + RetentionYears` at `00:00:00 +00:00`, and `IsPurgeEligible` = `evaluationInstantUtc >= retentionUntilUtc`. **Exclusive boundary, documented as exclusive at the declaration.** No year literal, no clock read, no inclusive end-of-day value. The seven-year period is an **engineering product default, explicitly not a CRA statutory statement** — say so in the XML doc. Covers AC-105, AC-107, AC-108, AC-148–AC-152. — files: `src/Tenancy/Services/RetentionPolicy.cs`, `tests/Tenancy.Tests/Services/RetentionPolicyTests.cs` — depends on: T01, T01A

---

## Wave 2 — `Tenancy` composed behaviour (3 parallel)

- [ ] **T08** — Implement the persisted review read-model types (state, `[JsonIgnore]`, hand-written structural `ToString()`) and `ReviewPackageProjection.Project` — the closed FR-22 allowlist, in the ordered fail-closed stages of `architecture.md` §6.1. **Never reads `T4Slip.Sin`, names no `RecipientIdentifier`, constructs no masking/redaction/sanitizing/scrubbing helper.** Stores the **trusted** firm, never the package's. Copies every collection completely, in package order, with explicit ordinals — no sort, group, filter, dedupe, merge, subtotal, aggregate, or drop. Covers AC-40, AC-42–AC-45, AC-47–AC-51 (composed), AC-81–AC-83, AC-88, AC-90, AC-92–AC-94, AC-103, AC-110–AC-113, AC-126. — files: `src/Tenancy/Services/ReviewPackageProjection.cs`, `src/Tenancy/Models/PersistedReviewRecord.cs`, `src/Tenancy/Models/PersistedSlip.cs`, `src/Tenancy/Models/PersistedBoxAmount.cs`, `src/Tenancy/Models/PersistedLineEntry.cs`, `src/Tenancy/Models/PersistedSourceBoxCode.cs`, `src/Tenancy/Models/PersistedUnmappedBoxCode.cs`, `tests/Tenancy.Tests/Services/ReviewPackageProjectionTests.cs`, `tests/Tenancy.Tests/Services/ReviewPackageProjectionRejectionTests.cs`, `tests/Tenancy.Tests/Models/PersistedReadModelTests.cs`, `tests/Tenancy.Tests/Support/SyntheticPackages.cs` — depends on: T01, T03, T04, T05, T06, T07

- [ ] **T09** — Implement `AuthenticationSession`, `SessionBoundaries`, `SessionEvaluation`, and `SessionLifetimePolicy`: 30-minute idle, 8-hour absolute; `Touch` advances the idle boundary, **never** the absolute one, and clamps idle so it cannot exceed absolute; `Evaluate` refuses when revoked or when the instant has reached either boundary. Covers AC-168–AC-171, AC-152 (session boundaries). — files: `src/Tenancy/Services/SessionLifetimePolicy.cs`, `src/Tenancy/Models/AuthenticationSession.cs`, `src/Tenancy/Models/SessionBoundaries.cs`, `src/Tenancy/Models/SessionEvaluation.cs`, `tests/Tenancy.Tests/Services/SessionLifetimePolicyTests.cs` — depends on: T01, T03

- [ ] **T10** — Implement `PortalUser`, `Firm`, `FirmMembership`, `MembershipVerification`, and `MembershipVerificationPolicy` (R-1): refuse unless the user exists and is active, the membership exists, the membership is active, and its `FirmId` matches the session's `ActiveFirmId`. **Must not call `SessionLifetimePolicy`** — R-1 and R-2 stay independent. Covers AC-157–AC-159 (policy level), AC-186 (policy level). — files: `src/Tenancy/Services/MembershipVerificationPolicy.cs`, `src/Tenancy/Models/PortalUser.cs`, `src/Tenancy/Models/Firm.cs`, `src/Tenancy/Models/FirmMembership.cs`, `src/Tenancy/Models/MembershipVerification.cs`, `tests/Tenancy.Tests/Services/MembershipVerificationPolicyTests.cs` — depends on: T01, T02

---

## Wave 3 — the EF model and the initial migration (1 task)

Single task by design: `DbContext`, configurations, and migration converge on one
model snapshot, so parallel siblings would conflict on it.

T01A already owns the Persistence scaffold-test transition:
`PersistenceContractTests.cs` now pins the registration contract without
requiring a stub exception, and `PersistenceBoundaryTests.cs` scans a required
baseline without freezing the complete future file list. T11–T16 therefore do
not need either file in their scopes, and no later task may revive an
implementation-state or all-files-exactly sentinel.

- [ ] **T11** — Implement `TaxAiDbContext` and one `IEntityTypeConfiguration<>` per entity (backing-field navigations so `Tenancy`'s types stay sealed and immutable), and generate the initial repository-owned migration. Every tenant-owned table gets a non-nullable `FirmId`; `FirmId` leads every tenant index and unique constraint; `FirmId` is `uniqueidentifier`; every string identifier column declares an explicit binary collation; every monetary column declares explicit precision and scale; `(FirmId, CaseId, ParticipantId, TaxYear, PackageVersion)` is unique; the identity carve-out is **exactly three** tables and none carries taxpayer content; the migration's `Down` drops exactly what `Up` created, **including the session table**; the exclusive meaning of `RetentionUntilUtc` is documented in the configuration and in the migration. Also ship the relational test harness (SQLite context factory) that Wave 4 consumes, and the migration-scan guard that excludes **only** the `[Migration("…")]` id token from the nine-digit scan while proving a planted nine-digit run elsewhere in the same file is still caught. Covers AC-86, AC-87, AC-100, AC-115–AC-125, AC-155, AC-156, AC-162, AC-183, AC-184. — files: `src/Persistence/TaxAiDbContext.cs`, `src/Persistence/Configurations/PersistedReviewRecordConfiguration.cs`, `src/Persistence/Configurations/PersistedSlipConfiguration.cs`, `src/Persistence/Configurations/PersistedBoxAmountConfiguration.cs`, `src/Persistence/Configurations/PersistedLineEntryConfiguration.cs`, `src/Persistence/Configurations/PersistedSourceBoxCodeConfiguration.cs`, `src/Persistence/Configurations/PersistedUnmappedBoxCodeConfiguration.cs`, `src/Persistence/Configurations/FirmConfiguration.cs`, `src/Persistence/Configurations/PortalUserConfiguration.cs`, `src/Persistence/Configurations/FirmMembershipConfiguration.cs`, `src/Persistence/Configurations/AuthenticationSessionConfiguration.cs`, `src/Persistence/Migrations/` (generated initial migration, its designer file, and the model snapshot), `tests/Persistence.Tests/Support/RelationalTestContext.cs`, `tests/Persistence.Tests/Support/SyntheticFixtures.cs`, `tests/Persistence.Tests/Schema/ModelShapeTests.cs`, `tests/Persistence.Tests/Schema/TenantIsolationSchemaTests.cs`, `tests/Persistence.Tests/Schema/PiiSchemaTests.cs`, `tests/Persistence.Tests/Schema/MigrationTests.cs` — depends on: T08, T09, T10

---

## Wave 4 — `Persistence` behaviour (4 parallel)

- [ ] **T12** — Implement `SqlReviewRecordRepository`: `SaveAsync` projects, then in one firm-scoped transaction assigns `PackageVersion = max + 1` for the subject key and writes; earlier versions stay byte-unchanged; a unique-constraint race is **refused**, never resolved by overwrite. `FindAsync` is scoped by `(FirmId, recordId)` and returns a result **byte-identical** for not-found and cross-firm, echoing no identifier. **No update, no upsert, no delete member exists.** Covers AC-35, AC-38, AC-39, AC-52, AC-73 (contract level), AC-78–AC-85, AC-103, AC-104, AC-126. — files: `src/Persistence/Repositories/SqlReviewRecordRepository.cs`, `tests/Persistence.Tests/Repositories/ReviewRecordRepositoryTests.cs`, `tests/Persistence.Tests/Repositories/ReviewRecordTenantIsolationTests.cs`, `tests/Persistence.Tests/Repositories/ReviewRecordVersioningTests.cs` — depends on: T11

- [ ] **T13** — Implement `SqlAuthenticationSessionStore`: create, find-by-`SessionId` (the enumerated exemption, returning `ActiveFirmId` rather than accepting one), touch-by-resolved-session, revoke-by-resolved-session, plus an explicit privileged expired-row removal command **invoked by no timer, hosted service, or scheduler**. Prove cross-instance visibility and cross-instance revocation with two independently constructed stores over one database. A revoked or unknown session is never resurrected or re-created. Covers AC-164, AC-173, AC-175, AC-180, AC-181, AC-185. — files: `src/Persistence/Repositories/SqlAuthenticationSessionStore.cs`, `tests/Persistence.Tests/Repositories/AuthenticationSessionStoreTests.cs`, `tests/Persistence.Tests/Repositories/SessionRevocationTests.cs` — depends on: T11

- [ ] **T14** — Implement `SqlIdentityDirectory`: user by issuer + subject (ordinal), active memberships by user, one membership by `(userId, membershipId)`, firms by id. Every identifier comparison is ordinal. A freshly migrated database returns empty for all of them. Covers AC-157–AC-159 (data level), AC-182, AC-187, AC-188. — files: `src/Persistence/Repositories/SqlIdentityDirectory.cs`, `tests/Persistence.Tests/Repositories/IdentityDirectoryTests.cs` — depends on: T11

- [ ] **T15** — Implement `SqlReviewRecordPurgeService`: deletes exactly the given firm's records whose `RetentionUntilUtc` has been reached or passed, and nothing else; requires a trusted firm with no omitting overload; returns and logs **counts and opaque identifiers only** — never a display name, employer name, amount, box code, CRA line code, or nine-digit run. Covers AC-36, AC-70–AC-72, AC-140, AC-149–AC-151 (integration). — files: `src/Persistence/Services/SqlReviewRecordPurgeService.cs`, `tests/Persistence.Tests/Services/ReviewRecordPurgeServiceTests.cs`, `tests/Persistence.Tests/Services/PurgeDiagnosticsTests.cs` — depends on: T11

---

## Wave 5 — `Persistence` composition surface (1 task)

- [ ] **T16** — Implement `PersistenceRegistration.AddTaxAiPersistence` (the single registration extension the composition root calls, so EF Core never enters the portal's project file), `GuidOpaqueIdentifierSource`, `SqlConnectionStringGuard` (present, requires encryption, does not disable certificate validation), and `DevelopmentFixtureSeeder` (synthetic only — `Test Taxpayer 001`, `Example Employer A`, deterministic GUIDs, toy amounts; **no SIN field of any kind, no nine-digit run, no address, email, or phone**). No `EnsureCreated`, no `Migrate` at startup, no schema mutation outside a migration. Covers AC-74, AC-75 (guard level), AC-101, AC-102, AC-124, AC-139, AC-189 (seeder level), AC-191. — files: `src/Persistence/PersistenceRegistration.cs`, `src/Persistence/Services/GuidOpaqueIdentifierSource.cs`, `src/Persistence/Services/SqlConnectionStringGuard.cs`, `src/Persistence/Services/DevelopmentFixtureSeeder.cs`, `tests/Persistence.Tests/Composition/PersistenceRegistrationTests.cs`, `tests/Persistence.Tests/Composition/ConnectionStringGuardTests.cs`, `tests/Persistence.Tests/Composition/DevelopmentSeederTests.cs` — depends on: T12, T13, T14, T15

---

## Wave 6 — portal composition and startup refusal (1 task)

Single task: `Program.cs` and `ModuleRegistration.cs` are the convergence point.

- [ ] **T17** — Add `Tenancy` and `Persistence` project references to the portal, call `AddTaxAiPersistence` from `ModuleRegistration.cs` (still the only place any `Add*` appears in portal source, now calling exactly one registration extension per contributing module), add `PortalStartupGuards` (connection string present; outside Development also encrypted transport with certificate validation, a configured Data Protection key ring, and the durable session store — the environment test compares `EnvironmentName` ordinally so the `IsDevelopment` token stays confined to its three approved uses), and invoke the environment-gated development seeder. The connection string is required in every environment and is supplied only by configuration providers outside source control; SQL Server registration opens no connection at startup, so portal tests supply a syntactically valid, never-opened string in memory and substitute SQLite when they exercise storage. **Amendments landing here:** `PortalProject_ReferencesExactlyCaseProcessingAndTaxLogicEngine` → the exact four-entry inward set plus proof the portal references neither `TaxCases`, `CaseReview`, nor `DocumentExtraction`; `TheCompiledPortal_ReferencesExactlyCaseProcessingAndTaxLogicEngine` → the exact new referenced-assembly set, still excluding `TaxCases` and `CaseReview`; `EveryServiceRegistrationLivesInTheCompositionRoot` → every `Add*`/`TryAdd*` is in `ModuleRegistration.cs`, which calls exactly the approved per-module extensions; both `ExpectedProductionFiles` scanner lists → the new exact production-file set with the walk-and-comment guards intact; `BothValidationFlagsAreSetOutsideAnyDevelopmentBranch` → validation unconditional **and** every environment-sensitive branch guards one of exactly four approved concerns. Covers AC-10, AC-12, AC-13, AC-31–AC-33, AC-74–AC-77, AC-114, AC-124, AC-134–AC-139, AC-178, AC-179. — files: `src/TaxAI.Portal/TaxAI.Portal.csproj`, `src/TaxAI.Portal/ModuleRegistration.cs`, `src/TaxAI.Portal/Program.cs`, `src/TaxAI.Portal/PortalStartupGuards.cs`, `src/TaxAI.Portal/appsettings.json`, `src/TaxAI.Portal/Properties/launchSettings.json`, `.gitignore`, `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`, `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`, `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`, `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`, `tests/TaxAI.Portal.Tests/Startup/StartupRefusalTests.cs` — depends on: T16

---

## Wave 7 — portal authentication and the durable session (1 task)

- [ ] **T18** — Add the portal's one `PackageReference` (`Microsoft.AspNetCore.Authentication.OpenIdConnect`, pinned 8.0.x) and implement `PortalIdentity` (OIDC challenge → cookie scheme; `SaveTokens = false`; **every provider-supplied `taxai_firm_id` discarded before the local principal is created**; sign-in resolves the user by issuer + subject and their active memberships — exactly one → `ActiveFirmId` set and the claim issued, zero or several → `ActiveFirmId` null and **no claim**, the application choosing neither) and `SessionTicketStore` (an `ITicketStore` over `IAuthenticationSessionStore` that stores **no serialized ticket**: the cookie carries only the protected opaque key, and the principal — including `taxai_firm_id` — is rebuilt from the session row on every retrieve; an unknown, revoked, or out-of-bounds key returns null and creates nothing; a store failure returns null and fails closed). Map `GET /sign-in` (anonymous challenge) and `POST /sign-out` (revokes server-side **and** clears the cookie). Cookie is `HttpOnly`, `Secure`, `SameSite=Lax`. `/` and `/health` stay byte-identical and keep answering when the session store throws. **Amendments landing here:** `PortalProject_DeclaresNoPackageReference` → the portal's package set is **exactly** the one approved OIDC package at an exactly pinned version; `PortalSecurityTests.AuthenticationPatterns` → an exact allow-list of files, schemes, and the durable ticket store, with every endpoint provably either on the enumerated anonymous list or authorization-required; `ShellPageTests` route inventory → the exact new inventory with exact HTTP methods and no route parameter named or bound to a firm, case, participant, document, session, or record identifier; `InfrastructurePatterns` file-system row → narrowed to the developer-local key ring inside an approved branch, with `UseStaticFiles`/`wwwroot` still forbidden outright. Covers AC-11, AC-29, AC-30 (preserved), AC-36, AC-53–AC-55, AC-63, AC-64, AC-67–AC-69, AC-95, AC-98, AC-99, AC-131, AC-133, AC-163, AC-165–AC-167, AC-172, AC-174, AC-176, AC-177. — files: `src/TaxAI.Portal/TaxAI.Portal.csproj`, `src/TaxAI.Portal/Program.cs`, `src/TaxAI.Portal/ModuleRegistration.cs`, `src/TaxAI.Portal/PortalIdentity.cs`, `src/TaxAI.Portal/SessionTicketStore.cs`, `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`, `tests/TaxAI.Portal.Tests/ShellPageTests.cs`, `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`, `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`, `tests/TaxAI.Portal.Tests/Identity/SignInTests.cs`, `tests/TaxAI.Portal.Tests/Identity/SessionLifetimeTests.cs`, `tests/TaxAI.Portal.Tests/Identity/SessionRevocationTests.cs`, `tests/TaxAI.Portal.Tests/Identity/CookieAndTokenTests.cs` — depends on: T17

---

## Wave 8 — per-request verification and the firm surface (1 task)

- [ ] **T19** — Implement `PortalAuthorization` (the R-1 requirement re-verified server-side on **every** protected request: session live and in bounds → user active → membership exists → active → matches `ActiveFirmId`; any failure, **including a store exception or timeout**, refuses with the fixed constant error response and no record content) and `FirmEndpoints` (`GET /firms` listing the user's active memberships by firm display name and **opaque membership identifier**; `POST /firms/select` taking exactly one caller-supplied value — a `membershipId` — plus the antiforgery token, verifying the membership belongs to the authenticated user and is currently active, revoking the current session, and issuing a **new** session with a new `SessionId` and a new `AbsoluteExpiresAtUtc`). **The portal reads a firm identifier from no request channel at all.** `/`, `/health`, `/sign-in`, and the OIDC callback paths remain the exact enumerated anonymous set and skip R-1. **Amendments landing here:** `PortalSecurityTests.InputBindingPatterns` → an exact allow-list of the antiforgery token and the membership selector, in the approved file only, and never a firm, case, participant, document, session, or package identifier used as an authorization boundary; `NoMappedEndpointTakesARouteParameter` → the final exact route inventory with exact HTTP methods. Covers AC-37, AC-56–AC-62, AC-65, AC-66, AC-73 (endpoint level), AC-96, AC-97, AC-127–AC-130, AC-132, AC-160, AC-161, AC-186, AC-190. — files: `src/TaxAI.Portal/PortalAuthorization.cs`, `src/TaxAI.Portal/FirmEndpoints.cs`, `src/TaxAI.Portal/Program.cs`, `src/TaxAI.Portal/ModuleRegistration.cs`, `tests/TaxAI.Portal.Tests/ShellPageTests.cs`, `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`, `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`, `tests/TaxAI.Portal.Tests/Identity/MembershipVerificationTests.cs`, `tests/TaxAI.Portal.Tests/Identity/FirmSelectionTests.cs`, `tests/TaxAI.Portal.Tests/Identity/TenantIsolationAtTheHttpBoundaryTests.cs` — depends on: T18

> `src/TaxAI.Portal/PortalContent.cs` is deliberately **not** in any task's file
> list. `/` and `/health` stay byte-for-byte; the firms page renders from
> `FirmEndpoints.cs`, so the constants spec 010 froze are never reopened.

---

## Wave 9 — documentation (1 task)

- [ ] **T20** — Write `src/Tenancy/CLAUDE.md` and `src/Persistence/CLAUDE.md` (each stating its boundary, exact reference set, forbidden dependencies, PII posture, and that it declares no tax-year policy of its own) plus their pointer `AGENTS.md` bridges (< 15 lines, linking `../../AGENTS.md`, `../../CLAUDE.md`, and the local `CLAUDE.md`). **Rewrite `src/TaxAI.Portal/CLAUDE.md` to the narrowed gate — narrowed, never deleted**: it must state the four-reference set, the **no-SIN-in-any-form** rule, the firm-claim-only rule, the never-trust-a-provider-firm-claim rule, the server-side-revocation rule, that the UI computes no tax and implements no CRA mapping, that the portal holds no tax-year policy, and that rendering taxpayer data, uploading a document, mutating or deleting a stored package, provisioning a firm or membership, or adding a second taxpayer-data surface **still requires a further `tax-domain` specification**. Update `README.md`, root `CLAUDE.md`, and `NEXT.md`. Record the seven-year retention default as a **product decision, not a CRA fact**, and record the B-3 consequence plainly: this build starts in Development only and may not be deployed anywhere that can receive real taxpayer data until the deployment specification exists. **No existing module's `CLAUDE.md` or `AGENTS.md` is edited, and no total project count is asserted anywhere.** **Amendments landing here:** `TheReadmeMakesNoClaimThePortalCannotBack` → an exact capability inventory (the README must state sign-in, revocable sessions, tenancy, a database, and retention **do** ship, and must still be provably unable to claim taxpayer-data screens, upload, OCR/LLM, deployment, or provisioning); `PortalInstructions_*` → the narrowed gate text. Covers AC-141–AC-147. — files: `src/Tenancy/CLAUDE.md`, `src/Tenancy/AGENTS.md`, `src/Persistence/CLAUDE.md`, `src/Persistence/AGENTS.md`, `src/TaxAI.Portal/CLAUDE.md`, `README.md`, `CLAUDE.md`, `NEXT.md`, `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`, `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs` — depends on: T19

---

## Wave 10 — end-to-end verification (1 task)

- [ ] **T21** — Ship the **Amendments audit**: for every row of the PRD's Amendments table, assert that a named replacement exists and that it asserts a strictly narrower condition, and that no superseded assertion was deleted without one. Add the repository-wide end-to-end assertions that span modules: full-suite regression, unchanged module public surfaces and embedded resource names, no nine-digit run reaching the database across a full save, and no taxpayer value in any log across a full save, read, sign-in, and purge. Raise the `Tenancy.Tests`, `Persistence.Tests`, and `TaxAI.Portal.Tests` **floors** in the manifest to the delivered counts (floors, never equalities). Write `verification.md` recording AC-1…AC-191 one by one with the test that proves each. Covers AC-24–AC-26, AC-28, AC-89, AC-91, AC-109. — files: `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs`, `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`, `tests/Persistence.Tests/EndToEnd/PiiAtRestTests.cs`, `tests/Tenancy.Tests/EndToEnd/DiagnosticsLeakTests.cs`, `scripts/test-projects.json`, `specs/011-tenant-identity-and-persistence-foundation/verification.md` — depends on: T20

---

## Never automated

- **Merging.** `git merge` and `gh pr merge` always require a human.
- **Opening a non-draft PR with unverified tests.** If tests cannot run, open the
  PR as a draft and say so in the title.
- **Approving your own work.** No agent merges its own PR.
