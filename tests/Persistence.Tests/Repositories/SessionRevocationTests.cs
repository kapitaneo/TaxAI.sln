using Microsoft.EntityFrameworkCore;
using Persistence.Repositories;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Repositories;

public sealed class SessionRevocationTests
{
    [Fact] // AC-173
    public async Task RevokedSessionIsRefusedOnTheNextFind()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        await SeedIdentityAsync(context);
        var store = new SqlAuthenticationSessionStore(context);
        var created = await store.CreateAsync(
            SyntheticFixtures.UserId,
            SyntheticFixtures.FirmId,
            SyntheticFixtures.CreatedAtUtc,
            CancellationToken.None);
        var revokedAt = SyntheticFixtures.CreatedAtUtc.AddMinutes(5);

        await store.RevokeAsync(created, revokedAt, CancellationToken.None);
        var found = await store.FindAsync(created.SessionId, CancellationToken.None);

        Assert.Null(found);
        context.ChangeTracker.Clear();
        var persisted = Assert.Single(
            await context.Set<AuthenticationSession>()
                .AsNoTracking()
                .ToListAsync());
        Assert.Equal(revokedAt, persisted.RevokedAtUtc);
    }

    [Fact] // AC-181
    public async Task RevocationThroughOneStoreIsImmediatelyEffectiveInAnother()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var firstContext = relational.CreateContext();
        await using var secondContext = relational.CreateContext();
        await SeedIdentityAsync(firstContext);
        var firstStore = new SqlAuthenticationSessionStore(firstContext);
        var secondStore = new SqlAuthenticationSessionStore(secondContext);
        var created = await firstStore.CreateAsync(
            SyntheticFixtures.UserId,
            SyntheticFixtures.FirmId,
            SyntheticFixtures.CreatedAtUtc,
            CancellationToken.None);
        var resolvedBySecond = await secondStore.FindAsync(
            created.SessionId,
            CancellationToken.None);
        Assert.NotNull(resolvedBySecond);

        await secondStore.RevokeAsync(
            resolvedBySecond,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(5),
            CancellationToken.None);

        Assert.Null(
            await firstStore.FindAsync(
                created.SessionId,
                CancellationToken.None));
    }

    [Fact] // AC-175
    public async Task CopiedPreLogoutSessionCannotBeTouchedBackToLife()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var firstContext = relational.CreateContext();
        await using var secondContext = relational.CreateContext();
        await SeedIdentityAsync(firstContext);
        var firstStore = new SqlAuthenticationSessionStore(firstContext);
        var secondStore = new SqlAuthenticationSessionStore(secondContext);
        var copiedBeforeLogout = await firstStore.CreateAsync(
            SyntheticFixtures.UserId,
            SyntheticFixtures.FirmId,
            SyntheticFixtures.CreatedAtUtc,
            CancellationToken.None);

        await secondStore.RevokeAsync(
            copiedBeforeLogout,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(5),
            CancellationToken.None);
        var touched = await firstStore.TouchAsync(
            copiedBeforeLogout,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(10),
            CancellationToken.None);

        Assert.Null(touched);
        firstContext.ChangeTracker.Clear();
        var persisted = Assert.Single(
            await firstContext.Set<AuthenticationSession>()
                .AsNoTracking()
                .ToListAsync());
        Assert.NotNull(persisted.RevokedAtUtc);
        Assert.Equal(copiedBeforeLogout.SessionId, persisted.SessionId);
    }

    [Fact] // AC-175
    public async Task UnknownSessionCannotBeCreatedByTouchOrRevoke()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        await SeedIdentityAsync(context);
        var store = new SqlAuthenticationSessionStore(context);
        var unknown = SyntheticFixtures.Session(
            activeFirmId: SyntheticFixtures.FirmId);

        var touched = await store.TouchAsync(
            unknown,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(5),
            CancellationToken.None);
        await store.RevokeAsync(
            unknown,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(5),
            CancellationToken.None);

        Assert.Null(touched);
        Assert.Empty(await context.Set<AuthenticationSession>().ToListAsync());
    }

    [Fact]
    public async Task RepeatedRevocationDoesNotReplaceTheSecurityBearingInstant()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        await SeedIdentityAsync(context);
        var store = new SqlAuthenticationSessionStore(context);
        var created = await store.CreateAsync(
            SyntheticFixtures.UserId,
            SyntheticFixtures.FirmId,
            SyntheticFixtures.CreatedAtUtc,
            CancellationToken.None);
        var firstInstant = SyntheticFixtures.CreatedAtUtc.AddMinutes(5);

        await store.RevokeAsync(created, firstInstant, CancellationToken.None);
        await store.RevokeAsync(
            created,
            SyntheticFixtures.CreatedAtUtc.AddMinutes(10),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var persisted = Assert.Single(
            await context.Set<AuthenticationSession>()
                .AsNoTracking()
                .ToListAsync());
        Assert.Equal(firstInstant, persisted.RevokedAtUtc);
    }

    private static async Task SeedIdentityAsync(TaxAiDbContext context)
    {
        context.Add(SyntheticFixtures.Firm());
        context.Add(SyntheticFixtures.User());
        await context.SaveChangesAsync();
    }
}
