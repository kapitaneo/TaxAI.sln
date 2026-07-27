# PRD 010 — Portal Foundation

## Feature classification

**Class: `platform`.**

Portal Foundation introduces the repository's first hostable application: an
ASP.NET Core web host, a single composition root that wires the already-shipped
modules into a dependency-injection container, one fixed health endpoint, and one
static shell page. It changes repository structure, build tooling, module
composition, hosting, and the UI foundation.

It introduces and changes **no** tax fact: no box, no CRA line, no mapping, no
rate, threshold, limit, deadline, gross-up, credit percentage, tax-year rule, or
taxpayer-domain validation. It renders no taxpayer data, accepts no user input,
touches no SIN, and invokes no parser, mapper, or mapping config.

**Tax/CRA semantic impact: none**

This classification is only honest because the scope is drawn to make it true.
The moment the portal renders a taxpayer amount, accepts an uploaded slip, or
decides what a preparer sees, it becomes `tax-domain` and every tax-domain
protection reattaches. FR-18 makes that a hard, written gate rather than a
convention. If any part of this feature is found to change tax behaviour, that
change is a **defect against this PRD**, not a delivered outcome (see Out of
scope).

**Tax-year scope: `Not applicable`.** No tax semantics change, so no tax year is
in scope. This feature adds no supported-year policy, reads no year-scoped
config, and contains no year literal.

---

## Problem

TaxAI has five deterministic modules and no way to run any of them. Concretely,
today:

1. **There is no deployable application.** `TaxAI.sln` declares production class
   libraries and test projects only. `dotnet run` has no target. The
   modular-monolith decision in root `CLAUDE.md` commits to "one initially-
   deployable application"; that application does not exist, so the commitment is
   unverifiable.

2. **There is no composition root.** Every consumer of `T4CaseProcessor` must
   construct `EmbeddedT4LineMapProvider` itself, at its own call site. Today the
   only such call sites are tests. The first real caller will invent its own
   wiring, and the second will invent a different one — which is how a process
   ends up with two provider instances and, eventually, two different answers
   about which mapping config is authoritative.

3. **The inward dependency rule is documented but not compiled.** Root
   `CLAUDE.md` states `UI / API host ──► application/module contracts ──►
   TaxLogicEngine`, and each module's boundary suite proves *it* has no ASP.NET
   Core dependency. Nothing proves the other direction — that a host layer can
   reach the engine only through contracts — because no host layer exists. The
   rule "UI must not compute tax or implement CRA mappings" currently has no UI to
   bind.

4. **The next features have nowhere to land.** Spec 009's Accountant Review
   Package is merged, and its read model explicitly names "the Portal Foundation
   author" as its consumer — but there is no host to consume it from. Document
   upload, review screens, and firm workflows all presuppose a host. Each of
   those is a Class A-heavy feature; none should be the feature that also has to
   decide, under deadline, where `Program.cs` lives.

5. **The README is drifting.** It states `TaxAI.sln  Solution (two projects)` and
   lists only `src/TaxLogicEngine/` and `tests/TaxLogicEngine.Tests/`. Five
   modules and five test projects exist. A README that describes a repository
   that is not there is the same correctness hazard spec 005 fixed once already.

**What this feature deliberately is not.** It is not a portal. It is the
foundation of one: a host that starts, composes the existing modules, serves two
fixed responses, and proves — mechanically — that no tax logic, no taxpayer data,
and no PII crossed into the web layer. Every screen, every upload, every login,
and every byte of taxpayer data is a later, separately-specified feature.

---

## Current repository state and reuse analysis

Grounded in the actual tree at `D:\Projects\TaxAI` (branch
`spec/010-portal-foundation`, which carries spec 010's artifacts only; spec 009
is implemented and merged to `master`), read directly during this analysis — not
from memory and not from a prior PRD taken as a source.

### Solution and projects that exist today

`TaxAI.sln` (format 12.00, VS 17) at the repository root declares these projects,
each once, at these paths:

| Project | Path | SDK / TFM |
|---|---|---|
| `TaxLogicEngine` | `src\TaxLogicEngine\TaxLogicEngine.csproj` | `Microsoft.NET.Sdk`, net8.0 |
| `TaxLogicEngine.Tests` | `tests\TaxLogicEngine.Tests\TaxLogicEngine.Tests.csproj` | net8.0, xunit 2.5.3 |
| `TaxCases` | `src\TaxCases\TaxCases.csproj` | net8.0 |
| `TaxCases.Tests` | `tests\TaxCases.Tests\TaxCases.Tests.csproj` | net8.0, xunit 2.5.3 |
| `DocumentExtraction` | `src\DocumentExtraction\DocumentExtraction.csproj` | net8.0 |
| `DocumentExtraction.Tests` | `tests\DocumentExtraction.Tests\DocumentExtraction.Tests.csproj` | net8.0, xunit 2.5.3 |
| `CaseProcessing` | `src\CaseProcessing\CaseProcessing.csproj` | net8.0 |
| `CaseProcessing.Tests` | `tests\CaseProcessing.Tests\CaseProcessing.Tests.csproj` | net8.0, xunit 2.5.3 |
| `CaseReview` | `src\CaseReview\CaseReview.csproj` | `Microsoft.NET.Sdk`, net8.0 |
| `CaseReview.Tests` | `tests\CaseReview.Tests\CaseReview.Tests.csproj` | net8.0, xunit 2.5.3 |

Test projects uniformly use `xunit` 2.5.3, `xunit.runner.visualstudio` 2.5.3,
`Microsoft.NET.Test.Sdk` 17.8.0, `coverlet.collector` 6.0.0.

*(This table is a description of today's tree, not a licence to assert a total
project count anywhere — see Migration constraints.)*

### What does **not** exist today

- **No web, host, API, or UI project of any kind.** A repository-wide grep for
  `Microsoft.AspNetCore`, `WebApplication`, `IServiceCollection`, and
  `Microsoft.Extensions` finds matches **only inside the existing boundary tests**,
  where those names appear as *forbidden* patterns being scanned for. No
  production file references any of them.
- **No `Portal` anywhere in production code, in any project file, or in
  `TaxAI.sln`.** Occurrences of the word are confined to spec documents (spec
  009's user story 10, and spec 010's own artifacts) and `NEXT.md`.
- No `global.json`, no `Directory.Build.props`, no `Directory.Packages.props`, no
  `.editorconfig`, no `nuget.config`, no `.github/` CI workflow, no Dockerfile,
  no `package.json`, no `node_modules`.
- No dependency-injection registration of any kind, anywhere.

### Reused unchanged (referenced, never re-specified or modified)

