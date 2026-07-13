using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.Models;

public sealed class TaxCaseCreateTests
{
    private const string CaseId = "case-1";
    private const string FirmA = "firm-a";
    private const string PrimaryId = "p-1";

    private static ParticipantDescriptor Primary(
        string? participantId = PrimaryId,
        RelationshipKind relationship = RelationshipKind.PrimaryTaxpayer,
        string? displayName = null,
        string? sin = null,
        string? firmId = null) =>
        new(participantId, relationship, displayName, sin, firmId);

    private static CreateTaxCaseRequest Request(
        string? caseId = CaseId,
        string? firmId = FirmA,
        int? taxYear = 2025,
        ParticipantDescriptor? primary = null,
        string? correlationReference = null) =>
        new(caseId, firmId, taxYear, primary ?? Primary(), correlationReference);

    private static void AssertFailsClosed(ParseResult<CaseOperationOutcome> result, string expectedField)
    {
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, error => error.Field == expectedField);
    }

    // --- Happy path (AC-1, AC-38, AC-41, AC-13 creation aspect) ---

    [Fact]
    public void Create_MinimalValidCase_ReturnsCaseWithOnePrimaryAndNoErrors()
    {
        var result = TaxCase.Create(Request());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);

        var taxCase = result.Value!.Case;
        Assert.Equal(CaseId, taxCase.CaseId);
        Assert.Equal(FirmA, taxCase.FirmId);
        Assert.Equal(2025, taxCase.TaxYear);
        Assert.Equal(CaseStatus.Intake, taxCase.Status);

        var participant = Assert.Single(taxCase.Participants);
        Assert.Equal(PrimaryId, participant.ParticipantId);
        Assert.Equal(RelationshipKind.PrimaryTaxpayer, participant.Relationship);

        Assert.Empty(taxCase.Evidence);
        Assert.Empty(taxCase.Answers);
        Assert.Null(taxCase.Checklist);
    }

    [Fact]
    public void Create_MatchingDescriptorFirm_SucceedsAndCaseOwnsFirm()
    {
        var result = TaxCase.Create(Request(primary: Primary(firmId: FirmA, displayName: "Alex Doe")));

        Assert.True(result.IsSuccess);
        Assert.Equal(FirmA, result.Value!.Case.FirmId);
        Assert.Equal("Alex Doe", result.Value.Case.Participants[0].DisplayName);
    }

    [Fact]
    public void Create_WithoutSin_SucceedsAndFabricatesNoSin()
    {
        var result = TaxCase.Create(Request(primary: Primary(sin: null)));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Case.Participants[0].Sin);
    }

    [Fact]
    public void Create_WithSin_StoresOnlyRecipientIdentifierAndMasks()
    {
        var result = TaxCase.Create(Request(primary: Primary(sin: "123456789")));

        Assert.True(result.IsSuccess);
        var participant = result.Value!.Case.Participants[0];

        var sin = Assert.IsType<RecipientIdentifier>(participant.Sin);
        Assert.Equal("***-**-789", sin.Masked);
        Assert.DoesNotContain("123456789", participant.ToString());
        Assert.DoesNotContain("123456789", result.Value.Case.ToString());
        Assert.Contains("***-**-789", participant.ToString());
    }

    // --- Change record (AC-32 creation arm; determinism) ---

    [Fact]
    public void Create_EmitsExactlyOneDeterministicCaseCreatedRecord()
    {
        var result = TaxCase.Create(Request(correlationReference: "corr-7"));

        Assert.True(result.IsSuccess);
        var expected = new CaseChangeRecord(
            CaseId,
            CaseChangeKind.CaseCreated,
            PrimaryId,
            EvidenceId: null,
            QuestionId: null,
            PreviousStatus: null,
            ResultingStatus: CaseStatus.Intake,
            CorrelationReference: "corr-7",
            Detail: null);

        Assert.Equal(expected, result.Value!.Change);
    }

    [Fact]
    public void Create_SameInputTwice_YieldsEqualRecordsAndEqualObservableState()
    {
        var first = TaxCase.Create(Request(correlationReference: "corr-7"));
        var second = TaxCase.Create(Request(correlationReference: "corr-7"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Change, second.Value!.Change);
        Assert.Equal(first.Value.Case.CaseId, second.Value.Case.CaseId);
        Assert.Equal(first.Value.Case.FirmId, second.Value.Case.FirmId);
        Assert.Equal(first.Value.Case.TaxYear, second.Value.Case.TaxYear);
        Assert.Equal(first.Value.Case.Status, second.Value.Case.Status);
        Assert.Equal(first.Value.Case.Participants, second.Value.Case.Participants);
    }

    // --- Fail-closed rejections (AC-3, AC-4, AC-5, AC-12, AC-36, AC-42/33 creation arms) ---

    [Fact]
    public void Create_NullRequest_FailsClosedWithoutThrowing()
    {
        var exception = Record.Exception(() =>
        {
            var result = TaxCase.Create(null!);
            Assert.True(result.IsFailure);
            Assert.Null(result.Value);
            Assert.NotEmpty(result.Errors);
        });

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankCaseId_FailsClosed(string? caseId) =>
        AssertFailsClosed(TaxCase.Create(Request(caseId: caseId)), "CaseId");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankFirmId_FailsClosed(string? firmId) =>
        AssertFailsClosed(TaxCase.Create(Request(firmId: firmId)), "FirmId");

    [Fact]
    public void Create_AbsentTaxYear_FailsClosedAndNeverDefaults() =>
        AssertFailsClosed(TaxCase.Create(Request(taxYear: null)), "TaxYear");

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(1999)]
    public void Create_UnsupportedTaxYear_FailsClosedAndIsNeverCoerced(int year)
    {
        var result = TaxCase.Create(Request(taxYear: year));

        AssertFailsClosed(result, "TaxYear");
        // No case exists at all, so nothing was coerced to 2025 (AC-4).
        Assert.Null(result.Value);
    }

    [Fact]
    public void Create_MissingPrimaryTaxpayer_FailsClosed()
    {
        var request = new CreateTaxCaseRequest(CaseId, FirmA, 2025, PrimaryTaxpayer: null, CorrelationReference: null);
        AssertFailsClosed(TaxCase.Create(request), "PrimaryTaxpayer");
    }

    [Theory]
    [InlineData(RelationshipKind.SpouseOrCommonLaw)]
    [InlineData(RelationshipKind.Dependant)]
    public void Create_NonPrimaryRelationship_FailsClosed(RelationshipKind relationship) =>
        AssertFailsClosed(
            TaxCase.Create(Request(primary: Primary(relationship: relationship))),
            "PrimaryTaxpayer");

    [Fact]
    public void Create_UndefinedRelationship_FailsClosed() =>
        AssertFailsClosed(
            TaxCase.Create(Request(primary: Primary(relationship: (RelationshipKind)99))),
            "PrimaryTaxpayer");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankParticipantId_FailsClosed(string? participantId) =>
        AssertFailsClosed(
            TaxCase.Create(Request(primary: Primary(participantId: participantId))),
            "PrimaryTaxpayer");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Create_PresentButBlankSin_FailsClosed(string sin) =>
        AssertFailsClosed(
            TaxCase.Create(Request(primary: Primary(sin: sin))),
            "PrimaryTaxpayer");

    [Fact]
    public void Create_CrossFirmPrimary_FailsClosedAndAddsNothing()
    {
        var result = TaxCase.Create(Request(primary: Primary(firmId: "firm-b")));

        AssertFailsClosed(result, "PrimaryTaxpayer");
        Assert.Contains(result.Errors, error => error.Message.Contains("cross-firm"));
    }

    [Fact]
    public void Create_EmptyRequest_ReturnsOneErrorPerMissingFieldWithoutThrowing()
    {
        var request = new CreateTaxCaseRequest(null, null, null, null, null);

        var exception = Record.Exception(() =>
        {
            var result = TaxCase.Create(request);

            Assert.True(result.IsFailure);
            Assert.Null(result.Value);
            Assert.Equal(4, result.Errors.Count);
            Assert.Equal(
                new[] { "CaseId", "FirmId", "TaxYear", "PrimaryTaxpayer" },
                result.Errors.Select(error => error.Field).ToArray());
        });

        Assert.Null(exception);
    }

    // --- PII in rejection paths (AC-37 creation arm) ---

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    public void Create_RejectedRequestCarryingSin_NeverEchoesRawSinInErrors(string sin)
    {
        // Blank participant id forces failure while a full SIN rides on the descriptor.
        var result = TaxCase.Create(Request(primary: Primary(participantId: " ", sin: sin)));

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("123456789", error.Message);
            Assert.DoesNotContain("123-456-789", error.Message);
        });
    }

    [Fact]
    public void Create_Failure_ProducesNoCaseAndNoChangeRecord()
    {
        var result = TaxCase.Create(Request(taxYear: 2024));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }
}
