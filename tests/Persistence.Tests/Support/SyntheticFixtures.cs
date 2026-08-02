using System.Reflection;
using ClientIntake.Models;
using TaxCases.Models;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Persistence.Tests.Support;

/// <summary>
/// Obviously synthetic, deterministic rows for relational tests.
/// No fixture declares a SIN-shaped field or value.
/// </summary>
internal static class SyntheticFixtures
{
    public static readonly Guid FirmId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1");

    public static readonly Guid OtherFirmId =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2");

    public static readonly Guid UserId =
        Guid.Parse("cccccccc-cccc-4ccc-8ccc-ccccccccccc3");

    public static readonly DateTimeOffset CreatedAtUtc =
        new(new DateTime(2028, 1, 2, 3, 4, 5, DateTimeKind.Utc));

    public static Firm Firm(
        Guid? firmId = null,
        string displayName = "Example Firm A") =>
        Construct<Firm>(
            firmId ?? FirmId,
            displayName,
            CreatedAtUtc,
            CreatedAtUtc);

    public static PortalUser User(
        Guid? userId = null,
        bool isActive = true) =>
        Construct<PortalUser>(
            userId ?? UserId,
            "https://identity.example.test",
            "synthetic-subject",
            isActive,
            CreatedAtUtc,
            CreatedAtUtc);

    public static FirmMembership Membership(
        Guid? firmId = null,
        Guid? userId = null,
        bool isActive = true) =>
        Construct<FirmMembership>(
            Guid.Parse("dddddddd-dddd-4ddd-8ddd-ddddddddddd4"),
            firmId ?? FirmId,
            userId ?? UserId,
            isActive,
            CreatedAtUtc,
            CreatedAtUtc);

    public static AuthenticationSession Session(
        Guid? activeFirmId = null,
        DateTimeOffset? revokedAtUtc = null) =>
        Construct<AuthenticationSession>(
            Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee5"),
            UserId,
            activeFirmId,
            CreatedAtUtc,
            CreatedAtUtc,
            CreatedAtUtc.AddMinutes(30),
            CreatedAtUtc.AddHours(8),
            revokedAtUtc);

    public static PersistedReviewRecord ReviewRecord(
        Guid? firmId = null,
        int packageVersion = 1)
    {
        var selectedFirmId = firmId ?? FirmId;
        var slipId = Guid.Parse("abababab-abab-4aba-8aba-abababababa6");
        var lineEntryId = Guid.Parse("bcbcbcbc-bcbc-4bcb-8bcb-bcbcbcbcbcb7");

        var sourceCode = Construct<PersistedSourceBoxCode>(
            Guid.Parse("cdcdcdcd-cdcd-4cdc-8cdc-cdcdcdcdcdc8"),
            selectedFirmId,
            lineEntryId,
            0,
            "B_A");
        var lineEntry = Construct<PersistedLineEntry>(
            lineEntryId,
            selectedFirmId,
            slipId,
            0,
            "L_A",
            2.005m,
            Array.AsReadOnly([sourceCode]));
        var boxAmount = Construct<PersistedBoxAmount>(
            Guid.Parse("dededede-dede-4ded-8ded-dededededed9"),
            selectedFirmId,
            slipId,
            0,
            "B_A",
            1.005m);
        var unmappedCode = Construct<PersistedUnmappedBoxCode>(
            Guid.Parse("efefefef-efef-4efe-8efe-efefefefefea"),
            selectedFirmId,
            slipId,
            0,
            "B_B");
        var recordId = Guid.Parse("acacacac-acac-4aca-8aca-acacacacacab");
        var slip = Construct<PersistedSlip>(
            slipId,
            selectedFirmId,
            recordId,
            0,
            SlipTypeCodes.T4,
            Guid.Parse("bdbdbdbd-bdbd-4bdb-8bdb-bdbdbdbdbdbc"),
            "Test Taxpayer 001",
            "Example Employer A",
            Array.AsReadOnly([boxAmount]),
            Array.AsReadOnly([lineEntry]),
            Array.AsReadOnly([unmappedCode]));

        return Construct<PersistedReviewRecord>(
            recordId,
            selectedFirmId,
            "case_alpha",
            "participant_alpha",
            2025,
            packageVersion,
            CreatedAtUtc,
            CreatedAtUtc,
            new DateTimeOffset(
                new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            Array.AsReadOnly([slip]));
    }

    public static NewIntakeCase IntakeCase(
        Guid? firmId = null,
        Guid? caseId = null,
        byte digestByte = 0x21)
    {
        var selectedFirm = TrustedFirmId.FromGuid(firmId ?? FirmId);
        var selectedCaseId = caseId ??
            Guid.Parse("12121212-1212-4212-8212-121212121212");
        var expectedItemId =
            Guid.Parse("13131313-1313-4313-8313-131313131313");
        var actor = Guid.Parse("14141414-1414-4414-8414-141414141414");
        var link = new IntakeLinkRecord(
            Guid.Parse("15151515-1515-4515-8515-151515151515"),
            Digest(digestByte),
            CreatedAtUtc,
            CreatedAtUtc.AddDays(2),
            null,
            null);
        var stored = new StoredIntakeCase(
            selectedFirm,
            selectedCaseId,
            Guid.Parse("16161616-1616-4616-8616-161616161616"),
            "Test Taxpayer Intake",
            2025,
            CaseStatus.InformationGathering,
            0,
            [new IntakeExpectedItemRecord(expectedItemId, DocumentCategory.T4Slip, 1)],
            link,
            [],
            [],
            [],
            [],
            null);

        return new NewIntakeCase(
            stored,
            [
                Audit(IntakeAuditKind.CaseCreated, actor),
                Audit(IntakeAuditKind.ChecklistConfirmed, actor),
                Audit(IntakeAuditKind.LinkIssued, actor),
            ]);
    }

    public static IntakeTokenDigest Digest(byte value) =>
        new(Enumerable.Repeat(value, 32).Select(candidate => (byte)candidate).ToArray());

    public static IntakeAuditEventRecord Audit(
        IntakeAuditKind kind,
        Guid actor,
        Guid? expectedItemId = null) =>
        new(
            Guid.NewGuid(),
            0,
            kind,
            IntakeActorKind.Client,
            actor,
            expectedItemId,
            CreatedAtUtc);

    private static T Construct<T>(params object?[] arguments)
    {
        var constructor = typeof(T)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == arguments.Length);

        return (T)constructor.Invoke(arguments);
    }
}
