# Spec 008 — T4 Case Processor verification

Task T06 final verification of the whole feature, against the approved
[prd.md](prd.md) and the public contract in [architecture.md](architecture.md).

## Commands run

```
dotnet build TaxAI.sln --no-incremental
  Build succeeded. 4 Warning(s), 0 Error(s)

dotnet test TaxAI.sln
  CaseProcessing.Tests       594 passed, 0 failed, 0 skipped
  DocumentExtraction.Tests   673 passed, 0 failed, 0 skipped
  TaxCases.Tests             337 passed, 0 failed, 0 skipped
  TaxLogicEngine.Tests       261 passed, 0 failed, 0 skipped
  TOTAL                     1865 passed, 0 failed, 0 skipped
```

The 4 warnings are pre-existing in `tests/TaxLogicEngine.Tests` (2× CS0649,
2× xUnit2008); they predate spec 007 and this spec declares that tree
untouchable. Both `CaseProcessing` projects build warning-free. The build is
`--no-incremental`, so no warning can be hidden by an up-to-date project — an
incremental build hid one during spec 007 T02.

## Test-suite growth

| Wave | Task | Suite added | Cumulative `CaseProcessing.Tests` |
|---|---|---|---|
| 0 | T01 | contract, result omission, tax years | 312 |
| 1 | T02 | `PayloadStructureGuardTests` | 340 |
| 2 | T03 | `T4CaseProcessorTests` | 380 |
| 3 | T04 | acceptance, PII, determinism (end-to-end) | 539 |
| 3 | T05 | `CaseProcessingBoundaryTests` | 570 |
| 4 | T06 | `RepositoryVerificationTests` | 594 |

The three pre-existing suites are unchanged at 261 / 337 / 673 — the same counts
spec 007 recorded.

## Acceptance criteria

Every test name below is in `tests/CaseProcessing.Tests`. Class prefixes:
`P` = `Services.T4CaseProcessorTests`, `G` = `Services.PayloadStructureGuardTests`,
`A` = `EndToEnd.T4ProcessingAcceptanceTests`, `I` = `EndToEnd.T4ProcessingPiiTests`,
`D` = `EndToEnd.T4ProcessingDeterminismTests`, `B` = `Boundaries.CaseProcessingBoundaryTests`,
`R` = `EndToEnd.RepositoryVerificationTests`, `C` = `ContractTests`,
`O` = `Models.ResultOmissionTests`, `Y` = `Services.SupportedProcessingTaxYearsTests`.

