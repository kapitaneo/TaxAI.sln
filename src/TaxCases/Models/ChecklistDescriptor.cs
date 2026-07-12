namespace TaxCases.Models;

public sealed record ChecklistDescriptor
{
    public ChecklistDescriptor(
        IReadOnlyList<EvidenceRequirementDescriptor>? evidenceRequirements,
        IReadOnlyList<IntakeQuestion>? questions)
    {
        EvidenceRequirements = evidenceRequirements is null
            ? null
            : Array.AsReadOnly(evidenceRequirements.ToArray());
        Questions = questions is null
            ? null
            : Array.AsReadOnly(questions.ToArray());
    }

    public IReadOnlyList<EvidenceRequirementDescriptor>? EvidenceRequirements { get; }

    public IReadOnlyList<IntakeQuestion>? Questions { get; }

    public override string ToString() =>
        $"ChecklistDescriptor {{ EvidenceRequirementCount = {EvidenceRequirements?.Count}, QuestionCount = {Questions?.Count} }}";
}
