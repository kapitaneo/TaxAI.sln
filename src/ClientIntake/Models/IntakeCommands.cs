using System.Security.Cryptography;
using System.Text.Json.Serialization;
using TaxCases.Models;

namespace ClientIntake.Models;

/// <summary>
/// The untrusted inputs, the workflow configuration, the two token primitives,
/// and the closed result wrapper.
/// <para>
/// Every command property is nullable on purpose. These records are bound from a
/// browser form, so "absent" and "not a number" have to be representable
/// <i>before</i> validation rather than silently defaulted to zero — a missing
/// expected-document count that arrives as <c>0</c> is indistinguishable from a
/// deliberate zero, and one of those is a validation failure.
/// </para>
/// <para>
/// No command carries a firm. Accountant scope is resolved server-side from the
/// request's authorization state and passed as a <c>TrustedFirmId</c>; a firm on
/// a request DTO is a tenant boundary a caller can edit.
/// </para>
/// </summary>
public sealed record CreateIntakeCaseCommand(
    [property: JsonIgnore] string? PrimaryTaxpayerDisplayName,
    int? TaxYear,
    int? ExpectedT4Count,
    int? ExpectedMedicalExpenseCount)
{
    /// <summary>Structural only: the display name is never rendered here.</summary>
    public override string ToString() =>
        $"{nameof(CreateIntakeCaseCommand)}(name: {(PrimaryTaxpayerDisplayName is null ? "absent" : "present")})";
}

public sealed record IntakeAnswerInput(
    DocumentCategory? Category,
    AvailabilityAnswerCode? Answer)
{
    public override string ToString() =>
        $"{nameof(IntakeAnswerInput)}({Category?.ToString() ?? "absent"}, {Answer?.ToString() ?? "absent"})";
}

public sealed record SaveIntakeAnswersCommand(
    IReadOnlyList<IntakeAnswerInput>? Answers)
{
    /// <summary>
    /// Copied on the way in. A caller that keeps its own reference to the list
    /// must not be able to change what the workflow validated after validation.
    /// </summary>
    public IReadOnlyList<IntakeAnswerInput>? Answers { get; } =
        Answers is null ? null : Array.AsReadOnly(Answers.ToArray());

    public override string ToString() =>
        $"{nameof(SaveIntakeAnswersCommand)}(answers: {Answers?.Count.ToString() ?? "absent"})";
}

public sealed record IntakeUploadCommand(
    DocumentCategory? Category,
    int? Ordinal,
    IntakeMediaKind? DeclaredMediaKind,
    long? DeclaredLength)
{
    /// <summary>
    /// The declared media kind and length are presentation hints only. The
    /// endpoint independently verifies the content signature and the streamed
    /// byte count; neither declared value is trusted.
    /// </summary>
    public override string ToString() =>
        $"{nameof(IntakeUploadCommand)}({Category?.ToString() ?? "absent"}, "
        + $"{DeclaredMediaKind?.ToString() ?? "absent"})";
}

/// <summary>
/// The workflow's required settings. There is no compiled fallback for any of
/// them: a missing or invalid lifetime or limit fails startup rather than
/// silently selecting a default that nobody approved.
/// </summary>
public sealed record IntakeWorkflowOptions(
    TimeSpan LinkLifetime,
    TimeSpan ClientSessionIdleLifetime,
    long MaximumFileBytes,
    int MaximumFilesPerRequest)
{
    public override string ToString() => nameof(IntakeWorkflowOptions);
}

/// <summary>
/// A high-entropy bootstrap or session credential, in memory only.
/// <para>
/// This is the one value in the workflow that is genuinely secret, so the type
/// makes leaking it awkward: <see cref="ToString"/> is a fixed literal, the
/// property is never serialized, and the raw text is reachable only through an
/// explicitly named <see cref="Reveal"/> call that shows up in a diff.
/// </para>
/// </summary>
public sealed class IntakeSecret
{
    /// <summary>Exactly 32 bytes, unpadded base64url — 43 characters.</summary>
    private const int EncodedLength = 43;

    private const int DecodedLength = 32;

    private readonly string _canonical;

    private IntakeSecret(string canonical) => _canonical = canonical;

