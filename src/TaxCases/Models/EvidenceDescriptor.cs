using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record EvidenceDescriptor(
    [property: JsonIgnore] string? EvidenceId,
    [property: JsonIgnore] string? Category,
    [property: JsonIgnore] string? ParticipantId,
    int? TaxYear,
    [property: JsonIgnore] string? SourceReference,
    [property: JsonIgnore] string? FirmId)
{
    public override string ToString() => $"EvidenceDescriptor {{ TaxYear = {TaxYear} }}";
}
