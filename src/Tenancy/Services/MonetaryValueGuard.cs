using System.Globalization;
using TaxLogicEngine.Models;
using Tenancy.Models;

namespace Tenancy.Services;

/// <summary>
/// Refuses any amount the store could not hold exactly.
/// <para>
/// The storage layer must be provably incapable of altering a number. The engine
/// already rounded half-up to cents where rounding was a tax decision; anything
/// this module did to an amount afterwards would be an <em>undocumented second
/// rounding</em>, and the accountant would sign off on a figure the engine never
/// produced.
/// </para>
/// <para>
/// So the rule is refusal, not adjustment: an amount that would not survive a
/// store-and-reload round trip under the declared precision and scale is
/// rejected and nothing is written. It is never rounded, truncated, or
/// re-scaled.
/// </para>
/// <para>
/// The check reads the value's invariant string form. It performs
/// <strong>no arithmetic on the amount</strong> — arithmetic on a monetary value
/// is the thing this module must not be able to do at all.
/// </para>
/// </summary>
public static class MonetaryValueGuard
{
    private const int MaximumWholeNumberDigits =
        PersistedSchema.MonetaryPrecision - PersistedSchema.MonetaryScale;

    public static ValidationError? Validate(decimal amount, string field)
    {
        var invariantValue = amount.ToString(CultureInfo.InvariantCulture);
        var wholeNumberDigits = 0;
        var fractionalDigits = 0;
        var afterDecimalSeparator = false;

        foreach (var character in invariantValue)
        {
            if (character == '-')
            {
                continue;
            }

            if (character == '.')
            {
                afterDecimalSeparator = true;
                continue;
            }

            if (afterDecimalSeparator)
            {
                fractionalDigits++;
            }
            else
            {
                wholeNumberDigits++;
            }
        }

        return wholeNumberDigits <= MaximumWholeNumberDigits &&
               fractionalDigits <= PersistedSchema.MonetaryScale
            ? null
            : new ValidationError(field, TenancyErrors.AmountNotExactlyStorable);
    }
}
