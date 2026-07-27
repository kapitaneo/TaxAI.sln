using CaseProcessing.Models;
using CaseReview.Models;
using TaxLogicEngine.Models;

namespace CaseReview.Services;

/// <summary>
/// The deterministic package-assembly boundary. Wave 0 declares the signature;
/// T04 implements the fail-closed pipeline.
/// </summary>
public static class ReviewPackageAssembler
{
    public static ParseResult<AccountantReviewPackage> Assemble(
        IReadOnlyList<ProcessedT4Extraction?>? extractions) =>
        throw new NotImplementedException();
}
