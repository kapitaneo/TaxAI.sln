# Spec 009 — Accountant Review Package verification

Task T08 final verification of the complete feature against the approved
[prd.md](prd.md), the contract in [architecture.md](architecture.md), and the
immutable task plan in [tasks.md](tasks.md).

## Commands run

```text
dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CaseReview.Tests.ContractTests|FullyQualifiedName~ContractOmissionTests"
  160 passed, 0 failed, 0 skipped

dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ReviewContentModelTests"
  12 passed, 0 failed, 0 skipped

dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ReviewDecisionModelTests"
  16 passed, 0 failed, 0 skipped

dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ReviewPackageAssemblerTests"
  21 passed, 0 failed, 0 skipped

dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AccountantReviewPackageDecisionTests"
  41 passed, 0 failed, 0 skipped

dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CaseReview.Tests.EndToEnd.ReviewPackage"
  181 passed, 0 failed, 0 skipped

dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CaseReviewBoundaryTests"
  35 passed, 0 failed, 0 skipped

dotnet test tests/CaseReview.Tests/CaseReview.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~RepositoryVerificationTests"
  16 passed, 0 failed, 0 skipped

dotnet build TaxAI.sln --no-incremental
  Build succeeded. 4 Warning(s), 0 Error(s)

dotnet test TaxAI.sln
  CaseReview.Tests            482 passed, 0 failed, 0 skipped
  CaseProcessing.Tests        594 passed, 0 failed, 0 skipped
  DocumentExtraction.Tests    673 passed, 0 failed, 0 skipped
  TaxCases.Tests              337 passed, 0 failed, 0 skipped
  TaxLogicEngine.Tests        261 passed, 0 failed, 0 skipped
  TOTAL                      2347 passed, 0 failed, 0 skipped
```

The four warnings are pre-existing in `tests/TaxLogicEngine.Tests` (two
CS0649 and two xUnit2008 warnings). `CaseReview` and `CaseReview.Tests` build
warning-free. The no-incremental build ensures an up-to-date project cannot
hide a warning.

## Test-suite growth

| Wave | Task | Suite added | Cases | Cumulative `CaseReview.Tests` |
|---|---|---|---:|---:|
| 0 | T01 | contract and constructed PII omission | 160 | 160 |
| 1 | T02 | immutable reviewed-content models | 12 | 172 |
| 1 | T03 | decision descriptor and sign-off models | 16 | 188 |
| 2 | T04 | package assembly | 21 | 209 |
| 2 | T05 | decision recording and acknowledgement gate | 41 | 250 |
| 3 | T06 | public-pipeline acceptance, PII, and determinism | 181 | 431 |
| 3 | T07 | contract, source, IL, and dependency boundaries | 35 | 466 |
| 4 | T08 | repository-wide verification | 16 | 482 |

The four pre-existing suites remain at 261 / 337 / 673 / 594, matching the
counts recorded before spec 009.

## Acceptance criteria

Every evidence name below is in `tests/CaseReview.Tests`. Prefixes:
`C` = `ContractTests`, `O` = `Models.ContractOmissionTests`,
`M` = `Models.ReviewContentModelTests`,
`I` = `Models.ReviewDecisionModelTests`,
`A` = `Services.ReviewPackageAssemblerTests`,
`S` = `Models.AccountantReviewPackageDecisionTests`,
`E` = `EndToEnd.ReviewPackageAcceptanceTests`,
`P` = `EndToEnd.ReviewPackagePiiTests`,
`D` = `EndToEnd.ReviewPackageDeterminismTests`,
`B` = `Boundaries.CaseReviewBoundaryTests`, and
`R` = `EndToEnd.RepositoryVerificationTests`.

