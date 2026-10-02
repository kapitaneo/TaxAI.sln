using System.Reflection;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using TaxCases.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace ClientIntake.Tests;

/// <summary>
/// Spec 014 T01 — the contract wave's proof. It freezes every public signature
/// the later tasks code against.
/// <para>
/// The exact-set assertions are the point. A test that checked only that a member
/// exists would let T02 quietly widen the surface — add an overload that omits a
/// firm, a generic repository, a content read — and still pass. Each set below is
/// closed, so an addition is a failure that has to be argued for in a diff.
/// </para>
/// </summary>
public sealed class ContractTests
{
    private static readonly Assembly Module = typeof(IIntakeStore).Assembly;

    // ------------------------------------------------- the exact public surface

    [Fact]
    public void TheModuleExportsExactlyTheApprovedTypes()
    {
        Assert.Equal(
            new[]
            {
                "ClientIntake.Contracts.IAccountantIntakeWorkflow",
                "ClientIntake.Contracts.IClientIntakeWorkflow",
                "ClientIntake.Contracts.IIntakeContentStore",
                "ClientIntake.Contracts.IIntakeCredentialSource",
                "ClientIntake.Contracts.IIntakeStore",
                // Spec 016 T04: the consumed source registry and purger ports.
                "ClientIntake.Contracts.ISourceObjectPurger",
                "ClientIntake.Contracts.ISourceRegistryStore",
                "ClientIntake.Models.AccountantIntakeView",
                "ClientIntake.Models.AvailabilityAnswerCode",
                "ClientIntake.Models.ClientIntakeView",
                "ClientIntake.Models.CreateIntakeCaseCommand",
                "ClientIntake.Models.CreatedIntakeCase",
                "ClientIntake.Models.ExpectedDocumentStatus",
                "ClientIntake.Models.ExpectedDocumentView",
                "ClientIntake.Models.IntakeActorKind",
                "ClientIntake.Models.IntakeAnswerInput",
                "ClientIntake.Models.IntakeAnswerVersionRecord",
                "ClientIntake.Models.IntakeAuditEventRecord",
                "ClientIntake.Models.IntakeAuditKind",
                // Spec 017 T03: the firm-scoped case-list read's result shape.
                "ClientIntake.Models.IntakeCaseList",
                "ClientIntake.Models.IntakeCaseListEntry",
                "ClientIntake.Models.IntakeClientScope",
                "ClientIntake.Models.IntakeDocumentRecord",
                "ClientIntake.Models.IntakeExpectedItemRecord",
                "ClientIntake.Models.IntakeLinkInspection",
                "ClientIntake.Models.IntakeLinkRecord",
                "ClientIntake.Models.IntakeLinkScope",
                "ClientIntake.Models.IntakeLinkState",
                "ClientIntake.Models.IntakeMediaKind",
                "ClientIntake.Models.IntakeQuestionView",
                "ClientIntake.Models.IntakeRedemptionProof",
                "ClientIntake.Models.IntakeResultCode",
                "ClientIntake.Models.IntakeResult`1",
                "ClientIntake.Models.IntakeReviewAssociationRecord",
                "ClientIntake.Models.IntakeSecret",
                "ClientIntake.Models.IntakeStoreWriteCode",
                "ClientIntake.Models.IntakeSubmissionAnswerReference",
                "ClientIntake.Models.IntakeSubmissionDocumentReference",
                "ClientIntake.Models.IntakeSubmissionRecord",
                "ClientIntake.Models.IntakeTokenDigest",
                "ClientIntake.Models.IntakeTokenPurpose",
                "ClientIntake.Models.IntakeUploadAttemptRecord",
                "ClientIntake.Models.IntakeUploadCommand",
                "ClientIntake.Models.IntakeUploadOutcome",
                "ClientIntake.Models.IntakeWorkflowOptions",
                "ClientIntake.Models.IssuedIntakeLink",
                "ClientIntake.Models.NewIntakeCase",
                "ClientIntake.Models.NewIntakeSession",
                "ClientIntake.Models.RedeemedIntakeSession",
                "ClientIntake.Models.ReviewHandoffState",
                "ClientIntake.Models.SaveIntakeAnswersCommand",
                "ClientIntake.Models.SourceObjectPurgeOutcome",
                "ClientIntake.Models.SourceRegistration",
                "ClientIntake.Models.SourceRegistryState",
                "ClientIntake.Models.SourceRegistryWriteCode",
                "ClientIntake.Models.StagedIntakeContent",
                "ClientIntake.Models.StoredIntakeCase",
                "ClientIntake.Services.AccountantIntakeWorkflow",
                "ClientIntake.Services.ClientIntakeWorkflow",
            },
            Module.GetExportedTypes()
                .Select(type => type.FullName!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheModuleCreatesNoSecondDocumentCategoryOrReadinessVocabulary()
    {
        // The existing TaxCases enums are reused. A second copy is how two
        // answers about the same taxpayer start to disagree.
        Assert.DoesNotContain(
            Module.GetExportedTypes().Where(type => type.IsEnum).Select(type => type.Name),
            name =>
                name.Contains("DocumentCategory", StringComparison.Ordinal) ||
                name.Contains("Readiness", StringComparison.Ordinal) &&
                    !name.Equals("ReviewHandoffState", StringComparison.Ordinal) ||
                name.Contains("CaseStatus", StringComparison.Ordinal));

        Assert.Equal("TaxCases.Models", typeof(DocumentCategory).Namespace);
        Assert.Equal("TaxCases.Models", typeof(ReadinessState).Namespace);
        Assert.Equal("TaxCases.Models", typeof(CaseStatus).Namespace);
    }

    // ------------------------------------------------------------ closed enums

    [Fact]
    public void EveryClosedEnumHasExactlyItsApprovedMembersAndValues()
    {
        AssertEnum<AvailabilityAnswerCode>(("AvailableNow", 0), ("WillProvideLater", 1), ("Unavailable", 2));

        AssertEnum<ExpectedDocumentStatus>(
            ("Requested", 0), ("Uploaded", 1), ("Rejected", 2), ("Missing", 3), ("Ready", 4));

        AssertEnum<IntakeMediaKind>(("Pdf", 0), ("Jpeg", 1), ("Png", 2));

        AssertEnum<IntakeUploadOutcome>(("Accepted", 0), ("Rejected", 1), ("Duplicate", 2));

        AssertEnum<IntakeLinkState>(("Active", 0), ("Expired", 1), ("Revoked", 2), ("Spent", 3));

        AssertEnum<IntakeActorKind>(("Accountant", 0), ("Client", 1));

        AssertEnum<IntakeAuditKind>(
            ("CaseCreated", 0), ("ChecklistConfirmed", 1), ("LinkIssued", 2), ("LinkRedeemed", 3),
            ("LinkRevoked", 4), ("LinkReissued", 5), ("AnswerSaved", 6), ("UploadAccepted", 7),
            ("UploadRejected", 8), ("DuplicateRejected", 9), ("SubmissionCreated", 10),
            ("ReadinessChanged", 11), ("ReviewAssociated", 12));

        AssertEnum<IntakeResultCode>(
            ("Success", 0), ("Invalid", 1), ("NotFound", 2), ("Conflict", 3), ("Duplicate", 4));

        AssertEnum<IntakeStoreWriteCode>(
            ("Success", 0), ("NotFound", 1), ("Conflict", 2), ("Duplicate", 3));

        AssertEnum<ReviewHandoffState>(
            ("NotReady", 0), ("AwaitingReviewRecord", 1), ("Available", 2), ("Unavailable", 3));

        AssertEnum<IntakeTokenPurpose>(("BootstrapLink", 0), ("ClientSession", 1));
    }

    // -------------------------------------------- exact record property vocabularies

    [Fact]
    public void EveryRecordHasExactlyItsApprovedPropertySet()
    {
        AssertProperties<CreateIntakeCaseCommand>(
            "ExpectedMedicalExpenseCount", "ExpectedT4Count", "PrimaryTaxpayerDisplayName", "TaxYear");
        AssertProperties<IntakeAnswerInput>("Answer", "Category");
        AssertProperties<SaveIntakeAnswersCommand>("Answers");
        AssertProperties<IntakeUploadCommand>(
            "Category", "DeclaredLength", "DeclaredMediaKind", "Ordinal");
        AssertProperties<IntakeWorkflowOptions>(
            "ClientSessionIdleLifetime", "LinkLifetime", "MaximumFileBytes", "MaximumFilesPerRequest");
        AssertProperties<IntakeTokenDigest>("Bytes");

        AssertProperties<CreatedIntakeCase>("CaseId", "LinkCredential", "LinkExpiresAtUtc");
        AssertProperties<IssuedIntakeLink>("LinkCredential", "LinkExpiresAtUtc");
        AssertProperties<IntakeRedemptionProof>("LinkDigest", "LinkExpiresAtUtc");
        AssertProperties<IntakeLinkInspection>("Proof", "TaxYear");
        AssertProperties<RedeemedIntakeSession>("AbsoluteExpiresAtUtc", "SessionCredential");
        AssertProperties<IntakeQuestionView>("Category", "CurrentAnswer", "Options", "Prompt");
        AssertProperties<ExpectedDocumentView>("Category", "Ordinal", "Status");
        AssertProperties<ClientIntakeView>(
            "ExpectedDocuments", "LatestSubmissionVersion", "Questions", "TaxYear", "UnsubmittedChanges");
        AssertProperties<AccountantIntakeView>(
            "AnsweredQuestionCount", "CaseStatus", "ExpectedDocuments", "ExpectedMedicalExpenseCount",
            "ExpectedQuestionCount", "ExpectedT4Count", "HandoffState", "LatestSubmissionVersion",
            "LinkExpiresAtUtc", "LinkIssuedAtUtc", "LinkRevokedAtUtc", "LinkSpentAtUtc", "LinkState",
            "PrimaryTaxpayerDisplayName", "Readiness", "ReviewRecordId", "TaxYear", "UnsubmittedChanges");

        AssertProperties<IntakeExpectedItemRecord>("Category", "ExpectedItemId", "Ordinal");
        AssertProperties<IntakeLinkRecord>(
            "CredentialDigest", "ExpiresAtUtc", "IssuedAtUtc", "LinkId", "RevokedAtUtc", "SpentAtUtc");
        AssertProperties<IntakeAnswerVersionRecord>(
            "ActorKind", "ActorReference", "Answer", "Category", "RecordedAtUtc", "Version");
        AssertProperties<IntakeDocumentRecord>(
            "ByteCount", "Category", "ContentKey", "DocumentId", "ExpectedItemId", "Fingerprint",
            "MediaKind", "UploadedAtUtc", "Version");
        AssertProperties<IntakeUploadAttemptRecord>(
            "AttemptId", "AttemptedAtUtc", "ExpectedItemId", "Outcome");
        AssertProperties<IntakeSubmissionAnswerReference>("AnswerVersion", "Category");
        AssertProperties<IntakeSubmissionDocumentReference>("DocumentId");
        AssertProperties<IntakeSubmissionRecord>(
            "ActorKind", "ActorReference", "Answers", "Documents", "SubmittedAtUtc", "Version");
        AssertProperties<IntakeReviewAssociationRecord>("AssociatedAtUtc", "RecordId");
        AssertProperties<IntakeAuditEventRecord>(
            "ActorKind", "ActorReference", "EventId", "ExpectedItemId", "Kind", "OccurredAtUtc", "Sequence");
        AssertProperties<StoredIntakeCase>(
            "AnswerVersions", "CaseId", "CaseStatus", "ConcurrencyVersion", "CurrentLink", "Documents",
            "ExpectedItems", "Firm", "PrimaryParticipantId", "PrimaryTaxpayerDisplayName",
            "ReviewAssociation", "Submissions", "TaxYear", "UploadAttempts");
        AssertProperties<NewIntakeCase>("AuditEvents", "Case");
        AssertProperties<IntakeLinkScope>("CaseId", "ExpiresAtUtc", "Firm", "LinkDigest");
        AssertProperties<IntakeClientScope>(
            "AbsoluteExpiresAtUtc", "CaseId", "EvaluationAtUtc", "Firm", "IdleExpiresAtUtc",
            "SessionDigest", "SessionId");
        AssertProperties<NewIntakeSession>(
            "AbsoluteExpiresAtUtc", "CreatedAtUtc", "IdleExpiresAtUtc", "SessionDigest", "SessionId");
        AssertProperties<StagedIntakeContent>("ByteCount", "ContentKey", "Fingerprint");
    }

    [Fact]
    public void NoPublicRecordExposesAMutableCollectionOrArray()
    {
        // An `init` accessor reports CanWrite == true, so a naive writability check
        // would flag every positional record property. Only a genuine settable
        // property — one whose setter carries no IsExternalInit modifier — is a
        // mutation a caller could perform after validation.
        var offending = Module.GetExportedTypes()
            .Where(type => type.Namespace == "ClientIntake.Models")
            .SelectMany(type => type.GetProperties())
            .Where(property =>
                property.PropertyType.IsArray ||
                (property.PropertyType.IsGenericType &&
                 property.PropertyType.GetGenericTypeDefinition() == typeof(List<>)) ||
                IsGenuinelySettable(property))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offending);
    }

    private static bool IsGenuinelySettable(PropertyInfo property) =>
        property.SetMethod is { } setter &&
        !setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName ==
                "System.Runtime.CompilerServices.IsExternalInit");

    // --------------------------------------------------- exact port signatures

    [Fact]
    public void TheAccountantWorkflowPortIsExactlyTheApprovedFirmScopedSurface()
    {
        AssertMembers<IAccountantIntakeWorkflow>(
            "AssociateReviewAsync", "CreateAsync", "GetProgressAsync", "ReissueLinkAsync", "RevokeLinkAsync");

        // Every member leads with a trusted firm, and none has an overload or an
        // optional argument that could omit it.
        foreach (var method in typeof(IAccountantIntakeWorkflow).GetMethods())
        {
            Assert.Equal(typeof(TrustedFirmId), method.GetParameters()[0].ParameterType);
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.IsOptional);
            Assert.Equal(
                typeof(CancellationToken),
                method.GetParameters()[^1].ParameterType);
        }
    }

