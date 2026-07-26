namespace CaseProcessing.Models;

/// <summary>
/// The closed set of <c>ValidationError.Field</c> names this module may emit,
/// declared once as constants so tests can assert the set mechanically. Messages
/// naming these fields are fixed, non-interpolated templates and never echo a
/// caller or payload value (PII by omission).
/// <para>
/// Errors originating in <c>T4SlipParser</c>, <c>T4LineMapper</c>, or an
/// <c>IT4LineMapProvider</c> keep the engine's own field names — they are
/// surfaced verbatim — so this closed set governs only the errors this module
/// originates itself.
/// </para>
/// </summary>
public static class T4ProcessingFields
{
    /// <summary>The extraction argument itself (null).</summary>
    public const string Extraction = "Extraction";

    /// <summary>The extraction's document kind is not a T4 slip.</summary>
    public const string Kind = "Kind";

    /// <summary>The T4 payload is absent on an extraction that claims to carry one.</summary>
    public const string Payload = "Payload";

    /// <summary>The payload's box collection is structurally untraversable.</summary>
    public const string PayloadBoxes = "PayloadBoxes";

    /// <summary>A payload box carries no usable code.</summary>
    public const string PayloadBoxCode = "PayloadBoxCode";

    /// <summary>The extraction's tax year is not a supported processing year.</summary>
    public const string TaxYear = "TaxYear";

    /// <summary>The parsed slip's year disagrees with the extraction's tax year.</summary>
    public const string SlipYear = "SlipYear";
}
