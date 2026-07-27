# Tasks: Portal Foundation (spec 010)

**Feature class: `platform`** (repository structure, hosting, composition, UI
foundation). **Tax/CRA semantic impact: none.** Implements the approved
[prd.md](prd.md) through the structure fixed in [architecture.md](architecture.md).

## Binding execution rules

- This file is **read-only once approved**. Never edit it and never check a box.
- One explicitly named TaskID per branch, per PR, per review. A human merges each
  wave to `master` before the next wave begins. **Agents never merge.**
- Every task may create or modify **only** the exact files in its declared file
  list, including its own `.done` marker. Files outside the list must not change.
- **`src/TaxLogicEngine/`, `tests/TaxLogicEngine.Tests/`, `src/TaxCases/`,
  `tests/TaxCases.Tests/`, `src/DocumentExtraction/`,
  `tests/DocumentExtraction.Tests/`, `src/CaseProcessing/`, and
  `tests/CaseProcessing.Tests/` are untouchable in every task** — source, tests,
  project files, `CLAUDE.md`, and `AGENTS.md` alike. If a task appears to need an
  edit there, the design has drifted outside the PRD (FR-16); stop and escalate
  rather than relaxing an existing assertion.
- Tests ship **with** each task. No existing test may be removed, skipped,
  renamed, weakened, or rewritten to make a task pass.
- Before completing **every** task, run `dotnet build TaxAI.sln --no-incremental`
  and `dotnet test TaxAI.sln`. The build must be warning-free for the new
  projects and the full solution suite must pass. Use `--no-incremental` — an
  incremental build hid a warning during spec 007 T02.
- A marker is `specs/010-portal-foundation/status/<TaskID>.done` containing the PR
  number and commit SHA. Every task in every earlier wave must have its marker
  before a later wave starts.
- **Never assert a total project count for `TaxAI.sln`** — in a test, a doc, or
  the README. Assert presence-and-uniqueness at approved paths. A total count
  blocked spec 007's Wave 0 outright and required PR #66 to unpick; root
  `CLAUDE.md` forbids it.
- **The portal computes no tax and holds no PII.** No CRA line number, box code,
  box→line table, tax-year literal, taxpayer type, SIN, masking helper, or
  arithmetic token may enter `src/TaxAI.Portal/`. The v1 PII posture is
  **omission** — the data never enters the process.
- **No `PackageReference` in the portal** (AC-3). **No `InternalsVisibleTo`
  anywhere**, including on the new projects (AC-16).
- **Purely additive.** Nothing moves, nothing is renamed. `TaxAI.sln` is edited
  by **appending only**.

### Source-scanning rules (T04, T05, and any scan)

Scan **executable lines only** — strip `///`, `//`, `*`, `/*` lines. The portal's
own documentation legitimately names forbidden concepts while explaining why they
are forbidden, and scanning raw text would make honest comments unwritable. Reuse
the proven helper shape from
`tests/CaseProcessing.Tests/Boundaries/CaseProcessingBoundaryTests.cs`
(`FindRepositoryRoot`, `HandwrittenSourceFiles`, `ExecutableLines`,
`ProjectReferences`) by **re-implementing it in the portal test project** — do
not reference, move, or modify the `CaseProcessing.Tests` file.

Every scanning task must include a **scanner guard**: assert the walk saw exactly
the expected production file set and that a known-present pattern still matches.
Without it, a broken walk makes every negative assertion pass for free.

### Framework-assembly filter (fixed once, here)

A web host references dozens of shared-framework assemblies, so the
`CaseProcessing` filter cannot be copied verbatim. When asserting the portal's
compiled references, treat a name as framework if it equals `netstandard`, starts
with `System`, or starts with `Microsoft.`; assert the remainder is exactly
`{ CaseProcessing, TaxLogicEngine }` (AC-40). A transitive `DocumentExtraction`
leak must fail this assertion.

---

## Wave 0 — repository/architecture contract

