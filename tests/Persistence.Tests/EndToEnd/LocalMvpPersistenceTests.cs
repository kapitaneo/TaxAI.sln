using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CaseProcessing.Services;
using ClientIntake.Contracts;
using ClientIntake.Models;
using ClientIntake.Services;
using ExtractionWorkflow.Contracts;
using ExtractionWorkflow.Models;
using ExtractionWorkflow.Services;
using Microsoft.EntityFrameworkCore;
using Persistence.Repositories;
using Persistence.Tests.Support;
using TaxCases.Models;
using TaxLogicEngine.Services;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Tests.EndToEnd;

/// <summary>
/// Spec 017 T04 — the relational half of the local journey. The real intake and
/// extraction workflows run through the real <see cref="SqlIntakeStore"/>,
/// <see cref="SqlExtractionStore"/> and <see cref="SqlReviewRecordRepository"/>
/// against a real relational database with the repository's migrations applied.
/// <para>
/// The source is the shape the local sample generator writes, and it is the
/// bytes the client workflow was given: the in-process provider reads the
/// recipient identifier out of those bytes, so the value genuinely travels the
/// chain before every written column is read back.
/// </para>
/// <para>
/// The recipient-identifier value is assembled at run time. No nine-digit
/// literal exists in this file.
/// </para>
/// </summary>
public sealed class LocalMvpPersistenceTests
{
    private const string Header = "TAXAI-DETERMINISTIC-DOCUMENT/1";

    private const string TaxpayerName = "Synthetic Demo Taxpayer";

    private static readonly Regex NineDigitRun = new(
        "[0-9]{9}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [Fact] // FR-30, FR-47: the journey ends in one review, and the list read finds the case.
    public async Task TheLocalJourneyEndsInOneReviewRecordAndTheCaseIsListedForItsFirmOnly()
    {
        await using var journey = await RelationalJourney.CreateAsync(NewRecipientIdentifier());

        var reviewRecordId = await journey.RunToReviewAsync();

        // The firm-scoped list read, through the real store: exactly this case,
        // by display name and stored tax year.
        var listed = await journey.Intake.ListCasesAsync(journey.Firm, 100, CancellationToken.None);
        var entry = Assert.Single(listed.Cases);
        var stored = await journey.Intake.FindAsync(journey.Firm, journey.CaseId, CancellationToken.None);
        Assert.Equal(journey.CaseId, entry.CaseId);
        Assert.Equal(TaxpayerName, entry.PrimaryTaxpayerDisplayName);
        Assert.Equal(stored!.TaxYear, entry.TaxYear);
        Assert.False(listed.HasMore);

        // Another firm sees nothing, and cannot open the case or its review.
        var other = TrustedFirmId.FromGuid(SyntheticFixtures.OtherFirmId);
        Assert.Empty((await journey.Intake.ListCasesAsync(other, 100, CancellationToken.None)).Cases);
        Assert.Null(await journey.Intake.FindAsync(other, journey.CaseId, CancellationToken.None));
        Assert.Null(await journey.Reviews.FindAsync(other, reviewRecordId, CancellationToken.None));

        // One review record, associated with the case, carrying the corrected
        // value the existing chain produced.
        Assert.Equal(1, await journey.ReviewRecordCountAsync());
        Assert.Equal(reviewRecordId, stored.ReviewAssociation!.RecordId);
        var record = await journey.Reviews.FindAsync(journey.Firm, reviewRecordId, CancellationToken.None);
        var slip = Assert.Single(record!.Slips);
        Assert.Equal("T4", slip.SlipTypeCode);
        Assert.Contains(slip.BoxAmounts, box => box.BoxCode == "14" && box.Amount == 51000.00m);
        Assert.NotEmpty(slip.LineEntries);

        // The correction is a second immutable version beside the first.
        var document = await journey.Extractions.FindDocumentAsync(
            journey.Firm, journey.CaseId, journey.T4DocumentId, CancellationToken.None);
        Assert.Equal(2, document!.Versions.Count);
        Assert.Single(document.Corrections);
    }

    [Fact] // AC-56, relational half
    public async Task ThePlantedIdentifierReachesNoColumnOfAnyRowTheJourneyWrote()
    {
        var planted = NewRecipientIdentifier();
        await using var journey = await RelationalJourney.CreateAsync(planted);

        await journey.RunToReviewAsync();

        // The identifier really was in the uploaded bytes the provider read.
        Assert.Contains(planted, Encoding.UTF8.GetString(journey.Source.Single()), StringComparison.Ordinal);
        Assert.True(journey.Source.OpenCount > 0);

        var cells = await journey.ReadEveryCellAsync();

        // Every table the journey writes holds rows, so the scan is not vacuous.
        var populated = cells.Select(cell => cell.Table).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var table in new[]
                 {
                     "IntakeCases", "IntakeDocuments", "IntakeAuditEvents", "IntakeSubmissions",
                     "ExtractionVersions", "ExtractionCorrections", "ExtractionAcceptances",
                     "ExtractionAuditEvents", "PersistedReviewRecords", "PersistedSlips",
                     "PersistedBoxAmounts", "PersistedLineEntries",
                 })
        {
            Assert.Contains(table, populated);
        }

        // Control: the cell reader would show the value, as text or as bytes.
        Assert.Contains(planted, RelationalJourney.CellText(planted), StringComparison.Ordinal);
        Assert.Contains(planted, RelationalJourney.CellText(Encoding.UTF8.GetBytes(planted)), StringComparison.Ordinal);
        Assert.Contains(planted, RelationalJourney.CellText(Encoding.Unicode.GetBytes(planted)), StringComparison.Ordinal);

        var grouped = $"{planted[..3]}-{planted[3..6]}-{planted[6..]}";
        var spaced = $"{planted[..3]} {planted[3..6]} {planted[6..]}";
        var masked = new Regex(
            @"[*#•xX]{2,}[ \-]?[*#•xX]{0,3}[ \-]?(?:" +
            Regex.Escape(planted[^4..]) + "|" + Regex.Escape(planted[^3..]) + @")(?![0-9])",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        Assert.True(cells.Count > 100);
        foreach (var cell in cells)
        {
            var where = $"{cell.Table}.{cell.Column}";
            Assert.False(cell.Value.Contains(planted, StringComparison.Ordinal), where);
            Assert.False(cell.Value.Contains(grouped, StringComparison.Ordinal), where);
            Assert.False(cell.Value.Contains(spaced, StringComparison.Ordinal), where);
            Assert.False(cell.Value.Contains(Header, StringComparison.Ordinal), where);
            Assert.False(masked.IsMatch(cell.Value), where);

            // Any other nine-digit rendering. An opaque identifier is random
            // hexadecimal, so a run of digits inside one is chance, not a value.
            if (!cell.IsOpaqueIdentifier)
            {
                Assert.False(NineDigitRun.IsMatch(cell.Value), where);
                Assert.False(cell.Value.Contains(planted[..6], StringComparison.Ordinal), where);
                Assert.False(cell.Value.Contains(planted[^6..], StringComparison.Ordinal), where);
            }
        }

        // No column exists to hold one, either.
        Assert.DoesNotContain(
            cells.Select(cell => cell.Column).Distinct(StringComparer.OrdinalIgnoreCase),
            column => column.Contains("Sin", StringComparison.Ordinal) ||
                      column.Contains("SocialInsurance", StringComparison.OrdinalIgnoreCase) ||
                      column.Contains("RecipientIdentifier", StringComparison.OrdinalIgnoreCase) ||
                      column.Contains("Box12", StringComparison.OrdinalIgnoreCase));
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>Assembled at run time; never a literal in a tracked file.</summary>
    private static string NewRecipientIdentifier() =>
        string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(0, 10)));

