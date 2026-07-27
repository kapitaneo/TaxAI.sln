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
        ReviewedSlips = reviewedSlips;
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

    public bool Equals(AccountantReviewPackage? other) => throw new NotImplementedException();

    public override bool Equals(object? obj) => throw new NotImplementedException();

    public override int GetHashCode() => throw new NotImplementedException();

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
