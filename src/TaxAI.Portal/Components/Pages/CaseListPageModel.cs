using System.Text.Json.Serialization;

namespace TaxAI.Portal.Components.Pages;

/// <summary>
/// The case-list browser model. <see cref="HasMore"/> selects one fixed notice;
/// no count of cases is carried or rendered.
/// </summary>
public sealed record CaseListPageModel(
    IReadOnlyList<CaseListRowModel> Cases,
    bool HasMore);

/// <summary>
/// The exact case-list allowlist: the primary taxpayer display name, the stored
/// tax year, and the opaque case selector, which appears only inside link
/// targets to existing accountant routes.
/// </summary>
public sealed record CaseListRowModel(
    [property: JsonIgnore] Guid CaseId,
    string PrimaryTaxpayerDisplayName,
    int TaxYear);