| AC | Result | Exact evidence |
|---:|:---:|---|
| 1 | pass | `A.Assemble_SingleExtraction_SucceedsWithExactAttributionAndNoSignOff`, `R.RepresentativePublicPipelineAssemblesAndSignsExactContent` |
| 2 | pass | `A.Assemble_SeveralExtractions_PreservesSuppliedOrderAndEveryYear`, `E.PublicPipeline_MultiSlipHappyPath_PreservesOrderAndSignsReplacement` |
| 3 | pass | `A.Assemble_SeveralExtractions_PreservesSuppliedOrderAndEveryYear`, `E.PublicPipeline_MultiSlipHappyPath_PreservesOrderAndSignsReplacement` |
| 4 | pass | `A.Assemble_SingleExtraction_SucceedsWithExactAttributionAndNoSignOff`, `E.PublicPipeline_ControlledMap_PreservesEveryProcessedValue` |
| 5 | pass | `A.Assemble_PreservesSlipIdentityAndEveryEngineProducedSequence`, `R.RepresentativePublicPipelineAssemblesAndSignsExactContent` |
| 6 | pass | `A.Assemble_PreservesSlipIdentityAndEveryEngineProducedSequence`, `E.PublicPipeline_ControlledMap_PreservesEveryProcessedValue` |
| 7 | pass | `A.Assemble_PreservesSlipIdentityAndEveryEngineProducedSequence`, `E.PublicPipeline_OneAcknowledgementCoversSeveralSlipsAndUnmappedCodes` |
| 8 | pass | `A.Assemble_PreservesSlipIdentityAndEveryEngineProducedSequence`, `E.PublicPipeline_ControlledMap_PreservesEveryProcessedValue` |
| 9 | pass | `A.Assemble_SameInputsTwice_ProducesEqualPackages`, `D.AcceptedPipeline_ReplaysFiveTimesWithEqualPackagesAndSignOffs` |
| 10 | pass | `A.Assemble_DoesNotMutateAnyProcessedInput`, `E.PublicPipeline_AssemblyAndDecisionMutateNoUpstreamInputOrOriginalPackage` |
| 11 | pass | `A.Assemble_NullCollection_FailsClosedWithoutThrowing` |
| 12 | pass | `A.Assemble_EmptyCollection_FailsClosed` |
| 13 | pass | `A.Assemble_NullElement_FailsClosedWithoutThrowing` |
| 14 | pass | `A.Assemble_FirmMismatch_FailsClosedWithoutLeakingInput` |
| 15 | pass | `A.Assemble_CaseMismatch_FailsClosedWithoutLeakingInput` |
| 16 | pass | `A.Assemble_ParticipantMismatch_FailsClosedWithoutLeakingInput` |
| 17 | pass | `A.Assemble_IdentifierNearMatches_AreComparedOrdinally` |
| 18 | pass | `A.Assemble_DuplicateDocumentId_FailsClosedWithoutLeakingInput` |
| 19 | pass | `S.RecordDecision_NullDescriptor_FailsClosedWithoutThrowing` |
| 20 | pass | `S.RecordDecision_UndefinedDecision_FailsClosed` |
| 21 | pass | `S.RecordDecision_BlankReviewerReference_FailsClosed` |
| 22 | pass | `S.RecordDecision_BlankDecidedAtReference_FailsClosed` |
| 23 | pass | `S.RecordDecision_RejectedWithoutNonBlankNote_FailsClosed` |
| 24 | pass | `S.RecordDecision_ChangesRequestedWithoutNonBlankNote_FailsClosed` |
| 25 | pass | `S.RecordDecision_SecondDecision_FailsAndKeepsExistingSignOff` |
| 26 | pass | `S.RecordDecision_ApprovedWithoutUnmappedBoxes_ReturnsSignedReplacement`, `E.PublicPipeline_ApprovalWithoutUnmappedCodes_NeedsNoAcknowledgement` |
| 27 | pass | `S.RecordDecision_NonApprovalDecision_PreservesExactNote`, `E.PublicPipeline_AllThreeDecisions_AreRecordedExactly` |
| 28 | pass | `S.RecordDecision_ReplacementPreservesExactReviewedContent`, `E.PublicPipeline_AssemblyAndDecisionMutateNoUpstreamInputOrOriginalPackage` |
| 29 | pass | `S.RecordDecision_OpaqueNonDateDecidedAtReference_IsNotInterpreted` |
| 30 | pass | `S.RecordDecision_SameInputsTwice_ProduceEqualReplacements`, `D.AcceptedPipeline_ReplaysFiveTimesWithEqualPackagesAndSignOffs` |
| 31 | pass | `A.Assemble_EmptyLineMapping_SucceedsWithAnEmptyReviewedLineSnapshot`, `E.PublicPipeline_EmptyMapping_SucceedsWithEmptyLineEntries` |
| 32 | pass | `A.Assemble_ZeroAmount_RemainsExactlyTheEnginesValue`, `E.PublicPipeline_ZeroAndHalfCentValues_EqualUpstreamOutput` |
| 33 | pass | `A.Assemble_HalfCentAmount_EqualsTheUpstreamEngineOutput`, `E.PublicPipeline_ZeroAndHalfCentValues_EqualUpstreamOutput` |
| 34 | pass | `B.SourceContainsNoCraLineOrBoxCodeLiteralAndNoMoneyArithmetic`, `B.CompiledAssemblyContainsNoForbiddenTaxOrInfrastructureReference` |
| 35 | pass | `P.CompactSin_ReachesReviewOnlyAsTheMaskedRecipientIdentifier`, `P.PublicPipelineMatrix_NoPlantOrNineDigitRunEscapes` |
| 36 | pass | `P.PublicPipelineMatrix_NoPlantOrNineDigitRunEscapes` across every assembly and decision rejection family |
| 37 | pass | `P.PublicPipelineMatrix_NoPlantOrNineDigitRunEscapes`, `I.DecisionModelRenderingAndJsonNeverLeakCallerStringsOrSinLikeRuns` |
| 38 | pass | `P.PublicPipeline_ExactDefensiveJsonShapesOmitEveryCallerString`, `P.EveryCallerControlledCaseReviewString_IsJsonIgnored`, `O.EveryPublicRenderingAndJsonSurface_OmitsTheRawPlant` |
| 39 | pass | `B.AssemblyDeclaresNoSinCarrierOrMaskingHelper`, `P.CompactSin_ReachesReviewOnlyAsTheMaskedRecipientIdentifier` |
| 40 | pass | `B.EveryValidationFieldIsClosedAndEveryMessageIsAFixedConstant`, `C.ReviewFields_IsExactlyTheClosedThirteenConstantSet` |
| 41 | pass | `C.SupportedReviewTaxYears_IsExactlyThe2025OnlyContract`, `B.SupportedYearPolicyIsExactly2025AndLivesInOneSourceFile`, `R.UnsupportedYearsAreRejectedBeforeReviewAndByReviewPolicy` |
| 42 | pass | `A.Assemble_SeveralExtractions_PreservesSuppliedOrderAndEveryYear`, `E.PublicPipeline_MultiSlipHappyPath_PreservesOrderAndSignsReplacement` |
| 43 | pass | `B.AssemblerSourceAndIlContainUnconditionalPerElementYearGuards` |
| 44 | pass | `B.ProductionSourceContainsNoForbiddenDependencyOrBehavior`, `B.SourceContainsNoCraLineOrBoxCodeLiteralAndNoMoneyArithmetic`, `B.CompiledAssemblyContainsNoCraLineBoxCodeOrMappingResourceLiteral`, `R.CaseReviewSourceContainsNoForbiddenDependencyOrTaxToken` |
| 45 | pass | `R.NoPreExistingProjectOrSourceTreeReferencesCaseReview`, plus the 2,347-test solution run and unchanged pre-existing suite counts above |
| 46 | pass | `B.ProductionProjectReferencesExactlyCaseProcessingAndTaxLogicEngine`, `B.CompiledAssemblyReferencesExactlyTheTwoApprovedModules`, `R.CaseReviewReferencesExactlyItsTwoApprovedCompiledModules` |
| 47 | pass | `B.NoExistingProductionOrTestProjectReferencesCaseReview`, `B.LoadedUpstreamAssembliesDoNotReferenceCaseReview`, `R.NoPreExistingProjectOrSourceTreeReferencesCaseReview` |
| 48 | pass | `B.ProductionSourceContainsNoForbiddenDependencyOrBehavior`, `B.CompiledAssemblyContainsNoForbiddenTaxOrInfrastructureReference`, `R.CaseReviewSourceContainsNoForbiddenDependencyOrTaxToken` |
| 49 | pass | `B.SolutionDeclaresBothSpecProjectsExactlyOnceAtApprovedPaths`, `R.SpecProjectsAreDeclaredExactlyOnceAtApprovedPaths`, `R.EveryDeclaredSolutionProjectAndProjectReferenceResolves` |
| 50 | pass | `S.RecordDecision_ApprovalWithUnmappedBoxesAndAbsentAcknowledgement_FailsClosed`, `E.PublicPipeline_RequiredAcknowledgementAbsentOrFalse_FailsClosed`, `R.RepresentativeUnmappedApprovalWithoutAcknowledgementFailsClosed` |
| 51 | pass | `S.RecordDecision_ApprovalWithUnmappedBoxesAndNegativeAcknowledgement_FailsClosed`, `E.PublicPipeline_RequiredAcknowledgementAbsentOrFalse_FailsClosed` |
| 52 | pass | `S.RecordDecision_OneAffirmativeAcknowledgementCoversAllSlipsAndCodes`, `E.PublicPipeline_OneAcknowledgementCoversSeveralSlipsAndUnmappedCodes` |
| 53 | pass | `S.RecordDecision_NonApprovalWithUnmappedBoxes_DoesNotRequireAcknowledgement`, `E.PublicPipeline_AllThreeDecisions_AreRecordedExactly` |
| 54 | pass | `S.AcknowledgementContract_IsPackageLevelWithNoPerCodeOrDisputeMember`, `B.ContractHasOnePackageLevelAcknowledgementAndNoPerCodeDisputeSurface`, `R.ContractHasNoPerCodeAcknowledgementOrMappedDisputeMember` |

