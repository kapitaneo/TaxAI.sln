namespace TaxCases.Services;

public static class SupportedTaxYears
{
    private static readonly IReadOnlyCollection<int> Years = Array.AsReadOnly([2025]);

    public static IReadOnlyCollection<int> All => Years;

    public static bool IsSupported(int year) => year == 2025;
}
