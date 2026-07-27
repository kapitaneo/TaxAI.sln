# Architecture: Portal Foundation (spec 010)

**Feature class: `platform`** (repository structure, hosting, composition, and
UI foundation). Implements the approved [prd.md](prd.md).

**Tax/CRA semantic impact: none.** No tax mapping, calculation, parser
behaviour, or public tax contract changes. See *Tax behaviour unchanged*.

This document fixes every decision the PRD delegated to `/plan`: the
server-rendering mechanism, the exact response literals, the file set, the
registration lifetimes, and the mechanical shape of each verification. Where a
decision is mine rather than the PRD's, it is tagged `[PLAN]` and justified.

---

## Current structure

Read directly from the tree at `D:\Projects\TaxAI` (branch
`spec/009-accountant-review-package`).

`TaxAI.sln` — Visual Studio format 12.00, `VisualStudioVersion = 17.9.34607.119`,
solution GUID `{3140F723-B2E4-4D9E-967B-26F6E57CCD9E}`. Six solution
configurations (`Debug|Release` × `Any CPU|x64|x86`). Eight project declarations,
each once, all with the C# project-type GUID
`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`:

| Project | Path | SDK | Project GUID |
|---|---|---|---|
| `TaxLogicEngine` | `src\TaxLogicEngine\TaxLogicEngine.csproj` | `Microsoft.NET.Sdk` | `{0390423C-…A5C5}` |
| `TaxLogicEngine.Tests` | `tests\TaxLogicEngine.Tests\…` | `Microsoft.NET.Sdk` | `{2F692EB7-…0076}` |
| `TaxCases` | `src\TaxCases\TaxCases.csproj` | `Microsoft.NET.Sdk` | `{A5A7F602-…B1C1}` |
| `TaxCases.Tests` | `tests\TaxCases.Tests\…` | `Microsoft.NET.Sdk` | `{C2D431A4-…4184}` |
| `DocumentExtraction` | `src\DocumentExtraction\DocumentExtraction.csproj` | `Microsoft.NET.Sdk` | `{63E522D5-…AEE4}` |
| `DocumentExtraction.Tests` | `tests\DocumentExtraction.Tests\…` | `Microsoft.NET.Sdk` | `{CFBDCA0E-…E24A}` |
| `CaseProcessing` | `src\CaseProcessing\CaseProcessing.csproj` | `Microsoft.NET.Sdk` | `{7B1C0A3E-…8B70}` |
| `CaseProcessing.Tests` | `tests\CaseProcessing.Tests\…` | `Microsoft.NET.Sdk` | `{1D8E4F92-…60C3}` |