**54 of 54 pass. 0 fail.**

## Full-chain and boundary review

- **Public chain.** `R.RepresentativePublicPipelineAssemblesAndSignsExactContent`
  executes `ExtractionEnvelope` → `ExtractionEnvelopeValidator.Validate` →
  `T4CaseProcessor.Process` → `ReviewPackageAssembler.Assemble` →
  `AccountantReviewPackage.RecordDecision` with the embedded production mapping
  provider. It verifies exact slip identity, upstream line values, unsigned
  original state, and signed replacement state.
- **Acknowledgement gate.**
  `R.RepresentativeUnmappedApprovalWithoutAcknowledgementFailsClosed` repeats
  the negative gate through the public chain with a controlled empty map;
  T05 and T06 separately cover absent, negative, affirmative, multiple-slip,
  and non-approval behavior.
- **Tax-year isolation.** The public ingress rejects 2024 and 2026, the local
  review policy independently rejects both, and T07 source/IL checks prove every
  materialized processed extraction is checked for year equality and support.
  No fabricated `ProcessedT4Extraction` is introduced for test convenience.
- **No tax truth.** CaseReview calls only
  `T4SlipBoxAmounts.Enumerate`. Source and IL checks reject parsers, mappers,
  providers, mapping resources, CRA line literals, box-code tables, arithmetic,
  and rounding.
