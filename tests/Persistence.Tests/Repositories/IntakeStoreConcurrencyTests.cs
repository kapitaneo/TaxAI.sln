using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Tests.Support;

namespace Persistence.Tests.Repositories;

public sealed class IntakeStoreConcurrencyTests
{
    [Fact]
    public async Task StaleExpectedVersionWritesNothing()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        await context.SaveChangesAsync();
        var store = new SqlIntakeStore(context);
        var intake = SyntheticFixtures.IntakeCase();
        await store.CreateAsync(intake, CancellationToken.None);
        var link = await store.FindLinkScopeAsync(
            intake.Case.CurrentLink.CredentialDigest,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(1),
            CancellationToken.None);
        var session = new NewIntakeSession(
            Guid.NewGuid(),
            SyntheticFixtures.Digest(0x31),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(1),
            SyntheticFixtures.CreatedAtUtc.AddHours(4),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(31));
        await store.RedeemLinkAsync(
            link!,
            session,
            SyntheticFixtures.Audit(IntakeAuditKind.LinkRedeemed, session.SessionId),
            CancellationToken.None);
        var scope = await store.FindClientScopeAsync(
            session.SessionDigest,
            session.CreatedAtUtc.AddMinutes(1),
            CancellationToken.None);
        var liveScope = Assert.IsType<IntakeClientScope>(scope);
        var answer = new IntakeAnswerVersionRecord(
            intake.Case.ExpectedItems[0].Category,
            1,
            AvailabilityAnswerCode.Unavailable,
            IntakeActorKind.Client,
            session.SessionId,
            session.CreatedAtUtc.AddMinutes(2));

        var first = await store.AppendAnswersAsync(
            liveScope,
            1,
            session.CreatedAtUtc.AddMinutes(32),
            [answer],
            [SyntheticFixtures.Audit(IntakeAuditKind.AnswerSaved, session.SessionId)],
            CancellationToken.None);
        var stale = await store.AppendAnswersAsync(
            liveScope,
            1,
            session.CreatedAtUtc.AddMinutes(33),
            [answer with { Version = 2 }],
            [SyntheticFixtures.Audit(IntakeAuditKind.AnswerSaved, session.SessionId)],
            CancellationToken.None);

        Assert.Equal(IntakeStoreWriteCode.Success, first);
        Assert.Equal(IntakeStoreWriteCode.NotFound, stale);
        Assert.Single(await context.Set<IntakeAnswerVersionEntity>().ToListAsync());
        Assert.Equal(5, await context.Set<IntakeAuditEventEntity>().CountAsync());
    }
}
