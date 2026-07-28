namespace Tenancy.Models;

/// <summary>
/// The two lifetime boundaries of a session, computed from a supplied instant.
/// <para>Wave 0 contract stage: shape declared, behaviour deferred.</para>
/// </summary>
public sealed class SessionBoundaries
{
    internal SessionBoundaries(
        DateTimeOffset idleExpiresAtUtc,
        DateTimeOffset absoluteExpiresAtUtc) => throw new NotImplementedException();

    public DateTimeOffset IdleExpiresAtUtc { get; private set; }

    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }

    /// <summary>Fixed type identity only; no instant is rendered.</summary>
    public override string ToString() => throw new NotImplementedException();
}
