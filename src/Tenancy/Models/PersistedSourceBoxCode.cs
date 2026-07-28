using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// One box code that fed a CRA line entry, in the engine's own order.
/// <para>Wave 0 contract stage: shape declared, behaviour deferred.</para>
/// </summary>
public sealed class PersistedSourceBoxCode
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private PersistedSourceBoxCode()
    {
    }

    internal PersistedSourceBoxCode(
        Guid sourceBoxCodeId,
        Guid firmId,
        Guid lineEntryId,
        int ordinal,
        string boxCode) => throw new NotImplementedException();

    public Guid SourceBoxCodeId { get; private set; }

    public Guid FirmId { get; private set; }

    public Guid LineEntryId { get; private set; }

    public int Ordinal { get; private set; }

    [JsonIgnore]
    public string BoxCode { get; private set; } = string.Empty;

    /// <summary>Fixed type identity only; no box code is rendered.</summary>
    public override string ToString() => throw new NotImplementedException();
}
