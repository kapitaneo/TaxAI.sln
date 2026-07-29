# Architecture: Tenant Identity and Persistence Foundation

**Spec:** `specs/011-tenant-identity-and-persistence-foundation/`
**Feature class (from the PRD):** **`tax-domain`** — mixed with a large
`platform` component, and therefore `tax-domain` in full, with no platform
relaxation claimed anywhere in this document.

This is the `tax-domain` architecture template. Because the feature also moves
repository structure, the sections a `platform` plan would require —
current/target structure, module boundaries, dependency direction, migration
sequence, path and reference changes, backward compatibility, build/test command
changes, rollback, and a "tax behaviour unchanged" confirmation — are folded in
below rather than dropped.

---

## 0. Read this first — three Class A items, now answered or clarified

The PRD states, correctly, that it makes **zero Class A assumptions**. Designing
against it surfaced places where the requirements needed a human decision or
clarification. Per root `CLAUDE.md`, a domain / PII / tenant-isolation ambiguity
is **Class A: never assume, stop and ask.** All three items below are now
answered or clarified on record. None may be reopened by an implementing agent.

### A-1 — The firm-scope exemption list and T18A identity recovery

> **DECIDED by the human, corrected by T18A: four enumerated exemptions.** The
> exemption list contains exactly the four operations named below and nothing
> else. FR-21's
> requirement is unrelaxed for every taxpayer-data operation — save, read, and
> purge each still take a trusted `FirmId` as a required parameter with no
> omitting overload. `FirmScopeExemptions.All` ships as an asserted constant of
> exactly four entries, and AC-41's reflection test is written against it.

FR-21 and AC-41 require that *every* lookup, save, and delete in `Tenancy` and
`Persistence` take a trusted `FirmId` as a required parameter, with **exactly
one** enumerated exemption: session lookup by `SessionId`.

But FR-39 keys the local user by **OIDC issuer + subject**, FR-45 requires
resolving the user's **active memberships** in order to decide what the trusted
firm even is, and merged T18 leaves the durable session with only the opaque
local `UserId`. Per-request active-user revalidation and replacement-session
issuance therefore require one more read of the same carve-out user table by
`UserId`. All three identity-directory reads necessarily run before a trusted
firm exists and cannot be scoped by one without circularity. This is the same
circularity FR-18 already acknowledges when it carves the user, membership, and
session tables out of the mandatory-`FirmId`-column rule.

The decided resolution: the exemption list contains **exactly four** entries,
all confined to FR-18's three-table identity carve-out:

