using TaxLogicEngine.Models;

namespace Tenancy.Services;

/// <summary>
/// Validates a caller-supplied identifier as an opaque token.
/// <para>
/// Case and participant identifiers arrive as caller strings. They are checked
/// for emptiness, a bounded length, and a restricted character set — and
/// <strong>nothing is trimmed, normalized, case-folded, or truncated to make a
/// value pass</strong>. A value quietly reshaped to fit is a value that no
/// longer identifies what the caller meant, and it would then be compared
/// ordinally against the original everywhere else.
/// </para>
/// <para>Wave 0 contract stage: behaviour is implemented in a later task.</para>
/// </summary>
public static class OpaqueTokenGuard
{
    public static ValidationError? Validate(string? value, int maxLength, string field) =>
        throw new NotImplementedException();
}
