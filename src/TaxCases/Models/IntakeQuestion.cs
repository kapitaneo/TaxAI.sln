using System.Text.Json.Serialization;

namespace TaxCases.Models;

public sealed record IntakeQuestion(
    [property: JsonIgnore] string QuestionId,
    [property: JsonIgnore] string Prompt,
    IntakeQuestionScope Scope,
    [property: JsonIgnore] string? ParticipantId)
{
    public override string ToString() => $"IntakeQuestion {{ Scope = {Scope} }}";
}
