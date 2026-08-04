using System.Globalization;
using System.Text.Json.Serialization;

namespace ExtractionWorkflow.Models;

/// <summary>
/// The workflow's closed outcome vocabulary. It carries no message, no
/// identifier, and no field name — a failure code a caller can read is a code
/// that cannot become an oracle.
/// <para>
/// <see cref="NotFound"/> is deliberately the answer for a case, document, or
/// content key that does not exist <i>and</i> for one that belongs to another
/// firm. Nothing distinguishes them, so a caller cannot learn that a resource
/// exists by being refused differently.
/// </para>
/// </summary>
public enum ExtractionResultCode
{
    Success = 0,
    Invalid = 1,
    NotFound = 2,
    Conflict = 3
}

/// <summary>
/// The workflow's closed outcome. It carries a code and, on success, a value —
/// never a message, a field name, an identifier, or an exception.
/// <para>
/// Modelled on the intake workflow's result for the same reason: a failure that
/// explains itself to the caller explains itself to an attacker too.
/// </para>
/// </summary>
public sealed class ExtractionResult<TValue>
{
    private ExtractionResult(ExtractionResultCode code, TValue? value)
    {
        Code = code;
        Value = value;
    }

    public ExtractionResultCode Code { get; }

    /// <summary>The projected value on success. Never rendered by diagnostics.</summary>
    [JsonIgnore]
    public TValue? Value { get; }

    public bool IsSuccess => Code == ExtractionResultCode.Success;

    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static ExtractionResult<TValue> Success(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ExtractionResult<TValue>(ExtractionResultCode.Success, value);
    }

    /// <summary>
    /// Creates a failure. <see cref="ExtractionResultCode.Success"/> is refused:
    /// a "failure" carrying the success code and no value would be a null
    /// success everywhere downstream.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The code is undefined or is the success code.</exception>
    public static ExtractionResult<TValue> Failure(ExtractionResultCode code)
    {
        if (!Enum.IsDefined(code) || code == ExtractionResultCode.Success)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        return new ExtractionResult<TValue>(code, default);
    }

    /// <summary>The code only; a successful value is never rendered.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "ExtractionResult({0})",
            Code);
}

/// <summary>
/// Names the one stored document an accountant asked to extract.
/// <para>
/// <b>There is no firm member, and there is no content-key member.</b> The firm
/// comes from the request's already-revalidated authorization state and arrives
/// as an explicit <c>TrustedFirmId</c> argument; the opaque content key is read
/// from the server-stored case, never accepted from a caller. The case and
/// document identifiers are resource selectors, verified against server-stored
/// state before anything is read — a selector that names another firm's document
/// is indistinguishable from one that names nothing.
/// </para>
/// <para>
/// The tax year is likewise absent by construction. It is the server-stored
/// <c>StoredIntakeCase.TaxYear</c> and nothing else, so a route value, query
/// string, form field, header, or cookie has nowhere to put one.
/// </para>
/// </summary>
public sealed class ExtractDocumentCommand
{
    /// <summary>The case selector, verified against the trusted firm's stored cases.</summary>
    [JsonIgnore]
    public Guid CaseId { get; init; }

    /// <summary>The document selector, verified against that same stored case.</summary>
    [JsonIgnore]
    public Guid DocumentId { get; init; }

    /// <summary>Fixed type identity only; a selector is never rendered.</summary>
    public override string ToString() => nameof(ExtractDocumentCommand);
}

/// <summary>
/// The configuration the ingest workflow refuses to start without.
/// <para>
/// No member has a default. A missing or invalid value refuses at composition
/// time rather than silently selecting one, because a bound nobody chose is a
/// bound nobody reviewed.
/// </para>
/// </summary>
public sealed class ExtractionWorkflowOptions
{
    /// <summary>
    /// The bound on one document-intelligence call.
    /// <para>
    /// It is a fail-closed bound, never a quality signal: reaching it produces
    /// the same structural refusal as a provider that returned nothing, and
    /// nothing partial is stored. It is emphatically not a confidence bound —
    /// no confidence bound exists anywhere in this feature.
    /// </para>
    /// </summary>
    public TimeSpan ProviderCallLimit { get; init; }

    /// <summary>Fixed type identity only.</summary>
    public override string ToString() => nameof(ExtractionWorkflowOptions);
}
