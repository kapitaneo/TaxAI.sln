using System.Globalization;
using System.Text.Json.Serialization;

namespace CaseReview.Models;

/// <summary>
/// One immutable review unit for exactly one firm, case, participant, and tax
/// year. Construction is internal so only the fail-closed assembler can create
/// a package.
/// </summary>
public sealed partial class AccountantReviewPackage : IEquatable<AccountantReviewPackage>
{
    internal AccountantReviewPackage(
        string firmId,
        string caseId,
        string participantId,
        int taxYear,
        IReadOnlyList<ReviewedSlip> reviewedSlips,
        ReviewSignOff? signOff)
    {
        FirmId = firmId;
        CaseId = caseId;
        ParticipantId = participantId;
        TaxYear = taxYear;
        ReviewedSlips = Array.AsReadOnly(reviewedSlips.ToArray());
        SignOff = signOff;
    }

    [JsonIgnore]
    public string FirmId { get; }

    [JsonIgnore]
    public string CaseId { get; }

    [JsonIgnore]
    public string ParticipantId { get; }

    public int TaxYear { get; }

    [JsonIgnore]
    public IReadOnlyList<ReviewedSlip> ReviewedSlips { get; }

    [JsonIgnore]
    public ReviewSignOff? SignOff { get; }

    public bool Equals(AccountantReviewPackage? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
               string.Equals(FirmId, other.FirmId, StringComparison.Ordinal) &&
               string.Equals(CaseId, other.CaseId, StringComparison.Ordinal) &&
               string.Equals(
                   ParticipantId,
                   other.ParticipantId,
                   StringComparison.Ordinal) &&
               TaxYear == other.TaxYear &&
               ReviewedSlips.SequenceEqual(other.ReviewedSlips) &&
               (ReferenceEquals(SignOff, other.SignOff) ||
                (SignOff is not null && SignOff.Equals(other.SignOff)));
    }

    public override bool Equals(object? obj) => Equals(obj as AccountantReviewPackage);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FirmId, StringComparer.Ordinal);
        hash.Add(CaseId, StringComparer.Ordinal);
        hash.Add(ParticipantId, StringComparer.Ordinal);
        hash.Add(TaxYear);
        hash.Add(ReviewedSlips.Count);

        foreach (var reviewedSlip in ReviewedSlips)
        {
            hash.Add(reviewedSlip);
        }

        hash.Add(SignOff);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Fixed structural facts only; never attribution, reviewed content, a
    /// reviewer value, note, amount, box code, line number, or SIN.
    /// </summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "AccountantReviewPackage(TaxYear: {0}, ReviewedSlips: {1}, UnmappedBoxes: {2}, HasSignOff: {3})",
            TaxYear,
            ReviewedSlips.Count,
            ReviewedSlips.Sum(reviewedSlip => reviewedSlip.UnmappedBoxCodes.Count),
            SignOff is not null);
}
