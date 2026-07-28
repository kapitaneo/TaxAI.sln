using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// A user's membership of one firm, with an active state a principal can revoke.
/// <para>
/// Disabling a membership takes effect on the departed employee's
/// <strong>next protected request</strong> — not when their cookie eventually
/// expires. That is why the membership is re-verified server-side every time,
/// and why this check is deliberately independent of the session check.
/// </para>
/// <para>
/// One of the three identity tables. It carries a non-nullable firm column and
/// is looked up by user.
/// </para>
/// <para>Wave 0 contract stage: shape declared, behaviour deferred.</para>
/// </summary>
public sealed class FirmMembership
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private FirmMembership()
    {
    }

    internal FirmMembership(
        Guid membershipId,
        Guid firmId,
        Guid userId,
        bool isActive,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc) => throw new NotImplementedException();

    /// <summary>
    /// The opaque selector a member may present when switching firms. It is only
    /// ever resolved against the authenticated user's own memberships, which is
    /// why no firm identifier is ever read from a request.
    /// </summary>
    public Guid MembershipId { get; private set; }

    public Guid FirmId { get; private set; }

    public Guid UserId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Fixed structural facts only; never an identifier.</summary>
    public override string ToString() => throw new NotImplementedException();
}
