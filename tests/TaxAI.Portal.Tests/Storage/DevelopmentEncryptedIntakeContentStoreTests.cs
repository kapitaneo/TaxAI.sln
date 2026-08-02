using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ClientIntake.Contracts;
using ClientIntake.Models;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Storage;

public sealed class DevelopmentEncryptedIntakeContentStoreTests
{
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"));

    [Fact]
    public async Task StageEncryptsStreamsAndRoundTripsAcrossChunks()
    {
        var root = NewRoot();
        try
        {
            var store = CreateStore(root);
            var plaintext = Enumerable.Range(0, 70_000)
                .Select(index => (byte)(index % 251))
                .ToArray();

            var staged = await store.StageAsync(
                Firm,
                Case(1),
                new MemoryStream(plaintext),
                plaintext.Length,
                CancellationToken.None);

            Assert.Equal(plaintext.Length, staged.ByteCount);
            Assert.Equal(32, staged.Fingerprint.Count);
            var file = Assert.Single(Directory.GetFiles(root, "*.bin"));
            var envelope = await File.ReadAllBytesAsync(file);
            Assert.False(envelope.AsSpan().IndexOf(plaintext) >= 0);
            Assert.Equal(
                plaintext,
                await ReadForTestingAsync(store, Firm, Case(1), staged.ContentKey));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CiphertextIsRandomizedAndFingerprintIsScoped()
    {
        var root = NewRoot();
        try
        {
            var store = CreateStore(root);
            var content = Encoding.UTF8.GetBytes("synthetic intake content");
            var sameCaseFirst = await StageAsync(store, Firm, Case(1), content);
            var sameCaseSecond = await StageAsync(store, Firm, Case(1), content);
            var otherCase = await StageAsync(store, Firm, Case(2), content);
            var otherFirm = await StageAsync(
                store,
                TrustedFirmId.FromGuid(
                    Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2")),
                Case(1),
                content);

            Assert.Equal(
                sameCaseFirst.Fingerprint,
                sameCaseSecond.Fingerprint);
            Assert.NotEqual(sameCaseFirst.Fingerprint, otherCase.Fingerprint);
            Assert.NotEqual(sameCaseFirst.Fingerprint, otherFirm.Fingerprint);

            var envelopes = Directory.GetFiles(root, "*.bin")
                .Select(File.ReadAllBytes)
                .ToArray();
            Assert.Equal(4, envelopes.Length);
            Assert.False(envelopes[0].SequenceEqual(envelopes[1]));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task EmptyOversizedAndCancelledStreamsLeaveNoObject()
    {
        var root = NewRoot();
        try
        {
            var store = CreateStore(root);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.StageAsync(
                    Firm,
                    Case(1),
                    new MemoryStream(),
                    8,
                    CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.StageAsync(
                    Firm,
                    Case(1),
                    new MemoryStream(new byte[9]),
                    8,
                    CancellationToken.None));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.StageAsync(
                    Firm,
                    Case(1),
                    new MemoryStream(new byte[8]),
                    8,
                    cancelled.Token));

            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task TamperingTruncationAndWrongScopeAreRefused()
    {
        var root = NewRoot();
        try
        {
            var store = CreateStore(root);
            var staged = await StageAsync(
                store,
                Firm,
                Case(1),
                Encoding.UTF8.GetBytes("synthetic tamper probe"));
            var file = Assert.Single(Directory.GetFiles(root, "*.bin"));
            var original = await File.ReadAllBytesAsync(file);

            var tampered = original.ToArray();
            tampered[^1] ^= 0x01;
            await File.WriteAllBytesAsync(file, tampered);
            await Assert.ThrowsAnyAsync<CryptographicException>(() =>
                ReadForTestingAsync(store, Firm, Case(1), staged.ContentKey));

            await File.WriteAllBytesAsync(file, original[..^1]);
            await Assert.ThrowsAnyAsync<CryptographicException>(() =>
                ReadForTestingAsync(store, Firm, Case(1), staged.ContentKey));

            await File.WriteAllBytesAsync(file, original);
            await Assert.ThrowsAnyAsync<CryptographicException>(() =>
                ReadForTestingAsync(store, Firm, Case(2), staged.ContentKey));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DeleteIsIdempotentAndThePublicPortHasNoReadMember()
    {
        var root = NewRoot();
        try
        {
            var store = CreateStore(root);
            var staged = await StageAsync(store, Firm, Case(1), [1, 2, 3]);

            await store.DeleteIfExistsAsync(
                TrustedFirmId.FromGuid(
                    Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2")),
                Case(1),
                staged.ContentKey,
                CancellationToken.None);
            Assert.Single(Directory.GetFiles(root));

            await store.DeleteIfExistsAsync(
                Firm,
                Case(1),
                staged.ContentKey,
                CancellationToken.None);
            await store.DeleteIfExistsAsync(
                Firm,
                Case(1),
                staged.ContentKey,
                CancellationToken.None);

            Assert.Empty(Directory.GetFiles(root));
            Assert.DoesNotContain(
                typeof(IIntakeContentStore).GetMethods(),
                method => method.Name.Contains("Read", StringComparison.Ordinal));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static Task<StagedIntakeContent> StageAsync(
        IIntakeContentStore store,
        TrustedFirmId firm,
        Guid caseId,
        byte[] content) =>
        store.StageAsync(
            firm,
            caseId,
            new MemoryStream(content),
            content.Length,
            CancellationToken.None);

    private static IIntakeContentStore CreateStore(string root)
    {
        var type = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.DevelopmentEncryptedIntakeContentStore",
            throwOnError: true)!;
        return (IIntakeContentStore)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                root,
                Enumerable.Repeat((byte)0x61, 32).ToArray(),
                Enumerable.Repeat((byte)0x62, 32).ToArray(),
            ],
            culture: null)!;
    }

    private static async Task<byte[]> ReadForTestingAsync(
        IIntakeContentStore store,
        TrustedFirmId firm,
        Guid caseId,
        string contentKey)
    {
        var method = store.GetType().GetMethod(
            "ReadForTestingAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<byte[]>)method.Invoke(
            store,
            [firm, caseId, contentKey, CancellationToken.None])!;
        return await task;
    }

    private static Guid Case(int suffix) =>
        Guid.Parse($"cccccccc-cccc-4ccc-8ccc-{suffix:D12}");

    private static string NewRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "taxai-intake-content-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
