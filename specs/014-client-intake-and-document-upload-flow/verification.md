# Verification 014 — Client Intake and Document Upload Flow

## Result

Spec 014 is complete across four merge units: T01 froze the `ClientIntake`
contract and migrated the review screen onto a static-SSR Blazor foundation; T02
implemented the secure backend, the firm/case-scoped SQL store, one additive
migration, and the Development-only encrypted content adapter; T03 mapped the
accountant and client HTTP surface and rendered every successful screen through
strongly typed components; T04 added only cross-module acceptance and security
tests and this evidence record.

**T04 changed no production code.** It touched three new test files, two existing
test files, the test manifest, the three active guidance documents, and this
file.

## What T04 exercises that no earlier task could

Only after T01–T03 merged can one test drive the real composition end to end.
Two suites do it, from opposite ends:

- **`tests/Persistence.Tests/EndToEnd/ClientIntakeWorkflowPersistenceTests.cs`** —
  the real `AccountantIntakeWorkflow` and `ClientIntakeWorkflow` driving the real
  `SqlIntakeStore` and `SqlReviewRecordRepository`, through the approved
  `InternalsVisibleTo("Persistence.Tests")` seam, against a real relational
  database with the real `20260730000000_AddClientIntake` migration applied. Each
  store call gets its own context, the way the portal's scoped registration does,
  so no EF identity map can answer a read the database never saw. Only the
  credential source, the content adapter, and the clock are supplied by the test,
  because randomness, key material, and time are the workflow's only
  non-deterministic inputs.
- **`tests/TaxAI.Portal.Tests/EndToEnd/ClientIntakeAcceptanceTests.cs`** and
  **`ClientIntakeSecurityAcceptanceTests.cs`** — the real portal composition over
  HTTP: routing, rate limiting, authentication, R-1 authorization, antiforgery,
  the cookie protector, the multipart transport, both real workflows, the real
  `CryptographicIntakeCredentialSource`, the real
  `DevelopmentEncryptedIntakeContentStore`, and every Razor component. Two firms
  run over **one shared** intake store, so a cross-firm read is a real cross-firm
  read rather than a lookup in an empty dictionary.

Only two collaborators are substituted in the portal suite — the intake metadata
store and the review-record repository — and for one reason: the real ones are
SQL Server. Those exact adapters are the ones exercised for real in the
persistence suite. Between the two suites, no part of the shipped path is
represented by a double in both.

## Synthetic data statement

Every taxpayer-shaped value in the T04 suites — display names, source bytes,
original filenames, and every SIN-shaped probe — is assembled in memory at test
runtime and is obviously synthetic. No probe is a real identifier, and none is
checked in as a fixture literal.

## Acceptance evidence

`P` = `Persistence.Tests`, `C` = `ClientIntake.Tests`, `W` = `TaxAI.Portal.Tests`.
T04's own files are named in full; earlier tasks' suites are named by class.

### Happy path and reuse

| Criterion | Evidence |
|---|---|
| AC-1 | `ClientIntakeAcceptanceTests.AnAccountantCreatesACaseAClientCompletesItAndTheReviewOpens` asserts one case under firm F with the requested category counts, one active link digest, and the raw identifier-free URL returned once. `ClientIntakeWorkflowPersistenceTests.TheCompleteFlowReachesReadyThroughTheRealRelationalAdapter` asserts the same against the real relational store. |
| AC-2 | Both files above run redeem → answer → upload (PDF and PNG) → submit and assert one immutable submission at version 1 referencing exactly those answer and document versions, with reused `TaxCase.Readiness` = `Ready`. |
| AC-3 | `ClientIntakeAcceptanceTests` asserts the rendered progress page carries the display name, 2025, expected counts, `Ready` item statuses, `2 of 2 answered`, `Version 1`, unsubmitted-changes state, link state, and no excluded identifier; `EveryServerIdentifier()` is asserted absent. |

### Case, checklist, and year fail-closed rules

