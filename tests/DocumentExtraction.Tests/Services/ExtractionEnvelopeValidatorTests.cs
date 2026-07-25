using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Tests.Services;

/// <summary>
/// T02 — the fail-closed envelope gate. Covers AC-1 through AC-19, AC-21,
/// AC-23's validator arm, and AC-28, plus the mandatory PII-omission matrix run
/// through <see cref="ExtractionEnvelopeValidator.Validate"/>.
/// <para>
/// Every rejection asserts the same two invariants: no value is produced, and
/// every <c>ValidationError.Field</c> is one of the closed
/// <see cref="ExtractionFields"/> constants.
/// </para>
/// </summary>
public sealed class ExtractionEnvelopeValidatorTests
{
    private static readonly Regex NineConsecutiveDigits =
        new(@"\d{9}", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> ClosedFieldSet = typeof(ExtractionFields)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    public static TheoryData<string?> BlankValues() => new() { null, "", "   ", "\t", "\r\n" };

    // ---------------------------------------------------------------- happy path

    [Fact]
    public void AC1_ValidT4Envelope_SucceedsAndExposesThePayloadReferenceIdentical()
    {
        var payload = ValidPayload();
        var envelope = T4Envelope(payload: payload, provenance: Provenance(confidence: 0.87m));

        var result = ExtractionEnvelopeValidator.Validate(envelope);

        AssertAccepted(result);
        var value = result.Value!;
        Assert.Equal("doc-1", value.DocumentId);
        Assert.Equal(ExtractedDocumentKind.T4Slip, value.Kind);
        Assert.Equal("firm-1", value.FirmId);
        Assert.Equal("case-1", value.CaseId);
        Assert.Equal("participant-1", value.ParticipantId);
        Assert.Equal(2025, value.TaxYear);
        Assert.Equal("store/ref-1", value.SourceReference);
        Assert.Equal("provider", value.ProviderLabel);
        Assert.Equal(0.87m, value.Confidence);

        // Reference-identical, not merely equal: nothing was cloned or rebuilt.
        Assert.Same(payload, value.T4Payload);
        Assert.Same(payload.Boxes, value.T4Payload!.Boxes);
    }

    [Fact]
    public void AC2_ValidGenericEvidenceEnvelope_SucceedsWithSourceReferenceAndNoPayload()
    {
        var envelope = GenericEvidenceEnvelope();

        var result = ExtractionEnvelopeValidator.Validate(envelope);

        AssertAccepted(result);
        Assert.Equal(ExtractedDocumentKind.GenericEvidence, result.Value!.Kind);
        Assert.Equal("store/ref-1", result.Value.SourceReference);
        Assert.Null(result.Value.T4Payload);
    }

    [Fact]
    public void AcceptedReferences_PassThroughExactlyAsSupplied_WithoutTrimmingOrNormalization()
    {
        // Non-blank but padded: the contract treats references as opaque and must
        // not normalize them (architecture rule 8).
        var envelope = T4Envelope(documentId: "  doc-1  ", caseId: "\tcase-1\t");

        var result = ExtractionEnvelopeValidator.Validate(envelope);

        AssertAccepted(result);
        Assert.Equal("  doc-1  ", result.Value!.DocumentId);
        Assert.Equal("\tcase-1\t", result.Value.CaseId);
    }

    public static TheoryData<decimal?> AcceptedConfidences() => new() { null, 0m, 1m, 0.5m };

    [Theory]
    [MemberData(nameof(AcceptedConfidences))]
    public void AC16_ConfidenceAtBoundsOrAbsent_IsAccepted(decimal? confidence)
    {
        var envelope = T4Envelope(provenance: Provenance(confidence: confidence));

        var result = ExtractionEnvelopeValidator.Validate(envelope);

        AssertAccepted(result);
        Assert.Equal(confidence, result.Value!.Confidence);
    }

    [Fact]
    public void AC18_DuplicateBoxCodes_SurviveValidationInOrder()
    {
        var payload = new T4RawInput
        {
            Year = 2025,
            EmployerName = "Employer Ltd",
            Boxes = new[]
            {
                new T4RawBox("14", "first"),
                new T4RawBox("14", "second"),
                new T4RawBox("16", "third"),
            },
        };

        var result = ExtractionEnvelopeValidator.Validate(T4Envelope(payload: payload));

        AssertAccepted(result);
        Assert.Same(payload, result.Value!.T4Payload);
        // T4RawBox.Value is nullable, so the expected tuples must be too — otherwise
        // the comparison is against (string, string) and warns CS8620.
        Assert.Equal(
            new (string Code, string? Value)[] { ("14", "first"), ("14", "second"), ("16", "third") },
            result.Value.T4Payload!.Boxes.Select(box => (box.Code, box.Value)));
    }

    [Fact]
    public void AC21_BoxValueTheEngineWouldReject_StillPassesTheEnvelopeGate()
    {
        // "not-a-number" in an amount box and an unknown box code are the engine's
        // problem downstream; the envelope gate makes no judgement about either.
        var payload = new T4RawInput
        {
            Year = 2025,
            EmployerName = "Employer Ltd",
            Boxes = new[]
            {
                new T4RawBox("14", "not-a-number"),
                new T4RawBox("ZZZ", "???"),
            },
        };

        var result = ExtractionEnvelopeValidator.Validate(T4Envelope(payload: payload));

        AssertAccepted(result);
        Assert.Same(payload, result.Value!.T4Payload);
        Assert.Equal("not-a-number", result.Value.T4Payload!.Boxes[0].Value);
    }

    // ---------------------------------------------------------- fail-closed rules

    [Fact]
    public void NullEnvelope_FailsWithASingleEnvelopeError_AndNeverThrows()
    {
        var result = ExtractionEnvelopeValidator.Validate(null);

        AssertRejected(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ExtractionFields.Envelope, error.Field);
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AC3_BlankDocumentId_FailsNamingTheDocumentIdField(string? blank) =>
        AssertSingleFieldRejection(T4Envelope(documentId: blank), ExtractionFields.DocumentId);

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AC5_BlankFirmId_FailsNamingTheFirmIdField(string? blank) =>
        AssertSingleFieldRejection(T4Envelope(firmId: blank), ExtractionFields.FirmId);

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AC6_BlankCaseId_FailsNamingTheCaseIdField(string? blank) =>
        AssertSingleFieldRejection(T4Envelope(caseId: blank), ExtractionFields.CaseId);

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AC7_BlankParticipantId_FailsNamingTheParticipantIdField(string? blank) =>
        AssertSingleFieldRejection(T4Envelope(participantId: blank), ExtractionFields.ParticipantId);

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AC8_BlankSourceReferenceOnAT4Envelope_FailsNamingTheSourceReferenceField(string? blank) =>
        AssertSingleFieldRejection(
            T4Envelope(provenance: Provenance(sourceReference: blank)),
            ExtractionFields.SourceReference);

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AC8_BlankSourceReferenceOnAGenericEvidenceEnvelope_FailsNamingTheSourceReferenceField(string? blank) =>
        AssertSingleFieldRejection(
            GenericEvidenceEnvelope(provenance: Provenance(sourceReference: blank)),
            ExtractionFields.SourceReference);

    [Fact]
    public void AC8_AbsentProvenance_ReportsTheSourceReferenceError()
    {
        // The reference is required for every kind, so a missing container is the
        // same failure as a blank reference — not a separate "Provenance" error.
        AssertSingleFieldRejection(T4Envelope(noProvenance: true), ExtractionFields.SourceReference);
        AssertSingleFieldRejection(
            GenericEvidenceEnvelope(noProvenance: true),
            ExtractionFields.SourceReference);
    }

    [Fact]
    public void AC17_WhitespaceOnlyReferences_AreTreatedExactlyAsBlank()
    {
        const string whitespace = "   ";

        AssertSingleFieldRejection(T4Envelope(documentId: whitespace), ExtractionFields.DocumentId);
        AssertSingleFieldRejection(T4Envelope(firmId: whitespace), ExtractionFields.FirmId);
        AssertSingleFieldRejection(T4Envelope(caseId: whitespace), ExtractionFields.CaseId);
        AssertSingleFieldRejection(T4Envelope(participantId: whitespace), ExtractionFields.ParticipantId);
        AssertSingleFieldRejection(
            T4Envelope(provenance: Provenance(sourceReference: whitespace)),
            ExtractionFields.SourceReference);
    }

    public static TheoryData<ExtractedDocumentKind?> UndefinedKinds() => new()
    {
        null,
        (ExtractedDocumentKind)0,      // default(ExtractedDocumentKind) is not a defined value
        (ExtractedDocumentKind)3,      // a T5 stand-in — T5 is deferred by approved scope
        (ExtractedDocumentKind)99,
        (ExtractedDocumentKind)(-1),
    };

    [Theory]
    [MemberData(nameof(UndefinedKinds))]
    public void AC4_AbsentOrUndefinedKind_FailsNamingTheKindField(ExtractedDocumentKind? kind) =>
        AssertSingleFieldRejection(T4Envelope(kind: kind), ExtractionFields.Kind);

    [Fact]
    public void AC9_AbsentEnvelopeTaxYear_FailsAndIsNeverCoerced()
    {
        var envelope = T4Envelope(taxYear: null);

        AssertSingleFieldRejection(envelope, ExtractionFields.TaxYear);
        Assert.Null(envelope.TaxYear);
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(1999)]
    public void AC10_UnsupportedEnvelopeTaxYear_FailsWithNoCoercionOrAdjacentYearFallback(int year)
    {
        // The payload year matches the envelope year, so the only defect is that the
        // year is unsupported — proving no fallback to 2025 happens.
        var envelope = T4Envelope(taxYear: year, payload: ValidPayload(year: year));

        AssertSingleFieldRejection(envelope, ExtractionFields.TaxYear);
        Assert.Equal(year, envelope.TaxYear);
        Assert.Equal(year, envelope.T4Payload!.Year);
    }

    [Fact]
    public void AC11_AbsentPayloadYear_FailsNamingThePayloadYearField()
    {
        var envelope = T4Envelope(payload: ValidPayload(year: null));

        AssertSingleFieldRejection(envelope, ExtractionFields.PayloadYear);
        Assert.Null(envelope.T4Payload!.Year);
    }

    [Fact]
    public void AC12_EnvelopePayloadYearMismatch_FailsWithNeitherValueCoerced()
    {
        var envelope = T4Envelope(taxYear: 2025, payload: ValidPayload(year: 2024));

        AssertSingleFieldRejection(envelope, ExtractionFields.PayloadYear);
        Assert.Equal(2025, envelope.TaxYear);
        Assert.Equal(2024, envelope.T4Payload!.Year);
    }

    [Fact]
    public void AC13_T4SlipWithoutPayload_FailsNamingThePayloadField() =>
        AssertSingleFieldRejection(T4Envelope(noPayload: true), ExtractionFields.Payload);

    [Fact]
    public void AC13_GenericEvidenceCarryingAT4Payload_FailsNamingThePayloadField() =>
        AssertSingleFieldRejection(
            GenericEvidenceEnvelope(payload: ValidPayload()),
            ExtractionFields.Payload);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AC14_EmptyT4Extraction_FailsNamingThePayloadContentField(string? identityText)
    {
        var payload = new T4RawInput
        {
            Year = 2025,
            EmployerName = identityText,
            EmployerAddress = identityText,
            EmployeeName = identityText,
            EmployeeAddress = identityText,
            Boxes = Array.Empty<T4RawBox>(),
        };

        AssertSingleFieldRejection(T4Envelope(payload: payload), ExtractionFields.PayloadContent);
    }

    [Fact]
    public void AC14_NullBoxesWithBlankIdentityText_IsAlsoAnEmptyExtraction()
    {
        var payload = new T4RawInput { Year = 2025, Boxes = null! };

        AssertSingleFieldRejection(T4Envelope(payload: payload), ExtractionFields.PayloadContent);
    }

    [Fact]
    public void AC14_AnyIdentityTextOrAnyBox_IsNotAnEmptyExtraction()
    {
        // The rule is mechanical and conjunctive: one box, or one identity field,
        // is enough to clear it.
        AssertAccepted(ExtractionEnvelopeValidator.Validate(T4Envelope(payload: new T4RawInput
        {
            Year = 2025,
            EmployeeAddress = "1 Main St",
            Boxes = Array.Empty<T4RawBox>(),
        })));

        AssertAccepted(ExtractionEnvelopeValidator.Validate(T4Envelope(payload: new T4RawInput
        {
            Year = 2025,
            Boxes = new[] { new T4RawBox("14", null) },
        })));
    }

    public static TheoryData<decimal> RejectedConfidences() => new() { -0.1m, 1.1m, -1m, 2m };

    [Theory]
    [MemberData(nameof(RejectedConfidences))]
    public void AC15_ConfidenceOutsideTheInclusiveRange_FailsNamingTheConfidenceField(decimal confidence) =>
        AssertSingleFieldRejection(
            T4Envelope(provenance: Provenance(confidence: confidence)),
            ExtractionFields.Confidence);

    [Fact]
    public void AC19_EmptyEnvelope_ReportsOneErrorPerMissingRequiredFieldAndThrowsNothing()
    {
        var result = ExtractionEnvelopeValidator.Validate(new ExtractionEnvelope());

        AssertRejected(result);
        Assert.Equal(
            new[]
            {
                ExtractionFields.DocumentId,
                ExtractionFields.FirmId,
                ExtractionFields.CaseId,
                ExtractionFields.ParticipantId,
                ExtractionFields.SourceReference,
                ExtractionFields.Kind,
                ExtractionFields.TaxYear,
            },
            result.Errors.Select(error => error.Field));

        // One error per field — never a duplicate for the same defect.
        Assert.Equal(
            result.Errors.Count,
            result.Errors.Select(error => error.Field).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MultipleIndependentDefects_AreAllReportedInOnePass()
    {
        var result = ExtractionEnvelopeValidator.Validate(T4Envelope(
            documentId: null,
            caseId: "   ",
            taxYear: 2024,
            payload: ValidPayload(year: 2024),
            provenance: Provenance(confidence: 5m)));

        AssertRejected(result);
        Assert.Equal(
            new[]
            {
                ExtractionFields.DocumentId,
                ExtractionFields.CaseId,
                ExtractionFields.TaxYear,
                ExtractionFields.Confidence,
            },
            result.Errors.Select(error => error.Field));
    }

    [Fact]
    public void EveryRejection_UsesOnlyTheClosedExtractionFieldsSet()
    {
        foreach (var envelope in EveryRejectedEnvelope())
        {
            var result = ExtractionEnvelopeValidator.Validate(envelope);

            AssertRejected(result);
            Assert.All(result.Errors, error => Assert.Contains(error.Field, ClosedFieldSet));
        }
    }

    // ------------------------------------------------------------- AC-28 replay

    [Fact]
    public void AC28_ValidatingTheSameAcceptedEnvelopeTwice_YieldsEqualResultsAndTheSamePayloadReference()
    {
        var envelope = T4Envelope();

        var first = ExtractionEnvelopeValidator.Validate(envelope);
        var second = ExtractionEnvelopeValidator.Validate(envelope);

        Assert.Equal(first.IsSuccess, second.IsSuccess);
        Assert.True(first.IsSuccess);
        Assert.Empty(first.Errors);
        Assert.Empty(second.Errors);
        Assert.Same(envelope.T4Payload, first.Value!.T4Payload);
        Assert.Same(first.Value.T4Payload, second.Value!.T4Payload);
        Assert.Equal(first.Value.DocumentId, second.Value.DocumentId);
        Assert.Equal(first.Value.TaxYear, second.Value.TaxYear);
        Assert.Equal(first.Value.Confidence, second.Value.Confidence);
    }

    [Fact]
    public void AC28_ValidatingTheSameRejectedEnvelopeTwice_YieldsEqualErrorSequences()
    {
        foreach (var envelope in EveryRejectedEnvelope())
        {
            var first = ExtractionEnvelopeValidator.Validate(envelope);
            var second = ExtractionEnvelopeValidator.Validate(envelope);

            Assert.Equal(first.IsSuccess, second.IsSuccess);

            // ValidationError is a record, so sequence equality compares both the
            // field and the message, in order.
            Assert.Equal(first.Errors, second.Errors);
        }
    }

    // ------------------------------------- AC-23: PII omission through Validate

    private static readonly string[] SinLikeValues = { "123456789", "123-456-789", "123 456 789" };

    private static readonly string[] PlantSlots =
    {
        "Box12Value", "OtherBoxValue", "EmployerName", "EmployeeName",
        "DocumentId", "FirmId", "CaseId", "ParticipantId", "SourceReference", "ProviderLabel",
    };

    public static TheoryData<string, string> PiiMatrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var slot in PlantSlots)
        {
            foreach (var sin in SinLikeValues)
            {
                data.Add(slot, sin);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PiiMatrix))]
    public void AC23_AcceptedEnvelope_ExposesPlantedValuesInMemoryButOmitsThemFromEveryOutput(
        string slot,
        string planted)
    {
        var result = ExtractionEnvelopeValidator.Validate(PlantedEnvelope(slot, planted, payloadYear: 2025));

        AssertAccepted(result);
        var value = result.Value!;

        // Readable exactly as supplied where the contract exposes it programmatically.
        Assert.Equal(planted, ReadSlot(value, slot));

        // …and absent from every rendered or serialized output.
        AssertOmitted(planted, value.ToString());
        AssertOmitted(planted, JsonSerializer.Serialize(value));
    }

    [Theory]
    [MemberData(nameof(PiiMatrix))]
    public void AC23_RejectedEnvelope_NeverEchoesAPlantedValueInAnyError(string slot, string planted)
    {
        // A 2024 payload year against a 2025 envelope: rejected while every planted
        // caller/payload value is present on the envelope.
        var result = ExtractionEnvelopeValidator.Validate(PlantedEnvelope(slot, planted, payloadYear: 2024));

        AssertRejected(result);
        Assert.All(result.Errors, error =>
        {
            Assert.Contains(error.Field, ClosedFieldSet);
            AssertOmitted(planted, error.Field);
            AssertOmitted(planted, error.Message);
        });
    }

    [Theory]
    [MemberData(nameof(PiiMatrix))]
    public void AC23_EveryRejectionFamily_KeepsPlantedValuesOutOfEveryError(string slot, string planted)
    {
        foreach (var envelope in PlantedRejectionFamilies(slot, planted))
        {
            var result = ExtractionEnvelopeValidator.Validate(envelope);

            AssertRejected(result);
            Assert.All(result.Errors, error =>
            {
                AssertOmitted(planted, error.Field);
                AssertOmitted(planted, error.Message);
            });
        }
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertAccepted(ParseResult<ValidatedExtraction> result)
    {
        Assert.True(result.IsSuccess, Describe(result));
        Assert.False(result.IsFailure);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
    }

    private static void AssertRejected(ParseResult<ValidatedExtraction> result)
    {
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// Asserts the envelope is rejected for exactly one reason, naming
    /// <paramref name="field"/> — the shape every one-defect AC requires.
    /// </summary>
    private static void AssertSingleFieldRejection(ExtractionEnvelope envelope, string field)
    {
        var result = ExtractionEnvelopeValidator.Validate(envelope);

        AssertRejected(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.Field);
        Assert.Contains(error.Field, ClosedFieldSet);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static void AssertOmitted(string planted, string output)
    {
        Assert.DoesNotContain(planted, output, StringComparison.Ordinal);
        Assert.DoesNotMatch(NineConsecutiveDigits, output);
    }

    private static string Describe(ParseResult<ValidatedExtraction> result) =>
        result.IsSuccess
            ? "accepted"
            : "rejected: " + string.Join(", ", result.Errors.Select(error => error.Field));

    private static T4RawInput ValidPayload(int? year = 2025) => new()
    {
        Year = year,
        EmployerName = "Employer Ltd",
        EmployeeName = "Employee",
        Boxes = new[] { new T4RawBox("14", "50000.00") },
    };

    private static ExtractionProvenance Provenance(
        string? sourceReference = "store/ref-1",
        string? providerLabel = "provider",
        decimal? confidence = null) => new()
        {
            SourceReference = sourceReference,
            ProviderLabel = providerLabel,
            Confidence = confidence,
        };

    private static ExtractionEnvelope T4Envelope(
        string? documentId = "doc-1",
        ExtractedDocumentKind? kind = ExtractedDocumentKind.T4Slip,
        string? firmId = "firm-1",
        string? caseId = "case-1",
        string? participantId = "participant-1",
        int? taxYear = 2025,
        ExtractionProvenance? provenance = null,
        T4RawInput? payload = null,
        bool noProvenance = false,
        bool noPayload = false) => new()
        {
            DocumentId = documentId,
            Kind = kind,
            FirmId = firmId,
            CaseId = caseId,
            ParticipantId = participantId,
            TaxYear = taxYear,
            Provenance = noProvenance ? null : provenance ?? Provenance(),
            T4Payload = noPayload ? null : payload ?? ValidPayload(),
        };

    private static ExtractionEnvelope GenericEvidenceEnvelope(
        ExtractionProvenance? provenance = null,
        T4RawInput? payload = null,
        bool noProvenance = false) => new()
        {
            DocumentId = "doc-1",
            Kind = ExtractedDocumentKind.GenericEvidence,
            FirmId = "firm-1",
            CaseId = "case-1",
            ParticipantId = "participant-1",
            TaxYear = 2025,
            Provenance = noProvenance ? null : provenance ?? Provenance(),
            T4Payload = payload,
        };

    /// <summary>One envelope per fail-closed rule, for the invariants that must
    /// hold across every rejection family.</summary>
    private static IEnumerable<ExtractionEnvelope> EveryRejectedEnvelope()
    {
        yield return new ExtractionEnvelope();
        yield return T4Envelope(documentId: null);
        yield return T4Envelope(firmId: "  ");
        yield return T4Envelope(caseId: "");
        yield return T4Envelope(participantId: null);
        yield return T4Envelope(noProvenance: true);
        yield return T4Envelope(provenance: Provenance(sourceReference: "   "));
        yield return T4Envelope(kind: null);
        yield return T4Envelope(kind: (ExtractedDocumentKind)99);
        yield return T4Envelope(taxYear: null);
        yield return T4Envelope(taxYear: 2024, payload: ValidPayload(year: 2024));
        yield return T4Envelope(taxYear: 2026, payload: ValidPayload(year: 2026));
        yield return T4Envelope(noPayload: true);
        yield return GenericEvidenceEnvelope(payload: ValidPayload());
        yield return T4Envelope(payload: ValidPayload(year: null));
        yield return T4Envelope(payload: ValidPayload(year: 2024));
        yield return T4Envelope(payload: new T4RawInput { Year = 2025, Boxes = Array.Empty<T4RawBox>() });
        yield return T4Envelope(provenance: Provenance(confidence: -0.1m));
        yield return T4Envelope(provenance: Provenance(confidence: 1.1m));
    }

    private static IEnumerable<ExtractionEnvelope> PlantedRejectionFamilies(string slot, string planted)
    {
        yield return PlantedEnvelope(slot, planted, payloadYear: 2024);             // year mismatch
        yield return PlantedEnvelope(slot, planted, payloadYear: 2025, kind: null); // undefined kind
        yield return PlantedEnvelope(slot, planted, payloadYear: 2025, taxYear: 2024);
        yield return PlantedEnvelope(slot, planted, payloadYear: 2025, confidence: 9m);
        yield return PlantedEnvelope(slot, planted, payloadYear: null);             // absent payload year
    }

    private static ExtractionEnvelope PlantedEnvelope(
        string slot,
        string planted,
        int? payloadYear,
        ExtractedDocumentKind? kind = ExtractedDocumentKind.T4Slip,
        int? taxYear = 2025,
        decimal? confidence = null)
    {
        string Plant(string name, string fallback) => slot == name ? planted : fallback;

        return new ExtractionEnvelope
        {
            DocumentId = Plant("DocumentId", "doc-1"),
            Kind = kind,
            FirmId = Plant("FirmId", "firm-1"),
            CaseId = Plant("CaseId", "case-1"),
            ParticipantId = Plant("ParticipantId", "participant-1"),
            TaxYear = taxYear,
            Provenance = new ExtractionProvenance
            {
                SourceReference = Plant("SourceReference", "store/ref-1"),
                ProviderLabel = Plant("ProviderLabel", "provider"),
                Confidence = confidence,
            },
            T4Payload = new T4RawInput
            {
                Year = payloadYear,
                EmployerName = Plant("EmployerName", "Employer Ltd"),
                EmployeeName = Plant("EmployeeName", "Employee"),
                Boxes = new[]
                {
                    new T4RawBox("12", Plant("Box12Value", "value-12")),
                    new T4RawBox("14", Plant("OtherBoxValue", "value-14")),
                },
            },
        };
    }

    private static string? ReadSlot(ValidatedExtraction value, string slot) => slot switch
    {
        "Box12Value" => value.T4Payload!.Boxes.Single(box => box.Code == "12").Value,
        "OtherBoxValue" => value.T4Payload!.Boxes.Single(box => box.Code == "14").Value,
        "EmployerName" => value.T4Payload!.EmployerName,
        "EmployeeName" => value.T4Payload!.EmployeeName,
        "DocumentId" => value.DocumentId,
        "FirmId" => value.FirmId,
        "CaseId" => value.CaseId,
        "ParticipantId" => value.ParticipantId,
        "SourceReference" => value.SourceReference,
        "ProviderLabel" => value.ProviderLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown plant slot."),
    };
}
