using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.Models;

public sealed class TaxCaseEvidenceTests
{
    private const string CaseId = "case-1";
    private const string FirmA = "firm-a";
    private const string PrimaryId = "p-1";
    private const string SpouseId = "p-2";
    private const int CaseYear = 2025;

    private static TaxCase NewCase()
    {
        var created = TaxCase.Create(new CreateTaxCaseRequest(
            CaseId,
            FirmA,
            CaseYear,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            CorrelationReference: null));

        Assert.True(created.IsSuccess);

        var withSpouse = created.Value!.Case.AddParticipant(
            new ParticipantDescriptor(SpouseId, RelationshipKind.SpouseOrCommonLaw, null, null, null),
            correlationReference: null);

        Assert.True(withSpouse.IsSuccess);
        return withSpouse.Value!.Case;
    }

    private static EvidenceDescriptor Descriptor(
        string? evidenceId = "e-1",
        string? category = "T4Slip",
        string? participantId = PrimaryId,
        int? taxYear = CaseYear,
        string? sourceReference = null,
        string? firmId = null) =>
        new(evidenceId, category, participantId, taxYear, sourceReference, firmId);

    private static TaxCase Attached(TaxCase taxCase, EvidenceDescriptor descriptor)
    {
        var result = taxCase.AttachEvidence(descriptor, correlationReference: null);
        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static void AssertFailsClosedAndUnchanged(
        TaxCase original,
        ParseResult<CaseOperationOutcome> result,
        string expectedField)
    {
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, error => error.Field == expectedField);

        // The original case is untouched: no evidence, no year drift (AC-33/39/42).
        Assert.Empty(original.Evidence);
        Assert.Equal(CaseYear, original.TaxYear);
        Assert.Equal(CaseStatus.Intake, original.Status);
    }

    // --- Happy path (AC-9, AC-10, AC-18–AC-21) ---

