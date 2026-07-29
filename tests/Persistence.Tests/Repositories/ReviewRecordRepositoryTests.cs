using System.Reflection;
using CaseReview.Models;
using CaseReview.Services;
using Persistence.Repositories;
using Persistence.Tests.Support;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Tests.Repositories;

public sealed class ReviewRecordRepositoryTests
{
    [Fact]
    public async Task SaveAndFindRoundTripTheCompleteProjectedGraph()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var package = ReviewRecordRepositoryTestData.Package(
            reviewedSlips:
            [
                ReviewRecordRepositoryTestData.Slip(
                    lineEntries:
                    [
                        ReviewRecordRepositoryTestData.Line(
                            "line_beta",
                            2.3456m,
                            ["source_beta", "source_alpha"]),
                        ReviewRecordRepositoryTestData.Line("line_alpha", 0m),
                    ],
                    boxAmounts:
                    [
                        ReviewRecordRepositoryTestData.Box("box_gamma", 3.004m),
                        ReviewRecordRepositoryTestData.Box("box_beta", 1.005m),
                        ReviewRecordRepositoryTestData.Box("box_alpha", -1.005m),
                    ],
                    unmappedBoxCodes: ["unmapped_alpha"]),
            ]);
        var projected = ReviewRecordRepositoryTestData.Project(package);

        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        var found = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            saved.Value!.RecordId,
            CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(package.TaxYear, found.TaxYear);
        Assert.Equal(package.CaseId, found.CaseId);
        Assert.Equal(package.ParticipantId, found.ParticipantId);

