using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence.Services;
using Persistence.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Tests.Services;

public sealed class ReviewRecordPurgeServiceTests
{
    private static readonly Guid FirmId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");

    private static readonly Guid OtherFirmId =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");

    private static readonly DateTimeOffset RetentionBoundary =
        new(new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void TheServiceIsInternalSealedAndImplementsOnlyThePurgePort()
    {
        var type = typeof(SqlReviewRecordPurgeService);

        Assert.False(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Equal(new[] { typeof(IReviewRecordPurgeService) }, type.GetInterfaces());

        var purge = Assert.Single(type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(IReviewRecordPurgeService.PurgeAsync));

        Assert.Equal(
            new[]
            {
                typeof(TrustedFirmId),
                typeof(DateTimeOffset),
                typeof(CancellationToken),
            },
            purge.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.All(purge.GetParameters(), parameter =>
        {
            Assert.False(parameter.IsOptional);
            Assert.False(parameter.HasDefaultValue);
        });
    }

    [Fact]
    public async Task FreshDatabaseProducesTheNamedNoEligibleRecordsOutcome()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        var service = CreateService(context);

        var outcome = await service.PurgeAsync(
            TrustedFirmId.FromGuid(FirmId),
            RetentionBoundary,
            CancellationToken.None);

        Assert.Equal(0, outcome.RecordsDeleted);
        Assert.Equal(0, outcome.SlipsDeleted);
        Assert.Equal(nameof(PurgeOutcome), outcome.ToString());
    }

    [Fact]
    public async Task OneTickBeforeTheExclusiveBoundarySparesTheRecord()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await AddAsync(relational, SyntheticFixtures.ReviewRecord());

        await using (var purgeContext = relational.CreateContext())
        {
            var outcome = await CreateService(purgeContext).PurgeAsync(
                TrustedFirmId.FromGuid(FirmId),
                RetentionBoundary.AddTicks(-1),
                CancellationToken.None);

            Assert.Equal(0, outcome.RecordsDeleted);
            Assert.Equal(0, outcome.SlipsDeleted);
        }

        await using var verifyContext = relational.CreateContext();
        Assert.Equal(1, await verifyContext.Set<PersistedReviewRecord>().CountAsync());
        Assert.Equal(1, await verifyContext.Set<PersistedSlip>().CountAsync());
    }

    [Fact]
    public async Task TheBoundaryInstantDeletesTheRecordAndItsCompleteGraph()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await AddAsync(relational, SyntheticFixtures.ReviewRecord());

        await using (var purgeContext = relational.CreateContext())
        {
            var outcome = await CreateService(purgeContext).PurgeAsync(
                TrustedFirmId.FromGuid(FirmId),
                RetentionBoundary,
                CancellationToken.None);

            Assert.Equal(1, outcome.RecordsDeleted);
            Assert.Equal(1, outcome.SlipsDeleted);
        }

        await using var verifyContext = relational.CreateContext();
        Assert.Equal(0, await verifyContext.Set<PersistedReviewRecord>().CountAsync());
        Assert.Equal(0, await verifyContext.Set<PersistedSlip>().CountAsync());
        Assert.Equal(0, await verifyContext.Set<PersistedBoxAmount>().CountAsync());
        Assert.Equal(0, await verifyContext.Set<PersistedLineEntry>().CountAsync());
        Assert.Equal(0, await verifyContext.Set<PersistedSourceBoxCode>().CountAsync());
        Assert.Equal(0, await verifyContext.Set<PersistedUnmappedBoxCode>().CountAsync());
    }

    [Fact]
    public async Task ALaterInstantIsPurgeEligible()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await AddAsync(relational, SyntheticFixtures.ReviewRecord());

        await using var purgeContext = relational.CreateContext();
        var outcome = await CreateService(purgeContext).PurgeAsync(
            TrustedFirmId.FromGuid(FirmId),
            RetentionBoundary.AddTicks(1),
            CancellationToken.None);

