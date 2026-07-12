using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public static ParseResult<CaseOperationOutcome> Create(CreateTaxCaseRequest request) =>
        throw new NotImplementedException();
}
