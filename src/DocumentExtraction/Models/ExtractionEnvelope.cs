using System.Globalization;
using System.Text.Json.Serialization;
using TaxLogicEngine.Models;

namespace DocumentExtraction.Models;

/// <summary>
/// The untrusted extraction submission. Everything is nullable/loose on purpose —
/// it is unvalidated upstream data, exactly like <see cref="T4RawInput"/> itself.
/// Every envelope passes through <c>ExtractionEnvelopeValidator.Validate</c>,
/// the single fail-closed gate; nothing here is trusted until it does.
/// <para>
/// The attribution tuple is <see cref="FirmId"/> + <see cref="CaseId"/> +
/// <see cref="ParticipantId"/> + <see cref="TaxYear"/> (case-scoped;
/// <see cref="ParticipantId"/> is case-local and never meaningful without
/// <see cref="CaseId"/>). Validation is <b>presence-only</b>: it never proves
/// the case belongs to the firm or the participant to the case.
/// </para>
/// <para>
/// PII is handled by <b>omission</b>: the payload (which may carry an unmasked
/// SIN in Box 12) and every caller-controlled string are
/// <see cref="JsonIgnoreAttribute"/> and are never emitted by
/// <see cref="ToString"/>. A sealed class, not a record, so no
/// compiler-generated <c>ToString()</c> can print property values.
/// </para>
/// </summary>
public sealed class ExtractionEnvelope
{
    /// <summary>Opaque caller-supplied identifier for the submitted document.</summary>
    [JsonIgnore]
    public string? DocumentId { get; init; }

    /// <summary>The claimed document kind; absent or undefined values fail closed.</summary>
    public ExtractedDocumentKind? Kind { get; init; }

    /// <summary>Opaque caller-supplied firm identifier.</summary>
    [JsonIgnore]
    public string? FirmId { get; init; }

    /// <summary>Opaque caller-supplied case identifier.</summary>
    [JsonIgnore]
    public string? CaseId { get; init; }

    /// <summary>Opaque caller-supplied case-local participant identifier.</summary>
    [JsonIgnore]
    public string? ParticipantId { get; init; }

    /// <summary>The tax year the extraction targets. Spec 007 supports exactly 2025.</summary>
    public int? TaxYear { get; init; }

    /// <summary>Provenance of the extraction; its source reference is required for every kind.</summary>
    public ExtractionProvenance? Provenance { get; init; }

    /// <summary>
    /// The raw T4 payload — required when <see cref="Kind"/> is
    /// <see cref="ExtractedDocumentKind.T4Slip"/>, forbidden otherwise. It stays
    /// an untrusted <see cref="T4RawInput"/>: nothing in this module parses,
    /// types, normalizes, redacts, converts, maps, rounds, or calculates it.
    /// </summary>
    [JsonIgnore]
    public T4RawInput? T4Payload { get; init; }

    /// <summary>Fixed structural facts only — never a caller or payload value.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionEnvelope(Kind: {0}, TaxYear: {1}, HasPayload: {2}, BoxCount: {3}, HasProvenance: {4})",
            Kind?.ToString() ?? "absent",
            TaxYear?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            T4Payload is not null,
            T4Payload?.Boxes?.Count ?? 0,
            Provenance is not null);
}
