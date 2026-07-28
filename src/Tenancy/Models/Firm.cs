using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// A firm. Its own primary key is the tenant identifier every other tenant-owned
/// table carries, so it needs no carve-out from the firm-column rule.
/// <para>
/// A firm's display name is not taxpayer data and may be rendered to a signed-in
/// member. Nothing about a taxpayer is stored here.
/// </para>
/// <para>Wave 0 contract stage: shape declared, behaviour deferred.</para>
/// </summary>
public sealed class Firm
{
    /// <summary>Materialization seam for the storage layer.</summary>
    private Firm()
    {
    }

    internal Firm(
        Guid firmId,
        string displayName,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc) => throw new NotImplementedException();

    public Guid FirmId { get; private set; }

    [JsonIgnore]
    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Fixed type identity only; the firm's name is never rendered here.</summary>
    public override string ToString() => throw new NotImplementedException();
}
