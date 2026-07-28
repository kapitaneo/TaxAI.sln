using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// One populated box code the engine deliberately did not map, in the package's
/// own order.
/// <para>
/// An empty collection is stored as an empty collection and stays
/// distinguishable from one that was never stored. Dropping these would hide
/// exactly the boxes an accountant must look at.
/// </para>
/// <para>Wave 0 contract stage: shape declared, behaviour deferred.</para>
/// </summary>
public sealed class PersistedUnmappedBoxCode
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private PersistedUnmappedBoxCode()
    {
    }

    internal PersistedUnmappedBoxCode(
        Guid unmappedBoxCodeId,
        Guid firmId,
        Guid slipId,
        int ordinal,
        string boxCode) => throw new NotImplementedException();

    public Guid UnmappedBoxCodeId { get; private set; }

    public Guid FirmId { get; private set; }

    public Guid SlipId { get; private set; }

    public int Ordinal { get; private set; }

    [JsonIgnore]
    public string BoxCode { get; private set; } = string.Empty;

    /// <summary>Fixed type identity only; no box code is rendered.</summary>
    public override string ToString() => throw new NotImplementedException();
}
