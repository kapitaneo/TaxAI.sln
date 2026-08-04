using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExtractionWorkflow.Models;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Tests.Review;

public sealed class CorrectionVocabularyTests
{
    [Fact]
    public void CorrectionVocabularyIsExactlyTheApprovedSet()
    {
        Assert.Equal(
            new[]
            {
                "T4BoxValue",
                "T4BoxCode",
                "AddT4Box",
                "RemoveT4Box",
                "EmployerDisplayName",
                "EmployeeDisplayName",
                "EmployerAddress",
                "EmployeeAddress",
                "MedicalServiceDate",
                "MedicalGrossAmount",
                "MedicalCurrency",
                "MedicalProviderDisplayName",
                "MedicalPatientDisplayName",
                "MedicalReceiptReference",
            },
            Enum.GetNames<CorrectionField>());
    }

    [Theory]
    [InlineData("TaxYear")]
    [InlineData("FirmId")]
    [InlineData("CaseParticipant")]
    [InlineData("DocumentContentKey")]
    [InlineData("CraLine")]
    [InlineData("ValidationResult")]
    [InlineData("SocialInsuranceNumber")]
    public void ProhibitedTargetsHaveNoCorrectionField(string prohibited)
    {
        Assert.DoesNotContain(
            Enum.GetNames<CorrectionField>(),
            name => name.Contains(prohibited, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CorrectionCommandCarriesOnlySelectorsFieldBoxAndReplacement()
    {
        Assert.Equal(
            new[] { "BoxCode", "CaseId", "CorrectedValue", "DocumentId", "Field" },
            typeof(ApplyExtractionCorrectionCommand)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray());
    }

    [Theory]
    [InlineData(nameof(ApplyExtractionCorrectionCommand.CaseId))]
    [InlineData(nameof(ApplyExtractionCorrectionCommand.DocumentId))]
    [InlineData(nameof(ApplyExtractionCorrectionCommand.BoxCode))]
    [InlineData(nameof(ApplyExtractionCorrectionCommand.CorrectedValue))]
    public void CorrectionSelectorsAndValuesAreNotSerializable(string propertyName)
    {
        var property = typeof(ApplyExtractionCorrectionCommand).GetProperty(propertyName)!;
        Assert.NotNull(property.GetCustomAttribute<JsonIgnoreAttribute>());
    }

    [Fact]
    public void CorrectionRecordRefusesBoxTwelveAtConstruction()
    {
        Assert.Throws<ArgumentException>(
            () => new ExtractionCorrectionRecord(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CorrectionField.T4BoxValue,
                T4BoxCodes.SocialInsuranceNumber,
                "prior",
                "corrected",
                ExtractionActorKind.Accountant,
                Guid.NewGuid(),
                Ingest.IngestHarness.Instant,
                Guid.NewGuid()));
    }

    [Fact]
    public void CommandSerializationAndDiagnosticsOmitAllSuppliedValues()
    {
        var marker = "PRIVATE-CORRECTION-MARKER";
        var command = new ApplyExtractionCorrectionCommand
        {
            CaseId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Field = CorrectionField.T4BoxValue,
            BoxCode = "14",
            CorrectedValue = marker,
        };

        Assert.DoesNotContain(marker, JsonSerializer.Serialize(command), StringComparison.Ordinal);
        Assert.DoesNotContain(marker, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("14", command.ToString(), StringComparison.Ordinal);
    }
}