| Criterion | Evidence |
|---|---|
| AC-4 | `ClientIntakeAcceptanceTests.CaseCreationRefusesBeforeStorageWhenTheCallerHasNoTrustedFirm` deactivates the membership and asserts a `403` before the workflow, with the store count unchanged. `W:IntakeAuthorizationTests` covers the policy matrix. |
| AC-5 | The same test posts firm G in two extra form fields and asserts the case is created under firm F, that no firm-G case exists, and — by `C:ContractTests` — that no command record declares a firm member at all. |
| AC-6 | `ClientIntakeAcceptanceTests.InvalidCreateInputFailsClosedAndStoresNothing` covers blank and whitespace display names; `C:IntakeDisplayNameGuardTests` covers the guard directly. |
| AC-7 | The same theory covers absent, 2024, and 2026 in separate requests; each fails closed and none is coerced to 2025. |
| AC-8 | The same theory covers T4 count 0 with medical count 0. |
| AC-9 | The same theory covers negative, missing, and 26-item counts; `C:IntakeCaseFactoryTests` covers 0–25 per FR-4. |
| AC-10 | `ClientIntakeAcceptanceTests.TheClientPageIsAdaptiveAndRefusesAnUnrequestedCategory` posts a medical answer and a medical upload to a T4-only case: both refuse and store nothing. `C:ClientIntakeWorkflowTests` covers `Other` and undefined categories. |
| AC-11 | `C:AccountantIntakeWorkflowTests` and `P:IntakeStoreTests` prove the case, checklist, link, and opening audit events commit in one transaction, so a link-digest failure leaves no reachable case, link, metadata, or creation event. |

### Link and replay fail-closed rules

| Criterion | Evidence |
|---|---|
| AC-12 | `ClientIntakeAcceptanceTests` parses the issued URL and asserts the fixed origin and `/client-intake/link/` prefix, an empty query and fragment, and that no firm, user, membership, participant, or content identifier appears in it. |
| AC-13 | `ClientIntakeWorkflowPersistenceTests.TheRawLinkCredentialIsNeverPersistedInAnyForm` reads **every value of every intake table** out of the database and asserts the raw credential is absent, then asserts the stored 32-byte value equals the keyed digest of that credential. |
| AC-14 | `ClientIntakeAcceptanceTests.TheRawClientUrlIsShownOnceAndIsUnrecoverableAfterwards` proves progress cannot recover the URL and that reissue returns a different credential which invalidates the old one. `ClientIntakeWorkflowPersistenceTests.ReissueMintsANewCredentialAndInvalidatesTheDerivedSession` proves the same at the store. |
| AC-15 | `W:IntakeConfigurationTests` and `W:StartupRefusalTests` start hosts with absent, zero, negative, and non-finite lifetimes and assert each refuses to start. |
| AC-16 | `C:ClientIntakeWorkflowTests` covers the last instant before expiry and exactly at expiry; `ClientIntakeWorkflowPersistenceTests.ARevokedOrAlreadySpentLinkRefusesRedemptionIdentically` covers the store-side boundary. |
| AC-17 | `P:IntakeStoreConcurrencyTests` proves exactly one session survives two concurrent redemptions. `ClientIntakeWorkflowPersistenceTests.ARevokedOrAlreadySpentLinkRefusesRedemptionIdentically` proves the losing replay is byte-identical to an unknown link and that exactly one session row exists. |
| AC-18 | `ClientIntakeSecurityAcceptanceTests.EveryInvalidClientResourceProducesTheSameEmptyRefusal` includes a revoked link among six refusal cases and asserts identical status, empty body, and identical header sets. |
| AC-19 | `ClientIntakeWorkflowPersistenceTests.ReissueMintsANewCredentialAndInvalidatesTheDerivedSession` asserts the old link and the derived session both fail, one active link remains, and both prior link rows are retained. |
| AC-20 | `ClientIntakeAcceptanceTests` and `IntakeFlow.RedeemAsync` assert the confirmation GET creates no session, the raw credential is absent from the confirmation HTML/action/hidden field, the POST carries only a protected handle, and the redirect is a single `303` to the fixed clean `/client-intake`. |
| AC-21 | The same flow asserts the session cookie is `HttpOnly`, `Secure`, `SameSite=Strict`, path-scoped, contains no raw credential, and contains none of `EveryServerIdentifier()`. `W:ClientIntakePageTests` pins the cookie attributes directly. |
| AC-22 | `ClientIntakeSecurityAcceptanceTests.ARevokedClientSessionRefusesTheNextOperation` and the shared refusal case cover revoked and missing sessions; `C:ClientIntakeWorkflowTests` covers idle-, absolute-, and link-expired sessions. |
| AC-23 | `ClientIntakeSecurityAcceptanceTests.AFailingIntakeStoreFailsClosedWithoutATaxpayerResponse` makes the store throw and asserts an empty `404` with no display name and no diagnostic text. |