*(A description of today's tree. **No artifact of this feature asserts a total
project count** — root `CLAUDE.md` forbids it; a total count blocked spec 007's
Wave 0 and required PR #66 to unpick.)*

Every production project targets `net8.0` with `ImplicitUsings` and `Nullable`
enabled. Every test project adds `IsPackable=false`, `IsTestProject=true`,
`<Using Include="Xunit" />`, and pins `coverlet.collector` 6.0.0,
`Microsoft.NET.Test.Sdk` 17.8.0, `xunit` 2.5.3, `xunit.runner.visualstudio`
2.5.3.

Production reference graph today (all one-way, inward):

```
CaseProcessing ──► DocumentExtraction
CaseProcessing ──► TaxLogicEngine
TaxCases          (no production project reference)
DocumentExtraction (no production project reference)
TaxLogicEngine     (no production project reference; two EmbeddedResource items)
```

**Nothing executable exists.** There is no host, no `Program`, no
`IServiceCollection` registration, and no `Main`. `dotnet run` has no target.
The only occurrences of `Microsoft.AspNetCore`, `WebApplication`, or
`IServiceCollection` in the tree are *forbidden-pattern strings inside boundary
tests* (e.g. `CaseProcessingBoundaryTests.ForbiddenProductionPatterns`).

Types this feature composes, verified by reading them:

- `TaxLogicEngine.Services.IT4LineMapProvider` → `EmbeddedT4LineMapProvider`
  (`sealed`, implicit parameterless constructor, one method
  `ParseResult<T4LineMap> TryGetForYear(int taxYear)`).
- `TaxLogicEngine.Services.IT5LineMapProvider` → `EmbeddedT5LineMapProvider`
  (same shape, `ParseResult<T5LineMap>`).
- `CaseProcessing.Services.T4CaseProcessor` — `sealed`, one public constructor
  `T4CaseProcessor(IT4LineMapProvider lineMapProvider)`, one public method
  `ParseResult<ProcessedT4Extraction> Process(ValidatedExtraction? extraction)`.

All three are stateless after construction.

---

## Target structure

Two new sibling projects, purely additive:

```plaintext
src/TaxAI.Portal/                    ASP.NET Core web host (Microsoft.NET.Sdk.Web, net8.0)
  Program.cs                         host builder, provider validation, two routes, public partial Program
  ModuleRegistration.cs              THE composition root — the only file with Add* registrations
  PortalContent.cs                   the fixed response literals (internal const strings)
  appsettings.json                   logging defaults only; no secret, no connection string
  Properties/launchSettings.json     localhost-only development profile
  CLAUDE.md                          portal boundary + the FR-18 gate
  AGENTS.md                          pointer only (<15 lines)

tests/TaxAI.Portal.Tests/            xUnit (Microsoft.NET.Sdk, net8.0)
  ProjectStructureTests.cs           T01 — project/solution/reference/instruction-file shape
  CompositionRootTests.cs            T02 — container contents, validation flags, ingress absence
  HealthEndpointTests.cs             T02 — GET /health, unmatched route in Production
  ShellPageTests.cs                  T03 — GET /, endpoint enumeration
  Boundaries/PortalBoundaryTests.cs  T04 — dependency direction + "the line it may not cross"
  Security/PortalSecurityTests.cs    T05 — PII/security posture, input-binding absence
  EndToEnd/SolutionAndRegressionTests.cs   T06 — solution shape + existing-module regression
  EndToEnd/RepositoryVerificationTests.cs  T07 — documentation + final traceability
```

Resulting reference graph (added arrows only):

```
TaxAI.Portal ──► CaseProcessing ──► DocumentExtraction
TaxAI.Portal ──► TaxLogicEngine  ◄──┘
TaxAI.Portal.Tests ──► TaxAI.Portal
```

Nothing points back. No module gains a reference, a package, or a file.

### Namespace, assembly, and public surface

- Assembly / root namespace: `TaxAI.Portal`.
- `ModuleRegistration` and `PortalContent` are **`internal static`**.
- The portal's **only public type is `Program`** (`public partial class Program { }`
  appended to the top-level-statement file), and it declares no public member of
  its own. It is public **solely** so `WebApplicationFactory<Program>` can find
  the entry point.
- **No `InternalsVisibleTo` is added anywhere, including on the portal** `[PLAN]`.
  Tests assert response bodies against literals written independently in the test
  project, which is a stronger check than comparing a constant to itself, and it
  keeps "the only two internals grants in the repository are the pre-existing
  ones" true without qualification (AC-16).

### The two endpoints and their exact bodies

Fixed by this document so no downstream task invents them.

| Route | Method | Status | Content type | Body |
|---|---|---|---|---|
| `/health` | GET | 200 | `text/plain; charset=utf-8` | `Healthy` (exactly; no trailing newline) |
| `/` | GET | 200 | `text/html; charset=utf-8` | the shell document below, byte-for-byte |

```html
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>TaxAI</title>
</head>
<body>
<h1>TaxAI</h1>
<p>This application serves no taxpayer data.</p>
</body>
</html>
```

