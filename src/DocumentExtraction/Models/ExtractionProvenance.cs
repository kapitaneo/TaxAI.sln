using System.Globalization;
using System.Text.Json.Serialization;

namespace DocumentExtraction.Models;

/// <summary>
/// Where an extraction came from. Untrusted upstream metadata: everything is
/// nullable/loose because it arrives unvalidated.
/// <para>
/// PII is handled by <b>omission</b>: the caller-controlled strings are
/// <see cref="JsonIgnoreAttribute"/> for supported direct JSON serialization and
/// are never emitted by <see cref="ToString"/>, which prints fixed structural
/// facts only. A sealed class, not a record, so no compiler-generated
/// <c>ToString()</c> can print property values.
/// </para>
/// </summary>
public sealed class ExtractionProvenance
{
    /// <summary>
    /// Opaque caller-supplied reference to the stored source document.
    /// Required by the validator for every kind.
    /// </summary>
    [JsonIgnore]
    public string? SourceReference { get; init; }

    /// <summary>Optional caller-supplied label naming the extraction provider.</summary>
    [JsonIgnore]
    public string? ProviderLabel { get; init; }

    /// <summary>
    /// Optional provider confidence. <c>0m</c>–<c>1m</c> inclusive is accepted by the
    /// validator; outside fails closed; there is no low-confidence threshold.
    /// </summary>
    public decimal? Confidence { get; init; }

    /// <summary>Fixed structural facts only — never a caller-supplied value.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionProvenance(HasSourceReference: {0}, HasProviderLabel: {1}, Confidence: {2})",
            SourceReference is not null,
            ProviderLabel is not null,
            Confidence?.ToString(CultureInfo.InvariantCulture) ?? "absent");
}
