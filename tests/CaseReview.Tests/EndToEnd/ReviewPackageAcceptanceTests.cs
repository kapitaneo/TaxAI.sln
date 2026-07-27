using CaseProcessing.Models;
using CaseProcessing.Services;
using CaseReview.Models;
using CaseReview.Services;
using DocumentExtraction.Models;
using DocumentExtraction.Services;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;

namespace CaseReview.Tests.EndToEnd;

public sealed class ReviewPackageAcceptanceTests
{
    [Fact]
    public void PublicPipeline_MultiSlipHappyPath_PreservesOrderAndSignsReplacement()
    {
        var first = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-first"));
        var third = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-third"));
        var second = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-second"));

        var assembled = ReviewPackageAssembler.Assemble(
            [first.Processed, third.Processed, second.Processed]);

        Assert.True(assembled.IsSuccess);
        var package = assembled.Value!;
        Assert.Equal(
            new[] { "document-first", "document-third", "document-second" },
            package.ReviewedSlips.Select(reviewed => reviewed.DocumentId));
        Assert.Equal(3, package.ReviewedSlips.Count);
        Assert.Null(package.SignOff);
        Assert.All(
            package.ReviewedSlips,
            reviewed => Assert.Equal(package.TaxYear, reviewed.TaxYear));

        var signed = package.RecordDecision(
            new ReviewDecisionDescriptor(
                SignOffDecision.Approved,
                "reviewer-1",
                "decided-at-1",
                note: null,
                unmappedBoxesAcknowledged: null));

        Assert.True(signed.IsSuccess);
        Assert.Null(package.SignOff);
        Assert.Equal(package.ReviewedSlips, signed.Value!.ReviewedSlips);
        Assert.Equal(SignOffDecision.Approved, signed.Value.SignOff!.Decision);
    }

