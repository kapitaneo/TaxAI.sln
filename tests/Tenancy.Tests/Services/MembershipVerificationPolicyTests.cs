using System.Text.Json;
using Tenancy.Models;
using Tenancy.Services;

namespace Tenancy.Tests.Services;

public sealed class MembershipVerificationPolicyTests
{
    private static readonly Guid UserId =
        Guid.Parse("12ab34cd-56ef-78ab-90cd-ef12ab34cd56");

    private static readonly Guid OtherUserId =
        Guid.Parse("ab12cd34-ef56-78ab-90cd-12ef34ab56cd");

    private static readonly Guid FirmId =
        Guid.Parse("23bc45de-67fa-89bc-a1de-fa23bc45de67");

    private static readonly Guid OtherFirmId =
        Guid.Parse("bc23de45-fa67-89bc-a1de-23fa45bc67de");

    private static readonly Guid MembershipId =
        Guid.Parse("34cd56ef-78ab-90cd-b2ef-ab34cd56ef78");

    private static readonly Guid SessionId =
        Guid.Parse("45de67fa-89bc-a1de-c3fa-bc45de67fa89");

    private static readonly DateTimeOffset CreatedAtUtc = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Verify_ActiveUserAndMembershipMatchingTheSession_ReturnsVerified()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(isActive: true),
            Session());

        Assert.True(result.IsVerified);
        Assert.Null(result.RefusalReason);
        Assert.Equal("MembershipVerification(Verified)", result.ToString());
    }

    [Fact]
    public void Verify_MissingUser_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            user: null,
            Membership(isActive: true),
            Session());

        AssertRefused(result, TenancyErrors.UserNotActive);
    }

    [Fact]
    public void Verify_InactiveUser_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: false),
            Membership(isActive: true),
            Session());

        AssertRefused(result, TenancyErrors.UserNotActive);
    }

    [Fact]
    public void Verify_UserThatDoesNotMatchTheSession_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(userId: OtherUserId, isActive: true),
            Membership(isActive: true),
            Session());

        AssertRefused(result, TenancyErrors.UserNotActive);
    }

    [Fact]
    public void Verify_MissingMembership_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            membership: null,
            Session());

        AssertRefused(result, TenancyErrors.MembershipNotFound);
    }

    [Fact]
    public void Verify_MembershipBelongingToAnotherUser_IsIndistinguishableFromMissing()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(userId: OtherUserId, isActive: true),
            Session());

        AssertRefused(result, TenancyErrors.MembershipNotFound);
    }

    [Fact]
    public void Verify_InactiveMembership_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(isActive: false),
            Session());

        AssertRefused(result, TenancyErrors.MembershipNotActive);
    }

    [Fact]
    public void Verify_SessionWithoutAnActiveFirm_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(isActive: true),
            Session(omitActiveFirm: true));

        AssertRefused(result, TenancyErrors.MembershipFirmMismatch);
    }

    [Fact]
    public void Verify_MembershipForAnotherFirm_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(firmId: OtherFirmId, isActive: true),
            Session());

        AssertRefused(result, TenancyErrors.MembershipFirmMismatch);
    }

    [Fact]
    public void Verify_MissingIdentifiers_FailClosed()
    {
        var userResult = MembershipVerificationPolicy.Verify(
            User(userId: Guid.Empty, isActive: true),
            Membership(isActive: true),
            Session(userId: Guid.Empty));
        var membershipResult = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(membershipId: Guid.Empty, isActive: true),
            Session());
        var firmResult = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(firmId: Guid.Empty, isActive: true),
            Session(activeFirmId: Guid.Empty));

        AssertRefused(userResult, TenancyErrors.UserNotActive);
        AssertRefused(membershipResult, TenancyErrors.MembershipNotFound);
        AssertRefused(firmResult, TenancyErrors.MembershipFirmMismatch);
    }

    [Fact]
    public void Verify_DoesNotEvaluateSessionLifetimeOrRevocation()
    {
        var session = Session(
            revokedAtUtc: CreatedAtUtc,
            idleExpiresAtUtc: CreatedAtUtc,
            absoluteExpiresAtUtc: CreatedAtUtc);

        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(isActive: true),
            session);

        Assert.True(result.IsVerified);
        Assert.Null(result.RefusalReason);
    }

    [Fact]
    public void Verify_NullSession_FailsClosed()
    {
        var result = MembershipVerificationPolicy.Verify(
            User(isActive: true),
            Membership(isActive: true),
            session: null!);

        AssertRefused(result, TenancyErrors.UserNotActive);
    }

    [Fact]
    public void IdentityModels_PreserveTheirLocalContract()
    {
        var user = User(isActive: true);
        var firm = new Firm(
            FirmId,
            "Example Firm A",
            CreatedAtUtc,
            CreatedAtUtc.AddHours(1));
        var membership = Membership(isActive: true);

        Assert.Equal(UserId, user.UserId);
        Assert.Equal("https://id.example.test", user.Issuer);
        Assert.Equal("subject-alpha", user.Subject);
        Assert.True(user.IsActive);
        Assert.Equal(CreatedAtUtc, user.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc.AddHours(1), user.UpdatedAtUtc);

        Assert.Equal(FirmId, firm.FirmId);
        Assert.Equal("Example Firm A", firm.DisplayName);
        Assert.Equal(CreatedAtUtc, firm.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc.AddHours(1), firm.UpdatedAtUtc);

        Assert.Equal(MembershipId, membership.MembershipId);
        Assert.Equal(FirmId, membership.FirmId);
        Assert.Equal(UserId, membership.UserId);
        Assert.True(membership.IsActive);
        Assert.Equal(CreatedAtUtc, membership.CreatedAtUtc);
        Assert.Equal(CreatedAtUtc.AddHours(1), membership.UpdatedAtUtc);
    }

    [Fact]
    public void IdentityModelToStrings_EmitNoIdentityOrDisplayValue()
    {
        var user = User(isActive: true);
        var firm = new Firm(
            FirmId,
            "Example Firm A",
            CreatedAtUtc,
            CreatedAtUtc.AddHours(1));
        var membership = Membership(isActive: true);

        Assert.Equal(nameof(PortalUser), user.ToString());
        Assert.Equal(nameof(Firm), firm.ToString());
        Assert.Equal(nameof(FirmMembership), membership.ToString());

        var combined = $"{user} {firm} {membership}";
        Assert.DoesNotContain(UserId.ToString("D"), combined, StringComparison.Ordinal);
        Assert.DoesNotContain(FirmId.ToString("D"), combined, StringComparison.Ordinal);
        Assert.DoesNotContain(MembershipId.ToString("D"), combined, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Issuer, combined, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Subject, combined, StringComparison.Ordinal);
        Assert.DoesNotContain(firm.DisplayName, combined, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectSerialization_OmitsIssuerSubjectAndFirmDisplayName()
    {
        var user = User(isActive: true);
        var firm = new Firm(
            FirmId,
            "Example Firm A",
            CreatedAtUtc,
            CreatedAtUtc.AddHours(1));

        var serialized = $"{JsonSerializer.Serialize(user)} {JsonSerializer.Serialize(firm)}";

        Assert.DoesNotContain(user.Issuer, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Subject, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(firm.DisplayName, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionPolicy_DoesNotReferenceTheSessionLifetimePolicy()
    {
        var source = File.ReadAllLines(MembershipPolicySource())
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

        Assert.DoesNotContain(
            source,
            line => line.Contains(nameof(SessionLifetimePolicy), StringComparison.Ordinal));
    }

    private static PortalUser User(Guid? userId = null, bool isActive = true) =>
        new(
            userId ?? UserId,
            "https://id.example.test",
            "subject-alpha",
            isActive,
            CreatedAtUtc,
            CreatedAtUtc.AddHours(1));

    private static FirmMembership Membership(
        Guid? membershipId = null,
        Guid? firmId = null,
        Guid? userId = null,
        bool isActive = true) =>
        new(
            membershipId ?? MembershipId,
            firmId ?? FirmId,
            userId ?? UserId,
            isActive,
            CreatedAtUtc,
            CreatedAtUtc.AddHours(1));

    private static AuthenticationSession Session(
        Guid? sessionId = null,
        Guid? userId = null,
        Guid? activeFirmId = null,
        bool omitActiveFirm = false,
        DateTimeOffset? idleExpiresAtUtc = null,
        DateTimeOffset? absoluteExpiresAtUtc = null,
        DateTimeOffset? revokedAtUtc = null)
    {
        // T09 owns AuthenticationSession's construction behaviour and runs in a
        // parallel worktree. Materialize the Wave 0 contract shape directly so
        // T10's focused tests remain independent of that sibling implementation.
        var session = (AuthenticationSession)Activator.CreateInstance(
            typeof(AuthenticationSession),
            nonPublic: true)!;

        Set(session, nameof(AuthenticationSession.SessionId), sessionId ?? SessionId);
        Set(session, nameof(AuthenticationSession.UserId), userId ?? UserId);
        Set<Guid?>(
            session,
            nameof(AuthenticationSession.ActiveFirmId),
            omitActiveFirm ? null : activeFirmId ?? FirmId);
        Set(session, nameof(AuthenticationSession.CreatedAtUtc), CreatedAtUtc);
        Set(session, nameof(AuthenticationSession.LastSeenAtUtc), CreatedAtUtc);
        Set(
            session,
            nameof(AuthenticationSession.IdleExpiresAtUtc),
            idleExpiresAtUtc ?? CreatedAtUtc.AddMinutes(1));
        Set(
            session,
            nameof(AuthenticationSession.AbsoluteExpiresAtUtc),
            absoluteExpiresAtUtc ?? CreatedAtUtc.AddHours(1));
        Set<DateTimeOffset?>(session, nameof(AuthenticationSession.RevokedAtUtc), revokedAtUtc);

        return session;
    }

    private static void Set<T>(AuthenticationSession session, string propertyName, T value) =>
        typeof(AuthenticationSession)
            .GetProperty(propertyName)!
            .SetValue(session, value);

    private static void AssertRefused(
        MembershipVerification result,
        string expectedReason)
    {
        Assert.False(result.IsVerified);
        Assert.Equal(expectedReason, result.RefusalReason);
        Assert.Equal("MembershipVerification(Refused)", result.ToString());
    }

    private static string MembershipPolicySource() =>
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Tenancy",
            "Services",
            "MembershipVerificationPolicy.cs");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the repository root containing TaxAI.sln.");
    }
}
