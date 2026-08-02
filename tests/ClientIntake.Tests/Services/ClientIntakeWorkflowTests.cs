using ClientIntake.Models;
using ClientIntake.Services;
using ClientIntake.Tests.Support;
using TaxCases.Models;

namespace ClientIntake.Tests.Services;

public sealed class ClientIntakeWorkflowTests
{
    [Fact]
    public async Task InspectIsReadOnlyAndRedemptionIsSingleUse()
    {
        var fixture = await FixtureAsync();

        var firstInspection = await fixture.Client.InspectLinkAsync(
            fixture.Created.LinkCredential,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);
        var secondInspection = await fixture.Client.InspectLinkAsync(
            fixture.Created.LinkCredential,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.True(firstInspection.IsSuccess);
        Assert.True(secondInspection.IsSuccess);

        var redeemed = await fixture.Client.RedeemLinkAsync(
            firstInspection.Value!.Proof,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);
        var replay = await fixture.Client.RedeemLinkAsync(
            secondInspection.Value!.Proof,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None);

        Assert.True(redeemed.IsSuccess);
        Assert.Equal(IntakeResultCode.NotFound, replay.Code);
    }

    [Fact]
    public async Task ChangedAnswersAppendVersionsAndDuplicateCategoriesFailAtomically()
    {
        var fixture = await RedeemedFixtureAsync();
        var first = await fixture.Client.SaveAnswersAsync(
            fixture.Session.SessionCredential,
            new SaveIntakeAnswersCommand(
            [
                new IntakeAnswerInput(
                    DocumentCategory.T4Slip,
                    AvailabilityAnswerCode.WillProvideLater),
            ]),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);
        var second = await fixture.Client.SaveAnswersAsync(
            fixture.Session.SessionCredential,
            new SaveIntakeAnswersCommand(
            [
                new IntakeAnswerInput(
                    DocumentCategory.T4Slip,
                    AvailabilityAnswerCode.AvailableNow),
            ]),
            SyntheticIntakeFixtures.Now.AddMinutes(2),
            CancellationToken.None);
        var duplicate = await fixture.Client.SaveAnswersAsync(
            fixture.Session.SessionCredential,
            new SaveIntakeAnswersCommand(
            [
                new IntakeAnswerInput(
                    DocumentCategory.T4Slip,
                    AvailabilityAnswerCode.AvailableNow),
                new IntakeAnswerInput(
                    DocumentCategory.T4Slip,
                    AvailabilityAnswerCode.Unavailable),
            ]),
            SyntheticIntakeFixtures.Now.AddMinutes(3),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(IntakeResultCode.Invalid, duplicate.Code);
        var stored = await fixture.Store.FindAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            CancellationToken.None);
        Assert.Equal(
            new[] { 1, 2 },
            stored!.AnswerVersions
                .Where(answer => answer.Category == DocumentCategory.T4Slip)
                .Select(answer => answer.Version));
    }

    [Fact]
    public async Task ValidDistinctUploadThenSubmissionUsesExistingReadinessAuthority()
    {
        var fixture = await RedeemedFixtureAsync(t4: 1, medical: 0);
        var answered = await fixture.Client.SaveAnswersAsync(
            fixture.Session.SessionCredential,
            new SaveIntakeAnswersCommand(
            [
                new IntakeAnswerInput(
                    DocumentCategory.T4Slip,
                    AvailabilityAnswerCode.AvailableNow),
            ]),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);
        Assert.True(answered.IsSuccess);

        var bytes = SyntheticIntakeFixtures.Pdf();
        var uploaded = await fixture.Client.UploadAsync(
            fixture.Session.SessionCredential,
            new IntakeUploadCommand(
                DocumentCategory.T4Slip,
                Ordinal: 1,
                IntakeMediaKind.Pdf,
                bytes.Length),
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(2),
            CancellationToken.None);
        Assert.True(uploaded.IsSuccess);
        Assert.Equal(
            ExpectedDocumentStatus.Uploaded,
            Assert.Single(uploaded.Value!.ExpectedDocuments).Status);

        var submitted = await fixture.Client.SubmitAsync(
            fixture.Session.SessionCredential,
            SyntheticIntakeFixtures.Now.AddMinutes(3),
            CancellationToken.None);
        Assert.True(submitted.IsSuccess);
        Assert.Equal(
            ExpectedDocumentStatus.Ready,
            Assert.Single(submitted.Value!.ExpectedDocuments).Status);

        var progress = await fixture.Accountant.GetProgressAsync(
            SyntheticIntakeFixtures.Firm,
            fixture.Created.CaseId,
            SyntheticIntakeFixtures.Now.AddMinutes(3),
            CancellationToken.None);
        Assert.Equal(ReadinessState.Ready, progress.Value!.Readiness);
        Assert.Equal(ReviewHandoffState.AwaitingReviewRecord, progress.Value.HandoffState);
    }

    [Fact]
    public async Task SameCaseDuplicateIsFixedAndCreatesNoSecondContentObject()
    {
        var fixture = await RedeemedFixtureAsync(t4: 1, medical: 0);
        var bytes = SyntheticIntakeFixtures.Pdf();
        var command = new IntakeUploadCommand(
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Pdf,
            bytes.Length);

        var first = await fixture.Client.UploadAsync(
            fixture.Session.SessionCredential,
            command,
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(1),
            CancellationToken.None);
        var second = await fixture.Client.UploadAsync(
            fixture.Session.SessionCredential,
            command,
            new MemoryStream(bytes),
            SyntheticIntakeFixtures.Now.AddMinutes(2),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(IntakeResultCode.Duplicate, second.Code);
        Assert.Equal(1, fixture.Content.Count);
    }

    [Fact]
    public async Task ConcurrentSameCaseDuplicateHasOneWinnerAndOneFixedLoser()
    {
        var store = new InMemoryIntakeStore();
        var source = new SyntheticIntakeFixtures.CredentialSource();
        var accountant = new AccountantIntakeWorkflow(
            store,
            source,
            new SyntheticIntakeFixtures.ReviewRepository(),
            SyntheticIntakeFixtures.Options());
        var content = new SyntheticIntakeFixtures.BarrierContentStore();
        var client = new ClientIntakeWorkflow(
            store,
            content,
            source,
            SyntheticIntakeFixtures.Options());
        var created = (await accountant.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand(t4: 1, medical: 0),
            SyntheticIntakeFixtures.Now,
            CancellationToken.None)).Value!;
        var inspection = (await client.InspectLinkAsync(
            created.LinkCredential,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None)).Value!;
        var session = (await client.RedeemLinkAsync(
            inspection.Proof,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None)).Value!;
        var bytes = SyntheticIntakeFixtures.Pdf();
        var command = new IntakeUploadCommand(
            DocumentCategory.T4Slip,
            1,
            IntakeMediaKind.Pdf,
            bytes.Length);

        var writes = await Task.WhenAll(
            client.UploadAsync(
                session.SessionCredential,
                command,
                new MemoryStream(bytes),
                SyntheticIntakeFixtures.Now.AddMinutes(1),
                CancellationToken.None),
            client.UploadAsync(
                session.SessionCredential,
                command,
                new MemoryStream(bytes),
                SyntheticIntakeFixtures.Now.AddMinutes(1),
                CancellationToken.None));

        Assert.Single(writes, result => result.IsSuccess);
        Assert.Single(writes, result => result.Code == IntakeResultCode.Duplicate);
        Assert.Equal(1, content.Count);
    }

    private static async Task<Fixture> FixtureAsync(int t4 = 1, int medical = 1)
    {
        var store = new InMemoryIntakeStore();
        var source = new SyntheticIntakeFixtures.CredentialSource();
        var review = new SyntheticIntakeFixtures.ReviewRepository();
        var accountant = new AccountantIntakeWorkflow(
            store,
            source,
            review,
            SyntheticIntakeFixtures.Options());
        var content = new SyntheticIntakeFixtures.ContentStore();
        var client = new ClientIntakeWorkflow(
            store,
            content,
            source,
            SyntheticIntakeFixtures.Options());
        var created = (await accountant.CreateAsync(
            SyntheticIntakeFixtures.Firm,
            Guid.NewGuid(),
            SyntheticIntakeFixtures.CreateCommand(t4, medical),
            SyntheticIntakeFixtures.Now,
            CancellationToken.None)).Value!;
        return new Fixture(store, content, accountant, client, created, null);
    }

    private static async Task<Fixture> RedeemedFixtureAsync(int t4 = 1, int medical = 1)
    {
        var fixture = await FixtureAsync(t4, medical);
        var inspection = (await fixture.Client.InspectLinkAsync(
            fixture.Created.LinkCredential,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None)).Value!;
        var session = (await fixture.Client.RedeemLinkAsync(
            inspection.Proof,
            SyntheticIntakeFixtures.Now,
            CancellationToken.None)).Value!;
        return fixture with { NullableSession = session };
    }

    private sealed record Fixture(
        InMemoryIntakeStore Store,
        SyntheticIntakeFixtures.ContentStore Content,
        AccountantIntakeWorkflow Accountant,
        ClientIntakeWorkflow Client,
        CreatedIntakeCase Created,
        RedeemedIntakeSession? NullableSession)
    {
        internal RedeemedIntakeSession Session =>
            NullableSession ?? throw new InvalidOperationException();
    }
}
