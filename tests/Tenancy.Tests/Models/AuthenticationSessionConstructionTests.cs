using System.Reflection;
using TaxLogicEngine.Models;
using Tenancy.Models;
using Tenancy.Services;

namespace Tenancy.Tests.Models;

public sealed class AuthenticationSessionConstructionTests
{
    private static readonly Guid SessionId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");

    private static readonly Guid UserId =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");

    private static readonly Guid FirmId =
        Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3");

    [Fact]
    public void CreateBuildsAnInitiallyUnrevokedSessionWithPolicyBoundaries()
    {
        var createdAtUtc = UtcInstant();

        var result = AuthenticationSession.Create(
            SessionId,
            UserId,
            FirmId,
            createdAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);

        var session = Assert.IsType<AuthenticationSession>(result.Value);
        Assert.Equal(SessionId, session.SessionId);
        Assert.Equal(UserId, session.UserId);
        Assert.Equal(FirmId, session.ActiveFirmId);
        Assert.Equal(createdAtUtc, session.CreatedAtUtc);
        Assert.Equal(createdAtUtc, session.LastSeenAtUtc);
        Assert.Equal(
            createdAtUtc.Add(SessionLifetimePolicy.IdleTimeout),
            session.IdleExpiresAtUtc);
        Assert.Equal(
            createdAtUtc.Add(SessionLifetimePolicy.AbsoluteLifetime),
            session.AbsoluteExpiresAtUtc);
        Assert.True(session.IdleExpiresAtUtc <= session.AbsoluteExpiresAtUtc);
        Assert.Null(session.RevokedAtUtc);
    }

    [Fact]
    public void CreateAllowsNoActiveFirmWithoutChoosingOne()
    {
        var result = AuthenticationSession.Create(
            SessionId,
            UserId,
            activeFirmId: null,
            UtcInstant());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ActiveFirmId);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("user")]
    [InlineData("firm")]
    public void CreateRefusesAnEmptyRequiredOrPresentIdentifier(string identifier)
    {
        var sessionId = identifier == "session" ? Guid.Empty : SessionId;
        var userId = identifier == "user" ? Guid.Empty : UserId;
        Guid? firmId = identifier == "firm" ? Guid.Empty : FirmId;

        var result = AuthenticationSession.Create(
            sessionId,
            userId,
            firmId,
            UtcInstant());

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            identifier switch
            {
                "session" => TenancyFields.SessionId,
                "user" => TenancyFields.UserId,
                _ => TenancyFields.ActiveFirmId,
            },
            error.Field);
        Assert.Equal(TenancyErrors.TokenEmpty, error.Message);
    }

    [Fact]
    public void CreateRefusesANonZeroOffsetWithoutNormalizingIt()
    {
        var nonUtc = new DateTimeOffset(
            2033,
            1,
            1,
            8,
            0,
            0,
            TimeSpan.FromMinutes(30));

        var result = AuthenticationSession.Create(
            SessionId,
            UserId,
            FirmId,
            nonUtc);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        var error = Assert.Single(result.Errors);
        Assert.Equal(TenancyFields.CreatedAtUtc, error.Field);
        Assert.Equal(TenancyErrors.InstantNotUtc, error.Message);
    }

    [Fact]
    public void PublicConstructionCannotSupplyBoundariesOrRevokedState()
    {
        Assert.Empty(typeof(AuthenticationSession).GetConstructors());

        var materializationConstructor = typeof(AuthenticationSession)
            .GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

        Assert.NotNull(materializationConstructor);
        Assert.True(materializationConstructor!.IsPrivate);

        var create = typeof(AuthenticationSession)
            .GetMethod(
                nameof(AuthenticationSession.Create),
                BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(create);
        Assert.Equal(
            new[] { typeof(Guid), typeof(Guid), typeof(Guid?), typeof(DateTimeOffset) },
            create!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            create.GetParameters(),
            parameter =>
                parameter.Name == "idleExpiresAtUtc" ||
                parameter.Name == "absoluteExpiresAtUtc" ||
                parameter.Name == "revokedAtUtc");
    }

    [Fact]
    public void RenderingDisclosesNoIdentifierOrInstant()
    {
        var session = AuthenticationSession.Create(
            SessionId,
            UserId,
            FirmId,
            UtcInstant()).Value!;

        var rendered = session.ToString();

        Assert.Equal(
            $"{nameof(AuthenticationSession)} {{ IsRevoked = False }}",
            rendered);
        Assert.DoesNotContain(SessionId.ToString("D"), rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(UserId.ToString("D"), rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(FirmId.ToString("D"), rendered, StringComparison.Ordinal);
    }

    private static DateTimeOffset UtcInstant() =>
        new(2033, 1, 1, 8, 0, 0, TimeSpan.Zero);
}
