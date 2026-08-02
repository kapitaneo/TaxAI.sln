using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using ClientIntake.Tests.Support;
using TaxCases.Models;

namespace ClientIntake.Tests.Services;

public sealed class IntakeCaseFactoryTests
{
    [Fact]
    public async Task BothPerCategoryMaximumsProduceClosedOrderedSlots()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(
            t4: 25,
            medical: 25);
        var stored = await fixture.Store.FindAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            CancellationToken.None);

        Assert.Equal(50, stored!.ExpectedItems.Count);
        Assert.Equal(
            Enumerable.Range(1, 25),
            stored.ExpectedItems
                .Where(item => item.Category == DocumentCategory.T4Slip)
                .Select(item => item.Ordinal));
        Assert.Equal(
            Enumerable.Range(1, 25),
            stored.ExpectedItems
                .Where(item =>
                    item.Category == DocumentCategory.MedicalExpenseEvidence)
                .Select(item => item.Ordinal));
        Assert.DoesNotContain(
            stored.ExpectedItems,
            item => item.Category == DocumentCategory.Other);
    }

    [Fact]
    public async Task InvalidGeneratedIdentifiersFailBeforeStoreCreation()
    {
        var store = new InMemoryIntakeStore();
        var workflow = new AccountantIntakeWorkflow(
            store,
            new EmptyIdentifierCredentialSource(),
            new SyntheticIntakeFixtures.ReviewRepository(),
            SyntheticIntakeFixtures.Options());

        var result = await workflow.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand(),
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Invalid, result.Code);
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void WorkflowConstructionRefusesEveryNonPositiveOption(
        IntakeWorkflowOptions options)
    {
        var store = new InMemoryIntakeStore();
        var source = new SyntheticIntakeFixtures.CredentialSource();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AccountantIntakeWorkflow(
                store,
                source,
                new SyntheticIntakeFixtures.ReviewRepository(),
                options));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ClientIntakeWorkflow(
                store,
                new SyntheticIntakeFixtures.ContentStore(),
                source,
                options));
    }

    public static TheoryData<IntakeWorkflowOptions> InvalidOptions =>
        new()
        {
            new(
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1),
                1,
                1),
            new(
                TimeSpan.FromMinutes(1),
                TimeSpan.Zero,
                1,
                1),
            new(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1),
                0,
                1),
            new(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1),
                1,
                0),
        };

    private sealed class EmptyIdentifierCredentialSource :
        IIntakeCredentialSource
    {
        public Guid CreateIdentifier() => Guid.Empty;

        public IntakeSecret CreateCredential() =>
            new SyntheticIntakeFixtures.CredentialSource().CreateCredential();

        public IntakeTokenDigest Digest(
            IntakeSecret credential,
            IntakeTokenPurpose purpose) =>
            new SyntheticIntakeFixtures.CredentialSource().Digest(
                credential,
                purpose);
    }
}
