using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Persistence.Repositories;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Repositories;

public sealed class ReviewRecordVersioningTests
{
    [Fact]
    public async Task SavingTheSameSubjectCreatesTheNextImmutableVersion()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(
            context,
            new RepositorySequenceIdentifierSource());
        var firstPackage = ReviewRecordRepositoryTestData.Package(
            reviewedSlips:
            [
                ReviewRecordRepositoryTestData.Slip(
                    taxpayerDisplayName: "Test Taxpayer Original",
                    employerDisplayName: "Example Employer Original"),
            ]);
        var secondPackage = ReviewRecordRepositoryTestData.Package(
            reviewedSlips:
            [
                ReviewRecordRepositoryTestData.Slip(
                    taxpayerDisplayName: "Test Taxpayer Regenerated",
                    employerDisplayName: "Example Employer Regenerated"),
            ]);

        var firstSave = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            firstPackage,
            ReviewRecordRepositoryTestData.EvaluationInstantUtc,
            CancellationToken.None);
        var firstBeforeSecondSave = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            firstSave.Value!.RecordId,
            CancellationToken.None);
        var immutableSnapshot = CompleteSnapshot(firstBeforeSecondSave!);

        var secondSave = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            secondPackage,
            ReviewRecordRepositoryTestData.EvaluationInstantUtc.AddMinutes(1),
            CancellationToken.None);
        var firstAfterSecondSave = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            firstSave.Value.RecordId,
            CancellationToken.None);
        var secondRecord = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            secondSave.Value!.RecordId,
            CancellationToken.None);

        Assert.True(firstSave.IsSuccess);
        Assert.True(secondSave.IsSuccess);
        Assert.Equal(1, firstAfterSecondSave!.PackageVersion);
        Assert.Equal(2, secondRecord!.PackageVersion);
        Assert.Equal(immutableSnapshot, CompleteSnapshot(firstAfterSecondSave));
        Assert.Equal(
            "Test Taxpayer Original",
            Assert.Single(firstAfterSecondSave.Slips).TaxpayerDisplayName);
        Assert.Equal(
            "Test Taxpayer Regenerated",
            Assert.Single(secondRecord.Slips).TaxpayerDisplayName);
        Assert.NotEqual(firstAfterSecondSave.RecordId, secondRecord.RecordId);
    }

    [Fact]
    public async Task VersionSequenceIsScopedByTheCompleteSubjectKey()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(
            context,
            new RepositorySequenceIdentifierSource());

        var firstSubject = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            ReviewRecordRepositoryTestData.Package(caseId: "case_alpha"),
            ReviewRecordRepositoryTestData.EvaluationInstantUtc,
            CancellationToken.None);
        var secondSubject = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            ReviewRecordRepositoryTestData.Package(caseId: "case_beta"),
            ReviewRecordRepositoryTestData.EvaluationInstantUtc,
            CancellationToken.None);
        var firstSubjectAgain = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            ReviewRecordRepositoryTestData.Package(caseId: "case_alpha"),
            ReviewRecordRepositoryTestData.EvaluationInstantUtc,
            CancellationToken.None);

        Assert.Equal(1, firstSubject.Value!.PackageVersion);
        Assert.Equal(1, secondSubject.Value!.PackageVersion);
        Assert.Equal(2, firstSubjectAgain.Value!.PackageVersion);
    }

    [Fact]
    public async Task UniqueVersionRaceIsRefusedAndCannotOverwriteThePriorVersion()
    {
        await using var database = await VersionRaceDatabase.CreateAsync();

        Guid firstRecordId;
        string firstSnapshot;
        await using (var firstContext = database.CreateContext())
        {
            var firstRepository = new SqlReviewRecordRepository(
                firstContext,
                new RepositorySequenceIdentifierSource());
            var firstSave = await firstRepository.SaveAsync(
                ReviewRecordRepositoryTestData.TrustedFirmOne,
                ReviewRecordRepositoryTestData.Package(),
                ReviewRecordRepositoryTestData.EvaluationInstantUtc,
                CancellationToken.None);

            firstRecordId = firstSave.Value!.RecordId;
            firstSnapshot = CompleteSnapshot(firstSave.Value);
        }

        await using (var racingContext = database.CreateContext(
            new InsertCompetingVersionInterceptor()))
        {
            var racingRepository = new SqlReviewRecordRepository(
                racingContext,
                new RepositorySequenceIdentifierSource(first: 100));

            var raced = await racingRepository.SaveAsync(
                ReviewRecordRepositoryTestData.TrustedFirmOne,
                ReviewRecordRepositoryTestData.Package(),
                ReviewRecordRepositoryTestData.EvaluationInstantUtc.AddMinutes(1),
                CancellationToken.None);

            Assert.True(raced.IsFailure);
            var error = Assert.Single(raced.Errors);
            Assert.Equal(TenancyFields.Package, error.Field);
            Assert.Equal("The review record could not be saved.", error.Message);
            Assert.Null(raced.Value);
        }

        await using var verificationContext = database.CreateContext();
        var verificationRepository = new SqlReviewRecordRepository(
            verificationContext,
            new RepositorySequenceIdentifierSource(first: 200));
        var persisted = await verificationRepository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            firstRecordId,
            CancellationToken.None);

        Assert.Equal(firstSnapshot, CompleteSnapshot(persisted!));
        Assert.Equal(
            new[] { 1 },
            await verificationContext.Set<PersistedReviewRecord>()
                .Select(record => record.PackageVersion)
                .ToArrayAsync());
    }

    private static string CompleteSnapshot(PersistedReviewRecord record)
    {
        var pieces = new List<string>
        {
            record.RecordId.ToString("D"),
            record.FirmId.ToString("D"),
            record.CaseId,
            record.ParticipantId,
            record.TaxYear.ToString(CultureInfo.InvariantCulture),
            record.PackageVersion.ToString(CultureInfo.InvariantCulture),
            record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            record.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            record.RetentionUntilUtc.ToString("O", CultureInfo.InvariantCulture),
        };

        foreach (var slip in record.Slips)
        {
            pieces.AddRange(
            [
                slip.SlipId.ToString("D"),
                slip.FirmId.ToString("D"),
                slip.RecordId.ToString("D"),
                slip.Ordinal.ToString(CultureInfo.InvariantCulture),
                slip.SlipTypeCode,
                slip.DocumentReference.ToString("D"),
                slip.TaxpayerDisplayName,
                slip.EmployerDisplayName,
            ]);

            foreach (var amount in slip.BoxAmounts)
            {
                pieces.AddRange(
                [
                    amount.BoxAmountId.ToString("D"),
                    amount.FirmId.ToString("D"),
                    amount.SlipId.ToString("D"),
                    amount.Ordinal.ToString(CultureInfo.InvariantCulture),
                    amount.BoxCode,
                    amount.Amount.ToString(CultureInfo.InvariantCulture),
                ]);
            }

            foreach (var entry in slip.LineEntries)
            {
                pieces.AddRange(
                [
                    entry.LineEntryId.ToString("D"),
                    entry.FirmId.ToString("D"),
                    entry.SlipId.ToString("D"),
                    entry.Ordinal.ToString(CultureInfo.InvariantCulture),
                    entry.LineCode,
                    entry.Amount.ToString(CultureInfo.InvariantCulture),
                ]);

                foreach (var source in entry.SourceBoxCodes)
                {
                    pieces.AddRange(
                    [
                        source.SourceBoxCodeId.ToString("D"),
                        source.FirmId.ToString("D"),
                        source.LineEntryId.ToString("D"),
                        source.Ordinal.ToString(CultureInfo.InvariantCulture),
                        source.BoxCode,
                    ]);
                }
            }

            foreach (var code in slip.UnmappedBoxCodes)
            {
                pieces.AddRange(
                [
                    code.UnmappedBoxCodeId.ToString("D"),
                    code.FirmId.ToString("D"),
                    code.SlipId.ToString("D"),
                    code.Ordinal.ToString(CultureInfo.InvariantCulture),
                    code.BoxCode,
                ]);
            }
        }

        return string.Join("|", pieces);
    }
}

