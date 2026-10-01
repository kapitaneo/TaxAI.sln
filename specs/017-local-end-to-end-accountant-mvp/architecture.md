# Architecture: Local End-to-End Accountant MVP

## Status and authority

Feature class: **`tax-domain`** (PRD `## Feature classification`), with
`Tax/CRA semantic impact: none`. The [PRD](prd.md) is authoritative; it was
approved by the human on 2026-10-01 with clarifications (FR-42 narrowed to
correction and finalization; readings D-1 to D-5 confirmed; Class B assumptions
accepted; the amendment table approved exactly as listed).

Planning baseline: **`7ee2574`** (`origin/master`, merge of PR #152, spec 016
T06). Spec 016 is frozen after T06. Nothing here resumes T07–T10, deploys an
Azure resource, or touches `specs/016-*`, `infra/azure/`, or `scripts/azure/`.

The human approved this plan on 2026-10-01, authorized guard items X-1 to X-7
as listed in `tasks.md`, and confirmed the settings-storage, case-ordering,
refusal-notice, and Development sign-in decisions recorded there. `/implement`
runs only on an explicitly named task.

### T06 re-read (baseline check)

T06 changed no file this spec edits: nothing under `src/ClientIntake/`,
`src/DocumentIntelligence/`, `src/ExtractionWorkflow/`, the portal
`Components/`, any `*Endpoints.cs`, `Program.cs`, `PortalIdentity.cs`,
`SqlConnectionStringGuard.cs`, `DevelopmentFixtureSeeder.cs`,
`RouteAuthenticationBaselineTests.cs`, `Persistence.Tests/Composition/`, or
`ClientIntake.Tests/`. No PRD requirement is invalidated. Three T06 facts
constrain the plan:

- `ModuleRegistration.cs` gained a Production-only `RecoveryOrchestrator`
  registration, and `CompositionRootTests` slices the source text between
  `services.AddTaxAiAzureInfrastructure(` and
  `services.AddScoped<IAccountantIntakeWorkflow`. T01 must not reorder that
  region.
- `PersistenceRegistration.AddTaxAiPersistence` now also registers
  `IRecoveryReconciler`. T01's change to that file is additive beside it.
- `tests/TaxAI.Portal.Tests/EndToEnd/AmendmentAuditTests.cs` is a ledger that
  names live guard test methods **by name**. Every guard this spec amends keeps
  its existing test method names, so the ledger file itself never changes.

Current floors (`scripts/test-projects.json`): `TaxAI.Portal.Tests` 1189,
`Persistence.Tests` 305, `Tenancy.Tests` 395, `ClientIntake.Tests` 188,
`DocumentIntelligence.Tests` 230. Floors rise with each task and never fall.

## Approach

No tax file changes. No file under `src/TaxLogicEngine/`, `src/TaxCases/`,
`src/DocumentExtraction/`, `src/CaseProcessing/`, `src/CaseReview/`,
`src/ExtractionWorkflow/`, `src/TaxAI.AzureInfrastructure/`,
`src/DocumentIntelligence/Production/`, or `src/TaxAI.Portal/Production/` is
edited. No project, package reference, migration, or solution entry is added.

The feature closes five gaps between pieces that already work:

1. **Local run.** One PowerShell entry point prepares LocalDB, applies the
   existing migrations with the repository-local `dotnet-ef`, generates the
   Development settings, and starts the host over HTTPS loopback.
2. **Development identity.** The seeder adds one synthetic user and membership;
   a Development-only route signs that user in through the *existing* cookie
   sign-in event, so the durable session, firm claim, and R-1 revalidation are
   the normal ones.
3. **Upload bridge.** The deterministic Development provider tolerates one
   leading `%PDF-` line before its existing header. The upload gate is not
   edited. Samples are generated locally and never tracked.
4. **Case list, navigation, feedback.** One firm-scoped list read, one route,
   one accountant navigation component, and a closed notice vocabulary for an
   in-scope refused correction or finalization.
5. **Proof.** A vertical-slice test drives the whole flow over real HTTP,
   including the real multipart upload, plus a demo guide.

