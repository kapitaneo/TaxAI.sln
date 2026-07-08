namespace TaxLogicEngine.Models;

/// <summary>
/// The T5 Box 22 recipient identification number (a SIN or a business number).
/// <para>
/// The raw value is held privately and is <b>never</b> emitted. Both
/// <see cref="ToString"/> and <see cref="Masked"/> reveal only the last three
/// digits, satisfying the CLAUDE.md SIN rule. Masking is unconditional — it does
/// not depend on recipient type — so there is no code path that can leak the full
/// value (a business number is masked too, which is accepted).
/// </para>
/// </summary>
public sealed class RecipientIdentifier : IEquatable<RecipientIdentifier>
{
    /// <summary>How many trailing digits remain visible when masked.</summary>
    private const int VisibleDigits = 3;

    /// <summary>Length of a Social Insurance Number, used to pick the masked layout.</summary>
    private const int SinLength = 9;

    // Separators stripped; used for masking, equality, and hashing. Never exposed.
    private readonly string _normalized;

    /// <summary>
    /// Creates an identifier from a raw Box 22 value. Whitespace and hyphens are
    /// stripped for normalization.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is null, empty, whitespace, or contains no
    /// alphanumeric characters.
    /// </exception>
    public RecipientIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Recipient identification number must not be empty.", nameof(value));
        }

        _normalized = Normalize(value);

        if (_normalized.Length == 0)
        {
            throw new ArgumentException(
                "Recipient identification number must contain at least one alphanumeric character.",
                nameof(value));
        }
    }

    /// <summary>
    /// The value with everything but the last three digits masked. A standard
    /// nine-digit SIN is rendered as <c>***-**-789</c>; other lengths mask all but
    /// the final three characters. A value of three characters or fewer is masked
    /// completely (revealing "the last three" would reveal all of it).
    /// </summary>
    public string Masked
    {
        get
        {
            if (_normalized.Length <= VisibleDigits)
            {
                return new string('*', _normalized.Length);
            }

            var last3 = _normalized[^VisibleDigits..];

            if (_normalized.Length == SinLength && _normalized.All(char.IsDigit))
            {
                return $"***-**-{last3}";
            }

            return new string('*', _normalized.Length - VisibleDigits) + last3;
        }
    }

    /// <summary>Returns <see cref="Masked"/> — never the full value.</summary>
    public override string ToString() => Masked;

    public bool Equals(RecipientIdentifier? other) =>
        other is not null && _normalized == other._normalized;

    public override bool Equals(object? obj) => Equals(obj as RecipientIdentifier);

    public override int GetHashCode() => _normalized.GetHashCode(StringComparison.Ordinal);

    private static string Normalize(string value) =>
        new(value.Where(c => c is not (' ' or '-' or '\t')).ToArray());
}