    [Fact]
    public void PublicPipeline_ControlledMap_PreservesEveryProcessedValue()
    {
        var provider = ReviewPipelineFixture.Provider(
            new T4BoxLineMapping(
                T4BoxCodes.EmploymentIncome,
                ["TEST-DESTINATION-B"]),
            new T4BoxLineMapping(
                T4BoxCodes.IncomeTaxDeducted,
                ["TEST-DESTINATION-A"]));
        var payload = ReviewPipelineFixture.Payload(
            amountBoxes:
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"),
                new T4RawBox(T4BoxCodes.IncomeTaxDeducted, "8000.00"),
                new T4RawBox(T4BoxCodes.RppContributions, "900.00"),
                new T4RawBox(T4BoxCodes.UnionDues, "400.00"),
            ]);
        var upstream = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(payload: payload),
            provider);
        Assert.NotEmpty(upstream.Processed.Mapping.Lines);
        Assert.NotEmpty(upstream.Processed.Mapping.UnmappedBoxCodes);

        var assembled = ReviewPackageAssembler.Assemble([upstream.Processed]);

        Assert.True(assembled.IsSuccess);
        var reviewed = Assert.Single(assembled.Value!.ReviewedSlips);
        Assert.Same(upstream.Processed.Slip, reviewed.Slip);
        Assert.Equal(upstream.Processed.DocumentId, reviewed.DocumentId);
        Assert.Equal(upstream.Processed.SourceReference, reviewed.SourceReference);
        Assert.Equal(upstream.Processed.TaxYear, reviewed.TaxYear);
        ReviewPipelineFixture.AssertLinesEqual(
            upstream.Processed.Mapping.Lines,
            reviewed.LineEntries);
        Assert.Equal(
            T4SlipBoxAmounts.Enumerate(upstream.Processed.Slip).ToArray(),
            reviewed.BoxAmounts
                .Select(amount => (amount.BoxCode, amount.Amount))
                .ToArray());
        Assert.Equal(
            upstream.Processed.Mapping.UnmappedBoxCodes,
            reviewed.UnmappedBoxCodes);
    }

    [Fact]
    public void PublicPipeline_EmptyMapping_SucceedsWithEmptyLineEntries()
    {
        var upstream = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(
                payload: ReviewPipelineFixture.Payload(
                    amountBoxes: Array.Empty<T4RawBox>())),
            ReviewPipelineFixture.EmptyProvider());
        Assert.Empty(upstream.Processed.Mapping.Lines);

        var assembled = ReviewPackageAssembler.Assemble([upstream.Processed]);

        Assert.True(assembled.IsSuccess);
        var reviewed = Assert.Single(assembled.Value!.ReviewedSlips);
        Assert.Empty(reviewed.LineEntries);
        Assert.Empty(reviewed.BoxAmounts);
        Assert.Empty(reviewed.UnmappedBoxCodes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100.005")]
    [InlineData("100.015")]
    public void PublicPipeline_ZeroAndHalfCentValues_EqualUpstreamOutput(
        string rawAmount)
    {
        var payload = ReviewPipelineFixture.Payload(
            amountBoxes:
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, rawAmount),
            ]);
        var upstream = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(payload: payload));
        var engineAmounts = T4SlipBoxAmounts
            .Enumerate(upstream.Processed.Slip)
            .ToArray();

        var assembled = ReviewPackageAssembler.Assemble([upstream.Processed]);

        Assert.True(assembled.IsSuccess);
        var reviewed = Assert.Single(assembled.Value!.ReviewedSlips);
        Assert.Same(upstream.Processed.Slip, reviewed.Slip);
        Assert.Equal(
            upstream.Processed.Slip.EmploymentIncome,
            reviewed.Slip.EmploymentIncome);
        Assert.Equal(
            engineAmounts,
            reviewed.BoxAmounts
                .Select(amount => (amount.BoxCode, amount.Amount))
                .ToArray());
        ReviewPipelineFixture.AssertLinesEqual(
            upstream.Processed.Mapping.Lines,
            reviewed.LineEntries);
    }

    [Theory]
    [InlineData(SignOffDecision.Approved)]
    [InlineData(SignOffDecision.ChangesRequested)]
    [InlineData(SignOffDecision.Rejected)]
    public void PublicPipeline_AllThreeDecisions_AreRecordedExactly(
        SignOffDecision decision)
    {
        var upstream = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(
                payload: ReviewPipelineFixture.Payload(
                    amountBoxes: Array.Empty<T4RawBox>())));
        var package = ReviewPipelineFixture.Assemble([upstream.Processed]);
        var note = decision == SignOffDecision.Approved
            ? null
            : $"note-for-{decision}";
        var descriptor = new ReviewDecisionDescriptor(
            decision,
            "reviewer-1",
            "opaque-decided-at",
            note,
            unmappedBoxesAcknowledged: null);

        var result = package.RecordDecision(descriptor);

        Assert.True(result.IsSuccess);
        var signOff = result.Value!.SignOff!;
        Assert.Equal(decision, signOff.Decision);
        Assert.Equal(descriptor.ReviewerReference, signOff.ReviewerReference);
        Assert.Equal(descriptor.DecidedAtReference, signOff.DecidedAtReference);
        Assert.Equal(note, signOff.Note);
        Assert.Null(signOff.UnmappedBoxesAcknowledged);
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void PublicPipeline_OneAcknowledgementCoversSeveralSlipsAndUnmappedCodes()
    {
        var provider = ReviewPipelineFixture.EmptyProvider();
        var first = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(
                documentId: "document-a",
                payload: ReviewPipelineFixture.Payload(
                    amountBoxes:
                    [
                        new T4RawBox(T4BoxCodes.EmploymentIncome, "100.00"),
                        new T4RawBox(T4BoxCodes.UnionDues, "10.00"),
                    ])),
            provider);
        var second = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(
                documentId: "document-b",
                payload: ReviewPipelineFixture.Payload(
                    amountBoxes:
                    [
                        new T4RawBox(T4BoxCodes.IncomeTaxDeducted, "25.00"),
                        new T4RawBox(T4BoxCodes.RppContributions, "30.00"),
                    ])),
            provider);
        var third = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(
                documentId: "document-c",
                payload: ReviewPipelineFixture.Payload(
                    amountBoxes:
                    [
                        new T4RawBox(T4BoxCodes.CharitableDonations, "15.00"),
                    ])),
            provider);
        var package = ReviewPipelineFixture.Assemble(
            [first.Processed, second.Processed, third.Processed]);
        Assert.All(
            package.ReviewedSlips,
            reviewed => Assert.NotEmpty(reviewed.UnmappedBoxCodes));

        var result = package.RecordDecision(
            new ReviewDecisionDescriptor(
                SignOffDecision.Approved,
                "reviewer-1",
                "decided-at-1",
                note: null,
                unmappedBoxesAcknowledged: true));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.SignOff!.UnmappedBoxesAcknowledged);
        Assert.Equal(package.ReviewedSlips, result.Value.ReviewedSlips);
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void PublicPipeline_ApprovalWithoutUnmappedCodes_NeedsNoAcknowledgement()
    {
        var upstream = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(
                payload: ReviewPipelineFixture.Payload(
                    amountBoxes: Array.Empty<T4RawBox>())));
        var package = ReviewPipelineFixture.Assemble([upstream.Processed]);
        Assert.Empty(Assert.Single(package.ReviewedSlips).UnmappedBoxCodes);

        var result = package.RecordDecision(
            new ReviewDecisionDescriptor(
                SignOffDecision.Approved,
                "reviewer-1",
                "decided-at-1",
                note: null,
                unmappedBoxesAcknowledged: null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SignOff!.UnmappedBoxesAcknowledged);
        Assert.Null(package.SignOff);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void PublicPipeline_RequiredAcknowledgementAbsentOrFalse_FailsClosed(
        bool? acknowledgement)
    {
        var upstream = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(),
            ReviewPipelineFixture.EmptyProvider());
        var package = ReviewPipelineFixture.Assemble([upstream.Processed]);
        Assert.NotEmpty(Assert.Single(package.ReviewedSlips).UnmappedBoxCodes);

        var result = package.RecordDecision(
            new ReviewDecisionDescriptor(
                SignOffDecision.Approved,
                "reviewer-1",
                "decided-at-1",
                note: null,
                unmappedBoxesAcknowledged: acknowledgement));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(
            ReviewFields.UnmappedBoxAcknowledgement,
            Assert.Single(result.Errors).Field);
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void PublicPipeline_AssemblyAndDecisionMutateNoUpstreamInputOrOriginalPackage()
    {
        var first = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-a"));
        var second = ReviewPipelineFixture.Process(
            ReviewPipelineFixture.Envelope(documentId: "document-b"));
        var firstSnapshot = ReviewPipelineFixture.Snapshot(first);
        var secondSnapshot = ReviewPipelineFixture.Snapshot(second);

        var package = ReviewPipelineFixture.Assemble(
            [first.Processed, second.Processed]);
        var signed = package.RecordDecision(
            new ReviewDecisionDescriptor(
                SignOffDecision.ChangesRequested,
                "reviewer-1",
                "not-a-timestamp",
                "review-note",
                unmappedBoxesAcknowledged: null));

        Assert.True(signed.IsSuccess);
        Assert.Null(package.SignOff);
        Assert.NotNull(signed.Value!.SignOff);
        Assert.Equal(package.ReviewedSlips, signed.Value.ReviewedSlips);
        ReviewPipelineFixture.AssertUnchanged(firstSnapshot, first);
        ReviewPipelineFixture.AssertUnchanged(secondSnapshot, second);
    }
}

