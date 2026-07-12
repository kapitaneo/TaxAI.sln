using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> TransitionTo(
        CaseStatus target,
        string? correlationReference) =>
        throw new NotImplementedException();

    public ParseResult<CaseOperationOutcome> Reopen(
        string? reason,
        string? correlationReference) =>
        throw new NotImplementedException();
}
