namespace Tenancy.Models;

/// <summary>
/// The two lifetime boundaries of a session, computed from a supplied instant.
/// </summary>
public sealed class SessionBoundaries
{
    internal SessionBoundaries(
        DateTimeOffset idleExpiresAtUtc,
        DateTimeOffset absoluteExpiresAtUtc)
    {
        IdleExpiresAtUtc = idleExpiresAtUtc;
        AbsoluteExpiresAtUtc = absoluteExpiresAtUtc;
    }

    public DateTimeOffset IdleExpiresAtUtc { get; private set; }

    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }

    /// <summary>Fixed type identity only; no instant is rendered.</summary>
    public override string ToString() => nameof(SessionBoundaries);
}
