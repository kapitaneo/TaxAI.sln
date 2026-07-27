namespace CaseReview.Services;

/// <summary>
/// The module-local tax-year policy. Spec 009 reviews exactly 2025, with no
/// current-year inference, adjacent-year fallback, clock read, or mapping
/// resource inspection.
/// </summary>
public static class SupportedReviewTaxYears
{
    /// <summary>Every supported review tax year: 2025 only.</summary>
    public static IReadOnlyList<int> All { get; } = new[] { 2025 };

    public static bool IsSupported(int taxYear) => All.Contains(taxYear);
}
