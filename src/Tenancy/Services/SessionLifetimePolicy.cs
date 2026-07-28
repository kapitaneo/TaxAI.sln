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
/// <para>Wave 0 contract stage: behaviour is implemented in a later task.</para>
/// </summary>
public static class SessionLifetimePolicy
{
    public static TimeSpan IdleTimeout { get; } = TimeSpan.FromMinutes(30);

    public static TimeSpan AbsoluteLifetime { get; } = TimeSpan.FromHours(8);

    /// <summary>Both boundaries for a session created at the supplied instant.</summary>
    public static ParseResult<SessionBoundaries> CreateBoundaries(DateTimeOffset createdAtUtc) =>
        throw new NotImplementedException();

    /// <summary>
    /// Advances the idle boundary, clamped so it can never exceed the absolute
    /// boundary, which is returned unchanged.
    /// </summary>
    public static ParseResult<SessionBoundaries> Touch(
        AuthenticationSession session,
        DateTimeOffset atUtc) => throw new NotImplementedException();

    /// <summary>
    /// Refuses a revoked session, and one whose instant has reached either
    /// boundary. Evaluated per request — never swept by a timer.
    /// </summary>
    public static SessionEvaluation Evaluate(
        AuthenticationSession session,
        DateTimeOffset atUtc) => throw new NotImplementedException();
}