internal static class ReviewPipelineFixture
{
    internal const int TaxYear = 2025;

    internal static readonly string[] Plants =
    {
        "123456789",
        "123-456-789",
        "123 456 789",
        "ZZMARKERZZ",
        "<<leak-me>>",
    };

    internal static readonly string[] PlantSlots =
    {
        "Box12Value",
        "OtherBoxValue",
        "EmployerName",
        "EmployerAddress",
        "EmployeeName",
        "EmployeeAddress",
        "DocumentId",
        "FirmId",
        "CaseId",
        "ParticipantId",
        "SourceReference",
        "ReviewerReference",
        "DecidedAtReference",
        "Note",
    };

    internal static readonly string[] AssemblyFailureFamilies =
    {
        "NullCollection",
        "EmptyCollection",
        "NullElement",
        "FirmMismatch",
        "CaseMismatch",
        "ParticipantMismatch",
        "OrdinalParticipantMismatch",
        "DuplicateDocument",
        "MultipleDefects",
    };

    internal static readonly string[] DecisionFailureFamilies =
    {
        "NullDecision",
        "UndefinedDecision",
        "BlankReviewer",
        "BlankDecidedAt",
        "MissingRejectedNote",
        "MissingChangesNote",
        "SecondDecision",
        "AcknowledgementAbsent",
        "AcknowledgementNegative",
        "MultipleDefects",
    };