| Existing type | Where | Role here |
|---|---|---|
| `IT4LineMapProvider` / `EmbeddedT4LineMapProvider` | `src/TaxLogicEngine/Services/` | Registered in the container as the engine's T4 mapping-config port and its embedded implementation. Stateless, parameterless. Not modified, not wrapped, not re-implemented. |
| `IT5LineMapProvider` / `EmbeddedT5LineMapProvider` | `src/TaxLogicEngine/Services/` | Same, for T5. |
| `T4CaseProcessor` | `src/CaseProcessing/Services/` | Registered so the container can construct the spec 008 coordinator from its declared constructor `T4CaseProcessor(IT4LineMapProvider)`. **Registered only** — no endpoint resolves or invokes it in v1. |
| `TaxAI.sln` | repo root | Two project declarations are appended. No existing declaration, GUID, or configuration row is altered. |
| The five modules' `CLAUDE.md` / `AGENTS.md` pattern | `src/<Module>/` | The portal follows the same local-instructions pattern. **No existing module's `CLAUDE.md` or `AGENTS.md` is edited.** |

Nothing in the existing type inventory — `ParseResult<T>`, `ValidationError`,
`MoneyRounding`, `RecipientIdentifier`, the T4/T5 slip, raw-input, mapping, and
line-entry types, `TaxCase` and its descriptors, `ExtractionEnvelope`,
`ValidatedExtraction`, `ProcessedT4Extraction`, `AccountantReviewPackage` and its
review types — is re-specified, duplicated, wrapped, or re-implemented. The
portal names none of them (AC-31, AC-32, AC-50).

### Relied on but deliberately NOT consumed

- **`IDocumentExtractionIngress`** (`src/DocumentExtraction/Ports/`) — spec 007
  ships the signature only and states explicitly that **no implementation ships**.
  The portal **does not implement it and does not register it** (AC-24). Writing
  an ingress here would be inventing the untrusted-input intake path as a side
  effect of a hosting spec.
- **`TaxCases`** — its public surface is immutable value types and static policy
  services with nothing to register. No project reference in either direction.
- **`DocumentExtraction`** — no direct project reference; `ExtractionEnvelopeValidator`
  is static and nothing in v1 calls it.
- **Spec 009's `CaseReview` module** — exists and is merged (`src/CaseReview/`,
  `tests/CaseReview.Tests/`). The portal nonetheless **does not reference it,
  registers nothing from it, and names none of its types in v1** `[ASSUMED]`
  (AC-50). Rationale: `AccountantReviewPackage` and its `ReviewedSlip` content
  **are taxpayer data** — slips, box amounts, CRA line entries, source-box
  provenance, and a masked SIN — and rendering them is exactly what the FR-18
  gate exists to stop until Class A questions A–F are answered. Consuming
  `CaseReview` is the first screen spec's job, not this one's. The independence
  is two-directional: no existing module references the portal either (AC-10).
- **`SafeDiagnosticText`, `RecipientIdentifier`, and every masking helper** — not
  referenced. The portal handles no PII in v1, so it needs no masking, and
  introducing a second masking helper is exactly what every prior module's
  instructions forbid.

### Existing tests this feature must not disturb (verified by reading them)

This was checked file-by-file because a new project in `src/` is precisely the
kind of change that trips a repository-verification suite:

- `CaseProcessingBoundaryTests.NoOtherModuleReferencesCaseProcessing` enumerates
  its targets with explicit `[InlineData]` (`src/TaxLogicEngine`, `src/TaxCases`,
  `src/DocumentExtraction`, and their test trees). It does **not** walk `src/`
  generically, so a new sibling project does not enter its scan.
- `CaseProcessing.Tests/EndToEnd/RepositoryVerificationTests.PreExistingTrees` is
  a fixed six-entry list frozen at spec 008. A new tree is out of its scope.
- **`CaseReview.Tests` (spec 009, merged) does not obstruct this feature** —
  verified file-by-file:
  - its forbidden-pattern and production-file scans are rooted at
    `Path.Combine(RepositoryRoot, "src", "CaseReview")`, so they never see the
    portal's tree;
  - its solution assertions are presence-and-uniqueness (`Assert.Single(...)`
    over `projectLines` matching an approved path), never a total count;
  - `RepositoryVerificationTests.PreExistingTrees` is a frozen **eight**-entry
    list that predates CaseReview, and `CaseReviewBoundaryTests.ExistingProjectFiles`
    is a frozen eight-entry literal whose `Assert.Equal(8, …)` checks that
    literal's own length — not the repository's project count;
  - `EveryDeclaredSolutionProjectAndProjectReferenceResolves` *does* iterate every
    project declared in `TaxAI.sln`, but asserts only that each declaration and
    each of its project references **resolves to an existing file**; a portal
    declared at a real path with real references satisfies it;
  - `EveryKnownProjectHasItsExactApprovedReferenceSet` iterates a fixed
    ten-entry `ExpectedProjects` list, not the solution, so the portal is simply
    outside it;
  - `NoExistingProductionOrTestProjectReferencesCaseReview` and
    `NoPreExistingProjectOrSourceTreeReferencesCaseReview` iterate those frozen
    eight-entry lists, so the portal never enters their scan — and it would pass
    anyway, because it names `CaseReview` nowhere (AC-50).
- Every module's forbidden-pattern scan (`Microsoft.AspNetCore`, `DbContext`,
  `HttpClient`, `Math.Round`, five-digit CRA line literals, …) is rooted at
  `Path.Combine(RepositoryRoot, "src", "<ThatModule>")`. **No test scans all of
  `src/`.**
- Every solution assertion is presence-and-uniqueness
  (`Assert.Equal(1, projectLines.Count(...))` / `Assert.Single(...)`). **No test
  asserts a total project count** — the trap recorded in root `CLAUDE.md` after
  PR #66.
- **No test reads root `CLAUDE.md`, `README.md`, or `NEXT.md`.** Doc updates
  cannot break the suite.
- Each module's own `CLAUDE.md`/`AGENTS.md` content assertions are rooted at that
  module's directory, so a new portal `CLAUDE.md`/`AGENTS.md` neither satisfies
  nor breaks them.

**Conclusion:** adding an ASP.NET Core host as a new sibling project requires
**zero edits to any existing test, project file, or module source**, across all
five modules and all five test projects. If `/plan` finds itself needing to edit
an existing test's expectations, that is a signal the design has drifted outside
this PRD (FR-16, AC-11).

### Genuinely new (introduced by this feature)

Each item is justified individually, because "do not create empty, speculative
modules" binds this PRD:

| New artifact | Why it is not speculative |
|---|---|
| `src/TaxAI.Portal/` — one ASP.NET Core web project `[ASSUMED]` | The single deployable the modular-monolith decision already committed to. It ships running code (composition root, two endpoints), not an empty shell. |
| A single composition root registering the existing module contracts | The whole point of the feature. Without it, wiring is invented per call site (Problem 2). |
| `GET /health` returning a fixed literal | The deterministic endpoint that proves the host starts and the container validated. Without one endpoint there is nothing to assert against. |
| `GET /` returning a static shell page | Proves the host can serve server-rendered UI and fixes the "server-rendered, no client-side data" posture before any screen exists. Contains no data binding. |
| `tests/TaxAI.Portal.Tests/` | Every AC below needs somewhere to live. |
| `src/TaxAI.Portal/CLAUDE.md` + pointer `AGENTS.md` | The established per-module pattern; it is where the "UI computes no tax" and "no taxpayer data until Class A answers" gates are written down. |

**No project, folder, page, controller, or interface is created for a future
module or a future screen.** No Clients, Firms, Documents, Intake, or Review
project is scaffolded (AC-12).

---

## Desired outcome

A repository in which:

- `dotnet run --project src/TaxAI.Portal` starts a web application locally;
- that application composes `TaxLogicEngine` and `CaseProcessing` through one
  explicit composition root whose container is validated at startup;
- `GET /health` and `GET /` answer with fixed content, in-process-testable
  without binding a network port;
- the inward dependency direction is proved by tests in both directions — the
  host reaches modules through their contracts, and no module references the
  host;
- the web layer is proved to contain no tax computation, no CRA line number, no
  mapping table, no taxpayer type, no PII, and no tax-year policy;
- the documentation states what the portal is, what it deliberately is not, and
  which Class A questions must be answered before it may handle taxpayer data;

all with **zero** change to any tax behaviour, public tax contract, namespace,
assembly identity, embedded-resource logical name, rounding behaviour, SIN
masking, or tax-year failure behaviour, and with the full existing test suite
green.

---

## Preserved behaviour (invariants; any deviation is a defect)

1. **T4 and T5 slip parsing** behaviour and outputs — unchanged.
2. **T4 and T5 box→CRA line mapping** behaviour, including the deliberate
   `UnmappedBoxCodes` deferrals — unchanged.
3. **Tax Case Core** (`src/TaxCases/`) behaviour — unchanged.
4. **Document Extraction Contract** (`src/DocumentExtraction/`) behaviour,
   including that `IDocumentExtractionIngress` still has **no implementation** —
   unchanged.
5. **T4 Case Processor** (`src/CaseProcessing/`) behaviour, including its
   eight-stage order, verbatim error surfacing, and single-year provider query,
   and **Accountant Review Package** (`src/CaseReview/`) behaviour, including its
   assembly rules, package-level acknowledgement gate, single-decision
   immutability, and 2025-only year policy — unchanged.
6. **Public contracts** — every public type, member, and signature in all five
   existing modules stays exactly as-is; nothing added, renamed, removed, or
   re-shaped.
7. **Assembly identity and namespaces** of all five modules — unchanged.
8. **Embedded-resource logical names** — `TaxLogicEngine.Mappings.T4LineMap.2025.json`
   and `TaxLogicEngine.Mappings.T5LineMap.2025.json` remain exactly those strings.
9. **Monetary rounding** — `MoneyRounding.ToCents` half-up
   (`MidpointRounding.AwayFromZero`) — unchanged and not invoked here.
10. **SIN masking** — `RecipientIdentifier` masking to the last 3 digits —
    unchanged, and no second masking path is introduced.
11. **Tax-year isolation / fail-closed** — every module's 2025-only policy and
    its no-adjacent-year-fallback behaviour — unchanged. The portal declares no
    year policy of its own.
12. **`InternalsVisibleTo` grants** — the three that exist on `master` are
    unchanged, and **no new grant is added to any existing module, nor declared
    by either new project**. Two are declared in project files
    (`CaseProcessing` → `CaseProcessing.Tests`, `CaseReview` →
    `CaseReview.Tests`); the third is a source-level assembly attribute in
    `src/TaxLogicEngine/Services/EmbeddedT4LineMapProvider.cs`
    (`TaxLogicEngine` → `TaxLogicEngine.Tests`). No project file other than those
    two declares one.
13. **The full existing test suite passes**, with the same set of tests, before
    and after — none removed, skipped, renamed, or weakened.
14. **Every existing test file and every existing `.csproj` is byte-unchanged.**

---

## Migration constraints

- **Additive only.** No existing project, file, folder, namespace, or type moves,
  is renamed, or is edited — except the documentation files named in FR-17. The
  untouchable trees are `src/TaxLogicEngine/`, `src/TaxCases/`,
  `src/DocumentExtraction/`, `src/CaseProcessing/`, `src/CaseReview/`, and their
  five test trees.
- **No edit to any existing test.** If a design choice would require relaxing an
  existing boundary assertion, the design is wrong, not the assertion.
- **No `PackageReference`, `FrameworkReference`, `EmbeddedResource`, or `Content`
  item may be added to any pre-existing production project.** Four of the five
  modules already assert this about themselves; the fifth (`TaxLogicEngine`,
  whose embedded mapping configs are the deliberate pre-existing exception) must
  not gain one either.
- **ASP.NET Core enters the repository in exactly two projects** — the portal and
  its test project — and nowhere else. Tax-domain code keeps its zero-web-
  dependency guarantee by never being touched.
- **Never assert a total project count** for `TaxAI.sln`, in a test, in a doc, or
  in the README. The two new projects are asserted by presence-and-uniqueness at
  their approved paths, exactly as specs 006–009 do.
- **The solution is edited by appending only**: two `Project(...)`/`EndProject`
  blocks with fresh GUIDs and their configuration rows. No existing GUID,
  ordering, or configuration row changes.
- **Stage-by-stage verification.** After each stage, `dotnet build TaxAI.sln` and
  `dotnet test TaxAI.sln` must both succeed before the next stage begins.
- **No tracked build output or machine-local file** — no `bin/`, `obj/`, `.vs/`,
  `*.user`, `node_modules/`, secret, certificate, or `.claude/settings.local.json`
  may become tracked as a side effect.
- **No dependency on spec 009.** Spec 009 is merged, but this feature neither
  references, registers, nor names any of its types (AC-50).
- **Ordering `[ASSUMED]`** (each stage independently green):
  1. Create the portal project and its test project, add both to the solution,
     add the local `CLAUDE.md` and pointer `AGENTS.md`; verify build + full test
     run.
  2. Add the composition root and the health endpoint; verify container
     validation and endpoint behaviour in-process.
  3. Add the static shell page.
  4. Add the boundary, PII-absence, and regression suites.
  5. Update `README.md`, root `CLAUDE.md`, and `NEXT.md`; record
     `specs/010-portal-foundation/verification.md`.