### Flow, by existing and new parts

| Step | Surface | Status |
|---|---|---|
| Start | `scripts/local/Start-LocalDemo.ps1` | new (T01) |
| Sign in | `GET`/`POST /development/sign-in` → existing `OnSigningIn` → `SessionTicketStore` | new route, existing session path (T01) |
| Case list | `GET /intake-cases` | new (T03) |
| Create case | `GET /intake-cases/new`, `POST /intake-cases` | existing, unchanged |
| Client link, redeem, answer, upload, submit | the six `/client-intake` routes | existing, unchanged |
| Extract, review, correct, accept | `ExtractionEndpoints` | existing; refusal feedback for correction (T03) |
| Finalize | `POST …/extractions/finalize` | existing; refusal feedback (T03) |
| Review output | `GET /reviews/{recordId}` | existing, byte-identical |

### 1. Local startup (T01)

`scripts/local/Start-LocalDemo.ps1` performs, idempotently:

1. toolchain check (`scripts/Assert-DotNet10Toolchain.ps1`, `dotnet tool restore`);
2. database: ensure the LocalDB instance and database exist, then
   `dotnet ef database update` against it (explicit developer action; the
   application still never migrates);
3. settings: generate once, reuse afterwards;
4. samples (added by T02);
5. `dotnet run --project src/TaxAI.Portal --launch-profile` the HTTPS profile,
   printing only the sign-in URL and the sample directory.

Parameters: `-SetupOnly` (no host start), `-Reset` (regenerate keys, content
root, and database, after confirmation), `-ConnectionString` (another loopback
SQL Server), and `-SettingsPath` (write settings to a given file instead of the
default store; used by tests so no test writes to a developer profile).

