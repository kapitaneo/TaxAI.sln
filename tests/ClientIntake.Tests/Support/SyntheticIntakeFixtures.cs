using System.Security.Cryptography;
using System.Reflection;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using TaxLogicEngine.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace ClientIntake.Tests.Support;

internal static class SyntheticIntakeFixtures
{
    internal static readonly DateTimeOffset Now =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    internal static readonly TrustedFirmId Firm =
        TrustedFirmId.FromGuid(Guid.Parse("11111111-1111-4111-8111-111111111111"));

    internal static IntakeWorkflowOptions Options() =>
        new(
            LinkLifetime: TimeSpan.FromHours(1),
            ClientSessionIdleLifetime: TimeSpan.FromMinutes(15),
            MaximumFileBytes: 1024 * 1024,
            MaximumFilesPerRequest: 1);

    internal static CreateIntakeCaseCommand CreateCommand(
        int t4 = 1,
        int medical = 1) =>
        new(
            "Synthetic Taxpayer",
            TaxYear: 2025,
            ExpectedT4Count: t4,
            ExpectedMedicalExpenseCount: medical);

    internal static byte[] Pdf(string suffix = "A") =>
        System.Text.Encoding.ASCII.GetBytes($"%PDF-1.7 synthetic-{suffix}");

    internal static byte[] Jpeg(string suffix = "A") =>
        [0xff, 0xd8, 0xff, .. System.Text.Encoding.ASCII.GetBytes($"synthetic-{suffix}")];

    internal static byte[] Png(string suffix = "A") =>
    [
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
        .. System.Text.Encoding.ASCII.GetBytes($"synthetic-{suffix}"),
    ];

    internal static async Task<Harness> CreateHarnessAsync(
        int t4 = 1,
        int medical = 1,
        ReviewRepository? review = null)
    {
        var store = new InMemoryIntakeStore();
        var source = new CredentialSource();
        var actualReview = review ?? new ReviewRepository();
        var accountant = new AccountantIntakeWorkflow(
            store,
            source,
            actualReview,
            Options());
        var content = new ContentStore();
        var client = new ClientIntakeWorkflow(
            store,
            content,
            source,
            Options());
        var result = await accountant.CreateAsync(
            Firm,
            Guid.NewGuid(),
            CreateCommand(t4, medical),
            Now,
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return new Harness(
            store,
            content,
            actualReview,
            accountant,
            client,
            result.Value!);
    }

    internal static PersistedReviewRecord ReviewRecord(
        Guid recordId,
        Guid caseId,
        int taxYear,
        TrustedFirmId? firm = null)
    {
        var constructor = typeof(PersistedReviewRecord)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 10);
        return (PersistedReviewRecord)constructor.Invoke(
        [
            recordId,
            (firm ?? Firm).Value,
            caseId.ToString("D"),
            Guid.NewGuid().ToString("D"),
            taxYear,
            1,
            Now,
            Now,
            Now.AddYears(1),
            Array.Empty<PersistedSlip>(),
        ]);
    }

    internal sealed record Harness(
        InMemoryIntakeStore Store,
        ContentStore Content,
        ReviewRepository Review,
        AccountantIntakeWorkflow Accountant,
        ClientIntakeWorkflow Client,
        CreatedIntakeCase Created)
    {
        internal async Task<RedeemedIntakeSession> RedeemAsync()
        {
            var inspection = await Client.InspectLinkAsync(
                Created.LinkCredential,
                Now,
                CancellationToken.None);
            Assert.True(inspection.IsSuccess);
            var redemption = await Client.RedeemLinkAsync(
                inspection.Value!.Proof,
                Now,
                CancellationToken.None);
            Assert.True(redemption.IsSuccess);
            return redemption.Value!;
        }
    }

    internal sealed class CredentialSource : IIntakeCredentialSource
    {
        private int _identifier;
        private int _credential;

        public Guid CreateIdentifier()
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(Interlocked.Increment(ref _identifier))
                .CopyTo(bytes, 0);
            bytes[7] = 0x40;
            bytes[8] = 0x80;
            return new Guid(bytes);
        }

