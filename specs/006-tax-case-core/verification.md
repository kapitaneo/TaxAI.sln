# Spec 006 — Tax Case Core verification

Task T10 final verification of the approved PRD, architecture, task ownership,
module boundaries, repository hygiene, and unchanged TaxLogicEngine baseline.

## Final command results

- `dotnet test tests/TaxCases.Tests/TaxCases.Tests.csproj --no-restore`:
  **PASS** — 337 passed, 0 failed, 0 skipped (all TaxCases task suites,
  including 8 T10 repository-verification tests).
- `dotnet build TaxAI.sln`: **PASS** — 0 warnings, 0 errors.
- `dotnet test TaxAI.sln --no-restore`: **PASS** — 598 passed, 0 failed,
  0 skipped (337 TaxCases + 261 TaxLogicEngine).
- `git diff --check`: **PASS**.
- Tracked-file hygiene scan (`git ls-files`): **PASS** — 0 tracked `bin/`,
  `obj/`, `.vs/`, `*.user`, `settings.local.json`, secret, `.env`, or
  machine-specific files.
- TaxLogicEngine history scan (`git diff --name-only <spec-006-introduction>^ HEAD
  -- src/TaxLogicEngine tests/TaxLogicEngine.Tests`): **PASS** — 0 changed files.
- Historical task ownership audit (`gh pr view <PR> --json files` compared with
  each T01–T09 allowlist in `tasks.md`): **PASS** — PRs #52 through #60 contain
  no file outside their owning task's declared list.
- T10 changed-file scope: **PASS** — only
  `tests/TaxCases.Tests/EndToEnd/RepositoryVerificationTests.cs`,
  `specs/006-tax-case-core/verification.md`, `NEXT.md`, and root `CLAUDE.md`
  before the separately committed `T10.done` marker.

## Acceptance criteria

Every row names the exact xUnit test or repository command that supplies the
mechanical evidence. A row remains PASS only if the final full solution run is
green.