    /// <summary>
    /// Parses the canonical unpadded base64url encoding of exactly 32 bytes.
    /// <para>
    /// Strict by construction. Nothing is trimmed, padded, case-folded, or
    /// normalized, and a non-canonical encoding of the right bytes is still
    /// refused — two spellings of one credential would mean two digests for one
    /// secret. Whitespace is rejected before decoding, because the framework's
    /// base64 decoder would otherwise skip it.
    /// </para>
    /// </summary>
    public static bool TryParse(string? candidate, out IntakeSecret? value)
    {
        value = null;

        if (candidate is null || candidate.Length != EncodedLength)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!IsBase64UrlCharacter(character))
            {
                return false;
            }
        }

        var padded = string.Concat(
            candidate.Replace('-', '+').Replace('_', '/'),
            "=");

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length != DecodedLength)
        {
            return false;
        }

        // Re-encode and compare. This is what rejects a non-canonical final
        // character whose unused trailing bits are not zero.
        if (!string.Equals(Encode(decoded), candidate, StringComparison.Ordinal))
        {
            return false;
        }

        value = new IntakeSecret(candidate);
        return true;
    }

    /// <summary>
    /// Creates a credential from 32 cryptographically random bytes. The only
    /// construction path other than strict parsing.
    /// </summary>
    internal static IntakeSecret FromRandomBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != DecodedLength)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        return new IntakeSecret(Encode(bytes));
    }

    /// <summary>
    /// The raw canonical credential. Named to be conspicuous: every call site is
    /// a place a secret leaves the type, and there are deliberately few.
    /// <para>
    /// [ASSUMED] The architecture writes this member as
    /// <c>[JsonIgnore] public string Reveal()</c>, but
    /// <c>JsonIgnoreAttribute</c> targets only properties and fields, so that
    /// spelling does not compile. It is declared as a plain method instead: a
    /// method is not a serializable member, so the credential is omitted from
    /// JSON by construction rather than by attribute. One-pass human override.
    /// </para>
    /// </summary>
    public string Reveal() => _canonical;

    /// <summary>Fixed type identity. The credential is never rendered.</summary>
    public override string ToString() => nameof(IntakeSecret);

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool IsBase64UrlCharacter(char character) =>
        character is >= 'A' and <= 'Z' ||
        character is >= 'a' and <= 'z' ||
        character is >= '0' and <= '9' ||
        character is '-' or '_';
}

/// <summary>
/// The keyed digest of a credential — the only form that is ever stored or
/// looked up.
/// <para>
/// Equality is constant-time. A digest lookup compares an attacker-supplied
/// value against a stored one, and an early-exit comparison over a keyed digest
/// is a timing oracle.
/// </para>
/// </summary>
public sealed record IntakeTokenDigest(IReadOnlyList<byte> Bytes)
{
    private const int RequiredLength = 32;

    private readonly byte[] _bytes = Copy(Bytes);

    /// <summary>
    /// A read-only snapshot. The constructor copied the caller's array, so a
    /// caller holding the original cannot mutate a digest after it was stored.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<byte> Bytes => Array.AsReadOnly(_bytes);

    public bool Equals(IntakeTokenDigest? other) =>
        other is not null &&
        CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_bytes);
        return hash.ToHashCode();
    }

    /// <summary>Fixed type identity. The digest bytes are never rendered.</summary>
    public override string ToString() => nameof(IntakeTokenDigest);

    private static byte[] Copy(IReadOnlyList<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Count != RequiredLength)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        return bytes.ToArray();
    }
}

/// <summary>
/// The workflow's closed outcome. It carries a code and, on success, a value —
/// never a message, a field name, an identifier, or an exception.
/// <para>
/// A failure that explains itself to the caller explains itself to an attacker
/// too. "Not found" and "belongs to another firm" are deliberately the same
/// code.
/// </para>
/// </summary>
public sealed class IntakeResult<T>
{
    private IntakeResult(IntakeResultCode code, T? value)
    {
        Code = code;
        Value = value;
    }

    public IntakeResultCode Code { get; }

    [JsonIgnore]
    public T? Value { get; }

    public bool IsSuccess => Code == IntakeResultCode.Success;

    public static IntakeResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new IntakeResult<T>(IntakeResultCode.Success, value);
    }

    /// <summary>
    /// Creates a failure. <see cref="IntakeResultCode.Success"/> is refused:
    /// a "failure" carrying the success code and no value would be a null
    /// success everywhere downstream.
    /// </summary>
    public static IntakeResult<T> Failure(IntakeResultCode code)
    {
        if (!Enum.IsDefined(code) || code == IntakeResultCode.Success)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        return new IntakeResult<T>(code, default);
    }

    /// <summary>The code only; a successful value is never rendered.</summary>
    public override string ToString() => $"{nameof(IntakeResult<T>)}({Code})";
}
