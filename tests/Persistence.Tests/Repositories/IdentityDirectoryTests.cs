using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Persistence.Repositories;
using Persistence.Tests.Support;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Tests.Repositories;

public sealed class IdentityDirectoryTests
{
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");

    private static readonly Guid OtherUserId =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");

    private static readonly Guid FirmId =
        Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3");

    private static readonly Guid OtherFirmId =
        Guid.Parse("dddddddd-dddd-4ddd-8ddd-ddddddddddd4");

    private static readonly Guid UnrequestedFirmId =
        Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee5");

    private static readonly Guid MembershipId =
        Guid.Parse("abababab-abab-4aba-8aba-abababababa6");

    private static readonly Guid OtherMembershipId =
        Guid.Parse("bcbcbcbc-bcbc-4bcb-8bcb-bcbcbcbcbcb7");

    private static readonly Guid InactiveMembershipId =
        Guid.Parse("cdcdcdcd-cdcd-4cdc-8cdc-cdcdcdcdcdc8");

    [Fact]
    public void TheDirectoryIsInternalSealedAndImplementsOnlyTheIdentityPort()
    {
        var type = typeof(SqlIdentityDirectory);

        Assert.False(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Equal(new[] { typeof(IIdentityDirectory) }, type.GetInterfaces());
    }

    [Fact]
    public async Task FreshlyMigratedDatabaseReturnsEmptyForEveryIdentityRead()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        var directory = new SqlIdentityDirectory(context);

        Assert.Null(await directory.FindUserAsync(
            "https://identity.example.test",
            "unprovisioned-subject",
            CancellationToken.None));
        Assert.Null(await directory.FindUserByIdAsync(
            UserId,
            CancellationToken.None));
        Assert.Empty(await directory.FindActiveMembershipsAsync(
            UserId,
            CancellationToken.None));
        Assert.Null(await directory.FindMembershipAsync(
            UserId,
            MembershipId,
            CancellationToken.None));
        Assert.Empty(await directory.FindFirmsAsync(
            [FirmId],
            CancellationToken.None));
    }