| Criterion | Result | Exact evidence |
|---|---|---|
| AC-1 | **PASS** | `TaxCases.Tests.Models.TaxCaseCreateTests.Create_MinimalValidCase_ReturnsCaseWithOnePrimaryAndNoErrors`; `RepositoryVerificationTests.RepresentativeCompleteCase_ReachesCompletedWithReadyState` |
| AC-2 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseAcceptanceTests.FullFamilyCase_AllStatePresentAndCorrectlyAttributed`; `RepositoryVerificationTests.RepresentativeCompleteCase_ReachesCompletedWithReadyState` |
| AC-3 | **PASS** | `TaxCases.Tests.Models.TaxCaseCreateTests.Create_BlankFirmId_FailsClosed` |
| AC-4 | **PASS** | `TaxCases.Tests.Models.TaxCaseCreateTests.Create_AbsentTaxYear_FailsClosedAndNeverDefaults`; `TaxCaseCreateTests.Create_UnsupportedTaxYear_FailsClosedAndIsNeverCoerced` |
| AC-5 | **PASS** | `TaxCases.Tests.Models.TaxCaseCreateTests.Create_MissingPrimaryTaxpayer_FailsClosed` |
| AC-6 | **PASS** | `TaxCases.Tests.Models.TaxCaseParticipantTests.AddParticipant_SecondPrimary_FailsClosed` |
| AC-7 | **PASS** | `TaxCases.Tests.Models.TaxCaseParticipantTests.AddParticipant_SecondSpouse_FailsClosed` |
| AC-8 | **PASS** | `TaxCases.Tests.Models.TaxCaseParticipantTests.AddParticipant_DuplicateOfEarlierAddedId_FailsClosed` |
| AC-9 | **PASS** | `TaxCases.Tests.Models.TaxCaseEvidenceTests.AttachEvidence_NonMemberParticipant_FailsClosed` |
| AC-10 | **PASS** | `TaxCases.Tests.Models.TaxCaseEvidenceTests.AttachEvidence_MismatchedTaxYear_FailsClosedAndNeverAdopted` |
| AC-11 | **PASS** | `TaxCases.Tests.Models.TaxCaseEvidenceTests.AttachEvidence_UnknownOrCaseMismatchedCategory_FailsClosedNamingIt` |
| AC-12 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseAcceptanceTests.CrossFirmInputs_AreRejectedEverywhereAndNeverAppearInState` |
| AC-13 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseAcceptanceTests.FirmOwnership_IsInheritedAndHasNoTransferSurface`; `RepositoryVerificationTests.UnsupportedValuesOwnershipAndReadinessMutatorsAreAbsent` |
| AC-14 | **PASS** | `TaxCases.Tests.Models.TaxCaseLifecycleTests.TransitionTo_EveryForbiddenPair_FailsClosedAndChangesNothing` |
| AC-15 | **PASS** | `TaxCases.Tests.Models.TaxCaseLifecycleTests.TransitionTo_NotReadyCase_CannotEnterReview` |
| AC-16 | **PASS** | `TaxCases.Tests.Models.TaxCaseIntakeTests.AnswerQuestion_UnknownQuestion_FailsClosed` |
| AC-17 | **PASS** | `TaxCases.Tests.ContractTests.TaxCase_ExposesExactGetOnlyPropertiesAndOperationSignatures`; `RepositoryVerificationTests.UnsupportedValuesOwnershipAndReadinessMutatorsAreAbsent` |
| AC-18 | **PASS** | `TaxCases.Tests.Models.TaxCaseEvidenceTests.AttachEvidence_TwoDistinctT4sForOneParticipant_IdentitySeparateFromCategory` |
| AC-19 | **PASS** | `TaxCases.Tests.Models.TaxCaseEvidenceTests.AttachEvidence_TwoDistinctMedicalRecords_BothRetrievableByIdentity` |
| AC-20 | **PASS** | `TaxCases.Tests.Models.TaxCaseEvidenceTests.AttachEvidence_DuplicateId_FailsClosedEvenAcrossCategories` |
| AC-21 | **PASS** | `TaxCases.Tests.Models.TaxCaseEvidenceTests.AttachEvidence_BlankEvidenceId_FailsClosed` |
| AC-22 | **PASS** | `TaxCases.Tests.Services.ReadinessCalculatorTests.OtherEvidence_NeverSatisfiesT4OrMedicalRequirements` |
| AC-23 | **PASS** | `TaxCases.Tests.Services.ReadinessCalculatorTests.UnconfiguredChecklist_IsAlwaysNotReady` |
| AC-24 | **PASS** | `TaxCases.Tests.Services.ReadinessCalculatorTests.MissingEvidenceReason_NamesRequirementParticipantCategoryAndRemainingCount`; `ReadinessCalculatorTests.UnansweredQuestion_ForcesNotReady` |
| AC-25 | **PASS** | `TaxCases.Tests.Services.ReadinessCalculatorTests.LastRequiredEvidence_FlipsDerivedStateToReady`; `ReadinessCalculatorTests.LastRequiredAnswer_FlipsDerivedStateToReady` |
| AC-26 | **PASS** | `TaxCases.Tests.Services.ReadinessCalculatorTests.ConfirmedEmptyChecklist_IsReadyWithNoReasons` |
| AC-27 | **PASS** | `TaxCases.Tests.Models.TaxCaseLifecycleTests.AddParticipant_AgainstTerminalCase_FailsClosedAndChangesNothing`; `AttachEvidence_AgainstTerminalCase_FailsClosedAndChangesNothing`; `ConfirmChecklist_AgainstTerminalCase_FailsClosedAndChangesNothing`; `AnswerQuestion_AgainstTerminalCase_FailsClosedAndChangesNothing`; `TransitionTo_PlainTransitionOutOfCompleted_FailsClosed` |
| AC-28 | **PASS** | `TaxCases.Tests.Models.TaxCaseLifecycleTests.Reopen_CompletedCase_ReturnsToInformationGatheringWithRecomputedReadiness` |
| AC-29 | **PASS** | `TaxCases.Tests.Models.TaxCaseLifecycleTests.Reopen_NullOrBlankReason_FailsClosed` |
| AC-30 | **PASS** | `TaxCases.Tests.Models.TaxCaseLifecycleTests.AddParticipant_AgainstTerminalCase_FailsClosedAndChangesNothing`; `AttachEvidence_AgainstTerminalCase_FailsClosedAndChangesNothing`; `ConfirmChecklist_AgainstTerminalCase_FailsClosedAndChangesNothing`; `AnswerQuestion_AgainstTerminalCase_FailsClosedAndChangesNothing`; `Reopen_OutsideCompleted_FailsClosed` with `Cancelled` theory data |
| AC-31 | **PASS** | `TaxCases.Tests.Models.TaxCaseLifecycleTests.TransitionTo_EmitsExactlyOneDeterministicStatusChangedRecord` |
| AC-32 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseAcceptanceTests.AcceptedOperations_ProduceEveryChangeKindWithIdentifyingFields` |
| AC-33 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseAcceptanceTests.Create_Rejected_ProducesNoCaseAndNoRecord`; all six sibling rejected-operation atomicity tests in the same class |
| AC-34 | **PASS** | `TaxCases.Tests.Boundaries.TaxCasesBoundaryTests.ProductionSource_ContainsNoForbiddenDependencyOrBehavior`; `RepositoryVerificationTests.ForbiddenInfrastructureAndProviderDependenciesAreAbsent` |
| AC-35 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseDeterminismTests.IdenticalReplays_ProduceEqualChangeRecordSequences` |
| AC-36 | **PASS** | `TaxCases.Tests.Models.TaxCaseCreateTests.Create_EmptyRequest_ReturnsOneErrorPerMissingFieldWithoutThrowing` |
| AC-37 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCasePiiTests.MandatoryPiiMatrix_ActualOperationsRetainDomainValuesButProtectEveryOutput`; `SpaceSeparatedSin_IsSafeInAcceptedValueRejectedErrorReadinessReasonAndChangeDiagnostic` |
| AC-38 | **PASS** | `TaxCases.Tests.Models.TaxCaseCreateTests.Create_WithoutSin_SucceedsAndFabricatesNoSin` |
| AC-39 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseAcceptanceTests.A2025Case_NeverAdoptsAnotherYearsEvidenceOrRules` |
| AC-40 | **PASS** | `TaxCases.Tests.Boundaries.TaxCasesBoundaryTests.ProductionSource_ContainsNoForbiddenDependencyOrBehavior` with `CRA line/box mapping` data; `RepositoryVerificationTests.ForbiddenInfrastructureAndProviderDependenciesAreAbsent` |
| AC-41 | **PASS** | `TaxCases.Tests.Models.TaxCaseCreateTests.Create_WithSin_StoresOnlyRecipientIdentifierAndMasks`; `ContractTests.Assembly_ExportsExactlyTheApprovedPublicTypes` |
| AC-42 | **PASS** | `TaxCases.Tests.ContractTests.TaxCase_ExposesExactGetOnlyPropertiesAndOperationSignatures`; seven rejected-operation tests in `TaxCaseAcceptanceTests` |
| AC-43 | **PASS** | `TaxCases.Tests.EndToEnd.TaxCaseDeterminismTests.IdenticalReplays_ProduceEqualObservableState`; `IdenticalReplays_ProduceEqualReadiness` |

## Repository-level evidence

- The four spec 006 projects present at their approved paths, resolvable project
  references, dependency direction:
  `RepositoryVerificationTests.SolutionContainsTheFourSpec006ProjectsAndAllReferencesResolve`.
- Architecture public manifest and unsupported public values/mutators:
  `RepositoryVerificationTests.PublicContractMatchesArchitectureManifestExactly` and
  `UnsupportedValuesOwnershipAndReadinessMutatorsAreAbsent`.
- Provider/infrastructure boundary: `RepositoryVerificationTests.ForbiddenInfrastructureAndProviderDependenciesAreAbsent`.
- Tracked-file hygiene: `RepositoryVerificationTests.RepositoryHygiene_HasNoForbiddenTrackedFiles`.
- TaxLogicEngine unchanged since spec 006 began:
  `RepositoryVerificationTests.TaxLogicEngineProductionAndTests_RemainedUnchangedThroughoutSpec006`.
- T01–T09 markers and all 43 evidence rows:
  `RepositoryVerificationTests.AllPrerequisiteMarkersAndAcceptanceCriteriaHavePassingEvidence`.
