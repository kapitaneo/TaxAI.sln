using System.Reflection;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;

namespace ExtractionWorkflow.Tests.Ingest;

/// <summary>
/// Spec 015 T04 — medical validation is structural. It says what the receipt
/// showed, and it says nothing about tax.
/// </summary>
public sealed class MedicalEvidenceValidatorTests
{
    [Fact]
    public void EveryApprovedFieldGetsExactlyOneOutcomeInAFixedOrder()
    {
        var outcomes = MedicalEvidenceValidator.Validate(IngestHarness.ValidMedicalResult());

        Assert.Equal(
            new[]
            {
                ExtractionWorkflowFields.MedicalServiceDate,
                ExtractionWorkflowFields.MedicalGrossAmount,
                ExtractionWorkflowFields.MedicalCurrency,
                ExtractionWorkflowFields.MedicalProviderDisplayName,
                ExtractionWorkflowFields.MedicalPatientDisplayName,
                ExtractionWorkflowFields.MedicalReceiptReference,
            },
            outcomes.Select(outcome => outcome.Field).ToArray());

        Assert.All(
            outcomes,
            outcome => Assert.Equal(FieldOutcomeKind.Extracted, outcome.Kind));
    }

    [Fact]
    public void TheSameProviderResultAlwaysYieldsTheSameSequence()
    {
        var first = MedicalEvidenceValidator.Validate(IngestHarness.ValidMedicalResult());
        var second = MedicalEvidenceValidator.Validate(IngestHarness.ValidMedicalResult());

        Assert.Equal(
            first.Select(outcome => (outcome.Field, outcome.Kind)).ToArray(),
            second.Select(outcome => (outcome.Field, outcome.Kind)).ToArray());
    }

