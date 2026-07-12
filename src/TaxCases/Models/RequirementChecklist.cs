namespace TaxCases.Models;

public sealed record RequirementChecklist
{
    public RequirementChecklist(
        IReadOnlyList<EvidenceRequirement> evidenceRequirements,
        IReadOnlyList<IntakeQuestion> questions)
    {
        EvidenceRequirements = Array.AsReadOnly(
            (evidenceRequirements ?? throw new ArgumentNullException(nameof(evidenceRequirements))).ToArray());
        Questions = Array.AsReadOnly(
            (questions ?? throw new ArgumentNullException(nameof(questions))).ToArray());
    }

    public IReadOnlyList<EvidenceRequirement> EvidenceRequirements { get; }

    public IReadOnlyList<IntakeQuestion> Questions { get; }

    public override string ToString() =>
        $"RequirementChecklist {{ EvidenceRequirementCount = {EvidenceRequirements.Count}, QuestionCount = {Questions.Count} }}";
}
