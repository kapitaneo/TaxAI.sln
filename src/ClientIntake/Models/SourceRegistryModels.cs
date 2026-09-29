using System.Text.Json.Serialization;
using Tenancy.Models;

namespace ClientIntake.Models;

/// <summary>
/// Where one stored source object is in its life (spec 016 T04).
/// <para>
/// The registry row exists before the object's storage allocation becomes
/// reachable, so an upload that never commits is still tracked. The states only
/// move forward: <see cref="Staged"/> becomes <see cref="Bound"/> when the intake
/// metadata commit names it, or <see cref="PurgePending"/> when compensating
/// cleanup takes it; <see cref="PurgePending"/> becomes <see cref="Purged"/> only
/// on verified deletion. A <see cref="Bound"/> object is never cleaned up here —
/// due lifecycle deletion belongs to a later, separately approved slice.
/// </para>
/// </summary>
public enum SourceRegistryState
{
    Staged = 1,
    Bound = 2,
    PurgePending = 3,
    Purged = 4,
}

/// <summary>The closed outcome of a registry write.</summary>
public enum SourceRegistryWriteCode
{
    Success = 1,
    NotFound = 2,
    Conflict = 3,
}

/// <summary>
/// What a physical deletion attempt could actually prove. There is no value for
/// "probably deleted": only <see cref="Complete"/> is terminal, and it requires
/// the primary <b>and</b> every replica to be observed absent.
/// </summary>
public enum SourceObjectPurgeOutcome
{
    /// <summary>The primary and every replica were observed absent.</summary>
    Complete = 1,

    /// <summary>
    /// The primary was observed absent; a replica has not been, so the object is
    /// not yet proven destroyed and must be replayed.
    /// </summary>
    Pending = 2,

    /// <summary>Nothing could be verified; the deletion must be replayed.</summary>
    Unavailable = 3,
}

/// <summary>
/// One source object's registry entry: its trusted scope, the opaque resource
/// identifier its storage objects are named by, the reader content key that is
/// distinct from it, its state and its instants.
/// <para>
/// It holds no source byte, file name, key material, provider identifier or
/// taxpayer value. The identifiers and the content key are
/// <see cref="JsonIgnoreAttribute"/>-marked and never appear in
/// <see cref="ToString"/>.
/// </para>
/// </summary>
public sealed class SourceRegistration
{
    public SourceRegistration(
        TrustedFirmId firm,
        Guid caseId,
        Guid resourceId,
        string contentKey,
        SourceRegistryState state,
        DateTimeOffset stagedAtUtc,
        Guid? documentId,
        DateTimeOffset? boundAtUtc,
        DateTimeOffset? purgeRecordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentKey);

        if (caseId == Guid.Empty ||
            resourceId == Guid.Empty ||
            !Enum.IsDefined(state) ||
            stagedAtUtc.Offset != TimeSpan.Zero ||
            documentId == Guid.Empty ||
            (documentId is null) != (boundAtUtc is null) ||
            (state == SourceRegistryState.Bound && documentId is null) ||
            (state == SourceRegistryState.Staged && documentId is not null) ||
            (state is SourceRegistryState.PurgePending or SourceRegistryState.Purged) !=
                (purgeRecordedAtUtc is not null) ||
            boundAtUtc is { Offset.Ticks: not 0 } ||
            purgeRecordedAtUtc is { Offset.Ticks: not 0 })
        {
            throw new ArgumentException("The source registration is inconsistent.");
        }

        Firm = firm;
        CaseId = caseId;
        ResourceId = resourceId;
        ContentKey = contentKey;
        State = state;
        StagedAtUtc = stagedAtUtc;
        DocumentId = documentId;
        BoundAtUtc = boundAtUtc;
        PurgeRecordedAtUtc = purgeRecordedAtUtc;
    }

    [JsonIgnore]
    public TrustedFirmId Firm { get; }

    [JsonIgnore]
    public Guid CaseId { get; }

    /// <summary>The opaque name of the stored objects. Never the content key.</summary>
    [JsonIgnore]
    public Guid ResourceId { get; }

    /// <summary>The reader content key. It never authorizes a read by itself.</summary>
    [JsonIgnore]
    public string ContentKey { get; }

    public SourceRegistryState State { get; }

    public DateTimeOffset StagedAtUtc { get; }

    [JsonIgnore]
    public Guid? DocumentId { get; }

    public DateTimeOffset? BoundAtUtc { get; }

    public DateTimeOffset? PurgeRecordedAtUtc { get; }

    /// <summary>Fixed structural facts only — never an identifier or key.</summary>
    public override string ToString() => $"{nameof(SourceRegistration)}({State})";
}