**Settings storage — a deviation from the PRD's assumed mechanism, approved.**
The PRD assumed a `UserSecretsId` in `TaxAI.Portal.csproj`. That would make
every Development `WebApplicationFactory<Program>` test host on a developer
machine load that developer's secrets, so tests that prove a refusal when a
setting is absent (including this spec's AC-13) would pass or fail depending on
local state; `ProjectStructureTests.PortalProject_DeclaresTheApprovedProperty`
also pins the project's properties. Instead:

- generated values are stored in the .NET user-secrets store under a fixed
  identifier passed explicitly (`dotnet user-secrets --id …`), outside the tree;
- the script reads them back and supplies them to the launched host as
  process-scoped environment variables (`TaxAi__Intake__…`,
  `ConnectionStrings__TaxAiDatabase`);
- `TaxAI.Portal.csproj` is **not** edited.

The accepted decision (user secrets hold generated local configuration) is
kept; only the auto-loading property is dropped.

`Properties/launchSettings.json` gains one HTTPS loopback profile (standard SDK
development certificate; port chosen in T01, loopback only). OIDC settings are
inert placeholders; `PortalIdentity`'s every-environment requirement is
untouched.

### 2. Local SQL allowance (T01)

`SqlConnectionStringGuard.RequireValid(string?)` keeps its exact behaviour. One
internal overload takes `allowDevelopmentLoopbackCertificateTrust`. With it
`true`, `TrustServerCertificate=True` is accepted **only** when the data source
is loopback: `(localdb)\…`, or a host of `localhost`, `127.0.0.1`, `::1`, `.`,
or `(local)` after removing a `tcp:` prefix, port, and instance name. Explicit
`Encrypt=True`, the `HostNameInCertificate` refusal, `Persist Security Info`,
and the authentication rules all still apply on that path.

Persistence does not read the environment. The opt-in arrives two ways:

- **Host:** an additive overload
  `AddTaxAiPersistence(IServiceCollection, string, bool allowDevelopmentLoopbackCertificateTrust)`.
  The existing two-parameter method is unchanged and delegates with `false`.
  `ModuleRegistration` makes its single persistence call with the existing
  `runsInDevelopment` ordinal comparison as the argument.
- **Migration tool:** the private design-time factory accepts one explicit
  argument (`dotnet ef database update -- --development-loopback`), supplied
  only by the local script. Without it the factory is as strict as today.

`PortalStartupGuards` and `ProductionConfigurationValidator` are not edited, so
outside Development `TrustServerCertificate=True` is refused by three
independent checks exactly as today.

### 3. Development identity (T01)

- `Persistence.DevelopmentFixtureIdentity` — a public static class with two
  constants, the synthetic issuer and subject (for example an `.invalid`
  issuer). It is the one shared fact between the seeder and the host.
- `DevelopmentFixtureSeeder` additionally inserts, idempotently and with
  deterministic identifiers, one `PortalUsers` row and one active
  `FirmMemberships` row in the existing seeded firm. No credential, token, or
  session row. It is still invoked only from `Program.cs`'s existing
  Development branch. No migration inserts identity.
- `DevelopmentSignInEndpoints.Map` is called from `Program.cs` only when
  `PortalStartupGuards.RunsInDevelopment(environmentName)` is true (ordinal).
  - `GET /development/sign-in` returns `DevelopmentSignInPage` (static SSR; one
    antiforgery form; no parameter).
  - `POST /development/sign-in` (antiforgery required, anonymous by explicit
    declaration) builds a principal carrying only the fixed issuer and subject
    under the external authentication type — the same shape
    `FirmEndpoints.IssueReplacementAsync` already uses — and calls
    `SignInAsync(PortalIdentity.CookieScheme, …)`. `ResolveLocalPrincipalAsync`
    then performs the directory lookup, issues the firm claim under the existing
    exactly-one-membership rule, and `SessionTicketStore` writes the durable
    session. The handler reads no request value other than the antiforgery
    token, and redirects to a compile-time constant.
  - A missing or inactive seeded user throws the existing fixed
    `UnresolvedPrincipalMessage` and becomes the existing fixed `500`.

The redirect constant is `/intake-cases/new` in T01 (the only accountant page
that exists then) and becomes `/intake-cases` in T03.

### 4. Deterministic bridge and samples (T02)

`DeterministicDocumentIntelligenceProvider.Parse` changes in one place: while
the header has not been seen, the **first** non-empty line may begin with
`%PDF-`; it is skipped, and the next non-empty line must then equal the header
exactly. Any other arrangement stays `Unsupported`. Decoding stays strict
UTF-8, so JPEG and PNG sources stay `Unsupported`. No PDF structure is parsed.
`IntakeUploadValidator` and `Production/*` are untouched.

`scripts/local/New-LocalDemoSamples.ps1` writes `t4-sample.pdf` and
`medical-sample.pdf` into `artifacts/local-demo/` (already git-ignored). The T4
sample prints 2025, "Synthetic" names, Box 14, Box 45, and a Box 12 value built
at run time from random digits; the script holds no nine-digit literal and
prints no value. Which box produces a line entry is read from the existing
`T4LineMap.2025.json` by the implementer; this plan asserts no box-to-line
fact. `Start-LocalDemo.ps1` calls the generator when the samples are absent.

### 5. Case list, navigation, feedback (T03)

**List read.** One member is added to `IIntakeStore`:

```
Task<IntakeCaseList> ListCasesAsync(
    TrustedFirmId firm, int maximumCases, CancellationToken cancellationToken);
```

`SqlIntakeStore` filters by `FirmId` first and orders by the instant of the
case's stored `CaseCreated` audit event (newest first, `CaseId` as tie-break),
reading `maximumCases + 1` rows to compute `HasMore`. **The intake case row has
no creation column**, so the PRD's "stored creation instant" is the existing
audit instant; no column, index, or migration is added. (Approved.)

`IAccountantIntakeWorkflow` is not changed. `CaseListEndpoints` reads the store
directly, as `ExtractionEndpoints` already does for `FindAsync`.

**Route.** `CaseListEndpoints.cs` (a new file, so the byte-pinned
`IntakeEndpoints.cs` is untouched) maps `GET /intake-cases` under
`FirmScopedPolicy`, takes the firm only from
`PortalAuthorization.BootstrapStateFor`, binds no request value, uses the fixed
maximum `100`, applies the intake response headers, and returns
`CaseListPage`.

**Navigation.** `AccountantNavigation` (shared, parameterless, static SSR)
renders the case-list link, the create link, and the existing sign-out as an
antiforgery form. It is placed inside accountant pages, never in
`PortalLayout`, which client pages share. It is added to `CaseListPage`,
`ExtractionListPage`, `ExtractionReviewPage`, and `IntakeCaseProgressPage`
(the last by a line-exact approved diff that also adds the link to the case's
documents page). `ReviewPage`, the root shell, and the create and issued pages
stay byte-identical; new styles live in component-scoped `.razor.css` because
`wwwroot/intake.css` is byte-pinned.

**Feedback (FR-42, approved narrowly).** Only `CorrectAsync` and
`FinalizeAsync` change. Both routes already run the scope-first endpoint
filter, so a missing, foreign, or inconsistent selector has returned the empty
`404` before the handler runs. Then:

| Workflow result | Response |
|---|---|
| `Success` | unchanged redirect |
| `NotFound` | unchanged empty `404` |
| `Invalid` | the page re-rendered with the fixed notice, status `422` |
| `Conflict` | the page re-rendered with the fixed notice, status `409` |

The correction re-renders `ExtractionReviewPage`; the finalization re-renders
`ExtractionListPage`. The notice is a closed enum parameter
(`WorkflowNoticeKind`: `None`, `CorrectionRefused`, `CorrectionConflict`,
`FinalizationRefused`, `FinalizationConflict`) rendered by `WorkflowNotice`
from four constant texts. **No query, form, or header value selects a notice**,
so nothing caller-supplied can be reflected and no request-input allowlist
changes. Extract, classification, accept, and duplicate routes keep their
current responses. A refused operation still mutates nothing.

## Data model

No tax model, mapping file, DTO in the engine, or JSON mapping changes.
Mappings remain data-driven; no CRA line number appears in any new C#,
PowerShell, or Razor file.

New types:

| Type | Project | Shape |
|---|---|---|
| `IntakeCaseListEntry` | `ClientIntake.Models` | `CaseId`, `PrimaryTaxpayerDisplayName`, `TaxYear`; identifier and name `[JsonIgnore]`; `ToString` emits no taxpayer text |
| `IntakeCaseList` | `ClientIntake.Models` | `IReadOnlyList<IntakeCaseListEntry> Cases`, `bool HasMore` |
| `DevelopmentFixtureIdentity` | `Persistence` | two string constants |
| `CaseListPageModel`, `CaseListRowModel` | portal `Components.Pages` | exactly display name, tax year, and case selector (AC-59) |
| `WorkflowNoticeKind` | portal `Components.Pages` | closed enum, five members |

No table, column, index, or migration. Two seeded rows (Development only).

## Services & public API

| Member | Change | Rules that apply |
|---|---|---|
| `SqlConnectionStringGuard.RequireValid(string?, bool)` | new internal overload | default path byte-for-byte as strict; allowance needs loopback and explicit `Encrypt=True` |
| `PersistenceRegistration.AddTaxAiPersistence(…, string, bool)` | new public overload | existing overload unchanged; no environment read |
| `IDevelopmentFixtureSeeder.SeedAsync` | signature unchanged; documentation records the one Development user and membership | no credential; Development only |
| `IIntakeStore.ListCasesAsync` | new member | `TrustedFirmId` first, no omitting overload; the two firm-omitting lookups stay the only ones |
| `DeterministicDocumentIntelligenceProvider` | one tolerated leading line | never throws; copies values verbatim; decides nothing about tax |
| `DevelopmentSignInEndpoints`, `CaseListEndpoints` | new internal static classes | firm only from the revalidated session; no request input |
| `ExtractionEndpoints.CorrectAsync`, `FinalizeAsync` | refusal branch only | scope first; closed notices; no value interpolation |

Domain rules: tax year stays the server-stored `StoredIntakeCase.TaxYear`
(2025 is the only supported year; no new year authority); amounts stay strings
until the engine parses them; rounding stays `MoneyRounding`; no SIN in any
form reaches a structured surface, and Box 12 lives only in the generated
untracked sample and in process memory for one extraction call.

## Wave shape

Four tasks in four sequential waves. The human's explicit four-task
decomposition overrides the generic contract-only Wave 0 pattern (precedent:
spec 016). The tasks share composition and guard files, so parallelism would
be manufactured; each task declares a contract together with its working
consumer. Wave 4 is the end-to-end verification wave.

## Trade-offs

1. **Development sign-in through the cookie sign-in event vs. a local OIDC
   container.** A container exercises the real OIDC handler but adds a runtime
   dependency, a second certificate, and client configuration. Rejected by
   decision D-1. The chosen route reuses the one place a local principal is
   built, so nothing about authorization is parallel.
2. **Provider tolerance vs. a new upload media kind.** Accepting a text kind at
   the upload gate would have changed spec 014's signature gate. Rejected by
   D-2. The tolerance lives only in the Development provider.
3. **Environment variables from the script vs. `UserSecretsId` in the project.**
   The property is one line and gives `dotnet run` parity, but makes test
   outcomes depend on a developer's machine and changes a pinned project
   property. Rejected; see "Settings storage".
4. **List member on `IIntakeStore` vs. a new single-method port.** A new port
   would break no existing test double but adds a registration, a lifetime
   entry, and a second store-shaped contract; the accepted assumption was an
   additive member. Chosen: the member, at the cost of five compile-forced
   test-double additions (authorized as X-6 in `tasks.md`).
5. **Notice by re-render vs. redirect with a closed query code.** A redirect
   keeps post-redirect-get, but adds a request input and its allowlist
   amendment, and a reflected-selector risk. Rejected. Re-render reads nothing
   from the caller; the cost is a non-redirect response to a POST.
6. **Ordering by the audit instant vs. a new creation column.** A column needs
   a migration and its guards. Rejected for a bounded list of 100.
7. **Additive persistence overload vs. changing the existing signature.** The
   baseline contract test requires every baseline signature to survive, so the
   existing method cannot change shape.

## Risks

| Risk | Mitigation |
|---|---|
| Development sign-in reachable outside Development | mapped under an ordinal `Development` check; AC-19 to AC-23 inventory and indistinguishability tests in Production and Staging; no seeding outside Development |
| Certificate bypass leaking to a deployable host | strict default; bool opt-in from one ordinal comparison; loopback-only; `PortalStartupGuards` and the production validator untouched; AC-29 to AC-34 |
| Bridge mistaken for PDF support | exact two-line rule; `Unsupported` otherwise; production adapter directory byte-identical (AC-44); guide states samples are not viewable PDFs |
| SIN-shaped value tracked or printed | generated at run time into an ignored directory; AC-51, AC-52, AC-55; tooling output scan (AC-10); end-to-end confinement (AC-56) |
| Cross-firm leak through the list | firm-leading query; firm only from session state; AC-60 to AC-62; relational test on the real store |
| Refusal notice becoming an oracle | scope-first filter unchanged; `NotFound` stays `404`; notices constant and selected only by operation and result code |
| Guard amendments weakening unrelated guarantees | each amendment re-pins to an exact set or line-exact diff; test method names kept; unlisted failures are stop-and-ask |
| Guards outside the approved table | X-1 to X-7 authorized narrowly in `tasks.md`; anything else is stop-and-ask |
| LocalDB behaviour differs by machine | `-ConnectionString` accepts any loopback SQL Server; T04 records one real run in `verification.md` |
| Accidental tax change | no tax file in any task list; AC-85, AC-86, AC-88; full gate each task |

## Tax behaviour unchanged

No parser, mapper, mapping file, rounding helper, tax-year authority, review
assembler, or public tax contract changes. The review output shown at the end
of the demo is produced only by the existing chain, and AC-88 compares it with
a direct invocation of that chain. `pwsh ./scripts/Invoke-SolutionTests.ps1`
passes after every task with no floor lowered.
