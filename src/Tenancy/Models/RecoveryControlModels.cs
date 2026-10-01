using System.Globalization;
using System.Text.Json.Serialization;
using Tenancy.Contracts;

namespace Tenancy.Models;

/// <summary>
/// The closed kinds of fact the independent recovery control ledger holds
/// (spec 016 T06, FR-10, FR-11).
/// <para>
/// <see cref="Genesis"/> and <see cref="Release"/> name one database
/// incarnation; the two terminal kinds name one already-decided destruction by
/// opaque identifiers only. There is no zero member: an uninitialized kind is
/// not a fact.
/// </para>
/// </summary>
public enum RecoveryControlRecordKind
{
    /// <summary>The ledger's first fact: the first released database incarnation.</summary>
    Genesis = 1,

    /// <summary>A database incarnation whose recovery reconciliation completed.</summary>
    Release = 2,

    /// <summary>A terminal source-object destruction (firm, case, resource).</summary>
    TerminalSource = 3,

    /// <summary>A terminal structured review-record destruction (firm, record).</summary>
    TerminalStructured = 4,
}

/// <summary>
/// What an append could prove. <see cref="Unacknowledged"/> is the zero value:
/// an append nobody confirmed is never read as durable.
/// </summary>
public enum RecoveryControlAppendOutcome
{
    /// <summary>Not proven in every copy the record's kind requires.</summary>
    Unacknowledged = 0,

    /// <summary>Written, read back and identical in every required copy.</summary>
    Acknowledged = 1,

    /// <summary>
    /// A different fact already occupies this record's identity. The ledger is
    /// append-only, so this is an integrity failure, never an overwrite.
    /// </summary>
    Conflict = 2,
}

/// <summary>
/// What a ledger read could establish. <see cref="Unavailable"/> is the zero
/// value: a read nobody completed never reads as a complete ledger.
/// </summary>
public enum RecoveryControlReadStatus
{
    Unavailable = 0,
    Complete = 1,

    /// <summary>A stored object is malformed or does not belong to the ledger.</summary>
    Invalid = 2,
}

/// <summary>What replaying one terminal fact against the recovered database did.</summary>
public enum RecoveryReplayOutcome
{
    /// <summary>Nothing was established; the replay must run again.</summary>
    Incomplete = 0,

    /// <summary>The recovered database already shows the terminal state.</summary>
    AlreadyTerminal = 1,

    /// <summary>The resurrected resource was fenced or removed by this replay.</summary>
    Applied = 2,

    /// <summary>The recovered database holds no such resource in that scope.</summary>
    Absent = 3,

    /// <summary>
    /// The resource cannot be removed without deleting other retained history;
    /// the environment stays quarantined rather than cascading.
    /// </summary>
    Blocked = 4,
}

/// <summary>
/// The closed reasons a recovered environment stays quarantined (FR-11). There
/// is no "released" member: release is the absence of every reason.
/// </summary>
public enum RecoveryQuarantineReason
{
    ControlLedgerUnavailable = 1,
    ControlLedgerInvalid = 2,
    ControlGenesisMissing = 3,
    RecoveryIntervalUncovered = 4,
    RecoveredDataOutsideBackupWindow = 5,
    ControlEvidenceStale = 6,
    AuthorizationAttestationMissing = 7,
    AuthorizationAttestationStale = 8,
    TerminalReplayIncomplete = 9,
    RegistryReplayIncomplete = 10,
    AccessInvalidationIncomplete = 11,
    DatabaseIncarnationUnavailable = 12,
    ReleaseUnacknowledged = 13,
    SecurityConfigurationInvalid = 14,
}

/// <summary>
/// One physical incarnation of the application database: an opaque SHA-256
/// fingerprint of the server, database and creation instant, as lower-case
/// hexadecimal.
/// <para>
/// A point-in-time restore, a geo-secondary taking over and a copy each create
/// a new incarnation, so an environment never inherits a previous
/// incarnation's release. A zone failover of the same database does not change
/// it. The fingerprint names no server, database or instant.
/// </para>
/// </summary>
public sealed class DatabaseIncarnation
{
    public const int FingerprintLength = 64;

    private DatabaseIncarnation(string fingerprint)
    {
        Fingerprint = fingerprint;
    }

    [JsonIgnore]
    public string Fingerprint { get; }

