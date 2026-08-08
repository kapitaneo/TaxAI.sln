using System.Reflection;
using System.Text;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ExtractionWorkflow.Contracts;
using TaxCases.Models;
using Tenancy.Models;

namespace TaxAI.Portal.Tests.Extraction;

public sealed class ContentReaderTests
{
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1"));

    [Fact]
    public async Task FourPartStoredScopeIsRequiredBeforeContentIsOpened()
    {
        var root = NewRoot();
        try
        {
            var metadata = new IntakeStore();
            var adapter = CreateAdapter(root, metadata);
            var content = Encoding.UTF8.GetBytes(
                "TAXAI-DETERMINISTIC-DOCUMENT/1\nKIND:T4\nBYTE-MARKER-ONLY-IN-LEASE");
            var staged = await ((IIntakeContentStore)adapter).StageAsync(
                Firm,
                Case(1),
                new MemoryStream(content),
                content.Length,
                CancellationToken.None);
            metadata.Value = StoredCase(
                Firm,
                Case(1),
                Document(1),
                staged.ContentKey);
            var reader = Assert.IsAssignableFrom<IExtractionContentReader>(
                adapter);

            await using var lease = await reader.OpenAsync(
                Firm,
                Case(1),
                Document(1),
                staged.ContentKey,
                CancellationToken.None);

            Assert.NotNull(lease);
            using var copy = new MemoryStream();
            await lease!.Content.CopyToAsync(copy);
            Assert.Equal(content, copy.ToArray());

            Assert.Null(await reader.OpenAsync(
                TrustedFirmId.FromGuid(
                    Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2")),
                Case(1),
                Document(1),
                staged.ContentKey,
                CancellationToken.None));
            Assert.Null(await reader.OpenAsync(
                Firm,
                Case(2),
                Document(1),
                staged.ContentKey,
                CancellationToken.None));
            Assert.Null(await reader.OpenAsync(
                Firm,
                Case(1),
                Document(2),
                staged.ContentKey,
                CancellationToken.None));
            Assert.Null(await reader.OpenAsync(
                Firm,
                Case(1),
                Document(1),
                CanonicalKey(0x7f),
                CancellationToken.None));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task MatchingForgedMetadataCannotBypassFirmAndCaseKeyDerivation()
    {
        var root = NewRoot();
        try
        {
            var metadata = new IntakeStore();
            var adapter = CreateAdapter(root, metadata);
            var content = Encoding.UTF8.GetBytes("scope-bound-content");
            var staged = await ((IIntakeContentStore)adapter).StageAsync(
                Firm,
                Case(1),
                new MemoryStream(content),
                content.Length,
                CancellationToken.None);
            var otherFirm = TrustedFirmId.FromGuid(
                Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2"));
            metadata.Value = StoredCase(
                otherFirm,
                Case(2),
                Document(1),
                staged.ContentKey);

            Assert.Null(await ((IExtractionContentReader)adapter).OpenAsync(
                otherFirm,
                Case(2),
                Document(1),
                staged.ContentKey,
                CancellationToken.None));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DisposingTheLeaseMakesItsContentUnreachable()
    {
        var root = NewRoot();
        try
        {
            var metadata = new IntakeStore();
            var adapter = CreateAdapter(root, metadata);
            var staged = await ((IIntakeContentStore)adapter).StageAsync(
                Firm,
                Case(1),
                new MemoryStream([1, 2, 3, 4]),
                4,
                CancellationToken.None);
            metadata.Value = StoredCase(
                Firm,
                Case(1),
                Document(1),
                staged.ContentKey);
            var lease = await ((IExtractionContentReader)adapter).OpenAsync(
                Firm,
                Case(1),
                Document(1),
                staged.ContentKey,
                CancellationToken.None);

            Assert.NotNull(lease);
            await lease!.DisposeAsync();

            Assert.True(lease.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => lease.Content);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static object CreateAdapter(string root, IIntakeStore metadata)
    {
        var type = typeof(Program).Assembly.GetType(
            "TaxAI.Portal.DevelopmentEncryptedIntakeContentStore",
            throwOnError: true)!;
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                root,
                Enumerable.Repeat((byte)0x61, 32).ToArray(),
                Enumerable.Repeat((byte)0x62, 32).ToArray(),
                metadata,
            ],
            culture: null)!;
    }

    private static StoredIntakeCase StoredCase(
        TrustedFirmId firm,
        Guid caseId,
        Guid documentId,
        string contentKey)
    {
        var expectedId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var now = DateTimeOffset.Parse("2026-08-05T12:00:00+00:00");
        return new StoredIntakeCase(
            firm,
            caseId,
            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"),
            "Synthetic Taxpayer",
            2025,
            CaseStatus.InformationGathering,
            1,
            [new IntakeExpectedItemRecord(
                expectedId,
                DocumentCategory.T4Slip,
                1)],
            new IntakeLinkRecord(
                Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff"),
                new IntakeTokenDigest(Enumerable.Repeat((byte)0x21, 32).ToArray()),
                now,
                now.AddHours(1),
                null,
                null),
            [],
            [
                new IntakeDocumentRecord(
                    documentId,
                    expectedId,
                    DocumentCategory.T4Slip,
                    1,
                    contentKey,
                    IntakeMediaKind.Pdf,
                    4,
                    Enumerable.Repeat((byte)0x31, 32).ToArray(),
                    now),
            ],
            [],
            [],
            null);
    }

    private static string CanonicalKey(byte value) =>
        Convert.ToBase64String(Enumerable.Repeat(value, 32).ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static Guid Case(int suffix) =>
        Guid.Parse($"cccccccc-cccc-4ccc-8ccc-{suffix:D12}");

    private static Guid Document(int suffix) =>
        Guid.Parse($"dddddddd-dddd-4ddd-8ddd-{suffix:D12}");

    private static string NewRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "taxai-extraction-content-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class IntakeStore : IIntakeStore
    {
        internal StoredIntakeCase? Value { get; set; }

        public Task<StoredIntakeCase?> FindAsync(
            TrustedFirmId firm,
            Guid caseId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Value is not null &&
                Value.CaseId == caseId &&
                string.Equals(
                    Value.Firm.ToCanonicalString(),
                    firm.ToCanonicalString(),
                    StringComparison.Ordinal)
                    ? Value
                    : null);

        public Task<IntakeStoreWriteCode> CreateAsync(
            NewIntakeCase intakeCase,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeLinkScope?> FindLinkScopeAsync(
            IntakeTokenDigest linkDigest,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeStoreWriteCode> RedeemLinkAsync(
            IntakeLinkScope linkScope,
            NewIntakeSession session,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeClientScope?> FindClientScopeAsync(
            IntakeTokenDigest sessionDigest,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredIntakeCase?> FindForClientAsync(
            IntakeClientScope scope,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeStoreWriteCode> ReissueLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            IntakeLinkRecord replacement,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeStoreWriteCode> RevokeLinkAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            DateTimeOffset revokedAtUtc,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeStoreWriteCode> AppendAnswersAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IReadOnlyList<IntakeAnswerVersionRecord> answers,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeStoreWriteCode> AppendUploadAttemptAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IntakeUploadAttemptRecord attempt,
            IntakeDocumentRecord? document,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeStoreWriteCode> AppendSubmissionAsync(
            IntakeClientScope scope,
            long expectedConcurrencyVersion,
            DateTimeOffset renewedIdleExpiresAtUtc,
            IntakeSubmissionRecord submission,
            IReadOnlyList<IntakeAuditEventRecord> auditEvents,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IntakeStoreWriteCode> AssociateReviewAsync(
            TrustedFirmId firm,
            Guid caseId,
            long expectedConcurrencyVersion,
            IntakeReviewAssociationRecord association,
            IntakeAuditEventRecord auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