---

## User stories

1. As a **developer**, I run one documented command and get a running TaxAI
   application, so "the modular monolith is one deployable app" stops being a
   claim and becomes something I can open in a browser.
2. As an **architect**, I have exactly one place where modules are wired into the
   container, so the second consumer of the engine cannot invent a second wiring.
3. As an **operator**, I can call one health endpoint that answers with a fixed
   string and nothing else — no version, no configuration, no diagnostics — so
   probing the app leaks nothing about it.
4. As a **reviewer**, I can point at a test that proves the web layer contains no
   CRA line number, no mapping table, no rounding, and no taxpayer type, so
   "UI must not compute tax" is enforced rather than remembered.
5. As a **module owner**, I keep my guarantee that nothing depends on me except
   inward-pointing layers, and that my project still carries zero package
   references and zero ASP.NET Core dependency.
6. As the **author of the next feature**, I inherit a host, a composition root,
   and a written list of the Class A questions I must get answered before I may
   render one taxpayer amount — so I do not answer them by accident.
7. As the **taxpayer whose return depends on this engine**, none of this changes
   a single box, line, or amount, and no data about me is rendered, logged,
   stored, or transmitted by anything this feature ships.

---

## Functional requirements

`[new]` marks a genuinely new artifact. `[reuse]` marks reuse of an existing
type. `[boundary]` marks a module-boundary invariant from root `CLAUDE.md`.
**No requirement below asserts a tax or CRA fact.**

### The host

**FR-1 `[new]`** — A single ASP.NET Core web project SHALL be added at
`src/TaxAI.Portal/` `[ASSUMED]`, targeting net8.0 with nullable reference types
and implicit usings enabled, using the web SDK. It is the repository's only
executable project.

**FR-2 `[new]`** — A matching test project SHALL be added at
`tests/TaxAI.Portal.Tests/` `[ASSUMED]`, using the same xUnit tooling versions as
the five existing test projects, and referencing only the portal project.

**FR-3 `[boundary]`** — The portal's production project references SHALL be
exactly `TaxLogicEngine` and `CaseProcessing`, pointing inward `[ASSUMED]`. It
SHALL NOT reference `TaxCases`, SHALL NOT directly reference
`DocumentExtraction`, and no existing project SHALL reference the portal in
either direction.

**FR-4 `[new]`** — Both new projects SHALL be declared in `TaxAI.sln` exactly
once each, at their approved paths, by appending to the existing file. No
existing declaration SHALL be altered, and **no artifact of this feature SHALL
assert a total project count**.

**FR-5 `[new]`** — The portal SHALL declare **zero** `PackageReference` items:
the web SDK's framework reference is the whole of its dependency surface. Test
tooling (including the in-process web test host) belongs to the test project
only.

### Composition root

**FR-6 `[reuse]`** — A single composition root SHALL register, in exactly one
file: `IT4LineMapProvider` → `EmbeddedT4LineMapProvider`, `IT5LineMapProvider` →
`EmbeddedT5LineMapProvider`, and `T4CaseProcessor` `[ASSUMED]`. These types are
reused as they are — not wrapped, not subclassed, not re-implemented, not
modified.

**FR-7 `[new]`** — The service provider SHALL be validated on build with scope
validation enabled, so a mis-wired container fails at startup rather than on
first request.

**FR-8 `[boundary]`** — No service registration SHALL appear outside the
composition-root file. Wiring lives in one place or it lives in several.

**FR-9 `[boundary]`** — `IDocumentExtractionIngress` SHALL NOT be implemented,
registered, or resolved. Spec 007 shipped the signature deliberately without an
implementation; creating one here would invent the untrusted-input intake path
inside a hosting spec.

**FR-10 `[boundary]`** — Nothing in the portal SHALL resolve or invoke
`T4CaseProcessor` at request time in v1. It is registered to prove the container
can construct it; there is no endpoint that runs it.

### Endpoints

**FR-11 `[new]`** — The portal SHALL expose exactly two routes `[ASSUMED]`:
`GET /health`, returning HTTP 200 and a fixed literal body; and `GET /`,
returning HTTP 200 and a static server-rendered shell page. No other route SHALL
be mapped.

**FR-12 `[new]`** — Neither endpoint SHALL accept a request body, route value,
query value, form value, header value, or cookie. v1 has no input surface.

**FR-13 `[new]`** — The health response SHALL contain a fixed literal only: no
version, no environment name, no configuration value, no dependency status, no
timestamp, no machine or user name. A probe endpoint that describes the system is
a disclosure surface.

**FR-14 `[new]`** — Outside the Development environment, no error response SHALL
include a stack trace, exception type name, or filesystem path; an unmatched
route SHALL return 404 with no diagnostic detail.

### The line the portal may not cross

**FR-15 `[boundary]`** — The portal SHALL compute no tax and implement no CRA
mapping. It SHALL contain **no CRA line-number literal, no box code, no box→line
table, no tax-year literal, no supported-tax-year policy**, and SHALL invoke no
`T4SlipParser`, `T5SlipParser`, `T4LineMapper`, `T5LineMapper`,
`T4SlipBoxAmounts`, `T5SlipBoxAmounts`, or `MoneyRounding`, and SHALL read no
`TaxLogicEngine.Mappings` resource. It SHALL perform no arithmetic or rounding:
no `Math.Round`, no `MidpointRounding`, no `double`, no `float`, no `decimal`.

**FR-16 `[boundary]`** — The portal SHALL name no taxpayer data type — no
`T4Slip`, `T5Slip`, `T4RawInput`, `T5RawInput`, `TaxLineEntry`,
`T4MappingResult`, `RecipientIdentifier`, `ValidatedExtraction`,
`ExtractionEnvelope`, `ProcessedT4Extraction`, or `TaxCase` — and SHALL handle,
render, log, serialize, persist, or transmit **no taxpayer data and no PII of any
kind**. It SHALL contain no SIN handling and no masking or redaction helper. In
v1 the safest possible PII posture is available and is therefore mandatory:
**the data never enters the process.**

**FR-17 `[new]`** — Documentation SHALL be updated as follows, and no further:
a new `src/TaxAI.Portal/CLAUDE.md` stating the portal's boundary and the FR-18
gate; a new pointer-only `src/TaxAI.Portal/AGENTS.md` matching the established
pattern; `README.md` updated to describe the actual repository (including
removing the stale "two projects" claim and asserting no project count); root
`CLAUDE.md` repository-layout and current-state entries; and `NEXT.md`.
**No existing module's `CLAUDE.md` or `AGENTS.md` SHALL be edited** — no
module-boundary rule is rewritten, relaxed, or reinterpreted by this feature.

