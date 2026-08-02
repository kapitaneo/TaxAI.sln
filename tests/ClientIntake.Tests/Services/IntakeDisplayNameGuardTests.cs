using ClientIntake.Models;
using ClientIntake.Tests.Support;
using Tenancy.Models;

namespace ClientIntake.Tests.Services;

public sealed class IntakeDisplayNameGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Taxpayer 123456789")]
    [InlineData("Taxpayer xxx-xxx-123")]
    public async Task UnsafeOrAbsentDisplayTextCreatesNothing(string? displayName)
    {
        var store = new InMemoryIntakeStore();
        var workflow = new ClientIntake.Services.AccountantIntakeWorkflow(
            store,
            new SyntheticIntakeFixtures.CredentialSource(),
            new SyntheticIntakeFixtures.ReviewRepository(),
            SyntheticIntakeFixtures.Options());

        var result = await workflow.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand() with
            {
                PrimaryTaxpayerDisplayName = displayName,
            },
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Invalid, result.Code);
        Assert.Null(result.Value);
        if (!string.IsNullOrEmpty(displayName))
        {
            Assert.DoesNotContain(
                displayName,
                result.ToString(),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TextAtTheApprovedMaximumWithShortDigitsIsAccepted()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync();
        var maximum = "Unit 123 " + new string(
            'A',
            PersistedSchema.DisplayNameMaxLength - "Unit 123 ".Length);
        var result = await fixture.Accountant.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand() with
            {
                PrimaryTaxpayerDisplayName = maximum,
            },
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TextAboveTheApprovedMaximumIsRefused()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync();
        var result = await fixture.Accountant.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand() with
            {
                PrimaryTaxpayerDisplayName =
                    new string('A', PersistedSchema.DisplayNameMaxLength + 1),
            },
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Invalid, result.Code);
    }
}
