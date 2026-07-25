# Spec 007 — Document Extraction Contract verification

Task T05 final verification of the whole feature, against the PRD as revised by
human review (2026-07-13) and the public contract in
[architecture.md](architecture.md).

## Commands run

```
dotnet build TaxAI.sln --no-incremental
  Build succeeded. 4 Warning(s), 0 Error(s)

dotnet test TaxAI.sln
  DocumentExtraction.Tests  673 passed, 0 failed, 0 skipped
  TaxLogicEngine.Tests      261 passed, 0 failed, 0 skipped
  TaxCases.Tests            337 passed, 0 failed, 0 skipped
  TOTAL                    1271 passed, 0 failed, 0 skipped
```

The 4 warnings are pre-existing in `tests/TaxLogicEngine.Tests` (2× CS0649,
2× xUnit2008) and predate spec 007. The module and its test project build
warning-free. (A CS8620 introduced by T02 was fixed separately in PR #70; the
build here is `--no-incremental`, so no warning can be hidden by an up-to-date
project.)

## Test-suite growth

| Wave | Task | Suite | Cumulative `DocumentExtraction.Tests` |
|---|---|---|---|
| 0 | T01 | contract, payload omission, tax years | 148 |
| 1 | T02 | `ExtractionEnvelopeValidatorTests` | 307 |
| 2 | T03 | acceptance, PII, determinism (end-to-end) | 633 |
| 2 | T04 | `DocumentExtractionBoundaryTests` | 658 |
| 3 | T05 | `RepositoryVerificationTests` | 673 |

## Acceptance criteria

All test names below are in `tests/DocumentExtraction.Tests`.