**FR-18 `[boundary]` — The gate.** The portal's local `CLAUDE.md` SHALL state, and
this PRD asserts, that **no endpoint, page, component, or middleware that renders
taxpayer data, accepts user input, uploads a document, persists data, reads a
session or identity, or crosses a firm/case/participant boundary may be added to
the portal until the Class A questions in Open questions are answered in a
separate specification.** Adding any of those makes the feature `tax-domain`, and
that reclassification is not a design decision anyone downstream gets to make
silently.

**FR-19 `[new]`** — The portal SHALL introduce no authentication,
authorization, session, cookie, or identity mechanism in v1, because it exposes
no protected resource. This is an exclusion, not a security design: the security
design is Class A and is reserved for the human (Open questions A–F).

**FR-20 `[new]`** — The portal SHALL introduce no persistence, database, ORM,
HTTP client, message queue, OCR provider, LLM provider, email provider, cloud
SDK, background service, scheduler, or system-clock-dependent behaviour.

**FR-21 `[new]`** — The portal SHALL be server-rendered `[ASSUMED]`. No SPA
framework, no client-side data fetching, no JSON API surface, no `package.json`,
and no npm/node artifact SHALL be introduced. Taxpayer data must never be shipped
to a browser as data; establishing that posture is cheaper now than after a screen
exists.

**FR-22 `[new]`** — No configuration file SHALL contain a secret, connection
string, credential, certificate, key, or token. Any tracked launch profile SHALL
use local development defaults only; hosting exposure, TLS, and deployment are
out of scope (Open questions E).

**FR-23** — This feature SHALL change no behaviour listed under Preserved
behaviour, and the full existing test suite SHALL remain green with the same set
of tests.

---

## Out of scope

Each item is explicitly excluded. **Any accidental change to tax behaviour is out
of scope and is a defect, not a delivered outcome.**

- **Any change to tax semantics** — no new or changed box, CRA line, mapping,
  rate, threshold, limit, deadline, calculation, or taxpayer-domain validation.
- **Any change to any existing module's source, tests, project file, `CLAUDE.md`,
  or `AGENTS.md`** — `TaxLogicEngine`, `TaxCases`, `DocumentExtraction`,
  `CaseProcessing`, and `CaseReview`, and their five test trees.
- **Rendering, exporting, or displaying any taxpayer data** — including a review
  package, a slip, a line entry, an amount, a name, or a SIN in any form, masked
  included.
- **Accepting any user input** — forms, uploads, query parameters, request
  bodies, headers, or cookies.
- **Authentication, authorization, sessions, identity, user management, firm
  onboarding, and tenant isolation enforcement** — Class A, reserved (Open
  questions A, B).
- **Persistence of any kind** — database, EF Core, migrations, file storage,
  caching, or audit storage.
- **Implementing `IDocumentExtractionIngress`**, or any OCR/LLM integration or
  adapter.
- **Consuming spec 009's Accountant Review Package** — merged and available, and
  deliberately left unconsumed: its contents are taxpayer data, so wiring it into
  a screen is the first taxpayer-data feature's job, behind the FR-18 gate.
- **Deployment, hosting environment, TLS, reverse proxy, containers, CI/CD, data
  residency, monitoring, telemetry, and log shipping** — Class A/infrastructure,
  reserved (Open questions D, E).
- **Any UI beyond one static shell page** — no navigation model, no design
  system, no component library, no theming, no accessibility programme, no
  localization/bilingual strategy.
- **Empty placeholder projects, pages, controllers, view models, or interfaces
  for future modules or future screens.**
- **API versioning, OpenAPI/Swagger, or any machine-facing contract.**
- **Microservices or any out-of-process module split.**
- **Renaming any assembly, namespace, public type, or embedded-resource logical
  name.**
- **`Directory.Build.props`, `global.json`, central package management, analyzers,
  or CI workflows** `[ASSUMED excluded]` — smallest useful foundation only.

---

## Acceptance criteria

Each criterion is a single mechanically-checkable condition (Given/When/Then).

### Structure and solution (FR-1 – FR-5)

1. **Portal project exists with the web SDK.** Given the repository after this
   feature, When `src/TaxAI.Portal/TaxAI.Portal.csproj` is loaded, Then it exists
   and its `Sdk` attribute is the ASP.NET Core web SDK. `[ASSUMED path]`
2. **Portal project settings.** Given that project file, When its properties are
   read, Then `TargetFramework` is `net8.0`, `Nullable` is `enable`, and
   `ImplicitUsings` is `enable`.
3. **Portal declares no package reference.** Given that project file, When
   scanned, Then it contains zero `PackageReference` items.
4. **Test project exists with the repository's standard tooling.** Given
   `tests/TaxAI.Portal.Tests/TaxAI.Portal.Tests.csproj`, When its package
   references are read, Then `xunit` is 2.5.3, `xunit.runner.visualstudio` is
   2.5.3, `Microsoft.NET.Test.Sdk` is 17.8.0, and `coverlet.collector` is 6.0.0.
   `[ASSUMED path]`
5. **Solution declares each new project exactly once.** Given `TaxAI.sln`, When
   its `Project(` lines are counted, Then the portal declaration appears exactly
   once and the portal-test declaration appears exactly once, at their approved
   paths. *(No total project count is asserted — root `CLAUDE.md`.)*
6. **Existing solution entries are untouched.** Given `TaxAI.sln`, When compared
   with its previous content, Then every pre-existing `Project(` line and every
   pre-existing configuration row is unchanged, and the only additions are the
   two new projects' blocks and rows.
7. **Build succeeds.** Given the repository, When `dotnet build TaxAI.sln` runs,
   Then it exits 0.
8. **Portal production references are exactly two.** Given the portal project
   file, When its `ProjectReference` items are read, Then they are exactly
   `../TaxLogicEngine/TaxLogicEngine.csproj` and
   `../CaseProcessing/CaseProcessing.csproj`. `[ASSUMED]`
9. **Portal test project references exactly the portal.** Given the portal test
   project file, When its `ProjectReference` items are read, Then the only entry
   is the portal project.
10. **No reverse reference.** Given every pre-existing `.csproj` under `src/` and
    `tests/`, When each is read, Then none contains the string `TaxAI.Portal`.
