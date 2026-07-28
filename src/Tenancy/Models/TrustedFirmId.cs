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
/// Wave 0 contract stage: the shape is declared; the strict parse and the
/// ordinal equality are implemented in a later task.
/// </para>
/// </summary>
public sealed class TrustedFirmId : IEquatable<TrustedFirmId>
{
    private TrustedFirmId(Guid value) => Value = value;

    /// <summary>The canonical firm identifier.</summary>
    public Guid Value { get; }

    /// <summary>Wraps a firm identifier the server itself resolved.</summary>
    public static TrustedFirmId FromGuid(Guid value) => throw new NotImplementedException();

    /// <summary>
    /// Parses a canonical firm identifier strictly. Returns false — never a
    /// coerced value — for anything that is not already canonical.
    /// </summary>
    public static bool TryParse(string? candidate, out TrustedFirmId? firmId) =>
        throw new NotImplementedException();

    /// <summary>The exact canonical string form, for the application-issued claim.</summary>
    public string ToCanonicalString() => throw new NotImplementedException();

    public bool Equals(TrustedFirmId? other) => throw new NotImplementedException();

    public override bool Equals(object? obj) => throw new NotImplementedException();

    public override int GetHashCode() => throw new NotImplementedException();

    /// <summary>Fixed type identity only; the firm identifier is never rendered.</summary>
    public override string ToString() => throw new NotImplementedException();
}