    [Fact]
    public void AnAbsentProviderResultSurfacesEveryFieldAsMissingRatherThanThrowing()
    {
        var outcomes = MedicalEvidenceValidator.Validate(null);

        Assert.Equal(6, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.Equal(FieldOutcomeKind.Missing, outcome.Kind));
        Assert.True(MedicalEvidenceValidator.BlocksAcceptance(outcomes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnextractableDateBlocksAcceptanceAndIsNotInvented(string? printed)
    {
        var outcomes = MedicalEvidenceValidator.Validate(
            IngestHarness.ValidMedicalResult(serviceDate: printed));

        Assert.True(MedicalEvidenceValidator.BlocksAcceptance(outcomes));
        Assert.Equal(
            FieldOutcomeKind.Missing,
            Outcome(outcomes, ExtractionWorkflowFields.MedicalServiceDate).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnextractableAmountBlocksAcceptanceAndIsNotInvented(string? printed)
    {
        var outcomes = MedicalEvidenceValidator.Validate(
            IngestHarness.ValidMedicalResult(grossAmount: printed));

        Assert.True(MedicalEvidenceValidator.BlocksAcceptance(outcomes));
        Assert.Equal(
            FieldOutcomeKind.Missing,
            Outcome(outcomes, ExtractionWorkflowFields.MedicalGrossAmount).Kind);
    }

    [Fact]
    public void MissingOptionalFieldsAreSurfacedWithoutBlockingAndWithoutJudgement()
    {
        var outcomes = MedicalEvidenceValidator.Validate(
            IngestHarness.ValidMedicalResult(
                currency: null,
                providerName: null,
                patientName: null,
                receiptReference: null));

        Assert.False(MedicalEvidenceValidator.BlocksAcceptance(outcomes));

        foreach (var field in new[]
                 {
                     ExtractionWorkflowFields.MedicalCurrency,
                     ExtractionWorkflowFields.MedicalProviderDisplayName,
                     ExtractionWorkflowFields.MedicalPatientDisplayName,
                     ExtractionWorkflowFields.MedicalReceiptReference,
                 })
        {
            Assert.Equal(FieldOutcomeKind.Missing, Outcome(outcomes, field).Kind);
        }
    }

    [Fact]
    public void AnOutcomeSetThatOmitsARequiredFieldIsTreatedAsBlocking()
    {
        // An outcome nobody produced is not an outcome this may treat as
        // satisfied. Failing open here would be the one way a version with no
        // amount could reach acceptance.
        Assert.True(
            MedicalEvidenceValidator.BlocksAcceptance(Array.Empty<FieldValidationOutcome>()));
    }

    [Fact]
    public void TheValidatorRefusesAnAbsentOutcomeCollection()
    {
        Assert.Throws<ArgumentNullException>(
            () => MedicalEvidenceValidator.BlocksAcceptance(null!));
    }

    [Fact]
    public void NoMemberOfTheValidatorAssertsAnythingAboutTax()
    {
        var names = typeof(MedicalEvidenceValidator)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToArray();

        Assert.NotEmpty(names);

        foreach (var forbidden in new[]
                 {
                     "Eligib", "Deduct", "Claim", "Reimburse", "CraLine", "Threshold",
                     "Total", "Sum", "Aggregate", "Dependant", "Period",
                 })
        {
            Assert.DoesNotContain(
                names,
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    // -------------------------------------------- the medical ingest path

    [Fact]
    public async Task AReceiptIsRecordedAsANeutralEvidenceVersionWithNothingNormalized()
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.MedicalExpenseEvidence;
        harness.Provider.MedicalResult = IngestHarness.ValidMedicalResult(
            serviceDate: "14 mars 2025", grossAmount: "$1,120.50 CAD");

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.Equal(ExtractionRecordedKind.MedicalEvidenceRecorded, result.Value!.Recorded);

        var stored = Assert.Single(harness.Store.MedicalVersions);
        Assert.Equal("14 mars 2025", stored.ServiceDateAsPrinted);
        Assert.Equal("$1,120.50 CAD", stored.GrossAmountAsPrinted);
        Assert.True(stored.Currency.IsKnown);
        Assert.Equal("CAD", stored.Currency.CodeAsPrinted);

        // Nothing about the T4 chain ran, and no T4 version exists.
        Assert.Empty(harness.Store.Versions);
        Assert.Equal(0, harness.Ingress.SubmitCount);
    }

    [Fact]
    public async Task AnUndeclaredCurrencyIsRecordedAsExplicitlyUnknown()
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.MedicalExpenseEvidence;
        harness.Provider.MedicalResult = IngestHarness.ValidMedicalResult(currency: null);

        await harness.ExtractAsync();

        var stored = Assert.Single(harness.Store.MedicalVersions);
        Assert.False(stored.Currency.IsKnown);
        Assert.Null(stored.Currency.CodeAsPrinted);
    }

    [Fact]
    public async Task ASecondMedicalExtractionAppendsAndLeavesTheFirstUnchanged()
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.MedicalExpenseEvidence;

        await harness.ExtractAsync();
        var first = harness.Store.MedicalVersions[0];

        await harness.ExtractAsync();

        Assert.Equal(2, harness.Store.MedicalVersions.Count);
        Assert.Same(first, harness.Store.MedicalVersions[0]);
        Assert.Equal(1, harness.Store.MedicalVersions[0].Version);
        Assert.Equal(2, harness.Store.MedicalVersions[1].Version);
        Assert.Equal(
            first.MedicalEvidenceVersionId,
            harness.Store.MedicalVersions[1].PrecedingVersionId);
    }

    [Fact]
    public async Task AMedicalExtractionRecordsItsOwnAuditKind()
    {
        var harness = IngestHarness.Create();
        harness.Provider.Classification = DocumentClassification.MedicalExpenseEvidence;

        await harness.ExtractAsync();

        Assert.Equal(
            ExtractionAuditKind.MedicalEvidenceRecorded,
            Assert.Single(harness.Store.AuditEvents).Kind);
    }

    private static FieldValidationOutcome Outcome(
        IReadOnlyList<FieldValidationOutcome> outcomes,
        string field) =>
        outcomes.Single(outcome => outcome.Field == field);
}
