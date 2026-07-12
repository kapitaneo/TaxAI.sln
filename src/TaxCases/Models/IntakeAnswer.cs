using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record IntakeAnswer(
    [property: JsonIgnore] string QuestionId,
    [property: JsonIgnore] string? ParticipantId,
    [property: JsonIgnore] string Answer)
{
    public override string ToString() => "IntakeAnswer { Answer = <redacted> }";
}