| AC | Result | Evidence |
|---|---|---|
| 1 | pass | `P.Process_ValidExtraction_Succeeds` |
| 2 | pass | `A.FullChain_EnvelopeToValidatedExtractionToResult_MatchesTheEngine`, `A.FullChain_PayloadReachesTheParserReferenceIdentical` |
| 3 | pass | `P.Process_Slip_IsExactlyTheParsersOwn` (property-by-property against `T4SlipParser`) |
| 4 | pass | `P.Process_Mapping_IsExactlyTheMappersOwn` (entries, amounts, source box codes, in order) |
| 5 | pass | `P.Process_EchoesAttributionExactly` (five untrimmed marker strings, ordinal) |
| 6 | pass | `P.Process_UnmappedBoxCodes_PassThroughUnchangedAndNeverReject` |
| 7 | pass | `D.Replaying_TheSameExtraction_YieldsAnEqualResult`, `D.TwoDistinctButEquivalentExtractions_Agree`, `D.EveryRejectionFamily_KeepsItsErrorOrderingAcrossReplays`, `D.AcceptedReplays_ProduceIdenticalSlipsAndLineEntries`, `D.Processing_MutatesNeitherTheExtractionNorItsPayload` |
| 8 | pass | `P.Process_Success_YearsCohere` |
| 9 | pass | `P.Process_NullExtraction_FailsClosedWithExactlyOneError` |
| 10 | pass | `P.Process_GenericEvidenceKind_FailsAndNeverQueriesTheProvider` |
| 11 | pass | `G.Inspect_NullBoxCollection_YieldsOnePayloadBoxesError`, `P.Process_NullBoxCollection_FailsClosedWithoutThrowing` |
| 12 | pass | `G.Inspect_NullElementInBoxes_YieldsOnePayloadBoxesError`, `G.Inspect_EveryElementNull_YieldsOneErrorPerElement` |
| 13 | pass | `G.Inspect_NullBoxCode_...`, `G.Inspect_EmptyBoxCode_...`, `G.Inspect_WhitespaceOnlyBoxCode_...` (5 cases incl. tab, CRLF, NBSP) |
| 14 | pass | `P.Process_ParserRejection_CarriesTheParsersErrorsVerbatim(family: "duplicate box code")` |
| 15 | pass | `P.Process_MissingRequiredBox_CarriesTheParsersErrorVerbatim(omittedBoxCode: "12")` |
| 16 | pass | `P.Process_MissingRequiredBox_CarriesTheParsersErrorVerbatim(omittedBoxCode: "45")` |
| 17 | pass | `P.Process_MissingRequiredBox_CarriesTheParsersErrorVerbatim(omittedBoxCode: "54")` |
| 18 | pass | `P.Process_ParserRejection_CarriesTheParsersErrorsVerbatim(family: "malformed amount")` |
| 19 | pass | `P.Process_ParserRejection_CarriesTheParsersErrorsVerbatim(family: "negative amount")` |
| 20 | pass | `P.Process_ParserRejection_CarriesTheParsersErrorsVerbatim(family: "constrained code out of range")` |
| 21 | pass | `P.Process_ParserRejection_CarriesTheParsersErrorsVerbatim(family: "missing companion box")` |
| 22 | pass | `P.Process_ProviderFailsForTheYear_SurfacesItsErrorsAndAsksForNoOtherYear` (two-error stub pins ordering) |
| 23 | pass | `P.Process_ParserRejection_StopsThePipelineBeforeTheProvider` (recording provider never invoked) |
| 24 | pass | `P.Process_EmptyBoxList_CarriesTheParsersRequiredBoxErrors` |
| 25 | pass | `P.Process_ZeroAmounts_SucceedsAndEqualsTheEngine` |
| 26 | pass | `P.Process_OnlyTheRequiredBoxes_SucceedsAndEqualsTheEngine` |
| 27 | pass | `P.Process_HalfCentBoundary_IsTheEnginesRoundingUnchanged` (`100.005`->`100.01`, `100.015`->`100.02`, equal to `MoneyRounding.ToCents`) |
| 28 | pass | `P.Module_PerformsNoArithmetic`, `P.ModuleSourceScanner_SeesEveryProductionFile` |
| 29 | pass | `I.AcceptedPipeline_WithBox12As123456789_ExposesOnlyTheMaskedIdentifier`, `I.AcceptedResult_JsonSerializationIsTheTaxYearAndNothingElse` |
| 30 | pass | `I.NoPlantedValueLeaksThroughAnyPipeline` (55 combinations × 13 pipelines), `I.SelfOriginatedErrors_UseTheClosedFieldSetAndEchoNoCallerValue` |
| 31 | pass | `I.Module_DeclaresNoSinPropertyNoSinTypeAndNoMaskingHelper`, `I.Result_ExposesExactlyOneSinBearingMember`, `B.TheModuleDeclaresNoSinPropertyAndNoSinType` |
| 32 | pass | `I.ResultPublicSurface_ExposesNoRawPayloadType`, `I.RawPayload_IsUnreachableFromASuccessfulResult`, `I.RawPayloadTypeDetector_CatchesEveryWayOfSpellingIt`, `C.ProcessedT4Extraction_DoesNotExposeTheRawPayload` |
| 33 | pass | `I.AcceptedResult_ToStringCarriesStructuralFactsOnly`, `P.Process_EverySelfOriginatedField_IsAT4ProcessingFieldsConstant`, `C.T4ProcessingFields_DeclaresExactlyTheClosedConstantSet` |
| 34 | pass | `P.YearPolicy_IsExactly2025`, `Y.All_ContainsExactly2025`, `B.TheSupportedYearPolicy_LivesOnlyInSupportedProcessingTaxYears` |
| 35 | pass | `P.Process_AsksTheProviderForTheSlipsYearExactlyOnce` |
| 36 | pass | `P.Process_ProviderHoldingOnly2024_NeverMapsA2025Slip` (`MapWasHandedOut` stays false) |
| 37 | pass | `P.OffYearEnvelopes_NeverBecomeAValidatedExtraction`, `R.NoUnsupportedYearIsEverAccepted` |
| 38 | pass | `A.LineNumbers_ComeOnlyFromTheInjectedMap`, `A.LineNumbers_FollowTheProviderAndNothingElse`, `A.ModuleSource_ContainsNoCraLineLiteralAndNoMappingResourceName`, `B.NoCraLineNumberLiteralAppearsInTheModule` |
| 39 | pass | `A.ModuleSource_ConstructsNoSlipAndInterpretsNoBoxValue`, `A.ModuleSource_ReadsNoBoxValue`, `A.ModuleRuntime_NeverProducesASlipTheParserWouldNot`, `G.GuardSource_ReadsNoBoxValue` |
| 40 | pass | `B.ProductionProject_ReferencesExactlyDocumentExtractionAndTaxLogicEngine`, `B.CompiledAssembly_ReferencesExactlyTheTwoApprovedModules`, `R.EveryProject_DeclaresExactlyItsApprovedReferenceSet`, `R.NoProjectDeclaresAnUnexpectedReference` |
| 41 | pass | `B.TheModuleReferencesNoTaxCasesProjectOrAssembly`, `B.NoOtherModuleReferencesCaseProcessing` (6 trees), `R.NoTaxCasesCouplingExistsInEitherDirection`, `R.DependenciesPointInwardWithNoReverseEdge` |
| 42 | pass | `B.ProductionSource_ContainsNoForbiddenDependencyOrBehavior` (14 pattern families), `R.NoForbiddenBoundaryTokenSurvivesInTheModule` |
| 43 | pass | `R.ThePreExistingModuleTrees_AreUntouchedBySpec008`, plus the solution run above: 261 / 337 / 673 all green, unchanged from spec 007's recorded counts, and `git diff 71c9fcc..HEAD` shows no change under any of the six pre-existing trees |

