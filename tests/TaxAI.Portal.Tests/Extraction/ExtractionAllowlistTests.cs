using System.Reflection;
using System.Text.Json;
using ClientIntake.Models;
using ExtractionWorkflow.Models;
using TaxAI.Portal.Components.Pages;
using TaxCases.Models;

namespace TaxAI.Portal.Tests.Extraction;

public sealed class ExtractionAllowlistTests
{
    [Theory]
    [MemberData(nameof(ClosedBrowserModels))]
    public void BrowserModelsHaveExactlyTheirApprovedMembers(
        Type model,
        string[] approvedMembers)
    {
        var actual = model
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            approvedMembers.OrderBy(name => name, StringComparer.Ordinal),
            actual);
    }

    public static TheoryData<Type, string[]> ClosedBrowserModels() => new()
    {
        {
            typeof(ExtractionListPageModel),
            [
                "CaseId",
                "Documents",
                "IsFinalized",
                "TaxYear",
            ]
        },
        {
            typeof(ExtractionListItemModel),
            [
                "AcceptanceState",
                "AutomatedClassification",
                "DeclaredCategory",
                "DeclaredOrdinal",
                "DocumentId",
                "EffectiveClassification",
                "ExtractionVersionCount",
                "HasUnresolvedDuplicateCandidate",
                "MedicalEvidenceVersionCount",
            ]
        },
        {
            typeof(ExtractionReviewPageModel),
            [
                "AcceptanceState",
                "AutomatedClassification",
                "Confidence",
                "CorrectionCount",
                "DeclaredCategory",
                "DeclaredOrdinal",
                "DuplicateCandidates",
                "EffectiveClassification",
                "FieldOutcomes",
                "IsClassificationOverridden",
                "LatestVersion",
                "Medical",
                "ProviderLabel",
                "T4",
            ]
        },
        {
            typeof(ExtractionFieldRowModel),
            [
                "Field",
                "Outcome",
            ]
        },
        {
            typeof(T4ExtractionPanelModel),
            [
                "Boxes",
                "EmployeeAddress",
                "EmployeeDisplayName",
                "EmployerAddress",
                "EmployerDisplayName",
            ]
        },
        {
            typeof(T4BoxDisplayModel),
            [
                "Code",
                "Value",
            ]
        },
        {
            typeof(MedicalEvidencePanelModel),
            [
                "CurrencyAsPrinted",
                "GrossAmountAsPrinted",
                "PatientDisplayName",
                "ProviderDisplayName",
                "ReceiptReference",
                "ServiceDateAsPrinted",
            ]
        },
        {
            typeof(DuplicateCandidateModel),
            [
                "CandidateId",
            ]
        },
    };

    [Fact]
    public void FullyPopulatedMedicalProjectionEmitsOnlyTheClosedAllowlist()
    {
        const string providerMarker = "PROVIDER-MARKER-015-T06";
        const string patientMarker = "PATIENT-MARKER-015-T06";
        var documentId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var candidateId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        Assert.True(
            FieldValidationOutcome.TryCreate(
                ExtractionWorkflowFields.MedicalServiceDate,
                FieldOutcomeKind.Extracted,
                out var outcome));

        var medical = new MedicalEvidenceVersion(
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            documentId,
            version: 3,
            serviceDateAsPrinted: "2026-07-02",
            grossAmountAsPrinted: "$123.45",
            CurrencyDeclaration.FromPrintedCode("CAD"),
            providerMarker,
            patientMarker,
            receiptReference: "RECEIPT-MARKER-015-T06",
            StoredExtractionProvenance.FromStorage(
                "Deterministic",
                confidence: 0.875m),
            [outcome!],
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            precedingVersionId:
                Guid.Parse("44444444-4444-4444-8444-444444444444"),
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        var stored = new StoredDocumentExtraction(
            documentId,
            Guid.Parse("55555555-5555-4555-8555-555555555555"),
            [],
            [medical],
            [],
            [],
            [
                new DuplicateCandidateRecord(
                    candidateId,
                    documentId,
                    Guid.Parse("66666666-6666-4666-8666-666666666666"),
                    new DateTimeOffset(2026, 8, 5, 11, 0, 0, TimeSpan.Zero)),
            ],
            [],
            []);

        var model = Project(
            new IntakeExpectedItemRecord(
                Guid.Parse("77777777-7777-4777-8777-777777777777"),
                DocumentCategory.MedicalExpenseEvidence,
                Ordinal: 2),
            stored);
        var json = JsonSerializer.Serialize(model);

        Assert.Contains(providerMarker, json, StringComparison.Ordinal);
        Assert.Contains(patientMarker, json, StringComparison.Ordinal);
        Assert.Contains("RECEIPT-MARKER-015-T06", json, StringComparison.Ordinal);
        Assert.Contains("2026-07-02", json, StringComparison.Ordinal);
        Assert.Contains("$123.45", json, StringComparison.Ordinal);
        Assert.Contains("CAD", json, StringComparison.Ordinal);
        Assert.DoesNotContain(documentId.ToString("D"), json, StringComparison.Ordinal);
        Assert.DoesNotContain(candidateId.ToString("D"), json, StringComparison.Ordinal);
        AssertForbiddenMembersAreAbsent(json);
    }

    [Fact]
    public void FullyPopulatedT4ProjectionCannotCarryBoxTwelve()
    {
        const string excludedSinMarker = "123456789";
        Assert.False(
            ExtractionBoxValue.TryCreate(
                "12",
                excludedSinMarker,
                out _));
        Assert.True(
            ExtractionBoxValue.TryCreate(
                "14",
                "4321.09",
                out var employmentIncome));
        Assert.True(
            ClassificationOutcome.TryAutomated(
                DocumentClassification.T4Slip,
                out var classification));
        Assert.True(
            FieldValidationOutcome.TryCreate(
                ExtractionWorkflowFields.PayloadBoxes,
                FieldOutcomeKind.Extracted,
                out var outcome));

        var documentId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        var version = new ExtractionVersion(
            Guid.Parse("99999999-9999-4999-8999-999999999999"),
            documentId,
            version: 1,
            classification!,
            StoredExtractionProvenance.FromStorage(
                "Deterministic",
                confidence: 0.75m),
            employerDisplayName: "Employer marker",
            employeeDisplayName: "Employee marker",
            employerAddress: "Employer address marker",
            employeeAddress: "Employee address marker",
            [employmentIncome!],
            [outcome!],
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            precedingVersionId: null,
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));
        var stored = new StoredDocumentExtraction(
            documentId,
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            [version],
            [],
            [],
            [],
            [],
            [],
            []);

        var json = JsonSerializer.Serialize(
            Project(
                new IntakeExpectedItemRecord(
                    Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
                    DocumentCategory.T4Slip,
                    Ordinal: 1),
                stored));

        Assert.Contains("Employer marker", json, StringComparison.Ordinal);
        Assert.Contains("4321.09", json, StringComparison.Ordinal);
        Assert.DoesNotContain(excludedSinMarker, json, StringComparison.Ordinal);
        AssertForbiddenMembersAreAbsent(json);
    }

    [Fact]
    public void CorrectionVocabularyIsClosedAndHasNoSinMember()
    {
        Assert.Equal(
            new[]
            {
                "AddT4Box",
                "EmployeeAddress",
                "EmployeeDisplayName",
                "EmployerAddress",
                "EmployerDisplayName",
                "MedicalCurrency",
                "MedicalGrossAmount",
                "MedicalPatientDisplayName",
                "MedicalProviderDisplayName",
                "MedicalReceiptReference",
                "MedicalServiceDate",
                "RemoveT4Box",
                "T4BoxCode",
                "T4BoxValue",
            },
            Enum.GetNames<CorrectionField>()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    private static ExtractionReviewPageModel Project(
        IntakeExpectedItemRecord expected,
        StoredDocumentExtraction stored)
    {
        var endpoints = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.ExtractionEndpoints",
            throwOnError: true)!;
        var method = endpoints.GetMethod(
            "ProjectReview",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new MissingMethodException(endpoints.FullName, "ProjectReview");

        return Assert.IsType<ExtractionReviewPageModel>(
            method.Invoke(null, [expected, stored]));
    }

    private static void AssertForbiddenMembersAreAbsent(string json)
    {
        foreach (var forbidden in new[]
                 {
                     "ContentKey",
                     "Fingerprint",
                     "FirmId",
                     "TenantId",
                     "SessionId",
                     "SourceReference",
                     "RawProviderPayload",
                     "FileName",
                     "Diagnosis",
                     "Medication",
                     "ServiceDescription",
                     "HealthCard",
                     "Insurance",
                     "SocialInsurance",
                     "Sin",
                 })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
