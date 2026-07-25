namespace DocumentExtraction.Services;

/// <summary>
/// The module-local tax-year policy: spec 007 supports exactly 2025. A new
/// supported year is a code change to exactly this one place — a scope change,
/// never a silent widening. There is no "current" year, no adjacent-year
/// fallback, no system-clock read, and deliberately no inspection of
/// TaxLogicEngine mapping resources to infer support.
/// </summary>
public static class SupportedExtractionTaxYears
{
    /// <summary>Every supported tax year. Spec 007: 2025 only.</summary>
    public static IReadOnlyList<int> All { get; } = new[] { 2025 };

    /// <summary>True only when <paramref name="taxYear"/> is a supported year.</summary>
    public static bool IsSupported(int taxYear) => All.Contains(taxYear);
}
