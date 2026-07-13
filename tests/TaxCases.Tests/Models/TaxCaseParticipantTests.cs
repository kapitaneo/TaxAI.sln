using TaxCases.Models;
using TaxLogicEngine.Models;

namespace TaxCases.Tests.Models;

public sealed class TaxCaseParticipantTests
{
    private const string CaseId = "case-1";
    private const string FirmA = "firm-a";
    private const string PrimaryId = "p-1";
    private const string SpouseId = "p-2";

    private static TaxCase NewCase()
    {
        var result = TaxCase.Create(new CreateTaxCaseRequest(
            CaseId,
            FirmA,
            2025,
            new ParticipantDescriptor(PrimaryId, RelationshipKind.PrimaryTaxpayer, null, null, null),
            CorrelationReference: null));

        Assert.True(result.IsSuccess);
        return result.Value!.Case;
    }

    private static ParticipantDescriptor Descriptor(
        string? participantId = SpouseId,
        RelationshipKind relationship = RelationshipKind.SpouseOrCommonLaw,
        string? displayName = null,
        string? sin = null,
        string? firmId = null) =>
        new(participantId, relationship, displayName, sin, firmId);

    private static TaxCase Added(TaxCase taxCase, ParticipantDescriptor descriptor)
    {
        var result = taxCase.AddParticipant(descriptor, correlationReference: null);
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

        // The original case is untouched and no change record exists (AC-42/33).
        var participant = Assert.Single(original.Participants);
        Assert.Equal(PrimaryId, participant.ParticipantId);
        Assert.Equal(CaseStatus.Intake, original.Status);
    }

    // --- Happy path (AC-6, AC-7 structural dependants, AC-13 participant aspect) ---

    [Fact]
    public void AddParticipant_Spouse_ReturnsReplacementWithSpouseAppended()
    {
        var result = NewCase().AddParticipant(Descriptor(), correlationReference: null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);

        var participants = result.Value!.Case.Participants;
        Assert.Equal(2, participants.Count);
        Assert.Equal(SpouseId, participants[1].ParticipantId);
        Assert.Equal(RelationshipKind.SpouseOrCommonLaw, participants[1].Relationship);
    }

    [Fact]
    public void AddParticipant_MultipleDistinctDependants_AllStructuralAndUnlimited()
    {
        var taxCase = NewCase();
        taxCase = Added(taxCase, Descriptor("d-1", RelationshipKind.Dependant));
        taxCase = Added(taxCase, Descriptor("d-2", RelationshipKind.Dependant));
        taxCase = Added(taxCase, Descriptor("d-3", RelationshipKind.Dependant));

        Assert.Equal(4, taxCase.Participants.Count);
        Assert.Equal(3, taxCase.Participants.Count(p => p.Relationship == RelationshipKind.Dependant));
    }

    [Fact]
    public void AddParticipant_EachParticipantRetrievableOnlyByItsCaseLocalId()
    {
        var taxCase = Added(Added(NewCase(), Descriptor()), Descriptor("d-1", RelationshipKind.Dependant));

        var spouse = Assert.Single(taxCase.Participants, p => p.ParticipantId == SpouseId);
        Assert.Equal(RelationshipKind.SpouseOrCommonLaw, spouse.Relationship);

        var dependant = Assert.Single(taxCase.Participants, p => p.ParticipantId == "d-1");
        Assert.Equal(RelationshipKind.Dependant, dependant.Relationship);

        Assert.DoesNotContain(taxCase.Participants, p => p.ParticipantId == "unknown-id");
    }

    [Fact]
    public void AddParticipant_PreservesInsertionOrder()
    {
        var taxCase = NewCase();
        taxCase = Added(taxCase, Descriptor("d-1", RelationshipKind.Dependant));
        taxCase = Added(taxCase, Descriptor());
        taxCase = Added(taxCase, Descriptor("d-2", RelationshipKind.Dependant));

        Assert.Equal(
            new[] { PrimaryId, "d-1", SpouseId, "d-2" },
            taxCase.Participants.Select(p => p.ParticipantId).ToArray());
    }

    [Fact]
    public void AddParticipant_WithoutSin_SucceedsAndFabricatesNoSin()
    {
        var taxCase = Added(NewCase(), Descriptor(sin: null));

        Assert.Null(taxCase.Participants[1].Sin);
    }

    [Fact]
    public void AddParticipant_WithSin_StoresOnlyRecipientIdentifierAndMasks()
    {
        var taxCase = Added(NewCase(), Descriptor(sin: "123456789"));
        var spouse = taxCase.Participants[1];

        var sin = Assert.IsType<RecipientIdentifier>(spouse.Sin);
        Assert.Equal("***-**-789", sin.Masked);
        Assert.DoesNotContain("123456789", spouse.ToString());
        Assert.DoesNotContain("123456789", taxCase.ToString());
        Assert.Contains("***-**-789", spouse.ToString());
    }

    [Fact]
    public void AddParticipant_MatchingDescriptorFirm_Succeeds()
    {
        var taxCase = Added(NewCase(), Descriptor(firmId: FirmA, displayName: "Sam Doe"));

        Assert.Equal(FirmA, taxCase.FirmId);
        Assert.Equal("Sam Doe", taxCase.Participants[1].DisplayName);
    }

