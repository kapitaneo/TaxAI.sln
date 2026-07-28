using TaxLogicEngine.Models;
using Tenancy.Models;

namespace Tenancy.Services;

/// <summary>
/// The server-side session lifetime rules.
/// <para>
/// Both boundaries are enforced from the session record, not from the cookie's
/// own expiry — a cookie's expiry is a value the client holds, and a client that
/// keeps presenting an expired one must still be refused.
/// </para>
/// <para>
/// Activity extends the idle boundary and <strong>never</strong> the absolute
/// one. Sliding expiration that can push out the absolute maximum is not a
/// maximum at all: a continuously active session would never require
/// reauthentication.
/// </para>
/// <para>
/// Durations are expressed as <see cref="TimeSpan"/> factory calls rather than
/// second counts. A five-digit second count would also match the repository's
/// CRA-line-number scan, and a duration indistinguishable from a hardcoded CRA
/// line is a duration that defeats the scan.
/// </para>
/// </summary>
public static class SessionLifetimePolicy
{
    public static TimeSpan IdleTimeout { get; } = TimeSpan.FromMinutes(30);

    public static TimeSpan AbsoluteLifetime { get; } = TimeSpan.FromHours(8);

    /// <summary>Both boundaries for a session created at the supplied instant.</summary>
    public static ParseResult<SessionBoundaries> CreateBoundaries(DateTimeOffset createdAtUtc)
    {
        var utcError = UtcInstantGuard.Validate(createdAtUtc, TenancyFields.CreatedAtUtc);
        if (utcError is not null)
        {
            return ParseResult<SessionBoundaries>.Failure(utcError);
        }

        return ParseResult<SessionBoundaries>.Success(
            new SessionBoundaries(
                createdAtUtc.Add(IdleTimeout),
                createdAtUtc.Add(AbsoluteLifetime)));
    }

    /// <summary>
    /// Advances the idle boundary, clamped so it can never exceed the absolute
    /// boundary, which is returned unchanged.
    /// </summary>
    public static ParseResult<SessionBoundaries> Touch(
        AuthenticationSession session,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(session);

        var utcError = UtcInstantGuard.Validate(atUtc, TenancyFields.EvaluationInstant);
        if (utcError is not null)
        {
            return ParseResult<SessionBoundaries>.Failure(utcError);
        }

        var evaluation = Evaluate(session, atUtc);
        if (!evaluation.IsUsable)
        {
            return ParseResult<SessionBoundaries>.Failure(
                new ValidationError(
                    TenancyFields.SessionId,
                    evaluation.RefusalReason!));
        }

        var remainingLifetime = session.AbsoluteExpiresAtUtc - atUtc;
        var idleExpiresAtUtc = remainingLifetime <= IdleTimeout
            ? session.AbsoluteExpiresAtUtc
            : atUtc.Add(IdleTimeout);

        if (idleExpiresAtUtc < session.IdleExpiresAtUtc)
        {
            idleExpiresAtUtc = session.IdleExpiresAtUtc;
        }

        if (idleExpiresAtUtc > session.AbsoluteExpiresAtUtc)
        {
            idleExpiresAtUtc = session.AbsoluteExpiresAtUtc;
        }

        return ParseResult<SessionBoundaries>.Success(
            new SessionBoundaries(
                idleExpiresAtUtc,
                session.AbsoluteExpiresAtUtc));
    }

    /// <summary>
    /// Refuses a revoked session, and one whose instant has reached either
    /// boundary. Evaluated per request — never swept by a timer.
    /// </summary>
    public static SessionEvaluation Evaluate(
        AuthenticationSession session,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (UtcInstantGuard.Validate(atUtc, TenancyFields.EvaluationInstant) is not null ||
            HasNonZeroOffset(session))
        {
            return new SessionEvaluation(
                isUsable: false,
                TenancyErrors.InstantNotUtc);
        }

        if (session.RevokedAtUtc.HasValue)
        {
            return new SessionEvaluation(
                isUsable: false,
                TenancyErrors.SessionRevoked);
        }

        if (atUtc >= session.IdleExpiresAtUtc)
        {
            return new SessionEvaluation(
                isUsable: false,
                TenancyErrors.SessionIdleExpired);
        }

        if (atUtc >= session.AbsoluteExpiresAtUtc)
        {
            return new SessionEvaluation(
                isUsable: false,
                TenancyErrors.SessionAbsoluteExpired);
        }

        return new SessionEvaluation(
            isUsable: true,
            refusalReason: null);
    }

    private static bool HasNonZeroOffset(AuthenticationSession session) =>
        session.CreatedAtUtc.Offset != TimeSpan.Zero ||
        session.LastSeenAtUtc.Offset != TimeSpan.Zero ||
        session.IdleExpiresAtUtc.Offset != TimeSpan.Zero ||
        session.AbsoluteExpiresAtUtc.Offset != TimeSpan.Zero ||
        session.RevokedAtUtc is { Offset: var revokedOffset } &&
        revokedOffset != TimeSpan.Zero;
}
