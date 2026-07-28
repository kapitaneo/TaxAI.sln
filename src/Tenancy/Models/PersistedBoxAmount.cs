using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// One engine-produced box amount at rest, in the package's own order.
/// <para>
/// The amount is copied verbatim. This module performs no arithmetic and no
/// rounding on it: an amount that could not be stored exactly is refused before
/// anything is written, never re-scaled to fit.
/// </para>
/// </summary>
public sealed class PersistedBoxAmount
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private PersistedBoxAmount()
    {
    }

    internal PersistedBoxAmount(
        Guid boxAmountId,
        Guid firmId,
        Guid slipId,
        int ordinal,
        string boxCode,
        decimal amount)
    {
        BoxAmountId = boxAmountId;
        FirmId = firmId;
        SlipId = slipId;
        Ordinal = ordinal;
        BoxCode = boxCode;
        Amount = amount;
    }

    public Guid BoxAmountId { get; private set; }

    public Guid FirmId { get; private set; }

    public Guid SlipId { get; private set; }

    public int Ordinal { get; private set; }

    /// <summary>Produced upstream by the engine. Never invented here.</summary>
    [JsonIgnore]
    public string BoxCode { get; private set; } = string.Empty;

    [JsonIgnore]
    public decimal Amount { get; private set; }

    /// <summary>Fixed type identity only; no box code or amount is rendered.</summary>
    public override string ToString() => nameof(PersistedBoxAmount);
}
