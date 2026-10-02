using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClientIntake.Models;
using TaxCases.Models;
using Tenancy.Models;

namespace ClientIntake.Tests.Models;

/// <summary>
/// Spec 014 T01 — the PII boundary as a test rather than a promise.
/// <para>
/// There is no JSON API in spec 014, which is exactly why these assertions exist.
/// The omissions are defence in depth against a future one and against a
/// diagnostic sink that serializes whatever it is handed. A `[JsonIgnore]` that
/// nobody checks is a `[JsonIgnore]` that can be silently dropped — and one
/// already was: a `[property:]` target on a positional parameter whose property
/// is hand-declared is discarded by the compiler (CS0657) while looking exactly
/// like a working omission.
/// </para>
/// </summary>
public sealed class ContractOmissionTests
{
    /// <summary>
    /// The exact property that must never be serialized, by declaring type. Every
    /// identifier, secret, digest, taxpayer text, content key, and fingerprint.
    /// </summary>
    public static TheoryData<string, string> OmittedProperties() => new()
    {
        { nameof(CreateIntakeCaseCommand), "PrimaryTaxpayerDisplayName" },
        { nameof(CreatedIntakeCase), "CaseId" },
        { nameof(CreatedIntakeCase), "LinkCredential" },
        { nameof(IssuedIntakeLink), "LinkCredential" },
        { nameof(IntakeRedemptionProof), "LinkDigest" },
        { nameof(IntakeLinkInspection), "Proof" },
        { nameof(RedeemedIntakeSession), "SessionCredential" },
        { nameof(IntakeQuestionView), "Prompt" },
        { nameof(AccountantIntakeView), "PrimaryTaxpayerDisplayName" },
        { nameof(AccountantIntakeView), "ReviewRecordId" },
        { nameof(IntakeTokenDigest), "Bytes" },
        { nameof(IntakeExpectedItemRecord), "ExpectedItemId" },
        { nameof(IntakeLinkRecord), "LinkId" },
        { nameof(IntakeLinkRecord), "CredentialDigest" },
        { nameof(IntakeAnswerVersionRecord), "ActorReference" },
        { nameof(IntakeDocumentRecord), "DocumentId" },
        { nameof(IntakeDocumentRecord), "ExpectedItemId" },
        { nameof(IntakeDocumentRecord), "ContentKey" },
        { nameof(IntakeDocumentRecord), "Fingerprint" },
        { nameof(IntakeUploadAttemptRecord), "AttemptId" },
        { nameof(IntakeUploadAttemptRecord), "ExpectedItemId" },
        { nameof(IntakeSubmissionDocumentReference), "DocumentId" },
        { nameof(IntakeSubmissionRecord), "ActorReference" },
        { nameof(IntakeReviewAssociationRecord), "RecordId" },
        { nameof(IntakeAuditEventRecord), "EventId" },
        { nameof(IntakeAuditEventRecord), "ActorReference" },
        { nameof(IntakeAuditEventRecord), "ExpectedItemId" },
        { nameof(StoredIntakeCase), "Firm" },
        { nameof(StoredIntakeCase), "CaseId" },
        { nameof(StoredIntakeCase), "PrimaryParticipantId" },
        { nameof(StoredIntakeCase), "PrimaryTaxpayerDisplayName" },
        { nameof(IntakeLinkScope), "Firm" },
        { nameof(IntakeLinkScope), "CaseId" },
        { nameof(IntakeLinkScope), "LinkDigest" },
        { nameof(IntakeClientScope), "Firm" },
        { nameof(IntakeClientScope), "CaseId" },
        { nameof(IntakeClientScope), "SessionId" },
        { nameof(IntakeClientScope), "SessionDigest" },
        { nameof(NewIntakeSession), "SessionId" },
        { nameof(NewIntakeSession), "SessionDigest" },
        { nameof(StagedIntakeContent), "ContentKey" },
        { nameof(StagedIntakeContent), "Fingerprint" },
        // Spec 016 T04: the source registry's scope, opaque resource and key.
        { nameof(SourceRegistration), "Firm" },
        { nameof(SourceRegistration), "CaseId" },
        { nameof(SourceRegistration), "ResourceId" },
        { nameof(SourceRegistration), "ContentKey" },
        { nameof(SourceRegistration), "DocumentId" },
        // Spec 017 T03 (human-authorized 2026-10-01): the case-list entry's
        // selector and taxpayer display name.
        { nameof(IntakeCaseListEntry), "CaseId" },
        { nameof(IntakeCaseListEntry), "PrimaryTaxpayerDisplayName" },
    };

