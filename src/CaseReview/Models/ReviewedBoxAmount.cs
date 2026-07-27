using System.Text.Json.Serialization;

namespace CaseReview.Models;

/// <summary>
/// A value-for-value snapshot of one populated box amount emitted by the
/// engine's existing T4 slip flattener.
/// </summary>
public sealed class ReviewedBoxAmount : IEquatable<ReviewedBoxAmount>
{
    internal ReviewedBoxAmount(string boxCode, decimal amount)
    {
        BoxCode = boxCode;
        Amount = amount;
    }

    /// <summary>The engine-provided box code, readable only in memory.</summary>
    [JsonIgnore]
    public string BoxCode { get; }

    /// <summary>The already-produced engine amount, readable only in memory.</summary>
    [JsonIgnore]
    public decimal Amount { get; }

    public bool Equals(ReviewedBoxAmount? other) =>
        other is not null &&
        string.Equals(BoxCode, other.BoxCode, StringComparison.Ordinal) &&
        Amount == other.Amount;

    public override bool Equals(object? obj) => Equals(obj as ReviewedBoxAmount);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(BoxCode),
        Amount);

    /// <summary>Fixed type identity only; no box code or amount is rendered.</summary>
    public override string ToString() => nameof(ReviewedBoxAmount);
}