| # | Operation | Why it cannot take a trusted firm |
|---|---|---|
| 1 | `IAuthenticationSessionStore.FindAsync(Guid sessionId, …)` | Establishes the firm scope; returns `ActiveFirmId` rather than accepting one (the PRD's own wording). |
| 2 | `IIdentityDirectory.FindUserAsync(string issuer, string subject, …)` | FR-39's key is issuer + subject. No firm is known at sign-in. |
| 3 | `IIdentityDirectory.FindUserByIdAsync(Guid userId, …)` | A live session holds the opaque local user id. This read revalidates active state and recovers locally stored issuer + subject for the existing T18 issuance flow; it returns no provider profile or taxpayer data. |
| 4 | `IIdentityDirectory.FindActiveMembershipsAsync(Guid userId, …)` | FR-45 requires this result *to choose* the firm. |

The list ships as a real, asserted constant (`FirmScopeExemptions.All`) so a
reviewer reads exactly four named entries and nothing else, and the reflection
test in AC-41 is written against that constant. **Every operation touching a
tenant-owned review record — save, read, purge — still requires a trusted firm
with no omitting overload, exactly as FR-21 demands.** Nothing about the
taxpayer-data path is relaxed.

The alternative considered and **not** chosen was rewording FR-21 so the rule
scopes to "tenant-owned aggregates" and the four bootstrap operations are
described as carve-out rather than exemption. Same code, different wording; the
human chose the explicit four-entry list, which is the version a reviewer can
count.

### A-2 — FR-42's Azure key ring is not implementable inside FR-13's one-package budget

> **DECIDED by the human: ship the refusal, not the provider.** 011 registers no
> Azure Data Protection provider and a non-Development host therefore refuses to
> start. FR-13/AC-11's one-package budget is unchanged, and FR-42's Azure
> Storage + Key Vault key ring becomes an obligation of the later deployment
> specification.

FR-42 requires that, outside Development, the Data Protection key ring be a
configured **Canadian Azure Storage location protected by Key Vault**. Persisting
a key ring to Azure Blob Storage requires
`Azure.Extensions.AspNetCore.DataProtection.Blobs`, and protecting it with Key
Vault requires `Azure.Extensions.AspNetCore.DataProtection.Keys`. FR-13 and
AC-11 permit the portal **exactly one** `PackageReference`, and it is spent on
OpenID Connect.

The decided resolution: 011 ships the **refusal**, not the provider.
`PortalStartupGuards` requires both key-ring settings outside
Development *and* requires that a durable Data Protection persistence provider
has been registered; because 011 registers none, **a non-Development host
refuses to start.** That is consistent with the PRD's own Out of scope ("this
build SHALL NOT be deployed to any environment that can receive real taxpayer
data until that deployment spec exists") and satisfies AC-76 literally. It also
means AC-74, AC-75, AC-76, AC-77, AC-179, and AC-189 are exercised against a
host that is *designed* to fail startup outside Development, which is what those
ACs assert.

> **B-6 — the registration half is binding, and is what T17 must implement.**
> A first pass at T17 implemented the settings half only and proposed to carry
> the registration half as documentation. **That is rejected: documentation
> saying "do not deploy" is not a runtime safety control.** The registration
> check is the whole of A-2's protection — without it, two configuration values
> are enough to start a host whose cookies are protected by a machine-local key
> ring that no second instance can read. See B-6 in `tasks.md` for the binding
> rule and the enumerated rejection list.
>
> Empirically, on ASP.NET Core 8: a bare `WebApplication` registers **no**
> `IDataProtectionProvider` at all, and even after `AddDataProtection()` the
> `KeyManagementOptions.XmlRepository` stays **null** while `XmlKeyManager`
> resolves `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys` plus DPAPI lazily. A null
> repository is therefore not "not yet decided" — it *is* the local fallback, and
> the guard must read it as such.

The alternatives considered and **not** chosen were: (a) raising the portal's
package budget to three and wiring the real Azure providers now — rejected
because it brings cloud SDK surface into the portal ahead of the deployment
spec that owns residency; and (b) accepting a configured **file-system**
key-ring path outside Development — durable and package-free, but not "Azure
Storage protected by Key Vault", so residency would rest on deployment
configuration with nothing in the build to enforce it.

**Consequence, stated plainly:** a spec-011-only build starts in Development
only. That is the intended, conservative outcome and matches the PRD's own Out
of scope — this build may not be deployed anywhere that can receive real
taxpayer data until the deployment specification exists. `README.md`, the
portal's `CLAUDE.md`, and `NEXT.md` must say so (T20).

### A-3 — `DateTimeOffset` does not retain originating `DateTime.Kind`

> **CLARIFIED by the human:** `UtcInstantGuard` accepts a `DateTimeOffset` only
> when `Offset == TimeSpan.Zero`. It rejects every non-zero offset, never
> normalizes one, and does not infer unavailable originating `DateTime.Kind`
> provenance. This Class A clarification supersedes the ambiguous earlier
> wording and does not change T03's public signature.

Equal zero-offset `DateTimeOffset` values may have been constructed from either
`DateTimeKind.Utc` or `DateTimeKind.Unspecified`, but the resulting value does
not retain that distinction. AC-154 therefore cannot require the guard to reject
a zero-offset value based on provenance it cannot observe. A future API that
accepts `DateTime` directly may reject `DateTimeKind.Unspecified` before
constructing a `DateTimeOffset`; that separate API is outside T03.

---

## 1. Current structure

Read from the tree at `D:\Projects\TaxAI` on `master`, not from a prior document.

```
TaxAI.sln
src/  TaxLogicEngine  TaxCases  DocumentExtraction  CaseProcessing  CaseReview  TaxAI.Portal
tests/ TaxLogicEngine.Tests TaxCases.Tests DocumentExtraction.Tests
       CaseProcessing.Tests CaseReview.Tests TaxAI.Portal.Tests
```

- `src/TaxAI.Portal/` production source is exactly `Program.cs`,
  `ModuleRegistration.cs`, `PortalContent.cs`. Its project file declares
  **zero** `PackageReference` and exactly two `ProjectReference`s
  (`CaseProcessing`, `TaxLogicEngine`).
- `tests/TaxAI.Portal.Tests/` is `CompositionRootTests.cs`,
  `HealthEndpointTests.cs`, `ProjectStructureTests.cs`, `ShellPageTests.cs`,
  `Boundaries/PortalBoundaryTests.cs`, `Security/PortalSecurityTests.cs`,
  `EndToEnd/SolutionAndRegressionTests.cs`,
  `EndToEnd/RepositoryVerificationTests.cs`.
- There is **no** persistence, identity, `scripts/` folder, `global.json`,
  `Directory.Build.props`, `.editorconfig`, CI workflow, or client-side stack.

**Precondition, verified and currently unmet.** The PRD's FR-9 and AC-20 assume
`scripts/Invoke-SolutionTests.ps1` and `scripts/test-projects.json` are already on
`master`. They are **not**: they exist only on the unmerged branch
`chore/test-execution-gate` (commit `d2d9696`, PR #97). **Wave 0 must not start
until that PR is merged.** `tasks.md` records this as a hard gate rather than
silently recreating those files, which would fork the manifest.

---

## 2. Target structure

```
src/
  TaxLogicEngine/      (untouched)
  TaxCases/            (untouched)
  DocumentExtraction/  (untouched)
  CaseProcessing/      (untouched)
  CaseReview/          (untouched)
  Tenancy/             NEW — deterministic identity, tenancy, session, read-model,
                       retention contracts and behaviour. No EF, no ASP.NET Core,
                       no clock, no I/O.
  Persistence/         NEW — the ONLY project in the repository that references
                       EF Core. DbContext, entity configurations, migrations,
                       repository/session-store/purge implementations.
  TaxAI.Portal/        EDITED — authentication, durable session, trusted firm
                       scope, firm-selection surface, composition, startup refusal.
tests/
  Tenancy.Tests/       NEW
  Persistence.Tests/   NEW
  TaxAI.Portal.Tests/  EDITED — every superseded spec 010 assertion replaced in
                       place by a strictly stronger one.
scripts/test-projects.json  EDITED — two new projects registered; floors raised.
```

Names deliberately avoid `Clients`, `Firms`, `Documents`, `Intake`, `Review` —
spec 010's `NoPlaceholderProjectExistsForAFutureModule` forbids exactly those five
`.csproj` names.

---

## 3. Module boundaries — what each project owns

### `src/Tenancy/` — deterministic, provider-independent

Owns: the trusted-firm value type, the persisted read-model types, the identity
and session record types, the **allowlist projection** from
`AccountantReviewPackage`, every fail-closed guard, the session lifetime rules,
the R-1 membership decision, the exclusive retention boundary calculation, the
closed field-name and error-message constants, and the repository / session-store
/ purge / identifier **ports**.

Owns no I/O, no clock, no environment read, no EF Core, no ASP.NET Core, and no
tax-year literal. Its production references are exactly `CaseReview` and
`TaxLogicEngine`.

**It reads a clock never.** Every instant — `CreatedAtUtc`, `LastSeenAtUtc`,
purge evaluation — arrives as an explicit `DateTimeOffset` parameter, so the same
inputs always produce the same record (FR-33, AC-108).

### `src/Persistence/` — the only EF Core project

Owns: `TaxAiDbContext`, one `IEntityTypeConfiguration<>` per entity, the
repository-owned migrations under `Migrations/`, the four implementations of
`Tenancy`'s ports, the opaque-identifier source, the SQL Server connection-string
guard, the environment-gated development seeder, and one registration extension.

Its only project reference is `Tenancy`. It takes **no** ASP.NET Core dependency:
the registration extension needs only
`Microsoft.Extensions.DependencyInjection.Abstractions`, which arrives with EF
Core.

### `src/TaxAI.Portal/` — the host

Owns: the OIDC challenge, the cookie scheme whose ticket store is the durable
`Tenancy` session contract, the materialization of the `taxai_firm_id` claim from
the session record, the per-request R-1 verification, the firm-selection surface,
antiforgery, the startup refusal checks, and the single composition root.

Owns no tax logic, no CRA line number, no box code, no tax-year literal, no
`decimal`, and **no taxpayer data** — `/` and `/health` keep their exact spec 010
bodies, and no route renders a slip, an amount, or a CRA line.

---

## 4. Dependency direction — the arrows that must not reverse

```
TaxAI.Portal ──► Tenancy ──────► CaseReview ──► CaseProcessing ──► DocumentExtraction
             ──► Persistence ──► Tenancy                       └─► TaxLogicEngine
             ──► CaseProcessing
             ──► TaxLogicEngine
```

- **Nothing references `TaxAI.Portal`** — not a project file, not a source file,
  not transitively (AC-14).
- **`Tenancy` never names `Persistence`** (AC-15). Inversion is by port.
- **`Persistence` never names `CaseReview`, `CaseProcessing`,
  `DocumentExtraction`, `TaxCases`, or `TaxAI.Portal`.** It sees taxpayer content
  only as already-projected `Tenancy` entities — so the tax modules cannot leak
  into an EF model even by accident.
- **`CaseReview` reaches the portal transitively but is never named there.** The
  portal calls only `Tenancy` and `Persistence` types; the C# compiler emits an
  assembly reference only for named types, so `CaseReview` stays out of the
  portal assembly's referenced-assembly set and spec 010's
  `TheCompiledPortalDoesNotReferenceCaseReview` and `ThePortalNamesNoSpec009Type`
  both stay green **unchanged**. This is a load-bearing consequence of putting
  the projection in `Tenancy`: the caller runs
  `ReviewPackageProjection.Project` while it can still name
  `AccountantReviewPackage`, then passes only the resulting
  `PersistedReviewRecord` to `IReviewRecordRepository.SaveAsync`.
  `Persistence` therefore never names, receives, serializes, or otherwise
  crosses an executable `CaseReview` type at its boundary.

---

## 5. Data model

### 5.1 What is read from the existing modules (nothing is re-specified)

| Read from | Members read | Never read |
|---|---|---|
| `AccountantReviewPackage` | `FirmId`, `CaseId`, `ParticipantId`, `TaxYear`, `ReviewedSlips` | `SignOff` |
| `ReviewedSlip` | `TaxYear`, `Slip`, `LineEntries`, `BoxAmounts`, `UnmappedBoxCodes` | `DocumentId`, `SourceReference` |
| `ReviewedBoxAmount` | `BoxCode`, `Amount` | — |
| `TaxLineEntry` | `LineNumber`, `Amount`, `SourceBoxCodes` | — |
| `T4Slip` | `EmployeeName`, `EmployerName` | **`Sin`**, `EmployeeAddress`, `EmployerAddress`, `EmployerAccountNumber`, `RppDpspRegistrationNumber`, and every amount box |
| `SupportedReviewTaxYears` | `IsSupported(int)` | — |
| `ParseResult<T>`, `ValidationError` | reused as the result and error shapes | — |

`RecipientIdentifier` is not named anywhere in the new code. **PII here is
omission, not masking** — the SIN never enters `Tenancy`, so there is nothing to
mask (FR-23, AC-88).

### 5.2 The persisted review read model (`Tenancy.Models`)

The FR-22 allowlist, made concrete. **This property set is the exact target of
AC-123 — nothing more, nothing less.** All are sealed classes; every
caller-controlled or taxpayer-content property is `[JsonIgnore]`; each type has a
hand-written structural `ToString()`.

**`PersistedReviewRecord`**

| Property | Type | Source |
|---|---|---|
| `RecordId` | `Guid` | TaxAI-generated opaque identifier |
| `FirmId` | `Guid` | **the trusted firm — never the package's** |
| `CaseId` | `string` | package, after the FR-25 opacity guard |
| `ParticipantId` | `string` | package, after the FR-25 opacity guard |
| `TaxYear` | `int` | package |
| `PackageVersion` | `int` | existing maximum + 1 |
| `CreatedAtUtc` | `DateTimeOffset` | supplied evaluation instant |
| `UpdatedAtUtc` | `DateTimeOffset` | supplied evaluation instant (equal to `CreatedAtUtc`; the record is immutable, and FR-33 requires the column) |
| `RetentionUntilUtc` | `DateTimeOffset` | `RetentionPolicy`, from `TaxYear` alone — **exclusive boundary** |
| `Slips` | `IReadOnlyList<PersistedSlip>` | package order |

**`PersistedSlip`** — `SlipId` (Guid), `FirmId`, `RecordId`, `Ordinal` (int),
`SlipTypeCode` (string, structural), `DocumentReference` (Guid, **TaxAI-generated**),
`TaxpayerDisplayName` (from `T4Slip.EmployeeName`), `EmployerDisplayName` (from
`T4Slip.EmployerName`), `BoxAmounts`, `LineEntries`, `UnmappedBoxCodes`.

**`PersistedBoxAmount`** — `BoxAmountId`, `FirmId`, `SlipId`, `Ordinal`,
`BoxCode`, `Amount` (`decimal`).

**`PersistedLineEntry`** — `LineEntryId`, `FirmId`, `SlipId`, `Ordinal`,
`LineCode`, `Amount` (`decimal`), `SourceBoxCodes`.

**`PersistedSourceBoxCode`** — `SourceBoxCodeId`, `FirmId`, `LineEntryId`,
`Ordinal`, `BoxCode`.

**`PersistedUnmappedBoxCode`** — `UnmappedBoxCodeId`, `FirmId`, `SlipId`,
`Ordinal`, `BoxCode`.

**Declared nowhere, in any table:** a SIN in any form, a SIN-presence flag, a
date of birth, an address, an email, a phone, an employer account number, an
RPP/DPSP registration number, bank details, `SourceReference`, a source path or
blob key, raw OCR text, document bytes, an OIDC token, a provider diagnostic
string, a reviewer note, a decision, or a mapping-config version (AC-86, AC-87,
AC-100; the mapping-version absence is the PRD's recorded decision — adding one
would change `TaxLogicEngine`'s public contract, which FR-4 forbids).

`Ordinal` is not decoration: FR-28 forbids sorting, grouping, filtering,
deduplicating, merging, subtotalling, or dropping anything, so package order is
stored explicitly and every read orders by it (AC-84, AC-85).

### 5.3 The identity carve-out — exactly three tables (FR-18, AC-183)

**`PortalUser`** — `UserId` (Guid), `Issuer` (string), `Subject` (string),
`IsActive` (bool), `CreatedAtUtc`, `UpdatedAtUtc`. Unique `(Issuer, Subject)`.
**No email, phone, display name, or provider profile claim** (FR-39, AC-100).

**`FirmMembership`** — `MembershipId` (Guid), `FirmId` (Guid, **non-nullable**),
`UserId` (Guid), `IsActive` (bool), `CreatedAtUtc`, `UpdatedAtUtc`. Unique
`(FirmId, UserId)`.

**`AuthenticationSession`** — `SessionId` (Guid), `UserId` (Guid),
`ActiveFirmId` (`Guid?` — **nullable by FR-45**, because zero or several active
memberships legitimately means no firm), `CreatedAtUtc`, `LastSeenAtUtc`,
`IdleExpiresAtUtc`, `AbsoluteExpiresAtUtc`, `RevokedAtUtc` (`DateTimeOffset?`).
**No OIDC token, no ticket blob, no claims payload, no free text** (FR-64,
AC-162, AC-165, AC-184).

`AuthenticationSession.Create(sessionId, userId, activeFirmId, createdAtUtc)`
is its public construction seam. It refuses empty required identifiers and an
empty present active-firm identifier, requires a zero-offset UTC creation
instant, derives the 30-minute idle and 8-hour absolute boundaries through
`SessionLifetimePolicy`, and creates only an initially-unrevoked state whose
idle boundary cannot exceed its absolute boundary. Callers cannot supply
boundaries or a revoked state. EF retains only its private materialization
constructor; `Persistence` needs neither reflection nor internal access.

**`Firm`** — `FirmId` (Guid, primary key), `DisplayName` (string),
`CreatedAtUtc`, `UpdatedAtUtc`. `Firm` is *not* in the carve-out and does not
need to be: its own primary key is a non-nullable `FirmId`, so FR-18 holds
literally.

### 5.4 Schema decisions (`Tenancy.Models.PersistedSchema`, one source of truth)

Both the C# guards and the EF configuration read these constants, so a column
length can never disagree with the guard that enforces it.

| Constant | Value | Why |
|---|---|---|
| `MonetaryPrecision` / `MonetaryScale` | `19` / `4` | FR-20's explicit precision and scale; never the provider default (AC-121). |
| `CaseIdMaxLength` / `ParticipantIdMaxLength` | `64` | FR-25 opacity bound. |
| `DisplayNameMaxLength` | `200` | Taxpayer and employer display names (FR-26). |
| `BoxCodeMaxLength` / `LineCodeMaxLength` | `16` | Engine-produced codes. |
| `SlipTypeCodeMaxLength` | `8` | Structural discriminator. |
| `IssuerMaxLength` / `SubjectMaxLength` | `256` | OIDC identity key. |
| `FirmDisplayNameMaxLength` | `200` | Not taxpayer data (FR-50). |

No constant is a five-digit integer, so AC-109's `\b\d{5}\b` scan stays clean.

- **`FirmId` is `uniqueidentifier`** (AC-119) so tenant comparison can never
  depend on collation.
- **Every string identifier column declares an explicit binary, case- and
  accent-sensitive collation** (`Latin1_General_BIN2`), and every identifier
  comparison in code is `StringComparison.Ordinal` (FR-19, AC-120).
- **`FirmId` leads every index and unique constraint on a tenant-owned entity**
  (FR-18, AC-118).
- **`(FirmId, CaseId, ParticipantId, TaxYear, PackageVersion)` is unique**
  (FR-27, AC-122). That constraint is also the concurrency control: two
  simultaneous saves for the same subject key cannot both write version *n* — the
  loser hits the unique violation and is **refused**, never an overwrite.
- **All instants are `DateTimeOffset` with a zero offset** (FR-34, AC-155).

### 5.5 Retention — the exclusive boundary

```
RetentionUntilUtc = 1 January (TaxYear + TaxYearEndOffsetYears + RetentionYears), 00:00:00 +00:00
                    TaxYearEndOffsetYears = 1,  RetentionYears = 7
For TaxYear 2025 =  2033-01-01T00:00:00Z
```

Purge-eligible when `evaluationInstantUtc >= RetentionUntilUtc`. So
`2032-12-31T23:59:59.9999999Z` is **not** eligible and `2033-01-01T00:00:00Z`
**is** (AC-148–AC-151).

Two constants rather than a literal `8` keeps the meaning legible and keeps the
source free of any `(19|20)\d{2}` year literal (AC-105). **No inclusive
end-of-day value is computed, stored, defaulted, or compared anywhere** — no
`23:59:59`, no `.9999999`, no `AddTicks(-1)` (AC-152). The word **exclusive**
appears in the property's XML doc, in its EF configuration, and in the migration
(AC-156).

**The seven-year period is an engineering product default set by the human. It is
not a CRA statutory statement, is not cited to canada.ca, and must not be
described as a CRA fact in code, comment, or documentation.**

### 5.6 JSON mapping config

**None.** This feature adds no mapping config, reads none, and touches neither
`Mappings/T4LineMap.2025.json` nor `Mappings/T5LineMap.2025.json`. Every CRA line
code stored arrives from the package the engine produced (AC-26, AC-110, AC-111).

---

## 6. Services and public API

Results use the repository's existing `ParseResult<T>` and `ValidationError`. No
new result type is invented. Every module-originated error uses a
`TenancyFields` constant and a **fixed, non-interpolated** message from
`TenancyErrors` — a template that cannot take an argument cannot leak a caller
value (FR-56, AC-92).

### 6.1 `Tenancy` — deterministic services

**`UtcInstantGuard`**
`static ValidationError? Validate(DateTimeOffset instant, string field)`
Accepts only an instant whose `Offset` is exactly `TimeSpan.Zero`; refuses every
non-zero offset. **Never converts, never coerces, never normalizes, never assumes
UTC.** It does not infer the originating `DateTime.Kind`, which a
`DateTimeOffset` does not retain, so equal zero-offset values are treated alike
regardless of whether their source was `DateTimeKind.Utc` or
`DateTimeKind.Unspecified` (FR-34, AC-153, AC-154). A future direct-`DateTime`
API may perform a kind check before construction, but that is outside T03. Every
`Tenancy` entry point that takes an instant calls this guard first.

**`OpaqueTokenGuard`**
`static ValidationError? Validate(string? value, int maxLength, string field)`
Non-empty, `<= maxLength`, characters restricted to `A–Z a–z 0–9 - _`. **Nothing
is trimmed, normalized, case-folded, or truncated to make a value pass**
(FR-25, AC-49, AC-50).

**`TaxpayerTextGuard`**
`static ValidationError? Validate(string? value, int maxLength, string field)`
Refuses a value exceeding `maxLength` — **never truncates** — and refuses any
value containing nine consecutive digits (FR-26, AC-47, AC-48).

**`MonetaryValueGuard`**
`static ValidationError? Validate(decimal amount, string field)`
Refuses any amount that would not survive a `decimal(19,4)` round trip exactly.
Implemented by inspecting the invariant-culture string form's digit counts —
**no arithmetic is performed on a monetary value** (AC-113), and no rounding or
truncation is ever applied (FR-20, AC-46). `1.005m` and `-1.005m` have scale 3
and pass unaltered (AC-78, AC-79); `0m` passes and is stored as a row (AC-80).

**`RetentionPolicy`**
`static DateTimeOffset ComputeRetentionUntilUtc(int taxYear)`
`static bool IsPurgeEligible(DateTimeOffset retentionUntilUtc, DateTimeOffset evaluationInstantUtc)`
Derived from `TaxYear` alone; never from a clock, never from "the current year",
never from an adjacent year (AC-107).

**`SlipTypeCodes`**
`const string T4`; `static bool IsRecognized(string? code)`. A structural code
asserting **no** CRA semantics — no box meaning, no line meaning, no rate, no
threshold. An unrecognized value is refused (FR-24, AC-51).

**`SessionLifetimePolicy`**
```
static readonly TimeSpan IdleTimeout      = TimeSpan.FromMinutes(30);
static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);

static ParseResult<SessionBoundaries> CreateBoundaries(DateTimeOffset createdAtUtc);
static ParseResult<SessionBoundaries> Touch(AuthenticationSession s, DateTimeOffset atUtc);
static SessionEvaluation Evaluate(AuthenticationSession s, DateTimeOffset atUtc);
```
`Touch` advances `IdleExpiresAtUtc` and **never** `AbsoluteExpiresAtUtc`, and
clamps the new idle boundary so it cannot exceed the absolute one (FR-65,
AC-170, AC-171). `Evaluate` refuses when revoked, when
`atUtc >= IdleExpiresAtUtc`, or when `atUtc >= AbsoluteExpiresAtUtc` (FR-66,
AC-168, AC-169). Durations are `TimeSpan.FromMinutes/FromHours`, **never a
five-digit second count** — spec 010's CRA-line-literal scan would match one.

**`MembershipVerificationPolicy`** (R-1, pure)
`static MembershipVerification Verify(PortalUser? user, FirmMembership? membership, AuthenticationSession session)`
Refuses unless: the user exists and is active; the membership exists; the
membership is active; and `membership.FirmId == session.ActiveFirmId`. It is a
**separate type from `SessionLifetimePolicy` on purpose** — FR-72 and AC-186
require the R-1 and R-2 checks to be independent, and two policies that cannot
call each other cannot short-circuit each other.

**`ReviewPackageProjection`** — the allowlist projection
```
static ParseResult<PersistedReviewRecord> Project(
    TrustedFirmId trustedFirm,
    AccountantReviewPackage package,
    DateTimeOffset evaluationInstantUtc,
    IOpaqueIdentifierSource identifiers);
```
Ordered, fail-closed, accumulating stages — **nothing is written unless every
stage passes**:

1. null package / null identifier source → refuse (AC-40);
2. `evaluationInstantUtc` UTC guard (AC-153, AC-154);
3. `package.FirmId` parses and equals `trustedFirm` ordinally, else refuse
   (AC-42) — and **the trusted firm, never the package's, is what is stored**
   (AC-126);
4. `SupportedReviewTaxYears.IsSupported(package.TaxYear)`, else refuse — no
   coercion, no adjacent-year fallback, in either direction (AC-43, AC-44);
5. every slip's `TaxYear` equals the package's, else refuse (AC-45);
6. `CaseId` and `ParticipantId` opacity (AC-49, AC-50);
7. per slip: display-name and employer-name guards (AC-47, AC-48), slip-type
   recognition (AC-51), box-code/line-code guards, monetary guards on every box
   amount and line amount (AC-46);
8. build the record: trusted `FirmId`, generated identifiers, `TaxYear`,
   `CreatedAtUtc = UpdatedAtUtc = evaluationInstantUtc`,
   `RetentionUntilUtc = RetentionPolicy.ComputeRetentionUntilUtc(TaxYear)`,
   collections copied **completely and in package order** with explicit ordinals.

`PackageVersion` is **not** decided here — the projection is pure and the
existing maximum is a storage fact. The projection emits version `1` and the
repository assigns the real version inside its firm-scoped transaction.

A successful projection is the **only** value accepted by the review-record
repository. Projection and every package-level validation above happen before
the `Persistence` boundary is crossed; no repository contract or implementation
names `AccountantReviewPackage` or any other executable `CaseReview` type.

**It never reads `T4Slip.Sin`, names no `RecipientIdentifier`, and constructs no
masking, redaction, sanitizing, or scrubbing helper** (FR-23, AC-88).

### 6.2 `Tenancy` — ports (implemented only in `Persistence`)

```
interface IReviewRecordRepository
    Task<ParseResult<PersistedReviewRecord>> SaveAsync(
        TrustedFirmId firm, PersistedReviewRecord record, CancellationToken ct);
    Task<PersistedReviewRecord?> FindAsync(
        TrustedFirmId firm, Guid recordId, CancellationToken ct);
```
Two members, both firm-required, **no update, no upsert, no delete** — FR-27 and
FR-37 mean there is no method for a user-reachable mutation to reach (AC-73).
`SaveAsync` accepts only the already-projected model. It independently requires
the trusted firm and refuses a record whose `FirmId` does not match that scope;
it does not re-run projection or receive package-level inputs.
`FindAsync` returns `null` identically for "does not exist" and "belongs to
another firm", echoing no identifier (FR-32, AC-52).

```
interface IReviewRecordPurgeService
    Task<PurgeOutcome> PurgeAsync(
        TrustedFirmId firm, DateTimeOffset evaluationInstantUtc, CancellationToken ct);
```
One member, firm required, no omitting overload (AC-70). `PurgeOutcome` carries
**counts only** — records deleted, slips deleted — and no identifier of a
taxpayer value (FR-36, AC-140). Its only public construction seams are
`NoEligibleRecords()` for `(0, 0)` and
`RecordsPurged(recordsDeleted, slipsDeleted)` for a positive record count and a
non-negative slip count. It has no public constructor or mutable setter, and
its fixed `ToString()` reveals no counts.

```
interface IAuthenticationSessionStore
    Task<AuthenticationSession> CreateAsync(Guid userId, Guid? activeFirmId,
        DateTimeOffset createdAtUtc, CancellationToken ct);
    Task<AuthenticationSession?> FindAsync(Guid sessionId, CancellationToken ct);   // exemption 1
    Task<AuthenticationSession?> TouchAsync(AuthenticationSession session,
        DateTimeOffset atUtc, CancellationToken ct);
    Task RevokeAsync(AuthenticationSession session, DateTimeOffset atUtc,
        CancellationToken ct);
```
`TouchAsync` and `RevokeAsync` take an **already-resolved session**, not an
identifier, so the only identifier-keyed firm-omitting lookup in the whole
contract is `FindAsync` — exactly the operation the PRD enumerated.

```
interface IIdentityDirectory
    Task<PortalUser?> FindUserAsync(string issuer, string subject, CancellationToken ct);        // exemption 2
    Task<PortalUser?> FindUserByIdAsync(Guid userId, CancellationToken ct);                       // exemption 3
    Task<IReadOnlyList<FirmMembership>> FindActiveMembershipsAsync(Guid userId, CancellationToken ct); // exemption 4
    Task<FirmMembership?> FindMembershipAsync(Guid userId, Guid membershipId, CancellationToken ct);
    Task<IReadOnlyList<Firm>> FindFirmsAsync(IReadOnlyList<Guid> firmIds, CancellationToken ct);
```
See **A-1**. `FindUserByIdAsync` is a direct parameterized read of the carve-out
user row and returns the closed `PortalUser` shape, including issuer, subject,
and active state but no provider token, profile data, or taxpayer information.
`FindMembershipAsync` is keyed by the authenticated user's own
`UserId` plus an opaque membership identifier — it can only ever return a
membership the caller already owns, which is why the firm-switch path never
accepts a firm identifier at all (see §7.4).

```
interface IOpaqueIdentifierSource   Guid NewIdentifier();
interface IDevelopmentFixtureSeeder Task SeedAsync(CancellationToken ct);
```

### 6.3 `Persistence` — implementations

- **`TaxAiDbContext`** (internal) + one `IEntityTypeConfiguration<>` per entity.
  Navigations are `IReadOnlyList<T>` mapped through backing fields
  (`PropertyAccessMode.Field`), which is what lets the entity types stay sealed
  and immutable in `Tenancy` while EF still materializes them.
- **`SqlReviewRecordRepository`** — accepts only a successful
  `ReviewPackageProjection.Project` output, verifies that its `FirmId` matches
  the independently supplied trusted firm, then, in one transaction scoped to
  that firm, reads
  `MAX(PackageVersion)` for `(FirmId, CaseId, ParticipantId, TaxYear)`, writes at
  `max + 1`, and leaves every earlier version byte-unchanged (AC-39). Reads are
  `WHERE FirmId = @firm AND RecordId = @id`, ordered by `Ordinal`. It never
  names or receives an executable `CaseReview` type and does not run projection.
- **`SqlAuthenticationSessionStore`** — creates sessions only through
  `AuthenticationSession.Create`, with no reflection or internal access; the
  four session operations plus a privileged expired-row removal command that
  **is never invoked on a timer** (FR-66, AC-185).
- **`SqlIdentityDirectory`** — the four identity reads.
- **`SqlReviewRecordPurgeService`** — deletes exactly the records of the given
  firm whose `RetentionUntilUtc` has been reached or passed, per
  `RetentionPolicy.IsPurgeEligible`; deletes nothing else, and never another
  firm's row (AC-71, AC-72).
- **`GuidOpaqueIdentifierSource`** — `Guid.NewGuid()`. It lives here, not in
  `Tenancy`, so `Tenancy` stays deterministic.
- **`SqlConnectionStringGuard`** — the connection string must be present, must
  require encryption, and must not disable certificate validation (FR-53,
  AC-74, AC-75).
- **`DevelopmentFixtureSeeder`** — synthetic rows only: `Test Taxpayer 001`,
  `Example Employer A`, deterministic GUIDs, toy amounts, **no SIN field of any
  kind and no nine-digit run anywhere** (FR-59, AC-101, AC-102).
- **`PersistenceRegistration.AddTaxAiPersistence(IServiceCollection, string connectionString)`**
  — the single registration extension the composition root calls, so the SQL
  Server package never enters the portal's project file (FR-13).

**Schema comes only from migrations.** No `EnsureCreated`, no `Migrate` at
startup, no schema mutation outside a migration (FR-16, AC-124). The initial
migration's `Down` drops exactly what `Up` created, **including the session
table** — a rollback that leaves taxpayer data or a live session behind is a
defect.

### 6.4 `TaxAI.Portal` — the host

**Authentication (`PortalIdentity.cs`).** OIDC challenge → cookie session. On
sign-in the portal:

1. **discards every claim named `taxai_firm_id` that arrived from the provider,
   before the local principal is created** (FR-43, AC-55);
2. **discards the OIDC access, refresh, and id tokens** — `SaveTokens = false`;
   they never reach the cookie, browser storage, the session record, the
   database, or a log (FR-40, AC-98, AC-99, AC-165);
3. resolves the local user by issuer + subject, then their **active** memberships:
   **exactly one** → that firm becomes `ActiveFirmId` and `taxai_firm_id` is
   issued; **zero or more than one** → `ActiveFirmId` is null, **no claim is
   issued**, and every firm-scoped operation fails closed until an explicit
   selection succeeds. **The application never chooses a firm** (FR-45, AC-63,
   AC-64).

**The durable session (`SessionTicketStore.cs`).** An `ITicketStore` over
`IAuthenticationSessionStore`. Deliberately, **no serialized ticket is stored**:

- `StoreAsync` creates the session row and returns `SessionId` as the key;
- the cookie therefore carries **only** the Data-Protection-protected key — no
  claims payload, no `FirmId`, no `UserId`, no token, no taxpayer value (FR-40,
  AC-163);
- `RetrieveAsync` loads the row, runs `SessionLifetimePolicy.Evaluate`, and
  **rebuilds** the principal — materializing `taxai_firm_id` from the record's
  `ActiveFirmId` on every request, so claim and record cannot drift (FR-43,
  AC-166);
- an unknown, revoked, or out-of-bounds session returns `null` → the request is
  unauthenticated, and **no row is created, resurrected, or re-issued** (FR-68,
  AC-173–AC-175);
- a store failure is caught and returns `null` → fail closed, no principal, no
  cached fallback (FR-70, AC-176). `/` and `/health` are constants and keep
  answering (AC-177).
- `RenewAsync` calls `TouchAsync`; `RemoveAsync` calls `RevokeAsync`.

Not storing a ticket blob is what makes AC-163, AC-165, and AC-184 provable by
reading the schema instead of by trusting a serializer.

**Per-request verification (`PortalAuthorization.cs`).** Authorization has two
stages, and the distinction is narrow:

- authenticated tenancy-bootstrap endpoints (`GET /firms` and
  `POST /firms/select`) require a live, in-bounds server-side session, a valid
  session `UserId`, a fresh `FindUserByIdAsync` result whose user is active, and
  freshly loaded active memberships. They may be reached while `ActiveFirmId` is
  null because choosing it is their only purpose;
- every normal firm-scoped protected endpoint runs that same sequence and then
  additionally requires one active membership whose user and firm match the
  session's `UserId` and `ActiveFirmId`.

Any failure, **including a store exception or timeout, refuses** with the fixed
error response and no record content (FR-47, AC-157–AC-160). `/`, `/health`,
`/sign-in`, and the OIDC callback paths are the enumerated anonymous set and skip
both stages (AC-161). The bootstrap exemption does not weaken R-1 globally.

**Startup refusal (`PortalStartupGuards.cs`).** Outside Development the host
refuses to start when the connection string is absent, does not require encrypted
transport with certificate validation, the Data Protection key ring is
unconfigured, or the registered session store is not the durable implementation
(FR-53, FR-71; AC-74–AC-76, AC-179). See **A-2**.

> **The key-ring check is a service-provider check, not a configuration check
> (B-6).** Both settings must be present *and* the built provider must contain
> `IDataProtectionProvider`, a non-null `KeyManagementOptions.XmlRepository`, and
> a non-null `KeyManagementOptions.XmlEncryptor`, **neither of which may be an
> implementation declared in the ASP.NET Core Data Protection framework
> assembly.** That single assembly rule rejects every built-in local option —
> `EphemeralXmlRepository`, `FileSystemXmlRepository`,
> `ReadOnlyFileSystemXmlRepository`, `RegistryXmlRepository`, `NullXmlEncryptor`,
> `InvalidEncryptor`, `DpapiXmlEncryptor`, `DpapiNGXmlEncryptor`, and
> `CertificateXmlEncryptor` — while admitting the intended Azure Blob Storage
> repository and Azure Key Vault encryptor unchanged. Development is exempt and
> keeps its approved developer-local key ring.
>
> **Consequence for the portal test suite.** Every suite that starts a
> **Production** host must therefore register an approved durable provider
> itself. Four do: `CompositionRootTests`, `PortalSecurityTests`,
> `HealthEndpointTests`, and `ShellPageTests`. The last two were absent from
> T17's file allowlist, which is the scope defect T17A corrects; the shared
> test-only fake lives in
> `tests/TaxAI.Portal.Tests/Support/DurableDataProtectionTestServices.cs`.

> **The environment branch is written without the `IsDevelopment` token.**
> `Program.cs` passes `builder.Environment.EnvironmentName` to the guard, which
> compares it ordinally against `Environments.Development`. AC-32 requires every
> `IsDevelopment` occurrence to guard one of three approved concerns; a *negated*
> `IsDevelopment` around a security control would violate that reading even
> though it strengthens security. The replacement test therefore enumerates
> environment-sensitive branches **by semantics**, and approves exactly four:
> developer key ring, development fixture seeding, developer exception page, and
> the FR-53 startup refusal — the last of which is only ever *enabled* outside
> Development and never disables a control inside it.

**Routes (FR-48).** `GET /` and `GET /health` — unchanged, anonymous, constant
bodies, byte-for-byte. `GET /sign-in` — anonymous challenge. The OIDC handler's
own callback paths — anonymous. `GET /firms`, `POST /firms/select`,
`POST /sign-out` — authorization-required, and the two POSTs antiforgery-protected.
**No other route is mapped**, no route pattern carries a firm, case, participant,
document, session, or record identifier (AC-127–AC-130).

**The firm-selection surface (`FirmEndpoints.cs`).** `GET /firms` lists the
signed-in user's active memberships by **firm display name and opaque membership
identifier**. `POST /firms/select` takes exactly one caller-supplied value — a
`membershipId` — plus the antiforgery token, verifies that membership belongs to
the authenticated user and is currently active, **revokes the current session**,
and issues a **new** session with a new `SessionId` and a new
`AbsoluteExpiresAtUtc` carrying the new `ActiveFirmId` (FR-46, AC-37).

The endpoint takes `UserId` only from the authenticated server-side session,
reloads the `PortalUser` through `FindUserByIdAsync`, refuses a missing or
inactive user, and reloads current active memberships. Once the selected
membership is proved active and owned by that user, issuer and subject are
recovered from the local user row and passed through the existing T18
sign-in/session-issuance flow. They never come from the request or caller claims
and never enter the opaque cookie.

> **The selector is a membership identifier, never a firm identifier.** FR-46
> permits an unverified firm selector; taking a membership id instead means the
> portal reads a firm identifier from a request **nowhere at all**, which makes
> AC-59 provable by a source scan rather than by reasoning about intent. A firm
> id supplied in a query string, header, or cookie is not merely untrusted — it
> is read by no code path (AC-56–AC-58).

**Composition (`ModuleRegistration.cs`).** Extended in place, still the only
place any `Add*`/`TryAdd*` appears in portal source. It calls **exactly one
registration extension per contributing module** — `AddTaxAiPersistence` and the
portal's own identity/authorization wiring — plus the three spec 010 singletons,
unchanged (FR-14, AC-31 preserved).

---

## 7. Path, project-reference, and command changes

**New paths:** `src/Tenancy/`, `src/Persistence/`, `src/Persistence/Migrations/`,
`tests/Tenancy.Tests/`, `tests/Persistence.Tests/`.

**Reference changes:**

| Project | Before | After |
|---|---|---|
| `Tenancy` | — | → `CaseReview`, `TaxLogicEngine` |
| `Persistence` | — | → `Tenancy`; packages `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design` (both 8.0.x pinned) |
| `TaxAI.Portal` | → `CaseProcessing`, `TaxLogicEngine` | → `CaseProcessing`, `TaxLogicEngine`, **`Tenancy`**, **`Persistence`**; **one** package: `Microsoft.AspNetCore.Authentication.OpenIdConnect` 8.0.x |
| `Tenancy.Tests` | — | → `Tenancy`, `CaseReview`, `TaxLogicEngine`; standard xUnit tooling |
| `Persistence.Tests` | — | → `Persistence`; standard xUnit tooling **plus `Microsoft.EntityFrameworkCore.Sqlite`** |
| The five tax modules | — | **unchanged — not one byte** |

**Solution:** appended only. New `Project(...)`/`EndProject` blocks with fresh
GUIDs and their configuration rows. No existing GUID, ordering, or configuration
row changes (AC-18). **No total project count is asserted anywhere** (AC-17).

**Build and test commands are unchanged:**

```bash
dotnet build TaxAI.sln
```

```bash
dotnet test TaxAI.sln
```

```bash
pwsh ./scripts/Invoke-SolutionTests.ps1
```

`scripts/test-projects.json` gains `Tenancy.Tests` and `Persistence.Tests` in the
**same PR that introduces them**, with counts recorded as **floors** — never
exact equalities (FR-9, AC-20). The `TaxAI.Portal.Tests` floor rises once, in the
final verification task, after every portal test has landed.

**Testing mechanism** (`[ASSUMED]`, Class B): relational behaviour — tenant
isolation, uniqueness, versioning, purge, revocation, cross-instance visibility —
runs against **SQLite**, a real relational provider available without Azure.
Schema and DDL assertions target the **generated SQL Server migration** plus
`GenerateCreateScript()` on a SQL-Server-configured context, which needs no live
database. **No non-relational in-memory provider is used for any isolation,
constraint, or revocation test.** `Microsoft.EntityFrameworkCore.Sqlite` is a
*test-project* package only; AC-8 still holds — `Persistence.csproj` names the
SQL Server provider and no other.

---

## 8. Backward compatibility

**Unchanged, and asserted:**

- every public type, member, and signature of `TaxLogicEngine`, `TaxCases`,
  `DocumentExtraction`, `CaseProcessing`, and `CaseReview` (FR-4, AC-25);
- their assembly identities, namespaces, package/framework/embedded-resource
  item sets (AC-27), and the two embedded mapping resources' exact logical names
  (AC-26);
- `RecipientIdentifier`'s masking to the last three digits — **untouched**; the
  new SIN decision governs what the *new* persistence layer stores and what a
  portal surface may emit, not the engine's in-memory contract;
- `MoneyRounding.ToCents` half-up — unchanged **and never invoked here**;
- `GET /` and `GET /health` — bodies byte-for-byte, status codes, content types,
  GET-only, anonymous (AC-29, AC-30);
- container validation on unconditionally (AC-31); `Program` the portal's only
  public type, every new portal type `internal` (AC-33);
- the three pre-existing `InternalsVisibleTo` grants stay three (AC-22) — the
  shipped scan is rooted at the five pre-existing modules, so the new modules'
  grants to their own test projects do not disturb it;
- `IDocumentExtractionIngress` still has no implementation anywhere (AC-34).

**Deliberately changed — each replaced by a strictly stronger assertion, none
deleted or weakened.** Every row of the PRD's Amendments table is carried into
`tasks.md` and lands **in the task that breaks it**, not in a later cleanup wave.

> **A defect in the PRD's own stage ordering, corrected here.** The PRD's
> Migration constraints put "replacement of every superseded spec 010 assertion"
> at stage 6, after the portal work at stages 4–5. But those assertions *fail the
> moment stage 4 lands*, and every stage must be independently green. The
> replacements therefore ship with the change that invalidates them. Stage 6
> becomes what it can actually be: the **audit** that every Amendments row has a
> named, strictly narrower successor (AC-28) — not the place they are first
> written.

---

## 9. Migration sequence

Each wave is independently green: `dotnet build TaxAI.sln` and the test gate must
both pass before the next begins.

| Wave | What lands |
|---|---|
| **0** | Four projects, solution entries, manifest registration, every `Tenancy`/`Persistence` signature with temporary `NotImplementedException` scaffold bodies, constants, and the boundary/contract test suites. T01A then transitions implementation-state sentinels and frozen Persistence file lists to durable contract/boundary coverage before any Wave 1 PR merges. |
| **1** | `Tenancy` leaf guards and policies: UTC instant, opacity, taxpayer text, monetary round-trip, retention boundary, trusted firm id, slip-type code. |
| **2** | `Tenancy` composed behaviour: the allowlist projection, the session lifetime rules, the R-1 membership decision. |
| **3** | `Persistence` EF model: `DbContext`, entity configurations, the initial migration, the relational test harness. |
| **4** | T14 identity-directory behaviour lands first; T11A then corrects the review-record, authentication-session, and purge-outcome construction seams. Only after T11A lands may the review-record repository, session store, and purge service implementations resume. |
| **5** | `Persistence` registration extension, identifier source, connection-string guard, development seeder. |
| **6** | Portal composition: the two new project references, `AddTaxAiPersistence`, startup refusal, gated dev seeding — **plus** the spec 010 reference/registration/production-file assertions this breaks. |
| **7** | Portal authentication and the durable session: OIDC package, cookie scheme, ticket store, `/sign-in`, `/sign-out`, claim materialization — **plus** the spec 010 authentication/route assertions this breaks. T18A then adds the fourth bootstrap identity lookup and records the two-stage authorization decision before T19. |
| **8** | R-1 authorization and the firm surface: `/firms`, `POST /firms/select`, antiforgery — **plus** the remaining route and input assertions. |
| **9** | Documentation: two module `CLAUDE.md`/`AGENTS.md` pairs, the **narrowed** portal gate, `README.md`, root `CLAUDE.md`, `NEXT.md`. |
| **10** | End-to-end verification: the Amendments audit, `verification.md`, manifest floor raises. |

Waves 3, 5, 6, 7, 8, 9, and 10 contain a single task each. That is deliberate,
not an oversight: an EF model plus its migration converges on one snapshot file,
and ASP.NET Core wiring converges on `Program.cs` and `ModuleRegistration.cs`.
Splitting those into "parallel" siblings would guarantee the merge conflicts the
one-file-one-task rule exists to prevent. Waves 1, 2, and 4 carry the real
parallelism — 6, 3, and the 4 originally independent persistence-behaviour
tasks. T11A is a corrective prerequisite introduced after T14 merged; it is not
a fifth parallel implementation.

---

## 10. Trade-offs

**1. Where the persistence entity types live — `Tenancy` (chosen) vs. `Persistence`.**
*Rejected:* a second parallel type hierarchy of EF POCOs inside `Persistence`,
with a mapper between them. It reads as the more orthodox "entities separate from
domain models". It was rejected because the mapper is precisely the component
FR-28 forbids to exist: a hand-written translation layer that can reorder, drop,
or merge a line entry, and that must be re-verified every time a property is
added. Putting the entities in `Tenancy` and the **EF configuration** in
`Persistence` satisfies FR-17's actual requirement — no `CaseReview`,
`CaseProcessing`, `DocumentExtraction`, or `TaxLogicEngine` type is mapped as an
entity, owned type, or complex type — while keeping exactly one description of
what a persisted record is. The cost is that the entity shapes must stay
EF-materializable (backing-field navigations), which is a real but bounded
constraint.

**2. The session store — a rebuilt principal (chosen) vs. a stored ticket blob.**
*Rejected:* the conventional `ITicketStore` implementation, which serializes the
whole `AuthenticationTicket` into a column. It is less code and it is what most
samples do. It was rejected because it makes AC-163, AC-165, and AC-184 —
"the cookie carries no claims payload", "the session row holds no OIDC token",
"no identity table carries taxpayer content" — assertions about the *contents of
an opaque blob*, provable only by deserializing it and trusting that nothing was
added later. Storing the enumerated columns and rebuilding the principal from
them on every retrieve makes the same guarantees readable straight off the
schema, and gives FR-43's "materialized from the session record on each request"
for free. The cost is one identity lookup per request — which R-1 (FR-47)
mandates anyway, so it is not an extra cost at all.

**3. The firm-switch selector — a membership id (chosen) vs. a firm id.**
*Rejected:* accepting the requested `FirmId` in the form post, as FR-46's wording
literally describes, treated as an unverified request. It was rejected because it
leaves a firm identifier being read from a request body, and AC-59 then has to be
argued rather than scanned: a reviewer must reason about whether that particular
read is "used as an authorization boundary". Taking an opaque membership id
belonging to the authenticated user removes the read entirely — the portal
contains no expression that gets a firm identifier from `RouteData`,
`Request.Query`, `Request.Form`, `Request.Body`, `Request.Headers`, or
`Request.Cookies`, and the test is a grep. This is strictly stronger than the
PRD requires.

**4. Amendment replacements shipped per-wave (chosen) vs. one amendment wave.**
*Rejected:* the PRD's own stage 6. See §8 — it cannot be green.

**5. SQLite for relational behaviour (chosen) vs. a SQL Server test container.**
*Rejected:* Testcontainers / LocalDB. It would exercise the real provider,
including collation and `decimal(19,4)` behaviour. It was rejected because it
adds a Docker or LocalDB prerequisite to `dotnet test TaxAI.sln`, which every
task in this repository must be able to run, and the test-execution gate treats a
project that did not run as a failure. SQLite covers isolation, uniqueness,
versioning, ordering, and revocation; the SQL-Server-specific facts (collation,
`uniqueidentifier`, precision and scale) are asserted against the **generated
migration DDL**, which needs no server. The residual gap is named in Risks.

---

## 11. Risks and mitigations

### Correctness

| Risk | Mitigation |
|---|---|
| An amount is silently rounded or re-scaled on the way to the database, so the accountant reviews a number the engine did not produce. | `MonetaryValueGuard` **refuses** anything that cannot round-trip exactly; explicit `decimal(19,4)`; no arithmetic on a monetary value anywhere (AC-113); half-cent tests in both signs plus a zero test (AC-78–AC-80). |
| A collection is reordered or deduplicated, so provenance no longer matches the slip. | Explicit stored `Ordinal`; every read orders by it; a three-slip round-trip test and a duplicate-CRA-line test that proves two rows survive and no total is stored (AC-84, AC-85). |
| Retention is computed one day, one tick, or one year off. | Boundary is **exclusive** and derived from `TaxYear` alone; tests at `2032-12-31T23:59:59.9999999Z` (false), exactly `2033-01-01T00:00:00Z` (true), and one tick later (true); a scan proving no inclusive end-of-day value is modelled anywhere (AC-148–AC-152). |
| Two concurrent saves both write version *n*. | The unique index on `(FirmId, CaseId, ParticipantId, TaxYear, PackageVersion)` makes the loser fail; the failure is **refused**, never resolved by overwrite. |
| SQLite's type affinities differ from SQL Server's, hiding a provider bug. | Named, accepted residual. Schema facts are asserted against the generated SQL Server DDL, not against SQLite; the first real-database environment is gated behind the deployment spec that this build may not be deployed without. |

### Security and PII

| Risk | Mitigation |
|---|---|
| A SIN reaches the database, a log, an error, a URL, or a fixture. | **Omission, not masking**: `T4Slip.Sin` is never read, no column exists, no SIN-presence flag exists; a nine-digit-run refusal on taxpayer text; scans over persisted columns, exceptions, logs, response bodies, URLs, cookies, migrations, and fixtures (AC-86–AC-102, AC-125). |
| A generated EF migration id (`20260728…`) contains a nine-consecutive-digit run and trips AC-125's scan. | Real collision. The scan excludes **exactly** the `[Migration("…")]` argument and the snapshot's migration-id token, and a guard test plants a nine-digit run elsewhere in the same file to prove the scan still catches it — the same scanner-guard pattern spec 010 already uses. |
| A firm boundary is crossed by a guessed identifier. | Every taxpayer-data operation requires a trusted `FirmId` with no omitting overload; `FirmId` leads every tenant index; a cross-firm read is **byte-identical** to a not-found read; a cross-firm write is impossible because the trusted firm, not the package's, is stored (AC-41, AC-52, AC-126). |
| A copied pre-logout cookie replays. | Revocation is a **server-side** row, visible to every instance; an unknown, revoked, or expired key returns null and **creates nothing** (AC-173–AC-175, AC-180, AC-181). |
| A disabled membership keeps working until the session expires. | R-1 runs on every protected request, in a policy type that cannot call the session policy, so neither check can short-circuit the other (AC-65, AC-186). |
| The session store goes down and the host degrades open. | Every failure path returns null / refuses; `/` and `/health` stay up because they are constants (AC-160, AC-176, AC-177). |
| A provider-supplied `taxai_firm_id` is trusted. | The claim is stripped before the local principal is created, and the claim is re-materialized from the session record on every request (AC-55, AC-166, AC-167). |
| A bypass or super-user path is introduced "for testing". | FR-74: no bypass, super-user, impersonation, or skip-authorization claim, header, config key, or code path, in any environment; no hard-coded firm, user, issuer, subject, or GUID identity literal in production source (AC-190, AC-191). |
| A default firm or administrator is seeded so the build is demoable. | FR-73: no default firm, no default administrator, no production credential, no membership outside Development; a freshly migrated Production host has four empty tables (AC-187–AC-189). |

### CRA compliance

| Risk | Mitigation |
|---|---|
| The seven-year retention default is later read as a CRA statutory rule. | Recorded as an **engineering product default set by the human**, deliberately uncited, and stated as such in the field's documentation, in `Tenancy`'s `CLAUDE.md`, and in `verification.md`. If a CRA retention rule turns out to be relevant it is a new Class A question, not a re-labelling. |
| A CRA line number or box code is invented in the new code. | Neither module contains a five-digit integer literal, a box→line table, a mapping-resource name, `GetManifestResourceStream`, or any engine service name; every stored CRA line code is proven to have come from the package (AC-109–AC-112). |
| Year isolation is lost at the storage layer. | The only tax-year authority is `SupportedReviewTaxYears`; neither new module declares a year literal, a supported-year list, a "current year" concept, or a fallback; 2024 and 2026 are refused identically in both directions (AC-43, AC-44, AC-103–AC-106). |

### Cross-cutting breakage and CI drift

| Risk | Mitigation |
|---|---|
| A superseded spec 010 assertion is quietly deleted instead of strengthened. | Every Amendments row is a named task obligation, the replacement is named in the PR body, and wave 10 audits that each row has a strictly narrower successor (AC-28). No test may be deleted. |
| The new test projects are not registered and silently never run. | FR-9: registered in `scripts/test-projects.json` in the same PR, with **floor** counts. The gate fails an unregistered project on purpose. |
| A total-project-count assertion re-enters the repository. | Forbidden explicitly; the new suites assert presence-and-uniqueness only. Spec 006 shipped a count and it blocked spec 007's Wave 0 outright. |
| `scripts/` is still unmerged when Wave 0 starts. | Recorded as a hard precondition in `tasks.md`. Wave 0 must not recreate those files. |

---

## 12. Rollback and recovery

- All work happens on feature branches; `master` is untouched until a **human**
  merges. Merging is never automated.
- Each wave is independently green, so a failing wave reverts to the last green
  wave.
- **Code rollback is not database rollback.** Every migration has a working
  `Down`; the initial migration's `Down` drops exactly what its `Up` created,
  **including the session table**. A rollback leaving taxpayer data or a live
  session behind is a defect, and the down script's completeness is checked before
  any environment holding real data exists.
- **Reverting wave 7 or later is a security event, not a schema change.**
  Returning to cookie-only sessions would silently restore the replay window R-2
  exists to close, so such a revert must be accompanied by revoking every
  outstanding session.
- If `Tenancy` and `Persistence` were deleted outright, only the portal would need
  reverting: no tax module depends on either (AC-14, AC-21).
- **Recovery check:** after any revert, `dotnet build TaxAI.sln` and the test gate
  must return to green with the same test set.

---

## 13. Tax behaviour unchanged

**No tax mapping, calculation, parser behaviour, rounding rule, tax-year policy,
CRA fact, or public tax contract changes in this feature.**

- No box→line mapping is read, written, or altered; both embedded mapping
  configs keep their exact contents and logical names.
- `T4SlipParser`, `T5SlipParser`, `T4LineMapper`, `T5LineMapper`,
  `T4SlipBoxAmounts`, `T5SlipBoxAmounts`, `MoneyRounding`,
  `ReviewPackageAssembler`, and `RecordDecision` are **named nowhere** in the new
  code and invoked nowhere.
- Not one file under `src/TaxLogicEngine/`, `src/TaxCases/`,
  `src/DocumentExtraction/`, `src/CaseProcessing/`, `src/CaseReview/`, or their
  five test trees is edited.
- Every CRA line code, box code, and amount that reaches the database was
  produced upstream by the engine and is copied verbatim, in order.
- The full existing suite stays green — 2,571 tests across six projects, none
  removed, skipped, renamed, or weakened — except the spec 010 posture assertions
  enumerated in the PRD's Amendments table, **each replaced by a strictly
  stronger one.**

**This feature asserts no CRA fact and therefore cites none.**