        public IntakeSecret CreateCredential()
        {
            var bytes = new byte[32];
            BitConverter.GetBytes(Interlocked.Increment(ref _credential))
                .CopyTo(bytes, 0);
            var value = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            Assert.True(IntakeSecret.TryParse(value, out var credential));
            return credential!;
        }

        public IntakeTokenDigest Digest(
            IntakeSecret credential,
            IntakeTokenPurpose purpose)
        {
            var key = Enumerable.Repeat(
                purpose == IntakeTokenPurpose.BootstrapLink
                    ? (byte)0x41
                    : (byte)0x52,
                32).ToArray();
            using var hmac = new HMACSHA256(key);
            return new IntakeTokenDigest(
                hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes(
                    credential.Reveal())));
        }
    }

    internal sealed class ContentStore : IIntakeContentStore
    {
        private readonly Dictionary<string, byte[]> _objects = [];

        internal int Count => _objects.Count;

        public async Task<StagedIntakeContent> StageAsync(
            TrustedFirmId firm,
            Guid caseId,
            Stream content,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var buffer = new byte[128];
            while (true)
            {
                var read = await content.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                bytes.AddRange(buffer.AsSpan(0, read).ToArray());
                if (bytes.Count > maximumBytes)
                {
                    throw new InvalidDataException();
                }
            }

            using var hmac = new HMACSHA256(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{firm.ToCanonicalString()}|{caseId:D}|synthetic-key")
                    .Concat(new byte[32])
                    .Take(32)
                    .ToArray());
            var fingerprint = hmac.ComputeHash(bytes.ToArray());
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            _objects.Add(key, bytes.ToArray());
            return new StagedIntakeContent(key, bytes.Count, fingerprint);
        }

        public Task DeleteIfExistsAsync(
            TrustedFirmId firm,
            Guid caseId,
            string contentKey,
            CancellationToken cancellationToken)
        {
            _objects.Remove(contentKey);
            return Task.CompletedTask;
        }
    }

    internal sealed class BarrierContentStore : IIntakeContentStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, byte[]> _objects = [];
        private readonly TaskCompletionSource _bothStaged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stagedCount;

        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _objects.Count;
                }
            }
        }

        public async Task<StagedIntakeContent> StageAsync(
            TrustedFirmId firm,
            Guid caseId,
            Stream content,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            if (bytes.Length == 0 || bytes.Length > maximumBytes)
            {
                throw new InvalidDataException();
            }

            using var hmac = new HMACSHA256(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{firm.ToCanonicalString()}|{caseId:D}|barrier")
                    .Concat(new byte[32])
                    .Take(32)
                    .ToArray());
            var fingerprint = hmac.ComputeHash(bytes);
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            lock (_gate)
            {
                _objects.Add(key, bytes);
                if (++_stagedCount == 2)
                {
                    _bothStaged.TrySetResult();
                }
            }

            await _bothStaged.Task.WaitAsync(cancellationToken);
            return new StagedIntakeContent(key, bytes.Length, fingerprint);
        }

        public Task DeleteIfExistsAsync(
            TrustedFirmId firm,
            Guid caseId,
            string contentKey,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _objects.Remove(contentKey);
            }

            return Task.CompletedTask;
        }
    }

    internal sealed class ReviewRepository : IReviewRecordRepository
    {
        internal ReviewRepository(PersistedReviewRecord? record = null)
        {
            Record = record;
        }

        internal PersistedReviewRecord? Record { get; set; }
        internal int FindCalls { get; private set; }
        internal int SaveCalls { get; private set; }

        public Task<ParseResult<PersistedReviewRecord>> SaveAsync(
            TrustedFirmId firm,
            PersistedReviewRecord record,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            throw new InvalidOperationException("Review creation is not part of intake.");
        }

        public Task<PersistedReviewRecord?> FindAsync(
            TrustedFirmId firm,
            Guid recordId,
            CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult(
                Record is not null &&
                Record.RecordId == recordId &&
                Record.FirmId == firm.Value
                    ? Record
                    : null);
        }
    }
}
