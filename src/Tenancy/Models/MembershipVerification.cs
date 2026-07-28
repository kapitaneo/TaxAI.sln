namespace Tenancy.Models;

/// <summary>
/// The outcome of the per-request membership re-verification.
/// <para>
/// Deliberately a separate type from <see cref="SessionEvaluation"/>, produced
/// by a separate policy that cannot call the session policy. A live, unrevoked,
/// in-bounds session whose membership has been disabled must still be refused —
/// two checks that cannot reach each other cannot short-circuit each other.
/// </para>
/// </summary>
public sealed class MembershipVerification
{
    internal MembershipVerification(bool isVerified, string? refusalReason)
    {
        IsVerified = isVerified;
        RefusalReason = refusalReason;
    }

    public bool IsVerified { get; private set; }

    /// <summary>
    /// A fixed constant from <see cref="TenancyErrors"/> when refused; null when
    /// verified. It never names the firm, the user, or the membership.
    /// </summary>
    public string? RefusalReason { get; private set; }

    /// <summary>Fixed structural facts only.</summary>
    public override string ToString() =>
        IsVerified
            ? "MembershipVerification(Verified)"
            : "MembershipVerification(Refused)";
}
