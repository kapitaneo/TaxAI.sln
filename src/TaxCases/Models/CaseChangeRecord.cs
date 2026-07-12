using System.Text.Json.Serialization;
using TaxCases.Services;

namespace TaxCases.Models;

public sealed record CaseChangeRecord(
    [property: JsonIgnore] string CaseId,
    CaseChangeKind Kind,
    [property: JsonIgnore] string? ParticipantId,
    [property: JsonIgnore] string? EvidenceId,
    [property: JsonIgnore] string? QuestionId,
    CaseStatus? PreviousStatus,
    CaseStatus? ResultingStatus,
    [property: JsonIgnore] string? CorrelationReference,
    [property: JsonIgnore] string? Detail)
{
    public override string ToString() =>
        $"CaseChangeRecord {{ Kind = {Kind}, CaseId = {SafeDiagnosticText.RedactSinLikeSequences(CaseId)}, " +
        $"ParticipantId = {SafeDiagnosticText.RedactSinLikeSequences(ParticipantId)}, " +
        $"EvidenceId = {SafeDiagnosticText.RedactSinLikeSequences(EvidenceId)}, " +
        $"QuestionId = {SafeDiagnosticText.RedactSinLikeSequences(QuestionId)}, " +
        $"PreviousStatus = {PreviousStatus}, ResultingStatus = {ResultingStatus}, " +
        $"CorrelationReference = {SafeDiagnosticText.RedactSinLikeSequences(CorrelationReference)}, " +
        $"Detail = {SafeDiagnosticText.RedactSinLikeSequences(Detail)} }}";
}