    /// <summary>
    /// Accepts exactly 64 lower-case hexadecimal characters; anything else is
    /// refused rather than normalized.
    /// </summary>
    public static bool TryCreate(string? fingerprint, out DatabaseIncarnation? incarnation)
    {
        incarnation = null;
        if (fingerprint is not { Length: FingerprintLength } ||
            !fingerprint.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'))
        {
            return false;
        }

        incarnation = new DatabaseIncarnation(fingerprint);
        return true;
    }

    public bool SameAs(DatabaseIncarnation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);
    }

    public override string ToString() => nameof(DatabaseIncarnation);
}

/// <summary>
/// The recovery windows that bound the control ledger's retention (FR-10,
/// FR-11, AC-103, AC-111).
/// <para>
/// Azure SQL Database keeps point-in-time restore backups for a configured
/// 1–35 days and, because a full backup is taken weekly, keeps full,
/// differential and log backups "for up to a week longer than the configured
/// retention period" (Microsoft Learn, <i>Automated backups — Azure SQL
/// Database</i>, read 2026-09-29). The longest recoverable window is therefore
/// the configured retention plus that seven-day chain allowance, and the
/// ledger must be retained strictly longer.
/// </para>
/// </summary>
public static class RecoveryRetentionWindow
{
    /// <summary>The approved SQL short-term retention: the 35-day maximum.</summary>
    public const int PointInTimeRestoreDays = 35;

    /// <summary>The documented weekly backup-chain dependency.</summary>
    public const int BackupChainDays = 7;

    /// <summary>[ASSUMED] The upper bound on the configured ledger retention.</summary>
    public const int MaximumControlLedgerRetentionDays = 365;

    /// <summary>
    /// [ASSUMED] How old the newest authorization attestation may be when a
    /// recovery is released: the four-hour regional recovery objective.
    /// </summary>
    public static readonly TimeSpan AttestationMaximumAge = TimeSpan.FromHours(4);

    /// <summary>The longest window from which a database can be recovered.</summary>
    public static TimeSpan LongestRecoverableWindow(int pointInTimeRestoreDays, int backupChainDays)
    {
        if (pointInTimeRestoreDays is < 1 or > PointInTimeRestoreDays || backupChainDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointInTimeRestoreDays));
        }

        return TimeSpan.FromDays(pointInTimeRestoreDays + backupChainDays);
    }

    /// <summary>
    /// True only when the ledger retention is strictly longer than the longest
    /// recoverable window and within the approved bound.
    /// </summary>
    public static bool LedgerOutlivesBackups(
        int controlLedgerRetentionDays,
        int pointInTimeRestoreDays,
        int backupChainDays) =>
        pointInTimeRestoreDays is >= 1 and <= PointInTimeRestoreDays &&
        backupChainDays >= BackupChainDays &&
        controlLedgerRetentionDays <= MaximumControlLedgerRetentionDays &&
        TimeSpan.FromDays(controlLedgerRetentionDays) >
            LongestRecoverableWindow(pointInTimeRestoreDays, backupChainDays);

    /// <summary>Reads a setting as a non-negative invariant integer.</summary>
    public static bool TryReadDays(string? candidate, out int days) =>
        int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out days);
}

/// <summary>
/// The state of one stored release evidence document. <see cref="Unavailable"/>
/// is the zero value; only <see cref="Present"/> carries a manifest.
/// </summary>
public enum ProductionEvidenceDocumentStatus
{
    Unavailable = 0,
    Present = 1,
    Absent = 2,

    /// <summary>The release process withdrew the evidence after publishing it.</summary>
    Withdrawn = 3,

    /// <summary>The stored object is malformed or oversized.</summary>
    Invalid = 4,
}

/// <summary>
/// What the recovered database holds, structurally: whether any taxpayer data
/// or session exists, and the newest activity instant. No identifier or value.
/// </summary>
public sealed class RecoveredDataExtent
{
    private RecoveredDataExtent(bool isReadable, bool holdsTaxpayerData, DateTimeOffset? latestActivityUtc)
    {
        IsReadable = isReadable;
        HoldsTaxpayerData = holdsTaxpayerData;
        LatestActivityUtc = latestActivityUtc;
    }

    public static RecoveredDataExtent Unreadable { get; } = new(false, true, null);

    public static RecoveredDataExtent Observed(bool holdsTaxpayerData, DateTimeOffset? latestActivityUtc)
    {
        if (latestActivityUtc is { Offset.Ticks: not 0 })
        {
            throw new ArgumentException("An activity instant must be UTC.", nameof(latestActivityUtc));
        }

        return new RecoveredDataExtent(true, holdsTaxpayerData, latestActivityUtc);
    }