Both bodies are compile-time constants in `PortalContent`. Neither contains a
four-digit or five-digit numeric literal, a run of nine digits, a version, an
environment name, a machine or user name, a path, a timestamp, or any
configuration value (AC-13/28/33/35/36/37). The shell references no external
stylesheet, script, font, or image — there is nothing to fetch and no
client-side stack to introduce (AC-42).

---

## Module boundaries

| Module | Owns | May not |
|---|---|---|
| `TaxAI.Portal` | Process startup, the DI composition root, HTTP routing, and server-rendered markup. | Compute tax; name a CRA line, box code, or tax year; name a taxpayer type; hold PII; accept input; persist; authenticate. |
| `TaxLogicEngine` | Parsing, box→CRA-line mapping, rounding, SIN masking, year-scoped mapping config. | Know that a web host exists. |
| `CaseProcessing` | The deterministic T4 coordinator. | Know that a web host exists. |
| `TaxCases`, `DocumentExtraction` | Unchanged; not referenced by the portal. | — |

`src/TaxAI.Portal/CLAUDE.md` is where this is written down for future agents. It
must state, in its own words, each of: dependencies point inward and are exactly
`TaxLogicEngine` and `CaseProcessing`; the UI computes no tax and implements no
CRA mapping; the portal holds no tax-year policy; the portal handles no taxpayer
data and no PII; no endpoint accepts input; and **the FR-18 gate** — no endpoint,
page, component, or middleware that renders taxpayer data, accepts user input,
uploads a document, persists data, reads a session or identity, or crosses a
firm/case/participant boundary may be added until the PRD's Class A questions
(A–F) are answered in a separate specification, because adding one reclassifies
the work as `tax-domain`.

The content test asserts these substrings are present, case-insensitively:
`TaxLogicEngine`, `CaseProcessing`, `inward`, `no tax`, `CRA`, `tax-year`,
`taxpayer data`, `PII`, `input`, `Class A`, `tax-domain`.

---

## Dependency direction

```
        ┌──────────────────────────────────────────────┐
        │  TaxAI.Portal   (UI / API host)              │
        │  Program.cs · ModuleRegistration · content   │
        └───────────────┬──────────────────┬───────────┘
                        │                  │
                        ▼                  ▼
              CaseProcessing         TaxLogicEngine
                        │                  ▲
                        ├──────────────────┘
                        ▼
                 DocumentExtraction
```

Arrows that must never reverse:

1. **No module may reference `TaxAI.Portal`** — not in a `.csproj`, not in
   source, not transitively (AC-10, enforced by scanning every pre-existing
   `.csproj` and handwritten `.cs` file for the string `TaxAI.Portal`).
2. **No ASP.NET Core type may enter a tax-domain module.** Already enforced by
   each module's own forbidden-pattern scan; this feature adds nothing to those
   trees, so those scans keep passing untouched.
3. **The portal may not reach around a contract.** It resolves
   `IT4LineMapProvider`/`IT5LineMapProvider` as interfaces and constructs
   `T4CaseProcessor` only through the container.
4. **`DocumentExtraction` arrives transitively and stays unnamed.** The portal
   adds no direct reference and names no type from it — so its compiled assembly
   does not reference it at all (AC-40).

`TaxCases` is not referenced in either direction.

---

## Migration sequence

The PRD's five stages, each independently green
(`dotnet build TaxAI.sln --no-incremental` and `dotnet test TaxAI.sln` both
succeed) before the next begins, and each merged to `master` by a human.

1. **Scaffold** — create both projects, append two blocks to `TaxAI.sln`, add the
   two production project references and the one test reference, write
   `PortalContent` (the literals are the contract), declare
   `ModuleRegistration.AddTaxAiModules` with a `NotImplementedException` body,
   write `Program.cs` as a host that builds and starts with **no registration and
   no route**, and write the portal's `CLAUDE.md`/`AGENTS.md` **first**, so every
   later task is bound by them. → **T01**
2. **Compose and answer** — implement `AddTaxAiModules`, force provider
   validation on, call the extension from `Program.cs`, map `GET /health`. → **T02**