internal sealed class InsertCompetingVersionInterceptor : SaveChangesInterceptor
{
    private bool _inserted;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        var candidateEntry = context?.ChangeTracker
            .Entries<PersistedReviewRecord>()
            .SingleOrDefault(entry => entry.State == EntityState.Added);
        var candidate = candidateEntry?.Entity;

        if (_inserted || context is null || candidate is null)
        {
            return await base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        _inserted = true;
        var competingRecordId =
            Guid.Parse("dddddddd-dddd-4ddd-8ddd-ddddddddddd4");

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO PersistedReviewRecords
                (RecordId, FirmId, CaseId, ParticipantId, TaxYear,
                 PackageVersion, CreatedAtUtc, UpdatedAtUtc, RetentionUntilUtc)
            VALUES
                ({competingRecordId}, {candidate.FirmId}, {candidate.CaseId},
                 {candidate.ParticipantId}, {candidate.TaxYear},
                 {candidate.PackageVersion}, {candidate.CreatedAtUtc},
                 {candidate.UpdatedAtUtc}, {candidate.RetentionUntilUtc})
            """,
            cancellationToken);

        return await base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
}

internal sealed class VersionRaceDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private VersionRaceDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    internal static async Task<VersionRaceDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateCollation(
            PersistedSchema.IdentifierCollation,
            static (left, right) => StringComparer.Ordinal.Compare(left, right));

        var database = new VersionRaceDatabase(connection);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();
        return database;
    }

    internal TaxAiDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TaxAiDbContext>()
            .UseSqlite(_connection, contextOwnsConnection: false)
            .AddInterceptors(interceptors)
            .EnableDetailedErrors()
            .Options;

        return new TaxAiDbContext(options);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
