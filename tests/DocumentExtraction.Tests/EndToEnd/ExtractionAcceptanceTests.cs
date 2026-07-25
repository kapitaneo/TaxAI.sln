using System.Reflection;
using DocumentExtraction.Models;
using DocumentExtraction.Ports;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Tests.EndToEnd;

/// <summary>
/// T03 — acceptance and untrusted-input assurance exercised only through the
/// module's public surface. Covers the accepted envelope families with their
/// exact exposed attribution, AC-20 (a validated extraction produces no tax
/// truth), AC-21, and AC-25 (tax-year isolation).
/// </summary>
public sealed class ExtractionAcceptanceTests
{
    /// <summary>
    /// The engine types a validated extraction must never become or contain.
    /// Named by type, not by string, so a rename cannot silently weaken this.
    /// </summary>
    private static readonly Type[] ForbiddenTaxTruthTypes =
    {
        typeof(T4Slip),
        typeof(T4MappingResult),
        typeof(TaxLineEntry),
    };

    [Fact]
    public void ValidT4Envelope_IsAcceptedWithExactlyTheSuppliedAttribution()
    {
        var payload = Payload();
        var result = ExtractionEnvelopeValidator.Validate(T4Envelope(payload));

        var value = Accepted(result);
        Assert.Equal("doc-t4", value.DocumentId);
        Assert.Equal(ExtractedDocumentKind.T4Slip, value.Kind);
        Assert.Equal("firm-7", value.FirmId);
        Assert.Equal("case-42", value.CaseId);
        Assert.Equal("participant-3", value.ParticipantId);
        Assert.Equal(2025, value.TaxYear);
        Assert.Equal("store://t4/scan-1", value.SourceReference);
        Assert.Equal("provider-x", value.ProviderLabel);
        Assert.Equal(0.91m, value.Confidence);
        Assert.Same(payload, value.T4Payload);
    }

    [Fact]
    public void ValidGenericEvidenceEnvelope_IsAcceptedWithItsSourceReferenceAndNoPayload()
    {
        var result = ExtractionEnvelopeValidator.Validate(GenericEvidenceEnvelope());

        var value = Accepted(result);
        Assert.Equal(ExtractedDocumentKind.GenericEvidence, value.Kind);
        Assert.Equal("store://evidence/receipt-9", value.SourceReference);
        Assert.Equal("firm-7", value.FirmId);
        Assert.Equal("case-42", value.CaseId);
        Assert.Equal("participant-3", value.ParticipantId);
        Assert.Equal(2025, value.TaxYear);
        Assert.Null(value.T4Payload);
    }

    // ------------------------------------------- AC-20: no tax truth is produced

    [Fact]
    public void AC20_AcceptedResult_IsNeitherASlipAMappingResultNorALineEntry()
    {
        var value = Accepted(ExtractionEnvelopeValidator.Validate(T4Envelope(Payload())));

        Assert.IsType<ValidatedExtraction>(value);
        foreach (var forbidden in ForbiddenTaxTruthTypes)
        {
            Assert.False(
                forbidden.IsInstanceOfType(value),
                $"A validated extraction must never be a {forbidden.Name}.");
        }
    }

    [Fact]
    public void AC20_AcceptedResult_ExposesNoPropertyOfAnyTaxTruthType()
    {
        // Walk the public surface rather than spot-checking: a later property of a
        // forbidden type would be caught here, not silently shipped.
        var properties = typeof(ValidatedExtraction)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        foreach (var property in properties)
        {
            foreach (var forbidden in ForbiddenTaxTruthTypes)
            {
                Assert.False(
                    forbidden.IsAssignableFrom(property.PropertyType),
                    $"{property.Name} exposes {forbidden.Name}; the contract must carry no tax truth.");
            }
        }
    }

    [Fact]
    public void AC20_AcceptedResult_ExposesTheRawPayloadUninspectedAndUnconverted()
    {
        var boxes = new[]
        {
            new T4RawBox("14", " 50000.00 "),   // untrimmed on purpose
            new T4RawBox("22", "7,500.5"),      // unrounded, thousands separator
            new T4RawBox("12", "123456789"),
        };
        var payload = new T4RawInput
        {
            Year = 2025,
            EmployerName = "  Employer Ltd  ",
            EmployeeName = "Employee",
            Boxes = boxes,
        };

        var value = Accepted(ExtractionEnvelopeValidator.Validate(T4Envelope(payload)));

        // Reference identity is the strongest available proof that nothing was
        // parsed, typed, normalized, redacted, converted, mapped, or rounded:
        // a transformed payload could not be the same instance.
        Assert.Same(payload, value.T4Payload);
        Assert.Same(boxes, value.T4Payload!.Boxes);
        Assert.IsType<T4RawInput>(value.T4Payload);

        // Values survive byte-for-byte, including whitespace and formatting the
        // engine would later reject or normalize.
        Assert.Equal(" 50000.00 ", value.T4Payload.Boxes[0].Value);
        Assert.Equal("7,500.5", value.T4Payload.Boxes[1].Value);
        Assert.Equal("123456789", value.T4Payload.Boxes[2].Value);
        Assert.Equal("  Employer Ltd  ", value.T4Payload.EmployerName);
    }

    [Fact]
    public void AC21_APayloadTheEngineWouldRejectStillClearsTheEnvelopeGate()
    {
        // T4SlipParser would reject all of this: a duplicate code, a non-numeric
        // amount, an unknown code, and an empty value. The envelope gate makes no
        // judgement about any of it.
        var payload = new T4RawInput
        {
            Year = 2025,
            EmployerName = "Employer Ltd",
            Boxes = new[]
            {
                new T4RawBox("14", "not-a-number"),
                new T4RawBox("14", "duplicate"),
                new T4RawBox("ZZZZ", "unknown code"),
                new T4RawBox("16", null),
            },
        };

        var value = Accepted(ExtractionEnvelopeValidator.Validate(T4Envelope(payload)));

        Assert.Same(payload, value.T4Payload);
        Assert.Equal(4, value.T4Payload!.Boxes.Count);
    }

