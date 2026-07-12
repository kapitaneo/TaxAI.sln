using System.Text;

namespace TaxCases.Services;

internal static class SafeDiagnosticText
{
    private const string MaskPrefix = "***-**-";

    internal static string? RedactSinLikeSequences(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var result = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length;)
        {
            if (TryReadFormatted(value, index, '-', out var formattedLastThree) ||
                TryReadFormatted(value, index, ' ', out formattedLastThree))
            {
                result.Append(MaskPrefix).Append(formattedLastThree);
                index += 11;
                continue;
            }

            if (IsAsciiDigit(value[index]))
            {
                var end = index;
                while (end < value.Length && IsAsciiDigit(value[end]))
                {
                    end++;
                }

                var length = end - index;
                if (length >= 9)
                {
                    result.Append(MaskPrefix).Append(value, end - 3, 3);
                }
                else
                {
                    result.Append(value, index, length);
                }

                index = end;
                continue;
            }

            result.Append(value[index]);
            index++;
        }

        return result.ToString();
    }

    private static bool TryReadFormatted(string value, int index, char separator, out string lastThree)
    {
        lastThree = string.Empty;
        if (index + 11 > value.Length)
        {
            return false;
        }

        if (!IsAsciiDigit(value[index]) ||
            !IsAsciiDigit(value[index + 1]) ||
            !IsAsciiDigit(value[index + 2]) ||
            value[index + 3] != separator ||
            !IsAsciiDigit(value[index + 4]) ||
            !IsAsciiDigit(value[index + 5]) ||
            !IsAsciiDigit(value[index + 6]) ||
            value[index + 7] != separator ||
            !IsAsciiDigit(value[index + 8]) ||
            !IsAsciiDigit(value[index + 9]) ||
            !IsAsciiDigit(value[index + 10]))
        {
            return false;
        }

        lastThree = value.Substring(index + 8, 3);
        return true;
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
}
