# Spec 017 verification — Local End-to-End Accountant MVP

## Result

**PASS for every acceptance criterion, by executable evidence.** T04 adds the
vertical-slice, relational and repository proofs for T01 to T03 and changes no
product behaviour: it adds three test files, amends the README capability
guard, and writes documentation.

One real local run was performed and is recorded under [Local run](#local-run).
It drove the started application over HTTPS with a scripted client holding two
separate cookie sessions. **It was not performed in a rendering browser**, and
that difference is stated there rather than smoothed over.

Planning baseline: `7ee2574` (merge of PR #152, spec 016 T06). Feature class:
`tax-domain`, with no tax or CRA semantic impact. No CRA fact is asserted by
this specification or by this record.

The synthetic recipient-identifier values used by the tests and by the local
run are built at run time and are never written here.

## Evidence index

| ID | Executable evidence |
|---|---|
| E01 | `TaxAI.Portal.Tests/Startup/LocalDemoToolingTests.cs` runs `scripts/local/Start-LocalDemo.ps1 -SetupOnly` against a temporary settings file: the generated configuration starts a Development host, a second run keeps the keys and the content root, the three keys are distinct, nothing becomes tracked, nothing sensitive is printed, the launch profile binds loopback only, the OIDC requirement is unchanged, and `TrustServerCertificate=True` is still refused in Production and Staging. |
| E02 | `Persistence.Tests/Composition/DevelopmentLoopbackConnectionTests.cs` and the unmodified refusals in `ConnectionStringGuardTests.cs`: the allowance needs the explicit opt-in and a loopback data source, and explicit encryption and the host-name refusal still apply. |
| E03 | `TaxAI.Portal.Tests/Identity/DevelopmentSignInTests.cs`: the durable session, the normal principal, full revalidation, sign-out revocation, absence and indistinguishability outside Development, no seeding outside Development, no seed in a migration, antiforgery, no caller input honoured, fail-closed for an absent or inactive seeded user, and no parallel authorization model. |
| E04 | `Persistence.Tests/Composition/DevelopmentSeederTests.cs`: exactly one synthetic user and one active membership, credential-free and idempotent. |
| E05 | `DocumentIntelligence.Tests/DeterministicBridgeTests.cs`: the signature-then-header rule, every `Unsupported` arrangement, image signatures, the unchanged header-first form, and the unchanged package references. |
| E06 | `TaxAI.Portal.Tests/EndToEnd/UploadExtractionBridgeTests.cs`: each source travels the real multipart `POST /client-intake/documents`; year coherence fails closed; a header-first upload is rejected by the unchanged gate; the production adapter is byte-identical and does not name the header; a production profile naming the deterministic provider is refused. |
| E07 | `TaxAI.Portal.Tests/Startup/LocalDemoSampleTests.cs`: the generator run into a temporary directory produces ignored, usable samples; its recipient-identifier value is in no tracked file; the tooling holds no nine-digit literal; the medical sample carries no nine-digit run. |
| E08 | `TaxAI.Portal.Tests/CaseListTests.cs`, `Persistence.Tests/Repositories/IntakeCaseListTests.cs` and `ClientIntake.Tests/ContractTests.cs`: the list, its exact allowlist, firm isolation with no cross-firm signal, refusal before the store, fail-closed store failure, no search or paging input, the firm-required contract, the bound, the empty state, safe headers, the stored year, the exact route inventories, and presentation-only components. |
| E09 | `TaxAI.Portal.Tests/Extraction/WorkflowFeedbackTests.cs`: the four fixed notices for an in-scope refused correction or finalization, nothing appended, the other four mutations unchanged, a `NotFound` result keeping the empty `404`, the closed vocabulary, and value-free texts. The missing, foreign and invalid-selector `404` assertions are the baseline suites `DocumentExtractionSecurityAcceptanceTests.cs` and `ExtractionAuthorizationTests.cs`, unmodified. |
| E10 | `TaxAI.Portal.Tests/EndToEnd/LocalMvpVerticalSliceTests.cs` (T04): the real Development composition driven from the Development sign-in to `/reviews/{recordId}` by two browsers that request only an address a received response offered, plus the sign-in address and the one-time client link. Sources arrive only by the real multipart `POST`. It covers the T4 flow with a correction, the medical flow, an unaccepted medical document, the empty-slip and missing-box refusals, the zero-amount case, the comparison of the rendered review with the existing chain, the planted-identifier sweep over every response, address, cookie, header, log line and record, and the client-page scan. |
| E11 | `Persistence.Tests/EndToEnd/LocalMvpPersistenceTests.cs` (T04): the real intake and extraction workflows through `SqlIntakeStore`, `SqlExtractionStore` and `SqlReviewRecordRepository` with the real migrations applied; the firm-scoped list read; and a read of every column of every written row for the planted identifier, its grouped, spaced, masked and partial forms, and any other nine-digit run. |
| E12 | `TaxAI.Portal.Tests/EndToEnd/LocalMvpRepositoryTests.cs` (T04): baseline comparisons from `7ee2574` to the tree spec 017 produced, and source, route, manifest and documentation scans. |
| E13 | `TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs`: the README capability inventory, amended by T04 under the approved table to name the local demo, the Development-only sign-in and the firm-scoped case list, to keep disclaiming every unshipped surface, and to forbid a claim of deployment, export or accountant upload. |
| E14 | The gate, `pwsh ./scripts/Invoke-SolutionTests.ps1`, recorded under [Gate](#gate). |
| E15 | The real local run recorded under [Local run](#local-run). |

## Acceptance-criterion ledger

"Pass" means the criterion is satisfied by the executable evidence named in
that row.

### Primary vertical slice

| Criterion | Result | Evidence |
|---|---|---|
| AC-1 — The T4 flow completes over real HTTP | Pass | E10 `TheT4FlowCompletesFromSignInToTheReviewOutputThroughRenderedLinksAndForms`, `TheJourneyRunsOnTheRealDevelopmentCompositionAndTheDurableSessionPath`; E15 |
| AC-2 — The medical flow completes over real HTTP | Pass | E10 `TheMedicalFlowCompletesAndStaysOutOfTheReviewRecord`; E15 |
| AC-3 — Medical evidence does not gate finalization | Pass | E10 `AnUnacceptedMedicalDocumentDoesNotGateFinalization` |
| AC-4 — No direct content staging | Pass | E10 `ThisSuiteNeverStagesContentDirectly` |
| AC-5 — Navigation is complete | Pass | E10: the browser refuses any address no response offered; the step lists and the two given addresses are asserted exactly; `TheCaseListOffersTheCreatedCaseByNameAndStoredYear` |

### Local startup

| Criterion | Result | Evidence |
|---|---|---|
| AC-6 — Generated configuration starts a Development host | Pass | E01; E15 |
| AC-7 — Idempotent setup | Pass | E01 |
| AC-8 — The three generated keys are distinct | Pass | E01 |
| AC-9 — Nothing sensitive is tracked | Pass | E01 |
| AC-10 — The tool prints no sensitive value | Pass | E01; E15 |
| AC-11 — No startup migration | Pass | E12 `NoProductionProjectMigratesOrCreatesTheSchema` |
| AC-12 — Loopback binding only | Pass | E01 |
| AC-13 — OIDC requirement unchanged | Pass | E01 |

### Development sign-in

| Criterion | Result | Evidence |
|---|---|---|
| AC-14 — It uses the durable session path | Pass | E03; E10 |
| AC-15 — The principal is the normal one | Pass | E03 |
| AC-16 — R-1 still applies | Pass | E03 |
| AC-17 — Sign-out revokes | Pass | E03 |
| AC-18 — Seeding is exact and idempotent | Pass | E04 |
| AC-19 — Absent in Production | Pass | E03 |
| AC-20 — Absent in Staging | Pass | E03 |
| AC-21 — Indistinguishable from unmapped outside Development | Pass | E03 |
| AC-22 — No seeding outside Development | Pass | E03 |
| AC-23 — No seed in any migration | Pass | E03 |
| AC-24 — Antiforgery required | Pass | E03 |
| AC-25 — No caller input is honoured | Pass | E03 |
| AC-26 — Inactive seeded user fails closed | Pass | E03 |
| AC-27 — No parallel model | Pass | E03 |

### Local SQL

| Criterion | Result | Evidence |
|---|---|---|
| AC-28 — Accepted on the Development loopback path | Pass | E02; E15 |
| AC-29 — Refused in Production | Pass | E01 |
| AC-30 — Refused in Staging | Pass | E01 |
| AC-31 — Refused by default | Pass | E02 |
| AC-32 — Refused for a non-loopback data source | Pass | E02 |
| AC-33 — Explicit encryption still required | Pass | E02 |
| AC-34 — Host-name override still refused | Pass | E02 |
| AC-35 — Production validator unchanged | Pass | E12 `TheProductionValidatorLibraryIsUnchanged` |

### Deterministic bridge

| Criterion | Result | Evidence |
|---|---|---|
| AC-36 — The bridge format passes the unchanged gate and extracts | Pass | E06; E10; E15 |
| AC-37 — The upload gate is unchanged | Pass | E12 `TheUploadGateIsByteIdentical` |
| AC-38 — Tax-year isolation through the real path | Pass | E06 |
| AC-39 — Header without a signature is still rejected at upload | Pass | E06 |
| AC-40 — A PDF signature without the header is unsupported | Pass | E05; E06 |
| AC-41 — The header must immediately follow the signature line | Pass | E05 |
| AC-42 — Image signatures gain no bridge | Pass | E05 |
| AC-43 — The legacy form still works | Pass | E05 |
| AC-44 — The production adapter is untouched | Pass | E06; E12 `TheProductionAdmissionCodeAndItsTestsAreUnmodified` |
| AC-45 — No parsing dependency | Pass | E05; E12 `NoProjectFileOrSolutionEntryChanged` |
| AC-46 — The Development provider is still refused in Production | Pass | E06 |
| AC-47 — Empty slip fails closed through the bridge | Pass | E10 `AnEmptySlipFailsClosedThroughTheBridge` |
| AC-48 — Missing required box fails closed without a SIN signal | Pass | E10 `AMissingRequiredBoxFailsClosedWithoutNamingTheBoxOrTheIdentifier` |
| AC-49 — Zero amount and absent optional boxes pass unchanged | Pass | E10 `AZeroAmountAndNoOptionalBoxRenderExactlyWhatTheExistingChainProduces` |

### Samples and SIN hygiene

| Criterion | Result | Evidence |
|---|---|---|
| AC-50 — Samples are ignored by git | Pass | E07 |
| AC-51 — The generated Box 12 value is in no tracked file | Pass | E07 |
| AC-52 — The generator holds no nine-digit literal | Pass | E07 |
| AC-53 — The generated sample is usable | Pass | E07; E15 |
| AC-54 — The medical sample carries no SIN shape | Pass | E07 |
| AC-55 — Documentation carries no SIN shape | Pass | E12 `TheGuideTheReadmeAndTheSpecFolderCarryNoNineDigitRun` |
| AC-56 — No SIN in any structured surface, end to end | Pass | E10 `ThePlantedIdentifierReachesNoResponseAddressCookieHeaderLogOrRecord` (HTTP, logs, records); E11 `ThePlantedIdentifierReachesNoColumnOfAnyRowTheJourneyWrote` (every SQL column); E15 |
| AC-57 — Existing SIN/PII guards are intact | Pass | E12 `EveryBaselineSinAndPiiGuardStillExistsUnderItsName`, `NoGuardFileChangedExceptByAnApprovedAmendment`; E14 |

### Case list

| Criterion | Result | Evidence |
|---|---|---|
| AC-58 — The list shows the firm's cases | Pass | E08 |
| AC-59 — The allowlist is exact | Pass | E08 |
| AC-60 — Firm isolation | Pass | E08; E11 |
| AC-61 — No cross-firm existence signal | Pass | E08 |
| AC-62 — Caller-supplied firm is ignored | Pass | E08 |
| AC-63 — Unauthenticated is refused before the store | Pass | E08 |
| AC-64 — No active firm is refused before the store | Pass | E08 |
| AC-65 — Revoked session and inactive membership are refused before the store | Pass | E08 |
| AC-66 — Store failure fails closed | Pass | E08 |
| AC-67 — No search or paging input | Pass | E08 |
| AC-68 — The contract is firm-required | Pass | E08 |
| AC-69 — The bound holds | Pass | E08 |
| AC-70 — Empty state | Pass | E08; E10 |
| AC-71 — Safe headers and no JSON | Pass | E08 |
| AC-72 — The stored year is rendered | Pass | E08; E10 |

### Navigation and feedback

| Criterion | Result | Evidence |
|---|---|---|
| AC-73 — Clients see no accountant navigation | Pass | E10 `NoClientResponseOffersAnAccountantAddressOrControl` |
| AC-74 — In-scope refused correction gives a notice | Pass | E09 |
| AC-75 — In-scope refused finalization gives a notice | Pass | E09; E10 (finalizing with no accepted T4) |
| AC-76 — Safe `404` is unchanged | Pass | E09: the baseline suites, unmodified |
| AC-76a — The other four mutations are unchanged | Pass | E09 |
| AC-76b — A `NotFound` workflow result keeps the `404` | Pass | E09 |
| AC-77 — Notices are the closed approved vocabulary | Pass | E09 |
| AC-78 — Notices carry no data | Pass | E09 |
| AC-79 — Preserved surfaces are byte-identical | Pass | E12 `TheReviewSurfaceAndTheFixedShellContentAreByteIdentical`, `TheFixedResponsesAreExactlyTheUnchangedConstants` |

### Route, component, and dependency inventory

| Criterion | Result | Evidence |
|---|---|---|
| AC-80 — The route inventory is exact | Pass | E08 |
| AC-81 — No accountant upload exists | Pass | E12 `NoFirmScopedRouteAcceptsADocumentAndTheOnlyMultipartRouteIsTheClientUpload` |
| AC-82 — Components stay presentation-only | Pass | E08 |
| AC-83 — References and packages are unchanged | Pass | E12 `NoProjectFileOrSolutionEntryChanged` |
| AC-84 — No background initiator | Pass | E12 `TheFeatureRegistersNoBackgroundInitiator` |

### Tax behaviour, data-driven mapping, and the gate

| Criterion | Result | Evidence |
|---|---|---|
| AC-85 — No tax file changed | Pass | E12 `NoTaxFileOrTaxTestChanged` (ten trees) |
| AC-86 — Rounding is untouched | Pass | E12 `TheRoundingHelperAndItsMidpointTestsAreUntouched`; E14 |
| AC-87 — No hard-coded CRA line or mapping | Pass | E12 `TheFeatureAddsNoCraLineMappingArithmeticOrRounding` |
| AC-88 — The review output equals the engine's output | Pass | E10 `TheReviewOutputEqualsWhatTheExistingChainProducesForTheCorrectedPayload` |
| AC-89 — Spec 016 is untouched | Pass | E12 `Spec016IsUntouchedAndGainedNoStatusMarker` |
| AC-90 — Production admission is still refused | Pass | E12 `TheProductionAdmissionCodeAndItsTestsAreUnmodified`; E14 |
| AC-91 — The full gate is green | Pass | E14 |
| AC-92 — No floor is lowered | Pass | E12 `NoTestFloorWasLoweredAndNoProjectWasRemoved` |
| AC-93 — The demo guide exists and is clean | Pass | E12 `TheDemoGuideNamesTheOneCommandAndTheTwelveSteps`, `TheDemoGuideCarriesNoKeyCredentialOrConnectionString` |

## How the baseline comparisons are bounded

"No tax file changed" is a claim about the work of spec 017, so
`LocalMvpRepositoryTests` compares the baseline `7ee2574` with **the tree spec
017 produced**: the commit that recorded this specification's final completion
marker, or the working tree (untracked files included) while that marker is not
yet committed. While T04 was being written and gated, every comparison ran
against the working tree. Once the marker is committed, the same comparisons
describe that commit, so a later specification that legitimately changes a tax
file, a project file or the solution is not blocked by this one. This is the
bounding spec 007's repository suite already uses.

Rules that are not historical are asserted against the working tree always: no
startup migration, no nine-digit run in the documentation, no lowered floor, no
accountant upload route, and the demo guide's content.

## Gate

```powershell
dotnet build TaxAI.sln --no-incremental
pwsh ./scripts/Invoke-SolutionTests.ps1
```

Run on Windows in the default configuration, on the final T04 tree. Build: 0
errors. Test execution gate passed: every registered project ran, nothing
failed, nothing was skipped, and every floor was met.

| Project | Passed | Floor |
|---|---:|---:|
| TaxLogicEngine.Tests | 291 | 291 |
| TaxCases.Tests | 340 | 340 |
| DocumentExtraction.Tests | 676 | 676 |
| CaseProcessing.Tests | 609 | 609 |
| CaseReview.Tests | 482 | 482 |
| TaxAI.Portal.Tests | 1357 | 1357 (raised from 1310) |
| Tenancy.Tests | 395 | 395 |
| Persistence.Tests | 349 | 349 (raised from 347) |
| ClientIntake.Tests | 192 | 192 |
| ExtractionWorkflow.Tests | 360 | 360 |
| DocumentIntelligence.Tests | 259 | 259 |
| TaxAI.AzureInfrastructure.Tests | 528 | 528 |

Total: 5,838 passed. No floor was lowered. T04 added 47 portal tests (12
vertical-slice, 35 repository) and 2 relational tests.

The new portal suites were also run six further times in the Release
configuration with identical results, to look for a chance failure in the
planted-value scans; none occurred.

## Local run

One run, on 2026-10-01, on the developer machine (Windows 11, SQL Server
LocalDB), from the T04 tree.

**Start.** `pwsh ./scripts/local/Start-LocalDemo.ps1`, the one command, with no
argument. It restored the tools, started LocalDB, applied the repository-owned
migrations, reused the existing generated settings and samples, and started the
host. The host logged `Now listening on: https://localhost:7199` and
`Hosting environment: Development`. The seeder's idempotent user and membership
statements ran. A host left running from the pre-T03 build was stopped first, so
the run exercised the current code.

**How it was driven.** By a scripted HTTPS client against the running host, with
two separate cookie containers: one for the accountant and one for the client.
Each step used the address and the antiforgery token the previous response
rendered. The two generated files in `artifacts/local-demo/` were sent by path
through the real multipart upload route; their content was never read into a
transcript or printed.

| Step | Request | Result |
|---|---|---|
| 2 | Development sign-in page, then the sign-in form | `200`; landed on `/intake-cases` |
| 3 | Case list, create page | `200` |
| 3-4 | Create a case (one T4, one medical document) | `200`; the one-time link was shown, on the `https://localhost:7199` origin |
| 5 | Client link in the second session, then redeem | `200`; landed on `/client-intake` |
| 5 | Answers | `200` |
| 5 | Upload the generated `t4-sample.pdf` | `200` |
| 5 | Upload the generated `medical-sample.pdf` | `200` |
| 5 | Submit the intake | `200` |
| 6 | Intake progress, case documents | `200`; the new case was listed and two documents were offered |
| 6-7 | Run extraction on the T4, read the extraction review | `200`; automated classification `T4Slip` |
| 8-9 | Correct box 14 | `200`; latest version 2; the corrected value was shown |
| 10 | Accept the T4 | `200`; acceptance `AcceptedByAccountant` |
| 6-7, 10 | Run extraction on the medical document and accept it | `200`; the medical evidence panel was shown |
| 11-12 | Finalize; follow the redirect | `200`; landed on `/reviews/{recordId}` |

The review output showed one slip, box 14 with the corrected amount, and one tax
line. Nothing medical appeared on it.

**Confinement, checked on the real run.** The generated recipient-identifier
value was read into memory by the checking script and compared; only the
outcome was printed.

- 32 captured responses (bodies, headers, cookies, addresses): the value and its
  grouped and spaced forms were absent.
- The host's own log for the whole run (EF command logging included): the value
  was absent, there was no standalone nine-digit run, and no key or connection
  value appeared.
- The local database: 33 tables read, 29 with rows, 1,235 cells. No cell held
  the value, and no non-identifier cell held a nine-digit run.
- The content directory: 8 stored objects, none readable as plain text.

**What this run was not.** It was not performed in a rendering browser. The
machine's .NET HTTPS development certificate is not trusted, so the built-in
browser refused `https://localhost:7199`, and trusting that certificate is a
machine security setting this task does not change. Three things were therefore
**not observed by this run**: the pages as rendered and styled; the tracked
upload module running in a browser; and the questionnaire's interactive panel
revealing the upload slots after an answer is chosen. The first is presentation
only. The second and third are exercised in process by E10, which reads the
upload address and header name out of the served module and uses the upload
forms the server renders, but no test runs the script itself. A person following
`docs/local-demo.md` in two browser windows covers all three.

**Observed in passing, and not changed.** On SQL Server the review output shows
amounts at the stored scale, with four decimal places, where the in-process
suites show two. The value is the same; this is the existing spec 012 rule of
rendering a stored amount exactly as stored, and T04 changes no product
behaviour. It is recorded here for a human decision.

The three earlier attempts of the checking script stopped at the case-documents
step because of a mistake in the script's own pattern for a list item. Each left
one unused synthetic case in the local database; the application returned no
unexpected response in any of them.

## Guard amendments made by T04

Approved PRD table:

- `tests/TaxAI.Portal.Tests/EndToEnd/RepositoryVerificationTests.cs` — the
  README capability inventory. `TheReadmeNamesTheFoundationCapabilitiesThatShip`
  gains exactly the local demo, the Development-only sign-in, the firm-scoped
  case list and the guide's path. In
  `TheReadmeExplicitlyDisclaimsEveryUnshippedSurface`, the line "No taxpayer
  listing or unapproved taxpayer-data surface" is replaced, because the approved
  firm-scoped case list made it false, by the exact statements that remain true
  (no taxpayer search or cross-firm listing; no search, filter, sort, paging or
  global view; no accountant-side document upload; a local run is never
  production authorization; no real taxpayer document locally), and the list of
  forbidden claims gains accountant upload, export, deployment, taxpayer search
  and real-document reading. Both test method names are kept.

Authorized X-items: **X-7 was not used.**
`tests/TaxAI.Portal.Tests/EndToEnd/DocumentExtractionAcceptanceTests.cs` is not
edited; the vertical-slice suite needed no helper added to it.

`AmendmentAuditTests.cs` is not edited. No test was removed, skipped or
weakened.

## `[ASSUMED]` decisions (Class B, open to one-pass override)

- The baseline comparisons are bounded to the tree spec 017 produced, as
  described above, rather than pinned to the working tree forever.
- AC-83 is asserted as byte identity of every project file, `TaxAI.sln`,
  `global.json` and the tool manifest, which is stronger than the criterion.
- AC-87 is asserted over the lines the feature added, not over whole files: the
  Development provider already transported a confidence value before spec 017.
  A printed sample amount with its cents is not treated as a CRA line literal.
- For the nine-digit scan of HTTP captures, random opaque material (Blazor's
  protected prerender markers, and any unbroken run of thirty-two or more token
  characters such as an opaque identifier, an antiforgery token, a protected
  cookie or the one-time credential) is removed first; the planted value itself
  and its grouped and spaced forms are checked against the raw text. In the
  relational scan, a cell that is exactly an opaque identifier is exempt from
  the generic nine-digit check only, never from the planted-value check.
- The guide documents trusting the HTTPS development certificate as a
  prerequisite; the tool does not change that machine setting.

## Scope confirmation

T04 changed no file under `src/`, `scripts/local/`, `infra/`, or any project
file, migration or `TaxAI.sln`. The files it touched are the three new test
files, `RepositoryVerificationTests.cs`, `docs/local-demo.md`, `README.md`,
`NEXT.md`, `CLAUDE.md`, `scripts/test-projects.json`, this file, and its own
completion marker. `tasks.md` was not edited.
