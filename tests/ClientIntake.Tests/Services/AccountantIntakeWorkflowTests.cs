using ClientIntake.Models;
using ClientIntake.Services;
using ClientIntake.Tests.Support;

namespace ClientIntake.Tests.Services;

public sealed class AccountantIntakeWorkflowTests
{
    [Fact]
    public async Task CreationCommitsOneScopedCaseChecklistLinkAndOpeningAudit()
    {
        var fixture = Fixture();

        var result = await fixture.Workflow.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand(t4: 2, medical: 1),
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = result.Value!;
        var stored = await fixture.Store.FindAsync(
            SyntheticIntakeFixtures.Firm,
            created.CaseId,
            CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(3, stored!.ExpectedItems.Count);
        Assert.Equal(2, stored.ExpectedItems.Count(item =>
            item.Category == TaxCases.Models.DocumentCategory.T4Slip));
        Assert.Equal(1, stored.ExpectedItems.Count(item =>
            item.Category == TaxCases.Models.DocumentCategory.MedicalExpenseEvidence));
        Assert.Equal(TaxCases.Models.CaseStatus.InformationGathering, stored.CaseStatus);
        Assert.Equal(3, fixture.Store.Audit(
            SyntheticIntakeFixtures.Firm,
            created.CaseId).Count);
        Assert.DoesNotContain(
            created.LinkCredential.Reveal(),
            stored.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2024, 1, 0)]
    [InlineData(2026, 1, 0)]
    [InlineData(2025, 0, 0)]
    [InlineData(2025, 26, 0)]
    [InlineData(2025, -1, 1)]
    public async Task InvalidYearOrCountsCreateNothing(
        int year,
        int t4,
        int medical)
    {
        var fixture = Fixture();
        var command = new CreateIntakeCaseCommand(
            "Synthetic Taxpayer",
            year,
            t4,
            medical);

        var result = await fixture.Workflow.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            command,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Invalid, result.Code);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    [InlineData("123 456 789")]
    [InlineData("***-***-123")]
    public async Task StructuredIdentifierShapedDisplayTextIsRefusedWithoutAudit(
        string displayName)
    {
        var fixture = Fixture();

        var result = await fixture.Workflow.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand() with
            {
                PrimaryTaxpayerDisplayName = displayName,
            },
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Invalid, result.Code);
        Assert.DoesNotContain(displayName, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReissueReturnsANewCredentialAndInvalidatesThePriorOne()
    {
        var fixture = Fixture();
        var created = (await fixture.Workflow.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand(),
            SyntheticIntakeFixtures.Now,
            CancellationToken.None)).Value!;

        var reissued = await fixture.Workflow.ReissueLinkAsync(
            SyntheticIntakeFixtures.Firm,
            created.CaseId,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);

        Assert.True(reissued.IsSuccess);
        Assert.NotEqual(
            created.LinkCredential.Reveal(),
            reissued.Value!.LinkCredential.Reveal());
    }

    private static (
        AccountantIntakeWorkflow Workflow,
        InMemoryIntakeStore Store) Fixture()
    {
        var store = new InMemoryIntakeStore();
        return (
            new AccountantIntakeWorkflow(
                store,
                new SyntheticIntakeFixtures.CredentialSource(),
                new SyntheticIntakeFixtures.ReviewRepository(),
                SyntheticIntakeFixtures.Options()),
            store);
    }
}
