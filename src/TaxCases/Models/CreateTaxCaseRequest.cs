using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record CreateTaxCaseRequest(
    [property: JsonIgnore] string? CaseId,
    [property: JsonIgnore] string? FirmId,
    int? TaxYear,
    ParticipantDescriptor? PrimaryTaxpayer,
    [property: JsonIgnore] string? CorrelationReference)
{
    public override string ToString() =>
        $"CreateTaxCaseRequest {{ TaxYear = {TaxYear}, PrimaryTaxpayer = {PrimaryTaxpayer} }}";
}