    [Fact]
    public void TheClientWorkflowPortCarriesNoIdentifierAtAll()
    {
        AssertMembers<IClientIntakeWorkflow>(
            "GetAsync", "InspectLinkAsync", "RedeemLinkAsync", "SaveAnswersAsync", "SubmitAsync", "UploadAsync");

        // Scope is derived from a digested credential and returned by the store.
        // A Guid or a TrustedFirmId here would be a scope the client supplies.
        foreach (var method in typeof(IClientIntakeWorkflow).GetMethods())
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter =>
                    parameter.ParameterType == typeof(Guid) ||
                    parameter.ParameterType == typeof(Guid?) ||
                    parameter.ParameterType == typeof(TrustedFirmId));

            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.IsOptional);
        }
    }

    [Fact]
    public void TheStorePortHasExactlyTwoFirmOmittingDigestLookups()
    {
        AssertMembers<IIntakeStore>(
            "AppendAnswersAsync", "AppendSubmissionAsync", "AppendUploadAttemptAsync",
            "AssociateReviewAsync", "CreateAsync", "FindAsync", "FindClientScopeAsync",
            "FindForClientAsync", "FindLinkScopeAsync", "ListCasesAsync", "RedeemLinkAsync",
            "ReissueLinkAsync", "RevokeLinkAsync");

        // The exemption is exactly two members, and each is a digest lookup that
        // RETURNS a trusted scope rather than accepting one.
        var firmOmitting = typeof(IIntakeStore)
            .GetMethods()
            .Where(method => !method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(TrustedFirmId) ||
                parameter.ParameterType == typeof(IntakeClientScope) ||
                parameter.ParameterType == typeof(IntakeLinkScope) ||
                parameter.ParameterType == typeof(NewIntakeCase)))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "FindClientScopeAsync", "FindLinkScopeAsync" }, firmOmitting);

        foreach (var name in firmOmitting)
        {
            var method = typeof(IIntakeStore).GetMethod(name)!;
            Assert.Equal(typeof(IntakeTokenDigest), method.GetParameters()[0].ParameterType);
        }
    }

    [Fact] // spec 017 AC-68
    public void TheCaseListReadRequiresATrustedFirmFirstWithNoOmittingOverload()
    {
        // One member by that name: an overload is how a firm-omitting variant
        // arrives looking like a convenience.
        var method = Assert.Single(
            typeof(IIntakeStore).GetMethods(),
            candidate => candidate.Name == "ListCasesAsync");

        Assert.Equal(
            new[] { typeof(TrustedFirmId), typeof(int), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.DoesNotContain(
            method.GetParameters(),
            parameter => parameter.IsOptional || parameter.HasDefaultValue);
        Assert.Equal(typeof(Task<IntakeCaseList>), method.ReturnType);

        // The result is exactly the minimum needed to select and open a case.
        // No instant, status, count, or other identifier travels with it.
        AssertProperties<IntakeCaseListEntry>(
            "CaseId", "PrimaryTaxpayerDisplayName", "TaxYear");
        AssertProperties<IntakeCaseList>("Cases", "HasMore");
    }

    [Fact]
    public void TheCaseListIsAReadOnlySnapshotWithStructuralDiagnostics()
    {
        var source = new List<IntakeCaseListEntry>
        {
            new(Guid.NewGuid(), "Synthetic Taxpayer", 2025),
        };
        var list = new IntakeCaseList(source, HasMore: true);
        source.Clear();

        var entry = Assert.Single(list.Cases);
        Assert.Throws<ArgumentNullException>(() => new IntakeCaseList(null!, false));

        // Neither diagnostic carries the selector or the taxpayer text.
        Assert.Equal("IntakeCaseListEntry", entry.ToString());
        Assert.Equal("IntakeCaseList(cases: 1, more: True)", list.ToString());
    }

    [Fact]
    public void TheStorePortHasNoGenericRepositoryQueryableOrHistoryMutation()
    {
        var members = typeof(IIntakeStore).GetMethods();

        Assert.DoesNotContain(members, method => method.IsGenericMethod);

        foreach (var method in members)
        {
            foreach (var forbidden in new[] { "Update", "Delete", "Remove", "Set", "Query", "Save" })
            {
                Assert.DoesNotContain(forbidden, method.Name, StringComparison.Ordinal);
            }

            // No IQueryable in or out: an exposed queryable hands the caller the
            // ability to write its own unscoped query.
            Assert.DoesNotContain("IQueryable", method.ReturnType.Name, StringComparison.Ordinal);
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => parameter.ParameterType.Name.Contains("IQueryable", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheContentPortStagesAndCleansUpButNeverReads()
    {
        AssertMembers<IIntakeContentStore>("DeleteIfExistsAsync", "StageAsync");

        // No read member of any shape. There is no way to get opaque source bytes
        // back out of this contract, which is what keeps SIN-bearing content out
        // of every query, diagnostic, and URL.
        Assert.DoesNotContain(
            typeof(IIntakeContentStore).GetMethods(),
            method =>
                method.ReturnType == typeof(Task<Stream>) ||
                method.ReturnType == typeof(Stream) ||
                method.ReturnType == typeof(Task<byte[]>) ||
                method.Name.Contains("Read", StringComparison.Ordinal) ||
                method.Name.Contains("Open", StringComparison.Ordinal) ||
                method.Name.Contains("Get", StringComparison.Ordinal) ||
                method.Name.Contains("Download", StringComparison.Ordinal));

        var stage = typeof(IIntakeContentStore).GetMethod(nameof(IIntakeContentStore.StageAsync))!;
        Assert.Equal(typeof(TrustedFirmId), stage.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(Stream), stage.GetParameters()[2].ParameterType);
        Assert.Equal(typeof(long), stage.GetParameters()[3].ParameterType);
        Assert.Equal(typeof(Task<StagedIntakeContent>), stage.ReturnType);
    }

    [Fact]
    public void TheSecretSourceNeverStoresOrReturnsARawCredentialFromStorage()
    {
        AssertMembers<IIntakeCredentialSource>("CreateCredential", "CreateIdentifier", "Digest");

        var digest = typeof(IIntakeCredentialSource).GetMethod(nameof(IIntakeCredentialSource.Digest))!;
        Assert.Equal(typeof(IntakeTokenDigest), digest.ReturnType);
        Assert.Equal(
            new[] { typeof(IntakeSecret), typeof(IntakeTokenPurpose) },
            digest.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        // Digesting is one-way: nothing verifies or recovers a raw credential.
        Assert.DoesNotContain(
            typeof(IIntakeCredentialSource).GetMethods(),
            method =>
                method.Name.Contains("Verify", StringComparison.Ordinal) ||
                method.Name.Contains("Validate", StringComparison.Ordinal) ||
                method.Name.Contains("Store", StringComparison.Ordinal));
    }

    // --------------------------------- the workflow stubs are frozen, not built

    [Fact]
    public void BothWorkflowsImplementTheirPortExactlyAndAddNoPublicMember()
    {
        Assert.Equal(
            new[] { typeof(IAccountantIntakeWorkflow) },
            typeof(AccountantIntakeWorkflow).GetInterfaces());
        Assert.Equal(
            new[] { typeof(IClientIntakeWorkflow) },
            typeof(ClientIntakeWorkflow).GetInterfaces());

        Assert.True(typeof(AccountantIntakeWorkflow).IsSealed);
        Assert.True(typeof(ClientIntakeWorkflow).IsSealed);

        AssertMembers<AccountantIntakeWorkflow>(
            "AssociateReviewAsync", "CreateAsync", "GetProgressAsync", "ReissueLinkAsync", "RevokeLinkAsync");
        AssertMembers<ClientIntakeWorkflow>(
            "GetAsync", "InspectLinkAsync", "RedeemLinkAsync", "SaveAnswersAsync", "SubmitAsync", "UploadAsync");
    }

    [Fact]
    public void TheAccountantWorkflowConstructorTakesExactlyTheApprovedDependencies()
    {
        var constructor = Assert.Single(typeof(AccountantIntakeWorkflow).GetConstructors());

        Assert.Equal(
            new[]
            {
                typeof(IIntakeStore),
                typeof(IIntakeCredentialSource),
                typeof(IReviewRecordRepository),
                typeof(IntakeWorkflowOptions),
            },
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Fact]
    public void TheClientWorkflowConstructorTakesExactlyTheApprovedDependencies()
    {
        var constructor = Assert.Single(typeof(ClientIntakeWorkflow).GetConstructors());

        Assert.Equal(
            new[]
            {
                typeof(IIntakeStore),
                typeof(IIntakeContentStore),
                typeof(IIntakeCredentialSource),
                typeof(IntakeWorkflowOptions),
            },
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    [Fact]
    public void NeitherWorkflowTakesAClockEnvironmentUserOrHttpDependency()
    {
        foreach (var workflow in new[] { typeof(AccountantIntakeWorkflow), typeof(ClientIntakeWorkflow) })
        {
            foreach (var parameter in workflow.GetConstructors().Single().GetParameters())
            {
                foreach (var forbidden in new[]
                         {
                             "Clock", "TimeProvider", "Environment", "HttpContext", "HttpRequest",
                             "Principal", "Identity", "Configuration", "Logger", "DbContext",
                         })
                {
                    Assert.DoesNotContain(
                        forbidden,
                        parameter.ParameterType.Name,
                        StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void WorkflowConstructorsRefuseMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new AccountantIntakeWorkflow(
            store: null!, credentialSource: null!, reviewRecords: null!, options: null!));

        Assert.Throws<ArgumentNullException>(() => new ClientIntakeWorkflow(
            store: null!, contentStore: null!, credentialSource: null!, options: null!));
    }

    // ------------------------------------------------------- token primitives

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 32 zero bytes
    public void TheSecretAcceptsTheCanonicalEncodingOfExactlyThirtyTwoBytes(string candidate)
    {
        Assert.True(IntakeSecret.TryParse(candidate, out var value));
        Assert.NotNull(value);
        Assert.Equal(candidate, value!.Reveal());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAA")]                                          // too short
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]  // 44 chars
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]   // padded
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]   // base64, not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]   // non-canonical trailing bits
    [InlineData(" AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // leading space
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]   // trailing space
    [InlineData("AAAAAAAAAAAAAAAAAAAAA\nAAAAAAAAAAAAAAAAAAAAA")]  // embedded newline
    [InlineData("AAAAAAAAAAAAAAAAAAAA!AAAAAAAAAAAAAAAAAAAAAA")]   // outside the alphabet
    public void TheSecretRefusesAnythingNotAlreadyCanonical(string? candidate)
    {
        // Nothing is trimmed, padded, case-folded, or normalized. Two spellings of
        // one credential would mean two digests for one secret; and the framework
        // base64 decoder silently skips whitespace, which is why whitespace is
        // rejected before decoding rather than after.
        Assert.False(IntakeSecret.TryParse(candidate, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TheSecretNeverRendersItsCredential()
    {
        Assert.True(IntakeSecret.TryParse(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            out var value));

        Assert.Equal("IntakeSecret", value!.ToString());
        Assert.DoesNotContain("AAAA", value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDigestRequiresExactlyThirtyTwoBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntakeTokenDigest(new byte[31]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntakeTokenDigest(new byte[33]));
        Assert.Throws<ArgumentNullException>(() => new IntakeTokenDigest(null!));

        Assert.Equal(32, new IntakeTokenDigest(new byte[32]).Bytes.Count);
    }

    [Fact]
    public void TheDigestCopiesItsInputAndExposesNoMutableArray()
    {
        var source = new byte[32];
        source[0] = 1;

        var digest = new IntakeTokenDigest(source);

        // Mutating the caller's array must not change a digest that was already
        // stored or compared.
        source[0] = 99;

        Assert.Equal(1, digest.Bytes[0]);
        Assert.IsNotType<byte[]>(digest.Bytes);
    }

    [Fact]
    public void TheDigestComparesInConstantTimeAndByValue()
    {
        var first = new byte[32];
        first[31] = 7;
        var second = new byte[32];
        second[31] = 7;
        var different = new byte[32];
        different[31] = 8;

        Assert.Equal(new IntakeTokenDigest(first), new IntakeTokenDigest(second));
        Assert.Equal(
            new IntakeTokenDigest(first).GetHashCode(),
            new IntakeTokenDigest(second).GetHashCode());
        Assert.NotEqual(new IntakeTokenDigest(first), new IntakeTokenDigest(different));
        Assert.False(new IntakeTokenDigest(first).Equals(null));
    }

    [Fact]
    public void TheDigestNeverRendersItsBytes()
    {
        var bytes = new byte[32];
        bytes[0] = 0xAB;

        Assert.Equal("IntakeTokenDigest", new IntakeTokenDigest(bytes).ToString());
    }

    // ------------------------------------------------------ the result wrapper

    [Fact]
    public void TheResultCarriesACodeAndOnSuccessAValue()
    {
        var success = IntakeResult<int>.Success(4);

        Assert.True(success.IsSuccess);
        Assert.Equal(IntakeResultCode.Success, success.Code);
        Assert.Equal(4, success.Value);

        var failure = IntakeResult<int>.Failure(IntakeResultCode.NotFound);

        Assert.False(failure.IsSuccess);
        Assert.Equal(IntakeResultCode.NotFound, failure.Code);
        Assert.Equal(0, failure.Value);
    }

    [Fact]
    public void TheResultRefusesASuccessCodedFailureAndANullSuccess()
    {
        // A "failure" carrying the success code and no value would be a null
        // success at every call site downstream.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IntakeResult<int>.Failure(IntakeResultCode.Success));
        Assert.Throws<ArgumentNullException>(
            () => IntakeResult<string>.Success(null!));
    }

    [Fact]
    public void TheResultRendersOnlyItsCode()
    {
        // `nameof` on a generic yields the unmangled name, so the rendered form is
        // "IntakeResult", not "IntakeResult`1".
        Assert.Equal(
            "IntakeResult(NotFound)",
            IntakeResult<string>.Failure(IntakeResultCode.NotFound).ToString());

        Assert.DoesNotContain(
            "taxpayer-marker",
            IntakeResult<string>.Success("taxpayer-marker").ToString(),
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static void AssertEnum<TEnum>(params (string Name, int Value)[] expected)
        where TEnum : struct, Enum
    {
        Assert.Equal(
            expected.Select(member => member.Name).ToArray(),
            Enum.GetNames<TEnum>());

        foreach (var (name, value) in expected)
        {
            Assert.Equal(value, Convert.ToInt32(Enum.Parse<TEnum>(name)));
        }
    }

    private static void AssertProperties<T>(params string[] expected) =>
        Assert.Equal(
            expected,
            typeof(T).GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

    private static void AssertMembers<T>(params string[] expected) =>
        Assert.Equal(
            expected,
            typeof(T).GetMethods(Declared)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Where(name => name is not (
                    "ToString" or "Equals" or "GetHashCode" or "Deconstruct"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

}
