namespace TaxCases.Models;

public sealed record CaseOperationOutcome(TaxCase Case, CaseChangeRecord Change)
{
    public override string ToString() =>
        $"CaseOperationOutcome {{ CaseStatus = {Case.Status}, ChangeKind = {Change.Kind} }}";
}
