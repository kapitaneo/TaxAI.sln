using TaxLogicEngine.Models;

namespace TaxCases.Models;

public sealed partial class TaxCase
{
    public ParseResult<CaseOperationOutcome> ConfirmChecklist(
        ChecklistDescriptor checklist,
        string? correlationReference) =>
        throw new NotImplementedException();

    public ParseResult<CaseOperationOutcome> AnswerQuestion(
        IntakeAnswerDescriptor answer,
        string? correlationReference) =>
        throw new NotImplementedException();
}