- **PII.** The complete 5-plant × 14-slot matrix covers successful assembly,
  successful sign-off, every constructible assembly rejection, and every
  decision rejection. Caller strings are omitted from JSON/rendering/errors;
  the only SIN representation is the existing masked `RecipientIdentifier`
  reached via `ReviewedSlip.Slip.Sin`.
- **Dependency direction.** CaseReview references exactly CaseProcessing and
  TaxLogicEngine. There is no direct DocumentExtraction or TaxCases reference
  and no reverse reference from any earlier production or test project.
- **Determinism and immutability.** Accepted and rejected families replay five
  times with stable packages, sign-offs, errors, and order. Distinct equivalent
  input collections behave equally, and all caller-owned inputs remain
  unchanged.

## Existing behavior and declared-file ownership

The behavioral half of AC-45 is the full solution run: all four pre-existing
test assemblies are green at their prior counts (261 / 337 / 673 / 594).

The structural half uses:

```text
git diff --exit-code
  21e66eba7d83226bc7624d455b9c19a55bb951f8..HEAD --
  src/TaxLogicEngine tests/TaxLogicEngine.Tests
  src/TaxCases tests/TaxCases.Tests
  src/DocumentExtraction tests/DocumentExtraction.Tests
  src/CaseProcessing tests/CaseProcessing.Tests
```

