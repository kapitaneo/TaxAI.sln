namespace Tenancy.Models;

/// <summary>
/// A firm identifier the server established for itself — never one a caller
/// supplied.
/// <para>
/// It exists as a type rather than a bare <see cref="Guid"/> so that "this value
/// was trusted" is checkable by the compiler at every repository boundary. A
/// method that demands a <c>TrustedFirmId</c> cannot be handed a route value, a
/// query value, a header, or a form field without someone writing an explicit,
/// visible conversion.
/// </para>
/// <para>
/// The underlying value is a GUID so tenant comparison can never depend on a
/// database collation, and parsing is strict: nothing is trimmed, normalized, or
/// case-folded to make a candidate pass.
/// </para>
/// <para>
/// The canonical representation is the lower-case, hyphenated <c>D</c> format.
/// Candidates must already equal that representation ordinally.
/// </para>
/// </summary>
public sealed class TrustedFirmId : IEquatable<TrustedFirmId>
{
    private TrustedFirmId(Guid value) => Value = value;

    /// <summary>The canonical firm identifier.</summary>
    public Guid Value { get; }

    /// <summary>Wraps a firm identifier the server itself resolved.</summary>
    public static TrustedFirmId FromGuid(Guid value) => new(value);

    /// <summary>
    /// Parses a canonical firm identifier strictly. Returns false — never a
    /// coerced value — for anything that is not already canonical.
    /// </summary>
    public static bool TryParse(string? candidate, out TrustedFirmId? firmId)
    {
        firmId = null;

        if (candidate is null ||
            !Guid.TryParseExact(candidate, "D", out var value) ||
            !string.Equals(candidate, value.ToString("D"), StringComparison.Ordinal))
        {
            return false;
        }

        firmId = new TrustedFirmId(value);
        return true;
    }

    /// <summary>The exact canonical string form, for the application-issued claim.</summary>
    public string ToCanonicalString() => Value.ToString("D");

    public bool Equals(TrustedFirmId? other) =>
        other is not null && Value.Equals(other.Value);

    public override bool Equals(object? obj) =>
        obj is TrustedFirmId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Fixed type identity only; the firm identifier is never rendered.</summary>
    public override string ToString() => nameof(TrustedFirmId);
}
