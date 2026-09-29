using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TaxAI.AzureInfrastructure.Keys;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Content;

/// <summary>
/// The server-owned scope one stored source object is bound to: the trusted
/// firm, the case and the opaque resource identifier the storage objects are
/// named by. Every encrypted chunk authenticates all three, so ciphertext moved
/// to another firm, case or resource never decrypts.
/// </summary>
public sealed class SourceEnvelopeScope
{
    public SourceEnvelopeScope(TrustedFirmId firm, Guid caseId, Guid resourceId)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || resourceId == Guid.Empty)
        {
            throw new ArgumentException("The source scope is incomplete.");
        }

        Firm = firm;
        CaseId = caseId;
        ResourceId = resourceId;
    }

    public TrustedFirmId Firm { get; }

    public Guid CaseId { get; }

    public Guid ResourceId { get; }

    internal bool SameAs(SourceEnvelopeScope other) =>
        Firm.Equals(other.Firm) &&
        CaseId == other.CaseId &&
        ResourceId == other.ResourceId;

    /// <summary>Fixed type identity only; no identifier is ever rendered.</summary>
    public override string ToString() => nameof(SourceEnvelopeScope);
}

/// <summary>
/// The authenticated source-content envelope (spec 016 FR-03/FR-04; AC-026,
/// AC-034, AC-038, AC-039).
/// <para>
/// <b>No novel primitive.</b> Content is split into fixed 64 KiB chunks, each
/// sealed with AES-256-GCM from the platform cryptography library under a
/// fresh, random, per-content 256-bit data key. The 96-bit nonce is an 8-byte
/// random prefix plus the 4-byte chunk index. Each chunk's associated data binds
/// the format magic, the firm, the case, the resource identifier, the chunk
/// index, its length and whether it is final, so a truncated, reordered,
/// duplicated, extended or transplanted chunk fails authentication.
/// </para>
/// <para>
/// The data key is never stored in plaintext. It is wrapped by the customer-
/// managed Key Vault key (<see cref="KeyVaultContentKeyWrapper"/>) and the
/// wrapped form is kept in a separate <b>key envelope</b> object, so a wrapping
/// key rotation can rewrap it without rewriting the upload-once source object.
/// </para>
/// <para>
/// Decryption is all-or-nothing: the whole bounded ciphertext is verified before
/// a single plaintext byte is released, so no partial plaintext ever reaches a
/// caller after a later integrity failure. Every failure is one fixed
/// <see cref="CryptographicException"/> that names nothing.
/// </para>
/// </summary>
public static class SourceEnvelopeCodec
{
    public const int ChunkSize = 64 * 1024;
    public const int TagSize = 16;
    public const int NoncePrefixSize = 8;
    public const int DataKeySize = 32;

    /// <summary>Magic, nonce prefix and chunk size.</summary>
    public const int HeaderSize = 8 + NoncePrefixSize + sizeof(int);

    /// <summary>Chunk index, length, final flag and tag.</summary>
    public const int ChunkOverhead = sizeof(uint) + sizeof(int) + 1 + TagSize;

    /// <summary>The largest key envelope this codec ever writes or reads.</summary>
    public const int MaximumKeyEnvelopeBytes = 4096;

    internal const string RefusalMessage = "The source envelope is invalid.";

    private static readonly byte[] ContentMagic = "TAISRC01"u8.ToArray();
    private static readonly byte[] KeyMagic = "TAISKY01"u8.ToArray();

    /// <summary>The storage object holding the upload-once ciphertext.</summary>
    public static string ContentObjectName(Guid resourceId) => $"content/{resourceId:N}";

    /// <summary>The storage object holding the wrapped data key.</summary>
    public static string KeyObjectName(Guid resourceId) => $"envelope/{resourceId:N}";

