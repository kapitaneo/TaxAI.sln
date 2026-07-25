using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Tests.EndToEnd;

/// <summary>
/// T03 — AC-28 replay determinism through the public API. The same envelope
/// must always yield the same outcome: equal success/failure, an equal error
/// sequence <i>in the same order</i>, and the same payload reference.
/// <para>
/// Error <b>ordering</b> is asserted, not just set equality — an
/// order-dependent implementation detail (dictionary or hash-set iteration,
/// say) would otherwise slip through while still producing "the same errors".
/// </para>
/// </summary>
public sealed class ExtractionDeterminismTests
{
    private const int ReplayCount = 5;

    public static TheoryData<string> AcceptedCases() => new()
    {
        "T4",
        "T4NoConfidence",
        "T4DuplicateBoxes",
        "GenericEvidence",
    };

    public static TheoryData<string> RejectedCases() => new()
    {
        "Empty",
        "NullishReferences",
        "UndefinedKind",
        "UnsupportedYear",
        "PayloadYearMismatch",
        "AbsentPayloadYear",
        "MissingPayload",
        "PayloadOnGenericEvidence",
        "EmptyExtraction",
        "ConfidenceOutOfRange",
        "ManyDefectsAtOnce",
    };

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public void AC28_ReplayingAnAcceptedEnvelope_YieldsEqualResultsAndTheSamePayloadReference(string name)
    {
        var envelope = Case(name);

        var results = Enumerable.Range(0, ReplayCount)
            .Select(_ => ExtractionEnvelopeValidator.Validate(envelope))
            .ToArray();

        var first = results[0];
        Assert.True(first.IsSuccess);

        foreach (var result in results)
        {
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Errors);
            Assert.Equal(first.Value!.DocumentId, result.Value!.DocumentId);
            Assert.Equal(first.Value.Kind, result.Value.Kind);
            Assert.Equal(first.Value.FirmId, result.Value.FirmId);
            Assert.Equal(first.Value.CaseId, result.Value.CaseId);
            Assert.Equal(first.Value.ParticipantId, result.Value.ParticipantId);
            Assert.Equal(first.Value.TaxYear, result.Value.TaxYear);
            Assert.Equal(first.Value.SourceReference, result.Value.SourceReference);
            Assert.Equal(first.Value.ProviderLabel, result.Value.ProviderLabel);
            Assert.Equal(first.Value.Confidence, result.Value.Confidence);

            // The same instance every time — never a defensive copy.
            Assert.Same(envelope.T4Payload, result.Value.T4Payload);
            Assert.Equal(first.Value.ToString(), result.Value.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void AC28_ReplayingARejectedEnvelope_YieldsAnIdenticalErrorSequence(string name)
    {
        var envelope = Case(name);

        var results = Enumerable.Range(0, ReplayCount)
            .Select(_ => ExtractionEnvelopeValidator.Validate(envelope))
            .ToArray();

        var first = results[0];
        Assert.False(first.IsSuccess);
        Assert.NotEmpty(first.Errors);

        foreach (var result in results)
        {
            Assert.False(result.IsSuccess);
            Assert.Null(result.Value);

            // ValidationError is a record: sequence equality compares Field and
            // Message pairwise, in order.
            Assert.Equal(first.Errors, result.Errors);
            Assert.Equal(
                first.Errors.Select(error => error.Field),
                result.Errors.Select(error => error.Field));
        }
    }

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void AC28_TwoDistinctButEquivalentEnvelopes_ProduceTheSameErrorSequence(string name)
    {
        // Determinism is a property of the input's content, not of object identity:
        // rebuilding an equivalent envelope must reproduce the result exactly.
        var first = ExtractionEnvelopeValidator.Validate(Case(name));
        var second = ExtractionEnvelopeValidator.Validate(Case(name));

        Assert.Equal(first.IsSuccess, second.IsSuccess);
        Assert.Equal(first.Errors, second.Errors);
    }

    [Fact]
    public void AC28_ValidationMutatesNothingOnTheEnvelopeOrItsPayload()
    {
        var envelope = Case("ManyDefectsAtOnce");
        var payload = envelope.T4Payload!;
        var boxesBefore = payload.Boxes.Select(box => (box.Code, box.Value)).ToArray();

        ExtractionEnvelopeValidator.Validate(envelope);
        ExtractionEnvelopeValidator.Validate(envelope);

        Assert.Equal("   ", envelope.DocumentId);
        Assert.Equal(2024, envelope.TaxYear);
        Assert.Equal(1999, payload.Year);
        Assert.Same(payload, envelope.T4Payload);
        Assert.Equal(boxesBefore, payload.Boxes.Select(box => (box.Code, box.Value)));
    }

    [Fact]
    public void AC28_AnAcceptedAndARejectedEnvelopeNeverConvergeAcrossReplays()
    {
        var accepted = Case("T4");
        var rejected = Case("UnsupportedYear");

        for (var replay = 0; replay < ReplayCount; replay++)
        {
            Assert.True(ExtractionEnvelopeValidator.Validate(accepted).IsSuccess);
            Assert.False(ExtractionEnvelopeValidator.Validate(rejected).IsSuccess);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static ExtractionEnvelope Case(string name) => name switch
    {
        "T4" => T4Envelope(),
        "T4NoConfidence" => T4Envelope(confidence: null),
        "T4DuplicateBoxes" => T4Envelope(payload: new T4RawInput
        {
            Year = 2025,
            EmployerName = "Employer Ltd",
            Boxes = new[]
            {
                new T4RawBox("14", "one"),
                new T4RawBox("14", "two"),
                new T4RawBox("16", "three"),
            },
        }),
        "GenericEvidence" => GenericEvidenceEnvelope(),

        "Empty" => new ExtractionEnvelope(),
        "NullishReferences" => T4Envelope(documentId: null, firmId: "", caseId: "   ", participantId: "\t"),
        "UndefinedKind" => T4Envelope(kind: (ExtractedDocumentKind)99),
        "UnsupportedYear" => T4Envelope(taxYear: 2026, payload: Payload(2026)),
        "PayloadYearMismatch" => T4Envelope(payload: Payload(2024)),
        "AbsentPayloadYear" => T4Envelope(payload: Payload(null)),
        "MissingPayload" => T4Envelope(noPayload: true),
        "PayloadOnGenericEvidence" => GenericEvidenceEnvelope(payload: Payload()),
        "EmptyExtraction" => T4Envelope(payload: new T4RawInput
        {
            Year = 2025,
            Boxes = Array.Empty<T4RawBox>(),
        }),
        "ConfidenceOutOfRange" => T4Envelope(confidence: 3m),
        "ManyDefectsAtOnce" => T4Envelope(
            documentId: "   ",
            taxYear: 2024,
            payload: Payload(1999),
            confidence: -5m),

        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown determinism case."),
    };

    private static T4RawInput Payload(int? year = 2025) => new()
    {
        Year = year,
        EmployerName = "Employer Ltd",
        EmployeeName = "Employee",
        Boxes = new[] { new T4RawBox("14", "50000.00") },
    };

    private static ExtractionEnvelope T4Envelope(
        string? documentId = "doc-1",
        ExtractedDocumentKind? kind = ExtractedDocumentKind.T4Slip,
        string? firmId = "firm-1",
        string? caseId = "case-1",
        string? participantId = "participant-1",
        int? taxYear = 2025,
        decimal? confidence = 0.5m,
        T4RawInput? payload = null,
        bool noPayload = false) => new()
        {
            DocumentId = documentId,
            Kind = kind,
            FirmId = firmId,
            CaseId = caseId,
            ParticipantId = participantId,
            TaxYear = taxYear,
            Provenance = new ExtractionProvenance
            {
                SourceReference = "store/ref-1",
                ProviderLabel = "provider",
                Confidence = confidence,
            },
            T4Payload = noPayload ? null : payload ?? Payload(),
        };

    private static ExtractionEnvelope GenericEvidenceEnvelope(T4RawInput? payload = null) => new()
    {
        DocumentId = "doc-evidence",
        Kind = ExtractedDocumentKind.GenericEvidence,
        FirmId = "firm-1",
        CaseId = "case-1",
        ParticipantId = "participant-1",
        TaxYear = 2025,
        Provenance = new ExtractionProvenance
        {
            SourceReference = "store/ref-1",
            ProviderLabel = "provider",
        },
        T4Payload = payload,
    };
}
