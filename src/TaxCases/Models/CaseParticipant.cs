using System.Text.Json.Serialization;
using TaxCases.Services;
using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed record CaseParticipant(
    [property: JsonIgnore] string ParticipantId,
    RelationshipKind Relationship,
    [property: JsonIgnore] string? DisplayName,
    RecipientIdentifier? Sin)
{
    public override string ToString() =>
        $"CaseParticipant {{ Relationship = {Relationship}, Sin = {Sin?.Masked ?? "<none>"} }}";
}