    internal static EmbeddedT4LineMapProvider Embedded { get; } = new();

    internal static T4RawInput Payload(
        string slot = "",
        string plant = "",
        IReadOnlyList<T4RawBox>? amountBoxes = null)
    {
        var boxes = new List<T4RawBox>
        {
            new(
                T4BoxCodes.SocialInsuranceNumber,
                Slotted(slot, "Box12Value", plant, "046454286")),
            new(
                T4BoxCodes.EmployerAccountNumber,
                "12345678RP0001"),
            new(T4BoxCodes.DentalBenefits, "1"),
            new(
                T4BoxCodes.RppDpspRegistrationNumber,
                Slotted(slot, "OtherBoxValue", plant, "RPP-REFERENCE")),
        };
        boxes.AddRange(
            amountBoxes ??
            [
                new T4RawBox(T4BoxCodes.EmploymentIncome, "52000.00"),
                new T4RawBox(T4BoxCodes.IncomeTaxDeducted, "8000.00"),
            ]);

        return new T4RawInput
        {
            Year = TaxYear,
            EmployerName = Slotted(
                slot,
                "EmployerName",
                plant,
                "Employer Name"),
            EmployerAddress = Slotted(
                slot,
                "EmployerAddress",
                plant,
                "Employer Address"),
            EmployeeName = Slotted(
                slot,
                "EmployeeName",
                plant,
                "Employee Name"),
            EmployeeAddress = Slotted(
                slot,
                "EmployeeAddress",
                plant,
                "Employee Address"),
            Boxes = boxes,
        };
    }

    internal static ExtractionEnvelope Envelope(
        string slot = "",
        string plant = "",
        string documentId = "document-1",
        string firmId = "firm-1",
        string caseId = "case-1",
        string participantId = "participant-1",
        string sourceReference = "source-1",
        T4RawInput? payload = null) =>
        new()
        {
            DocumentId = Slotted(
                slot,
                "DocumentId",
                plant,
                documentId),
            Kind = ExtractedDocumentKind.T4Slip,
            FirmId = Slotted(slot, "FirmId", plant, firmId),
            CaseId = Slotted(slot, "CaseId", plant, caseId),
            ParticipantId = Slotted(
                slot,
                "ParticipantId",
                plant,
                participantId),
            TaxYear = TaxYear,
            Provenance = new ExtractionProvenance
            {
                SourceReference = Slotted(
                    slot,
                    "SourceReference",
                    plant,
                    sourceReference),
                ProviderLabel = "test-provider",
            },
            T4Payload = payload ?? Payload(slot, plant),
        };

    internal static ProcessedRun Process(
        ExtractionEnvelope envelope,
        IT4LineMapProvider? provider = null)
    {
        var validated = ExtractionEnvelopeValidator.Validate(envelope);
        Assert.True(
            validated.IsSuccess,
            "The test envelope must clear the public extraction gate.");

        var processed = new T4CaseProcessor(provider ?? Embedded)
            .Process(validated.Value);
        Assert.True(
            processed.IsSuccess,
            "The test extraction must clear the public case processor.");

        return new ProcessedRun(
            envelope,
            validated.Value!,
            processed.Value!);
    }

    internal static AccountantReviewPackage Assemble(
        IReadOnlyList<ProcessedT4Extraction?> processed)
    {
        var result = ReviewPackageAssembler.Assemble(processed);
        Assert.True(
            result.IsSuccess,
            "The test processed inputs must clear the public review assembler.");
        return result.Value!;
    }

    internal static AcceptedReviewRun Accepted(
        string slot = "",
        string plant = "")
    {
        var upstream = Process(Envelope(slot, plant));
        var package = Assemble([upstream.Processed]);
        var descriptor = Descriptor(slot, plant);
        var signed = package.RecordDecision(descriptor);
        Assert.True(
            signed.IsSuccess,
            "The test decision must clear the public decision gate.");

        return new AcceptedReviewRun(
            upstream,
            package,
            descriptor,
            signed.Value!);
    }

    internal static ReviewDecisionDescriptor Descriptor(
        string slot = "",
        string plant = "",
        SignOffDecision decision = SignOffDecision.ChangesRequested,
        bool? acknowledgement = null) =>
        new(
            decision,
            Slotted(
                slot,
                "ReviewerReference",
                plant,
                "reviewer-1"),
            Slotted(
                slot,
                "DecidedAtReference",
                plant,
                "decided-at-1"),
            Slotted(slot, "Note", plant, "review-note"),
            acknowledgement);

