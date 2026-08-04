using System.Text.Json;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Tests.Ingest;

namespace ExtractionWorkflow.Tests.Review;

public sealed class ReviewPiiTests
{
    [Fact]
    public async Task SinNeverAppearsInCorrectionHistoryOrPersistedReviewRecord()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.CorrectAsync(CorrectionField.T4BoxValue, "51000.00", "14");
        await harness.AcceptDocumentAsync();
        await harness.FinalizeAsync();

        var document = await harness.DocumentAsync();
        Assert.DoesNotContain(
            document.Corrections,
            correction =>
                correction.PriorValue == IngestHarness.PlantedSin ||
                correction.CorrectedValue == IngestHarness.PlantedSin);

        var json = JsonSerializer.Serialize(harness.ReviewRecords.Saved);
        Assert.DoesNotContain(IngestHarness.PlantedSin, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectionAndFinalizationDiagnosticsContainNoTaxpayerValues()
    {
        var harness = await ReviewHarness.CreateT4Async();
        var corrected = await harness.CorrectAsync(
            CorrectionField.EmployeeDisplayName, "PRIVATE-NAME-MARKER");
        await harness.AcceptDocumentAsync();
        var finalized = await harness.FinalizeAsync();

        Assert.DoesNotContain(
            "PRIVATE-NAME-MARKER", corrected.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PRIVATE-NAME-MARKER", corrected.Value!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PRIVATE-NAME-MARKER", finalized.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PRIVATE-NAME-MARKER", finalized.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistedRecordOmitsAddressesSourceReferencesAndSin()
    {
        var harness = await ReviewHarness.CreateT4Async();
        await harness.AcceptDocumentAsync();
        await harness.FinalizeAsync();

        var serialized = JsonSerializer.Serialize(Assert.Single(harness.ReviewRecords.Saved));

        Assert.DoesNotContain("12 Main Street", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("9 Bay Road", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(IngestHarness.DocumentId.ToString("D"), serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(IngestHarness.PlantedSin, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MedicalNamesNeverEnterT4ReviewPersistence()
    {
        var harness = await ReviewHarness.CreateMedicalAsync();
        await harness.AcceptDocumentAsync();
        await harness.FinalizeAsync();

        var serialized = JsonSerializer.Serialize(harness.ReviewRecords.Saved);
        Assert.DoesNotContain("Bay Street Dental", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Casey Taxpayer", serialized, StringComparison.Ordinal);
    }
}
