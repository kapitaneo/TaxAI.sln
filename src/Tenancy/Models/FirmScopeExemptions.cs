namespace Tenancy.Models;

/// <summary>
/// The closed list of operations permitted to look something up without a
/// trusted firm identifier.
/// <para>
/// Every operation that touches a tenant-owned review record — save, read,
/// purge — takes a trusted <c>FirmId</c> as a required parameter, with no
/// overload, optional argument, default, or alternate code path that omits it.
/// The four entries below are the bootstrap of tenancy itself: each one runs
/// <em>before</em> a trusted firm exists, and none can be scoped by one without
/// circularity.
/// </para>
/// <para>
/// They operate only over the three-table identity carve-out in the schema —
/// the user, firm-membership, and session tables. There are two approved user
/// reads because sign-in starts from issuer plus subject while later requests
/// start from the opaque local user identifier held by the server-side session.
/// A fifth entry would mean something outside the approved identity bootstrap
/// had escaped the firm scope, so the list is asserted by name and by count.
/// </para>
/// </summary>
public static class FirmScopeExemptions
{
    /// <summary>
    /// Establishes the trusted firm scope, and therefore cannot require one as
    /// input. It <em>returns</em> the session's active firm rather than
    /// accepting one.
    /// </summary>
    public const string SessionLookupBySessionId =
        "IAuthenticationSessionStore.FindAsync(Guid, CancellationToken)";

    /// <summary>
    /// The local user is keyed by OIDC issuer and subject. At sign-in no firm is
    /// known yet — resolving the user is how one becomes knowable.
    /// </summary>
    public const string UserLookupByIssuerAndSubject =
        "IIdentityDirectory.FindUserAsync(String, String, CancellationToken)";

    /// <summary>
    /// A live session carries the opaque local user identifier but deliberately
    /// stores no issuer or subject. Reloading the carve-out user row is how each
    /// request revalidates active state and how firm selection recovers the
    /// locally stored issuer and subject for the existing sign-in flow.
    /// </summary>
    public const string UserLookupByUserId =
        "IIdentityDirectory.FindUserByIdAsync(Guid, CancellationToken)";

    /// <summary>
    /// The active memberships are the input to the decision about which firm, if
    /// any, the session may hold. Scoping this by a firm would presuppose its
    /// own answer.
    /// </summary>
    public const string ActiveMembershipLookupByUserId =
        "IIdentityDirectory.FindActiveMembershipsAsync(Guid, CancellationToken)";

    /// <summary>Exactly four approved identity-bootstrap entries. Never more.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        SessionLookupBySessionId,
        UserLookupByIssuerAndSubject,
        UserLookupByUserId,
        ActiveMembershipLookupByUserId,
    });
}