    public bool IsReadable { get; }

    /// <summary>Conservatively true whenever the extent could not be read.</summary>
    public bool HoldsTaxpayerData { get; }

    public DateTimeOffset? LatestActivityUtc { get; }

    public override string ToString() => nameof(RecoveredDataExtent);
}

/// <summary>Counts of access revoked by recovery; structural only.</summary>
public sealed class RecoveryAccessInvalidation
{
    public RecoveryAccessInvalidation(int authenticationSessions, int clientSessions, int intakeLinks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(authenticationSessions);
        ArgumentOutOfRangeException.ThrowIfNegative(clientSessions);
        ArgumentOutOfRangeException.ThrowIfNegative(intakeLinks);

        AuthenticationSessions = authenticationSessions;
        ClientSessions = clientSessions;
        IntakeLinks = intakeLinks;
    }

    public int AuthenticationSessions { get; }

    public int ClientSessions { get; }

    public int IntakeLinks { get; }

    public override string ToString() => nameof(RecoveryAccessInvalidation);
}

/// <summary>Counts from applying an authorization attestation; structural only.</summary>
public sealed class RecoveryMembershipReconciliation
{
    public RecoveryMembershipReconciliation(int deactivated, int unrestored)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deactivated);
        ArgumentOutOfRangeException.ThrowIfNegative(unrestored);

        Deactivated = deactivated;
        Unrestored = unrestored;
    }

    /// <summary>Restored active memberships the attestation no longer authorizes.</summary>
    public int Deactivated { get; }

    /// <summary>Attested memberships the recovered database does not hold.</summary>
    public int Unrestored { get; }

    public override string ToString() => nameof(RecoveryMembershipReconciliation);
}

/// <summary>
/// The pure recovery-release decisions (FR-11). No clock, store or network is
/// read here: every fact and the evaluation instant are parameters, and every
/// rule fails closed.
/// </summary>
public static class RecoveryReleasePolicy
{
    /// <summary>[ASSUMED] How old a ledger read may be when a decision uses it.</summary>
    public static readonly TimeSpan ControlReadMaximumAge = TimeSpan.FromMinutes(15);

    /// <summary>
    /// True when the ledger records the release (or genesis) of exactly this
    /// incarnation.
    /// </summary>
    public static bool IsReleased(
        IEnumerable<RecoveryControlRecord> records,
        DatabaseIncarnation incarnation)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(incarnation);