It reports no diff. The base is the merged spec-009 plan immediately before
T01. No parser, mapper, mapping configuration, existing public type,
`InternalsVisibleTo` grant, or earlier module behavior changed.

Task ownership is exact:

| Task | Changed files |
|---|---|
| T01 | `TaxAI.sln`; CaseReview project, instruction, contract/model/service scaffold files; CaseReview test project and contract/omission tests; `status/T01.done` |
| T02 | reviewed amount/slip/package core models; `ReviewContentModelTests.cs`; `status/T02.done` |
| T03 | sign-off model; `ReviewDecisionModelTests.cs`; `status/T03.done` (`ReviewDecisionDescriptor.cs` required no diff) |
| T04 | assembler; `ReviewPackageAssemblerTests.cs`; `status/T04.done` |
| T05 | package decision partial; `AccountantReviewPackageDecisionTests.cs`; `status/T05.done` |
| T06 | three `ReviewPackage*` end-to-end suites; `status/T06.done` |
| T07 | `CaseReviewBoundaryTests.cs`; `status/T07.done` |
| T08 | `RepositoryVerificationTests.cs`; this record; `NEXT.md`; root `CLAUDE.md`; `status/T08.done` |

No task changed an undeclared file. `tasks.md` is unchanged and its checkboxes
remain read-only; completion is recorded only by `status/*.done`.

## Repository hygiene

- `git ls-files` matches no tracked `bin/`, `obj/`, `.vs/`, `*.user`,
  `.claude/settings.local.json`, private key, credential, or secret artifact.
- `.gitignore` covers build output and local-only settings.
- The solution asserts this spec's two project declarations by exact
  presence-and-uniqueness only; no test freezes the solution to a total count.
- All seven prerequisite markers exist and T08's marker is created only after
  the required no-incremental build and full solution suite pass.

## Known exclusions carried forward

- A recorded sign-off is an internal workflow record. It does not verify
  membership, reviewer authorization, legal attestation, or filing authority.
- Participant-level line aggregation, content-level duplicate-slip detection,
  raw OCR presentation, correction/editing, persistence, rendering, and T5
  review remain outside v1.
- The inherited employee-copy/required-Box-54 question remains a spec-003
  concern. Spec 009 neither changes nor answers it.
- Cross-year `ProcessedT4Extraction` values remain intentionally
  unconstructible outside CaseProcessing. The defensive year guard is verified
  structurally rather than by weakening the shipped module boundary.

## Conclusion

AC-1 through AC-54 all pass. The Accountant Review Package is complete: its
public contract is exact, review content is immutable and faithful to upstream
engine output, the package-level unmapped-box acknowledgement is fail-closed,
PII is masked or omitted, no tax truth is recomputed, and every existing suite
remains green. Repository state is updated by T08 to record spec 009 complete
and name Portal Foundation as the next feature.