11. **No pre-existing file changed.** Given `git diff` against the merge base,
    When the changed-file list is read, Then it contains no path under
    `src/TaxLogicEngine/`, `src/TaxCases/`, `src/DocumentExtraction/`,
    `src/CaseProcessing/`, `src/CaseReview/`, or any of the five existing test
    trees.
12. **No speculative project.** Given the repository, When all `.csproj` files
    are listed, Then the only additions are the two named above, and no project
    exists for Clients, Firms, Documents, Intake, or Review.

### Regression — existing behaviour stays green (FR-23, Preserved behaviour)

13. **Full suite green.** Given the repository, When `dotnet test TaxAI.sln`
    runs, Then it exits 0 and every pre-existing test in `TaxLogicEngine.Tests`,
    `TaxCases.Tests`, `DocumentExtraction.Tests`, `CaseProcessing.Tests`, and
    `CaseReview.Tests` passes, with none removed, skipped, or renamed.
14. **No tax mapping, calculation, or parser behaviour changed.** Given the five
    modules' parser, mapper, provider, rounding, review-assembly, PII, and
    year-isolation suites, When they run after this feature, Then every one
    passes unchanged, and the set of public type full-names exported by each of
    the five modules is identical to its pre-feature set.
15. **Embedded mapping resources are untouched.** Given the built
    `TaxLogicEngine` assembly, When its manifest resource names are enumerated,
    Then `TaxLogicEngine.Mappings.T4LineMap.2025.json` and
    `TaxLogicEngine.Mappings.T5LineMap.2025.json` are present with exactly those
    names.
16. **No new internals grant.** Given every pre-existing production project file
    and every pre-existing production source file, When scanned for
    `InternalsVisibleTo`, Then exactly the three pre-existing grants are found —
    `CaseProcessing` → `CaseProcessing.Tests` and `CaseReview` →
    `CaseReview.Tests` in project files, and `TaxLogicEngine` →
    `TaxLogicEngine.Tests` as a source attribute in
    `src/TaxLogicEngine/Services/EmbeddedT4LineMapProvider.cs` — and neither new
    project declares one.

### Host behaviour (FR-6 – FR-14)

17. **The host starts in-process.** Given the portal, When it is started through
    the in-process test host, Then startup completes without throwing and without
    binding a network port.
18. **Container is validated on build with scopes.** Given the host's service
    provider options, When inspected, Then validate-on-build and validate-scopes
    are both enabled.
19. **T4 mapping port resolves.** Given the started host, When
    `IT4LineMapProvider` is resolved, Then the instance is an
    `EmbeddedT4LineMapProvider`.
20. **T5 mapping port resolves.** Given the started host, When
    `IT5LineMapProvider` is resolved, Then the instance is an
    `EmbeddedT5LineMapProvider`.
21. **The processor is constructible from the container.** Given the started
    host, When `T4CaseProcessor` is resolved, Then resolution succeeds.
22. **Registrations live in one file.** Given the portal's source, When scanned
    for `AddSingleton`, `AddScoped`, `AddTransient`, and `TryAdd*`, Then every
    match is in the single composition-root file.
23. **Health endpoint.** Given the started host, When `GET /health` is requested,
    Then the response status is 200 and the body equals the fixed expected
    literal exactly.
24. **The ingress port is not registered.** Given the started host, When
    `IDocumentExtractionIngress` is requested from the container with a
    null-returning lookup, Then the result is null, and the portal's source
    contains no type implementing that interface.
25. **Shell page.** Given the started host, When `GET /` is requested, Then the
    response status is 200, the content type is HTML, and the body equals/contains
    the fixed expected shell content with no dynamic value.
26. **Only two routes are mapped.** Given the started host's endpoint data
    source, When the mapped endpoints are enumerated, Then there are exactly two,
    both GET, with route patterns `/` and `/health`, and neither pattern contains
    a route parameter. `[ASSUMED paths]`
27. **No input binding anywhere.** Given the portal's source, When scanned, Then
    it contains no `[FromBody]`, `[FromQuery]`, `[FromRoute]`, `[FromForm]`,
    `[FromHeader]`, `Request.Body`, `Request.Query`, `Request.Form`,
    `Request.Headers`, `Request.Cookies`, or `IFormFile`.
28. **Health response discloses nothing.** Given the health response body, When
    inspected, Then it contains no version, environment name, machine name, user
    name, path, timestamp, or configuration value.
29. **Unknown route in Production.** Given the host started with the Production
    environment, When an unmatched path is requested, Then the status is 404 and
    the body contains no stack trace, exception type name, or filesystem path.
30. **No developer exception page outside Development.** Given the portal's
    source, When scanned, Then any developer-exception-page call is inside an
    `IsDevelopment()` branch, or absent entirely.

### The line the portal may not cross (FR-15, FR-16)

31. **No tax computation token.** Given the portal's production source
    (executable lines), When scanned, Then it contains none of: `T4SlipParser`,
    `T5SlipParser`, `T4LineMapper`, `T5LineMapper`, `T4SlipBoxAmounts`,
    `T5SlipBoxAmounts`, `MoneyRounding`, `Math.Round`, `MidpointRounding`,
    `double`, `float`, `decimal`.
32. **No taxpayer data type.** Given the portal's production source, When
    scanned, Then it names none of: `T4Slip`, `T5Slip`, `T4RawInput`,
    `T5RawInput`, `TaxLineEntry`, `T4MappingResult`, `T5MappingResult`,
    `RecipientIdentifier`, `ValidatedExtraction`, `ExtractionEnvelope`,
    `ProcessedT4Extraction`, `TaxCase`.
33. **No CRA line-number literal.** Given the portal's production source, When
    scanned for a five-digit integer literal, Then there are zero matches.
34. **No mapping table or mapping-resource read.** Given the portal's production
    source, When scanned, Then it contains no box→line dictionary, no box-code
    list, no `TaxLogicEngine.Mappings` resource name, and no
    `GetManifestResourceStream`.
35. **No tax-year policy or year literal.** Given the portal's production source,
    When scanned, Then it contains no four-digit tax-year literal (2024, 2025,
    2026) and declares no supported-tax-year type or policy.

### PII and security posture (FR-16, FR-19, FR-20, FR-22)

36. **No PII token in the portal.** Given the portal's production source, When
    scanned, Then it contains no SIN-related identifier, no masking or redaction
    helper, no `SafeDiagnosticText`, and no nine-consecutive-digit literal.
37. **No response can carry taxpayer data.** Given the started host, When the
    bodies of `GET /` and `GET /health` are read, Then neither contains any
    nine-consecutive-digit substring, and both equal their fixed expected content.