        var slip = Assert.Single(found.Slips);
        Assert.Equal(
            new[] { "box_gamma", "box_beta", "box_alpha" },
            slip.BoxAmounts.Select(amount => amount.BoxCode));
        Assert.Equal(
            new[] { 3.004m, 1.005m, -1.005m },
            slip.BoxAmounts.Select(amount => amount.Amount));
        Assert.Equal(
            new[] { "line_beta", "line_alpha" },
            slip.LineEntries.Select(entry => entry.LineCode));
        Assert.Equal(
            new[] { 2.3456m, 0m },
            slip.LineEntries.Select(entry => entry.Amount));
        Assert.Equal(
            new[] { "source_beta", "source_alpha" },
            slip.LineEntries[0].SourceBoxCodes.Select(code => code.BoxCode));
        Assert.Equal(
            new[] { "unmapped_alpha" },
            slip.UnmappedBoxCodes.Select(code => code.BoxCode));
    }

    [Fact]
    public async Task FirstSaveStampsVersionInstantsRetentionAndTrustedFirm()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var projected = ReviewRecordRepositoryTestData.Project();

        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal(ReviewRecordRepositoryTestData.FirmOne, saved.Value!.FirmId);
        Assert.Equal(1, saved.Value.PackageVersion);
        Assert.Equal(
            ReviewRecordRepositoryTestData.EvaluationInstantUtc,
            saved.Value.CreatedAtUtc);
        Assert.Equal(
            ReviewRecordRepositoryTestData.EvaluationInstantUtc,
            saved.Value.UpdatedAtUtc);
        Assert.Equal(
            RetentionPolicy.ComputeRetentionUntilUtc(saved.Value.TaxYear),
            saved.Value.RetentionUntilUtc);
        Assert.Equal(TimeSpan.Zero, saved.Value.RetentionUntilUtc.Offset);
    }

    [Fact]
    public async Task EmptyPackagePersistsAsARecordWithNoSlips()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var projected = ReviewRecordRepositoryTestData.Project(
            ReviewRecordRepositoryTestData.Package(reviewedSlips: []));

        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);
        var found = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            saved.Value!.RecordId,
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.NotNull(found);
        Assert.Empty(found.Slips);
    }

    [Fact]
    public async Task EmptyUnmappedCodesAndAbsentOptionalSlipFieldsAreAccepted()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var package = ReviewRecordRepositoryTestData.Package(
            reviewedSlips:
            [
                ReviewRecordRepositoryTestData.Slip(unmappedBoxCodes: []),
            ]);
        var projected = ReviewRecordRepositoryTestData.Project(package);

        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);
        var found = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            saved.Value!.RecordId,
            CancellationToken.None);

        Assert.True(saved.IsSuccess);
        var slip = Assert.Single(found!.Slips);
        Assert.Empty(slip.UnmappedBoxCodes);
    }

    [Fact]
    public async Task EveryCollectionRoundTripsInItsOriginalOrder()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var package = ReviewRecordRepositoryTestData.Package(
            reviewedSlips:
            [
                ReviewRecordRepositoryTestData.OrderedSlip("c"),
                ReviewRecordRepositoryTestData.OrderedSlip("a"),
                ReviewRecordRepositoryTestData.OrderedSlip("b"),
            ]);
        var projected = ReviewRecordRepositoryTestData.Project(package);

        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);
        var found = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            saved.Value!.RecordId,
            CancellationToken.None);

        Assert.Equal(
            new[] { "Test Taxpayer c", "Test Taxpayer a", "Test Taxpayer b" },
            found!.Slips.Select(slip => slip.TaxpayerDisplayName));

        foreach (var slip in found.Slips)
        {
            Assert.Equal(
                new[] { "box_second", "box_first" },
                slip.BoxAmounts.Select(amount => amount.BoxCode));
            Assert.Equal(
                new[] { "line_second", "line_first" },
                slip.LineEntries.Select(entry => entry.LineCode));
            Assert.Equal(
                new[] { "source_second", "source_first" },
                slip.LineEntries[0].SourceBoxCodes.Select(code => code.BoxCode));
            Assert.Equal(
                new[] { "unmapped_second", "unmapped_first" },
                slip.UnmappedBoxCodes.Select(code => code.BoxCode));
        }
    }

    [Fact]
    public async Task DuplicateLineCodesRemainSeparateRowsAndAreNeverAggregated()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new SqlReviewRecordRepository(context);
        var package = ReviewRecordRepositoryTestData.Package(
            reviewedSlips:
            [
                ReviewRecordRepositoryTestData.Slip(
                    taxpayerDisplayName: "Test Taxpayer A",
                    lineEntries:
                    [
                        ReviewRecordRepositoryTestData.Line("same_line", 1.005m),
                    ]),
                ReviewRecordRepositoryTestData.Slip(
                    taxpayerDisplayName: "Test Taxpayer B",
                    lineEntries:
                    [
                        ReviewRecordRepositoryTestData.Line("same_line", 2.005m),
                    ]),
            ]);
        var projected = ReviewRecordRepositoryTestData.Project(package);

        var saved = await repository.SaveAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            projected,
            CancellationToken.None);
        var found = await repository.FindAsync(
            ReviewRecordRepositoryTestData.TrustedFirmOne,
            saved.Value!.RecordId,
            CancellationToken.None);

        var entries = found!.Slips.SelectMany(slip => slip.LineEntries).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry => Assert.Equal("same_line", entry.LineCode));
        Assert.Equal(new[] { 1.005m, 2.005m }, entries.Select(entry => entry.Amount));
    }

    [Fact]
    public void RepositorySurfaceAcceptsOnlyTheAlreadyProjectedTenancyModel()
    {
        var save = typeof(SqlReviewRecordRepository)
            .GetMethod(nameof(IReviewRecordRepository.SaveAsync))!;

        Assert.Equal(
            new[]
            {
                typeof(TrustedFirmId),
                typeof(PersistedReviewRecord),
                typeof(CancellationToken),
            },
            save.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            typeof(Task<ParseResult<PersistedReviewRecord>>),
            save.ReturnType);
        Assert.DoesNotContain(
            typeof(SqlReviewRecordRepository).Assembly.GetReferencedAssemblies(),
            reference => string.Equals(
                reference.Name,
                "CaseReview",
                StringComparison.Ordinal));
    }
}

internal static class ReviewRecordRepositoryTestData
{
    internal static readonly Guid FirmOne =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");

    internal static readonly Guid FirmTwo =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");

