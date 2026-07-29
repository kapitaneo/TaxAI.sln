using Microsoft.Extensions.Logging;
using Persistence.Services;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Services;

public sealed class PurgeDiagnosticsTests
{
    [Fact]
    public async Task CompletedPurgeLogsOnlyStructuralCountsAndTheOpaqueFirmIdentifier()
    {
        var logger = new RecordingLogger<SqlReviewRecordPurgeService>();

        await using var relational = await RelationalTestContext.CreateAsync();
        await using (var writeContext = relational.CreateContext())
        {
            writeContext.Add(SyntheticFixtures.ReviewRecord());
            await writeContext.SaveChangesAsync();
        }

        await using var purgeContext = relational.CreateContext();
        var service = new SqlReviewRecordPurgeService(purgeContext, logger);
        var outcome = await service.PurgeAsync(
            TrustedFirmId.FromGuid(SyntheticFixtures.FirmId),
            new DateTimeOffset(
                new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            "Purged 1 review records and 1 slips for firm " +
            $"{SyntheticFixtures.FirmId:D}.",
            entry.Message);
        Assert.Equal(1, outcome.RecordsDeleted);
        Assert.Equal(1, outcome.SlipsDeleted);

        foreach (var forbidden in new[]
        {
            "Test Taxpayer 001",
            "Example Employer A",
            "1.005",
            "2.005",
            "B_A",
            "B_B",
            "L_A",
        })
        {
            Assert.DoesNotContain(
                forbidden,
                entry.Message,
                StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"\d{9}", entry.Message);
    }

    [Fact]
    public async Task NoEligibleRecordLogContainsOnlyZeroCountsAndTheOpaqueFirmIdentifier()
    {
        var logger = new RecordingLogger<SqlReviewRecordPurgeService>();

        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        var service = new SqlReviewRecordPurgeService(context, logger);

        var outcome = await service.PurgeAsync(
            TrustedFirmId.FromGuid(SyntheticFixtures.FirmId),
            SyntheticFixtures.CreatedAtUtc,
            CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(
            "Purged 0 review records and 0 slips for firm " +
            $"{SyntheticFixtures.FirmId:D}.",
            entry.Message);
        Assert.Equal(0, outcome.RecordsDeleted);
        Assert.Equal(0, outcome.SlipsDeleted);
        Assert.DoesNotMatch(@"\d{9}", entry.Message);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, formatter(state, exception)));
    }

    private sealed record Entry(LogLevel Level, string Message);
}
