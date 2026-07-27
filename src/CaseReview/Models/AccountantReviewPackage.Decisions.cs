using TaxLogicEngine.Models;

namespace CaseReview.Models;

public sealed partial class AccountantReviewPackage
{
    /// <summary>
    /// Records exactly one validated human decision on a replacement package.
    /// Wave 0 declares the signature; T05 implements the fail-closed behavior.
    /// </summary>
    public ParseResult<AccountantReviewPackage> RecordDecision(
        ReviewDecisionDescriptor? decision) =>
        throw new NotImplementedException();
}