    [Theory]
    [MemberData(nameof(OmittedProperties))]
    public void TheDeclaredPropertyCarriesAnEffectiveJsonIgnore(string typeName, string propertyName)
    {
        var type = typeof(IntakeTokenDigest).Assembly
            .GetType($"ClientIntake.Models.{typeName}", throwOnError: true)!;

        var property = type.GetProperty(propertyName);
        Assert.NotNull(property);

        // GetCustomAttribute reads the attribute as the compiler actually emitted
        // it. A discarded `[property:]` target fails here, which is the whole
        // point: the attribute has to be where it takes effect, not where it reads
        // nicely in the source.
        Assert.NotNull(property!.GetCustomAttribute<JsonIgnoreAttribute>());
    }

    [Fact]
    public void TheOmissionListCoversEveryIdentifierSecretDigestAndTaxpayerTextProperty()
    {
        // Without this, adding a property would exempt it from the rule above
        // instead of failing. Every property whose name or type says "sensitive"
        // must appear in the table.
        var declared = OmittedProperties()
            .Select(row => $"{row[0]}.{row[1]}")
            .ToHashSet(StringComparer.Ordinal);

        var sensitive = typeof(IntakeTokenDigest).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "ClientIntake.Models" && !type.IsEnum)
            .SelectMany(type => type.GetProperties().Select(property => (type, property)))
            .Where(entry => IsSensitive(entry.property))
            .Select(entry => $"{entry.type.Name}.{entry.property.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(sensitive);

        var uncovered = sensitive.Where(name => !declared.Contains(name)).ToArray();

        Assert.True(
            uncovered.Length == 0,
            $"Sensitive properties missing from the omission table: {string.Join(", ", uncovered)}");
    }

