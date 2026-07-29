namespace Tenancy.Models;

/// <summary>
/// What a retention purge did, as <strong>counts only</strong>.
/// <para>
/// No display name, employer name, amount, box code, CRA line code, case
/// identifier, or participant identifier appears here or in anything the purge
/// logs. An operator needs to know that four records were deleted; knowing whose
/// they were is exactly the disclosure a purge must not create.
/// </para>
/// </summary>
public sealed class PurgeOutcome
{
    private PurgeOutcome(int recordsDeleted, int slipsDeleted)
    {
        RecordsDeleted = recordsDeleted;
        SlipsDeleted = slipsDeleted;
    }

    /// <summary>
    /// Creates the valid outcome for a firm-scoped purge with no eligible
    /// records.
    /// </summary>
    public static PurgeOutcome NoEligibleRecords() => new(0, 0);

    /// <summary>
    /// Creates a successful structural outcome for one or more deleted records.
    /// Counts are never inferred from, or replaced by, taxpayer identifiers.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="recordsDeleted"/> is not positive, or
    /// <paramref name="slipsDeleted"/> is negative.
    /// </exception>
    public static PurgeOutcome RecordsPurged(
        int recordsDeleted,
        int slipsDeleted)
    {
        if (recordsDeleted <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordsDeleted),
                "A completed purge must delete at least one record.");
        }

        if (slipsDeleted < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slipsDeleted),
                "A deleted-slip count cannot be negative.");
        }

        return new PurgeOutcome(recordsDeleted, slipsDeleted);
    }

    public int RecordsDeleted { get; private set; }

    public int SlipsDeleted { get; private set; }

    /// <summary>
    /// Fixed type identity only. Counts remain available through their typed
    /// properties without entering an implicitly formatted diagnostic string.
    /// </summary>
    public override string ToString() => nameof(PurgeOutcome);
}
