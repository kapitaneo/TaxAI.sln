using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentExtraction.Models;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Tests.Models;

/// <summary>
/// The mandatory PII-omission matrix at the constructed-value/plumbing level
/// (T01 arm). SIN-like strings are planted separately in the Box 12 value,
/// another box's value, the payload identity fields, and every
/// caller-controlled reference, on both an accepted-shaped and a
/// rejected-shaped envelope plus a reflection-constructed
/// <see cref="ValidatedExtraction"/>. The raw values must stay readable in
/// memory exactly as supplied, while every supported direct JSON serialization
/// and every public <c>ToString()</c> omits them entirely — not even a masked
/// form may appear.
/// </summary>
public sealed class PayloadOmissionTests
{
    private static readonly string[] SinLikeValues = { "123456789", "123-456-789", "123 456 789" };

    private static readonly string[] PlantSlots =
    {
        "Box12Value",
        "OtherBoxValue",
        "EmployerName",
        "EmployeeName",
        "DocumentId",
        "FirmId",
        "CaseId",
        "ParticipantId",
        "SourceReference",
        "ProviderLabel",
    };

    private static readonly Regex NineConsecutiveDigits = new(@"\d{9}", RegexOptions.None, TimeSpan.FromSeconds(1));

    public static TheoryData<string, string> Matrix()
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
    [MemberData(nameof(Matrix))]
    public void PlantedValue_StaysReadableInMemoryExactlyAsSupplied(string slot, string planted)
    {
        foreach (var envelope in BothEnvelopes(slot, planted))
        {
            Assert.Equal(planted, ReadSlot(envelope, slot));
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void EnvelopeJson_OmitsThePlantedValueEntirely(string slot, string planted)
    {
        foreach (var envelope in BothEnvelopes(slot, planted))
        {
            var json = JsonSerializer.Serialize(envelope);
            Assert.DoesNotContain(planted, json, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void EnvelopeToString_OmitsThePlantedValueAndAnyNineDigitRun(string slot, string planted)
    {
        foreach (var envelope in BothEnvelopes(slot, planted))
        {
            var outputs = new[] { envelope.ToString(), envelope.Provenance!.ToString() };
            foreach (var output in outputs)
            {
                Assert.DoesNotContain(planted, output, StringComparison.Ordinal);
                Assert.DoesNotMatch(NineConsecutiveDigits, output);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ValidatedExtractionJsonAndToString_OmitThePlantedValueEntirely(string slot, string planted)
    {
        var validated = BuildValidatedExtraction(slot, planted);

        var json = JsonSerializer.Serialize(validated);
        Assert.DoesNotContain(planted, json, StringComparison.Ordinal);

        var text = validated.ToString();
        Assert.DoesNotContain(planted, text, StringComparison.Ordinal);
        Assert.DoesNotMatch(NineConsecutiveDigits, text);
    }

    [Fact]
    public void ValidatedExtraction_ExposesPlantedValuesProgrammaticallyUnchanged()
    {
        foreach (var sin in SinLikeValues)
        {
            var payload = BuildPayload("Box12Value", sin, year: 2025);
            var validated = ContractTests.CreateValidatedExtraction(sin, sin, sin, sin, sin, sin, payload);

            Assert.Equal(sin, validated.DocumentId);
            Assert.Equal(sin, validated.FirmId);
            Assert.Equal(sin, validated.CaseId);
            Assert.Equal(sin, validated.ParticipantId);
            Assert.Equal(sin, validated.SourceReference);
            Assert.Equal(sin, validated.ProviderLabel);
            Assert.Same(payload, validated.T4Payload);
            Assert.Equal(sin, validated.T4Payload!.Boxes.Single(b => b.Code == "12").Value);
        }
    }

    // --- builders ---

    /// <summary>An accepted-shaped envelope and a rejected-shaped one (2024 payload year).</summary>
    private static IEnumerable<ExtractionEnvelope> BothEnvelopes(string slot, string planted)
    {
        yield return BuildEnvelope(slot, planted, payloadYear: 2025);
        yield return BuildEnvelope(slot, planted, payloadYear: 2024);
    }

    private static ExtractionEnvelope BuildEnvelope(string slot, string planted, int payloadYear)
    {
        string Plant(string name, string fallback) => slot == name ? planted : fallback;

        return new ExtractionEnvelope
        {
            DocumentId = Plant("DocumentId", "doc-1"),
            Kind = ExtractedDocumentKind.T4Slip,
            FirmId = Plant("FirmId", "firm-1"),
            CaseId = Plant("CaseId", "case-1"),
            ParticipantId = Plant("ParticipantId", "participant-1"),
            TaxYear = 2025,
            Provenance = new ExtractionProvenance
            {
                SourceReference = Plant("SourceReference", "store/ref-1"),
                ProviderLabel = Plant("ProviderLabel", "provider"),
                Confidence = 0.95m,
            },
            T4Payload = BuildPayload(slot, planted, payloadYear),
        };
    }

    private static T4RawInput BuildPayload(string slot, string planted, int year)
    {
        string Plant(string name, string fallback) => slot == name ? planted : fallback;

        return new T4RawInput
        {
            Year = year,
            EmployerName = Plant("EmployerName", "Employer Ltd"),
            EmployeeName = Plant("EmployeeName", "Employee"),
            Boxes = new[]
            {
                new T4RawBox("12", Plant("Box12Value", "value-12")),
                new T4RawBox("14", Plant("OtherBoxValue", "value-14")),
            },
        };
    }

    private static ValidatedExtraction BuildValidatedExtraction(string slot, string planted)
    {
        string Plant(string name, string fallback) => slot == name ? planted : fallback;

        return ContractTests.CreateValidatedExtraction(
            Plant("DocumentId", "doc-1"),
            Plant("FirmId", "firm-1"),
            Plant("CaseId", "case-1"),
            Plant("ParticipantId", "participant-1"),
            Plant("SourceReference", "store/ref-1"),
            Plant("ProviderLabel", "provider"),
            BuildPayload(slot, planted, year: 2025));
    }

    private static string? ReadSlot(ExtractionEnvelope envelope, string slot) => slot switch
    {
        "Box12Value" => envelope.T4Payload!.Boxes.Single(b => b.Code == "12").Value,
        "OtherBoxValue" => envelope.T4Payload!.Boxes.Single(b => b.Code == "14").Value,
        "EmployerName" => envelope.T4Payload!.EmployerName,
        "EmployeeName" => envelope.T4Payload!.EmployeeName,
        "DocumentId" => envelope.DocumentId,
        "FirmId" => envelope.FirmId,
        "CaseId" => envelope.CaseId,
        "ParticipantId" => envelope.ParticipantId,
        "SourceReference" => envelope.Provenance!.SourceReference,
        "ProviderLabel" => envelope.Provenance!.ProviderLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown plant slot."),
    };
}
