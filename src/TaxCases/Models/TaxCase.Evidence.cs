using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> AttachEvidence(
        EvidenceDescriptor evidence,
        string? correlationReference) =>
        throw new NotImplementedException();
}