        return records.Any(record =>
            record.Kind is RecoveryControlRecordKind.Genesis or RecoveryControlRecordKind.Release &&
            record.Incarnation is not null &&
            record.Incarnation.SameAs(incarnation));
    }

    /// <summary>
    /// A brand-new environment may write the ledger's genesis only when the
    /// ledger is completely empty and the database holds no taxpayer data,
    /// session or activity. An empty ledger beside existing data is missing
    /// control history (AC-115), never a fresh start.
    /// </summary>
    public static bool MayBootstrap(RecoveryControlSnapshot ledger, RecoveredDataExtent extent)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(extent);

        return ledger.Status == RecoveryControlReadStatus.Complete &&
               ledger.Records.Count == 0 &&
               ledger.Attestation is null &&
               extent.IsReadable &&
               !extent.HoldsTaxpayerData &&
               extent.LatestActivityUtc is null;
    }

    /// <summary>
    /// Every precondition that must hold before a recovered incarnation may be
    /// reconciled and released: an available, fresh, valid ledger whose
    /// history reaches back past the recovered data; recovered data inside the
    /// legitimate backup window; and current authorization evidence newer than
    /// that data. An empty result means reconciliation may proceed.
    /// </summary>
    public static IReadOnlyList<RecoveryQuarantineReason> EvaluatePreconditions(
        RecoveryReleaseFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var reasons = new List<RecoveryQuarantineReason>();
        var now = facts.EvaluatedAtUtc;
        var ledger = facts.Ledger;

        if (facts.Incarnation is null)
        {
            reasons.Add(RecoveryQuarantineReason.DatabaseIncarnationUnavailable);
        }

        if (ledger.Status == RecoveryControlReadStatus.Invalid)
        {
            reasons.Add(RecoveryQuarantineReason.ControlLedgerInvalid);
            return Close(reasons);
        }

        if (ledger.Status != RecoveryControlReadStatus.Complete)
        {
            reasons.Add(RecoveryQuarantineReason.ControlLedgerUnavailable);
            return Close(reasons);
        }

        if (ledger.ReadAtUtc > now || now - ledger.ReadAtUtc > ControlReadMaximumAge)
        {
            reasons.Add(RecoveryQuarantineReason.ControlEvidenceStale);
        }

        var geneses = ledger.Records
            .Where(record => record.Kind == RecoveryControlRecordKind.Genesis)
            .ToArray();
        var latest = facts.Extent.LatestActivityUtc;

        if (geneses.Length > 1)
        {
            reasons.Add(RecoveryQuarantineReason.ControlLedgerInvalid);
        }
        else if (geneses.Length == 0)
        {
            reasons.Add(RecoveryQuarantineReason.ControlGenesisMissing);
        }
        else if (!facts.Extent.IsReadable)
        {
            reasons.Add(RecoveryQuarantineReason.RecoveryIntervalUncovered);
        }
        else if (latest is { } activity)
        {
            if (activity < geneses[0].RecordedAtUtc || activity > now)
            {
                // Activity the ledger's history does not reach, or data from the future.
                reasons.Add(RecoveryQuarantineReason.RecoveryIntervalUncovered);
            }

            if (!RecoveryRetentionWindow.LedgerOutlivesBackups(
                    facts.ControlLedgerRetentionDays,
                    facts.PointInTimeRestoreDays,
                    facts.BackupChainDays) ||
                now - activity > TimeSpan.FromDays(facts.ControlLedgerRetentionDays))
            {
                reasons.Add(RecoveryQuarantineReason.RecoveryIntervalUncovered);
            }
            else if (now - activity > RecoveryRetentionWindow.LongestRecoverableWindow(
                         facts.PointInTimeRestoreDays,
                         facts.BackupChainDays))
            {
                // Older than any legitimate backup: not an approved restore.
                reasons.Add(RecoveryQuarantineReason.RecoveredDataOutsideBackupWindow);
            }
        }
        else if (facts.Extent.HoldsTaxpayerData)
        {
            // Data with no attributable activity cannot be placed in the interval.
            reasons.Add(RecoveryQuarantineReason.RecoveryIntervalUncovered);
        }

        if (ledger.Attestation is not { } attestation)
        {
            reasons.Add(RecoveryQuarantineReason.AuthorizationAttestationMissing);
        }
        else if (attestation.RecordedAtUtc > now ||
                 now - attestation.RecordedAtUtc > RecoveryRetentionWindow.AttestationMaximumAge ||
                 (latest is { } newest && attestation.RecordedAtUtc < newest))
        {
            reasons.Add(RecoveryQuarantineReason.AuthorizationAttestationStale);
        }

        return Close(reasons);
    }

    private static IReadOnlyList<RecoveryQuarantineReason> Close(List<RecoveryQuarantineReason> reasons) =>
        reasons.Distinct().OrderBy(reason => (int)reason).ToArray();
}

/// <summary>
/// The facts <see cref="RecoveryReleasePolicy.EvaluatePreconditions"/> decides
/// on, gathered by the orchestrator. Structural: its <see cref="ToString"/>
/// names no identifier or value.
/// </summary>
public sealed class RecoveryReleaseFacts
{
    public RecoveryReleaseFacts(
        DatabaseIncarnation? incarnation,
        RecoveryControlSnapshot ledger,
        RecoveredDataExtent extent,
        int controlLedgerRetentionDays,
        int pointInTimeRestoreDays,
        int backupChainDays,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(extent);
        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The evaluation instant must be UTC.", nameof(evaluatedAtUtc));
        }

        Incarnation = incarnation;
        Ledger = ledger;
        Extent = extent;
        ControlLedgerRetentionDays = controlLedgerRetentionDays;
        PointInTimeRestoreDays = pointInTimeRestoreDays;
        BackupChainDays = backupChainDays;
        EvaluatedAtUtc = evaluatedAtUtc;
    }

    public DatabaseIncarnation? Incarnation { get; }

    public RecoveryControlSnapshot Ledger { get; }

    public RecoveredDataExtent Extent { get; }

    public int ControlLedgerRetentionDays { get; }

    public int PointInTimeRestoreDays { get; }

    public int BackupChainDays { get; }

    public DateTimeOffset EvaluatedAtUtc { get; }

    public override string ToString() => nameof(RecoveryReleaseFacts);
}
