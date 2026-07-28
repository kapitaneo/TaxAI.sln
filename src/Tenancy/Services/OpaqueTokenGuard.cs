using Tenancy.Models;
using TaxLogicEngine.Models;

namespace Tenancy.Services;

/// <summary>
/// Validates a caller-supplied identifier as an opaque token.
/// <para>
/// Case and participant identifiers arrive as caller strings. They are checked
/// for emptiness, a bounded length, and a restricted character set — and
/// <strong>nothing is trimmed, normalized, case-folded, or truncated to make a
/// value pass</strong>. A value quietly reshaped to fit is a value that no
/// longer identifies what the caller meant, and it would then be compared
/// ordinally against the original everywhere else.
/// </para>
/// </summary>
public static class OpaqueTokenGuard
{
    public static ValidationError? Validate(string? value, int maxLength, string field)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new ValidationError(field, TenancyErrors.TokenEmpty);
        }

        if (value.Length > maxLength)
        {
            return new ValidationError(field, TenancyErrors.TokenTooLong);
        }

        foreach (var character in value)
        {
            if (!IsPermitted(character))
            {
                return new ValidationError(field, TenancyErrors.TokenCharacterNotPermitted);
            }
        }

        return null;
    }

    private static bool IsPermitted(char character) =>
        (character >= 'A' && character <= 'Z') ||
        (character >= 'a' && character <= 'z') ||
        (character >= '0' && character <= '9') ||
        character is '-' or '_';
}
