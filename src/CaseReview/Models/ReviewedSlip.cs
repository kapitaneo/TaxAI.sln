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
        LineEntries = lineEntries;
        BoxAmounts = boxAmounts;
        UnmappedBoxCodes = unmappedBoxCodes;
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

    public bool Equals(ReviewedSlip? other) => throw new NotImplementedException();

    public override bool Equals(object? obj) => throw new NotImplementedException();

    public override int GetHashCode() => throw new NotImplementedException();

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
}
