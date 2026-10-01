using System.Text.Json;
using Tenancy.Contracts;
using Tenancy.Models;

namespace Tenancy.Tests.Production;

/// <summary>
/// Spec 016 T06 — the recovery control contracts and the pure release policy
/// (FR-10, FR-11; AC-103, AC-108, AC-111, AC-113 to AC-116, AC-165).
/// Every rule is exercised against synthetic facts only; no clock, store or
/// network is involved.
/// </summary>
public sealed class RecoveryControlContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly TrustedFirmId Firm = TrustedFirmId.FromGuid(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000071"));

    private static DatabaseIncarnation Incarnation(char fill = 'a') =>
        DatabaseIncarnation.TryCreate(new string(fill, 64), out var incarnation) ? incarnation! : throw new InvalidOperationException();

    private static RecoveryAuthorizationAttestation Attestation(DateTimeOffset at) =>
        new(at, [new RecoveryAuthorizedMembership(Firm, Guid.Parse("dddddddd-0000-4000-8000-000000000071"))]);

    private static RecoveryControlSnapshot Ledger(
        DateTimeOffset? genesisAt = null,
        DateTimeOffset? attestationAt = null,
        DateTimeOffset? readAt = null,
        int geneses = 1)
    {
        var records = Enumerable.Range(0, geneses)
            .Select(index => RecoveryControlRecord.Genesis(Incarnation((char)('a' + index)), genesisAt ?? Now.AddDays(-80)))
            .ToList();
        return new RecoveryControlSnapshot(
            RecoveryControlReadStatus.Complete,
            readAt ?? Now,
            records,
            attestationAt is null ? Attestation(Now.AddMinutes(-30)) : Attestation(attestationAt.Value));
    }

    private static RecoveryReleaseFacts Facts(
        RecoveryControlSnapshot? ledger = null,
        RecoveredDataExtent? extent = null,
        DatabaseIncarnation? incarnation = null,
        bool noIncarnation = false,
        int ledgerDays = 90,
        int restoreDays = 35,
        int chainDays = 7) =>
        new(
            noIncarnation ? null : incarnation ?? Incarnation('f'),
            ledger ?? Ledger(),
            extent ?? RecoveredDataExtent.Observed(true, Now.AddHours(-2)),
            ledgerDays,
            restoreDays,
            chainDays,
            Now);

    // ------------------------------------------------------------ windows

    [Fact] // AC-111: the approved retention is the 35-day maximum.
    public void ThePointInTimeRestoreRetentionIsThe35DayMaximum()
    {
        Assert.Equal(35, RecoveryRetentionWindow.PointInTimeRestoreDays);
        Assert.Equal(7, RecoveryRetentionWindow.BackupChainDays);
        Assert.Equal(TimeSpan.FromDays(42), RecoveryRetentionWindow.LongestRecoverableWindow(35, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryRetentionWindow.LongestRecoverableWindow(36, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryRetentionWindow.LongestRecoverableWindow(0, 7));
    }

    [Theory] // AC-103: the ledger outlives every recoverable backup, strictly.
    [InlineData(90, 35, 7, true)]
    [InlineData(43, 35, 7, true)]
    [InlineData(42, 35, 7, false)]
    [InlineData(41, 35, 7, false)]
    [InlineData(90, 35, 6, false)]
    [InlineData(366, 35, 7, false)]
    [InlineData(90, 36, 7, false)]
    [InlineData(90, 0, 7, false)]
    public void TheLedgerMustOutliveTheLongestRecoverableWindow(int ledger, int restore, int chain, bool expected) =>
        Assert.Equal(expected, RecoveryRetentionWindow.LedgerOutlivesBackups(ledger, restore, chain));

    // ------------------------------------------------------- incarnations

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void AnIncarnationIsExactlyALowerCaseSha256(string? candidate) =>
        Assert.False(DatabaseIncarnation.TryCreate(candidate, out _));

    [Fact]
    public void AnIncarnationComparesOrdinallyAndRendersStructurally()
    {
        Assert.True(Incarnation('a').SameAs(Incarnation('a')));
        Assert.False(Incarnation('a').SameAs(Incarnation('b')));
        Assert.Equal(nameof(DatabaseIncarnation), Incarnation().ToString());
        Assert.DoesNotContain("aaaa", JsonSerializer.Serialize(Incarnation()), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ records

    [Fact]
    public void EveryRecordShapeIsClosedAndSerializesNoIdentifier()
    {
        var terminal = RecoveryControlRecord.TerminalSource(Firm, Guid.NewGuid(), Guid.NewGuid(), Now);
        var json = JsonSerializer.Serialize(terminal);

        Assert.DoesNotContain(Firm.Value.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("RecoveryControlRecord(TerminalSource)", terminal.ToString());
        Assert.Throws<ArgumentException>(() => RecoveryControlRecord.TerminalSource(Firm, Guid.Empty, Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => RecoveryControlRecord.TerminalStructured(Firm, Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => RecoveryControlRecord.Genesis(Incarnation(), new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void RecordsAreTheSameFactOnlyWhenEveryPartMatches()
    {
        var resource = Guid.NewGuid();
        var a = RecoveryControlRecord.TerminalStructured(Firm, resource, Now);

        Assert.True(a.SameAs(RecoveryControlRecord.TerminalStructured(Firm, resource, Now)));
        Assert.False(a.SameAs(RecoveryControlRecord.TerminalStructured(Firm, resource, Now.AddTicks(1))));
        Assert.False(a.SameAs(RecoveryControlRecord.TerminalStructured(TrustedFirmId.FromGuid(Guid.NewGuid()), resource, Now)));
        Assert.False(RecoveryControlRecord.Release(Incarnation(), Now).SameAs(RecoveryControlRecord.Genesis(Incarnation(), Now)));
    }

    [Fact]
    public void AnAttestationListsEachMembershipOnceAndOmitsThemFromSerialization()
    {
        var user = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new RecoveryAuthorizationAttestation(
            Now,
            [new RecoveryAuthorizedMembership(Firm, user), new RecoveryAuthorizedMembership(Firm, user)]));
        Assert.Throws<ArgumentException>(() => new RecoveryAuthorizedMembership(Firm, Guid.Empty));

        var json = JsonSerializer.Serialize(new RecoveryAuthorizationAttestation(Now, [new RecoveryAuthorizedMembership(Firm, user)]));
        Assert.DoesNotContain(user.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // A snapshot that is not complete carries no fact that could be trusted.
    public void AnIncompleteSnapshotCarriesNoFacts()
    {
        var invalid = new RecoveryControlSnapshot(
            RecoveryControlReadStatus.Invalid, Now, [RecoveryControlRecord.Genesis(Incarnation(), Now)], Attestation(Now));

        Assert.Empty(invalid.Records);
        Assert.Null(invalid.Attestation);
        Assert.Empty(RecoveryControlSnapshot.Unavailable(Now).Records);
    }

    [Fact]
    public void AnEvidenceDocumentIsPresentOnlyWithItsManifest()
    {
        Assert.Equal("{}", ProductionEvidenceDocument.Present("{}").ManifestText);
        Assert.Null(ProductionEvidenceDocument.Refused(ProductionEvidenceDocumentStatus.Withdrawn).ManifestText);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionEvidenceDocument.Refused(ProductionEvidenceDocumentStatus.Present));
        Assert.Throws<ArgumentException>(() => ProductionEvidenceDocument.Present(" "));
        Assert.Equal(default, (ProductionEvidenceDocumentStatus)0);
        Assert.Equal(ProductionEvidenceDocumentStatus.Unavailable, default(ProductionEvidenceDocumentStatus));
        Assert.Equal(RecoveryControlAppendOutcome.Unacknowledged, default(RecoveryControlAppendOutcome));
        Assert.Equal(RecoveryControlReadStatus.Unavailable, default(RecoveryControlReadStatus));
        Assert.Equal(RecoveryReplayOutcome.Incomplete, default(RecoveryReplayOutcome));
    }

    // ------------------------------------------------------ release policy

    [Fact]
    public void AReleaseOrGenesisOfExactlyThisIncarnationReleasesIt()
    {
        var records = new[]
        {
            RecoveryControlRecord.Genesis(Incarnation('a'), Now),
            RecoveryControlRecord.Release(Incarnation('b'), Now),
            RecoveryControlRecord.TerminalStructured(Firm, Guid.NewGuid(), Now),
        };

        Assert.True(RecoveryReleasePolicy.IsReleased(records, Incarnation('a')));
        Assert.True(RecoveryReleasePolicy.IsReleased(records, Incarnation('b')));
        Assert.False(RecoveryReleasePolicy.IsReleased(records, Incarnation('c')));
    }

    [Fact] // AC-115: an empty ledger beside existing data is never a fresh start.
    public void OnlyAnEmptyLedgerBesideAPristineDatabaseMayBootstrap()
    {
        var empty = new RecoveryControlSnapshot(RecoveryControlReadStatus.Complete, Now, [], null);

        Assert.True(RecoveryReleasePolicy.MayBootstrap(empty, RecoveredDataExtent.Observed(false, null)));
        Assert.False(RecoveryReleasePolicy.MayBootstrap(empty, RecoveredDataExtent.Observed(true, Now)));
        Assert.False(RecoveryReleasePolicy.MayBootstrap(empty, RecoveredDataExtent.Observed(false, Now)));
        Assert.False(RecoveryReleasePolicy.MayBootstrap(empty, RecoveredDataExtent.Unreadable));
        Assert.False(RecoveryReleasePolicy.MayBootstrap(Ledger(), RecoveredDataExtent.Observed(false, null)));
        Assert.False(RecoveryReleasePolicy.MayBootstrap(RecoveryControlSnapshot.Unavailable(Now), RecoveredDataExtent.Observed(false, null)));
        Assert.False(RecoveryReleasePolicy.MayBootstrap(
            new RecoveryControlSnapshot(RecoveryControlReadStatus.Complete, Now, [], Attestation(Now)),
            RecoveredDataExtent.Observed(false, null)));
    }

    [Fact] // Positive control: a covered, fresh, attested recovery may proceed.
    public void ACoveredFreshAttestedRecoveryHasNoPrecondition()
    {
        Assert.Empty(RecoveryReleasePolicy.EvaluatePreconditions(Facts()));
    }

    public static TheoryData<string, RecoveryQuarantineReason> Quarantines() => new()
    {
        { "incarnation unavailable", RecoveryQuarantineReason.DatabaseIncarnationUnavailable },
        { "ledger unavailable", RecoveryQuarantineReason.ControlLedgerUnavailable },
        { "ledger invalid", RecoveryQuarantineReason.ControlLedgerInvalid },
        { "two geneses", RecoveryQuarantineReason.ControlLedgerInvalid },
        { "genesis missing", RecoveryQuarantineReason.ControlGenesisMissing },
        { "stale ledger read", RecoveryQuarantineReason.ControlEvidenceStale },
        { "ledger read from the future", RecoveryQuarantineReason.ControlEvidenceStale },
        { "ledger started after the data", RecoveryQuarantineReason.RecoveryIntervalUncovered },
        { "data older than the ledger retains", RecoveryQuarantineReason.RecoveryIntervalUncovered },
        { "ledger retention not beyond backups", RecoveryQuarantineReason.RecoveryIntervalUncovered },
        { "data from the future", RecoveryQuarantineReason.RecoveryIntervalUncovered },
        { "unreadable data extent", RecoveryQuarantineReason.RecoveryIntervalUncovered },
        { "data without activity", RecoveryQuarantineReason.RecoveryIntervalUncovered },
        { "data older than any backup", RecoveryQuarantineReason.RecoveredDataOutsideBackupWindow },
        { "attestation missing", RecoveryQuarantineReason.AuthorizationAttestationMissing },
        { "attestation too old", RecoveryQuarantineReason.AuthorizationAttestationStale },
        { "attestation older than the data", RecoveryQuarantineReason.AuthorizationAttestationStale },
        { "attestation from the future", RecoveryQuarantineReason.AuthorizationAttestationStale },
    };

    [Theory] // AC-115, AC-116 and the verification list: each precondition refuses on its own.
    [MemberData(nameof(Quarantines))]
    public void EachPreconditionQuarantinesIndependently(string scenario, RecoveryQuarantineReason expected)
    {
        var facts = scenario switch
        {
            "incarnation unavailable" => Facts(noIncarnation: true),
            "ledger unavailable" => Facts(ledger: RecoveryControlSnapshot.Unavailable(Now)),
            "ledger invalid" => Facts(ledger: RecoveryControlSnapshot.Invalid(Now)),
            "two geneses" => Facts(ledger: Ledger(geneses: 2)),
            "genesis missing" => Facts(ledger: Ledger(geneses: 0)),
            "stale ledger read" => Facts(ledger: Ledger(readAt: Now.AddMinutes(-16))),
            "ledger read from the future" => Facts(ledger: Ledger(readAt: Now.AddMinutes(1))),
            "ledger started after the data" => Facts(ledger: Ledger(genesisAt: Now.AddHours(-1))),
            "data older than the ledger retains" => Facts(
                ledger: Ledger(genesisAt: Now.AddDays(-400), attestationAt: Now.AddMinutes(-1)),
                extent: RecoveredDataExtent.Observed(true, Now.AddDays(-91))),
            "ledger retention not beyond backups" => Facts(ledgerDays: 42),
            "data from the future" => Facts(extent: RecoveredDataExtent.Observed(true, Now.AddMinutes(5))),
            "unreadable data extent" => Facts(extent: RecoveredDataExtent.Unreadable),
            "data without activity" => Facts(extent: RecoveredDataExtent.Observed(true, null)),
            "data older than any backup" => Facts(extent: RecoveredDataExtent.Observed(true, Now.AddDays(-43))),
            "attestation missing" => Facts(ledger: new RecoveryControlSnapshot(
                RecoveryControlReadStatus.Complete, Now, [RecoveryControlRecord.Genesis(Incarnation(), Now.AddDays(-80))], null)),
            "attestation too old" => Facts(ledger: Ledger(attestationAt: Now.AddHours(-5))),
            "attestation older than the data" => Facts(ledger: Ledger(attestationAt: Now.AddHours(-3))),
            _ => Facts(ledger: Ledger(attestationAt: Now.AddMinutes(1))),
        };

        Assert.Contains(expected, RecoveryReleasePolicy.EvaluatePreconditions(facts));
    }

    [Fact] // AC-165: nothing in the facts can waive a precondition.
    public void ReasonsAreClosedDistinctAndOrderedWithNoReleasingMember()
    {
        var reasons = RecoveryReleasePolicy.EvaluatePreconditions(Facts(
            noIncarnation: true,
            ledger: Ledger(geneses: 0, attestationAt: Now.AddDays(-1), readAt: Now.AddHours(-1))));

        Assert.Equal(reasons.OrderBy(reason => (int)reason), reasons);
        Assert.Equal(reasons.Distinct(), reasons);
        Assert.DoesNotContain(
            Enum.GetNames<RecoveryQuarantineReason>(),
            name => name.Contains("Released", StringComparison.Ordinal) || name.Contains("None", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new RecoveryReleaseFacts(
            null, Ledger(), RecoveredDataExtent.Unreadable, 90, 35, 7, new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.FromHours(-5))));
    }

    [Fact]
    public void CountsAreStructuralAndNeverNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryAccessInvalidation(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryMembershipReconciliation(0, -1));
        Assert.Equal(nameof(RecoveryAccessInvalidation), new RecoveryAccessInvalidation(1, 2, 3).ToString());
        Assert.Throws<ArgumentException>(() => RecoveredDataExtent.Observed(true, new DateTimeOffset(2026, 9, 29, 7, 0, 0, TimeSpan.FromHours(-5))));
        Assert.True(RecoveredDataExtent.Unreadable.HoldsTaxpayerData);
    }
}