### Questionnaire and immutable history

| Criterion | Evidence |
|---|---|
| AC-24 | `ClientIntakeAcceptanceTests.TheClientPageIsAdaptiveAndRefusesAnUnrequestedCategory` renders a T4-only case and asserts the T4 question and both T4 slots appear and no medical question or slot does. |
| AC-25 | `W:ClientIntakePageTests` renders the medical-only case; `C:IntakeProjectionTests` pins the adaptive question set. |
| AC-26 | `ClientIntakeAcceptanceTests.EverySupportedAnswerCodeIsAcceptedAndEveryOtherValueIsRefused` accepts each of the three codes and refuses free text, an unknown code, a blank value, a lower-cased value, and a numeric value, asserting no version is appended. |
| AC-27 | `ClientIntakeAcceptanceTests.TheClientPageIsAdaptiveAndRefusesAnUnrequestedCategory` posts an unrequested medical answer and upload and asserts the safe empty `404` and no stored answer or document. |
| AC-28 | `ClientIntakeWorkflowPersistenceTests.AnswerAndSubmissionHistoryIsAppendedRatherThanRewritten` reads both answer rows and asserts version 1 still holds its original code and version 2 the new one. |
| AC-29 | `C:ClientIntakeWorkflowTests` covers a duplicated category in one request; the workflow refuses before any version is appended. |
| AC-30 | The persistence immutability test asserts submission 1 still references answer version 1 and submission 2 references version 2. |
| AC-31 | `P:IntakeStoreConcurrencyTests` proves exactly one snapshot survives two concurrent next-version attempts and the loser leaves no partial references. |
| AC-32 | `ClientIntakeWorkflowPersistenceTests.AnUnavailableItemProjectsMissingAndKeepsTheCaseNotReady` submits with one item declared unavailable: the snapshot is created, that item projects `missing`, and readiness is `NotReady`. |

### Upload validation and duplicate detection

