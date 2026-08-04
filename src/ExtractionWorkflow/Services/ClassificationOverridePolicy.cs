using ExtractionWorkflow.Models;

namespace ExtractionWorkflow.Services;

/// <summary>The closed policy for explicit human classification overrides.</summary>
public static class ClassificationOverridePolicy
{
    /// <summary>
    /// Only the two extraction paths are valid targets. Unsupported/"Other" is
    /// an automated fail-closed outcome, never a human-created tax input.
    /// </summary>
    public static bool IsSupportedTarget(DocumentClassification target) =>
        target is DocumentClassification.T4Slip
            or DocumentClassification.MedicalExpenseEvidence;

    public static bool TryCreate(
        Guid overrideId,
        Guid documentId,
        DocumentClassification automated,
        DocumentClassification target,
        Guid actorReference,
        DateTimeOffset overriddenAtUtc,
        out ClassificationOverrideRecord? record)
    {
        record = null;

        if (overrideId == Guid.Empty ||
            documentId == Guid.Empty ||
            actorReference == Guid.Empty ||
            !Enum.IsDefined(automated) ||
            !IsSupportedTarget(target) ||
            !ExtractionWorkflowOptionsGuard.IsUtc(overriddenAtUtc))
        {
            return false;
        }

        record = new ClassificationOverrideRecord(
            overrideId,
            documentId,
            automated,
            target,
            ExtractionActorKind.Accountant,
            actorReference,
            overriddenAtUtc);
        return true;
    }
}
