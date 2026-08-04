using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Tests.Ingest;

/// <summary>
/// Spec 015 T04 — probable duplicates are flagged, never resolved; compared
/// within one case only; and never compared on anything derived from the SIN.
/// </summary>
public sealed class DuplicateDetectorTests
{
    private static readonly DateTimeOffset Instant = IngestHarness.Instant;

    private static readonly Guid FirstDocument = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SecondDocument = new("aaaaaaaa-0000-0000-0000-000000000002");

    // ------------------------------------------------------------- T4

    [Fact]
    public void TwoScansOfTheSameEmployeeCopyAreProbableDuplicatesWithoutBoxFiftyFour()
    {
        var first = T4Version(FirstDocument, ("14", "50000.00"), ("45", "1"));
        var second = T4Version(SecondDocument, ("14", "50000.00"), ("45", "1"));

        var candidate = DuplicateCandidateDetector.FindT4Candidate(
            SecondDocument, second, CaseWith(first));

        Assert.Equal(FirstDocument, candidate);

        // Box 54 is absent from both, and its absence neither prevented the
        // comparison nor was required for it.
        Assert.DoesNotContain(first.BoxValues, box => box.Code == "54");
        Assert.DoesNotContain(second.BoxValues, box => box.Code == "54");
    }

    [Fact]
    public void ARescanWithDifferentSpacingAndCasingIsStillAProbableDuplicate()
    {
        var first = T4Version(
            FirstDocument,
            employerName: "Acme Manufacturing",
            employeeName: "Casey Taxpayer",
            boxes: new[] { ("14", "50000.00") });

        var second = T4Version(
            SecondDocument,
            employerName: "  ACME   MANUFACTURING ",
            employeeName: "casey taxpayer",
            boxes: new[] { ("14", " 50000.00 ") });

        Assert.Equal(
            FirstDocument,
            DuplicateCandidateDetector.FindT4Candidate(SecondDocument, second, CaseWith(first)));
    }

    [Fact]
    public void TheBoxOrderTwoScansHappenedToBeReadInDoesNotMakeThemDiffer()
    {
        var first = T4Version(FirstDocument, ("14", "50000.00"), ("45", "1"));
        var second = T4Version(SecondDocument, ("45", "1"), ("14", "50000.00"));

        Assert.Equal(
            FirstDocument,
            DuplicateCandidateDetector.FindT4Candidate(SecondDocument, second, CaseWith(first)));
    }

    [Fact]
    public void TwoDistinctSameEmployerSlipsAreNotDuplicates()
    {
        // Same taxpayer, same employer, genuinely different income. Treating
        // these as duplicates would lose a slip's worth of income.
        var first = T4Version(FirstDocument, ("14", "50000.00"), ("45", "1"));
        var second = T4Version(SecondDocument, ("14", "12500.00"), ("45", "1"));

        Assert.Null(
            DuplicateCandidateDetector.FindT4Candidate(SecondDocument, second, CaseWith(first)));
    }

    [Fact]
    public void ASlipWithAnExtraBoxIsNotADuplicateOfOneWithout()
    {
        var first = T4Version(FirstDocument, ("14", "50000.00"), ("45", "1"));
        var second = T4Version(
            SecondDocument, ("14", "50000.00"), ("45", "1"), ("22", "9000.00"));

        Assert.Null(
            DuplicateCandidateDetector.FindT4Candidate(SecondDocument, second, CaseWith(first)));
    }

    [Fact]
    public void ADocumentIsNeverADuplicateCandidateOfItself()
    {
        var version = T4Version(FirstDocument, ("14", "50000.00"));

        Assert.Null(
            DuplicateCandidateDetector.FindT4Candidate(
                FirstDocument, version, CaseWith(version)));
    }

    [Fact]
    public void TheComparisonNeverUsesTheSocialInsuranceNumberBox()
    {
        // Two documents identical except Box 12, and two differing only in
        // Box 12, are the same question — because the box cannot be represented
        // on a version at all, so neither pair can differ by it.
        Assert.False(ExtractionBoxValue.TryCreate(
            T4BoxCodes.SocialInsuranceNumber, "123456789", out _));

        var first = T4Version(FirstDocument, ("14", "50000.00"));
        var second = T4Version(SecondDocument, ("14", "50000.00"));

        Assert.Equal(
            FirstDocument,
            DuplicateCandidateDetector.FindT4Candidate(SecondDocument, second, CaseWith(first)));
    }