| Criterion | Evidence |
|---|---|
| AC-33 | `ClientIntakeAcceptanceTests.EachConfiguredMediaKindIsAcceptedWhenItsSignatureMatches` uploads a runtime-generated PDF, JPEG, and PNG in separate cases and asserts one document record with the expected media kind each time. |
| AC-34 | `ClientIntakeAcceptanceTests.EmptyUnsupportedAndSignatureMismatchedUploadsAreRejected` posts zero bytes, asserts refusal, asserts no document record, and asserts the item renders `Rejected`. |
| AC-35 | The same test posts an archive and an executable signature, each declared as PDF; both are refused with the fixed invalid body and no source bytes remain reachable. |
| AC-36 | The same test posts a real PNG declared as a PDF and asserts refusal with no document record. `C:IntakeUploadValidatorTests` covers the signature table directly. |
| AC-37 | `W:UploadEndpointTests` posts a malformed multipart body and asserts the fixed error with no state change and no attributable audit event. |
| AC-38 | `ClientIntakeAcceptanceTests.TheConfiguredByteAndFileCountLimitsAreEnforced` uploads exactly the configured maximum (accepted, byte count asserted) and one byte over (refused, no retained content). |
| AC-39 | The same test posts two file sections against a one-file-per-request limit and asserts refusal before persistence. |
| AC-40 | `ClientIntakeSecurityAcceptanceTests.SinProbesAndFilenamesSurviveOnlyInsideTheEncryptedObject` plants a SIN-shaped, script-bearing original filename on the wire and asserts it appears in no response, HTML, header, stored metadata, content key, audit record, or log. |
| AC-41 | `ClientIntakeAcceptanceTests.ASecondIdenticalUploadIsRejectedAsADuplicate` and `ClientIntakeWorkflowPersistenceTests.IdenticalBytesDuplicateWithinACaseAndNeverCorrelateAcrossScopes` both assert the second identical upload is refused as a duplicate with no second record. |
| AC-42 | `ClientIntakeWorkflowPersistenceTests.DuplicateDetectionUsesContentRatherThanTheOriginalFilename` accepts distinct bytes that the transport never names, proving detection is content-based. |
| AC-43 | The persistence duplicate test uploads identical bytes to a second case in the same firm and a case in another firm, asserts both are accepted, and asserts the three scope-derived fingerprints are all different. `ClientIntakeSecurityAcceptanceTests.IdenticalBytesInTwoFirmsRevealNothingAboutEachOther` asserts the two HTTP responses are indistinguishable and that neither carries the other scope's identifiers. |
| AC-44 | `P:IntakeStoreConcurrencyTests` proves exactly one document object survives two concurrent identical uploads and the other attempt receives the fixed duplicate result. |
| AC-45 | `C:ClientIntakeWorkflowTests` fails the content store after streaming and asserts no accepted metadata, no `ReceivedEvidence`, and no ready status. |
| AC-46 | `C:ClientIntakeWorkflowTests` and `W:DevelopmentEncryptedIntakeContentStoreTests` prove the unreferenced object is removed after a metadata failure and that diagnostics carry only fixed structural facts. |
| AC-47 | `ClientIntakeSecurityAcceptanceTests.SinProbesAndFilenamesSurviveOnlyInsideTheEncryptedObject` uploads a runtime-assembled body containing names, a SIN-shaped probe, amounts, `-1.005`/`1.005`, and T4 box-like text, then asserts none appears in any DTO, HTML, stored metadata, audit record, or log, and that no parsed field or monetary result exists. `ClientIntakeSecurityAcceptanceTests.NoExtractionParserMapperOrReviewAssemblerIsInTheRuntimeGraph` asserts no rounding or arithmetic service is registered. |

### Status and readiness

| Criterion | Evidence |
|---|---|
| AC-48 | `ClientIntakeAcceptanceTests.PerItemStatusFollowsTheOneFixedPrecedenceRule` asserts `Requested` before any answer, upload, or submission. |
| AC-49 | The same test asserts `Uploaded` for a valid upload not yet referenced by a submission. |
| AC-50 | `ClientIntakeAcceptanceTests.EmptyUnsupportedAndSignatureMismatchedUploadsAreRejected` asserts `Rejected` after a failed attempt and that every recorded attempt has outcome `Rejected` with no stored bytes. |
| AC-51 | `ClientIntakeWorkflowPersistenceTests.AnUnavailableItemProjectsMissingAndKeepsTheCaseNotReady` asserts `Missing` and `NotReady`. |
| AC-52 | Both happy-path tests assert `Ready` for every item referenced by the latest submission. |
| AC-53 | `C:IntakeProjectionTests` covers the two-T4/one-medical checklist against a one-of-each submission: medical ready, the unsatisfied T4 still requested, case `NotReady`. |
| AC-54 | `C:IntakeProjectionTests` covers two medical documents against a one-T4/one-medical checklist: medical satisfied, T4 requested, `NotReady`. |
| AC-55 | The happy-path tests carry readiness from `NotReady` to `Ready` when the final document enters a new submission; no writable ready flag exists, asserted by the model scan in `ClientIntakeWorkflowPersistenceTests.TheCompleteFlowReachesReadyThroughTheRealRelationalAdapter`. |
| AC-56 | `ClientIntakeAcceptanceTests.PerItemStatusFollowsTheOneFixedPrecedenceRule` saves a replacement upload after a ready submission and asserts the item stays `Ready`, the page reports unsubmitted changes, and the stored submission is unchanged at version 1. |

### Tenant isolation and safe not-found