    [Fact]
    public void NoSerializedPayloadContainsAnIdentifierSecretDigestOrTaxpayerText()
    {
        // The end-to-end version of the rule: build one populated instance of every
        // record that can carry a sensitive value and prove the serializer emits
        // none of them.
        const string DisplayNameMarker = "taxpayer-display-marker";
        const string ContentKeyMarker = "content-key-marker";

        var firm = TrustedFirmId.FromGuid(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"));
        var caseId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");
        var itemId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3");
        var digest = new IntakeTokenDigest(Enumerable.Repeat((byte)0x5A, 32).ToArray());
        var fingerprint = Enumerable.Repeat((byte)0x6B, 32).ToArray();
        var instant = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.True(IntakeSecret.TryParse(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            out var credential));

        var link = new IntakeLinkRecord(itemId, digest, instant, instant, null, null);

        object[] payloads =
        [
            new CreateIntakeCaseCommand(DisplayNameMarker, 2025, 1, 1),
            new CreatedIntakeCase(caseId, credential!, instant),
            new IssuedIntakeLink(credential!, instant),
            new IntakeRedemptionProof(digest, instant),
            new IntakeLinkInspection(2025, new IntakeRedemptionProof(digest, instant)),
            new RedeemedIntakeSession(credential!, instant),
            new IntakeQuestionView(
                DocumentCategory.T4Slip,
                DisplayNameMarker,
                [AvailabilityAnswerCode.AvailableNow],
                null),
            digest,
            link,
            new IntakeExpectedItemRecord(itemId, DocumentCategory.T4Slip, 1),
            new IntakeAnswerVersionRecord(
                DocumentCategory.T4Slip, 1, AvailabilityAnswerCode.AvailableNow,
                IntakeActorKind.Client, caseId, instant),
            new IntakeDocumentRecord(
                itemId, itemId, DocumentCategory.T4Slip, 1, ContentKeyMarker,
                IntakeMediaKind.Pdf, 10, fingerprint, instant),
            new IntakeUploadAttemptRecord(itemId, itemId, IntakeUploadOutcome.Accepted, instant),
            new IntakeSubmissionDocumentReference(itemId),
            new IntakeSubmissionRecord(
                1, [], [], IntakeActorKind.Client, caseId, instant),
            new IntakeReviewAssociationRecord(caseId, instant),
            new IntakeAuditEventRecord(
                itemId, 1, IntakeAuditKind.CaseCreated, IntakeActorKind.Accountant,
                caseId, itemId, instant),
            new StoredIntakeCase(
                firm, caseId, itemId, DisplayNameMarker, 2025, CaseStatus.InformationGathering,
                1, [], link, [], [], [], [], null),
            new IntakeLinkScope(firm, caseId, digest, instant),
            new IntakeClientScope(firm, caseId, itemId, digest, instant, instant, instant),
            new NewIntakeSession(itemId, digest, instant, instant, instant),
            new StagedIntakeContent(ContentKeyMarker, 10, fingerprint),
            new SourceRegistration(
                firm, caseId, itemId, ContentKeyMarker, SourceRegistryState.Bound,
                instant, itemId, instant, null),
            new AccountantIntakeView(
                DisplayNameMarker, 2025, CaseStatus.InformationGathering, ReadinessState.NotReady,
                1, 1, [], 0, 2, null, false, IntakeLinkState.Active, instant, instant, null, null,
                ReviewHandoffState.NotReady, caseId),
        ];

        foreach (var payload in payloads)
        {
            var json = JsonSerializer.Serialize(payload, payload.GetType());

            foreach (var prohibited in new[]
                     {
                         DisplayNameMarker,
                         ContentKeyMarker,
                         credential!.Reveal(),
                         firm.Value.ToString("D"),
                         caseId.ToString("D"),
                         itemId.ToString("D"),
                         "5A5A",
                         "6B6B",
                         "90,90",
                         "107,107",
                     })
            {
                Assert.DoesNotContain(
                    prohibited,
                    json,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------------------------------------------------------- safe rendering

    [Fact]
    public void EveryPublicModelDeclaresItsOwnStructuralToString()
    {
        // A record that inherits the compiler-generated ToString prints every
        // property, including the ones the serializer is careful to omit.
        var missing = typeof(IntakeTokenDigest).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "ClientIntake.Models" && !type.IsEnum)
            .Where(type => type.GetMethod(
                nameof(ToString),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                Type.EmptyTypes) is null)
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void NoToStringEmitsACallerStringIdentifierSecretOrDigest()
    {
        const string Marker = "taxpayer-display-marker";
        const string ContentKey = "content-key-marker";

        var firm = TrustedFirmId.FromGuid(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"));
        var caseId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");
        var digest = new IntakeTokenDigest(Enumerable.Repeat((byte)0x5A, 32).ToArray());
        var instant = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.True(IntakeSecret.TryParse(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            out var credential));

        var link = new IntakeLinkRecord(caseId, digest, instant, instant, null, null);

        object[] instances =
        [
            new CreateIntakeCaseCommand(Marker, 2025, 1, 1),
            new CreatedIntakeCase(caseId, credential!, instant),
            new IntakeQuestionView(DocumentCategory.T4Slip, Marker, [], null),
            new StoredIntakeCase(
                firm, caseId, caseId, Marker, 2025, CaseStatus.Intake, 1, [], link, [], [], [], [], null),
            new StagedIntakeContent(ContentKey, 10, new byte[32]),
            new AccountantIntakeView(
                Marker, 2025, CaseStatus.Intake, ReadinessState.NotReady, 1, 1, [], 0, 2, null,
                false, IntakeLinkState.Active, instant, instant, null, null,
                ReviewHandoffState.NotReady, caseId),
            digest,
            credential!,
            IntakeResult<string>.Success(Marker),
        ];

        foreach (var instance in instances)
        {
            var rendered = instance.ToString()!;

            foreach (var prohibited in new[]
                     {
                         Marker,
                         ContentKey,
                         credential!.Reveal(),
                         firm.Value.ToString("D"),
                         caseId.ToString("D"),
                         "5A",
                         "2030",
                     })
            {
                Assert.DoesNotContain(prohibited, rendered, StringComparison.Ordinal);
            }

            // Structural, not empty: the type identity is always there.
            Assert.StartsWith(
                instance.GetType().Name.Split('`')[0],
                rendered,
                StringComparison.Ordinal);
        }
    }

    // ------------------------------------------- collections are snapshots

    [Fact]
    public void EveryCollectionConstructorCopiesIntoAReadOnlySnapshot()
    {
        var mutableAnswers = new List<IntakeAnswerInput>
        {
            new(DocumentCategory.T4Slip, AvailabilityAnswerCode.AvailableNow),
        };
        var command = new SaveIntakeAnswersCommand(mutableAnswers);
        mutableAnswers.Clear();
        Assert.Single(command.Answers!);

        var mutableOptions = new List<AvailabilityAnswerCode> { AvailabilityAnswerCode.Unavailable };
        var question = new IntakeQuestionView(DocumentCategory.T4Slip, "prompt", mutableOptions, null);
        mutableOptions.Clear();
        Assert.Single(question.Options);

        var mutableDocuments = new List<ExpectedDocumentView>
        {
            new(DocumentCategory.T4Slip, 1, ExpectedDocumentStatus.Requested),
        };
        var view = new ClientIntakeView(2025, [], mutableDocuments, null, false);
        mutableDocuments.Clear();
        Assert.Single(view.ExpectedDocuments);

        var mutableFingerprint = new byte[32];
        mutableFingerprint[0] = 3;
        var staged = new StagedIntakeContent("key", 1, mutableFingerprint);
        mutableFingerprint[0] = 9;
        Assert.Equal(3, staged.Fingerprint[0]);
    }

    [Fact]
    public void ANullCollectionIsRefusedRatherThanStored()
    {
        Assert.Throws<ArgumentNullException>(
            () => new IntakeQuestionView(DocumentCategory.T4Slip, "prompt", null!, null));
        Assert.Throws<ArgumentNullException>(
            () => new ClientIntakeView(2025, null!, [], null, false));
        Assert.Throws<ArgumentNullException>(
            () => new StagedIntakeContent("key", 1, null!));

        // The one deliberately nullable collection: an absent answers list has to
        // be representable before validation, because "absent" and "empty" are
        // different failures.
        Assert.Null(new SaveIntakeAnswersCommand(null).Answers);
    }

    private static bool IsSensitive(PropertyInfo property) =>
        property.PropertyType == typeof(TrustedFirmId) ||
        property.PropertyType == typeof(IntakeSecret) ||
        property.PropertyType == typeof(IntakeTokenDigest) ||
        property.PropertyType == typeof(IntakeRedemptionProof) ||
        property.PropertyType == typeof(Guid) ||
        property.PropertyType == typeof(Guid?) ||
        property.PropertyType == typeof(IReadOnlyList<byte>) ||
        property.Name.Contains("DisplayName", StringComparison.Ordinal) ||
        property.Name.Contains("Prompt", StringComparison.Ordinal) ||
        property.Name.Contains("ContentKey", StringComparison.Ordinal);
}
