using System.Globalization;
using System.Text.Json.Serialization;
using TaxLogicEngine.Models;

namespace CaseReview.Models;

/// <summary>
/// One processed T4 as presented for review. It carries the exact parsed slip
/// reference and ordered snapshots of the engine output without recomputation.
/// </summary>
public sealed class ReviewedSlip : IEquatable<ReviewedSlip>
{
    internal ReviewedSlip(
        string documentId,
        int taxYear,
        string sourceReference,
        T4Slip slip,
        IReadOnlyList<TaxLineEntry> lineEntries,
        IReadOnlyList<ReviewedBoxAmount> boxAmounts,
        IReadOnlyList<string> unmappedBoxCodes)
    {
        DocumentId = documentId;
        TaxYear = taxYear;
        SourceReference = sourceReference;
        Slip = slip;
        LineEntries = Array.AsReadOnly(
            lineEntries
                .Select(entry => new TaxLineEntry(
                    entry.LineNumber,
                    entry.Amount,
                    Array.AsReadOnly(entry.SourceBoxCodes.ToArray())))
                .ToArray());
        BoxAmounts = Array.AsReadOnly(
            boxAmounts
                .Select(amount => new ReviewedBoxAmount(amount.BoxCode, amount.Amount))
                .ToArray());
        UnmappedBoxCodes = Array.AsReadOnly(unmappedBoxCodes.ToArray());
    }

    [JsonIgnore]
    public string DocumentId { get; }

    public int TaxYear { get; }

    [JsonIgnore]
    public string SourceReference { get; }

    [JsonIgnore]
    public T4Slip Slip { get; }

    [JsonIgnore]
    public IReadOnlyList<TaxLineEntry> LineEntries { get; }

    [JsonIgnore]
    public IReadOnlyList<ReviewedBoxAmount> BoxAmounts { get; }

    [JsonIgnore]
    public IReadOnlyList<string> UnmappedBoxCodes { get; }

    public bool Equals(ReviewedSlip? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
               string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal) &&
               TaxYear == other.TaxYear &&
               string.Equals(SourceReference, other.SourceReference, StringComparison.Ordinal) &&
               ReferenceEquals(Slip, other.Slip) &&
               LineEntriesEqual(LineEntries, other.LineEntries) &&
               BoxAmounts.SequenceEqual(other.BoxAmounts) &&
               UnmappedBoxCodes.SequenceEqual(
                   other.UnmappedBoxCodes,
                   StringComparer.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as ReviewedSlip);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId, StringComparer.Ordinal);
        hash.Add(TaxYear);
        hash.Add(SourceReference, StringComparer.Ordinal);
        hash.Add(Slip, ReferenceEqualityComparer.Instance);
        hash.Add(LineEntries.Count);

        foreach (var entry in LineEntries)
        {
            hash.Add(entry.LineNumber, StringComparer.Ordinal);
            hash.Add(entry.Amount);
            hash.Add(entry.SourceBoxCodes.Count);

            foreach (var sourceBoxCode in entry.SourceBoxCodes)
            {
                hash.Add(sourceBoxCode, StringComparer.Ordinal);
            }
        }

        hash.Add(BoxAmounts.Count);
        foreach (var amount in BoxAmounts)
        {
            hash.Add(amount);
        }

        hash.Add(UnmappedBoxCodes.Count);
        foreach (var unmappedBoxCode in UnmappedBoxCodes)
        {
            hash.Add(unmappedBoxCode, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Fixed structural facts only; never an identifier, box code, line number,
    /// amount, identity string, or SIN.
    /// </summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ReviewedSlip(TaxYear: {0}, LineEntries: {1}, BoxAmounts: {2}, UnmappedBoxes: {3}, HasSin: {4})",
            TaxYear,
            LineEntries.Count,
            BoxAmounts.Count,
            UnmappedBoxCodes.Count,
            Slip.Sin is not null);

    private static bool LineEntriesEqual(
        IReadOnlyList<TaxLineEntry> left,
        IReadOnlyList<TaxLineEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftEntry = left[index];
            var rightEntry = right[index];

            if (!string.Equals(
                    leftEntry.LineNumber,
                    rightEntry.LineNumber,
                    StringComparison.Ordinal) ||
                leftEntry.Amount != rightEntry.Amount ||
                !leftEntry.SourceBoxCodes.SequenceEqual(
                    rightEntry.SourceBoxCodes,
                    StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
