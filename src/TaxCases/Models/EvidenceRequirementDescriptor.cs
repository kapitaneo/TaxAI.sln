using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record EvidenceRequirementDescriptor(
    [property: JsonIgnore] string? RequirementId,
    [property: JsonIgnore] string? ParticipantId,
    [property: JsonIgnore] string? Category,
    int? MinimumCount)
{
    public override string ToString() =>
        $"EvidenceRequirementDescriptor {{ MinimumCount = {MinimumCount} }}";
}