    [Fact]
    public void OnlyTheLatestVersionOfAnAlreadyExtractedDocumentIsCompared()
    {
        var superseded = T4Version(FirstDocument, ("14", "50000.00"));
        var current = T4Version(FirstDocument, version: 2, boxes: new[] { ("14", "12500.00") });

        var candidate = T4Version(SecondDocument, ("14", "12500.00"));

        Assert.Equal(
            FirstDocument,
            DuplicateCandidateDetector.FindT4Candidate(
                SecondDocument,
                candidate,
                new StoredCaseExtractions(
                    IngestHarness.CaseId,
                    new[] { Document(FirstDocument, new[] { superseded, current }) },
                    Array.Empty<ExtractionAcceptanceRecord>(),
                    Array.Empty<ExtractionAuditEventRecord>())));
    }

    // -------------------------------------------------------- medical

    [Fact]
    public void TwoIdenticalReceiptsAreProbableDuplicates()
    {
        var first = MedicalVersion(FirstDocument);
        var second = MedicalVersion(SecondDocument);

        Assert.Equal(
            FirstDocument,
            DuplicateCandidateDetector.FindMedicalCandidate(
                SecondDocument, second, MedicalCaseWith(first)));
    }

    [Fact]
    public void TwoReceiptsDifferingInAmountAreNot()
    {
        var first = MedicalVersion(FirstDocument);
        var second = MedicalVersion(SecondDocument, grossAmount: "$99.00");

        Assert.Null(
            DuplicateCandidateDetector.FindMedicalCandidate(
                SecondDocument, second, MedicalCaseWith(first)));
    }

    [Fact]
    public void AnAbsentOptionalFieldMatchesOnlyAnotherAbsentOne()
    {
        var withReference = MedicalVersion(FirstDocument);
        var withoutReference = MedicalVersion(SecondDocument, receiptReference: null);

        Assert.Null(
            DuplicateCandidateDetector.FindMedicalCandidate(
                SecondDocument, withoutReference, MedicalCaseWith(withReference)));

        var alsoWithout = MedicalVersion(FirstDocument, receiptReference: null);

        Assert.Equal(
            FirstDocument,
            DuplicateCandidateDetector.FindMedicalCandidate(
                SecondDocument, withoutReference, MedicalCaseWith(alsoWithout)));
    }

    [Fact]
    public void AnUnknownCurrencyIsNotTreatedAsAnyParticularCurrency()
    {
        var declared = MedicalVersion(FirstDocument);
        var undeclared = MedicalVersion(SecondDocument, currency: null);

        Assert.Null(
            DuplicateCandidateDetector.FindMedicalCandidate(
                SecondDocument, undeclared, MedicalCaseWith(declared)));
    }

    // ------------------------------------------------- flag, never resolve

    [Fact]
    public async Task ASecondScanIsFlaggedAndNeitherDocumentIsDiscarded()
    {
        var harness = IngestHarness.Create();

        // A document already extracted in the same case, with the same content.
        harness.Store.AddVersion(T4Version(
            FirstDocument,
            employerName: "Acme Manufacturing",
            employeeName: "Casey Taxpayer",
            boxes: new[] { ("14", "50000.00"), ("45", "1") }));

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);

        var candidate = Assert.Single(harness.Store.DuplicateCandidates);
        Assert.Equal(IngestHarness.DocumentId, candidate.DocumentId);
        Assert.Equal(FirstDocument, candidate.ComparedDocumentId);
        Assert.Equal(IngestHarness.Instant, candidate.DetectedAtUtc);

        // Both remain, and neither is accepted, merged, or excluded.
        Assert.Equal(2, harness.Store.Versions.Count);
        Assert.True(result.Value!.Document.HasUnresolvedDuplicateCandidate);
        Assert.Equal(
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            result.Value.Document.AcceptanceState);

