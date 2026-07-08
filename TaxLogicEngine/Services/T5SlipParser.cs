using System.Globalization;
using TaxLogicEngine.Models;

namespace TaxLogicEngine.Services;

/// <summary>
/// Turns already-extracted <see cref="T5RawInput"/> into a typed <see cref="T5Slip"/>.
/// Pure and deterministic: no I/O, no ambient state, same input → same result.
/// <para>
/// Fail-closed: input is validated first; on any problem the parser returns
/// <see cref="ParseResult{T}.Failure"/> with structured errors and <b>no</b> model,
/// so partially-valid data can never leak downstream. Validation messages never
/// echo the recipient identification number (Box 22).
/// </para>
/// </summary>
public sealed class T5SlipParser
{
    private const int MinRecipientType = 1;
    private const int MaxRecipientType = 5; // CRA: 1 individual .. 5 government/intl org

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

    // Non-amount boxes: strings/codes, not monetary values (so exempt from amount checks).
    private static readonly HashSet<string> ScalarBoxCodes = new(StringComparer.Ordinal)
    {
        T5BoxCodes.ReportCode,
        T5BoxCodes.RecipientIdentificationNumber,
        T5BoxCodes.RecipientType,
        T5BoxCodes.ForeignCurrency,
        T5BoxCodes.Transit,
        T5BoxCodes.RecipientAccount,
    };

    /// <summary>
    /// Validates and parses one slip's extracted boxes. Returns the typed slip on
    /// success, or all collected validation errors (and no model) on failure.
    /// </summary>
    public ParseResult<T5Slip> Parse(T5RawInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var errors = new List<ValidationError>();

        ValidateNoDuplicateBoxes(input, errors);
        ValidateRequiredFields(input, errors);
        ValidateRecipientType(input, errors);
        ValidateAmounts(input, errors);

        return errors.Count > 0
            ? ParseResult<T5Slip>.Failure(errors)
            : ParseResult<T5Slip>.Success(BuildSlip(input));
    }

    private static void ValidateNoDuplicateBoxes(T5RawInput input, List<ValidationError> errors)
    {
        foreach (var group in input.Boxes.GroupBy(b => b.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add(new ValidationError($"Box {group.Key}", $"Box {group.Key} appears more than once."));
        }
    }

    private static void ValidateRequiredFields(T5RawInput input, List<ValidationError> errors)
    {
        if (input.TaxYear is null)
        {
            errors.Add(new ValidationError(nameof(T5RawInput.TaxYear), "Tax year is required."));
        }

        if (string.IsNullOrWhiteSpace(input.PayerName))
        {
            errors.Add(new ValidationError(nameof(T5RawInput.PayerName), "Payer name is required."));
        }

        if (string.IsNullOrWhiteSpace(input.RecipientName))
        {
            errors.Add(new ValidationError(nameof(T5RawInput.RecipientName), "Recipient name is required."));
        }
    }

    private static void ValidateRecipientType(T5RawInput input, List<ValidationError> errors)
    {
        const string field = "Box 23";
        var value = FirstValue(input, T5BoxCodes.RecipientType);

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError(field, "Recipient type is required."));
        }
        else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
        {
            errors.Add(new ValidationError(field, "Recipient type must be a whole number."));
        }
        else if (type is < MinRecipientType or > MaxRecipientType)
        {
            errors.Add(new ValidationError(field, $"Recipient type must be one of {MinRecipientType}-{MaxRecipientType}."));
        }
    }

    private static void ValidateAmounts(T5RawInput input, List<ValidationError> errors)
    {
        foreach (var box in input.Boxes)
        {
            // Scalar boxes are not monetary; blank values mean "absent", not garbled.
            if (ScalarBoxCodes.Contains(box.Code) || string.IsNullOrWhiteSpace(box.Value))
            {
                continue;
            }

            if (!decimal.TryParse(box.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                errors.Add(new ValidationError($"Box {box.Code}", $"Box {box.Code} must be a numeric amount."));
            }
            else if (amount < 0)
            {
                errors.Add(new ValidationError($"Box {box.Code}", $"Box {box.Code} must not be negative."));
            }
        }
    }

    // Builds the slip from already-validated input (happy-path mapping).
    private static T5Slip BuildSlip(T5RawInput input)
    {
        var scalars = new Dictionary<string, string?>(StringComparer.Ordinal);
        var other = new List<T5OtherInformation>();

        foreach (var box in input.Boxes)
        {
            if (AmountBoxCodes.Contains(box.Code) || ScalarBoxCodes.Contains(box.Code))
            {
                scalars[box.Code] = box.Value;
            }
            else
            {
                other.Add(new T5OtherInformation(box.Code, ParseAmount(box.Value)));
            }
        }

        var currency = Scalar(scalars, T5BoxCodes.ForeignCurrency);
        var recipientId = Scalar(scalars, T5BoxCodes.RecipientIdentificationNumber);

        return new T5Slip
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
    }

    private static string? FirstValue(T5RawInput input, string code) =>
        input.Boxes.FirstOrDefault(b => string.Equals(b.Code, code, StringComparison.Ordinal))?.Value;

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