    internal static readonly TrustedFirmId TrustedFirmOne =
        TrustedFirmId.FromGuid(FirmOne);

    internal static readonly TrustedFirmId TrustedFirmTwo =
        TrustedFirmId.FromGuid(FirmTwo);

    internal static readonly DateTimeOffset EvaluationInstantUtc =
        new(new DateTime(2028, 2, 3, 4, 5, 6, DateTimeKind.Utc));

    internal static int SupportedTaxYear => SupportedReviewTaxYears.All.Single();

    internal static AccountantReviewPackage Package(
        Guid? firmId = null,
        string caseId = "case_alpha",
        string participantId = "participant_alpha",
        IReadOnlyList<ReviewedSlip>? reviewedSlips = null)
    {
        var slips = reviewedSlips ??
        [
            Slip(),
        ];

        return Construct<AccountantReviewPackage>(
            (firmId ?? FirmOne).ToString("D"),
            caseId,
            participantId,
            SupportedTaxYear,
            slips,
            null);
    }

    internal static PersistedReviewRecord Project(
        AccountantReviewPackage? package = null,
        TrustedFirmId? trustedFirm = null,
        DateTimeOffset? evaluationInstantUtc = null,
        IOpaqueIdentifierSource? identifiers = null)
    {
        var projected = ReviewPackageProjection.Project(
            trustedFirm ?? TrustedFirmOne,
            package ?? Package(),
            evaluationInstantUtc ?? EvaluationInstantUtc,
            identifiers ?? new RepositorySequenceIdentifierSource());

        Assert.True(
            projected.IsSuccess,
            $"Synthetic projection failed with {projected.Errors.Count} error(s).");

        return projected.Value!;
    }

    internal static ReviewedSlip OrderedSlip(string marker) =>
        Slip(
            taxpayerDisplayName: $"Test Taxpayer {marker}",
            employerDisplayName: $"Example Employer {marker}",
            lineEntries:
            [
                Line("line_second", 2.005m, ["source_second", "source_first"]),
                Line("line_first", 1.005m),
            ],
            boxAmounts:
            [
                Box("box_second", 2.005m),
                Box("box_first", 1.005m),
            ],
            unmappedBoxCodes: ["unmapped_second", "unmapped_first"]);

    internal static ReviewedSlip Slip(
        string taxpayerDisplayName = "Test Taxpayer 001",
        string employerDisplayName = "Example Employer A",
        IReadOnlyList<TaxLineEntry>? lineEntries = null,
        IReadOnlyList<ReviewedBoxAmount>? boxAmounts = null,
        IReadOnlyList<string>? unmappedBoxCodes = null)
    {
        var slip = new T4Slip
        {
            Year = SupportedTaxYear,
            EmployeeName = taxpayerDisplayName,
            EmployerName = employerDisplayName,
            DentalBenefitCode = "1",
        };

        return Construct<ReviewedSlip>(
            "synthetic_document",
            SupportedTaxYear,
            "synthetic_source",
            slip,
            lineEntries ??
            [
                Line("line_alpha", 0m),
            ],
            boxAmounts ??
            [
                Box("box_alpha", 1.005m),
            ],
            unmappedBoxCodes ?? ["unmapped_alpha"]);
    }

    internal static ReviewedBoxAmount Box(string code, decimal amount) =>
        Construct<ReviewedBoxAmount>(code, amount);

    internal static TaxLineEntry Line(
        string code,
        decimal amount,
        IReadOnlyList<string>? sourceBoxCodes = null) =>
        new(code, amount, sourceBoxCodes ?? Array.Empty<string>());

    private static T Construct<T>(params object?[] arguments)
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);

        return (T)constructor.Invoke(arguments);
    }
}

internal sealed class RepositorySequenceIdentifierSource : IOpaqueIdentifierSource
{
    private int _next;

    internal RepositorySequenceIdentifierSource(int first = 1)
    {
        _next = first;
    }

    public Guid NewIdentifier()
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(_next).CopyTo(bytes, 0);
        _next++;
        return new Guid(bytes);
    }
}
