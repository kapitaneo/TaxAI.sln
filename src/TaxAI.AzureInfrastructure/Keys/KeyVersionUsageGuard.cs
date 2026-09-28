using System.Globalization;

namespace TaxAI.AzureInfrastructure.Keys;

/// <summary>
/// The deterministic key-lifecycle decisions of spec 016 FR-04 (AC-044, AC-046,
/// DEP-14): whether a key version may be retired, whether an emergency
/// revocation is authorized, and whether a rotation policy meets the 90-day
/// target.
/// <para>
/// <b>Retirement is refused unless it is proved safe.</b> A version may be
/// disabled or destroyed only when it is not the active version and complete,
/// current usage evidence from <i>every</i> source — retained ciphertext, the
/// Data Protection key ring and recoverable approved backups — shows that
/// nothing still needs it. Missing, incomplete, stale, future-dated or
/// malformed evidence is a refusal, never an empty inventory. Retirement is not
/// proof of source purge; disabling access and destroying ciphertext are
/// different events.
/// </para>
/// <para>
/// <b>Emergency revocation is privileged.</b> It requires a just-in-time
/// elevation (never standing access), a closed justification with a reference,
/// a separate reviewer, and a bounded window that contains the decision instant.
/// </para>
/// <para>
/// The guard performs no I/O and reads no clock: every instant is supplied.
/// Decisions accumulate closed refusal codes and quote no identifier.
/// </para>
/// </summary>
public sealed class KeyVersionUsageGuard
{
    /// <summary>The approved active-key rotation target (FR-04).</summary>
    public static readonly TimeSpan RotationTarget = TimeSpan.FromDays(90);

    /// <summary>
    /// [ASSUMED] Usage evidence older than this is stale and cannot authorize a
    /// retirement; the drill regenerates the inventory immediately beforehand.
    /// </summary>
    public static readonly TimeSpan EvidenceMaximumAge = TimeSpan.FromHours(24);

    /// <summary>
    /// [ASSUMED] The longest approved emergency-elevation window, matching a
    /// single just-in-time activation.
    /// </summary>
    public static readonly TimeSpan EmergencyWindowMaximum = TimeSpan.FromHours(8);

    /// <summary>The three usage sources whose evidence retirement requires.</summary>
    public static readonly IReadOnlyList<KeyUsageSource> RequiredSources = new[]
    {
        KeyUsageSource.RetainedCiphertext,
        KeyUsageSource.DataProtectionKeyRing,
        KeyUsageSource.RecoverableBackup,
    };

    /// <summary>Decides whether one key version may be disabled or destroyed.</summary>
    public KeyGuardDecision EvaluateRetirement(KeyRetirementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var refusals = new List<KeyGuardRefusal>();

        if (!TryReadVersion(request.KeyVersionId, out var candidateKey) ||
            !TryReadVersion(request.ActiveVersionId, out var activeKey) ||
            !string.Equals(candidateKey.Key, activeKey.Key, StringComparison.OrdinalIgnoreCase))
        {
            refusals.Add(KeyGuardRefusal.KeyVersionMalformed);
            return new KeyGuardDecision(refusals);
        }

        if (string.Equals(candidateKey.Version, activeKey.Version, StringComparison.OrdinalIgnoreCase))
        {
            refusals.Add(KeyGuardRefusal.ActiveVersion);
        }

        foreach (var source in RequiredSources)
        {
            var evidence = request.Evidence.Where(item => item.Source == source).ToArray();
            if (evidence.Length != 1)
            {
                refusals.Add(KeyGuardRefusal.UsageEvidenceIncomplete);
                continue;
            }

            var item = evidence[0];
            if (!item.IsComplete)
            {
                refusals.Add(KeyGuardRefusal.UsageEvidenceIncomplete);
            }

            if (item.ObservedAtUtc > request.EvaluatedAtUtc ||
                request.EvaluatedAtUtc - item.ObservedAtUtc > EvidenceMaximumAge)
            {
                refusals.Add(KeyGuardRefusal.UsageEvidenceStale);
            }

            foreach (var reference in item.ReferencedKeyVersionIds)
            {
                if (!TryReadVersion(reference, out var referenced))
                {
                    refusals.Add(KeyGuardRefusal.UsageEvidenceIncomplete);
                }
                else if (referenced == candidateKey)
                {
                    refusals.Add(KeyGuardRefusal.VersionStillRequired);
                }
            }
        }

        if (request.Evidence.Any(item => !Enum.IsDefined(item.Source)))
        {
            refusals.Add(KeyGuardRefusal.UsageEvidenceIncomplete);
        }

        return new KeyGuardDecision(refusals);
    }

