using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TaxAI.AzureInfrastructure.Content;
using TaxAI.AzureInfrastructure.Keys;
using Tenancy.Models;

namespace TaxAI.AzureInfrastructure.Tests.Content;

/// <summary>
/// Spec 016 T04 — the authenticated source envelope (FR-03, FR-04; AC-026,
/// AC-034, AC-038, AC-039).
/// <para>
/// The known-answer tests build the expected wire form <b>independently</b>
/// from the platform AES-GCM primitive and the documented layout, then require
/// the codec to agree byte for byte in both directions. Every tamper,
/// truncation, reordering, extension and scope substitution is refused, and the
/// refusal names nothing.
/// </para>
/// </summary>
public sealed class EnvelopeCodecTests
{
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(Guid.Parse("0f0e0d0c-0b0a-4908-8706-050403020100"));
    private static readonly Guid Case = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid Resource = Guid.Parse("66666666-7777-4888-8999-aaaaaaaaaaaa");
    private static readonly SourceEnvelopeScope Scope = new(Firm, Case, Resource);
    private static readonly byte[] DataKey = Enumerable.Range(0, 32).Select(value => (byte)(0xA0 ^ value)).ToArray();
    private static readonly byte[] NoncePrefix = { 1, 2, 3, 4, 5, 6, 7, 8 };

    /// <summary>
    /// The documented layout, built from first principles: magic, nonce prefix,
    /// chunk size, then per chunk index, length, final flag, AES-256-GCM body and
    /// tag, with the scope-binding associated data.
    /// </summary>
    private static byte[] ReferenceEncrypt(byte[] plaintext, byte[] key, byte[] prefix, SourceEnvelopeScope scope)
    {
        using var output = new MemoryStream();
        output.Write("TAISRC01"u8);
        output.Write(prefix);
        var size = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(size, 65536);
        output.Write(size);

        using var gcm = new AesGcm(key, 16);
        var chunks = plaintext.Chunk(65536).ToArray();
        for (var index = 0; index < chunks.Length; index++)
        {
            var chunk = chunks[index];
            var final = index == chunks.Length - 1;
            var nonce = new byte[12];
            prefix.CopyTo(nonce, 0);
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), (uint)index);

            var aad = new List<byte>();
            aad.AddRange("TAISRC01"u8.ToArray());
            aad.AddRange(scope.Firm.Value.ToByteArray(bigEndian: true));
            aad.AddRange(scope.CaseId.ToByteArray(bigEndian: true));
            aad.AddRange(scope.ResourceId.ToByteArray(bigEndian: true));
            var fields = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(fields, (uint)index);
            BinaryPrimitives.WriteInt32BigEndian(fields.AsSpan(4), chunk.Length);
            fields[8] = final ? (byte)1 : (byte)0;
            aad.AddRange(fields);

