using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaseReview.Models;
using TaxLogicEngine.Models;

namespace CaseReview.Tests.Models;

public sealed class ReviewContentModelTests
{
    [Fact]
    public void ReviewedBoxAmount_PreservesValuesAndHasOrdinalValueEquality()
    {
        var first = new ReviewedBoxAmount("14a", 123.45m);
        var equivalent = new ReviewedBoxAmount("14a", 123.45m);

        Assert.Equal("14a", first.BoxCode);
        Assert.Equal(123.45m, first.Amount);
        Assert.True(first.Equals(first));
        Assert.True(first.Equals(equivalent));
        Assert.True(first.Equals((object)equivalent));
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first.GetHashCode(), first.GetHashCode());

        Assert.False(first.Equals(new ReviewedBoxAmount("14A", 123.45m)));
        Assert.False(first.Equals(new ReviewedBoxAmount("14a", 123.46m)));
        Assert.False(first.Equals((ReviewedBoxAmount?)null));
        Assert.False(first.Equals("14a"));
    }

    [Fact]
    public void ReviewedSlip_PreservesEveryValueAndTheExactSlipReference()
    {
        var slip = BuildSlip();
        var lineEntries = new[]
        {
            new TaxLineEntry("line-b", 20.02m, new[] { "box-2", "box-1" }),
            new TaxLineEntry("line-a", 10.01m, new[] { "box-3" }),
        };
        var boxAmounts = new[]
        {
            new ReviewedBoxAmount("box-2", 20.02m),
            new ReviewedBoxAmount("box-1", 10.01m),
        };
        var unmappedCodes = new[] { "code-z", "code-a" };

        var reviewed = new ReviewedSlip(
            " Document-Marker ",
            2025,
            " Source-Marker ",
            slip,
            lineEntries,
            boxAmounts,
            unmappedCodes);

        Assert.Equal(" Document-Marker ", reviewed.DocumentId);
        Assert.Equal(2025, reviewed.TaxYear);
        Assert.Equal(" Source-Marker ", reviewed.SourceReference);
        Assert.Same(slip, reviewed.Slip);
        Assert.NotSame(lineEntries, reviewed.LineEntries);
        Assert.NotSame(lineEntries[0], reviewed.LineEntries[0]);
        Assert.Equal(
            new[] { ("line-b", 20.02m), ("line-a", 10.01m) },
            reviewed.LineEntries
                .Select(entry => (entry.LineNumber, entry.Amount))
                .ToArray());
        Assert.Equal(
            new[] { "box-2", "box-1" },
            reviewed.LineEntries[0].SourceBoxCodes);
        Assert.Equal(
            new[] { ("box-2", 20.02m), ("box-1", 10.01m) },
            reviewed.BoxAmounts
                .Select(amount => (amount.BoxCode, amount.Amount))
                .ToArray());
        Assert.Equal(new[] { "code-z", "code-a" }, reviewed.UnmappedBoxCodes);
    }

    [Fact]
    public void ReviewedSlip_DefensivelySnapshotsEveryCallerOwnedSequence()
    {
        var sourceCodes = new List<string> { "box-1", "box-2" };
        var lineEntries = new List<TaxLineEntry>
        {
            new("line-1", 10.01m, sourceCodes),
        };
        var boxAmounts = new List<ReviewedBoxAmount>
        {
            new("box-1", 10.01m),
        };
        var unmappedCodes = new List<string> { "code-1" };
        var reviewed = new ReviewedSlip(
            "document",
            2025,
            "source",
            BuildSlip(),
            lineEntries,
            boxAmounts,
            unmappedCodes);
        var hashBeforeMutation = reviewed.GetHashCode();

        sourceCodes[0] = "mutated-source-code";
        sourceCodes.Add("added-source-code");
        lineEntries[0] = new TaxLineEntry("mutated-line", 999m);
        lineEntries.Add(new TaxLineEntry("added-line", 888m));
        boxAmounts[0] = new ReviewedBoxAmount("mutated-box", 777m);
        boxAmounts.Add(new ReviewedBoxAmount("added-box", 666m));
        unmappedCodes[0] = "mutated-code";
        unmappedCodes.Add("added-code");

        Assert.Equal(new[] { "box-1", "box-2" }, reviewed.LineEntries[0].SourceBoxCodes);
        Assert.Equal("line-1", Assert.Single(reviewed.LineEntries).LineNumber);
        Assert.Equal(("box-1", 10.01m), (
            Assert.Single(reviewed.BoxAmounts).BoxCode,
            Assert.Single(reviewed.BoxAmounts).Amount));
        Assert.Equal(new[] { "code-1" }, reviewed.UnmappedBoxCodes);
        Assert.Equal(hashBeforeMutation, reviewed.GetHashCode());
    }

    [Fact]
    public void ReviewedSlip_CollectionsAndNestedProvenanceAreNonWritable()
    {
        var reviewed = BuildReviewedSlip(BuildSlip(), suffix: "a");

        AssertReadOnly(reviewed.LineEntries, new TaxLineEntry("new-line", 1m));
        AssertReadOnly(reviewed.LineEntries[0].SourceBoxCodes, "new-box");
        AssertReadOnly(reviewed.BoxAmounts, new ReviewedBoxAmount("new-box", 1m));
        AssertReadOnly(reviewed.UnmappedBoxCodes, "new-code");
    }

    [Fact]
    public void ReviewedSlip_EquivalentSnapshotsCompareEqualWithEqualHashes()
    {
        var slip = BuildSlip();
        var first = BuildReviewedSlip(slip, suffix: "a");
        var equivalent = BuildReviewedSlip(slip, suffix: "a");

        Assert.NotSame(first, equivalent);
        Assert.True(first.Equals(first));
        Assert.True(first.Equals(equivalent));
        Assert.True(first.Equals((object)equivalent));
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first.GetHashCode(), first.GetHashCode());
    }

    [Fact]
    public void ReviewedSlip_EqualityRequiresTheExactSlipReference()
    {
        var first = BuildReviewedSlip(BuildSlip(), suffix: "a");
        var sameValuesWithDifferentSlip = BuildReviewedSlip(BuildSlip(), suffix: "a");

        Assert.False(first.Equals(sameValuesWithDifferentSlip));
    }

    [Fact]
    public void ReviewedSlip_ReorderedOrDifferentContentComparesUnequal()
    {
        var slip = BuildSlip();
        var baseline = BuildReviewedSlip(slip, suffix: "a");

        AssertUnequal(baseline, BuildReviewedSlip(slip, suffix: "A"));
        AssertUnequal(baseline, BuildReviewedSlip(slip, taxYear: 2024));
        AssertUnequal(baseline, BuildReviewedSlip(slip, sourceReference: "different"));
        AssertUnequal(
            baseline,
            BuildReviewedSlip(
                slip,
                lineEntries:
                [
                    new TaxLineEntry("line-2", 20.02m, new[] { "box-2", "box-1" }),
                    new TaxLineEntry("line-1", 10.01m, new[] { "box-1", "box-2" }),
                ]));
        AssertUnequal(
            baseline,
            BuildReviewedSlip(
                slip,
                lineEntries:
                [
                    new TaxLineEntry("line-1", 10.01m, new[] { "box-2", "box-1" }),
                    new TaxLineEntry("line-2", 20.02m, new[] { "box-2", "box-1" }),
                ]));
        AssertUnequal(
            baseline,
            BuildReviewedSlip(
                slip,
                lineEntries:
                [
                    new TaxLineEntry("line-1", 10.02m, new[] { "box-1", "box-2" }),
                    new TaxLineEntry("line-2", 20.02m, new[] { "box-2", "box-1" }),
                ]));
        AssertUnequal(
            baseline,
            BuildReviewedSlip(
                slip,
                boxAmounts:
                [
                    new ReviewedBoxAmount("box-2", 20.02m),
                    new ReviewedBoxAmount("box-1", 10.01m),
                ]));
        AssertUnequal(
            baseline,
            BuildReviewedSlip(slip, unmappedBoxCodes: ["unmapped-2", "unmapped-1"]));
        Assert.False(baseline.Equals((ReviewedSlip?)null));
        Assert.False(baseline.Equals(new object()));
    }

    [Fact]
    public void AccountantReviewPackage_PreservesUnsignedStateAndSnapshotsSlipOrder()
    {
        var slip = BuildSlip();
        var first = BuildReviewedSlip(slip, suffix: "a");
        var second = BuildReviewedSlip(slip, suffix: "b");
        var callerOwned = new List<ReviewedSlip> { second, first };
        var package = new AccountantReviewPackage(
            " Firm ",
            " Case ",
            " Participant ",
            2025,
            callerOwned,
            signOff: null);
        var hashBeforeMutation = package.GetHashCode();

        callerOwned.Clear();
        callerOwned.Add(first);

        Assert.Equal(" Firm ", package.FirmId);
        Assert.Equal(" Case ", package.CaseId);
        Assert.Equal(" Participant ", package.ParticipantId);
        Assert.Equal(2025, package.TaxYear);
        Assert.Equal(new[] { second, first }, package.ReviewedSlips);
        Assert.Null(package.SignOff);
        Assert.Equal(hashBeforeMutation, package.GetHashCode());
        AssertReadOnly(package.ReviewedSlips, first);
    }

    [Fact]
    public void AccountantReviewPackage_EquivalentUnsignedValuesCompareEqualWithEqualHashes()
    {
        var slip = BuildSlip();
        var first = BuildPackage(slip);
        var equivalent = BuildPackage(slip);

        Assert.NotSame(first, equivalent);
        Assert.True(first.Equals(first));
        Assert.True(first.Equals(equivalent));
        Assert.True(first.Equals((object)equivalent));
        Assert.Equal(first.GetHashCode(), equivalent.GetHashCode());
        Assert.Equal(first.GetHashCode(), first.GetHashCode());
    }

    [Fact]
    public void AccountantReviewPackage_ReorderedOrDifferentCoreContentComparesUnequal()
    {
        var slip = BuildSlip();
        var baseline = BuildPackage(slip);

        AssertUnequal(baseline, BuildPackage(slip, firmId: "FIRM"));
        AssertUnequal(baseline, BuildPackage(slip, caseId: "CASE"));
        AssertUnequal(baseline, BuildPackage(slip, participantId: "PARTICIPANT"));
        AssertUnequal(baseline, BuildPackage(slip, taxYear: 2024));
        AssertUnequal(
            baseline,
            BuildPackage(
                slip,
                reviewedSlips:
                [
                    BuildReviewedSlip(slip, suffix: "b"),
                    BuildReviewedSlip(slip, suffix: "a"),
                ]));
        AssertUnequal(
            baseline,
            BuildPackage(
                slip,
                reviewedSlips: [BuildReviewedSlip(slip, suffix: "a")]));
        Assert.False(baseline.Equals((AccountantReviewPackage?)null));
        Assert.False(baseline.Equals(new object()));
    }

    [Fact]
    public void T02ModelPropertiesRemainGetOnly()
    {
        foreach (var type in new[]
                 {
                     typeof(ReviewedBoxAmount),
                     typeof(ReviewedSlip),
                     typeof(AccountantReviewPackage),
                 })
        {
            Assert.All(
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public),
                property => Assert.Null(property.SetMethod));
        }
    }

    [Fact]
    public void RenderingAndJsonExposeOnlyFixedStructuralFacts()
    {
        const string sin = "123456789";
        var slip = BuildSlip(sin);
        var reviewed = new ReviewedSlip(
            "DOCUMENT-PII",
            2025,
            "SOURCE-PII",
            slip,
            [
                new TaxLineEntry(
                    "LINE-PII",
                    9876543.21m,
                    new[] { "SOURCE-BOX-PII", "SOURCE-BOX-2-PII" }),
            ],
            [new ReviewedBoxAmount("BOX-PII", 9876543.21m)],
            ["UNMAPPED-PII"]);
        var package = new AccountantReviewPackage(
            "FIRM-PII",
            "CASE-PII",
            "PARTICIPANT-PII",
            2025,
            new[] { reviewed },
            signOff: null);

        Assert.Equal("ReviewedBoxAmount", reviewed.BoxAmounts[0].ToString());
        Assert.Equal(
            "ReviewedSlip(TaxYear: 2025, LineEntries: 1, BoxAmounts: 1, UnmappedBoxes: 1, HasSin: True)",
            reviewed.ToString());
        Assert.Equal(
            "AccountantReviewPackage(TaxYear: 2025, ReviewedSlips: 1, UnmappedBoxes: 1, HasSignOff: False)",
            package.ToString());
        Assert.Equal("{}", JsonSerializer.Serialize(reviewed.BoxAmounts[0]));
        Assert.Equal("""{"TaxYear":2025}""", JsonSerializer.Serialize(reviewed));
        Assert.Equal("""{"TaxYear":2025}""", JsonSerializer.Serialize(package));

        var outputs = new[]
        {
            reviewed.BoxAmounts[0].ToString(),
            reviewed.ToString(),
            package.ToString(),
            JsonSerializer.Serialize(reviewed.BoxAmounts[0]),
            JsonSerializer.Serialize(reviewed),
            JsonSerializer.Serialize(package),
        };
        var forbidden = new[]
        {
            sin,
            "***-**-789",
            "DOCUMENT-PII",
            "SOURCE-PII",
            "LINE-PII",
            "9876543.21",
            "SOURCE-BOX-PII",
            "SOURCE-BOX-2-PII",
            "BOX-PII",
            "UNMAPPED-PII",
            "FIRM-PII",
            "CASE-PII",
            "PARTICIPANT-PII",
            "Employer PII",
            "Employee PII",
        };

        Assert.All(outputs, output =>
        {
            Assert.DoesNotMatch(new Regex(@"\d{9}", RegexOptions.CultureInvariant), output);
            Assert.All(
                forbidden,
                value => Assert.DoesNotContain(value, output, StringComparison.Ordinal));
        });
    }

    private static AccountantReviewPackage BuildPackage(
        T4Slip slip,
        string firmId = "firm",
        string caseId = "case",
        string participantId = "participant",
        int taxYear = 2025,
        IReadOnlyList<ReviewedSlip>? reviewedSlips = null) =>
        new(
            firmId,
            caseId,
            participantId,
            taxYear,
            reviewedSlips ??
            [
                BuildReviewedSlip(slip, suffix: "a"),
                BuildReviewedSlip(slip, suffix: "b"),
            ],
            signOff: null);

    private static ReviewedSlip BuildReviewedSlip(
        T4Slip slip,
        string suffix = "a",
        int taxYear = 2025,
        string? sourceReference = null,
        IReadOnlyList<TaxLineEntry>? lineEntries = null,
        IReadOnlyList<ReviewedBoxAmount>? boxAmounts = null,
        IReadOnlyList<string>? unmappedBoxCodes = null) =>
        new(
            $"document-{suffix}",
            taxYear,
            sourceReference ?? $"source-{suffix}",
            slip,
            lineEntries ??
            [
                new TaxLineEntry("line-1", 10.01m, new[] { "box-1", "box-2" }),
                new TaxLineEntry("line-2", 20.02m, new[] { "box-2", "box-1" }),
            ],
            boxAmounts ??
            [
                new ReviewedBoxAmount("box-1", 10.01m),
                new ReviewedBoxAmount("box-2", 20.02m),
            ],
            unmappedBoxCodes ?? ["unmapped-1", "unmapped-2"]);

    private static T4Slip BuildSlip(string sin = "000000000") =>
        new()
        {
            Year = 2025,
            EmployerName = "Employer PII",
            EmployeeName = "Employee PII",
            DentalBenefitCode = "1",
            Sin = new RecipientIdentifier(sin),
        };

    private static void AssertReadOnly<T>(IReadOnlyList<T> values, T item)
    {
        var collection = Assert.IsAssignableFrom<ICollection<T>>(values);

        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection.Add(item));
        Assert.Throws<NotSupportedException>(() => collection.Remove(item));
        Assert.Throws<NotSupportedException>(() => collection.Clear());

        var nonGeneric = Assert.IsAssignableFrom<IList>(values);
        Assert.True(nonGeneric.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => nonGeneric.Add(item));
    }

    private static void AssertUnequal<T>(T left, T right)
    {
        Assert.False(EqualityComparer<T>.Default.Equals(left, right));
        Assert.NotEqual(left, right);
    }
}
