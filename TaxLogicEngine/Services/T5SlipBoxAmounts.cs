using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Flattens a <see cref="T5Slip"/> into the (box code, amount) pairs the mapper
/// works with — the single place that knows box code → typed property. Yields the
/// standard amount boxes whose value is non-zero (absent boxes default to 0 and are
/// skipped, so they are not reported as unmapped), plus every "Other information"
/// entry verbatim.
/// </summary>
public static class T5SlipBoxAmounts
{
    private static readonly (string Code, Func<T5Slip, decimal> Amount)[] TypedBoxes =
    {
        (T5BoxCodes.OtherThanEligibleDividendsActual, s => s.OtherThanEligibleDividendsActual),
        (T5BoxCodes.OtherThanEligibleDividendsTaxable, s => s.OtherThanEligibleDividendsTaxable),
        (T5BoxCodes.OtherThanEligibleDividendsTaxCredit, s => s.OtherThanEligibleDividendsTaxCredit),
        (T5BoxCodes.InterestFromCanadianSources, s => s.InterestFromCanadianSources),
        (T5BoxCodes.OtherIncomeFromCanadianSources, s => s.OtherIncomeFromCanadianSources),
        (T5BoxCodes.ForeignIncome, s => s.ForeignIncome),
        (T5BoxCodes.ForeignTaxPaid, s => s.ForeignTaxPaid),
        (T5BoxCodes.RoyaltiesFromCanadianSources, s => s.RoyaltiesFromCanadianSources),
        (T5BoxCodes.CapitalGainsDividends, s => s.CapitalGainsDividends),
        (T5BoxCodes.AccruedIncomeAnnuities, s => s.AccruedIncomeAnnuities),
        (T5BoxCodes.EligibleDividendsActual, s => s.EligibleDividendsActual),
        (T5BoxCodes.EligibleDividendsTaxable, s => s.EligibleDividendsTaxable),
        (T5BoxCodes.EligibleDividendsTaxCredit, s => s.EligibleDividendsTaxCredit),
    };

    /// <summary>Yields the slip's populated box amounts as (code, amount) pairs.</summary>
    public static IEnumerable<(string BoxCode, decimal Amount)> Enumerate(T5Slip slip)
    {
        ArgumentNullException.ThrowIfNull(slip);

        foreach (var (code, amount) in TypedBoxes)
        {
            var value = amount(slip);
            if (value != 0m)
            {
                yield return (code, value);
            }
        }

        foreach (var info in slip.OtherInformation)
        {
            yield return (info.Code, info.Amount);
        }
    }
}
