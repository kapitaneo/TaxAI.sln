using ClientIntake.Models;
using Persistence.Repositories;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Repositories;

public sealed class IntakeStoreTenantIsolationTests
{
    [Fact]
    public async Task ForeignFirmReadIsIdenticalToMissing()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        context.Add(SyntheticFixtures.Firm(
            SyntheticFixtures.OtherFirmId,
            "Example Firm B"));
        await context.SaveChangesAsync();
        var store = new SqlIntakeStore(context);
        var intake = SyntheticFixtures.IntakeCase();
        await store.CreateAsync(intake, CancellationToken.None);

        var foreign = await store.FindAsync(
            TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId),
            intake.Case.CaseId,
            CancellationToken.None);
        var missing = await store.FindAsync(
            TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(foreign);
        Assert.Null(missing);
        Assert.Equal(
            IntakeStoreWriteCode.NotFound,
            await store.RevokeLinkAsync(
                TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId),
                intake.Case.CaseId,
                0,
                SyntheticFixtures.CreatedAtUtc.AddMinutes(2),
                SyntheticFixtures.Audit(
                    IntakeAuditKind.LinkRevoked,
                    Guid.NewGuid()),
                CancellationToken.None));
    }
}
