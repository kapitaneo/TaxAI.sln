using System.Text.Json.Serialization;

namespace TaxAI.Portal.Components.Pages;

/// <summary>
/// The exact closed model the review component may render.
/// <para>
/// This replaces spec 012's private nested DTOs without widening them. The
/// allowlist is the type: persistence identifiers, tenant and session values,
/// participant and document references, retention facts, and non-display
/// timestamps are omitted <b>by construction</b>, so a field cannot leak by
/// somebody forgetting to exclude it — it has to be added on purpose, in a diff.
/// </para>
/// <para>
/// Amounts arrive as <c>string</c>, already formatted invariantly by the
/// endpoint from the stored value. That is deliberate: a numeric type here would
/// invite the component to format, round, or total it, and the portal computes no
/// tax. There is no <c>decimal</c> in this file and no arithmetic anywhere on the
/// path to the browser.
/// </para>
/// <para>
/// Every property is <c>[JsonIgnore]</c>-marked and every <c>ToString()</c>
/// reports structural counts only. Nothing serializes these models today; the
/// omissions exist so that a future diagnostic sink or JSON surface cannot make
/// taxpayer data serializable by accident.
/// </para>
/// </summary>
public sealed record ReviewPageModel(
    [property: JsonIgnore] int Year,
    [property: JsonIgnore] int Version,
    IReadOnlyList<ReviewSlipModel> Slips)
{
    /// <summary>
    /// A read-only snapshot. The endpoint projected this from an immutable stored
    /// version; copying keeps it immutable across the render.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ReviewSlipModel> Slips { get; } =
        Array.AsReadOnly((Slips ?? throw new ArgumentNullException(nameof(Slips))).ToArray());

    /// <summary>Structural only: no year, version, or slip content.</summary>
    public override string ToString() =>
        $"{nameof(ReviewPageModel)}(slips: {Slips.Count})";
}

public sealed record ReviewSlipModel(
    [property: JsonIgnore] string Type,
    [property: JsonIgnore] string Taxpayer,
    [property: JsonIgnore] string Employer,
    IReadOnlyList<ReviewBoxModel> Boxes,
    IReadOnlyList<ReviewLineModel> Lines,
    IReadOnlyList<string> UnmappedCodes)
{
    [JsonIgnore]
    public IReadOnlyList<ReviewBoxModel> Boxes { get; } =
        Array.AsReadOnly((Boxes ?? throw new ArgumentNullException(nameof(Boxes))).ToArray());

    [JsonIgnore]
    public IReadOnlyList<ReviewLineModel> Lines { get; } =
        Array.AsReadOnly((Lines ?? throw new ArgumentNullException(nameof(Lines))).ToArray());

    [JsonIgnore]
    public IReadOnlyList<string> UnmappedCodes { get; } =
        Array.AsReadOnly((UnmappedCodes ?? throw new ArgumentNullException(nameof(UnmappedCodes))).ToArray());

    public override string ToString() =>
        $"{nameof(ReviewSlipModel)}(boxes: {Boxes.Count}, lines: {Lines.Count}, "
        + $"unmapped: {UnmappedCodes.Count})";
}

public sealed record ReviewBoxModel(
    [property: JsonIgnore] string Code,
    [property: JsonIgnore] string Amount)
{
    public override string ToString() => nameof(ReviewBoxModel);
}

public sealed record ReviewLineModel(
    [property: JsonIgnore] string Code,
    [property: JsonIgnore] string Amount,
    IReadOnlyList<string> SourceBoxes)
{
    [JsonIgnore]
    public IReadOnlyList<string> SourceBoxes { get; } =
        Array.AsReadOnly((SourceBoxes ?? throw new ArgumentNullException(nameof(SourceBoxes))).ToArray());

    public override string ToString() =>
        $"{nameof(ReviewLineModel)}(sources: {SourceBoxes.Count})";
}
