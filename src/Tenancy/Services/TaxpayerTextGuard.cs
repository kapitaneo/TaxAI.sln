using Tenancy.Models;
using TaxLogicEngine.Models;

namespace Tenancy.Services;

/// <summary>
/// Guards the two taxpayer-content strings this module stores — the taxpayer
/// display name and the employer display name.
/// <para>
/// An over-length value is refused, <strong>never truncated</strong>: a
/// truncated taxpayer name is a wrong record, and it is wrong silently.
/// </para>
/// <para>
/// A value containing nine consecutive digits is refused outright. No SIN in any
/// form may reach this database, and a nine-digit run arriving inside a display
/// name is precisely how one would — pasted into the wrong field upstream, then
/// stored as a "name" nobody scans.
/// </para>
/// </summary>
public static class TaxpayerTextGuard
{
    private const int ForbiddenConsecutiveDigitCount = 9;

    public static ValidationError? Validate(string? value, int maxLength, string field)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            return new ValidationError(field, TenancyErrors.TextTooLong);
        }

        var consecutiveDigitCount = 0;

        foreach (var character in value)
        {
            if (!char.IsDigit(character))
            {
                consecutiveDigitCount = 0;
                continue;
            }

            consecutiveDigitCount++;

            if (consecutiveDigitCount == ForbiddenConsecutiveDigitCount)
            {
                return new ValidationError(field, TenancyErrors.TextContainsDigitRun);
            }
        }

        return null;
    }
}
