using TaxCases.Models;
using TaxCases.Services;

namespace TaxCases.Tests.Services;

public sealed class CaseStatusTransitionsTests
{
    private static readonly HashSet<(CaseStatus From, CaseStatus To)> Allowed =
    [
        (CaseStatus.Intake, CaseStatus.InformationGathering),
        (CaseStatus.Intake, CaseStatus.Cancelled),
        (CaseStatus.InformationGathering, CaseStatus.InReview),
        (CaseStatus.InformationGathering, CaseStatus.Cancelled),
        (CaseStatus.InReview, CaseStatus.InformationGathering),
        (CaseStatus.InReview, CaseStatus.Completed),
        (CaseStatus.InReview, CaseStatus.Cancelled)
    ];

    public static TheoryData<CaseStatus, CaseStatus, bool> EveryPair()
    {
        var data = new TheoryData<CaseStatus, CaseStatus, bool>();
        foreach (var from in Enum.GetValues<CaseStatus>())
        {
            foreach (var to in Enum.GetValues<CaseStatus>())
            {
                data.Add(from, to, Allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void IsAllowed_ImplementsExactTransitionTable(
        CaseStatus from,
        CaseStatus to,
        bool expected)
    {
        Assert.Equal(expected, CaseStatusTransitions.IsAllowed(from, to));
    }

    [Fact]
    public void CaseStatus_DoesNotContainArchived()
    {
        Assert.DoesNotContain("Archived", Enum.GetNames<CaseStatus>());
    }
}