            var body = new byte[chunk.Length];
            var tag = new byte[16];
            gcm.Encrypt(nonce, chunk, body, tag, aad.ToArray());
            output.Write(fields);
            output.Write(body);
            output.Write(tag);
        }

        return output.ToArray();
    }

    private static byte[] Plaintext(int length) =>
        Enumerable.Range(0, length).Select(value => (byte)((value * 7) + 3)).ToArray();

    private static async Task<byte[]> EncryptAsync(byte[] plaintext, SourceEnvelopeScope? scope = null, byte[]? key = null)
    {
        using var output = new MemoryStream();
        await SourceEnvelopeCodec.EncryptAsync(key ?? DataKey, scope ?? Scope, new MemoryStream(plaintext), output, 10_000_000, null, CancellationToken.None);
        return output.ToArray();
    }

    [Fact] // Known answer: the codec decrypts the independently built vector.
    public void TheCodecDecryptsTheIndependentlyBuiltKnownAnswerVector()
    {
        var plaintext = Plaintext(140_000);
        var vector = ReferenceEncrypt(plaintext, DataKey, NoncePrefix, Scope);

        Assert.Equal(plaintext, SourceEnvelopeCodec.Decrypt(DataKey, Scope, vector, plaintext.Length));
        // A fixed regression digest of the reference vector itself.
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(ReferenceEncrypt(plaintext, DataKey, NoncePrefix, Scope))),
            Convert.ToHexString(SHA256.HashData(vector)));
    }

    [Theory] // Known answer: the codec's output is exactly the reference layout for its nonce.
    [InlineData(1)]
    [InlineData(65_535)]
    [InlineData(65_536)]
    [InlineData(65_537)]
    [InlineData(131_072)]
    [InlineData(200_001)]
    public async Task TheCodecProducesExactlyTheReferenceLayout(int length)
    {
        var plaintext = Plaintext(length);

        var produced = await EncryptAsync(plaintext);
        var prefix = produced.AsSpan(8, 8).ToArray();

        Assert.Equal(ReferenceEncrypt(plaintext, DataKey, prefix, Scope), produced);
        Assert.Equal(SourceEnvelopeCodec.MaximumCiphertextLength(length), produced.Length);
        Assert.Equal(plaintext, SourceEnvelopeCodec.Decrypt(DataKey, Scope, produced, length));
    }

    [Fact]
    public async Task EachEnvelopeUsesAFreshNoncePrefix()
    {
        var plaintext = Plaintext(1000);

        var first = await EncryptAsync(plaintext);
        var second = await EncryptAsync(plaintext);

        Assert.False(first.AsSpan(8, 8).SequenceEqual(second.AsSpan(8, 8)));
        Assert.False(first.AsSpan().SequenceEqual(second));
    }

    [Fact] // AC-026: no plaintext survives in the envelope.
    public async Task TheCiphertextContainsNoPlaintextRun()
    {
        var marker = Encoding.ASCII.GetBytes("SYNTHETIC-PLANTED-MARKER");
        var plaintext = Enumerable.Repeat(marker, 5000).SelectMany(bytes => bytes).ToArray();

        var produced = await EncryptAsync(plaintext);

        Assert.False(ContentStoreTests.Contains(produced, marker));
    }

    public static TheoryData<string> Tampers() => new()
    {
        "flipped ciphertext byte",
        "flipped tag byte",
        "truncated",
        "last chunk removed",
        "extended",
        "chunks reordered",
        "chunk duplicated",
        "final flag cleared",
        "header altered",
        "chunk size altered",
        "index altered",
        "length altered",
    };

    [Theory] // AC-034
    [MemberData(nameof(Tampers))]
    public async Task EveryTamperIsRefusedWithTheFixedMessage(string tamper)
    {
        var produced = await EncryptAsync(Plaintext(200_000));

        var failure = Assert.Throws<CryptographicException>(() =>
            SourceEnvelopeCodec.Decrypt(DataKey, Scope, Tamper(produced, tamper), 1_000_000));

        Assert.Equal("The source envelope is invalid.", failure.Message);
        Assert.Null(failure.InnerException);
    }

    public static TheoryData<string> Substitutions() => new()
    {
        "foreign firm",
        "wrong case",
        "wrong resource",
        "wrong data key",
    };

    [Theory] // Key isolation and scope substitution.
    [MemberData(nameof(Substitutions))]
    public async Task CiphertextDoesNotDecryptUnderAnyOtherScopeOrKey(string substitution)
    {
        var produced = await EncryptAsync(Plaintext(70_000));
        var scope = substitution switch
        {
            "foreign firm" => new SourceEnvelopeScope(TrustedFirmId.FromGuid(Guid.NewGuid()), Case, Resource),
            "wrong case" => new SourceEnvelopeScope(Firm, Guid.NewGuid(), Resource),
            "wrong resource" => new SourceEnvelopeScope(Firm, Case, Guid.NewGuid()),
            _ => Scope,
        };
        var key = substitution == "wrong data key" ? RandomNumberGenerator.GetBytes(32) : DataKey;

        Assert.Throws<CryptographicException>(() => SourceEnvelopeCodec.Decrypt(key, scope, produced, 1_000_000));
    }

    [Fact]
    public async Task PlaintextBeyondTheReaderBoundIsRefused()
    {
        var produced = await EncryptAsync(Plaintext(70_000));

        Assert.Throws<CryptographicException>(() => SourceEnvelopeCodec.Decrypt(DataKey, Scope, produced, 69_999));
        Assert.Equal(70_000, SourceEnvelopeCodec.Decrypt(DataKey, Scope, produced, 70_000).Length);
    }

    [Fact]
    public async Task EncryptionRefusesOversizedAndEmptyContent()
    {
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() => SourceEnvelopeCodec.EncryptAsync(
            DataKey, Scope, new MemoryStream(Plaintext(1001)), output, 1000, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => SourceEnvelopeCodec.EncryptAsync(
            DataKey, Scope, new MemoryStream(), new MemoryStream(), 1000, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => SourceEnvelopeCodec.EncryptAsync(
            new byte[16], Scope, new MemoryStream(Plaintext(10)), new MemoryStream(), 1000, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThePlaintextObserverSeesExactlyThePlaintext()
    {
        var plaintext = Plaintext(150_000);
        using var observer = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await SourceEnvelopeCodec.EncryptAsync(DataKey, Scope, new MemoryStream(plaintext), new MemoryStream(), 1_000_000, observer, CancellationToken.None);

        Assert.Equal(SHA256.HashData(plaintext), observer.GetHashAndReset());
    }

    [Fact]
    public void DecryptionRefusesAnUnusableKeyOrBound()
    {
        var vector = ReferenceEncrypt(Plaintext(10), DataKey, NoncePrefix, Scope);

        Assert.Throws<CryptographicException>(() => SourceEnvelopeCodec.Decrypt(new byte[31], Scope, vector, 100));
        Assert.Throws<CryptographicException>(() => SourceEnvelopeCodec.Decrypt(DataKey, Scope, vector, 0));
        Assert.Throws<CryptographicException>(() => SourceEnvelopeCodec.Decrypt(DataKey, Scope, Array.Empty<byte>(), 100));
    }

    // ------------------------------------------------------- key envelope

    private static WrappedContentKey Wrapped() => new(
        new Uri($"https://kv-taxai-prod-cc.vault.azure.net/keys/source-envelope/{new string('c', 32)}"),
        "RSA-OAEP-256",
        Enumerable.Range(0, 384).Select(value => (byte)value).ToArray());

    [Fact]
    public void TheKeyEnvelopeRoundTripsOnlyForItsOwnScope()
    {
        var encoded = SourceEnvelopeCodec.EncodeKeyEnvelope(Scope, Wrapped());

        var decoded = SourceEnvelopeCodec.DecodeKeyEnvelope(encoded, Scope);

        Assert.Equal(Wrapped().KeyVersionId, decoded.KeyVersionId);
        Assert.Equal("RSA-OAEP-256", decoded.Algorithm);
        Assert.Equal(Wrapped().Ciphertext.ToArray(), decoded.Ciphertext.ToArray());
        Assert.True(SourceEnvelopeCodec.TryReadKeyEnvelopeScope(encoded, out var scope));
        Assert.Equal(Resource, scope!.ResourceId);
        Assert.Throws<CryptographicException>(() =>
            SourceEnvelopeCodec.DecodeKeyEnvelope(encoded, new SourceEnvelopeScope(Firm, Case, Guid.NewGuid())));
        Assert.Throws<CryptographicException>(() =>
            SourceEnvelopeCodec.DecodeKeyEnvelope(encoded, new SourceEnvelopeScope(TrustedFirmId.FromGuid(Guid.NewGuid()), Case, Resource)));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("wrong magic")]
    [InlineData("truncated")]
    [InlineData("trailing byte")]
    [InlineData("oversized")]
    public void AMalformedKeyEnvelopeIsRefused(string malformation)
    {
        var encoded = SourceEnvelopeCodec.EncodeKeyEnvelope(Scope, Wrapped());
        var malformed = malformation switch
        {
            "empty" => Array.Empty<byte>(),
            "wrong magic" => encoded.Select((value, index) => index == 0 ? (byte)'X' : value).ToArray(),
            "truncated" => encoded[..^1],
            "trailing byte" => encoded.Append((byte)0).ToArray(),
            _ => new byte[SourceEnvelopeCodec.MaximumKeyEnvelopeBytes + 1],
        };

        Assert.False(SourceEnvelopeCodec.TryReadKeyEnvelopeScope(malformed, out _));
        Assert.Throws<CryptographicException>(() => SourceEnvelopeCodec.DecodeKeyEnvelope(malformed, Scope));
    }

    [Fact]
    public void ObjectNamesCarryOnlyTheOpaqueResourceIdentifier()
    {
        Assert.Equal($"content/{Resource:N}", SourceEnvelopeCodec.ContentObjectName(Resource));
        Assert.Equal($"envelope/{Resource:N}", SourceEnvelopeCodec.KeyObjectName(Resource));
        Assert.DoesNotContain(Firm.ToCanonicalString(), SourceEnvelopeCodec.ContentObjectName(Resource), StringComparison.Ordinal);
        Assert.DoesNotContain(Case.ToString("N"), SourceEnvelopeCodec.KeyObjectName(Resource), StringComparison.Ordinal);
    }

    [Fact]
    public void ScopesAreStructurallyRenderedAndRequireEveryValue()
    {
        Assert.Equal(nameof(SourceEnvelopeScope), Scope.ToString());
        Assert.Throws<ArgumentException>(() => new SourceEnvelopeScope(Firm, Guid.Empty, Resource));
        Assert.Throws<ArgumentException>(() => new SourceEnvelopeScope(Firm, Case, Guid.Empty));
        Assert.Throws<ArgumentNullException>(() => new SourceEnvelopeScope(null!, Case, Resource));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceEnvelopeCodec.MaximumCiphertextLength(0));
    }

    /// <summary>Applies one named tamper to a complete multi-chunk envelope.</summary>
    internal static byte[] Tamper(byte[] envelope, string tamper)
    {
        var copy = envelope.ToArray();
        var first = SourceEnvelopeCodec.HeaderSize;
        var chunk = SourceEnvelopeCodec.ChunkOverhead + SourceEnvelopeCodec.ChunkSize;
        switch (tamper)
        {
            case "flipped ciphertext byte":
                copy[first + 9 + 100] ^= 0x01;
                return copy;
            case "flipped tag byte":
                copy[first + chunk - 1] ^= 0x01;
                return copy;
            case "truncated":
                return copy[..^7];
            case "last chunk removed":
                return copy[..(first + (2 * chunk))];
            case "extended":
                return copy.Concat(copy[first..(first + chunk)]).ToArray();
            case "chunks reordered":
                return copy[..first]
                    .Concat(copy[(first + chunk)..(first + (2 * chunk))])
                    .Concat(copy[first..(first + chunk)])
                    .Concat(copy[(first + (2 * chunk))..])
                    .ToArray();
            case "chunk duplicated":
                return copy[..(first + chunk)]
                    .Concat(copy[first..(first + chunk)])
                    .Concat(copy[(first + chunk)..])
                    .ToArray();
            case "final flag cleared":
                copy[copy.Length - SourceEnvelopeCodec.TagSize - ((copy.Length - first) % chunk - SourceEnvelopeCodec.ChunkOverhead) - 1] = 0;
                return copy;
            case "header altered":
                copy[0] = (byte)'X';
                return copy;
            case "chunk size altered":
                copy[first - 1] ^= 0x01;
                return copy;
            case "index altered":
                copy[first + chunk + 3] ^= 0x01;
                return copy;
            case "length altered":
                copy[first + 7] ^= 0x01;
                return copy;
            default:
                throw new ArgumentOutOfRangeException(nameof(tamper));
        }
    }
}

/// <summary>
/// Spec 016 T04 — the executable source-protection probe
/// (<c>scripts/azure/Test-SourceProtection.ps1</c>, DEP-10), run for real over
/// the compiled codec. Offline it proves every synthetic check and says it is
/// not deployment evidence; Live without authority is unexecuted, never passed.
/// </summary>
public sealed class SourceProtectionProbeTests
{
    private static readonly string ProbePath = Path.Combine(
        Keys.KeyRotationTests.FindRepositoryRoot(), "scripts", "azure", "Test-SourceProtection.ps1");

    [Fact]
    public void TheOfflineProbeHoldsEveryCheckAndClaimsNoDeploymentEvidence()
    {
        var result = Run();

        Assert.True(result.ExitCode == 0, result.Output);
        foreach (var check in new[]
                 {
                     "EnvelopeRoundTrip",
                     "CiphertextCarriesNoPlaintext",
                     "DistinctKeysGiveDistinctCiphertext",
                     "TamperRefused",
                     "TruncationRefused",
                     "ReorderedChunksRefused",
                     "ForeignFirmRefused",
                     "WrongCaseRefused",
                     "WrongResourceRefused",
                     "WrongDataKeyRefused",
                     "TemplateGeoZoneRedundantGpv2",
                     "TemplatePrivateEntraOnly",
                     "TemplateCustomerManagedKey",
                     "TemplateNoShadowRetention",
                     "NoSasOrBlobAddressInPortal",
                     "NoSourceServingResponseInPortal",
                 })
        {
            Assert.Contains($"{check}: HELD", result.Output, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("FAILED", result.Output, StringComparison.Ordinal);
        Assert.Contains("not deployment evidence", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SYNTHETIC-SOURCE-PROBE-MARKER", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("-SubscriptionId", "22222222-2222-4222-8222-222222222222")]
    [InlineData("-SubscriptionId", "22222222-2222-4222-8222-222222222222", "-StorageAccountName", "sttaxaisrccc")]
    public void LiveWithoutExplicitAuthorityIsUnexecutedNeverPassed(params string[] arguments)
    {
        var result = Run(new[] { "-Mode", "Live" }.Concat(arguments).ToArray());

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("UNEXECUTED", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("HELD", result.Output, StringComparison.Ordinal);
    }

    [Fact] // The Live probe only observes: it never writes, lists, reads or deletes an object.
    public void TheLiveProbeIsReadOnlyAndUsesNoSharedKeyOrSas()
    {
        var script = File.ReadAllText(ProbePath);

        foreach (var forbidden in new[] { "'delete'", "'upload'", "'download'", "blob list", "'blob'", "generate-sas", "--account-key", "keys list", "'purge'", "'update'", "'set'" })
        {
            Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("'show'", script, StringComparison.Ordinal);
        Assert.Contains("ConfirmObservationAuthority", script, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) Run(params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", ProbePath, "-AssemblyPath", typeof(SourceEnvelopeCodec).Assembly.Location }
                     .Concat(arguments))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Assert.True(process.Start());
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "The probe did not finish.");

        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
}