| AC | Result | Evidence |
|---|---|---|
| AC-1 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC1_ValidT4Envelope_SucceedsAndExposesThePayloadReferenceIdentical`; `EndToEnd.ExtractionAcceptanceTests.ValidT4Envelope_IsAcceptedWithExactlyTheSuppliedAttribution` |
| AC-2 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC2_ValidGenericEvidenceEnvelope_SucceedsWithSourceReferenceAndNoPayload`; `EndToEnd.ExtractionAcceptanceTests.ValidGenericEvidenceEnvelope_IsAcceptedWithItsSourceReferenceAndNoPayload` |
| AC-3 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC3_BlankDocumentId_FailsNamingTheDocumentIdField` (null, empty, spaces, tab, CRLF) |
| AC-4 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC4_AbsentOrUndefinedKind_FailsNamingTheKindField` (null, `(ExtractedDocumentKind)0`, `3` as a T5 stand-in, `99`, `-1`) |
| AC-5 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC5_BlankFirmId_FailsNamingTheFirmIdField` |
| AC-6 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC6_BlankCaseId_FailsNamingTheCaseIdField` |
| AC-7 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC7_BlankParticipantId_FailsNamingTheParticipantIdField` |
| AC-8 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC8_BlankSourceReferenceOnAT4Envelope_FailsNamingTheSourceReferenceField`; `AC8_BlankSourceReferenceOnAGenericEvidenceEnvelope_FailsNamingTheSourceReferenceField`; `AC8_AbsentProvenance_ReportsTheSourceReferenceError` |
| AC-9 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC9_AbsentEnvelopeTaxYear_FailsAndIsNeverCoerced` |
| AC-10 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC10_UnsupportedEnvelopeTaxYear_FailsWithNoCoercionOrAdjacentYearFallback` (2024, 2026, 1999) |
| AC-11 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC11_AbsentPayloadYear_FailsNamingThePayloadYearField` |
| AC-12 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC12_EnvelopePayloadYearMismatch_FailsWithNeitherValueCoerced` |
| AC-13 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC13_T4SlipWithoutPayload_FailsNamingThePayloadField`; `AC13_GenericEvidenceCarryingAT4Payload_FailsNamingThePayloadField` |
| AC-14 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC14_EmptyT4Extraction_FailsNamingThePayloadContentField`; `AC14_NullBoxesWithBlankIdentityText_IsAlsoAnEmptyExtraction`; converse proved by `AC14_AnyIdentityTextOrAnyBox_IsNotAnEmptyExtraction` |
| AC-15 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC15_ConfidenceOutsideTheInclusiveRange_FailsNamingTheConfidenceField` (`-0.1m`, `1.1m`, `-1m`, `2m`) |
| AC-16 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC16_ConfidenceAtBoundsOrAbsent_IsAccepted` (`0m`, `1m`, `0.5m`, absent) |
| AC-17 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC17_WhitespaceOnlyReferences_AreTreatedExactlyAsBlank` (all five references) |
| AC-18 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC18_DuplicateBoxCodes_SurviveValidationInOrder` |
| AC-19 | **PASS** | `Services.ExtractionEnvelopeValidatorTests.AC19_EmptyEnvelope_ReportsOneErrorPerMissingRequiredFieldAndThrowsNothing` (7 fields, one error each, fixed order) |
| AC-20 | **PASS** | `EndToEnd.ExtractionAcceptanceTests.AC20_AcceptedResult_IsNeitherASlipAMappingResultNorALineEntry`; `AC20_AcceptedResult_ExposesNoPropertyOfAnyTaxTruthType`; `AC20_AcceptedResult_ExposesTheRawPayloadUninspectedAndUnconverted` |
| AC-21 | **PASS** | `EndToEnd.ExtractionAcceptanceTests.AC21_APayloadTheEngineWouldRejectStillClearsTheEnvelopeGate`; `Services.ExtractionEnvelopeValidatorTests.AC21_BoxValueTheEngineWouldReject_StillPassesTheEnvelopeGate` |
| AC-22 | **PASS** | `EndToEnd.ExtractionPiiTests.AC22_Box12HoldingARealSinOnAnAcceptedEnvelope_IsOmittedFromEveryOutput`; `AC22_AnyPlantedValueOnAnAcceptedEnvelope_StaysInMemoryButLeavesNoOutput`; `Models.PayloadOmissionTests` (value level) |
| AC-23 | **PASS** | `EndToEnd.ExtractionPiiTests.AC23_NoRejection_EverEchoesAPlantedValueInAnyErrorOrDiagnostic` (11 rejection families); `AC23_ARejectionCarryingASinInBox12AndMarkersInEveryReference_LeaksNothing`; validator arm `Services.ExtractionEnvelopeValidatorTests.AC23_RejectedEnvelope_NeverEchoesAPlantedValueInAnyError` |
| AC-24 | **PASS** | `Boundaries.DocumentExtractionBoundaryTests.AC24_ThePublicSurfaceDeclaresNoSinPropertyOrSinType`; `ProductionSource_ContainsNoForbiddenDependencyOrBehavior` with the `RecipientIdentifier / SIN representation` and `masking helper` data |
| AC-25 | **PASS** | `EndToEnd.ExtractionAcceptanceTests.AC25_AValidatedExtractionExistsOnlyWhenBothYearsAre2025` (exhaustive 4×4 year matrix); `AC25_The2024And2026EnvelopesAndThe2025Envelope2024PayloadAllFailClosed`; `AC25_AGenericEvidenceEnvelopeIsAlsoYearIsolated` |
| AC-26 | **PASS** | `Boundaries.DocumentExtractionBoundaryTests.AC26_NoCraLineNumberLiteralAppearsInTheModule`; `ProductionSource_ContainsNoForbiddenDependencyOrBehavior` with the `engine parser/mapper/rounding`, `CRA line mapping`, `mapping resource read`, and `rounding / money math` data; `EndToEnd.RepositoryVerificationTests.AC26_AC27_NoForbiddenBoundaryTokenSurvivesInTheModule` |
| AC-27 | **PASS** | `ProductionSource_ContainsNoForbiddenDependencyOrBehavior` with the `system clock`, `filesystem I/O`, `network I/O`, `database`, `environment / current user`, `OCR / LLM / cloud / email`, `ASP.NET Core`, and `generated identifier` data; `EndToEnd.RepositoryVerificationTests.AC26_AC27_NoForbiddenBoundaryTokenSurvivesInTheModule` |
| AC-28 | **PASS** | `EndToEnd.ExtractionDeterminismTests` — 5 tests over 4 accepted and 11 rejected cases, 5 replays each, asserting equal error ordering, equivalence across distinct envelopes, and that validation mutates nothing; plus `Services.ExtractionEnvelopeValidatorTests.AC28_*` |
| AC-29 | **PASS** | `EndToEnd.RepositoryVerificationTests.AC29_EveryProjectReferenceIsExactlyTheApprovedSetAndPointsInward`; `AC29_NoReverseDependencyOnDocumentExtractionExistsAnywhere`; `AC29_TheModuleAssemblyReferencesOnlyTaxLogicEngine`; `Spec007ChangedNothingUnderTaxLogicEngineOrTaxCasesProduction`; and the full suite green (261 TaxLogicEngine + 337 TaxCases unchanged) |
| AC-30 | **PASS** | `EndToEnd.RepositoryVerificationTests.AC30_ThePayloadAndResultTypesAreTheEnginesOwnAndNothingIsDuplicated`; `AC30_NoT5KindOrT5TokenExistsInTheModule`; `Boundaries.DocumentExtractionBoundaryTests.AC30_NoT5KindExistsAndTheKindSetIsExactlyT4SlipAndGenericEvidence` |

**30 of 30 acceptance criteria pass. 0 fail.**

## Repository-level evidence

- Six solution projects present at their approved paths, every declared project
  and reference resolvable:
  `EndToEnd.RepositoryVerificationTests.AllSixExpectedProjectsAreDeclaredOnceAndEveryReferenceResolves`.
- Public contract against architecture.md:
  `EndToEnd.RepositoryVerificationTests.PublicContractMatchesTheArchitectureManifestExactly`
  (exactly 8 exported types) and `ContractTests` (T01 reflection suite).
- No public `ValidatedExtraction` constructor:
  `EndToEnd.RepositoryVerificationTests.NoPublicValidatedExtractionConstructorExists`.