3. **Shell page** — map `GET /`. → **T03**
4. **Prove it** — boundary, security/PII, and regression suites, in parallel. →
   **T04 ‖ T05 ‖ T06**
5. **Record it** — `README.md`, root `CLAUDE.md`, `NEXT.md`, and the AC-by-AC
   `verification.md`. → **T07**

Stage 1 deliberately ships a host that starts and serves nothing rather than one
that throws at startup: an unreachable `NotImplementedException` in a composition
root is invisible to `dotnet build` and would put a broken-on-run `master`
between two merges. `Program.cs` does not call `AddTaxAiModules` until T02
implements it `[PLAN]`.

---

## Path and project-reference changes

**Nothing moves and nothing is renamed.** The complete set of path-level changes:

| Change | File | Nature |
|---|---|---|
| Add | `src/TaxAI.Portal/**` (7 files listed in *Target structure*) | new |
| Add | `tests/TaxAI.Portal.Tests/**` (csproj + 8 test files) | new |
| Append | `TaxAI.sln` | two `Project(…)`/`EndProject` blocks + their 12 configuration rows each |
| Edit | `README.md`, `CLAUDE.md`, `NEXT.md` | documentation only (T07) |
| Add | `specs/010-portal-foundation/verification.md`, `status/T01–T07.done` | spec artifacts |

New project references, and only these:

```xml
<!-- src/TaxAI.Portal/TaxAI.Portal.csproj -->
<ProjectReference Include="..\CaseProcessing\CaseProcessing.csproj" />
<ProjectReference Include="..\TaxLogicEngine\TaxLogicEngine.csproj" />

<!-- tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj -->
<ProjectReference Include="..\..\src\TaxAI.Portal\TaxAI.Portal.csproj" />
```

Solution edit rules: fresh GUIDs for the two new projects, the same project-type
GUID as every existing entry, blocks appended after the last `EndProject`,
configuration rows appended at the end of `ProjectConfigurationPlatforms`
covering all six configurations. **No existing GUID, ordering, or row changes**
(AC-6).

Project files:

```xml
<!-- src/TaxAI.Portal/TaxAI.Portal.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <!-- ProjectReference items above. Zero PackageReference items (AC-3): the
       web SDK's implicit Microsoft.AspNetCore.App framework reference is the
       whole dependency surface. -->
</Project>
```

The test project mirrors the four existing test projects exactly and adds one
package: `Microsoft.AspNetCore.Mvc.Testing` **8.0.0** `[PLAN]` — the in-process
web test host (`WebApplicationFactory`), which binds no network port. It is the
only new package in the repository, it lives in a test project only, and AC-4
pins the other four versions unchanged. If `8.0.0` cannot be restored in the
build environment, the implementer pins the lowest restorable `8.0.x`, records
the pinned version and the reason in `verification.md`, and changes nothing else.

---

## Backward-compatibility requirements

Unchanged, and asserted rather than assumed:

- **Assembly names and namespaces** of `TaxLogicEngine`, `TaxCases`,
  `DocumentExtraction`, `CaseProcessing`.
- **Every public type, member, and signature** of those four modules — the set of
  exported public type full-names per module is identical before and after
  (AC-14).
- **Embedded-resource logical names** `TaxLogicEngine.Mappings.T4LineMap.2025.json`
  and `TaxLogicEngine.Mappings.T5LineMap.2025.json`, enumerated off the built
  assembly (AC-15).
- **`InternalsVisibleTo` grants** — exactly the two that exist
  (`TaxLogicEngine`→`TaxLogicEngine.Tests`, `CaseProcessing`→`CaseProcessing.Tests`);
  none added (AC-16).
- **`MoneyRounding` half-up cents**, **`RecipientIdentifier` last-3-digit
  masking**, **2025-only year isolation and no adjacent-year fallback** — all
  unchanged and none invoked by the portal.
