using System.Reflection;
using CaseReview.Models;
using TaxLogicEngine.Models;

namespace CaseReview.Tests.Models;

public sealed class AccountantReviewPackageDecisionTests
{
    [Fact]
    public void RecordDecision_NullDescriptor_FailsClosedWithoutThrowing()
    {
        var package = BuildPackage();

        var exception = Record.Exception(() => package.RecordDecision(null));

        Assert.Null(exception);
        AssertSingleFailure(
            package.RecordDecision(null),
            ReviewFields.Decision,
            "A review decision is required.");
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_UndefinedDecision_FailsClosed()
    {
        var package = BuildPackage();
        var descriptor = Decision(decision: (SignOffDecision)int.MaxValue);

        var result = package.RecordDecision(descriptor);

        AssertSingleFailure(
            result,
            ReviewFields.Decision,
            "Decision must be a defined sign-off decision.");
        Assert.Null(package.SignOff);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RecordDecision_BlankReviewerReference_FailsClosed(
        string? reviewerReference)
    {
        var package = BuildPackage();

        var result = package.RecordDecision(
            Decision(reviewerReference: reviewerReference));

        AssertSingleFailure(
            result,
            ReviewFields.ReviewerReference,
            "A reviewer reference is required.");
        Assert.Null(package.SignOff);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RecordDecision_BlankDecidedAtReference_FailsClosed(
        string? decidedAtReference)
    {
        var package = BuildPackage();

        var result = package.RecordDecision(
            Decision(decidedAtReference: decidedAtReference));

        AssertSingleFailure(
            result,
            ReviewFields.DecidedAtReference,
            "A decided-at reference is required.");
        Assert.Null(package.SignOff);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RecordDecision_RejectedWithoutNonBlankNote_FailsClosed(
        string? note)
    {
        var package = BuildPackage();

        var result = package.RecordDecision(
            Decision(decision: SignOffDecision.Rejected, note: note));

        AssertSingleFailure(
            result,
            ReviewFields.Note,
            "A note is required for this decision.");
        Assert.Null(package.SignOff);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void RecordDecision_ChangesRequestedWithoutNonBlankNote_FailsClosed(
        string? note)
    {
        var package = BuildPackage();

        var result = package.RecordDecision(
            Decision(
                decision: SignOffDecision.ChangesRequested,
                note: note));

        AssertSingleFailure(
            result,
            ReviewFields.Note,
            "A note is required for this decision.");
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_SecondDecision_FailsAndKeepsExistingSignOff()
    {
        var original = BuildPackage();
        var firstResult = original.RecordDecision(Decision());
        Assert.True(firstResult.IsSuccess);
        var signed = firstResult.Value!;
        var existingSignOff = Assert.IsType<ReviewSignOff>(signed.SignOff);

        var secondResult = signed.RecordDecision(
            Decision(
                decision: SignOffDecision.Rejected,
                reviewerReference: "second-reviewer",
                decidedAtReference: "second-decided-at",
                note: "second-note"));

        AssertSingleFailure(
            secondResult,
            ReviewFields.SignOff,
            "A review sign-off has already been recorded.");
        Assert.Same(existingSignOff, signed.SignOff);
        Assert.Null(original.SignOff);
    }

    [Fact]
    public void RecordDecision_AlreadySigned_DoesNotEvaluateInvalidDescriptor()
    {
        var signed = BuildPackage()
            .RecordDecision(Decision())
            .Value!;
        var invalid = new ReviewDecisionDescriptor(
            (SignOffDecision)int.MaxValue,
            null,
            null,
            null,
            null);

        var result = signed.RecordDecision(invalid);

        AssertSingleFailure(
            result,
            ReviewFields.SignOff,
            "A review sign-off has already been recorded.");
    }

    [Fact]
    public void RecordDecision_NullDescriptor_PrecedesAlreadySignedGuard()
    {
        var signed = BuildPackage()
            .RecordDecision(Decision())
            .Value!;

        var result = signed.RecordDecision(null);

        AssertSingleFailure(
            result,
            ReviewFields.Decision,
            "A review decision is required.");
        Assert.NotNull(signed.SignOff);
    }

    [Fact]
    public void RecordDecision_ApprovalWithUnmappedBoxesAndAbsentAcknowledgement_FailsClosed()
    {
        var package = BuildPackage(["unmapped-a"]);

        var result = package.RecordDecision(
            Decision(acknowledgement: null));

        AssertSingleFailure(
            result,
            ReviewFields.UnmappedBoxAcknowledgement,
            "Approval requires an unmapped-box acknowledgement.");
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_ApprovalWithUnmappedBoxesAndNegativeAcknowledgement_FailsClosed()
    {
        var package = BuildPackage(["unmapped-a"]);

        var result = package.RecordDecision(
            Decision(acknowledgement: false));

        AssertSingleFailure(
            result,
            ReviewFields.UnmappedBoxAcknowledgement,
            "Approval requires an affirmative unmapped-box acknowledgement.");
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_InvalidDescriptorErrors_AccumulateInFixedOrder()
    {
        var package = BuildPackage();
        var descriptor = new ReviewDecisionDescriptor(
            (SignOffDecision)int.MaxValue,
            null,
            " ",
            note: null,
            unmappedBoxesAcknowledged: null);

        var result = package.RecordDecision(descriptor);

        AssertFailureFields(
            result,
            ReviewFields.Decision,
            ReviewFields.ReviewerReference,
            ReviewFields.DecidedAtReference);
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_RequiredNoteError_FollowsBlankReferences()
    {
        var package = BuildPackage();
        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.ChangesRequested,
            " ",
            null,
            note: "\t",
            unmappedBoxesAcknowledged: null);

        var result = package.RecordDecision(descriptor);

        AssertFailureFields(
            result,
            ReviewFields.ReviewerReference,
            ReviewFields.DecidedAtReference,
            ReviewFields.Note);
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_AcknowledgementError_IsLastInFixedOrder()
    {
        var package = BuildPackage(
            Array.Empty<string>(),
            ["unmapped-on-later-slip"]);
        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.Approved,
            null,
            " ",
            note: null,
            unmappedBoxesAcknowledged: null);

        var result = package.RecordDecision(descriptor);

        AssertFailureFields(
            result,
            ReviewFields.ReviewerReference,
            ReviewFields.DecidedAtReference,
            ReviewFields.UnmappedBoxAcknowledgement);
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_FailureErrorsNeverEchoCallerValues()
    {
        const string reviewer = "REVIEWER-MARKER-123456789";
        const string decidedAt = "DECIDED-MARKER-123456789";
        const string note = "NOTE-MARKER-123456789";
        var package = BuildPackage(["UNMAPPED-MARKER"]);
        var descriptor = new ReviewDecisionDescriptor(
            (SignOffDecision)int.MaxValue,
            reviewer,
            decidedAt,
            note,
            unmappedBoxesAcknowledged: false);

        var result = package.RecordDecision(descriptor);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        var error = Assert.Single(result.Errors);
        Assert.Equal(ReviewFields.Decision, error.Field);
        AssertSafeError(
            error,
            reviewer,
            decidedAt,
            note,
            "UNMAPPED-MARKER",
            "123456789");
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_ApprovedWithoutUnmappedBoxes_ReturnsSignedReplacement()
    {
        const string reviewer = "  reviewer-MARKER  ";
        const string decidedAt = "\tdecided-at-MARKER ";
        var original = BuildPackage();

        var result = original.RecordDecision(
            Decision(
                reviewerReference: reviewer,
                decidedAtReference: decidedAt,
                note: null,
                acknowledgement: null));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var replacement = Assert.IsType<AccountantReviewPackage>(result.Value);
        Assert.NotSame(original, replacement);
        Assert.Null(original.SignOff);
        var signOff = Assert.IsType<ReviewSignOff>(replacement.SignOff);
        Assert.Equal(SignOffDecision.Approved, signOff.Decision);
        Assert.Equal(reviewer, signOff.ReviewerReference, StringComparer.Ordinal);
        Assert.Equal(decidedAt, signOff.DecidedAtReference, StringComparer.Ordinal);
        Assert.Null(signOff.Note);
        Assert.Null(signOff.UnmappedBoxesAcknowledged);
    }

    [Theory]
    [InlineData(SignOffDecision.ChangesRequested)]
    [InlineData(SignOffDecision.Rejected)]
    public void RecordDecision_NonApprovalDecision_PreservesExactNote(
        SignOffDecision decision)
    {
        const string note = "  NOTE-MARKER-123456789  ";
        var original = BuildPackage();

        var result = original.RecordDecision(
            Decision(decision: decision, note: note));

        Assert.True(result.IsSuccess);
        var signOff = Assert.IsType<ReviewSignOff>(result.Value!.SignOff);
        Assert.Equal(decision, signOff.Decision);
        Assert.Equal(note, signOff.Note, StringComparer.Ordinal);
        Assert.Null(original.SignOff);
    }

    [Fact]
    public void RecordDecision_ReplacementPreservesExactReviewedContent()
    {
        var original = BuildPackage(
            ["unmapped-a", "unmapped-b"],
            Array.Empty<string>());

        var result = original.RecordDecision(
            Decision(acknowledgement: true));

        Assert.True(result.IsSuccess);
        var replacement = result.Value!;
        Assert.Equal(original.ReviewedSlips, replacement.ReviewedSlips);
        Assert.Equal(original.ReviewedSlips.Count, replacement.ReviewedSlips.Count);

        for (var index = 0; index < original.ReviewedSlips.Count; index++)
        {
            var expected = original.ReviewedSlips[index];
            var actual = replacement.ReviewedSlips[index];
            Assert.Same(expected, actual);
            Assert.Same(expected.Slip, actual.Slip);
            Assert.Same(expected.LineEntries, actual.LineEntries);
            Assert.Same(expected.BoxAmounts, actual.BoxAmounts);
            Assert.Same(expected.UnmappedBoxCodes, actual.UnmappedBoxCodes);
        }

        Assert.Null(original.SignOff);
        Assert.NotNull(replacement.SignOff);
    }

    [Fact]
    public void RecordDecision_OpaqueNonDateDecidedAtReference_IsNotInterpreted()
    {
        const string decidedAtReference = "not-a-timestamp";
        var package = BuildPackage();

        var result = package.RecordDecision(
            Decision(decidedAtReference: decidedAtReference));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            decidedAtReference,
            result.Value!.SignOff!.DecidedAtReference,
            StringComparer.Ordinal);
    }

    [Fact]
    public void RecordDecision_SameInputsTwice_ProduceEqualReplacements()
    {
        var package = BuildPackage(["unmapped-a"]);
        var descriptor = Decision(
            reviewerReference: "reviewer",
            decidedAtReference: "opaque-reference",
            note: "optional-note",
            acknowledgement: true);

        var first = package.RecordDecision(descriptor);
        var second = package.RecordDecision(descriptor);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotSame(first.Value, second.Value);
        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Value!.GetHashCode(), second.Value!.GetHashCode());
        Assert.Null(package.SignOff);
    }

    [Fact]
    public void RecordDecision_OneAffirmativeAcknowledgementCoversAllSlipsAndCodes()
    {
        var package = BuildPackage(
            ["unmapped-a", "unmapped-b"],
            Array.Empty<string>(),
            ["unmapped-c", "unmapped-d"]);

        var result = package.RecordDecision(
            Decision(acknowledgement: true));

        Assert.True(result.IsSuccess);
        var signOff = Assert.IsType<ReviewSignOff>(result.Value!.SignOff);
        Assert.True(signOff.UnmappedBoxesAcknowledged);
        Assert.Equal(3, result.Value.ReviewedSlips.Count);
        Assert.Equal(
            new[]
            {
                "unmapped-a",
                "unmapped-b",
                "unmapped-c",
                "unmapped-d",
            },
            result.Value.ReviewedSlips.SelectMany(
                reviewed => reviewed.UnmappedBoxCodes));
        Assert.Null(package.SignOff);
    }

    [Theory]
    [InlineData(SignOffDecision.ChangesRequested)]
    [InlineData(SignOffDecision.Rejected)]
    public void RecordDecision_NonApprovalWithUnmappedBoxes_DoesNotRequireAcknowledgement(
        SignOffDecision decision)
    {
        var package = BuildPackage(["unmapped-a"]);

        var result = package.RecordDecision(
            Decision(
                decision: decision,
                note: "required-note",
                acknowledgement: null));

        Assert.True(result.IsSuccess);
        var signOff = result.Value!.SignOff!;
        Assert.Equal(decision, signOff.Decision);
        Assert.Null(signOff.UnmappedBoxesAcknowledged);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordDecision_ApprovalWithoutUnmappedBoxes_PreservesOptionalAcknowledgement(
        bool? acknowledgement)
    {
        var package = BuildPackage();

        var result = package.RecordDecision(
            Decision(acknowledgement: acknowledgement));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            acknowledgement,
            result.Value!.SignOff!.UnmappedBoxesAcknowledged);
    }

    [Fact]
    public void RecordDecision_ApprovedOptionalBlankNote_IsPreservedExactly()
    {
        const string blankNote = " \t ";
        var package = BuildPackage();

        var result = package.RecordDecision(
            Decision(note: blankNote));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            blankNote,
            result.Value!.SignOff!.Note,
            StringComparer.Ordinal);
    }

    [Fact]
    public void AcknowledgementContract_IsPackageLevelWithNoPerCodeOrDisputeMember()
    {
        Assert.Equal(
            new[] { nameof(ReviewDecisionDescriptor.UnmappedBoxesAcknowledged) },
            AcknowledgementProperties(typeof(ReviewDecisionDescriptor)));
        Assert.Equal(
            new[] { nameof(ReviewSignOff.UnmappedBoxesAcknowledged) },
            AcknowledgementProperties(typeof(ReviewSignOff)));
        Assert.Empty(AcknowledgementProperties(typeof(AccountantReviewPackage)));
        Assert.Empty(AcknowledgementProperties(typeof(ReviewedSlip)));
        Assert.Empty(AcknowledgementProperties(typeof(ReviewedBoxAmount)));

        var publicTypes = new[]
        {
            typeof(ReviewDecisionDescriptor),
            typeof(ReviewSignOff),
            typeof(AccountantReviewPackage),
            typeof(ReviewedSlip),
            typeof(ReviewedBoxAmount),
        };

        Assert.All(
            publicTypes.SelectMany(type => type.GetMembers()),
            member =>
            {
                Assert.DoesNotContain(
                    "Disposition",
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "Dispute",
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    private static ReviewDecisionDescriptor Decision(
        SignOffDecision decision = SignOffDecision.Approved,
        string? reviewerReference = "reviewer-1",
        string? decidedAtReference = "decided-at-1",
        string? note = null,
        bool? acknowledgement = null) =>
        new(
            decision,
            reviewerReference,
            decidedAtReference,
            note,
            acknowledgement);

    private static AccountantReviewPackage BuildPackage(
        params string[][] unmappedCodesBySlip)
    {
        if (unmappedCodesBySlip.Length == 0)
        {
            unmappedCodesBySlip = [Array.Empty<string>()];
        }

        var reviewedSlips = unmappedCodesBySlip
            .Select(
                (unmappedCodes, index) =>
                    new ReviewedSlip(
                        $"document-{index}",
                        2025,
                        $"source-{index}",
                        BuildSlip(),
                        [
                            new TaxLineEntry(
                                $"line-{index}",
                                10.01m,
                                [$"source-box-{index}"]),
                        ],
                        [
                            new ReviewedBoxAmount(
                                $"box-{index}",
                                10.01m),
                        ],
                        unmappedCodes))
            .ToArray();

        return new AccountantReviewPackage(
            "firm-1",
            "case-1",
            "participant-1",
            2025,
            reviewedSlips,
            signOff: null);
    }

    private static T4Slip BuildSlip() =>
        new()
        {
            Year = 2025,
            EmployerName = "Employer",
            EmployeeName = "Employee",
            DentalBenefitCode = "1",
        };

    private static ValidationError AssertSingleFailure(
        ParseResult<AccountantReviewPackage> result,
        string field,
        string message)
    {
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.Field, StringComparer.Ordinal);
        Assert.Equal(message, error.Message, StringComparer.Ordinal);
        return error;
    }

    private static void AssertFailureFields(
        ParseResult<AccountantReviewPackage> result,
        params string[] fields)
    {
        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(fields, result.Errors.Select(error => error.Field));
    }

    private static void AssertSafeError(
        ValidationError error,
        params string[] forbiddenValues)
    {
        foreach (var forbiddenValue in forbiddenValues)
        {
            Assert.DoesNotContain(
                forbiddenValue,
                error.Field,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                forbiddenValue,
                error.Message,
                StringComparison.Ordinal);
        }
    }

    private static string[] AcknowledgementProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(
                property => property.Name.Contains(
                    "Acknowledg",
                    StringComparison.Ordinal))
            .Select(property => property.Name)
            .ToArray();
}