    internal static AssemblyFailureRun AssemblyFailure(
        string family,
        string slot = "",
        string plant = "")
    {
        if (string.Equals(family, "NullCollection", StringComparison.Ordinal))
        {
            return new AssemblyFailureRun(
                inputs: null,
                ReviewPackageAssembler.Assemble(null));
        }

        if (string.Equals(family, "EmptyCollection", StringComparison.Ordinal))
        {
            ProcessedT4Extraction?[] empty = [];
            return new AssemblyFailureRun(
                empty,
                ReviewPackageAssembler.Assemble(empty));
        }

        var first = Process(
            Envelope(
                slot,
                plant,
                documentId: "document-a"));

        if (string.Equals(family, "NullElement", StringComparison.Ordinal))
        {
            ProcessedT4Extraction?[] inputs = [first.Processed, null];
            return new AssemblyFailureRun(
                inputs,
                ReviewPackageAssembler.Assemble(inputs));
        }

        var secondDocumentId = string.Equals(
            family,
            "DuplicateDocument",
            StringComparison.Ordinal) ||
            string.Equals(
                family,
                "MultipleDefects",
                StringComparison.Ordinal)
            ? first.Processed.DocumentId
            : "document-b";
        var secondFirmId = family switch
        {
            "FirmMismatch" or "MultipleDefects" => "firm-different",
            _ => first.Processed.FirmId,
        };
        var secondCaseId = family switch
        {
            "CaseMismatch" or "MultipleDefects" => "case-different",
            _ => first.Processed.CaseId,
        };
        var secondParticipantId = family switch
        {
            "ParticipantMismatch" or "MultipleDefects" =>
                "participant-different",
            "OrdinalParticipantMismatch" =>
                $" {first.Processed.ParticipantId}",
            _ => first.Processed.ParticipantId,
        };
        var second = Process(
            Envelope(
                documentId: secondDocumentId,
                firmId: secondFirmId,
                caseId: secondCaseId,
                participantId: secondParticipantId,
                sourceReference: "source-b"));
        ProcessedT4Extraction?[] pair =
        [
            first.Processed,
            second.Processed,
        ];

        return new AssemblyFailureRun(
            pair,
            ReviewPackageAssembler.Assemble(pair));
    }

