namespace Tenancy.Services;

/// <summary>
/// Derives a record's retention boundary from its tax year, and decides purge
/// eligibility against a supplied instant.
/// <para>
/// <strong>The seven-year period is an engineering product default set by the
/// human. It is explicitly NOT a statement of any CRA statutory minimum, is not
/// sourced from canada.ca, and must not be presented as a CRA fact.</strong> If
/// a CRA retention rule later proves relevant, that is a new domain question and
/// a new specification — not a re-labelling of this default.
/// </para>
/// <para>
/// The boundary is <strong>EXCLUSIVE</strong>: a record is purge-eligible when
/// the evaluation instant is greater than or equal to it. One tick before, the
/// record survives; at exactly that instant, it does not. No inclusive
/// end-of-day value is computed, stored, defaulted, or compared anywhere — an
/// "end of day" written as 23:59:59 is a boundary that silently changes meaning
/// with precision.
/// </para>
/// <para>
/// The boundary depends on the record's own tax year and on nothing else. No
/// clock is read here, so two identical packages saved at different moments
/// receive identical boundaries.
/// </para>
/// <para>Wave 0 contract stage: behaviour is implemented in a later task.</para>
/// </summary>
public static class RetentionPolicy
{
    /// <summary>The exclusive end of a tax year is 1 January of the following year.</summary>
    public const int TaxYearEndOffsetYears = 1;

    /// <summary>The fixed v1 product retention period. Not a CRA statutory figure.</summary>
    public const int RetentionYears = 7;

    /// <summary>
    /// Midnight UTC on 1 January of the tax year plus
    /// <see cref="TaxYearEndOffsetYears"/> plus <see cref="RetentionYears"/>.
    /// </summary>
    public static DateTimeOffset ComputeRetentionUntilUtc(int taxYear) =>
        throw new NotImplementedException();

    /// <summary>
    /// True when the evaluation instant has reached or passed the exclusive
    /// boundary.
    /// </summary>
    public static bool IsPurgeEligible(
        DateTimeOffset retentionUntilUtc,
        DateTimeOffset evaluationInstantUtc) => throw new NotImplementedException();
}
