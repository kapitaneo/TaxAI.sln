using System.Text.Json;
using System.Text.RegularExpressions;
using TaxLogicEngine.Models;
using Tenancy.Models;
using Tenancy.Services;

namespace Tenancy.Tests.Services;

public sealed class SessionLifetimePolicyTests
{
    private static readonly Guid SessionId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid UserId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid FirmId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void LifetimesAreTheApprovedDurations()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(30),
            SessionLifetimePolicy.IdleTimeout);
        Assert.Equal(
            TimeSpan.FromHours(8),
            SessionLifetimePolicy.AbsoluteLifetime);
    }

    [Fact]
    public void CreateBoundaries_CreatesTheIdleAndAbsoluteBoundaries()
    {
        var createdAtUtc = UtcInstant(8, 0);

        var result = SessionLifetimePolicy.CreateBoundaries(createdAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
        Assert.Equal(
            createdAtUtc.Add(TimeSpan.FromMinutes(30)),
            result.Value.IdleExpiresAtUtc);
        Assert.Equal(
            createdAtUtc.Add(TimeSpan.FromHours(8)),
            result.Value.AbsoluteExpiresAtUtc);
    }

    [Fact]
    public void CreateBoundaries_AcceptsZeroOffsetFromUnspecifiedDateTime()
    {
        var unspecified = new DateTime(
            2033,
            1,
            1,
            8,
            0,
            0,
            DateTimeKind.Unspecified);
        var createdAtUtc = new DateTimeOffset(unspecified, TimeSpan.Zero);

        var result = SessionLifetimePolicy.CreateBoundaries(createdAtUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.Zero, result.Value!.IdleExpiresAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, result.Value.AbsoluteExpiresAtUtc.Offset);
    }

    [Fact]
    public void CreateBoundaries_RejectsANonZeroOffsetWithoutNormalizingIt()
    {
        var createdAt = new DateTimeOffset(
            2033,
            1,
            1,
            8,
            0,
            0,
            TimeSpan.FromHours(1));
        var original = createdAt;

        var result = SessionLifetimePolicy.CreateBoundaries(createdAt);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(
            new[]
            {
                new ValidationError(
                    TenancyFields.CreatedAtUtc,
                    TenancyErrors.InstantNotUtc),
            },
            result.Errors);
        Assert.Equal(original, createdAt);
        Assert.Equal(TimeSpan.FromHours(1), createdAt.Offset);
    }

    [Fact]
    public void Touch_AdvancesIdleAndLeavesAbsoluteUnchanged()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);
        var atUtc = createdAtUtc.Add(TimeSpan.FromMinutes(10));

        var result = SessionLifetimePolicy.Touch(session, atUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            atUtc.Add(TimeSpan.FromMinutes(30)),
            result.Value!.IdleExpiresAtUtc);
        Assert.True(result.Value.IdleExpiresAtUtc > session.IdleExpiresAtUtc);
        Assert.Equal(
            session.AbsoluteExpiresAtUtc,
            result.Value.AbsoluteExpiresAtUtc);
    }

    [Fact]
    public void Touch_ClampsIdleAtTheAbsoluteBoundary()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(
            createdAtUtc,
            idleExpiresAtUtc: createdAtUtc.Add(TimeSpan.FromHours(8)));
        var atUtc = session.AbsoluteExpiresAtUtc.Subtract(TimeSpan.FromMinutes(10));

        var result = SessionLifetimePolicy.Touch(session, atUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            session.AbsoluteExpiresAtUtc,
            result.Value!.IdleExpiresAtUtc);
        Assert.Equal(
            session.AbsoluteExpiresAtUtc,
            result.Value.AbsoluteExpiresAtUtc);
    }

    [Fact]
    public void Touch_AnEarlierConcurrentInstantNeverMovesIdleBackward()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var currentIdleExpiresAtUtc =
            createdAtUtc.Add(TimeSpan.FromMinutes(45));
        var session = ActiveSession(
            createdAtUtc,
            idleExpiresAtUtc: currentIdleExpiresAtUtc);
        var earlierConcurrentInstant =
            createdAtUtc.Add(TimeSpan.FromMinutes(5));

        var result = SessionLifetimePolicy.Touch(
            session,
            earlierConcurrentInstant);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            currentIdleExpiresAtUtc,
            result.Value!.IdleExpiresAtUtc);
        Assert.Equal(
            session.AbsoluteExpiresAtUtc,
            result.Value.AbsoluteExpiresAtUtc);
    }

    [Fact]
    public void Touch_RejectsARevokedSession()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(
            createdAtUtc,
            revokedAtUtc: createdAtUtc.Add(TimeSpan.FromMinutes(5)));

        var result = SessionLifetimePolicy.Touch(
            session,
            createdAtUtc.Add(TimeSpan.FromMinutes(10)));

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        Assert.Equal(
            new[]
            {
                new ValidationError(
                    TenancyFields.SessionId,
                    TenancyErrors.SessionRevoked),
            },
            result.Errors);
    }

    [Fact]
    public void Touch_RejectsASessionAtItsIdleBoundary()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);

        var result = SessionLifetimePolicy.Touch(
            session,
            session.IdleExpiresAtUtc);

        Assert.True(result.IsFailure);
        Assert.Equal(
            TenancyErrors.SessionIdleExpired,
            Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void Touch_RejectsANonZeroOffsetWithoutChangingTheSession()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);
        var originalIdle = session.IdleExpiresAtUtc;
        var originalAbsolute = session.AbsoluteExpiresAtUtc;
        var at = new DateTimeOffset(
            2033,
            1,
            1,
            8,
            10,
            0,
            TimeSpan.FromHours(-1));

        var result = SessionLifetimePolicy.Touch(session, at);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new ValidationError(
                TenancyFields.EvaluationInstant,
                TenancyErrors.InstantNotUtc),
            Assert.Single(result.Errors));
        Assert.Equal(originalIdle, session.IdleExpiresAtUtc);
        Assert.Equal(originalAbsolute, session.AbsoluteExpiresAtUtc);
        Assert.Equal(TimeSpan.FromHours(-1), at.Offset);
    }

    [Fact]
    public void Evaluate_AcceptsAnUnrevokedSessionBeforeBothBoundaries()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);

        var evaluation = SessionLifetimePolicy.Evaluate(
            session,
            createdAtUtc.Add(TimeSpan.FromMinutes(10)));

        Assert.True(evaluation.IsUsable);
        Assert.Null(evaluation.RefusalReason);
    }

    [Fact]
    public void Evaluate_RejectsARevokedSession()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(
            createdAtUtc,
            revokedAtUtc: createdAtUtc.Add(TimeSpan.FromMinutes(5)));

        var evaluation = SessionLifetimePolicy.Evaluate(
            session,
            createdAtUtc.Add(TimeSpan.FromMinutes(10)));

        Assert.False(evaluation.IsUsable);
        Assert.Equal(
            TenancyErrors.SessionRevoked,
            evaluation.RefusalReason);
    }

    [Fact]
    public void Evaluate_RejectsAtTheIdleBoundary()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);

        var evaluation = SessionLifetimePolicy.Evaluate(
            session,
            session.IdleExpiresAtUtc);

        Assert.False(evaluation.IsUsable);
        Assert.Equal(
            TenancyErrors.SessionIdleExpired,
            evaluation.RefusalReason);
    }

    [Fact]
    public void Evaluate_RejectsAfterTheIdleBoundary()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);

        var evaluation = SessionLifetimePolicy.Evaluate(
            session,
            session.IdleExpiresAtUtc.Add(TimeSpan.FromTicks(1)));

        Assert.False(evaluation.IsUsable);
        Assert.Equal(
            TenancyErrors.SessionIdleExpired,
            evaluation.RefusalReason);
    }

    [Fact]
    public void Evaluate_RejectsAtTheAbsoluteBoundary()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var absoluteExpiresAtUtc = createdAtUtc.Add(TimeSpan.FromHours(8));
        var session = ActiveSession(
            createdAtUtc,
            idleExpiresAtUtc: absoluteExpiresAtUtc.Add(TimeSpan.FromMinutes(1)));

        var evaluation = SessionLifetimePolicy.Evaluate(
            session,
            absoluteExpiresAtUtc);

        Assert.False(evaluation.IsUsable);
        Assert.Equal(
            TenancyErrors.SessionAbsoluteExpired,
            evaluation.RefusalReason);
    }

    [Fact]
    public void Evaluate_RejectsAfterTheAbsoluteBoundary()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var absoluteExpiresAtUtc = createdAtUtc.Add(TimeSpan.FromHours(8));
        var session = ActiveSession(
            createdAtUtc,
            idleExpiresAtUtc: absoluteExpiresAtUtc.Add(TimeSpan.FromMinutes(1)));

        var evaluation = SessionLifetimePolicy.Evaluate(
            session,
            absoluteExpiresAtUtc.Add(TimeSpan.FromTicks(1)));

        Assert.False(evaluation.IsUsable);
        Assert.Equal(
            TenancyErrors.SessionAbsoluteExpired,
            evaluation.RefusalReason);
    }

    [Fact]
    public void Evaluate_RejectsANonZeroEvaluationOffset()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);
        var at = new DateTimeOffset(
            2033,
            1,
            1,
            8,
            10,
            0,
            TimeSpan.FromMinutes(30));

        var evaluation = SessionLifetimePolicy.Evaluate(session, at);

        Assert.False(evaluation.IsUsable);
        Assert.Equal(
            TenancyErrors.InstantNotUtc,
            evaluation.RefusalReason);
        Assert.Equal(TimeSpan.FromMinutes(30), at.Offset);
    }

    [Fact]
    public void Evaluate_FailsClosedWhenAStoredInstantHasANonZeroOffset()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(
            createdAtUtc,
            idleExpiresAtUtc: new DateTimeOffset(
                2033,
                1,
                1,
                8,
                30,
                0,
                TimeSpan.FromMinutes(30)));

        var evaluation = SessionLifetimePolicy.Evaluate(
            session,
            createdAtUtc.Add(TimeSpan.FromMinutes(10)));

        Assert.False(evaluation.IsUsable);
        Assert.Equal(
            TenancyErrors.InstantNotUtc,
            evaluation.RefusalReason);
    }

    [Fact]
    public void AuthenticationSession_PreservesTheDeclaredState()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var lastSeenAtUtc = createdAtUtc.Add(TimeSpan.FromMinutes(5));
        var idleExpiresAtUtc = createdAtUtc.Add(TimeSpan.FromMinutes(35));
        var absoluteExpiresAtUtc = createdAtUtc.Add(TimeSpan.FromHours(8));
        var revokedAtUtc = createdAtUtc.Add(TimeSpan.FromMinutes(6));

        var session = new AuthenticationSession(
            SessionId,
            UserId,
            FirmId,
            createdAtUtc,
            lastSeenAtUtc,
            idleExpiresAtUtc,
            absoluteExpiresAtUtc,
            revokedAtUtc);

        Assert.Equal(SessionId, session.SessionId);
        Assert.Equal(UserId, session.UserId);
        Assert.Equal(FirmId, session.ActiveFirmId);
        Assert.Equal(createdAtUtc, session.CreatedAtUtc);
        Assert.Equal(lastSeenAtUtc, session.LastSeenAtUtc);
        Assert.Equal(idleExpiresAtUtc, session.IdleExpiresAtUtc);
        Assert.Equal(absoluteExpiresAtUtc, session.AbsoluteExpiresAtUtc);
        Assert.Equal(revokedAtUtc, session.RevokedAtUtc);
    }

    [Fact]
    public void AuthenticationSession_AllowsNoActiveFirm()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = new AuthenticationSession(
            SessionId,
            UserId,
            activeFirmId: null,
            createdAtUtc,
            createdAtUtc,
            createdAtUtc.Add(TimeSpan.FromMinutes(30)),
            createdAtUtc.Add(TimeSpan.FromHours(8)),
            revokedAtUtc: null);

        Assert.Null(session.ActiveFirmId);
    }

    [Fact]
    public void SessionTypes_RenderOnlyStructuralFacts()
    {
        var createdAtUtc = UtcInstant(8, 0);
        var session = ActiveSession(createdAtUtc);
        var boundaries = new SessionBoundaries(
            session.IdleExpiresAtUtc,
            session.AbsoluteExpiresAtUtc);
        var evaluation = new SessionEvaluation(
            isUsable: false,
            TenancyErrors.SessionRevoked);

        var rendered = new[]
        {
            session.ToString(),
            boundaries.ToString(),
            evaluation.ToString(),
        };

        Assert.All(rendered, value =>
        {
            Assert.DoesNotContain(
                SessionId.ToString(),
                value,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                UserId.ToString(),
                value,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                FirmId.ToString(),
                value,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                TenancyErrors.SessionRevoked,
                value,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                createdAtUtc.ToString("O"),
                value,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AuthenticationSession_DefaultSerializationExposesNoSessionState()
    {
        var session = ActiveSession(UtcInstant(8, 0));

        var json = JsonSerializer.Serialize(session);

        Assert.Equal("{}", json);
    }

    [Fact]
    public void AuthenticationSession_DeclaresNoTicketTokenClaimOrTaxpayerContent()
    {
        var forbidden = new[]
        {
            "Ticket",
            "Token",
            "Claim",
            "Email",
            "DisplayName",
            "Taxpayer",
            "BoxCode",
            "LineCode",
            "Amount",
        };

        var memberNames = typeof(AuthenticationSession)
            .GetMembers()
            .Select(member => member.Name)
            .ToArray();

        Assert.DoesNotContain(
            memberNames,
            name => forbidden.Any(
                token => name.Contains(token, StringComparison.Ordinal)));
    }

    [Fact]
    public void ProductionSource_UsesNoClockTimerOrInclusiveBoundaryConstruction()
    {
        var sources = new[]
        {
            Source("Services", "SessionLifetimePolicy.cs"),
            Source("Models", "AuthenticationSession.cs"),
            Source("Models", "SessionBoundaries.cs"),
            Source("Models", "SessionEvaluation.cs"),
        };
        var executable = string.Join(
            "\n",
            sources.SelectMany(ExecutableLines));
        var fiveDigits = new Regex(
            @"\b\d{5}\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(
            "DateTime.Now",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DateTime.UtcNow",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DateTimeOffset.Now",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DateTimeOffset.UtcNow",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TimeProvider",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Timer",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "23:59:59",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".9999999",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddTicks(-1)",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddMilliseconds(-1)",
            executable,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(fiveDigits, executable);
    }

    private static AuthenticationSession ActiveSession(
        DateTimeOffset createdAtUtc,
        Guid? activeFirmId = default,
        DateTimeOffset? idleExpiresAtUtc = null,
        DateTimeOffset? revokedAtUtc = null) =>
        new(
            SessionId,
            UserId,
            activeFirmId ?? FirmId,
            createdAtUtc,
            createdAtUtc,
            idleExpiresAtUtc ?? createdAtUtc.Add(TimeSpan.FromMinutes(30)),
            createdAtUtc.Add(TimeSpan.FromHours(8)),
            revokedAtUtc);

    private static DateTimeOffset UtcInstant(int hour, int minute) =>
        new(2033, 1, 1, hour, minute, 0, TimeSpan.Zero);

    private static string Source(string folder, string file) =>
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Tenancy",
            folder,
            file);

    private static IEnumerable<string> ExecutableLines(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0 &&
                !line.StartsWith("///", StringComparison.Ordinal) &&
                !line.StartsWith("//", StringComparison.Ordinal) &&
                !line.StartsWith("*", StringComparison.Ordinal) &&
                !line.StartsWith("/*", StringComparison.Ordinal));

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
