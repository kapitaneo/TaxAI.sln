using ClientIntake.Models;
using ClientIntake.Tests.Support;
using TaxCases.Models;

namespace ClientIntake.Tests.Services;

public sealed class IntakeProjectionTests
{
    [Fact]
    public async Task StatusPrecedenceAndUnsubmittedChangesFollowImmutableFacts()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(
            t4: 1,
            medical: 0);
        var session = await fixture.RedeemAsync();

        var initial = await fixture.Client.GetAsync(
            session.SessionCredential,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);
        AssertStatus(initial, ExpectedDocumentStatus.Requested, unsubmitted: false);

        var answered = await SaveAnswerAsync(
            fixture,
            session,
            AvailabilityAnswerCode.Unavailable,
            minute: 1);
        AssertStatus(answered, ExpectedDocumentStatus.Requested, unsubmitted: true);

        var missing = await fixture.Client.SubmitAsync(
            session.SessionCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(2),
            CancellationToken.None);
        AssertStatus(missing, ExpectedDocumentStatus.Missing, unsubmitted: false);

        var malformed = System.Text.Encoding.ASCII.GetBytes("not-a-pdf");
        var rejected = await fixture.Client.UploadAsync(
            session.SessionCredential,
            new IntakeUploadCommand(
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                malformed.Length),
            new MemoryStream(malformed),
            SyntheticIntakeFixtures.Now.AddMinutes(3),
            CancellationToken.None);
        Assert.Equal(IntakeResultCode.Invalid, rejected.Code);
        var afterRejection = await fixture.Client.GetAsync(
            session.SessionCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(3),
            CancellationToken.None);
        AssertStatus(afterRejection, ExpectedDocumentStatus.Rejected, unsubmitted: false);

        var firstBytes = SyntheticIntakeFixtures.Pdf("first");
        var uploaded = await UploadAsync(fixture, session, firstBytes, minute: 4);
        AssertStatus(uploaded, ExpectedDocumentStatus.Uploaded, unsubmitted: true);

        var ready = await fixture.Client.SubmitAsync(
            session.SessionCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(5),
            CancellationToken.None);
        AssertStatus(ready, ExpectedDocumentStatus.Ready, unsubmitted: false);

        var replacementBytes = SyntheticIntakeFixtures.Pdf("replacement");
        var replacement = await UploadAsync(
            fixture,
            session,
            replacementBytes,
            minute: 6);
        AssertStatus(replacement, ExpectedDocumentStatus.Ready, unsubmitted: true);

        var resubmitted = await fixture.Client.SubmitAsync(
            session.SessionCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(7),
            CancellationToken.None);
        AssertStatus(resubmitted, ExpectedDocumentStatus.Ready, unsubmitted: false);
        Assert.Equal(3, resubmitted.Value!.LatestSubmissionVersion);
    }

    [Fact]
    public async Task SubmissionReferencesTheCurrentDocumentEvenIfAvailabilityChanged()
    {
        var fixture = await SyntheticIntakeFixtures.CreateHarnessAsync(
            t4: 1,
            medical: 0);
        var session = await fixture.RedeemAsync();
        var bytes = SyntheticIntakeFixtures.Pdf();
        _ = await UploadAsync(fixture, session, bytes, minute: 1);
        _ = await SaveAnswerAsync(
            fixture,
            session,
            AvailabilityAnswerCode.Unavailable,
            minute: 2);

        var submitted = await fixture.Client.SubmitAsync(
            session.SessionCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(3),
            CancellationToken.None);

        AssertStatus(submitted, ExpectedDocumentStatus.Ready, unsubmitted: false);
        var stored = await fixture.Store.FindAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            CancellationToken.None);
        Assert.Single(Assert.Single(stored!.Submissions).Documents);
    }

    private static Task<IntakeResult<ClientIntakeView>> SaveAnswerAsync(
        SyntheticIntakeFixtures.Harness fixture,
        RedeemedIntakeSession session,
        AvailabilityAnswerCode answer,
        int minute) =>
        fixture.Client.SaveAnswersAsync(
            session.SessionCredential,
            new SaveIntakeAnswersCommand(
            [
                new IntakeAnswerInput(DocumentCategory.T4Slip, answer),
            ]),
            SyntheticIntakeFixtures.Now.AddMinutes(minute),
            CancellationToken.None);

    private static Task<IntakeResult<ClientIntakeView>> UploadAsync(
        SyntheticIntakeFixtures.Harness fixture,
        RedeemedIntakeSession session,
        byte[] bytes,
        int minute) =>
        fixture.Client.UploadAsync(
            session.SessionCredential,
            new IntakeUploadCommand(
                DocumentCategory.T4Slip,
                1,
                IntakeMediaKind.Pdf,
                bytes.Length),
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(minute),
            CancellationToken.None);

    private static void AssertStatus(
        IntakeResult<ClientIntakeView> result,
        ExpectedDocumentStatus expected,
        bool unsubmitted)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Assert.Single(result.Value!.ExpectedDocuments).Status);
        Assert.Equal(unsubmitted, result.Value.UnsubmittedChanges);
    }
}
