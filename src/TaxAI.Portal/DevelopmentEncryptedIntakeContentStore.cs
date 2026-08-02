using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Tenancy.Models;

namespace TaxAI.Portal;

internal sealed class DevelopmentEncryptedIntakeContentStore :
    IIntakeContentStore
{
    private const int ChunkSize = 64 * 1024;
    private const int TagSize = 16;
    private const int SaltSize = 16;
    private const int NoncePrefixSize = 8;
    private static readonly byte[] Magic = "TAIIC001"u8.ToArray();

    private readonly string _root;
    private readonly byte[] _encryptionKey;
    private readonly byte[] _fingerprintKey;

    internal DevelopmentEncryptedIntakeContentStore(
        string root,
        byte[] encryptionKey,
        byte[] fingerprintKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(encryptionKey);
        ArgumentNullException.ThrowIfNull(fingerprintKey);
        if (!Path.IsPathFullyQualified(root) ||
            encryptionKey.Length != 32 ||
            fingerprintKey.Length != 32 ||
            encryptionKey.AsSpan().SequenceEqual(fingerprintKey))
        {
            throw new ArgumentException(
                "The development content store configuration is invalid.");
        }

        _root = Path.GetFullPath(root);
        _encryptionKey = encryptionKey.ToArray();
        _fingerprintKey = fingerprintKey.ToArray();
    }

    public async Task<StagedIntakeContent> StageAsync(
        TrustedFirmId firm,
        Guid caseId,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        ArgumentNullException.ThrowIfNull(content);
        if (caseId == Guid.Empty ||
            maximumBytes <= 0 ||
            !content.CanRead)
        {
            throw new ArgumentException("The content could not be staged.");
        }

        Directory.CreateDirectory(_root);
        var contentKey = Encode(RandomNumberGenerator.GetBytes(32));
        var temporaryPath = Path.Combine(
            _root,
            $"{Encode(RandomNumberGenerator.GetBytes(16))}.tmp");
        var finalPath = ObjectPath(firm, caseId, contentKey);

        try
        {
            var result = await EncryptAsync(
                firm,
                caseId,
                contentKey,
                content,
                maximumBytes,
                temporaryPath,
                cancellationToken);
            File.Move(temporaryPath, finalPath, overwrite: false);
            return new StagedIntakeContent(
                contentKey,
                result.ByteCount,
                result.Fingerprint);
        }
        catch
        {
            DeletePath(temporaryPath);
            DeletePath(finalPath);
            throw;
        }
    }

    public Task DeleteIfExistsAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        cancellationToken.ThrowIfCancellationRequested();
        if (caseId == Guid.Empty || !IsCanonicalKey(contentKey))
        {
            throw new ArgumentException("The content key is invalid.");
        }

        DeletePath(ObjectPath(firm, caseId, contentKey));
        return Task.CompletedTask;
    }

    internal async Task<byte[]> ReadForTestingAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(firm);
        if (caseId == Guid.Empty || !IsCanonicalKey(contentKey))
        {
            throw new ArgumentException("The content key is invalid.");
        }

        var path = ObjectPath(firm, caseId, contentKey);
        if (!File.Exists(path))
        {
            throw new CryptographicException("The content envelope is invalid.");
        }
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var header = new byte[Magic.Length + SaltSize + NoncePrefixSize + sizeof(int)];
        await ReadExactlyAsync(input, header, cancellationToken);
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(
                Magic.Length + SaltSize + NoncePrefixSize,
                sizeof(int))) != ChunkSize)
        {
            throw new CryptographicException("The content envelope is invalid.");
        }

        var salt = header.AsSpan(Magic.Length, SaltSize).ToArray();
        var noncePrefix = header.AsSpan(
            Magic.Length + SaltSize,
            NoncePrefixSize).ToArray();
        var objectKey = DeriveKey(
            _encryptionKey,
            salt,
            EncryptionInfo(firm, caseId, contentKey));
        var plaintext = new List<byte>();
        var expectedIndex = 0u;
        var sawFinal = false;

        using var cipher = new AesGcm(objectKey, TagSize);
        while (!sawFinal)
        {
            var chunkHeader = new byte[sizeof(uint) + sizeof(int) + 1];
            await ReadExactlyAsync(input, chunkHeader, cancellationToken);
            var index = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader);
            var length = BinaryPrimitives.ReadInt32BigEndian(
                chunkHeader.AsSpan(sizeof(uint), sizeof(int)));
            sawFinal = chunkHeader[^1] == 1;
            if (index != expectedIndex ||
                length <= 0 ||
                length > ChunkSize ||
                chunkHeader[^1] > 1)
            {
                throw new CryptographicException("The content envelope is invalid.");
            }

            var encrypted = new byte[length];
            var tag = new byte[TagSize];
            await ReadExactlyAsync(input, encrypted, cancellationToken);
            await ReadExactlyAsync(input, tag, cancellationToken);
            var decrypted = new byte[length];
            cipher.Decrypt(
                Nonce(noncePrefix, index),
                encrypted,
                tag,
                decrypted,
                AdditionalData(index, length, sawFinal));
            plaintext.AddRange(decrypted);
            expectedIndex = checked(index + 1);
        }

        if (input.ReadByte() != -1)
        {
            throw new CryptographicException("The content envelope is invalid.");
        }

        return plaintext.ToArray();
    }

    public override string ToString() =>
        nameof(DevelopmentEncryptedIntakeContentStore);

    private async Task<EncryptionResult> EncryptAsync(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey,
        Stream content,
        long maximumBytes,
        string path,
        CancellationToken cancellationToken)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        var objectKey = DeriveKey(
            _encryptionKey,
            salt,
            EncryptionInfo(firm, caseId, contentKey));
        var scopedFingerprintKey = DeriveKey(
            _fingerprintKey,
            salt: [],
            FingerprintInfo(firm, caseId));

        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await output.WriteAsync(Magic, cancellationToken);
        await output.WriteAsync(salt, cancellationToken);
        await output.WriteAsync(noncePrefix, cancellationToken);
        var chunkSizeBytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(chunkSizeBytes, ChunkSize);
        await output.WriteAsync(chunkSizeBytes, cancellationToken);

        using var cipher = new AesGcm(objectKey, TagSize);
        using var fingerprint = new HMACSHA256(scopedFingerprintKey);
        var buffer = new byte[ChunkSize];
        var index = 0u;
        var carry = -1;
        long byteCount = 0;
        var wroteChunk = false;

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
                var read = await content.ReadAsync(
                    buffer.AsMemory(count, buffer.Length - count),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            if (count == 0)
            {
                break;
            }

            var next = new byte[1];
            var nextCount = await content.ReadAsync(next, cancellationToken);
            var final = nextCount == 0;
            if (!final)
            {
                carry = next[0];
            }

            byteCount = checked(byteCount + count);
            if (byteCount > maximumBytes)
            {
                throw new InvalidDataException("The content exceeds its configured limit.");
            }

            _ = fingerprint.TransformBlock(buffer, 0, count, null, 0);
            var encrypted = new byte[count];
            var tag = new byte[TagSize];
            cipher.Encrypt(
                Nonce(noncePrefix, index),
                buffer.AsSpan(0, count),
                encrypted,
                tag,
                AdditionalData(index, count, final));

            var chunkHeader = new byte[sizeof(uint) + sizeof(int) + 1];
            BinaryPrimitives.WriteUInt32BigEndian(chunkHeader, index);
            BinaryPrimitives.WriteInt32BigEndian(
                chunkHeader.AsSpan(sizeof(uint), sizeof(int)),
                count);
            chunkHeader[^1] = final ? (byte)1 : (byte)0;
            await output.WriteAsync(chunkHeader, cancellationToken);
            await output.WriteAsync(encrypted, cancellationToken);
            await output.WriteAsync(tag, cancellationToken);
            wroteChunk = true;

            if (final)
            {
                break;
            }

            if (index == uint.MaxValue)
            {
                throw new InvalidDataException("The content envelope is too large.");
            }

            index++;
        }

        if (!wroteChunk)
        {
            throw new InvalidDataException("Empty content is not accepted.");
        }

        _ = fingerprint.TransformFinalBlock([], 0, 0);
        await output.FlushAsync(cancellationToken);
        return new EncryptionResult(byteCount, fingerprint.Hash!);
    }

    private static byte[] AdditionalData(uint index, int length, bool final)
    {
        var result = new byte[Magic.Length + sizeof(uint) + sizeof(int) + 1];
        Magic.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            result.AsSpan(Magic.Length, sizeof(uint)),
            index);
        BinaryPrimitives.WriteInt32BigEndian(
            result.AsSpan(Magic.Length + sizeof(uint), sizeof(int)),
            length);
        result[^1] = final ? (byte)1 : (byte)0;
        return result;
    }

    private static byte[] Nonce(byte[] prefix, uint index)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            nonce.AsSpan(NoncePrefixSize, sizeof(uint)),
            index);
        return nonce;
    }

    private static byte[] EncryptionInfo(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey) =>
        Encoding.UTF8.GetBytes(
            $"TaxAI.Intake.Content.v1|{firm.ToCanonicalString()}|{caseId:D}|{contentKey}");

    private static byte[] FingerprintInfo(
        TrustedFirmId firm,
        Guid caseId) =>
        Encoding.UTF8.GetBytes(
            $"TaxAI.Intake.Fingerprint.v1|{firm.ToCanonicalString()}|{caseId:D}");

    private static byte[] DeriveKey(
        byte[] inputKey,
        byte[] salt,
        byte[] info)
    {
        var actualSalt = salt.Length == 0 ? new byte[32] : salt;
        byte[] pseudorandomKey;
        using (var extract = new HMACSHA256(actualSalt))
        {
            pseudorandomKey = extract.ComputeHash(inputKey);
        }

        var blockInput = new byte[info.Length + 1];
        info.CopyTo(blockInput, 0);
        blockInput[^1] = 1;
        using var expand = new HMACSHA256(pseudorandomKey);
        return expand.ComputeHash(blockInput);
    }

    private static async Task ReadExactlyAsync(
        Stream input,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await input.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);
            if (read == 0)
            {
                throw new CryptographicException("The content envelope is invalid.");
            }

            offset += read;
        }
    }

    private static bool IsCanonicalKey(string contentKey)
    {
        if (contentKey is null || contentKey.Length != 43)
        {
            return false;
        }

        return contentKey.All(character =>
            character is >= 'A' and <= 'Z' ||
            character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character is '-' or '_');
    }

    private string ObjectPath(
        TrustedFirmId firm,
        Guid caseId,
        string contentKey)
    {
        var info = Encoding.UTF8.GetBytes(
            $"TaxAI.Intake.ObjectName.v1|{firm.ToCanonicalString()}|{caseId:D}|{contentKey}");
        var objectName = Encode(DeriveKey(_encryptionKey, salt: [], info));
        return Path.Combine(_root, $"{objectName}.bin");
    }

    private static string Encode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static void DeletePath(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record EncryptionResult(long ByteCount, byte[] Fingerprint);
}
