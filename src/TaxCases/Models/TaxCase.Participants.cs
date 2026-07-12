using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> AddParticipant(
        ParticipantDescriptor participant,
        string? correlationReference) =>
        throw new NotImplementedException();
}
