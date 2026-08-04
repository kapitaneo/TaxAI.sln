namespace ExtractionWorkflow.Models;

/// <summary>
/// The fixed field names this module's own errors and field outcomes use.
/// <para>
/// Declared once as constants so a test can assert the closed set
/// mechanically, and so no error can ever be identified by a caller-supplied
/// or document-supplied string. A field name here names a *part of the
/// structure*, never a value found on a document.
/// </para>
/// <para>
/// Field keys produced by the existing engine are not repeated here. When a
/// T4 value is refused, the outcome carries the engine's own
/// <c>ValidationError.Field</c> verbatim so the mapping between an engine error
/// and a field outcome stays one-to-one; re-labelling it with a constant from
/// this set would break that correspondence.
/// </para>
/// </summary>
public static class ExtractionWorkflowFields
{
    /// <summary>The provider result as a whole.</summary>
    public const string Provider = "Provider";

    /// <summary>The provider result's extracted box collection.</summary>
    public const string PayloadBoxes = "PayloadBoxes";

    /// <summary>One entry within that collection.</summary>
    public const string PayloadBox = "PayloadBox";

    /// <summary>The box code carried by one entry.</summary>
    public const string PayloadBoxCode = "PayloadBoxCode";

    /// <summary>A stored, non-SIN box code.</summary>
    public const string BoxCode = "BoxCode";

    // The approved medical evidence fields, as structural outcome keys. Each
    // names a field position on a receipt, never its content.
    public const string MedicalServiceDate = "MedicalServiceDate";
    public const string MedicalGrossAmount = "MedicalGrossAmount";
    public const string MedicalCurrency = "MedicalCurrency";
    public const string MedicalProviderDisplayName = "MedicalProviderDisplayName";
    public const string MedicalPatientDisplayName = "MedicalPatientDisplayName";
    public const string MedicalReceiptReference = "MedicalReceiptReference";
}