    /// <summary>The shape the local sample generator writes.</summary>
    private static byte[] T4Source(string recipientIdentifier) => Encoding.UTF8.GetBytes(string.Join(
        '\n',
        "%PDF-1.7",
        Header,
        "KIND: T4",
        "YEAR: 2025",
        "EMPLOYER-NAME: Synthetic Employer Ltd.",
        "EMPLOYEE-NAME: Synthetic Employee",
        $"BOX 12: {recipientIdentifier}",
        "BOX 14: 50000.00",
        "BOX 45: 1") + "\n");

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
            UploadedSources source,
            Guid caseId,
            Guid t4DocumentId)
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
        }

        internal TrustedFirmId Firm { get; } = TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);

        internal Guid Actor { get; } = Guid.Parse("10101010-1010-4010-8010-101010101010");

        internal DateTimeOffset Now { get; } = new(2028, 2, 3, 4, 5, 6, TimeSpan.Zero);

        internal Guid CaseId { get; }

        internal Guid T4DocumentId { get; }

        internal SqlIntakeStore Intake { get; }

        internal SqlExtractionStore Extractions { get; }

        internal SqlReviewRecordRepository Reviews { get; }

        internal ExtractionIngestWorkflow Ingest { get; }

        internal ExtractionReviewWorkflow Review { get; }

        internal UploadedSources Source { get; }

        /// <summary>
        /// Creates the case and drives the client half: redeem, answer, upload
        /// the bridge-format source, submit.
        /// </summary>
        internal static async Task<RelationalJourney> CreateAsync(string recipientIdentifier)
        {
            var database = await RelationalTestContext.CreateAsync();
            var context = database.CreateContext();
            context.Add(SyntheticFixtures.Firm());
            context.Add(SyntheticFixtures.Firm(SyntheticFixtures.OtherFirmId, "Example Firm B"));
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
            var accountant = new AccountantIntakeWorkflow(intake, credentials, reviews, intakeOptions);
            var source = new UploadedSources();
            var client = new ClientIntakeWorkflow(intake, source, credentials, intakeOptions);

            var firm = TrustedFirmId.FromGuid(SyntheticFixtures.FirmId);
            var actor = Guid.Parse("10101010-1010-4010-8010-101010101010");
            var now = new DateTimeOffset(2028, 2, 3, 4, 5, 6, TimeSpan.Zero);

            var created = await accountant.CreateAsync(
                firm,
                actor,
                new CreateIntakeCaseCommand(TaxpayerName, 2025, 1, 0),
                now.AddMinutes(-10),
                CancellationToken.None);
            Assert.True(created.IsSuccess);
            var inspected = await client.InspectLinkAsync(
                created.Value!.LinkCredential, now.AddMinutes(-9), CancellationToken.None);
            Assert.True(inspected.IsSuccess);
            var redeemed = await client.RedeemLinkAsync(
                inspected.Value!.Proof, now.AddMinutes(-8), CancellationToken.None);
            Assert.True(redeemed.IsSuccess);
            var session = redeemed.Value!.SessionCredential;
            Assert.True((await client.SaveAnswersAsync(
                session,
                new SaveIntakeAnswersCommand(
                [
                    new IntakeAnswerInput(DocumentCategory.T4Slip, AvailabilityAnswerCode.AvailableNow),
                ]),
                now.AddMinutes(-7),
                CancellationToken.None)).IsSuccess);

            var bytes = T4Source(recipientIdentifier);
            using (var stream = new MemoryStream(bytes, writable: false))
            {
                Assert.True((await client.UploadAsync(
                    session,
                    new IntakeUploadCommand(DocumentCategory.T4Slip, 1, IntakeMediaKind.Pdf, bytes.LongLength),
                    stream,
                    now.AddMinutes(-6),
                    CancellationToken.None)).IsSuccess);
            }

            Assert.True((await client.SubmitAsync(session, now.AddMinutes(-4), CancellationToken.None)).IsSuccess);

            var loaded = await intake.FindAsync(firm, created.Value.CaseId, CancellationToken.None);
            var t4DocumentId = Assert.Single(loaded!.Documents).DocumentId;

            var provider = new BridgeFormatProvider();
            var ingress = new DocumentExtractionIngress();
            var processor = new T4CaseProcessor(new EmbeddedT4LineMapProvider());
            var identifiers = new SequentialIdentifierSource();
            var options = new ExtractionWorkflowOptions { ProviderCallLimit = TimeSpan.FromSeconds(5) };

            return new RelationalJourney(
                database,
                context,
                intake,
                extractions,
                reviews,
                new ExtractionIngestWorkflow(
                    intake, extractions, source, provider, ingress, processor, identifiers, options),
                new ExtractionReviewWorkflow(
                    intake, accountant, extractions, source, provider, ingress, processor, reviews,
                    identifiers, options),
                source,
                created.Value.CaseId,
                t4DocumentId);
        }

        /// <summary>The accountant half: extract, correct, accept, finalize.</summary>
        internal async Task<Guid> RunToReviewAsync()
        {
            var extracted = await Ingest.ExtractAsync(
                Firm,
                Actor,
                new ExtractDocumentCommand { CaseId = CaseId, DocumentId = T4DocumentId },
                Now,
                CancellationToken.None);
            Assert.True(extracted.IsSuccess, extracted.Code.ToString());
            Assert.True(extracted.Value!.IsEngineAccepted);

            Assert.True((await Review.CorrectAsync(
                Firm,
                Actor,
                new ApplyExtractionCorrectionCommand
                {
                    CaseId = CaseId,
                    DocumentId = T4DocumentId,
                    Field = CorrectionField.T4BoxValue,
                    BoxCode = "14",
                    CorrectedValue = "51000.00",
                },
                Now.AddMinutes(1),
                CancellationToken.None)).IsSuccess);

            Assert.True((await Review.AcceptDocumentAsync(
                Firm,
                Actor,
                new AcceptExtractionDocumentCommand { CaseId = CaseId, DocumentId = T4DocumentId },
                Now.AddMinutes(2),
                CancellationToken.None)).IsSuccess);

            var finalized = await Review.FinalizeCaseAsync(
                Firm,
                Actor,
                new FinalizeExtractionCaseCommand { CaseId = CaseId },
                Now.AddMinutes(3),
                CancellationToken.None);
            Assert.True(finalized.IsSuccess, finalized.Code.ToString());
            return finalized.Value!.ReviewRecordId;
        }

        internal async Task<long> ReviewRecordCountAsync()
        {
            await _context.Database.OpenConnectionAsync();
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM PersistedReviewRecords;";
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
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

            // The provider's own migration bookkeeping is not a row the journey
            // wrote, and its keys are timestamps.
            tables.RemoveAll(table => table.StartsWith("__EF", StringComparison.Ordinal));

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
                            var value = reader.GetValue(index);
                            cells.Add(new StoredCell(
                                table,
                                columns[index],
                                CellText(value),
                                IsOpaqueIdentifier(value)));
                        }
                    }
                }
            }

            return cells;
        }

        internal static string CellText(object value) => value is byte[] bytes
            ? string.Join("\n", Encoding.UTF8.GetString(bytes), Encoding.Unicode.GetString(bytes))
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        private static bool IsOpaqueIdentifier(object value) =>
            value switch
            {
                Guid => true,
                byte[] bytes => bytes.Length == 16,
                string text => Guid.TryParseExact(text, "D", out _),
                _ => false,
            };

        private static async Task<string[]> ColumnsAsync(DbConnection connection, string table)
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

    /// <summary>
    /// Holds exactly the bytes the client workflow uploaded and hands the same
    /// bytes back to the extraction workflow, scoped by firm and case. It is the
    /// only stand-in here: the real adapter writes encrypted files outside any
    /// database, which is the one place a recipient identifier may remain.
    /// </summary>
    private sealed class UploadedSources : IIntakeContentStore, IExtractionContentReader
    {
        private readonly Dictionary<string, (TrustedFirmId Firm, Guid CaseId, byte[] Bytes)> _content = [];

        internal int OpenCount { get; private set; }

        internal byte[] Single() => Assert.Single(_content.Values).Bytes;

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
            var key = $"content-{_content.Count + 1}";
            _content.Add(key, (firm, caseId, received.ToArray()));
            return new StagedIntakeContent(key, received.Length, SHA256.HashData(received.ToArray()));
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
                    ? ExtractionSourceLease.Create(new MemoryStream(stored.Bytes, writable: false))
                    : null);
        }
    }

    /// <summary>
    /// Reads the bridge format line by line and copies values as printed. This
    /// test project does not reference the Development provider; the format is
    /// the same, and nothing here decides anything about tax.
    /// </summary>
    private sealed class BridgeFormatProvider : IDocumentIntelligenceProvider
    {
        public async Task<ProviderClassificationResult?> ClassifyAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            var lines = await ReadAsync(source, cancellationToken);
            return new ProviderClassificationResult
            {
                Classification = Value(lines, "KIND") == "T4"
                    ? DocumentClassification.T4Slip
                    : DocumentClassification.Unsupported,
                ProviderLabel = "test-bridge-format",
            };
        }

        public async Task<ProviderT4Result?> ExtractT4Async(
            Stream source,
            CancellationToken cancellationToken)
        {
            var lines = await ReadAsync(source, cancellationToken);
            if (Value(lines, "KIND") != "T4")
            {
                return null;
            }

            return new ProviderT4Result
            {
                Year = int.Parse(Value(lines, "YEAR")!, CultureInfo.InvariantCulture),
                EmployerName = Value(lines, "EMPLOYER-NAME"),
                EmployeeName = Value(lines, "EMPLOYEE-NAME"),
                Boxes = lines
                    .Where(line => line.Key.StartsWith("BOX ", StringComparison.Ordinal))
                    .Select(line => new ProviderBoxValue
                    {
                        Code = line.Key["BOX ".Length..].Trim(),
                        Value = line.Value,
                    })
                    .ToArray(),
            };
        }

        public Task<ProviderMedicalResult?> ExtractMedicalAsync(
            Stream source,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProviderMedicalResult?>(null);

        private static string? Value(IReadOnlyList<KeyValuePair<string, string>> lines, string key) =>
            lines.Where(line => line.Key == key).Select(line => line.Value).FirstOrDefault();

        private static async Task<IReadOnlyList<KeyValuePair<string, string>>> ReadAsync(
            Stream source,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(source, Encoding.UTF8, false, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.StartsWith("%PDF-", lines[0], StringComparison.Ordinal);
            Assert.Equal(Header, lines[1]);

            return lines
                .Skip(2)
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .Select(parts => new KeyValuePair<string, string>(parts[0].Trim(), parts[1].Trim()))
                .ToArray();
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

        public IntakeTokenDigest Digest(IntakeSecret credential, IntakeTokenPurpose purpose)
        {
            var material = Encoding.UTF8.GetBytes($"{purpose}:{credential.Reveal()}");
            return new IntakeTokenDigest(HMACSHA256.HashData(new byte[32], material));
        }
    }

    private sealed record StoredCell(string Table, string Column, string Value, bool IsOpaqueIdentifier);
}
