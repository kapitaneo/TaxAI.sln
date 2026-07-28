using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// The server-side session of record.
/// <para>
/// Revocation is a fact in this table, not a cookie that was cleared. Clearing a
/// browser cookie leaves a copied pre-logout cookie replayable; a revoked row is
/// refused by every application instance on the very next request.
/// </para>
/// <para>
/// It carries <strong>no OIDC access, refresh, or id token</strong>, no
/// serialized authentication ticket, no claims payload, no email, no display
/// name, no free text, and no taxpayer value. The browser cookie holds only a
/// protected opaque key into this row, and the principal — including the firm
/// claim — is rebuilt from these columns on every request, so the claim and the
/// record cannot drift apart.
/// </para>
/// <para>
/// One of the three identity tables. Its firm is <strong>nullable</strong> on
/// purpose: a signed-in principal with zero or several active memberships
/// legitimately has no active firm, and the application never picks one on the
/// user's behalf.
/// </para>
/// </summary>
public sealed class AuthenticationSession
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private AuthenticationSession()
    {
    }

    internal AuthenticationSession(
        Guid sessionId,
        Guid userId,
        Guid? activeFirmId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastSeenAtUtc,
        DateTimeOffset idleExpiresAtUtc,
        DateTimeOffset absoluteExpiresAtUtc,
        DateTimeOffset? revokedAtUtc)
    {
        SessionId = sessionId;
        UserId = userId;
        ActiveFirmId = activeFirmId;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = lastSeenAtUtc;
        IdleExpiresAtUtc = idleExpiresAtUtc;
        AbsoluteExpiresAtUtc = absoluteExpiresAtUtc;
        RevokedAtUtc = revokedAtUtc;
    }

    /// <summary>The opaque key the protected cookie carries, and nothing more.</summary>
    [JsonIgnore]
    public Guid SessionId { get; private set; }

    [JsonIgnore]
    public Guid UserId { get; private set; }

    /// <summary>Null when the user holds zero or several active memberships.</summary>
    [JsonIgnore]
    public Guid? ActiveFirmId { get; private set; }

    [JsonIgnore]
    public DateTimeOffset CreatedAtUtc { get; private set; }

    [JsonIgnore]
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    /// <summary>Advanced by activity, and never beyond the absolute boundary.</summary>
    [JsonIgnore]
    public DateTimeOffset IdleExpiresAtUtc { get; private set; }

    /// <summary>
    /// Fixed at creation and <strong>never</strong> extended. Sliding expiration
    /// moves the idle boundary only; reauthentication is required once this
    /// instant is reached.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }

    /// <summary>Non-null once revoked. The security-bearing fact of sign-out.</summary>
    [JsonIgnore]
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>
    /// Fixed structural facts only; never the session identifier, the user
    /// identifier, or the firm.
    /// </summary>
    public override string ToString() =>
        $"{nameof(AuthenticationSession)} {{ IsRevoked = {RevokedAtUtc.HasValue} }}";
}
