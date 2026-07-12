using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Flattens a <see cref="T4Slip"/> into the (box code, amount) pairs the mapper
/// works with — the single place that knows box code → typed property. Yields the
/// amount boxes whose value is non-zero (absent boxes default to 0 and are skipped,
/// so they are not reported as unmapped), plus every "Other information" entry
/// verbatim. Non-amount boxes (Box 12 SIN, 54, 50, 10, 29, 45, the 28 exempt
/// indicators) are never enumerated, so the SIN never enters the pipeline. Mirrors
/// <c>T5SlipBoxAmounts</c>.
/// <para>
/// The deferred amount boxes (16/16A/17/17A/24/26/46/55/56) are deliberately
/// included here: when populated they must surface as <c>UnmappedBoxCodes</c> at the
/// mapper rather than be silently dropped — the config simply does not map them.
/// </para>
/// </summary>
public static class T4SlipBoxAmounts
{
    private static readonly (string Code, Func<T4Slip, decimal> Amount)[] TypedBoxes =
    {
        (T4BoxCodes.EmploymentIncome, s => s.EmploymentIncome),
        (T4BoxCodes.CppContributions, s => s.CppContributions),
        (T4BoxCodes.SecondCppContributions, s => s.SecondCppContributions),
        (T4BoxCodes.QppContributions, s => s.QppContributions),
        (T4BoxCodes.SecondQppContributions, s => s.SecondQppContributions),
        (T4BoxCodes.EiPremiums, s => s.EiPremiums),
        (T4BoxCodes.RppContributions, s => s.RppContributions),
        (T4BoxCodes.IncomeTaxDeducted, s => s.IncomeTaxDeducted),
        (T4BoxCodes.EiInsurableEarnings, s => s.EiInsurableEarnings),
        (T4BoxCodes.CppQppPensionableEarnings, s => s.CppQppPensionableEarnings),
        (T4BoxCodes.UnionDues, s => s.UnionDues),
        (T4BoxCodes.CharitableDonations, s => s.CharitableDonations),
        (T4BoxCodes.PensionAdjustment, s => s.PensionAdjustment),
        (T4BoxCodes.PpipPremiums, s => s.PpipPremiums),
        (T4BoxCodes.PpipInsurableEarnings, s => s.PpipInsurableEarnings),
    };

    /// <summary>Yields the slip's populated box amounts as (code, amount) pairs.</summary>
    public static IEnumerable<(string BoxCode, decimal Amount)> Enumerate(T4Slip slip)
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