    /// <summary>Decides whether an emergency revocation is authorized now.</summary>
    public KeyGuardDecision EvaluateEmergencyRevocation(EmergencyRevocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var refusals = new List<KeyGuardRefusal>();

        if (request.Elevation != PrivilegedElevation.JustInTime)
        {
            refusals.Add(KeyGuardRefusal.ElevationNotJustInTime);
        }

        if (!Enum.IsDefined(request.Justification) ||
            request.Justification == RevocationJustification.None ||
            string.IsNullOrWhiteSpace(request.JustificationReference))
        {
            refusals.Add(KeyGuardRefusal.JustificationMissing);
        }

        if (request.RequesterId == Guid.Empty ||
            request.ReviewerId == Guid.Empty ||
            request.RequesterId == request.ReviewerId)
        {
            refusals.Add(KeyGuardRefusal.IndependentReviewMissing);
        }

        var window = request.WindowEndsAtUtc - request.WindowStartsAtUtc;
        if (window <= TimeSpan.Zero || window > EmergencyWindowMaximum)
        {
            refusals.Add(KeyGuardRefusal.ElevationWindowNotBounded);
        }
        else if (request.EvaluatedAtUtc < request.WindowStartsAtUtc ||
                 request.EvaluatedAtUtc >= request.WindowEndsAtUtc)
        {
            refusals.Add(KeyGuardRefusal.OutsideElevationWindow);
        }

        return new KeyGuardDecision(refusals);
    }

    /// <summary>
    /// True only when a rotation policy rotates exactly at the 90-day target
    /// after creation, written as the ISO 8601 duration Key Vault reports.
    /// </summary>
    public static bool MeetsRotationTarget(string? rotateAfterCreate)
    {
        if (rotateAfterCreate is null ||
            rotateAfterCreate.Length < 3 ||
            !rotateAfterCreate.StartsWith('P') ||
            !rotateAfterCreate.EndsWith('D') ||
            !int.TryParse(
                rotateAfterCreate.AsSpan(1, rotateAfterCreate.Length - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var days))
        {
            return false;
        }

        return TimeSpan.FromDays(days) == RotationTarget;
    }

    public override string ToString() => nameof(KeyVersionUsageGuard);

    private static bool TryReadVersion(string? candidate, out VersionReference reference)
    {
        reference = default;
        if (candidate is null ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !KeyIdentifier.TryReadVersioned(uri, out var host, out var name, out var version))
        {
            return false;
        }

        reference = new VersionReference(
            $"{host!.ToLowerInvariant()}/{name}",
            version!.ToLowerInvariant());
        return true;
    }

    private readonly record struct VersionReference(string Key, string Version);
}

/// <summary>Where evidence that a key version is still needed comes from.</summary>
public enum KeyUsageSource
{
    RetainedCiphertext = 1,
    DataProtectionKeyRing = 2,
    RecoverableBackup = 3,
}

/// <summary>How an emergency operator obtained privilege.</summary>
public enum PrivilegedElevation
{
    None = 0,
    Standing = 1,
    JustInTime = 2,
}

/// <summary>The closed reasons an emergency revocation may be requested for.</summary>
public enum RevocationJustification
{
    None = 0,
    SuspectedKeyCompromise = 1,
    SuspectedIdentityCompromise = 2,
    ProviderSecurityIncident = 3,
}

/// <summary>The closed reasons a key decision is refused.</summary>
public enum KeyGuardRefusal
{
    KeyVersionMalformed = 1,
    ActiveVersion = 2,
    VersionStillRequired = 3,
    UsageEvidenceIncomplete = 4,
    UsageEvidenceStale = 5,
    ElevationNotJustInTime = 6,
    JustificationMissing = 7,
    IndependentReviewMissing = 8,
    ElevationWindowNotBounded = 9,
    OutsideElevationWindow = 10,
}

/// <summary>
/// One usage inventory from one source: whether it is complete, when it was
/// observed, and every versioned key identifier it found in use.
/// </summary>
public sealed class KeyUsageEvidence
{
    public KeyUsageEvidence(
        KeyUsageSource source,
        bool isComplete,
        DateTimeOffset observedAtUtc,
        IEnumerable<string> referencedKeyVersionIds)
    {
        ArgumentNullException.ThrowIfNull(referencedKeyVersionIds);

        Source = source;
        IsComplete = isComplete;
        ObservedAtUtc = observedAtUtc;
        ReferencedKeyVersionIds = referencedKeyVersionIds.ToArray();
    }