- **Every pre-existing test file and `.csproj` is byte-unchanged**, and every
  pre-existing test still runs and passes, none removed, skipped, or renamed
  (AC-11, AC-13, AC-43).

---

## Build and test command changes

Existing commands are unchanged and keep working:

```bash
dotnet build TaxAI.sln
```

```bash
dotnet test TaxAI.sln
```

New commands introduced by this feature:

```bash
dotnet run --project src/TaxAI.Portal
```

```bash
dotnet test tests/TaxAI.Portal.Tests --filter "FullyQualifiedName~TestMethodName"
```

Every task additionally runs `dotnet build TaxAI.sln --no-incremental` before
completion — an incremental build hid a warning during spec 007 T02.

Places that must be updated, and no others: `README.md` (project structure, run
command, and the stale `Solution (two projects)` line — replaced with a
**count-free** description), root `CLAUDE.md` (repository layout + current
state), `NEXT.md`. There is no CI workflow in this repository, so there is no CI
command to drift; that absence is itself recorded in `verification.md`.

---

## Trade-offs

**1. Minimal APIs returning fixed content, over Razor Pages.** `[PLAN]`

The PRD assumed "Razor Pages-style server rendering" and explicitly delegated the
mechanism to `/plan`. I chose two `MapGet` calls returning compile-time-constant
content. Razor Pages was rejected on three grounds: (a) a single `Pages/Index.cshtml`
registers **two** endpoints (`/` and `/Index`), which contradicts FR-11 and AC-26
("exactly two mapped endpoints") on day one — the constraint would have to be
weakened to accommodate the tooling, and the PRD is explicit that weakening an
assertion to fit a design means the design is wrong; (b) it pulls the MVC/Razor
service graph, a view-location convention, `_ViewImports`/`_Layout` files, and a
model-binding pipeline into a host whose entire job is to return two fixed
strings — model binding is the exact surface FR-12 and AC-27 forbid; (c) the
rendered output stops being a byte-for-byte constant, so AC-25 and AC-37 weaken
from equality to "contains". The cost of this choice is real: the first
data-bearing screen will add a view engine. That is the correct spec to make
that decision in, with the Class A answers in hand — and adding Razor Pages later
is additive, whereas removing a rendering pipeline that screens already depend on
is not.

**2. HTML as an internal constant, over `wwwroot/index.html` + `UseStaticFiles`.** `[PLAN]`

A static file is the conventional answer and was rejected: `UseStaticFiles`
serves *whatever is in the directory*, so the portal's response set stops being
enumerable and starts being a function of the filesystem — precisely the property
that makes a future upload path dangerous. It also adds `Content` items to the
project, introduces filesystem I/O into a host that otherwise has none, and
`UseDefaultFiles` would serve `/` through middleware rather than an endpoint,
making AC-26's enumeration blind to it. A constant is inspectable at compile
time, cannot be replaced at deploy time, and keeps "the portal serves exactly
two known byte sequences" mechanically true.

**3. One portal-owned composition root, over per-module `AddXxx()` extensions.** `[PLAN]`

