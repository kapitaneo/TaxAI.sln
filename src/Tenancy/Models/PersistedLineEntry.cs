using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// One engine-produced CRA line entry at rest, in the package's own order.
/// <para>
/// Every CRA line code stored here originated in TaxLogicEngine's embedded
/// mapping config and arrived through the assembled package. This module owns no
/// box-to-line table, no CRA line literal, and no arithmetic — two entries
/// carrying the same line code are stored as two rows and are never combined
/// into a total.
/// </para>
/// <para>Wave 0 contract stage: shape declared, behaviour deferred.</para>
/// </summary>
public sealed class PersistedLineEntry
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private PersistedLineEntry()
    {
    }

    internal PersistedLineEntry(
        Guid lineEntryId,
        Guid firmId,
        Guid slipId,
        int ordinal,
        string lineCode,
        decimal amount,
        IReadOnlyList<PersistedSourceBoxCode> sourceBoxCodes) => throw new NotImplementedException();

    public Guid LineEntryId { get; private set; }

    public Guid FirmId { get; private set; }

    public Guid SlipId { get; private set; }

    public int Ordinal { get; private set; }

    /// <summary>The engine's CRA line code, copied verbatim.</summary>
    [JsonIgnore]
    public string LineCode { get; private set; } = string.Empty;

    [JsonIgnore]
    public decimal Amount { get; private set; }

    /// <summary>SIN-free provenance: the box codes that fed this line.</summary>
    [JsonIgnore]
    public IReadOnlyList<PersistedSourceBoxCode> SourceBoxCodes { get; private set; } =
        Array.Empty<PersistedSourceBoxCode>();

    /// <summary>Fixed type identity only; no line code or amount is rendered.</summary>
    public override string ToString() => throw new NotImplementedException();
}