- Ingress port signature and absence of any production implementor:
  `EndToEnd.RepositoryVerificationTests.TheIngressPortDeclaresExactlyTheApprovedAsyncSignatureAndHasNoProductionImplementor`.
- Representative accepted flows:
  `ARepresentativeT4FlowIsAcceptedAndCarriesNoTaxTruth`,
  `ARepresentativeGenericEvidenceFlowIsAccepted`.
- No unsupported year accepted: `NoUnsupportedYearIsEverAccepted` (2023, 2024,
  2026, 2027).
- Tracked-file hygiene: `NoForbiddenFileIsTrackedAnywhereInTheRepository`.
- TaxLogicEngine and TaxCases production trees unchanged since spec 007 began:
  `Spec007ChangedNothingUnderTaxLogicEngineOrTaxCasesProduction`.

## Anti-vacuity measures

A suite that silently stops testing is worse than no suite, so three guards
were added and two mutation checks were run:

- **Scanner guard** —
  `Boundaries.DocumentExtractionBoundaryTests.TheForbiddenPatternScan_ActuallyMatchesSomething_SoItCannotPassVacuously`
  asserts the file walk sees all 8 production files and that a known-present
  pattern still matches. Without it, a broken walk would make every
  forbidden-token assertion pass for free.
- **PII matrix guard** —
  `EndToEnd.ExtractionPiiTests.EveryPlantSlotIsActuallyReachable_SoTheMatrixCannotSilentlyStopCovering`
  asserts each of the 10 plant slots is readable back off the accepted result.
- **Non-numeric markers** — the PII matrix plants `ZZMARKERZZ` and `<<leak-me>>`
  alongside the three SIN forms, because a nine-digit regex cannot catch an
  echoed document id or source reference.
- **T02 mutation check** — disabling the two year-isolation rules (unsupported
  envelope year, envelope/payload year coherence) failed 66 tests.
- **T04 mutation check** — injecting `DateTime.UtcNow`, `RecipientIdentifier`,
  the CRA line number `10100`, a `double`, and a T5 type name into a production
  file failed exactly 5 boundary tests.

Both mutations were reverted before their commits; no mutation is present in
any merged diff.

## Scope and ownership review

- Every task changed only its declared files, verified per task with
  `git diff --name-only origin/master..HEAD` and recorded in each
  `status/T0N.done` marker.
- `src/TaxLogicEngine/` and `src/TaxCases/` are unchanged by spec 007.
  `tests/TaxCases.Tests/` was changed only by two repository-hygiene PRs
  (#65, #66) that are explicitly **not** part of any spec 007 task — see the
  deviation note below.
- Dependency direction is one-way and inward:
  `DocumentExtraction → TaxLogicEngine`, with no `TaxCases` reference in either
  direction and no reverse reference to this module from anywhere.
- No tracked `bin/`, `obj/`, `.vs/`, `*.user`, secret, or
  `.claude/settings.local.json`.

## Deviations and decisions

### `[ASSUMED]` — project-count exclusivity deliberately not asserted

`tasks.md` T05 specifies "Negative tests prove no unexpected project/reference".
Reference exclusivity **is** asserted strictly
(`AC29_EveryProjectReferenceIsExactlyTheApprovedSetAndPointsInward`). A total
**project-count** assertion deliberately is not, because:

1. No acceptance criterion requires it. AC-29 is about references and
   dependency direction; AC-30 is about reused types. Presence-and-uniqueness
   satisfies both in full.
2. It is a known repository defect, not a safeguard. Spec 006 shipped exactly
   that assertion, and it blocked this spec's own Wave 0 — task T01 could not
   reach a green suite while also being required to add two projects. It was
   fixed in PR #66 by moving to presence-and-uniqueness. Re-introducing it here
   would block spec 008's first scaffold task in precisely the same way.

The tests therefore prove all six expected projects are each declared exactly
once at their approved path, so a missing, duplicated, renamed, or relocated
project still fails, while the solution stays free to grow.

This was flagged to the human before implementation and is tagged `[ASSUMED]`
for one-pass override: if project-count exclusivity is wanted, it is a
two-line change to `AllSixExpectedProjectsAreDeclaredOnceAndEveryReferenceResolves`.

### Out-of-task repository fixes (context, not spec 007 work)

Three PRs outside the spec 007 task list were needed and were merged
separately, each on its own branch with its own human review:

- **#65** — a spec 006 verification regex used `\|$` under
  `RegexOptions.Multiline`; .NET's `$` never matches before `\r\n`, so the test
  failed on any Windows/CRLF checkout, including a clean `master`. Pre-existing
  and unrelated to spec 007.
- **#66** — the two spec 006 "exactly four projects" assertions, relaxed to
  presence-and-uniqueness (see above).
- **#70** — a CS8620 nullability warning introduced by T02, fixed after T04
  surfaced it under a `--no-incremental` build.

None was folded into a spec 007 task, because `tests/TaxCases.Tests/` is
declared untouchable by every spec 007 task and `ExtractionEnvelopeValidatorTests.cs`
is T02's declared file.
