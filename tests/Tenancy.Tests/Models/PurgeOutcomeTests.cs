using System.Reflection;
using Tenancy.Models;

namespace Tenancy.Tests.Models;

public sealed class PurgeOutcomeTests
{
    [Fact]
    public void NoEligibleRecordsCreatesTheOnlyZeroRecordOutcome()
    {
        var outcome = PurgeOutcome.NoEligibleRecords();

        Assert.Equal(0, outcome.RecordsDeleted);
        Assert.Equal(0, outcome.SlipsDeleted);
        Assert.Equal(nameof(PurgeOutcome), outcome.ToString());
    }

    [Fact]
    public void RecordsPurgedPreservesNonNegativeStructuralCounts()
    {
        var outcome = PurgeOutcome.RecordsPurged(
            recordsDeleted: 2,
            slipsDeleted: 0);

        Assert.Equal(2, outcome.RecordsDeleted);
        Assert.Equal(0, outcome.SlipsDeleted);
        Assert.Equal(nameof(PurgeOutcome), outcome.ToString());
    }

    [Theory]
    [InlineData(0, 0, "recordsDeleted")]
    [InlineData(-1, 0, "recordsDeleted")]
    [InlineData(1, -1, "slipsDeleted")]
    public void RecordsPurgedRefusesInvalidCountsWithoutEchoingThem(
        int recordsDeleted,
        int slipsDeleted,
        string expectedParameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => PurgeOutcome.RecordsPurged(recordsDeleted, slipsDeleted));

        Assert.Equal(expectedParameter, exception.ParamName);
        Assert.DoesNotContain(
            recordsDeleted.ToString(System.Globalization.CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);
        if (slipsDeleted < 0)
        {
            Assert.DoesNotContain(
                slipsDeleted.ToString(System.Globalization.CultureInfo.InvariantCulture),
                exception.Message,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlyTheTwoNamedFactoriesArePublicConstructionSeams()
    {
        Assert.Empty(typeof(PurgeOutcome).GetConstructors());

        var methods = typeof(PurgeOutcome)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                nameof(PurgeOutcome.NoEligibleRecords),
                nameof(PurgeOutcome.RecordsPurged),
            },
            methods.Select(method => method.Name));
        Assert.Empty(methods[0].GetParameters());
        Assert.Equal(
            new[] { typeof(int), typeof(int) },
            methods[1].GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(
            typeof(PurgeOutcome).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }
}
