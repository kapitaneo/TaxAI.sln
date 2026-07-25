using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Tests.EndToEnd;

/// <summary>
/// T03 — the mandatory PII-omission matrix run end-to-end through the public
/// API (AC-22, AC-23). PII here is handled by <b>omission</b>, not masking: the
/// planted value must simply not be there, so a masked form such as
/// <c>***-**-789</c> would fail these tests just as a raw SIN would.
/// <para>
/// Every plant is asserted against four output channels — <c>ValidationError.Field</c>,
/// <c>ValidationError.Message</c>, every public <c>ToString()</c>, and supported
/// direct JSON serialization — for an accepted envelope and for every rejection
/// family, while the same values stay readable in memory exactly as supplied.
/// </para>
/// </summary>
public sealed class ExtractionPiiTests
{
    private static readonly string[] SinLikeValues = { "123456789", "123-456-789", "123 456 789" };

    /// <summary>Non-numeric markers: they catch an echo that a digit-run regex cannot.</summary>
    private static readonly string[] MarkerValues = { "ZZMARKERZZ", "<<leak-me>>" };

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

    private static readonly Regex NineConsecutiveDigits =
        new(@"\d{9}", RegexOptions.None, TimeSpan.FromSeconds(1));

    public static TheoryData<string, string> Matrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var slot in PlantSlots)
        {
            foreach (var planted in SinLikeValues.Concat(MarkerValues))
            {
                data.Add(slot, planted);
            }
        }

        return data;
    }

    // -------------------------------------------------- AC-22: accepted envelope

    [Fact]
    public void AC22_Box12HoldingARealSinOnAnAcceptedEnvelope_IsOmittedFromEveryOutput()
    {
        // The criterion's literal case: a valid T4 envelope whose Box 12 is a SIN
        // and whose identity text fields are populated.
        var envelope = Envelope("Box12Value", "123456789");

        var result = ExtractionEnvelopeValidator.Validate(envelope);
        var value = Accepted(result);

        // Retained in memory, unmasked and unchanged — the downstream parser needs it.
        Assert.Equal("123456789", value.T4Payload!.Boxes.Single(box => box.Code == "12").Value);

        // …and absent from everything the module renders or serializes.
        foreach (var output in Outputs(envelope, value))
        {
            AssertOmitted("123456789", output);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void AC22_AnyPlantedValueOnAnAcceptedEnvelope_StaysInMemoryButLeavesNoOutput(
        string slot,
        string planted)
    {
        var envelope = Envelope(slot, planted);

        var value = Accepted(ExtractionEnvelopeValidator.Validate(envelope));

        Assert.Equal(planted, ReadSlot(value, slot));
        foreach (var output in Outputs(envelope, value))
        {
            AssertOmitted(planted, output);
        }
    }

    // ------------------------------------------------- AC-23: every rejection

    public static TheoryData<string, string, string> RejectionMatrix()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var family in RejectionFamilyNames)
        {
            foreach (var slot in PlantSlots)
            {
                // One representative SIN form and one marker per slot per family:
                // the full cross-product adds runtime without adding coverage, since
                // the omission path does not branch on the planted value's shape.
                data.Add(family, slot, "123456789");
                data.Add(family, slot, "ZZMARKERZZ");
            }
        }

        return data;
    }

    private static readonly string[] RejectionFamilyNames =
    {
        "BlankDocumentId",
        "UndefinedKind",
        "UnsupportedEnvelopeYear",
        "AbsentEnvelopeYear",
        "AbsentPayloadYear",
        "PayloadYearMismatch",
        "PayloadKindMismatch",
        "MissingPayload",
        "EmptyExtraction",
        "ConfidenceOutOfRange",
        "AbsentProvenance",
    };

    [Theory]
    [MemberData(nameof(RejectionMatrix))]
    public void AC23_NoRejection_EverEchoesAPlantedValueInAnyErrorOrDiagnostic(
        string family,
        string slot,
        string planted)
    {
        var envelope = RejectedEnvelope(family, slot, planted);

        var result = ExtractionEnvelopeValidator.Validate(envelope);

        Assert.False(result.IsSuccess, $"Family '{family}' was expected to fail closed.");
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);

        foreach (var error in result.Errors)
        {
            AssertOmitted(planted, error.Field);
            AssertOmitted(planted, error.Message);
        }

        // Diagnostics on the rejected envelope itself must be just as clean.
        AssertOmitted(planted, envelope.ToString());
        if (envelope.Provenance is not null)
        {
            AssertOmitted(planted, envelope.Provenance.ToString());
        }

        AssertOmitted(planted, JsonSerializer.Serialize(envelope));
    }

    [Fact]
    public void AC23_ARejectionCarryingASinInBox12AndMarkersInEveryReference_LeaksNothing()
    {
        // Everything planted at once, so a leak from any single channel is caught
        // even if the per-slot theories were somehow all satisfied individually.
        var envelope = new ExtractionEnvelope
        {
            DocumentId = "DOC-MARKER-123456789",
            Kind = ExtractedDocumentKind.T4Slip,
            FirmId = "FIRM-MARKER-987654321",
            CaseId = "CASE-MARKER",
            ParticipantId = "PARTICIPANT-MARKER",
            TaxYear = 2024,
            Provenance = new ExtractionProvenance
            {
                SourceReference = "SOURCE-MARKER",
                ProviderLabel = "PROVIDER-MARKER",
                Confidence = 42m,
            },
            T4Payload = new T4RawInput
            {
                Year = 1999,
                EmployerName = "EMPLOYER-MARKER",
                EmployerAddress = "ADDRESS-MARKER",
                EmployeeName = "EMPLOYEE-MARKER",
                EmployeeAddress = "EMPLOYEE-ADDRESS-MARKER",
                Boxes = new[]
                {
                    new T4RawBox("12", "123456789"),
                    new T4RawBox("14", "BOX-MARKER"),
                },
            },
        };

        var result = ExtractionEnvelopeValidator.Validate(envelope);

        Assert.False(result.IsSuccess);
        var planted = new[]
        {
            "DOC-MARKER-123456789", "FIRM-MARKER-987654321", "CASE-MARKER", "PARTICIPANT-MARKER",
            "SOURCE-MARKER", "PROVIDER-MARKER", "EMPLOYER-MARKER", "ADDRESS-MARKER",
            "EMPLOYEE-MARKER", "EMPLOYEE-ADDRESS-MARKER", "123456789", "987654321", "BOX-MARKER",
        };

        var outputs = result.Errors.Select(error => error.Field)
            .Concat(result.Errors.Select(error => error.Message))
            .Append(envelope.ToString())
            .Append(envelope.Provenance!.ToString())
            .Append(JsonSerializer.Serialize(envelope));

        foreach (var output in outputs)
        {
            foreach (var value in planted)
            {
                AssertOmitted(value, output);
            }
        }
    }

    [Fact]
    public void EveryPlantSlotIsActuallyReachable_SoTheMatrixCannotSilentlyStopCovering()
    {
        // Guards the matrix itself: if a slot stopped being planted (a typo in a
        // builder, say), the omission assertions would pass vacuously.
        foreach (var slot in PlantSlots)
        {
            var value = Accepted(ExtractionEnvelopeValidator.Validate(Envelope(slot, "ZZMARKERZZ")));
            Assert.Equal("ZZMARKERZZ", ReadSlot(value, slot));
        }
    }

    // ------------------------------------------------------------------ helpers

    private static ValidatedExtraction Accepted(ParseResult<ValidatedExtraction> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
        return result.Value!;
    }

    private static void AssertOmitted(string planted, string output)
    {
        Assert.DoesNotContain(planted, output, StringComparison.Ordinal);

        // Omission, not masking: no nine-digit run may survive in any form.
        Assert.DoesNotMatch(NineConsecutiveDigits, output);
    }

    private static IEnumerable<string> Outputs(ExtractionEnvelope envelope, ValidatedExtraction value)
    {
        yield return envelope.ToString();
        yield return envelope.Provenance!.ToString();
        yield return value.ToString();
        yield return JsonSerializer.Serialize(envelope);
        yield return JsonSerializer.Serialize(value);
    }

    private static ExtractionEnvelope Envelope(
        string slot,
        string planted,
        ExtractedDocumentKind? kind = ExtractedDocumentKind.T4Slip,
        int? taxYear = 2025,
        int? payloadYear = 2025,
        decimal? confidence = null,
        bool noProvenance = false,
        bool noPayload = false,
        bool emptyPayload = false,
        string? documentIdOverride = null)
    {
        string Plant(string name, string fallback) => slot == name ? planted : fallback;

        var payload = emptyPayload
            ? new T4RawInput { Year = payloadYear, Boxes = Array.Empty<T4RawBox>() }
            : new T4RawInput
            {
                Year = payloadYear,
                EmployerName = Plant("EmployerName", "Employer Ltd"),
                EmployeeName = Plant("EmployeeName", "Employee"),
                Boxes = new[]
                {
                    new T4RawBox("12", Plant("Box12Value", "value-12")),
                    new T4RawBox("14", Plant("OtherBoxValue", "value-14")),
                },
            };

        return new ExtractionEnvelope
        {
            DocumentId = documentIdOverride ?? Plant("DocumentId", "doc-1"),
            Kind = kind,
            FirmId = Plant("FirmId", "firm-1"),
            CaseId = Plant("CaseId", "case-1"),
            ParticipantId = Plant("ParticipantId", "participant-1"),
            TaxYear = taxYear,
            Provenance = noProvenance
                ? null
                : new ExtractionProvenance
                {
                    SourceReference = Plant("SourceReference", "store/ref-1"),
                    ProviderLabel = Plant("ProviderLabel", "provider"),
                    Confidence = confidence,
                },
            T4Payload = noPayload ? null : payload,
        };
    }

    private static ExtractionEnvelope RejectedEnvelope(string family, string slot, string planted) => family switch
    {
        "BlankDocumentId" => Envelope(slot, planted, documentIdOverride: "   "),
        "UndefinedKind" => Envelope(slot, planted, kind: (ExtractedDocumentKind)99),
        "UnsupportedEnvelopeYear" => Envelope(slot, planted, taxYear: 2024, payloadYear: 2024),
        "AbsentEnvelopeYear" => Envelope(slot, planted, taxYear: null),
        "AbsentPayloadYear" => Envelope(slot, planted, payloadYear: null),
        "PayloadYearMismatch" => Envelope(slot, planted, taxYear: 2025, payloadYear: 2024),
        "PayloadKindMismatch" => Envelope(slot, planted, kind: ExtractedDocumentKind.GenericEvidence),
        "MissingPayload" => Envelope(slot, planted, noPayload: true),
        "EmptyExtraction" => Envelope(slot, planted, emptyPayload: true),
        "ConfidenceOutOfRange" => Envelope(slot, planted, confidence: 7m),
        "AbsentProvenance" => Envelope(slot, planted, noProvenance: true),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown rejection family."),
    };

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