        Assert.Equal(1, outcome.RecordsDeleted);
        Assert.Equal(1, outcome.SlipsDeleted);
    }

    [Fact]
    public async Task PurgeDeletesOnlyEligibleRecordsForTheTrustedFirm()
    {
        var eligibleId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3");
        var unexpiredId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-ddddddddddd4");
        var otherFirmRecordId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee5");

        await using var relational = await RelationalTestContext.CreateAsync();
        await AddAsync(
            relational,
            ReviewRecord(eligibleId, FirmId, RetentionBoundary.AddTicks(-1)),
            ReviewRecord(unexpiredId, FirmId, RetentionBoundary.AddTicks(1)),
            ReviewRecord(otherFirmRecordId, OtherFirmId, RetentionBoundary.AddTicks(-1)));

        await using (var purgeContext = relational.CreateContext())
        {
            var outcome = await CreateService(purgeContext).PurgeAsync(
                TrustedFirmId.FromGuid(FirmId),
                RetentionBoundary,
                CancellationToken.None);

            Assert.Equal(1, outcome.RecordsDeleted);
            Assert.Equal(0, outcome.SlipsDeleted);
        }

        await using var verifyContext = relational.CreateContext();
        var survivors = await verifyContext.Set<PersistedReviewRecord>()
            .AsNoTracking()
            .OrderBy(record => record.RecordId)
            .Select(record => new { record.FirmId, record.RecordId })
            .ToArrayAsync();

        Assert.Equal(2, survivors.Length);
        Assert.Contains(survivors, record =>
            record.FirmId == FirmId && record.RecordId == unexpiredId);
        Assert.Contains(survivors, record =>
            record.FirmId == OtherFirmId && record.RecordId == otherFirmRecordId);
    }

    [Fact]
    public async Task PurgeScopedToAnotherFirmCannotDeleteAnExpiredRecord()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await AddAsync(relational, SyntheticFixtures.ReviewRecord());

        await using (var purgeContext = relational.CreateContext())
        {
            var outcome = await CreateService(purgeContext).PurgeAsync(
                TrustedFirmId.FromGuid(OtherFirmId),
                RetentionBoundary,
                CancellationToken.None);

            Assert.Equal(0, outcome.RecordsDeleted);
            Assert.Equal(0, outcome.SlipsDeleted);
        }

        await using var verifyContext = relational.CreateContext();
        Assert.Equal(1, await verifyContext.Set<PersistedReviewRecord>().CountAsync());
    }

    [Fact]
    public async Task NonUtcEvaluationIsRefusedWithoutDeletingAnything()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await AddAsync(relational, SyntheticFixtures.ReviewRecord());

        await using (var purgeContext = relational.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => CreateService(purgeContext).PurgeAsync(
                    TrustedFirmId.FromGuid(FirmId),
                    RetentionBoundary.ToOffset(TimeSpan.FromHours(1)),
                    CancellationToken.None));

            Assert.Equal("evaluationInstantUtc", exception.ParamName);
            Assert.DoesNotContain(
                RetentionBoundary.ToString(),
                exception.Message,
                StringComparison.Ordinal);
        }

        await using var verifyContext = relational.CreateContext();
        Assert.Equal(1, await verifyContext.Set<PersistedReviewRecord>().CountAsync());
    }

    [Fact]
    public async Task CancellationIsHonoredWithoutDeletingAnything()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await AddAsync(relational, SyntheticFixtures.ReviewRecord());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await using (var purgeContext = relational.CreateContext())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateService(purgeContext).PurgeAsync(
                    TrustedFirmId.FromGuid(FirmId),
                    RetentionBoundary,
                    cancellation.Token));
        }

        await using var verifyContext = relational.CreateContext();
        Assert.Equal(1, await verifyContext.Set<PersistedReviewRecord>().CountAsync());
    }

    private static SqlReviewRecordPurgeService CreateService(TaxAiDbContext context) =>
        new(context, NullLogger<SqlReviewRecordPurgeService>.Instance);

    private static async Task AddAsync(
        RelationalTestContext relational,
        params PersistedReviewRecord[] records)
    {
        await using var context = relational.CreateContext();
        context.AddRange(records);
        await context.SaveChangesAsync();
    }

    private static PersistedReviewRecord ReviewRecord(
        Guid recordId,
        Guid firmId,
        DateTimeOffset retentionUntilUtc) =>
        Construct<PersistedReviewRecord>(
            recordId,
            firmId,
            $"case_{recordId:N}",
            $"participant_{recordId:N}",
            2025,
            1,
            SyntheticFixtures.CreatedAtUtc,
            SyntheticFixtures.CreatedAtUtc,
            retentionUntilUtc,
            Array.Empty<PersistedSlip>());

    private static T Construct<T>(params object?[] arguments)
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);

        return (T)constructor.Invoke(arguments);
    }
}
