using System.Text.Json.Serialization;
using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed class ParticipantDescriptor
{
    [JsonIgnore]
    private readonly string? _rawSin;

    public ParticipantDescriptor(
        string? participantId,
        RelationshipKind relationship,
        string? displayName,
        string? sin,
        string? firmId)
    {
        ParticipantId = participantId;
        Relationship = relationship;
        DisplayName = displayName;
        _rawSin = sin;
        FirmId = firmId;
        MaskedSin = Mask(sin);
    }

    [JsonIgnore]
    public string? ParticipantId { get; }

    public RelationshipKind Relationship { get; }

    [JsonIgnore]
    public string? DisplayName { get; }

    public string? MaskedSin { get; }

    [JsonIgnore]
    public string? FirmId { get; }

    internal string? RawSin => _rawSin;

    public override string ToString() =>
        $"ParticipantDescriptor {{ Relationship = {Relationship}, MaskedSin = {MaskedSin ?? "<none>"} }}";

    private static string? Mask(string? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return new RecipientIdentifier(value).Masked;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
