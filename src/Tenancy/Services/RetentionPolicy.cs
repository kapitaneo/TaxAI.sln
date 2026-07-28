namespace Tenancy.Services;

/// <summary>
/// Derives a record's retention boundary from its tax year, and decides purge
/// eligibility against a supplied instant.
/// <para>
/// <strong>The seven-year period is an engineering product default set by the
/// human. It is explicitly not a CRA statutory statement or a statement of any
/// CRA statutory minimum, is not sourced from canada.ca, and must not be
/// presented as a CRA fact.</strong> If a CRA retention rule later proves
/// relevant, that is a new domain question and a new specification — not a
/// re-labelling of this default.
/// </para>
/// <para>
/// The boundary is <strong>EXCLUSIVE</strong>: a record is purge-eligible when
/// the evaluation instant is greater than or equal to it. One tick before, the
/// record survives; at exactly that instant, it does not. No inclusive
/// end-of-day value is computed, stored, defaulted, or compared anywhere.
/// </para>
/// <para>
/// The boundary depends on the record's own tax year and on nothing else. No
/// clock is read here, so two identical packages saved at different moments
/// receive identical boundaries.
/// </para>
/// </summary>
public static class RetentionPolicy
{
    /// <summary>The exclusive end of a tax year is 1 January of the following year.</summary>
    public const int TaxYearEndOffsetYears = 1;

    /// <summary>
    /// The seven-year period is an engineering product default, explicitly not
    /// a CRA statutory statement.
    /// </summary>
    public const int RetentionYears = 7;

    /// <summary>
    /// Computes the exclusive retention boundary: midnight UTC on 1 January of
    /// the tax year plus
    /// <see cref="TaxYearEndOffsetYears"/> plus <see cref="RetentionYears"/>.
    /// </summary>
    public static DateTimeOffset ComputeRetentionUntilUtc(int taxYear) =>
        new(
            taxYear + TaxYearEndOffsetYears + RetentionYears,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);

    /// <summary>
    /// True when the evaluation instant has reached or passed the exclusive
    /// boundary.
    /// </summary>
    public static bool IsPurgeEligible(
        DateTimeOffset retentionUntilUtc,
        DateTimeOffset evaluationInstantUtc) =>
        evaluationInstantUtc >= retentionUntilUtc;
}
