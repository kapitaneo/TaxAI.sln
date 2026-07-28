using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// One reviewed slip at rest.
/// <para>
/// The slip's SIN is not here, not masked, not flagged, and not derivable. The
/// projection never reads it.
/// </para>
/// <para>
/// Wave 0 contract stage: the shape is declared; construction and
/// <see cref="ToString"/> are implemented in a later task.
/// </para>
/// </summary>
public sealed class PersistedSlip
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private PersistedSlip()
    {
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
        IReadOnlyList<PersistedUnmappedBoxCode> unmappedBoxCodes) => throw new NotImplementedException();

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
    public IReadOnlyList<PersistedBoxAmount> BoxAmounts { get; private set; } =
        Array.Empty<PersistedBoxAmount>();

    [JsonIgnore]
    public IReadOnlyList<PersistedLineEntry> LineEntries { get; private set; } =
        Array.Empty<PersistedLineEntry>();

    [JsonIgnore]
    public IReadOnlyList<PersistedUnmappedBoxCode> UnmappedBoxCodes { get; private set; } =
        Array.Empty<PersistedUnmappedBoxCode>();

    /// <summary>Fixed structural facts only.</summary>
    public override string ToString() => throw new NotImplementedException();
}