The idiomatic .NET pattern is each module shipping its own
`IServiceCollection.AddModule()`. Rejected outright: it requires a
`Microsoft.Extensions.DependencyInjection.Abstractions` **PackageReference in
every tax-domain module**, which violates both root `CLAUDE.md` ("tax-domain code
must not depend on … cloud SDKs, frontend frameworks" and the inward-arrow rule)
and the PRD's migration constraint that no package reference may be added to any
pre-existing production project. Three of the four modules already assert
`PackageReference`-emptiness about themselves. Centralizing the wiring in the
host is what keeps the domain projects dependency-free — and it is also what
Problem 2 in the PRD asks for: one place, not several.

**4. Test-project `WebApplicationFactory`, over an integration host binding a port.** `[PLAN]`

Rejected binding a real port: it makes tests order-dependent and
environment-dependent (port contention on a developer machine or agent), and it
would put a listening socket in CI. `WebApplicationFactory` runs the full
pipeline in memory over `TestServer`, which satisfies AC-17's "without binding a
network port" as a property of the design rather than a hope.

---

## Risks and mitigations

| Risk | Why it matters here | Mitigation |
|---|---|---|
| **ASP.NET Core enters the repository and leaks inward.** | The single largest structural risk of this feature. A later agent adds `Microsoft.Extensions.*` to a module "just for logging" and the domain stops being provider-independent. | Each module's own forbidden-pattern scan already fails on `Microsoft.AspNetCore`; this feature touches none of those trees. AC-10 forbids any reverse reference. The portal's `CLAUDE.md` states the rule at the point of entry. |
| **The portal becomes a second mapping authority.** | "UI must not compute tax" is unenforceable prose until something binds it. | T04 scans the portal's executable lines for parser/mapper/rounding tokens, taxpayer types, five-digit literals, box→line structures, mapping-resource reads, and year literals (AC-31–35), with a **scanner guard** proving the walk saw every file and a known pattern still matches — so a broken walk cannot make every assertion pass for free. |
| **`ValidateOnBuild`/`ValidateScopes` silently off.** | `WebApplication.CreateBuilder` enables them **in Development only**; the in-process test host runs as **Production** by default, so a config-free host would ship unvalidated and nobody would notice. | `Program.cs` sets both unconditionally via `UseDefaultServiceProvider`, and T02 proves it **behaviourally**: a test-only scoped probe resolved from the root provider must throw, and a test-only broken registration must make host startup throw. A source scan alone would not have caught a flag set inside an `IsDevelopment()` branch. |
| **Framework-assembly filter false-negative in AC-40.** | The existing `CaseProcessing` filter excludes only `System*`/`netstandard`. A web host references dozens of `Microsoft.*` assemblies, so copying that filter verbatim would fail; loosening it carelessly could hide a real dependency. | The filter is specified once, here: exclude names equal to `netstandard`, starting with `System`, or starting with `Microsoft.` (shared framework); assert the remainder equals exactly `{ CaseProcessing, TaxLogicEngine }`. Any non-framework dependency, including a transitive `DocumentExtraction` leak, fails the assertion. |
| **Default endpoints appear as the framework evolves.** | An added default endpoint would silently widen the attack surface. | AC-26 enumerates `EndpointDataSource` and asserts exactly two GET endpoints with patterns `/` and `/health` and no route parameter. |
| **`Microsoft.AspNetCore.Mvc.Testing` restore/version drift.** | The repository has no lock file and no central package management. | Version pinned explicitly (8.0.0); a substitution is allowed only within `8.0.x` and must be recorded in `verification.md`. It is a test-only dependency and cannot reach production. |
| **Scope creep into `tax-domain` under time pressure.** | The very next portal feature hits all six Class A questions at once. | FR-18 gate, written into `src/TaxAI.Portal/CLAUDE.md` in T01 — before any endpoint exists — and asserted by AC-44. |
| **CI drift.** | Commands documented in one place and run in another go stale. | There is no CI workflow in this repository; `README.md` and root `CLAUDE.md` are the only places commands live, both updated in T07, and `verification.md` records that no CI configuration exists to update. |
| **Host-header / exposure posture.** | `AllowedHosts` defaults to `*`. | v1 is a localhost development host only: the tracked launch profile binds `http://localhost` only (AC-41), no deployment artifact ships, and hosting/TLS/exposure is PRD Open question **E**, explicitly out of scope. `appsettings.json` carries logging defaults only. |

---

## Rollback / recovery approach

- All work happens on task branches off `master`; a human merges each. No agent
  merges.
- The change is **purely additive**: two new directories, two appended solution
  blocks, three documentation edits. Nothing is moved, renamed, or rewritten in
  place, so there is no copy/delete window in which content can be lost.
- **Per-stage rollback** = revert that stage's merge commit. Earlier green stages
  are untouched, because each stage is independently green by construction.
- **Full rollback** = revert all seven merges, or delete `src/TaxAI.Portal/` and
  `tests/TaxAI.Portal.Tests/` and remove their two solution blocks. Because no
  module references the portal (AC-10), removal is a one-directional amputation:
  nothing else stops compiling.
- **Recovery check** after any revert: `dotnet build TaxAI.sln` and
  `dotnet test TaxAI.sln` both green with the same test set.

---

## Tax behaviour unchanged

I confirm, and this plan is designed so that each clause is mechanically checked
rather than asserted:

- **No tax mapping changes.** No JSON mapping config is added, edited, moved, or
  read by this feature. The embedded logical names are asserted intact (AC-15).
- **No calculation changes.** The portal performs no arithmetic and contains no
  `decimal`, `double`, `float`, `Math.Round`, `MidpointRounding`, or
  `MoneyRounding` token (AC-31). `MoneyRounding` is never invoked.
- **No parser behaviour changes.** `T4SlipParser`, `T5SlipParser`, `T4LineMapper`,
  and `T5LineMapper` are neither named nor invoked by the portal (AC-31);
  `T4CaseProcessor` is **registered but never resolved at request time** (FR-10),
  because no endpoint runs it.
- **No public tax contract changes.** The four modules' exported public type sets
  are identical before and after (AC-14); no file under their `src/` or `tests/`
  trees changes (AC-11).
- **No tax-year policy is introduced.** The portal contains no four-digit year
  literal and declares no supported-year type (AC-35).
- **No PII path is created.** The portal names no taxpayer type, holds no SIN,
  and introduces no masking helper — the v1 posture is omission: the data never
  enters the process (AC-32, AC-36, AC-37).
- **The full existing test suite stays green**, with the same set of tests, none
  removed, skipped, renamed, or weakened (AC-13, AC-43). If a design choice would
  require relaxing an existing assertion, the design is wrong, not the assertion.

---

## Recorded Class B decisions made by this plan

Beyond the PRD's `[ASSUMED]` list, which stands unchanged:

- `[PLAN]` **Rendering mechanism** — minimal-API `MapGet` returning constant
  content, not Razor Pages (Trade-off 1). Server-rendered; no client framework.
- `[PLAN]` **Response literals** fixed byte-for-byte in this document.
- `[PLAN]` **File set** — `Program.cs`, `ModuleRegistration.cs`,
  `PortalContent.cs`, `appsettings.json`, `Properties/launchSettings.json`.
- `[PLAN]` **`ModuleRegistration` and `PortalContent` are `internal`**; `Program`
  is the portal's only public type; **no `InternalsVisibleTo` anywhere**.
- `[PLAN]` **All three registrations are singletons**, registered by concrete
  type for `T4CaseProcessor` and by interface for the two providers.
- `[PLAN]` **`Program.cs` does not call `AddTaxAiModules` until T02**, so no
  merge leaves `master` with a host that throws on startup.
- `[PLAN]` **`AddServerHeader = false`** on Kestrel — one line, defense in depth
  for user story 3 ("probing the app leaks nothing about it"). `TestServer` does
  not use Kestrel, so this is verified by source assertion, not over the wire.
- `[PLAN]` **`appsettings.json` carries logging defaults only**; `AllowedHosts`
  is left at its framework default because host filtering is part of the deferred
  hosting/exposure decision (Open question E), and pinning it would be a hosting
  decision this spec is not entitled to make.
- `[PLAN]` **AC-11, AC-13, and AC-43 are verified by recorded command evidence**
  in `verification.md` (`git diff --name-only`, `dotnet test TaxAI.sln`) rather
  than by a test that shells out to `git`. T06 carries the testable proxies.
- `[PLAN]` **`Microsoft.AspNetCore.Mvc.Testing` 8.0.0** as the in-process web
  test host, test project only.