**43 of 43 pass. 0 fail.**

## AC-43, stated precisely

AC-43 has two halves and they are verified differently, so neither is overclaimed:

- *"every existing test remains green"* — behavioural, and the evidence is the
  `dotnet test TaxAI.sln` run recorded above. The three pre-existing assemblies
  report 261 / 337 / 673 passed, identical to the counts in
  `specs/007-document-extraction-contract/verification.md`. No test was removed,
  skipped, or rewritten.
- *"no existing public type has been added, renamed, or changed"* — structural,
  and verified by `git diff 71c9fcc..HEAD`, which shows **no file changed** under
  `src/TaxLogicEngine/`, `tests/TaxLogicEngine.Tests/`, `src/TaxCases/`,
  `tests/TaxCases.Tests/`, `src/DocumentExtraction/`, or
  `tests/DocumentExtraction.Tests/`. No parser rule, mapper behaviour, or
  mapping-config entry was touched. `R.ThePreExistingModuleTrees_AreUntouchedBySpec008`
  additionally asserts none of those trees names `CaseProcessing`.

## Declared-file ownership

`git diff --name-only 71c9fcc..HEAD` (the plan merge → this branch) lists exactly
the files T01–T05 declared, and nothing else:

| Task | Files changed |
|---|---|
| T01 | `TaxAI.sln`, `src/CaseProcessing/{CaseProcessing.csproj, CLAUDE.md, AGENTS.md, Models/ProcessedT4Extraction.cs, Models/T4ProcessingFields.cs, Services/SupportedProcessingTaxYears.cs, Services/PayloadStructureGuard.cs, Services/T4CaseProcessor.cs}`, `tests/CaseProcessing.Tests/{CaseProcessing.Tests.csproj, ContractTests.cs, Models/ResultOmissionTests.cs, Services/SupportedProcessingTaxYearsTests.cs}`, `status/T01.done` |
| T02 | `src/CaseProcessing/Services/PayloadStructureGuard.cs`, `tests/CaseProcessing.Tests/Services/PayloadStructureGuardTests.cs`, `status/T02.done` |
| T03 | `src/CaseProcessing/Services/T4CaseProcessor.cs`, `tests/CaseProcessing.Tests/Services/T4CaseProcessorTests.cs`, `status/T03.done` |
| T04 | `tests/CaseProcessing.Tests/EndToEnd/{T4ProcessingAcceptanceTests.cs, T4ProcessingPiiTests.cs, T4ProcessingDeterminismTests.cs}`, `status/T04.done` |
| T05 | `tests/CaseProcessing.Tests/Boundaries/CaseProcessingBoundaryTests.cs`, `status/T05.done` |
| T06 | `tests/CaseProcessing.Tests/EndToEnd/RepositoryVerificationTests.cs`, this file, `NEXT.md`, `CLAUDE.md`, `status/T06.done` |

