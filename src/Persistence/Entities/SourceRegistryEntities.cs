using ClientIntake.Models;

namespace Persistence.Entities;

/// <summary>
/// One stored source object's registry row (spec 016 T04). Structural only: the
/// trusted scope, the opaque resource identifier, the reader content key, the
/// state and its instants. No source byte, key material, file name, provider
/// identifier or taxpayer value has a column here.
/// </summary>
internal sealed class SourceRegistrationEntity
{
    public Guid FirmId { get; set; }
    public Guid CaseId { get; set; }
    public Guid ResourceId { get; set; }
    public string ContentKey { get; set; } = string.Empty;
    public SourceRegistryState State { get; set; }
    public DateTimeOffset StagedAtUtc { get; set; }
    public Guid? DocumentId { get; set; }
    public DateTimeOffset? BoundAtUtc { get; set; }
    public DateTimeOffset? PurgeRecordedAtUtc { get; set; }
}