| Criterion | Evidence |
|---|---|
| AC-57 | `P:TenantIsolationSchemaTests` and `P:ModelShapeTests` enumerate every intake entity and assert non-null server-derived firm and case keys, with the two FR-15 lookups returning rather than accepting them. |
| AC-58 | `ClientIntakeWorkflowPersistenceTests.OnlyTheTwoDigestLookupsOmitATrustedFirmOrDerivedScope` reflects over `IIntakeStore` and asserts exactly `FindLinkScopeAsync` and `FindClientScopeAsync` omit a trusted firm or derived scope, and that neither accepts a `TrustedFirmId`. |
| AC-59 | `ClientIntakeSecurityAcceptanceTests.ForeignFirmAndForeignCaseSelectorsAreByteIdenticalToMissing` compares status, body, and normalized headers for a foreign-firm case and a missing case, then proves the same case is still readable by its owner. `ClientIntakeWorkflowPersistenceTests.AForeignFirmSelectorIsIndistinguishableFromAMissingCase` proves the store never returns the foreign row. |
| AC-60 | `ClientIntakeAcceptanceTests.TheClientPageIsAdaptiveAndRefusesAnUnrequestedCategory` and `W:ClientIntakeSecurityTests` post foreign item and document selectors and assert the same empty `404` with no read or mutation. |
| AC-61 | `ClientIntakeSecurityAcceptanceTests.EveryInvalidClientResourceProducesTheSameEmptyRefusal` compares malformed, wrong-length, random, replayed-spent, revoked, and missing-session cases and asserts a single distinct header set across all six, with nothing mutated. |
| AC-62 | `W:ClientIntakeSecurityTests` exceeds the configured public partition with both valid and invalid credentials and asserts the same fixed `429`, with neither redeemed nor audited. |
| AC-63 | `W:UploadEndpointTests` and `W:IntakeAuthorizationTests` post every state-changing route without a valid antiforgery token and assert no case, link, answer, document, submission, status change, or audit event is created. |

### PII, browser, and audit

| Criterion | Evidence |
|---|---|
| AC-64 | `ClientIntakeSecurityAcceptanceTests.SinProbesAndFilenamesSurviveOnlyInsideTheEncryptedObject` scans browser bodies, response headers, stored metadata, content keys, audit records, and captured logs for six SIN shapes and finds none. `ClientIntakeWorkflowPersistenceTests.NoSinShapeOrFilenameReachesAnyStoredIntakeValue` runs the same scan over every value of every intake table. |
| AC-65 | The same two tests assert the probe **is** still present inside the staged object — proving the scan is measuring omission rather than an empty store — and this document states explicitly that the source document is not claimed SIN-free. |
| AC-66 | `ClientIntakeSecurityAcceptanceTests.NoBrowserModelDeclaresASinOrExcludedIdentifierField` and `ClientIntakeWorkflowPersistenceTests.NoIntakeColumnDeclaresASinOrMaskingConcept` enumerate browser DTO properties and EF properties and assert no SIN, partial-SIN, presence-flag, masking, redaction, filename, or content member. |
| AC-67 | `W:ClientIntakePageTests` and the acceptance happy path assert the FR-39 fields are present and that no identity, taxpayer, or storage field is. `ClientIntakeSecurityAcceptanceTests` additionally asserts the client projections declare no `…Id` property at all. |
| AC-68 | `W:IntakePageTests` renders a display name containing HTML metacharacters and asserts contextual encoding plus the FR-38 field set with no raw content or excluded identifier. |
| AC-69 | `ClientIntakeWorkflowPersistenceTests.TheAuditTrailIsAppendOnlyOrderedAndStructuralOnly` asserts ordered sequences, the expected event kinds for the happy path, one firm per event, and the exact nine-property audit entity — no free text, filename, digest, fingerprint, or amount. |
| AC-70 | The same test presents a random invalid token and asserts the audit row count is unchanged. |

### Review handoff