    internal static DecisionFailureRun DecisionFailure(
        string family,
        string slot = "",
        string plant = "")
    {
        var needsUnmapped = family is
            "AcknowledgementAbsent" or
            "AcknowledgementNegative";
        var upstream = Process(
            Envelope(slot, plant),
            needsUnmapped ? EmptyProvider() : Embedded);
        var unsigned = Assemble([upstream.Processed]);
        var target = unsigned;
        ReviewDecisionDescriptor? descriptor;

        switch (family)
        {
            case "NullDecision":
                descriptor = null;
                break;
            case "UndefinedDecision":
                descriptor = Descriptor(
                    slot,
                    plant,
                    (SignOffDecision)0);
                break;
            case "BlankReviewer":
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.Approved,
                    " ",
                    Slotted(
                        slot,
                        "DecidedAtReference",
                        plant,
                        "decided-at-1"),
                    Slotted(slot, "Note", plant, null),
                    unmappedBoxesAcknowledged: null);
                break;
            case "BlankDecidedAt":
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.Approved,
                    Slotted(
                        slot,
                        "ReviewerReference",
                        plant,
                        "reviewer-1"),
                    "\t",
                    Slotted(slot, "Note", plant, null),
                    unmappedBoxesAcknowledged: null);
                break;
            case "MissingRejectedNote":
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.Rejected,
                    Slotted(
                        slot,
                        "ReviewerReference",
                        plant,
                        "reviewer-1"),
                    Slotted(
                        slot,
                        "DecidedAtReference",
                        plant,
                        "decided-at-1"),
                    note: null,
                    unmappedBoxesAcknowledged: null);
                break;
            case "MissingChangesNote":
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.ChangesRequested,
                    Slotted(
                        slot,
                        "ReviewerReference",
                        plant,
                        "reviewer-1"),
                    Slotted(
                        slot,
                        "DecidedAtReference",
                        plant,
                        "decided-at-1"),
                    note: " ",
                    unmappedBoxesAcknowledged: null);
                break;
            case "SecondDecision":
                var firstDecision = unsigned.RecordDecision(
                    Descriptor(
                        slot,
                        plant,
                        SignOffDecision.ChangesRequested));
                Assert.True(firstDecision.IsSuccess);
                target = firstDecision.Value!;
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.Rejected,
                    "second-reviewer",
                    "second-decided-at",
                    "second-note",
                    unmappedBoxesAcknowledged: null);
                break;
            case "AcknowledgementAbsent":
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.Approved,
                    Slotted(
                        slot,
                        "ReviewerReference",
                        plant,
                        "reviewer-1"),
                    Slotted(
                        slot,
                        "DecidedAtReference",
                        plant,
                        "decided-at-1"),
                    Slotted(slot, "Note", plant, null),
                    unmappedBoxesAcknowledged: null);
                break;
            case "AcknowledgementNegative":
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.Approved,
                    Slotted(
                        slot,
                        "ReviewerReference",
                        plant,
                        "reviewer-1"),
                    Slotted(
                        slot,
                        "DecidedAtReference",
                        plant,
                        "decided-at-1"),
                    Slotted(slot, "Note", plant, null),
                    unmappedBoxesAcknowledged: false);
                break;
            case "MultipleDefects":
                descriptor = new ReviewDecisionDescriptor(
                    SignOffDecision.ChangesRequested,
                    " ",
                    null,
                    note: "\t",
                    unmappedBoxesAcknowledged: null);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(family),
                    family,
                    "Unknown decision failure family.");
        }

        return new DecisionFailureRun(
            upstream,
            unsigned,
            target,
            descriptor,
            target.RecordDecision(descriptor));
    }

    internal static IT4LineMapProvider EmptyProvider() =>
        Provider(Array.Empty<T4BoxLineMapping>());

    internal static IT4LineMapProvider Provider(
        params T4BoxLineMapping[] mappings) =>
        new InMemoryProvider(
            new T4LineMappingConfig(TaxYear, mappings));

    internal static ProcessedSnapshot Snapshot(ProcessedRun run) =>
        new(
            run.Envelope.DocumentId,
            run.Envelope.FirmId,
            run.Envelope.CaseId,
            run.Envelope.ParticipantId,
            run.Envelope.TaxYear,
            run.Envelope.Provenance,
            run.Envelope.T4Payload,
            run.Envelope.T4Payload!.Boxes,
            run.Validated,
            run.Processed.DocumentId,
            run.Processed.FirmId,
            run.Processed.CaseId,
            run.Processed.ParticipantId,
            run.Processed.TaxYear,
            run.Processed.SourceReference,
            run.Processed.Slip,
            run.Processed.Mapping);

    internal static void AssertUnchanged(
        ProcessedSnapshot expected,
        ProcessedRun actual)
    {
        Assert.Equal(expected.EnvelopeDocumentId, actual.Envelope.DocumentId);
        Assert.Equal(expected.EnvelopeFirmId, actual.Envelope.FirmId);
        Assert.Equal(expected.EnvelopeCaseId, actual.Envelope.CaseId);
        Assert.Equal(
            expected.EnvelopeParticipantId,
            actual.Envelope.ParticipantId);
        Assert.Equal(expected.EnvelopeTaxYear, actual.Envelope.TaxYear);
        Assert.Same(expected.Provenance, actual.Envelope.Provenance);
        Assert.Same(expected.Payload, actual.Envelope.T4Payload);
        Assert.Same(expected.Boxes, actual.Envelope.T4Payload!.Boxes);
        Assert.Same(expected.Validated, actual.Validated);
        Assert.Equal(expected.DocumentId, actual.Processed.DocumentId);
        Assert.Equal(expected.FirmId, actual.Processed.FirmId);
        Assert.Equal(expected.CaseId, actual.Processed.CaseId);
        Assert.Equal(expected.ParticipantId, actual.Processed.ParticipantId);
        Assert.Equal(expected.TaxYear, actual.Processed.TaxYear);
        Assert.Equal(expected.SourceReference, actual.Processed.SourceReference);
        Assert.Same(expected.Slip, actual.Processed.Slip);
        Assert.Same(expected.Mapping, actual.Processed.Mapping);
    }

    internal static void AssertLinesEqual(
        IReadOnlyList<TaxLineEntry> expected,
        IReadOnlyList<TaxLineEntry> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].LineNumber, actual[index].LineNumber);
            Assert.Equal(expected[index].Amount, actual[index].Amount);
            Assert.Equal(
                expected[index].SourceBoxCodes,
                actual[index].SourceBoxCodes);
        }
    }

    internal static string Slotted(
        string slot,
        string target,
        string plant,
        string? fallback) =>
        string.Equals(slot, target, StringComparison.Ordinal)
            ? plant
            : fallback!;

    private sealed class InMemoryProvider : IT4LineMapProvider
    {
        private readonly T4LineMap _map;

        internal InMemoryProvider(T4LineMappingConfig config)
        {
            var result = T4LineMap.Create(config);
            Assert.True(result.IsSuccess, "The controlled test map must be valid.");
            _map = result.Value!;
        }

        public ParseResult<T4LineMap> TryGetForYear(int taxYear) =>
            taxYear == _map.TaxYear
                ? ParseResult<T4LineMap>.Success(_map)
                : ParseResult<T4LineMap>.Failure(
                    new ValidationError(
                        "TaxYear",
                        "The controlled test provider has no map for this year."));
    }
}