    [Fact]
    public void AttachEvidence_TwoDistinctT4sForOneParticipant_IdentitySeparateFromCategory()
    {
        var taxCase = NewCase();
        taxCase = Attached(taxCase, Descriptor("t4-first"));
        taxCase = Attached(taxCase, Descriptor("t4-second"));

        Assert.Equal(2, taxCase.Evidence.Count);

        var first = Assert.Single(taxCase.Evidence, e => e.EvidenceId == "t4-first");
        var second = Assert.Single(taxCase.Evidence, e => e.EvidenceId == "t4-second");
        Assert.Equal(DocumentCategory.T4Slip, first.Category);
        Assert.Equal(DocumentCategory.T4Slip, second.Category);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AttachEvidence_TwoDistinctMedicalRecords_BothRetrievableByIdentity()
    {
        var taxCase = NewCase();
        taxCase = Attached(taxCase, Descriptor("med-1", "MedicalExpenseEvidence"));
        taxCase = Attached(taxCase, Descriptor("med-2", "MedicalExpenseEvidence"));

        Assert.Equal(2, taxCase.Evidence.Count);
        Assert.Single(taxCase.Evidence, e => e.EvidenceId == "med-1");
        Assert.Single(taxCase.Evidence, e => e.EvidenceId == "med-2");
        Assert.All(taxCase.Evidence, e => Assert.Equal(DocumentCategory.MedicalExpenseEvidence, e.Category));
    }

    [Fact]
    public void AttachEvidence_RetainsAttributionYearAndSourceReference()
    {
        var taxCase = Attached(NewCase(), Descriptor(
            "e-9",
            "Other",
            participantId: SpouseId,
            sourceReference: "upload-batch-7"));

        var evidence = Assert.Single(taxCase.Evidence);
        Assert.Equal("e-9", evidence.EvidenceId);
        Assert.Equal(DocumentCategory.Other, evidence.Category);
        Assert.Equal(SpouseId, evidence.ParticipantId);
        Assert.Equal(CaseYear, evidence.TaxYear);
        Assert.Equal("upload-batch-7", evidence.SourceReference);
    }

    [Fact]
    public void AttachEvidence_AbsentSourceReference_StoresNull()
    {
        var taxCase = Attached(NewCase(), Descriptor(sourceReference: null));

        Assert.Null(Assert.Single(taxCase.Evidence).SourceReference);
    }

    [Fact]
    public void AttachEvidence_PreservesInsertionOrder()
    {
        var taxCase = NewCase();
        taxCase = Attached(taxCase, Descriptor("e-1"));
        taxCase = Attached(taxCase, Descriptor("e-2", "MedicalExpenseEvidence", SpouseId));
        taxCase = Attached(taxCase, Descriptor("e-3", "Other"));

        Assert.Equal(
            new[] { "e-1", "e-2", "e-3" },
            taxCase.Evidence.Select(e => e.EvidenceId).ToArray());
    }

    [Fact]
    public void AttachEvidence_MatchingDescriptorFirm_Succeeds()
    {
        var taxCase = Attached(NewCase(), Descriptor(firmId: FirmA));

        Assert.Single(taxCase.Evidence);
        Assert.Equal(FirmA, taxCase.FirmId);
    }

    // --- Change record and immutability (AC-32/33 evidence arms) ---

    [Fact]
    public void AttachEvidence_EmitsExactlyOneDeterministicEvidenceAttachedRecord()
    {
        var result = NewCase().AttachEvidence(Descriptor(), "corr-4");

        Assert.True(result.IsSuccess);
        var expected = new CaseChangeRecord(
            CaseId,
            CaseChangeKind.EvidenceAttached,
            PrimaryId,
            EvidenceId: "e-1",
            QuestionId: null,
            PreviousStatus: null,
            ResultingStatus: null,
            CorrelationReference: "corr-4",
            Detail: null);

        Assert.Equal(expected, result.Value!.Change);
    }

    [Fact]
    public void AttachEvidence_Success_LeavesOriginalCaseUnchanged()
    {
        var original = NewCase();

        var result = original.AttachEvidence(Descriptor(), correlationReference: null);

        Assert.True(result.IsSuccess);
        Assert.NotSame(original, result.Value!.Case);
        Assert.Empty(original.Evidence);
        Assert.Single(result.Value.Case.Evidence);
    }

    [Fact]
    public void AttachEvidence_SameInputTwice_YieldsEqualRecordsAndEqualEvidence()
    {
        var first = NewCase().AttachEvidence(Descriptor(sourceReference: "src-1"), "corr-4");
        var second = NewCase().AttachEvidence(Descriptor(sourceReference: "src-1"), "corr-4");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Change, second.Value!.Change);
        Assert.Equal(first.Value.Case.Evidence, second.Value.Case.Evidence);
    }

    // --- Fail-closed rejections (AC-11, AC-12 evidence arm, AC-39, AC-42) ---

