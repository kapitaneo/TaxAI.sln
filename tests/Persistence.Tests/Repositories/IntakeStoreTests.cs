using ClientIntake.Models;
using Microsoft.EntityFrameworkCore;
using Persistence.Entities;
using Persistence.Repositories;
using Persistence.Tests.Support;

namespace Persistence.Tests.Repositories;

public sealed class IntakeStoreTests
{
    [Fact]
    public async Task CreateRedeemAndAppendRoundTripImmutableHistory()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        await context.SaveChangesAsync();
        var store = new SqlIntakeStore(context);
        var intake = SyntheticFixtures.IntakeCase();

        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await store.CreateAsync(intake, CancellationToken.None));

        var linkScope = await store.FindLinkScopeAsync(
            intake.Case.CurrentLink.CredentialDigest,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(1),
            CancellationToken.None);
        Assert.NotNull(linkScope);

        var session = new NewIntakeSession(
            Guid.Parse("17171717-1717-4717-8717-171717171717"),
            SyntheticFixtures.Digest(0x22),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(1),
            SyntheticFixtures.CreatedAtUtc.AddHours(8),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(31));
        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await store.RedeemLinkAsync(
                linkScope!,
                session,
                SyntheticFixtures.Audit(
                    IntakeAuditKind.LinkRedeemed,
                    session.SessionId),
                CancellationToken.None));
        Assert.Null(await store.FindLinkScopeAsync(
            intake.Case.CurrentLink.CredentialDigest,
            session.CreatedAtUtc,
            CancellationToken.None));

        var clientScope = await store.FindClientScopeAsync(
            session.SessionDigest,
            session.CreatedAtUtc.AddMinutes(1),
            CancellationToken.None);
        Assert.NotNull(clientScope);
        var answer = new IntakeAnswerVersionRecord(
            intake.Case.ExpectedItems[0].Category,
            1,
            AvailabilityAnswerCode.WillProvideLater,
            IntakeActorKind.Client,
            session.SessionId,
            session.CreatedAtUtc.AddMinutes(2));
        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await store.AppendAnswersAsync(
                clientScope!,
                1,
                session.CreatedAtUtc.AddMinutes(32),
                [answer],
                [
                    SyntheticFixtures.Audit(
                        IntakeAuditKind.AnswerSaved,
                        session.SessionId),
                ],
                CancellationToken.None));

        var loaded = await store.FindForClientAsync(
            clientScope with
            {
                EvaluationAtUtc = session.CreatedAtUtc.AddMinutes(2),
            },
            CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.ConcurrencyVersion);
        Assert.Equal(answer, Assert.Single(loaded.AnswerVersions));
        Assert.Equal(5, await context.Set<IntakeAuditEventEntity>().CountAsync());
    }

    [Fact]
    public async Task ReissueRetainsHistoryAndInvalidatesPriorSession()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        context.Add(SyntheticFixtures.Firm());
        await context.SaveChangesAsync();
        var store = new SqlIntakeStore(context);
        var intake = SyntheticFixtures.IntakeCase();
        await store.CreateAsync(intake, CancellationToken.None);
        var linkScope = await store.FindLinkScopeAsync(
            intake.Case.CurrentLink.CredentialDigest,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(1),
            CancellationToken.None);
        var session = new NewIntakeSession(
            Guid.NewGuid(),
            SyntheticFixtures.Digest(0x23),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(1),
            SyntheticFixtures.CreatedAtUtc.AddHours(4),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(31));
        await store.RedeemLinkAsync(
            linkScope!,
            session,
            SyntheticFixtures.Audit(IntakeAuditKind.LinkRedeemed, session.SessionId),
            CancellationToken.None);
        var replacement = new IntakeLinkRecord(
            Guid.NewGuid(),
            SyntheticFixtures.Digest(0x24),
            SyntheticFixtures.CreatedAtUtc.AddMinutes(3),
            SyntheticFixtures.CreatedAtUtc.AddDays(1),
            null,
            null);

        Assert.Equal(
            IntakeStoreWriteCode.Success,
            await store.ReissueLinkAsync(
                intake.Case.Firm,
                intake.Case.CaseId,
                1,
                replacement,
                [
                    SyntheticFixtures.Audit(
                        IntakeAuditKind.LinkRevoked,
                        Guid.NewGuid()),
                    SyntheticFixtures.Audit(
                        IntakeAuditKind.LinkReissued,
                        Guid.NewGuid()),
                ],
                CancellationToken.None));

        Assert.Null(await store.FindClientScopeAsync(
            session.SessionDigest,
            replacement.IssuedAtUtc,
            CancellationToken.None));
        Assert.NotNull(await store.FindLinkScopeAsync(
            replacement.CredentialDigest,
            replacement.IssuedAtUtc,
            CancellationToken.None));
        Assert.Equal(2, await context.Set<IntakeLinkEntity>().CountAsync());
        Assert.Single(
            await context.Set<IntakeLinkEntity>()
                .Where(candidate => candidate.RevokedAtUtc == null)
                .ToListAsync());
    }
}