- [ ] **T01** — Scaffold both projects, fix the response literals, declare the
  composition-root signature, and write the portal's instruction files — files:
  `TaxAI.sln`, `src/TaxAI.Portal/TaxAI.Portal.csproj`,
  `src/TaxAI.Portal/Program.cs`, `src/TaxAI.Portal/ModuleRegistration.cs`,
  `src/TaxAI.Portal/PortalContent.cs`, `src/TaxAI.Portal/appsettings.json`,
  `src/TaxAI.Portal/Properties/launchSettings.json`,
  `src/TaxAI.Portal/CLAUDE.md`, `src/TaxAI.Portal/AGENTS.md`,
  `tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj`,
  `tests/TaxAI.Portal.Tests/ProjectStructureTests.cs`,
  `specs/010-portal-foundation/status/T01.done` — depends on: none

  Create `src/TaxAI.Portal/` with `Microsoft.NET.Sdk.Web`, net8.0, nullable and
  implicit usings enabled, **zero `PackageReference`**, and exactly the two
  production references `../CaseProcessing/CaseProcessing.csproj` and
  `../TaxLogicEngine/TaxLogicEngine.csproj`. Create
  `tests/TaxAI.Portal.Tests/` with the repository's standard xUnit tooling
  (xunit 2.5.3, xunit.runner.visualstudio 2.5.3, Microsoft.NET.Test.Sdk 17.8.0,
  coverlet.collector 6.0.0) plus `Microsoft.AspNetCore.Mvc.Testing` 8.0.0,
  referencing only the portal. Append both projects to `TaxAI.sln` with fresh
  GUIDs, the existing C# project-type GUID, and configuration rows for all six
  configurations — **appending only**.

  `PortalContent.cs` (`internal static`) holds the two response literals **exactly
  as fixed in architecture.md** — `Healthy` and the shell HTML document — plus
  their content types. These constants are this wave's contract: T02 and T03 map
  them and may not invent bodies. `ModuleRegistration.cs` (`internal static`)
  declares `AddTaxAiModules(this IServiceCollection services)` with a
  `NotImplementedException` body. `Program.cs` is a host that builds and starts
  with **no registration and no route**, sets `AddServerHeader = false`, sets
  `ValidateOnBuild`/`ValidateScopes` unconditionally via
  `UseDefaultServiceProvider`, and ends with `public partial class Program { }`
  for testability. It **does not call `AddTaxAiModules` yet** — an unreachable
  `NotImplementedException` in a composition root is invisible to `dotnet build`
  and would leave `master` with a host that throws on startup.

  Write `src/TaxAI.Portal/CLAUDE.md` and the pointer-only `AGENTS.md`
  **before** any other portal content, so every later task is bound by them.
  `appsettings.json` carries logging defaults only; `launchSettings.json` declares
  a localhost-only HTTP profile with no secret.

  **Tests** (`ProjectStructureTests.cs`) — AC-1 (web SDK), AC-2 (the three
  properties), AC-3 (zero `PackageReference`), AC-4 (the four pinned test
  versions), AC-5 (each new project declared exactly once at its approved path —
  **presence-and-uniqueness, never a total count**), AC-8 (production references
  are exactly the two), AC-9 (test project references only the portal), AC-44
  (portal `CLAUDE.md` states: inward dependencies naming `TaxLogicEngine` and
  `CaseProcessing`; UI computes no tax and implements no CRA mapping; no tax-year
  policy; no taxpayer data or PII; no endpoint accepts input; and the FR-18 gate
  naming the Class A questions and the `tax-domain` reclassification), AC-45
  (`AGENTS.md` is fewer than 15 lines and links `../../AGENTS.md`,
  `../../CLAUDE.md`, and the local `CLAUDE.md`). Also assert the portal's only
  public type is `Program` and that it declares no public member of its own.

  **Mechanical acceptance:** `dotnet build TaxAI.sln --no-incremental` succeeds
  warning-free; `dotnet test TaxAI.sln` passes with every pre-existing test
  green; no file outside the declared list changed; no `InternalsVisibleTo` was
  added.