    [Fact]
    public async Task FindUserByIdReturnsTheExactLocalUserIncludingInactiveState()
    {
        const string issuer = "https://identity.example.test/bootstrap";
        const string subject = "bootstrap-subject";

        await using var relational = await RelationalTestContext.CreateAsync();
        await using (var writeContext = relational.CreateContext())
        {
            writeContext.Add(User(UserId, issuer, subject, isActive: false));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = relational.CreateContext();
        var directory = new SqlIdentityDirectory(readContext);

        var found = await directory.FindUserByIdAsync(
            UserId,
            CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(UserId, found.UserId);
        Assert.Equal(issuer, found.Issuer);
        Assert.Equal(subject, found.Subject);
        Assert.False(found.IsActive);
        Assert.Null(await directory.FindUserByIdAsync(
            OtherUserId,
            CancellationToken.None));
    }

    [Fact]
    public void UserLookupByIdExposesOnlyTheApprovedLocalIdentityShape()
    {
        var method = typeof(IIdentityDirectory)
            .GetMethod(nameof(IIdentityDirectory.FindUserByIdAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<PortalUser?>), method.ReturnType);
        Assert.Equal(
            new[] { typeof(Guid), typeof(CancellationToken) },
            method.GetParameters().Select(parameter => parameter.ParameterType));

        Assert.Equal(
            new[]
            {
                nameof(PortalUser.CreatedAtUtc),
                nameof(PortalUser.IsActive),
                nameof(PortalUser.Issuer),
                nameof(PortalUser.Subject),
                nameof(PortalUser.UpdatedAtUtc),
                nameof(PortalUser.UserId),
            },
            typeof(PortalUser)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task FindUserRequiresAnOrdinalIssuerAndSubjectMatch()
    {
        const string issuer = "https://identity.example.test/Tenant-A";
        const string subject = "Subject-\u00c9";

        await using var relational = await RelationalTestContext.CreateAsync();
        await using (var writeContext = relational.CreateContext())
        {
            writeContext.Add(User(UserId, issuer, subject, isActive: false));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = relational.CreateContext();
        var directory = new SqlIdentityDirectory(readContext);

        var found = await directory.FindUserAsync(
            issuer,
            subject,
            CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(UserId, found.UserId);
        Assert.False(found.IsActive);
        Assert.Null(await directory.FindUserAsync(
            "https://identity.example.test/tenant-A",
            subject,
            CancellationToken.None));
        Assert.Null(await directory.FindUserAsync(
            issuer,
            "subject-\u00c9",
            CancellationToken.None));
        Assert.Null(await directory.FindUserAsync(
            issuer,
            "Subject-E\u0301",
            CancellationToken.None));
    }

    [Fact]
    public async Task FindActiveMembershipsReturnsOnlyActiveRowsForTheUser()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using (var writeContext = relational.CreateContext())
        {
            writeContext.AddRange(
                User(UserId, "issuer-a", "subject-a"),
                User(OtherUserId, "issuer-b", "subject-b"),
                Firm(FirmId, "Example Firm A"),
                Firm(OtherFirmId, "Example Firm B"),
                Firm(UnrequestedFirmId, "Example Firm C"),
                Membership(MembershipId, FirmId, UserId, isActive: true),
                Membership(OtherMembershipId, OtherFirmId, UserId, isActive: true),
                Membership(InactiveMembershipId, UnrequestedFirmId, UserId, isActive: false),
                Membership(
                    Guid.Parse("dededede-dede-4ded-8ded-dededededed9"),
                    FirmId,
                    OtherUserId,
                    isActive: true));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = relational.CreateContext();
        var directory = new SqlIdentityDirectory(readContext);

        var memberships = await directory.FindActiveMembershipsAsync(
            UserId,
            CancellationToken.None);

        Assert.Equal(
            new[] { MembershipId, OtherMembershipId }.OrderBy(id => id),
            memberships.Select(membership => membership.MembershipId).OrderBy(id => id));
        Assert.All(memberships, membership =>
        {
            Assert.Equal(UserId, membership.UserId);
            Assert.True(membership.IsActive);
        });
        Assert.True(Assert.IsAssignableFrom<IList<FirmMembership>>(memberships).IsReadOnly);
    }

    [Fact]
    public async Task FindMembershipRequiresBothTheAuthenticatedUserAndMembershipId()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using (var writeContext = relational.CreateContext())
        {
            writeContext.AddRange(
                User(UserId, "issuer-a", "subject-a"),
                User(OtherUserId, "issuer-b", "subject-b"),
                Firm(FirmId, "Example Firm A"),
                Membership(MembershipId, FirmId, UserId, isActive: false));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = relational.CreateContext();
        var directory = new SqlIdentityDirectory(readContext);

        var found = await directory.FindMembershipAsync(
            UserId,
            MembershipId,
            CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(MembershipId, found.MembershipId);
        Assert.False(found.IsActive);
        Assert.Null(await directory.FindMembershipAsync(
            OtherUserId,
            MembershipId,
            CancellationToken.None));
        Assert.Null(await directory.FindMembershipAsync(
            UserId,
            OtherMembershipId,
            CancellationToken.None));
    }

    [Fact]
    public async Task FindFirmsReturnsOnlyRequestedExistingFirmsOnce()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using (var writeContext = relational.CreateContext())
        {
            writeContext.AddRange(
                Firm(FirmId, "Example Firm A"),
                Firm(OtherFirmId, "Example Firm B"),
                Firm(UnrequestedFirmId, "Example Firm C"));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = relational.CreateContext();
        var directory = new SqlIdentityDirectory(readContext);

        var firms = await directory.FindFirmsAsync(
            [
                OtherFirmId,
                Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff"),
                FirmId,
                OtherFirmId,
            ],
            CancellationToken.None);

        Assert.Equal(
            new[] { FirmId, OtherFirmId }.OrderBy(id => id),
            firms.Select(firm => firm.FirmId).OrderBy(id => id));
        Assert.True(Assert.IsAssignableFrom<IList<Firm>>(firms).IsReadOnly);
    }

    [Fact]
    public async Task EmptyFirmRequestHonorsCancellationWithoutQuerying()
    {
        await using var relational = await RelationalTestContext.CreateAsync();
        await using var context = relational.CreateContext();
        var directory = new SqlIdentityDirectory(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => directory.FindFirmsAsync([], cancellation.Token));
    }

    private static PortalUser User(
        Guid userId,
        string issuer,
        string subject,
        bool isActive = true) =>
        Construct<PortalUser>(
            userId,
            issuer,
            subject,
            isActive,
            SyntheticFixtures.CreatedAtUtc,
            SyntheticFixtures.CreatedAtUtc);

    private static Firm Firm(
        Guid firmId,
        string displayName) =>
        Construct<Firm>(
            firmId,
            displayName,
            SyntheticFixtures.CreatedAtUtc,
            SyntheticFixtures.CreatedAtUtc);

    private static FirmMembership Membership(
        Guid membershipId,
        Guid firmId,
        Guid userId,
        bool isActive) =>
        Construct<FirmMembership>(
            membershipId,
            firmId,
            userId,
            isActive,
            SyntheticFixtures.CreatedAtUtc,
            SyntheticFixtures.CreatedAtUtc);

    private static T Construct<T>(params object?[] arguments)
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);

        return (T)constructor.Invoke(arguments);
    }
}