38. **No request logging.** Given the portal's source and configuration, When
    scanned, Then they contain no `AddHttpLogging`/`UseHttpLogging` and no log
    statement that writes a request path, query string, header, cookie, or body.
39. **No authentication or authorization surface.** Given the portal's source,
    When scanned, Then it contains no `AddAuthentication`, `AddAuthorization`,
    `UseAuthentication`, `UseAuthorization`, `[Authorize]`, `ClaimsPrincipal`, or
    cookie/session configuration.
40. **No infrastructure dependency.** Given the portal's source and its compiled
    assembly's non-framework references, When inspected, Then they contain no
    `DbContext`, `SqlConnection`, `EntityFrameworkCore`, `System.Data`,
    `HttpClient`, OCR/LLM/email/cloud SDK token, `DateTime.Now`/`UtcNow`, or
    background-service registration, and the only referenced non-framework
    assemblies are `TaxLogicEngine` and `CaseProcessing`.
41. **No secret and no non-local binding.** Given every tracked configuration and
    launch-profile file under the portal, When read, Then none contains a
    connection string, password, key, token, or certificate, and any declared URL
    binds to localhost only.
42. **No client-side stack.** Given the repository, When listed, Then there is no
    `package.json`, no `node_modules`, no SPA framework asset, and no tracked
    minified bundle.
43. **Repository hygiene.** Given the feature's commits, When tracked files are
    listed, Then no `bin/`, `obj/`, `.vs/`, `*.user`, secret, certificate, or
    `.claude/settings.local.json` file became tracked.

### Documentation and independence (FR-17, FR-18)

44. **Portal instructions exist and carry the gate.** Given
    `src/TaxAI.Portal/CLAUDE.md`, When read, Then it states each of: dependencies
    point inward and are exactly `TaxLogicEngine` and `CaseProcessing`; the UI
    computes no tax and implements no CRA mapping; the portal holds no tax-year
    policy; the portal handles no taxpayer data or PII in v1; no endpoint accepts
    input; and the FR-18 gate naming the Class A questions that must be answered
    before any taxpayer-data surface is added.
45. **Pointer-only bridge.** Given `src/TaxAI.Portal/AGENTS.md`, When read, Then
    it is fewer than 15 lines and links `../../AGENTS.md`, `../../CLAUDE.md`, and
    the local `CLAUDE.md` — matching the five existing modules' pattern.
46. **No existing module instruction file changed.** Given `git diff` against the
    merge base, When the changed-file list is read, Then no `CLAUDE.md` or
    `AGENTS.md` under `src/TaxLogicEngine/`, `src/TaxCases/`,
    `src/DocumentExtraction/`, `src/CaseProcessing/`, or `src/CaseReview/`
    appears in it.
47. **README describes the actual repository.** Given `README.md`, When read,
    Then it lists the portal project and its run command, documents every
    existing project tree, and no longer claims the solution has "two projects" —
    and it asserts no project count at all.
48. **README claims nothing the portal lacks.** Given `README.md`, When grepped,
    Then it makes no claim of authentication, user accounts, taxpayer-data
    screens, document upload, persistence, deployment, or OCR/LLM integration.
49. **Root instructions record the new layout.** Given root `CLAUDE.md`, When
    read, Then its repository-layout section lists `src/TaxAI.Portal/` and
    `tests/TaxAI.Portal.Tests/`, and its current-state section records spec 010's
    delivered scope.
50. **No coupling to spec 009.** Given the portal's production source, its
    project file, and its compiled assembly, When scanned, Then the source and
    project file name none of `CaseReview`, `AccountantReviewPackage`,
    `ReviewedSlip`, `ReviewSignOff`, or `ReviewPackageAssembler`, and the
    assembly's referenced assemblies do not include `CaseReview`.

---

## Rollback and recovery

- All work happens on a feature branch; `master` is untouched until a human
  merges. Merging is never automated.
- The change is **purely additive**: two new project directories, two solution
  blocks, and five documentation edits. Reverting the branch's commits restores
  the exact prior tree — no file was moved, renamed, or rewritten in place, so
  there is no copy/delete gap in which content can be lost.
- Each ordered stage (see Migration constraints) is independently green, so a
  failing stage reverts to the last green stage without discarding earlier ones.
- **Recovery check:** after any revert, `dotnet build TaxAI.sln` and
  `dotnet test TaxAI.sln` must return to green with the same test set — AC-13
  guards both directions.
- If the portal is ever deleted outright, nothing else in the repository stops
  working: no module depends on it (AC-10), so removal is a one-directional
  amputation.

---

## Open questions

### Class A — reserved for the human; excluded from v1, nothing assumed

None of these blocks `/plan`, because this PRD **excludes every surface that
would require answering them**. Excluding is the conservative direction: a
feature cannot get wrong something it does not do. They are listed because the
very next portal feature will hit all of them at once, and FR-18 turns that from
an accident into a stop sign.

- **A — Authentication and authorization.** Who may reach the portal, how they
  authenticate, how a reviewer's identity is established, and what an accountant
  is authorized to see. Specs 007, 008, and 009 all assigned membership
  verification and caller authorization to "the future application workflow." The
  portal is that workflow's home, and it currently performs **no authorization
  check whatsoever** — which is safe only because it exposes nothing.
- **B — Tenant / firm isolation at the HTTP boundary.** Every module isolates
  firm, case, participant, and year *in memory*. Nothing yet decides how a
  request is bound to a firm, or what prevents one firm's request from reaching
  another firm's data. This must be settled before the first data-bearing
  endpoint, not after.
- **C — What taxpayer data a UI may render, and how.** In particular whether a
  masked SIN (last 3 digits, per root `CLAUDE.md`) may appear on a screen at all,
  and what else counts as PII for display purposes. v1 renders nothing, so this
  PRD takes no position.
- **D — Logging, telemetry, and error reporting policy** for a process that will
  eventually hold taxpayer data: what may be logged, where logs go, retention,
  and how an unhandled exception is reported without echoing request content. v1
  logs no request data (AC-38); the policy itself is yours.
- **E — Hosting, deployment, TLS, public exposure, and data residency.** Where a
  Canadian tax application's data and logs may physically live is a legal and
  compliance question this agent will not answer or approximate. v1 is a local
  development host only; no deployment artifact ships.
- **F — Deployable topology.** Whether this same host will later also expose the
  untrusted document-extraction ingress (`IDocumentExtractionIngress`), or whether
  untrusted intake gets its own boundary. v1 registers no ingress at all (AC-24).

### `[ASSUMED]` — Class B decisions, made and recorded for one-pass override

- `[ASSUMED]` **Spec folder and slug** are `specs/010-portal-foundation/` (001–009
  taken, confirmed by listing `specs/`); the feature name is taken verbatim from
  `NEXT.md`.
