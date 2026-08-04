using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Services;

/// <summary>
/// Derives current state from append-only history.
/// <para>
/// <b>Nothing here is stored.</b> There is no writable status, no mutable
/// acceptance column, and no "latest" marker anyone could overwrite: whether a
/// document is accepted, which version supersedes which, which classification is
/// effective, and whether a duplicate is still outstanding are all computed from
/// the facts each time they are asked for. An immutable history whose summary is
/// a mutable field is not an immutable history.
/// </para>
/// <para>
/// Every input arrives from a firm-scoped, case-scoped store read the caller
/// already performed. This class performs no lookup of its own, so it cannot see
/// across a firm, a case, or a participant.
/// </para>
/// </summary>
public static class ExtractionProjection
{
    /// <summary>
    /// The version number the next appended extraction takes: one past the
    /// highest already recorded, and one when none is. A version number is a
    /// position in a chain, never a count of attempts.
    /// </summary>
    public static int NextExtractionVersion(StoredDocumentExtraction? stored) =>
        Highest(stored?.Versions, version => version.Version) + 1;

    /// <summary>The same, for the separate medical evidence chain.</summary>
    public static int NextMedicalEvidenceVersion(StoredDocumentExtraction? stored) =>
        Highest(stored?.MedicalEvidenceVersions, version => version.Version) + 1;

    /// <summary>
    /// The identifier of the version a newly appended one follows, or null when
    /// it is the first.
    /// </summary>
    public static Guid? PrecedingExtractionVersionId(StoredDocumentExtraction? stored) =>
        LatestExtractionVersion(stored)?.ExtractionVersionId;

    /// <summary>The same, for the medical chain.</summary>
    public static Guid? PrecedingMedicalEvidenceVersionId(StoredDocumentExtraction? stored) =>
        LatestMedicalEvidenceVersion(stored)?.MedicalEvidenceVersionId;

    /// <summary>The highest-numbered extraction version, or null when none exists.</summary>
    public static ExtractionVersion? LatestExtractionVersion(StoredDocumentExtraction? stored)
    {
        ExtractionVersion? latest = null;

        foreach (var version in stored?.Versions ?? Array.Empty<ExtractionVersion>())
        {
            if (latest is null || version.Version > latest.Version)
            {
                latest = version;
            }
        }

        return latest;
    }

    /// <summary>The highest-numbered medical evidence version, or null when none exists.</summary>
    public static MedicalEvidenceVersion? LatestMedicalEvidenceVersion(
        StoredDocumentExtraction? stored)
    {
        MedicalEvidenceVersion? latest = null;

        foreach (var version in stored?.MedicalEvidenceVersions
                 ?? Array.Empty<MedicalEvidenceVersion>())
        {
            if (latest is null || version.Version > latest.Version)
            {
                latest = version;
            }
        }

        return latest;
    }

    /// <summary>
    /// The classification an extraction of this document must follow.
    /// <para>
    /// The automated value controls unless a human explicitly overrode it, which
    /// is what makes an unrecognized document produce nothing rather than
    /// whatever a checklist slot claimed. The automated value is retained beside
    /// the override, so the two can be shown together and neither silently wins.
    /// </para>
    /// <para>
    /// A stored override that no longer names a supported path is ignored rather
    /// than obeyed: <c>TryOverride</c> refuses it and the automated value stands.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="automated"/> is null.</exception>
    public static ClassificationOutcome Effective(
        ClassificationOutcome automated,
        StoredDocumentExtraction? stored)
    {
        ArgumentNullException.ThrowIfNull(automated);

        var latest = LatestOverride(stored);

        if (latest is null)
        {
            return automated;
        }

        return automated.TryOverride(
            latest.OverriddenClassification,
            latest.ActorReference,
            latest.OverriddenAtUtc,
            out var overridden)
            ? overridden!
            : automated;
    }

