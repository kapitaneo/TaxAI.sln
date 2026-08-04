using System.Globalization;

namespace DocumentIntelligence;

/// <summary>
/// The configuration the deterministic adapter requires, and the guard that
/// refuses a host that did not supply it.
/// <para>
/// Nothing here has a default. A missing, empty, or invalid value refuses
/// startup rather than falling back to something plausible: a silently defaulted
/// size limit is a size limit that stops being checked, and a silently defaulted
/// label is a provenance record that names nothing.
/// </para>
/// </summary>
public sealed class DocumentIntelligenceOptions
{
    /// <summary>
    /// The largest source document the adapter will read. Content beyond this
    /// limit is refused rather than truncated — a truncated document would
    /// extract as a document that is merely missing fields.
    /// </summary>
    public long MaximumSourceBytes { get; init; }

    /// <summary>
    /// The server-owned label recorded as the provenance of what ran. It is
    /// configuration, never taxpayer data and never a caller value.
    /// </summary>
    public string? ProviderLabel { get; init; }

    /// <summary>The longest label the guard accepts.</summary>
    public const int MaximumProviderLabelLength = 64;

    /// <summary>
    /// Returns the options when every value is present and usable, and throws
    /// otherwise. Called at composition time, so an unusable configuration is a
    /// host that refuses to start rather than a surface that half works.
    /// </summary>
    /// <exception cref="ArgumentNullException">The options are absent.</exception>
    /// <exception cref="ArgumentException">A value is missing, empty, or invalid.</exception>
    public static DocumentIntelligenceOptions RequireValid(DocumentIntelligenceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaximumSourceBytes <= 0)
        {
            throw new ArgumentException(
                "The maximum source byte count must be a positive value.",
                nameof(options));
        }

        var label = options.ProviderLabel;

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException(
                "The provider label is required.",
                nameof(options));
        }

        if (label.Length > MaximumProviderLabelLength)
        {
            throw new ArgumentException(
                "The provider label is longer than the permitted length.",
                nameof(options));
        }

        foreach (var character in label)
        {
            if (char.IsControl(character))
            {
                throw new ArgumentException(
                    "The provider label contains a control character.",
                    nameof(options));
            }
        }

        return options;
    }

    /// <summary>Fixed structural facts only — never the configured label.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "DocumentIntelligenceOptions(MaximumSourceBytes: {0}, HasProviderLabel: {1})",
            MaximumSourceBytes,
            ProviderLabel is not null);
}