    [Fact]
    public void AttachEvidence_NullDescriptor_FailsClosedWithoutThrowing()
    {
        var original = NewCase();

        var exception = Record.Exception(() =>
            AssertFailsClosedAndUnchanged(
                original,
                original.AttachEvidence(null!, correlationReference: null),
                "Evidence"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AttachEvidence_BlankEvidenceId_FailsClosed(string? evidenceId)
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AttachEvidence(Descriptor(evidenceId: evidenceId), null),
            "EvidenceId");
    }

    [Fact]
    public void AttachEvidence_DuplicateId_FailsClosedEvenAcrossCategories()
    {
        var withEvidence = Attached(NewCase(), Descriptor("e-1"));

        // Same identity with a different category is still the same identity (AC-10).
        var result = withEvidence.AttachEvidence(
            Descriptor("e-1", "MedicalExpenseEvidence", SpouseId), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "EvidenceId");
        Assert.Single(withEvidence.Evidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AttachEvidence_MissingCategory_FailsClosed(string? category)
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AttachEvidence(Descriptor(category: category), null),
            "Category");
    }

    [Theory]
    [InlineData("NotACategory")]
    [InlineData("t4slip")]
    [InlineData("T5Slip")]
    [InlineData("0")]
    public void AttachEvidence_UnknownOrCaseMismatchedCategory_FailsClosedNamingIt(string category)
    {
        var original = NewCase();
        var result = original.AttachEvidence(Descriptor(category: category), null);

        AssertFailsClosedAndUnchanged(original, result, "Category");
        var error = Assert.Single(result.Errors, e => e.Field == "Category");
        Assert.Contains($"'{category}'", error.Message);
    }

    [Fact]
    public void AttachEvidence_SinLikeCategory_ErrorNamesOnlyRedactedForm()
    {
        var original = NewCase();
        var result = original.AttachEvidence(Descriptor(category: "123456789"), null);

        AssertFailsClosedAndUnchanged(original, result, "Category");
        var error = Assert.Single(result.Errors, e => e.Field == "Category");
        Assert.DoesNotContain("123456789", error.Message);
        Assert.Contains("***-**-789", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AttachEvidence_BlankParticipant_FailsClosed(string? participantId)
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AttachEvidence(Descriptor(participantId: participantId), null),
            "ParticipantId");
    }

    [Fact]
    public void AttachEvidence_NonMemberParticipant_FailsClosed()
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AttachEvidence(Descriptor(participantId: "p-99"), null),
            "ParticipantId");
    }

    [Fact]
    public void AttachEvidence_AbsentTaxYear_FailsClosed()
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AttachEvidence(Descriptor(taxYear: null), null),
            "TaxYear");
    }

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    public void AttachEvidence_MismatchedTaxYear_FailsClosedAndNeverAdopted(int taxYear)
    {
        var original = NewCase();
        var result = original.AttachEvidence(Descriptor(taxYear: taxYear), null);

        AssertFailsClosedAndUnchanged(original, result, "TaxYear");
    }

    [Fact]
    public void AttachEvidence_CrossFirmEvidence_FailsClosedAndAddsNothing()
    {
        var original = NewCase();
        var result = original.AttachEvidence(Descriptor(firmId: "firm-b"), null);

        AssertFailsClosedAndUnchanged(original, result, "FirmId");
        Assert.Contains(result.Errors, error => error.Message.Contains("cross-firm"));
    }

    [Fact]
    public void AttachEvidence_EmptyDescriptor_ReturnsOneErrorPerMissingFieldWithoutThrowing()
    {
        var original = NewCase();
        var result = original.AttachEvidence(
            new EvidenceDescriptor(null, null, null, null, null, null), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(4, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Field == "EvidenceId");
        Assert.Contains(result.Errors, e => e.Field == "Category");
        Assert.Contains(result.Errors, e => e.Field == "ParticipantId");
        Assert.Contains(result.Errors, e => e.Field == "TaxYear");
    }

    // --- PII in rejection paths (AC-37 evidence arm) ---

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    public void AttachEvidence_RejectedDescriptorCarryingSinLikeText_NeverEchoesRawSinInErrors(string sinLike)
    {
        // Every string field carries the SIN-like pattern; the mismatched year forces failure.
        var result = NewCase().AttachEvidence(
            new EvidenceDescriptor(sinLike, sinLike, sinLike, 2024, sinLike, FirmA), null);

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("123456789", error.Message);
            Assert.DoesNotContain("123-456-789", error.Message);
        });
    }

    [Fact]
    public void AttachEvidence_StoredEvidenceToString_ExposesNoCallerStrings()
    {
        var taxCase = Attached(NewCase(), Descriptor(
            "id-123456789",
            sourceReference: "ref-123-456-789"));

        var text = Assert.Single(taxCase.Evidence).ToString();
        Assert.DoesNotContain("123456789", text);
        Assert.DoesNotContain("123-456-789", text);
    }

    [Fact]
    public void AttachEvidence_Failure_ProducesNoCaseAndNoChangeRecord()
    {
        var result = NewCase().AttachEvidence(Descriptor(evidenceId: "  "), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }
}