- `[ASSUMED]` **v1 scope** is: one runnable web host, one composition root, one
  health endpoint, one static shell page, one test project, and the documentation
  updates in FR-17 — and nothing else. No screen, no data, no input, no login, no
  storage.
- `[ASSUMED]` **Sequencing.** Spec 009 (Accountant Review Package) is complete and
  merged; Portal Foundation follows it and takes no dependency on it in either
  direction.
- `[ASSUMED]` **`CaseReview` is deliberately not consumed in v1.** The portal does
  not reference the merged `CaseReview` project, registers nothing from it, and
  names none of its types (AC-50), because `AccountantReviewPackage` and its
  reviewed slips are taxpayer data and rendering them is exactly what FR-18 gates
  until Class A questions A–F are answered. Overriding this assumption does not by
  itself make Portal Foundation a `tax-domain` feature — but the screen that
  consumes the package is one, and must be specified as such.
- `[ASSUMED]` **Project home and names** — `src/TaxAI.Portal/` (assembly
  `TaxAI.Portal`) and `tests/TaxAI.Portal.Tests/`, matching the repository's
  `src/`+`tests/` convention. The name is host-neutral ("Portal"), not
  screen-specific.
- `[ASSUMED]` **SDK and settings** — the ASP.NET Core web SDK, net8.0, nullable
  enabled, implicit usings enabled, matching the five existing projects.
- `[ASSUMED]` **Server-rendered, no client framework.** Razor Pages-style
  server rendering; no SPA, no JSON API, no npm. Rationale: taxpayer data must
  never be shipped to a browser as data, and that posture is cheapest to fix
  before a screen exists. `/plan` may choose the precise server-rendering
  mechanism, but not a client-side one.
- `[ASSUMED]` **Route paths and bodies** — `GET /health` returning the fixed
  literal `Healthy`, and `GET /` returning a static shell page whose only content
  is the application name and a one-line statement that no taxpayer data is
  served. Exact strings are `/plan`'s to fix, but they must be fixed literals.
- `[ASSUMED]` **Registered contracts** — `IT4LineMapProvider` →
  `EmbeddedT4LineMapProvider`, `IT5LineMapProvider` → `EmbeddedT5LineMapProvider`,
  and `T4CaseProcessor`. All singleton: the two providers are stateless and
  parameterless, and the processor holds only a provider. `/plan` may adjust
  lifetimes if it can show a stateless-ness violation.
- `[ASSUMED]` **`T5LineMapper`/`T4LineMapper` are not registered** — they are
  invoked as services by `CaseProcessing`, not resolved by the host.
- `[ASSUMED]` **Portal production references are `TaxLogicEngine` and
  `CaseProcessing` only.** `TaxCases` exposes only immutable values and static
  policy services (nothing to register), `DocumentExtraction` arrives
  transitively, and `CaseReview` is excluded on the taxpayer-data grounds above;
  none needs a direct reference in v1.
- `[ASSUMED]` **Composition root shape** — one `Program.cs` calling one
  registration extension in one file (working name `ModuleRegistration`); exact
  names are `/plan`'s to finalize.
- `[ASSUMED]` **Test approach** — in-process host testing (`WebApplicationFactory`
  over the web-test-host package) in the test project only, with no network port
  bound; `Program` exposed as a public partial class **solely** for testability
  and carrying no other public API.
- `[ASSUMED]` **Test tooling versions** match the five existing test projects
  (xunit 2.5.3, runner 2.5.3, Test.Sdk 17.8.0, coverlet 6.0.0), plus the 8.0.x
  in-process web test host.
- `[ASSUMED]` **No `Directory.Build.props`, no `global.json`, no central package
  management, no analyzer package, no CI workflow, no Dockerfile** — smallest
  useful foundation.
- `[ASSUMED]` **`Properties/launchSettings.json` may be tracked** with local
  development profiles only, no secret and no non-local binding (AC-41).
- `[ASSUMED]` **Documentation set** — new `src/TaxAI.Portal/CLAUDE.md` and
  pointer `AGENTS.md`; updates to `README.md`, root `CLAUDE.md`, and `NEXT.md`;
  and a `specs/010-portal-foundation/verification.md` AC-by-AC record following
  specs 005–009. **No existing module's instruction file is touched.**
- `[ASSUMED]` **Migration ordering** is the five-stage sequence in Migration
  constraints.
- `[ASSUMED]` **The README's stale "Solution (two projects)" line is corrected**
  as part of this feature and replaced with a count-free description, because this
  feature is what makes the project list change again.

No Class A assumption is made anywhere in this PRD. Every tax-semantic, PII,
tenant-isolation, and security-boundary question is either untouched by the
feature or explicitly excluded and listed above for the human.

---

## CRA sources

No external tax sources required; this feature introduces no tax facts.

---

## Definition of Done — self-check

**Both classes:**

- [x] `specs/010-portal-foundation/prd.md` exists with the required structure,
      opening with `## Feature classification` (Problem / User stories /
      Functional requirements / Out of scope / Acceptance criteria / Open
      questions / CRA sources)
- [x] Feature class named explicitly (`platform`) and justified; the boundary that
      would make it `tax-domain` is written down as an enforced gate (FR-18)
- [x] Reuse check performed and documented — `specs/*/prd.md` and the source tree
      were read directly; the reused engine and processor types are referenced and
      none re-specified; the five existing test suites, including the merged
      `CaseReview.Tests`, were read file-by-file to confirm no edit to them is
      required
- [x] Every AC is mechanically checkable by a single test
- [x] Zero Class A assumptions made — tax semantics, PII, tenant isolation, and
      security boundaries are all excluded from scope and surfaced (Open
      questions A–F), never assumed

**`platform` additionally:**

- [x] `Tax/CRA semantic impact: none` stated verbatim
- [x] Tax-year scope marked `Not applicable` (no tax semantics changed)
- [x] `## CRA sources` reads exactly `No external tax sources required; this
      feature introduces no tax facts.`
- [x] Current repository state, desired outcome, preserved behaviour, migration
      constraints, explicit out-of-scope, and rollback/recovery are all present
- [x] An AC asserts existing production behaviour and the full test suite remain
      green (AC-13), and that no tax mapping, calculation, or parser behaviour
      changed (AC-14, supported by AC-11, AC-15, AC-16)
- [x] No new application feature or empty speculative module invented — every new
      artifact is justified individually in the reuse analysis, and AC-12 forbids
      placeholder projects
- [x] Naming, folder structure, and migration ordering are tagged `[ASSUMED]`

**Result: ready for `/plan`.**