    public KeyUsageSource Source { get; }

    public bool IsComplete { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public IReadOnlyList<string> ReferencedKeyVersionIds { get; }

    public override string ToString() => nameof(KeyUsageEvidence);
}

/// <summary>A request to disable or destroy one key version.</summary>
public sealed class KeyRetirementRequest
{
    public KeyRetirementRequest(
        string keyVersionId,
        string activeVersionId,
        IEnumerable<KeyUsageEvidence> evidence,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        KeyVersionId = keyVersionId;
        ActiveVersionId = activeVersionId;
        Evidence = evidence.ToArray();
        EvaluatedAtUtc = evaluatedAtUtc;
    }

    public string KeyVersionId { get; }

    public string ActiveVersionId { get; }

    public IReadOnlyList<KeyUsageEvidence> Evidence { get; }

    public DateTimeOffset EvaluatedAtUtc { get; }

    public override string ToString() => nameof(KeyRetirementRequest);
}

/// <summary>A request to revoke key access in an incident.</summary>
public sealed class EmergencyRevocationRequest
{
    public EmergencyRevocationRequest(
        PrivilegedElevation elevation,
        RevocationJustification justification,
        string? justificationReference,
        Guid requesterId,
        Guid reviewerId,
        DateTimeOffset windowStartsAtUtc,
        DateTimeOffset windowEndsAtUtc,
        DateTimeOffset evaluatedAtUtc)
    {
        Elevation = elevation;
        Justification = justification;
        JustificationReference = justificationReference;
        RequesterId = requesterId;
        ReviewerId = reviewerId;
        WindowStartsAtUtc = windowStartsAtUtc;
        WindowEndsAtUtc = windowEndsAtUtc;
        EvaluatedAtUtc = evaluatedAtUtc;
    }

    public PrivilegedElevation Elevation { get; }

    public RevocationJustification Justification { get; }

    public string? JustificationReference { get; }

    public Guid RequesterId { get; }

    public Guid ReviewerId { get; }

    public DateTimeOffset WindowStartsAtUtc { get; }

    public DateTimeOffset WindowEndsAtUtc { get; }

    public DateTimeOffset EvaluatedAtUtc { get; }

    public override string ToString() => nameof(EmergencyRevocationRequest);
}

/// <summary>
/// A key decision: permitted only when no refusal applies. There is no
/// "permitted" code that can be supplied.
/// </summary>
public sealed class KeyGuardDecision
{
    internal KeyGuardDecision(IEnumerable<KeyGuardRefusal> refusals)
    {
        Refusals = refusals.Distinct().OrderBy(refusal => refusal).ToArray();
    }

    public bool IsPermitted => Refusals.Count == 0;

    public IReadOnlyList<KeyGuardRefusal> Refusals { get; }

    public override string ToString() => nameof(KeyGuardDecision);
}