| Criterion | Evidence |
|---|---|
| AC-71 | `ClientIntakeAcceptanceTests.HandoffShowsNoReviewLinkWithoutBothReadinessAndAnExactRecord` asserts the fixed awaiting state, no review link, and `SaveCalls == 0`. |
| AC-72 | The same test asserts a `NotReady` intake with an exact same-firm/same-case record shows no review link and mutates neither artifact. |
| AC-73 | `ClientIntakeAcceptanceTests.AnAccountantCreatesACaseAClientCompletesItAndTheReviewOpens` associates one independently created record, asserts one link to the exact `/reviews/{recordId:guid}`, and then opens that route and asserts spec 012 behaviour is preserved. `ClientIntakeWorkflowPersistenceTests.OneExactExistingReviewRecordIsAssociatedOnceAndNeverReplaced` asserts one immutable association row. |
| AC-74 | `ClientIntakeAcceptanceTests.AnInvalidOrRepeatedHandoffAssociationIsRefusedSafely` submits blank, malformed, empty-GUID, missing, foreign-firm, and wrong-case selectors and asserts the fixed safe failure with no association and no fallback selection. `ClientIntakeWorkflowPersistenceTests.AForeignOrWrongCaseReviewRecordIsRefusedAndAssociatesNothing` asserts the same at the store and that only the two records the test saved exist. |
| AC-75 | The recording review repository **throws** on `SaveAsync`, so any manufacture fails the suite; `SaveCalls == 0` is asserted after every flow. `ClientIntakeSecurityAcceptanceTests.NoExtractionParserMapperOrReviewAssemblerIsInTheRuntimeGraph` asserts the compiled portal references neither `CaseReview` nor `DocumentExtraction` and that no extraction, parser, mapper, assembler, or rounding service is registered. |

### Tax-domain boundaries and regression

| Criterion | Evidence |
|---|---|
| AC-76 | `ClientIntakeAcceptanceTests.InvalidCreateInputFailsClosedAndStoresNothing` covers 2024 and 2026; `C:AccountantIntakeWorkflowTests` and `C:ClientIntakeWorkflowTests` cover adjacent-year answers, documents, submissions, and associations, all failing closed with no adjacent-year policy. |
| AC-77 | `ClientIntakeSecurityAcceptanceTests.NoIntakeProductionSourceNamesACraLineBoxTableOrRounding` scans every `ClientIntake` source file and every intake-named portal file for `MoneyRounding`, `Math.Round`, `MidpointRounding`, line-map types, `RecipientIdentifier`, `double`, `float`, and five-digit literals. `SolutionAndRegressionTests.NoExtractionOcrLlmOrFilingCapabilityIsNamedByTheNewSurface` extends the scan to extraction, OCR, LLM, Azure, and filing capability names. |
| AC-78 | `ClientIntakeSecurityAcceptanceTests.SinProbesAndFilenamesSurviveOnlyInsideTheEncryptedObject` uploads a body containing `-1.005`, `1.005`, and box-like text and proves none of it is parsed, surfaced, or rounded; the source scan above proves no rounding or required-box rule exists to evaluate. |
| AC-79 | `pwsh ./scripts/Invoke-SolutionTests.ps1` — every registered project ran with no failure and no skip, and every floor was met. Totals are recorded under **Executed gates** below. All spec 012 review assertions (`W:ReviewPageTests`) and spec 011 identity assertions remain green after the Blazor migration. |
| AC-80 | `W:StartupRefusalTests` starts non-Development hosts without a registered durable key ring and asserts each refuses before serving a request. `SolutionAndRegressionTests.TheOnlyContentStoreImplementationIsTheDevelopmentOnlyAdapter` asserts the Development adapter is the repository's only `IIntakeContentStore` implementation. |
| AC-81 | `ClientIntakeSecurityAcceptanceTests.NoRetentionOrPurgeRuleWasIntroducedForIntakeOrSourceContent` and `SolutionAndRegressionTests.NoIntakeRetentionOrPurgeRuleWasIntroduced` scan the new surface for retention and purge concepts and find none; the latter also asserts spec 011's purge service is still exactly where it was. |
| AC-82 | `ClientIntakeAcceptanceTests.PerItemStatusFollowsTheOneFixedPrecedenceRule` covers case (b) — a ready item with a further valid upload stays `Ready` with unsubmitted changes. `C:IntakeProjectionTests` covers case (a) — a missing or rejected item with a valid unsubmitted replacement projects `Uploaded`. |
| AC-83 | `W:IntakeConfigurationTests` starts hosts with absent, empty, and invalid fingerprint-key configuration and asserts each refuses before accepting an upload. |
| AC-84 | `W:IntakeCredentialSourceTests` asserts credentials come from the approved cryptographic source, are opaque and non-empty, and are derived from no firm, case, taxpayer, clock, counter, or other identifier. The acceptance suite additionally asserts a reissued credential differs from the original. |
| AC-85 | `RepositoryVerificationTests.TheRootInstructionsRecordSpecFourteenAsCompleteWithItsEvidence`, `.TheAcceptanceEvidenceFileExistsAndCoversEveryCriterion`, and `.TheForwardRoadmapRecordsSpecFourteenAsCompleteAndTheGatesAsOpen` assert the guidance records exactly the shipped surfaces and the opaque-byte exception while retaining every FR-55 prohibition and the production refusal. |
| AC-86 | `ClientIntakeAcceptanceTests.AnInvalidOrRepeatedHandoffAssociationIsRefusedSafely` and `ClientIntakeWorkflowPersistenceTests.OneExactExistingReviewRecordIsAssociatedOnceAndNeverReplaced` both submit a second record after a successful association and assert a conflict with the first association unchanged and no success event appended. |
| AC-87 | `ClientIntakeAcceptanceTests.SinShapedAndOverLongDisplayNamesAreRefusedWithoutEchoOrStorage` submits compact, hyphenated, space-separated, and two masked SIN-shaped probes and asserts each is refused, never echoed in the response, and never stored. |
| AC-88 | The same test submits a name of exactly `PersistedSchema.DisplayNameMaxLength` (accepted, length asserted) and one character over (refused, with no truncation, persistence, or echo). |

