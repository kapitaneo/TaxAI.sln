using System.Security.Cryptography;
using System.Text;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Tenancy.Services;

/// <summary>
/// Decides whether an evidence manifest authorizes one exact production release.
/// <para>
/// Pure and deterministic: no clock, configuration, filesystem or network is
/// read here. The caller supplies the evaluation instant and the release it
/// claims to be. Every rule fails closed and nothing is defaulted
/// affirmatively — an absent item, an unknown state, a stale observation or an
/// approval for a different binding is a refusal, never a pass.
/// </para>
/// <para>
/// The two provider assurance gaps are evaluated independently of the DEP-05
/// evidence row and of each other, so resolving one can never stand in for the
/// other (AC-168, AC-169).
/// </para>
/// </summary>
public static class ProductionEvidencePolicy
{
    /// <summary>DEP-01 through DEP-24, every one mandatory.</summary>
    public const int RequiredEvidenceCount = 24;

    /// <summary>The mandatory evidence subjects, in DEP order.</summary>
    public static IReadOnlyList<ProductionEvidenceSubject> RequiredEvidence { get; } =
        Enum.GetValues<ProductionEvidenceSubject>().OrderBy(subject => (int)subject).ToArray();

    /// <summary>Both provider assurances, each independently required.</summary>
    public static IReadOnlyList<ProviderAssuranceSubject> RequiredAssurances { get; } =
        Enum.GetValues<ProviderAssuranceSubject>().OrderBy(subject => (int)subject).ToArray();

    /// <summary>Both named approval roles.</summary>
    public static IReadOnlyList<ProductionApprovalRole> RequiredApprovals { get; } =
        Enum.GetValues<ProductionApprovalRole>().OrderBy(role => (int)role).ToArray();

    /// <summary>
    /// Evaluates <paramref name="manifest"/> for <paramref name="expectedRelease"/>
    /// at <paramref name="evaluatedAtUtc"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="evaluatedAtUtc"/> is not a UTC instant.
    /// </exception>
    public static ProductionReadiness Evaluate(
        ProductionEvidenceManifest? manifest,
        ProductionReleaseBinding expectedRelease,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(expectedRelease);

        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The evaluation instant must be UTC.",
                nameof(evaluatedAtUtc));
        }

        if (manifest is null)
        {
            return ProductionReadiness.FromRefusals(
                new[] { ProductionAdmissionRefusal.EvidenceManifestMissing });
        }

        var refusals = new List<ProductionAdmissionRefusal>();

        if (!manifest.Release.Matches(expectedRelease))
        {
            refusals.Add(ProductionAdmissionRefusal.ReleaseBindingMismatch);
        }

        var evidence = manifest.Evidence;
        if (evidence.Any(item => !Enum.IsDefined(item.Subject)) ||
            evidence.GroupBy(item => item.Subject).Any(group => group.Count() > 1))
        {
            refusals.Add(ProductionAdmissionRefusal.EvidenceManifestInvalid);
        }

        if (RequiredEvidence.Any(subject => !evidence.Any(item =>
                item.Subject == subject &&
                IsCurrentApproval(item.Status, item.ObservedAtUtc, item.ExpiresAtUtc, evaluatedAtUtc))))
        {
            refusals.Add(ProductionAdmissionRefusal.MandatoryEvidenceIncomplete);
        }

        var assurances = manifest.Assurances;
        if (assurances.Any(item => !Enum.IsDefined(item.Subject)) ||
            assurances.GroupBy(item => item.Subject).Any(group => group.Count() > 1))
        {
            refusals.Add(ProductionAdmissionRefusal.EvidenceManifestInvalid);
        }

        if (!IsAssuranceResolved(
                assurances,
                ProviderAssuranceSubject.InputCustomerManagedKeyCoverage,
                evaluatedAtUtc))
        {
            refusals.Add(ProductionAdmissionRefusal.ProviderInputKeyAssuranceUnresolved);
        }

        if (!IsAssuranceResolved(
                assurances,
                ProviderAssuranceSubject.RetentionDeletionClock,
                evaluatedAtUtc))
        {
            refusals.Add(ProductionAdmissionRefusal.ProviderRetentionClockAssuranceUnresolved);
        }

        var approvals = manifest.Approvals;
        if (approvals.Any(approval => !Enum.IsDefined(approval.Role)) ||
            approvals.GroupBy(approval => approval.Role).Any(group => group.Count() > 1))
        {
            refusals.Add(ProductionAdmissionRefusal.EvidenceManifestInvalid);
        }

        if (RequiredApprovals.Any(role => !approvals.Any(approval =>
                approval.Role == role &&
                approval.ApprovedAtUtc.Offset == TimeSpan.Zero &&
                approval.ApprovedAtUtc <= evaluatedAtUtc &&
                approval.Release.Matches(expectedRelease))))
        {
            refusals.Add(ProductionAdmissionRefusal.ReleaseApprovalMissing);
        }

        return ProductionReadiness.FromRefusals(refusals);
    }

    /// <summary>
    /// A deterministic SHA-256 digest of a complete configuration set, as
    /// lower-case hexadecimal.
    /// <para>
    /// Keys are compared case-insensitively, as configuration keys are, and are
    /// ordered by their upper-invariant form, so provider order and key casing
    /// do not change the digest while any key or value change does. A key
    /// supplied twice is ambiguous and is refused.
    /// </para>
    /// </summary>
    public static string ComputeConfigurationDigest(
        IEnumerable<KeyValuePair<string, string?>> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var entries = settings
            .Select(entry => (Key: entry.Key?.ToUpperInvariant(), entry.Value))
            .ToArray();

        if (entries.Any(entry => string.IsNullOrWhiteSpace(entry.Key)))
        {
            throw new ArgumentException("A configuration key cannot be blank.", nameof(settings));
        }

        if (entries.GroupBy(entry => entry.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("A configuration key cannot repeat.", nameof(settings));
        }

        var canonical = new StringBuilder();
        foreach (var entry in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            canonical
                .Append(entry.Key!.Length)
                .Append(':')
                .Append(entry.Key)
                .Append('=')
                .Append(entry.Value?.Length ?? -1)
                .Append(':')
                .Append(entry.Value)
                .Append('\n');
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static bool IsAssuranceResolved(
        IReadOnlyList<ProviderAssurance> assurances,
        ProviderAssuranceSubject subject,
        DateTimeOffset evaluatedAtUtc) =>
        assurances.Any(item =>
            item.Subject == subject &&
            IsCurrentApproval(item.Status, item.ObservedAtUtc, item.ExpiresAtUtc, evaluatedAtUtc));

    private static bool IsCurrentApproval(
        ProductionEvidenceStatus status,
        DateTimeOffset observedAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset evaluatedAtUtc) =>
        status == ProductionEvidenceStatus.Approved &&
        observedAtUtc.Offset == TimeSpan.Zero &&
        expiresAtUtc.Offset == TimeSpan.Zero &&
        observedAtUtc <= evaluatedAtUtc &&
        evaluatedAtUtc < expiresAtUtc;
}
