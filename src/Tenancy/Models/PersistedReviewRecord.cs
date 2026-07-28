using System.Text.Json.Serialization;

namespace Tenancy.Models;

/// <summary>
/// One immutable, versioned snapshot of an accountant review package at rest.
/// <para>
/// Its property set is a <strong>closed allowlist</strong>. Nothing reaches the
/// database that is not named here or on a child type — in particular there is
/// no SIN in any form, no SIN-presence flag, no date of birth, no address, no
/// email, no phone, no employer account number, and no caller-supplied source
/// reference. PII here is handled by <em>omission, not masking</em>: the value
/// never enters the module, so there is nothing to mask.
/// </para>
/// <para>
/// Construction is internal so only the fail-closed projection can create one.
/// A record assembled around the guards would be a record nobody validated.
/// </para>
/// </summary>
public sealed class PersistedReviewRecord
{
    private readonly List<PersistedSlip> _slips = [];
    private readonly IReadOnlyList<PersistedSlip> _slipView;

    /// <summary>Materialization seam for the storage layer.</summary>
    private PersistedReviewRecord()
    {
        _slipView = _slips.AsReadOnly();
    }

    internal PersistedReviewRecord(
        Guid recordId,
        Guid firmId,
        string caseId,
        string participantId,
        int taxYear,
        int packageVersion,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset retentionUntilUtc,
        IReadOnlyList<PersistedSlip> slips)
        : this()
    {
        RecordId = recordId;
        FirmId = firmId;
        CaseId = caseId;
        ParticipantId = participantId;
        TaxYear = taxYear;
        PackageVersion = packageVersion;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RetentionUntilUtc = retentionUntilUtc;
        _slips.AddRange(slips);
    }

    /// <summary>A TaxAI-generated opaque identifier. Never derived from taxpayer content.</summary>
    public Guid RecordId { get; private set; }

    /// <summary>
    /// The <strong>trusted</strong> firm — never the firm the package carried.
    /// This is what makes a cross-firm write impossible to construct.
    /// </summary>
    public Guid FirmId { get; private set; }

    [JsonIgnore]
    public string CaseId { get; private set; } = string.Empty;

    [JsonIgnore]
    public string ParticipantId { get; private set; } = string.Empty;

    public int TaxYear { get; private set; }

    /// <summary>Existing maximum plus one. Earlier versions are never rewritten.</summary>
    public int PackageVersion { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Equal to <see cref="CreatedAtUtc"/>. The record is immutable, so this can
    /// never diverge; it is carried because the retention contract names it.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// The retention boundary, and it is <strong>EXCLUSIVE</strong>. A record is
    /// purge-eligible when the evaluation instant is greater than or equal to
    /// this value, so one tick before it the record survives and at exactly this
    /// instant it does not.
    /// <para>
    /// No inclusive end-of-day value is ever modelled. The seven-year period
    /// this is derived from is an engineering product default — it is
    /// <strong>not</strong> a statement of any CRA statutory minimum.
    /// </para>
    /// </summary>
    public DateTimeOffset RetentionUntilUtc { get; private set; }

    /// <summary>In the package's own order. Never sorted, filtered, or merged.</summary>
    [JsonIgnore]
    public IReadOnlyList<PersistedSlip> Slips => _slipView;

    /// <summary>
    /// Fixed structural facts only; never an identifier, name, amount, box code,
    /// CRA line code, or caller string.
    /// </summary>
    public override string ToString() => nameof(PersistedReviewRecord);
}