No two tasks shared a file. `tasks.md` was never edited and no checkbox in it was
ticked; completion is recorded only by the `status/*.done` markers.

## Boundary and hygiene review

- **Project direction.** `TaxLogicEngine` references nothing. `TaxCases` and
  `DocumentExtraction` each reference only `TaxLogicEngine`. `CaseProcessing`
  references exactly `DocumentExtraction` + `TaxLogicEngine`. Nothing references
  `CaseProcessing`. Verified at the project-file, compiled-assembly, and source
  level.
- **Deterministic boundaries.** No clock, filesystem, network, database,
  environment, OCR/LLM/cloud/email, ASP.NET Core, or generated identifier in the
  module. No `MoneyRounding`, `Math.Round`, `MidpointRounding`, `double`,
  `float`, or even a `decimal` token in executable code.
- **PII seam.** The raw `T4RawInput` is handled by omission throughout and never
  reaches the result; the parsed Box 12 is handled by masking through the
  engine's `RecipientIdentifier` alone. The module declares no SIN member, no SIN
  type, no masking helper, and constructs no `RecipientIdentifier`.
- **Solution assertions.** Every one is presence-and-uniqueness. No test in this
  spec asserts a total project count, per the root `CLAUDE.md` rule.
- **Hygiene.** `git ls-files` matches no `bin/`, `obj/`, `.vs/`, `*.user`,
  secret, or `.claude/settings.local.json`. `.gitignore` covers all of them
  (`R.RepositoryHygiene_BuildOutputAndLocalSettingsAreIgnored`).

## Decisions recorded during implementation

Three judgement calls were tagged `[ASSUMED]` for one-pass human override and are
recorded here so they are not rediscovered later:

1. **T02 — structural error order is rule-major, then positional.** Every
   null-element defect first, then every blank-code defect.
   `architecture.md` fixes the rule order but not whether the two per-box rules
   interleave positionally.
2. **T03 — stage 2 is sequential, not accumulating.** A non-T4 extraction
   correctly carries no payload (spec 007 forbids one), so also reporting "a T4
   payload is required" would invent a defect that is not there.
3. **T04 — the PII surfaces are split in two.** On the omission surfaces not even
   a masked remnant may appear, while the engine's masked `RecipientIdentifier`
   is the one permitted SIN representation. A single combined rule would either
   forbid the masking the contract requires or permit a remnant where nothing
   belongs.

## Known limitation carried forward

**Stage 6 (slip/extraction year mismatch) is implemented but unreachable.**
`SupportedProcessingTaxYears` is `[2025]` and `T4SlipParser` yields 2025 only, so
a slip that parses cannot disagree with an extraction that reached stage 6. There
is therefore no live rejection test for it. Instead
`P.SlipYearGuard_IsUnreachableOnlyBecauseBothYearPoliciesAgree` pins the property
that makes it unreachable and fails the moment either policy widens — at which
point stage 6 needs a live arm. This is a deliberate defensive guard, not dead
code, and it is the one place in the feature where an AC-adjacent behaviour is
covered by an invariant rather than by a rejection.

## Open question left for the human (unchanged by this spec)

The PRD raises whether `T4SlipParser` itself should be hardened against a null
`Boxes` collection, rather than only guarded at this seam. Spec 008 deliberately
did **not** change spec 003's shipped parser; `PayloadStructureGuard` closes the
gap at the boundary. That question remains open and is not resolved here.

## Conclusion

AC-1 through AC-43 all pass. The feature is complete, the full solution suite is
green, and no pre-existing module changed. `NEXT.md` and the root `CLAUDE.md` are
updated in this task to record spec 008 complete and to name the next feature,
the Accountant Review Package.