    // --------------------------------------------- AC-25: tax-year isolation

    public static TheoryData<int?, int?> YearCombinations()
    {
        var data = new TheoryData<int?, int?>();
        int?[] years = { null, 2024, 2025, 2026 };
        foreach (var envelopeYear in years)
        {
            foreach (var payloadYear in years)
            {
                data.Add(envelopeYear, payloadYear);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(YearCombinations))]
    public void AC25_AValidatedExtractionExistsOnlyWhenBothYearsAre2025(int? envelopeYear, int? payloadYear)
    {
        var envelope = T4Envelope(Payload(payloadYear), taxYear: envelopeYear);

        var result = ExtractionEnvelopeValidator.Validate(envelope);

        var shouldBeAccepted = envelopeYear == 2025 && payloadYear == 2025;
        Assert.Equal(shouldBeAccepted, result.IsSuccess);

        if (shouldBeAccepted)
        {
            Assert.Equal(2025, result.Value!.TaxYear);
            Assert.Equal(2025, result.Value.T4Payload!.Year);
        }
        else
        {
            Assert.Null(result.Value);
            Assert.NotEmpty(result.Errors);
        }

        // Never coerced in either direction, accepted or rejected.
        Assert.Equal(envelopeYear, envelope.TaxYear);
        Assert.Equal(payloadYear, envelope.T4Payload!.Year);
    }

    [Fact]
    public void AC25_The2024And2026EnvelopesAndThe2025Envelope2024PayloadAllFailClosed()
    {
        // The three cases the criterion names explicitly.
        Assert.False(ExtractionEnvelopeValidator.Validate(
            T4Envelope(Payload(2024), taxYear: 2024)).IsSuccess);
        Assert.False(ExtractionEnvelopeValidator.Validate(
            T4Envelope(Payload(2026), taxYear: 2026)).IsSuccess);
        Assert.False(ExtractionEnvelopeValidator.Validate(
            T4Envelope(Payload(2024), taxYear: 2025)).IsSuccess);
    }

    [Fact]
    public void AC25_AGenericEvidenceEnvelopeIsAlsoYearIsolated()
    {
        Assert.False(ExtractionEnvelopeValidator.Validate(GenericEvidenceEnvelope(taxYear: 2024)).IsSuccess);
        Assert.False(ExtractionEnvelopeValidator.Validate(GenericEvidenceEnvelope(taxYear: 2026)).IsSuccess);
        Assert.True(ExtractionEnvelopeValidator.Validate(GenericEvidenceEnvelope(taxYear: 2025)).IsSuccess);
    }

    // -------------------------------------------------------- ingress port shape

    [Fact]
    public async Task TheIngressPortShape_CanBeSatisfiedByATestOnlyAdapter_WithNoProductionImplementor()
    {
        // A minimal test-only adapter, permitted by T03: it proves the port's async
        // shape composes with the synchronous gate. No production implementation is
        // added, which the next assertion re-checks.
        IDocumentExtractionIngress ingress = new PassThroughIngress();

        var accepted = await ingress.SubmitAsync(T4Envelope(Payload()));
        var rejected = await ingress.SubmitAsync(new ExtractionEnvelope());

        Assert.True(accepted.IsSuccess);
        Assert.False(rejected.IsSuccess);

        var productionImplementors = typeof(ValidatedExtraction).Assembly
            .GetTypes()
            .Where(type => typeof(IDocumentExtractionIngress).IsAssignableFrom(type) && type.IsClass)
            .ToArray();
        Assert.Empty(productionImplementors);
    }

    private sealed class PassThroughIngress : IDocumentExtractionIngress
    {
        public Task<ParseResult<ValidatedExtraction>> SubmitAsync(
            ExtractionEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExtractionEnvelopeValidator.Validate(envelope));
    }

    // ------------------------------------------------------------------ helpers

    private static ValidatedExtraction Accepted(ParseResult<ValidatedExtraction> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
        return result.Value!;
    }

    private static T4RawInput Payload(int? year = 2025) => new()
    {
        Year = year,
        EmployerName = "Employer Ltd",
        EmployeeName = "Employee",
        Boxes = new[] { new T4RawBox("14", "50000.00") },
    };

    private static ExtractionEnvelope T4Envelope(T4RawInput payload, int? taxYear = 2025) => new()
    {
        DocumentId = "doc-t4",
        Kind = ExtractedDocumentKind.T4Slip,
        FirmId = "firm-7",
        CaseId = "case-42",
        ParticipantId = "participant-3",
        TaxYear = taxYear,
        Provenance = new ExtractionProvenance
        {
            SourceReference = "store://t4/scan-1",
            ProviderLabel = "provider-x",
            Confidence = 0.91m,
        },
        T4Payload = payload,
    };

    private static ExtractionEnvelope GenericEvidenceEnvelope(int? taxYear = 2025) => new()
    {
        DocumentId = "doc-evidence",
        Kind = ExtractedDocumentKind.GenericEvidence,
        FirmId = "firm-7",
        CaseId = "case-42",
        ParticipantId = "participant-3",
        TaxYear = taxYear,
        Provenance = new ExtractionProvenance
        {
            SourceReference = "store://evidence/receipt-9",
            ProviderLabel = "provider-x",
        },
    };
}
