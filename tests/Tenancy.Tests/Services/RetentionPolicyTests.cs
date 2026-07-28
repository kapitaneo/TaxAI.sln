using System.Text.RegularExpressions;
using Tenancy.Services;

namespace Tenancy.Tests.Services;

public sealed class RetentionPolicyTests
{
    [Fact]
    public void ComputeRetentionUntilUtc_ReturnsTheExclusiveJanuaryBoundary()
    {
        var retentionUntilUtc = RetentionPolicy.ComputeRetentionUntilUtc(2025);

        Assert.Equal(
            new DateTimeOffset(2033, 1, 1, 0, 0, 0, TimeSpan.Zero),
            retentionUntilUtc);
        Assert.Equal(TimeSpan.Zero, retentionUntilUtc.Offset);
    }

    [Fact]
    public void ComputeRetentionUntilUtc_DerivesTheBoundaryFromTheTaxYearAlone()
    {
        var earlierBoundary = RetentionPolicy.ComputeRetentionUntilUtc(2024);
        var laterBoundary = RetentionPolicy.ComputeRetentionUntilUtc(2025);

        Assert.Equal(earlierBoundary.AddYears(1), laterBoundary);
    }

    [Fact]
    public void IsPurgeEligible_OneTickBeforeTheExclusiveBoundary_ReturnsFalse()
    {
        var retentionUntilUtc = RetentionPolicy.ComputeRetentionUntilUtc(2025);

        var eligible = RetentionPolicy.IsPurgeEligible(
            retentionUntilUtc,
            retentionUntilUtc.AddTicks(-1));

        Assert.False(eligible);
    }

    [Fact]
    public void IsPurgeEligible_AtTheExclusiveBoundary_ReturnsTrue()
    {
        var retentionUntilUtc = RetentionPolicy.ComputeRetentionUntilUtc(2025);

        var eligible = RetentionPolicy.IsPurgeEligible(
            retentionUntilUtc,
            retentionUntilUtc);

        Assert.True(eligible);
    }

    [Fact]
    public void IsPurgeEligible_AfterTheExclusiveBoundary_ReturnsTrue()
    {
        var retentionUntilUtc = RetentionPolicy.ComputeRetentionUntilUtc(2025);

        var eligible = RetentionPolicy.IsPurgeEligible(
            retentionUntilUtc,
            retentionUntilUtc.AddTicks(1));

        Assert.True(eligible);
    }

    [Fact]
    public void ProductionSource_UsesNoYearLiteralClockOrInclusiveEndOfDayValue()
    {
        var source = File.ReadAllText(RetentionPolicySource());
        var yearLiteral = new Regex(
            @"\b(19|20)\d{2}\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.DoesNotMatch(yearLiteral, source);
        Assert.DoesNotContain("DateTime.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("23:59:59", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".9999999", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddTicks(-1)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddMilliseconds(-1)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void XmlDocumentation_StatesTheExclusiveProductDefaultSemantics()
    {
        var source = File.ReadAllText(RetentionPolicySource());

        Assert.Contains("exclusive retention boundary", source, StringComparison.Ordinal);
        Assert.Contains("engineering product default", source, StringComparison.Ordinal);
        Assert.Contains("explicitly not a CRA statutory statement", source, StringComparison.Ordinal);
    }

    private static string RetentionPolicySource() =>
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Tenancy",
            "Services",
            "RetentionPolicy.cs");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
