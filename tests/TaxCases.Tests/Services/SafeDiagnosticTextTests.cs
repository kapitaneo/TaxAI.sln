using System.Reflection;
using TaxCases.Services;

namespace TaxCases.Tests.Services;

public sealed class SafeDiagnosticTextTests
{
    public static TheoryData<string?, string?> RedactionCases => new()
    {
        { null, null },
        { string.Empty, string.Empty },
        { "no sensitive value", "no sensitive value" },
        { "123456789", "***-**-789" },
        { "123-456-789", "***-**-789" },
        { "123 456 789", "***-**-789" },
        { "0123456789", "***-**-789" },
        { "a123456789b987-654-321c111 222 333d", "a***-**-789b***-**-321c***-**-333d" },
        { "١٢٣٤٥٦٧٨٩", "١٢٣٤٥٦٧٨٩" }
    };

    [Theory]
    [MemberData(nameof(RedactionCases))]
    public void RedactSinLikeSequences_IsDeterministicAndAsciiOrdinal(string? input, string? expected)
    {
        Assert.Equal(expected, Redact(input));
        Assert.Equal(expected, Redact(input));
    }

    private static string? Redact(string? value)
    {
        var helper = typeof(SupportedTaxYears).Assembly.GetType(
            "TaxCases.Services.SafeDiagnosticText",
            throwOnError: true)!;
        var method = helper.GetMethod(
            "RedactSinLikeSequences",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        return (string?)method.Invoke(null, [value]);
    }
}
