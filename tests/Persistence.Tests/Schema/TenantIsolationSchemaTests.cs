using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence.Tests.Support;
using Persistence.Entities;
using Tenancy.Models;

namespace Persistence.Tests.Schema;

public sealed class TenantIsolationSchemaTests
{
    private static readonly Type[] IdentityCarveOut =
    [
        typeof(AuthenticationSession),
        typeof(FirmMembership),
        typeof(PortalUser),
    ];

    [Fact] // AC-117, AC-183
    public void TheIdentityCarveOutIsExactlyThreeTables()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var entities = context.Model.GetEntityTypes().ToArray();

        Assert.Equal(
            new[]
            {
                typeof(AuthenticationSession),
                typeof(FirmMembership),
                typeof(PortalUser),
            },
            IdentityCarveOut.OrderBy(type => type.FullName, StringComparer.Ordinal));

        var tenantEntities = entities
            .Where(entity => !IdentityCarveOut.Contains(entity.ClrType))
            .ToArray();

        Assert.All(
            tenantEntities,
            entity =>
            {
                var firmId = entity.FindProperty(nameof(Firm.FirmId));
                Assert.NotNull(firmId);
                Assert.False(firmId.IsNullable);
            });

        Assert.Null(
            entities.Single(entity => entity.ClrType == typeof(PortalUser))
                .FindProperty(nameof(Firm.FirmId)));
        Assert.Null(
            entities.Single(entity => entity.ClrType == typeof(AuthenticationSession))
                .FindProperty(nameof(Firm.FirmId)));
        Assert.NotNull(
            entities.Single(entity => entity.ClrType == typeof(FirmMembership))
                .FindProperty(nameof(Firm.FirmId)));
    }

    [Fact] // AC-118
    public void FirmIdLeadsEveryTenantKeyAndIndex()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var tenantEntities = context.Model
            .GetEntityTypes()
            .Where(entity => !IdentityCarveOut.Contains(entity.ClrType))
            .ToArray();

        var bootstrapIndexes = new List<(Type Entity, string Property)>();
        foreach (var entity in tenantEntities)
        {
            Assert.All(
                entity.GetKeys(),
                key => Assert.Equal(nameof(Firm.FirmId), key.Properties[0].Name));
            foreach (var index in entity.GetIndexes())
            {
                if (index.Properties[0].Name != nameof(Firm.FirmId))
                {
                    bootstrapIndexes.Add((entity.ClrType, index.Properties[0].Name));
                }
            }
        }

        Assert.Equal(
            new[]
            {
                (typeof(IntakeClientSessionEntity), nameof(IntakeClientSessionEntity.SessionDigest)),
                (typeof(IntakeLinkEntity), nameof(IntakeLinkEntity.CredentialDigest)),
            },
            bootstrapIndexes.OrderBy(entry => entry.Entity.FullName, StringComparer.Ordinal));
    }

    [Fact] // AC-118
    public void EveryReviewHierarchyForeignKeyCarriesFirmIdFirst()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var reviewTypes = new[]
        {
            typeof(PersistedSlip),
            typeof(PersistedBoxAmount),
            typeof(PersistedLineEntry),
            typeof(PersistedSourceBoxCode),
            typeof(PersistedUnmappedBoxCode),
        };

        foreach (var type in reviewTypes)
        {
            var foreignKeys = context.Model.FindEntityType(type)!.GetForeignKeys().ToArray();
            var key = Assert.Single(foreignKeys);
            Assert.Equal(nameof(Firm.FirmId), key.Properties[0].Name);
            Assert.Equal(nameof(Firm.FirmId), key.PrincipalKey.Properties[0].Name);
        }
    }

    [Fact] // AC-119
    public void EveryFirmIdentifierUsesUniqueidentifier()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var firmProperties = context.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property =>
                property.Name == nameof(Firm.FirmId) ||
                property.Name == nameof(AuthenticationSession.ActiveFirmId))
            .ToArray();

        Assert.NotEmpty(firmProperties);
        Assert.All(
            firmProperties,
            property => Assert.Equal("uniqueidentifier", property.GetColumnType()));
    }

    [Fact] // AC-120
    public void EveryStringIdentifierDeclaresTheBinaryCollation()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        var identifiers = new (Type Type, string Property)[]
        {
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.CaseId)),
            (typeof(PersistedReviewRecord), nameof(PersistedReviewRecord.ParticipantId)),
            (typeof(PersistedSlip), nameof(PersistedSlip.SlipTypeCode)),
            (typeof(PersistedBoxAmount), nameof(PersistedBoxAmount.BoxCode)),
            (typeof(PersistedLineEntry), nameof(PersistedLineEntry.LineCode)),
            (typeof(PersistedSourceBoxCode), nameof(PersistedSourceBoxCode.BoxCode)),
            (typeof(PersistedUnmappedBoxCode), nameof(PersistedUnmappedBoxCode.BoxCode)),
            (typeof(PortalUser), nameof(PortalUser.Issuer)),
            (typeof(PortalUser), nameof(PortalUser.Subject)),
        };

        foreach (var (type, propertyName) in identifiers)
        {
            var property = model.FindEntityType(type)!.FindProperty(propertyName)!;
            Assert.Equal(PersistedSchema.IdentifierCollation, property.GetCollation());
        }
    }

    [Fact] // AC-122
    public void SnapshotSubjectAndVersionAreUniqueWithinFirm()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var record = context.Model.FindEntityType(typeof(PersistedReviewRecord))!;

        var subjectVersion = Assert.Single(
            record.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Count == 5);

        Assert.Equal(
            new[]
            {
                nameof(PersistedReviewRecord.FirmId),
                nameof(PersistedReviewRecord.CaseId),
                nameof(PersistedReviewRecord.ParticipantId),
                nameof(PersistedReviewRecord.TaxYear),
                nameof(PersistedReviewRecord.PackageVersion),
            },
            subjectVersion.Properties.Select(property => property.Name));
    }

    [Fact] // identity constraints
    public void IdentityConstraintsMatchTheirClosedKeys()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var user = context.Model.FindEntityType(typeof(PortalUser))!;
        var userIdentity = Assert.Single(user.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            new[] { nameof(PortalUser.Issuer), nameof(PortalUser.Subject) },
            userIdentity.Properties.Select(property => property.Name));

        var membership = context.Model.FindEntityType(typeof(FirmMembership))!;
        var firmUser = Assert.Single(membership.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            new[] { nameof(FirmMembership.FirmId), nameof(FirmMembership.UserId) },
            firmUser.Properties.Select(property => property.Name));
    }
}
