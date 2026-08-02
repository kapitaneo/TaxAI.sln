using System.Text.RegularExpressions;
using Tenancy.Models;
using Tenancy.Services;

namespace ClientIntake.Services;

internal static partial class IntakeDisplayNameGuard
{
    internal static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (TaxpayerTextGuard.Validate(
                value,
                PersistedSchema.DisplayNameMaxLength,
                nameof(value)) is not null)
        {
            return false;
        }

        return !StructuredIdentifierPattern().IsMatch(value);
    }

    [GeneratedRegex(
        @"(?<![0-9A-Za-z])(?:[0-9xX*]{3}[- ]?){2}[0-9xX*]{3}(?![0-9A-Za-z])",
        RegexOptions.CultureInvariant)]
    private static partial Regex StructuredIdentifierPattern();
}
