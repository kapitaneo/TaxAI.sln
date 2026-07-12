using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record EvidenceRequirement(
    [property: JsonIgnore] string RequirementId,
    [property: JsonIgnore] string ParticipantId,
    DocumentCategory Category,
    int MinimumCount)
{
    public override string ToString() =>
        $"EvidenceRequirement {{ Category = {Category}, MinimumCount = {MinimumCount} }}";
}
