using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CaseReview.Models;

namespace CaseReview.Tests.Models;

public sealed class ReviewDecisionModelTests
{
    public static TheoryData<
        SignOffDecision,
        string?,
        string?,
        string?,
        bool?> DescriptorShapes =>
        new()
        {
            {
                (SignOffDecision)0,
                null,
                null,
                null,
                null
            },
            {
                (SignOffDecision)987654321,
                string.Empty,
                string.Empty,
                string.Empty,
                false
            },
            {
                SignOffDecision.ChangesRequested,
                " \t ",
                "not-a-timestamp",
                " \r\n ",
                true
            },
            {
                SignOffDecision.Rejected,
                " Reviewer Exact ",
                " Decided Exact ",
                " Note Exact ",
                null
            },
        };

    [Theory]
    [MemberData(nameof(DescriptorShapes))]
    public void Descriptor_PreservesEveryInputWithoutValidationOrNormalization(
        SignOffDecision decision,
        string? reviewerReference,
        string? decidedAtReference,
        string? note,
        bool? acknowledgement)
    {
        var exception = Record.Exception(
            () => new ReviewDecisionDescriptor(
                decision,
                reviewerReference,
                decidedAtReference,
                note,
                acknowledgement));

        Assert.Null(exception);

        var descriptor = new ReviewDecisionDescriptor(
            decision,
            reviewerReference,
            decidedAtReference,
            note,
            acknowledgement);

        Assert.Equal(decision, descriptor.Decision);
        Assert.Same(reviewerReference, descriptor.ReviewerReference);
        Assert.Same(decidedAtReference, descriptor.DecidedAtReference);
        Assert.Same(note, descriptor.Note);
        Assert.Equal(acknowledgement, descriptor.UnmappedBoxesAcknowledged);
    }

    [Fact]
    public void Descriptor_DoesNotInterpretTheDecidedAtReference()
    {
        const string opaqueReference = "not-a-timestamp";

        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.Approved,
            "reviewer",
            opaqueReference,
            note: null,
            unmappedBoxesAcknowledged: null);

        Assert.Same(opaqueReference, descriptor.DecidedAtReference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void DescriptorAndSignOff_PreserveAllAcknowledgementStates(bool? acknowledgement)
    {
        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.Approved,
            "reviewer",
            "decided-at",
            note: null,
            acknowledgement);
        var signOff = new ReviewSignOff(
            descriptor.Decision,
            descriptor.ReviewerReference!,
            descriptor.DecidedAtReference!,
            descriptor.Note,
            descriptor.UnmappedBoxesAcknowledged);

        Assert.Equal(acknowledgement, descriptor.UnmappedBoxesAcknowledged);
        Assert.Equal(acknowledgement, signOff.UnmappedBoxesAcknowledged);
    }

    [Fact]
    public void SignOff_PreservesEveryAcceptedValueExactly()
    {
        const string reviewer = " Reviewer Exact ";
        const string decidedAt = " not-a-timestamp ";
        const string note = " Note Exact ";
        var undefinedDecision = (SignOffDecision)12345;

        var signOff = new ReviewSignOff(
            undefinedDecision,
            reviewer,
            decidedAt,
            note,
            unmappedBoxesAcknowledged: false);

        Assert.Equal(undefinedDecision, signOff.Decision);
        Assert.Same(reviewer, signOff.ReviewerReference);
        Assert.Same(decidedAt, signOff.DecidedAtReference);
        Assert.Same(note, signOff.Note);
        Assert.False(signOff.UnmappedBoxesAcknowledged);
    }

