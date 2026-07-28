using Tenancy.Models;

namespace Tenancy.Contracts;

/// <summary>
/// Reads over the three identity tables that bootstrap tenancy.
/// <para>
/// Two of these members take no trusted firm, and cannot: the user is keyed by
/// OIDC issuer and subject, and the active memberships are the input to the
/// decision about which firm — if any — the session may hold. Scoping either by
/// a firm would presuppose its own answer. Both are named in
/// <see cref="FirmScopeExemptions"/>, which is exactly three entries long.
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