internal sealed class ProcessedRun
{
    internal ProcessedRun(
        ExtractionEnvelope envelope,
        ValidatedExtraction validated,
        ProcessedT4Extraction processed)
    {
        Envelope = envelope;
        Validated = validated;
        Processed = processed;
    }

    internal ExtractionEnvelope Envelope { get; }
    internal ValidatedExtraction Validated { get; }
    internal ProcessedT4Extraction Processed { get; }
}

internal sealed class AcceptedReviewRun
{
    internal AcceptedReviewRun(
        ProcessedRun upstream,
        AccountantReviewPackage unsignedPackage,
        ReviewDecisionDescriptor descriptor,
        AccountantReviewPackage signedPackage)
    {
        Upstream = upstream;
        UnsignedPackage = unsignedPackage;
        Descriptor = descriptor;
        SignedPackage = signedPackage;
    }

    internal ProcessedRun Upstream { get; }
    internal AccountantReviewPackage UnsignedPackage { get; }
    internal ReviewDecisionDescriptor Descriptor { get; }
    internal AccountantReviewPackage SignedPackage { get; }
}

internal sealed class AssemblyFailureRun
{
    internal AssemblyFailureRun(
        IReadOnlyList<ProcessedT4Extraction?>? inputs,
        ParseResult<AccountantReviewPackage> result)
    {
        Inputs = inputs;
        Result = result;
    }

    internal IReadOnlyList<ProcessedT4Extraction?>? Inputs { get; }
    internal ParseResult<AccountantReviewPackage> Result { get; }
}

internal sealed class DecisionFailureRun
{
    internal DecisionFailureRun(
        ProcessedRun upstream,
        AccountantReviewPackage unsignedPackage,
        AccountantReviewPackage targetPackage,
        ReviewDecisionDescriptor? descriptor,
        ParseResult<AccountantReviewPackage> result)
    {
        Upstream = upstream;
        UnsignedPackage = unsignedPackage;
        TargetPackage = targetPackage;
        Descriptor = descriptor;
        Result = result;
    }

    internal ProcessedRun Upstream { get; }
    internal AccountantReviewPackage UnsignedPackage { get; }
    internal AccountantReviewPackage TargetPackage { get; }
    internal ReviewDecisionDescriptor? Descriptor { get; }
    internal ParseResult<AccountantReviewPackage> Result { get; }
}

internal sealed record ProcessedSnapshot(
    string? EnvelopeDocumentId,
    string? EnvelopeFirmId,
    string? EnvelopeCaseId,
    string? EnvelopeParticipantId,
    int? EnvelopeTaxYear,
    ExtractionProvenance? Provenance,
    T4RawInput? Payload,
    IReadOnlyList<T4RawBox> Boxes,
    ValidatedExtraction Validated,
    string DocumentId,
    string FirmId,
    string CaseId,
    string ParticipantId,
    int TaxYear,
    string SourceReference,
    T4Slip Slip,
    T4MappingResult Mapping);
