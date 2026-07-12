using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record IntakeAnswerDescriptor(
    [property: JsonIgnore] string? QuestionId,
    [property: JsonIgnore] string? ParticipantId,
    [property: JsonIgnore] string? Answer)
{
    public override string ToString() => "IntakeAnswerDescriptor { Answer = <redacted> }";
}