---

## Wave 1 — composition root and the health endpoint

- [ ] **T02** — Implement the composition root, force provider validation, and map
  `GET /health` — files: `src/TaxAI.Portal/ModuleRegistration.cs`,
  `src/TaxAI.Portal/Program.cs`,
  `tests/TaxAI.Portal.Tests/CompositionRootTests.cs`,
  `tests/TaxAI.Portal.Tests/HealthEndpointTests.cs`,
  `specs/010-portal-foundation/status/T02.done` — depends on: T01

  Implement `AddTaxAiModules` as the **only** file in the portal containing
  `AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*`: register
  `IT4LineMapProvider` → `EmbeddedT4LineMapProvider`, `IT5LineMapProvider` →
  `EmbeddedT5LineMapProvider`, and the concrete `T4CaseProcessor`, all singleton.
  Register nothing else — in particular **not** `IDocumentExtractionIngress`
  (spec 007 ships that signature deliberately without an implementation) and
  **not** `T4LineMapper`/`T5LineMapper` (they are constructed inside
  `CaseProcessing`, not resolved by the host). Call the extension from
  `Program.cs` and map `GET /health` to `PortalContent`'s literal. No endpoint
  resolves or invokes `T4CaseProcessor`.

  **Tests** — AC-17 (the host starts through the in-process test host without
  throwing and without binding a network port), AC-19 (`IT4LineMapProvider`
  resolves to an `EmbeddedT4LineMapProvider`), AC-20 (the T5 equivalent), AC-21
  (`T4CaseProcessor` resolves), AC-23 (`GET /health` → 200 and a body equal to
  the literal `Healthy`, written independently in the test, with the plain-text
  content type), AC-24 (`IDocumentExtractionIngress` is **not** registered — a
  null-returning lookup returns null), AC-29 (with the Production environment, an
  unmatched path returns 404 and a body containing no stack trace, exception type
  name, or filesystem path).

  AC-18 must be proved **behaviourally, twice**, because
  `WebApplication.CreateBuilder` enables these flags in Development only and the
  test host runs as Production by default: a **test-only** scoped probe service
  resolved from the root provider must throw (proves `ValidateScopes`), and a
  **test-only** registration with an unresolvable dependency must make host
  startup throw (proves `ValidateOnBuild`). Add both probes from the test project
  only; neither may enter `ModuleRegistration`. Back them with a source assertion
  that both flags are set outside any `IsDevelopment()` branch.

  **Mechanical acceptance:** AC-17–AC-21, AC-23, AC-24, AC-29 pass; the portal
  declares exactly three registrations; task tests and `dotnet test TaxAI.sln`
  pass.

---

## Wave 2 — the static shell page

- [ ] **T03** — Map `GET /` to the fixed shell document — files:
  `src/TaxAI.Portal/Program.cs`,
  `tests/TaxAI.Portal.Tests/ShellPageTests.cs`,
  `specs/010-portal-foundation/status/T03.done` — depends on: T01, T02

  Map `GET /` returning `PortalContent`'s shell HTML with
  `text/html; charset=utf-8`. Server-rendered constant content only: no view
  engine, no static-file middleware, no `wwwroot`, no external stylesheet,
  script, font, or image, no client-side fetch, and no `package.json` or npm
  artifact anywhere in the repository.

  **Tests** — AC-25 (`GET /` → 200, HTML content type, body **equal** to the
  expected document written independently in the test — no dynamic value
  anywhere), AC-26 (resolve `EndpointDataSource` from the started host and
  enumerate: **exactly two** endpoints, both GET, patterns `/` and `/health`,
  neither containing a route parameter). Create the client before enumerating —
  endpoints materialize when the app starts. Also assert the response references
  no external URL.

  **Mechanical acceptance:** AC-25 and AC-26 pass; task tests and
  `dotnet test TaxAI.sln` pass.

---

