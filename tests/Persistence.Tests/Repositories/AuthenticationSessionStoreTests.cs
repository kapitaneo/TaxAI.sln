using Microsoft.EntityFrameworkCore;
using Persistence.Repositories;
using Persistence.Tests.Support;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Tests.Repositories;

public sealed class AuthenticationSessionStoreTests
{
    [Fact] // AC-164
    public async Task CreateWritesExactlyOneLiveSessionWithServerSideBoundaries()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        await SeedIdentityAsync(context);
        var store = new SqlAuthenticationSessionStore(context);

        var session = await store.CreateAsync(
            SyntheticFixtures.UserId,
            SyntheticFixtures.FirmId,
            SyntheticFixtures.CreatedAtUtc,
            CancellationToken.None);

        var persisted = Assert.Single(
            await context.Set<AuthenticationSession>()
                .AsNoTracking()
                .ToListAsync());
        Assert.Equal(session.SessionId, persisted.SessionId);
        Assert.NotEqual(Guid.Empty, persisted.SessionId);
        Assert.Equal(SyntheticFixtures.UserId, persisted.UserId);
        Assert.Equal(SyntheticFixtures.FirmId, persisted.ActiveFirmId);
        Assert.Equal(SyntheticFixtures.CreatedAtUtc, persisted.CreatedAtUtc);
        Assert.Equal(SyntheticFixtures.CreatedAtUtc, persisted.LastSeenAtUtc);
        Assert.Equal(
            SyntheticFixtures.CreatedAtUtc.Add(SessionLifetimePolicy.IdleTimeout),
            persisted.IdleExpiresAtUtc);
        Assert.Equal(
            SyntheticFixtures.CreatedAtUtc.Add(SessionLifetimePolicy.AbsoluteLifetime),
            persisted.AbsoluteExpiresAtUtc);
        Assert.Null(persisted.RevokedAtUtc);
        Assert.All(
            SessionInstants(persisted),
            instant => Assert.Equal(TimeSpan.Zero, instant.Offset));
    }

    [Fact]
    public async Task FindBySessionIdReturnsTheActiveFirmWithoutAcceptingOne()
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

        var found = await store.FindAsync(created.SessionId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(SyntheticFixtures.FirmId, found.ActiveFirmId);
    }

    [Fact]
    public async Task UnknownSessionReturnsNullAndCreatesNothing()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        await SeedIdentityAsync(context);
        var store = new SqlAuthenticationSessionStore(context);

        var found = await store.FindAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(found);
        Assert.Empty(await context.Set<AuthenticationSession>().ToListAsync());
    }

    [Fact]
    public async Task TouchAdvancesLastSeenAndIdleButNeverAbsoluteExpiry()
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
        var originalAbsolute = created.AbsoluteExpiresAtUtc;
        var touchedAt = SyntheticFixtures.CreatedAtUtc.AddMinutes(5);

        var touched = await store.TouchAsync(
            created,
            touchedAt,
            CancellationToken.None);

        Assert.NotNull(touched);
        Assert.Equal(touchedAt, touched.LastSeenAtUtc);
        Assert.Equal(
            touchedAt.Add(SessionLifetimePolicy.IdleTimeout),
            touched.IdleExpiresAtUtc);
        Assert.Equal(originalAbsolute, touched.AbsoluteExpiresAtUtc);

        context.ChangeTracker.Clear();
        var persisted = Assert.Single(
            await context.Set<AuthenticationSession>()
                .AsNoTracking()
                .ToListAsync());
        Assert.Equal(touchedAt, persisted.LastSeenAtUtc);
        Assert.Equal(touched.IdleExpiresAtUtc, persisted.IdleExpiresAtUtc);
        Assert.Equal(originalAbsolute, persisted.AbsoluteExpiresAtUtc);
    }

    [Fact] // AC-180
    public async Task IndependentlyConstructedStoreSeesACommittedSessionImmediately()
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

        var observed = await secondStore.FindAsync(
            created.SessionId,
            CancellationToken.None);

        Assert.NotNull(observed);
        Assert.Equal(created.SessionId, observed.SessionId);
        Assert.Equal(created.ActiveFirmId, observed.ActiveFirmId);
    }

    [Fact] // AC-185
    public async Task ExpiredRowsRemainUntilTheExplicitPrivilegedCommandRuns()
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
        var afterAbsoluteBoundary =
            created.AbsoluteExpiresAtUtc.AddTicks(1);

        Assert.Single(await context.Set<AuthenticationSession>().ToListAsync());

        var removed = await store.RemoveExpiredAsync(
            afterAbsoluteBoundary,
            CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Empty(await context.Set<AuthenticationSession>().ToListAsync());
    }

    [Fact] // AC-185
    public void StoreDeclaresNoTimerHostedServiceOrSchedulerSurface()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Persistence",
            "Repositories",
            "SqlAuthenticationSessionStore.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("IHostedService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BackgroundService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHostedService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Scheduler", source, StringComparison.Ordinal);
    }

    private static async Task SeedIdentityAsync(TaxAiDbContext context)
    {
        context.Add(SyntheticFixtures.Firm());
        context.Add(SyntheticFixtures.User());
        await context.SaveChangesAsync();
    }

    private static IEnumerable<DateTimeOffset> SessionInstants(
        AuthenticationSession session)
    {
        yield return session.CreatedAtUtc;
        yield return session.LastSeenAtUtc;
        yield return session.IdleExpiresAtUtc;
        yield return session.AbsoluteExpiresAtUtc;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