        Assert.Contains(
            harness.Store.AuditEvents,
            auditEvent => auditEvent.Kind == ExtractionAuditKind.DuplicateFlagged);
    }

    [Fact]
    public async Task ADistinctSecondSlipIsNotFlagged()
    {
        var harness = IngestHarness.Create();

        harness.Store.AddVersion(T4Version(
            FirstDocument,
            employerName: "Acme Manufacturing",
            employeeName: "Casey Taxpayer",
            boxes: new[] { ("14", "12500.00"), ("45", "1") }));

        var result = await harness.ExtractAsync();

        Assert.Equal(ExtractionResultCode.Success, result.Code);
        Assert.Empty(harness.Store.DuplicateCandidates);
        Assert.False(result.Value!.Document.HasUnresolvedDuplicateCandidate);
    }

    [Fact]
    public void ADeterminationCarriesOnlyOpaqueIdentifiers()
    {
        var record = new DuplicateCandidateRecord(
            Guid.NewGuid(), FirstDocument, SecondDocument, Instant);

        // No taxpayer-data-derived value of any kind: nothing here could be used
        // to test whether two taxpayers' documents match.
        Assert.Equal(
            new[] { "CandidateId", "ComparedDocumentId", "DetectedAtUtc", "DocumentId" },
            record.GetType()
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        Assert.Equal(nameof(DuplicateCandidateRecord), record.ToString());
    }

    // ------------------------------------------------------------- helpers

    private static StoredCaseExtractions CaseWith(ExtractionVersion version) =>
        new(
            IngestHarness.CaseId,
            new[] { Document(version.DocumentId, new[] { version }) },
            Array.Empty<ExtractionAcceptanceRecord>(),
            Array.Empty<ExtractionAuditEventRecord>());

    private static StoredCaseExtractions MedicalCaseWith(MedicalEvidenceVersion version) =>
        new(
            IngestHarness.CaseId,
            new[]
            {
                new StoredDocumentExtraction(
                    version.DocumentId,
                    IngestHarness.ParticipantId,
                    Array.Empty<ExtractionVersion>(),
                    new[] { version },
                    Array.Empty<ExtractionCorrectionRecord>(),
                    Array.Empty<ClassificationOverrideRecord>(),
                    Array.Empty<DuplicateCandidateRecord>(),
                    Array.Empty<DuplicateResolutionRecord>(),
                    Array.Empty<ExtractionAcceptanceRecord>()),
            },
            Array.Empty<ExtractionAcceptanceRecord>(),
            Array.Empty<ExtractionAuditEventRecord>());

    private static StoredDocumentExtraction Document(
        Guid documentId,
        IReadOnlyList<ExtractionVersion> versions) =>
        new(
            documentId,
            IngestHarness.ParticipantId,
            versions,
            Array.Empty<MedicalEvidenceVersion>(),
            Array.Empty<ExtractionCorrectionRecord>(),
            Array.Empty<ClassificationOverrideRecord>(),
            Array.Empty<DuplicateCandidateRecord>(),
            Array.Empty<DuplicateResolutionRecord>(),
            Array.Empty<ExtractionAcceptanceRecord>());

    private static ExtractionVersion T4Version(
        Guid documentId,
        params (string Code, string Value)[] boxValues) =>
        T4Version(documentId, boxes: boxValues);

    private static ExtractionVersion T4Version(
        Guid documentId,
        int version = 1,
        string? employerName = "Acme Manufacturing",
        string? employeeName = "Casey Taxpayer",
        IReadOnlyList<(string Code, string Value)>? boxes = null)
    {
        Assert.True(ClassificationOutcome.TryAutomated(
            DocumentClassification.T4Slip, out var classification));

        var values = new List<ExtractionBoxValue>();

        foreach (var (code, value) in boxes ?? Array.Empty<(string, string)>())
        {
            Assert.True(ExtractionBoxValue.TryCreate(code, value, out var boxValue));
            values.Add(boxValue!);
        }

        return new ExtractionVersion(
            Guid.NewGuid(),
            documentId,
            version,
            classification!,
            provenance: null,
            employerName,
            employeeName,
            employerAddress: null,
            employeeAddress: null,
            values,
            Array.Empty<FieldValidationOutcome>(),
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            precedingVersionId: null,
            Instant);
    }

    private static MedicalEvidenceVersion MedicalVersion(
        Guid documentId,
        string? serviceDate = "2025-03-14",
        string? grossAmount = "$1,120.50",
        string? currency = "CAD",
        string? providerName = "Bay Street Dental",
        string? patientName = "Casey Taxpayer",
        string? receiptReference = "R-4471") =>
        new(
            Guid.NewGuid(),
            documentId,
            1,
            serviceDate,
            grossAmount,
            CurrencyDeclaration.FromPrintedCode(currency),
            providerName,
            patientName,
            receiptReference,
            provenance: null,
            Array.Empty<FieldValidationOutcome>(),
            ExtractionAcceptanceState.AwaitingAccountantDecision,
            precedingVersionId: null,
            Instant);
}
