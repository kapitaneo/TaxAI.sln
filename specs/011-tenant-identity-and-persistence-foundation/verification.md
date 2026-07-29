# Verification: Tenant Identity and Persistence Foundation

Spec 011 is verified below criterion by criterion. Test names are fully
qualified by their test class; the repository gate is
`pwsh ./scripts/Invoke-SolutionTests.ps1`. The seven-year retention default
verified here is an engineering product decision, not a CRA fact or statutory
minimum.

| AC | Mechanical proof |
|---|---|
| AC-1 | `TenancyBoundaryTests.TheProjectDeclaresTheApprovedSettings` |
| AC-2 | `PersistenceBoundaryTests.TheProjectDeclaresTheApprovedSettings` |
| AC-3 | `TenancyBoundaryTests.TheProjectReferencesExactlyCaseReviewAndTaxLogicEngine` |
| AC-4 | `TenancyBoundaryTests.TheProjectDeclaresNoPackageReference` |
| AC-5 | `TenancyBoundaryTests.ProductionSource_ContainsNoForbiddenToken` |
| AC-6 | `PersistenceBoundaryTests.TheProjectReferencesExactlyTenancy` |
| AC-7 | `PersistenceBoundaryTests.PersistenceIsTheOnlyProductionProjectThatReferencesEfCore` |
| AC-8 | `PersistenceBoundaryTests.TheProjectUsesTheSqlServerProviderAndNoOther` |
| AC-9 | `PersistenceBoundaryTests.TheModuleTakesNoAspNetCoreDependency` |
| AC-10 | `ProjectStructureTests.PortalProject_ReferencesExactlyTheFourInwardModules` |
| AC-11 | `ProjectStructureTests.PortalProject_DeclaresExactlyTheOneApprovedPackage` and `PortalProject_PinsTheApprovedPackageToAnExactVersion` |
| AC-12 | `PortalBoundaryTests.ThePortalProject_DeclaresNoOrmDependency` |
| AC-13 | `PortalBoundaryTests.ThePortalProject_ReferencesNeitherTaxCasesNorCaseReview` and `TheCompiledPortal_NamesNoModuleItReachesThroughAContract` |
| AC-14 | `PortalBoundaryTests.NoPreExistingProjectReferencesThePortal` |
| AC-15 | `TenancyBoundaryTests.TheProjectReferencesExactlyCaseReviewAndTaxLogicEngine` and `ProductionSource_ContainsNoForbiddenToken` |
| AC-16 | `TenancyBoundaryTests.NoProjectUsesAReservedFutureModuleName` |
| AC-17 | `TenancyBoundaryTests.TheSolutionDeclaresTheNewProjectExactlyOnceAtItsApprovedPath` |
| AC-18 | `TenancyBoundaryTests.EveryPreExistingSolutionDeclarationSurvivedTheAppend` and `SolutionAndRegressionTests.EveryPreExistingProjectKeptItsConfigurationRows` |
| AC-19 | `TenancyBoundaryTests.TheNewTestProjectPinsTheRepositoryStandardToolingVersion` |
| AC-20 | `TenancyBoundaryTests.EveryTestProjectIsRegisteredInTheExecutionManifestExactlyOnce` |
| AC-21 | `SolutionAndRegressionTests.TheModulePublicSurfaceIsUnchanged`, `TheModuleKeptItsExactReferenceSet`, and the T21 changed-file allowlist review |
| AC-22 | `TenancyBoundaryTests.ThePreExistingInternalsGrantsAreStillExactlyThree` |
| AC-23 | `TenancyContractTests.NoGenericRepositoryAbstractionExists`, `NoDeclaredMemberExposesAQueryableOrAnUntypedSequence`, and `PersistenceContractTests.NoGenericRepositoryAbstractionExists` |
| AC-24 | `SolutionAndRegressionTests.EverySolutionTestProjectIsRegisteredInTheFullSuiteGate` plus the green solution test gate recorded below |
| AC-25 | `SolutionAndRegressionTests.TheModulePublicSurfaceIsUnchanged` and the unchanged five pre-existing module suites |
| AC-26 | `SolutionAndRegressionTests.TheEmbeddedMappingResourcesKeepTheirExactLogicalNames` |
| AC-27 | `TenancyBoundaryTests.NoPreExistingModuleGainedAPackageOrFrameworkReference` and `SolutionAndRegressionTests.TheModuleStillDeclaresNoPackageOrFrameworkReference` |
| AC-28 | `AmendmentAuditTests.EveryRequirementAmendmentNamesALiveStrictlyNarrowerProof`, `EveryShippedAssertionAmendmentHasANamedReplacementOrPreservation`, and `NoSupersededAssertionRowCanDisappearWithoutFailingTheAudit` |
| AC-29 | `HealthEndpointTests.HealthReturnsTheFixedLiteralWithTheHealthyStatus` and `HealthDisclosesNothingAboutTheSystem` |
| AC-30 | `ShellPageTests.TheRootReturnsTheFixedShellDocument` and `TheShellCarriesNoDynamicValue` |
| AC-31 | `CompositionRootTests.BothValidationFlagsAreSetOutsideAnyConditionalBlock` |
| AC-32 | `CompositionRootTests.EveryIsDevelopmentOccurrenceGuardsAnApprovedConcern` and `TheEnvironmentTokenIsNeverUsedToTurnOffARequirement` |
| AC-33 | `ProjectStructureTests.ThePortalsOnlyPublicTypeIsProgram` and `Program_DeclaresNoPublicMemberOfItsOwn` |
| AC-34 | `CompositionRootTests.TheUntrustedIngressPortIsNotRegistered` and `ThePortalImplementsNoIngress` |
| AC-35 | `ReviewRecordRepositoryTests.SaveAndFindRoundTripTheCompleteProjectedGraph` |
| AC-36 | `SignInTests.ExactlyOneActiveMembershipIssuesTheFirmClaimAndScopesTheSession` |
| AC-37 | `FirmSelectionTests.AValidSelectionRevokesTheOldSessionAndIssuesANewSelectedSession` |
| AC-38 | `ReviewRecordRepositoryTests.FirstSaveStampsVersionInstantsRetentionAndTrustedFirm` |
| AC-39 | `ReviewRecordVersioningTests.SavingTheSameSubjectCreatesTheNextImmutableVersion` |
| AC-40 | `ReviewPackageProjectionRejectionTests.Project_NullPackageAndIdentifierSource_FailsClosedWithoutThrowing` |
| AC-41 | `TenancyContractTests.EveryTaxpayerDataOperationRequiresATrustedFirm`, `TheOnlyFirmOmittingLookupsAreTheEnumeratedFour`, and `TheFirmScopeExemptionListIsExactlyFourNamedEntries` |
| AC-42 | `ReviewPackageProjectionRejectionTests.Project_PackageFirmMismatch_FailsClosedAndNeverStoresThePackageFirm` |
| AC-43 | `ReviewPackageProjectionRejectionTests.Project_UnsupportedTaxYear_FailsIdenticallyWithoutFallback` with the backward-year case |
| AC-44 | `ReviewPackageProjectionRejectionTests.Project_UnsupportedTaxYear_FailsIdenticallyWithoutFallback` with the forward-year case |
| AC-45 | `ReviewPackageProjectionRejectionTests.Project_SlipTaxYearDivergence_FailsClosed` |
| AC-46 | `ReviewPackageProjectionRejectionTests.Project_AmountThatRequiresAlteration_FailsForBoxAndLineValues` |
| AC-47 | `ReviewPackageProjectionRejectionTests.Project_OverLengthTaxpayerDisplayName_FailsWithoutTruncating` |
| AC-48 | `ReviewPackageProjectionRejectionTests.Project_ConsecutiveDigitRunInEmployerDisplayName_FailsWithoutLeakingIt` |
| AC-49 | `ReviewPackageProjectionRejectionTests.Project_UnsafeCaseIdentifier_FailsClosed` and `Project_OverLengthCaseIdentifier_FailsClosedWithoutTruncating` |
| AC-50 | `ReviewPackageProjectionRejectionTests.Project_UnsafeParticipantIdentifier_FailsClosed` |
| AC-51 | `SlipTypeCodeTests.IsRecognized_RefusesEveryOtherValueWithoutNormalization` |
| AC-52 | `ReviewRecordTenantIsolationTests.CrossFirmReadIsIdenticalToNotFound` |
| AC-53 | `MembershipVerificationPolicyTests.Verify_SessionWithoutAnActiveFirm_FailsClosed` |
| AC-54 | `TrustedFirmIdTests.TryParse_RefusesValuesThatAreNotCanonicalGuidText` |
| AC-55 | `SignInTests.AProviderSuppliedFirmClaimIsDiscardedBeforeTheLocalPrincipalExists` and `AProviderSuppliedFirmClaimCannotScopeAnOtherwiseUnscopedSession` |
| AC-56 | `TenantIsolationAtTheHttpBoundaryTests.AQueryStringFirmIdentifierDoesNotChangeTheBootstrapResponse` |
| AC-57 | `TenantIsolationAtTheHttpBoundaryTests.HeaderAndCookieFirmIdentifiersDoNotChangeTheBootstrapResponse` (header case) |
| AC-58 | `TenantIsolationAtTheHttpBoundaryTests.HeaderAndCookieFirmIdentifiersDoNotChangeTheBootstrapResponse` (cookie case) |
| AC-59 | `TenantIsolationAtTheHttpBoundaryTests.ProductionReadsNoFirmIdentifierFromAnyRequestChannel` |
| AC-60 | `FirmSelectionTests.AForeignMembershipAndAMissingMembershipAreIndistinguishable` |
| AC-61 | `FirmSelectionTests.ADisabledMembershipCannotCreateOrReplaceASession` |
| AC-62 | `FirmSelectionTests.SelectionWithoutAntiforgeryIsRefusedBeforeSessionMutation` |
| AC-63 | `SignInTests.ZeroActiveMembershipsLeavesTheSessionUnscopedAndIssuesNoClaim` |
| AC-64 | `SignInTests.SeveralActiveMembershipsLeaveTheSessionUnscopedAndIssueNoClaim` |
| AC-65 | `MembershipVerificationTests.ADisabledMembershipIsRefusedByTheFullFirmScopedStage` |
| AC-66 | `TenantIsolationAtTheHttpBoundaryTests.AnUnauthenticatedFirmsRequestIsRefusedWithoutContent` |
| AC-67 | `SessionLifetimeTests.AnIdleExpiredSessionIsRefusedAndRebuildsNoPrincipal` and `TheIdleBoundaryIsReachedNotMerelyPassed` |
| AC-68 | `SessionLifetimeTests.AnAbsoluteExpiredSessionIsRefusedEvenWhileItsIdleWindowIsOpen` |
| AC-69 | `SessionRevocationTests.AfterSignOutTheClientIsUnauthenticated` |
| AC-70 | `ReviewRecordPurgeServiceTests.TheServiceIsInternalSealedAndImplementsOnlyThePurgePort` and `TenancyContractTests.EveryTaxpayerDataOperationRequiresATrustedFirm` |
| AC-71 | `ReviewRecordPurgeServiceTests.OneTickBeforeTheExclusiveBoundarySparesTheRecord` |
| AC-72 | `ReviewRecordPurgeServiceTests.PurgeScopedToAnotherFirmCannotDeleteAnExpiredRecord` |
| AC-73 | `ReviewRecordTenantIsolationTests.RepositorySurfaceHasNoUpdateUpsertOrDeletePath` and `TenancyContractTests.TheRecordRepositoryDeclaresNoUpdateOrDeletePath` |
| AC-74 | `StartupRefusalTests.StartupRefusesWithoutAConnectionString` |
| AC-75 | `StartupRefusalTests.StartupRefusesAnUnprotectedTransportOutsideDevelopment` |
| AC-76 | `StartupRefusalTests.StartupRefusesAnUnconfiguredKeyRingOutsideDevelopment`, `StartupRefusesKeyRingSettingsWithNoRegisteredProvider`, `StartupRefusesARegisteredProviderWithNoExplicitRepository`, and `StartupRefusesAnApprovedRepositoryWithNoEncryptor` |
| AC-77 | `StartupRefusalTests.SeedingIsDisabledOutsideDevelopment` |
| AC-78 | `MonetaryValueGuardTests.Validate_AcceptsAmountsThatFitTheDeclaredPrecisionAndScale` and `ReviewRecordRepositoryTests.SaveAndFindRoundTripTheCompleteProjectedGraph` |
| AC-79 | `ReviewPackageProjectionTests.Project_AmountsAreCopiedBitForBitWithoutRescaling` and `ReviewRecordRepositoryTests.SaveAndFindRoundTripTheCompleteProjectedGraph` |
| AC-80 | `ReviewRecordRepositoryTests.SaveAndFindRoundTripTheCompleteProjectedGraph` |
| AC-81 | `ReviewRecordRepositoryTests.EmptyPackagePersistsAsARecordWithNoSlips` |
| AC-82 | `ReviewRecordRepositoryTests.EmptyUnmappedCodesAndAbsentOptionalSlipFieldsAreAccepted` |
| AC-83 | `ReviewRecordRepositoryTests.EmptyUnmappedCodesAndAbsentOptionalSlipFieldsAreAccepted` and `PiiSchemaTests.NoForbiddenPiiColumnOrPresenceFlagExists` |
| AC-84 | `ReviewRecordRepositoryTests.EveryCollectionRoundTripsInItsOriginalOrder` |
| AC-85 | `ReviewRecordRepositoryTests.DuplicateLineCodesRemainSeparateRowsAndAreNeverAggregated` |
| AC-86 | `PiiSchemaTests.NoForbiddenPiiColumnOrPresenceFlagExists` |
| AC-87 | `PiiSchemaTests.NoForbiddenPiiColumnOrPresenceFlagExists` |
| AC-88 | `ReviewPackageProjectionTests.Implementation_IsAnExplicitProjectionWithoutUnsafeSourceReadsOrCollectionTransforms` |
| AC-89 | `PiiAtRestTests.AFullSavePersistsNoNineDigitRunFromTheUpstreamSin` |
| AC-90 | `TenancyContractTests.EveryErrorMessageIsAFixedConstantWithNoFormatPlaceholder` and the projection rejection suite |
| AC-91 | `DiagnosticsLeakTests.TheDiagnosticsAuditCoversTheFullSaveReadSignInAndPurgeWorkflow`, `SaveReadAndSignInDeclareNoDiagnosticSink`, `PurgeIsTheOnlyLoggerAndItsTemplateAndArgumentsAreStructural`, plus both `PurgeDiagnosticsTests` runtime cases |
| AC-92 | `TenancyContractTests.EveryErrorMessageIsAFixedConstantWithNoFormatPlaceholder` and `ReviewPackageProjectionRejectionTests.Project_IndependentFailuresAccumulateInTheDeclaredStageOrder` |
| AC-93 | `PersistedReadModelTests.EveryToStringEmitsOnlyItsFixedTypeIdentity`, `MembershipVerificationPolicyTests.IdentityModelToStrings_EmitNoIdentityOrDisplayValue`, and `AuthenticationSessionConstructionTests.RenderingDisclosesNoIdentifierOrInstant` |
| AC-94 | `PersistedReadModelTests.DirectSerializationContainsNoCallerOrTaxpayerContent` |
| AC-95 | `PortalSecurityTests.ProductionSource_ContainsNoPiiToken` and `ProductionSource_ContainsNoNineDigitLiteral` |
| AC-96 | `PortalSecurityTests.NeitherResponseBodyContainsANineDigitRun` and `TheHealthBodyDisclosesNothingAboutTheSystem` |
| AC-97 | `ShellPageTests.NoRouteParameterNamesAnIdentifierOfAnyKind` and `TenantIsolationAtTheHttpBoundaryTests.ProductionReadsNoFirmIdentifierFromAnyRequestChannel` |
| AC-98 | `CookieAndTokenTests.TheDecryptedCookieCarriesNoFirmNoUserAndNoTaxpayerValue` and `TheSetCookieHeaderNamesNoIdentifierInTheClear` |
| AC-99 | `CookieAndTokenTests.NeitherTheCookieNorTheSessionRecordCarriesAnOidcToken` and `SignInTests.TheExternalHandlerSavesNoToken` |
| AC-100 | `PiiSchemaTests.IdentityTablesCarryOnlyTheApprovedStructuralColumns` |
| AC-101 | `DevelopmentSeederTests.EverySeededStringIsSyntheticAndContainsNoNineDigitRun` and `SeedSourceContainsNoForbiddenPersonalFieldOrIdentityLiteral` |
| AC-102 | `DevelopmentSeederTests.SeederWritesOneCompleteObviouslySyntheticReviewFixture` |
| AC-103 | `ReviewRecordRepositoryTests.FirstSaveStampsVersionInstantsRetentionAndTrustedFirm` |
| AC-104 | `ReviewRecordVersioningTests.VersionSequenceIsScopedByTheCompleteSubjectKey` and `ReviewRecordTenantIsolationTests.CrossFirmReadIsIdenticalToNotFound` |
| AC-105 | `TenancyBoundaryTests.NoTaxYearLiteralAppearsInTheModule` and `PersistenceBoundaryTests.ProductionSource_ContainsNoForbiddenToken` |
| AC-106 | `TenancyBoundaryTests.ProductionSource_ContainsNoForbiddenToken` and `ReviewPackageProjectionTests.Implementation_IsAnExplicitProjectionWithoutUnsafeSourceReadsOrCollectionTransforms` |
| AC-107 | `RetentionPolicyTests.ComputeRetentionUntilUtc_DerivesTheBoundaryFromTheTaxYearAlone` |
| AC-108 | `TenancyBoundaryTests.ProductionSource_ContainsNoForbiddenToken` and `SessionLifetimePolicyTests.ProductionSource_UsesNoClockTimerOrInclusiveBoundaryConstruction` |
| AC-109 | `SolutionAndRegressionTests.NoNewModulePortalMigrationSeedOrFixtureHardCodesACraLineNumber` |
| AC-110 | `ReviewPackageProjectionTests.Project_ValidPackage_CopiesTheClosedAllowlistInOrder` and `ReviewRecordRepositoryTests.SaveAndFindRoundTripTheCompleteProjectedGraph` |
| AC-111 | `TenancyBoundaryTests.ProductionSource_ContainsNoForbiddenToken`, `PersistenceBoundaryTests.ProductionSource_ContainsNoForbiddenToken`, and `PortalBoundaryTests.ThePortalDeclaresNoBoxCodeOrCraLineConcept` |
| AC-112 | `TenancyBoundaryTests.ProductionSource_InvokesNoEngineService`, `PersistenceBoundaryTests.ProductionSource_ContainsNoForbiddenToken`, and `PortalBoundaryTests.ProductionSource_ContainsNoForbiddenToken` |
| AC-113 | `MonetaryValueGuardTests.Implementation_InspectsTextWithoutMonetaryArithmeticOrAdjustment` |
| AC-114 | `PortalBoundaryTests.ProductionSource_ContainsNoForbiddenToken` |
| AC-115 | `PiiSchemaTests.NoUpstreamDomainTypeOrSerializerIsMapped` and `ModelShapeTests.TheModelMapsExactlyTheExplicitPersistenceEntities` |
| AC-116 | `PiiSchemaTests.NoUpstreamDomainTypeOrSerializerIsMapped` |
| AC-117 | `TenantIsolationSchemaTests.FirmIdLeadsEveryTenantKeyAndIndex` and `EveryReviewHierarchyForeignKeyCarriesFirmIdFirst` |
| AC-118 | `TenantIsolationSchemaTests.FirmIdLeadsEveryTenantKeyAndIndex` |
| AC-119 | `TenantIsolationSchemaTests.EveryFirmIdentifierUsesUniqueidentifier` |
| AC-120 | `TenantIsolationSchemaTests.EveryStringIdentifierDeclaresTheBinaryCollation` |
| AC-121 | `ModelShapeTests.EveryMonetaryPropertyDeclaresExplicitPrecisionAndScale` |
| AC-122 | `TenantIsolationSchemaTests.SnapshotSubjectAndVersionAreUniqueWithinFirm` |
| AC-123 | `ModelShapeTests.ReviewReadModelDeclaresExactlyTheClosedAllowlist` and `PersistedReadModelTests.EveryPersistedTypeDeclaresExactlyItsClosedAllowlist` |
| AC-124 | `PersistenceBoundaryTests.TheModuleCallsNoSchemaMutatingApi` and `CompositionRootTests.ResolvingTheStorageLayerOpensNoConnectionAndCreatesNoSchema` |
| AC-125 | `MigrationTests.InitialMigrationContainsNoSeedOrDataMutationOperation` and `MigrationFilesContainNoNineDigitRunOutsideTheMigrationIdAttribute` |
| AC-126 | `ReviewPackageProjectionRejectionTests.Project_PackageFirmMismatch_FailsClosedAndNeverStoresThePackageFirm` and `ReviewRecordTenantIsolationTests.ProjectedRecordFirmMismatchIsRefusedBeforeAnythingIsWritten` |
| AC-127 | `ShellPageTests.TheHostMapsExactlyTheApprovedRoutes` and `EachApprovedRouteAnswersExactlyItsOneMethod` |
| AC-128 | `ShellPageTests.NoMappedEndpointTakesARouteParameter` and `NoRouteParameterNamesAnIdentifierOfAnyKind` |
| AC-129 | `ShellPageTests.EveryEndpointIsEitherEnumeratedAnonymousOrAuthorizationRequired` |
| AC-130 | `ShellPageTests.EveryStateChangingRouteRequiresAntiforgery` and `FirmSelectionTests.SelectionWithoutAntiforgeryIsRefusedBeforeSessionMutation` |
| AC-131 | `CookieAndTokenTests.TheAuthenticationCookieIsHttpOnlySecureAndSameSiteLax` |
| AC-132 | `PortalSecurityTests.AnUnmatchedRouteLeaksNoDiagnosticDetail` and `HealthEndpointTests.AnUnmatchedRouteInProductionReturns404WithNoDiagnosticDetail` |
| AC-133 | `PortalSecurityTests.AnyDeveloperExceptionPageIsConfinedToDevelopment` |
| AC-134 | `PortalSecurityTests.ProductionSource_LogsNoRequestContent` and the telemetry rows in `LoggingPatterns` |
| AC-135 | `PortalSecurityTests.TrackedConfiguration_CarriesNoSecret` and `StartupRefusalTests.NoRefusalMessageEchoesAConfiguredValue` |
| AC-136 | `PortalSecurityTests.TrackedConfiguration_BindsOnlyToLocalhost` |
| AC-137 | `PortalSecurityTests.TrackedConfiguration_CarriesNoSecret`, `.gitignore`, and the full-suite tracked-file gate |
| AC-138 | `PortalSecurityTests.TheRepositoryHasNoClientSideStack` |
| AC-139 | `PersistenceRegistrationTests.RegistrationAddsNoHostedOrBackgroundService`, `SessionLifetimePolicyTests.ProductionSource_UsesNoClockTimerOrInclusiveBoundaryConstruction`, and `AuthenticationSessionStoreTests.StoreDeclaresNoTimerHostedServiceOrSchedulerSurface` |
| AC-140 | `PurgeDiagnosticsTests.CompletedPurgeLogsOnlyStructuralCountsAndTheOpaqueFirmIdentifier` and `DiagnosticsLeakTests.PurgeIsTheOnlyLoggerAndItsTemplateAndArgumentsAreStructural` |
| AC-141 | `ProjectStructureTests.NewModuleInstructions_StateTheirExactBoundaries` |
| AC-142 | `ProjectStructureTests.NewModuleAgentsBridges_ArePointersNotRulebooks` |
| AC-143 | `ProjectStructureTests.PortalInstructions_StateTheNarrowedBoundaryAndGate` |
| AC-144 | `RepositoryVerificationTests.NoExistingModuleInstructionFileWasRewrittenForTheHost` |
| AC-145 | `RepositoryVerificationTests.TheReadmeDocumentsEveryProjectTreeThatExists`, `TheReadmeNamesTheFoundationCapabilitiesThatShip`, and `TheReadmeExplicitlyDisclaimsEveryUnshippedSurface` |
| AC-146 | `RepositoryVerificationTests.TheRootInstructionsListTheNewTree`, `TheRootInstructionsRecordTheDeliveredScope`, and `TheRootInstructionsStillForbidATotalProjectCount` |
| AC-147 | `RepositoryVerificationTests.TheForwardRoadmapRecordsTheResolvedAndReservedQuestions` |
| AC-148 | `RetentionPolicyTests.ComputeRetentionUntilUtc_ReturnsTheExclusiveJanuaryBoundary` |
| AC-149 | `RetentionPolicyTests.IsPurgeEligible_OneTickBeforeTheExclusiveBoundary_ReturnsFalse` and `ReviewRecordPurgeServiceTests.OneTickBeforeTheExclusiveBoundarySparesTheRecord` |
| AC-150 | `RetentionPolicyTests.IsPurgeEligible_AtTheExclusiveBoundary_ReturnsTrue` and `ReviewRecordPurgeServiceTests.TheBoundaryInstantDeletesTheRecordAndItsCompleteGraph` |
| AC-151 | `RetentionPolicyTests.IsPurgeEligible_AfterTheExclusiveBoundary_ReturnsTrue` and `ReviewRecordPurgeServiceTests.ALaterInstantIsPurgeEligible` |
| AC-152 | `RetentionPolicyTests.ProductionSource_UsesNoYearLiteralClockOrInclusiveEndOfDayValue` and `SessionLifetimePolicyTests.ProductionSource_UsesNoClockTimerOrInclusiveBoundaryConstruction` |
| AC-153 | `UtcInstantGuardTests.NonZeroOffsetIsRejectedWithoutConversion`, `ReviewPackageProjectionRejectionTests.Project_NonZeroOffset_FailsClosedWithoutNormalizing`, and `ReviewRecordPurgeServiceTests.NonUtcEvaluationIsRefusedWithoutDeletingAnything` |
| AC-154 | `UtcInstantGuardTests.ZeroOffsetInstantCreatedFromUnspecifiedDateTimeIsAccepted` and `SessionLifetimePolicyTests.CreateBoundaries_AcceptsZeroOffsetFromUnspecifiedDateTime` |
| AC-155 | `ModelShapeTests.RelationalHarnessRoundTripsTheImmutableGraphWithUtcInstants` |
| AC-156 | `RetentionPolicyTests.XmlDocumentation_StatesTheExclusiveProductDefaultSemantics`, `ModelShapeTests.InstantsUseDateTimeOffsetAndRetentionIsDocumentedAsExclusive`, and `MigrationTests.GeneratedSqlServerSchemaCarriesEveryProviderSpecificFact` |
| AC-157 | `MembershipVerificationTests.AnInactiveLocalUserIsRefusedOnTheNextBootstrapRequest` and `MembershipVerificationPolicyTests.Verify_InactiveUser_FailsClosed` |
| AC-158 | `MembershipVerificationPolicyTests.Verify_MissingMembership_FailsClosed` and `TenantIsolationAtTheHttpBoundaryTests.MissingAndMismatchedMembershipsUseTheSameFirmScopedRefusal` |
| AC-159 | `MembershipVerificationPolicyTests.Verify_MembershipForAnotherFirm_FailsClosed` and `TenantIsolationAtTheHttpBoundaryTests.MissingAndMismatchedMembershipsUseTheSameFirmScopedRefusal` |
| AC-160 | `MembershipVerificationTests.AnIdentityStoreFailureUsesTheSameFixedRefusal` |
| AC-161 | `SessionRevocationTests.AnUnavailableStoreDoesNotBreakTheConstantRoutes`, `HealthEndpointTests.HealthReturnsTheFixedLiteralWithTheHealthyStatus`, and `SignInTests.SignInIsAnonymousAndChallengesTheExternalScheme` |
| AC-162 | `ModelShapeTests.SessionEntityDeclaresExactlyTheRequiredFields` |
| AC-163 | `CookieAndTokenTests.TheDecryptedCookieCarriesTheTicketKeyAndNothingElse` and `PortalSecurityTests.TheProductionSourceSerializesNoAuthenticationTicket` |
| AC-164 | `AuthenticationSessionStoreTests.CreateWritesExactlyOneLiveSessionWithServerSideBoundaries` and `SignInTests.ExactlyOneActiveMembershipIssuesTheFirmClaimAndScopesTheSession` |
| AC-165 | `CookieAndTokenTests.NeitherTheCookieNorTheSessionRecordCarriesAnOidcToken` |
| AC-166 | `CookieAndTokenTests.TheMaterializedFirmClaimEqualsTheSessionRecord` |
| AC-167 | `CookieAndTokenTests.TheClaimFollowsTheRecordWhenTheRecordChanges` and `AFirmClaimPresentedInTheCookieIsNeverTheOneTheApplicationSees` |
| AC-168 | `SessionLifetimeTests.AnIdleExpiredSessionIsRefusedAndRebuildsNoPrincipal` |
| AC-169 | `SessionLifetimeTests.AnAbsoluteExpiredSessionIsRefusedEvenWhileItsIdleWindowIsOpen` |
| AC-170 | `SessionLifetimeTests.ActivityNeverMovesTheAbsoluteBoundary` and `SessionLifetimePolicyTests.Touch_AdvancesIdleAndLeavesAbsoluteUnchanged` |
| AC-171 | `SessionLifetimeTests.ARequestPastTheHalfwayPointRenewsTheSessionThroughTheStore` and `SessionLifetimePolicyTests.Touch_ClampsIdleAtTheAbsoluteBoundary` |
| AC-172 | `SessionRevocationTests.SignOutRevokesTheServerSideSessionAndClearsTheCookie` |
| AC-173 | `SessionRevocationTests.ACopiedPreLogoutCookieCannotReuseARevokedSession` and `AuthenticationSessionStoreTests.UnknownSessionReturnsNullAndCreatesNothing` |
| AC-174 | `SessionRevocationTests.ACopiedPreLogoutCookieCannotReuseARevokedSession` |
| AC-175 | `SessionRevocationTests.AnUnknownOrRevokedKeyResolvesNothingAndReCreatesNothing` and `SessionRevocationTests.UnknownSessionCannotBeCreatedByTouchOrRevoke` |
| AC-176 | `SessionRevocationTests.AnUnavailableStoreRefusesTheProtectedRouteAndEstablishesNoPrincipal` and `AnUnavailableStoreRebuildsNoTicket` |
| AC-177 | `SessionRevocationTests.AnUnavailableStoreDoesNotBreakTheConstantRoutes` |
| AC-178 | `StartupRefusalTests.TheProductionSessionStoreIsTheDurableImplementation` |
| AC-179 | `StartupRefusalTests.StartupRefusesANonDurableSessionStoreOutsideDevelopment` |
| AC-180 | `AuthenticationSessionStoreTests.IndependentlyConstructedStoreSeesACommittedSessionImmediately` |
| AC-181 | `SessionRevocationTests.RevocationThroughOneStoreIsImmediatelyEffectiveInAnother` |
| AC-182 | `TenancyContractTests.TheFirmScopeExemptionListIsExactlyFourNamedEntries`, `EveryExemptedOperationActuallyExistsAndOmitsAFirm`, and `TheUserIdRecoveryLookupHasTheExactApprovedContract` |
| AC-183 | `TenantIsolationSchemaTests.TheIdentityCarveOutIsExactlyThreeTables` |
| AC-184 | `PiiSchemaTests.IdentityTablesCarryOnlyTheApprovedStructuralColumns` and `AuthenticationSession_DeclaresNoTicketTokenClaimOrTaxpayerContent` |
| AC-185 | `AuthenticationSessionStoreTests.ExpiredRowsRemainUntilTheExplicitPrivilegedCommandRuns` and `StoreDeclaresNoTimerHostedServiceOrSchedulerSurface` |
| AC-186 | `MembershipVerificationTests.ADisabledMembershipIsRefusedByTheFullFirmScopedStage`, `MembershipVerificationPolicyTests.Verify_DoesNotEvaluateSessionLifetimeOrRevocation`, and `ProductionPolicy_DoesNotReferenceTheSessionLifetimePolicy` |
| AC-187 | `IdentityDirectoryTests.FreshlyMigratedDatabaseReturnsEmptyForEveryIdentityRead` |
| AC-188 | `IdentityDirectoryTests.FreshlyMigratedDatabaseReturnsEmptyForEveryIdentityRead` and `MigrationTests.InitialMigrationContainsNoSeedOrDataMutationOperation` |
| AC-189 | `StartupRefusalTests.SeedingIsDisabledOutsideDevelopment` and `DevelopmentSeederTests.SeederCreatesNoIdentityMembershipSessionOrCredentialRow` |
| AC-190 | `PortalSecurityTests.TheApprovedAuthenticationFilesAreExactlyTheEnumeratedSet`, `ShellPageTests.EveryEndpointIsEitherEnumeratedAnonymousOrAuthorizationRequired`, and `TenantIsolationAtTheHttpBoundaryTests.ProductionReadsNoFirmIdentifierFromAnyRequestChannel` |
| AC-191 | `DevelopmentSeederTests.SeedSourceContainsNoForbiddenPersonalFieldOrIdentityLiteral`, `PortalSecurityTests.ProductionSource_ContainsNoPiiToken`, and the production-source GUID/identity scans in the boundary suites |

## Final gate

The completion marker is written only after these commands pass on the T21
branch:

```text
dotnet build TaxAI.sln
dotnet test TaxAI.sln
pwsh ./scripts/Invoke-SolutionTests.ps1
```

The manifest values are minimum floors, never exact expected counts.