    // --- Change record and immutability (AC-32/33 participant arms) ---

    [Fact]
    public void AddParticipant_EmitsExactlyOneDeterministicParticipantAddedRecord()
    {
        var result = NewCase().AddParticipant(Descriptor(), "corr-3");

        Assert.True(result.IsSuccess);
        var expected = new CaseChangeRecord(
            CaseId,
            CaseChangeKind.ParticipantAdded,
            SpouseId,
            EvidenceId: null,
            QuestionId: null,
            PreviousStatus: null,
            ResultingStatus: null,
            CorrelationReference: "corr-3",
            Detail: null);

        Assert.Equal(expected, result.Value!.Change);
    }

    [Fact]
    public void AddParticipant_Success_LeavesOriginalCaseUnchanged()
    {
        var original = NewCase();

        var result = original.AddParticipant(Descriptor(), correlationReference: null);

        Assert.True(result.IsSuccess);
        Assert.NotSame(original, result.Value!.Case);
        var participant = Assert.Single(original.Participants);
        Assert.Equal(PrimaryId, participant.ParticipantId);
    }

    [Fact]
    public void AddParticipant_SameInputTwice_YieldsEqualRecordsAndEqualParticipants()
    {
        var first = NewCase().AddParticipant(Descriptor(displayName: "Sam Doe"), "corr-3");
        var second = NewCase().AddParticipant(Descriptor(displayName: "Sam Doe"), "corr-3");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Change, second.Value!.Change);
        Assert.Equal(first.Value.Case.Participants, second.Value.Case.Participants);
    }

    // --- Fail-closed rejections (AC-8, AC-12 participant arm, AC-42) ---

    [Fact]
    public void AddParticipant_NullDescriptor_FailsClosedWithoutThrowing()
    {
        var original = NewCase();

        var exception = Record.Exception(() =>
            AssertFailsClosedAndUnchanged(
                original,
                original.AddParticipant(null!, correlationReference: null),
                "Participant"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddParticipant_BlankParticipantId_FailsClosed(string? participantId)
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AddParticipant(Descriptor(participantId: participantId), null),
            "ParticipantId");
    }

    [Fact]
    public void AddParticipant_DuplicateOfPrimaryId_FailsClosed()
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AddParticipant(Descriptor(participantId: PrimaryId), null),
            "ParticipantId");
    }

    [Fact]
    public void AddParticipant_DuplicateOfEarlierAddedId_FailsClosed()
    {
        var withSpouse = Added(NewCase(), Descriptor());

        var result = withSpouse.AddParticipant(
            Descriptor(participantId: SpouseId, relationship: RelationshipKind.Dependant), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "ParticipantId");
        Assert.Equal(2, withSpouse.Participants.Count);
    }

    [Fact]
    public void AddParticipant_SecondPrimary_FailsClosed()
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AddParticipant(Descriptor("p-9", RelationshipKind.PrimaryTaxpayer), null),
            "Relationship");
    }

    [Fact]
    public void AddParticipant_SecondSpouse_FailsClosed()
    {
        var withSpouse = Added(NewCase(), Descriptor());

        var result = withSpouse.AddParticipant(
            Descriptor("p-9", RelationshipKind.SpouseOrCommonLaw), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Field == "Relationship");
        Assert.Equal(2, withSpouse.Participants.Count);
        Assert.Equal(1, withSpouse.Participants.Count(
            p => p.Relationship == RelationshipKind.SpouseOrCommonLaw));
    }

    [Fact]
    public void AddParticipant_UndefinedRelationship_FailsClosed()
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AddParticipant(Descriptor(relationship: (RelationshipKind)99), null),
            "Relationship");
    }

    [Fact]
    public void AddParticipant_CrossFirmParticipant_FailsClosedAndAddsNothing()
    {
        var original = NewCase();
        var result = original.AddParticipant(Descriptor(firmId: "firm-b"), null);

        AssertFailsClosedAndUnchanged(original, result, "FirmId");
        Assert.Contains(result.Errors, error => error.Message.Contains("cross-firm"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void AddParticipant_PresentButBlankSin_FailsClosed(string sin)
    {
        var original = NewCase();

        AssertFailsClosedAndUnchanged(
            original,
            original.AddParticipant(Descriptor(sin: sin), null),
            "Sin");
    }

    // --- PII in rejection paths (AC-37 participant arm) ---

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    public void AddParticipant_RejectedDescriptorCarryingSin_NeverEchoesRawSinInErrors(string sin)
    {
        // Blank participant id forces failure while a full SIN rides on the descriptor.
        var result = NewCase().AddParticipant(Descriptor(participantId: " ", sin: sin), null);

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("123456789", error.Message);
            Assert.DoesNotContain("123-456-789", error.Message);
        });
    }

    [Fact]
    public void AddParticipant_Failure_ProducesNoCaseAndNoChangeRecord()
    {
        var result = NewCase().AddParticipant(Descriptor(participantId: PrimaryId), null);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }
}