## Wave 3 — proving the boundaries (three parallel tasks)

T04, T05, and T06 may run in parallel after T03. They share no file path and
reference no type created by a sibling; each reads only portal production types
and the repository tree. **None of them may modify production code** — a failing
assertion here means the design is wrong, and the fix belongs in a re-opened
earlier task, not in a weakened assertion.

- [ ] **T04** — Enforce dependency direction and the line the portal may not cross
  — files: `tests/TaxAI.Portal.Tests/Boundaries/PortalBoundaryTests.cs`,
  `specs/010-portal-foundation/status/T04.done` — depends on: T01, T02, T03

  **Tests** — AC-10 (no reverse reference: every pre-existing `.csproj` under
  `src/` and `tests/`, and every handwritten `.cs` file in those trees, contains
  no `TaxAI.Portal`), AC-40 (compiled non-framework references are exactly
  `CaseProcessing` and `TaxLogicEngine`, using the framework filter fixed above —
  a transitive `DocumentExtraction` leak must fail), AC-22 (every
  `AddSingleton`/`AddScoped`/`AddTransient`/`TryAdd*` match in the portal is in
  `ModuleRegistration.cs`), AC-31 (no `T4SlipParser`, `T5SlipParser`,
  `T4LineMapper`, `T5LineMapper`, `T4SlipBoxAmounts`, `T5SlipBoxAmounts`,
  `MoneyRounding`, `Math.Round`, `MidpointRounding`, `double`, `float`,
  `decimal`), AC-32 (no `T4Slip`, `T5Slip`, `T4RawInput`, `T5RawInput`,
  `TaxLineEntry`, `T4MappingResult`, `T5MappingResult`, `RecipientIdentifier`,
  `ValidatedExtraction`, `ExtractionEnvelope`, `ProcessedT4Extraction`,
  `TaxCase`), AC-33 (zero five-digit integer literals), AC-34 (no box→line
  dictionary, no box-code list, no `TaxLogicEngine.Mappings` resource name, no
  `GetManifestResourceStream`), AC-35 (no four-digit year literal and no
  supported-tax-year type or policy).

  Include the **scanner guard** (the walk sees exactly `Program.cs`,
  `ModuleRegistration.cs`, `PortalContent.cs`; a known-present pattern such as
  `AddTaxAiModules` still matches) and a **comment-filter guard** proving the
  filter removes documentation without removing code.

  **Mechanical acceptance:** AC-10, AC-22, AC-31–AC-35, AC-40 pass; no production
  file changed.

- [ ] **T05** — Enforce the PII, input, and security posture — files:
  `tests/TaxAI.Portal.Tests/Security/PortalSecurityTests.cs`,
  `specs/010-portal-foundation/status/T05.done` — depends on: T01, T02, T03

  **Tests** — AC-27 (no `[FromBody]`, `[FromQuery]`, `[FromRoute]`, `[FromForm]`,
  `[FromHeader]`, `Request.Body`, `Request.Query`, `Request.Form`,
  `Request.Headers`, `Request.Cookies`, or `IFormFile`), AC-28 (the health body
  discloses no version, environment name, machine name, user name, path,
  timestamp, or configuration value), AC-30 (any developer-exception-page call is
  inside an `IsDevelopment()` branch, or absent entirely), AC-36 (no SIN-related
  identifier, no masking/redaction helper, no `SafeDiagnosticText`, no
  nine-consecutive-digit literal), AC-37 (read the live bodies of `GET /` and
  `GET /health`: neither contains a nine-digit run and both equal their fixed
  expected content), AC-38 (no `AddHttpLogging`/`UseHttpLogging` and no log
  statement writing a request path, query string, header, cookie, or body),
  AC-39 (no `AddAuthentication`, `AddAuthorization`, `UseAuthentication`,
  `UseAuthorization`, `[Authorize]`, `ClaimsPrincipal`, or cookie/session
  configuration), AC-41 (every tracked configuration and launch-profile file
  under the portal contains no connection string, password, key, token, or
  certificate, and any declared URL binds to localhost only), AC-42 (no
  `package.json`, no `node_modules`, no SPA framework asset, no tracked minified
  bundle anywhere in the repository).

  Also assert the source-level posture the wire cannot show: `AddServerHeader =
  false` is set (`TestServer` does not use Kestrel), and the portal names no
  `DbContext`, `SqlConnection`, `EntityFrameworkCore`, `System.Data`,
  `HttpClient`, OCR/LLM/email/cloud SDK token, `DateTime.Now`/`UtcNow`, or
  background-service registration. Include a scanner guard.

  **Mechanical acceptance:** AC-27, AC-28, AC-30, AC-36–AC-39, AC-41, AC-42 pass;
  no production file changed.

