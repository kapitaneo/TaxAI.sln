using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// Reads over the three identity tables that bootstrap tenancy.
/// <para>
/// Three of these members take no trusted firm, and cannot: the user is keyed by
/// OIDC issuer and subject at sign-in, a live session later revalidates that user
/// by its opaque local identifier, and the active memberships are the input to
/// the decision about which firm — if any — the session may hold. Scoping any of
/// those reads by a firm would presuppose its own answer. All three are named in
/// <see cref="FirmScopeExemptions"/>, whose fourth entry is the session lookup
/// that established the local user identifier.
/// </para>
/// <para>
/// <see cref="FindMembershipAsync"/> is keyed by the authenticated user's own
/// identifier plus an opaque membership selector, so it can only ever return a
/// membership that caller already holds. That is what lets the firm-switch
/// surface accept no firm identifier at all.
/// </para>
/// </summary>
public interface IIdentityDirectory
{
    Task<PortalUser?> FindUserAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reloads the local user named by a live server-side session.
    /// <para>
    /// This is the approved recovery seam for tenancy bootstrap and firm
    /// selection: issuer, subject, and active state come from the local user
    /// record, never from a request value or caller-supplied claim. The user
    /// table is part of the identity carve-out and carries no firm column, so
    /// this lookup cannot require a trusted firm.
    /// </para>
    /// </summary>
    Task<PortalUser?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FirmMembership>> FindActiveMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves one membership belonging to this user. The selector is treated
    /// as an unverified request, never as an authorization boundary.
    /// </summary>
    Task<FirmMembership?> FindMembershipAsync(
        Guid userId,
        Guid membershipId,
        CancellationToken cancellationToken);

    /// <summary>Firms the caller already proved membership of, for display only.</summary>
    Task<IReadOnlyList<Firm>> FindFirmsAsync(
        IReadOnlyList<Guid> firmIds,
        CancellationToken cancellationToken);
}