    /// <summary>
    /// Projects one document's current state. A document with no version yet is
    /// described as unsupported with nothing recorded rather than refused: "no
    /// extraction has run" is a state a reader is entitled to see.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="stored"/> is null.</exception>
    public static DocumentExtractionView Document(StoredDocumentExtraction stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var latest = LatestExtractionVersion(stored);
        var latestMedical = LatestMedicalEvidenceVersion(stored);

        var classification = latest?.Classification;
        var automated = classification?.AutomatedClassification
            ?? DocumentClassification.Unsupported;
        var effective = classification?.EffectiveClassification
            ?? DocumentClassification.Unsupported;
        var isOverridden = classification?.IsOverridden ?? false;

        // A stored override that arrived after the latest version still governs:
        // it is a fact about the document, not about one version of it. The most
        // recent one wins, selected by its own instant rather than by the order
        // a caller happened to hand the collection over in.
        if (LatestOverride(stored) is { } record)
        {
            isOverridden = true;
            effective = record.OverriddenClassification;
        }

        var outcomes = latest is not null
            ? latest.FieldOutcomes
            : latestMedical?.FieldOutcomes ?? Array.Empty<FieldValidationOutcome>();

        return new DocumentExtractionView(
            stored.DocumentId,
            automated,
            effective,
            isOverridden,
            stored.Versions.Count,
            stored.MedicalEvidenceVersions.Count,
            latest?.Version ?? latestMedical?.Version,
            outcomes,
            AcceptanceState(stored),
            stored.Corrections.Count,
            HasUnresolvedDuplicateCandidate(stored));
    }

    /// <summary>
    /// Whether a human accepted the document's latest version.
    /// <para>
    /// An acceptance names the exact version it accepted, so a later version
    /// supersedes it by existing: correcting an accepted document returns it to
    /// awaiting a decision rather than inheriting the earlier approval.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="stored"/> is null.</exception>
    public static ExtractionAcceptanceState AcceptanceState(StoredDocumentExtraction stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var latestId = LatestExtractionVersion(stored)?.ExtractionVersionId
            ?? LatestMedicalEvidenceVersion(stored)?.MedicalEvidenceVersionId;

        if (latestId is null)
        {
            return ExtractionAcceptanceState.AwaitingAccountantDecision;
        }

        foreach (var acceptance in stored.Acceptances)
        {
            if (acceptance.Scope == ExtractionAcceptanceScope.Document &&
                acceptance.AcceptedVersionId == latestId)
            {
                return ExtractionAcceptanceState.AcceptedByAccountant;
            }
        }

        return ExtractionAcceptanceState.AwaitingAccountantDecision;
    }

    /// <summary>
    /// Whether a flagged probable duplicate is still awaiting an accountant
    /// decision. A flag is never a rejection: both documents stay present and
    /// nothing is discarded, merged, or excluded while one is outstanding.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="stored"/> is null.</exception>
    public static bool HasUnresolvedDuplicateCandidate(StoredDocumentExtraction stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        foreach (var candidate in stored.DuplicateCandidates)
        {
            var resolved = false;

            foreach (var resolution in stored.DuplicateResolutions)
            {
                if (resolution.CandidateId == candidate.CandidateId)
                {
                    resolved = true;
                }
            }

            if (!resolved)
            {
                return true;
            }
        }

        return false;
    }

    private static ClassificationOverrideRecord? LatestOverride(StoredDocumentExtraction? stored)
    {
        ClassificationOverrideRecord? latest = null;

        foreach (var record in stored?.ClassificationOverrides
                 ?? Array.Empty<ClassificationOverrideRecord>())
        {
            if (latest is null || record.OverriddenAtUtc > latest.OverriddenAtUtc)
            {
                latest = record;
            }
        }

        return latest;
    }

    private static int Highest<TVersion>(
        IReadOnlyList<TVersion>? versions,
        Func<TVersion, int> number)
    {
        var highest = 0;

        foreach (var version in versions ?? Array.Empty<TVersion>())
        {
            var candidate = number(version);

            if (candidate > highest)
            {
                highest = candidate;
            }
        }

        return highest;
    }
}