- [ ] **T06** — Verify solution shape and existing-module regression — files:
  `tests/TaxAI.Portal.Tests/EndToEnd/SolutionAndRegressionTests.cs`,
  `specs/010-portal-foundation/status/T06.done` — depends on: T01, T02, T03

  **Tests** — AC-6 (every pre-existing `Project(` line and configuration row in
  `TaxAI.sln` is intact and the only additions are the two new projects' blocks
  and rows — assert the eight pre-existing declarations and their GUIDs are
  present unchanged, **without asserting a total count**), AC-12 (no speculative
  project: no `.csproj` exists for Clients, Firms, Documents, Intake, Review, or
  CaseReview), AC-14 (the set of exported public type full-names of
  `TaxLogicEngine`, `TaxCases`, `DocumentExtraction`, and `CaseProcessing` matches
  a snapshot recorded in this test file — a public-surface change fails loudly),
  AC-15 (the built `TaxLogicEngine` assembly's manifest resource names include
  `TaxLogicEngine.Mappings.T4LineMap.2025.json` and
  `TaxLogicEngine.Mappings.T5LineMap.2025.json` with exactly those strings),
  AC-16 (every pre-existing production project file declares only the two
  pre-existing `InternalsVisibleTo` grants; neither new project declares one),
  AC-50 (neither the portal's source nor `TaxAI.sln` names `CaseReview`,
  `AccountantReviewPackage`, `ReviewedSlip`, or `ReviewSignOff`, and no
  CaseReview project is declared).

  Also assert the four modules' project files still declare exactly their
  pre-existing reference sets, and that the three tax-domain projects still
  declare no `PackageReference`/`FrameworkReference`.

  **Mechanical acceptance:** AC-6, AC-12, AC-14–AC-16, AC-50 pass; no production
  file changed.

---

## Wave 4 — documentation and final verification

