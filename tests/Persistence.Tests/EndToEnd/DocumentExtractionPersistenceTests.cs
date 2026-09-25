using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using CaseProcessing.Services;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using DocumentExtraction.Ports;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using Microsoft.EntityFrameworkCore;
using Persistence.Repositories;
using Persistence.Tests.Support;
using TaxCases.Models;
using TaxLogicEngine.Models;
using TaxLogicEngine.Services;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Tests.EndToEnd;

/// <summary>
/// Spec 015 T07 — the cross-module relational journey. Unit suites already pin
/// each collaborator; this suite proves that the real workflow and all three SQL
/// adapters agree on the same migrated schema and immutable identifiers.
/// </summary>
public sealed class DocumentExtractionPersistenceTests
{
    private const string PlantedSin = "123456789";
    private const string SourceMarker = "SOURCE-BYTE-MARKER-015-T07";
    private const string ProviderMarker = "PROVIDER-MARKER-015-T07";
    private const string PatientMarker = "PATIENT-MARKER-015-T07";

    [Fact]
    public async Task RealWorkflowsAndRealSqlAdaptersEndInOneAssociatedT4Review()
    {
        await using var journey = await RelationalJourney.CreateAsync();

        var t4 = await journey.Ingest.ExtractAsync(
            journey.Firm,
            journey.Actor,
            new ExtractDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.T4DocumentId,
            },
            journey.Now,
            CancellationToken.None);
        var medical = await journey.Ingest.ExtractAsync(
            journey.Firm,
            journey.Actor,
            new ExtractDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.MedicalDocumentId,
            },
            journey.Now.AddMinutes(1),
            CancellationToken.None);

        Assert.True(t4.IsSuccess, $"{t4.Code}; source opens: {journey.Source.OpenCount}");
        Assert.True(t4.Value!.IsEngineAccepted);
        Assert.Equal(ExtractionRecordedKind.T4ExtractionRecorded, t4.Value.Recorded);
        Assert.True(medical.IsSuccess, medical.Code.ToString());
        Assert.Equal(
            ExtractionRecordedKind.MedicalEvidenceRecorded,
            medical.Value!.Recorded);

        Assert.True((await journey.Review.AcceptDocumentAsync(
            journey.Firm,
            journey.Actor,
            new AcceptExtractionDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.T4DocumentId,
            },
            journey.Now.AddMinutes(2),
            CancellationToken.None)).IsSuccess);
        Assert.True((await journey.Review.AcceptDocumentAsync(
            journey.Firm,
            journey.Actor,
            new AcceptExtractionDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.MedicalDocumentId,
            },
            journey.Now.AddMinutes(3),
            CancellationToken.None)).IsSuccess);

        Assert.Null(await journey.Reviews.FindAsync(
            journey.Firm,
            Guid.NewGuid(),
            CancellationToken.None));

        var finalized = await journey.Review.FinalizeCaseAsync(
            journey.Firm,
            journey.Actor,
            new FinalizeExtractionCaseCommand { CaseId = journey.CaseId },
            journey.Now.AddMinutes(4),
            CancellationToken.None);

        Assert.True(finalized.IsSuccess);
        Assert.Equal(1, finalized.Value!.AcceptedT4DocumentCount);

        var persisted = await journey.Reviews.FindAsync(
            journey.Firm,
            finalized.Value.ReviewRecordId,
            CancellationToken.None);
        Assert.NotNull(persisted);
        var slip = Assert.Single(persisted!.Slips);
        Assert.Equal("Synthetic Employee", slip.TaxpayerDisplayName);
        Assert.DoesNotContain(ProviderMarker, slip.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PatientMarker, slip.ToString(), StringComparison.Ordinal);

        var storedCase = await journey.Intake.FindAsync(
            journey.Firm,
            journey.CaseId,
            CancellationToken.None);
        Assert.Equal(finalized.Value.ReviewRecordId, storedCase!.ReviewAssociation!.RecordId);

        var extractions = await journey.Extractions.FindCaseAsync(
            journey.Firm,
            journey.CaseId,
            CancellationToken.None);
        Assert.NotNull(extractions);
        Assert.Single(extractions!.CaseAcceptances);
        Assert.Single(extractions.Documents.Single(
            document => document.DocumentId == journey.T4DocumentId).Versions);
        var neutralEvidence = Assert.Single(extractions.Documents.Single(
            document => document.DocumentId == journey.MedicalDocumentId)
            .MedicalEvidenceVersions);
        Assert.Equal(ProviderMarker, neutralEvidence.ProviderDisplayName);
        Assert.Equal(PatientMarker, neutralEvidence.PatientDisplayName);

        var repeated = await journey.Review.FinalizeCaseAsync(
            journey.Firm,
            journey.Actor,
            new FinalizeExtractionCaseCommand { CaseId = journey.CaseId },
            journey.Now.AddMinutes(5),
            CancellationToken.None);
        Assert.False(repeated.IsSuccess);
        Assert.Single((await journey.Extractions.FindCaseAsync(
            journey.Firm,
            journey.CaseId,
            CancellationToken.None))!.CaseAcceptances);
    }

    [Fact]
    public async Task CompleteRelationalJourneyKeepsSourceAndSinOutOfEveryColumn()
    {
        await using var journey = await RelationalJourney.CreateAsync();

        var t4 = await journey.Ingest.ExtractAsync(
            journey.Firm,
            journey.Actor,
            new ExtractDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.T4DocumentId,
            },
            journey.Now,
            CancellationToken.None);
        Assert.True(t4.IsSuccess, t4.Code.ToString());
        var medical = await journey.Ingest.ExtractAsync(
            journey.Firm,
            journey.Actor,
            new ExtractDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.MedicalDocumentId,
            },
            journey.Now.AddMinutes(1),
            CancellationToken.None);
        Assert.True(medical.IsSuccess, medical.Code.ToString());

        Assert.True((await journey.Review.AcceptDocumentAsync(
            journey.Firm,
            journey.Actor,
            new AcceptExtractionDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.T4DocumentId,
            },
            journey.Now.AddMinutes(2),
            CancellationToken.None)).IsSuccess);
        Assert.True((await journey.Review.AcceptDocumentAsync(
            journey.Firm,
            journey.Actor,
            new AcceptExtractionDocumentCommand
            {
                CaseId = journey.CaseId,
                DocumentId = journey.MedicalDocumentId,
            },
            journey.Now.AddMinutes(3),
            CancellationToken.None)).IsSuccess);
        var finalized = await journey.Review.FinalizeCaseAsync(
            journey.Firm,
            journey.Actor,
            new FinalizeExtractionCaseCommand { CaseId = journey.CaseId },
            journey.Now.AddMinutes(4),
            CancellationToken.None);
        Assert.True(finalized.IsSuccess);

        var cells = await journey.ReadEveryCellAsync();
        var flattened = string.Join("\n", cells.Select(cell => cell.Value));

        Assert.DoesNotContain(PlantedSin, flattened, StringComparison.Ordinal);
        Assert.DoesNotContain("123-456-789", flattened, StringComparison.Ordinal);
        Assert.DoesNotContain(SourceMarker, flattened, StringComparison.Ordinal);

        var provider = Assert.Single(
            cells,
            cell => cell.Value.Contains(ProviderMarker, StringComparison.Ordinal));
        var patient = Assert.Single(
            cells,
            cell => cell.Value.Contains(PatientMarker, StringComparison.Ordinal));
        Assert.Equal(("MedicalEvidenceVersions", "ProviderDisplayName"),
            (provider.Table, provider.Column));
        Assert.Equal(("MedicalEvidenceVersions", "PatientDisplayName"),
            (patient.Table, patient.Column));

        var audits = cells.Where(cell =>
            cell.Table is "ExtractionAuditEvents" or "IntakeAuditEvents").ToArray();
        Assert.DoesNotContain(audits, cell =>
            cell.Value.Contains(ProviderMarker, StringComparison.Ordinal) ||
            cell.Value.Contains(PatientMarker, StringComparison.Ordinal) ||
            cell.Value.Contains(PlantedSin, StringComparison.Ordinal) ||
            cell.Value.Contains(SourceMarker, StringComparison.Ordinal));
    }

    private sealed class RelationalJourney : IAsyncDisposable
    {
        private readonly RelationalTestContext _database;
        private readonly TaxAiDbContext _context;

        private RelationalJourney(
            RelationalTestContext database,
            TaxAiDbContext context,
            SqlIntakeStore intake,
            SqlExtractionStore extractions,
            SqlReviewRecordRepository reviews,
            ExtractionIngestWorkflow ingest,
            ExtractionReviewWorkflow review,
            JourneyContentStore source,
            Guid caseId,
            Guid t4DocumentId,
            Guid medicalDocumentId)
        {
            _database = database;
            _context = context;
            Intake = intake;
            Extractions = extractions;
            Reviews = reviews;
            Ingest = ingest;
            Review = review;
            Source = source;
            CaseId = caseId;
            T4DocumentId = t4DocumentId;
            MedicalDocumentId = medicalDocumentId;
        }

        internal TrustedFirmId Firm { get; } =
            TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);
        internal Guid Actor { get; } =
            Guid.Parse("10101010-1010-4010-8010-101010101010");
        internal DateTimeOffset Now { get; } =
            new(2028, 2, 3, 4, 5, 6, TimeSpan.Zero);
        internal Guid CaseId { get; }
        internal Guid T4DocumentId { get; }
        internal Guid MedicalDocumentId { get; }
        internal SqlIntakeStore Intake { get; }
        internal SqlExtractionStore Extractions { get; }
        internal SqlReviewRecordRepository Reviews { get; }
        internal ExtractionIngestWorkflow Ingest { get; }
        internal ExtractionReviewWorkflow Review { get; }
        internal JourneyContentStore Source { get; }

        internal static async Task<RelationalJourney> CreateAsync()
        {
            var database = await RelationalTestContext.CreateAsync();
            var context = database.CreateContext();
            context.Add(SyntheticFixtures.Firm());
            await context.SaveChangesAsync();

            var intake = new SqlIntakeStore(context);
            var extractions = new SqlExtractionStore(context);
            var reviews = new SqlReviewRecordRepository(context);
            var credentials = new DeterministicCredentialSource();
            var intakeOptions = new IntakeWorkflowOptions(
                TimeSpan.FromHours(2),
                TimeSpan.FromMinutes(30),
                1024 * 1024,
                1);
            var accountant = new AccountantIntakeWorkflow(
                intake,
                credentials,
                reviews,
                intakeOptions);
            var source = new JourneyContentStore(T4Source(), MedicalSource());
            var client = new ClientIntakeWorkflow(
                intake,
                source,
                credentials,
                intakeOptions);

            var firm = TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);
            var actor = Guid.Parse("10101010-1010-4010-8010-101010101010");
            var now = new DateTimeOffset(2028, 2, 3, 4, 5, 6, TimeSpan.Zero);
            var created = await accountant.CreateAsync(
                firm,
                actor,
                new CreateIntakeCaseCommand(
                    "Synthetic Employee",
                    2025,
                    1,
                    1),
                now.AddMinutes(-10),
                CancellationToken.None);
            Assert.True(created.IsSuccess);
            var inspected = await client.InspectLinkAsync(
                created.Value!.LinkCredential,
                now.AddMinutes(-9),
                CancellationToken.None);
            Assert.True(inspected.IsSuccess);
            var redeemed = await client.RedeemLinkAsync(
                inspected.Value!.Proof,
                now.AddMinutes(-8),
                CancellationToken.None);
            Assert.True(redeemed.IsSuccess);
            var session = redeemed.Value!.SessionCredential;
            Assert.True((await client.SaveAnswersAsync(
                session,
                new SaveIntakeAnswersCommand(
                [
                    new IntakeAnswerInput(
                        DocumentCategory.T4Slip,
                        AvailabilityAnswerCode.AvailableNow),
                    new IntakeAnswerInput(
                        DocumentCategory.MedicalExpenseEvidence,
                        AvailabilityAnswerCode.AvailableNow),
                ]),
                now.AddMinutes(-7),
                CancellationToken.None)).IsSuccess);
            Assert.True((await UploadAsync(
                client,
                session,
                DocumentCategory.T4Slip,
                now.AddMinutes(-6))).IsSuccess);
            Assert.True((await UploadAsync(
                client,
                session,
                DocumentCategory.MedicalExpenseEvidence,
                now.AddMinutes(-5))).IsSuccess);
            Assert.True((await client.SubmitAsync(
                session,
                now.AddMinutes(-4),
                CancellationToken.None)).IsSuccess);

            var loadedIntake = await intake.FindAsync(
                firm,
                created.Value.CaseId,
                CancellationToken.None);
            Assert.NotNull(loadedIntake);
            var t4DocumentId = Assert.Single(
                loadedIntake!.Documents,
                document => document.Category == DocumentCategory.T4Slip).DocumentId;
            var medicalDocumentId = Assert.Single(
                loadedIntake.Documents,
                document => document.Category ==
                    DocumentCategory.MedicalExpenseEvidence).DocumentId;
            Assert.NotNull(await extractions.FindCaseAsync(
                firm,
                created.Value.CaseId,
                CancellationToken.None));
            var provider = new DeterministicInProcessProvider();
            var ingress = new ExtractionWorkflow.Services.DocumentExtractionIngress();
            var processor = new T4CaseProcessor(new EmbeddedT4LineMapProvider());
            var identifiers = new SequentialIdentifierSource();
            var options = new ExtractionWorkflowOptions
            {
                ProviderCallLimit = TimeSpan.FromSeconds(5),
            };

            return new RelationalJourney(
                database,
                context,
                intake,
                extractions,
                reviews,
                new ExtractionIngestWorkflow(
                    intake,
                    extractions,
                    source,
                    provider,
                    ingress,
                    processor,
                    identifiers,
                    options),
                new ExtractionReviewWorkflow(
                    intake,
                    accountant,
                    extractions,
                    source,
                    provider,
                    ingress,
                    processor,
                    reviews,
                    identifiers,
                    options),
                source,
                created.Value.CaseId,
                t4DocumentId,
                medicalDocumentId);
        }

        private static async Task<IntakeResult<ClientIntakeView>> UploadAsync(
            ClientIntakeWorkflow client,
            IntakeSecret session,
            DocumentCategory category,
            DateTimeOffset nowUtc)
        {
            var bytes = Encoding.UTF8.GetBytes(
                $"%PDF-1.7\nsynthetic upload {category}");
            using var stream = new MemoryStream(bytes, writable: false);
            return await client.UploadAsync(
                session,
                new IntakeUploadCommand(
                    category,
                    1,
                    IntakeMediaKind.Pdf,
                    bytes.LongLength),
                stream,
                nowUtc,
                CancellationToken.None);
        }

        internal async Task<IReadOnlyList<StoredCell>> ReadEveryCellAsync()
        {
            var connection = _context.Database.GetDbConnection();
            await _context.Database.OpenConnectionAsync();
            var tables = new List<string>();

            await using (var tableCommand = connection.CreateCommand())
            {
                tableCommand.CommandText =
                    "SELECT name FROM sqlite_master " +
                    "WHERE type = 'table' AND name NOT LIKE 'sqlite_%' " +
                    "ORDER BY name;";
                await using var reader = await tableCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            var cells = new List<StoredCell>();
            foreach (var table in tables)
            {
                var columns = await ColumnsAsync(connection, table);
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table}\";";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        if (!reader.IsDBNull(index))
                        {
                            cells.Add(new StoredCell(
                                table,
                                columns[index],
                                Convert.ToString(reader.GetValue(index),
                                    System.Globalization.CultureInfo.InvariantCulture)
                                ?? string.Empty));
                        }
                    }
                }
            }

            return cells;
        }

        private static async Task<string[]> ColumnsAsync(
            DbConnection connection,
            string table)
        {
            var columns = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }

            return columns.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    private static byte[] T4Source() => Encoding.UTF8.GetBytes(string.Join('\n',
        DeterministicInProcessProvider.Header,
        "KIND: T4",
        "CONFIDENCE: 0.75",
        "YEAR: 2025",
        "EMPLOYER-NAME: Synthetic Employer",
        "EMPLOYEE-NAME: Synthetic Employee",
        $"BOX 12: {PlantedSin}",
        "BOX 14: 50000.00",
        "BOX 45: 1",
        SourceMarker));

    private static byte[] MedicalSource() => Encoding.UTF8.GetBytes(string.Join('\n',
        DeterministicInProcessProvider.Header,
        "KIND: MEDICAL-EXPENSE-EVIDENCE",
        "CONFIDENCE: 0.50",
        "SERVICE-DATE: 14 mars 2025",
        "GROSS-AMOUNT: $120.50",
        "CURRENCY: CAD",
        $"PROVIDER-NAME: {ProviderMarker}",
        $"PATIENT-NAME: {PatientMarker}",
        "RECEIPT-REFERENCE: R-015-T07",
        SourceMarker));

    private sealed class JourneyContentStore(
        byte[] t4,
        byte[] medical) : IIntakeContentStore, IExtractionContentReader
    {
        private readonly Dictionary<string, (TrustedFirmId Firm, Guid CaseId, byte[] Bytes)>
            _content = [];
        private int _staged;

        internal int OpenCount { get; private set; }

        public async Task<StagedIntakeContent> StageAsync(
            TrustedFirmId firm,
            Guid caseId,
            Stream content,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            using var received = new MemoryStream();
            await content.CopyToAsync(received, cancellationToken);
            Assert.InRange(received.Length, 1, maximumBytes);
            var selected = _staged++ == 0 ? t4 : medical;
            var key = $"content-{_staged}";
            _content.Add(key, (firm, caseId, selected));
            return new StagedIntakeContent(
                key,
                received.Length,
                SHA256.HashData(received.ToArray()));
        }

        public Task DeleteIfExistsAsync(
            TrustedFirmId firm,
            Guid caseId,
            string contentKey,
            CancellationToken cancellationToken)
        {
            _content.Remove(contentKey);
            return Task.CompletedTask;
        }

        public Task<ExtractionSourceLease?> OpenAsync(
            TrustedFirmId firm,
            Guid caseId,
            Guid documentId,
            string contentKey,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            return Task.FromResult(
                _content.TryGetValue(contentKey, out var stored) &&
                stored.Firm.Equals(firm) &&
                stored.CaseId == caseId
                    ? ExtractionSourceLease.Create(
                        new MemoryStream(stored.Bytes, writable: false))
                    : null);
        }
    }

    private sealed class DeterministicInProcessProvider : IDocumentIntelligenceProvider
    {
        internal const string Header = "TAXAI-DETERMINISTIC-DOCUMENT/1";

        public async Task<ProviderClassificationResult?> ClassifyAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            var text = await ReadAsync(source, cancellationToken);
            return new ProviderClassificationResult
            {
                Classification = text.Contains(
                    "KIND: T4", StringComparison.Ordinal)
                    ? DocumentClassification.T4Slip
                    : text.Contains(
                        "KIND: MEDICAL-EXPENSE-EVIDENCE",
                        StringComparison.Ordinal)
                        ? DocumentClassification.MedicalExpenseEvidence
                        : DocumentClassification.Unsupported,
                Confidence = text.Contains("CONFIDENCE: 0.75", StringComparison.Ordinal)
                    ? 0.75m
                    : 0.50m,
                ProviderLabel = "test-deterministic",
            };
        }

        public async Task<ProviderT4Result?> ExtractT4Async(
            Stream source,
            CancellationToken cancellationToken)
        {
            var text = await ReadAsync(source, cancellationToken);
            if (!text.Contains("KIND: T4", StringComparison.Ordinal))
            {
                return null;
            }

            return new ProviderT4Result
            {
                Year = 2025,
                EmployerName = "Synthetic Employer",
                EmployeeName = "Synthetic Employee",
                Boxes =
                [
                    new ProviderBoxValue
                    {
                        Code = T4BoxCodes.SocialInsuranceNumber,
                        Value = PlantedSin,
                    },
                    new ProviderBoxValue { Code = "14", Value = "50000.00" },
                    new ProviderBoxValue { Code = "45", Value = "1" },
                ],
                Confidence = 0.75m,
            };
        }

        public async Task<ProviderMedicalResult?> ExtractMedicalAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            var text = await ReadAsync(source, cancellationToken);
            if (!text.Contains(
                    "KIND: MEDICAL-EXPENSE-EVIDENCE",
                    StringComparison.Ordinal))
            {
                return null;
            }

            return new ProviderMedicalResult
            {
                ServiceDateAsPrinted = "14 mars 2025",
                GrossAmountAsPrinted = "$120.50",
                CurrencyCodeAsPrinted = "CAD",
                ProviderDisplayName = ProviderMarker,
                PatientDisplayName = PatientMarker,
                ReceiptReference = "R-015-T07",
                Confidence = 0.50m,
            };
        }

        private static async Task<string> ReadAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(
                source,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }

    private sealed class SequentialIdentifierSource : IOpaqueIdentifierSource
    {
        private int _next;

        public Guid NewIdentifier()
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(++_next).CopyTo(bytes, 0);
            bytes[7] = 0x40;
            bytes[8] = 0x80;
            return new Guid(bytes);
        }
    }

    private sealed class DeterministicCredentialSource : IIntakeCredentialSource
    {
        private int _next;

        public Guid CreateIdentifier() => Guid.NewGuid();

        public IntakeSecret CreateCredential()
        {
            var bytes = Enumerable.Repeat((byte)0x31, 32).ToArray();
            bytes[^1] = (byte)++_next;
            var encoded = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            Assert.True(IntakeSecret.TryParse(encoded, out var credential));
            return credential!;
        }

        public IntakeTokenDigest Digest(
            IntakeSecret credential,
            IntakeTokenPurpose purpose)
        {
            var material = Encoding.UTF8.GetBytes(
                $"{purpose}:{credential.Reveal()}");
            return new IntakeTokenDigest(
                HMACSHA256.HashData(new byte[32], material));
        }
    }

    private sealed record StoredCell(string Table, string Column, string Value);
}
