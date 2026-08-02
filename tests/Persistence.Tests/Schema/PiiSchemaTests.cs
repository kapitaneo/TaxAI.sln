using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence.Tests.Support;
using Tenancy.Models;

namespace Persistence.Tests.Schema;

public sealed class PiiSchemaTests
{
    private static readonly string[] ForbiddenPropertyFragments =
    [
        "Address",
        "Birth",
        "Claim",
        "Email",
        "EmployerAccount",
        "Password",
        "Phone",
        "ProviderProfile",
        "Refresh",
        "Rpp",
        "Sin",
        "SocialInsurance",
        "SourceReference",
        "Ticket",
        "Token",
    ];

    [Fact] // AC-86, AC-87
    public void NoForbiddenPiiColumnOrPresenceFlagExists()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        var propertyNames = context.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.Name)
            .ToArray();

        foreach (var fragment in ForbiddenPropertyFragments)
        {
            Assert.DoesNotContain(
                propertyNames,
                name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("Presence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // AC-100, AC-184
    public void IdentityTablesCarryOnlyTheApprovedStructuralColumns()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var expected = new Dictionary<Type, string[]>
        {
            [typeof(PortalUser)] =
            [
                "CreatedAtUtc", "IsActive", "Issuer", "Subject", "UpdatedAtUtc", "UserId",
            ],
            [typeof(FirmMembership)] =
            [
                "CreatedAtUtc", "FirmId", "IsActive", "MembershipId",
                "UpdatedAtUtc", "UserId",
            ],
            [typeof(AuthenticationSession)] =
            [
                "AbsoluteExpiresAtUtc", "ActiveFirmId", "CreatedAtUtc",
                "IdleExpiresAtUtc", "LastSeenAtUtc", "RevokedAtUtc",
                "SessionId", "UserId",
            ],
        };

        foreach (var (type, properties) in expected)
        {
            Assert.Equal(
                properties,
                context.Model.FindEntityType(type)!
                    .GetProperties()
                    .Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    [Fact] // AC-115, AC-116
    public void NoUpstreamDomainTypeOrSerializerIsMapped()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();

        Assert.All(
            context.Model.GetEntityTypes(),
            entity => Assert.Contains(
                entity.ClrType.Assembly.GetName().Name,
                new[] { "Persistence", "Tenancy" }));

        var converters = context.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Select(property => property.GetValueConverter())
            .Where(converter => converter is not null)
            .ToArray();

        Assert.All(converters, converter => Assert.Equal(typeof(int), converter!.ProviderClrType));
    }

    [Fact] // AC-125, AC-187, AC-188
    public void TheInitialModelContainsNoSeedData()
    {
        using var context = RelationalTestContext.CreateSqlServerContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.All(
            model.GetEntityTypes(),
            entity => Assert.Empty(entity.GetSeedData()));
    }
}