### Blazor foundation and review-screen migration

| Criterion | Evidence |
|---|---|
| AC-89 | `W:BlazorBoundaryTests` and `W:ProjectStructureTests` assert one Blazor Web App foundation, no WebAssembly project, no npm tree, no standalone SPA, and no second composition-root path. |
| AC-90 | `W:BlazorBoundaryTests` enumerates every component's render-mode declaration and asserts only `ClientQuestionnairePanel` declares `InteractiveServer`, with no global mode applied. |
| AC-91 | `W:BlazorBoundaryTests` asserts `ReviewEndpoints.cs` contains no `StringBuilder`, appended markup, or assembled style block, and that every success response comes from a typed component; the fixed `404`, `429`, and plain-text error writers are asserted unchanged. |
| AC-92 | `W:ReviewPageTests` records the single `FindAsync` call with the server-derived firm, the route `RecordId`, and the request cancellation token, and asserts no save and no alternate, unscoped, or latest query. |
| AC-93 | `W:ReviewPageTests` covers malformed, missing, foreign-firm, browser-unsafe, revoked-session, inactive-user, and inactive-membership requests and asserts the byte-identical empty `404` or authorization refusal with no component rendered. |
| AC-94 | `W:ReviewPageTests` asserts the exact component parameter and view-model property set, the rendered HTML, headers, and page state, with no persistence entity, excluded identifier or instant, or SIN representation. |
| AC-95 | `W:ReviewPageTests` asserts collection order, explicit empty states, contextual encoding, invariant positive/negative/zero/four-decimal values without arithmetic, and non-cacheable headers. |
| AC-96 | `W:BlazorBoundaryTests` inspects every component's injections, namespaces, parameters, and event handlers and asserts none names a repository, content store, EF type, firm resolver, policy implementation, status or readiness calculator, fingerprint service, audit writer, parser, mapper, processor, or assembler. |
| AC-97 | `ClientIntakeSecurityAcceptanceTests.ARevokedAccountantMembershipRefusesTheNextOperation` and `.ARevokedClientSessionRefusesTheNextOperation` revoke between a rendered page (holding a valid antiforgery token) and the next POST, and assert the fixed refusal with the store's concurrency version unchanged. `W:ClientSessionRevocationTests` covers the connected-circuit case. |
| AC-98 | `W:BlazorBoundaryTests` and `W:ProjectStructureTests` assert shared layout and markup live in reusable components, styles come only from tracked local or component-scoped CSS, no endpoint emits an inline stylesheet, and no third-party resource is requested. |
| AC-99 | `RepositoryVerificationTests.TheReviewUiMigrationChangedNoPersistenceOrTenancyProductionFile` asserts the migration and snapshot inventory and the exact `Persistence` and `Tenancy` reference sets. `ClientIntakeWorkflowPersistenceTests.TheIntakeMigrationIsAdditiveAndLeavesTheReviewSchemaAlone` asserts no pending model changes against the SQL Server model, that the intake migration contains no `AlterColumn`/`AddColumn`/`Drop*`/`Rename*`/data-mutation operation, that every table it creates is an intake table, and that the pre-existing firm, identity, and review tables still exist alongside them. All spec 011/012 persistence tests are green in the gate below. |

