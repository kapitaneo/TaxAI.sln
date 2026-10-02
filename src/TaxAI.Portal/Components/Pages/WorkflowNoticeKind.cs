namespace TaxAI.Portal.Components.Pages;

/// <summary>
/// The closed notice vocabulary for an in-scope accountant operation the
/// existing workflow refused (spec 017, FR-42 and FR-43).
/// <para>
/// Exactly two operations and two result codes have a notice. A member is
/// chosen by the endpoint from the operation and the workflow's closed result
/// code, and from nothing else: no request value, field name, box code, or
/// engine message can select one, so no caller text can be reflected.
/// </para>
/// </summary>
public enum WorkflowNoticeKind
{
    None = 0,
    CorrectionRefused = 1,
    CorrectionConflict = 2,
    FinalizationRefused = 3,
    FinalizationConflict = 4,
}
