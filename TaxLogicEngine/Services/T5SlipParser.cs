using System.Globalization;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Turns already-extracted <see cref="T5RawInput"/> into a typed <see cref="T5Slip"/>.
/// Pure and deterministic: no I/O, no ambient state, same input → same result.
/// <para>
/// This is the happy-path mapping (T07): it assumes valid input and always returns
/// <see cref="ParseResult{T}.Success"/>. Validation and the fail-closed error paths
/// (required fields, ranges, duplicates, bad amounts) are added in T08.
/// </para>
/// </summary>
public sealed class T5SlipParser
{
    private static readonly HashSet<string> AmountBoxCodes = new(StringComparer.Ordinal)
    {
        T5BoxCodes.OtherThanEligibleDividendsActual,
        T5BoxCodes.OtherThanEligibleDividendsTaxable,
        T5BoxCodes.OtherThanEligibleDividendsTaxCredit,
        T5BoxCodes.InterestFromCanadianSources,
        T5BoxCodes.OtherIncomeFromCanadianSources,
        T5BoxCodes.ForeignIncome,
        T5BoxCodes.ForeignTaxPaid,
        T5BoxCodes.RoyaltiesFromCanadianSources,
        T5BoxCodes.CapitalGainsDividends,
        T5BoxCodes.AccruedIncomeAnnuities,
        T5BoxCodes.EligibleDividendsActual,
        T5BoxCodes.EligibleDividendsTaxable,
        T5BoxCodes.EligibleDividendsTaxCredit,
    };

    private static readonly HashSet<string> ScalarBoxCodes = new(StringComparer.Ordinal)
    {
        T5BoxCodes.ReportCode,
        T5BoxCodes.RecipientIdentificationNumber,
        T5BoxCodes.RecipientType,
        T5BoxCodes.ForeignCurrency,
        T5BoxCodes.Transit,
        T5BoxCodes.RecipientAccount,
    };

    /// <summary>Parses one slip's extracted boxes into a typed <see cref="T5Slip"/>.</summary>
    public ParseResult<T5Slip> Parse(T5RawInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var scalars = new Dictionary<string, string?>(StringComparer.Ordinal);
        var other = new List<T5OtherInformation>();

        foreach (var box in input.Boxes)
        {
            if (AmountBoxCodes.Contains(box.Code) || ScalarBoxCodes.Contains(box.Code))
            {
                // Happy path assumes unique codes; T08 rejects duplicates.
                scalars[box.Code] = box.Value;
            }
            else
            {
                // Unknown / non-standard box (e.g. box 30) — preserved verbatim.
                other.Add(new T5OtherInformation(box.Code, ParseAmount(box.Value)));
            }
        }

        var currency = Scalar(scalars, T5BoxCodes.ForeignCurrency);
        var recipientId = Scalar(scalars, T5BoxCodes.RecipientIdentificationNumber);

        var slip = new T5Slip
        {
            TaxYear = input.TaxYear ?? 0,
            PayerName = input.PayerName ?? string.Empty,
            PayerAddress = input.PayerAddress,
            RecipientName = input.RecipientName ?? string.Empty,
            RecipientAddress = input.RecipientAddress,
            RecipientType = Int(scalars, T5BoxCodes.RecipientType),
            RecipientId = string.IsNullOrWhiteSpace(recipientId) ? null : new RecipientIdentifier(recipientId),
            ReportCode = Scalar(scalars, T5BoxCodes.ReportCode),
            TransitNumber = Scalar(scalars, T5BoxCodes.Transit),
            AccountNumber = Scalar(scalars, T5BoxCodes.RecipientAccount),
            CurrencyCode = string.IsNullOrWhiteSpace(currency) ? "CAD" : currency,

            OtherThanEligibleDividendsActual = Amount(scalars, T5BoxCodes.OtherThanEligibleDividendsActual),
            OtherThanEligibleDividendsTaxable = Amount(scalars, T5BoxCodes.OtherThanEligibleDividendsTaxable),
            OtherThanEligibleDividendsTaxCredit = Amount(scalars, T5BoxCodes.OtherThanEligibleDividendsTaxCredit),
            InterestFromCanadianSources = Amount(scalars, T5BoxCodes.InterestFromCanadianSources),
            OtherIncomeFromCanadianSources = Amount(scalars, T5BoxCodes.OtherIncomeFromCanadianSources),
            ForeignIncome = Amount(scalars, T5BoxCodes.ForeignIncome),
            ForeignTaxPaid = Amount(scalars, T5BoxCodes.ForeignTaxPaid),
            RoyaltiesFromCanadianSources = Amount(scalars, T5BoxCodes.RoyaltiesFromCanadianSources),
            CapitalGainsDividends = Amount(scalars, T5BoxCodes.CapitalGainsDividends),
            AccruedIncomeAnnuities = Amount(scalars, T5BoxCodes.AccruedIncomeAnnuities),
            EligibleDividendsActual = Amount(scalars, T5BoxCodes.EligibleDividendsActual),
            EligibleDividendsTaxable = Amount(scalars, T5BoxCodes.EligibleDividendsTaxable),
            EligibleDividendsTaxCredit = Amount(scalars, T5BoxCodes.EligibleDividendsTaxCredit),

            OtherInformation = other,
        };

        return ParseResult<T5Slip>.Success(slip);
    }

    private static string? Scalar(IReadOnlyDictionary<string, string?> boxes, string code) =>
        boxes.TryGetValue(code, out var value) ? value : null;

    private static decimal Amount(IReadOnlyDictionary<string, string?> boxes, string code) =>
        boxes.TryGetValue(code, out var value) ? ParseAmount(value) : 0m;

    private static int Int(IReadOnlyDictionary<string, string?> boxes, string code) =>
        boxes.TryGetValue(code, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? MoneyRounding.ToCents(d)
            : 0m;
}