- [ ] **T07** — Update the repository's documentation and record AC-by-AC
  verification — files:
  `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`,
  `specs/010-portal-foundation/verification.md`,
  `README.md` *(modified only after AC-1–AC-50 all pass)*,
  `CLAUDE.md` *(modified only after AC-1–AC-50 all pass)*,
  `NEXT.md` *(modified only after AC-1–AC-50 all pass)*,
  `specs/010-portal-foundation/status/T07.done` —
  depends on: T01, T02, T03, T04, T05, T06

  Run every task suite and the whole solution, map AC-1 through AC-50 to exact
  test names or exact command evidence, and review the final diff for
  declared-file ownership, dependency direction, the no-tax-no-PII line, and
  hygiene.

  Update `README.md` to describe the repository as it actually is: list every
  project tree including `src/TaxAI.Portal/` and `tests/TaxAI.Portal.Tests/`,
  document `dotnet run --project src/TaxAI.Portal`, **delete the stale
  `Solution (two projects)` line and assert no project count at all**, and claim
  nothing the portal lacks — no authentication, user accounts, taxpayer-data
  screens, document upload, persistence, deployment, or OCR/LLM integration.
  Update root `CLAUDE.md`'s repository-layout and current-state sections, and
  `NEXT.md`. **No existing module's `CLAUDE.md` or `AGENTS.md` may be edited.**

  **Tests** — AC-46 (no `CLAUDE.md` or `AGENTS.md` under the four existing module
  trees is in the changed-file list), AC-47 (README lists the portal project and
  its run command, documents every existing project tree, no longer contains
  `two projects`, and asserts no project count), AC-48 (README claims no
  authentication, user account, taxpayer-data screen, document upload,
  persistence, deployment, or OCR/LLM capability), AC-49 (root `CLAUDE.md` lists
  both new trees and records spec 010's delivered scope).

  **Command evidence recorded in `verification.md`** — AC-7 (`dotnet build
  TaxAI.sln --no-incremental` exits 0), AC-11 (`git diff --name-only` against the
  merge base lists no path under the four existing module `src/` trees or their
  four test trees), AC-13 (`dotnet test TaxAI.sln` exits 0 with every pre-existing
  test green, none removed, skipped, or renamed — record the before/after test
  counts per project), AC-43 (`git ls-files` / `git status --porcelain` show no
  tracked `bin/`, `obj/`, `.vs/`, `*.user`, secret, certificate, or
  `.claude/settings.local.json`). These four are verified by recorded command
  output rather than by a test that shells out to `git`. Also record that this
  repository has **no CI workflow**, so no CI command required updating, and the
  pinned `Microsoft.AspNetCore.Mvc.Testing` version actually restored.

  **Only after every AC passes**, update the three state documents and create
  `T07.done`. If any AC fails, `verification.md` records the failure, the state
  documents stay unchanged, no marker is created, and the task stops.

  **Mechanical acceptance:** `verification.md` carries a pass/fail row for every
  AC-1 through AC-50 with exact test-name or command evidence;
  `dotnet build TaxAI.sln --no-incremental` and `dotnet test TaxAI.sln` both pass
  with every pre-existing test green; the diff shows no change under any of the
  eight pre-existing project trees; every task changed only its declared files.

---

## Acceptance-criterion ownership

| PRD criteria | Owning task |
|---|---|
| AC-1, AC-2, AC-3, AC-4, AC-5, AC-8, AC-9, AC-44, AC-45 | T01 |
| AC-17, AC-18, AC-19, AC-20, AC-21, AC-23, AC-24, AC-29 | T02 |
| AC-25, AC-26 | T03 |
| AC-10, AC-22, AC-31, AC-32, AC-33, AC-34, AC-35, AC-40 | T04 |
| AC-27, AC-28, AC-30, AC-36, AC-37, AC-38, AC-39, AC-41, AC-42 | T05 |
| AC-6, AC-12, AC-14, AC-15, AC-16, AC-50 | T06 |
| AC-7, AC-11, AC-13, AC-43, AC-46, AC-47, AC-48, AC-49 + full AC-1–AC-50 traceability | T07 |

## Negative-assertion ownership

| Rule that must fail closed | Required negative owner |
|---|---|
| Container mis-wiring caught at startup, not on first request (scope + build validation) | T02 |
| `IDocumentExtractionIngress` not registered; unmatched route 404 with no diagnostic detail | T02 |
| No third endpoint; no route parameter | T03 |
| No reverse reference to the portal; no forbidden non-framework assembly; registrations outside the composition root | T04 |
| No tax computation token, taxpayer type, CRA line literal, mapping table or resource read, tax-year literal | T04 |
| No input-binding surface; no PII token or nine-digit run in source or on the wire; no auth/session/logging/infrastructure surface; no secret; no non-local binding; no client-side stack | T05 |
| No altered pre-existing solution entry; no speculative project; no changed public tax surface; no renamed embedded resource; no new internals grant; no spec-009 coupling | T06 |
| No changed file under any pre-existing module tree; no changed existing module instruction file; no README claim the portal cannot back; no tracked build output or secret | T07 |

**7 tasks across 5 waves.** Wave 3 runs three tasks in parallel (T04, T05, T06)
with disjoint file ownership. Every sibling pair shares no file path and
references no sibling-created type.