## The one raw-byte exception, stated plainly

The authenticated encrypted source object outside the web root **may contain a
SIN**, because a real T4 image does. Nothing in this repository claims otherwise.
It is protected by unreachability rather than by scrubbing:

- `IIntakeContentStore` declares no read member — asserted by
  `ClientIntakeWorkflowPersistenceTests.NoSinShapeOrFilenameReachesAnyStoredIntakeValue`;
- no mapped route reads, previews, or downloads it — asserted by
  `ClientIntakeSecurityAcceptanceTests.NoMappedRouteReadsPreviewsOrDownloadsSourceContent`,
  which probes five plausible URL spellings and scans the route inventory;
- it is written outside the web root and its bytes are encrypted at rest —
  asserted by `ClientIntakeSecurityAcceptanceTests.SinProbesAndFilenamesSurviveOnlyInsideTheEncryptedObject`,
  which reads every staged file and finds no plaintext probe.

Every **structured** surface is SIN-free in every form: browser models, rendered
HTML, URLs, cookies, headers, SQL metadata, content keys, audit records, logs,
exceptions, and diagnostics.

## Executed gates

All commands ran from the repository root on 2026-08-02:

```text
git diff --check
(no output)

dotnet build TaxAI.sln
Build succeeded.

dotnet test TaxAI.sln
Passed. Failed: 0, Skipped: 0.

pwsh ./scripts/Invoke-SolutionTests.ps1
  ok    TaxLogicEngine.Tests           261 passed (floor 261)
  ok    TaxCases.Tests                 337 passed (floor 337)
  ok    DocumentExtraction.Tests       673 passed (floor 673)
  ok    CaseProcessing.Tests           594 passed (floor 594)
  ok    CaseReview.Tests               482 passed (floor 482)
  ok    TaxAI.Portal.Tests             571 passed (floor 571)
  ok    Tenancy.Tests                  289 passed (floor 289)
  ok    Persistence.Tests              167 passed (floor 167)
  ok    ClientIntake.Tests             157 passed (floor 157)

Test execution gate passed: every expected project ran and met its floor.
Total: 3,531 passed, 0 failed, 0 skipped.
```

No database migration was applied to any database. The pending-model check runs
against the SQL Server model in-process and opens no connection.

## What remains gated

Spec 014 completing does not move a single gate:

- no raw-content preview, read, or download surface;
- no production content provider, Azure key ring, or deployment configuration —
  the build still starts in Development only and may not be deployed anywhere
  that can receive real taxpayer data;
- no source-document or intake-history retention or purge rule; spec 011's
  review-record policy is unchanged;
- no extraction ingress, OCR or LLM adapter, parsing, mapping, tax calculation,
  T1, or NETFILE/EFILE capability;
- no taxpayer listing, value editing, review decision or approval screen, or
  export;
- no firm, user, membership, or administrator provisioning;
- no second interactive component, global render mode, WebAssembly project, npm
  tree, or third-party asset.

Any of those requires another human-approved `tax-domain` specification. Question
F — whether this host exposes the untrusted document-extraction ingress —
remains reserved.