    [Fact]
    public void SignOff_EquivalentValuesCompareEqualWithEqualStableHashes()
    {
        var first = BuildSignOff();
        var equivalent = BuildSignOff();

        Assert.NotSame(first, equivalent);
        Assert.True(first.Equals(first));
        Assert.True(first.Equals(equivalent));
        Assert.True(first.Equals((object)equivalent));
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first.GetHashCode(), first.GetHashCode());
    }

    [Fact]
    public void SignOff_EqualityCoversEveryMemberOrdinally()
    {
        var baseline = BuildSignOff();

        AssertUnequal(baseline, BuildSignOff(decision: SignOffDecision.Rejected));
        AssertUnequal(baseline, BuildSignOff(reviewerReference: "REVIEWER"));
        AssertUnequal(baseline, BuildSignOff(decidedAtReference: "DECIDED-AT"));
        AssertUnequal(baseline, BuildSignOff(note: "NOTE"));
        AssertUnequal(baseline, BuildSignOff(note: null));
        AssertUnequal(baseline, BuildSignOff(acknowledgement: false));
        AssertUnequal(baseline, BuildSignOff(acknowledgement: null));
        AssertUnequal(
            BuildSignOff(acknowledgement: null),
            BuildSignOff(acknowledgement: false));
        Assert.False(baseline.Equals((ReviewSignOff?)null));
        Assert.False(baseline.Equals(new object()));
    }

    [Fact]
    public void EquivalentSignedPackagesCompareEqualWithEqualHashes()
    {
        var first = new AccountantReviewPackage(
            "firm",
            "case",
            "participant",
            2025,
            Array.Empty<ReviewedSlip>(),
            BuildSignOff());
        var equivalent = new AccountantReviewPackage(
            "firm",
            "case",
            "participant",
            2025,
            Array.Empty<ReviewedSlip>(),
            BuildSignOff());

        Assert.Equal(first, equivalent);
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());

        var differentSignOff = new AccountantReviewPackage(
            "firm",
            "case",
            "participant",
            2025,
            Array.Empty<ReviewedSlip>(),
            BuildSignOff(acknowledgement: false));

        Assert.NotEqual(first, differentSignOff);
    }

    [Fact]
    public void DecisionModels_ExposeOnlySafeEnumAndAcknowledgementInJson()
    {
        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.Approved,
            "REVIEWER-PII-123456789",
            "DECIDED-PII-123456789",
            "NOTE-PII-123456789",
            unmappedBoxesAcknowledged: null);
        var signOff = new ReviewSignOff(
            SignOffDecision.ChangesRequested,
            "REVIEWER-PII-123456789",
            "DECIDED-PII-123456789",
            "NOTE-PII-123456789",
            unmappedBoxesAcknowledged: false);

        Assert.Equal(
            """{"Decision":1,"UnmappedBoxesAcknowledged":null}""",
            JsonSerializer.Serialize(descriptor));
        Assert.Equal(
            """{"Decision":2,"UnmappedBoxesAcknowledged":false}""",
            JsonSerializer.Serialize(signOff));
    }

    [Fact]
    public void DecisionModelRenderingAndJsonNeverLeakCallerStringsOrSinLikeRuns()
    {
        var descriptor = new ReviewDecisionDescriptor(
            SignOffDecision.Approved,
            "REVIEWER-PII-123456789",
            "DECIDED-PII-987654321",
            "NOTE-PII-111222333",
            unmappedBoxesAcknowledged: true);
        var signOff = new ReviewSignOff(
            descriptor.Decision,
            descriptor.ReviewerReference!,
            descriptor.DecidedAtReference!,
            descriptor.Note,
            descriptor.UnmappedBoxesAcknowledged);

        Assert.Equal(
            "ReviewDecisionDescriptor(HasReviewer: True, HasDecidedAt: True, HasNote: True, HasAcknowledgement: True)",
            descriptor.ToString());
        Assert.Equal(
            "ReviewSignOff(Decision: Approved, HasNote: True, HasAcknowledgement: True)",
            signOff.ToString());

        var outputs = new[]
        {
            descriptor.ToString(),
            signOff.ToString(),
            JsonSerializer.Serialize(descriptor),
            JsonSerializer.Serialize(signOff),
        };
        var forbidden = new[]
        {
            descriptor.ReviewerReference!,
            descriptor.DecidedAtReference!,
            descriptor.Note!,
            "123456789",
            "987654321",
            "111222333",
        };

        Assert.All(outputs, output =>
        {
            Assert.DoesNotMatch(new Regex(@"\d{9}", RegexOptions.CultureInvariant), output);
            Assert.All(
                forbidden,
                value => Assert.DoesNotContain(value, output, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void NullAndBlankInputsAreReportedOnlyAsStructuralPresence()
    {
        var absent = new ReviewDecisionDescriptor(
            (SignOffDecision)0,
            reviewerReference: null,
            decidedAtReference: null,
            note: null,
            unmappedBoxesAcknowledged: null);
        var blank = new ReviewDecisionDescriptor(
            (SignOffDecision)0,
            reviewerReference: string.Empty,
            decidedAtReference: " ",
            note: "\t",
            unmappedBoxesAcknowledged: false);

        Assert.Equal(
            "ReviewDecisionDescriptor(HasReviewer: False, HasDecidedAt: False, HasNote: False, HasAcknowledgement: False)",
            absent.ToString());
        Assert.Equal(
            "ReviewDecisionDescriptor(HasReviewer: True, HasDecidedAt: True, HasNote: True, HasAcknowledgement: True)",
            blank.ToString());
    }

    [Fact]
    public void DecisionModelPropertiesRemainGetOnlyAndCallerStringsRemainJsonIgnored()
    {
        AssertProperties(
            typeof(ReviewDecisionDescriptor),
            nameof(ReviewDecisionDescriptor.ReviewerReference),
            nameof(ReviewDecisionDescriptor.DecidedAtReference),
            nameof(ReviewDecisionDescriptor.Note));
        AssertProperties(
            typeof(ReviewSignOff),
            nameof(ReviewSignOff.ReviewerReference),
            nameof(ReviewSignOff.DecidedAtReference),
            nameof(ReviewSignOff.Note));
    }

    private static ReviewSignOff BuildSignOff(
        SignOffDecision decision = SignOffDecision.Approved,
        string reviewerReference = "reviewer",
        string decidedAtReference = "decided-at",
        string? note = "note",
        bool? acknowledgement = true) =>
        new(
            decision,
            reviewerReference,
            decidedAtReference,
            note,
            acknowledgement);

    private static void AssertProperties(Type type, params string[] ignoredNames)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.All(properties, property => Assert.Null(property.SetMethod));

        foreach (var ignoredName in ignoredNames)
        {
            var property = Assert.Single(
                properties,
                candidate => candidate.Name == ignoredName);

            Assert.NotNull(property.GetCustomAttribute<JsonIgnoreAttribute>());
        }

        foreach (var safeName in new[] { "Decision", "UnmappedBoxesAcknowledged" })
        {
            var property = Assert.Single(
                properties,
                candidate => candidate.Name == safeName);

            Assert.Null(property.GetCustomAttribute<JsonIgnoreAttribute>());
        }
    }

    private static void AssertUnequal(ReviewSignOff left, ReviewSignOff right)
    {
        Assert.False(left.Equals(right));
        Assert.False(left.Equals((object)right));
        Assert.NotEqual(left, right);
    }
}
