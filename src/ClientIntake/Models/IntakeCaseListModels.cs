using System.Text.Json.Serialization;

namespace ClientIntake.Models;

/// <summary>
/// One case as the firm-scoped list read returns it: the selector an existing
/// accountant route already takes, the primary taxpayer display name, and the
/// stored tax year. Nothing else about the case travels with it — no status, no
/// count, no instant, and no participant, document, or link identifier.
/// <para>
/// The instant the list is ordered by is deliberately absent. Ordering is the
/// store's concern, and a value that is not here cannot be rendered.
/// </para>
/// </summary>
public sealed record IntakeCaseListEntry(
    [property: JsonIgnore] Guid CaseId,
    [property: JsonIgnore] string PrimaryTaxpayerDisplayName,
    int TaxYear)
{
    public override string ToString() => nameof(IntakeCaseListEntry);
}

/// <summary>
/// The bounded, newest-first cases of exactly one trusted firm.
/// <see cref="HasMore"/> records only that the bound was reached; it is not a
/// count and says nothing about how many further cases exist.
/// </summary>
public sealed record IntakeCaseList(
    IReadOnlyList<IntakeCaseListEntry> Cases,
    bool HasMore)
{
    public IReadOnlyList<IntakeCaseListEntry> Cases { get; } =
        Array.AsReadOnly((Cases ?? throw new ArgumentNullException(nameof(Cases))).ToArray());

    public override string ToString() =>
        $"{nameof(IntakeCaseList)}(cases: {Cases.Count}, more: {HasMore})";
}
