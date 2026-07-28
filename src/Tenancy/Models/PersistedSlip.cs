using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// One reviewed slip at rest.
/// <para>
/// The slip's SIN is not here, not masked, not flagged, and not derivable. The
/// projection never reads it.
/// </para>
/// </summary>
public sealed class PersistedSlip
{
    private readonly List<PersistedBoxAmount> _boxAmounts = [];
    private readonly List<PersistedLineEntry> _lineEntries = [];
    private readonly List<PersistedUnmappedBoxCode> _unmappedBoxCodes = [];
    private readonly IReadOnlyList<PersistedBoxAmount> _boxAmountView;
    private readonly IReadOnlyList<PersistedLineEntry> _lineEntryView;
    private readonly IReadOnlyList<PersistedUnmappedBoxCode> _unmappedBoxCodeView;

    /// <summary>Materialization seam for the storage layer.</summary>
    private PersistedSlip()
    {
        _boxAmountView = _boxAmounts.AsReadOnly();
        _lineEntryView = _lineEntries.AsReadOnly();
        _unmappedBoxCodeView = _unmappedBoxCodes.AsReadOnly();
    }

    internal PersistedSlip(
        Guid slipId,
        Guid firmId,
        Guid recordId,
        int ordinal,
        string slipTypeCode,
        Guid documentReference,
        string taxpayerDisplayName,
        string employerDisplayName,
        IReadOnlyList<PersistedBoxAmount> boxAmounts,
        IReadOnlyList<PersistedLineEntry> lineEntries,
        IReadOnlyList<PersistedUnmappedBoxCode> unmappedBoxCodes)
        : this()
    {
        SlipId = slipId;
        FirmId = firmId;
        RecordId = recordId;
        Ordinal = ordinal;
        SlipTypeCode = slipTypeCode;
        DocumentReference = documentReference;
        TaxpayerDisplayName = taxpayerDisplayName;
        EmployerDisplayName = employerDisplayName;
        _boxAmounts.AddRange(boxAmounts);
        _lineEntries.AddRange(lineEntries);
        _unmappedBoxCodes.AddRange(unmappedBoxCodes);
    }

    public Guid SlipId { get; private set; }

    public Guid FirmId { get; private set; }

    public Guid RecordId { get; private set; }

    /// <summary>The slip's position in the package. Stored, never inferred.</summary>
    public int Ordinal { get; private set; }

    /// <summary>Structural only. Asserts no CRA semantics.</summary>
    public string SlipTypeCode { get; private set; } = string.Empty;

    /// <summary>
    /// A <strong>TaxAI-generated</strong> opaque reference. The extraction
    /// envelope's own document id and source reference are deliberately not
    /// persisted: both are caller-controlled, and neither is tax truth.
    /// </summary>
    public Guid DocumentReference { get; private set; }

    [JsonIgnore]
    public string TaxpayerDisplayName { get; private set; } = string.Empty;

    [JsonIgnore]
    public string EmployerDisplayName { get; private set; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<PersistedBoxAmount> BoxAmounts => _boxAmountView;

    [JsonIgnore]
    public IReadOnlyList<PersistedLineEntry> LineEntries => _lineEntryView;

    [JsonIgnore]
    public IReadOnlyList<PersistedUnmappedBoxCode> UnmappedBoxCodes => _unmappedBoxCodeView;

    /// <summary>Fixed structural facts only.</summary>
    public override string ToString() => nameof(PersistedSlip);
}
