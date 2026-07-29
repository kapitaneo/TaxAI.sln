using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Persistence.Repositories;
using Persistence.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Tests.Repositories;

public sealed class ReviewRecordTenantIsolationTests
{
    [Fact]
    public async Task CrossFirmReadIsIdenticalToNotFound()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var projected = ReviewRecordRepositoryTestData.Project();
        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);

        var crossFirm = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmTwo,
            saved.Value!.RecordId,
            CancellationToken.None);
        var missing = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmTwo,
            Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3"),
            CancellationToken.None);

        Assert.Null(crossFirm);
        Assert.Null(missing);
        Assert.Equal(missing, crossFirm);
    }

    [Fact]
    public async Task SameOpaqueRecordIdInTwoFirmsReturnsOnlyTheScopedRecord()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var firmOneRepository = new SqlReviewRecordRepository(context);
        var firmTwoRepository = new SqlReviewRecordRepository(context);
        var firmOneProjected = ReviewRecordRepositoryTestData.Project(
            package: ReviewRecordRepositoryTestData.Package(
                reviewedSlips:
                [
                    ReviewRecordRepositoryTestData.Slip(
                        taxpayerDisplayName: "Test Taxpayer Firm A"),
                ]),
            identifiers: new RepositorySequenceIdentifierSource());
        var firmTwoProjected = ReviewRecordRepositoryTestData.Project(
            package: ReviewRecordRepositoryTestData.Package(
                firmId: ReviewRecordRepositoryTestData.FirmTwo,
                reviewedSlips:
                [
                    ReviewRecordRepositoryTestData.Slip(
                        taxpayerDisplayName: "Test Taxpayer Firm B"),
                ]),
            trustedFirm: ReviewRecordRepositoryTestData.TrustedFirmTwo,
            identifiers: new RepositorySequenceIdentifierSource());

        var firmOneSave = await firmOneRepository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            firmOneProjected,
            CancellationToken.None);
        var firmTwoSave = await firmTwoRepository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmTwo,
            firmTwoProjected,
            CancellationToken.None);

        Assert.Equal(firmOneSave.Value!.RecordId, firmTwoSave.Value!.RecordId);

        var firmOneRecord = await firmOneRepository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            firmOneSave.Value.RecordId,
            CancellationToken.None);
        var firmTwoRecord = await firmTwoRepository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmTwo,
            firmTwoSave.Value.RecordId,
            CancellationToken.None);

        Assert.Equal(ReviewRecordRepositoryTestData.FirmOne, firmOneRecord!.FirmId);
        Assert.Equal(
            "Test Taxpayer Firm A",
            Assert.Single(firmOneRecord.Slips).TaxpayerDisplayName);
        Assert.Equal(ReviewRecordRepositoryTestData.FirmTwo, firmTwoRecord!.FirmId);
        Assert.Equal(
            "Test Taxpayer Firm B",
            Assert.Single(firmTwoRecord.Slips).TaxpayerDisplayName);
    }

    [Fact]
    public async Task ProjectedRecordFirmMismatchIsRefusedBeforeAnythingIsWritten()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var projectedForFirmTwo = ReviewRecordRepositoryTestData.Project(
            package: ReviewRecordRepositoryTestData.Package(
                firmId: ReviewRecordRepositoryTestData.FirmTwo),
            trustedFirm: ReviewRecordRepositoryTestData.TrustedFirmTwo);

        var result = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projectedForFirmTwo,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Contains(
            result.Errors,
            error => error.Field == TenancyFields.FirmId &&
                     error.Message ==
                         "The review record does not belong to the trusted firm.");
        Assert.Equal(
            0,
            await context.Set<PersistedReviewRecord>().CountAsync());
        Assert.DoesNotContain(
            ReviewRecordRepositoryTestData.FirmOne.ToString("D"),
            string.Join("|", result.Errors.Select(error => error.Message)),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ReviewRecordRepositoryTestData.FirmTwo.ToString("D"),
            string.Join("|", result.Errors.Select(error => error.Message)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RepositorySurfaceHasNoUpdateUpsertOrDeletePath()
    {
        Assert.Equal(
            new[]
            {
                nameof(IReviewRecordRepository.FindAsync),
                nameof(IReviewRecordRepository.SaveAsync),
            },
            typeof(IReviewRecordRepository)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        var concreteMethods = typeof(SqlReviewRecordRepository)
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(nameof(IReviewRecordRepository.FindAsync), concreteMethods);
        Assert.Contains(nameof(IReviewRecordRepository.SaveAsync), concreteMethods);
        Assert.DoesNotContain(
            concreteMethods,
            name =>
                name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Upsert", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }
}
