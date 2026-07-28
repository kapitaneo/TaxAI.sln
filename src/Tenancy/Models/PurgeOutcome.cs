namespace Tenancy.Models;

/// <summary>
/// What a retention purge did, as <strong>counts only</strong>.
/// <para>
/// No display name, employer name, amount, box code, CRA line code, case
/// identifier, or participant identifier appears here or in anything the purge
/// logs. An operator needs to know that four records were deleted; knowing whose
/// they were is exactly the disclosure a purge must not create.
/// </para>
/// <para>Wave 0 contract stage: shape declared, behaviour deferred.</para>
/// </summary>
public sealed class PurgeOutcome
{
    internal PurgeOutcome(int recordsDeleted, int slipsDeleted) =>
        throw new NotImplementedException();

    public int RecordsDeleted { get; private set; }

    public int SlipsDeleted { get; private set; }

    /// <summary>Structural counts only.</summary>
    public override string ToString() => throw new NotImplementedException();
}
