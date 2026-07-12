using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record ReceivedEvidence(
    [property: JsonIgnore] string EvidenceId,
    DocumentCategory Category,
    [property: JsonIgnore] string ParticipantId,
    int TaxYear,
    [property: JsonIgnore] string? SourceReference)
{
    public override string ToString() =>
        $"ReceivedEvidence {{ Category = {Category}, TaxYear = {TaxYear} }}";
}
