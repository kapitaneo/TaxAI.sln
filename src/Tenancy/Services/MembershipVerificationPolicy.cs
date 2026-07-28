using Tenancy.Models;

namespace Tenancy.Services;

/// <summary>
/// The per-request membership re-verification.
/// <para>
/// Every protected taxpayer-data request re-checks, server-side, that the local
/// user is active, that the selected membership still exists, that it is active,
/// and that its firm matches the session's active firm. A firm principal who
/// disables a departed employee's membership expects the next request to be
/// refused — not the next request after an eight-hour session happens to expire.
/// </para>
/// <para>
/// This type deliberately does not call, reference, or know about the session
/// policy. The two checks are independent, so neither can short-circuit the
/// other: a live, unrevoked, in-bounds session whose membership was disabled is
/// still refused.
/// </para>
/// <para>Wave 0 contract stage: behaviour is implemented in a later task.</para>
/// </summary>
public static class MembershipVerificationPolicy
{
    /// <summary>
    /// Fails closed on every missing, inactive, or mismatched input. A null user
    /// or a null membership is a refusal, never a pass.
    /// </summary>
    public static MembershipVerification Verify(
        PortalUser? user,
        FirmMembership? membership,
        AuthenticationSession session) => throw new NotImplementedException();
}