    /// <summary>The largest ciphertext a plaintext of the given bound can produce.</summary>
    public static long MaximumCiphertextLength(long maximumPlaintextBytes)
    {
        if (maximumPlaintextBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPlaintextBytes));
        }

        var chunks = checked((maximumPlaintextBytes + ChunkSize - 1) / ChunkSize);
        return checked(HeaderSize + maximumPlaintextBytes + (chunks * ChunkOverhead));
    }

    /// <summary>
    /// Streams <paramref name="plaintext"/> into <paramref name="ciphertext"/>,
    /// refusing more than <paramref name="maximumBytes"/> and an empty source, and
    /// feeds every plaintext byte to <paramref name="plaintextObserver"/> (the
    /// scoped keyed fingerprint) as it passes. Only one chunk of plaintext is ever
    /// held in memory, and it is cleared after use. Returns the plaintext length.
    /// </summary>
    public static async Task<long> EncryptAsync(
        ReadOnlyMemory<byte> dataKey,
        SourceEnvelopeScope scope,
        Stream plaintext,
        Stream ciphertext,
        long maximumBytes,
        IncrementalHash? plaintextObserver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (dataKey.Length != DataKeySize || maximumBytes <= 0)
        {
            throw new ArgumentException("The envelope parameters are invalid.");
        }

        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        var header = new byte[HeaderSize];
        ContentMagic.CopyTo(header, 0);
        noncePrefix.CopyTo(header, ContentMagic.Length);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(ContentMagic.Length + NoncePrefixSize), ChunkSize);
        await ciphertext.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        using var cipher = new AesGcm(dataKey.Span, TagSize);
        var buffer = new byte[ChunkSize];
        var encrypted = new byte[ChunkSize];
        var next = new byte[1];
        var carry = -1;
        var index = 0u;
        long total = 0;

        try
        {
            while (true)
            {
                var count = 0;
                if (carry >= 0)
                {
                    buffer[count++] = (byte)carry;
                    carry = -1;
                }

                while (count < buffer.Length)
                {
                    var read = await plaintext
                        .ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    count += read;
                }

                if (count == 0)
                {
                    if (index == 0)
                    {
                        throw new InvalidDataException("Empty content is not accepted.");
                    }

                    break;
                }

                var more = await plaintext.ReadAsync(next, cancellationToken).ConfigureAwait(false);
                var final = more == 0;
                if (!final)
                {
                    carry = next[0];
                }

                total = checked(total + count);
                if (total > maximumBytes)
                {
                    throw new InvalidDataException("The content exceeds its configured limit.");
                }

                plaintextObserver?.AppendData(buffer, 0, count);

                var chunkHeader = new byte[sizeof(uint) + sizeof(int) + 1];
                BinaryPrimitives.WriteUInt32BigEndian(chunkHeader, index);
                BinaryPrimitives.WriteInt32BigEndian(chunkHeader.AsSpan(sizeof(uint)), count);
                chunkHeader[^1] = final ? (byte)1 : (byte)0;

                var tag = new byte[TagSize];
                cipher.Encrypt(
                    Nonce(noncePrefix, index),
                    buffer.AsSpan(0, count),
                    encrypted.AsSpan(0, count),
                    tag,
                    AssociatedData(scope, index, count, final));

                await ciphertext.WriteAsync(chunkHeader, cancellationToken).ConfigureAwait(false);
                await ciphertext.WriteAsync(encrypted.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                await ciphertext.WriteAsync(tag, cancellationToken).ConfigureAwait(false);

                if (final)
                {
                    break;
                }

                index = index == uint.MaxValue
                    ? throw new InvalidDataException("The content envelope is too large.")
                    : index + 1;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(next);
        }

        return total;
    }

    /// <summary>
    /// Verifies the complete ciphertext and returns the plaintext, or throws the
    /// fixed refusal. The plaintext is released only when every chunk
    /// authenticated, the chunks were consecutive, exactly the last one was final,
    /// nothing trails it and the total is within <paramref name="maximumPlaintextBytes"/>.
    /// The caller owns the returned buffer and should clear it.
    /// </summary>
    public static byte[] Decrypt(
        ReadOnlySpan<byte> dataKey,
        SourceEnvelopeScope scope,
        ReadOnlySpan<byte> ciphertext,
        long maximumPlaintextBytes)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (dataKey.Length != DataKeySize || maximumPlaintextBytes <= 0)
        {
            throw new CryptographicException(RefusalMessage);
        }

        var length = MeasurePlaintext(ciphertext, maximumPlaintextBytes);
        var noncePrefix = ciphertext.Slice(ContentMagic.Length, NoncePrefixSize).ToArray();
        var plaintext = new byte[length];

        try
        {
            using var cipher = new AesGcm(dataKey, TagSize);
            var offset = HeaderSize;
            var written = 0;
            var index = 0u;
            while (offset < ciphertext.Length)
            {
                var count = BinaryPrimitives.ReadInt32BigEndian(ciphertext.Slice(offset + sizeof(uint)));
                var final = ciphertext[offset + sizeof(uint) + sizeof(int)] == 1;
                var body = offset + sizeof(uint) + sizeof(int) + 1;

                cipher.Decrypt(
                    Nonce(noncePrefix, index),
                    ciphertext.Slice(body, count),
                    ciphertext.Slice(body + count, TagSize),
                    plaintext.AsSpan(written, count),
                    AssociatedData(scope, index, count, final));

                written += count;
                offset = body + count + TagSize;
                index++;
            }

            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException(RefusalMessage);
        }
    }

    /// <summary>Encodes the wrapped data key together with the scope it belongs to.</summary>
    public static byte[] EncodeKeyEnvelope(SourceEnvelopeScope scope, WrappedContentKey wrapped)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(wrapped);

        var keyVersion = Encoding.UTF8.GetBytes(wrapped.KeyVersionId.AbsoluteUri);
        var algorithm = Encoding.UTF8.GetBytes(wrapped.Algorithm);
        var ciphertext = wrapped.Ciphertext.ToArray();

        using var output = new MemoryStream();
        output.Write(KeyMagic);
        output.Write(scope.Firm.Value.ToByteArray(bigEndian: true));
        output.Write(scope.CaseId.ToByteArray(bigEndian: true));
        output.Write(scope.ResourceId.ToByteArray(bigEndian: true));
        WriteField(output, keyVersion);
        WriteField(output, algorithm);
        WriteField(output, ciphertext);

        if (output.Length > MaximumKeyEnvelopeBytes)
        {
            throw new CryptographicException(RefusalMessage);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Decodes a key envelope and refuses it unless it belongs to exactly
    /// <paramref name="expected"/>.
    /// </summary>
    public static WrappedContentKey DecodeKeyEnvelope(ReadOnlySpan<byte> envelope, SourceEnvelopeScope expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (!TryReadKeyEnvelope(envelope, out var scope, out var wrapped) ||
            !scope!.SameAs(expected))
        {
            throw new CryptographicException(RefusalMessage);
        }

        return wrapped!;
    }

    /// <summary>
    /// Reads only the scope a key envelope names, for the scoped purge check.
    /// Returns <see langword="false"/> for anything malformed.
    /// </summary>
    public static bool TryReadKeyEnvelopeScope(ReadOnlySpan<byte> envelope, out SourceEnvelopeScope? scope) =>
        TryReadKeyEnvelope(envelope, out scope, out _);

    private static bool TryReadKeyEnvelope(
        ReadOnlySpan<byte> envelope,
        out SourceEnvelopeScope? scope,
        out WrappedContentKey? wrapped)
    {
        scope = null;
        wrapped = null;
        const int scopeBytes = 16 * 3;
        if (envelope.Length > MaximumKeyEnvelopeBytes ||
            envelope.Length < KeyMagic.Length + scopeBytes ||
            !envelope[..KeyMagic.Length].SequenceEqual(KeyMagic))
        {
            return false;
        }

        var offset = KeyMagic.Length;
        var firm = new Guid(envelope.Slice(offset, 16), bigEndian: true);
        var caseId = new Guid(envelope.Slice(offset + 16, 16), bigEndian: true);
        var resourceId = new Guid(envelope.Slice(offset + 32, 16), bigEndian: true);
        offset += scopeBytes;

        if (!TryReadField(envelope, ref offset, out var keyVersion) ||
            !TryReadField(envelope, ref offset, out var algorithm) ||
            !TryReadField(envelope, ref offset, out var ciphertext) ||
            offset != envelope.Length ||
            firm == Guid.Empty ||
            caseId == Guid.Empty ||
            resourceId == Guid.Empty ||
            ciphertext.Length == 0)
        {
            return false;
        }

        try
        {
            if (!Uri.TryCreate(Encoding.UTF8.GetString(keyVersion), UriKind.Absolute, out var keyVersionId))
            {
                return false;
            }

            scope = new SourceEnvelopeScope(TrustedFirmId.FromGuid(firm), caseId, resourceId);
            wrapped = new WrappedContentKey(keyVersionId, Encoding.UTF8.GetString(algorithm), ciphertext);
            return true;
        }
        catch (ArgumentException)
        {
            scope = null;
            return false;
        }
    }

    private static long MeasurePlaintext(ReadOnlySpan<byte> ciphertext, long maximumPlaintextBytes)
    {
        if (ciphertext.Length < HeaderSize + ChunkOverhead ||
            !ciphertext[..ContentMagic.Length].SequenceEqual(ContentMagic) ||
            BinaryPrimitives.ReadInt32BigEndian(ciphertext.Slice(ContentMagic.Length + NoncePrefixSize)) != ChunkSize)
        {
            throw new CryptographicException(RefusalMessage);
        }

        long total = 0;
        var offset = HeaderSize;
        var expected = 0u;
        var sawFinal = false;
        while (offset < ciphertext.Length)
        {
            if (sawFinal ||
                ciphertext.Length - offset < ChunkOverhead)
            {
                throw new CryptographicException(RefusalMessage);
            }

            var index = BinaryPrimitives.ReadUInt32BigEndian(ciphertext.Slice(offset));
            var count = BinaryPrimitives.ReadInt32BigEndian(ciphertext.Slice(offset + sizeof(uint)));
            var flag = ciphertext[offset + sizeof(uint) + sizeof(int)];
            if (index != expected ||
                count <= 0 ||
                count > ChunkSize ||
                flag > 1 ||
                (flag == 0 && count != ChunkSize) ||
                ciphertext.Length - offset - ChunkOverhead < count)
            {
                throw new CryptographicException(RefusalMessage);
            }

            sawFinal = flag == 1;
            total += count;
            if (total > maximumPlaintextBytes)
            {
                throw new CryptographicException(RefusalMessage);
            }

            offset += ChunkOverhead + count;
            expected++;
        }

        if (!sawFinal)
        {
            throw new CryptographicException(RefusalMessage);
        }

        return total;
    }

    private static byte[] AssociatedData(SourceEnvelopeScope scope, uint index, int length, bool final)
    {
        var data = new byte[ContentMagic.Length + (16 * 3) + sizeof(uint) + sizeof(int) + 1];
        var span = data.AsSpan();
        ContentMagic.CopyTo(span);
        scope.Firm.Value.TryWriteBytes(span.Slice(8, 16), bigEndian: true, out _);
        scope.CaseId.TryWriteBytes(span.Slice(24, 16), bigEndian: true, out _);
        scope.ResourceId.TryWriteBytes(span.Slice(40, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(56), index);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(60), length);
        span[^1] = final ? (byte)1 : (byte)0;
        return data;
    }

    private static byte[] Nonce(byte[] prefix, uint index)
    {
        var nonce = new byte[NoncePrefixSize + sizeof(uint)];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(NoncePrefixSize), index);
        return nonce;
    }

    private static void WriteField(Stream output, byte[] value)
    {
        if (value.Length is 0 or > ushort.MaxValue)
        {
            throw new CryptographicException(RefusalMessage);
        }

        Span<byte> length = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)value.Length);
        output.Write(length);
        output.Write(value);
    }

    private static bool TryReadField(ReadOnlySpan<byte> source, ref int offset, out byte[] value)
    {
        value = Array.Empty<byte>();
        if (source.Length - offset < sizeof(ushort))
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset));
        offset += sizeof(ushort);
        if (length == 0 || source.Length - offset < length)
        {
            return false;
        }

        value = source.Slice(offset, length).ToArray();
        offset += length;
        return true;
    }
}
