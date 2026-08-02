using ClientIntake.Models;
using ClientIntake.Tests.Support;
using TaxCases.Models;

namespace ClientIntake.Tests.Services;

public sealed class IntakeUploadValidatorTests
{
    [Theory]
    [InlineData(IntakeMediaKind.Pdf)]
    [InlineData(IntakeMediaKind.Jpeg)]
    [InlineData(IntakeMediaKind.Png)]
    public async Task EachApprovedSignatureIsAccepted(IntakeMediaKind mediaKind)
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(
            t4: 1,
            medical: 0);
        var session = await fixture.RedeemAsync();
        var bytes = Bytes(mediaKind);

        var result = await fixture.Client.UploadAsync(
            session.SessionCredential,
            new IntakeUploadCommand(
                DocumentCategory.T4Slip,
                1,
                mediaKind,
                bytes.Length),
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fixture.Content.Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EmptyMismatchedAndMisdeclaredContentIsRejectedAndRemoved(
        bool hasContent,
        bool lengthMismatch)
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(
            t4: 1,
            medical: 0);
        var session = await fixture.RedeemAsync();
        var bytes = hasContent
            ? SyntheticIntakeFixtures.Pdf()
            : [];
        var kind = hasContent && !lengthMismatch
            ? IntakeMediaKind.Jpeg
            : IntakeMediaKind.Pdf;
        var declaredLength = lengthMismatch
            ? bytes.Length + 1
            : Math.Max(1, bytes.Length);

        var result = await fixture.Client.UploadAsync(
            session.SessionCredential,
            new IntakeUploadCommand(
                DocumentCategory.T4Slip,
                1,
                kind,
                declaredLength),
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.Invalid, result.Code);
        Assert.Equal(0, fixture.Content.Count);
        var stored = await fixture.Store.FindAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            CancellationToken.None);
        Assert.Empty(stored!.Documents);
        Assert.Equal(
            IntakeUploadOutcome.Rejected,
            Assert.Single(stored.UploadAttempts).Outcome);
    }

    [Fact]
    public async Task AnUnrequestedSlotIsNotAnUploadOracle()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(
            t4: 1,
            medical: 0);
        var session = await fixture.RedeemAsync();
        var bytes = SyntheticIntakeFixtures.Pdf();

        var result = await fixture.Client.UploadAsync(
            session.SessionCredential,
            new IntakeUploadCommand(
                DocumentCategory.MedicalExpenseEvidence,
                1,
                IntakeMediaKind.Pdf,
                bytes.Length),
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);

        Assert.Equal(IntakeResultCode.NotFound, result.Code);
        Assert.Equal(0, fixture.Content.Count);
        var stored = await fixture.Store.FindAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            CancellationToken.None);
        Assert.Empty(stored!.UploadAttempts);
    }

    [Fact]
    public async Task CancellationDuringMetadataCommitRemovesStagedContent()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(
            t4: 1,
            medical: 0);
        var session = await fixture.RedeemAsync();
        var bytes = SyntheticIntakeFixtures.Pdf();
        fixture.Store.CancelNextUploadMutation = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Client.UploadAsync(
                session.SessionCredential,
                new IntakeUploadCommand(
                    DocumentCategory.T4Slip,
                    1,
                    IntakeMediaKind.Pdf,
                    bytes.Length),
                new MemoryStream(bytes),
                SyntheticIntakeFixtures.Now.AddMinutes(1),
                CancellationToken.None));

        Assert.Equal(0, fixture.Content.Count);
    }

    private static byte[] Bytes(IntakeMediaKind mediaKind) =>
        mediaKind switch
        {
            IntakeMediaKind.Pdf => SyntheticIntakeFixtures.Pdf(),
            IntakeMediaKind.Jpeg => SyntheticIntakeFixtures.Jpeg(),
            IntakeMediaKind.Png => SyntheticIntakeFixtures.Png(),
            _ => throw new ArgumentOutOfRangeException(nameof(mediaKind)),
        };
}
