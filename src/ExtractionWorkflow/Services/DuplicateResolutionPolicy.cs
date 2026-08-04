using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Services;

/// <summary>
/// Validates one decision against append-only duplicate history. A decision
/// records the accountant's conclusion; neither conclusion removes a document.
/// </summary>
public static class DuplicateResolutionPolicy
{
    public static bool TryCreate(
        Guid resolutionId,
        DuplicateCandidateRecord candidate,
        IReadOnlyList<DuplicateResolutionRecord> existing,
        DuplicateResolutionKind resolution,
        Guid actorReference,
        DateTimeOffset resolvedAtUtc,
        out DuplicateResolutionRecord? record)
    {
        record = null;

        if (resolutionId == Guid.Empty ||
            candidate is null ||
            existing is null ||
            actorReference == Guid.Empty ||
            !Enum.IsDefined(resolution) ||
            !ExtractionWorkflowOptionsGuard.IsUtc(resolvedAtUtc))
        {
            return false;
        }

        foreach (var prior in existing)
        {
            if (prior is not null && prior.CandidateId == candidate.CandidateId)
            {
                return false;
            }
        }

        record = new DuplicateResolutionRecord(
            resolutionId,
            candidate.CandidateId,
            resolution,
            ExtractionActorKind.Accountant,
            actorReference,
            resolvedAtUtc);
        return true;
    }
}
