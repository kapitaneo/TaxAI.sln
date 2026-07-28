namespace Tenancy.Models;

/// <summary>
/// The outcome of evaluating a session against a supplied instant.
/// <para>
/// Expiry is <em>evaluated on each request</em>, never swept by a timer or a
/// background service. A session that has passed a boundary is refused whether
/// or not anything ever got round to deleting its row.
/// </para>
/// </summary>
public sealed class SessionEvaluation
{
    internal SessionEvaluation(bool isUsable, string? refusalReason)
    {
        IsUsable = isUsable;
        RefusalReason = refusalReason;
    }

    /// <summary>True only when the session is unrevoked and within both bounds.</summary>
    public bool IsUsable { get; private set; }

    /// <summary>
    /// A fixed constant from <see cref="TenancyErrors"/> when refused; null when
    /// usable. Never an interpolated message.
    /// </summary>
    public string? RefusalReason { get; private set; }

    /// <summary>Fixed structural facts only.</summary>
    public override string ToString() =>
        $"{nameof(SessionEvaluation)} {{ IsUsable = {IsUsable} }}";
}
