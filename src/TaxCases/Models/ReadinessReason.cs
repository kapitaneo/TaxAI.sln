using System.Text.Json.Serialization;
using TaxCases.Services;

namespace TaxCases.Models;

public sealed record ReadinessReason(
    ReadinessReasonKind Kind,
    [property: JsonIgnore] string Detail)
{
    public override string ToString() =>
        $"ReadinessReason {{ Kind = {Kind}, Detail = {SafeDiagnosticText.RedactSinLikeSequences(Detail)} }}";
}
