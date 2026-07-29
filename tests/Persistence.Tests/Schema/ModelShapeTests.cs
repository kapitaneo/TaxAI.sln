using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence.Configurations;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Schema;

public sealed class ModelShapeTests
{
    private static readonly Type[] ExpectedEntityTypes =
    [
        typeof(AuthenticationSession),
        typeof(Firm),
        typeof(FirmMembership),
        typeof(PersistedBoxAmount),
        typeof(PersistedLineEntry),
        typeof(PersistedReviewRecord),
        typeof(PersistedSlip),
        typeof(PersistedSourceBoxCode),
        typeof(PersistedUnmappedBoxCode),
        typeof(PortalUser),
    ];

    [Fact] // AC-115
    public void TheModelMapsExactlyTheExplicitPersistenceEntities()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.Equal(
            ExpectedEntityTypes.OrderBy(type => type.FullName, StringComparer.Ordinal),
            context.Model.GetEntityTypes()
                .Select(entity => entity.ClrType)
                .OrderBy(type => type.FullName, StringComparer.Ordinal));
    }

    [Fact] // T11
    public void EveryEntityHasExactlyOneDedicatedConfiguration()
    {
        var configurationTypes = typeof(PersistedReviewRecordConfiguration)
            .Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract)
            .SelectMany(type => type.GetInterfaces()
                .Where(candidate =>
                    candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
                .Select(candidate => new
                {
                    Configuration = type,
                    Entity = candidate.GenericTypeArguments.Single(),
                }))
            .ToArray();

        Assert.Equal(ExpectedEntityTypes.Length, configurationTypes.Length);
        Assert.Equal(
            ExpectedEntityTypes.OrderBy(type => type.FullName, StringComparer.Ordinal),
            configurationTypes
                .Select(entry => entry.Entity)
                .OrderBy(type => type.FullName, StringComparer.Ordinal));
        Assert.All(
            configurationTypes,
            entry => Assert.Equal(1, configurationTypes.Count(
                other => other.Entity == entry.Entity)));
    }

    [Fact] // AC-123
    public void ReviewReadModelDeclaresExactlyTheClosedAllowlist()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var expected = new Dictionary<Type, string[]>
        {
            [typeof(PersistedReviewRecord)] =
            [
                "CaseId", "CreatedAtUtc", "FirmId", "PackageVersion",
                "ParticipantId", "RecordId", "RetentionUntilUtc", "TaxYear",
                "UpdatedAtUtc",
            ],
            [typeof(PersistedSlip)] =
            [
                "DocumentReference", "EmployerDisplayName", "FirmId", "Ordinal",
                "RecordId", "SlipId", "SlipTypeCode", "TaxpayerDisplayName",
            ],
            [typeof(PersistedBoxAmount)] =
            [
                "Amount", "BoxAmountId", "BoxCode", "FirmId", "Ordinal", "SlipId",
            ],
            [typeof(PersistedLineEntry)] =
            [
                "Amount", "FirmId", "LineCode", "LineEntryId", "Ordinal", "SlipId",
            ],
            [typeof(PersistedSourceBoxCode)] =
            [
                "BoxCode", "FirmId", "LineEntryId", "Ordinal", "SourceBoxCodeId",
            ],
            [typeof(PersistedUnmappedBoxCode)] =
            [
                "BoxCode", "FirmId", "Ordinal", "SlipId", "UnmappedBoxCodeId",
            ],
        };

        foreach (var (type, expectedProperties) in expected)
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(
                expectedProperties,
                entity.GetProperties()
                    .Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    [Fact] // AC-121
    public void EveryMonetaryPropertyDeclaresExplicitPrecisionAndScale()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var monetaryProperties = context.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(decimal))
            .ToArray();

        Assert.Equal(2, monetaryProperties.Length);
        Assert.All(
            monetaryProperties,
            property =>
            {
                Assert.Equal(PersistedSchema.MonetaryPrecision, property.GetPrecision());
                Assert.Equal(PersistedSchema.MonetaryScale, property.GetScale());
                Assert.Equal(
                    $"decimal({PersistedSchema.MonetaryPrecision},{PersistedSchema.MonetaryScale})",
                    property.GetColumnType());
            });
    }

    [Fact] // AC-162
    public void SessionEntityDeclaresExactlyTheRequiredFields()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var session = context.Model.FindEntityType(typeof(AuthenticationSession))!;

        Assert.Equal(
            new[]
            {
                "AbsoluteExpiresAtUtc", "ActiveFirmId", "CreatedAtUtc",
                "IdleExpiresAtUtc", "LastSeenAtUtc", "RevokedAtUtc",
                "SessionId", "UserId",
            },
            session.GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.True(session.FindProperty(nameof(AuthenticationSession.ActiveFirmId))!.IsNullable);
        Assert.True(session.FindProperty(nameof(AuthenticationSession.RevokedAtUtc))!.IsNullable);
    }

    [Fact] // T11 backing-field requirement
    public void ImmutableCollectionNavigationsUseTheirDeclaredBackingFields()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var expected = new Dictionary<(Type Type, string Navigation), string>
        {
            [(typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.Slips))] = "_slips",
            [(typeof(PersistedSlip), nameof(PersistedSlip.BoxAmounts))] = "_boxAmounts",
            [(typeof(PersistedSlip), nameof(PersistedSlip.LineEntries))] = "_lineEntries",
            [(typeof(PersistedSlip), nameof(PersistedSlip.UnmappedBoxCodes))] = "_unmappedBoxCodes",
            [(typeof(PersistedLineEntry), nameof(PersistedLineEntry.SourceBoxCodes))] = "_sourceBoxCodes",
        };

        foreach (var ((type, navigationName), fieldName) in expected)
        {
            var navigation = context.Model.FindEntityType(type)!.FindNavigation(navigationName)!;
            Assert.Equal(fieldName, navigation.FieldInfo!.Name);
            Assert.Equal(PropertyAccessMode.Field, navigation.GetPropertyAccessMode());
        }
    }

    [Fact] // AC-155, AC-156
    public void InstantsUseDateTimeOffsetAndRetentionIsDocumentedAsExclusive()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var instantProperties = model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property =>
                property.ClrType == typeof(DateTimeOffset) ||
                property.ClrType == typeof(DateTimeOffset?))
            .ToArray();

        Assert.NotEmpty(instantProperties);
        Assert.All(
            instantProperties,
            property => Assert.Equal("datetimeoffset", property.GetColumnType()));

        var retention = model
            .FindEntityType(typeof(PersistedReviewRecord))!
            .FindProperty(nameof(PersistedReviewRecord.RetentionUntilUtc))!;

        Assert.Contains("Exclusive UTC", retention.GetComment(), StringComparison.Ordinal);
        Assert.False(retention.IsNullable);
    }

    [Fact] // AC-155, backing-field materialization
    public async Task RelationalHarnessRoundTripsTheImmutableGraphWithUtcInstants()
    {
        await using var database = await RelationalTestContext.CreateAsync();
        await using (var writeContext = database.CreateContext())
        {
            writeContext.Add(SyntheticFixtures.Firm());
            writeContext.Add(SyntheticFixtures.User());
            writeContext.Add(SyntheticFixtures.Membership());
            writeContext.Add(SyntheticFixtures.Session(SyntheticFixtures.FirmId));
            writeContext.Add(SyntheticFixtures.ReviewRecord());
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = database.CreateContext();
        var record = await readContext.Set<PersistedReviewRecord>()
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.BoxAmounts)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.LineEntries)
                    .ThenInclude(entry => entry.SourceBoxCodes)
            .Include(candidate => candidate.Slips)
                .ThenInclude(slip => slip.UnmappedBoxCodes)
            .SingleAsync();
        var session = await readContext.Set<AuthenticationSession>().SingleAsync();

        var slip = Assert.Single(record.Slips);
        Assert.Single(slip.BoxAmounts);
        Assert.Single(slip.LineEntries);
        Assert.Single(slip.LineEntries.Single().SourceBoxCodes);
        Assert.Single(slip.UnmappedBoxCodes);

        Assert.Equal(TimeSpan.Zero, record.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, record.UpdatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, record.RetentionUntilUtc.Offset);
        Assert.Equal(TimeSpan.Zero, session.CreatedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, session.LastSeenAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, session.IdleExpiresAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, session.AbsoluteExpiresAtUtc.Offset);
    }
}
