using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Persistence.Services;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Composition;

public sealed class DevelopmentSeederTests
{
    [Fact]
    public async Task SeederWritesOneCompleteObviouslySyntheticReviewFixture()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        var seeder = new DevelopmentFixtureSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);

        var record = await context.Set<PersistedReviewRecord>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.BoxAmounts)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.LineEntries)
                    .ThenInclude(entry => entry.SourceBoxCodes)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.UnmappedBoxCodes)
            .SingleAsync();
        var slip = Assert.Single(record.Slips);

        Assert.Equal("Test Taxpayer 001", slip.TaxpayerDisplayName);
        Assert.Equal("Example Employer A", slip.EmployerDisplayName);
        Assert.Equal(1.25m, Assert.Single(slip.BoxAmounts).Amount);
        Assert.Equal(2.50m, Assert.Single(slip.LineEntries).Amount);
        Assert.Single(slip.LineEntries[0].SourceBoxCodes);
        Assert.Single(slip.UnmappedBoxCodes);
        Assert.Equal(nameof(DevelopmentFixtureSeeder), seeder.ToString());
    }

    [Fact]
    public async Task SeederIsIdempotentAndUsesDeterministicIdentifiers()
    {
        var first = await SeedAndReadIdentifiersAsync();
        var second = await SeedAndReadIdentifiersAsync();

        Assert.Equal(first, second);
        Assert.DoesNotContain(Guid.Empty, first);
        Assert.All(
            first,
            identifier => Assert.DoesNotMatch(@"\d{9}", identifier.ToString("D")));
    }

    [Fact]
    public async Task RepeatedSeedingCreatesNoDuplicateRows()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        var seeder = new DevelopmentFixtureSeeder(context);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(1, await context.Set<Firm>().CountAsync());
        Assert.Equal(1, await context.Set<PersistedReviewRecord>().CountAsync());
        Assert.Equal(1, await context.Set<PersistedSlip>().CountAsync());
        Assert.Equal(1, await context.Set<PersistedBoxAmount>().CountAsync());
        Assert.Equal(1, await context.Set<PersistedLineEntry>().CountAsync());
        Assert.Equal(1, await context.Set<PersistedSourceBoxCode>().CountAsync());
        Assert.Equal(1, await context.Set<PersistedUnmappedBoxCode>().CountAsync());
    }

    [Fact]
    public async Task SeederCreatesNoIdentityMembershipSessionOrCredentialRow()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();

        await new DevelopmentFixtureSeeder(context)
            .SeedAsync(CancellationToken.None);

        Assert.Empty(await context.Set<PortalUser>().ToArrayAsync());
        Assert.Empty(await context.Set<FirmMembership>().ToArrayAsync());
        Assert.Empty(await context.Set<AuthenticationSession>().ToArrayAsync());
    }

    [Fact]
    public async Task EverySeededStringIsSyntheticAndContainsNoNineDigitRun()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();

        await new DevelopmentFixtureSeeder(context)
            .SeedAsync(CancellationToken.None);

        var record = await context.Set<PersistedReviewRecord>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.BoxAmounts)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.LineEntries)
                    .ThenInclude(entry => entry.SourceBoxCodes)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.UnmappedBoxCodes)
            .SingleAsync();
        var firm = await context.Set<Firm>().AsNoTracking().SingleAsync();
        var slip = Assert.Single(record.Slips);

        var values = new[]
        {
            firm.DisplayName,
            record.CaseId,
            record.ParticipantId,
            slip.SlipTypeCode,
            slip.TaxpayerDisplayName,
            slip.EmployerDisplayName,
            Assert.Single(slip.BoxAmounts).BoxCode,
            Assert.Single(slip.LineEntries).LineCode,
            Assert.Single(slip.LineEntries[0].SourceBoxCodes).BoxCode,
            Assert.Single(slip.UnmappedBoxCodes).BoxCode,
        };

        Assert.Contains("Test Taxpayer 001", values);
        Assert.Contains("Example Employer A", values);
        Assert.All(
            values,
            value => Assert.DoesNotMatch(@"\d{9}", value));
    }

    [Fact]
    public void SeedSourceContainsNoForbiddenPersonalFieldOrIdentityLiteral()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Persistence",
            "Services",
            "DevelopmentFixtureSeeder.cs"));

        Assert.DoesNotMatch(
            new Regex(
                @"\b(Sin|SocialInsurance|Address|Email|Phone)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            source);
        Assert.DoesNotMatch(@"\d{9}", source);
        Assert.DoesNotMatch(
            @"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b",
            source);
        Assert.DoesNotContain("DateTimeOffset.Now", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.", source, StringComparison.Ordinal);
    }

    private static async Task<Guid[]> SeedAndReadIdentifiersAsync()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();

        await new DevelopmentFixtureSeeder(context)
            .SeedAsync(CancellationToken.None);

        var record = await context.Set<PersistedReviewRecord>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.BoxAmounts)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.LineEntries)
                    .ThenInclude(entry => entry.SourceBoxCodes)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.UnmappedBoxCodes)
            .SingleAsync();
        var slip = Assert.Single(record.Slips);

        return
        [
            record.FirmId,
            record.RecordId,
            slip.SlipId,
            slip.DocumentReference,
            Assert.Single(slip.BoxAmounts).BoxAmountId,
            Assert.Single(slip.LineEntries).LineEntryId,
            Assert.Single(slip.LineEntries[0].SourceBoxCodes).SourceBoxCodeId,
            Assert.Single(slip.UnmappedBoxCodes).UnmappedBoxCodeId,
        ];
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
