using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tenancy.Contracts;
using Tenancy.Models;
using Tenancy.Services;

namespace Persistence.Services;

/// <summary>
/// Writes one obviously synthetic, idempotent development review fixture.
/// <para>
/// It creates no user, membership, session, credential, or privileged
/// principal. Environment gating belongs to the host; this service performs no
/// environment read and is invoked only from that explicit development branch.
/// </para>
/// <para>
/// The fixed names, structural codes, amounts, and derived identifiers are
/// synthetic. They contain no real person, employer, account, contact, or
/// government identifier.
/// </para>
/// </summary>
internal sealed class DevelopmentFixtureSeeder : IDevelopmentFixtureSeeder
{
    private const string FirmDisplayName = "Example Firm A";
    private const string TaxpayerDisplayName = "Test Taxpayer 001";
    private const string EmployerDisplayName = "Example Employer A";
    private const string CaseId = "development_case";
    private const string ParticipantId = "development_participant";
    private const string BoxCode = "SYNTHETIC_BOX";
    private const string LineCode = "SYNTHETIC_LINE";
    private const string UnmappedBoxCode = "SYNTHETIC_OTHER";

    private static readonly DateTimeOffset CreatedAtUtc =
        new(DateTime.UnixEpoch.AddYears(55).AddDays(1));

    private static readonly Guid FirmId =
        DeterministicIdentifier("taxai-development-fixture-firm");
    private static readonly Guid RecordId =
        DeterministicIdentifier("taxai-development-fixture-record");
    private static readonly Guid SlipId =
        DeterministicIdentifier("taxai-development-fixture-slip");
    private static readonly Guid BoxAmountId =
        DeterministicIdentifier("taxai-development-fixture-box-amount");
    private static readonly Guid LineEntryId =
        DeterministicIdentifier("taxai-development-fixture-line-entry");
    private static readonly Guid SourceBoxCodeId =
        DeterministicIdentifier("taxai-development-fixture-source-code");
    private static readonly Guid UnmappedBoxCodeId =
        DeterministicIdentifier("taxai-development-fixture-unmapped-code");
    private static readonly Guid DocumentReference =
        DeterministicIdentifier("taxai-development-fixture-document");

    private readonly TaxAiDbContext _context;

    public DevelopmentFixtureSeeder(TaxAiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var taxYear = CreatedAtUtc.Year;
        var retentionUntilUtc =
            RetentionPolicy.ComputeRetentionUntilUtc(taxYear);

        await using var transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [Firms]
                ([FirmId], [DisplayName], [CreatedAtUtc], [UpdatedAtUtc])
            SELECT
                {FirmId}, {FirmDisplayName}, {CreatedAtUtc}, {CreatedAtUtc}
            WHERE NOT EXISTS
                (SELECT 1 FROM [Firms] WHERE [FirmId] = {FirmId});
            """,
            cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [PersistedReviewRecords]
                ([RecordId], [FirmId], [CaseId], [ParticipantId], [TaxYear],
                 [PackageVersion], [CreatedAtUtc], [UpdatedAtUtc],
                 [RetentionUntilUtc])
            SELECT
                {RecordId}, {FirmId}, {CaseId}, {ParticipantId}, {taxYear},
                {1}, {CreatedAtUtc}, {CreatedAtUtc}, {retentionUntilUtc}
            WHERE NOT EXISTS
                (SELECT 1 FROM [PersistedReviewRecords]
                 WHERE [RecordId] = {RecordId});
            """,
            cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [PersistedSlips]
                ([SlipId], [FirmId], [RecordId], [Ordinal], [SlipTypeCode],
                 [DocumentReference], [TaxpayerDisplayName],
                 [EmployerDisplayName])
            SELECT
                {SlipId}, {FirmId}, {RecordId}, {0}, {SlipTypeCodes.T4},
                {DocumentReference}, {TaxpayerDisplayName},
                {EmployerDisplayName}
            WHERE NOT EXISTS
                (SELECT 1 FROM [PersistedSlips] WHERE [SlipId] = {SlipId});
            """,
            cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [PersistedBoxAmounts]
                ([BoxAmountId], [FirmId], [SlipId], [Ordinal], [BoxCode],
                 [Amount])
            SELECT
                {BoxAmountId}, {FirmId}, {SlipId}, {0}, {BoxCode}, {1.25m}
            WHERE NOT EXISTS
                (SELECT 1 FROM [PersistedBoxAmounts]
                 WHERE [BoxAmountId] = {BoxAmountId});
            """,
            cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [PersistedLineEntries]
                ([LineEntryId], [FirmId], [SlipId], [Ordinal], [LineCode],
                 [Amount])
            SELECT
                {LineEntryId}, {FirmId}, {SlipId}, {0}, {LineCode}, {2.50m}
            WHERE NOT EXISTS
                (SELECT 1 FROM [PersistedLineEntries]
                 WHERE [LineEntryId] = {LineEntryId});
            """,
            cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [PersistedSourceBoxCodes]
                ([SourceBoxCodeId], [FirmId], [LineEntryId], [Ordinal],
                 [BoxCode])
            SELECT
                {SourceBoxCodeId}, {FirmId}, {LineEntryId}, {0}, {BoxCode}
            WHERE NOT EXISTS
                (SELECT 1 FROM [PersistedSourceBoxCodes]
                 WHERE [SourceBoxCodeId] = {SourceBoxCodeId});
            """,
            cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [PersistedUnmappedBoxCodes]
                ([UnmappedBoxCodeId], [FirmId], [SlipId], [Ordinal],
                 [BoxCode])
            SELECT
                {UnmappedBoxCodeId}, {FirmId}, {SlipId}, {0},
                {UnmappedBoxCode}
            WHERE NOT EXISTS
                (SELECT 1 FROM [PersistedUnmappedBoxCodes]
                 WHERE [UnmappedBoxCodeId] = {UnmappedBoxCodeId});
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Fixed type identity only; never fixture content.</summary>
    public override string ToString() => nameof(DevelopmentFixtureSeeder);

    private static Guid DeterministicIdentifier(string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(purpose));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
