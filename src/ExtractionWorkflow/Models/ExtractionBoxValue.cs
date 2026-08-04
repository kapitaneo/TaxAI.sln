using System.Globalization;
using System.Text.Json.Serialization;
using TaxLogicEngine.Models;

namespace ExtractionWorkflow.Models;

/// <summary>
/// One extracted box code and its printed value, safe to keep.
/// <para>
/// <b>This type refuses Box 12 at construction.</b> The highest-risk defect in
/// this feature is a loop that writes every extracted box to a store: one such
/// loop would persist a social insurance number. Making the exclusion a type
/// invariant rather than a filter means the rule holds at every call site that
/// exists today and at every call site written later, including ones nobody
/// reviewed with this in mind. There is no constructor, factory, overload, or
/// setter that produces an instance carrying the SIN box.
/// </para>
/// <para>
/// The comparison normalizes — trimmed and case-insensitive — while the stored
/// code keeps whatever the document showed. Normalization here can only ever
/// cause a <i>refusal</i>, so a padded or oddly-cased spelling of the SIN box
/// cannot slip past, and no accepted value is silently rewritten.
/// </para>
/// <para>
/// Both members are omitted from serialization and from
/// <see cref="ToString"/>. A box value is a taxpayer amount and a box code is
/// copied from an untrusted document, so neither belongs in a diagnostic.
/// </para>
/// </summary>
public sealed class ExtractionBoxValue
{
    private ExtractionBoxValue(string code, string? value)
    {
        Code = code;
        Value = value;
    }

    /// <summary>The box code exactly as extracted. Never <c>T4BoxCodes.SocialInsuranceNumber</c>.</summary>
    [JsonIgnore]
    public string Code { get; }

    /// <summary>The printed value exactly as extracted, or null when the box was empty.</summary>
    [JsonIgnore]
    public string? Value { get; }

    /// <summary>
    /// Creates a box value, or refuses. Returns false — never a repaired
    /// instance — for an absent or blank code and for the SIN box in any
    /// spelling. Refusing rather than throwing keeps untrusted provider output
    /// from reaching a caller as an exception.
    /// </summary>
    public static bool TryCreate(string? code, string? value, out ExtractionBoxValue? boxValue)
    {
        boxValue = null;

        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        if (IsSocialInsuranceNumberBox(code))
        {
            return false;
        }

        boxValue = new ExtractionBoxValue(code, value);
        return true;
    }

    /// <summary>
    /// Whether a candidate code names the box this type refuses. Exposed so a
    /// caller can skip that box before it tries to keep it, rather than
    /// discovering the refusal afterwards.
    /// </summary>
    public static bool IsSocialInsuranceNumberBox(string? code) =>
        code is not null &&
        string.Equals(
            code.Trim(),
            T4BoxCodes.SocialInsuranceNumber,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Fixed structural facts only — never the code and never the value.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionBoxValue(HasValue: {0})",
            Value is not null);
}
